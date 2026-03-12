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
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Combat;

/// <summary>
/// Executes powerup effects during combat. Created as a child of GameManager
/// and orchestrates smoke, medkit, shield, airstrike, and EMP mechanics.
/// </summary>
public partial class PowerupExecutor : Node
{
    private VoxelWorld? _voxelWorld;
    private Dictionary<PlayerSlot, CommanderActor>? _cachedCommanders;

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
            TurnsRemaining = 1,
        };

        player.Powerups.AddActiveEffect(PowerupType.SmokeScreen, player.Slot, 1, data);

        // Spawn visual smoke cloud
        PowerupFX.SpawnSmokeScreen(GetTree().Root, center, zone);

        // Shader dissolve is applied by the caller via ReenforceSmokeScreens()
        // so that ALL active smoke zones (from multiple players) are set at once.

        PowerupActivated?.Invoke(PowerupType.SmokeScreen, player.Slot, center);
        GD.Print($"[Powerup] {player.Slot}: Smoke Screen deployed — fortress invisible for 1 round.");
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
    /// <summary>
    /// Floor division that rounds toward negative infinity (not toward zero like C#'s / operator).
    /// Required because microvoxel coordinates can be negative, and truncation toward zero
    /// maps e.g. -60/16 to -3 instead of the correct chunk index -4.
    /// </summary>
    private static int FloorDiv(int a, int b)
    {
        return a / b - (a % b != 0 && (a ^ b) < 0 ? 1 : 0);
    }

    private void HideZoneChunks(BuildZone zone, bool hide)
    {
        if (_voxelWorld == null)
        {
            GD.Print($"[Smoke] HideZoneChunks: _voxelWorld is null!");
            return;
        }

        // Use floor division for min (negative coords truncate wrong with C# /)
        // and ceiling-style for max (positive coords are fine with normal /)
        int cs = GameConfig.ChunkSize;
        Vector3I minChunk = new Vector3I(
            FloorDiv(zone.OriginMicrovoxels.X, cs),
            FloorDiv(zone.OriginMicrovoxels.Y, cs),
            FloorDiv(zone.OriginMicrovoxels.Z, cs));
        Vector3I maxChunk = new Vector3I(
            FloorDiv(zone.MaxMicrovoxelsInclusive.X, cs),
            FloorDiv(zone.MaxMicrovoxelsInclusive.Y, cs),
            FloorDiv(zone.MaxMicrovoxelsInclusive.Z, cs));

        int chunkCount = 0;
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
                        chunkCount++;
                    }
                }
            }
        }
        GD.Print($"[Smoke] HideZoneChunks(hide={hide}): zone {zone.OriginMicrovoxels}→{zone.MaxMicrovoxelsInclusive}, chunks {minChunk}→{maxChunk}, toggled {chunkCount} chunks");
    }

    /// <summary>
    /// Re-applies smoke dissolve shader for all active smoke screens.
    /// The shader-based dissolve survives chunk remeshing automatically,
    /// but this is still called on turn change as a safety net.
    /// </summary>
    public void ReenforceSmokeScreens(
        IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers,
        Dictionary<PlayerSlot, CommanderActor>? commanders = null)
    {
        // Cache commanders reference for internal calls (e.g. smoke expiration)
        if (commanders != null) _cachedCommanders = commanders;

        // Collect all active smoke zones and which players have smoke active
        List<BuildZone> activeZones = new();
        HashSet<PlayerSlot> smokedPlayers = new();
        foreach (PlayerData player in allPlayers.Values)
        {
            foreach (ActivePowerup effect in player.Powerups.GetActiveEffects(PowerupType.SmokeScreen))
            {
                if (effect.TargetData is SmokeScreenData smokeData)
                {
                    activeZones.Add(smokeData.Zone);
                    smokedPlayers.Add(player.Slot);
                }
            }
        }

        if (activeZones.Count > 0)
            SetAllSmokeZones(activeZones, 0.92f);
        else
            ClearSmokeZoneDissolve();

        // Hide/show commanders based on smoke status
        var cmds = commanders ?? _cachedCommanders;
        if (cmds != null)
        {
            foreach (var (slot, cmd) in cmds)
            {
                if (!GodotObject.IsInstanceValid(cmd) || cmd.IsDead) continue;
                cmd.Visible = !smokedPlayers.Contains(slot);
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

        // Duration = 1 round (expires after one full turn rotation)
        player.Powerups.AddActiveEffect(PowerupType.ShieldGenerator, player.Slot, 1, null);

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
        Node capturedRoot = GetTree().Root;

        AirstrikeFlyover.Spawn(
            GetTree().Root,
            targetWorld,
            player.PlayerColor,
            planeCount,
            onBombDrop: () =>
            {
                // Spawn falling bomb meshes that drop from plane altitude to impact
                float dropAltitude = targetWorld.Y + 20f; // matches AirstrikeFlyover.FlyoverAltitude
                for (int shell = 0; shell < 3; shell++)
                {
                    float delay = shell * 0.3f;
                    int capturedShell = shell;
                    Vector3 capturedImpact = impactPositions[shell];

                    capturedTree.CreateTimer(delay).Timeout += () =>
                    {
                        SpawnFallingBomb(capturedRoot, capturedWorld,
                            capturedImpact with { Y = dropAltitude },
                            capturedImpact, capturedInstigator, capturedShell);
                    };
                }
            });

        PowerupActivated?.Invoke(PowerupType.AirstrikeBeacon, player.Slot, targetWorld);
        GD.Print($"[Powerup] {player.Slot}: Airstrike called on {targetBuildUnit} targeting {targetEnemy}.");
        return true;
    }

    /// <summary>
    /// Spawns a dark bomb mesh that falls from dropStart to impactPos over ~0.8s,
    /// then triggers an explosion on arrival.
    /// </summary>
    private static void SpawnFallingBomb(Node parent, VoxelWorld world,
        Vector3 dropStart, Vector3 impactPos, PlayerSlot instigator, int bombIndex)
    {
        Node3D bomb = new Node3D();
        bomb.Name = $"AirstrikeBomb_{bombIndex}";
        parent.AddChild(bomb);
        bomb.GlobalPosition = dropStart;

        // Bomb body — dark cylinder
        MeshInstance3D body = new MeshInstance3D();
        CylinderMesh bodyMesh = new CylinderMesh();
        bodyMesh.TopRadius = 0.15f;
        bodyMesh.BottomRadius = 0.15f;
        bodyMesh.Height = 0.6f;
        bodyMesh.RadialSegments = 6;
        body.Mesh = bodyMesh;
        StandardMaterial3D bombMat = new StandardMaterial3D();
        bombMat.AlbedoColor = new Color(0.15f, 0.15f, 0.15f);
        bombMat.Metallic = 0.6f;
        body.MaterialOverride = bombMat;
        bomb.AddChild(body);

        // Nose cone
        MeshInstance3D nose = new MeshInstance3D();
        CylinderMesh noseMesh = new CylinderMesh();
        noseMesh.TopRadius = 0f;
        noseMesh.BottomRadius = 0.15f;
        noseMesh.Height = 0.2f;
        noseMesh.RadialSegments = 6;
        nose.Mesh = noseMesh;
        nose.MaterialOverride = bombMat;
        nose.Position = new Vector3(0, -0.4f, 0); // below body
        bomb.AddChild(nose);

        // Tail fins (small box)
        MeshInstance3D fins = new MeshInstance3D();
        BoxMesh finMesh = new BoxMesh();
        finMesh.Size = new Vector3(0.35f, 0.08f, 0.35f);
        fins.Mesh = finMesh;
        fins.MaterialOverride = bombMat;
        fins.Position = new Vector3(0, 0.3f, 0); // top of body
        bomb.AddChild(fins);

        // Animate the fall using a Tween (accelerating, simulates gravity)
        float fallDuration = 0.8f;
        Tween tween = bomb.CreateTween();
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.In); // accelerates like gravity
        tween.TweenProperty(bomb, "global_position", impactPos, fallDuration);
        tween.TweenCallback(Callable.From(() =>
        {
            Explosion.Trigger(parent, world, impactPos, 40, 4f, instigator);
            GD.Print($"[Powerup] Airstrike bomb {bombIndex + 1}/3 hit at {impactPos}.");
            bomb.QueueFree();
        }));
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
        bool smokeExpired = false;
        foreach (ActivePowerup e in expired)
        {
            if (e.Type == PowerupType.SmokeScreen) smokeExpired = true;
            CleanupExpiredEffect(e);
            PowerupExpired?.Invoke(e.Type, e.Owner);
            GD.Print($"[Powerup] {e.Owner}: {e.Type} expired.");
        }
        allExpired.AddRange(expired);

        // When a smoke expires, re-sync shader with any remaining active smoke zones
        if (smokeExpired)
            ReenforceSmokeScreens(allPlayers);

        return allExpired;
    }

    /// <summary>
    /// Force-expires all active powerup effects for a player (called when their commander dies).
    /// Prevents orphaned effects that never tick down because the dead player is removed from turn order.
    /// </summary>
    public void ExpireAllEffects(PlayerData player, IReadOnlyDictionary<PlayerSlot, PlayerData> allPlayers)
    {
        // Force all remaining turns to 0 and tick to expire them
        foreach (ActivePowerup effect in player.Powerups.ActiveEffects)
            effect.RemainingTurns = 0;

        List<ActivePowerup> expired = player.Powerups.TickAndExpire();
        bool smokeExpired = false;
        foreach (ActivePowerup e in expired)
        {
            if (e.Type == PowerupType.SmokeScreen) smokeExpired = true;
            CleanupExpiredEffect(e);
            PowerupExpired?.Invoke(e.Type, e.Owner);
            GD.Print($"[Powerup] {e.Owner}: {e.Type} force-expired (commander killed).");
        }

        if (smokeExpired)
            ReenforceSmokeScreens(allPlayers);
    }

    /// <summary>
    /// Cleans up visual FX nodes when a powerup effect expires.
    /// </summary>
    private void CleanupExpiredEffect(ActivePowerup effect)
    {
        // Smoke dissolve cleanup is handled by ReenforceSmokeScreens in TickAllPlayerEffects
        // (it re-syncs remaining active zones after any smoke expires)

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
                if (effect.Type == PowerupType.SmokeScreen && child is Node3D smokeRoot)
                {
                    // Fade the smoke cloud out over 0.5s so it disappears at the same
                    // time as the dissolve shader clears (no visible gap).
                    // Stop emitting immediately, then tween alpha to 0 and free.
                    foreach (Node sub in smokeRoot.GetChildren())
                    {
                        if (sub is GpuParticles3D particles)
                            particles.Emitting = false;
                    }
                    Tween tween = smokeRoot.CreateTween();
                    tween.TweenProperty(smokeRoot, "modulate:a", 0f, 0.5f);
                    tween.TweenCallback(Callable.From(() =>
                    {
                        if (GodotObject.IsInstanceValid(smokeRoot))
                            smokeRoot.QueueFree();
                    }));
                }
                else
                {
                    child.QueueFree();
                }
                break;
            }
        }
    }

    /// <summary>
    /// Sets ALL active smoke zones on the shared voxel shader at once.
    /// Supports up to 4 simultaneous zones (one per player).
    /// </summary>
    private static void SetAllSmokeZones(List<BuildZone> zones, float dissolve)
    {
        ShaderMaterial mat = VoxelChunk.GetSharedOpaqueShaderMaterial();
        mat.SetShaderParameter("smoke_dissolve", dissolve);
        mat.SetShaderParameter("smoke_zone_count", zones.Count);

        Vector3 noZone = new Vector3(-99999f, -99999f, -99999f);
        for (int i = 0; i < 4; i++)
        {
            Vector3 minWorld = noZone;
            Vector3 maxWorld = noZone;
            if (i < zones.Count)
            {
                minWorld = MathHelpers.MicrovoxelToWorld(zones[i].OriginMicrovoxels);
                maxWorld = MathHelpers.MicrovoxelToWorld(zones[i].MaxMicrovoxelsInclusive + Vector3I.One);
                // Raise the Y minimum so the ground/foundation layer is NOT dissolved
                minWorld.Y += GameConfig.MicrovoxelMeters * 0.5f;
            }
            mat.SetShaderParameter($"smoke_zone_min_{i}", minWorld);
            mat.SetShaderParameter($"smoke_zone_max_{i}", maxWorld);
        }
        GD.Print($"[Smoke] SetAllSmokeZones: {zones.Count} active zones");
    }

    /// <summary>
    /// Clears the smoke dissolve shader — restores all blocks to fully visible.
    /// </summary>
    private static void ClearSmokeZoneDissolve()
    {
        ShaderMaterial mat = VoxelChunk.GetSharedOpaqueShaderMaterial();
        mat.SetShaderParameter("smoke_dissolve", 0f);
        mat.SetShaderParameter("smoke_zone_count", 0);
        Vector3 noZone = new Vector3(-99999f, -99999f, -99999f);
        for (int i = 0; i < 4; i++)
        {
            mat.SetShaderParameter($"smoke_zone_min_{i}", noZone);
            mat.SetShaderParameter($"smoke_zone_max_{i}", noZone);
        }
    }

    /// <summary>
    /// Cleans up ALL powerup FX nodes from the scene tree.
    /// Called on game end / return to menu to prevent FX persisting to the menu.
    /// </summary>
    public void CleanupAllFX()
    {
        ClearSmokeZoneDissolve();

        string[] fxNames = { "SmokeScreenFX", "ShieldBubbleFX", "EmpFX" };
        Node root = GetTree().Root;
        foreach (Node child in root.GetChildren())
        {
            if (GodotObject.IsInstanceValid(child) && fxNames.Contains(child.Name.ToString()))
            {
                child.QueueFree();
            }
        }
    }
}
