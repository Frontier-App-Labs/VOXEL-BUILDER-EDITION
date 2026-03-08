using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Building;

public partial class GhostPreview : Node3D
{
    private MultiMeshInstance3D? _multiMeshInstance;

    public override void _Ready()
    {
        _multiMeshInstance = GetNodeOrNull<MultiMeshInstance3D>("Preview");
        if (_multiMeshInstance == null)
        {
            _multiMeshInstance = new MultiMeshInstance3D();
            _multiMeshInstance.Name = "Preview";
            AddChild(_multiMeshInstance);
        }

        MultiMesh multiMesh = new MultiMesh();
        multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        multiMesh.Mesh = new BoxMesh { Size = Vector3.One * GameConfig.MicrovoxelMeters };
        _multiMeshInstance.Multimesh = multiMesh;
    }

    public void SetPreview(IEnumerable<Vector3I> microvoxelPositions, bool isValid)
    {
        if (_multiMeshInstance?.Multimesh == null)
        {
            return;
        }

        List<Vector3I> positions = new List<Vector3I>(microvoxelPositions);
        _multiMeshInstance.Multimesh.InstanceCount = positions.Count;
        for (int index = 0; index < positions.Count; index++)
        {
            Transform3D transform = Transform3D.Identity.Translated(MathHelpers.MicrovoxelToWorld(positions[index]));
            _multiMeshInstance.Multimesh.SetInstanceTransform(index, transform);
        }

        _multiMeshInstance.Visible = positions.Count > 0;
    }
}
