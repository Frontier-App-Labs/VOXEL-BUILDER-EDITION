using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public partial class WeaponPlacer : Node
{
    public T PlaceWeapon<T>(Node parent, VoxelWorld world, Vector3I buildUnitPosition, PlayerSlot owner)
        where T : WeaponBase, new()
    {
        T weapon = new T();
        weapon.AssignOwner(owner);
        weapon.Position = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(buildUnitPosition)) + new Vector3(GameConfig.BuildUnitMeters * 0.5f, GameConfig.BuildUnitMeters * 0.5f, GameConfig.BuildUnitMeters * 0.5f);
        parent.AddChild(weapon);
        foreach (Vector3I mountVoxel in EnumerateMountVoxels(buildUnitPosition))
        {
            Voxel.Voxel current = world.GetVoxel(mountVoxel);
            if (!current.IsAir)
            {
                world.SetVoxel(mountVoxel, current.WithWeaponMount(true), owner);
            }
        }

        return weapon;
    }

    private static System.Collections.Generic.IEnumerable<Vector3I> EnumerateMountVoxels(Vector3I buildUnitPosition)
    {
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
            {
                yield return microBase + new Vector3I(x, 0, z);
            }
        }
    }
}
