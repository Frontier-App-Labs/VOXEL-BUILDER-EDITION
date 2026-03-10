using System.Collections.Generic;

namespace VoxelSiege.Core;

public sealed class PlayerProfile
{
    public string DisplayName { get; set; } = "Player";
    public int Level { get; set; } = 1;
    public int TotalXp { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int TotalBlocksPlaced { get; set; }
    public int TotalBlocksDestroyed { get; set; }
    public string FavoriteMaterial { get; set; } = "Stone";
    public long WalletBalance { get; set; }
    public HashSet<string> UnlockedCommanderSkins { get; set; } = new HashSet<string>();
    public HashSet<string> UnlockedTitles { get; set; } = new HashSet<string>();

    /// <summary>
    /// Number of sandbox build slots the player owns. Starts at 1; additional slots cost 50,000.
    /// </summary>
    public int SandboxSlots { get; set; } = 1;

    /// <summary>
    /// Names of saved sandbox builds (matches saved blueprint files in user://blueprints/).
    /// </summary>
    public List<string> SavedBuilds { get; set; } = new List<string>();
}
