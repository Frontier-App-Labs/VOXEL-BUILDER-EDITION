using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Camera;

/// <summary>
/// Perlin noise-based camera shake. Produces smooth, organic-feeling screen shake
/// rather than random jitter. Multiple shakes stack and blend together.
/// Accessed via <c>CameraShake.Instance?.Shake(intensity, duration)</c> or found
/// via the "CameraShake" group for auto-discovery by FX systems.
/// </summary>
public partial class CameraShake : Node
{
    public static CameraShake? Instance { get; private set; }

    /// <summary>When false, all shakes are suppressed (e.g. during Menu phase).</summary>
    public bool Enabled { get; set; } = true;

    [Export]
    public Camera3D? TargetCamera { get; set; }

    private readonly List<ShakeInstance> _activeShakes = new();
    private FastNoiseLite _noise = null!;
    private float _noiseTime;

    private struct ShakeInstance
    {
        public float Intensity;
        public float Duration;
        public float Elapsed;
    }

    public override void _EnterTree()
    {
        Instance = this;
        AddToGroup("CameraShake");
    }

    public override void _Ready()
    {
        _noise = new FastNoiseLite();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _noise.Frequency = 1.5f;
        _noise.Seed = (int)(GD.Randi() % 10000);
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        ResetOffset();
    }

    /// <summary>
    /// Triggers a camera shake with the given intensity and duration.
    /// Multiple calls stack and blend together for compound effects.
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        _activeShakes.Add(new ShakeInstance
        {
            Intensity = intensity,
            Duration = duration,
            Elapsed = 0f
        });
    }

    /// <summary>Legacy alias kept for compatibility with existing callers.</summary>
    public void Trigger(float intensity)
    {
        Shake(intensity, 0.4f);
    }

    public override void _Process(double delta)
    {
        Camera3D? cam = TargetCamera ?? GetViewport()?.GetCamera3D();
        if (cam == null)
        {
            return;
        }

        if (!Enabled)
        {
            _activeShakes.Clear();
            ResetOffsetOn(cam);
            return;
        }

        float dt = (float)delta;
        _noiseTime += dt * 20f;

        // Calculate combined shake intensity from all active shakes
        float combinedIntensity = 0f;

        for (int i = _activeShakes.Count - 1; i >= 0; i--)
        {
            ShakeInstance shake = _activeShakes[i];
            shake.Elapsed += dt;

            if (shake.Elapsed >= shake.Duration)
            {
                _activeShakes.RemoveAt(i);
                continue;
            }

            // Smooth falloff: intensity decreases over duration using quadratic ease-out
            float progress = shake.Elapsed / shake.Duration;
            float falloff = 1f - (progress * progress);
            combinedIntensity += shake.Intensity * falloff;

            _activeShakes[i] = shake;
        }

        if (combinedIntensity <= 0.001f)
        {
            ResetOffsetOn(cam);
            return;
        }

        // Cap combined intensity to prevent wild shaking with many simultaneous explosions
        combinedIntensity = Mathf.Min(combinedIntensity, 2f);

        // Sample Perlin noise at different offsets for each axis for smooth, organic motion
        float shakeX = _noise.GetNoise2D(_noiseTime, 0f) * combinedIntensity;
        float shakeY = _noise.GetNoise2D(0f, _noiseTime) * combinedIntensity;
        float shakeRot = _noise.GetNoise2D(_noiseTime * 0.7f, _noiseTime) * combinedIntensity * 0.3f;

        // Apply position shake via camera offsets (non-destructive to transform)
        cam.HOffset = shakeX * 0.08f + shakeRot * 0.02f;
        cam.VOffset = shakeY * 0.08f;
    }

    private void ResetOffset()
    {
        if (TargetCamera != null && GodotObject.IsInstanceValid(TargetCamera))
        {
            TargetCamera.HOffset = 0f;
            TargetCamera.VOffset = 0f;
        }
    }

    private static void ResetOffsetOn(Camera3D cam)
    {
        cam.HOffset = 0f;
        cam.VOffset = 0f;
    }
}
