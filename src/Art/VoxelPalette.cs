using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Art;

/// <summary>
/// Generates a palette texture from voxel model colors with subtle per-pixel noise
/// for micro-contrast. This makes procedural voxel models render through the same
/// texture pipeline as world blocks — fixing dark upward faces (flat vertex colors
/// get crushed by ACES tonemapping) and washed-out appearance from the toon shader.
///
/// Usage:
///   var palette = new VoxelPalette();
///   palette.AddColors(voxelArray1);
///   palette.AddColors(voxelArray2); // shared palette across parts
///   palette.Build();
///   ArrayMesh mesh = builder.BuildMesh(voxels, palette);
///   meshInstance.MaterialOverride = palette.CreateMaterial();
/// </summary>
public class VoxelPalette
{
    /// <summary>Pixels per color patch in the palette texture.</summary>
    private const int PatchSize = 8;

    /// <summary>Per-pixel brightness noise range (±4%).</summary>
    private const float NoiseAmount = 0.04f;

    private readonly Dictionary<Color, int> _colorToIndex = new();
    private readonly List<Color> _colors = new();
    private bool _built;

    /// <summary>The generated palette texture. Null before Build() or if no colors were added.</summary>
    public ImageTexture? Texture { get; private set; }

    /// <summary>Texture width in pixels.</summary>
    public int TextureWidth { get; private set; }

    /// <summary>Texture height in pixels.</summary>
    public int TextureHeight { get; private set; }

    /// <summary>Number of columns in the palette grid.</summary>
    public int Columns { get; private set; }

    /// <summary>
    /// Registers all unique colors from a voxel array.
    /// Call before Build(). Can be called multiple times to build a shared palette.
    /// </summary>
    public void AddColors(Color?[,,] voxels)
    {
        int sx = voxels.GetLength(0);
        int sy = voxels.GetLength(1);
        int sz = voxels.GetLength(2);

        for (int x = 0; x < sx; x++)
            for (int y = 0; y < sy; y++)
                for (int z = 0; z < sz; z++)
                {
                    Color? c = voxels[x, y, z];
                    if (c.HasValue && !_colorToIndex.ContainsKey(c.Value))
                    {
                        _colorToIndex[c.Value] = _colors.Count;
                        _colors.Add(c.Value);
                    }
                }
    }

    /// <summary>
    /// Builds the palette texture. Must be called after all AddColors() calls.
    /// Each color gets a PatchSize×PatchSize block with subtle per-pixel noise
    /// that provides micro-contrast surviving ACES tonemapping.
    /// </summary>
    public void Build()
    {
        _built = true;
        if (_colors.Count == 0) return;

        Columns = Mathf.CeilToInt(Mathf.Sqrt(_colors.Count));
        if (Columns < 1) Columns = 1;
        int rows = (_colors.Count + Columns - 1) / Columns;
        TextureWidth = Columns * PatchSize;
        TextureHeight = rows * PatchSize;

        Image img = Image.CreateEmpty(TextureWidth, TextureHeight, false, Image.Format.Rgba8);
        RandomNumberGenerator rng = new() { Seed = 7919 };

        for (int i = 0; i < _colors.Count; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            int px = col * PatchSize;
            int py = row * PatchSize;

            Color baseColor = _colors[i];
            for (int dx = 0; dx < PatchSize; dx++)
            {
                for (int dy = 0; dy < PatchSize; dy++)
                {
                    float noise = rng.RandfRange(-NoiseAmount, NoiseAmount);
                    Color noisy = new Color(
                        Mathf.Clamp(baseColor.R + noise, 0f, 1f),
                        Mathf.Clamp(baseColor.G + noise, 0f, 1f),
                        Mathf.Clamp(baseColor.B + noise, 0f, 1f),
                        1.0f
                    );
                    img.SetPixel(px + dx, py + dy, noisy);
                }
            }
        }

        Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Returns the UV coordinates for a quad (4 vertices) mapped to the given color's
    /// patch in the palette texture. The UVs sample the inner portion to avoid bleeding.
    /// Vertex order matches VoxelModelBuilder's FaceUVs: BL, TL, TR, BR.
    /// </summary>
    public Vector2[] GetFaceUVs(Color color)
    {
        if (!_built || Texture == null || !_colorToIndex.TryGetValue(color, out int idx))
        {
            // Fallback: full-texture UVs (shouldn't happen in practice)
            return new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) };
        }

        int col = idx % Columns;
        int row = idx / Columns;

        // Center of the patch in normalized UV space
        float uCenter = (col * PatchSize + PatchSize * 0.5f) / TextureWidth;
        float vCenter = (row * PatchSize + PatchSize * 0.5f) / TextureHeight;

        // Half-extent, inset by 10% to avoid texture filtering bleeding
        float uHalf = (PatchSize * 0.45f) / TextureWidth;
        float vHalf = (PatchSize * 0.45f) / TextureHeight;

        return new[]
        {
            new Vector2(uCenter - uHalf, vCenter + vHalf), // bottom-left
            new Vector2(uCenter - uHalf, vCenter - vHalf), // top-left
            new Vector2(uCenter + uHalf, vCenter - vHalf), // top-right
            new Vector2(uCenter + uHalf, vCenter + vHalf), // bottom-right
        };
    }

    /// <summary>
    /// Creates a StandardMaterial3D configured for the palette texture.
    /// Nearest-neighbor filtering preserves the crispy voxel art aesthetic.
    /// </summary>
    public StandardMaterial3D CreateMaterial(float metallic = 0.0f, float roughness = 0.8f)
    {
        StandardMaterial3D mat = new();
        mat.AlbedoTexture = Texture;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        mat.Metallic = metallic;
        mat.Roughness = roughness;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        return mat;
    }
}
