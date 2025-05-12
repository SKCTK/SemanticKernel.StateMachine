using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using System.Text;
using System.Linq;
using Stateless;
using Stateless.Graph;
using SemanticKernel.StateMachine;

namespace Tests;

/// <summary>
/// Adventure game state machine helper for integration tests
/// </summary>
public class AdventureGame
{
    private readonly StateMachine<GameState, GameTrigger> _stateMachine;
    private GameItems _inventory = GameItems.None;
    private bool _monsterDefeated = false;
    private bool _puzzleSolved = false;
    private bool _secretRoomKnowledgeFromScroll = false; // ADDED: Flag for scroll knowledge
    private bool _chestOpenedInTreasureRoom = false; // ADDED: Flag for treasure chest status
    private int _health = 100;
    private int _gold = 0;
    
    public GameState CurrentState => _stateMachine.State;
    public GameItems Inventory => _inventory;
    public int Health => _health;
    public int Gold => _gold;
    public bool MonsterDefeated => _monsterDefeated;
    public bool PuzzleSolved => _puzzleSolved;
    public bool HasTreasure => _inventory.HasFlag(GameItems.Treasure);
    public bool HasKey => _inventory.HasFlag(GameItems.Key);
    public bool HasSword => _inventory.HasFlag(GameItems.Sword);
    public bool HasShield => _inventory.HasFlag(GameItems.Shield);
    public bool HasPotion => _inventory.HasFlag(GameItems.Potion);
    public bool HasMagicAmulet => _inventory.HasFlag(GameItems.MagicAmulet);
    
    public AdventureGame()
    {
        _stateMachine = new StateMachine<GameState, GameTrigger>(GameState.Entrance);
        ConfigureGameRules();
    }
    
    /// <summary>
    /// Configures the game's state machine transitions
    /// </summary>
    private void ConfigureGameRules()
    {
        // From Entrance
        _stateMachine.Configure(GameState.Entrance)
            // REMOVED: Scroll acquisition on entry
            // .OnEntry(t => {
            //     if (!_inventory.HasFlag(GameItems.Scroll)) {
            //         _inventory |= GameItems.Scroll;
            //     }
            // })
            .Permit(GameTrigger.GoNorth, GameState.MainHall)
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure);
        
