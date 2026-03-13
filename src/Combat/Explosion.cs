using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Commander;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Army;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

public partial class Explosion : Node3D
{
    /// <summary>
    /// Tracks a destroyed voxel's position and original material type for FX purposes.
    /// </summary>
    public struct DestroyedVoxelInfo
    {
        public Vector3I Position;
        public VoxelMaterialType Material;
    }

    private static readonly Vector3I[] NeighborOffsets =
    {
        Vector3I.Up,
        Vector3I.Down,
        Vector3I.Left,
        Vector3I.Right,
        new Vector3I(0, 0, 1),
        new Vector3I(0, 0, -1)
    };

    /// <summary>
    /// Optional callback set by GameManager to apply shield damage multipliers.
    /// Given a microvoxel position, returns a multiplier (0.5 if shielded, 1.0 otherwise).
    /// </summary>
    public static Func<Vector3I, float>? ShieldMultiplierCallback;

    /// <summary>
    /// Fired after all voxel modifications from an explosion (including structural collapse)
    /// have been applied. The host uses this to broadcast authoritative voxel changes to clients.
    /// </summary>
    public static event Action<List<(Vector3I Position, VoxelValue NewVoxel)>>? VoxelDamageApplied;

    /// <summary>
    /// Fires VoxelDamageApplied from external code (e.g. drill bore changes in ProjectileBase).
    /// </summary>
    public static void NotifyVoxelDamage(List<(Vector3I Position, VoxelValue NewVoxel)> changes)
    {
        if (changes.Count > 0)
            VoxelDamageApplied?.Invoke(changes);
    }

    private static readonly Vector3[] DecalDirections =
    {
        Vector3.Up, Vector3.Down,
        Vector3.Left, Vector3.Right,
        Vector3.Forward, Vector3.Back
    };

    public static void Trigger(Node parent, VoxelWorld world, Vector3 worldPosition, int baseDamage, float radiusMicrovoxels, PlayerSlot? instigator)
    {
        Explosion explosion = new Explosion();
        parent.AddChild(explosion);
        explosion.GlobalPosition = worldPosition;
        explosion.Detonate(world, worldPosition, baseDamage, radiusMicrovoxels, instigator);
    }

