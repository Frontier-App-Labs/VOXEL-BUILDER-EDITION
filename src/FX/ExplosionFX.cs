using Godot;
using System.Collections.Generic;

namespace VoxelSiege.FX;

/// <summary>
/// Flagship explosion visual effect with seven distinct layers for maximum
/// "Tactical Toy Warfare" satisfaction:
///   1. Detonation flash   – brilliant white-yellow OmniLight3D, very brief
///   2. Fireball core      – white-hot inner burst that quickly turns orange
///   3. Fireball shell     – larger, slower orange-red expanding cloud
///   4. Shockwave ring     – expanding translucent torus mesh
///   5. Sparks / embers    – tiny bright cubes with gravity arcs
///   6. Rising smoke       – dark grey billows that drift upward and dissipate
///   7. Cinders            – tiny glowing motes that float upward after the blast
///
/// All particle resources (Curve, CurveTexture, Gradient, Meshes) are cached
/// as static fields to avoid per-explosion allocations.  The whole node tree
/// is object-pooled for reuse.
///
/// Camera shake intensity is modulated by distance to the active camera so
/// far-away explosions feel appropriately muted.
/// </summary>
public partial class ExplosionFX : Node3D
{
    // ── Child nodes ────────────────────────────────────────────────
    private GpuParticles3D _fireballCore = null!;
    private GpuParticles3D _fireballShell = null!;
    private GpuParticles3D _sparks = null!;
    private GpuParticles3D _smoke = null!;
    private GpuParticles3D _cinders = null!;
    private MeshInstance3D _shockwaveRing = null!;
    private OmniLight3D _flash = null!;

    // ── Flash animation state ──────────────────────────────────────
    private float _flashEnergy;
    private float _flashTimer;
    private const float FlashDuration = 0.15f;

    // ── Shockwave animation state ──────────────────────────────────
    private float _shockwaveTimer;
    private float _shockwaveLifetime;
    private float _shockwaveMaxRadius;
    private StandardMaterial3D? _shockwaveMat;

    // ── Cleanup ────────────────────────────────────────────────────
    private float _cleanupTimer;
    private float _cleanupDelay;
    private bool _initialized;

    // ── Cached particle resources (shared across all explosions) ───
    private static CurveTexture? _coreScaleTex;
    private static CurveTexture? _shellScaleTex;
    private static CurveTexture? _smokeScaleTex;
    private static CurveTexture? _cinderScaleTex;
    private static SphereMesh? _cachedCoreSphere;
    private static SphereMesh? _cachedShellSphere;
    private static SphereMesh? _cachedSmokeSphere;
    private static BoxMesh? _cachedSparksBox;
    private static SphereMesh? _cachedCinderSphere;
    private static TorusMesh? _cachedShockwaveTorus;

    // ── Object pool ────────────────────────────────────────────────
    private const int PoolSize = 8;
    private static readonly Queue<ExplosionFX> _pool = new();

    // ================================================================
    //  PUBLIC API
    // ================================================================

    /// <summary>
    /// Immediately frees all active and pooled ExplosionFX instances.
    /// Call this when transitioning between game phases (e.g. menu battle → match)
    /// to prevent stale explosion effects from persisting into the new arena.
    /// </summary>
    public static void ClearAll()
    {
        while (_pool.Count > 0)
        {
            ExplosionFX pooled = _pool.Dequeue();
            if (IsInstanceValid(pooled))
            {
                pooled.Free();
            }
        }
    }

    /// <summary>
    /// Spawns a complete explosion effect at the given world position.
    /// Uses object pooling to reuse pre-built node trees.
    /// </summary>
    public static ExplosionFX Spawn(Node parent, Vector3 position, float radius, Color? tint = null)
    {
        ExplosionFX fx;
        if (_pool.Count > 0)
        {
            fx = _pool.Dequeue();
            // Re-parent to the caller's tree if the old parent was freed or differs
            if (!GodotObject.IsInstanceValid(fx.GetParent()) || fx.GetParent() != parent)
            {
                if (GodotObject.IsInstanceValid(fx.GetParent()))
                {
                    fx.GetParent().RemoveChild(fx);
                }
                parent.AddChild(fx);
            }
            fx.Visible = true;
            fx.SetProcess(true);
        }
        else
        {
            fx = new ExplosionFX();
            parent.AddChild(fx);
        }
        fx.GlobalPosition = position;
        fx.Initialize(radius, tint ?? new Color(1f, 0.6f, 0.15f));
        return fx;
    }

