using Godot;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VoxelSiege.Building;

/// <summary>
/// A preset building blueprint definition. Each block is stored as a Vector3I offset
/// relative to the placement origin. The material is a placeholder — at placement time,
/// the player's currently selected material is used for all blocks.
/// </summary>
public sealed class BlueprintDefinition
{
    public string Name { get; }
    public string Icon { get; }
    public string Description { get; }
    public ReadOnlyCollection<Vector3I> Offsets { get; }

    /// <summary>
    /// Bounding box size in build units (for cost estimation and preview sizing).
    /// </summary>
    public Vector3I Size { get; }

    public int BlockCount => Offsets.Count;

    public BlueprintDefinition(string name, string icon, string description, List<Vector3I> offsets)
    {
        Name = name;
        Icon = icon;
        Description = description;
        Offsets = offsets.AsReadOnly();

        // Compute bounding box
        if (offsets.Count == 0)
        {
            Size = Vector3I.Zero;
            return;
        }

        Vector3I min = offsets[0];
        Vector3I max = offsets[0];
        foreach (Vector3I offset in offsets)
        {
            min = new Vector3I(
                System.Math.Min(min.X, offset.X),
                System.Math.Min(min.Y, offset.Y),
                System.Math.Min(min.Z, offset.Z));
            max = new Vector3I(
                System.Math.Max(max.X, offset.X),
                System.Math.Max(max.Y, offset.Y),
                System.Math.Max(max.Z, offset.Z));
        }

        Size = max - min + Vector3I.One;
    }

    /// <summary>
    /// Returns the blueprint offsets rotated by the given number of 90-degree CW steps
    /// around the Y axis, relative to the origin.
    /// </summary>
    public List<Vector3I> GetRotatedOffsets(int rotation)
    {
        rotation = ((rotation % 4) + 4) % 4;
        if (rotation == 0)
        {
            return new List<Vector3I>(Offsets);
        }

        List<Vector3I> rotated = new List<Vector3I>(Offsets.Count);
        foreach (Vector3I offset in Offsets)
        {
            int dx = offset.X;
            int dz = offset.Z;
            int rx, rz;

            switch (rotation)
            {
                case 1: // 90° CW
                    rx = -dz;
                    rz = dx;
                    break;
                case 2: // 180°
                    rx = -dx;
                    rz = -dz;
                    break;
                case 3: // 270° CW
                    rx = dz;
                    rz = -dx;
                    break;
                default:
                    rx = dx;
                    rz = dz;
                    break;
            }

            rotated.Add(new Vector3I(rx, offset.Y, rz));
        }

        return rotated;
    }
}

/// <summary>
/// Static registry of all preset building blueprints. Each blueprint defines block
/// positions in build-unit coordinates relative to a placement origin (0,0,0).
/// The player's selected material is applied to all blocks at placement time.
/// </summary>
public static class BuildBlueprints
{
    private static BlueprintDefinition[]? _all;

    /// <summary>All available blueprint presets.</summary>
    public static BlueprintDefinition[] All => _all ??= CreateAll();

    private static BlueprintDefinition[] CreateAll()
    {
        return new[]
        {
            CreateTower(),
            CreateRoom(),
            CreateWallSegment(),
            CreateBunker(),
            CreateRampStaircase(),
            CreateSniperNest(),
        };
    }

