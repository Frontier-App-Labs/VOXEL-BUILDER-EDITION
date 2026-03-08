using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Building;

public sealed class BlueprintVoxelData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Data { get; set; }
}

public sealed class BlueprintWeaponData
{
    public string WeaponId { get; set; } = string.Empty;
    public Vector3I BuildUnitPosition { get; set; }
}

public sealed class BlueprintData
{
    public string Name { get; set; } = string.Empty;
    public Vector3I DimensionsBuildUnits { get; set; }
    public List<BlueprintVoxelData> Voxels { get; set; } = new List<BlueprintVoxelData>();
    public List<BlueprintWeaponData> Weapons { get; set; } = new List<BlueprintWeaponData>();
    public Vector3I? CommanderBuildUnitPosition { get; set; }
}

public partial class BlueprintSystem : Node
{
    public string MakeBlueprintPath(string name)
    {
        return $"user://blueprints/{name.ToLowerInvariant().Replace(' ', '_')}.json";
    }

    public BlueprintData Capture(VoxelWorld world, BuildZone zone, string name)
    {
        BlueprintData blueprint = new BlueprintData
        {
            Name = name,
            DimensionsBuildUnits = zone.SizeBuildUnits,
        };

        for (int z = zone.OriginMicrovoxels.Z; z <= zone.MaxMicrovoxelsInclusive.Z; z++)
        {
            for (int y = zone.OriginMicrovoxels.Y; y <= zone.MaxMicrovoxelsInclusive.Y; y++)
            {
                for (int x = zone.OriginMicrovoxels.X; x <= zone.MaxMicrovoxelsInclusive.X; x++)
                {
                    VoxelValue voxel = world.GetVoxel(new Vector3I(x, y, z));
                    if (voxel.IsAir)
                    {
                        continue;
                    }

                    blueprint.Voxels.Add(new BlueprintVoxelData
                    {
                        X = x - zone.OriginMicrovoxels.X,
                        Y = y - zone.OriginMicrovoxels.Y,
                        Z = z - zone.OriginMicrovoxels.Z,
                        Data = voxel.Data,
                    });
                }
            }
        }

        return blueprint;
    }

    public void SaveBlueprint(BlueprintData blueprint)
    {
        SaveSystem.SaveJson(MakeBlueprintPath(blueprint.Name), blueprint);
    }

    public BlueprintData? LoadBlueprint(string name)
    {
        return SaveSystem.LoadJson<BlueprintData>(MakeBlueprintPath(name));
    }
}
