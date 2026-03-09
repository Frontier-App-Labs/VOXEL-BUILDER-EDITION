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
/// Tracks blade positions so grass can be removed when terrain is destroyed.
/// </summary>
public static class TerrainDecorator
{
    private static readonly Random Rng = new Random(42);

    // Grass instance tracking for dynamic removal
    private static MultiMesh? _grassMultiMesh;
    private static readonly Dictionary<long, List<int>> _grassPositionMap = new();

    /// <summary>
    /// Encodes microvoxel X,Z into a single long key for fast lookup.
    /// </summary>
    private static long GrassKey(int x, int z) => ((long)x << 32) | (uint)z;

    // Grass blade dimensions — small blades for a dense carpet look
    private const float BladeWidth = 0.03f;
    private const float BladeMinHeight = 0.08f;
    private const float BladeMaxHeight = 0.18f;
    private const float BladeDepth = 0.03f;

    // Grass color variation
    private static readonly Color GrassBaseColor = new Color(0.18f, 0.42f, 0.12f);
    private static readonly Color GrassDarkColor = new Color(0.12f, 0.30f, 0.08f);
    private static readonly Color GrassLightColor = new Color(0.25f, 0.50f, 0.16f);

    /// <summary>
    /// Removes grass blades at the given microvoxel positions.
    /// Called when terrain is destroyed by explosions so grass doesn't float in the air.
    /// </summary>
    public static void RemoveGrassAt(IEnumerable<Vector3I> destroyedPositions)
    {
        if (_grassMultiMesh == null) return;

        // Zero-scale transform to hide instances (can't remove from MultiMesh, but scale=0 hides them)
        Transform3D hidden = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), new Vector3(0, -1000, 0));

        foreach (Vector3I pos in destroyedPositions)
        {
            long key = GrassKey(pos.X, pos.Z);
            if (_grassPositionMap.TryGetValue(key, out var instanceIndices))
            {
                foreach (int idx in instanceIndices)
                {
                    _grassMultiMesh.SetInstanceTransform(idx, hidden);
                }
                _grassPositionMap.Remove(key);
            }
        }
    }

    /// <summary>
    /// Removes grass blades within a world-space radius of the given position.
    /// More aggressive than position-based removal — catches nearby blades too.
    /// </summary>
    public static void RemoveGrassInRadius(Vector3 worldCenter, float radiusMeters)
    {
        if (_grassMultiMesh == null) return;

        float microMeters = GameConfig.MicrovoxelMeters;
        int radiusMicro = Mathf.CeilToInt(radiusMeters / microMeters) + 1;
        Vector3I center = new Vector3I(
            Mathf.RoundToInt(worldCenter.X / microMeters),
            0,
            Mathf.RoundToInt(worldCenter.Z / microMeters));

        Transform3D hidden = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), new Vector3(0, -1000, 0));

        for (int dx = -radiusMicro; dx <= radiusMicro; dx++)
        {
            for (int dz = -radiusMicro; dz <= radiusMicro; dz++)
            {
                long key = GrassKey(center.X + dx, center.Z + dz);
                if (_grassPositionMap.TryGetValue(key, out var instanceIndices))
                {
                    foreach (int idx in instanceIndices)
                    {
                        _grassMultiMesh.SetInstanceTransform(idx, hidden);
                    }
                    _grassPositionMap.Remove(key);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  TREE VARIETY SYSTEM
    // ─────────────────────────────────────────────────

    /// <summary>Tree size categories for varied forest generation.</summary>
    private enum TreeType
    {
        SmallBush,      // 3-5 blocks tall, thin trunk, tiny canopy
        MediumTree,     // 6-8 blocks tall, standard canopy
        LargeTree,      // 10-14 blocks tall, wide canopy — strategic obstacles
        TallPine,       // 12-18 blocks tall, narrow/conical — very tall, hard to shoot over
        GiantOldGrowth, // 8-12 blocks tall but very wide trunk and massive canopy
    }

    /// <summary>Minimum spacing (in microvoxels) between tree trunks to avoid overlap.</summary>
    private const int TreeMinSpacing = 8;

    /// <summary>Target tree density: roughly 1 tree per this many square microvoxels.</summary>
    private const int TreeDensityAreaPerTree = 200;

    /// <summary>
    /// Places trees and vegetation around the arena, avoiding build zones.
    /// Trees spawn across the entire map (flat arena + mountains) wherever
    /// there isn't a build zone. Multiple tree size variants create strategic
    /// variety — small bushes, standard trees, tall pines, and massive
    /// old-growth trees that players must destroy or navigate around.
    /// Also scatters thin grass blades across the entire arena ground.
    /// </summary>
    public static void DecorateArena(VoxelWorld world, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones)
    {
        int halfWidth = GameConfig.PrototypeArenaWidth / 2;
        int halfDepth = GameConfig.PrototypeArenaDepth / 2;
        int groundY = GameConfig.PrototypeGroundThickness; // first y above ground

        // Use a dedicated seeded random for tree placement (reproducible forests)
        Random treeRng = new Random(12345);

        // The spawnable area covers the arena AND the mountain border
        int innerEdge = halfWidth + MountainStartOffset;
        int outerEdge = innerEdge + MountainBorderWidth;

        // Estimate target tree count from total spawnable area
        int totalArea = (outerEdge * 2) * (outerEdge * 2);
        int targetTrees = totalArea / TreeDensityAreaPerTree;

        // Track placed tree positions for spacing checks
        List<Vector3I> placedPositions = new List<Vector3I>(targetTrees);

        int placed = 0;
        int maxAttempts = targetTrees * 8;
        int attempts = 0;

        while (placed < targetTrees && attempts < maxAttempts)
        {
            attempts++;

            int tx = treeRng.Next(-outerEdge + 3, outerEdge - 3);
            int tz = treeRng.Next(-outerEdge + 3, outerEdge - 3);

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

            // Check minimum spacing against already-placed trees
            bool tooClose = false;
            for (int i = 0; i < placedPositions.Count; i++)
            {
                int dx = placedPositions[i].X - tx;
                int dz = placedPositions[i].Z - tz;
                if (dx * dx + dz * dz < TreeMinSpacing * TreeMinSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Determine terrain height at this position (flat arena vs mountain)
            bool onMountain = tx <= -innerEdge || tx >= innerEdge ||
                              tz <= -innerEdge || tz >= innerEdge;
            int terrainY = groundY;
            if (onMountain)
            {
                int distFromInner = DistanceFromInnerEdge(tx, tz, innerEdge);
                float heightFraction = (float)distFromInner / MountainBorderWidth;
                float rampUp = heightFraction * heightFraction;
                float noiseVal = MultiOctaveNoise(tx, tz);
                int columnHeight = (int)(MountainMaxHeight * rampUp * noiseVal);
                columnHeight = Math.Clamp(columnHeight, 0, MountainMaxHeight);
                terrainY = groundY + columnHeight;

                // Skip very steep mountain peaks (near the outer edge) — no trees there
                if (distFromInner > MountainBorderWidth - 4)
                    continue;
            }

            // Choose tree type based on weighted random distribution.
            // Mountains get more pines; flat terrain gets more variety.
            TreeType type = PickTreeType(treeRng, onMountain);

            Vector3I basePos = new Vector3I(tx, terrainY, tz);
            GenerateTreeVariant(world, basePos, type, treeRng);
            placedPositions.Add(new Vector3I(tx, 0, tz)); // Y=0 for 2D spacing check
            placed++;
        }

        // Scatter thin grass blades across the entire arena ground
        ScatterGrassBlades(world, buildZones, halfWidth, halfDepth, groundY);
    }

    /// <summary>
    /// Picks a tree type using weighted random selection.
    /// Mountain positions favour pines; flat terrain has more variety.
    /// </summary>
    private static TreeType PickTreeType(Random rng, bool onMountain)
    {
        int roll = rng.Next(100);
        if (onMountain)
        {
            // Mountains: more pines and small bushes, fewer giants
            if (roll < 20) return TreeType.SmallBush;
            if (roll < 40) return TreeType.MediumTree;
            if (roll < 55) return TreeType.LargeTree;
            if (roll < 85) return TreeType.TallPine;
            return TreeType.GiantOldGrowth;
        }
        else
        {
            // Flat arena: good mix of all sizes
            if (roll < 25) return TreeType.SmallBush;
            if (roll < 50) return TreeType.MediumTree;
            if (roll < 70) return TreeType.LargeTree;
            if (roll < 85) return TreeType.TallPine;
            return TreeType.GiantOldGrowth;
        }
    }

    /// <summary>
    /// Generates a tree of the specified type at the given base position.
    /// All trees are built from Wood (trunk) and Leaves (canopy) voxels,
    /// making them fully destructible.
    /// </summary>
    private static void GenerateTreeVariant(VoxelWorld world, Vector3I basePos, TreeType type, Random rng)
    {
        switch (type)
        {
            case TreeType.SmallBush:
                GenerateSmallBush(world, basePos, rng);
                break;
            case TreeType.MediumTree:
                GenerateMediumTree(world, basePos, rng);
                break;
            case TreeType.LargeTree:
                GenerateLargeTree(world, basePos, rng);
                break;
            case TreeType.TallPine:
                GenerateTallPine(world, basePos, rng);
                break;
            case TreeType.GiantOldGrowth:
                GenerateGiantOldGrowth(world, basePos, rng);
                break;
        }
    }

    /// <summary>
    /// Small bush/sapling: 3-5 blocks tall, 1-wide trunk, small spherical canopy (radius 1-2).
    /// Provides minimal cover; easy to destroy.
    /// </summary>
    private static void GenerateSmallBush(VoxelWorld world, Vector3I basePos, Random rng)
    {
        int trunkHeight = rng.Next(3, 6); // 3-5
        for (int y = 0; y < trunkHeight; y++)
        {
            world.SetVoxel(basePos + new Vector3I(0, y, 0), Voxel.Create(VoxelMaterialType.Wood));
        }

        int canopyRadius = rng.Next(1, 3); // 1-2
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
                        if (distSq > (canopyRadius - 1) * (canopyRadius - 1) && rng.NextDouble() < 0.35)
                            continue;
                        world.SetVoxel(canopyCenter + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Medium tree: 6-8 blocks tall, 1-wide trunk, spherical canopy (radius 2-3).
    /// Standard obstacle; provides moderate cover.
    /// </summary>
    private static void GenerateMediumTree(VoxelWorld world, Vector3I basePos, Random rng)
    {
        int trunkHeight = rng.Next(6, 9); // 6-8
        for (int y = 0; y < trunkHeight; y++)
        {
            world.SetVoxel(basePos + new Vector3I(0, y, 0), Voxel.Create(VoxelMaterialType.Wood));
        }

        int canopyRadius = rng.Next(2, 4); // 2-3
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
                        if (distSq > (canopyRadius - 1) * (canopyRadius - 1) && rng.NextDouble() < 0.3)
                            continue;
                        world.SetVoxel(canopyCenter + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Large tree: 10-14 blocks tall, 2x2 trunk, wide spherical canopy (radius 4-5).
    /// Strategic obstacle — significant cover, requires effort to destroy.
    /// </summary>
    private static void GenerateLargeTree(VoxelWorld world, Vector3I basePos, Random rng)
    {
        int trunkHeight = rng.Next(10, 15); // 10-14
        for (int y = 0; y < trunkHeight; y++)
        {
            // 2x2 trunk
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 1; dz++)
                {
                    world.SetVoxel(basePos + new Vector3I(dx, y, dz), Voxel.Create(VoxelMaterialType.Wood));
                }
            }
        }

        int canopyRadius = rng.Next(4, 6); // 4-5
        Vector3I canopyCenter = basePos + new Vector3I(0, trunkHeight + 1, 0);
        for (int z = -canopyRadius; z <= canopyRadius; z++)
        {
            for (int y = -2; y <= canopyRadius; y++)
            {
                for (int x = -canopyRadius; x <= canopyRadius; x++)
                {
                    int distSq = x * x + y * y + z * z;
                    if (distSq <= canopyRadius * canopyRadius)
                    {
                        if (distSq > (canopyRadius - 1) * (canopyRadius - 1) && rng.NextDouble() < 0.3)
                            continue;
                        world.SetVoxel(canopyCenter + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tall pine: 12-18 blocks tall, 1-wide trunk, conical canopy that tapers from
    /// radius 4 at the base to radius 1 at the top. Very tall — hard to shoot over,
    /// blocks line of sight. The cone shape makes them visually distinct from round trees.
    /// </summary>
    private static void GenerateTallPine(VoxelWorld world, Vector3I basePos, Random rng)
    {
        int trunkHeight = rng.Next(12, 19); // 12-18
        for (int y = 0; y < trunkHeight; y++)
        {
            world.SetVoxel(basePos + new Vector3I(0, y, 0), Voxel.Create(VoxelMaterialType.Wood));
        }

        // Conical canopy: starts partway up the trunk
        int canopyStart = trunkHeight / 3;       // lower third is bare trunk
        int canopyLayers = trunkHeight - canopyStart;
        int maxRadius = rng.Next(3, 5);          // 3-4 at widest

        for (int layer = 0; layer < canopyLayers; layer++)
        {
            int y = canopyStart + layer;
            // Radius tapers linearly from maxRadius at the bottom to 1 at the top
            float t = (float)layer / Math.Max(canopyLayers - 1, 1);
            int layerRadius = Math.Max(1, (int)(maxRadius * (1f - t * 0.8f)));

            for (int z = -layerRadius; z <= layerRadius; z++)
            {
                for (int x = -layerRadius; x <= layerRadius; x++)
                {
                    int distSq = x * x + z * z;
                    if (distSq <= layerRadius * layerRadius)
                    {
                        // Rough up the edges for organic look
                        if (distSq > (layerRadius - 1) * (layerRadius - 1) && rng.NextDouble() < 0.35)
                            continue;
                        world.SetVoxel(basePos + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }

        // Small pointed tip at the very top
        world.SetVoxel(basePos + new Vector3I(0, trunkHeight, 0), Voxel.Create(VoxelMaterialType.Leaves));
        if (trunkHeight > 14)
        {
            world.SetVoxel(basePos + new Vector3I(0, trunkHeight + 1, 0), Voxel.Create(VoxelMaterialType.Leaves));
        }
    }

    /// <summary>
    /// Giant old-growth: 8-12 blocks tall but with a massive 3x3 trunk and enormous
    /// spherical canopy (radius 5-6). These are wide, sturdy trees that act as
    /// significant terrain features — great cover but expensive to destroy.
    /// </summary>
    private static void GenerateGiantOldGrowth(VoxelWorld world, Vector3I basePos, Random rng)
    {
        int trunkHeight = rng.Next(8, 13); // 8-12
        for (int y = 0; y < trunkHeight; y++)
        {
            // 3x3 trunk (offset by -1 so it's centered)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    // Slightly round the corners of the trunk
                    if (Math.Abs(dx) == 1 && Math.Abs(dz) == 1 && y > trunkHeight - 2)
                        continue;
                    world.SetVoxel(basePos + new Vector3I(dx, y, dz), Voxel.Create(VoxelMaterialType.Wood));
                }
            }
        }

        // Root buttresses: extend trunk outward at the base for massive look
        for (int dy = 0; dy < Math.Min(3, trunkHeight); dy++)
        {
            int rootSpread = 2 - dy; // wider at ground level
            if (rootSpread <= 0) break;
            for (int dx = -rootSpread - 1; dx <= rootSpread + 1; dx++)
            {
                for (int dz = -rootSpread - 1; dz <= rootSpread + 1; dz++)
                {
                    // Only the cardinal extensions (not diagonals past the trunk)
                    if (Math.Abs(dx) > 1 && Math.Abs(dz) > 1)
                        continue;
                    world.SetVoxel(basePos + new Vector3I(dx, dy, dz), Voxel.Create(VoxelMaterialType.Wood));
                }
            }
        }

        int canopyRadius = rng.Next(5, 7); // 5-6
        Vector3I canopyCenter = basePos + new Vector3I(0, trunkHeight + 1, 0);
        for (int z = -canopyRadius; z <= canopyRadius; z++)
        {
            for (int y = -2; y <= canopyRadius; y++)
            {
                for (int x = -canopyRadius; x <= canopyRadius; x++)
                {
                    int distSq = x * x + y * y + z * z;
                    if (distSq <= canopyRadius * canopyRadius)
                    {
                        // More aggressive edge removal for a rough, organic canopy
                        if (distSq > (canopyRadius - 1) * (canopyRadius - 1) && rng.NextDouble() < 0.35)
                            continue;
                        if (distSq > (canopyRadius - 2) * (canopyRadius - 2) && rng.NextDouble() < 0.15)
                            continue;
                        world.SetVoxel(canopyCenter + new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Leaves));
                    }
                }
            }
        }
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

        // Mountain range extents — grass covers the arena AND mountains
        int innerEdge = halfWidth + MountainStartOffset;
        int outerEdge = innerEdge + MountainBorderWidth;

        // Clear previous grass tracking data
        _grassPositionMap.Clear();
        _grassMultiMesh = null;

        // First pass: collect all valid blade transforms + colors + their microvoxel positions
        List<(Transform3D transform, Color color, int gx, int gz)> bladeInstances = new List<(Transform3D, Color, int, int)>();

        // Walk the full terrain (arena + mountains) in 2x2 microvoxel cells
        for (int cellZ = -outerEdge + 1; cellZ < outerEdge - 1; cellZ += 2)
        {
            for (int cellX = -outerEdge + 1; cellX < outerEdge - 1; cellX += 2)
            {
                // Determine if this cell is on mountain terrain
                bool onMountain = cellX <= -innerEdge || cellX >= innerEdge ||
                                  cellZ <= -innerEdge || cellZ >= innerEdge;

                // Fewer blades on mountains, denser on flat arena
                int bladeCount = onMountain ? Rng.Next(2, 5) : Rng.Next(3, 7);
                for (int i = 0; i < bladeCount; i++)
                {
                    int gx = cellX + Rng.Next(0, 2);
                    int gz = cellZ + Rng.Next(0, 2);

                    // Skip positions beyond the outer mountain edge (no ground there)
                    if (gx <= -outerEdge || gx >= outerEdge || gz <= -outerEdge || gz >= outerEdge)
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
                    float worldY;

                    // Check if this blade position is on the mountain ring
                    bool bladeOnMountain = gx <= -innerEdge || gx >= innerEdge ||
                                           gz <= -innerEdge || gz >= innerEdge;
                    if (bladeOnMountain)
                    {
                        // Place grass on top of the mountain surface
                        int distFromInner = DistanceFromInnerEdge(gx, gz, innerEdge);
                        float heightFraction = (float)distFromInner / MountainBorderWidth;
                        float rampUp = heightFraction * heightFraction;
                        float noiseVal = MultiOctaveNoise(gx, gz);
                        int columnHeight = (int)(MountainMaxHeight * rampUp * noiseVal);
                        columnHeight = Math.Clamp(columnHeight, 0, MountainMaxHeight);
                        worldY = (groundY + columnHeight) * microMeters;
                    }
                    else
                    {
                        worldY = groundY * microMeters;
                    }

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

                    bladeInstances.Add((new Transform3D(basis, pos), bladeColor, gx, gz));
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

            // Track which instance index belongs to which microvoxel cell
            long key = GrassKey(bladeInstances[i].gx, bladeInstances[i].gz);
            if (!_grassPositionMap.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                _grassPositionMap[key] = list;
            }
            list.Add(i);
        }

        _grassMultiMesh = multiMesh;

        // Material: ShaderMaterial with inline wind-sway vertex shader.
        // Uses the same global wind parameters (wind_time, wind_strength, wind_direction)
        // that WindAnimator updates each frame for tree sway, so grass moves in sync
        // with the trees. Vertex color is used for per-instance albedo tint.
        Shader grassShader = new Shader();
        grassShader.Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled;

// Read the same global wind parameters that WindAnimator drives for trees.
global uniform float wind_time;
global uniform float wind_strength;
global uniform vec2 wind_direction;

void vertex() {
    // World-space position of this vertex (via MODEL_MATRIX)
    vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // Sway only the upper portion of each blade — vertices with positive local Y.
    // The sway amount scales linearly with local Y so the base stays anchored.
    float sway_factor = max(VERTEX.y, 0.0);

    // Organic sway using sin waves offset by world position.
    // Slightly different frequencies on X vs Z so it doesn't look mechanical.
    float freq_x = 3.7;
    float freq_z = 4.3;
    float sway_x = sin(wind_time * 1.0 + world_pos.x * freq_x + world_pos.z * 2.1) * 0.5
                  + sin(wind_time * 1.7 + world_pos.x * 1.3 + world_pos.z * 3.9) * 0.3;
    float sway_z = sin(wind_time * 1.1 + world_pos.z * freq_z + world_pos.x * 2.5) * 0.5
                  + sin(wind_time * 1.9 + world_pos.z * 1.5 + world_pos.x * 3.3) * 0.3;

    // Blend sway direction with the global wind direction for coherent motion.
    // wind_strength keeps the displacement subtle (default ~0.15).
    float displacement = wind_strength * sway_factor * 0.15;
    VERTEX.x += (sway_x * wind_direction.x + sway_z * 0.3) * displacement;
    VERTEX.z += (sway_z * wind_direction.y + sway_x * 0.3) * displacement;
}

void fragment() {
    ALBEDO = COLOR.rgb;
}
";
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = grassShader;

        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = "GrassBlades";
        mmi.Multimesh = multiMesh;
        mmi.MaterialOverride = mat;

        world.AddChild(mmi);
    }


    // ─────────────────────────────────────────────────
    //  FLAG MARKERS — team-colored flags at build zone corners
    // ─────────────────────────────────────────────────

    // Flag dimensions (metres)
    private const float FlagPoleHeight = 2.5f;      // total pole height
    private const float FlagPoleRadius = 0.04f;      // pole thickness (half-width of square pole)
    private const float FlagClothWidth = 0.8f;       // cloth extends horizontally from the pole
    private const float FlagClothHeight = 0.5f;      // cloth height
    private const int   FlagClothSegments = 6;       // horizontal subdivisions for wave animation

    // Cached flag cloth shader (shared by all flag instances)
    private static ShaderMaterial? _flagClothShaderMaterial;

    /// <summary>
    /// Marks the borders of each build zone with small team-colored flags
    /// at the 4 corners. Each flag has a thin vertical pole and a cloth
    /// piece that ripples in the wind using the global wind parameters.
    /// </summary>
    public static void MarkBuildZoneBorders(VoxelWorld world, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones)
    {
        CreateBuildZoneCornerFlags(world, buildZones);
    }

    /// <summary>
    /// Creates team-colored flag markers at the 4 corners of each build zone.
    /// Replaces the old translucent pillar markers with 3D modeled flags.
    /// </summary>
    private static void CreateBuildZoneCornerFlags(
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

            Vector3I zMin = zone.OriginMicrovoxels;
            Vector3I zMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;
            int groundY = GameConfig.PrototypeGroundThickness;

            // Convert microvoxel coords to world position
            Vector3 worldMin = new Vector3(zMin.X, groundY, zMin.Z) * microMeters;
            Vector3 worldMax = new Vector3(zMax.X, groundY, zMax.Z) * microMeters;

            // Place flags slightly outside each corner of the build zone
            float offset = microMeters * 1.5f;
            Vector3[] cornerPositions =
            {
                new Vector3(worldMin.X - offset, worldMin.Y, worldMin.Z - offset), // near-left
                new Vector3(worldMax.X + offset, worldMin.Y, worldMin.Z - offset), // near-right
                new Vector3(worldMin.X - offset, worldMin.Y, worldMax.Z + offset), // far-left
                new Vector3(worldMax.X + offset, worldMin.Y, worldMax.Z + offset), // far-right
            };

            foreach (Vector3 cornerPos in cornerPositions)
            {
                CreateFlag(parent, groupName, teamColor, cornerPos);
            }
        }
    }

    /// <summary>
    /// Creates a single flag at the given world position: a thin vertical pole
    /// topped by a rectangular cloth mesh that ripples in the wind.
    /// </summary>
    private static void CreateFlag(Node3D parent, string groupName, Color teamColor, Vector3 basePosition)
    {
        // --- Container node for the whole flag ---
        Node3D flagRoot = new();
        flagRoot.Name = "TeamFlag";
        flagRoot.AddToGroup(groupName);
        flagRoot.Position = basePosition;

        // --- Pole: thin dark-brown vertical box ---
        MeshInstance3D pole = new();
        pole.Name = "FlagPole";

        BoxMesh poleBox = new();
        float poleWidth = FlagPoleRadius * 2f;
        poleBox.Size = new Vector3(poleWidth, FlagPoleHeight, poleWidth);
        pole.Mesh = poleBox;

        // Pole is centered on Y, so offset up by half height so base sits on ground
        pole.Position = new Vector3(0f, FlagPoleHeight * 0.5f, 0f);

        StandardMaterial3D poleMat = new();
        poleMat.AlbedoColor = new Color(0.25f, 0.18f, 0.10f); // dark wood/brown
        poleMat.Roughness = 0.9f;
        poleMat.Metallic = 0.0f;
        pole.MaterialOverride = poleMat;

        flagRoot.AddChild(pole);

        // --- Pole cap: small sphere at the very top ---
        MeshInstance3D cap = new();
        cap.Name = "PoleCap";
        SphereMesh capSphere = new();
        capSphere.Radius = FlagPoleRadius * 1.8f;
        capSphere.Height = FlagPoleRadius * 3.6f;
        capSphere.RadialSegments = 6;
        capSphere.Rings = 3;
        cap.Mesh = capSphere;
        cap.Position = new Vector3(0f, FlagPoleHeight + FlagPoleRadius * 0.5f, 0f);

        StandardMaterial3D capMat = new();
        capMat.AlbedoColor = new Color(0.55f, 0.50f, 0.42f); // light grey/metal
        capMat.Roughness = 0.5f;
        capMat.Metallic = 0.4f;
        cap.MaterialOverride = capMat;

        flagRoot.AddChild(cap);

        // --- Cloth: multi-segment quad mesh attached near the top of the pole ---
        MeshInstance3D cloth = new();
        cloth.Name = "FlagCloth";

        ArrayMesh clothMesh = BuildFlagClothMesh(teamColor);
        cloth.Mesh = clothMesh;

        // Position the cloth so it hangs from just below the pole top.
        // The cloth mesh is built with the attachment edge at X=0, extending in +X.
        // Y=0 in the mesh corresponds to the top of the cloth.
        cloth.Position = new Vector3(FlagPoleRadius, FlagPoleHeight - 0.05f, 0f);

        cloth.MaterialOverride = GetFlagClothShaderMaterial(teamColor);

        flagRoot.AddChild(cloth);

        parent.AddChild(flagRoot);
    }

    /// <summary>
    /// Builds a subdivided quad mesh for the flag cloth.
    /// The mesh extends in +X from the pole (width = FlagClothWidth) and
    /// hangs downward in -Y (height = FlagClothHeight).
    /// Multiple horizontal segments allow the vertex shader to create a
    /// convincing ripple/wave effect. Vertex colors carry the team color.
    /// UV.x encodes horizontal position (0 at pole, 1 at free edge) which
    /// the shader uses to scale the wave amplitude.
    /// </summary>
    private static ArrayMesh BuildFlagClothMesh(Color teamColor)
    {
        int cols = FlagClothSegments;       // horizontal subdivisions
        int rows = 3;                        // vertical subdivisions (top, mid, bottom)

        int vertCount = (cols + 1) * (rows + 1);
        int triCount = cols * rows * 2;

        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Color[] colors = new Color[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] indices = new int[triCount * 3];

        float dx = FlagClothWidth / cols;
        float dy = FlagClothHeight / rows;

        // Slightly darken the trailing edge for visual depth
        Color edgeColor = teamColor.Darkened(0.15f);

        int vi = 0;
        for (int r = 0; r <= rows; r++)
        {
            for (int c = 0; c <= cols; c++)
            {
                float u = (float)c / cols;
                float v = (float)r / rows;

                vertices[vi] = new Vector3(c * dx, -r * dy, 0f);
                normals[vi] = Vector3.Back; // face outward in +Z initially
                uvs[vi] = new Vector2(u, v);
                colors[vi] = teamColor.Lerp(edgeColor, u);
                vi++;
            }
        }

        // Build triangle indices (two triangles per cell)
        int ii = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int topLeft = r * (cols + 1) + c;
                int topRight = topLeft + 1;
                int botLeft = topLeft + (cols + 1);
                int botRight = botLeft + 1;

                // Triangle 1
                indices[ii++] = topLeft;
                indices[ii++] = botLeft;
                indices[ii++] = topRight;

                // Triangle 2
                indices[ii++] = topRight;
                indices[ii++] = botLeft;
                indices[ii++] = botRight;
            }
        }

        ArrayMesh mesh = new();
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Returns a ShaderMaterial for the flag cloth that uses the global wind
    /// parameters (wind_time, wind_strength, wind_direction) driven by WindAnimator
    /// to create a rippling wave effect. UV.x controls how much each vertex
    /// displaces — the pole-side edge stays fixed while the free edge waves most.
    /// Each flag instance gets its own material clone so team colors are independent.
    /// </summary>
    private static ShaderMaterial GetFlagClothShaderMaterial(Color teamColor)
    {
        // Build the base shader once, then clone per-flag for unique team color
        if (_flagClothShaderMaterial == null)
        {
            Shader flagShader = new Shader();
            flagShader.Code = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);

// Same global wind parameters that WindAnimator drives for trees and grass.
global uniform float wind_time;
global uniform float wind_strength;
global uniform vec2 wind_direction;

void vertex() {
    // UV.x goes from 0 (attached to pole) to 1 (free edge).
    // Wave amplitude increases with distance from the pole.
    float wave_factor = UV.x * UV.x;

    // World position for spatial variation so nearby flags don't wave identically.
    vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // Primary wave — large slow ripple
    float wave1 = sin(wind_time * 2.5 + UV.x * 6.0 + world_pos.x * 1.3) * 0.35;
    // Secondary wave — faster, smaller, for cloth texture
    float wave2 = sin(wind_time * 4.7 + UV.x * 10.0 + world_pos.z * 2.1) * 0.15;
    // Tertiary flutter — high frequency at the free edge
    float wave3 = sin(wind_time * 7.3 + UV.x * 14.0 + world_pos.x * 3.7) * 0.08;

    float displacement = (wave1 + wave2 + wave3) * wave_factor * wind_strength * 3.5;

    // Displace primarily in Z (perpendicular to the cloth face) with a touch of Y
    VERTEX.z += displacement;
    VERTEX.y += displacement * 0.15 * sin(wind_time * 1.8 + UV.x * 4.0);

    // Slight billowing outward in X at the free edge
    VERTEX.x += sin(wind_time * 1.5 + world_pos.z * 2.0) * wave_factor * wind_strength * 0.2;
}

void fragment() {
    // Use vertex color blended with team_color uniform for the cloth face.
    // Adds subtle shading: slightly brighter at the top, darker at the bottom.
    float shade = mix(1.05, 0.85, UV.y);
    ALBEDO = team_color.rgb * COLOR.rgb * shade;
    ROUGHNESS = 0.95;
    METALLIC = 0.0;
    // Slight translucency at edges for a cloth feel
    ALPHA = 1.0;
}
";
            _flagClothShaderMaterial = new ShaderMaterial();
            _flagClothShaderMaterial.Shader = flagShader;
        }

        // Clone so each flag can have its own team color
        ShaderMaterial instance = (ShaderMaterial)_flagClothShaderMaterial.Duplicate();
        instance.SetShaderParameter("team_color", teamColor);
        return instance;
    }

    // Old single-variant GenerateTree removed — replaced by GenerateTreeVariant
    // with 5 tree types (SmallBush, MediumTree, LargeTree, TallPine, GiantOldGrowth).

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

                // Fill ground layers under mountains to match the arena floor:
                // Y=0: Foundation (indestructible bedrock)
                world.SetVoxel(new Vector3I(x, 0, z), Voxel.Create(VoxelMaterialType.Foundation));
                // Y=1 to Y=2: Stone (destructible)
                world.SetVoxel(new Vector3I(x, 1, z), Voxel.Create(VoxelMaterialType.Stone));
                world.SetVoxel(new Vector3I(x, 2, z), Voxel.Create(VoxelMaterialType.Stone));
                // Y=3 to Y=5: Dirt (destructible top layers)
                for (int y = 3; y < groundY; y++)
                {
                    world.SetVoxel(new Vector3I(x, y, z), Voxel.Create(VoxelMaterialType.Dirt));
                }

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
