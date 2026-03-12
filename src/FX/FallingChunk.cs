using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Converts disconnected voxel groups into falling RigidBody3D physics chunks.
/// Generates a mesh from visible faces, applies collision, and handles impact behaviour.
/// During fall, sheds crumble particles for a visual collapse effect.
/// On shatter, spawns material-aware debris and a dust cloud.
///
/// Tiny chunks (1-2 voxels) are converted directly to debris particles instead of
/// full RigidBody3D chunks for better performance.
/// </summary>
public partial class FallingChunk : RigidBody3D
{
    private const int MaxActiveFallingChunks = 10000;
    private const float MaxLifetime = 60.0f;
    private const float FrozenRuinLifetime = 45.0f;  // How long frozen ruins persist before cleanup
    private const float FrozenRuinFadeDuration = 2.0f;  // Seconds to fade out before removal
    private const float SettleVelocityThreshold = 0.8f;  // Lower threshold so chunks settle sooner
    private const float SettleTimeRequired = 0.5f;        // Shorter wait before freezing
    private const float ShatterImpactVelocity = 30.0f;  // Much higher threshold — only extreme impacts shatter
    private const int CompoundCollisionThreshold = 8;
    private const int SimpleBoxCollisionThreshold = 32;
    private const int MaxComponentSize = 200;  // Split components larger than this into sub-chunks
    private const int MinComponentSize = 1;    // Minimum voxels for a falling chunk (don't discard single voxels)
    private const int DebrisParticleThreshold = 2;  // Chunks with this many or fewer voxels become debris particles

    // Physics tuning for Teardown-like tumbling and piling
    private const float ChunkBounce = 0.15f;
    private const float ChunkFriction = 0.9f;    // Higher friction so chunks stop sliding faster
    private const float LinearDamping = 1.5f;     // Much higher damping so chunks don't slide far
    private const float AngularDamping = 2.0f;    // Higher spin damping so chunks stop tumbling quickly

    // Crumble particle shedding during fall
    private const float CrumbleInterval = 0.15f;      // Seconds between crumble particle spawns
    private const int MaxCrumbleParticles = 8;         // Max crumble spawns per chunk per fall
    private const float CrumbleSpeedThreshold = 1.5f;  // Minimum speed to shed crumble particles

    private static readonly List<FallingChunk> ActiveChunks = new();

    private float _age;
    private float _frozenAge;  // Tracks time spent frozen as a ruin
    private float _settleTimer;
    private bool _isFrozen;
    private List<Vector3I> _voxelPositions = new();
    private List<Color> _voxelColors = new();
    private List<VoxelMaterialType> _voxelMaterials = new();
    private float _impactVelocity;
    private float _crumbleTimer;
    private int _crumbleCount;
    private bool _hasHitGround;

    // Maps FallingChunk face indices to VoxelFaceDirection for atlas UV lookup
    private static readonly VoxelFaceDirection[] FaceToDirection =
    {
        VoxelFaceDirection.Right,   // face 0: +X
        VoxelFaceDirection.Left,    // face 1: -X
        VoxelFaceDirection.Top,     // face 2: +Y
        VoxelFaceDirection.Bottom,  // face 3: -Y
        VoxelFaceDirection.Front,   // face 4: -Z
        VoxelFaceDirection.Back,    // face 5: +Z
    };

    // 6 cardinal directions for face culling
    private static readonly Vector3I[] FaceDirections =
    {
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        new Vector3I(0, 0, -1),
        new Vector3I(0, 0, 1),
    };

    private static readonly Vector3[] FaceNormals =
    {
        Vector3.Right,
        Vector3.Left,
        Vector3.Up,
        Vector3.Down,
        Vector3.Forward,  // -Z in Godot
        Vector3.Back,     // +Z in Godot
    };

    // ── Cleanup ─────────────────────────────────────────────────────────

    /// <summary>
    /// Immediately frees all active falling chunks.
    /// Call when transitioning between game phases.
    /// </summary>
    public static void ClearAll()
    {
        // Snapshot the list since Free() triggers _ExitTree which modifies ActiveChunks
        List<FallingChunk> snapshot = new List<FallingChunk>(ActiveChunks);
        ActiveChunks.Clear();
        foreach (FallingChunk chunk in snapshot)
        {
            if (IsInstanceValid(chunk))
            {
                chunk.Free();
            }
        }
    }

    /// <summary>
    /// Makes all active FallingChunks semi-transparent (for kill cam) or restores them.
    /// Uses alpha transparency so debris doesn't obscure the commander death.
    /// </summary>
    public static void SetKillCamAlpha(float alpha)
    {
        foreach (FallingChunk chunk in ActiveChunks)
        {
            if (!IsInstanceValid(chunk)) continue;
            foreach (Node child in chunk.GetChildren())
            {
                if (child is MeshInstance3D meshInst)
                {
                    Material? surfMat = meshInst.MaterialOverride ?? meshInst.GetSurfaceOverrideMaterial(0);
                    if (surfMat is StandardMaterial3D stdMat)
                    {
                        if (alpha < 0.99f)
                        {
                            stdMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                            Color c = stdMat.AlbedoColor;
                            stdMat.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
                        }
                        else
                        {
                            // Restore full opacity
                            stdMat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                            Color c = stdMat.AlbedoColor;
                            stdMat.AlbedoColor = new Color(c.R, c.G, c.B, 1f);
                        }
                    }
                }
            }
        }
    }

