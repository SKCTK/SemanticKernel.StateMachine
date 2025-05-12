namespace Tests;

/// <summary>
/// Game items that can be collected
/// </summary>
[Flags]
public enum GameItems
{
    None = 0,
    Key = 1,
    Treasure = 2,
    Sword = 4,
    Shield = 8,
    Potion = 16,
    Scroll = 32,
    Map = 64,
    MagicAmulet = 128
}