using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Core;

public sealed class MatchSettings
{
    public MatchVisibility Visibility { get; set; } = MatchVisibility.FriendsOnly;
    public float BuildTimeSeconds { get; set; } = GameConfig.DefaultBuildTime;
    public int StartingBudget { get; set; } = GameConfig.DefaultBudget;
    public int ArenaSize { get; set; } = GameConfig.MediumArena;
    public float TurnTimeSeconds { get; set; } = GameConfig.DefaultTurnTime;
    public WeaponTier WeaponTierCap { get; set; } = WeaponTier.Tier3;
    public bool FriendlyFire { get; set; }
    public FogMode FogMode { get; set; } = FogMode.Full;
}

public sealed class PlayerStats
{
    public int VoxelsPlaced { get; set; }
    public int VoxelsDestroyed { get; set; }
    public int ShotsFired { get; set; }
    public int ShotsHit { get; set; }
    public int DamageDealt { get; set; }
    public int CommanderKills { get; set; }
}

public sealed class PlayerData
{
    public long PeerId { get; set; }
    public PlayerSlot Slot { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public Color PlayerColor { get; set; } = Colors.White;
    public bool IsReady { get; set; }
    public bool IsAlive { get; set; } = true;
    public int Budget { get; private set; } = GameConfig.DefaultBudget;
    public int CommanderHealth { get; set; } = GameConfig.CommanderHP;
    public Vector3I? CommanderMicrovoxelPosition { get; set; }
    public List<string> WeaponIds { get; } = new List<string>();
    public PlayerStats Stats { get; } = new PlayerStats();

    public void ResetForMatch(MatchSettings settings)
    {
        Budget = settings.StartingBudget;
        CommanderHealth = GameConfig.CommanderHP;
        CommanderMicrovoxelPosition = null;
        WeaponIds.Clear();
        Stats.VoxelsPlaced = 0;
        Stats.VoxelsDestroyed = 0;
        Stats.ShotsFired = 0;
        Stats.ShotsHit = 0;
        Stats.DamageDealt = 0;
        Stats.CommanderKills = 0;
        IsAlive = true;
        IsReady = false;
    }

    public bool CanSpend(int amount)
    {
        return Budget >= amount;
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (Budget < amount)
        {
            return false;
        }

        Budget -= amount;
        return true;
    }

    public void Refund(int amount)
    {
        if (amount > 0)
        {
            Budget += amount;
        }
    }
}