    // ── Creation ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a FallingChunk from a list of connected, disconnected voxel positions.
    /// Reads material colors, removes voxels from the world, builds mesh and collision.
    /// Tiny groups (1-2 voxels) are converted directly to debris particles instead of
    /// creating a full RigidBody3D chunk, which is cheaper and looks better at small scale.
    /// </summary>
    public static FallingChunk? Create(List<Vector3I> voxelPositions, VoxelWorld world, Vector3 explosionCenter)
    {
        if (voxelPositions.Count == 0)
        {
            return null;
        }

        // First pass: collect valid voxels (skip any already removed by previous chunk creation)
        List<Vector3I> validPositions = new List<Vector3I>(voxelPositions.Count);
        List<Color> validColors = new List<Color>(voxelPositions.Count);
        List<VoxelMaterialType> validMaterials = new List<VoxelMaterialType>(voxelPositions.Count);
        Vector3 centerOfMass = Vector3.Zero;

        for (int i = 0; i < voxelPositions.Count; i++)
        {
            Vector3I pos = voxelPositions[i];
            VoxelValue voxel = world.GetVoxel(pos);
            if (voxel.IsAir)
            {
                continue; // Already removed by a previous chunk or explosion
            }
            Color color = VoxelMaterials.GetPreviewColor(voxel.Material);
            validPositions.Add(pos);
            validColors.Add(color);
            validMaterials.Add(voxel.Material);
            centerOfMass += MathHelpers.MicrovoxelToWorld(pos);
        }

        if (validPositions.Count == 0)
        {
            return null;
        }

        centerOfMass /= validPositions.Count;

        // Remove voxels from world now that we know they're valid
        for (int i = 0; i < validPositions.Count; i++)
        {
            world.SetVoxel(validPositions[i], VoxelValue.Air);
        }

        // Remove grass blades at AND above the removed voxels.
        // Grass sits on the surface, so we need to clear both the destroyed positions
        // and one layer above (where grass blades are visually anchored).
        List<Vector3I> grassRemovalPositions = new List<Vector3I>(validPositions.Count * 2);
        for (int i = 0; i < validPositions.Count; i++)
        {
            grassRemovalPositions.Add(validPositions[i]);
            grassRemovalPositions.Add(validPositions[i] + Vector3I.Up);
        }
        TerrainDecorator.RemoveGrassAt(grassRemovalPositions);

        // Unfreeze any frozen ruin chunks that lost terrain support because these
        // voxels were just removed. This handles cases where FallingChunk.Create is
        // called from non-explosion code paths (e.g. structural disconnection).
        UnfreezeUnsupportedRuins(world);

        // Tiny chunks (1-2 voxels): convert to debris particles instead of full RigidBody3D.
        // This is cheaper, looks better at small scale, and reduces active chunk count.
        if (validPositions.Count <= DebrisParticleThreshold)
        {
            Node fxParent = world.GetTree().Root;
            for (int i = 0; i < validPositions.Count; i++)
            {
                Vector3 voxelWorld = MathHelpers.MicrovoxelToWorld(validPositions[i]);
                DebrisFX.SpawnDebris(fxParent, voxelWorld, validColors[i], explosionCenter, 2, validMaterials[i]);
            }
            return null;
        }

        // Enforce cap only on ACTIVE (in-motion) chunks — frozen ruins are free to persist.
        CleanupExcess();
        int activeCount = 0;
        for (int i = 0; i < ActiveChunks.Count; i++)
        {
            if (!ActiveChunks[i]._isFrozen) activeCount++;
        }
        if (activeCount >= MaxActiveFallingChunks)
        {
            // Force-freeze the oldest active (non-frozen) chunk so it persists as a ruin
            // rather than being destroyed. New chunks always get to animate.
            for (int i = 0; i < ActiveChunks.Count; i++)
            {
                if (!ActiveChunks[i]._isFrozen)
                {
                    ActiveChunks[i].FreezeAsRuin();
                    break;
                }
            }
        }

        FallingChunk chunk = new FallingChunk();
        chunk._voxelPositions = validPositions;
        chunk._voxelColors = validColors;
        chunk._voxelMaterials = validMaterials;

        HashSet<Vector3I> positionSet = new HashSet<Vector3I>(validPositions);

        // Build the visible-face mesh with per-face UVs mapped into the atlas tile rects.
        // Unlike static world chunks which use a world-space triplanar shader, falling
        // chunks bake UVs per-vertex so textures stay correct as the chunk rotates.
        VoxelTextureAtlas atlas = world.TextureAtlas;
        bool hasAtlas = atlas.HasGeneratedTextures && atlas.AtlasTexture != null;
        if (!hasAtlas)
        {
            GD.PrintErr($"FallingChunk: Atlas not available! HasGeneratedTextures={atlas.HasGeneratedTextures}, AtlasTexture={(atlas.AtlasTexture != null ? "loaded" : "null")}");
        }

        // Determine if any voxels use transparent materials (Ice, Glass).
        // Chunks with transparent materials need alpha blending enabled.
        bool hasTransparentMaterial = false;
        for (int i = 0; i < validMaterials.Count; i++)
        {
            if (VoxelMaterials.IsTransparent(validMaterials[i]))
            {
                hasTransparentMaterial = true;
                break;
            }
        }

        ArrayMesh? mesh = BuildChunkMesh(validPositions, positionSet, validColors, validMaterials, centerOfMass, atlas, hasAtlas);
        if (mesh != null)
        {
            MeshInstance3D meshInstance = new MeshInstance3D();
            meshInstance.Mesh = mesh;

            // Use StandardMaterial3D with per-vertex UVs (NOT triplanar shader).
            // Triplanar uses world-space UVs which slide as chunks move/rotate.
            // StandardMaterial3D with baked per-corner UVs keeps textures correct.
            StandardMaterial3D mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.Roughness = 0.8f;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Back; // Both sides have explicit geometry with correct normals
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
            if (hasTransparentMaterial)
            {
                mat.Roughness = 0.05f;
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }
            if (hasAtlas)
            {
                mat.AlbedoTexture = atlas.AtlasTexture;
            }
            meshInstance.MaterialOverride = mat;
            chunk.AddChild(meshInstance);
        }

        // Build collision shape
        BuildCollisionShape(chunk, validPositions, positionSet, centerOfMass, mesh);

        // Physics properties — tuned for Teardown-like tumbling and pile-up
        chunk.Mass = Mathf.Max(0.5f, validPositions.Count * 0.1f);
        chunk.GravityScale = 1.2f;
        chunk.ContactMonitor = true;
        chunk.MaxContactsReported = 4;
        chunk.CollisionLayer = 4;      // Debris layer
        chunk.CollisionMask = 1 | 4;   // Collide with world AND other debris (for piling up)

        // Physics material for realistic bounce/friction so chunks tumble and pile
        chunk.PhysicsMaterialOverride = new PhysicsMaterial
        {
            Bounce = ChunkBounce,
            Friction = ChunkFriction
        };

        // Damping prevents infinite sliding/spinning on flat surfaces
        chunk.LinearDamp = LinearDamping;
        chunk.AngularDamp = AngularDamping;

        // CCD prevents fast-moving chunks from tunneling through the ground
        chunk.ContinuousCd = true;

        // Add to scene tree FIRST (required for physics operations and GlobalPosition)
        Node root = world.GetTree().Root;
        root.AddChild(chunk);
        chunk.AddToGroup("FallingChunks");
        chunk.GlobalPosition = centerOfMass;

        // Apply initial impulse away from explosion center (must be in tree)
        // Reduced impulse so chunks tumble nearby rather than flying across the map
        Vector3 impulseDir = (centerOfMass - explosionCenter).Normalized();
        if (impulseDir.LengthSquared() < 0.01f)
        {
            impulseDir = Vector3.Up;
        }
        float impulseMag = Mathf.Clamp(validPositions.Count * 0.15f, 0.5f, 4.0f);
        chunk.ApplyImpulse(impulseDir * impulseMag + Vector3.Up * impulseMag * 0.3f);

        // Add angular velocity for visual tumbling (reduced so chunks don't spin wildly)
        chunk.AngularVelocity = new Vector3(
            (float)GD.RandRange(-1.5, 1.5),
            (float)GD.RandRange(-1.5, 1.5),
            (float)GD.RandRange(-1.5, 1.5));
        ActiveChunks.Add(chunk);

        // If the chunk contains flammable materials that were on fire,
        // attach fire particles as a child so fire follows the falling chunk.
        // Also remove the static fire at those world positions so it doesn't hover.
        AttachFireIfBurning(chunk, validPositions, validMaterials);

        return chunk;
    }

