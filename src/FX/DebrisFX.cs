using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Spawns material-aware debris from destroyed voxels. Debris shapes vary by material:
///   - Default (Stone/Brick/Concrete/etc.): chunky cubes
///   - Glass/Ice: flat, transparent shard-like particles
///   - Wood/Bark: elongated splinter-shaped particles
///   - Metal: small dense cubes with metallic bounce
///   - Sand/Dirt: tiny particles with fast settle
///
/// Physics debris is capped at MaxDebrisObjects (30). Beyond that, visual-only debris
/// (simple MeshInstance3D with manual gravity) is used up to MaxVisualDebris (80).
/// Settled debris persists on the ground as ruins (MaxRuinObjects=150).
/// </summary>
public partial class DebrisFX : Node3D
{
    private static DebrisFX? _instance;
    private readonly Queue<RigidBody3D> _pool = new();
    private readonly List<DebrisEntry> _active = new();
    private readonly Queue<Node3D> _visualPool = new();
    private readonly List<VisualDebrisEntry> _activeVisual = new();
    private readonly Queue<RuinEntry> _ruins = new();
    private readonly Queue<VisualRuinEntry> _visualRuins = new();
    private int _totalAllocated;
    private int _totalVisualAllocated;

    // Shared resources to avoid per-body allocations
    private static PhysicsMaterial? _sharedPhysicsMaterial;
    private static PhysicsMaterial? _sharedGlassPhysicsMaterial;
    private static PhysicsMaterial? _sharedMetalPhysicsMaterial;

    // Spawn queuing: max 5 debris spawns per frame
    private const int MaxSpawnsPerFrame = 5;
    private int _spawnsThisFrame;
    private readonly Queue<QueuedSpawn> _spawnQueue = new();

    // Settling thresholds
    private const float SettleVelocityThreshold = 0.5f;
    private const float SettleTimeRequired = 0.3f;
    private const float VisualGroundThreshold = 0.05f;

    /// <summary>
    /// Describes the debris shape category based on material type.
    /// </summary>
    private enum DebrisShape
    {
        Cube,       // Default: stone, brick, concrete, obsidian, foundation, leaves
        Shard,      // Glass, ice: flat transparent angular pieces
        Splinter,   // Wood, bark: elongated rectangular pieces
        MetalChunk, // Metal types: small dense cubes with high bounce
        Granular,   // Sand, dirt: tiny particles that settle quickly
    }

    private struct DebrisEntry
    {
        public RigidBody3D Body;
        public MeshInstance3D Mesh;
        public double SpawnTime;
        public double Lifetime;
        public bool IsSettled;
        public float SettleTimer;
        public DebrisShape Shape;
    }

    private struct VisualDebrisEntry
    {
        public Node3D Root;
        public MeshInstance3D Mesh;
        public double SpawnTime;
        public double Lifetime;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public bool IsSettled;
        public float SettleTimer;
        public DebrisShape Shape;
    }

    private struct RuinEntry
    {
        public RigidBody3D Body;
        public MeshInstance3D Mesh;
    }

    private struct VisualRuinEntry
    {
        public Node3D Root;
        public MeshInstance3D Mesh;
    }

    private struct QueuedSpawn
    {
        public Vector3 Position;
        public Color Color;
        public Vector3 ExplosionCenter;
        public int Count;
        public VoxelMaterialType Material;
    }

    // ── Shared physics materials ────────────────────────────────────────

    private static PhysicsMaterial SharedPhysicsMaterial
    {
        get
        {
            _sharedPhysicsMaterial ??= new PhysicsMaterial
            {
                Bounce = 0.3f,
                Friction = 0.7f
            };
            return _sharedPhysicsMaterial;
        }
    }

    private static PhysicsMaterial SharedGlassPhysicsMaterial
    {
        get
        {
            _sharedGlassPhysicsMaterial ??= new PhysicsMaterial
            {
                Bounce = 0.15f,
                Friction = 0.4f
            };
            return _sharedGlassPhysicsMaterial;
        }
    }

