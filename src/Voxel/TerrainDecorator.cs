using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;

namespace VoxelSiege.Voxel;

/// <summary>
/// Generates procedural vegetation and zone markers on the terrain.
/// Grass is rendered as a dense carpet of small box blades via MultiMeshInstance3D
/// for efficient instanced rendering with per-blade color variation.
/// </summary>
public static class TerrainDecorator
{
    private static readonly Random Rng = new Random(42);

    // Grass blade dimensions — small blades for a dense carpet look
    private const float BladeWidth = 0.03f;
    private const float BladeMinHeight = 0.08f;
    private const float BladeMaxHeight = 0.18f;
    private const float BladeDepth = 0.03f;

    // Grass color variation
    private static readonly Color GrassBaseColor = new Color(0.30f, 0.65f, 0.20f);
    private static readonly Color GrassDarkColor = new Color(0.22f, 0.50f, 0.15f);
    private static readonly Color GrassLightColor = new Color(0.40f, 0.75f, 0.28f);

    /// <summary>
    /// Places trees and vegetation around the arena, avoiding build zones.
    /// Also scatters thin grass blades across the entire arena ground.
    /// </summary>
    public static void DecorateArena(VoxelWorld world, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones)
    {
        int halfWidth = GameConfig.PrototypeArenaWidth / 2;
        int halfDepth = GameConfig.PrototypeArenaDepth / 2;
        int groundY = GameConfig.PrototypeGroundThickness; // first y above ground

        // Place trees scattered around the arena (not in build zones)
        int treeCount = 20;
        int placed = 0;
        int attempts = 0;
        while (placed < treeCount && attempts < 200)
        {
            attempts++;
            int tx = Rng.Next(-halfWidth + 2, halfWidth - 2);
            int tz = Rng.Next(-halfDepth + 2, halfDepth - 2);

            // Check this position is not inside any build zone (with buffer)
            bool inZone = false;
            foreach (BuildZone zone in buildZones.Values)
            {
                Vector3I zMin = zone.OriginMicrovoxels;
                Vector3I zMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;
                int buffer = 6; // 3 build units * 2 microvoxels
                if (tx >= zMin.X - buffer && tx <= zMax.X + buffer &&
                    tz >= zMin.Z - buffer && tz <= zMax.Z + buffer)
                {
                    inZone = true;
                    break;
                }
            }

            if (inZone) continue;

            GenerateTree(world, new Vector3I(tx, groundY, tz));
            placed++;
        }

        // Scatter thin grass blades across the entire arena ground
        ScatterGrassBlades(world, buildZones, halfWidth, halfDepth, groundY);
    }

