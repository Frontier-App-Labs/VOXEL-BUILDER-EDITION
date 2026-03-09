using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Singleton fire spread manager. Tracks burning voxels, ticks fire each turn,
/// spreads to neighbors, spawns particle/light effects, and destroys consumed voxels.
/// </summary>
public partial class FireSystem : Node
{
    public static FireSystem? Instance { get; private set; }

    // ── Fire state ──────────────────────────────────────────────────────
    public struct FireState
    {
        public float Intensity; // 0‥1
        public float Fuel;      // material-dependent starting value
        public float Timer;     // seconds burning (informational)
    }

    private readonly Dictionary<Vector3I, FireState> _burningVoxels = new();

    // ── Fuel lookup per flammable material ───────────────────────────────
    private static readonly Dictionary<VoxelMaterialType, float> FuelByMaterial = new()
    {
        [VoxelMaterialType.Wood] = 3.0f,
        [VoxelMaterialType.Leaves] = 1.5f,
        [VoxelMaterialType.Bark] = 4.0f,
    };

    // ── Pooled particle emitters & lights ────────────────────────────────
    private const int MaxFireParticles = 100;
    private const int MaxFireLights = 20;
    private const float SpreadChance = 0.4f;
    private const float IntensityPerTick = 0.15f;
    private const float FuelConsumeRate = 0.5f;
    private const float LightIntensityThreshold = 0.6f;
    private const float SpreadIntensityThreshold = 0.4f;

    private readonly Queue<GpuParticles3D> _particlePool = new();
    private readonly Dictionary<Vector3I, GpuParticles3D> _activeParticles = new();
    private readonly Queue<OmniLight3D> _lightPool = new();
    private readonly Dictionary<Vector3I, OmniLight3D> _activeLights = new();

    // Shared fire particle material (created once)
    private static ParticleProcessMaterial? _sharedFireProcessMaterial;
#pragma warning disable CS0169 // Field is never used
    private static ShaderMaterial? _sharedFireOverlayMaterial;
#pragma warning restore CS0169

    // 6 cardinal directions for neighbor checks
    private static readonly Vector3I[] Neighbors =
    {
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        new Vector3I(0, 0, -1),
        new Vector3I(0, 0, 1),
    };