    private static PhysicsMaterial SharedMetalPhysicsMaterial
    {
        get
        {
            _sharedMetalPhysicsMaterial ??= new PhysicsMaterial
            {
                Bounce = 0.5f,
                Friction = 0.5f
            };
            return _sharedMetalPhysicsMaterial;
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Immediately clears all active debris, visual debris, ruins, and queued spawns.
    /// Call when transitioning between game phases to prevent stale debris persisting.
    /// </summary>
    public static void ClearAll()
    {
        if (_instance == null || !IsInstanceValid(_instance)) return;

        // Clear queued spawns
        _instance._spawnQueue.Clear();

        // Return all active physics debris to pool
        for (int i = _instance._active.Count - 1; i >= 0; i--)
        {
            _instance.ReturnDebris(_instance._active[i]);
        }
        _instance._active.Clear();

        // Return all active visual debris to pool
        for (int i = _instance._activeVisual.Count - 1; i >= 0; i--)
        {
            _instance.ReturnVisualDebris(_instance._activeVisual[i]);
        }
        _instance._activeVisual.Clear();

        // Return all ruins to pool
        while (_instance._ruins.Count > 0)
        {
            _instance.ReturnRuinToPool(_instance._ruins.Dequeue());
        }
        while (_instance._visualRuins.Count > 0)
        {
            _instance.ReturnVisualRuinToPool(_instance._visualRuins.Dequeue());
        }
    }

    /// <summary>
    /// Spawns debris cubes flying outward from an explosion center.
    /// Backwards-compatible overload without material type (defaults to Stone).
    /// </summary>
    public static void SpawnDebris(Node parent, Vector3 position, Color materialColor, Vector3 explosionCenter, int count = 3)
    {
        SpawnDebris(parent, position, materialColor, explosionCenter, count, VoxelMaterialType.Stone);
    }

    /// <summary>
    /// Spawns material-aware debris flying outward from an explosion center.
    /// Shape, size, physics, and transparency vary based on material type.
    /// </summary>
    public static void SpawnDebris(Node parent, Vector3 position, Color materialColor, Vector3 explosionCenter, int count, VoxelMaterialType material)
    {
        DebrisFX manager = GetOrCreateManager(parent);
        manager.EnqueueSpawn(position, materialColor, explosionCenter, count, material);

        // Play retro debris ping sound
        Core.AudioDirector.Instance?.PlaySFX("debris_impact", position);
    }

    // ── Manager singleton ───────────────────────────────────────────────

    private static DebrisFX GetOrCreateManager(Node parent)
    {
        if (_instance != null && IsInstanceValid(_instance))
        {
            return _instance;
        }

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            foreach (Node node in tree.GetNodesInGroup("DebrisManager"))
            {
                if (node is DebrisFX existing)
                {
                    _instance = existing;
                    return existing;
                }
            }
        }

        DebrisFX manager = new DebrisFX();
        manager.Name = "DebrisFXManager";
        manager.AddToGroup("DebrisManager");
        parent.GetTree()!.Root.AddChild(manager);
        _instance = manager;
        return manager;
    }

    // ── Material to shape mapping ───────────────────────────────────────

    private static DebrisShape GetShapeForMaterial(VoxelMaterialType material)
    {
        return material switch
        {
            VoxelMaterialType.Glass or VoxelMaterialType.Ice => DebrisShape.Shard,
            VoxelMaterialType.Wood or VoxelMaterialType.Bark => DebrisShape.Splinter,
            VoxelMaterialType.Metal or VoxelMaterialType.ReinforcedSteel or VoxelMaterialType.ArmorPlate => DebrisShape.MetalChunk,
            VoxelMaterialType.Sand or VoxelMaterialType.Dirt => DebrisShape.Granular,
            _ => DebrisShape.Cube,
        };
    }

    /// <summary>
    /// Returns a randomized mesh size for debris based on material shape.
    /// Glass shards are flat, wood splinters are elongated, etc.
    /// </summary>
    private static Vector3 GetDebrisSize(DebrisShape shape)
    {
        return shape switch
        {
            // Glass shards: flat, angular pieces (thin Y dimension)
            DebrisShape.Shard => new Vector3(
                (float)GD.RandRange(0.04, 0.12),
                (float)GD.RandRange(0.01, 0.03),
                (float)GD.RandRange(0.06, 0.14)),

            // Wood splinters: elongated rectangular pieces (long Z dimension)
            DebrisShape.Splinter => new Vector3(
                (float)GD.RandRange(0.02, 0.05),
                (float)GD.RandRange(0.02, 0.04),
                (float)GD.RandRange(0.10, 0.25)),

            // Metal chunks: small dense cubes
            DebrisShape.MetalChunk => new Vector3(
                (float)GD.RandRange(0.04, 0.10),
                (float)GD.RandRange(0.04, 0.10),
                (float)GD.RandRange(0.04, 0.10)),

            // Sand/dirt granules: tiny bits
            DebrisShape.Granular => new Vector3(
                (float)GD.RandRange(0.02, 0.06),
                (float)GD.RandRange(0.02, 0.06),
                (float)GD.RandRange(0.02, 0.06)),

            // Default cubes: standard chunky voxel debris
            _ => Vector3.One * (float)GD.RandRange(0.05, 0.15),
        };
    }

    /// <summary>
    /// Returns physics tuning parameters for different debris shapes.
    /// </summary>
    private static (float impulseScale, float angularScale, float gravityScale, float mass) GetPhysicsParams(DebrisShape shape)
    {
        return shape switch
        {
            DebrisShape.Shard => (0.8f, 12f, 1.2f, 0.05f),      // Light, spinny glass
            DebrisShape.Splinter => (1.0f, 10f, 1.3f, 0.08f),    // Tumbling wood pieces
            DebrisShape.MetalChunk => (0.6f, 6f, 2.0f, 0.3f),    // Heavy, less air time
            DebrisShape.Granular => (1.2f, 15f, 1.8f, 0.03f),    // Scattered fast
            _ => (1.0f, 8f, 1.5f, 0.1f),                         // Default cubes
        };
    }

    /// <summary>
    /// Creates a StandardMaterial3D configured for the given debris shape and color.
    /// Glass/Ice get transparency; Metal gets slight metallic look; others are opaque.
    /// </summary>
    private static StandardMaterial3D CreateDebrisMaterial(DebrisShape shape, Color color)
    {
        StandardMaterial3D mat = new StandardMaterial3D();

        switch (shape)
        {
            case DebrisShape.Shard:
                // Transparent glass/ice shards
                mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.55f);
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.Metallic = 0.1f;
                mat.Roughness = 0.15f;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                break;

            case DebrisShape.MetalChunk:
                // Slightly metallic look
                mat.AlbedoColor = color;
                mat.Metallic = 0.4f;
                mat.Roughness = 0.45f;
                break;

            case DebrisShape.Splinter:
                // Warm wood with slight color variation
                float rVariation = (float)GD.RandRange(-0.05, 0.05);
                mat.AlbedoColor = new Color(
                    Mathf.Clamp(color.R + rVariation, 0f, 1f),
                    Mathf.Clamp(color.G + rVariation * 0.7f, 0f, 1f),
                    Mathf.Clamp(color.B + rVariation * 0.3f, 0f, 1f));
                mat.Roughness = 0.8f;
                break;

            default:
                mat.AlbedoColor = color;
                mat.Roughness = 0.7f;
                break;
        }

        return mat;
    }

