using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.Building;

public sealed class SymmetryTool
{
    public BuildSymmetryMode Mode { get; set; }

    public HashSet<Vector3I> Apply(BuildZone zone, IEnumerable<Vector3I> buildUnits)
    {
        HashSet<Vector3I> resolved = new HashSet<Vector3I>();
        foreach (Vector3I buildUnit in buildUnits)
        {
            resolved.Add(buildUnit);
            foreach (Vector3I mirrored in MirrorBuildUnit(zone, buildUnit))
            {
                resolved.Add(mirrored);
            }
        }

        return resolved;
    }

    public IEnumerable<Vector3I> MirrorBuildUnit(BuildZone zone, Vector3I buildUnit)
    {
        Vector3I min = zone.OriginBuildUnits;
        Vector3I max = zone.OriginBuildUnits + zone.SizeBuildUnits - Vector3I.One;
        if (Mode == BuildSymmetryMode.MirrorX || Mode == BuildSymmetryMode.MirrorXZ)
        {
            int mirroredX = max.X - (buildUnit.X - min.X);
            yield return new Vector3I(mirroredX, buildUnit.Y, buildUnit.Z);
        }

        if (Mode == BuildSymmetryMode.MirrorZ || Mode == BuildSymmetryMode.MirrorXZ)
        {
            int mirroredZ = max.Z - (buildUnit.Z - min.Z);
            yield return new Vector3I(buildUnit.X, buildUnit.Y, mirroredZ);
        }

        if (Mode == BuildSymmetryMode.MirrorXZ)
        {
            int mirroredX = max.X - (buildUnit.X - min.X);
            int mirroredZ = max.Z - (buildUnit.Z - min.Z);
            yield return new Vector3I(mirroredX, buildUnit.Y, mirroredZ);
        }
    }
}