    // ================================================================
    //  INITIALIZATION
    // ================================================================

    private void Initialize(float radius, Color tint)
    {
        float scale = Mathf.Clamp(radius, 0.5f, 8f);

        if (!_initialized)
        {
            CreateFireballCore(scale, tint);
            CreateFireballShell(scale, tint);
            CreateSparks(scale, tint);
            CreateSmoke(scale);
            CreateCinders(scale, tint);
            CreateShockwaveRing(scale);
            CreateFlash(scale);
            _initialized = true;
        }
        else
        {
            ResetParticles(scale, tint);
        }

        TriggerCameraShake(scale);

        _cleanupDelay = 4.0f;
        _cleanupTimer = 0f;
        _flashTimer = 0f;
        _shockwaveTimer = 0f;
    }

    private void ResetParticles(float scale, Color tint)
    {
        // ── Fireball core ──
        if (_fireballCore.ProcessMaterial is ParticleProcessMaterial coreMat)
        {
            coreMat.InitialVelocityMin = 0.3f * scale;
            coreMat.InitialVelocityMax = 1.5f * scale;
            coreMat.EmissionSphereRadius = 0.1f * scale;
            coreMat.ScaleMin = 0.25f * scale;
            coreMat.ScaleMax = 0.5f * scale;
        }
        _fireballCore.Emitting = true;

        // ── Fireball shell ──
        if (_fireballShell.ProcessMaterial is ParticleProcessMaterial shellMat)
        {
            shellMat.InitialVelocityMin = 0.8f * scale;
            shellMat.InitialVelocityMax = 2.5f * scale;
            shellMat.EmissionSphereRadius = 0.2f * scale;
            shellMat.ScaleMin = 0.4f * scale;
            shellMat.ScaleMax = 0.8f * scale;
        }
        _fireballShell.Emitting = true;

        // ── Sparks ──
        if (_sparks.ProcessMaterial is ParticleProcessMaterial sparkMat)
        {
            sparkMat.InitialVelocityMin = 5f * scale;
            sparkMat.InitialVelocityMax = 12f * scale;
            sparkMat.EmissionSphereRadius = 0.05f * scale;
        }
        _sparks.Emitting = true;

        // ── Smoke ──
        if (_smoke.ProcessMaterial is ParticleProcessMaterial smokeMat)
        {
            smokeMat.InitialVelocityMin = 0.8f * scale;
            smokeMat.InitialVelocityMax = 2.2f * scale;
            smokeMat.EmissionSphereRadius = 0.3f * scale;
            smokeMat.ScaleMin = 0.6f * scale;
            smokeMat.ScaleMax = 1.2f * scale;
        }
        _smoke.Emitting = true;

        // ── Cinders ──
        if (_cinders.ProcessMaterial is ParticleProcessMaterial cinderMat)
        {
            cinderMat.InitialVelocityMin = 0.3f * scale;
            cinderMat.InitialVelocityMax = 1.0f * scale;
            cinderMat.EmissionSphereRadius = 0.3f * scale;
        }
        _cinders.Emitting = true;

        // ── Flash ──
        _flashEnergy = 12f;
        _flash.LightEnergy = _flashEnergy;
        _flash.OmniRange = 4f * scale;

        // ── Shockwave ring ──
        _shockwaveMaxRadius = 2.5f * scale;
        _shockwaveLifetime = 0.4f;
        _shockwaveTimer = 0f;
        _shockwaveRing.Visible = true;
        _shockwaveRing.Scale = Vector3.One * 0.01f;
        if (_shockwaveMat != null)
        {
            _shockwaveMat.AlbedoColor = new Color(1f, 0.85f, 0.5f, 0.6f);
        }
    }

