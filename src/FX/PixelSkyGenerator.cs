using Godot;

namespace VoxelSiege.FX;

/// <summary>
/// Generates a pixelated sky <see cref="ShaderMaterial"/> that matches the blocky voxel art
/// style of Voxel Siege. The sky uses a custom shader
/// (<c>res://assets/shaders/pixel_sky.gdshader</c>) that quantises the view direction into
/// a coarse pixel grid, producing stepped colour bands, a square sun disc, blocky clouds
/// and subtle twinkling stars.
/// </summary>
public static class PixelSkyGenerator
{
    private const string ShaderPath = "res://assets/shaders/pixel_sky.gdshader";

    /// <summary>
    /// Creates a <see cref="ShaderMaterial"/> configured as a pixelated sky.
    /// The material uses <c>shader_type sky</c>, so it can be assigned directly to a
    /// <see cref="Sky.SkyMaterial"/>.
    /// </summary>
    /// <param name="preset">Visual preset that adjusts colour palette and mood.</param>
    /// <returns>A ready-to-use <see cref="ShaderMaterial"/> for the sky.</returns>
    public static ShaderMaterial CreatePixelSky(SkyPreset preset = SkyPreset.GoldenHour)
    {
        Shader shader = GD.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            GD.PushError($"[PixelSkyGenerator] Failed to load sky shader at '{ShaderPath}'.");
            return new ShaderMaterial();
        }

        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = shader;

        ApplyPreset(mat, preset);

