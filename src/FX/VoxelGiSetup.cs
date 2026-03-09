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

    public override void _Ready()
    {
        GD.Print("[VoxelGiSetup] _Ready called, setting up lighting and sky...");
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
            GD.PushError("[VoxelGiSetup] DEFERRED CHECK FAILED: WorldEnvironment is null or freed!");
            return;
        }

        Godot.Environment? env = _worldEnv.Environment;
        if (env == null)
        {
            GD.PushError("[VoxelGiSetup] DEFERRED CHECK FAILED: Environment resource is null!");
            return;
        }

        if (env.BackgroundMode != Godot.Environment.BGMode.Sky)
        {
            GD.PushError($"[VoxelGiSetup] DEFERRED CHECK FAILED: BackgroundMode is {env.BackgroundMode}, expected Sky!");
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
        }

        if (env.Sky == null)
        {
            GD.PushError("[VoxelGiSetup] DEFERRED CHECK FAILED: Sky resource is null! Re-creating...");
            Sky fallback = new Sky();
            ProceduralSkyMaterial mat = new ProceduralSkyMaterial();
            mat.SkyTopColor = new Color(0.18f, 0.30f, 0.55f);
            mat.SkyHorizonColor = new Color(0.58f, 0.72f, 0.82f);
            mat.GroundBottomColor = new Color(0.30f, 0.25f, 0.20f);
            mat.GroundHorizonColor = new Color(0.55f, 0.55f, 0.50f);
            fallback.SkyMaterial = mat;
            env.Sky = fallback;
        }
        else if (env.Sky.SkyMaterial == null)
        {
            GD.PushError("[VoxelGiSetup] DEFERRED CHECK FAILED: SkyMaterial is null! Re-creating...");
            ProceduralSkyMaterial mat = new ProceduralSkyMaterial();
            mat.SkyTopColor = new Color(0.18f, 0.30f, 0.55f);
            mat.SkyHorizonColor = new Color(0.58f, 0.72f, 0.82f);
            mat.GroundBottomColor = new Color(0.30f, 0.25f, 0.20f);
            mat.GroundHorizonColor = new Color(0.55f, 0.55f, 0.50f);
            env.Sky.SkyMaterial = mat;
        }

        // Check if this WorldEnvironment is actually in the scene tree
        if (!_worldEnv.IsInsideTree())
        {
            GD.PushError("[VoxelGiSetup] DEFERRED CHECK FAILED: WorldEnvironment is NOT in the scene tree!");
        }
        else
        {
            GD.Print($"[VoxelGiSetup] Deferred validation PASSED. Sky is {env.Sky.SkyMaterial?.GetType().Name}, " +
                     $"BackgroundMode={env.BackgroundMode}, WorldEnv in tree at path: {_worldEnv.GetPath()}");
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
        _worldEnv = new WorldEnvironment();
        _worldEnv.Name = "WorldEnvironment";

        Godot.Environment env = new Godot.Environment();

        // --- Sky: use ProceduralSkyMaterial first (built-in, guaranteed to work) ---
        // Then attempt to upgrade to the pixel sky shader if it loads successfully.
        Sky sky = new Sky();
        sky.RadianceSize = Sky.RadianceSizeEnum.Size128;

        // Start with ProceduralSkyMaterial -- this ALWAYS works in Godot 4
        ProceduralSkyMaterial proceduralSky = new ProceduralSkyMaterial();
        proceduralSky.SkyTopColor = new Color(0.18f, 0.30f, 0.55f);
        proceduralSky.SkyHorizonColor = new Color(0.58f, 0.72f, 0.82f);
        proceduralSky.GroundBottomColor = new Color(0.30f, 0.25f, 0.20f);
        proceduralSky.GroundHorizonColor = new Color(0.55f, 0.55f, 0.50f);
        proceduralSky.SunAngleMax = 30.0f;
        proceduralSky.SunCurve = 0.15f;
        sky.SkyMaterial = proceduralSky;
        GD.Print("[VoxelGiSetup] ProceduralSkyMaterial created as baseline sky.");

        // Attempt to upgrade to the pixel sky shader for the retro look
        try
        {
            Sky pixelSky = PixelSkyGenerator.CreatePixelSkyResource();
            if (pixelSky?.SkyMaterial is ShaderMaterial shaderMat && shaderMat.Shader != null)
            {
                sky.SkyMaterial = shaderMat;
                GD.Print("[VoxelGiSetup] Upgraded to pixel sky shader successfully.");
            }
            else
            {
                GD.Print("[VoxelGiSetup] Pixel sky shader not available, keeping ProceduralSkyMaterial.");
            }
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[VoxelGiSetup] Pixel sky shader failed: {ex.Message}. Using ProceduralSkyMaterial.");
        }

        GD.Print($"[VoxelGiSetup] Final sky material type: {sky.SkyMaterial?.GetType().Name ?? "NULL"}");

        env.Sky = sky;
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        env.AmbientLightSkyContribution = 0.6f;
        env.AmbientLightEnergy = 0.35f;
        env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;

        // --- Tonemap: ACES ---
        env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        env.TonemapWhite = 6.0f;

        // --- SSAO (reduced intensity: partially redundant with VoxelGI bounce lighting) ---
        env.SsaoEnabled = true;
        env.SsaoRadius = 2.0f;
        env.SsaoIntensity = 1.0f;

        // --- SSR (screen-space reflections for metal/glass voxels, reduced steps) ---
        env.SsrEnabled = true;
        env.SsrMaxSteps = 32;
        env.SsrFadeIn = 0.15f;
        env.SsrFadeOut = 2.0f;
        env.SsrDepthTolerance = 0.2f;

        // --- Glow (bloom on bright particles/explosions) ---
        env.GlowEnabled = true;
        env.GlowIntensity = 0.5f;
        env.GlowStrength = 1.0f;
        env.GlowBloom = 0.1f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        env.GlowHdrThreshold = 1.2f;

        // --- Fog (subtle atmospheric depth) ---
        env.FogEnabled = true;
        env.FogLightColor = new Color(0.7f, 0.75f, 0.85f);
        env.FogLightEnergy = 0.3f;
        env.FogDensity = 0.002f;

        // --- Volumetric fog OFF (too expensive) ---
        env.VolumetricFogEnabled = false;

        _worldEnv.Environment = env;
        AddChild(_worldEnv);

        // Final validation: confirm everything is wired up correctly
        GD.Print($"[VoxelGiSetup] WorldEnvironment added to scene tree. " +
                 $"Environment valid: {_worldEnv.Environment != null}, " +
                 $"BackgroundMode: {env.BackgroundMode}, " +
                 $"Sky valid: {env.Sky != null}, " +
                 $"SkyMaterial valid: {env.Sky?.SkyMaterial != null}");
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