    public void Detonate(VoxelWorld world, Vector3 worldPosition, int baseDamage, float radiusMicrovoxels, PlayerSlot? instigator)
    {
        // Play explosion impact sound at the detonation point
        AudioDirector.Instance?.PlaySFX("explosion_impact", worldPosition);

        Vector3I center = MathHelpers.WorldToMicrovoxel(worldPosition);
        int radius = Mathf.CeilToInt(radiusMicrovoxels);
        float radiusMeters = radiusMicrovoxels * GameConfig.MicrovoxelMeters;

        // Use pooled collections to reduce GC pressure
        List<Vector3I> destroyedPositions = world.AcquireList();
        List<DestroyedVoxelInfo> destroyedVoxelInfos = new List<DestroyedVoxelInfo>();
        HashSet<Vector3I> affectedPositions = world.AcquireHashSet();

        // Collect all voxel changes first, then apply in bulk for efficiency.
        // This avoids per-voxel chunk lookups and queues one remesh per chunk
        // instead of one per voxel.
        var bulkChanges = new List<(Vector3I Position, VoxelValue NewVoxel)>();

        foreach (Vector3I position in MathHelpers.EnumerateSphere(center, radius))
        {
            VoxelValue voxel = world.GetVoxel(position);
            if (voxel.IsAir || voxel.Material == VoxelMaterialType.Foundation)
            {
                continue;
            }

            float distance = position.DistanceTo(center);
            int damage = DamageCalculator.CalculateExplosionDamage(baseDamage, radiusMicrovoxels, distance, voxel.Material);
            if (damage <= 0)
            {
                continue;
            }

            // Apply shield damage reduction if the target position is shielded
            float shieldMult = ShieldMultiplierCallback?.Invoke(position) ?? 1.0f;
            if (shieldMult < 1.0f)
            {
                damage = Mathf.Max(1, (int)(damage * shieldMult));
            }

            int nextHitPoints = voxel.HitPoints - damage;
            if (nextHitPoints <= 0)
            {
                // Track destroyed voxels with their material type for debris FX
                destroyedPositions.Add(position);
                destroyedVoxelInfos.Add(new DestroyedVoxelInfo
                {
                    Position = position,
                    Material = voxel.Material
                });
                bulkChanges.Add((position, VoxelValue.Air));
                affectedPositions.Add(position);
            }
            else
            {
                bulkChanges.Add((position, voxel.WithHitPoints(nextHitPoints).WithDamaged(true)));
                affectedPositions.Add(position);
            }
        }

        // Apply all voxel changes in a single bulk pass (one remesh per affected chunk)
        world.ApplyBulkChanges(bulkChanges, instigator);

        // Remove grass blades above destroyed terrain so they don't float in the air
        if (destroyedPositions.Count > 0)
        {
            TerrainDecorator.RemoveGrassInRadius(worldPosition, radiusMeters + 0.5f);
        }

        // Ignite surviving flammable voxels near the blast
        if (FireSystem.Instance != null)
        {
            foreach (Vector3I position in affectedPositions)
            {
                VoxelValue remaining = world.GetVoxel(position);
                if (!remaining.IsAir && VoxelMaterials.GetDefinition(remaining.Material).IsFlammable)
                {
                    FireSystem.Instance.IgniteAt(position);
                }
            }
        }

        // Extend search bounds well beyond blast radius to detect structural disconnection.
        // Horizontally: 5x blast radius so the BFS can find connection paths around the crater
        // and detect disconnected portions of wide structures. Minimum 10m to catch small blasts.
        // Vertically: from ground level to max build height so upper portions of structures
        // that lost support are detected even when the explosion hit low on a tall structure.
        float horizontalRadius = Mathf.Max(radiusMicrovoxels * GameConfig.MicrovoxelMeters * 5f, 10f);
        float maxBuildHeight = (GameConfig.PrototypeGroundThickness +
            GameConfig.FourPlayerBuildZoneHeight * GameConfig.MicrovoxelsPerBuildUnit) *
            GameConfig.MicrovoxelMeters;
        Vector3 boundsMin = new Vector3(
            worldPosition.X - horizontalRadius,
            0f,
            worldPosition.Z - horizontalRadius);
        Vector3 boundsMax = new Vector3(
            worldPosition.X + horizontalRadius,
            maxBuildHeight,
            worldPosition.Z + horizontalRadius);
        Aabb searchBounds = new Aabb(boundsMin, boundsMax - boundsMin);
        List<Vector3I> disconnected = world.FindDisconnectedVoxels(searchBounds);

        if (disconnected.Count > 0)
        {
            // Group into connected islands and spawn falling chunks instead of deleting.
            // Pass world for material-aware splitting: chunks break along material
            // boundaries (e.g. wood floor separates from brick wall above it).
            List<List<Vector3I>> components = FallingChunk.GroupConnectedComponents(
                new HashSet<Vector3I>(disconnected), world);
            foreach (List<Vector3I> component in components)
            {
                FallingChunk.Create(component, world, worldPosition);
            }
        }

        // Broadcast all voxel changes (explosion damage + structural collapse) so the
        // host can send authoritative state to clients, preventing voxel world drift.
        if (VoxelDamageApplied != null && (bulkChanges.Count > 0 || disconnected.Count > 0))
        {
            var allChanges = new List<(Vector3I Position, VoxelValue NewVoxel)>(bulkChanges.Count + disconnected.Count);
            allChanges.AddRange(bulkChanges);
            // Structural collapse: disconnected voxels were set to Air by FallingChunk.Create
            foreach (Vector3I pos in disconnected)
                allChanges.Add((pos, VoxelValue.Air));
            VoxelDamageApplied.Invoke(allChanges);
        }

        world.ReturnList(disconnected);

        // Unfreeze any frozen ruin chunks that lost terrain support due to the explosion.
        // This prevents settled debris from floating in the air after the ground beneath
        // them is destroyed. Must run after voxel destruction AND FallingChunk.Create
        // (which also removes voxels from the world).
        FallingChunk.UnfreezeUnsupportedRuins(world);
        FallingChunk.UnfreezeRuinsInRadius(worldPosition, radiusMeters + 1f);
        DebrisFX.BlastRuinsInRadius(worldPosition, radiusMeters + 1f);

        foreach (Node node in GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is not Commander.Commander commander)
            {
                continue;
            }

            float commanderDistance = commander.GlobalPosition.DistanceTo(worldPosition) / GameConfig.MicrovoxelMeters;
            int commanderDamage = DamageCalculator.CalculateCommanderDamage(baseDamage, radiusMicrovoxels, commanderDistance);

            // Cap commander damage so high-damage weapons (drills, etc.) don't
            // one-shot commanders. Max 8 per explosion = 2-shot kill (HP=15).
            commanderDamage = Mathf.Min(commanderDamage, GameConfig.MaxExplosionCommanderDamage);

            if (commanderDamage > 0)
            {
                // Exposed commanders take extra damage — they have no cover
                if (commander.IsExposed)
                {
                    commanderDamage = Mathf.RoundToInt(commanderDamage * GameConfig.CommanderExposedMultiplier);
                }

                // Apply shield damage reduction if the commander's zone is shielded
                Vector3I cmdMicro = MathHelpers.WorldToMicrovoxel(commander.GlobalPosition);
                float cmdShieldMult = ShieldMultiplierCallback?.Invoke(cmdMicro) ?? 1.0f;
                if (cmdShieldMult < 1.0f)
                {
                    commanderDamage = Mathf.Max(1, (int)(commanderDamage * cmdShieldMult));
                }

                commander.ApplyDamage(commanderDamage, instigator, worldPosition);
            }

            commander.EvaluateExposure(world);

            // Trigger panic when an explosion lands within 1.5x the blast radius
            float panicRadius = radiusMicrovoxels * 1.5f;
            if (commanderDistance <= panicRadius)
            {
                commander.TriggerPanic(5f);
            }
        }

