using Godot;

namespace VoxelSiege.FX;

/// <summary>
/// Sets up VoxelGI global illumination, environment (sky, tonemap, SSAO, SSR, glow),
/// and a directional sun light for the arena. Should be added to the scene tree once
/// the terrain geometry exists so that VoxelGI can be baked.
/// </summary>
public partial class VoxelGiSetup : Node3D
{
    private VoxelGI? _voxelGi;
    private WorldEnvironment? _worldEnv;
    private DirectionalLight3D? _sunLight;
    private bool _baked;

    // Arena half-extents in metres (arena is 128 microvoxels * 0.5m = 64m across, ~68f with margin)
    private static readonly Vector3 ArenaExtents = new Vector3(68f, 20f, 68f);

    private bool _initialized;

    public override void _Ready()
    {
        ForceInitialize();
    }

    /// <summary>
    /// Runs the full setup (sun, sky, GI nodes) if not already initialized.
    /// Called from _Ready() normally, but also called explicitly by GameManager
    /// to guarantee initialization during deferred loading chains where _Ready()
    /// may not fire on the same frame as AddChild().
    /// </summary>
    public void ForceInitialize()
    {
        if (_initialized) return;
        _initialized = true;

        GD.Print("[VoxelGiSetup] Initializing lighting and sky...");
        SetupSunLight();
        SetupWorldEnvironment();
        SetupVoxelGi();
        GD.Print("[VoxelGiSetup] Setup complete. Scheduling deferred validation...");

        // Deferred validation: check after one frame that the sky is still valid
        // (catches cases where another WorldEnvironment node overrides this one)
        Callable.From(ValidateSkyDeferred).CallDeferred();
    }

    private void ValidateSkyDeferred()
    {
        if (_worldEnv == null || !GodotObject.IsInstanceValid(_worldEnv))
        {
            GD.PushError("[VoxelGiSetup] DEFERRED: WorldEnvironment is null or freed!");
            return;
        }

        Godot.Environment? env = _worldEnv.Environment;
        if (env == null)
        {
            GD.PushError("[VoxelGiSetup] DEFERRED: Environment resource is null!");
            return;
        }

        if (env.BackgroundMode != Godot.Environment.BGMode.Sky)
        {
            GD.PushError($"[VoxelGiSetup] DEFERRED: BackgroundMode is {env.BackgroundMode}, expected Sky! Fixing...");
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
        }

        // If sky or sky material is missing, recreate with ProceduralSkyMaterial
        if (env.Sky == null || env.Sky.SkyMaterial == null)
        {
            GD.PushError("[VoxelGiSetup] DEFERRED: Sky/SkyMaterial is null! Recreating...");
            ProceduralSkyMaterial fallback = new ProceduralSkyMaterial();
            fallback.SkyTopColor = new Color(0.35f, 0.55f, 0.95f);
            fallback.SkyHorizonColor = new Color(0.65f, 0.75f, 0.90f);
            fallback.SkyCurve = 0.1f;
            fallback.SkyEnergyMultiplier = 1.0f;
            fallback.GroundBottomColor = new Color(0.15f, 0.12f, 0.10f);
            fallback.GroundHorizonColor = new Color(0.45f, 0.40f, 0.35f);
            fallback.SunAngleMax = 30.0f;
            fallback.SunCurve = 0.15f;
            fallback.UseDebanding = true;

            if (env.Sky == null) env.Sky = new Sky();
            env.Sky.SkyMaterial = fallback;
        }

        if (_worldEnv.IsInsideTree())
        {
            GD.Print($"[VoxelGiSetup] Deferred OK. Sky: {env.Sky?.SkyMaterial?.GetType().Name}");
        }
        else
        {
            GD.PushError("[VoxelGiSetup] DEFERRED: WorldEnvironment is NOT in the scene tree!");
        }
    }

    /// <summary>
    /// Call this after terrain / build foundations have been generated to bake VoxelGI.
    /// </summary>
    public void BakeGi()
    {
        if (_voxelGi != null && !_baked)
        {
            _voxelGi.Bake();
            _baked = true;
            GD.Print("[VoxelGiSetup] VoxelGI bake triggered.");
        }
    }

    // ─────────────────────────────────────────────────
    //  SUN LIGHT
    // ─────────────────────────────────────────────────

    private void SetupSunLight()
    {
        _sunLight = new DirectionalLight3D();
        _sunLight.Name = "SunLight";

        // Warm sunlight colour
        _sunLight.LightColor = new Color(1.0f, 0.95f, 0.85f);
        _sunLight.LightEnergy = 0.9f;

        // Shadows
        _sunLight.ShadowEnabled = true;
        _sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        _sunLight.DirectionalShadowMaxDistance = 120f;

        // Angle: slight pitch (sun ~60 degrees above horizon, offset yaw)
        _sunLight.RotationDegrees = new Vector3(-55f, -30f, 0f);

        AddChild(_sunLight);
    }

