using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Singleton fire spread manager. Tracks burning voxels, ticks fire in REAL TIME
/// (every frame), spreads to neighbors including multi-block gap jumps, spawns
/// particle/light effects, and destroys consumed voxels.
/// Fire runs continuously via _Process regardless of game phase or turn state.
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
    private const float IntensityGrowthRate = 0.3f;       // per second (was 0.15 per turn)
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

    // ── Real-time timers ────────────────────────────────────────────────
    private float _spreadTimer;
    private float _damageTimer;
    private int _spreadTickCount; // monotonic counter for deterministic RNG seeding

    // ── Pre-computed spread offsets ──────────────────────────────────────
    // All offsets within FireSpreadRadius, sorted by distance so adjacent
    // cells are checked first (better for early-out optimizations).
    private static Vector3I[]? _spreadOffsets;
    private static float[]? _spreadDistances;

    // ── Lifecycle ────────────────────────────────────────────────────────

    public override void _EnterTree()
    {
        Instance = this;
        // Fire visuals and logic run regardless of pause or game phase
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        BuildSpreadOffsets();
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Real-time fire tick. Uses fixed timestep accumulation so both local and
    /// online clients produce identical fire state (same damage, same spread).
    /// </summary>
    public override void _Process(double delta)
    {
        if (_burningVoxels.Count == 0)
        {
            return;
        }

        float dt = (float)delta;

        // Damage tick — fixed timestep so fuel/intensity math is identical across clients
        _damageTimer += dt;
        while (_damageTimer >= GameConfig.FireDamageTickInterval)
        {
            _damageTimer -= GameConfig.FireDamageTickInterval;
            ProcessDamageTick(GameConfig.FireDamageTickInterval);
        }

        // Spread tick — fixed timestep + deterministic RNG
        _spreadTimer += dt;
        while (_spreadTimer >= GameConfig.FireSpreadInterval)
        {
            _spreadTimer -= GameConfig.FireSpreadInterval;
            _spreadTickCount++;
            ProcessSpreadTick();
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
    /// Legacy entry point kept for external callers. Now a no-op since fire
    /// is processed in real-time via _Process.
    /// </summary>
    public void ProcessFireTick(VoxelWorld world)
    {
        // Fire now runs in real-time via _Process — this is intentionally empty.
    }

    /// <summary>
    /// Returns the number of currently burning voxels.
    /// </summary>
    public int BurningCount => _burningVoxels.Count;

    /// <summary>
    /// Returns true if the given microvoxel position is currently on fire.
    /// </summary>
    public bool IsOnFire(Vector3I microvoxelPos)
    {
        return _burningVoxels.ContainsKey(microvoxelPos);
    }

    /// <summary>
    /// Extinguishes fire at a specific position (e.g. when a burning voxel becomes a falling chunk).
    /// </summary>
    public void ExtinguishAt(Vector3I microvoxelPos)
    {
        if (_burningVoxels.Remove(microvoxelPos))
        {
            RecycleEffects(microvoxelPos);
        }
    }

    /// <summary>
    /// Clears all fires and recycles all effects.
    /// </summary>
    public void ExtinguishAll()
    {
        List<Vector3I> keys = new List<Vector3I>(_burningVoxels.Keys);
        foreach (Vector3I pos in keys)
        {
            RecycleEffects(pos);
        }
        _burningVoxels.Clear();
        _spreadTickCount = 0;
        _spreadTimer = 0f;
        _damageTimer = 0f;
    }

    // ── Real-time tick internals ────────────────────────────────────────

    /// <summary>
    /// Applies fire damage and fuel consumption to all burning voxels.
    /// Called every FireDamageTickInterval seconds for continuous burn.
    /// </summary>
    private void ProcessDamageTick(float elapsed)
    {
        VoxelWorld? world = GetVoxelWorld();
        if (world == null)
        {
            return;
        }

        List<Vector3I> positions = new List<Vector3I>(_burningVoxels.Keys);
        List<Vector3I> toRemove = new List<Vector3I>();
        HashSet<Vector3I> affectedArea = new HashSet<Vector3I>();

        foreach (Vector3I pos in positions)
        {
            FireState state = _burningVoxels[pos];

            // Grow intensity over time
            state.Intensity = Mathf.Min(state.Intensity + IntensityGrowthRate * elapsed, 1.0f);
            state.Timer += elapsed;

            // Consume fuel proportional to intensity and elapsed time
            state.Fuel -= state.Intensity * FuelConsumeRate * elapsed;

            if (state.Fuel <= 0f)
            {
                // Destroy the voxel — it has been consumed by fire
                world.SetVoxel(pos, VoxelValue.Air);
                toRemove.Add(pos);
                affectedArea.Add(pos);
                continue;
            }

            _burningVoxels[pos] = state;

            // Update visual effects
            SpawnOrUpdateEffects(pos, state.Intensity);
        }

        // Remove burned-out voxels
        foreach (Vector3I pos in toRemove)
        {
            _burningVoxels.Remove(pos);
            RecycleEffects(pos);
        }

        // Handle structural collapse from fire-destroyed voxels
        if (affectedArea.Count > 0)
        {
            HandleStructuralCollapse(world, affectedArea);
        }
    }

    /// <summary>
    /// Checks for fire spread to nearby flammable voxels, including gap-jumping
    /// up to FireSpreadRadius blocks away. Called every FireSpreadInterval seconds.
    /// </summary>
    private void ProcessSpreadTick()
    {
        VoxelWorld? world = GetVoxelWorld();
        if (world == null)
        {
            return;
        }

        if (_spreadOffsets == null || _spreadDistances == null)
        {
            return;
        }

        // Sort burning positions so both clients iterate in the same order.
        // This ensures the deterministic RNG produces identical results.
        List<Vector3I> positions = new List<Vector3I>(_burningVoxels.Keys);
        positions.Sort((a, b) =>
        {
            int cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            return a.Z.CompareTo(b.Z);
        });

        // Deterministic RNG seeded by the monotonic tick counter.
        // Both clients increment _spreadTickCount identically (fixed timestep).
        Random rng = new Random(_spreadTickCount * 7919);

        List<(Vector3I pos, FireState state)> toIgnite = new();

        foreach (Vector3I pos in positions)
        {
            FireState state = _burningVoxels[pos];

            // Only spread when fire is intense enough
            if (state.Intensity <= SpreadIntensityThreshold)
            {
                continue;
            }

            // Check all offsets within the spread radius
            for (int i = 0; i < _spreadOffsets.Length; i++)
            {
                Vector3I candidatePos = pos + _spreadOffsets[i];
                float dist = _spreadDistances[i];

                if (_burningVoxels.ContainsKey(candidatePos))
                {
                    continue;
                }

                // Check if already queued for ignition this tick
                bool alreadyQueued = false;
                for (int q = 0; q < toIgnite.Count; q++)
                {
                    if (toIgnite[q].pos == candidatePos)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }
                if (alreadyQueued)
                {
                    continue;
                }

                VoxelValue candidateVoxel = world.GetVoxel(candidatePos);
                if (candidateVoxel.IsAir)
                {
                    continue;
                }

                VoxelMaterialDefinition candidateDef = VoxelMaterials.GetDefinition(candidateVoxel.Material);
                if (!candidateDef.IsFlammable || !FuelByMaterial.TryGetValue(candidateVoxel.Material, out float candidateFuel))
                {
                    continue;
                }

                // Spread chance decreases with distance — adjacent blocks use full
                // SpreadChance, farther blocks (gap jumps) use the lower FireJumpChance
                // scaled by intensity
                float chance;
                if (dist <= 1.01f)
                {
                    // Direct neighbor (face-adjacent)
                    chance = SpreadChance * state.Intensity;
                }
                else
                {
                    // Gap jump — chance falls off with distance
                    float falloff = 1.0f / dist;
                    chance = GameConfig.FireJumpChance * state.Intensity * falloff;
                }

                if ((float)rng.NextDouble() < chance)
                {
                    toIgnite.Add((candidatePos, new FireState
                    {
                        Intensity = 0.1f,
                        Fuel = candidateFuel,
                        Timer = 0f,
                    }));
                }
            }
        }

        // Add newly ignited voxels
        foreach ((Vector3I pos, FireState state) in toIgnite)
        {
            if (!_burningVoxels.ContainsKey(pos))
            {
                _burningVoxels[pos] = state;
                SpawnOrUpdateEffects(pos, state.Intensity);
            }
        }
    }

    /// <summary>
    /// After fire destroys voxels, check for disconnected structures and
    /// spawn falling chunks for structural collapse.
    /// </summary>
    private void HandleStructuralCollapse(VoxelWorld world, HashSet<Vector3I> affectedArea)
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

    // ── Spread offset pre-computation ───────────────────────────────────

    /// <summary>
    /// Pre-computes all integer offsets within FireSpreadRadius, excluding the
    /// origin, sorted by Euclidean distance so adjacent cells are checked first.
    /// </summary>
    private static void BuildSpreadOffsets()
    {
        if (_spreadOffsets != null)
        {
            return;
        }

        int r = GameConfig.FireSpreadRadius;
        List<(Vector3I offset, float dist)> offsets = new();

        for (int x = -r; x <= r; x++)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int z = -r; z <= r; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    float dist = Mathf.Sqrt(x * x + y * y + z * z);
                    if (dist <= r)
                    {
                        offsets.Add((new Vector3I(x, y, z), dist));
                    }
                }
            }
        }

        // Sort by distance — adjacent first, then gap jumps
        offsets.Sort((a, b) => a.dist.CompareTo(b.dist));

        _spreadOffsets = new Vector3I[offsets.Count];
        _spreadDistances = new float[offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
        {
            _spreadOffsets[i] = offsets[i].offset;
            _spreadDistances[i] = offsets[i].dist;
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