    /// <summary>
    /// Scatters small grass blades across the arena ground using MultiMeshInstance3D
    /// for efficient instanced rendering. Each 2x2 microvoxel cell spawns 3-6 tiny
    /// blades with random height, rotation, lean, and green color variation.
    /// Two passes: one for short blades and one for tall blades (each gets its own
    /// MultiMesh so the box geometry matches the height band).
    /// </summary>
    private static void ScatterGrassBlades(VoxelWorld world, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones,
        int halfWidth, int halfDepth, int groundY)
    {
        float microMeters = GameConfig.MicrovoxelMeters;

        // First pass: collect all valid blade transforms + colors
        List<(Transform3D transform, Color color)> bladeInstances = new List<(Transform3D, Color)>();

        // Walk the arena in 2x2 microvoxel cells (denser grid than before)
        for (int cellZ = -halfDepth + 1; cellZ < halfDepth - 1; cellZ += 2)
        {
            for (int cellX = -halfWidth + 1; cellX < halfWidth - 1; cellX += 2)
            {
                int bladeCount = Rng.Next(3, 7); // 3-6 blades per cell
                for (int i = 0; i < bladeCount; i++)
                {
                    int gx = cellX + Rng.Next(0, 2);
                    int gz = cellZ + Rng.Next(0, 2);

                    // Clamp to arena bounds
                    if (gx <= -halfWidth || gx >= halfWidth || gz <= -halfDepth || gz >= halfDepth)
                        continue;

                    // Skip positions inside build zones (with small buffer)
                    bool inZone = false;
                    foreach (BuildZone zone in buildZones.Values)
                    {
                        Vector3I zMin = zone.OriginMicrovoxels;
                        Vector3I zMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;
                        int buffer = 2;
                        if (gx >= zMin.X - buffer && gx <= zMax.X + buffer &&
                            gz >= zMin.Z - buffer && gz <= zMax.Z + buffer)
                        {
                            inZone = true;
                            break;
                        }
                    }

                    if (inZone) continue;

                    // Compute world position for the base of this grass blade
                    float worldX = gx * microMeters + (float)(Rng.NextDouble() * microMeters * 0.9);
                    float worldZ = gz * microMeters + (float)(Rng.NextDouble() * microMeters * 0.9);
                    float worldY = groundY * microMeters;

                    float bladeHeight = (float)(BladeMinHeight + Rng.NextDouble() * (BladeMaxHeight - BladeMinHeight));

                    // Pick a random green shade
                    float colorLerp = (float)Rng.NextDouble();
                    Color bladeColor;
                    if (colorLerp < 0.33f)
                        bladeColor = GrassDarkColor;
                    else if (colorLerp < 0.66f)
                        bladeColor = GrassBaseColor;
                    else
                        bladeColor = GrassLightColor;

                    // Slight per-blade random tint
                    bladeColor = bladeColor.Lightened((float)(Rng.NextDouble() * 0.1 - 0.05));

                    // Random Y rotation
                    float rotY = (float)(Rng.NextDouble() * Mathf.Pi * 2.0);
                    // Slight lean for organic feel
                    float leanX = (float)(Rng.NextDouble() * 8.0 - 4.0);
                    float leanZ = leanX * 0.5f;

                    // Build transform: scale Y to match blade height, then rotate and translate.
                    // The MultiMesh uses a unit-height box (BladeWidth x 1.0 x BladeDepth),
                    // so we scale Y by bladeHeight.
                    Basis basis = Basis.Identity;
                    basis = basis.Scaled(new Vector3(1f, bladeHeight, 1f));
                    basis = new Basis(Vector3.Up, rotY) * basis;
                    // Apply lean as small rotations on X and Z
                    basis = new Basis(Vector3.Right, Mathf.DegToRad(leanX)) * basis;
                    basis = new Basis(Vector3.Forward, Mathf.DegToRad(leanZ)) * basis;

                    // Blade base sits on the ground; box is centered, so offset up by half the blade height
                    Vector3 pos = new Vector3(worldX, worldY + bladeHeight * 0.5f, worldZ);

                    bladeInstances.Add((new Transform3D(basis, pos), bladeColor));
                }
            }
        }

        if (bladeInstances.Count == 0) return;

        // Build MultiMesh
        MultiMesh multiMesh = new MultiMesh();
        multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        multiMesh.UseColors = true;
        multiMesh.InstanceCount = bladeInstances.Count;

        // Unit box: BladeWidth x 1.0 x BladeDepth (Y scaled per-instance via transform)
        BoxMesh bladeMesh = new BoxMesh();
        bladeMesh.Size = new Vector3(BladeWidth, 1.0f, BladeDepth);
        multiMesh.Mesh = bladeMesh;

        for (int i = 0; i < bladeInstances.Count; i++)
        {
            multiMesh.SetInstanceTransform(i, bladeInstances[i].transform);
            multiMesh.SetInstanceColor(i, bladeInstances[i].color);
        }

        // Material: use vertex color as albedo so each instance gets its own tint
        StandardMaterial3D mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = "GrassBlades";
        mmi.Multimesh = multiMesh;
        mmi.MaterialOverride = mat;

        world.AddChild(mmi);
    }


    /// <summary>
    /// Marks the borders of each build zone with small corner post voxels
    /// and subtle team-colored translucent mesh markers at the 4 corners.
    /// No full border lines -- just corner markers to keep the scene clean.
    /// </summary>
    public static void MarkBuildZoneBorders(VoxelWorld world, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones)
    {
        // Use Stone for the corner post voxels
        VoxelMaterialType postMat = VoxelMaterialType.Stone;

        int groundY = GameConfig.PrototypeGroundThickness - 1; // top of ground
        int postHeight = 4; // 4 voxels tall at each corner

        foreach ((PlayerSlot slot, BuildZone zone) in buildZones)
        {
            Vector3I zMin = zone.OriginMicrovoxels;
            Vector3I zMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;

            // Only corner posts -- no full border walls
            Vector3I[] corners =
            {
                new(zMin.X - 1, groundY, zMin.Z - 1),
                new(zMax.X, groundY, zMin.Z - 1),
                new(zMin.X - 1, groundY, zMax.Z),
                new(zMax.X, groundY, zMax.Z),
            };
            foreach (Vector3I corner in corners)
            {
                for (int h = 0; h < postHeight; h++)
                {
                    world.SetVoxel(corner + new Vector3I(0, h, 0), Voxel.Create(postMat));
                }
            }
        }

        // Create subtle colored corner markers (mesh) instead of full border strips
        CreateBuildZoneCornerMarkers(world, buildZones);
    }

