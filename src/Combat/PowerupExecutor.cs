using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Executes powerup effects during combat. Created as a child of GameManager
/// and orchestrates smoke, medkit, shield, airstrike, and EMP mechanics.
/// </summary>
public partial class PowerupExecutor : Node
{
    private VoxelWorld? _voxelWorld;

    /// <summary>
    /// Raised when a powerup effect is activated, so UI / FX systems can respond.
    /// </summary>
    public event Action<PowerupType, PlayerSlot, Vector3>? PowerupActivated;

    /// <summary>
    /// Raised when a timed powerup expires.
    /// </summary>
    public event Action<PowerupType, PlayerSlot>? PowerupExpired;

    public override void _Ready()
    {
        _voxelWorld = GetParent()?.GetNodeOrNull<VoxelWorld>("GameWorld");
    }

    // ─────────────────────────────────────────────────
    //  SMOKE SCREEN (invisibility)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Data for smoke screen: tracks the zone and the number of players in the
    /// match so we know when a full rotation has elapsed.
    /// </summary>
    public sealed class SmokeScreenData
    {
        public BuildZone Zone { get; init; }
        public int TotalPlayers { get; init; }
        public int TurnsRemaining { get; set; }
    }

    /// <summary>
    /// Activates smoke screen: makes the player's fortress visually invisible
    /// for a full rotation of turns. Bots will fire randomly at the smoked zone.
    /// Debris from destroyed blocks IS still visible.
    /// </summary>
    public bool ActivateSmokeScreen(PlayerData player, int totalPlayersAlive)
    {
        // Prevent stacking — can't smoke an already-smoked zone
        if (player.Powerups.HasActiveEffect(PowerupType.SmokeScreen))
        {
            GD.Print($"[Powerup] {player.Slot}: Fortress already smoked.");
            return false;
        }

        if (!player.Powerups.TryConsume(PowerupType.SmokeScreen))
        {
            return false;
        }

        BuildZone zone = player.AssignedBuildZone;
        Vector3 center = MathHelpers.MicrovoxelToWorld(
            zone.OriginMicrovoxels + zone.SizeMicrovoxels / 2);

        // Duration = total alive players (one full rotation back to this player)
        SmokeScreenData data = new SmokeScreenData
        {
            Zone = zone,
            TotalPlayers = totalPlayersAlive,
            TurnsRemaining = totalPlayersAlive,
        };

        player.Powerups.AddActiveEffect(PowerupType.SmokeScreen, player.Slot, totalPlayersAlive, data);

        // Spawn visual smoke cloud
        PowerupFX.SpawnSmokeScreen(GetTree().Root, center, zone);

        // Hide all voxel chunks within the zone
        HideZoneChunks(zone, true);

        PowerupActivated?.Invoke(PowerupType.SmokeScreen, player.Slot, center);
        GD.Print($"[Powerup] {player.Slot}: Smoke Screen deployed — fortress invisible for {totalPlayersAlive} turns.");
        return true;
    }

    /// <summary>
    /// Checks if a player's fortress is currently cloaked by smoke screen.
    /// Used by bot AI to determine if it should fire randomly.
    /// </summary>
    public bool IsZoneSmoked(PlayerSlot owner, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        if (!allPlayers.TryGetValue(owner, out PlayerData? player))
        {
            return false;
        }

        return player.Powerups.HasActiveEffect(PowerupType.SmokeScreen);
    }