    // ================================================================
    //  PER-FRAME UPDATE
    // ================================================================

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // ── Flash fade ──
        if (_flashTimer < FlashDuration)
        {
            _flashTimer += dt;
            float t = Mathf.Clamp(_flashTimer / FlashDuration, 0f, 1f);
            // Cubic ease-out for snappy falloff
            float falloff = t * t * t;
            _flash.LightEnergy = Mathf.Lerp(_flashEnergy, 0f, falloff);
        }
        else if (_flash.LightEnergy > 0f)
        {
            _flash.LightEnergy = 0f;
        }

        // ── Shockwave ring expansion ──
        if (_shockwaveTimer < _shockwaveLifetime)
        {
            _shockwaveTimer += dt;
            float t = Mathf.Clamp(_shockwaveTimer / _shockwaveLifetime, 0f, 1f);

            // Quick ease-out expansion (sqrt gives fast start, slow end)
            float expandT = Mathf.Sqrt(t);
            float currentRadius = expandT * _shockwaveMaxRadius;
            _shockwaveRing.Scale = new Vector3(currentRadius, currentRadius * 0.3f, currentRadius);

            // Fade out alpha: fully opaque at start, transparent by end
            if (_shockwaveMat != null)
            {
                float alpha = Mathf.Lerp(0.6f, 0f, t * t);
                Color c = _shockwaveMat.AlbedoColor;
                _shockwaveMat.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
            }
        }
        else if (_shockwaveRing.Visible)
        {
            _shockwaveRing.Visible = false;
        }