    private static PhysicsMaterial GetPhysicsMaterialForShape(DebrisShape shape)
    {
        return shape switch
        {
            DebrisShape.Shard => SharedGlassPhysicsMaterial,
            DebrisShape.MetalChunk => SharedMetalPhysicsMaterial,
            _ => SharedPhysicsMaterial,
        };
    }

    // ── Spawn queuing ───────────────────────────────────────────────────

    private void EnqueueSpawn(Vector3 position, Color color, Vector3 explosionCenter, int count, VoxelMaterialType material)
    {
        _spawnQueue.Enqueue(new QueuedSpawn
        {
            Position = position,
            Color = color,
            ExplosionCenter = explosionCenter,
            Count = count,
            Material = material
        });
    }

    private void SpawnDebrisInternal(Vector3 position, Color materialColor, Vector3 explosionCenter, int count, VoxelMaterialType material)
    {
        Vector3 outwardDir = (position - explosionCenter).Normalized();
        if (outwardDir.LengthSquared() < 0.01f)
        {
            outwardDir = Vector3.Up;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        DebrisShape shape = GetShapeForMaterial(material);
        (float impulseScale, float angularScale, float gravityScale, float mass) = GetPhysicsParams(shape);

        for (int i = 0; i < count; i++)
        {
            if (_spawnsThisFrame >= MaxSpawnsPerFrame)
            {
                // Re-queue remaining
                if (i < count)
                {
                    _spawnQueue.Enqueue(new QueuedSpawn
                    {
                        Position = position,
                        Color = materialColor,
                        ExplosionCenter = explosionCenter,
                        Count = count - i,
                        Material = material
                    });
                }
                return;
            }

            Vector3 debrisSize = GetDebrisSize(shape);
            Vector3 spawnPos = position + new Vector3(
                (float)GD.RandRange(-0.2, 0.2),
                (float)GD.RandRange(0, 0.3),
                (float)GD.RandRange(-0.2, 0.2));
            Vector3 rotation = new Vector3(
                (float)GD.RandRange(0, Mathf.Tau),
                (float)GD.RandRange(0, Mathf.Tau),
                (float)GD.RandRange(0, Mathf.Tau));
            Vector3 impulse = outwardDir * (float)GD.RandRange(2.0, 6.0) * impulseScale;
            impulse += new Vector3(
                (float)GD.RandRange(-2.0, 2.0),
                (float)GD.RandRange(1.5, 5.0),
                (float)GD.RandRange(-2.0, 2.0));
            Vector3 angVel = new Vector3(
                (float)GD.RandRange(-angularScale, angularScale),
                (float)GD.RandRange(-angularScale, angularScale),
                (float)GD.RandRange(-angularScale, angularScale));

            // Lifetime varies by material -- granular settles fast, glass is short
            float lifetime = shape switch
            {
                DebrisShape.Granular => GameConfig.DebrisDespawnTime * 0.6f,
                DebrisShape.Shard => GameConfig.DebrisDespawnTime * 0.8f,
                _ => GameConfig.DebrisDespawnTime,
            };

            // Try physics debris first (up to MaxDebrisObjects)
            if (_active.Count < GameConfig.MaxDebrisObjects)
            {
                DebrisEntry entry = AcquireDebris(materialColor, shape, debrisSize, gravityScale, mass);
                entry.SpawnTime = now;
                entry.Lifetime = lifetime;
                entry.IsSettled = false;
                entry.SettleTimer = 0f;
                entry.Shape = shape;

                entry.Body.GlobalPosition = spawnPos;
                entry.Body.Rotation = rotation;
                entry.Body.LinearVelocity = Vector3.Zero;
                entry.Body.AngularVelocity = angVel;
                entry.Body.ApplyImpulse(impulse * mass * 80f);
                entry.Body.Visible = true;
                entry.Body.Freeze = false;

                _active.Add(entry);
            }
            // Fall back to visual-only debris (no physics cost)
            else if (_activeVisual.Count < GameConfig.MaxVisualDebris)
            {
                VisualDebrisEntry vEntry = AcquireVisualDebris(materialColor, shape, debrisSize);
                vEntry.SpawnTime = now;
                vEntry.Lifetime = lifetime;
                vEntry.Velocity = impulse * 0.8f;
                vEntry.AngularVelocity = angVel;
                vEntry.IsSettled = false;
                vEntry.SettleTimer = 0f;
                vEntry.Shape = shape;

                vEntry.Root.GlobalPosition = spawnPos;
                vEntry.Root.Rotation = rotation;
                vEntry.Root.Visible = true;

                _activeVisual.Add(vEntry);
            }
            else
            {
                break;
            }

            _spawnsThisFrame++;
        }
    }

    // ── Process ─────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        float dt = (float)delta;

        // Reset per-frame spawn counter and process queued spawns
        _spawnsThisFrame = 0;
        while (_spawnQueue.Count > 0 && _spawnsThisFrame < MaxSpawnsPerFrame)
        {
            QueuedSpawn queued = _spawnQueue.Dequeue();
            SpawnDebrisInternal(queued.Position, queued.Color, queued.ExplosionCenter, queued.Count, queued.Material);
        }

        // Update physics debris
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            DebrisEntry entry = _active[i];
            double elapsed = now - entry.SpawnTime;
            double remaining = entry.Lifetime - elapsed;

            // Check if the debris has settled (low velocity for a sustained period)
            if (!entry.IsSettled)
            {
                float velSq = entry.Body.LinearVelocity.LengthSquared();
                if (velSq < SettleVelocityThreshold * SettleVelocityThreshold)
                {
                    entry.SettleTimer += dt;
                    if (entry.SettleTimer >= SettleTimeRequired)
                    {
                        entry.IsSettled = true;
                    }
                }
                else
                {
                    entry.SettleTimer = 0f;
                }
                _active[i] = entry;
            }

            // Settled debris converts to a ruin instead of fading out
            if (entry.IsSettled)
            {
                ConvertToRuin(entry);
                _active[i] = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                continue;
            }

            if (remaining <= 0)
            {
                // Only airborne/unsettled debris expires and returns to pool
                ReturnDebris(entry);
                _active[i] = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                continue;
            }

            // Fade out during last 1.5 seconds -- only for airborne debris (not settled)
            if (remaining < 1.5 && entry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
            {
                if (mat.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                }
                float alpha = (float)(remaining / 1.5);
                Color c = mat.AlbedoColor;
                mat.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
            }
        }

        // Update visual-only debris (manual gravity simulation)
        for (int i = _activeVisual.Count - 1; i >= 0; i--)
        {
            VisualDebrisEntry vEntry = _activeVisual[i];
            double elapsed = now - vEntry.SpawnTime;
            double remaining = vEntry.Lifetime - elapsed;

            // Gravity varies per shape -- heavier materials fall faster
            float gravity = vEntry.Shape switch
            {
                DebrisShape.Shard => 9.8f * 1.2f,
                DebrisShape.MetalChunk => 9.8f * 2.0f,
                DebrisShape.Granular => 9.8f * 1.8f,
                _ => 9.8f * 1.5f,
            };

            // Check if visual debris has settled on the ground
            if (!vEntry.IsSettled)
            {
                if (vEntry.Root.GlobalPosition.Y <= VisualGroundThreshold &&
                    vEntry.Velocity.LengthSquared() < SettleVelocityThreshold * SettleVelocityThreshold)
                {
                    vEntry.SettleTimer += dt;
                    if (vEntry.SettleTimer >= SettleTimeRequired)
                    {
                        vEntry.IsSettled = true;
                    }
                }
                else
                {
                    vEntry.SettleTimer = 0f;
                }
            }

            // Settled visual debris converts to a visual ruin
            if (vEntry.IsSettled)
            {
                ConvertToVisualRuin(vEntry);
                _activeVisual[i] = _activeVisual[_activeVisual.Count - 1];
                _activeVisual.RemoveAt(_activeVisual.Count - 1);
                continue;
            }

            if (remaining <= 0)
            {
                ReturnVisualDebris(vEntry);
                _activeVisual[i] = _activeVisual[_activeVisual.Count - 1];
                _activeVisual.RemoveAt(_activeVisual.Count - 1);
                continue;
            }

            // Apply gravity to velocity
            vEntry.Velocity += new Vector3(0, -gravity * dt, 0);
            vEntry.Root.GlobalPosition += vEntry.Velocity * dt;
            vEntry.Root.Rotation += vEntry.AngularVelocity * dt;

            // Simple ground clamp (y=0) with material-appropriate bounce
            if (vEntry.Root.GlobalPosition.Y < 0)
            {
                Vector3 pos = vEntry.Root.GlobalPosition;
                pos.Y = 0;
                vEntry.Root.GlobalPosition = pos;

                float bounceFactor = vEntry.Shape switch
                {
                    DebrisShape.Shard => 0.15f,      // Glass barely bounces
                    DebrisShape.MetalChunk => 0.45f,  // Metal pings and clinks
                    DebrisShape.Granular => 0.1f,     // Sand/dirt settles fast
                    _ => 0.3f,                        // Standard bounce
                };
                vEntry.Velocity = new Vector3(
                    vEntry.Velocity.X * 0.5f,
                    -vEntry.Velocity.Y * bounceFactor,
                    vEntry.Velocity.Z * 0.5f);

                // Reduce angular velocity on bounce
                vEntry.AngularVelocity *= 0.5f;
            }

            // Fade out during last 1.5 seconds -- only for airborne debris
            if (remaining < 1.5 && vEntry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D vMat)
            {
                if (vMat.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                {
                    vMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                }
                float alpha = (float)(remaining / 1.5);
                Color c = vMat.AlbedoColor;
                vMat.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
            }

            _activeVisual[i] = vEntry;
        }

        // Enforce ruin cap: remove oldest ruins when limit exceeded
        int totalRuins = _ruins.Count + _visualRuins.Count;
        while (totalRuins > GameConfig.MaxRuinObjects)
        {
            if (_ruins.Count > 0)
            {
                RuinEntry oldest = _ruins.Dequeue();
                ReturnRuinToPool(oldest);
            }
            else if (_visualRuins.Count > 0)
            {
                VisualRuinEntry oldest = _visualRuins.Dequeue();
                ReturnVisualRuinToPool(oldest);
            }
            totalRuins = _ruins.Count + _visualRuins.Count;
        }
    }

    // ── Ruin conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Converts a settled physics debris entry into a permanent ruin.
    /// The body is frozen and removed from collision processing but remains visible.
    /// Glass ruins stay translucent; other materials become fully opaque.
    /// </summary>
    private void ConvertToRuin(DebrisEntry entry)
    {
        entry.Body.Freeze = true;
        entry.Body.LinearVelocity = Vector3.Zero;
        entry.Body.AngularVelocity = Vector3.Zero;
        entry.Body.CollisionLayer = 0;
        entry.Body.CollisionMask = 0;
        entry.Body.Visible = true;

        if (entry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            if (entry.Shape == DebrisShape.Shard)
            {
                // Glass ruins stay translucent
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                Color c = mat.AlbedoColor;
                mat.AlbedoColor = new Color(c.R, c.G, c.B, 0.45f);
            }
            else
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                Color c = mat.AlbedoColor;
                mat.AlbedoColor = new Color(c.R, c.G, c.B, 1f);
            }
        }

        _ruins.Enqueue(new RuinEntry
        {
            Body = entry.Body,
            Mesh = entry.Mesh
        });
    }

