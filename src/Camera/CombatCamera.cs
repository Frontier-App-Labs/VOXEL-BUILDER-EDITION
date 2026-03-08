using Godot;

namespace VoxelSiege.Camera;

public partial class CombatCamera : Camera3D
{
    private Node3D? _followTarget;
    private Vector3 _offset = new Vector3(0f, 2f, -6f);

    public void Follow(Node3D target, Vector3? offset = null)
    {
        _followTarget = target;
        if (offset.HasValue)
        {
            _offset = offset.Value;
        }
    }

    public override void _Process(double delta)
    {
        if (_followTarget == null)
        {
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_followTarget.GlobalPosition + _offset, 0.12f);
        LookAt(_followTarget.GlobalPosition, Vector3.Up);
    }
}