    // ─────────────────────────────────────────────────
    //  WORLD ENVIRONMENT
    // ─────────────────────────────────────────────────

    private void SetupWorldEnvironment()
    {
        // Remove ALL existing WorldEnvironment nodes in the entire scene tree
        // to prevent duplicates. Godot 4 only supports one active WorldEnvironment;
        // if multiple exist, behavior is undefined (often results in a grey sky).
        var existingNodes = GetTree().Root.FindChildren("*", "WorldEnvironment");
        foreach (var node in existingNodes)
        {
            if (node is WorldEnvironment we)
            {
                GD.Print($"[VoxelGiSetup] Removing existing WorldEnvironment: {we.GetPath()}");
                we.GetParent()?.RemoveChild(we);
                we.QueueFree();
            }
        }

        _worldEnv = new WorldEnvironment();
        _worldEnv.Name = "WorldEnvironment";

        // Always create the environment fresh to avoid stale cached resources
        // that might reference shaders which fail to compile at runtime.
        Godot.Environment env = CreateEnvironment();

        _worldEnv.Environment = env;
        AddChild(_worldEnv);

        GD.Print($"[VoxelGiSetup] WorldEnvironment added. " +
                 $"BackgroundMode: {env.BackgroundMode}, " +
                 $"Sky: {env.Sky?.SkyMaterial?.GetType().Name ?? "NULL"}");
    }

    /// <summary>
    /// Builds the full Environment resource from scratch (sky + post-processing).
    /// Called once, then saved to disk for fast reuse on subsequent launches.
    /// </summary>
    private Godot.Environment CreateEnvironment()
    {
        Godot.Environment env = new Godot.Environment();

        // --- Sky: use panorama pixel sky for visual richness ---
        // The pixel_sky.gdshader can fail silently on GPU compile, so we use the
        // pixelated panorama images which always work and match the voxel aesthetic.
        Sky sky = FX.PixelSkyGenerator.CreatePixelPanoramaSky();
        GD.Print($"[VoxelGiSetup] Sky created: {sky.SkyMaterial?.GetType().Name ?? "NULL"}");

        env.Sky = sky;
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        env.AmbientLightSkyContribution = 0.6f;
        env.AmbientLightEnergy = 0.35f;
        env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;

        // Tonemap
        env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        env.TonemapWhite = 6.0f;

        // SSAO
        env.SsaoEnabled = true;
        env.SsaoRadius = 2.0f;
        env.SsaoIntensity = 1.0f;

        // SSR
        env.SsrEnabled = true;
        env.SsrMaxSteps = 32;
        env.SsrFadeIn = 0.15f;
        env.SsrFadeOut = 2.0f;
        env.SsrDepthTolerance = 0.2f;

        // Glow
        env.GlowEnabled = true;
        env.GlowIntensity = 0.5f;
        env.GlowStrength = 1.0f;
        env.GlowBloom = 0.1f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        env.GlowHdrThreshold = 1.2f;

        // Fog
        env.FogEnabled = true;
        env.FogLightColor = new Color(0.7f, 0.75f, 0.85f);
        env.FogLightEnergy = 0.3f;
        env.FogDensity = 0.002f;

        env.VolumetricFogEnabled = false;

        // Color adjustment: slight saturation boost for punchier colors
        env.AdjustmentEnabled = true;
        env.AdjustmentSaturation = 1.12f;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.05f;

        return env;
    }

    // ─────────────────────────────────────────────────
    //  VOXEL GI
    // ─────────────────────────────────────────────────

    private void SetupVoxelGi()
    {
        _voxelGi = new VoxelGI();
        _voxelGi.Name = "VoxelGI";
        _voxelGi.Size = ArenaExtents * 2f; // Size is full extents (diameter), not half
        _voxelGi.Subdiv = VoxelGI.SubdivEnum.Subdiv128;

        // Position at centre of the arena (y offset so it covers ground to max build height)
        _voxelGi.Position = new Vector3(0f, ArenaExtents.Y * 0.5f, 0f);

        VoxelGIData giData = new VoxelGIData();
        giData.DynamicRange = 4.0f;
        giData.Energy = 1.0f;
        giData.Bias = 1.5f;
        giData.NormalBias = 0.0f;
        giData.Propagation = 0.7f;
        giData.Interior = false;
        giData.UseTwoBounces = true;
        _voxelGi.Data = giData;

        AddChild(_voxelGi);
    }
}
