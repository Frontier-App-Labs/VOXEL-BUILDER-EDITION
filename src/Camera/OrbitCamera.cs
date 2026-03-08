using Godot;

namespace VoxelSiege.Camera;

public partial class OrbitCamera : Camera3D
{
    [Export]
    public NodePath? TargetPath { get; set; }

    [Export]
    public float Distance { get; set; } = 18f;

    [Export]
    public float Height { get; set; } = 8f;

    private float _yaw;

    public override void _Process(double delta)
    {
        Node3D? target = TargetPath is null ? null : GetNodeOrNull<Node3D>(TargetPath);
        if (target == null)
        {
            return;
        }

        _yaw += (float)delta * 0.25f;
        Vector3 offset = new Vector3(Mathf.Sin(_yaw), 0.4f, Mathf.Cos(_yaw)).Normalized() * Distance;
        offset.Y = Height;
        GlobalPosition = target.GlobalPosition + offset;
        LookAt(target.GlobalPosition, Vector3.Up);
    }
}
