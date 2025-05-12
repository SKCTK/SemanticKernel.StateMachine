using Stateless;
using Stateless.Graph;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using System.Collections.Generic;

namespace SemanticKernel.StateMachine;

public class StateMachinePlugin<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    public StateMachine<TState, TTrigger> StateMachine { get; }

    public StateMachinePlugin(
        StateMachine<TState, TTrigger> stateMachine)
    {
        ArgumentNullException.ThrowIfNull(stateMachine);
        StateMachine = stateMachine;
    }

    [KernelFunction, Description("Gets the current state of the state machine.")]
    public string GetCurrentState() => StateMachine.State.ToString();

    [KernelFunction, Description("Directly fires the specified trigger to transition the state machine without additional checks.")]
    public async Task<string> FireTrigger(
        [Description("The name of the trigger to fire directly, Must be a valid trigger name.")]
        string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName))
            return "Error: Trigger name cannot be empty.";

        if (!TryFindTriggerByName(triggerName, out TTrigger trigger))
            return $"Error: '{triggerName}' is not a valid trigger name. Available triggers: {String.Join(", ", GetAllTriggersWithDescriptions())}";
        
        try 
        {
            await StateMachine.FireAsync(trigger);
            return $"Successfully fired trigger '{GetTriggerRepresentation(trigger)}', transitioned to state: {StateMachine.State}";
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: Cannot fire trigger '{GetTriggerRepresentation(trigger)}' from state {StateMachine.State}. Details: {ex.Message}";
        }
    }

    [KernelFunction, Description("Transitions the state machine to a new state by executing the specified trigger.")]
    public async Task<string> Transition(
        [Description("The name of the trigger to execute, Must be a valid trigger name.")] 
        string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName))
            return "Error: Trigger name cannot be empty.";
        
        if (!TryFindTriggerByName(triggerName, out TTrigger trigger))
            return $"Error: '{triggerName}' is not a valid trigger name. Available triggers: {String.Join(", ", GetAllTriggersWithDescriptions())}";
            
        if (!StateMachine.CanFire(trigger))
            return $"Trigger '{GetTriggerRepresentation(trigger)}' cannot be executed from state {StateMachine.State}. Permitted triggers: {String.Join(", ", GetPermittedTriggersWithDescriptions())}";
            
        await StateMachine.FireAsync(trigger);
        return $"Trigger '{GetTriggerRepresentation(trigger)}' executed, transitioned to state: {StateMachine.State}";
    }

    [KernelFunction, Description("Checks if a specified trigger can be executed from the current state, without actually performing the transition.")]
    public string CanFireTrigger(
        [Description("The name of the trigger to check, Must be a valid trigger name.")] 
        string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName))
            return "Error: Trigger name cannot be empty.";
            
        if (!TryFindTriggerByName(triggerName, out TTrigger trigger))
            return $"Error: '{triggerName}' is not a valid trigger name. Available triggers: {String.Join(", ", GetAllTriggersWithDescriptions())}";
            
        return StateMachine.CanFire(trigger)
            ? $"Trigger '{GetTriggerRepresentation(trigger)}' can be executed from state {StateMachine.State}."
            : $"Trigger '{GetTriggerRepresentation(trigger)}' cannot be executed from state {StateMachine.State}. Permitted triggers: {String.Join(", ", GetPermittedTriggersWithDescriptions())}";
    }

    [KernelFunction, Description("Returns all possible states of the state machine as a list of strings.")]
    public string[] GetStates()
    {
        var info = StateMachine.GetInfo();
        return info.States.Select(s => s.ToString()).ToArray();
    }

    [KernelFunction, Description("Visualizes the state machine as a Mermaid graph for understanding all possible states and transitions.")]
    public string GetMermaidGraph()
    {
        return MermaidGraph.Format(StateMachine.GetInfo());
    }

    [KernelFunction, Description("Returns all triggers that are permitted to be executed from the current state.")]
    public string[] GetPermittedTriggers()
    {
        return GetPermittedTriggersWithDescriptions();
    }

    [KernelFunction, Description("Returns all possible triggers defined for the state machine, regardless of the current state.")]
    public string[] GetAllTriggers()
    {
        return GetAllPossibleTriggers()
            .Select(GetTriggerRepresentation)
            .ToArray();
    }

    [KernelFunction, Description("Returns detailed instructions for using this plugin, including available states, triggers, and usage tips.")]
    public string GetStateMachineDocumentation()
    {
        return StateMachine.GetPluginInstructions();
    }

    private string GetTriggerRepresentation(TTrigger trigger)
    {
        return trigger.ToString();
    }

    private string[] GetPermittedTriggersWithDescriptions()
    {
        return StateMachine.PermittedTriggers
            .Select(GetTriggerRepresentation)
            .ToArray();
    }
    
    private string[] GetAllTriggersWithDescriptions()
    {
        return GetAllPossibleTriggers()
            .Select(GetTriggerRepresentation)
            .ToArray();
    }

    /// <summary>
    /// Tries to find a trigger by its name.
    /// </summary>
    /// <param name="triggerName">The name of the trigger to find.</param>
    /// <param name="trigger">The found trigger value if successful.</param>
    /// <returns>True if the trigger was found, false otherwise.</returns>
    private bool TryFindTriggerByName(string triggerName, out TTrigger? trigger)
    {
        // First try direct enum parsing if TTrigger is an enum type
        if (typeof(TTrigger).IsEnum && Enum.TryParse(typeof(TTrigger), triggerName, true, out var enumValue) && enumValue != null)
        {
            trigger = (TTrigger)enumValue;
            return true;
        }

        // If direct parsing failed, search through all possible triggers
        foreach (var possibleTrigger in GetAllPossibleTriggers())
        {
            if (string.Equals(GetTriggerRepresentation(possibleTrigger).Split(' ')[0], triggerName, StringComparison.OrdinalIgnoreCase) || 
                string.Equals(possibleTrigger.ToString(), triggerName, StringComparison.OrdinalIgnoreCase))
            {
                trigger = possibleTrigger;
                return true;
            }
        }
        
        // If we reach here, the trigger name is invalid
        trigger = default!; // Using default! as TTrigger is constrained to be non-null
        return false;
    }
    
    private bool IsValidEnumTrigger(TTrigger trigger, string triggerName)
    {
        if (!typeof(TTrigger).IsEnum)
            return false;
            
        // Check if the default value actually corresponds to the requested name
        string defaultTriggerName = trigger.ToString();
        return string.Equals(defaultTriggerName, triggerName, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<TTrigger> GetAllPossibleTriggers()
    {
        var allTriggers = new HashSet<TTrigger>();
        var info = StateMachine.GetInfo();
        if (info != null)
        {
            foreach (var stateInfo in info.States)
            {
                foreach (var transitionInfo in stateInfo.Transitions)
                {
                    if (transitionInfo.Trigger.UnderlyingTrigger is TTrigger underlyingTrigger)
                    {
                        allTriggers.Add(underlyingTrigger);
                    }
                }
            }
        }
        if (typeof(TTrigger).IsEnum)
        {
            foreach (TTrigger triggerValue in Enum.GetValues(typeof(TTrigger)))
            {
                allTriggers.Add(triggerValue);
            }
        }
        if (!allTriggers.Any() && StateMachine.PermittedTriggers.Any())
        {
             foreach (var pt in StateMachine.PermittedTriggers) allTriggers.Add(pt);
        }
        return allTriggers;
    }
}