        return mat;
    }

    /// <summary>
    /// Convenience method that returns a fully configured <see cref="Sky"/> resource
    /// with the pixelated shader material applied.
    /// </summary>
    public static Sky CreatePixelSkyResource(SkyPreset preset = SkyPreset.GoldenHour)
    {
        Sky sky = new Sky();
        sky.SkyMaterial = CreatePixelSky(preset);
        // Keep radiance low-res to match the pixel aesthetic and save perf
        sky.RadianceSize = Sky.RadianceSizeEnum.Size128;
        return sky;
    }

    /// <summary>
    /// Creates a <see cref="Sky"/> using a real pixelated panorama image (CC0 from Poly Haven).
    /// Falls back to procedural shader if the image is not found.
    /// </summary>
    public static Sky CreatePixelPanoramaSky()
    {
        // Try loading the pixelated panorama (64x32 for maximum blockiness)
        string[] paths = {
            "res://assets/textures/sky/sky_pixel_64x32.png",
            "res://assets/textures/sky/sky_pixel_128x64.png",
        };

        foreach (string path in paths)
        {
            if (ResourceLoader.Exists(path))
            {
                Texture2D loadedTex = ResourceLoader.Load<Texture2D>(path);
                if (loadedTex != null)
                {
                    Image img = loadedTex.GetImage();
                    if (img != null && img.GetFormat() != Image.Format.Rgba8)
                    {
                        img.Convert(Image.Format.Rgba8);
                    }
                    ImageTexture? tex = img != null ? ImageTexture.CreateFromImage(img) : null;
                    if (tex == null) continue;

                    PanoramaSkyMaterial panorama = new PanoramaSkyMaterial();
                    panorama.Panorama = tex;
                    panorama.Filter = false; // NEAREST filtering — keeps pixels blocky!

                    Sky sky = new Sky();
                    sky.SkyMaterial = panorama;
                    sky.RadianceSize = Sky.RadianceSizeEnum.Size128;
                    GD.Print($"[PixelSkyGenerator] Using pixelated panorama from {path}");
                    return sky;
                }
            }
        }

        // Fallback to procedural shader
        GD.Print("[PixelSkyGenerator] Panorama not found, trying procedural pixel sky.");
        Sky proceduralSky = CreatePixelSkyResource(SkyPreset.GoldenHour);
        if (proceduralSky.SkyMaterial is ShaderMaterial shaderMat && shaderMat.Shader != null)
        {
            return proceduralSky;
        }

        // Ultimate fallback: built-in ProceduralSkyMaterial (never returns grey)
        GD.PushWarning("[PixelSkyGenerator] All sky methods failed, using built-in ProceduralSkyMaterial.");
        ProceduralSkyMaterial builtinSky = new ProceduralSkyMaterial();
        builtinSky.SkyTopColor = new Color(0.18f, 0.30f, 0.55f);
        builtinSky.SkyHorizonColor = new Color(0.58f, 0.72f, 0.82f);
        builtinSky.GroundBottomColor = new Color(0.30f, 0.25f, 0.20f);
        builtinSky.GroundHorizonColor = new Color(0.55f, 0.55f, 0.50f);
        Sky fallbackSky = new Sky();
        fallbackSky.SkyMaterial = builtinSky;
        fallbackSky.RadianceSize = Sky.RadianceSizeEnum.Size128;
        return fallbackSky;
    }

    // ─────────────────────────────────────────────────
    //  Presets
    // ─────────────────────────────────────────────────

    private static void ApplyPreset(ShaderMaterial mat, SkyPreset preset)
    {
        switch (preset)
        {
            case SkyPreset.GoldenHour:
                ApplyGoldenHour(mat);
                break;
            case SkyPreset.HighNoon:
                ApplyHighNoon(mat);
                break;
            case SkyPreset.Overcast:
                ApplyOvercast(mat);
                break;
            default:
                ApplyGoldenHour(mat);
                break;
        }
    }

    /// <summary>
    /// Warm sunset / golden-hour palette. Default for Voxel Siege —
    /// evokes the "tactical toy warfare" aesthetic.
    /// </summary>
    private static void ApplyGoldenHour(ShaderMaterial mat)
    {
        mat.SetShaderParameter("pixel_density", 48.0f);
        mat.SetShaderParameter("color_bands", 6);

        mat.SetShaderParameter("zenith_color",        new Color(0.10f, 0.10f, 0.24f));
        mat.SetShaderParameter("upper_sky_color",     new Color(0.18f, 0.30f, 0.55f));
        mat.SetShaderParameter("mid_sky_color",       new Color(0.36f, 0.55f, 0.75f));
        mat.SetShaderParameter("low_sky_color",       new Color(0.58f, 0.72f, 0.82f));
        mat.SetShaderParameter("horizon_color",       new Color(0.91f, 0.66f, 0.28f));
        mat.SetShaderParameter("below_horizon_color", new Color(0.83f, 0.47f, 0.23f));
        mat.SetShaderParameter("ground_color",        new Color(0.30f, 0.25f, 0.20f));

        mat.SetShaderParameter("sun_tint",            new Color(1.0f, 0.96f, 0.83f));
        mat.SetShaderParameter("sun_pixel_radius",    3.0f);
        mat.SetShaderParameter("sun_glow_radius",     7.0f);
        mat.SetShaderParameter("sun_glow_color",      new Color(1.0f, 0.85f, 0.55f));

        mat.SetShaderParameter("cloud_speed",         0.006f);
        mat.SetShaderParameter("cloud_coverage",      0.40f);
        mat.SetShaderParameter("cloud_altitude",      0.30f);
        mat.SetShaderParameter("cloud_bright_color",  new Color(1.0f, 0.97f, 0.92f));
        mat.SetShaderParameter("cloud_shadow_color",  new Color(0.65f, 0.62f, 0.72f));

        mat.SetShaderParameter("star_density",        0.015f);
        mat.SetShaderParameter("star_brightness",     0.8f);
    }

    /// <summary>
    /// Bright daytime sky with vivid blue tones and fewer stars.
    /// </summary>
    private static void ApplyHighNoon(ShaderMaterial mat)
    {
        mat.SetShaderParameter("pixel_density", 48.0f);
        mat.SetShaderParameter("color_bands", 5);

        mat.SetShaderParameter("zenith_color",        new Color(0.15f, 0.22f, 0.55f));
        mat.SetShaderParameter("upper_sky_color",     new Color(0.25f, 0.45f, 0.80f));
        mat.SetShaderParameter("mid_sky_color",       new Color(0.40f, 0.62f, 0.90f));
        mat.SetShaderParameter("low_sky_color",       new Color(0.60f, 0.78f, 0.95f));
        mat.SetShaderParameter("horizon_color",       new Color(0.75f, 0.82f, 0.90f));
        mat.SetShaderParameter("below_horizon_color", new Color(0.55f, 0.55f, 0.50f));
        mat.SetShaderParameter("ground_color",        new Color(0.35f, 0.30f, 0.25f));

        mat.SetShaderParameter("sun_tint",            new Color(1.0f, 1.0f, 0.95f));
        mat.SetShaderParameter("sun_pixel_radius",    2.5f);
        mat.SetShaderParameter("sun_glow_radius",     5.0f);
        mat.SetShaderParameter("sun_glow_color",      new Color(1.0f, 0.95f, 0.80f));

        mat.SetShaderParameter("cloud_speed",         0.008f);
        mat.SetShaderParameter("cloud_coverage",      0.35f);
        mat.SetShaderParameter("cloud_altitude",      0.30f);
        mat.SetShaderParameter("cloud_bright_color",  new Color(1.0f, 1.0f, 1.0f));
        mat.SetShaderParameter("cloud_shadow_color",  new Color(0.70f, 0.70f, 0.78f));

        mat.SetShaderParameter("star_density",        0.0f);
        mat.SetShaderParameter("star_brightness",     0.0f);
    }

    /// <summary>
    /// Muted, grey overcast sky with heavier cloud coverage.
    /// </summary>
    private static void ApplyOvercast(ShaderMaterial mat)
    {
        mat.SetShaderParameter("pixel_density", 48.0f);
        mat.SetShaderParameter("color_bands", 4);

        mat.SetShaderParameter("zenith_color",        new Color(0.35f, 0.38f, 0.42f));
        mat.SetShaderParameter("upper_sky_color",     new Color(0.45f, 0.48f, 0.52f));
        mat.SetShaderParameter("mid_sky_color",       new Color(0.55f, 0.58f, 0.60f));
        mat.SetShaderParameter("low_sky_color",       new Color(0.62f, 0.64f, 0.65f));
        mat.SetShaderParameter("horizon_color",       new Color(0.68f, 0.67f, 0.65f));
        mat.SetShaderParameter("below_horizon_color", new Color(0.50f, 0.48f, 0.45f));
        mat.SetShaderParameter("ground_color",        new Color(0.30f, 0.28f, 0.26f));

        mat.SetShaderParameter("sun_tint",            new Color(0.90f, 0.88f, 0.82f));
        mat.SetShaderParameter("sun_pixel_radius",    2.0f);
        mat.SetShaderParameter("sun_glow_radius",     4.0f);
        mat.SetShaderParameter("sun_glow_color",      new Color(0.80f, 0.78f, 0.72f));

        mat.SetShaderParameter("cloud_speed",         0.004f);
        mat.SetShaderParameter("cloud_coverage",      0.65f);
        mat.SetShaderParameter("cloud_altitude",      0.25f);
        mat.SetShaderParameter("cloud_bright_color",  new Color(0.80f, 0.80f, 0.80f));
        mat.SetShaderParameter("cloud_shadow_color",  new Color(0.55f, 0.52f, 0.55f));

        mat.SetShaderParameter("star_density",        0.0f);
        mat.SetShaderParameter("star_brightness",     0.0f);
    }
}

/// <summary>
/// Predefined colour palettes for the pixel sky.
/// </summary>
public enum SkyPreset
{
    /// <summary>Warm sunset / golden-hour. Default for Voxel Siege.</summary>
    GoldenHour,

    /// <summary>Bright midday sky with vivid blues.</summary>
    HighNoon,

    /// <summary>Muted grey overcast with heavy clouds.</summary>
    Overcast,
}
