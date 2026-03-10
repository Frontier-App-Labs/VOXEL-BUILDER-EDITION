using Godot;
using System.Collections.Generic;

namespace VoxelSiege.FX;

/// <summary>
/// Weapon firing visual effects with per-weapon-type specialization.
/// Each weapon type gets distinct muzzle flash, smoke, flash light, and
/// ancillary effects (smoke rings, backblast, sparks, electric arcs).
/// Uses object pooling and cached particle resources for performance.
/// All effects use ParticleProcessMaterial with billboard spheres/boxes.
/// </summary>
public static class WeaponFX
{
    // ---- Object pools for burst effects ----
    private const int PoolSize = 16;
    private static readonly Queue<GpuParticles3D> _muzzleFlashPool = new();
    private static readonly Queue<GpuParticles3D> _smokePuffPool = new();
    private static readonly Queue<GpuParticles3D> _smokeRingPool = new();
    private static readonly Queue<GpuParticles3D> _backblastPool = new();
    private static readonly Queue<GpuParticles3D> _sparksPool = new();
    private static readonly Queue<GpuParticles3D> _electricArcPool = new();
    private static readonly Queue<OmniLight3D> _flashLightPool = new();

    // ---- Cached draw pass meshes ----
    private static SphereMesh? _cachedFlashSphere;
    private static SphereMesh? _cachedSmokeSphere;
    private static SphereMesh? _cachedIdleSmokeSphere;
    private static SphereMesh? _cachedSmokeRingSphere;
    private static SphereMesh? _cachedBackblastSphere;
    private static BoxMesh? _cachedSparkBox;
    private static SphereMesh? _cachedArcSphere;

    // ---- Cached curve textures ----
    private static CurveTexture? _cachedFlashScaleTex;
    private static CurveTexture? _cachedSmokeScaleTex;
    private static CurveTexture? _cachedIdleSmokeScaleTex;
    private static CurveTexture? _cachedSmokeRingScaleTex;
    private static CurveTexture? _cachedBackblastScaleTex;
    private static CurveTexture? _cachedArcScaleTex;

    // ==================================================================
    //  HIGH-LEVEL PER-WEAPON FX ENTRY POINTS
    // ==================================================================

    /// <summary>
    /// Cannon fire: big yellow-orange flash + ring of smoke puffs + bright
    /// omni flash + camera shake. The "loud visual bang".
    /// </summary>
    public static void SpawnCannonFireFX(Node parent, Vector3 position, Vector3 direction)
    {
        Node sceneRoot = parent.GetTree().Root;

        // 1. Big muzzle flash burst (more particles, bigger scale)
        SpawnMuzzleFlash(sceneRoot, position, direction, amount: 16, lifetime: 0.25,
            scaleMin: 0.1f, scaleMax: 0.22f, velocityMin: 2f, velocityMax: 5f, spread: 35f);

        // 2. Smoke ring expanding outward from barrel
        SpawnSmokeRing(sceneRoot, position, direction);

        // 3. Standard smoke puff drifting upward
        SpawnSmokePuff(sceneRoot, position);

        // 4. Bright flash light illuminating nearby geometry
        SpawnFlashLight(sceneRoot, position, new Color(1f, 0.85f, 0.4f), energy: 6f, range: 3f, duration: 0.15f);

        // 5. Camera shake for the bang
        TriggerCameraShake(parent, 0.25f, 0.3f);
    }

    /// <summary>
    /// Mortar fire: upward burst of thick smoke from tube mouth.
    /// Less flash, more smoke. Muffled thump feel.
    /// </summary>
    public static void SpawnMortarFireFX(Node parent, Vector3 position, Vector3 direction)
    {
        Node sceneRoot = parent.GetTree().Root;

        // 1. Small, quick muzzle flash (subdued compared to cannon)
        SpawnMuzzleFlash(sceneRoot, position, Vector3.Up, amount: 6, lifetime: 0.15,
            scaleMin: 0.04f, scaleMax: 0.1f, velocityMin: 1f, velocityMax: 2.5f, spread: 20f);

        // 2. Big upward smoke burst from tube mouth (the star of the show)
        SpawnMortarSmokeBurst(sceneRoot, position);

        // 3. Secondary drifting smoke puff
        SpawnSmokePuff(sceneRoot, position);

        // 4. Dim flash light (mortar is more muffled)
        SpawnFlashLight(sceneRoot, position, new Color(1f, 0.7f, 0.3f), energy: 3f, range: 2f, duration: 0.1f);

        // 5. Subtle camera shake
        TriggerCameraShake(parent, 0.15f, 0.25f);
    }

    /// <summary>
    /// Missile launch: backblast flame out the rear + launch smoke cloud +
    /// side flame jets. Dramatic rocket launch feel.
    /// </summary>
    public static void SpawnMissileFireFX(Node parent, Vector3 position, Vector3 direction)
    {
        Node sceneRoot = parent.GetTree().Root;

        // 1. Backblast flame shooting out the rear of the launcher
        Vector3 rearDirection = -direction.Normalized();
        SpawnBackblast(sceneRoot, position, rearDirection);

        // 2. Launch smoke cloud billowing around the launcher
        SpawnMissileLaunchSmoke(sceneRoot, position);

        // 3. Side flame jets (perpendicular to launch direction)
        Vector3 side = direction.Cross(Vector3.Up).Normalized();
        if (side.LengthSquared() < 0.001f)
            side = direction.Cross(Vector3.Right).Normalized();
        SpawnSideJet(sceneRoot, position, side);
        SpawnSideJet(sceneRoot, position, -side);

        // 4. Orange-red flash light
        SpawnFlashLight(sceneRoot, position, new Color(1f, 0.5f, 0.15f), energy: 5f, range: 2.5f, duration: 0.2f);

        // 5. Camera shake for the launch
        TriggerCameraShake(parent, 0.2f, 0.35f);
    }

