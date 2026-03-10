using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;

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
    public int BotCount { get; set; } = 1;
}

public sealed class PlayerStats
{
    public int VoxelsPlaced { get; set; }
    public int VoxelsDestroyed { get; set; }
    public int ShotsFired { get; set; }
    public int ShotsHit { get; set; }
    public int DamageDealt { get; set; }
    public int CommanderKills { get; set; }
    public long MatchEarnings { get; set; }

    public float Accuracy => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;
}

public sealed class PlayerData
{
    /// <summary>
    /// Fun military-themed names assigned randomly to bot players.
    /// </summary>
    private static readonly string[] BotNames =
    {
        "General Boom",
        "Commander Chaos",
        "Sgt. Brickface",
        "Captain Kaboom",
        "Major Meltdown",
        "Colonel Crumble",
        "Private Havoc",
        "Admiral Rubble",
        "Corporal Blast",
        "Lt. Demolish",
        "Marshal Mayhem",
        "Warden Wreck",
        "Baron Von Boom",
        "Duke Dynamite",
        "Chief Smasher",
        "Sgt. Shellshock",
        "Captain Crater",
        "Major Mortar",
        "Pvt. Powderkeg",
        "General Gravel",
    };

    private static readonly Random _botNameRng = new Random();

    /// <summary>
    /// Returns a set of unique random bot names (one per bot).
    /// </summary>
    public static string[] GetRandomBotNames(int count)
    {
        // Shuffle a copy and take the first 'count' names
        string[] shuffled = (string[])BotNames.Clone();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = _botNameRng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        string[] result = new string[Math.Min(count, shuffled.Length)];
        Array.Copy(shuffled, result, result.Length);
        return result;
    }

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
    public PowerupInventory Powerups { get; } = new PowerupInventory();
    public PlayerStats Stats { get; } = new PlayerStats();
    public int AirstrikesUsedThisRound { get; set; }

    /// <summary>
    /// The build zone assigned to this player for the current match.
    /// </summary>
    public BuildZone AssignedBuildZone { get; set; }

    public void ResetForMatch(MatchSettings settings)
    {
        Budget = settings.StartingBudget;
        CommanderHealth = GameConfig.CommanderHP;
        CommanderMicrovoxelPosition = null;
        WeaponIds.Clear();
        Powerups.Clear();
        Stats.VoxelsPlaced = 0;
        Stats.VoxelsDestroyed = 0;
        Stats.ShotsFired = 0;
        Stats.ShotsHit = 0;
        Stats.DamageDealt = 0;
        Stats.CommanderKills = 0;
        Stats.MatchEarnings = 0;
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
