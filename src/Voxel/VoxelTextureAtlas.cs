using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Voxel;

public enum VoxelFaceDirection
{
    Left,
    Right,
    Bottom,
    Top,
    Back,
    Front,
}

/// <summary>
/// Manages voxel material textures. Supports two modes:
///   1. AI-generated per-material textures loaded from res://assets/textures/voxels/
///   2. Fallback atlas tile mapping (original behaviour) when textures are absent.
///
/// When generated textures are present the atlas is built at runtime by packing
/// the individual per-material images into a single Texture2D atlas. The mesher
/// receives UV rects that map into this atlas exactly as before.
///
/// If no generated textures are found the class behaves identically to the
/// previous version -- tile positions are purely logical and the shader uses
/// vertex color exclusively.
/// </summary>
public sealed class VoxelTextureAtlas
{
    // ----- per-material tile position (logical fallback) -----
    private readonly Dictionary<(VoxelMaterialType Material, VoxelFaceDirection Face), Vector2I> _tileMap
        = new Dictionary<(VoxelMaterialType Material, VoxelFaceDirection Face), Vector2I>();

    // ----- generated-texture support -----
    private readonly Dictionary<VoxelMaterialType, Rect2> _generatedUvRects
        = new Dictionary<VoxelMaterialType, Rect2>();

    /// <summary>True when at least one AI-generated texture was loaded.</summary>
    public bool HasGeneratedTextures { get; private set; }

    /// <summary>
    /// The packed atlas texture built from generated per-material images.
    /// Null when no generated textures are available.
    /// </summary>
    public ImageTexture? AtlasTexture { get; private set; }

    /// <summary>
    /// Normalized tile size within the atlas texture (tile_pixels / atlas_pixels per axis).
    /// Valid only after TryLoadGeneratedTextures has run with at least one texture.
    /// Falls back to (1/TilesPerRow, 1/TilesPerRow) when no generated textures exist.
    /// </summary>
    public Vector2 NormalizedTileSize { get; private set; }

    public int TilesPerRow { get; }
    public int TileResolution { get; }

    // File-name mapping: VoxelMaterialType -> texture base name on disk.
    private static readonly Dictionary<VoxelMaterialType, string> TextureFileNames
        = new Dictionary<VoxelMaterialType, string>
        {
            [VoxelMaterialType.Dirt] = "dirt",
            [VoxelMaterialType.Wood] = "wood",
            [VoxelMaterialType.Stone] = "stone",
            [VoxelMaterialType.Brick] = "brick",
            [VoxelMaterialType.Concrete] = "concrete",
            [VoxelMaterialType.Metal] = "metal",
            [VoxelMaterialType.ReinforcedSteel] = "reinforcedsteel",
            [VoxelMaterialType.Glass] = "glass",
            [VoxelMaterialType.Obsidian] = "obsidian",
            [VoxelMaterialType.Sand] = "sand",
            [VoxelMaterialType.Ice] = "ice",
            [VoxelMaterialType.ArmorPlate] = "armorplate",
            [VoxelMaterialType.Foundation] = "foundation",
            [VoxelMaterialType.Leaves] = "leaves",
            [VoxelMaterialType.Bark] = "bark",
        };

    public VoxelTextureAtlas(int tilesPerRow = 8, int tileResolution = 32)
    {
        TilesPerRow = tilesPerRow;
        TileResolution = tileResolution;
        // Default tile size for the logical fallback (square atlas with TilesPerRow on each side).
        NormalizedTileSize = new Vector2(1f / tilesPerRow, 1f / tilesPerRow);
        SeedDefaultMappings();
        TryLoadGeneratedTextures();
    }

    /// <summary>
    /// Returns the UV rectangle for the given material and face.
    /// When generated textures are loaded the rect maps into <see cref="AtlasTexture"/>.
    /// </summary>
    public Rect2 GetUvRect(VoxelMaterialType material, VoxelFaceDirection face)
    {
        // Fix #6: Guard against Air lookups — Air has no tile entry
        if (material == VoxelMaterialType.Air)
        {
            return new Rect2(0, 0, 0, 0);
        }

        // Prefer generated-texture rect when available.
        if (_generatedUvRects.TryGetValue(material, out Rect2 genRect))
        {
            return genRect;
        }

        // Fallback: logical tile position.
        if (!_tileMap.TryGetValue((material, face), out Vector2I tile))
        {
            tile = _tileMap[(material, VoxelFaceDirection.Front)];
        }

        Vector2 tileSize = new Vector2(1f / TilesPerRow, 1f / TilesPerRow);
        return new Rect2(tile * tileSize, tileSize);
    }

    // ------------------------------------------------------------------
    // Generated texture loading
    // ------------------------------------------------------------------

