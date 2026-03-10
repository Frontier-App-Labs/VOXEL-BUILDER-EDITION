using Godot;

namespace VoxelSiege.FX;

/// <summary>
/// Loads a pixelated sky panorama and creates a PanoramaSkyMaterial.
/// Tries multiple loading methods to guarantee the sky appears.
/// CC0 source: Industrial Sunset Pure Sky from Poly Haven.
/// </summary>
public static class PixelSkyGenerator
{
    private static readonly string[] SkyPaths =
    {
        "res://assets/textures/sky/sky_panorama.png",
        "res://assets/textures/sky/sky_pixel_128x64.png",
        "res://assets/textures/sky/sky_pixel_64x32.png",
    };

    public static Sky CreatePixelPanoramaSky()
    {
        // Method 1: Load PNG from absolute OS path (bypasses Godot import system)
        foreach (string resPath in SkyPaths)
        {
            string absPath = ProjectSettings.GlobalizePath(resPath);
            GD.Print($"[Sky] Trying Image.Load: {absPath}");

            Image img = new Image();
            Error err = img.Load(absPath);
            if (err == Error.Ok && img.GetWidth() > 0)
            {
                GD.Print($"[Sky] Image.Load OK: {img.GetWidth()}x{img.GetHeight()}");
                return MakePanoramaSky(ImageTexture.CreateFromImage(img));
            }
            GD.Print($"[Sky] Image.Load failed: {err}");
        }

        // Method 2: ResourceLoader (goes through Godot import system)
        foreach (string resPath in SkyPaths)
        {
            GD.Print($"[Sky] Trying ResourceLoader: {resPath}");
            if (!ResourceLoader.Exists(resPath)) { GD.Print("[Sky] ResourceLoader: does not exist"); continue; }

            Texture2D? tex = ResourceLoader.Load<Texture2D>(resPath);
            if (tex == null) { GD.Print("[Sky] ResourceLoader: load returned null"); continue; }

            Image? img = tex.GetImage();
            if (img == null) { GD.Print("[Sky] ResourceLoader: GetImage returned null"); continue; }

            if (img.GetFormat() != Image.Format.Rgba8)
                img.Convert(Image.Format.Rgba8);

            ImageTexture imgTex = ImageTexture.CreateFromImage(img);
            if (imgTex != null)
            {
                GD.Print($"[Sky] ResourceLoader OK: {img.GetWidth()}x{img.GetHeight()}");
                return MakePanoramaSky(imgTex);
            }
        }

        // Method 3: HDR panorama via ResourceLoader
        string hdrPath = "res://assets/textures/sky/sky_panorama_hd.hdr";
        GD.Print($"[Sky] Trying HDR: {hdrPath}");
        if (ResourceLoader.Exists(hdrPath))
        {
            Texture2D? hdrTex = ResourceLoader.Load<Texture2D>(hdrPath);
            if (hdrTex != null)
            {
                GD.Print($"[Sky] HDR loaded: {hdrTex.GetWidth()}x{hdrTex.GetHeight()}");
                return MakePanoramaSky(hdrTex);
            }
        }

        // Method 4: ProceduralSkyMaterial (Godot built-in, zero dependencies)
        GD.PushWarning("[Sky] ALL image methods failed. Using ProceduralSkyMaterial.");
        ProceduralSkyMaterial proc = new ProceduralSkyMaterial();
        proc.SkyTopColor = new Color(0.18f, 0.30f, 0.55f);
        proc.SkyHorizonColor = new Color(0.65f, 0.75f, 0.90f);
        proc.GroundBottomColor = new Color(0.25f, 0.20f, 0.15f);
        proc.GroundHorizonColor = new Color(0.55f, 0.55f, 0.50f);
        proc.SunAngleMax = 30f;
        proc.SunCurve = 0.15f;

        Sky fallback = new Sky();
        fallback.SkyMaterial = proc;
        fallback.RadianceSize = Sky.RadianceSizeEnum.Size256;
        return fallback;
    }

    private static Sky MakePanoramaSky(Texture2D tex)
    {
        PanoramaSkyMaterial mat = new PanoramaSkyMaterial();
        mat.Panorama = tex;
        mat.Filter = true; // Smooth filtering at 512x256

        Sky sky = new Sky();
        sky.SkyMaterial = mat;
        sky.RadianceSize = Sky.RadianceSizeEnum.Size256;
        return sky;
    }
}
