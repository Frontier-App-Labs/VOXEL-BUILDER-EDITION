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
/// and orchestrates smoke, repair, drone, shield, airstrike, and EMP mechanics.
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
        // VoxelWorld is expected to be a sibling node named "GameWorld"
        _voxelWorld = GetParent()?.GetNodeOrNull<VoxelWorld>("GameWorld");
    }

    // ─────────────────────────────────────────────────
    //  SMOKE SCREEN
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Activates smoke screen over the player's build zone. Consumes the powerup
    /// from inventory and registers an active effect for 2 turns.
    /// </summary>
    public bool ActivateSmokeScreen(PlayerData player)
    {
        if (!player.Powerups.TryConsume(PowerupType.SmokeScreen))
        {
            return false;
        }

        BuildZone zone = player.AssignedBuildZone;
        Vector3 center = MathHelpers.MicrovoxelToWorld(
            zone.OriginMicrovoxels + zone.SizeMicrovoxels / 2);

        player.Powerups.AddActiveEffect(PowerupType.SmokeScreen, player.Slot, 2, zone);

        // Spawn visual smoke cloud
        PowerupFX.SpawnSmokeScreen(GetTree().Root, center, zone);

        PowerupActivated?.Invoke(PowerupType.SmokeScreen, player.Slot, center);
        GD.Print($"[Powerup] {player.Slot}: Smoke Screen deployed over build zone.");
        return true;
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
                if (effect.TargetData is not BuildZone zone)
                {
                    continue;
                }

                // Check if projectile is within the smoke zone (expanded slightly)
                Vector3I projMicro = MathHelpers.WorldToMicrovoxel(projectilePosition);
                Vector3I expandedMin = zone.OriginMicrovoxels - new Vector3I(2, 2, 2);
                Vector3I expandedMax = zone.MaxMicrovoxelsInclusive + new Vector3I(2, 2, 2);

                if (projMicro.X >= expandedMin.X && projMicro.X <= expandedMax.X &&
                    projMicro.Y >= expandedMin.Y && projMicro.Y <= expandedMax.Y &&
                    projMicro.Z >= expandedMin.Z && projMicro.Z <= expandedMax.Z)
                {
                    // Deflect by +/- 15 degrees randomly
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
    //  REPAIR KIT
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Instantly repairs up to 20 damaged voxels in the player's build zone,
    /// targeting the most damaged blocks first.
    /// </summary>
    public bool ActivateRepairKit(PlayerData player)
    {
        if (!player.Powerups.TryConsume(PowerupType.RepairKit))
        {
            return false;
        }

        if (_voxelWorld == null)
        {
            return false;
        }

        BuildZone zone = player.AssignedBuildZone;
        Vector3I min = zone.OriginMicrovoxels;
        Vector3I max = zone.MaxMicrovoxelsInclusive;

        // Collect all damaged voxels in the zone
        List<(Vector3I Position, VoxelValue Voxel, float DamagePercent)> damaged = new();

        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    Vector3I pos = new(x, y, z);
                    VoxelValue voxel = _voxelWorld.GetVoxel(pos);
                    if (voxel.IsAir || voxel.Material == VoxelMaterialType.Foundation)
                    {
                        continue;
                    }

                    int maxHp = VoxelMaterials.GetDefinition(voxel.Material).MaxHitPoints;
                    if (voxel.HitPoints < maxHp)
                    {
                        float damagePercent = 1f - (voxel.HitPoints / (float)maxHp);
                        damaged.Add((pos, voxel, damagePercent));
                    }
                }
            }
        }

        // Sort by most damaged first
        damaged.Sort((a, b) => b.DamagePercent.CompareTo(a.DamagePercent));

        int repaired = 0;
        const int maxRepairs = 20;

        Vector3 fxCenter = Vector3.Zero;

        for (int i = 0; i < damaged.Count && repaired < maxRepairs; i++)
        {
            var (pos, voxel, _) = damaged[i];
            int maxHp = VoxelMaterials.GetDefinition(voxel.Material).MaxHitPoints;
            _voxelWorld.SetVoxel(pos, voxel.WithHitPoints(maxHp).WithDamaged(false), player.Slot);
            fxCenter += MathHelpers.MicrovoxelToWorld(pos);
            repaired++;
        }

        if (repaired > 0)
        {
            fxCenter /= repaired;
            PowerupFX.SpawnRepairEffect(GetTree().Root, fxCenter, repaired);
        }
        else
        {
            // Refund if nothing to repair -- give the powerup back
            player.Powerups.TryBuy(PowerupType.RepairKit, player);
            GD.Print($"[Powerup] {player.Slot}: No damaged voxels found. Repair Kit refunded.");
            return false;
        }

        PowerupActivated?.Invoke(PowerupType.RepairKit, player.Slot, fxCenter);
        GD.Print($"[Powerup] {player.Slot}: Repair Kit healed {repaired} voxels.");
        return true;
    }

    // ─────────────────────────────────────────────────
    //  SPY DRONE
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Reveals the approximate location of the nearest enemy commander for 1 turn.
    /// Returns the approximate world position, or null if no enemy found.
    /// </summary>
    public Vector3? ActivateSpyDrone(PlayerData player, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        if (!player.Powerups.TryConsume(PowerupType.SpyDrone))
        {
            return null;
        }

        // Find first alive enemy with a known commander position
        foreach (PlayerData enemy in allPlayers.Values)
        {
            if (enemy.Slot == player.Slot || !enemy.IsAlive)
            {
                continue;
            }

            if (enemy.CommanderMicrovoxelPosition is Vector3I cmdPos)
            {
                // Add random offset of +/- 3 build units (6 microvoxels)
                RandomNumberGenerator rng = new();
                rng.Randomize();
                int offsetX = rng.RandiRange(-6, 6);
                int offsetZ = rng.RandiRange(-6, 6);
                Vector3I approxPos = cmdPos + new Vector3I(offsetX, 0, offsetZ);
                Vector3 worldPos = MathHelpers.MicrovoxelToWorld(approxPos);

                player.Powerups.AddActiveEffect(PowerupType.SpyDrone, player.Slot, 1, worldPos);
                PowerupFX.SpawnDroneHighlight(GetTree().Root, worldPos);
                PowerupActivated?.Invoke(PowerupType.SpyDrone, player.Slot, worldPos);
                GD.Print($"[Powerup] {player.Slot}: Spy Drone reveals enemy at ~{worldPos}.");
                return worldPos;
            }
        }

        // No valid enemy found -- refund
        player.Powerups.TryBuy(PowerupType.SpyDrone, player);
        GD.Print($"[Powerup] {player.Slot}: No enemy commander found. Spy Drone refunded.");
        return null;
    }

    // ─────────────────────────────────────────────────
    //  SHIELD GENERATOR
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Data for an active shield: center position and radius in build units.
    /// </summary>
    public readonly record struct ShieldData(Vector3I CenterBuildUnit, int RadiusBuildUnits);

    /// <summary>
    /// Creates a shield bubble over a 5x5x5 build unit area centered at the specified
    /// build unit position. Lasts 2 turns. Blocks inside take 50% less damage.
    /// </summary>
    public bool ActivateShieldGenerator(PlayerData player, Vector3I centerBuildUnit)
    {
        if (!player.Powerups.TryConsume(PowerupType.ShieldGenerator))
        {
            return false;
        }

        ShieldData data = new(centerBuildUnit, 2); // radius 2 = 5x5x5 area
        player.Powerups.AddActiveEffect(PowerupType.ShieldGenerator, player.Slot, 2, data);

        Vector3 worldCenter = MathHelpers.MicrovoxelToWorld(
            MathHelpers.BuildToMicrovoxel(centerBuildUnit));
        PowerupFX.SpawnShieldBubble(GetTree().Root, worldCenter, 2.5f);

        PowerupActivated?.Invoke(PowerupType.ShieldGenerator, player.Slot, worldCenter);
        GD.Print($"[Powerup] {player.Slot}: Shield Generator at {centerBuildUnit} for 2 turns.");
        return true;
    }

    /// <summary>
    /// Checks if a microvoxel position is within any active shield, and returns
    /// the damage multiplier (0.5 if shielded, 1.0 if not).
    /// </summary>
    public float GetShieldDamageMultiplier(Vector3I microvoxelPosition, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        Vector3I buildUnit = MathHelpers.MicrovoxelToBuild(microvoxelPosition);

        foreach (PlayerData player in allPlayers.Values)
        {
            foreach (ActivePowerup effect in player.Powerups.GetActiveEffects(PowerupType.ShieldGenerator))
            {
                if (effect.TargetData is not ShieldData shield)
                {
                    continue;
                }

                int dx = Math.Abs(buildUnit.X - shield.CenterBuildUnit.X);
                int dy = Math.Abs(buildUnit.Y - shield.CenterBuildUnit.Y);
                int dz = Math.Abs(buildUnit.Z - shield.CenterBuildUnit.Z);

                if (dx <= shield.RadiusBuildUnits && dy <= shield.RadiusBuildUnits && dz <= shield.RadiusBuildUnits)
                {
                    return 0.5f; // 50% damage reduction
                }
            }
        }

        return 1.0f;
    }

    // ─────────────────────────────────────────────────
    //  AIRSTRIKE BEACON
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Calls in 1-3 fighter jets that fly across the arena and drop 3 bombardment
    /// shells on an 8x8 build unit area of an enemy's fortress. The planes fly in
    /// from one edge, cross the map, and exit the other side.
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

        // Show targeting circles before impact
        PowerupFX.SpawnAirstrikeTargeting(GetTree().Root, targetWorld);

        // Pre-calculate the 3 bomb impact positions so we can fire them from the flyover callback
        RandomNumberGenerator rng = new();
        rng.Randomize();

        int planeCount = rng.RandiRange(1, 3);

        Vector3[] impactPositions = new Vector3[3];
        for (int shell = 0; shell < 3; shell++)
        {
            // Random position within 8x8 build unit area (16x16 microvoxels)
            int offsetX = rng.RandiRange(-8, 8);
            int offsetZ = rng.RandiRange(-8, 8);
            Vector3I impactMicro = MathHelpers.BuildToMicrovoxel(targetBuildUnit) + new Vector3I(offsetX, 0, offsetZ);

            // Find the highest non-air voxel at this XZ column
            int surfaceY = FindSurfaceY(impactMicro.X, impactMicro.Z);
            impactMicro = impactMicro with { Y = surfaceY };

            impactPositions[shell] = MathHelpers.MicrovoxelToWorld(impactMicro);
        }

        // Capture references for the lambda
        VoxelWorld capturedWorld = _voxelWorld;
        PlayerSlot capturedInstigator = player.Slot;
        SceneTree capturedTree = GetTree();

        // Spawn fighter jet flyover -- bombs drop when the planes reach the target
        AirstrikeFlyover.Spawn(
            GetTree().Root,
            targetWorld,
            player.PlayerColor,
            planeCount,
            onBombDrop: () =>
            {
                // Stagger the 3 bomb explosions over a short window for dramatic effect
                for (int shell = 0; shell < 3; shell++)
                {
                    float delay = shell * 0.3f;
                    int capturedShell = shell;
                    Vector3 capturedImpact = impactPositions[shell];

                    capturedTree.CreateTimer(delay).Timeout += () =>
                    {
                        // Cannon-equivalent damage: 40 base, 4 radius
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

        // Scan from top down to find the highest solid voxel
        int maxY = GameConfig.PrototypeGroundThickness + GameConfig.FourPlayerBuildZoneHeight * GameConfig.MicrovoxelsPerBuildUnit;
        for (int y = maxY; y >= 0; y--)
        {
            VoxelValue voxel = _voxelWorld.GetVoxel(new Vector3I(microX, y, microZ));
            if (voxel.IsSolid)
            {
                return y + 1; // Impact on top of the solid voxel
            }
        }

        return GameConfig.PrototypeGroundThickness;
    }

    // ─────────────────────────────────────────────────
    //  EMP BLAST
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Data for an active EMP: which weapon is disabled.
    /// </summary>
    public readonly record struct EmpData(string WeaponId, PlayerSlot TargetPlayer);

    /// <summary>
    /// Disables an enemy weapon for 2 turns. The weapon cannot fire while EMP'd.
    /// </summary>
    public bool ActivateEmp(PlayerData player, WeaponBase targetWeapon, PlayerSlot targetPlayer)
    {
        if (!player.Powerups.TryConsume(PowerupType.EmpBlast))
        {
            return false;
        }

        EmpData data = new(targetWeapon.WeaponId, targetPlayer);
        player.Powerups.AddActiveEffect(PowerupType.EmpBlast, player.Slot, 2, data);

        PowerupFX.SpawnEmpEffect(GetTree().Root, targetWeapon.GlobalPosition);
        PowerupActivated?.Invoke(PowerupType.EmpBlast, player.Slot, targetWeapon.GlobalPosition);
        GD.Print($"[Powerup] {player.Slot}: EMP disabled {targetWeapon.WeaponId} on {targetPlayer} for 2 turns.");
        return true;
    }

    /// <summary>
    /// Checks if a specific weapon is currently disabled by an EMP.
    /// Searches all players' active effects for an EMP targeting this weapon.
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

        // Only tick effects belonging to the current player's turn,
        // so that a "2 turn" duration means 2 of THIS player's turns.
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
        string fxName = effect.Type switch
        {
            PowerupType.SmokeScreen => "SmokeScreenFX",
            PowerupType.ShieldGenerator => "ShieldBubbleFX",
            PowerupType.EmpBlast => "EmpFX",
            PowerupType.SpyDrone => "DroneHighlightFX",
            _ => "",
        };

        if (string.IsNullOrEmpty(fxName))
        {
            return;
        }

        // Search the scene tree root for FX nodes with matching name
        Node root = GetTree().Root;
        foreach (Node child in root.GetChildren())
        {
            if (child.Name == fxName && GodotObject.IsInstanceValid(child))
            {
                child.QueueFree();
                break; // Remove one instance per expired effect
            }
        }
    }
}
