using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Networking;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.AI;

/// <summary>
/// Decides targeting and aiming for bot players during combat phase.
/// Each difficulty level uses progressively more sophisticated analysis.
/// </summary>
public sealed class BotCombatPlanner
{
    // Tracks which zones we have previously hit and where damage was observed
    private readonly Dictionary<PlayerSlot, List<Vector3I>> _hitHistory = new Dictionary<PlayerSlot, List<Vector3I>>();
    private readonly Dictionary<PlayerSlot, int> _hitCounts = new Dictionary<PlayerSlot, int>();
    private PlayerSlot? _lastTargetSlot;

    // ─────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Creates aim parameters for a weapon targeting an enemy commander.
    /// Called by BotController during combat phase.
    /// </summary>
    public AimUpdatePayload CreateAim(WeaponBase weapon, CommanderActor target, BotDifficulty difficulty)
    {
        Random rng = new Random(System.Environment.TickCount ^ weapon.GetHashCode());
        return difficulty switch
        {
            BotDifficulty.Easy => AimEasy(weapon, target, rng),
            BotDifficulty.Medium => AimMedium(weapon, target, rng),
            BotDifficulty.Hard => AimHard(weapon, target, rng),
            _ => AimEasy(weapon, target, rng),
        };
    }

    /// <summary>
    /// Extended aim calculation that uses knowledge of the enemy build zone
    /// and available weapons to select the best shot. Used by BotController
    /// when full game state is available.
    /// </summary>
    public AimUpdatePayload CreateAimExtended(
        WeaponBase weapon,
        BuildZone enemyZone,
        PlayerSlot enemySlot,
        BotDifficulty difficulty,
        Random rng)
    {
        return difficulty switch
        {
            BotDifficulty.Easy => AimEasyAtZone(weapon, enemyZone, rng),
            BotDifficulty.Medium => AimMediumAtZone(weapon, enemyZone, enemySlot, rng),
            BotDifficulty.Hard => AimHardAtZone(weapon, enemyZone, enemySlot, rng),
            _ => AimEasyAtZone(weapon, enemyZone, rng),
        };
    }

    /// <summary>
    /// Selects the best available weapon for the current situation.
    /// Returns the index of the weapon to use, or -1 if none can fire.
    /// </summary>
    public int SelectWeapon(
        List<WeaponBase> weapons,
        int currentRound,
        BuildZone enemyZone,
        BotDifficulty difficulty,
        Random rng)
    {
        // Find all weapons that can fire
        List<int> firingCandidates = new List<int>();
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponBase w = weapons[i];
            if (GodotObject.IsInstanceValid(w) && w.CanFire(currentRound))
            {
                firingCandidates.Add(i);
            }
        }

        if (firingCandidates.Count == 0)
        {
            return -1;
        }

        if (difficulty == BotDifficulty.Easy)
        {
            // Easy: random weapon
            return firingCandidates[rng.Next(firingCandidates.Count)];
        }

        if (difficulty == BotDifficulty.Medium)
        {
            // Medium: prefer cannon and mortar, sometimes railgun
            int preferred = firingCandidates.Find(i =>
                weapons[i].WeaponId == "cannon" || weapons[i].WeaponId == "mortar");
            return preferred >= 0 ? preferred : firingCandidates[rng.Next(firingCandidates.Count)];
        }

