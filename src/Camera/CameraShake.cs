using Godot;

namespace VoxelSiege.Camera;

public partial class CameraShake : Node
{
    [Export]
    public Camera3D? TargetCamera { get; set; }

    private float _intensity;

    public void Trigger(float intensity)
    {
        _intensity = Mathf.Max(_intensity, intensity);
    }

    public override void _Process(double delta)
    {
        if (TargetCamera == null || _intensity <= 0f)
        {
            return;
        }

        Vector3 jitter = new Vector3(
            (float)GD.RandRange(-_intensity, _intensity),
            (float)GD.RandRange(-_intensity, _intensity),
            (float)GD.RandRange(-_intensity, _intensity)) * 0.02f;
        TargetCamera.HOffset = jitter.X;
        TargetCamera.VOffset = jitter.Y;
        _intensity = Mathf.Max(0f, _intensity - (float)delta * 2f);
    }
}
