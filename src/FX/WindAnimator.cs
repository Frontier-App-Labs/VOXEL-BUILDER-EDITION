using Godot;

namespace VoxelSiege.FX;

/// <summary>
/// Updates global shader wind parameters each frame for vegetation animation.
/// Attach to any Node in the scene tree.
/// </summary>
public partial class WindAnimator : Node
{
    [Export]
    public float WindStrength { get; set; } = 0.15f;

    [Export]
    public float WindSpeed { get; set; } = 2.0f;

    [Export]
    public Vector2 WindDirection { get; set; } = new Vector2(1f, 0.5f);

    public override void _Ready()
    {
        // Update global shader uniforms (declared in project.godot [shader_globals])
        RenderingServer.GlobalShaderParameterSet("wind_time", 0.0f);
        RenderingServer.GlobalShaderParameterSet("wind_strength", WindStrength);
        RenderingServer.GlobalShaderParameterSet("wind_direction", WindDirection.Normalized());
    }

    public override void _Process(double delta)
    {
        float time = (float)Time.GetTicksMsec() / 1000.0f * WindSpeed;
        RenderingServer.GlobalShaderParameterSet("wind_time", time);
    }

    public override void _ExitTree()
    {
        // Reset wind to zero on exit
        RenderingServer.GlobalShaderParameterSet("wind_time", 0.0f);
        RenderingServer.GlobalShaderParameterSet("wind_strength", 0.0f);
    }
}
