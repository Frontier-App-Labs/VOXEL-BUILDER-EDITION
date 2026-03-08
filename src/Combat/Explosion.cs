using Godot;
using System.Collections.Generic;
using VoxelSiege.Commander;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

public partial class Explosion : Node3D
{
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
        HashSet<Vector3I> affectedPositions = new HashSet<Vector3I>();
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
                world.SetVoxel(position, VoxelValue.Air, instigator);
                affectedPositions.Add(position);
            }
            else
            {
                world.SetVoxel(position, voxel.WithHitPoints(nextHitPoints).WithDamaged(true), instigator);
                affectedPositions.Add(position);
            }
        }

        Aabb searchBounds = new Aabb(worldPosition - Vector3.One * radiusMicrovoxels * GameConfig.MicrovoxelMeters, Vector3.One * radiusMicrovoxels * GameConfig.MicrovoxelMeters * 2f);
        foreach (Vector3I disconnected in world.FindDisconnectedVoxels(searchBounds))
        {
            world.SetVoxel(disconnected, VoxelValue.Air, instigator);
        }

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

        QueueFree();
    }
}