    /// <summary>
    /// Converts a settled visual debris entry into a permanent visual ruin.
    /// </summary>
    private void ConvertToVisualRuin(VisualDebrisEntry entry)
    {
        entry.Root.Visible = true;

        if (entry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            if (entry.Shape == DebrisShape.Shard)
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                Color c = mat.AlbedoColor;
                mat.AlbedoColor = new Color(c.R, c.G, c.B, 0.45f);
            }
            else
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                Color c = mat.AlbedoColor;
                mat.AlbedoColor = new Color(c.R, c.G, c.B, 1f);
            }
        }

        _visualRuins.Enqueue(new VisualRuinEntry
        {
            Root = entry.Root,
            Mesh = entry.Mesh
        });
    }

    // ── Pool management ─────────────────────────────────────────────────

    private void ReturnRuinToPool(RuinEntry ruin)
    {
        ruin.Body.Visible = false;
        ruin.Body.Freeze = true;
        ruin.Body.CollisionLayer = 4;
        ruin.Body.CollisionMask = 1;
        ruin.Body.LinearVelocity = Vector3.Zero;
        ruin.Body.AngularVelocity = Vector3.Zero;
        _pool.Enqueue(ruin.Body);
    }

    private void ReturnVisualRuinToPool(VisualRuinEntry ruin)
    {
        ruin.Root.Visible = false;
        _visualPool.Enqueue(ruin.Root);
    }

    private DebrisEntry AcquireDebris(Color color, DebrisShape shape, Vector3 size, float gravityScale, float mass)
    {
        DebrisEntry entry;

        if (_pool.Count > 0)
        {
            RigidBody3D body = _pool.Dequeue();
            MeshInstance3D mesh = body.GetNode<MeshInstance3D>("Mesh");

            // Update the mesh shape and material for the new debris type
            BoxMesh boxMesh = new BoxMesh();
            boxMesh.Size = size;
            mesh.Mesh = boxMesh;

            StandardMaterial3D mat = CreateDebrisMaterial(shape, color);
            mesh.SetSurfaceOverrideMaterial(0, mat);

            // Update physics properties for this material type
            body.Mass = mass;
            body.GravityScale = gravityScale;
            body.PhysicsMaterialOverride = GetPhysicsMaterialForShape(shape);
            body.CollisionLayer = 4;
            body.CollisionMask = 1;

            // Update collision shape size
            if (body.GetNode<CollisionShape3D>("Collider") is CollisionShape3D collider &&
                collider.Shape is BoxShape3D boxShape)
            {
                boxShape.Size = size;
            }

            entry.Body = body;
            entry.Mesh = mesh;
            entry.SpawnTime = 0;
            entry.Lifetime = 0;
            entry.IsSettled = false;
            entry.SettleTimer = 0f;
            entry.Shape = shape;
            return entry;
        }

        // Create new debris RigidBody3D
        if (_totalAllocated >= GameConfig.MaxDebrisObjects)
        {
            DebrisEntry oldest = _active[0];
            _active.RemoveAt(0);
            ReturnDebris(oldest);
            return AcquireDebris(color, shape, size, gravityScale, mass);
        }

        RigidBody3D rb = new RigidBody3D();
        rb.Mass = mass;
        rb.GravityScale = gravityScale;
        rb.PhysicsMaterialOverride = GetPhysicsMaterialForShape(shape);

        // Collision shape
        CollisionShape3D newCollider = new CollisionShape3D();
        newCollider.Name = "Collider";
        BoxShape3D newBoxShape = new BoxShape3D();
        newBoxShape.Size = size;
        newCollider.Shape = newBoxShape;
        rb.AddChild(newCollider);

        // Mesh
        MeshInstance3D meshInst = new MeshInstance3D();
        meshInst.Name = "Mesh";
        BoxMesh newBoxMesh = new BoxMesh();
        newBoxMesh.Size = size;
        meshInst.Mesh = newBoxMesh;

        StandardMaterial3D material = CreateDebrisMaterial(shape, color);
        meshInst.SetSurfaceOverrideMaterial(0, material);
        rb.AddChild(meshInst);

        rb.CollisionLayer = 4;
        rb.CollisionMask = 1;

        AddChild(rb);
        _totalAllocated++;

        entry.Body = rb;
        entry.Mesh = meshInst;
        entry.SpawnTime = 0;
        entry.Lifetime = 0;
        entry.IsSettled = false;
        entry.SettleTimer = 0f;
        entry.Shape = shape;
        return entry;
    }

    private VisualDebrisEntry AcquireVisualDebris(Color color, DebrisShape shape, Vector3 size)
    {
        VisualDebrisEntry entry;

        if (_visualPool.Count > 0)
        {
            Node3D root = _visualPool.Dequeue();
            MeshInstance3D mesh = root.GetNode<MeshInstance3D>("Mesh");

            BoxMesh boxMesh = new BoxMesh();
            boxMesh.Size = size;
            mesh.Mesh = boxMesh;

            StandardMaterial3D mat = CreateDebrisMaterial(shape, color);
            mesh.SetSurfaceOverrideMaterial(0, mat);

            entry.Root = root;
            entry.Mesh = mesh;
            entry.SpawnTime = 0;
            entry.Lifetime = 0;
            entry.Velocity = Vector3.Zero;
            entry.AngularVelocity = Vector3.Zero;
            entry.IsSettled = false;
            entry.SettleTimer = 0f;
            entry.Shape = shape;
            return entry;
        }

        if (_totalVisualAllocated >= GameConfig.MaxVisualDebris)
        {
            VisualDebrisEntry oldest = _activeVisual[0];
            _activeVisual.RemoveAt(0);
            ReturnVisualDebris(oldest);
            return AcquireVisualDebris(color, shape, size);
        }

        Node3D node = new Node3D();
        MeshInstance3D meshInst = new MeshInstance3D();
        meshInst.Name = "Mesh";
        BoxMesh newBoxMesh = new BoxMesh();
        newBoxMesh.Size = size;
        meshInst.Mesh = newBoxMesh;

        StandardMaterial3D material = CreateDebrisMaterial(shape, color);
        meshInst.SetSurfaceOverrideMaterial(0, material);
        node.AddChild(meshInst);

        AddChild(node);
        _totalVisualAllocated++;

        entry.Root = node;
        entry.Mesh = meshInst;
        entry.SpawnTime = 0;
        entry.Lifetime = 0;
        entry.Velocity = Vector3.Zero;
        entry.AngularVelocity = Vector3.Zero;
        entry.IsSettled = false;
        entry.SettleTimer = 0f;
        entry.Shape = shape;
        return entry;
    }

    private void ReturnDebris(DebrisEntry entry)
    {
        entry.Body.Visible = false;
        entry.Body.Freeze = true;
        entry.Body.LinearVelocity = Vector3.Zero;
        entry.Body.AngularVelocity = Vector3.Zero;
        if (entry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
            Color c = mat.AlbedoColor;
            mat.AlbedoColor = new Color(c.R, c.G, c.B, 1f);
        }
        _pool.Enqueue(entry.Body);
    }

    private void ReturnVisualDebris(VisualDebrisEntry entry)
    {
        entry.Root.Visible = false;
        if (entry.Mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
            Color c = mat.AlbedoColor;
            mat.AlbedoColor = new Color(c.R, c.G, c.B, 1f);
        }
        _visualPool.Enqueue(entry.Root);
    }
}