        // Damage weapons caught in the blast radius
        foreach (Node node in GetTree().GetNodesInGroup("Weapons"))
        {
            if (node is not WeaponBase weapon || weapon.IsDestroyed)
            {
                continue;
            }

            float weaponDistance = weapon.GlobalPosition.DistanceTo(worldPosition) / GameConfig.MicrovoxelMeters;
            if (weaponDistance <= radiusMicrovoxels)
            {
                // Same falloff as commander damage: full at center, linear to zero at edge
                int weaponDamage = DamageCalculator.CalculateCommanderDamage(baseDamage, radiusMicrovoxels, weaponDistance);
                if (weaponDamage > 0)
                {
                    weapon.ApplyDamage(weaponDamage);
                }
            }
        }

        // Damage doors caught in the blast radius
        ArmyManager? armyManager = GetTree().Root.GetNodeOrNull<ArmyManager>("Main/ArmyManager");
        if (armyManager != null)
        {
            armyManager.Doors.DamageDoorsInRadius(worldPosition, radiusMicrovoxels, baseDamage, GetTree().Root);
        }

        // Damage troops caught in the blast radius
        foreach (Node node in GetTree().GetNodesInGroup("Troops"))
        {
            if (node is not TroopEntity troop || troop.CurrentHP <= 0)
                continue;

            float troopDistance = troop.GlobalPosition.DistanceTo(worldPosition) / GameConfig.MicrovoxelMeters;
            if (troopDistance <= radiusMicrovoxels)
            {
                int troopDamage = DamageCalculator.CalculateCommanderDamage(baseDamage, radiusMicrovoxels, troopDistance);
                if (troopDamage > 0)
                    troop.ApplyDamage(troopDamage, instigator, worldPosition);
            }
        }

        // --- Structural support check for weapons ---
        // After voxels have been destroyed (including disconnected chunks removed),
        // verify that each weapon still has at least one solid voxel underneath it.
        // Weapons that have lost support are destroyed (they fall and break).
        CheckWeaponSupport(world);

        // --- Visual Effects ---
        SpawnVisualEffects(world, worldPosition, radiusMeters, destroyedVoxelInfos);

        // Return pooled collections
        world.ReturnList(destroyedPositions);
        world.ReturnHashSet(affectedPositions);

