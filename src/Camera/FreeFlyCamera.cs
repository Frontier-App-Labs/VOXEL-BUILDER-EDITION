using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.Camera;

public partial class FreeFlyCamera : Camera3D
{
    [Export]
    public float MoveSpeed { get; set; } = 16f;

    [Export]
    public float LookSensitivity { get; set; } = 0.0025f;

    private float _pitch;
    private float _yaw;

    public override void _Ready()
    {
        Current = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * LookSensitivity), -1.45f, 1.45f);
            Rotation = new Vector3(_pitch, _yaw, 0f);
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Right)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _Process(double delta)
    {
        Vector3 input = Vector3.Zero;
        if (Input.IsActionPressed("move_forward"))
        {
            input -= Transform.Basis.Z;
        }

        if (Input.IsActionPressed("move_back"))
        {
            input += Transform.Basis.Z;
        }

        if (Input.IsActionPressed("move_left"))
        {
            input -= Transform.Basis.X;
        }

        if (Input.IsActionPressed("move_right"))
        {
            input += Transform.Basis.X;
        }

        if (Input.IsActionPressed("move_up"))
        {
            input += Transform.Basis.Y;
        }

        if (Input.IsActionPressed("move_down"))
        {
            input -= Transform.Basis.Y;
        }

        if (input != Vector3.Zero)
        {
            float speed = MoveSpeed * (Input.IsKeyPressed(Key.Shift) ? 2f : 1f);
            GlobalPosition += input.Normalized() * speed * (float)delta;
        }
    }
}