    /// <summary>
    /// Hides or shows all voxel chunks that overlap the given build zone.
    /// When hidden, the fortress becomes invisible but debris from hits is still
    /// spawned normally (debris is created as independent RigidBody3D nodes).
    /// </summary>
    private void HideZoneChunks(BuildZone zone, bool hide)
    {
        if (_voxelWorld == null)
        {
            return;
        }

        Vector3I minChunk = zone.OriginMicrovoxels / GameConfig.ChunkSize;
        Vector3I maxChunk = zone.MaxMicrovoxelsInclusive / GameConfig.ChunkSize;

        for (int z = minChunk.Z; z <= maxChunk.Z; z++)
        {
            for (int y = minChunk.Y; y <= maxChunk.Y; y++)
            {
                for (int x = minChunk.X; x <= maxChunk.X; x++)
                {
                    VoxelChunk? chunk = _voxelWorld.GetChunkAt(new Vector3I(x, y, z));
                    if (chunk != null)
                    {
                        chunk.Visible = !hide;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a projectile trajectory passes through any active smoke screen,
    /// and applies deflection if so.
    /// Returns the potentially deflected direction.
    /// </summary>
    public Vector3 ApplySmokeDeflection(Vector3 direction, Vector3 projectilePosition, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        foreach (PlayerData player in allPlayers.Values)
        {
            foreach (ActivePowerup effect in player.Powerups.GetActiveEffects(PowerupType.SmokeScreen))
            {
                if (effect.TargetData is not SmokeScreenData smokeData)
                {
                    continue;
                }

                BuildZone zone = smokeData.Zone;
                Vector3I projMicro = MathHelpers.WorldToMicrovoxel(projectilePosition);
                Vector3I expandedMin = zone.OriginMicrovoxels - new Vector3I(2, 2, 2);
                Vector3I expandedMax = zone.MaxMicrovoxelsInclusive + new Vector3I(2, 2, 2);

                if (projMicro.X >= expandedMin.X && projMicro.X <= expandedMax.X &&
                    projMicro.Y >= expandedMin.Y && projMicro.Y <= expandedMax.Y &&
                    projMicro.Z >= expandedMin.Z && projMicro.Z <= expandedMax.Z)
                {
                    RandomNumberGenerator rng = new();
                    rng.Randomize();
                    float deflectYaw = rng.RandfRange(-Mathf.DegToRad(15f), Mathf.DegToRad(15f));
                    float deflectPitch = rng.RandfRange(-Mathf.DegToRad(15f), Mathf.DegToRad(15f));

                    Vector3 deflected = direction.Rotated(Vector3.Up, deflectYaw);
                    Vector3 sideAxis = deflected.Cross(Vector3.Up).Normalized();
                    if (sideAxis.LengthSquared() > 0.001f)
                    {
                        deflected = deflected.Rotated(sideAxis, deflectPitch);
                    }

                    return deflected.Normalized();
                }
            }
        }

        return direction;
    }

    // ─────────────────────────────────────────────────
    //  MEDKIT (heals commander to full HP)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Instantly heals the player's Commander to full HP.
    /// </summary>
    public bool ActivateMedkit(PlayerData player)
    {
        if (!player.Powerups.TryConsume(PowerupType.Medkit))
        {
            return false;
        }

        // Find the commander node
        Commander.Commander? commander = FindCommanderForPlayer(player.Slot);
        if (commander == null)
        {
            // Refund if no commander found
            player.Powerups.RefundConsumed(PowerupType.Medkit);
            GD.Print($"[Powerup] {player.Slot}: No commander found. Medkit refunded.");
            return false;
        }

        Commander.CommanderHealth? health = commander.GetNodeOrNull<Commander.CommanderHealth>("CommanderHealth");
        if (health == null || health.IsDead)
        {
            player.Powerups.RefundConsumed(PowerupType.Medkit);
            GD.Print($"[Powerup] {player.Slot}: Commander dead or no health component. Medkit refunded.");
            return false;
        }

        int healed = health.MaxHealth - health.CurrentHealth;
        if (healed <= 0)
        {
            player.Powerups.RefundConsumed(PowerupType.Medkit);
            GD.Print($"[Powerup] {player.Slot}: Commander already at full health. Medkit refunded.");
            return false;
        }

        health.ResetHealth();

        Vector3 fxPos = commander.GlobalPosition + Vector3.Up * 1.5f;
        PowerupFX.SpawnMedkitEffect(GetTree().Root, fxPos);

        PowerupActivated?.Invoke(PowerupType.Medkit, player.Slot, fxPos);
        GD.Print($"[Powerup] {player.Slot}: Medkit healed Commander for {healed} HP (now {health.CurrentHealth}/{health.MaxHealth}).");
        return true;
    }

    private Commander.Commander? FindCommanderForPlayer(PlayerSlot slot)
    {
        foreach (Node node in GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is Commander.Commander cmd && cmd.OwnerSlot == slot)
            {
                return cmd;
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────
    //  SHIELD GENERATOR (player-wide 50% damage for 1 rotation)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Activates a shield that halves all damage to the player's fortress
    /// and commander for one full rotation of turns.
    /// </summary>
    public bool ActivateShieldGenerator(PlayerData player, int totalPlayersAlive)
    {
        if (!player.Powerups.TryConsume(PowerupType.ShieldGenerator))
        {
            return false;
        }

        // Duration = number of alive players (full rotation)
        player.Powerups.AddActiveEffect(PowerupType.ShieldGenerator, player.Slot, totalPlayersAlive, null);

        BuildZone zone = player.AssignedBuildZone;
        Vector3 worldCenter = MathHelpers.MicrovoxelToWorld(
            zone.OriginMicrovoxels + zone.SizeMicrovoxels / 2);
        float radiusMeters = (zone.SizeBuildUnits.X * GameConfig.BuildUnitMeters) * 0.5f;
        PowerupFX.SpawnShieldBubble(GetTree().Root, worldCenter, radiusMeters);

        PowerupActivated?.Invoke(PowerupType.ShieldGenerator, player.Slot, worldCenter);
        GD.Print($"[Powerup] {player.Slot}: Shield Generator active — 50% damage reduction for {totalPlayersAlive} turns.");
        return true;
    }

    /// <summary>
    /// Returns the shield damage multiplier for a given player.
    /// 0.5 if the player has an active shield, 1.0 otherwise.
    /// This is a player-wide effect, not position-based.
    /// </summary>
    public float GetShieldDamageMultiplier(PlayerSlot targetPlayer, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        if (!allPlayers.TryGetValue(targetPlayer, out PlayerData? player))
        {
            return 1.0f;
        }

        if (player.Powerups.HasActiveEffect(PowerupType.ShieldGenerator))
        {
            return 0.5f;
        }

        return 1.0f;
    }

    // ─────────────────────────────────────────────────
    //  AIRSTRIKE BEACON
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Calls in fighter jets that drop 3 bombardment shells on an 8x8 build unit
    /// area of an enemy's fortress.
    /// </summary>
    public bool ActivateAirstrike(PlayerData player, Vector3I targetBuildUnit, PlayerSlot targetEnemy)
    {
        if (!player.Powerups.TryConsume(PowerupType.AirstrikeBeacon))
        {
            return false;
        }

        if (_voxelWorld == null)
        {
            return false;
        }

        Vector3 targetWorld = MathHelpers.MicrovoxelToWorld(
            MathHelpers.BuildToMicrovoxel(targetBuildUnit));

        PowerupFX.SpawnAirstrikeTargeting(GetTree().Root, targetWorld);

        RandomNumberGenerator rng = new();
        rng.Randomize();

        int planeCount = rng.RandiRange(1, 3);

        Vector3[] impactPositions = new Vector3[3];
        for (int shell = 0; shell < 3; shell++)
        {
            int offsetX = rng.RandiRange(-8, 8);
            int offsetZ = rng.RandiRange(-8, 8);
            Vector3I impactMicro = MathHelpers.BuildToMicrovoxel(targetBuildUnit) + new Vector3I(offsetX, 0, offsetZ);
            int surfaceY = FindSurfaceY(impactMicro.X, impactMicro.Z);
            impactMicro = impactMicro with { Y = surfaceY };
            impactPositions[shell] = MathHelpers.MicrovoxelToWorld(impactMicro);
        }

        VoxelWorld capturedWorld = _voxelWorld;
        PlayerSlot capturedInstigator = player.Slot;
        SceneTree capturedTree = GetTree();

        AirstrikeFlyover.Spawn(
            GetTree().Root,
            targetWorld,
            player.PlayerColor,
            planeCount,
            onBombDrop: () =>
            {
                for (int shell = 0; shell < 3; shell++)
                {
                    float delay = shell * 0.3f;
                    int capturedShell = shell;
                    Vector3 capturedImpact = impactPositions[shell];

                    capturedTree.CreateTimer(delay).Timeout += () =>
                    {
                        Explosion.Trigger(capturedTree.Root, capturedWorld, capturedImpact, 40, 4f, capturedInstigator);
                        GD.Print($"[Powerup] Airstrike bomb {capturedShell + 1}/3 hit at {capturedImpact}.");
                    };
                }
            });

        PowerupActivated?.Invoke(PowerupType.AirstrikeBeacon, player.Slot, targetWorld);
        GD.Print($"[Powerup] {player.Slot}: Airstrike called on {targetBuildUnit} targeting {targetEnemy}.");
        return true;
    }

    private int FindSurfaceY(int microX, int microZ)
    {
        if (_voxelWorld == null)
        {
            return GameConfig.PrototypeGroundThickness;
        }

        int maxY = GameConfig.PrototypeGroundThickness + GameConfig.FourPlayerBuildZoneHeight * GameConfig.MicrovoxelsPerBuildUnit;
        for (int y = maxY; y >= 0; y--)
        {
            VoxelValue voxel = _voxelWorld.GetVoxel(new Vector3I(microX, y, microZ));
            if (voxel.IsSolid)
            {
                return y + 1;
            }
        }

        return GameConfig.PrototypeGroundThickness;
    }

    // ─────────────────────────────────────────────────
    //  EMP BLAST (probabilistic, 1/3 per weapon, min 1)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Data for an active EMP: which weapon is disabled.
    /// </summary>
    public readonly record struct EmpData(ulong WeaponInstanceId, string WeaponId, PlayerSlot TargetPlayer);

    /// <summary>
    /// Sends an EMP blast that has a 1/3 chance of disabling each enemy weapon,
    /// with a guaranteed minimum of 1 weapon disabled. Disabled weapons cannot fire
    /// for 2 turns.
    /// </summary>
    public bool ActivateEmp(PlayerData player, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers,
        IReadOnlyDictionary<PlayerSlot, List<WeaponBase>> allWeapons)
    {
        if (!player.Powerups.TryConsume(PowerupType.EmpBlast))
        {
            return false;
        }

        // Collect all valid enemy weapons
        List<(WeaponBase Weapon, PlayerSlot Owner)> candidates = new();
        foreach (var kvp in allWeapons)
        {
            if (kvp.Key == player.Slot)
            {
                continue;
            }

            if (!allPlayers.TryGetValue(kvp.Key, out PlayerData? enemy) || !enemy.IsAlive)
            {
                continue;
            }

            foreach (WeaponBase w in kvp.Value)
            {
                if (GodotObject.IsInstanceValid(w) && !w.IsDestroyed)
                {
                    candidates.Add((w, kvp.Key));
                }
            }
        }

        if (candidates.Count == 0)
        {
            player.Powerups.RefundConsumed(PowerupType.EmpBlast);
            GD.Print($"[Powerup] {player.Slot}: No enemy weapons to EMP. Refunded.");
            return false;
        }

        // Roll 1/3 chance per weapon
        RandomNumberGenerator rng = new();
        rng.Randomize();

        List<(WeaponBase Weapon, PlayerSlot Owner)> disabled = new();
        foreach (var (weapon, owner) in candidates)
        {
            if (rng.Randf() < 1f / 3f)
            {
                disabled.Add((weapon, owner));
            }
        }

        // Guarantee minimum 1 weapon disabled
        if (disabled.Count == 0)
        {
            int pickIndex = rng.RandiRange(0, candidates.Count - 1);
            disabled.Add(candidates[pickIndex]);
        }

        // Apply EMP to each disabled weapon
        bool success = false;
        foreach (var (weapon, owner) in disabled)
        {
            EmpData data = new(weapon.GetInstanceId(), weapon.WeaponId, owner);
            player.Powerups.AddActiveEffect(PowerupType.EmpBlast, player.Slot, 2, data);
            PowerupFX.SpawnEmpEffect(GetTree().Root, weapon.GlobalPosition);
            GD.Print($"[Powerup] {player.Slot}: EMP disabled {weapon.WeaponId} on {owner} for 2 turns.");
            success = true;
        }

        if (success)
        {
            Vector3 fxCenter = disabled[0].Weapon.GlobalPosition;
            PowerupActivated?.Invoke(PowerupType.EmpBlast, player.Slot, fxCenter);
            GD.Print($"[Powerup] {player.Slot}: EMP disabled {disabled.Count}/{candidates.Count} enemy weapons.");
        }

        return success;
    }

    /// <summary>
    /// Checks if a specific weapon instance is currently disabled by an EMP.
    /// Uses the weapon's Godot instance ID for exact match (not WeaponId string,
    /// since multiple weapons can share the same type ID like "cannon").
    /// </summary>
    public bool IsWeaponEmpDisabled(WeaponBase weapon, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        if (!GodotObject.IsInstanceValid(weapon))
        {
            return false;
        }

        ulong instanceId = weapon.GetInstanceId();
        foreach (PlayerData player in allPlayers.Values)
        {
            foreach (ActivePowerup effect in player.Powerups.GetActiveEffects(PowerupType.EmpBlast))
            {
                if (effect.TargetData is EmpData emp && emp.WeaponInstanceId == instanceId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Overload for backward compatibility — checks by weapon ID string.
    /// Prefer the WeaponBase overload for exact instance matching.
    /// </summary>
    public bool IsWeaponEmpDisabled(string weaponId, PlayerSlot weaponOwner, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        foreach (PlayerData player in allPlayers.Values)
        {
            foreach (ActivePowerup effect in player.Powerups.GetActiveEffects(PowerupType.EmpBlast))
            {
                if (effect.TargetData is EmpData emp &&
                    emp.WeaponId == weaponId &&
                    emp.TargetPlayer == weaponOwner)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────
    //  TURN TICK
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Called at the start of each turn to tick active powerup durations
    /// for the specified player, and handle expirations.
    /// Only ticks effects owned by the current player so duration is measured
    /// in that player's turns (effectively "rounds from their perspective").
    /// Returns a list of expired effects for FX cleanup.
    /// </summary>
    public List<ActivePowerup> TickAllPlayerEffects(IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers, PlayerSlot currentPlayer)
    {
        List<ActivePowerup> allExpired = new();

        if (!allPlayers.TryGetValue(currentPlayer, out PlayerData? currentPlayerData))
        {
            return allExpired;
        }

        List<ActivePowerup> expired = currentPlayerData.Powerups.TickAndExpire();
        foreach (ActivePowerup e in expired)
        {
            CleanupExpiredEffect(e);
            PowerupExpired?.Invoke(e.Type, e.Owner);
            GD.Print($"[Powerup] {e.Owner}: {e.Type} expired.");
        }
        allExpired.AddRange(expired);

        return allExpired;
    }

    /// <summary>
    /// Cleans up visual FX nodes when a powerup effect expires.
    /// </summary>
    private void CleanupExpiredEffect(ActivePowerup effect)
    {
        // Re-show zone chunks if smoke screen expired
        if (effect.Type == PowerupType.SmokeScreen && effect.TargetData is SmokeScreenData smokeData)
        {
            HideZoneChunks(smokeData.Zone, false);
        }

        string fxName = effect.Type switch
        {
            PowerupType.SmokeScreen => "SmokeScreenFX",
            PowerupType.ShieldGenerator => "ShieldBubbleFX",
            PowerupType.EmpBlast => "EmpFX",
            _ => "",
        };

        if (string.IsNullOrEmpty(fxName))
        {
            return;
        }

        Node root = GetTree().Root;
        foreach (Node child in root.GetChildren())
        {
            if (child.Name == fxName && GodotObject.IsInstanceValid(child))
            {
                child.QueueFree();
                break;
            }
        }
    }
}