        QueueFree();
    }

    /// <summary>
    /// Checks all placed weapons for structural support. A weapon requires at
    /// least one solid voxel directly below its build-unit footprint. If no
    /// support is found the weapon is destroyed (simulating it falling and
    /// breaking when the structure underneath is blown away).
    /// </summary>
    private void CheckWeaponSupport(VoxelWorld world)
    {
        foreach (Node node in GetTree().GetNodesInGroup("Weapons"))
        {
            if (node is not WeaponBase weapon || weapon.IsDestroyed)
            {
                continue;
            }

            // Convert the weapon's world position to the microvoxel coordinate
            // at the base of its build unit. Weapons sit at the bottom-center of
            // the build unit, so subtract half horizontally to get the corner.
            Vector3 weaponPos = weapon.GlobalPosition;
            Vector3 cornerPos = weaponPos - new Vector3(
                GameConfig.BuildUnitMeters * 0.5f,
                0f,
                GameConfig.BuildUnitMeters * 0.5f);
            Vector3I microBase = MathHelpers.WorldToMicrovoxel(cornerPos);

            // Check if at least one solid voxel exists below or within the
            // build unit footprint (matching ValidatePlacement logic so
            // weapons placed on thin floors aren't incorrectly destroyed).
            bool hasSupport = false;
            for (int y = -1; y < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; y++)
            {
                for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; z++)
                {
                    for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                    {
                        Vector3I check = microBase + new Vector3I(x, y, z);
                        if (world.GetVoxel(check).IsSolid)
                        {
                            hasSupport = true;
                            break;
                        }
                    }
                }
            }

            if (!hasSupport)
            {
                weapon.DestroyFromLostSupport();
            }
        }
    }

    private void SpawnVisualEffects(VoxelWorld world, Vector3 worldPosition, float radiusMeters, List<DestroyedVoxelInfo> destroyedVoxels)
    {
        Node fxParent = GetTree().Root;

        // 1. Main explosion effect (fireball, smoke, sparks, flash, camera shake)
        ExplosionFX.Spawn(fxParent, worldPosition, radiusMeters);

        // 2. Determine dominant material for dust tinting
        VoxelMaterialType dominantMaterial = GetDominantMaterial(destroyedVoxels);

        // 3. Material-tinted dust cloud around the explosion
        DustFX.Spawn(fxParent, worldPosition, radiusMeters, dominantMaterial);

        // 4. Debris for destroyed voxels (batched, respecting cap)
        // Spawn debris for destroyed voxels so structures visibly break apart.
        // Sample rate adapts to explosion size: small blasts get per-voxel debris,
        // large blasts sample every Nth voxel to stay within budget.
        int debrisSpawned = 0;
        const int maxDebrisPerExplosion = 120;

        // Adaptive step: spawn every voxel for small blasts, skip more for large ones
        int step = Mathf.Max(1, destroyedVoxels.Count / maxDebrisPerExplosion);

        for (int i = 0; i < destroyedVoxels.Count && debrisSpawned < maxDebrisPerExplosion; i += step)
        {
            DestroyedVoxelInfo info = destroyedVoxels[i];
            Vector3 voxelWorldPos = MathHelpers.MicrovoxelToWorld(info.Position);
            Color debrisColor = VoxelMaterials.GetPreviewColor(info.Material);

            // Spawn 2 debris pieces per sampled voxel for a more dramatic effect
            DebrisFX.SpawnDebris(fxParent, voxelWorldPos, debrisColor, worldPosition, 2, info.Material);
            debrisSpawned++;
        }

        // 5. Impact decals on remaining surfaces near the explosion
        SpawnImpactDecals(fxParent, world, worldPosition, radiusMeters);
    }

    /// <summary>
    /// Finds the most common material type among the destroyed voxels.
    /// Used for tinting dust clouds to match the dominant material.
    /// </summary>
    private static VoxelMaterialType GetDominantMaterial(List<DestroyedVoxelInfo> destroyed)
    {
        if (destroyed.Count == 0)
        {
            return VoxelMaterialType.Stone;
        }

        // Count material occurrences (lightweight: just track max inline)
        VoxelMaterialType best = destroyed[0].Material;
        int bestCount = 0;

        // Use the first element as initial guess, scan for more common ones
        Dictionary<VoxelMaterialType, int>? counts = null;
        if (destroyed.Count > 3)
        {
            counts = new Dictionary<VoxelMaterialType, int>();
            for (int i = 0; i < destroyed.Count; i++)
            {
                VoxelMaterialType mat = destroyed[i].Material;
                counts.TryGetValue(mat, out int c);
                counts[mat] = c + 1;
                if (c + 1 > bestCount)
                {
                    bestCount = c + 1;
                    best = mat;
                }
            }
        }

        return best;
    }

    private static void SpawnImpactDecals(Node parent, VoxelWorld world, Vector3 worldPosition, float radiusMeters)
    {
        // Place scorch marks only on actual solid surfaces near the blast.
        // Previously, decals were placed blindly in all 6 cardinal directions,
        // which left floating grey transparent quads in empty air where no
        // surface existed (the destroyed voxels were already removed).
        float stepSize = GameConfig.MicrovoxelMeters;
        float searchDist = radiusMeters * 1.5f;
        int maxSteps = Mathf.CeilToInt(searchDist / stepSize);

        foreach (Vector3 dir in DecalDirections)
        {
            // Walk outward from the blast center along this direction,
            // looking for the first solid voxel surface to place a scorch mark on.
            bool foundSurface = false;
            Vector3 decalPos = worldPosition;

            for (int step = 1; step <= maxSteps; step++)
            {
                Vector3 samplePos = worldPosition + dir * (step * stepSize);
                Vector3I micro = MathHelpers.WorldToMicrovoxel(samplePos);

                if (world.GetVoxel(micro).IsSolid)
                {
                    // Found a solid surface — place the decal just in front of it
                    decalPos = samplePos - dir * (stepSize * 0.5f);
                    foundSurface = true;
                    break;
                }
            }

            if (!foundSurface)
            {
                continue; // No surface in this direction — skip this decal
            }

            Vector3 normal = -dir; // Decal faces inward toward blast center
            ImpactDecals.Spawn(parent, decalPos, normal, radiusMeters * 0.6f);
        }
    }
}
