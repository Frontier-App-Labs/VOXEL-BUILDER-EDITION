using Godot;
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
        Vector3I center = MathHelpers.WorldToMicrovoxel(worldPosition);
        int radius = Mathf.CeilToInt(radiusMicrovoxels);
        float radiusMeters = radiusMicrovoxels * GameConfig.MicrovoxelMeters;

        // Use pooled collections to reduce GC pressure
        List<Vector3I> destroyedPositions = world.AcquireList();
        List<DestroyedVoxelInfo> destroyedVoxelInfos = new List<DestroyedVoxelInfo>();
        HashSet<Vector3I> affectedPositions = world.AcquireHashSet();

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
                world.SetVoxel(position, VoxelValue.Air, instigator);
                affectedPositions.Add(position);
            }
            else
            {
                world.SetVoxel(position, voxel.WithHitPoints(nextHitPoints).WithDamaged(true), instigator);
                affectedPositions.Add(position);
            }
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
        // Horizontally: 3x blast radius so the BFS can find connection paths around the crater.
        // Vertically: from ground level to max build height so upper portions of structures
        // that lost support are detected even when the explosion hit low on a tall structure.
        float horizontalRadius = radiusMicrovoxels * GameConfig.MicrovoxelMeters * 3f;
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
            // Group into connected islands and spawn falling chunks instead of deleting
            List<List<Vector3I>> components = FallingChunk.GroupConnectedComponents(
                new HashSet<Vector3I>(disconnected));
            foreach (List<Vector3I> component in components)
            {
                FallingChunk.Create(component, world, worldPosition);
            }
        }

        world.ReturnList(disconnected);

        foreach (Node node in GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is not Commander.Commander commander)
            {
                continue;
            }

            float commanderDistance = commander.GlobalPosition.DistanceTo(worldPosition) / GameConfig.MicrovoxelMeters;
            int commanderDamage = DamageCalculator.CalculateCommanderDamage(baseDamage, radiusMicrovoxels, commanderDistance);
            if (commanderDamage > 0)
            {
                commander.ApplyDamage(commanderDamage, instigator, worldPosition);
            }

            commander.EvaluateExposure(world);
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
                    troop.ApplyDamage(troopDamage, instigator);
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
            // at the base of its build unit (weapons are centered on a build unit,
            // so subtract half a build unit to get the corner).
            Vector3 weaponPos = weapon.GlobalPosition;
            Vector3 cornerPos = weaponPos - new Vector3(
                GameConfig.BuildUnitMeters * 0.5f,
                GameConfig.BuildUnitMeters * 0.5f,
                GameConfig.BuildUnitMeters * 0.5f);
            Vector3I microBase = MathHelpers.WorldToMicrovoxel(cornerPos);

            // Check if at least one voxel directly below the build unit footprint is solid
            bool hasSupport = false;
            for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; z++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; x++)
                {
                    Vector3I below = microBase + new Vector3I(x, -1, z);
                    if (world.GetVoxel(below).IsSolid)
                    {
                        hasSupport = true;
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
        // Spawn debris for every 3rd destroyed voxel, capped at 30 total to avoid lag spikes
        int debrisSpawned = 0;
        const int maxDebrisPerExplosion = 30;

        for (int i = 0; i < destroyedVoxels.Count && debrisSpawned < maxDebrisPerExplosion; i += 3)
        {
            DestroyedVoxelInfo info = destroyedVoxels[i];
            Vector3 voxelWorldPos = MathHelpers.MicrovoxelToWorld(info.Position);
            Color debrisColor = VoxelMaterials.GetPreviewColor(info.Material);

            DebrisFX.SpawnDebris(fxParent, voxelWorldPos, debrisColor, worldPosition, 1, info.Material);
            debrisSpawned++;
        }

        // 5. Impact decals on remaining surfaces near the explosion
        SpawnImpactDecals(fxParent, worldPosition, radiusMeters);
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

    private static void SpawnImpactDecals(Node parent, Vector3 worldPosition, float radiusMeters)
    {
        // Place scorch marks in cardinal directions around the blast center
        foreach (Vector3 dir in DecalDirections)
        {
            Vector3 decalPos = worldPosition + dir * radiusMeters * 0.8f;
            Vector3 normal = -dir; // Decal faces inward toward blast center
            ImpactDecals.Spawn(parent, decalPos, normal, radiusMeters * 0.6f);
        }
    }
}