    /// <summary>
    /// Creates small team-colored translucent mesh markers at the 4 corners of
    /// each build zone. Very subtle (15% opacity) to avoid washing out the scene.
    /// </summary>
    private static void CreateBuildZoneCornerMarkers(
        Node3D parent,
        IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones)
    {
        // Remove any previous border meshes
        const string groupName = "BuildZoneBorder";
        foreach (Node child in parent.GetTree().GetNodesInGroup(groupName))
        {
            child.QueueFree();
        }

        float microMeters = GameConfig.MicrovoxelMeters;

        foreach ((PlayerSlot slot, BuildZone zone) in buildZones)
        {
            int slotIndex = (int)slot;
            Color teamColor = GameConfig.PlayerColors[slotIndex % GameConfig.PlayerColors.Length];
            Color markerColor = new Color(teamColor.R, teamColor.G, teamColor.B, 0.18f);

            Vector3I zMin = zone.OriginMicrovoxels;
            Vector3I zMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;
            int groundY = GameConfig.PrototypeGroundThickness;

            // Convert microvoxel coords to world position
            Vector3 worldMin = new Vector3(zMin.X, groundY, zMin.Z) * microMeters;
            Vector3 worldMax = new Vector3(zMax.X, groundY, zMax.Z) * microMeters;

            // Corner marker size: 2 microvoxels wide/deep, 4 microvoxels tall
            float markerSize = microMeters * 2f;
            float markerHeight = microMeters * 4f;

            // 4 corner markers
            Vector3[] cornerPositions =
            {
                new Vector3(worldMin.X - markerSize, worldMin.Y, worldMin.Z - markerSize), // near-left
                new Vector3(worldMax.X, worldMin.Y, worldMin.Z - markerSize),               // near-right
                new Vector3(worldMin.X - markerSize, worldMin.Y, worldMax.Z),               // far-left
                new Vector3(worldMax.X, worldMin.Y, worldMax.Z),                             // far-right
            };
            Vector3 markerBoxSize = new Vector3(markerSize, markerHeight, markerSize);

            foreach (Vector3 cornerPos in cornerPositions)
            {
                CreateCornerMarker(parent, groupName, markerColor, cornerPos, markerBoxSize);
            }
        }
    }

    /// <summary>
    /// Creates a single small colored corner marker mesh as a child of the parent node.
    /// Slightly inset (shrunk by 0.02m per face) so that the translucent marker faces
    /// never sit on exactly the same plane as the opaque voxel corner-post faces,
    /// which would cause z-fighting / strobing.
    /// </summary>
    private static void CreateCornerMarker(Node3D parent, string groupName, Color color, Vector3 position, Vector3 size)
    {
        MeshInstance3D mesh = new();
        mesh.Name = "ZoneCorner";
        mesh.AddToGroup(groupName);

        // Shrink the marker box slightly so its faces are inset from the voxel
        // post geometry, preventing coplanar z-fighting.
        const float inset = 0.02f;
        Vector3 insetSize = size - new Vector3(inset * 2f, 0f, inset * 2f);

        BoxMesh box = new();
        box.Size = insetSize;
        mesh.Mesh = box;

        // Position at the center of the *original* footprint (BoxMesh is centered),
        // so the marker stays visually centred on the voxel post.
        mesh.Position = position + size * 0.5f;

        StandardMaterial3D mat = new();
        mat.AlbedoColor = color;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.NoDepthTest = false;
        mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always;
        mesh.MaterialOverride = mat;

        // Render above opaque geometry to reinforce draw-order separation
        mesh.SortingOffset = 0.1f;

        parent.AddChild(mesh);
    }

