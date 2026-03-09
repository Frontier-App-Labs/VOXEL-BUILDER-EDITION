using Godot;
using System.Collections.Generic;

namespace VoxelSiege.FX;

/// <summary>
/// Temporary scorch marks placed on surfaces after explosions.
/// Fades out over time and auto-despawns. Uses simple quad meshes
/// offset slightly from surfaces to avoid z-fighting.
/// </summary>
public partial class ImpactDecals : Node3D
{
    private static readonly List<DecalEntry> _activeDecals = new();
    private static readonly Queue<MeshInstance3D> _pool = new();
    private static ImpactDecals? _instance;

    private const float DecalLifetime = 10f;
    private const float FadeStartTime = 6f;
    private const float SurfaceOffset = 0.01f;
    private const int MaxDecals = 50;

    private struct DecalEntry
    {
        public MeshInstance3D Mesh;
        public StandardMaterial3D Material;
        public double SpawnTime;
        public float OriginalAlpha;
    }

    public override void _Ready()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
            _activeDecals.Clear();
            _pool.Clear();
        }
    }

    /// <summary>
    /// Clears all active decals and returns them to the pool.
    /// Call when transitioning between game phases.
    /// </summary>
    public static void ClearAll()
    {
        // Return all active decals to pool
        for (int i = _activeDecals.Count - 1; i >= 0; i--)
        {
            DecalEntry entry = _activeDecals[i];
            entry.Mesh.Visible = false;
            _pool.Enqueue(entry.Mesh);
        }
        _activeDecals.Clear();
    }

    /// <summary>
    /// Spawns a scorch mark decal on a surface.
    /// </summary>
    public static void Spawn(Node parent, Vector3 position, Vector3 normal, float radius)
    {
        ImpactDecals manager = GetOrCreateManager(parent);
        manager.SpawnInternal(position, normal, radius);
    }

    private static ImpactDecals GetOrCreateManager(Node parent)
    {
        if (_instance != null && IsInstanceValid(_instance))
        {
            return _instance;
        }

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            foreach (Node node in tree.GetNodesInGroup("DecalManager"))
            {
                if (node is ImpactDecals existing)
                {
                    _instance = existing;
                    return existing;
                }
            }
        }

        ImpactDecals manager = new ImpactDecals();
        manager.Name = "ImpactDecalsManager";
        manager.AddToGroup("DecalManager");
        parent.GetTree()!.Root.AddChild(manager);
        _instance = manager;
        return manager;
    }

    private MeshInstance3D AcquireDecalMesh()
    {
        if (_pool.Count > 0)
        {
            MeshInstance3D pooled = _pool.Dequeue();
            pooled.Visible = true;
            return pooled;
        }

        // If at capacity, recycle oldest active decal
        if (_activeDecals.Count >= MaxDecals)
        {
            DecalEntry oldest = _activeDecals[0];
            _activeDecals.RemoveAt(0);
            oldest.Mesh.Visible = true;
            return oldest.Mesh;
        }

        // Create new decal mesh
        MeshInstance3D mesh = new MeshInstance3D();
        QuadMesh quad = new QuadMesh();
        quad.Size = new Vector2(1f, 1f); // Will be resized per-spawn

        StandardMaterial3D mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.08f, 0.06f, 0.04f, 0.7f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.NoDepthTest = false;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        quad.Material = mat;

        mesh.Mesh = quad;
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(mesh);
        return mesh;
    }

    private void SpawnInternal(Vector3 position, Vector3 normal, float radius)
    {
        float decalSize = Mathf.Clamp(radius * 1.2f, 0.3f, 4f);

        MeshInstance3D mesh = AcquireDecalMesh();

        // Resize the quad
        if (mesh.Mesh is QuadMesh quad)
        {
            quad.Size = new Vector2(decalSize, decalSize);
        }

        // Reset material alpha
        StandardMaterial3D mat = (StandardMaterial3D)((QuadMesh)mesh.Mesh).Material!;
        mat.AlbedoColor = new Color(0.08f, 0.06f, 0.04f, 0.7f);

        // Position slightly offset from surface to avoid z-fighting
        mesh.GlobalPosition = position + (normal.Normalized() * SurfaceOffset);

        // Orient the quad to face along the surface normal
        if (normal.Normalized() != Vector3.Up && normal.Normalized() != Vector3.Down)
        {
            mesh.LookAt(position + normal, Vector3.Up);
        }
        else
        {
            // For horizontal surfaces, use Forward as the up hint
            mesh.LookAt(position + normal, Vector3.Forward);
        }

        // Add slight random rotation around the normal for variety
        mesh.RotateObjectLocal(Vector3.Forward, (float)GD.RandRange(0, Mathf.Tau));

        DecalEntry entry = new DecalEntry
        {
            Mesh = mesh,
            Material = mat,
            SpawnTime = Time.GetTicksMsec() / 1000.0,
            OriginalAlpha = mat.AlbedoColor.A
        };
        _activeDecals.Add(entry);
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;

        for (int i = _activeDecals.Count - 1; i >= 0; i--)
        {
            DecalEntry entry = _activeDecals[i];
            double elapsed = now - entry.SpawnTime;

            if (elapsed >= DecalLifetime)
            {
                // Return to pool for reuse instead of freeing
                entry.Mesh.Visible = false;
                _pool.Enqueue(entry.Mesh);
                _activeDecals.RemoveAt(i);
                continue;
            }

            // Fade out during the last portion of lifetime
            if (elapsed >= FadeStartTime)
            {
                float fadeProgress = (float)((elapsed - FadeStartTime) / (DecalLifetime - FadeStartTime));
                float alpha = Mathf.Lerp(entry.OriginalAlpha, 0f, fadeProgress);
                Color c = entry.Material.AlbedoColor;
                entry.Material.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
            }
        }
    }
}
