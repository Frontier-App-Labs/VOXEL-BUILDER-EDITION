using Godot;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using Xunit;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Tests;

public sealed class VoxelDataTests
{
    [Fact]
    public void VoxelCreate_PacksMaterialAndFlags()
    {
        VoxelValue voxel = VoxelValue.Create(VoxelMaterialType.Metal, hitPoints: 40, isWeaponMount: true, isCommanderSpawn: true, isDamaged: true);

        Assert.Equal(VoxelMaterialType.Metal, voxel.Material);
        Assert.True(voxel.IsWeaponMount);
        Assert.True(voxel.IsCommanderSpawn);
        Assert.True(voxel.IsDamaged);
        Assert.True(voxel.HitPoints > 0);
    }

    [Fact]
    public void MathHelpers_WorldChunkConversion_HandlesNegativeCoordinates()
    {
        Vector3I position = new Vector3I(-1, 17, -16);
        Vector3I chunk = MathHelpers.WorldToChunk(position);
        Vector3I local = MathHelpers.WorldToLocal(position);

        Assert.Equal(new Vector3I(-1, 1, -1), chunk);
        Assert.Equal(new Vector3I(15, 1, 0), local);
    }

    [Fact]
    public void DamageCalculator_FalloffRespectsDistance()
    {
        int nearDamage = DamageCalculator.CalculateExplosionDamage(100, 6f, 1f, VoxelMaterialType.Stone);
        int farDamage = DamageCalculator.CalculateExplosionDamage(100, 6f, 5f, VoxelMaterialType.Stone);

        Assert.True(nearDamage > farDamage);
        Assert.True(farDamage >= 0);
    }

    [Fact]
    public void BuildZone_ContainsExpectedCells()
    {
        BuildZone zone = new BuildZone(new Vector3I(0, 0, 0), new Vector3I(4, 4, 4));

        Assert.True(zone.ContainsBuildUnit(new Vector3I(0, 0, 0)));
        Assert.True(zone.ContainsBuildUnit(new Vector3I(3, 3, 3)));
        Assert.False(zone.ContainsBuildUnit(new Vector3I(4, 0, 0)));
    }
}
