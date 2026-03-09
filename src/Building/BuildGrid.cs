using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Building;

public partial class BuildGrid : Node3D
{
    [Export]
    public Vector3I ZoneOriginBuildUnits { get; set; } = Vector3I.Zero;

    [Export]
    public Vector3I ZoneSizeBuildUnits { get; set; } = new Vector3I(GameConfig.PrototypeBuildZoneWidth, GameConfig.PrototypeBuildZoneHeight, GameConfig.PrototypeBuildZoneDepth);

    public BuildZone Zone => new BuildZone(ZoneOriginBuildUnits, ZoneSizeBuildUnits);

    public Vector3I SnapWorldToBuildUnit(Vector3 worldPosition)
    {
        Vector3I micro = MathHelpers.WorldToMicrovoxel(worldPosition);
        return new Vector3I(
            Mathf.FloorToInt(micro.X / (float)GameConfig.MicrovoxelsPerBuildUnit),
            Mathf.FloorToInt(micro.Y / (float)GameConfig.MicrovoxelsPerBuildUnit),
            Mathf.FloorToInt(micro.Z / (float)GameConfig.MicrovoxelsPerBuildUnit));
    }

    public Vector3 BuildUnitToWorld(Vector3I buildUnit)
    {
        return MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(buildUnit));
    }
}