    /// <summary>
    /// Drill activation: spinning sparks spray, grinding particles,
    /// slight vibration effect. Mechanical, industrial feel.
    /// </summary>
    public static void SpawnDrillFireFX(Node parent, Vector3 position, Vector3 direction)
    {
        Node sceneRoot = parent.GetTree().Root;

        // 1. Activation spark burst (bright, fast, angular)
        SpawnDrillSparks(sceneRoot, position, direction);

        // 2. Grinding debris spray (brown/dirt chunks)
        SpawnGrindingDebris(sceneRoot, position, direction);

        // 3. Small flash light (hot metal sparks)
        SpawnFlashLight(sceneRoot, position, new Color(1f, 0.8f, 0.3f), energy: 2.5f, range: 1.5f, duration: 0.12f);

        // 4. Subtle vibration shake
        TriggerCameraShake(parent, 0.1f, 0.15f);
    }

    /// <summary>
    /// Railgun fire: bright cyan flash + electric arc sparks between rails +
    /// capacitor discharge glow + brief bloom. High-tech energy weapon feel.
    /// </summary>
    public static void SpawnRailgunFireFX(Node parent, Vector3 position, Vector3 direction)
    {
        Node sceneRoot = parent.GetTree().Root;

        // 1. Bright cyan muzzle flash (energy discharge)
        SpawnRailgunFlash(sceneRoot, position, direction);

        // 2. Electric arc sparks scattered from the barrel
        SpawnElectricArcs(sceneRoot, position, direction);

        // 3. Capacitor discharge glow (intense, brief light)
        SpawnFlashLight(sceneRoot, position, new Color(0.3f, 0.85f, 1f), energy: 10f, range: 4f, duration: 0.12f);

        // 4. Secondary warm-up dissipation particles
        SpawnCapacitorVent(sceneRoot, position);

        // 5. Strong camera shake for the discharge
        TriggerCameraShake(parent, 0.3f, 0.25f);
    }

    // ==================================================================
    //  RECOIL ANIMATION (called from WeaponBase)
    // ==================================================================

    /// <summary>
    /// Animates a weapon mesh sliding backward then returning to rest.
    /// Uses a Tween for smooth easing. The recoil direction is the opposite
    /// of the firing direction in local space.
    /// </summary>
    public static void AnimateRecoil(Node3D weaponMesh, Vector3 fireDirection, float distance, float duration)
    {
        if (weaponMesh == null || !GodotObject.IsInstanceValid(weaponMesh))
            return;

        // Kill any existing tweens on this node to prevent tween accumulation
        KillActiveTweens(weaponMesh);

        Vector3 restPos = weaponMesh.Position;
        // Recoil slides backward (opposite of fire direction) in local space
        Vector3 recoilOffset = -fireDirection.Normalized() * distance;
        Vector3 recoilPos = restPos + recoilOffset;

        Tween tween = weaponMesh.CreateTween();
        TrackTween(weaponMesh, tween);
        tween.SetEase(Tween.EaseType.Out);
        tween.SetTrans(Tween.TransitionType.Expo);

        // Quick snap backward
        tween.TweenProperty(weaponMesh, "position", recoilPos, duration * 0.2f);

        // Smooth return to rest
        tween.SetEase(Tween.EaseType.InOut);
        tween.SetTrans(Tween.TransitionType.Elastic);
        tween.TweenProperty(weaponMesh, "position", restPos, duration * 0.8f);
    }

    /// <summary>
    /// Animates a vibration/shake on the weapon mesh. Used for drill activation.
    /// </summary>
    public static void AnimateVibration(Node3D weaponMesh, float intensity, float duration)
    {
        if (weaponMesh == null || !GodotObject.IsInstanceValid(weaponMesh))
            return;

        // Kill any existing tweens on this node to prevent tween accumulation
        KillActiveTweens(weaponMesh);

        Vector3 restPos = weaponMesh.Position;

        Tween tween = weaponMesh.CreateTween();
        TrackTween(weaponMesh, tween);
        tween.SetEase(Tween.EaseType.InOut);
        tween.SetTrans(Tween.TransitionType.Sine);

        // Rapid oscillation sequence
        int shakes = 6;
        float shakeTime = duration / (shakes * 2);
        for (int i = 0; i < shakes; i++)
        {
            float damping = 1f - ((float)i / shakes);
            Vector3 offset = new Vector3(
                (float)GD.RandRange(-intensity, intensity) * damping,
                (float)GD.RandRange(-intensity, intensity) * damping,
                (float)GD.RandRange(-intensity, intensity) * damping);
            tween.TweenProperty(weaponMesh, "position", restPos + offset, shakeTime);
        }

        // Return to rest
        tween.TweenProperty(weaponMesh, "position", restPos, shakeTime * 2);
    }

    // ==================================================================
    //  IDLE SMOKE (unchanged from original)
    // ==================================================================