    // ─────────────────────────────────────────────────
    //  TOWER: 3x3 base, 6 tall, open top for weapon
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateTower()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        // 3x3 base, 6 tall, hollow shell (walls only, open top)
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                for (int z = 0; z < 3; z++)
                {
                    // Shell: edges only (hollow interior), but floor is solid
                    bool isShell = x == 0 || x == 2 || z == 0 || z == 2;
                    bool isFloor = y == 0;
                    if (isShell || isFloor)
                    {
                        offsets.Add(new Vector3I(x, y, z));
                    }
                }
            }
        }

        return new BlueprintDefinition(
            "Tower",
            "\u265c", // rook chess piece
            "3x3, 6 tall, open top",
            offsets);
    }

    // ─────────────────────────────────────────────────
    //  ROOM: 5x5x4 hollow box, 1-block walls, doorway
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateRoom()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++)
                {
                    bool isShell = x == 0 || x == 4 || y == 0 || y == 3 || z == 0 || z == 4;
                    if (!isShell)
                    {
                        continue;
                    }

                    // Doorway: remove blocks at the center of the front face (z=0),
                    // at ground level (y=0 and y=1), x=2 (center column)
                    if (z == 0 && x == 2 && (y == 1 || y == 2))
                    {
                        continue;
                    }

                    offsets.Add(new Vector3I(x, y, z));
                }
            }
        }

        return new BlueprintDefinition(
            "Room",
            "\u2302", // house symbol
            "5x5x4 room, doorway",
            offsets);
    }

    // ─────────────────────────────────────────────────
    //  WALL: 6x1x4 solid wall segment
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateWallSegment()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                offsets.Add(new Vector3I(x, y, 0));
            }
        }

        return new BlueprintDefinition(
            "Wall",
            "\u2588", // full block
            "6 wide, 4 tall, 1 deep",
            offsets);
    }

    // ─────────────────────────────────────────────────
    //  BUNKER: 4x4x3, thick walls, window slits
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateBunker()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                for (int z = 0; z < 4; z++)
                {
                    // Solid everywhere except the interior cavity:
                    // Interior is x=1..2, y=1..1, z=1..2 (small 2x1x2 gap inside)
                    bool isInterior = x >= 1 && x <= 2 && y >= 1 && y <= 1 && z >= 1 && z <= 2;

                    // Window slits at y=2 (top row) on front and sides, 1 block wide each
                    // Front slit: z=0, x=1 or x=2
                    bool isSlit = y == 2 && (
                        (z == 0 && (x == 1 || x == 2)) ||  // front face
                        (x == 0 && (z == 1 || z == 2)) ||  // left face
                        (x == 3 && (z == 1 || z == 2))     // right face
                    );

                    if (!isInterior && !isSlit)
                    {
                        offsets.Add(new Vector3I(x, y, z));
                    }
                }
            }
        }

        return new BlueprintDefinition(
            "Bunker",
            "\u2b1b", // black square
            "4x4x3, thick walls, slits",
            offsets);
    }

    // ─────────────────────────────────────────────────
    //  RAMP: 3-wide staircase going up 4 blocks
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateRampStaircase()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        // 4 steps, each 1 block tall, 3 wide (in X), going forward in Z
        for (int step = 0; step < 4; step++)
        {
            // Each step is solid from y=0 up to y=step, at z=step, width 3
            for (int y = 0; y <= step; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    offsets.Add(new Vector3I(x, y, step));
                }
            }
        }

        return new BlueprintDefinition(
            "Ramp",
            "\u25e2", // triangle
            "3 wide, 4 steps up",
            offsets);
    }

    // ─────────────────────────────────────────────────
    //  SNIPER NEST: 2x2 pillar, 5 tall, overhang top
    // ─────────────────────────────────────────────────
    private static BlueprintDefinition CreateSniperNest()
    {
        List<Vector3I> offsets = new List<Vector3I>();

        // 2x2 pillar from y=0 to y=4 (5 tall)
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    offsets.Add(new Vector3I(x, y, z));
                }
            }
        }

        // Overhang platform at y=4: extends 1 block out on all sides (4x4)
        // making a 4x1x4 platform on top of the 2x2 pillar
        for (int x = -1; x <= 2; x++)
        {
            for (int z = -1; z <= 2; z++)
            {
                // Skip the 2x2 pillar core — already added above
                if (x >= 0 && x <= 1 && z >= 0 && z <= 1)
                {
                    continue;
                }

                offsets.Add(new Vector3I(x, 4, z));
            }
        }

        // Railing: 1-block-high walls on the platform edge at y=5
        for (int x = -1; x <= 2; x++)
        {
            for (int z = -1; z <= 2; z++)
            {
                bool isEdge = x == -1 || x == 2 || z == -1 || z == 2;
                if (isEdge)
                {
                    offsets.Add(new Vector3I(x, 5, z));
                }
            }
        }

        return new BlueprintDefinition(
            "Sniper Nest",
            "\u2316", // crosshair
            "2x2 pillar, 5 tall, platform",
            offsets);
    }
}