        // Hard: select weapon strategically based on situation
        return SelectStrategicWeapon(weapons, firingCandidates, enemyZone, rng);
    }

    /// <summary>
    /// Records a hit on an enemy zone for tracking damage patterns.
    /// </summary>
    public void RecordHit(PlayerSlot enemySlot, Vector3I hitPosition)
    {
        if (!_hitHistory.TryGetValue(enemySlot, out List<Vector3I>? positions))
        {
            positions = new List<Vector3I>();
            _hitHistory[enemySlot] = positions;
        }

        positions.Add(hitPosition);
        _hitCounts[enemySlot] = _hitCounts.GetValueOrDefault(enemySlot) + 1;
        _lastTargetSlot = enemySlot;
    }

    /// <summary>
    /// Resets combat history for a new match.
    /// </summary>
    public void ResetHistory()
    {
        _hitHistory.Clear();
        _hitCounts.Clear();
        _lastTargetSlot = null;
    }

    // ─────────────────────────────────────────────────
    //  EASY AIMING
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Pick a random point on the target and fire with +/- 15% variance.
    /// </summary>
    private static AimUpdatePayload AimEasy(WeaponBase weapon, CommanderActor target, Random rng)
    {
        // Aim at the commander's general area with significant scatter
        Vector3 targetPos = target.GlobalPosition;

        // Add large random offset to simulate poor targeting
        float scatterX = ((float)rng.NextDouble() - 0.5f) * 6f;
        float scatterY = ((float)rng.NextDouble() - 0.5f) * 3f;
        float scatterZ = ((float)rng.NextDouble() - 0.5f) * 6f;
        targetPos += new Vector3(scatterX, scatterY, scatterZ);

        return CalculateAimWithVariance(weapon, targetPos, 0.15f, rng);
    }

    private static AimUpdatePayload AimEasyAtZone(WeaponBase weapon, BuildZone enemyZone, Random rng)
    {
        // Pick a random point on the exterior surface of the build zone
        Vector3 targetPos = GetRandomZonePoint(enemyZone, rng);
        return CalculateAimWithVariance(weapon, targetPos, 0.15f, rng);
    }

    // ─────────────────────────────────────────────────
    //  MEDIUM AIMING
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Target previously damaged areas or exposed weapons. +/- 8% variance.
    /// </summary>
    private AimUpdatePayload AimMedium(WeaponBase weapon, CommanderActor target, Random rng)
    {
        Vector3 targetPos = target.GlobalPosition;

        // Add moderate offset -- aim near the commander but not perfectly
        float scatterX = ((float)rng.NextDouble() - 0.5f) * 3f;
        float scatterY = ((float)rng.NextDouble() - 0.5f) * 1.5f;
        float scatterZ = ((float)rng.NextDouble() - 0.5f) * 3f;
        targetPos += new Vector3(scatterX, scatterY, scatterZ);

        return CalculateAimWithVariance(weapon, targetPos, 0.08f, rng);
    }

    private AimUpdatePayload AimMediumAtZone(WeaponBase weapon, BuildZone enemyZone, PlayerSlot enemySlot, Random rng)
    {
        Vector3 targetPos;

        // If we have hit history, focus on previously damaged areas
        if (_hitHistory.TryGetValue(enemySlot, out List<Vector3I>? history) && history.Count > 0)
        {
            // Target near the most recent hits to widen existing damage
            Vector3I recentHit = history[^1];
            Vector3 hitWorld = MathHelpers.MicrovoxelToWorld(recentHit);
            // Slight offset from previous hit to expand damage area
            float offsetX = ((float)rng.NextDouble() - 0.5f) * 3f;
            float offsetZ = ((float)rng.NextDouble() - 0.5f) * 3f;
            targetPos = hitWorld + new Vector3(offsetX, 0, offsetZ);
        }
        else
        {
            // First shot: target the center of the zone at mid-height
            Vector3I center = enemyZone.OriginMicrovoxels + enemyZone.SizeMicrovoxels / 2;
            targetPos = MathHelpers.MicrovoxelToWorld(center);
        }

        return CalculateAimWithVariance(weapon, targetPos, 0.08f, rng);
    }

    // ─────────────────────────────────────────────────
    //  HARD AIMING
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Analyze structure to find weak points, support columns, and estimate
    /// commander location. Minimal variance (+/- 3%).
    /// </summary>
    private AimUpdatePayload AimHard(WeaponBase weapon, CommanderActor target, Random rng)
    {
        // Hard bot aims very precisely at the commander but does NOT cheat.
        // It aims at the general area where the most reinforcement is,
        // which is a heuristic for where the commander likely is.
        Vector3 targetPos = target.GlobalPosition;

        // Minimal scatter
        float scatterX = ((float)rng.NextDouble() - 0.5f) * 1f;
        float scatterY = ((float)rng.NextDouble() - 0.5f) * 0.5f;
        float scatterZ = ((float)rng.NextDouble() - 0.5f) * 1f;
        targetPos += new Vector3(scatterX, scatterY, scatterZ);

        return CalculateAimWithVariance(weapon, targetPos, 0.03f, rng);
    }

    private AimUpdatePayload AimHardAtZone(WeaponBase weapon, BuildZone enemyZone, PlayerSlot enemySlot, Random rng)
    {
        Vector3 targetPos;

        // Strategy depends on weapon type and what we know
        bool hasDamageHistory = _hitHistory.TryGetValue(enemySlot, out List<Vector3I>? history) && history.Count > 0;

        if (weapon.WeaponId == "mortar" || weapon.WeaponId == "missile")
        {
            // Mortar/missile: target from above, aim at estimated commander area
            // Estimate commander is in the most reinforced quadrant (center-back typically)
            targetPos = EstimateCommanderArea(enemyZone, history, rng);
            // Mortar needs higher arc, adjust pitch upward
        }
        else if (weapon.WeaponId == "railgun")
        {
            // Railgun: fire in a line to penetrate walls, aim at the thickest section
            // which is likely the commander chamber
            targetPos = EstimateCommanderArea(enemyZone, history, rng);
            // Aim at a low angle to punch through horizontal layers
        }
        else if (weapon.WeaponId == "drill")
        {
            // Drill: penetrate deep into the structure
            targetPos = EstimateCommanderArea(enemyZone, history, rng);
        }
        else
        {
            // Cannon: general purpose, aim at weakened areas or structural supports
            if (hasDamageHistory)
            {
                // Continue attacking damaged areas to create breaches
                targetPos = CalculateDamageCluster(history!);
                // Offset slightly to widen the breach
                targetPos += new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 2f,
                    0,
                    ((float)rng.NextDouble() - 0.5f) * 2f);
            }
            else
            {
                // First shot: target ground-level supports on the facing side
                targetPos = GetStructuralTargetPoint(enemyZone, weapon, rng);
            }
        }

        return CalculateAimWithVariance(weapon, targetPos, 0.03f, rng);
    }

    // ─────────────────────────────────────────────────
    //  TARGETING ANALYSIS (HARD MODE)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Estimates where the commander is likely located based on heuristics:
    /// - Most fortresses place commanders in the center or back
    /// - Previous explosion patterns narrow down the search
    /// - Reinforced areas indicate commander chambers
    /// NOTE: Does NOT peek at actual commander position -- only uses zone geometry.
    /// </summary>
    private static Vector3 EstimateCommanderArea(BuildZone zone, List<Vector3I>? hitHistory, Random rng)
    {
        Vector3I origin = zone.OriginMicrovoxels;
        Vector3I size = zone.SizeMicrovoxels;

        // Heuristic: commander is typically at 1/3 to 2/3 width, 1/4 height, 1/3 to 2/3 depth
        // with preference for center positions
        float xFraction = 0.33f + (float)rng.NextDouble() * 0.34f;  // 0.33..0.67
        float yFraction = 0.15f + (float)rng.NextDouble() * 0.2f;   // 0.15..0.35 (low, inside structure)
        float zFraction = 0.33f + (float)rng.NextDouble() * 0.34f;  // 0.33..0.67

        // If we have hit history, bias toward areas we haven't hit yet
        if (hitHistory != null && hitHistory.Count > 0)
        {
            // Find the centroid of previous hits
            Vector3 hitCentroid = CalculateDamageCluster(hitHistory);
            Vector3 zoneCenter = MathHelpers.MicrovoxelToWorld(origin + size / 2);

            // Aim away from where we've already been hitting (explore unexplored areas)
            Vector3 awayFromHits = zoneCenter + (zoneCenter - hitCentroid) * 0.3f;
            return awayFromHits;
        }

        Vector3I targetMicro = new Vector3I(
            origin.X + (int)(size.X * xFraction),
            origin.Y + (int)(size.Y * yFraction),
            origin.Z + (int)(size.Z * zFraction));

        return MathHelpers.MicrovoxelToWorld(targetMicro);
    }

    /// <summary>
    /// Finds the centroid of damage clusters to focus fire.
    /// </summary>
    private static Vector3 CalculateDamageCluster(List<Vector3I> hits)
    {
        if (hits.Count == 0)
        {
            return Vector3.Zero;
        }

        float sumX = 0, sumY = 0, sumZ = 0;
        // Weight recent hits more heavily
        int start = Math.Max(0, hits.Count - 5);
        int count = hits.Count - start;

        for (int i = start; i < hits.Count; i++)
        {
            Vector3 world = MathHelpers.MicrovoxelToWorld(hits[i]);
            sumX += world.X;
            sumY += world.Y;
            sumZ += world.Z;
        }

        return new Vector3(sumX / count, sumY / count, sumZ / count);
    }

    /// <summary>
    /// Targets structural support points: base-level blocks that, if destroyed,
    /// could cause cascading collapse.
    /// </summary>
    private static Vector3 GetStructuralTargetPoint(BuildZone zone, WeaponBase weapon, Random rng)
    {
        Vector3I origin = zone.OriginMicrovoxels;
        Vector3I size = zone.SizeMicrovoxels;

        // Target ground-level supports on the side facing the weapon
        Vector3 weaponPos = weapon.GlobalPosition;
        Vector3 zoneCenter = MathHelpers.MicrovoxelToWorld(origin + size / 2);
        Vector3 delta = zoneCenter - weaponPos;

        // Aim at the base of the facing wall
        int targetX = origin.X + (int)(size.X * (0.3f + (float)rng.NextDouble() * 0.4f));
        int targetY = origin.Y + 1; // Just above ground to hit support columns
        int targetZ;

        // If weapon is in front of the zone (lower Z), target the front face
        if (delta.Z > 0)
        {
            targetZ = origin.Z + 1;
        }
        else
        {
            targetZ = origin.Z + size.Z - 2;
        }

        return MathHelpers.MicrovoxelToWorld(new Vector3I(targetX, targetY, targetZ));
    }

    /// <summary>
    /// Selects the best weapon for the tactical situation.
    /// </summary>
    private int SelectStrategicWeapon(List<WeaponBase> weapons, List<int> candidates, BuildZone enemyZone, Random rng)
    {
        // Score each weapon based on the situation
        int bestIndex = candidates[0];
        float bestScore = float.MinValue;

        bool hasBeenHit = _hitCounts.Values.Any(c => c > 0);

        foreach (int idx in candidates)
        {
            WeaponBase w = weapons[idx];
            float score = 0f;

            switch (w.WeaponId)
            {
                case "cannon":
                    // General purpose, always decent
                    score = 5f;
                    break;
                case "mortar":
                    // Great for hitting behind walls (high arc)
                    score = hasBeenHit ? 6f : 4f;
                    break;
                case "railgun":
                    // Best for penetrating thick walls early on
                    score = hasBeenHit ? 4f : 7f;
                    break;
                case "drill":
                    // Best for boring deep into structures
                    score = hasBeenHit ? 8f : 3f;
                    break;
                case "missile":
                    // Large blast radius, good for early damage
                    score = hasBeenHit ? 5f : 6f;
                    break;
            }

            // Add small random factor so behavior isn't fully deterministic
            score += (float)rng.NextDouble() * 2f;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    // ─────────────────────────────────────────────────
    //  AIM CALCULATION
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Calculates ballistic aim parameters (yaw, pitch, power) from weapon to target,
    /// then applies random variance to simulate imperfect aim.
    /// </summary>
    private static AimUpdatePayload CalculateAimWithVariance(
        WeaponBase weapon, Vector3 targetPos, float variancePercent, Random rng)
    {
        Vector3 delta = targetPos - weapon.GlobalPosition;
        float horizontalDist = new Vector2(delta.X, delta.Z).Length();

        // Yaw: angle in the XZ plane
        // Negate both args: GetDirection() uses Vector3.Forward=(0,0,-1)
        float yaw = Mathf.Atan2(-delta.X, -delta.Z);

        // Pitch: we need a ballistic arc calculation
        // For simplicity, use an approximation that accounts for gravity
        float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        float speed = weapon.ProjectileSpeed;

        // Calculate the pitch angle for a ballistic trajectory
        float pitch = CalculateBallisticPitch(horizontalDist, delta.Y, speed, gravity);

        // Power: based on distance, normalized to 0..1 range
        // For projectile weapons, power scales the launch speed
        float maxRange = speed * speed / gravity; // theoretical max range at 45 degrees
        float power = Mathf.Clamp(horizontalDist / (maxRange * 0.4f), 0.3f, 1f);

        // Special handling for certain weapon types
        if (weapon.WeaponId == "railgun" || weapon.WeaponId == "drill")
        {
            // Direct-fire weapons: aim directly at target
            pitch = Mathf.Atan2(delta.Y, horizontalDist);
            power = Mathf.Clamp(horizontalDist / 30f, 0.5f, 1f);
        }
        else if (weapon.WeaponId == "mortar")
        {
            // Mortar: use higher arc for lobbing over obstacles
            pitch = CalculateHighArcPitch(horizontalDist, delta.Y, speed, gravity);
            power = Mathf.Clamp(horizontalDist / (maxRange * 0.3f), 0.4f, 1f);
        }

        // Apply variance
        float yawVariance = ((float)rng.NextDouble() - 0.5f) * 2f * variancePercent * Mathf.Pi;
        float pitchVariance = ((float)rng.NextDouble() - 0.5f) * 2f * variancePercent;
        float powerVariance = ((float)rng.NextDouble() - 0.5f) * 2f * variancePercent;

        yaw += yawVariance;
        pitch += pitchVariance;
        power = Mathf.Clamp(power + powerVariance, 0.1f, 1f);

        return new AimUpdatePayload(weapon.WeaponId, yaw, pitch, power);
    }

    /// <summary>
    /// Calculates the optimal pitch angle for a ballistic trajectory (low arc).
    /// Uses the standard projectile motion equation.
    /// </summary>
    private static float CalculateBallisticPitch(float horizontalDist, float verticalDist, float speed, float gravity)
    {
        if (horizontalDist < 0.1f)
        {
            return verticalDist > 0 ? Mathf.Pi / 4f : Mathf.Pi / 6f;
        }

        // v^4 - g(g*x^2 + 2*y*v^2) discriminant for projectile angle
        float v2 = speed * speed;
        float v4 = v2 * v2;
        float gx2 = gravity * horizontalDist * horizontalDist;
        float discriminant = v4 - gravity * (gx2 + 2f * verticalDist * v2);

        if (discriminant < 0)
        {
            // Target is out of range, use 45 degree angle for maximum range
            return Mathf.Pi / 4f;
        }

        // Low trajectory solution (positive pitch = upward in AimingSystem)
        float sqrtDisc = Mathf.Sqrt(discriminant);
        float angle = Mathf.Atan2(v2 - sqrtDisc, gravity * horizontalDist);
        return angle;
    }

    /// <summary>
    /// Calculates a high-arc pitch for mortar-style weapons that need to lob
    /// projectiles over walls and obstacles.
    /// </summary>
    private static float CalculateHighArcPitch(float horizontalDist, float verticalDist, float speed, float gravity)
    {
        if (horizontalDist < 0.1f)
        {
            return Mathf.Pi / 3f;
        }

        float v2 = speed * speed;
        float v4 = v2 * v2;
        float gx2 = gravity * horizontalDist * horizontalDist;
        float discriminant = v4 - gravity * (gx2 + 2f * verticalDist * v2);

        if (discriminant < 0)
        {
            return Mathf.Pi / 3f;
        }

        // High trajectory solution (positive pitch = upward)
        float sqrtDisc = Mathf.Sqrt(discriminant);
        float angle = Mathf.Atan2(v2 + sqrtDisc, gravity * horizontalDist);
        return angle;
    }

    /// <summary>
    /// Gets a random point within the build zone, preferring positions near
    /// actual structure. Falls back to zone center if no solid voxels are found.
    /// </summary>
    private static Vector3 GetRandomZonePoint(BuildZone zone, Random rng)
    {
        // Simple fallback: target mid-height center with some randomness
        Vector3I origin = zone.OriginMicrovoxels;
        Vector3I size = zone.SizeMicrovoxels;

        // Target the interior of the zone with bias toward the center and lower half
        float xFrac = 0.2f + (float)rng.NextDouble() * 0.6f;
        float yFrac = 0.05f + (float)rng.NextDouble() * 0.4f; // Lower half where structures are
        float zFrac = 0.2f + (float)rng.NextDouble() * 0.6f;

        Vector3I point = new Vector3I(
            origin.X + (int)(size.X * xFrac),
            origin.Y + (int)(size.Y * yFrac),
            origin.Z + (int)(size.Z * zFrac));

        return MathHelpers.MicrovoxelToWorld(point);
    }

    // ─────────────────────────────────────────────────
    //  STATIC HELPERS (used by GameManager.ExecuteBotTurn)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Selects an enemy target based on bot difficulty. Static version used
    /// directly by GameManager when no persistent BotController is available.
    /// </summary>
    public static (PlayerSlot Slot, PlayerData Data) SelectTargetStatic(
        List<(PlayerSlot Slot, PlayerData Data)> enemies,
        BotDifficulty difficulty,
        Random rng)
    {
        if (enemies.Count == 1)
        {
            return enemies[0];
        }

        switch (difficulty)
        {
            case BotDifficulty.Easy:
                return enemies[rng.Next(enemies.Count)];

            case BotDifficulty.Medium:
                // Target the weakest enemy (lowest commander health)
                return enemies.OrderBy(e => e.Data.CommanderHealth).First();

            case BotDifficulty.Hard:
                // Finish off near-death enemies, otherwise target the strongest
                var nearDeath = enemies.Where(e => e.Data.CommanderHealth <= 30).ToList();
                if (nearDeath.Count > 0)
                {
                    return nearDeath.OrderBy(e => e.Data.CommanderHealth).First();
                }
                return enemies.OrderByDescending(e => e.Data.CommanderHealth).First();

            default:
                return enemies[0];
        }
    }

    /// <summary>
    /// Scans the enemy build zone for actual solid voxels and returns a world-space
    /// target position that is guaranteed to hit structure. Uses a coarse grid scan
    /// (every 2 microvoxels = 1 build unit) to find columns of solid material, then
    /// picks a target point based on difficulty.
    ///
    /// This solves the core problem: bots no longer aim at empty air inside the
    /// bounding box of a build zone.
    /// </summary>
    public static Vector3 FindSolidTargetInZone(
        VoxelWorld world,
        BuildZone zone,
        Vector3 weaponPosition,
        BotDifficulty difficulty,
        Random rng)
    {
        Vector3I origin = zone.OriginMicrovoxels;
        Vector3I size = zone.SizeMicrovoxels;
        Vector3I max = origin + size;

        // Coarse scan: sample every 2 microvoxels (= 1 build unit) across X and Z,
        // scanning the full Y column. Collect solid voxel positions.
        int step = GameConfig.MicrovoxelsPerBuildUnit; // 2
        List<Vector3I> solidPositions = new List<Vector3I>(64);

        for (int z = origin.Z; z < max.Z; z += step)
        {
            for (int x = origin.X; x < max.X; x += step)
            {
                // Scan the Y column from top to bottom, record the highest solid voxel
                for (int y = max.Y - 1; y >= origin.Y; y -= step)
                {
                    Vector3I pos = new Vector3I(x, y, z);
                    Voxel.Voxel voxel = world.GetVoxel(pos);
                    if (voxel.IsSolid)
                    {
                        solidPositions.Add(pos);
                        break; // Only take the top of each column for surface targeting
                    }
                }
            }
        }

        // If no solid voxels found (structure destroyed), fall back to zone center
        if (solidPositions.Count == 0)
        {
            GD.Print("[Bot] No solid voxels found in enemy zone — targeting zone center.");
            Vector3I center = origin + size / 2;
            return MathHelpers.MicrovoxelToWorld(center);
        }

        // Select a target based on difficulty
        Vector3I target;
        switch (difficulty)
        {
            case BotDifficulty.Easy:
                // Random solid position
                target = solidPositions[rng.Next(solidPositions.Count)];
                break;

            case BotDifficulty.Medium:
            {
                // Target the densest area: find the centroid of all solid positions
                // then pick the solid position closest to that centroid
                float sumX = 0, sumY = 0, sumZ = 0;
                foreach (Vector3I p in solidPositions)
                {
                    sumX += p.X;
                    sumY += p.Y;
                    sumZ += p.Z;
                }
                Vector3 centroid = new Vector3(
                    sumX / solidPositions.Count,
                    sumY / solidPositions.Count,
                    sumZ / solidPositions.Count);

                float bestDist = float.MaxValue;
                target = solidPositions[0];
                foreach (Vector3I p in solidPositions)
                {
                    float dist = new Vector3(p.X - centroid.X, p.Y - centroid.Y, p.Z - centroid.Z).LengthSquared();
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        target = p;
                    }
                }
                break;
            }

            case BotDifficulty.Hard:
            {
                // Target structural supports: find the lowest solid voxels
                // on the side facing the weapon, which when destroyed will cause
                // cascading collapse.
                Vector3 zoneCenter = MathHelpers.MicrovoxelToWorld(origin + size / 2);
                Vector3 toWeapon = (weaponPosition - zoneCenter).Normalized();

                // Score each position: prefer low Y (structural support) and
                // positions on the weapon-facing side
                float bestScore = float.MinValue;
                target = solidPositions[0];
                foreach (Vector3I p in solidPositions)
                {
                    Vector3 pWorld = MathHelpers.MicrovoxelToWorld(p);
                    Vector3 fromCenter = (pWorld - zoneCenter).Normalized();

                    // Low Y is better (structural support), weapon-facing is better
                    float heightPenalty = (p.Y - origin.Y) * -1f;
                    float facingBonus = fromCenter.Dot(toWeapon) * 3f;
                    float score = heightPenalty + facingBonus + (float)rng.NextDouble() * 0.5f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        target = p;
                    }
                }
                break;
            }

            default:
                target = solidPositions[rng.Next(solidPositions.Count)];
                break;
        }

        return MathHelpers.MicrovoxelToWorld(target);
    }
}