    private void TryLoadGeneratedTextures()
    {
        const string textureDir = "res://assets/textures/voxels";

        // Collect loaded images keyed by material type.
        var loadedImages = new Dictionary<VoxelMaterialType, Image>();

        foreach ((VoxelMaterialType matType, string baseName) in TextureFileNames)
        {
            // Try 32px first, then 64px fallback.
            string path32 = $"{textureDir}/{baseName}_32.png";
            string path64 = $"{textureDir}/{baseName}_64.png";

            // Use ResourceLoader to load imported textures correctly.
            // Godot imports PNGs as CompressedTexture2D — Image.Load() does not
            // handle the res:// remap and silently fails, leaving the atlas empty.
            Texture2D? tex = null;
            string? chosen = null;

            if (ResourceLoader.Exists(path32))
            {
                tex = ResourceLoader.Load<Texture2D>(path32);
                chosen = path32;
            }
            else if (ResourceLoader.Exists(path64))
            {
                tex = ResourceLoader.Load<Texture2D>(path64);
                chosen = path64;
            }

            if (tex is null)
            {
                continue;
            }

            Image? img = tex.GetImage();
            if (img is null)
            {
                GD.PrintErr($"VoxelTextureAtlas: Failed to get image data from {chosen}");
                continue;
            }

            // Decompress if the image came from a compressed texture format
            if (img.IsCompressed())
            {
                img.Decompress();
            }

            // Ensure consistent size.
            if (img.GetWidth() != TileResolution || img.GetHeight() != TileResolution)
            {
                img.Resize(TileResolution, TileResolution, Image.Interpolation.Nearest);
            }

            // Ensure RGBA8 format for consistent atlas blitting
            if (img.GetFormat() != Image.Format.Rgba8)
            {
                img.Convert(Image.Format.Rgba8);
            }

            loadedImages[matType] = img;
        }

        if (loadedImages.Count == 0)
        {
            return;
        }

        // Pack loaded images into a square atlas.
        int count = loadedImages.Count;
        int cols = TilesPerRow;
        int rows = (count + cols - 1) / cols;
        int atlasWidth = cols * TileResolution;
        int atlasHeight = rows * TileResolution;

        Image atlasImage = Image.CreateEmpty(atlasWidth, atlasHeight, false, Image.Format.Rgba8);

        int slot = 0;
        foreach ((VoxelMaterialType matType, Image img) in loadedImages)
        {
            int col = slot % cols;
            int row = slot / cols;
            int px = col * TileResolution;
            int py = row * TileResolution;

            // Blit the tile into the atlas.
            atlasImage.BlitRect(img, new Rect2I(0, 0, TileResolution, TileResolution), new Vector2I(px, py));

            // Compute normalised UV rect.
            Rect2 uvRect = new Rect2(
                (float)px / atlasWidth,
                (float)py / atlasHeight,
                (float)TileResolution / atlasWidth,
                (float)TileResolution / atlasHeight
            );
            _generatedUvRects[matType] = uvRect;

            slot++;
        }

        AtlasTexture = ImageTexture.CreateFromImage(atlasImage);
        HasGeneratedTextures = true;
        NormalizedTileSize = new Vector2((float)TileResolution / atlasWidth, (float)TileResolution / atlasHeight);

        GD.Print($"VoxelTextureAtlas: Loaded {loadedImages.Count} generated textures into {atlasWidth}x{atlasHeight} atlas.");
    }

    // ------------------------------------------------------------------
    // Fallback tile mapping (original behaviour)
    // ------------------------------------------------------------------

    private void SeedDefaultMappings()
    {
        RegisterUniform(VoxelMaterialType.Dirt, new Vector2I(0, 0));
        RegisterUniform(VoxelMaterialType.Wood, new Vector2I(1, 0));
        RegisterUniform(VoxelMaterialType.Stone, new Vector2I(2, 0));
        RegisterUniform(VoxelMaterialType.Brick, new Vector2I(3, 0));
        RegisterUniform(VoxelMaterialType.Concrete, new Vector2I(4, 0));
        RegisterUniform(VoxelMaterialType.Metal, new Vector2I(5, 0));
        RegisterUniform(VoxelMaterialType.ReinforcedSteel, new Vector2I(6, 0));
        RegisterUniform(VoxelMaterialType.Glass, new Vector2I(7, 0));
        RegisterUniform(VoxelMaterialType.Obsidian, new Vector2I(0, 1));
        RegisterUniform(VoxelMaterialType.Sand, new Vector2I(1, 1));
        RegisterUniform(VoxelMaterialType.Ice, new Vector2I(2, 1));
        RegisterUniform(VoxelMaterialType.ArmorPlate, new Vector2I(3, 1));
        RegisterUniform(VoxelMaterialType.Foundation, new Vector2I(4, 1));
        RegisterUniform(VoxelMaterialType.Leaves, new Vector2I(5, 1));
        RegisterUniform(VoxelMaterialType.Bark, new Vector2I(6, 1));
    }

    private void RegisterUniform(VoxelMaterialType material, Vector2I tile)
    {
        foreach (VoxelFaceDirection face in System.Enum.GetValues<VoxelFaceDirection>())
        {
            _tileMap[(material, face)] = tile;
        }
    }
}