    /// <summary>
    /// Creates falling debris chunks from a weapon's voxel grid when destroyed.
    /// Splits the weapon into 3-4 spatial groups that tumble independently.
    /// Uses vertex colors directly (no atlas) since weapons use procedural palettes.
    /// </summary>
    public static void CreateFromWeaponVoxels(
        Color?[,,] voxelGrid, float voxelSize, Vector3 weaponWorldPos,
        Vector3 explosionCenter, Node sceneRoot)
    {
        int w = voxelGrid.GetLength(0);
        int h = voxelGrid.GetLength(1);
        int d = voxelGrid.GetLength(2);

        // Collect all solid voxels
        List<(Vector3I pos, Color color)> allVoxels = new();
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        for (int z = 0; z < d; z++)
        {
            if (voxelGrid[x, y, z] is Color c)
                allVoxels.Add((new Vector3I(x, y, z), c));
        }

        if (allVoxels.Count == 0) return;

        // Split into 3-4 spatial groups by Y slices (base, body, barrel/top)
        // This creates natural breakup: base goes one way, barrel another
        int groupCount = Mathf.Min(allVoxels.Count, (int)GD.RandRange(3, 5));
        float sliceHeight = (float)h / groupCount;

        // Origin offset used by WeaponModelGenerator for centering the mesh
        Vector3 originOffset = new Vector3(-w * 0.5f * voxelSize, 0, -d * 0.5f * voxelSize);

        for (int g = 0; g < groupCount; g++)
        {
            int yMin = (int)(g * sliceHeight);
            int yMax = (int)((g + 1) * sliceHeight);

            List<Vector3I> groupPositions = new();
            List<Color> groupColors = new();
            Vector3 groupCenter = Vector3.Zero;

            foreach (var (pos, color) in allVoxels)
            {
                if (pos.Y >= yMin && pos.Y < yMax)
                {
                    groupPositions.Add(pos);
                    groupColors.Add(color);
                    // Convert grid position to local space (matching weapon mesh origin)
                    Vector3 localPos = new Vector3(pos.X, pos.Y, pos.Z) * voxelSize + originOffset;
                    groupCenter += weaponWorldPos + localPos;
                }
            }

            if (groupPositions.Count == 0) continue;
            groupCenter /= groupPositions.Count;

            // Tiny groups become debris particles
            if (groupPositions.Count <= 2)
            {
                for (int i = 0; i < groupPositions.Count; i++)
                {
                    Vector3 localPos = new Vector3(groupPositions[i].X, groupPositions[i].Y, groupPositions[i].Z) * voxelSize + originOffset;
                    Vector3 worldPos = weaponWorldPos + localPos;
                    DebrisFX.SpawnDebris(sceneRoot, worldPos, groupColors[i], explosionCenter, 2, VoxelMaterialType.Metal, voxelSize);
                }
                continue;
            }

            // Build mesh for this group
            HashSet<Vector3I> posSet = new HashSet<Vector3I>(groupPositions);
            ArrayMesh? mesh = BuildWeaponChunkMesh(groupPositions, posSet, groupColors, groupCenter, weaponWorldPos, originOffset, voxelSize);
            if (mesh == null) continue;

            FallingChunk chunk = new FallingChunk();
            // Don't store _voxelPositions — they're grid-local coords (0..w),
            // not world microvoxel coords. Shatter/crumble code calls
            // MicrovoxelToWorld() which would produce garbage positions.
            // Empty lists make shatter/crumble skip gracefully.

            MeshInstance3D meshInstance = new MeshInstance3D();
            meshInstance.Mesh = mesh;
            StandardMaterial3D mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.Roughness = 0.7f;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
            meshInstance.MaterialOverride = mat;
            chunk.AddChild(meshInstance);

            // Simple bounding box collision
            BoxShape3D boxShape = new BoxShape3D();
            Vector3I bMin = groupPositions[0], bMax = groupPositions[0];
            for (int i = 1; i < groupPositions.Count; i++)
            {
                bMin = new Vector3I(Mathf.Min(bMin.X, groupPositions[i].X), Mathf.Min(bMin.Y, groupPositions[i].Y), Mathf.Min(bMin.Z, groupPositions[i].Z));
                bMax = new Vector3I(Mathf.Max(bMax.X, groupPositions[i].X), Mathf.Max(bMax.Y, groupPositions[i].Y), Mathf.Max(bMax.Z, groupPositions[i].Z));
            }
            Vector3 boxSize = new Vector3(bMax.X - bMin.X + 1, bMax.Y - bMin.Y + 1, bMax.Z - bMin.Z + 1) * voxelSize;
            boxShape.Size = boxSize;
            Vector3 boxLocalCenter = (new Vector3(bMin.X + bMax.X + 1, bMin.Y + bMax.Y + 1, bMin.Z + bMax.Z + 1) * 0.5f * voxelSize + originOffset)
                - (groupCenter - weaponWorldPos);
            CollisionShape3D collider = new CollisionShape3D();
            collider.Shape = boxShape;
            collider.Position = boxLocalCenter;
            chunk.AddChild(collider);

            // Physics
            chunk.Mass = Mathf.Max(0.3f, groupPositions.Count * 0.05f);
            chunk.GravityScale = 1.3f;
            chunk.ContactMonitor = true;
            chunk.MaxContactsReported = 4;
            chunk.CollisionLayer = 4;
            chunk.CollisionMask = 1 | 4;
            chunk.PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.2f, Friction = 0.8f };
            chunk.LinearDamp = LinearDamping;
            chunk.AngularDamp = AngularDamping;
            chunk.ContinuousCd = true;

            sceneRoot.AddChild(chunk);
            chunk.AddToGroup("FallingChunks");
            chunk.GlobalPosition = groupCenter;

            // Explosive impulse outward from weapon center
            Vector3 impulseDir = (groupCenter - explosionCenter).Normalized();
            if (impulseDir.LengthSquared() < 0.01f)
                impulseDir = Vector3.Up;
            float impulseMag = Mathf.Clamp(groupPositions.Count * 0.08f, 0.3f, 2.5f);
            chunk.ApplyImpulse(impulseDir * impulseMag + Vector3.Up * impulseMag * 0.5f);
            chunk.AngularVelocity = new Vector3(
                (float)GD.RandRange(-3.0, 3.0),
                (float)GD.RandRange(-3.0, 3.0),
                (float)GD.RandRange(-3.0, 3.0));

            ActiveChunks.Add(chunk);
        }
    }

    /// <summary>
    /// Builds a mesh for a weapon debris chunk using vertex colors (no atlas).
    /// Similar to BuildChunkMesh but uses weapon-local coordinates and voxelSize.
    /// </summary>
    private static ArrayMesh? BuildWeaponChunkMesh(
        List<Vector3I> positions,
        HashSet<Vector3I> positionSet,
        List<Color> colors,
        Vector3 groupCenterWorld,
        Vector3 weaponWorldPos,
        Vector3 originOffset,
        float voxelSize)
    {
        SurfaceTool st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        bool hasAnyFaces = false;

        // Default UV rect (full 0-1 range since we use vertex colors, no atlas)
        Rect2 uvRect = new Rect2(0, 0, 1, 1);

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3I voxelPos = positions[i];
            Color color = colors[i];
            // Local position relative to group center
            Vector3 localPos = new Vector3(voxelPos.X, voxelPos.Y, voxelPos.Z) * voxelSize + originOffset;
            Vector3 localOrigin = (weaponWorldPos + localPos) - groupCenterWorld;

            for (int face = 0; face < 6; face++)
            {
                Vector3I neighborPos = voxelPos + FaceDirections[face];
                if (positionSet.Contains(neighborPos)) continue;

                AddFaceQuad(st, localOrigin, face, voxelSize, FaceNormals[face], color, uvRect);
                AddFaceQuad(st, localOrigin, face, voxelSize, FaceNormals[face], color, uvRect, reversed: true);
                hasAnyFaces = true;
            }
        }

        return hasAnyFaces ? st.Commit() : null;
    }

    private static void AttachFireIfBurning(FallingChunk chunk, List<Vector3I> positions, List<VoxelMaterialType> materials)
    {
        FireSystem? fire = FireSystem.Instance;
        if (fire == null) return;

        bool hasBurning = false;
        for (int i = 0; i < materials.Count; i++)
        {
            VoxelMaterialDefinition def = VoxelMaterials.GetDefinition(materials[i]);
            if (def.IsFlammable && fire.IsOnFire(positions[i]))
            {
                fire.ExtinguishAt(positions[i]);
                hasBurning = true;
            }
        }

        if (!hasBurning) return;

        // Attach a fire particle emitter as child of the chunk so it moves with it
        GpuParticles3D fireParticles = new GpuParticles3D();
        fireParticles.Name = "ChunkFire";
        fireParticles.Amount = 12;
        fireParticles.Lifetime = 0.6f;
        fireParticles.Explosiveness = 0.1f;
        fireParticles.OneShot = false;
        fireParticles.LocalCoords = true; // Particles follow the chunk as it falls/rotates
        fireParticles.FixedFps = 30;
        fireParticles.ProcessMode = Node.ProcessModeEnum.Always;

        ParticleProcessMaterial processMat = new ParticleProcessMaterial();
        processMat.Direction = new Vector3(0f, 1f, 0f);
        processMat.Spread = 30f;
        processMat.InitialVelocityMin = 0.3f;
        processMat.InitialVelocityMax = 0.8f;
        processMat.Gravity = new Vector3(0f, 0.5f, 0f);
        processMat.ScaleMin = 0.5f;
        processMat.ScaleMax = 1.2f;

        Gradient colorGradient = new Gradient();
        colorGradient.SetColor(0, new Color(1.0f, 0.9f, 0.3f, 0.9f));
        colorGradient.SetColor(1, new Color(0.8f, 0.2f, 0.0f, 0.0f));
        GradientTexture1D gradientTex = new GradientTexture1D();
        gradientTex.Gradient = colorGradient;
        processMat.ColorRamp = gradientTex;
        fireParticles.ProcessMaterial = processMat;

        QuadMesh drawMesh = new QuadMesh();
        drawMesh.Size = new Vector2(GameConfig.MicrovoxelMeters * 0.4f, GameConfig.MicrovoxelMeters * 0.4f);
        StandardMaterial3D drawMat = new StandardMaterial3D();
        drawMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        drawMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        drawMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        drawMat.AlbedoColor = new Color(1.0f, 0.6f, 0.1f, 0.8f);
        drawMat.EmissionEnabled = true;
        drawMat.Emission = new Color(1.0f, 0.4f, 0.0f);
        drawMat.EmissionEnergyMultiplier = 2.0f;
        drawMesh.Material = drawMat;
        fireParticles.DrawPass1 = drawMesh;

        chunk.AddChild(fireParticles);
        // Position at chunk center (local 0,0,0)
        fireParticles.Position = Vector3.Zero;
    }

    // ── Mesh generation ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a mesh with only the exposed faces (faces whose neighbor is not in the group).
    /// Uses per-corner UVs spanning atlas tile rects so textures are baked per-vertex and
    /// stick to the chunk as it falls/rotates (NOT world-space triplanar).
    /// Vertex colors are lightened toward white so StandardMaterial3D's texture*vertex_color
    /// multiplication doesn't crush texture detail on dark materials.
    /// </summary>
    private static ArrayMesh? BuildChunkMesh(
        List<Vector3I> positions,
        HashSet<Vector3I> positionSet,
        List<Color> colors,
        List<VoxelMaterialType> materials,
        Vector3 centerOfMass,
        VoxelTextureAtlas atlas,
        bool hasAtlas)
    {
        SurfaceTool st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float scale = GameConfig.MicrovoxelMeters;
        bool hasAnyFaces = false;

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3I voxelPos = positions[i];
            // Lighten vertex colors so StandardMaterial3D's texture*vertex_color
            // doesn't crush texture detail on dark/saturated materials
            Color color = hasAtlas ? colors[i].Lerp(Colors.White, 0.6f) : colors[i];
            VoxelMaterialType matType = materials[i];
            Vector3 localOrigin = MathHelpers.MicrovoxelToWorld(voxelPos) - centerOfMass;

            for (int face = 0; face < 6; face++)
            {
                Vector3I neighborPos = voxelPos + FaceDirections[face];
                if (positionSet.Contains(neighborPos))
                {
                    continue; // Neighbor in group, face is hidden
                }

                // Get the atlas UV rect for this material+face.
                // Each face gets proper corner UVs mapped into the tile rect so the
                // atlas texture is sampled correctly even as the chunk rotates.
                Rect2 uvRect = atlas.GetUvRect(matType, FaceToDirection[face]);

                // Add this face as a quad (two triangles) with per-corner atlas UVs
                AddFaceQuad(st, localOrigin, face, scale, FaceNormals[face], color, uvRect);
                // Add a reversed back-face so interior surfaces are visible.
                // Reuse the SAME normal as the outer face so both sides of each
                // panel are lit identically. When a chunk flips during physics,
                // the inner face that becomes visible shares the outer face's
                // normal and receives the same lighting instead of appearing black.
                AddFaceQuad(st, localOrigin, face, scale, FaceNormals[face], color, uvRect, reversed: true);
                hasAnyFaces = true;
            }
        }

        if (!hasAnyFaces)
        {
            return null;
        }

        // NOTE: Do NOT call st.GenerateNormals() here — normals are already set
        // manually per-vertex above. GenerateNormals() recalculates from winding
        // and can strip vertex color data in Godot 4's SurfaceTool, causing the
        // mesh to render as transparent/white artifacts instead of colored voxels.
        return st.Commit();
    }

    /// <summary>
    /// Adds a single voxel face (quad) to the SurfaceTool.
    /// Each vertex gets a proper per-corner UV mapped into the atlas tile rect so the
    /// texture is sampled correctly. UVs are baked per-vertex (NOT world-space triplanar)
    /// so textures stick to the chunk as it falls and rotates.
    /// Vertex layout: v0=bottom-left, v1=top-left, v2=top-right, v3=bottom-right
    /// UV layout:     uv0=(0,1),      uv1=(0,0),   uv2=(1,0),    uv3=(1,1)  within tile rect
    /// </summary>
    private static void AddFaceQuad(SurfaceTool st, Vector3 origin, int face, float size, Vector3 normal, Color color, Rect2 uvRect, bool reversed = false)
    {
        // Per-corner UVs spanning the full tile rect in the atlas.
        // This gives each voxel face the complete material texture baked into UVs,
        // so textures remain correct as the chunk moves and rotates.
        Vector2 uv0 = uvRect.Position + new Vector2(0, uvRect.Size.Y);           // bottom-left
        Vector2 uv1 = uvRect.Position;                                             // top-left
        Vector2 uv2 = uvRect.Position + new Vector2(uvRect.Size.X, 0);            // top-right
        Vector2 uv3 = uvRect.Position + uvRect.Size;                               // bottom-right

        Vector3 v0, v1, v2, v3;

        switch (face)
        {
            case 0: // +X (Right)
                v0 = origin + new Vector3(size, 0, 0);
                v1 = origin + new Vector3(size, size, 0);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, 0, size);
                break;
            case 1: // -X (Left)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(0, size, 0);
                v3 = origin + new Vector3(0, 0, 0);
                break;
            case 2: // +Y (Up)
                v0 = origin + new Vector3(0, size, 0);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, size, 0);
                break;
            case 3: // -Y (Down)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, 0, 0);
                v2 = origin + new Vector3(size, 0, 0);
                v3 = origin + new Vector3(size, 0, size);
                break;
            case 4: // -Z (Forward)
                v0 = origin + new Vector3(size, 0, 0);
                v1 = origin + new Vector3(size, size, 0);
                v2 = origin + new Vector3(0, size, 0);
                v3 = origin + new Vector3(0, 0, 0);
                break;
            default: // +Z (Back)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, 0, size);
                break;
        }

        if (reversed)
        {
            // Reversed winding so back-faces have correct normals for lighting
            // Triangle 1: v0, v2, v1
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv1); st.AddVertex(v1);
            // Triangle 2: v0, v3, v2
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv3); st.AddVertex(v3);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
        }
        else
        {
            // Triangle 1: v0, v1, v2
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv1); st.AddVertex(v1);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
            // Triangle 2: v0, v2, v3
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
            st.SetColor(color); st.SetNormal(normal); st.SetUV(uv3); st.AddVertex(v3);
        }
    }

    // ── Collision shape ─────────────────────────────────────────────────

    private static void BuildCollisionShape(
        FallingChunk chunk,
        List<Vector3I> positions,
        HashSet<Vector3I> positionSet,
        Vector3 centerOfMass,
        ArrayMesh? mesh)
    {
        float scale = GameConfig.MicrovoxelMeters;

        if (positions.Count < CompoundCollisionThreshold)
        {
            // Use individual BoxShape3D per voxel (compound collision)
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 localCenter = MathHelpers.MicrovoxelToWorld(positions[i]) - centerOfMass
                    + Vector3.One * scale * 0.5f;

                CollisionShape3D collider = new CollisionShape3D();
                BoxShape3D box = new BoxShape3D();
                box.Size = Vector3.One * scale;
                collider.Shape = box;
                collider.Position = localCenter;
                chunk.AddChild(collider);
            }
        }
        else if (positions.Count >= SimpleBoxCollisionThreshold)
        {
            // Large chunks: use a single bounding box to avoid expensive per-vertex convex shapes
            CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
        }
        else
        {
            // Medium chunks: use ConvexPolygonShape3D from the mesh vertices
            if (mesh != null && mesh.GetSurfaceCount() > 0)
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
                if (arrays.Count > 0 && arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array() is Vector3[] vertices && vertices.Length >= 4)
                {
                    ConvexPolygonShape3D convexShape = new ConvexPolygonShape3D();
                    convexShape.Points = vertices;

                    CollisionShape3D collider = new CollisionShape3D();
                    collider.Shape = convexShape;
                    chunk.AddChild(collider);
                }
                else
                {
                    // Fallback: bounding box
                    CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
                }
            }
            else
            {
                CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
            }
        }
    }

    private static void CreateBoundingBoxCollider(FallingChunk chunk, List<Vector3I> positions, Vector3 centerOfMass, float scale)
    {
        Vector3I min = positions[0];
        Vector3I max = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            Vector3I p = positions[i];
            if (p.X < min.X) min = new Vector3I(p.X, min.Y, min.Z);
            if (p.Y < min.Y) min = new Vector3I(min.X, p.Y, min.Z);
            if (p.Z < min.Z) min = new Vector3I(min.X, min.Y, p.Z);
            if (p.X > max.X) max = new Vector3I(p.X, max.Y, max.Z);
            if (p.Y > max.Y) max = new Vector3I(max.X, p.Y, max.Z);
            if (p.Z > max.Z) max = new Vector3I(max.X, max.Y, p.Z);
        }

        Vector3 minWorld = MathHelpers.MicrovoxelToWorld(min);
        Vector3 maxWorld = MathHelpers.MicrovoxelToWorld(max) + Vector3.One * scale;
        Vector3 size = maxWorld - minWorld;
        Vector3 localCenter = ((minWorld + maxWorld) * 0.5f) - centerOfMass;

        CollisionShape3D collider = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = size;
        collider.Shape = box;
        collider.Position = localCenter;
        chunk.AddChild(collider);
    }

    // ── Physics callbacks ───────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_isFrozen)
        {
            SetProcess(false);
            _frozenAge += dt;

            // After FrozenRuinLifetime, fade out and clean up so frozen ruins
            // don't accumulate forever and exhaust the ActiveChunks list.
            if (_frozenAge >= FrozenRuinLifetime)
            {
                FadeOutAndCleanup();
                return;
            }

            // Every 2 seconds, verify the ruin is still resting on something solid.
            // This catches cases where the support (terrain or other debris) was removed
            // after this chunk froze — e.g. debris it was resting on rolled away.
            if (_frozenAge >= 2f && Mathf.FloorToInt(_frozenAge / 2f) != Mathf.FloorToInt((_frozenAge - dt) / 2f))
            {
                PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
                if (space != null)
                {
                    // Raycast straight down from the chunk center
                    Vector3 from = GlobalPosition;
                    Vector3 to = from + Vector3.Down * 1.5f; // short check distance
                    PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
                    query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
                    query.CollisionMask = 1 | 4; // world + debris
                    var result = space.IntersectRay(query);
                    if (result.Count == 0)
                    {
                        // Nothing below — unfreeze and let gravity take over
                        UnfreezeRuin();
                    }
                }
            }
            return;
        }

        _age += dt;

        // Force cleanup after max lifetime (only for non-frozen, still-moving chunks)
        if (_age >= MaxLifetime)
        {
            ForceCleanup();
            return;
        }

        // Track settling — only accumulate settle time after ground contact
        // to prevent chunks freezing mid-air at the peak of their arc
        float speed = LinearVelocity.Length();
        if (speed < SettleVelocityThreshold && _hasHitGround)
        {
            _settleTimer += dt;
        }
        else
        {
            _settleTimer = 0f;
            _impactVelocity = Mathf.Max(_impactVelocity, speed);
        }

        // Shed crumble particles while airborne and moving fast enough
        if (!_hasHitGround && speed > CrumbleSpeedThreshold && _crumbleCount < MaxCrumbleParticles)
        {
            _crumbleTimer += dt;
            if (_crumbleTimer >= CrumbleInterval)
            {
                _crumbleTimer -= CrumbleInterval;
                SpawnCrumbleParticle();
                _crumbleCount++;
            }
        }

        // Check for collision with ground
        if (GetContactCount() > 0)
        {
            if (!_hasHitGround)
            {
                _hasHitGround = true;

                // Spawn a dust cloud on first ground impact
                SpawnImpactDust();

                // Apply extra damping on first ground contact to prevent sliding
                LinearDamp = 3.0f;
                AngularDamp = 4.0f;
            }

            float currentSpeed = LinearVelocity.Length();

            // High-speed impact: shatter into material-aware debris
            if (_impactVelocity > ShatterImpactVelocity && currentSpeed < ShatterImpactVelocity * 0.5f)
            {
                Shatter();
                return;
            }

            // Low velocity for long enough: freeze as static ruin
            if (_settleTimer >= SettleTimeRequired)
            {
                FreezeAsRuin();
                return;
            }
        }

        // Safety net: if chunk has fallen far below the world, clean it up
        if (GlobalPosition.Y < -50f)
        {
            ForceCleanup();
        }
    }

    private void FreezeAsRuin()
    {
        _isFrozen = true;
        _frozenAge = 0f;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        // Keep collision active so other chunks/debris can land on top of this ruin.
        // Layer 4 (debris) stays visible to mask 4 queries. Mask stays at 1 (world).
        // Add layer 1 so the frozen ruin acts as part of the "world" for other debris.
        CollisionLayer = 1 | 4;  // World + Debris layers — other chunks land on us
        CollisionMask = 0;       // Frozen ruin doesn't need to detect anything itself
    }

    /// <summary>
    /// Unfreezes a frozen ruin so it falls again with gravity.
    /// Called when the terrain beneath it has been destroyed.
    /// </summary>
    private void UnfreezeRuin()
    {
        _isFrozen = false;
        _frozenAge = 0f;
        _age = 0f;
        _settleTimer = 0f;
        _hasHitGround = false;
        Freeze = false;
        GravityScale = 1.2f;
        SetProcess(true);
        // Restore debris collision layers so it interacts with the world again
        CollisionLayer = 4;      // Debris layer
        CollisionMask = 1 | 4;   // Collide with world AND other debris
        // Restore damping to falling-chunk defaults (not the extra-sticky post-impact values)
        LinearDamp = LinearDamping;
        AngularDamp = AngularDamping;
    }

    /// <summary>
    /// Unfreezes any frozen ruins within the given explosion radius. This ensures
    /// that direct hits on settled debris blast them free again. Also applies a
    /// small impulse away from the explosion center.
    /// Chunks within 50% of the blast radius (inner kill zone) are shattered
    /// into debris particles and removed instead of merely pushed.
    /// </summary>
    public static void UnfreezeRuinsInRadius(Vector3 explosionCenter, float radius)
    {
        // Use a generous radius so explosions affect settled chunks well beyond the voxel blast
        float effectRadius = radius * 3f;
        float effectRadiusSq = effectRadius * effectRadius;
        // Shatter radius: generous so large chunks whose center is outside the blast
        // still get shattered if any part of them is within range.
        // Use 2x the blast radius to account for chunk spatial extent.
        float shatterRadius = radius * 2f;
        float shatterRadiusSq = shatterRadius * shatterRadius;
        for (int i = ActiveChunks.Count - 1; i >= 0; i--)
        {
            FallingChunk chunk = ActiveChunks[i];
            if (!IsInstanceValid(chunk))
            {
                continue;
            }

            // Use closest-point distance: check chunk center AND account for chunk spatial size.
            // Large chunks (like fallen tree canopies) have centers far from edges, so a direct
            // hit on the edge wouldn't register with center-only distance checks.
            float distSq = (chunk.GlobalPosition - explosionCenter).LengthSquared();

            // Estimate chunk radius from voxel count (rough bounding sphere)
            float chunkExtent = Mathf.Sqrt(chunk._voxelPositions.Count) * GameConfig.MicrovoxelMeters * 0.5f;
            float effectiveDistSq = distSq;
            if (chunkExtent > 0.1f)
            {
                // Reduce effective distance by chunk extent (closest edge of chunk to explosion)
                float dist = Mathf.Sqrt(distSq);
                float closestDist = Mathf.Max(0f, dist - chunkExtent);
                effectiveDistSq = closestDist * closestDist;
            }

            if (effectiveDistSq <= shatterRadiusSq)
            {
                // Kill zone — shatter any chunk (frozen or active) into debris particles
                chunk.Shatter();
            }
            else if (effectiveDistSq <= effectRadiusSq)
            {
                // Scale impulse by proximity — closer chunks get blasted harder
                float dist = Mathf.Sqrt(effectiveDistSq);
                float falloff = 1f - Mathf.Clamp(dist / effectRadius, 0f, 1f);
                float force = Mathf.Lerp(2f, 12f, falloff);
                Vector3 pushDir = (chunk.GlobalPosition - explosionCenter).Normalized();
                if (pushDir.LengthSquared() < 0.01f) pushDir = Vector3.Up;

                if (chunk._isFrozen)
                {
                    chunk.UnfreezeRuin();
                }
                chunk.ApplyImpulse(pushDir * force + Vector3.Up * force * 0.5f);
            }
        }
    }

    /// <summary>
    /// Scans all frozen FallingChunk ruins and unfreezes any that no longer have
    /// solid ground beneath them. A ruin is considered supported if at least one
    /// solid voxel exists within 1 microvoxel below any of its voxel positions
    /// (translated to current world position). Call this after voxels are destroyed
    /// (explosions, falling chunk creation) so ruins don't float in the air.
    /// </summary>
    public static void UnfreezeUnsupportedRuins(VoxelWorld world)
    {
        // Snapshot the list since unfreezing could theoretically modify it during iteration
        // (it doesn't currently, but be safe)
        for (int i = ActiveChunks.Count - 1; i >= 0; i--)
        {
            FallingChunk chunk = ActiveChunks[i];
            if (!IsInstanceValid(chunk) || !chunk._isFrozen)
            {
                continue;
            }

            if (!HasSupportBelow(chunk, world))
            {
                chunk.UnfreezeRuin();
            }
        }
    }

    /// <summary>
    /// Checks whether a frozen ruin still has at least one solid voxel supporting it
    /// from below. For each voxel position in the chunk, we convert to the chunk's
    /// current world-space position and check the voxel directly underneath. If any
    /// support is found, returns true immediately.
    /// </summary>
    private static bool HasSupportBelow(FallingChunk chunk, VoxelWorld world)
    {
        Vector3 chunkWorldPos = chunk.GlobalPosition;

        // The chunk's voxel positions are stored in microvoxel coordinates from when
        // they were originally removed from the world. The chunk's mesh is built relative
        // to their center of mass, and GlobalPosition IS that center of mass. So the
        // original world positions of each voxel were: MicrovoxelToWorld(voxelPos).
        // After settling, the chunk may have moved. The displacement from original
        // center of mass to current position tells us where the voxels are now.

        // Compute original center of mass in world space
        Vector3 originalCenter = Vector3.Zero;
        for (int i = 0; i < chunk._voxelPositions.Count; i++)
        {
            originalCenter += MathHelpers.MicrovoxelToWorld(chunk._voxelPositions[i]);
        }
        originalCenter /= chunk._voxelPositions.Count;

        Vector3 displacement = chunkWorldPos - originalCenter;

        for (int i = 0; i < chunk._voxelPositions.Count; i++)
        {
            // Current world position of this voxel
            Vector3 voxelWorld = MathHelpers.MicrovoxelToWorld(chunk._voxelPositions[i]) + displacement;

            // Check the voxel directly below (1 microvoxel down)
            Vector3 belowWorld = voxelWorld - new Vector3(0, GameConfig.MicrovoxelMeters, 0);
            Vector3I belowMicro = MathHelpers.WorldToMicrovoxel(belowWorld);

            if (world.GetVoxel(belowMicro).IsSolid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gradually fades out a frozen ruin over 2 seconds, then removes it.
    /// This prevents ruins from popping out of existence.
    /// </summary>
    private void FadeOutAndCleanup()
    {
        // Find mesh child and reduce opacity
        foreach (Node child in GetChildren())
        {
            if (child is MeshInstance3D meshInst)
            {
                Material? surfMat = meshInst.MaterialOverride ?? meshInst.GetSurfaceOverrideMaterial(0);
                if (surfMat is ShaderMaterial shaderMat)
                {
                    // For shader materials, just clean up — we can't easily fade them
                    ForceCleanup();
                    return;
                }
                else if (surfMat is StandardMaterial3D stdMat)
                {
                    if (stdMat.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                    {
                        stdMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    }
                    float fadeProgress = (_frozenAge - FrozenRuinLifetime) / 2.0f;  // 2 second fade
                    if (fadeProgress >= 1.0f)
                    {
                        ForceCleanup();
                        return;
                    }
                    Color c = stdMat.AlbedoColor;
                    stdMat.AlbedoColor = new Color(c.R, c.G, c.B, 1.0f - fadeProgress);
                    return;
                }
            }
        }
        // No mesh found or non-standard material — just clean up
        ForceCleanup();
    }

    private void Shatter()
    {
        Node fxParent = GetTree().Root;
        Vector3 center = GlobalPosition;

        // Spawn material-aware debris for each voxel position
        int maxDebris = Mathf.Min(_voxelPositions.Count, 20);
        for (int i = 0; i < maxDebris; i++)
        {
            Vector3 voxelWorld = MathHelpers.MicrovoxelToWorld(_voxelPositions[i]);
            Color color = i < _voxelColors.Count ? _voxelColors[i] : new Color(0.5f, 0.4f, 0.3f);
            VoxelMaterialType matType = i < _voxelMaterials.Count ? _voxelMaterials[i] : VoxelMaterialType.Stone;
            DebrisFX.SpawnDebris(fxParent, voxelWorld, color, center, 1, matType);
        }

        // Spawn a dust cloud at the shatter point, tinted to dominant material
        VoxelMaterialType dominantMat = GetDominantMaterial();
        DustFX.Spawn(fxParent, center, Mathf.Clamp(_voxelPositions.Count * 0.15f, 0.5f, 3f), dominantMat);

        ForceCleanup();
    }

    /// <summary>
    /// Spawns a small crumble particle shed from the chunk while falling.
    /// Picks a random voxel from the chunk and spawns a tiny debris piece at its
    /// world-space position (translated by the chunk's current transform).
    /// </summary>
    private void SpawnCrumbleParticle()
    {
        if (_voxelPositions.Count == 0)
        {
            return;
        }

        Node? fxParent = GetTree()?.Root;
        if (fxParent == null)
        {
            return;
        }

        // Pick a random surface voxel from the chunk
        int idx = (int)GD.Randi() % _voxelPositions.Count;
        Color color = idx < _voxelColors.Count ? _voxelColors[idx] : new Color(0.5f, 0.4f, 0.3f);
        VoxelMaterialType matType = idx < _voxelMaterials.Count ? _voxelMaterials[idx] : VoxelMaterialType.Stone;

        // Compute the world position of this voxel within the chunk's current transform
        Vector3 localPos = MathHelpers.MicrovoxelToWorld(_voxelPositions[idx]) - GlobalPosition;
        // The chunk has moved, so approximate the world position from the transform
        // Use a slight random offset for natural scatter
        Vector3 worldPos = GlobalPosition + localPos.Rotated(Vector3.Up, Rotation.Y) + new Vector3(
            (float)GD.RandRange(-0.1, 0.1),
            (float)GD.RandRange(-0.1, 0.1),
            (float)GD.RandRange(-0.1, 0.1));

        DebrisFX.SpawnDebris(fxParent, worldPos, color, GlobalPosition, 1, matType);
    }

    /// <summary>
    /// Spawns a dust cloud at the chunk's position when it first hits the ground.
    /// </summary>
    private void SpawnImpactDust()
    {
        Node? fxParent = GetTree()?.Root;
        if (fxParent == null)
        {
            return;
        }

        VoxelMaterialType dominantMat = GetDominantMaterial();
        float dustRadius = Mathf.Clamp(_voxelPositions.Count * 0.1f, 0.3f, 2f);
        DustFX.Spawn(fxParent, GlobalPosition, dustRadius, dominantMat);
    }

    /// <summary>
    /// Finds the most common material type in this chunk for tinting dust.
    /// </summary>
    private VoxelMaterialType GetDominantMaterial()
    {
        if (_voxelMaterials.Count == 0)
        {
            return VoxelMaterialType.Stone;
        }

        // Simple: just return the first material for small chunks,
        // scan for most common in larger ones
        if (_voxelMaterials.Count <= 3)
        {
            return _voxelMaterials[0];
        }

        VoxelMaterialType best = _voxelMaterials[0];
        int bestCount = 0;
        Dictionary<VoxelMaterialType, int> counts = new();
        for (int i = 0; i < _voxelMaterials.Count; i++)
        {
            VoxelMaterialType m = _voxelMaterials[i];
            counts.TryGetValue(m, out int c);
            counts[m] = c + 1;
            if (c + 1 > bestCount)
            {
                bestCount = c + 1;
                best = m;
            }
        }
        return best;
    }

    private void ForceCleanup()
    {
        ActiveChunks.Remove(this);
        QueueFree();
    }

    public override void _ExitTree()
    {
        ActiveChunks.Remove(this);
    }

    private static void CleanupExcess()
    {
        // Remove any chunks that have been freed
        for (int i = ActiveChunks.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(ActiveChunks[i]))
            {
                ActiveChunks.RemoveAt(i);
            }
        }
    }

    // ── Connected component grouping ────────────────────────────────────

    /// <summary>
    /// Groups a set of disconnected voxel positions into connected components using
    /// material-aware BFS. Voxels of different materials are treated as separate
    /// components so chunks break along natural material boundaries (e.g. a brick
    /// wall separates from the wood floor it sits on). This produces more realistic
    /// destruction where different construction materials fracture apart.
    /// Components larger than MaxComponentSize are split into sub-chunks so that
    /// individual falling pieces remain a manageable size for physics and visuals.
    /// </summary>
    public static List<List<Vector3I>> GroupConnectedComponents(HashSet<Vector3I> disconnected, VoxelWorld? world = null)
    {
        List<List<Vector3I>> components = new();
        HashSet<Vector3I> visited = new();

        // Build a material lookup if world is available for material-boundary splitting.
        // When world is null, fall back to connectivity-only grouping.
        Dictionary<Vector3I, VoxelMaterialType>? materialMap = null;
        if (world != null)
        {
            materialMap = new Dictionary<Vector3I, VoxelMaterialType>(disconnected.Count);
            foreach (Vector3I pos in disconnected)
            {
                VoxelValue voxel = world.GetVoxel(pos);
                materialMap[pos] = voxel.Material;
            }
        }

        foreach (Vector3I start in disconnected)
        {
            if (visited.Contains(start))
            {
                continue;
            }

            // BFS from this voxel, only connecting to same-material neighbors
            // when material info is available.
            VoxelMaterialType startMat = VoxelMaterialType.Air;
            if (materialMap != null)
            {
                materialMap.TryGetValue(start, out startMat);
            }

            List<Vector3I> component = new();
            Queue<Vector3I> frontier = new();
            frontier.Enqueue(start);
            visited.Add(start);

            while (frontier.Count > 0)
            {
                Vector3I current = frontier.Dequeue();
                component.Add(current);

                for (int d = 0; d < FaceDirections.Length; d++)
                {
                    Vector3I neighbor = current + FaceDirections[d];
                    if (!disconnected.Contains(neighbor) || visited.Contains(neighbor))
                    {
                        continue;
                    }

                    // Material-boundary check: only group same-material voxels together.
                    // Different materials break into separate chunks for natural fracturing.
                    if (materialMap != null)
                    {
                        materialMap.TryGetValue(neighbor, out VoxelMaterialType neighborMat);
                        if (neighborMat != startMat)
                        {
                            continue; // Different material — will be picked up as a separate component
                        }
                    }

                    visited.Add(neighbor);
                    frontier.Enqueue(neighbor);
                }
            }

            // Split oversized components into sub-chunks for better physics behavior.
            // Large monolithic chunks look unnatural and are expensive to simulate.
            if (component.Count > MaxComponentSize)
            {
                List<List<Vector3I>> subChunks = SplitComponent(component);
                components.AddRange(subChunks);
            }
            else
            {
                components.Add(component);
            }
        }

        return components;
    }

    /// <summary>
    /// Splits an oversized connected component into smaller sub-chunks by spatial partitioning.
    /// Divides the bounding box along the longest axis, then re-runs BFS connectivity
    /// within each half so that disconnected islands within a partition become separate
    /// chunks. This prevents inconsistent chunk sizing where spatially-close but
    /// structurally-separate voxel groups are merged into a single physics body.
    /// </summary>
    private static List<List<Vector3I>> SplitComponent(List<Vector3I> component)
    {
        List<List<Vector3I>> result = new();

        // Find bounding box center to split around
        Vector3I min = component[0];
        Vector3I max = component[0];
        for (int i = 1; i < component.Count; i++)
        {
            Vector3I p = component[i];
            if (p.X < min.X) min = new Vector3I(p.X, min.Y, min.Z);
            if (p.Y < min.Y) min = new Vector3I(min.X, p.Y, min.Z);
            if (p.Z < min.Z) min = new Vector3I(min.X, min.Y, p.Z);
            if (p.X > max.X) max = new Vector3I(p.X, max.Y, max.Z);
            if (p.Y > max.Y) max = new Vector3I(max.X, p.Y, max.Z);
            if (p.Z > max.Z) max = new Vector3I(max.X, max.Y, p.Z);
        }

        // Find the longest axis to split along for balanced partitions
        Vector3I size = max - min;
        int splitAxis; // 0=X, 1=Y, 2=Z
        int splitValue;
        if (size.X >= size.Y && size.X >= size.Z)
        {
            splitAxis = 0;
            splitValue = (min.X + max.X) / 2;
        }
        else if (size.Y >= size.X && size.Y >= size.Z)
        {
            splitAxis = 1;
            splitValue = (min.Y + max.Y) / 2;
        }
        else
        {
            splitAxis = 2;
            splitValue = (min.Z + max.Z) / 2;
        }

        // Partition voxels into two halves
        List<Vector3I> halfA = new();
        List<Vector3I> halfB = new();
        for (int i = 0; i < component.Count; i++)
        {
            Vector3I p = component[i];
            int coord = splitAxis switch { 0 => p.X, 1 => p.Y, _ => p.Z };
            if (coord <= splitValue)
                halfA.Add(p);
            else
                halfB.Add(p);
        }

        // Re-run BFS connectivity within each half to find disconnected sub-islands.
        // Without this, a spatial partition can merge structurally-separate groups
        // into one chunk, causing inconsistent sizes and weird physics.
        ProcessHalf(halfA, result);
        ProcessHalf(halfB, result);

        return result;
    }

    /// <summary>
    /// Processes one half of a spatial split: re-runs BFS to find connected sub-islands,
    /// then recursively splits any that are still oversized.
    /// </summary>
    private static void ProcessHalf(List<Vector3I> half, List<List<Vector3I>> result)
    {
        if (half.Count == 0)
        {
            return;
        }

        // Re-run BFS connectivity to break apart disconnected groups within this half
        HashSet<Vector3I> halfSet = new HashSet<Vector3I>(half);
        HashSet<Vector3I> visited = new();

        foreach (Vector3I start in half)
        {
            if (visited.Contains(start))
            {
                continue;
            }

            List<Vector3I> island = new();
            Queue<Vector3I> frontier = new();
            frontier.Enqueue(start);
            visited.Add(start);

            while (frontier.Count > 0)
            {
                Vector3I current = frontier.Dequeue();
                island.Add(current);

                for (int d = 0; d < FaceDirections.Length; d++)
                {
                    Vector3I neighbor = current + FaceDirections[d];
                    if (halfSet.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            if (island.Count > MaxComponentSize)
            {
                result.AddRange(SplitComponent(island));
            }
            else
            {
                result.Add(island);
            }
        }
    }
}