    // ── Lifecycle ────────────────────────────────────────────────────────

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.TurnChanged += OnTurnChanged;
        }
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        if (EventBus.Instance != null)
        {
            EventBus.Instance.TurnChanged -= OnTurnChanged;
        }
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Starts a fire at the given microvoxel position if the voxel is flammable.
    /// </summary>
    public void IgniteAt(Vector3I microvoxelPos)
    {
        if (_burningVoxels.ContainsKey(microvoxelPos))
        {
            return;
        }

        VoxelWorld? world = GetVoxelWorld();
        if (world == null)
        {
            return;
        }

        VoxelValue voxel = world.GetVoxel(microvoxelPos);
        if (voxel.IsAir)
        {
            return;
        }

        VoxelMaterialDefinition def = VoxelMaterials.GetDefinition(voxel.Material);
        if (!def.IsFlammable || !FuelByMaterial.TryGetValue(voxel.Material, out float fuel))
        {
            return;
        }

        _burningVoxels[microvoxelPos] = new FireState
        {
            Intensity = 0.1f,
            Fuel = fuel,
            Timer = 0f,
        };

        SpawnOrUpdateEffects(microvoxelPos, 0.1f);
    }

    /// <summary>
    /// Called between turns to advance fire simulation.
    /// </summary>
    public void ProcessFireTick(VoxelWorld world)
    {
        if (_burningVoxels.Count == 0)
        {
            return;
        }

        // Snapshot keys to avoid modifying collection while iterating
        List<Vector3I> positions = new List<Vector3I>(_burningVoxels.Keys);
        List<Vector3I> toRemove = new List<Vector3I>();
        List<(Vector3I pos, FireState state)> toIgnite = new List<(Vector3I, FireState)>();
        HashSet<Vector3I> affectedArea = new HashSet<Vector3I>();

        foreach (Vector3I pos in positions)
        {
            FireState state = _burningVoxels[pos];

            // 1. Increase intensity
            state.Intensity = Mathf.Min(state.Intensity + IntensityPerTick, 1.0f);
            state.Timer += 1.0f;

            // 2. Consume fuel
            state.Fuel -= state.Intensity * FuelConsumeRate;

            if (state.Fuel <= 0f)
            {
                // 3. Destroy the voxel
                world.SetVoxel(pos, VoxelValue.Air);
                toRemove.Add(pos);
                affectedArea.Add(pos);
                continue;
            }

            _burningVoxels[pos] = state;

            // 4. Spread to flammable neighbors if intensity is high enough
            if (state.Intensity > SpreadIntensityThreshold)
            {
                for (int d = 0; d < Neighbors.Length; d++)
                {
                    Vector3I neighborPos = pos + Neighbors[d];
                    if (_burningVoxels.ContainsKey(neighborPos))
                    {
                        continue;
                    }

                    // Check if already queued for ignition this tick
                    bool alreadyQueued = false;
                    for (int q = 0; q < toIgnite.Count; q++)
                    {
                        if (toIgnite[q].pos == neighborPos)
                        {
                            alreadyQueued = true;
                            break;
                        }
                    }
                    if (alreadyQueued)
                    {
                        continue;
                    }

                    VoxelValue neighborVoxel = world.GetVoxel(neighborPos);
                    if (neighborVoxel.IsAir)
                    {
                        continue;
                    }

                    VoxelMaterialDefinition neighborDef = VoxelMaterials.GetDefinition(neighborVoxel.Material);
                    if (!neighborDef.IsFlammable || !FuelByMaterial.TryGetValue(neighborVoxel.Material, out float neighborFuel))
                    {
                        continue;
                    }

                    if (GD.Randf() < SpreadChance)
                    {
                        toIgnite.Add((neighborPos, new FireState
                        {
                            Intensity = 0.1f,
                            Fuel = neighborFuel,
                            Timer = 0f,
                        }));
                    }
                }
            }

            // Update visual effects
            SpawnOrUpdateEffects(pos, state.Intensity);
        }

        // Remove burned-out voxels
        foreach (Vector3I pos in toRemove)
        {
            _burningVoxels.Remove(pos);
            RecycleEffects(pos);
        }

        // Add newly ignited neighbors
        foreach ((Vector3I pos, FireState state) in toIgnite)
        {
            if (!_burningVoxels.ContainsKey(pos))
            {
                _burningVoxels[pos] = state;
                SpawnOrUpdateEffects(pos, state.Intensity);
            }
        }

        // Find disconnected voxels in the affected area (fire can cause structural collapse)
        if (affectedArea.Count > 0)
        {
            Vector3 min = Vector3.One * float.MaxValue;
            Vector3 max = Vector3.One * float.MinValue;
            foreach (Vector3I pos in affectedArea)
            {
                Vector3 worldPos = MathHelpers.MicrovoxelToWorld(pos);
                min = new Vector3(Mathf.Min(min.X, worldPos.X), Mathf.Min(min.Y, worldPos.Y), Mathf.Min(min.Z, worldPos.Z));
                max = new Vector3(Mathf.Max(max.X, worldPos.X), Mathf.Max(max.Y, worldPos.Y), Mathf.Max(max.Z, worldPos.Z));
            }

            // Expand search bounds by a margin
            float margin = 3.0f * GameConfig.MicrovoxelMeters;
            Aabb searchBounds = new Aabb(min - Vector3.One * margin, (max - min) + Vector3.One * margin * 2f);
            List<Vector3I> disconnected = world.FindDisconnectedVoxels(searchBounds);

            if (disconnected.Count > 0)
            {
                // Group into connected components and spawn FallingChunks
                Vector3 explosionCenter = (min + max) * 0.5f;
                List<List<Vector3I>> components = FallingChunk.GroupConnectedComponents(
                    new HashSet<Vector3I>(disconnected), world);

                foreach (List<Vector3I> component in components)
                {
                    FallingChunk.Create(component, world, explosionCenter);
                }
            }

            world.ReturnList(disconnected);
        }
    }

    /// <summary>
    /// Returns the number of currently burning voxels.
    /// </summary>
    public int BurningCount => _burningVoxels.Count;

    /// <summary>
    /// Clears all fires and recycled all effects.
    /// </summary>
    public void ExtinguishAll()
    {
        List<Vector3I> keys = new List<Vector3I>(_burningVoxels.Keys);
        foreach (Vector3I pos in keys)
        {
            RecycleEffects(pos);
        }
        _burningVoxels.Clear();
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnTurnChanged(TurnChangedEvent payload)
    {
        VoxelWorld? world = GetVoxelWorld();
        if (world != null)
        {
            ProcessFireTick(world);
        }
    }

    // ── Effect management ───────────────────────────────────────────────

    private void SpawnOrUpdateEffects(Vector3I microvoxelPos, float intensity)
    {
        Vector3 worldPos = MathHelpers.MicrovoxelToWorld(microvoxelPos);

        // Particle emitter
        if (!_activeParticles.TryGetValue(microvoxelPos, out GpuParticles3D? particles))
        {
            particles = AcquireParticleEmitter();
            if (particles != null)
            {
                _activeParticles[microvoxelPos] = particles;
            }
        }

        if (particles != null)
        {
            particles.GlobalPosition = worldPos;
            particles.Emitting = true;

            // Scale amount by intensity
            particles.Amount = Mathf.Clamp((int)(8 + intensity * 4), 8, 12);
        }

        // Point light for intense fires
        if (intensity > LightIntensityThreshold)
        {
            if (!_activeLights.TryGetValue(microvoxelPos, out OmniLight3D? light))
            {
                light = AcquireLight();
                if (light != null)
                {
                    _activeLights[microvoxelPos] = light;
                }
            }

            if (light != null)
            {
                light.GlobalPosition = worldPos + Vector3.Up * GameConfig.MicrovoxelMeters * 0.5f;
                light.LightEnergy = intensity * 2.0f;
                light.Visible = true;
            }
        }
        else if (_activeLights.TryGetValue(microvoxelPos, out OmniLight3D? existingLight))
        {
            // Intensity dropped below threshold, recycle the light
            existingLight.Visible = false;
            _lightPool.Enqueue(existingLight);
            _activeLights.Remove(microvoxelPos);
        }
    }

    private void RecycleEffects(Vector3I microvoxelPos)
    {
        if (_activeParticles.TryGetValue(microvoxelPos, out GpuParticles3D? particles))
        {
            particles.Emitting = false;
            _particlePool.Enqueue(particles);
            _activeParticles.Remove(microvoxelPos);
        }

        if (_activeLights.TryGetValue(microvoxelPos, out OmniLight3D? light))
        {
            light.Visible = false;
            _lightPool.Enqueue(light);
            _activeLights.Remove(microvoxelPos);
        }
    }

    private GpuParticles3D? AcquireParticleEmitter()
    {
        if (_particlePool.Count > 0)
        {
            return _particlePool.Dequeue();
        }

        if (_activeParticles.Count >= MaxFireParticles)
        {
            return null; // Cap reached
        }

        GpuParticles3D particles = new GpuParticles3D();
        particles.Amount = 10;
        particles.Lifetime = 0.6f;
        particles.Explosiveness = 0.1f;
        particles.OneShot = false;
        particles.FixedFps = 30;
        particles.DrawOrder = GpuParticles3D.DrawOrderEnum.Lifetime;
        // Keep fire visuals animating between turns — damage is turn-based only
        particles.ProcessMode = ProcessModeEnum.Always;

        // Process material — controls particle motion
        ParticleProcessMaterial processMat = GetOrCreateFireProcessMaterial();
        particles.ProcessMaterial = processMat;

        // Draw pass — small billboard quad with warm color
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

        particles.DrawPass1 = drawMesh;
        AddChild(particles);
        return particles;
    }

    private OmniLight3D? AcquireLight()
    {
        if (_lightPool.Count > 0)
        {
            return _lightPool.Dequeue();
        }

        if (_activeLights.Count >= MaxFireLights)
        {
            return null; // Cap reached
        }

        OmniLight3D light = new OmniLight3D();
        light.LightColor = new Color(1.0f, 0.6f, 0.2f); // Warm orange
        light.LightEnergy = 1.0f;
        light.OmniRange = 3.0f;
        light.OmniAttenuation = 1.5f;
        light.ShadowEnabled = false; // Performance: no shadows from fire lights
        light.ProcessMode = ProcessModeEnum.Always;
        AddChild(light);
        return light;
    }

    private static ParticleProcessMaterial GetOrCreateFireProcessMaterial()
    {
        if (_sharedFireProcessMaterial != null)
        {
            return _sharedFireProcessMaterial;
        }

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 25f;
        mat.InitialVelocityMin = 0.3f;
        mat.InitialVelocityMax = 0.8f;
        mat.Gravity = new Vector3(0f, 0.5f, 0f); // Slight upward drift
        mat.ScaleMin = 0.5f;
        mat.ScaleMax = 1.2f;

        // Color ramp: yellow-orange to dark red, fading alpha
        Gradient colorGradient = new Gradient();
        colorGradient.SetColor(0, new Color(1.0f, 0.9f, 0.3f, 0.9f));
        colorGradient.SetColor(1, new Color(0.8f, 0.2f, 0.0f, 0.0f));
        GradientTexture1D gradientTex = new GradientTexture1D();
        gradientTex.Gradient = colorGradient;
        mat.ColorRamp = gradientTex;

        _sharedFireProcessMaterial = mat;
        return mat;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private VoxelWorld? GetVoxelWorld()
    {
        // Walk up to find VoxelWorld through GameManager's children or scene tree
        Node? parent = GetParent();
        if (parent != null)
        {
            VoxelWorld? world = parent.GetNodeOrNull<VoxelWorld>("GameWorld");
            if (world != null)
            {
                return world;
            }
        }

        // Fallback: search in tree
        SceneTree? tree = GetTree();
        if (tree != null)
        {
            foreach (Node node in tree.GetNodesInGroup("VoxelWorld"))
            {
                if (node is VoxelWorld vw)
                {
                    return vw;
                }
            }
        }

        return null;
    }
}