        // From Main Hall
        _stateMachine.Configure(GameState.MainHall)
            .OnEntry(t => {
                // First time entering the main hall, get a sword
                if (!HasSword) {
                    _inventory |= GameItems.Sword;
                }
                
                if (_health <= 0) {
                    // This should transition to GameOver
                    _stateMachine.Fire(GameTrigger.UseKey);
                }
            })
            .OnEntryFrom(GameTrigger.ReadScroll, t => { // ADDED: Action for ReadScroll
                if (!_inventory.HasFlag(GameItems.Scroll)) {
                    _inventory |= GameItems.Scroll; // Player acquires the scroll item
                }
                _secretRoomKnowledgeFromScroll = true; // Player gains knowledge from reading it
            })
            .Permit(GameTrigger.GoSouth, GameState.Entrance)
            .Permit(GameTrigger.GoEast, GameState.TreasureRoom)
            .Permit(GameTrigger.GoWest, GameState.DangerZone)
            .PermitReentry(GameTrigger.ReadScroll) // Allow ReadScroll trigger
            .PermitIf(GameTrigger.GoUp, GameState.SecretRoom, () => _secretRoomKnowledgeFromScroll) // CHANGED: Condition for GoUp
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure);
        
        // From Treasure Room - key required to open chest, then take treasure
        _stateMachine.Configure(GameState.TreasureRoom)
            .OnEntry(t => {
                // First time entering the treasure room, get a shield
                if (!HasShield) {
                    _inventory |= GameItems.Shield;
                }
                // Chest remains open if previously opened and treasure not taken,
                // or resets if treasure was taken (handled by !HasTreasure in guards).
            })
            // Permit OpenChest if player has key, chest isn't open yet, and treasure not yet taken
            .PermitReentryIf(GameTrigger.OpenChest, () => HasKey && !_chestOpenedInTreasureRoom && !HasTreasure)
            .OnEntryFrom(GameTrigger.OpenChest, t => {
                // Action for OpenChest: mark chest as opened
                if (HasKey && !_chestOpenedInTreasureRoom && !HasTreasure) // Guard should ensure this
                {
                    _chestOpenedInTreasureRoom = true;
                }
            })
            // Permit TakeTreasure if chest is open and treasure not yet taken
            .PermitReentryIf(GameTrigger.TakeTreasure, () => _chestOpenedInTreasureRoom && !HasTreasure)
            .OnEntryFrom(GameTrigger.TakeTreasure, t => {
                // Action for TakeTreasure: get treasure and gold
                if (_chestOpenedInTreasureRoom && !HasTreasure) // Guard should ensure this
                {
                    _inventory |= GameItems.Treasure;
                    _gold += 1000; // Base gold from treasure
                    if (HasMagicAmulet)
                    {
                        _gold += 4000; // Bonus gold if player has the magic amulet (total 5000)
                    }
                }
            })
            .Permit(GameTrigger.GoWest, GameState.MainHall)
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure); // To Exit, needs Treasure
        
        // From Secret Room
        _stateMachine.Configure(GameState.SecretRoom)
            .OnEntryFrom(GameTrigger.SolvePuzzle, t => {
                _puzzleSolved = true;
                _inventory |= GameItems.MagicAmulet;
            })
            .Permit(GameTrigger.GoDown, GameState.MainHall)
            .PermitReentry(GameTrigger.SolvePuzzle)
            .PermitIf(GameTrigger.GoEast, GameState.Vault, () => _puzzleSolved)
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure);
        
        // From Vault
        _stateMachine.Configure(GameState.Vault)
            .OnEntryFrom(GameTrigger.TakeTreasure, t => {
                _gold += 2000;
            })
            .Permit(GameTrigger.GoWest, GameState.SecretRoom)
            .PermitReentry(GameTrigger.TakeTreasure)
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure);
        
        // From Danger Zone - can find key here, fight monster or run away
        _stateMachine.Configure(GameState.DangerZone)
            .OnEntryFrom(GameTrigger.Fight, t => {
                if (MonsterDefeated) return; // Monster already defeated, no action

                bool localHasSword = HasSword; // Use local var for clarity in this scope
                bool localHasShield = HasShield;
                int damageTaken;
                
                if (localHasSword && localHasShield) damageTaken = 5;
                else if (localHasSword) damageTaken = 10;
                else if (localHasShield) damageTaken = 15;
                else damageTaken = 30;
                
                _health -= damageTaken;
                
                if (_health <= 0) {
                    // Player is defeated. Game over logic could be triggered here.
                    // For now, player is just at 0 health, monster not officially defeated by them.
                    return;
                }
                
                // Player survived the fight, so monster is considered defeated by them
                _monsterDefeated = true;
                _inventory |= GameItems.Key; // Grant the key for defeating the monster
                
                if (!HasPotion) { // Optionally, grant a potion
                    _inventory |= GameItems.Potion;
                }
            })
            .OnEntryFrom(GameTrigger.Run, t => {
                if (!MonsterDefeated && new Random().Next(2) == 0) {
                    _health -= 20; // Take damage while escaping
                }
            })
            .OnEntryFrom(GameTrigger.DrinkPotion, t => {
                if (HasPotion) {
                    _inventory &= ~GameItems.Potion; // Remove potion from inventory
                    _health = Math.Min(100, _health + 50); // Heal 50 HP, up to max 100
                }
            })
            .Permit(GameTrigger.GoEast, GameState.MainHall)
            .PermitReentry(GameTrigger.Fight)
            .Permit(GameTrigger.Run, GameState.MainHall)
            .PermitReentryIf(GameTrigger.DrinkPotion, () => HasPotion)
            .PermitIf(GameTrigger.UseKey, GameState.Exit, () => HasTreasure && MonsterDefeated);
        
        // From Exit to Victory - final step if player has enough gold
        _stateMachine.Configure(GameState.Exit)
            .PermitIf(GameTrigger.UseKey, GameState.Victory, () => _gold >= 3000);
    }
    
    /// <summary>
    /// Get the game state machine
    /// </summary>
    public StateMachine<GameState, GameTrigger> GetStateMachine() => _stateMachine;
    
    /// <summary>
    /// Get the game status description
    /// </summary>
    public string GetStatus()
    {
        string location = $"You are in the {CurrentState}. ";
        string inventoryStatus = _inventory == GameItems.None 
            ? "Your inventory is empty."
            : $"Inventory: {_inventory}";
        string healthStatus = $"Health: {_health}%";
        string goldStatus = $"Gold: {_gold} coins";
        
        return $"{location}{inventoryStatus}. {healthStatus}. {goldStatus}";
    }
    
    /// <summary>
    /// Creates a StateMachinePlugin for this game with custom trigger descriptions.
    /// </summary>
    public StateMachinePlugin<GameState, GameTrigger> CreatePlugin()
    {
        return new StateMachinePlugin<GameState, GameTrigger>(_stateMachine);
    }
    
    /// <summary>
    /// Generates a custom system prompt for the game with detailed state and trigger information
    /// </summary>
    /// <returns>A descriptive system prompt for the adventure game state machine</returns>
    public string GetGameSystemPrompt()
    {
        var plugin = CreatePlugin();
        var basePrompt = plugin.StateMachine.GetPluginInstructions();
        
        // Now add game-specific information
        var sb = new StringBuilder(basePrompt);
        sb.AppendLine();
        sb.AppendLine("GAME-SPECIFIC INFORMATION:");
        sb.AppendLine($"- Health: {Health}%");
        sb.AppendLine($"- Gold: {Gold} coins");
        sb.AppendLine($"- Inventory: {_inventory}");
        sb.AppendLine($"- Monster defeated: {MonsterDefeated}");
        sb.AppendLine($"- Puzzle solved: {PuzzleSolved}");
        sb.AppendLine($"- Secret room revealed by scroll: {_secretRoomKnowledgeFromScroll}");
        sb.AppendLine($"- Treasure chest opened: {_chestOpenedInTreasureRoom}"); // ADDED: For clarity in prompt
        
        // Add some helpful gameplay tips
        sb.AppendLine();
        sb.AppendLine("GAMEPLAY TIPS:");
        sb.AppendLine("- You need the key to open the treasure chest");
        sb.AppendLine("- The key is guarded by a monster in the Danger Zone");
        sb.AppendLine("- Having a sword and shield helps in combat");
        sb.AppendLine("- Reading the scroll reveals the location of a secret room");
        sb.AppendLine("- Potions can be used to restore health");
        sb.AppendLine("- You need at least 3000 gold to win the game");
        sb.AppendLine();
        sb.AppendLine("Help the user navigate the game by executing the appropriate triggers.");
        sb.AppendLine("Always explain what happened after the trigger and what the user can do next.");
        
        return sb.ToString();
    }
}