        // ── Cleanup / return to pool ──
        _cleanupTimer += dt;
        if (_cleanupTimer >= _cleanupDelay)
        {
            ReturnToPool();
        }
    }

    // ================================================================
    //  POOL MANAGEMENT
    // ================================================================

    private void ReturnToPool()
    {
        Visible = false;
        SetProcess(false);
        _fireballCore.Emitting = false;
        _fireballShell.Emitting = false;
        _sparks.Emitting = false;
        _smoke.Emitting = false;
        _cinders.Emitting = false;
        _flash.LightEnergy = 0f;
        _shockwaveRing.Visible = false;

        if (_pool.Count < PoolSize)
        {
            _pool.Enqueue(this);
        }
        else
        {
            QueueFree();
        }
    }

    // ================================================================
    //  LAYER 1 — FIREBALL CORE  (white-hot center, very brief)
    // ================================================================

    private void CreateFireballCore(float scale, Color tint)
    {
        _fireballCore = new GpuParticles3D();
        _fireballCore.Amount = 16;
        _fireballCore.Lifetime = 0.3;
        _fireballCore.Explosiveness = 0.98f;
        _fireballCore.OneShot = true;
        _fireballCore.Emitting = false; // Will be enabled after AddChild

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.5f, 0f);
        mat.Spread = 180f;
        mat.InitialVelocityMin = 0.3f * scale;
        mat.InitialVelocityMax = 1.5f * scale;
        mat.Gravity = new Vector3(0f, -0.5f, 0f);
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.1f * scale;

        mat.ScaleMin = 0.25f * scale;
        mat.ScaleMax = 0.5f * scale;
        mat.ScaleCurve = GetCoreScaleTexture();

        // White-hot → bright yellow → transparent
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 1f, 1f, 1f));       // pure white flash
        colorGrad.SetColor(1, new Color(1f, 0.7f, 0.1f, 0f));   // orange fade out
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.1f, new Color(1f, 1f, 0.85f, 1f));  // still nearly white
        colorGrad.AddPoint(0.35f, new Color(1f, 0.92f, 0.4f, 0.9f)); // bright yellow
        colorGrad.AddPoint(0.6f, new Color(1f, 0.75f, 0.2f, 0.5f));  // fading orange
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _fireballCore.ProcessMaterial = mat;
        _fireballCore.DrawPass1 = GetCoreSphere();

        AddChild(_fireballCore);
        _fireballCore.Emitting = true; // Start emission after node is in the tree
    }

    // ================================================================
    //  LAYER 2 — FIREBALL SHELL  (expanding orange-red cloud)
    // ================================================================

    private void CreateFireballShell(float scale, Color tint)
    {
        _fireballShell = new GpuParticles3D();
        _fireballShell.Amount = 22;
        _fireballShell.Lifetime = 0.55;
        _fireballShell.Explosiveness = 0.92f;
        _fireballShell.OneShot = true;
        _fireballShell.Emitting = false; // Will be enabled after AddChild

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.8f, 0f);
        mat.Spread = 180f;
        mat.InitialVelocityMin = 0.8f * scale;
        mat.InitialVelocityMax = 2.5f * scale;
        mat.Gravity = new Vector3(0f, -1.5f, 0f);
        mat.DampingMin = 1.5f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.2f * scale;

        mat.ScaleMin = 0.4f * scale;
        mat.ScaleMax = 0.8f * scale;
        mat.ScaleCurve = GetShellScaleTexture();

        // Bright orange → deep red → dark transparent
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 0.95f, 0.6f, 1f));    // bright yellow-white
        colorGrad.SetColor(1, new Color(0.3f, 0.08f, 0.02f, 0f)); // dark red fade
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.15f, new Color(1f, 0.8f, 0.25f, 1f));  // warm orange
        colorGrad.AddPoint(0.4f, new Color(tint.R, tint.G * 0.7f, tint.B * 0.3f, 0.85f)); // tinted
        colorGrad.AddPoint(0.7f, new Color(0.6f, 0.15f, 0.05f, 0.4f)); // deep red
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _fireballShell.ProcessMaterial = mat;
        _fireballShell.DrawPass1 = GetShellSphere();

        AddChild(_fireballShell);
        _fireballShell.Emitting = true; // Start emission after node is in the tree
    }

    // ================================================================
    //  LAYER 3 — SPARKS  (bright streaks with gravity arcs)
    // ================================================================

    private void CreateSparks(float scale, Color tint)
    {
        _sparks = new GpuParticles3D();
        _sparks.Amount = 30;
        _sparks.Lifetime = 0.9;
        _sparks.Explosiveness = 0.97f;
        _sparks.OneShot = true;
        _sparks.Emitting = false; // Will be enabled after AddChild

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.6f, 0f);
        mat.Spread = 180f;
        mat.InitialVelocityMin = 5f * scale;
        mat.InitialVelocityMax = 12f * scale;
        mat.Gravity = new Vector3(0f, -12f, 0f);   // strong gravity for arcing trajectories
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.05f * scale;

        mat.ScaleMin = 0.015f;
        mat.ScaleMax = 0.05f;

        // Angular velocity for tumbling sparks
        mat.AngularVelocityMin = -180f;
        mat.AngularVelocityMax = 180f;

        // Bright yellow → orange → dark
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 1f, 0.7f, 1f));       // bright yellow-white
        colorGrad.SetColor(1, new Color(0.4f, 0.1f, 0f, 0f));      // dark ember fade
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.15f, new Color(1f, 0.9f, 0.4f, 1f));  // golden
        colorGrad.AddPoint(0.5f, new Color(1f, 0.5f, 0.1f, 0.8f));  // orange
        colorGrad.AddPoint(0.8f, new Color(0.6f, 0.2f, 0.02f, 0.3f)); // dim ember
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _sparks.ProcessMaterial = mat;
        _sparks.DrawPass1 = GetSparksBox();

        AddChild(_sparks);
        _sparks.Emitting = true; // Start emission after node is in the tree
    }

    // ================================================================
    //  LAYER 4 — RISING SMOKE  (dark grey, lingers, drifts up)
    // ================================================================

    private void CreateSmoke(float scale)
    {
        _smoke = new GpuParticles3D();
        _smoke.Amount = 28;
        _smoke.Lifetime = 3.0;
        _smoke.Explosiveness = 0.70f;   // Slightly staggered for a rolling billow effect
        _smoke.Randomness = 0.3f;       // Per-particle variation in timing
        _smoke.OneShot = true;
        _smoke.Emitting = false; // Will be enabled after AddChild

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 140f;              // Wider spread for mushroom-cloud shape
        mat.InitialVelocityMin = 0.8f * scale;
        mat.InitialVelocityMax = 2.2f * scale;
        mat.Gravity = new Vector3(0f, 0.35f, 0f);   // Gentle upward drift (smoke rises slowly)
        mat.DampingMin = 3.5f;
        mat.DampingMax = 5.5f;           // High damping so smoke decelerates and hangs in the air
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.3f * scale;

        // Angular velocity for slow tumbling rotation (makes billowed shapes shift)
        mat.AngularVelocityMin = -25f;
        mat.AngularVelocityMax = 25f;

        // Smoke starts small, billows out significantly as it rises and fades
        mat.ScaleMin = 0.6f * scale;
        mat.ScaleMax = 1.2f * scale;
        mat.ScaleCurve = GetSmokeScaleTexture();

        // Dark warm smoke → lighter grey → transparent
        // Starts nearly opaque to read as dense smoke, fades smoothly
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.18f, 0.14f, 0.1f, 0.8f));    // dark warm grey, opaque
        colorGrad.SetColor(1, new Color(0.4f, 0.38f, 0.35f, 0f));      // light grey, fully transparent
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.08f, new Color(0.22f, 0.16f, 0.1f, 0.85f)); // brief dense peak
        colorGrad.AddPoint(0.25f, new Color(0.28f, 0.22f, 0.16f, 0.7f)); // still quite opaque
        colorGrad.AddPoint(0.5f, new Color(0.33f, 0.28f, 0.22f, 0.45f)); // mid-fade, lighter color
        colorGrad.AddPoint(0.75f, new Color(0.38f, 0.34f, 0.3f, 0.18f)); // wispy
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _smoke.ProcessMaterial = mat;
        _smoke.DrawPass1 = GetSmokeSphere();

        AddChild(_smoke);
        _smoke.Emitting = true; // Start emission after node is in the tree
    }

    // ================================================================
    //  LAYER 5 — CINDERS  (tiny glowing motes floating upward)
    // ================================================================

    private void CreateCinders(float scale, Color tint)
    {
        _cinders = new GpuParticles3D();
        _cinders.Amount = 12;
        _cinders.Lifetime = 1.8;
        _cinders.Explosiveness = 0.6f;   // staggered emission for lingering effect
        _cinders.OneShot = true;
        _cinders.Emitting = false; // Will be enabled after AddChild

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 90f;
        mat.InitialVelocityMin = 0.3f * scale;
        mat.InitialVelocityMax = 1.0f * scale;
        mat.Gravity = new Vector3(0f, 0.4f, 0f);   // gentle upward float
        mat.DampingMin = 1f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.3f * scale;

        mat.ScaleMin = 0.01f;
        mat.ScaleMax = 0.03f;
        mat.ScaleCurve = GetCinderScaleTexture();

        // Bright orange ember → dim red → transparent
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 0.8f, 0.3f, 1f));     // bright ember
        colorGrad.SetColor(1, new Color(0.5f, 0.1f, 0.02f, 0f));  // dark fade
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.3f, new Color(1f, 0.6f, 0.15f, 0.9f));  // warm orange
        colorGrad.AddPoint(0.7f, new Color(0.7f, 0.2f, 0.05f, 0.4f)); // dim
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _cinders.ProcessMaterial = mat;
        _cinders.DrawPass1 = GetCinderSphere();

        AddChild(_cinders);
        _cinders.Emitting = true; // Start emission after node is in the tree
    }

    // ================================================================
    //  LAYER 6 — SHOCKWAVE RING  (expanding translucent torus)
    // ================================================================

    private void CreateShockwaveRing(float scale)
    {
        _shockwaveRing = new MeshInstance3D();
        _shockwaveRing.Mesh = GetShockwaveTorus();
        _shockwaveRing.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // Per-instance material so each explosion can fade independently
        _shockwaveMat = new StandardMaterial3D();
        _shockwaveMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _shockwaveMat.AlbedoColor = new Color(1f, 0.85f, 0.5f, 0.6f);
        _shockwaveMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _shockwaveMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _shockwaveMat.NoDepthTest = true;
        _shockwaveRing.SetSurfaceOverrideMaterial(0, _shockwaveMat);

        _shockwaveRing.Scale = Vector3.One * 0.01f;
        _shockwaveMaxRadius = 2.5f * scale;
        _shockwaveLifetime = 0.4f;

        AddChild(_shockwaveRing);
    }

    // ================================================================
    //  LAYER 7 — DETONATION FLASH  (bright OmniLight3D)
    // ================================================================

    private void CreateFlash(float scale)
    {
        _flash = new OmniLight3D();
        _flashEnergy = 12f;
        _flash.LightEnergy = _flashEnergy;
        _flash.LightColor = new Color(1f, 0.92f, 0.65f);  // warm yellow-white
        _flash.OmniRange = 4f * scale;
        _flash.OmniAttenuation = 1.2f;
        _flash.ShadowEnabled = false;
        _flashTimer = 0f;
        AddChild(_flash);
    }

    // ================================================================
    //  CAMERA SHAKE (distance-attenuated)
    // ================================================================

    private void TriggerCameraShake(float scale)
    {
        Camera3D? cam = GetViewport()?.GetCamera3D();
        float distanceFactor = 1f;

        if (cam != null)
        {
            float dist = cam.GlobalPosition.DistanceTo(GlobalPosition);
            // Full intensity within 5m, falls off to zero at 40m
            distanceFactor = Mathf.Clamp(1f - (dist - 5f) / 35f, 0.1f, 1f);
        }

        float intensity = 0.35f * scale * distanceFactor;
        float duration = Mathf.Lerp(0.25f, 0.6f, Mathf.Clamp(scale / 4f, 0f, 1f));

        foreach (Node node in GetTree().GetNodesInGroup("CameraShake"))
        {
            if (node is Camera.CameraShake shake)
            {
                shake.Shake(intensity, duration);
                break;
            }
        }
    }

    // ================================================================
    //  CACHED CURVE TEXTURES
    // ================================================================

    private static CurveTexture GetCoreScaleTexture()
    {
        if (_coreScaleTex == null)
        {
            Curve c = new Curve();
            c.AddPoint(new Vector2(0f, 0.5f));   // start medium
            c.AddPoint(new Vector2(0.1f, 1f));    // expand fast
            c.AddPoint(new Vector2(0.4f, 0.7f));  // shrink
            c.AddPoint(new Vector2(1f, 0f));       // vanish
            _coreScaleTex = new CurveTexture { Curve = c };
        }
        return _coreScaleTex;
    }

    private static CurveTexture GetShellScaleTexture()
    {
        if (_shellScaleTex == null)
        {
            Curve c = new Curve();
            c.AddPoint(new Vector2(0f, 0.3f));   // start small
            c.AddPoint(new Vector2(0.2f, 1f));    // expand
            c.AddPoint(new Vector2(0.5f, 0.8f));  // plateau
            c.AddPoint(new Vector2(1f, 0.1f));    // shrink to near-zero
            _shellScaleTex = new CurveTexture { Curve = c };
        }
        return _shellScaleTex;
    }

    private static CurveTexture GetSmokeScaleTexture()
    {
        if (_smokeScaleTex == null)
        {
            Curve c = new Curve();
            c.AddPoint(new Vector2(0f, 0.15f));    // start quite small (dense kernel)
            c.AddPoint(new Vector2(0.08f, 0.45f));  // quick initial puff
            c.AddPoint(new Vector2(0.2f, 0.8f));    // rapid billow outward
            c.AddPoint(new Vector2(0.45f, 1.0f));   // near full size
            c.AddPoint(new Vector2(0.7f, 1.3f));    // continues expanding slowly
            c.AddPoint(new Vector2(1f, 1.6f));       // large wispy cloud at end
            _smokeScaleTex = new CurveTexture { Curve = c };
        }
        return _smokeScaleTex;
    }

    private static CurveTexture GetCinderScaleTexture()
    {
        if (_cinderScaleTex == null)
        {
            Curve c = new Curve();
            c.AddPoint(new Vector2(0f, 1f));
            c.AddPoint(new Vector2(0.3f, 0.8f));
            c.AddPoint(new Vector2(0.7f, 0.5f));
            c.AddPoint(new Vector2(1f, 0f));       // shrink to nothing
            _cinderScaleTex = new CurveTexture { Curve = c };
        }
        return _cinderScaleTex;
    }

    // ================================================================
    //  CACHED DRAW PASS MESHES
    // ================================================================

    private static SphereMesh GetCoreSphere()
    {
        if (_cachedCoreSphere == null)
        {
            _cachedCoreSphere = new SphereMesh
            {
                Radius = 0.2f,
                Height = 0.4f,
                RadialSegments = 16,
                Rings = 8
            };
            _cachedCoreSphere.Material = CreateBillboardMaterial();
        }
        return _cachedCoreSphere;
    }

    private static SphereMesh GetShellSphere()
    {
        if (_cachedShellSphere == null)
        {
            _cachedShellSphere = new SphereMesh
            {
                Radius = 0.3f,
                Height = 0.6f,
                RadialSegments = 16,
                Rings = 8
            };
            _cachedShellSphere.Material = CreateBillboardMaterial();
        }
        return _cachedShellSphere;
    }

    private static SphereMesh GetSmokeSphere()
    {
        if (_cachedSmokeSphere == null)
        {
            _cachedSmokeSphere = new SphereMesh
            {
                Radius = 0.35f,
                Height = 0.7f,
                RadialSegments = 16,
                Rings = 8
            };
            _cachedSmokeSphere.Material = CreateBillboardMaterial(isSmoke: true);
        }
        return _cachedSmokeSphere;
    }

    private static BoxMesh GetSparksBox()
    {
        if (_cachedSparksBox == null)
        {
            _cachedSparksBox = new BoxMesh
            {
                Size = new Vector3(0.02f, 0.02f, 0.12f) // elongated streaks
            };
            StandardMaterial3D mat = new StandardMaterial3D();
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.AlbedoColor = Colors.White;
            mat.VertexColorUseAsAlbedo = true;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            mat.NoDepthTest = true;
            _cachedSparksBox.Material = mat;
        }
        return _cachedSparksBox;
    }

    private static SphereMesh GetCinderSphere()
    {
        if (_cachedCinderSphere == null)
        {
            _cachedCinderSphere = new SphereMesh
            {
                Radius = 0.04f,
                Height = 0.08f,
                RadialSegments = 8,
                Rings = 4
            };
            _cachedCinderSphere.Material = CreateBillboardMaterial();
        }
        return _cachedCinderSphere;
    }

    private static TorusMesh GetShockwaveTorus()
    {
        if (_cachedShockwaveTorus == null)
        {
            _cachedShockwaveTorus = new TorusMesh();
            _cachedShockwaveTorus.InnerRadius = 0.85f;
            _cachedShockwaveTorus.OuterRadius = 1.0f;
            _cachedShockwaveTorus.Rings = 16;
            _cachedShockwaveTorus.RingSegments = 12;
        }
        return _cachedShockwaveTorus;
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private static StandardMaterial3D CreateBillboardMaterial(bool isSmoke = false)
    {
        StandardMaterial3D mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.AlbedoColor = Colors.White;
        mat.VertexColorUseAsAlbedo = true; // Required for ParticleProcessMaterial ColorRamp to tint particles
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        mat.NoDepthTest = true; // Prevents z-fighting between overlapping particles
        mat.RenderPriority = isSmoke ? -1 : 0; // Smoke renders behind fireball particles
        if (isSmoke)
        {
            // Proximity fade softens hard edges where smoke intersects geometry
            mat.ProximityFadeEnabled = true;
            mat.ProximityFadeDistance = 0.3f;
        }
        return mat;
    }
}