    /// <summary>
    /// Creates a persistent idle smoke emitter for ambient wisps.
    /// Returns a GpuParticles3D that should be added as a child of the weapon.
    /// Starts disabled; call Emitting = true after the weapon has fired once.
    /// 3 particles active at a time, very slow and transparent.
    /// </summary>
    public static GpuParticles3D CreateIdleSmoke()
    {
        GpuParticles3D idle = new GpuParticles3D();
        idle.Name = "IdleSmoke";
        idle.Amount = 3;
        idle.Lifetime = 3.0;
        idle.Emitting = false;
        idle.OneShot = false;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 25f;
        mat.InitialVelocityMin = 0.02f;
        mat.InitialVelocityMax = 0.08f;
        mat.Gravity = new Vector3(0f, 0.1f, 0f);
        mat.DampingMin = 1f;
        mat.DampingMax = 1f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.05f;

        mat.ScaleMin = 0.03f;
        mat.ScaleMax = 0.08f;
        mat.ScaleCurve = GetIdleSmokeScaleTexture();

        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.7f, 0.7f, 0.7f, 0.15f));
        colorGrad.SetColor(1, new Color(0.6f, 0.6f, 0.6f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.5f, new Color(0.65f, 0.65f, 0.65f, 0.1f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        idle.ProcessMaterial = mat;
        idle.DrawPass1 = GetIdleSmokeSphere();

        return idle;
    }

    // ==================================================================
    //  LEGACY API (kept for backward compatibility)
    // ==================================================================

    /// <summary>
    /// Generic muzzle flash. Kept for any callers using the old API.
    /// New code should use the per-weapon SpawnXxxFireFX methods.
    /// </summary>
    public static void SpawnMuzzleFlash(Node parent, Vector3 position, Vector3 direction)
    {
        SpawnMuzzleFlash(parent, position, direction, amount: 10, lifetime: 0.2,
            scaleMin: 0.06f, scaleMax: 0.15f, velocityMin: 1.5f, velocityMax: 4f, spread: 30f);
    }

    /// <summary>
    /// Generic smoke puff. Kept for any callers using the old API.
    /// </summary>
    public static void SpawnSmokePuff(Node parent, Vector3 position)
    {
        GpuParticles3D smoke;

        if (_smokePuffPool.Count > 0)
        {
            smoke = _smokePuffPool.Dequeue();
            smoke.Visible = true;
            smoke.SetProcess(true);
        }
        else
        {
            smoke = CreateSmokePuffParticles();
            parent.AddChild(smoke);
        }

        smoke.GlobalPosition = position;
        smoke.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)smoke.Lifetime + 0.2f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(smoke, _smokePuffPool);
        }
    }

    // ==================================================================
    //  CANNON-SPECIFIC: SMOKE RING
    // ==================================================================

    private static void SpawnSmokeRing(Node parent, Vector3 position, Vector3 direction)
    {
        GpuParticles3D ring;

        if (_smokeRingPool.Count > 0)
        {
            ring = _smokeRingPool.Dequeue();
            ring.Visible = true;
            ring.SetProcess(true);
        }
        else
        {
            ring = CreateSmokeRingParticles();
            parent.AddChild(ring);
        }

        ring.GlobalPosition = position + direction.Normalized() * 0.15f;
        ring.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)ring.Lifetime + 0.2f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(ring, _smokeRingPool);
        }
    }

    private static GpuParticles3D CreateSmokeRingParticles()
    {
        GpuParticles3D ring = new GpuParticles3D();
        ring.Name = "CannonSmokeRing";
        ring.Amount = 12;
        ring.Lifetime = 1.2;
        ring.Explosiveness = 0.9f;
        ring.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.2f, 0f);
        mat.Spread = 180f;
        mat.InitialVelocityMin = 0.8f;
        mat.InitialVelocityMax = 1.8f;
        mat.Gravity = new Vector3(0f, 0.15f, 0f);
        mat.DampingMin = 2.5f;
        mat.DampingMax = 2.5f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        mat.EmissionRingRadius = 0.2f;
        mat.EmissionRingInnerRadius = 0.05f;
        mat.EmissionRingHeight = 0.05f;
        mat.EmissionRingAxis = Vector3.Forward;

        mat.ScaleMin = 0.1f;
        mat.ScaleMax = 0.25f;
        mat.ScaleCurve = GetSmokeRingScaleTexture();

        // Warm grey smoke with slight brownish tint
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.6f, 0.55f, 0.5f, 0.55f));
        colorGrad.SetColor(1, new Color(0.5f, 0.48f, 0.45f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.3f, new Color(0.55f, 0.52f, 0.48f, 0.4f));
        colorGrad.AddPoint(0.7f, new Color(0.52f, 0.5f, 0.47f, 0.12f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        ring.ProcessMaterial = mat;
        ring.DrawPass1 = GetSmokeRingSphere();

        return ring;
    }

    // ==================================================================
    //  MORTAR-SPECIFIC: UPWARD SMOKE BURST
    // ==================================================================

    private static void SpawnMortarSmokeBurst(Node parent, Vector3 position)
    {
        GpuParticles3D burst = new GpuParticles3D();
        burst.Name = "MortarSmokeBurst";
        burst.Amount = 18;
        burst.Lifetime = 1.8;
        burst.Explosiveness = 0.92f;
        burst.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 25f;
        mat.InitialVelocityMin = 0.8f;
        mat.InitialVelocityMax = 2.2f;
        mat.Gravity = new Vector3(0f, 0.4f, 0f); // keep drifting up
        mat.DampingMin = 1.5f;
        mat.DampingMax = 1.5f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.1f;

        mat.ScaleMin = 0.12f;
        mat.ScaleMax = 0.3f;
        mat.ScaleCurve = GetSmokeScaleTexture();

        // Thick, dense white-grey smoke
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.85f, 0.82f, 0.78f, 0.7f));
        colorGrad.SetColor(1, new Color(0.65f, 0.62f, 0.58f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.2f, new Color(0.8f, 0.78f, 0.74f, 0.6f));
        colorGrad.AddPoint(0.6f, new Color(0.7f, 0.68f, 0.64f, 0.25f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        burst.ProcessMaterial = mat;
        burst.DrawPass1 = GetSmokeSphere();

        parent.AddChild(burst);
        burst.GlobalPosition = position;
        burst.Emitting = true;

        // Auto-cleanup
        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)burst.Lifetime + 0.3f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(burst))
                    burst.QueueFree();
            };
        }
    }

    // ==================================================================
    //  MISSILE-SPECIFIC: BACKBLAST + LAUNCH SMOKE + SIDE JETS
    // ==================================================================

    private static void SpawnBackblast(Node parent, Vector3 position, Vector3 rearDirection)
    {
        GpuParticles3D blast;

        if (_backblastPool.Count > 0)
        {
            blast = _backblastPool.Dequeue();
            blast.Visible = true;
            blast.SetProcess(true);
        }
        else
        {
            blast = CreateBackblastParticles();
            parent.AddChild(blast);
        }

        blast.GlobalPosition = position;

        if (blast.ProcessMaterial is ParticleProcessMaterial mat)
        {
            mat.Direction = rearDirection.Normalized();
        }

        blast.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)blast.Lifetime + 0.2f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(blast, _backblastPool);
        }
    }

    private static GpuParticles3D CreateBackblastParticles()
    {
        GpuParticles3D blast = new GpuParticles3D();
        blast.Name = "MissileBackblast";
        blast.Amount = 14;
        blast.Lifetime = 0.5;
        blast.Explosiveness = 0.9f;
        blast.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0f, -1f); // overwritten per-spawn
        mat.Spread = 20f;
        mat.InitialVelocityMin = 3f;
        mat.InitialVelocityMax = 7f;
        mat.Gravity = new Vector3(0f, -1f, 0f);
        mat.DampingMin = 3f;
        mat.DampingMax = 3f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.06f;

        mat.ScaleMin = 0.08f;
        mat.ScaleMax = 0.2f;
        mat.ScaleCurve = GetBackblastScaleTexture();

        // Hot orange-yellow flame fading to dark smoke
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 0.95f, 0.6f, 1f));
        colorGrad.SetColor(1, new Color(0.3f, 0.25f, 0.2f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.15f, new Color(1f, 0.7f, 0.2f, 0.95f));
        colorGrad.AddPoint(0.4f, new Color(0.9f, 0.4f, 0.1f, 0.6f));
        colorGrad.AddPoint(0.7f, new Color(0.4f, 0.3f, 0.2f, 0.2f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        blast.ProcessMaterial = mat;
        blast.DrawPass1 = GetBackblastSphere();

        return blast;
    }

    private static void SpawnMissileLaunchSmoke(Node parent, Vector3 position)
    {
        GpuParticles3D smoke = new GpuParticles3D();
        smoke.Name = "MissileLaunchSmoke";
        smoke.Amount = 16;
        smoke.Lifetime = 2.0;
        smoke.Explosiveness = 0.85f;
        smoke.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.5f, 0f);
        mat.Spread = 90f;
        mat.InitialVelocityMin = 0.5f;
        mat.InitialVelocityMax = 1.5f;
        mat.Gravity = new Vector3(0f, 0.3f, 0f);
        mat.DampingMin = 2f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.15f;

        mat.ScaleMin = 0.15f;
        mat.ScaleMax = 0.35f;
        mat.ScaleCurve = GetSmokeScaleTexture();

        // Dense white/grey billowing smoke cloud
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.8f, 0.78f, 0.75f, 0.6f));
        colorGrad.SetColor(1, new Color(0.55f, 0.52f, 0.5f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.3f, new Color(0.7f, 0.68f, 0.65f, 0.45f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        smoke.ProcessMaterial = mat;
        smoke.DrawPass1 = GetSmokeSphere();

        parent.AddChild(smoke);
        smoke.GlobalPosition = position;
        smoke.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)smoke.Lifetime + 0.3f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(smoke))
                    smoke.QueueFree();
            };
        }
    }

    private static void SpawnSideJet(Node parent, Vector3 position, Vector3 sideDirection)
    {
        GpuParticles3D jet = new GpuParticles3D();
        jet.Name = "MissileSideJet";
        jet.Amount = 6;
        jet.Lifetime = 0.25;
        jet.Explosiveness = 0.95f;
        jet.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = sideDirection.Normalized();
        mat.Spread = 15f;
        mat.InitialVelocityMin = 1.5f;
        mat.InitialVelocityMax = 3.5f;
        mat.Gravity = Vector3.Zero;
        mat.DampingMin = 5f;
        mat.DampingMax = 5f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point;

        mat.ScaleMin = 0.04f;
        mat.ScaleMax = 0.1f;

        // Bright orange flame, fast fade
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 0.85f, 0.4f, 1f));
        colorGrad.SetColor(1, new Color(1f, 0.3f, 0f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.3f, new Color(1f, 0.6f, 0.15f, 0.8f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        jet.ProcessMaterial = mat;
        jet.DrawPass1 = GetFlashSphere();

        parent.AddChild(jet);
        jet.GlobalPosition = position;
        jet.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)jet.Lifetime + 0.15f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(jet))
                    jet.QueueFree();
            };
        }
    }

    // ==================================================================
    //  DRILL-SPECIFIC: SPARKS + GRINDING DEBRIS
    // ==================================================================

    private static void SpawnDrillSparks(Node parent, Vector3 position, Vector3 direction)
    {
        GpuParticles3D sparks;

        if (_sparksPool.Count > 0)
        {
            sparks = _sparksPool.Dequeue();
            sparks.Visible = true;
            sparks.SetProcess(true);
        }
        else
        {
            sparks = CreateDrillSparksParticles();
            parent.AddChild(sparks);
        }

        sparks.GlobalPosition = position + direction.Normalized() * 0.1f;

        if (sparks.ProcessMaterial is ParticleProcessMaterial mat)
        {
            mat.Direction = direction.Normalized();
        }

        sparks.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)sparks.Lifetime + 0.2f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(sparks, _sparksPool);
        }
    }

    private static GpuParticles3D CreateDrillSparksParticles()
    {
        GpuParticles3D sparks = new GpuParticles3D();
        sparks.Name = "DrillSparks";
        sparks.Amount = 24;
        sparks.Lifetime = 0.4;
        sparks.Explosiveness = 0.8f;
        sparks.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0f, 1f); // overwritten per-spawn
        mat.Spread = 60f;
        mat.InitialVelocityMin = 2f;
        mat.InitialVelocityMax = 6f;
        mat.Gravity = new Vector3(0f, -6f, 0f);
        mat.DampingMin = 1f;
        mat.DampingMax = 1f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.08f;

        // Angular velocity for spinning sparks
        mat.AngularVelocityMin = -360f;
        mat.AngularVelocityMax = 360f;

        mat.ScaleMin = 0.01f;
        mat.ScaleMax = 0.04f;

        // Bright yellow-white sparks fading to orange-red
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 1f, 0.85f, 1f));
        colorGrad.SetColor(1, new Color(1f, 0.3f, 0f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.2f, new Color(1f, 0.9f, 0.5f, 1f));
        colorGrad.AddPoint(0.5f, new Color(1f, 0.6f, 0.2f, 0.7f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        sparks.ProcessMaterial = mat;
        sparks.DrawPass1 = GetSparkBox();

        return sparks;
    }

    private static void SpawnGrindingDebris(Node parent, Vector3 position, Vector3 direction)
    {
        GpuParticles3D debris = new GpuParticles3D();
        debris.Name = "DrillGrinding";
        debris.Amount = 10;
        debris.Lifetime = 0.6;
        debris.Explosiveness = 0.7f;
        debris.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        // Debris sprays outward perpendicular to drill direction
        mat.Direction = (direction + Vector3.Up * 0.5f).Normalized();
        mat.Spread = 50f;
        mat.InitialVelocityMin = 1f;
        mat.InitialVelocityMax = 3f;
        mat.Gravity = new Vector3(0f, -8f, 0f);
        mat.DampingMin = 0.5f;
        mat.DampingMax = 0.5f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.06f;

        mat.AngularVelocityMin = -180f;
        mat.AngularVelocityMax = 180f;

        mat.ScaleMin = 0.02f;
        mat.ScaleMax = 0.05f;

        // Brown/tan debris chunks
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.6f, 0.45f, 0.3f, 0.9f));
        colorGrad.SetColor(1, new Color(0.5f, 0.38f, 0.25f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        debris.ProcessMaterial = mat;
        debris.DrawPass1 = GetSparkBox(); // reuse small box mesh for debris chunks

        parent.AddChild(debris);
        debris.GlobalPosition = position;
        debris.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)debris.Lifetime + 0.2f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(debris))
                    debris.QueueFree();
            };
        }
    }

    // ==================================================================
    //  RAILGUN-SPECIFIC: ENERGY FLASH + ELECTRIC ARCS + CAPACITOR VENT
    // ==================================================================

    private static void SpawnRailgunFlash(Node parent, Vector3 position, Vector3 direction)
    {
        GpuParticles3D flash = new GpuParticles3D();
        flash.Name = "RailgunFlash";
        flash.Amount = 12;
        flash.Lifetime = 0.15;
        flash.Explosiveness = 0.98f;
        flash.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = direction.Normalized();
        mat.Spread = 25f;
        mat.InitialVelocityMin = 3f;
        mat.InitialVelocityMax = 8f;
        mat.Gravity = Vector3.Zero;
        mat.DampingMin = 8f;
        mat.DampingMax = 8f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.04f;

        mat.ScaleMin = 0.08f;
        mat.ScaleMax = 0.2f;
        mat.ScaleCurve = GetFlashScaleTexture();

        // Bright cyan/white energy flash
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.8f, 1f, 1f, 1f));
        colorGrad.SetColor(1, new Color(0.2f, 0.6f, 1f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.15f, new Color(0.6f, 0.95f, 1f, 1f));
        colorGrad.AddPoint(0.5f, new Color(0.3f, 0.8f, 1f, 0.5f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        flash.ProcessMaterial = mat;
        flash.DrawPass1 = GetFlashSphere();

        parent.AddChild(flash);
        flash.GlobalPosition = position;
        flash.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)flash.Lifetime + 0.15f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(flash))
                    flash.QueueFree();
            };
        }
    }

    private static void SpawnElectricArcs(Node parent, Vector3 position, Vector3 direction)
    {
        GpuParticles3D arcs;

        if (_electricArcPool.Count > 0)
        {
            arcs = _electricArcPool.Dequeue();
            arcs.Visible = true;
            arcs.SetProcess(true);
        }
        else
        {
            arcs = CreateElectricArcParticles();
            parent.AddChild(arcs);
        }

        arcs.GlobalPosition = position;

        if (arcs.ProcessMaterial is ParticleProcessMaterial mat)
        {
            mat.Direction = direction.Normalized();
        }

        arcs.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)arcs.Lifetime + 0.2f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(arcs, _electricArcPool);
        }
    }

    private static GpuParticles3D CreateElectricArcParticles()
    {
        GpuParticles3D arcs = new GpuParticles3D();
        arcs.Name = "ElectricArcs";
        arcs.Amount = 18;
        arcs.Lifetime = 0.3;
        arcs.Explosiveness = 0.85f;
        arcs.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0f, 1f); // overwritten per-spawn
        mat.Spread = 80f;
        mat.InitialVelocityMin = 1.5f;
        mat.InitialVelocityMax = 5f;
        mat.Gravity = Vector3.Zero;
        mat.DampingMin = 6f;
        mat.DampingMax = 6f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.12f;

        // High angular velocity for electric "jitter"
        mat.AngularVelocityMin = -720f;
        mat.AngularVelocityMax = 720f;

        mat.ScaleMin = 0.01f;
        mat.ScaleMax = 0.035f;
        mat.ScaleCurve = GetArcScaleTexture();

        // Electric blue-white arcs
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.7f, 0.95f, 1f, 1f));
        colorGrad.SetColor(1, new Color(0.15f, 0.4f, 1f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.1f, new Color(0.9f, 1f, 1f, 1f)); // white-hot center
        colorGrad.AddPoint(0.3f, new Color(0.4f, 0.85f, 1f, 0.8f));
        colorGrad.AddPoint(0.6f, new Color(0.2f, 0.5f, 1f, 0.3f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        arcs.ProcessMaterial = mat;
        arcs.DrawPass1 = GetArcSphere();

        return arcs;
    }

    private static void SpawnCapacitorVent(Node parent, Vector3 position)
    {
        GpuParticles3D vent = new GpuParticles3D();
        vent.Name = "CapacitorVent";
        vent.Amount = 8;
        vent.Lifetime = 0.8;
        vent.Explosiveness = 0.75f;
        vent.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 45f;
        mat.InitialVelocityMin = 0.3f;
        mat.InitialVelocityMax = 0.8f;
        mat.Gravity = new Vector3(0f, 0.5f, 0f);
        mat.DampingMin = 2f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.1f;

        mat.ScaleMin = 0.05f;
        mat.ScaleMax = 0.12f;

        // Faint cyan wisps dissipating upward
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.5f, 0.85f, 1f, 0.3f));
        colorGrad.SetColor(1, new Color(0.3f, 0.6f, 0.9f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        vent.ProcessMaterial = mat;
        vent.DrawPass1 = GetSmokeSphere();

        parent.AddChild(vent);
        vent.GlobalPosition = position;
        vent.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            tree.CreateTimer((float)vent.Lifetime + 0.3f).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(vent))
                    vent.QueueFree();
            };
        }
    }

    // ==================================================================
    //  FLASH LIGHT (shared by all weapons)
    // ==================================================================

    private static void SpawnFlashLight(Node parent, Vector3 position, Color color, float energy, float range, float duration)
    {
        OmniLight3D light;

        if (_flashLightPool.Count > 0)
        {
            light = _flashLightPool.Dequeue();
            light.Visible = true;
        }
        else
        {
            light = new OmniLight3D();
            light.Name = "WeaponFlash";
            light.OmniAttenuation = 1.5f;
            light.ShadowEnabled = false;
            parent.AddChild(light);
        }

        light.GlobalPosition = position;
        light.LightColor = color;
        light.LightEnergy = energy;
        light.OmniRange = range;

        // Kill any leftover tween from a previous pool use, then fade out and return
        KillActiveTweens(light);
        Tween tween = light.CreateTween();
        TrackTween(light, tween);
        tween.TweenProperty(light, "light_energy", 0f, duration);
        tween.TweenCallback(Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(light))
                return;

            light.Visible = false;
            light.LightEnergy = 0f;

            if (_flashLightPool.Count < PoolSize)
                _flashLightPool.Enqueue(light);
            else
                light.QueueFree();
        }));
    }

    // ==================================================================
    //  CAMERA SHAKE HELPER
    // ==================================================================

    private static void TriggerCameraShake(Node context, float intensity, float duration)
    {
        foreach (Node node in context.GetTree().GetNodesInGroup("CameraShake"))
        {
            if (node is Camera.CameraShake shake)
            {
                shake.Shake(intensity, duration);
                break;
            }
        }
    }

    // ==================================================================
    //  PARAMETERIZED MUZZLE FLASH (used by per-weapon methods)
    // ==================================================================

    private static void SpawnMuzzleFlash(Node parent, Vector3 position, Vector3 direction,
        int amount, double lifetime, float scaleMin, float scaleMax,
        float velocityMin, float velocityMax, float spread)
    {
        GpuParticles3D flash;

        if (_muzzleFlashPool.Count > 0)
        {
            flash = _muzzleFlashPool.Dequeue();
            flash.Visible = true;
            flash.SetProcess(true);
        }
        else
        {
            flash = CreateMuzzleFlashParticles();
            parent.AddChild(flash);
        }

        flash.GlobalPosition = position;
        flash.Amount = amount;
        flash.Lifetime = lifetime;

        if (flash.ProcessMaterial is ParticleProcessMaterial mat)
        {
            mat.Direction = direction.Normalized();
            mat.Spread = spread;
            mat.ScaleMin = scaleMin;
            mat.ScaleMax = scaleMax;
            mat.InitialVelocityMin = velocityMin;
            mat.InitialVelocityMax = velocityMax;
        }

        flash.Emitting = true;

        SceneTree? tree = parent.GetTree();
        if (tree != null)
        {
            float delay = (float)flash.Lifetime + 0.1f;
            tree.CreateTimer(delay).Timeout += () => ReturnToPool(flash, _muzzleFlashPool);
        }
    }

    // ==================================================================
    //  TWEEN CLEANUP
    // ==================================================================

    // Track active tweens per node to prevent tween accumulation.
    // Without this, rapid-fire weapons create unbounded tweens that pile up
    // and eventually cause Godot to silently fail creating new tweens,
    // making all animations stop mid-game.
    private static readonly Dictionary<ulong, Tween> _activeTweens = new();

    /// <summary>
    /// Kills any previously tracked tween on a node and stores the new one.
    /// Must be called after creating a tween to register it for cleanup.
    /// </summary>
    private static void TrackTween(Node node, Tween tween)
    {
        ulong id = node.GetInstanceId();
        if (_activeTweens.TryGetValue(id, out Tween? existing) && existing != null && existing.IsValid())
        {
            existing.Kill();
        }
        _activeTweens[id] = tween;

        // Auto-remove from tracking when the tween finishes
        tween.Finished += () =>
        {
            if (_activeTweens.TryGetValue(id, out Tween? current) && current == tween)
            {
                _activeTweens.Remove(id);
            }
        };
    }

    /// <summary>
    /// Kills any existing tracked tween on a node. Call before creating a new tween.
    /// </summary>
    private static void KillActiveTweens(Node node)
    {
        ulong id = node.GetInstanceId();
        if (_activeTweens.TryGetValue(id, out Tween? existing) && existing != null && existing.IsValid())
        {
            existing.Kill();
            _activeTweens.Remove(id);
        }
    }

    // ==================================================================
    //  POOL RETURN HELPERS
    // ==================================================================

    private static void ReturnToPool(GpuParticles3D particles, Queue<GpuParticles3D> pool)
    {
        if (!GodotObject.IsInstanceValid(particles))
            return;

        particles.Emitting = false;
        particles.Visible = false;
        particles.SetProcess(false);

        if (pool.Count < PoolSize)
            pool.Enqueue(particles);
        else
            particles.QueueFree();
    }

    // ==================================================================
    //  PARTICLE SYSTEM CONSTRUCTORS
    // ==================================================================

    private static GpuParticles3D CreateMuzzleFlashParticles()
    {
        GpuParticles3D flash = new GpuParticles3D();
        flash.Name = "MuzzleFlash";
        flash.Amount = 10;
        flash.Lifetime = 0.2;
        flash.Explosiveness = 0.95f;
        flash.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0f, 1f);
        mat.Spread = 30f;
        mat.InitialVelocityMin = 1.5f;
        mat.InitialVelocityMax = 4f;
        mat.Gravity = Vector3.Zero;
        mat.DampingMin = 5f;
        mat.DampingMax = 5f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.05f;

        mat.ScaleMin = 0.06f;
        mat.ScaleMax = 0.15f;
        mat.ScaleCurve = GetFlashScaleTexture();

        // Bright white-hot center fading to orange, then transparent
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(1f, 1f, 0.9f, 1f));
        colorGrad.SetColor(1, new Color(1f, 0.4f, 0f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.2f, new Color(1f, 0.95f, 0.6f, 1f));
        colorGrad.AddPoint(0.5f, new Color(1f, 0.65f, 0.15f, 0.7f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        flash.ProcessMaterial = mat;
        flash.DrawPass1 = GetFlashSphere();

        return flash;
    }

    private static GpuParticles3D CreateSmokePuffParticles()
    {
        GpuParticles3D smoke = new GpuParticles3D();
        smoke.Name = "SmokePuff";
        smoke.Amount = 20;
        smoke.Lifetime = 1.5;
        smoke.Explosiveness = 0.85f;
        smoke.OneShot = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 1f, 0f);
        mat.Spread = 40f;
        mat.InitialVelocityMin = 0.3f;
        mat.InitialVelocityMax = 0.8f;
        mat.Gravity = new Vector3(0f, 0.3f, 0f);
        mat.DampingMin = 2f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.08f;

        mat.ScaleMin = 0.08f;
        mat.ScaleMax = 0.2f;
        mat.ScaleCurve = GetSmokeScaleTexture();

        // Dense grey/white smoke fading to transparent
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.75f, 0.72f, 0.7f, 0.6f));
        colorGrad.SetColor(1, new Color(0.6f, 0.58f, 0.55f, 0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.3f, new Color(0.7f, 0.68f, 0.65f, 0.45f));
        colorGrad.AddPoint(0.7f, new Color(0.65f, 0.62f, 0.6f, 0.15f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        smoke.ProcessMaterial = mat;
        smoke.DrawPass1 = GetSmokeSphere();

        return smoke;
    }

    // ==================================================================
    //  CACHED RESOURCE HELPERS
    // ==================================================================

    private static CurveTexture GetFlashScaleTexture()
    {
        if (_cachedFlashScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1f));
            curve.AddPoint(new Vector2(0.3f, 0.6f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedFlashScaleTex = new CurveTexture();
            _cachedFlashScaleTex.Curve = curve;
        }
        return _cachedFlashScaleTex;
    }

    private static CurveTexture GetSmokeScaleTexture()
    {
        if (_cachedSmokeScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.4f));
            curve.AddPoint(new Vector2(0.2f, 0.8f));
            curve.AddPoint(new Vector2(0.5f, 1f));
            curve.AddPoint(new Vector2(1f, 1.3f));
            _cachedSmokeScaleTex = new CurveTexture();
            _cachedSmokeScaleTex.Curve = curve;
        }
        return _cachedSmokeScaleTex;
    }

    private static CurveTexture GetIdleSmokeScaleTexture()
    {
        if (_cachedIdleSmokeScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.3f));
            curve.AddPoint(new Vector2(0.3f, 1f));
            curve.AddPoint(new Vector2(0.7f, 1.2f));
            curve.AddPoint(new Vector2(1f, 0.8f));
            _cachedIdleSmokeScaleTex = new CurveTexture();
            _cachedIdleSmokeScaleTex.Curve = curve;
        }
        return _cachedIdleSmokeScaleTex;
    }

    private static CurveTexture GetSmokeRingScaleTexture()
    {
        if (_cachedSmokeRingScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.5f));
            curve.AddPoint(new Vector2(0.15f, 0.9f));
            curve.AddPoint(new Vector2(0.4f, 1f));
            curve.AddPoint(new Vector2(1f, 1.4f));
            _cachedSmokeRingScaleTex = new CurveTexture();
            _cachedSmokeRingScaleTex.Curve = curve;
        }
        return _cachedSmokeRingScaleTex;
    }

    private static CurveTexture GetBackblastScaleTexture()
    {
        if (_cachedBackblastScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1f));
            curve.AddPoint(new Vector2(0.2f, 0.8f));
            curve.AddPoint(new Vector2(0.5f, 1.2f)); // billows out
            curve.AddPoint(new Vector2(1f, 0.3f));
            _cachedBackblastScaleTex = new CurveTexture();
            _cachedBackblastScaleTex.Curve = curve;
        }
        return _cachedBackblastScaleTex;
    }

    private static CurveTexture GetArcScaleTexture()
    {
        if (_cachedArcScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.8f));
            curve.AddPoint(new Vector2(0.1f, 1f));
            curve.AddPoint(new Vector2(0.5f, 0.5f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedArcScaleTex = new CurveTexture();
            _cachedArcScaleTex.Curve = curve;
        }
        return _cachedArcScaleTex;
    }

    // ---- Mesh caches ----

    private static SphereMesh GetFlashSphere()
    {
        if (_cachedFlashSphere == null)
            _cachedFlashSphere = CreateBillboardSphere(0.12f);
        return _cachedFlashSphere;
    }

    private static SphereMesh GetSmokeSphere()
    {
        if (_cachedSmokeSphere == null)
            _cachedSmokeSphere = CreateBillboardSphere(0.15f);
        return _cachedSmokeSphere;
    }

    private static SphereMesh GetIdleSmokeSphere()
    {
        if (_cachedIdleSmokeSphere == null)
            _cachedIdleSmokeSphere = CreateBillboardSphere(0.1f);
        return _cachedIdleSmokeSphere;
    }

    private static SphereMesh GetSmokeRingSphere()
    {
        if (_cachedSmokeRingSphere == null)
            _cachedSmokeRingSphere = CreateBillboardSphere(0.18f);
        return _cachedSmokeRingSphere;
    }

    private static SphereMesh GetBackblastSphere()
    {
        if (_cachedBackblastSphere == null)
            _cachedBackblastSphere = CreateBillboardSphere(0.14f);
        return _cachedBackblastSphere;
    }

    private static BoxMesh GetSparkBox()
    {
        if (_cachedSparkBox == null)
        {
            _cachedSparkBox = new BoxMesh();
            _cachedSparkBox.Size = new Vector3(0.015f, 0.015f, 0.08f);
            StandardMaterial3D boxMat = new StandardMaterial3D();
            boxMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            boxMat.AlbedoColor = Colors.White;
            boxMat.VertexColorUseAsAlbedo = true;
            boxMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            boxMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            boxMat.NoDepthTest = false;
            _cachedSparkBox.Material = boxMat;
        }
        return _cachedSparkBox;
    }

    private static SphereMesh GetArcSphere()
    {
        if (_cachedArcSphere == null)
        {
            _cachedArcSphere = new SphereMesh();
            _cachedArcSphere.Radius = 0.04f;
            _cachedArcSphere.Height = 0.08f;
            _cachedArcSphere.RadialSegments = 4;
            _cachedArcSphere.Rings = 2;
            StandardMaterial3D mat = new StandardMaterial3D();
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.AlbedoColor = Colors.White;
            mat.VertexColorUseAsAlbedo = true;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            mat.NoDepthTest = false;
            mat.EmissionEnabled = true;
            mat.Emission = new Color(0.3f, 0.8f, 1f);
            mat.EmissionEnergyMultiplier = 2f;
            _cachedArcSphere.Material = mat;
        }
        return _cachedArcSphere;
    }

    private static SphereMesh CreateBillboardSphere(float radius)
    {
        SphereMesh sphere = new SphereMesh();
        sphere.Radius = radius;
        sphere.Height = radius * 2f;
        sphere.RadialSegments = 6;
        sphere.Rings = 3;
        StandardMaterial3D mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.AlbedoColor = Colors.White;
        mat.VertexColorUseAsAlbedo = true; // Required for ColorRamp to tint particles
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        mat.NoDepthTest = false;
        sphere.Material = mat;
        return sphere;
    }
}