    private static void GenerateTree(VoxelWorld world, Vector3I basePos)
    {
        // Tree trunk: 1x1 column, 3-5 blocks tall
        int trunkHeight = Rng.Next(3, 6);
        for (int y = 0; y < trunkHeight; y++)
        {
            world.SetVoxel(basePos + new Vector3I(0, y, 0), Voxel.Create(VoxelMaterialType.Wood));
        }

        // Tree canopy: sphere-ish cluster of Leaves voxels at the top
        int canopyRadius = Rng.Next(2, 4);
        Vector3I canopyCenter = basePos + new Vector3I(0, trunkHeight, 0);
        for (int z = -canopyRadius; z <= canopyRadius; z++)
        {
            for (int y = -1; y <= canopyRadius; y++)
            {
                for (int x = -canopyRadius; x <= canopyRadius; x++)
                {
                    int distSq = x * x + y * y + z * z;
                    if (distSq <= canopyRadius * canopyRadius)
                    {
                        // Skip some voxels randomly for organic look
                        if (distSq > (canopyRadius - 1) * (canopyRadius - 1) && Rng.NextDouble() < 0.3)
                            continue;

                        world.SetVoxel(canopyCenter + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  MOUNTAIN RANGE BORDER
    // ─────────────────────────────────────────────────

    // Noise parameters for natural-looking mountain height variation
    private const int MountainMinHeight = 15;
    private const int MountainMaxHeight = 40;
    private const int MountainBorderWidth = 30;  // how many microvoxels deep the mountain ring is
    public const int MountainStartOffset = 20;   // gap of flat ground between arena edge and mountain start

    /// <summary>
    /// Generates a procedural mountain range border around the outside perimeter
    /// of the arena. Mountains use Stone for the core and Dirt/Grass on top for a
    /// natural look. Height is varied using a simple hash-based noise function to
    /// create organic-looking peaks and valleys.
    /// </summary>
    public static void GenerateMountainBorder(VoxelWorld world)
    {
        int halfWidth = GameConfig.PrototypeArenaWidth / 2;
        int groundY = GameConfig.PrototypeGroundThickness;

        // The mountains start well outside the arena ground, with a wide flat gap
        // before the foothills begin. This keeps the playable area feeling open.
        // Arena ground spans from -halfWidth to +halfWidth on X and Z.
        // Mountains form a rectangular ring outside the flat buffer area.
        int innerEdge = halfWidth + MountainStartOffset;  // where mountains begin
        int outerEdge = innerEdge + MountainBorderWidth;  // where mountains end

        // Generate all four sides of the ring plus corners
        // We iterate over the full ring perimeter and for each column compute a height.
        for (int x = -outerEdge; x <= outerEdge; x++)
        {
            for (int z = -outerEdge; z <= outerEdge; z++)
            {
                // Skip the interior (only fill the ring)
                if (x > -innerEdge && x < innerEdge && z > -innerEdge && z < innerEdge)
                    continue;

                // Compute distance from the inner edge for height falloff
                int distFromInner = DistanceFromInnerEdge(x, z, innerEdge);

                // heightFraction goes from 0 at the inner edge to 1 at the outer edge
                float heightFraction = (float)distFromInner / MountainBorderWidth;

                // Use a smooth squared curve so mountains start at ground level (height=0)
                // at the inner edge and GRADUALLY rise. Foothills are gentle, peaks are
                // toward the outer edge. No steep wall effect.
                float rampUp = heightFraction * heightFraction;

                // Multi-octave noise for natural-looking peaks and ridges
                float noiseVal = MultiOctaveNoise(x, z);

                // Combine ramp with noise. At the inner edge (rampUp~0) height is near 0.
                // At the outer edge (rampUp~1) height reaches full mountain peaks.
                int columnHeight = (int)(MountainMaxHeight * rampUp * noiseVal);

                // Clamp to valid range but allow 0 at the inner edge so mountains
                // start flush with the flat arena ground (no step/lip).
                columnHeight = Math.Clamp(columnHeight, 0, MountainMaxHeight);

                // Also fill the ground layer under the mountains (Foundation + Dirt top)
                // This ensures continuous ground even for 0-height mountain columns.
                for (int y = 0; y < groundY - 1; y++)
                {
                    world.SetVoxel(new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Foundation));
                }
                // Top ground layer is Dirt (grass) to match the arena floor
                world.SetVoxel(new Vector3I(x, groundY - 1, z), Voxel.Create(VoxelMaterialType.Dirt));

                // Place mountain voxels for this column from ground level up
                for (int y = 0; y < columnHeight; y++)
                {
                    VoxelMaterialType mat;
                    if (y >= columnHeight - 1)
                    {
                        // Top layer: Dirt (green grass look)
                        mat = VoxelMaterialType.Dirt;
                    }
                    else if (y >= columnHeight - 3)
                    {
                        // Near-surface layer: mix of Dirt and Stone
                        mat = HashNoise(x + y * 17, z + y * 31) > 0.5f
                            ? VoxelMaterialType.Dirt
                            : VoxelMaterialType.Stone;
                    }
                    else
                    {
                        // Core: Stone
                        mat = VoxelMaterialType.Stone;
                    }

                    world.SetVoxel(new Vector3I(x, groundY + y, z), Voxel.Create(mat));
                }
            }
        }

        // Add some rocky outcrops / scattered boulders along the inner edge
        // for a natural transition between flat arena and mountains
        GenerateMountainFoothills(world, innerEdge, groundY);
    }

    /// <summary>
    /// Computes the minimum distance from a point to the inner edge of the mountain ring.
    /// </summary>
    private static int DistanceFromInnerEdge(int x, int z, int innerEdge)
    {
        int dx = 0;
        int dz = 0;

        if (x <= -innerEdge)
            dx = -innerEdge - x;
        else if (x >= innerEdge)
            dx = x - innerEdge;

        if (z <= -innerEdge)
            dz = -innerEdge - z;
        else if (z >= innerEdge)
            dz = z - innerEdge;

        return Math.Max(dx, dz);
    }

    /// <summary>
    /// Simple hash-based noise function returning a value in [0.5, 1.0].
    /// Produces deterministic but varied height values for mountain peaks.
    /// </summary>
    private static float HashNoise(int x, int z)
    {
        // Use multiple frequency layers for more natural variation
        float coarse = HashSingle(x / 7, z / 7);
        float medium = HashSingle(x / 3, z / 3);
        float fine = HashSingle(x, z);

        // Blend frequencies: coarse dominates for big peaks, fine adds detail
        float combined = coarse * 0.6f + medium * 0.25f + fine * 0.15f;
        return 0.5f + combined * 0.5f; // remap to [0.5, 1.0]
    }

    /// <summary>
    /// Multi-octave noise returning a value in [0.4, 1.0] with richer variation.
    /// Uses 4 octaves at different frequencies to create natural mountain ridges
    /// with large-scale peaks and fine-grained rocky detail.
    /// </summary>
    private static float MultiOctaveNoise(int x, int z)
    {
        // 4 octaves: very coarse ridgelines, coarse peaks, medium bumps, fine detail
        float veryCoarse = HashSingle(x / 12, z / 12);
        float coarse = HashSingle(x / 7, z / 7);
        float medium = HashSingle(x / 3, z / 3);
        float fine = HashSingle(x, z);

        // Weighted blend: very coarse creates broad ridges, fine adds craggy detail
        float combined = veryCoarse * 0.40f + coarse * 0.30f + medium * 0.20f + fine * 0.10f;
        return 0.4f + combined * 0.6f; // remap to [0.4, 1.0]
    }

    /// <summary>
    /// Single-frequency hash returning [0, 1].
    /// </summary>
    private static float HashSingle(int x, int z)
    {
        // Robert Jenkins' 32-bit integer hash
        int h = x * 374761393 + z * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
    }

    /// <summary>
    /// Scatters small rocky outcrops and boulders along the inner edge of the
    /// mountain ring to create a natural transition from flat arena to mountains.
    /// </summary>
    private static void GenerateMountainFoothills(VoxelWorld world, int innerEdge, int groundY)
    {
        Random foothillRng = new Random(7777); // fixed seed for consistency

        // Walk around the inner perimeter and occasionally place small rock clusters
        for (int side = 0; side < 4; side++)
        {
            int count = foothillRng.Next(8, 14); // 8-13 outcrops per side
            for (int i = 0; i < count; i++)
            {
                int along = foothillRng.Next(-innerEdge + 4, innerEdge - 4); // position along the side
                int inward = foothillRng.Next(-3, 1); // slightly inside or at the edge

                int bx, bz;
                switch (side)
                {
                    case 0: // North edge (-Z)
                        bx = along;
                        bz = -innerEdge + inward;
                        break;
                    case 1: // South edge (+Z)
                        bx = along;
                        bz = innerEdge - inward;
                        break;
                    case 2: // West edge (-X)
                        bx = -innerEdge + inward;
                        bz = along;
                        break;
                    default: // East edge (+X)
                        bx = innerEdge - inward;
                        bz = along;
                        break;
                }

                // Small boulder: 1-3 voxels tall, 1-2 wide
                int boulderH = foothillRng.Next(1, 4);
                int boulderW = foothillRng.Next(1, 3);
                for (int dy = 0; dy < boulderH; dy++)
                {
                    for (int dx = 0; dx < boulderW; dx++)
                    {
                        for (int dz = 0; dz < boulderW; dz++)
                        {
                            // Taper the boulder (smaller at top)
                            if (dy > 0 && (dx == boulderW - 1 || dz == boulderW - 1) && foothillRng.NextDouble() < 0.4)
                                continue;

                            VoxelMaterialType mat = dy == boulderH - 1
                                ? VoxelMaterialType.Dirt
                                : VoxelMaterialType.Stone;

                            world.SetVoxel(
                                new Vector3I(bx + dx, groundY + dy, bz + dz),
                                Voxel.Create(mat));
                        }
                    }
                }
            }
        }
    }
}
