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

public sealed class VoxelTextureAtlas
{
    private readonly Dictionary<(VoxelMaterialType Material, VoxelFaceDirection Face), Vector2I> _tileMap = new Dictionary<(VoxelMaterialType Material, VoxelFaceDirection Face), Vector2I>();

    public int TilesPerRow { get; }
    public int TileResolution { get; }

    public VoxelTextureAtlas(int tilesPerRow = 8, int tileResolution = 256)
    {
        TilesPerRow = tilesPerRow;
        TileResolution = tileResolution;
        SeedDefaultMappings();
    }

    public Rect2 GetUvRect(VoxelMaterialType material, VoxelFaceDirection face)
    {
        if (!_tileMap.TryGetValue((material, face), out Vector2I tile))
        {
            tile = _tileMap[(material, VoxelFaceDirection.Front)];
        }

        Vector2 tileSize = new Vector2(1f / TilesPerRow, 1f / TilesPerRow);
        return new Rect2(tile * tileSize, tileSize);
    }

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
    }

    private void RegisterUniform(VoxelMaterialType material, Vector2I tile)
    {
        foreach (VoxelFaceDirection face in System.Enum.GetValues<VoxelFaceDirection>())
        {
            _tileMap[(material, face)] = tile;
        }
    }
}
