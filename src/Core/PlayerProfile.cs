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
    public HashSet<string> UnlockedCommanderSkins { get; set; } = new HashSet<string>();
    public HashSet<string> UnlockedTitles { get; set; } = new HashSet<string>();
}
