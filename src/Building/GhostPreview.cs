using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Building;

public partial class GhostPreview : Node3D
{
    private MultiMeshInstance3D? _multiMeshInstance;
    private StandardMaterial3D? _validMaterial;
    private StandardMaterial3D? _invalidMaterial;
    private bool _lastIsValid;

    // Model mesh preview (for weapons/commanders)
    private MeshInstance3D? _modelPreview;
    private bool _modelMode;

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

        // Green semi-transparent for valid placement
        _validMaterial = new StandardMaterial3D();
        _validMaterial.AlbedoColor = new Color(0.2f, 0.9f, 0.2f, 0.45f);
        _validMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _validMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _validMaterial.NoDepthTest = true;

        // Red semi-transparent for invalid placement
        _invalidMaterial = new StandardMaterial3D();
        _invalidMaterial.AlbedoColor = new Color(0.9f, 0.2f, 0.2f, 0.45f);
        _invalidMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _invalidMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _invalidMaterial.NoDepthTest = true;

        _multiMeshInstance.MaterialOverride = _validMaterial;

        // Model mesh preview node (reused for weapon/commander previews)
        _modelPreview = new MeshInstance3D();
        _modelPreview.Name = "ModelPreview";
        _modelPreview.Visible = false;
        AddChild(_modelPreview);
    }

    /// <summary>
    /// Updates the ghost preview to show blocks at the given microvoxel positions.
    /// Green if valid, red if invalid.
    /// </summary>
    public void SetPreview(IEnumerable<Vector3I> microvoxelPositions, bool isValid)
    {
        if (_multiMeshInstance?.Multimesh == null)
        {
            return;
        }

        // Hide model preview if switching back to block mode
        if (_modelMode)
        {
            _modelMode = false;
            if (_modelPreview != null) _modelPreview.Visible = false;
        }

        List<Vector3I> positions = new List<Vector3I>(microvoxelPositions);
        _multiMeshInstance.Multimesh.InstanceCount = positions.Count;
        for (int index = 0; index < positions.Count; index++)
        {
            Transform3D transform = Transform3D.Identity.Translated(MathHelpers.MicrovoxelToWorld(positions[index])
                + (Vector3.One * GameConfig.MicrovoxelMeters * 0.5f));
            _multiMeshInstance.Multimesh.SetInstanceTransform(index, transform);
        }

        _multiMeshInstance.Visible = positions.Count > 0;

        if (isValid != _lastIsValid)
        {
            _multiMeshInstance.MaterialOverride = isValid ? _validMaterial : _invalidMaterial;
            _lastIsValid = isValid;
        }
    }

    /// <summary>
    /// Shows a model mesh preview (weapon or commander) at the given world position.
    /// The mesh is displayed with a green/red semi-transparent overlay.
    /// </summary>
    public void SetModelPreview(ArrayMesh mesh, Vector3 worldPosition, float yawRadians, bool isValid)
    {
        if (_modelPreview == null) return;

        _modelMode = true;

        // Hide the multi-mesh block preview
        if (_multiMeshInstance != null) _multiMeshInstance.Visible = false;

        _modelPreview.Mesh = mesh;
        _modelPreview.MaterialOverride = isValid ? _validMaterial : _invalidMaterial;
        _modelPreview.GlobalPosition = worldPosition;
        _modelPreview.Rotation = new Vector3(0f, yawRadians, 0f);
        _modelPreview.Visible = true;

        _lastIsValid = isValid;
    }

    /// <summary>
    /// Shows a single-block preview at the given build unit position.
    /// Uses ExpandBuildUnit to produce the correct shape for the current tool mode
    /// (e.g. thin wall, thin floor) instead of always showing a full 2x2x2 cube.
    /// </summary>
    public void ShowSingleBlock(Vector3I buildUnitPosition, bool isValid, BuildToolMode toolMode = BuildToolMode.Single)
    {
        List<Vector3I> micros = new List<Vector3I>();
        foreach (Vector3I micro in BuildSystem.ExpandBuildUnit(buildUnitPosition, toolMode, buildUnitPosition, buildUnitPosition))
        {
            micros.Add(micro);
        }

        SetPreview(micros, isValid);
    }

    /// <summary>
    /// Hides the ghost preview.
    /// </summary>
    public new void Hide()
    {
        if (_multiMeshInstance != null)
        {
            _multiMeshInstance.Visible = false;
        }
        if (_modelPreview != null)
        {
            _modelPreview.Visible = false;
        }
        _modelMode = false;
    }
}
