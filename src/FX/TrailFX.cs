using Godot;

namespace VoxelSiege.FX;

/// <summary>
/// Projectile trail effects. Attach as a child of a projectile node.
/// Supports multiple trail styles, each tuned for a specific weapon type:
///   - Cannon: Thick warm-grey smoke with slight orange glow, lingers and dissipates slowly
///   - Mortar: Puffy arcing smoke trail with brief spark shower at launch
///   - Rocket: Bright orange-yellow flame with white smoke exhaust (most dramatic)
///   - Drill: Spinning grey metal sparks spiraling around the flight path
///   - Energy: Bright cyan particles for railgun slugs (rarely used; railgun is hitscan)
/// Particle resources are cached as static fields to avoid per-trail allocations.
///
/// Key rendering notes:
///   - All draw-pass materials set VertexColorUseAsAlbedo = true so that
///     ParticleProcessMaterial.ColorRamp actually tints the particles.
///   - BillboardMode = Particles ensures smoke always faces the camera.
///   - NoDepthTest = false so trails are properly occluded by solid geometry.
///   - AngularVelocity gives smoke puffs a tumbling look.
/// </summary>
public partial class TrailFX : Node3D
{
    private GpuParticles3D _primary = null!;
    private GpuParticles3D? _secondary;
    private GpuParticles3D? _tertiary;
    private OmniLight3D? _glowLight;
    private float _orphanTimer;
    private bool _orphaned;
    private float _orphanLifetime;

    // Cached draw pass meshes (shared across all trails)
    private static SphereMesh? _cachedSmokeSphere030;
    private static SphereMesh? _cachedSmokeSphere020;
    private static SphereMesh? _cachedSmokeSphere012;
    private static SphereMesh? _cachedSmokeSphere006;
    private static SphereMesh? _cachedSmokeSphere003;
    private static BoxMesh? _cachedSparkBox;
    private static PrismMesh? _cachedSparkPrism;

    // Cached curve textures
    private static CurveTexture? _cachedCannonSmokeScaleTex;
    private static CurveTexture? _cachedCannonEmberScaleTex;
    private static CurveTexture? _cachedMortarSmokeScaleTex;
    private static CurveTexture? _cachedMortarSparkScaleTex;
    private static CurveTexture? _cachedRocketFireScaleTex;
    private static CurveTexture? _cachedRocketSmokeScaleTex;
    private static CurveTexture? _cachedRocketCoreScaleTex;
    private static CurveTexture? _cachedDrillSparkScaleTex;
    private static CurveTexture? _cachedEnergyScaleTex;

    // Cached alpha curves (for proper opacity falloff)
    private static CurveTexture? _cachedSmokeAlphaTex;
    private static CurveTexture? _cachedFireAlphaTex;

    public enum TrailStyle
    {
        Smoke,
        Mortar,
        Energy,
        Rocket,
        Drill
    }

    /// <summary>
    /// Creates a cannon smoke trail: thick warm-grey smoke puffs with a faint orange
    /// glow at the origin. Trail lingers and dissipates slowly.
    /// </summary>
    public static TrailFX CreateSmokeTrail(Node3D projectile)
    {
        TrailFX trail = new TrailFX();
        projectile.AddChild(trail);
        trail.InitializeCannonSmoke();
        trail._orphanLifetime = 2.0f;
        return trail;
    }

    /// <summary>
    /// Creates a mortar trail: puffy arcing smoke with a brief spark shower at launch.
    /// </summary>
    public static TrailFX CreateMortarTrail(Node3D projectile)
    {
        TrailFX trail = new TrailFX();
        projectile.AddChild(trail);
        trail.InitializeMortarSmoke();
        trail._orphanLifetime = 2.0f;
        return trail;
    }

    /// <summary>
    /// Creates an energy trail (railgun slugs — rarely used since railgun is hitscan).
    /// </summary>
    public static TrailFX CreateEnergyTrail(Node3D projectile)
    {
        TrailFX trail = new TrailFX();
        projectile.AddChild(trail);
        trail.InitializeEnergy();
        trail._orphanLifetime = 0.5f;
        return trail;
    }

    /// <summary>
    /// Creates a rocket trail: bright orange-yellow flame core with white smoke exhaust.
    /// The most dramatic trail. Includes a point light for illumination.
    /// </summary>
    public static TrailFX CreateRocketTrail(Node3D projectile)
    {
        TrailFX trail = new TrailFX();
        projectile.AddChild(trail);
        trail.InitializeRocket();
        trail._orphanLifetime = 2.5f;
        return trail;
    }

    /// <summary>
    /// Creates a drill trail: small grey metal sparks spiraling around the flight path.
    /// </summary>
    public static TrailFX CreateDrillTrail(Node3D projectile)
    {
        TrailFX trail = new TrailFX();
        projectile.AddChild(trail);
        trail.InitializeDrill();
        trail._orphanLifetime = 0.8f;
        return trail;
    }

    /// <summary>
    /// Call when the projectile is destroyed to let the trail linger and fade.
    /// Reparents the trail to the scene root so it persists.
    /// </summary>
    public void Detach()
    {
        if (_orphaned)
        {
            return;
        }

        _orphaned = true;
        _orphanTimer = 0f;

        // Stop emitting new particles
        _primary.Emitting = false;
        if (_secondary != null)
        {
            _secondary.Emitting = false;
        }
        if (_tertiary != null)
        {
            _tertiary.Emitting = false;
        }

        // Reparent to scene root so trail persists after projectile is freed
        Vector3 globalPos = GlobalPosition;
        Node? root = GetTree()?.Root;
        if (root != null && GetParent() != null)
        {
            GetParent().RemoveChild(this);
            root.AddChild(this);
            GlobalPosition = globalPos;
        }
    }

    public override void _Process(double delta)
    {
        if (!_orphaned)
        {
            return;
        }

        _orphanTimer += (float)delta;

        // Fade glow light during orphan period
        if (_glowLight != null && GodotObject.IsInstanceValid(_glowLight))
        {
            _glowLight.LightEnergy = Mathf.Max(0f, _glowLight.LightEnergy - (float)delta * 4f);
        }

        if (_orphanTimer >= _orphanLifetime)
        {
            QueueFree();
        }
    }

    // ========================================================================
    //  Cached resource helpers
    // ========================================================================

    // ---- Cannon smoke scale: starts small, expands to fat cloud as it lingers ----
    private static CurveTexture GetCannonSmokeScaleTexture()
    {
        if (_cachedCannonSmokeScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.4f));
            curve.AddPoint(new Vector2(0.1f, 0.7f));
            curve.AddPoint(new Vector2(0.35f, 1.0f));
            curve.AddPoint(new Vector2(0.7f, 1.3f));
            curve.AddPoint(new Vector2(1f, 1.5f));
            _cachedCannonSmokeScaleTex = new CurveTexture();
            _cachedCannonSmokeScaleTex.Curve = curve;
        }
        return _cachedCannonSmokeScaleTex;
    }

    // ---- Cannon ember scale: bright at birth, shrinks fast ----
    private static CurveTexture GetCannonEmberScaleTexture()
    {
        if (_cachedCannonEmberScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1.0f));
            curve.AddPoint(new Vector2(0.3f, 0.6f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedCannonEmberScaleTex = new CurveTexture();
            _cachedCannonEmberScaleTex.Curve = curve;
        }
        return _cachedCannonEmberScaleTex;
    }

    // ---- Mortar smoke scale: puffier, rounder clouds ----
    private static CurveTexture GetMortarSmokeScaleTexture()
    {
        if (_cachedMortarSmokeScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.5f));
            curve.AddPoint(new Vector2(0.08f, 0.9f));
            curve.AddPoint(new Vector2(0.3f, 1.3f));
            curve.AddPoint(new Vector2(0.7f, 1.6f));
            curve.AddPoint(new Vector2(1f, 1.8f));
            _cachedMortarSmokeScaleTex = new CurveTexture();
            _cachedMortarSmokeScaleTex.Curve = curve;
        }
        return _cachedMortarSmokeScaleTex;
    }

    // ---- Mortar spark scale: bright at birth, shrink to nothing ----
    private static CurveTexture GetMortarSparkScaleTexture()
    {
        if (_cachedMortarSparkScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1.0f));
            curve.AddPoint(new Vector2(0.3f, 0.7f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedMortarSparkScaleTex = new CurveTexture();
            _cachedMortarSparkScaleTex.Curve = curve;
        }
        return _cachedMortarSparkScaleTex;
    }

    // ---- Rocket fire scale: full brightness at birth, shrinks as it cools ----
    private static CurveTexture GetRocketFireScaleTexture()
    {
        if (_cachedRocketFireScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1.0f));
            curve.AddPoint(new Vector2(0.2f, 0.85f));
            curve.AddPoint(new Vector2(0.5f, 0.5f));
            curve.AddPoint(new Vector2(0.8f, 0.2f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedRocketFireScaleTex = new CurveTexture();
            _cachedRocketFireScaleTex.Curve = curve;
        }
        return _cachedRocketFireScaleTex;
    }

    // ---- Rocket smoke scale: starts thin, billow out as it cools ----
    private static CurveTexture GetRocketSmokeScaleTexture()
    {
        if (_cachedRocketSmokeScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.3f));
            curve.AddPoint(new Vector2(0.15f, 0.7f));
            curve.AddPoint(new Vector2(0.4f, 1.1f));
            curve.AddPoint(new Vector2(0.7f, 1.4f));
            curve.AddPoint(new Vector2(1f, 1.6f));
            _cachedRocketSmokeScaleTex = new CurveTexture();
            _cachedRocketSmokeScaleTex.Curve = curve;
        }
        return _cachedRocketSmokeScaleTex;
    }

    // ---- Rocket core scale: intense bright core that fades ----
    private static CurveTexture GetRocketCoreScaleTexture()
    {
        if (_cachedRocketCoreScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1.0f));
            curve.AddPoint(new Vector2(0.15f, 0.85f));
            curve.AddPoint(new Vector2(0.5f, 0.4f));
            curve.AddPoint(new Vector2(1f, 0.1f));
            _cachedRocketCoreScaleTex = new CurveTexture();
            _cachedRocketCoreScaleTex.Curve = curve;
        }
        return _cachedRocketCoreScaleTex;
    }

    // ---- Drill spark scale: bright flash, fades quickly ----
    private static CurveTexture GetDrillSparkScaleTexture()
    {
        if (_cachedDrillSparkScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 1.0f));
            curve.AddPoint(new Vector2(0.15f, 0.85f));
            curve.AddPoint(new Vector2(0.5f, 0.4f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedDrillSparkScaleTex = new CurveTexture();
            _cachedDrillSparkScaleTex.Curve = curve;
        }
        return _cachedDrillSparkScaleTex;
    }

    // ---- Energy scale: tight core that expands slightly then fades ----
    private static CurveTexture GetEnergyScaleTexture()
    {
        if (_cachedEnergyScaleTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.8f));
            curve.AddPoint(new Vector2(0.3f, 1.0f));
            curve.AddPoint(new Vector2(1f, 0f));
            _cachedEnergyScaleTex = new CurveTexture();
            _cachedEnergyScaleTex.Curve = curve;
        }
        return _cachedEnergyScaleTex;
    }

    // ---- Smoke alpha curve: thick at birth, proper falloff to transparent ----
    private static CurveTexture GetSmokeAlphaTexture()
    {
        if (_cachedSmokeAlphaTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.0f));  // fade in briefly
            curve.AddPoint(new Vector2(0.05f, 0.8f)); // quickly reach peak opacity
            curve.AddPoint(new Vector2(0.2f, 0.7f));  // hold thick
            curve.AddPoint(new Vector2(0.5f, 0.35f)); // gradual fade
            curve.AddPoint(new Vector2(0.8f, 0.1f));  // nearly gone
            curve.AddPoint(new Vector2(1f, 0.0f));   // fully transparent
            _cachedSmokeAlphaTex = new CurveTexture();
            _cachedSmokeAlphaTex.Curve = curve;
        }
        return _cachedSmokeAlphaTex;
    }

    // ---- Fire alpha curve: bright at birth, fast falloff ----
    private static CurveTexture GetFireAlphaTexture()
    {
        if (_cachedFireAlphaTex == null)
        {
            Curve curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.0f));
            curve.AddPoint(new Vector2(0.03f, 1.0f)); // instant full brightness
            curve.AddPoint(new Vector2(0.2f, 0.85f));
            curve.AddPoint(new Vector2(0.5f, 0.4f));
            curve.AddPoint(new Vector2(1f, 0.0f));
            _cachedFireAlphaTex = new CurveTexture();
            _cachedFireAlphaTex.Curve = curve;
        }
        return _cachedFireAlphaTex;
    }

    // ========================================================================
    //  Mesh caching -- all materials have VertexColorUseAsAlbedo + billboard
    // ========================================================================

    /// <summary>
    /// Returns a cached billboard sphere mesh at the nearest standard size.
    /// All materials are configured with:
    ///   - VertexColorUseAsAlbedo = true (required for ColorRamp tinting)
    ///   - BillboardMode = Particles (always face camera)
    ///   - NoDepthTest = false (no harsh geometry clipping)
    ///   - Alpha transparency
    /// </summary>
    private static SphereMesh GetSmokeSphere(float radius)
    {
        if (radius <= 0.04f)
        {
            _cachedSmokeSphere003 ??= CreateSmokeSphere(0.03f);
            return _cachedSmokeSphere003;
        }
        if (radius <= 0.09f)
        {
            _cachedSmokeSphere006 ??= CreateSmokeSphere(0.06f);
            return _cachedSmokeSphere006;
        }
        if (radius <= 0.16f)
        {
            _cachedSmokeSphere012 ??= CreateSmokeSphere(0.12f);
            return _cachedSmokeSphere012;
        }
        if (radius <= 0.25f)
        {
            _cachedSmokeSphere020 ??= CreateSmokeSphere(0.2f);
            return _cachedSmokeSphere020;
        }
        _cachedSmokeSphere030 ??= CreateSmokeSphere(0.3f);
        return _cachedSmokeSphere030;
    }

    private static SphereMesh CreateSmokeSphere(float radius)
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

    private static BoxMesh GetSparkBox()
    {
        if (_cachedSparkBox == null)
        {
            _cachedSparkBox = new BoxMesh();
            _cachedSparkBox.Size = new Vector3(0.02f, 0.02f, 0.06f);
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

    private static PrismMesh GetSparkPrism()
    {
        if (_cachedSparkPrism == null)
        {
            _cachedSparkPrism = new PrismMesh();
            _cachedSparkPrism.Size = new Vector3(0.015f, 0.04f, 0.015f);
            StandardMaterial3D prismMat = new StandardMaterial3D();
            prismMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            prismMat.AlbedoColor = Colors.White;
            prismMat.VertexColorUseAsAlbedo = true;
            prismMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            prismMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            prismMat.NoDepthTest = false;
            _cachedSparkPrism.Material = prismMat;
        }
        return _cachedSparkPrism;
    }

    // ========================================================================
    //  Trail initializers — one per weapon type
    // ========================================================================

    /// <summary>
    /// Cannon trail: Thick warm-grey smoke that lingers with a faint orange
    /// glow at the source. Trail is opaque near the projectile and fades
    /// gradually to transparent over ~1.5s.
    /// Primary = thick smoke puffs that expand and linger with turbulent rotation.
    /// Secondary = warm ember glow particles close to the cannonball.
    /// </summary>
    private void InitializeCannonSmoke()
    {
        // --- Primary: thick warm-grey smoke puffs ---
        _primary = new GpuParticles3D();
        _primary.Amount = 24;
        _primary.Lifetime = 1.5;
        _primary.Emitting = true;
        _primary.Randomness = 0.3f;

        ParticleProcessMaterial smokeMat = new ParticleProcessMaterial();
        smokeMat.Direction = new Vector3(0f, 1f, 0f);
        smokeMat.Spread = 25f;
        smokeMat.InitialVelocityMin = 0.15f;
        smokeMat.InitialVelocityMax = 0.6f;
        smokeMat.Gravity = new Vector3(0f, 0.35f, 0f); // slow upward drift
        smokeMat.DampingMin = 3f;
        smokeMat.DampingMax = 5f;
        smokeMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        smokeMat.EmissionSphereRadius = 0.06f;

        // Turbulent rotation for natural smoke tumbling
        smokeMat.AngularVelocityMin = -90f;
        smokeMat.AngularVelocityMax = 90f;

        // Scale: particles grow from small puffs to fat clouds
        smokeMat.ScaleMin = 0.15f;
        smokeMat.ScaleMax = 0.35f;
        smokeMat.ScaleCurve = GetCannonSmokeScaleTexture();

        // Warm grey smoke — slightly tan/brown tint for "Tactical Toy Warfare" warmth
        // Opacity handled via ColorRamp alpha + AlphaCurve for proper falloff
        Gradient smokeGrad = new Gradient();
        smokeGrad.SetColor(0, new Color(0.72f, 0.68f, 0.60f, 0.75f));
        smokeGrad.AddPoint(0.1f, new Color(0.68f, 0.64f, 0.56f, 0.7f));
        smokeGrad.AddPoint(0.35f, new Color(0.62f, 0.58f, 0.52f, 0.5f));
        smokeGrad.AddPoint(0.65f, new Color(0.56f, 0.53f, 0.48f, 0.25f));
        smokeGrad.SetColor(1, new Color(0.50f, 0.48f, 0.44f, 0.0f));
        GradientTexture1D smokeTex = new GradientTexture1D();
        smokeTex.Gradient = smokeGrad;
        smokeMat.ColorRamp = smokeTex;

        // Alpha curve: thick near the projectile, gentle fade-out
        smokeMat.AlphaCurve = GetSmokeAlphaTexture();

        // Slight hue variation between particles for organic feel
        smokeMat.HueVariationMin = -0.03f;
        smokeMat.HueVariationMax = 0.03f;

        _primary.ProcessMaterial = smokeMat;
        _primary.DrawPass1 = GetSmokeSphere(0.2f);
        AddChild(_primary);

        // --- Secondary: warm ember glow close to the cannonball ---
        _secondary = new GpuParticles3D();
        _secondary.Amount = 10;
        _secondary.Lifetime = 0.35;
        _secondary.Emitting = true;
        _secondary.Randomness = 0.2f;

        ParticleProcessMaterial glowMat = new ParticleProcessMaterial();
        glowMat.Direction = new Vector3(0f, 0f, -1f); // trails behind
        glowMat.Spread = 15f;
        glowMat.InitialVelocityMin = 0.05f;
        glowMat.InitialVelocityMax = 0.2f;
        glowMat.Gravity = Vector3.Zero;
        glowMat.DampingMin = 2f;
        glowMat.DampingMax = 3f;
        glowMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        glowMat.EmissionSphereRadius = 0.03f;

        glowMat.ScaleMin = 0.04f;
        glowMat.ScaleMax = 0.08f;
        glowMat.ScaleCurve = GetCannonEmberScaleTexture();

        // Warm orange glow fading to dark red
        Gradient glowGrad = new Gradient();
        glowGrad.SetColor(0, new Color(1f, 0.7f, 0.25f, 0.85f));
        glowGrad.AddPoint(0.3f, new Color(1f, 0.5f, 0.12f, 0.6f));
        glowGrad.SetColor(1, new Color(0.8f, 0.25f, 0.05f, 0f));
        GradientTexture1D glowTex = new GradientTexture1D();
        glowTex.Gradient = glowGrad;
        glowMat.ColorRamp = glowTex;

        glowMat.AlphaCurve = GetFireAlphaTexture();

        _secondary.ProcessMaterial = glowMat;
        _secondary.DrawPass1 = GetSmokeSphere(0.06f);
        AddChild(_secondary);
    }

    /// <summary>
    /// Mortar trail: puffy arcing smoke clouds (bigger, rounder than cannon) with
    /// a brief spark shower at launch that fades after 0.4s.
    /// Primary = large puffy smoke clouds with heavy turbulence.
    /// Secondary = launch sparks (OneShot burst).
    /// </summary>
    private void InitializeMortarSmoke()
    {
        // --- Primary: big puffy smoke clouds ---
        _primary = new GpuParticles3D();
        _primary.Amount = 26;
        _primary.Lifetime = 1.6;
        _primary.Emitting = true;
        _primary.Randomness = 0.4f;

        ParticleProcessMaterial smokeMat = new ParticleProcessMaterial();
        smokeMat.Direction = new Vector3(0f, 1f, 0f);
        smokeMat.Spread = 35f; // wider spread for puffier look
        smokeMat.InitialVelocityMin = 0.2f;
        smokeMat.InitialVelocityMax = 0.7f;
        smokeMat.Gravity = new Vector3(0f, 0.45f, 0f); // drifts upward
        smokeMat.DampingMin = 2.5f;
        smokeMat.DampingMax = 4f;
        smokeMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        smokeMat.EmissionSphereRadius = 0.1f;

        // Heavy turbulent rotation for puffy clouds
        smokeMat.AngularVelocityMin = -120f;
        smokeMat.AngularVelocityMax = 120f;

        smokeMat.ScaleMin = 0.18f;
        smokeMat.ScaleMax = 0.4f;
        smokeMat.ScaleCurve = GetMortarSmokeScaleTexture();

        // Warm grey smoke — slightly brighter/puffier than cannon, warmer tone
        Gradient smokeGrad = new Gradient();
        smokeGrad.SetColor(0, new Color(0.76f, 0.72f, 0.64f, 0.7f));
        smokeGrad.AddPoint(0.1f, new Color(0.72f, 0.68f, 0.60f, 0.65f));
        smokeGrad.AddPoint(0.35f, new Color(0.66f, 0.62f, 0.56f, 0.4f));
        smokeGrad.AddPoint(0.65f, new Color(0.58f, 0.55f, 0.50f, 0.15f));
        smokeGrad.SetColor(1, new Color(0.52f, 0.50f, 0.46f, 0f));
        GradientTexture1D smokeTex = new GradientTexture1D();
        smokeTex.Gradient = smokeGrad;
        smokeMat.ColorRamp = smokeTex;

        smokeMat.AlphaCurve = GetSmokeAlphaTexture();

        // Subtle hue variation for organic feel
        smokeMat.HueVariationMin = -0.04f;
        smokeMat.HueVariationMax = 0.04f;

        _primary.ProcessMaterial = smokeMat;
        _primary.DrawPass1 = GetSmokeSphere(0.3f);
        AddChild(_primary);

        // --- Secondary: spark shower at launch (OneShot burst) ---
        _secondary = new GpuParticles3D();
        _secondary.Amount = 20;
        _secondary.Lifetime = 0.45;
        _secondary.Explosiveness = 0.85f;
        _secondary.OneShot = true;
        _secondary.Emitting = true;
        _secondary.Randomness = 0.3f;

        ParticleProcessMaterial sparkMat = new ParticleProcessMaterial();
        sparkMat.Direction = new Vector3(0f, 1f, 0f);
        sparkMat.Spread = 55f; // wide spray
        sparkMat.InitialVelocityMin = 1.8f;
        sparkMat.InitialVelocityMax = 4.5f;
        sparkMat.Gravity = new Vector3(0f, -7f, 0f); // sparks fall with gravity
        sparkMat.DampingMin = 1f;
        sparkMat.DampingMax = 2f;
        sparkMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        sparkMat.EmissionSphereRadius = 0.04f;

        sparkMat.ScaleMin = 0.025f;
        sparkMat.ScaleMax = 0.06f;
        sparkMat.ScaleCurve = GetMortarSparkScaleTexture();

        // Bright orange-yellow sparks that cool to red
        Gradient sparkGrad = new Gradient();
        sparkGrad.SetColor(0, new Color(1f, 0.92f, 0.5f, 1f));
        sparkGrad.AddPoint(0.2f, new Color(1f, 0.75f, 0.25f, 0.9f));
        sparkGrad.AddPoint(0.5f, new Color(1f, 0.5f, 0.1f, 0.6f));
        sparkGrad.SetColor(1, new Color(0.8f, 0.3f, 0.05f, 0f));
        GradientTexture1D sparkTex = new GradientTexture1D();
        sparkTex.Gradient = sparkGrad;
        sparkMat.ColorRamp = sparkTex;

        _secondary.ProcessMaterial = sparkMat;
        _secondary.DrawPass1 = GetSparkPrism();
        AddChild(_secondary);
    }

    /// <summary>
    /// Energy trail: bright cyan particles with subtle glow light.
    /// Fades quickly. Used if a rail slug projectile ever flies (currently hitscan).
    /// Primary = tight cyan core particles.
    /// Includes OmniLight3D for illumination.
    /// </summary>
    private void InitializeEnergy()
    {
        _primary = new GpuParticles3D();
        _primary.Amount = 24;
        _primary.Lifetime = 0.25;
        _primary.Emitting = true;
        _primary.Randomness = 0.2f;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0f, -1f);
        mat.Spread = 10f;
        mat.InitialVelocityMin = 0.05f;
        mat.InitialVelocityMax = 0.25f;
        mat.Gravity = Vector3.Zero;
        mat.DampingMin = 1f;
        mat.DampingMax = 2f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.02f;

        mat.ScaleMin = 0.03f;
        mat.ScaleMax = 0.06f;
        mat.ScaleCurve = GetEnergyScaleTexture();

        // Bright cyan/blue glow -- saturated to match toy warfare palette
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.5f, 1f, 1f, 1f));
        colorGrad.AddPoint(0.2f, new Color(0.35f, 0.85f, 1f, 0.9f));
        colorGrad.AddPoint(0.6f, new Color(0.2f, 0.6f, 1f, 0.5f));
        colorGrad.SetColor(1, new Color(0.1f, 0.35f, 0.9f, 0f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _primary.ProcessMaterial = mat;
        _primary.DrawPass1 = GetSmokeSphere(0.06f);
        AddChild(_primary);

        // Glow light for energy trail
        _glowLight = new OmniLight3D();
        _glowLight.LightColor = new Color(0.3f, 0.7f, 1f);
        _glowLight.LightEnergy = 2f;
        _glowLight.OmniRange = 1.5f;
        _glowLight.OmniAttenuation = 2f;
        _glowLight.ShadowEnabled = false;
        AddChild(_glowLight);
    }

    /// <summary>
    /// Rocket trail: the most dramatic trail effect.
    /// Primary = bright white-hot flame core (tight, intense) with fast alpha falloff.
    /// Secondary = orange-yellow fire particles (wider, billowing) with turbulence.
    /// Tertiary = warm white-grey smoke exhaust that lingers and expands behind.
    /// Plus a point light for dynamic illumination.
    /// </summary>
    private void InitializeRocket()
    {
        // --- Primary: white-hot flame core (tight cluster near exhaust) ---
        _primary = new GpuParticles3D();
        _primary.Amount = 16;
        _primary.Lifetime = 0.2;
        _primary.Emitting = true;
        _primary.Randomness = 0.15f;

        ParticleProcessMaterial coreMat = new ParticleProcessMaterial();
        coreMat.Direction = new Vector3(0f, 0f, -1f); // behind the missile
        coreMat.Spread = 10f;
        coreMat.InitialVelocityMin = 0.4f;
        coreMat.InitialVelocityMax = 1.0f;
        coreMat.Gravity = Vector3.Zero;
        coreMat.DampingMin = 2f;
        coreMat.DampingMax = 3f;
        coreMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        coreMat.EmissionSphereRadius = 0.02f;

        coreMat.ScaleMin = 0.06f;
        coreMat.ScaleMax = 0.12f;
        coreMat.ScaleCurve = GetRocketCoreScaleTexture();

        // White-hot to bright yellow
        Gradient coreGrad = new Gradient();
        coreGrad.SetColor(0, new Color(1f, 1f, 0.95f, 1f));
        coreGrad.AddPoint(0.15f, new Color(1f, 0.96f, 0.8f, 0.95f));
        coreGrad.AddPoint(0.5f, new Color(1f, 0.85f, 0.5f, 0.6f));
        coreGrad.SetColor(1, new Color(1f, 0.7f, 0.25f, 0f));
        GradientTexture1D coreTex = new GradientTexture1D();
        coreTex.Gradient = coreGrad;
        coreMat.ColorRamp = coreTex;

        coreMat.AlphaCurve = GetFireAlphaTexture();

        _primary.ProcessMaterial = coreMat;
        _primary.DrawPass1 = GetSmokeSphere(0.12f);
        AddChild(_primary);

        // --- Secondary: orange-yellow fire particles (wider, billowing) ---
        _secondary = new GpuParticles3D();
        _secondary.Amount = 24;
        _secondary.Lifetime = 0.5;
        _secondary.Emitting = true;
        _secondary.Randomness = 0.3f;

        ParticleProcessMaterial fireMat = new ParticleProcessMaterial();
        fireMat.Direction = new Vector3(0f, 0.3f, -0.7f);
        fireMat.Spread = 30f;
        fireMat.InitialVelocityMin = 0.25f;
        fireMat.InitialVelocityMax = 0.8f;
        fireMat.Gravity = new Vector3(0f, 0.7f, 0f); // fire rises
        fireMat.DampingMin = 1.5f;
        fireMat.DampingMax = 2.5f;
        fireMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        fireMat.EmissionSphereRadius = 0.04f;

        // Turbulent rotation for flickering fire
        fireMat.AngularVelocityMin = -60f;
        fireMat.AngularVelocityMax = 60f;

        fireMat.ScaleMin = 0.08f;
        fireMat.ScaleMax = 0.18f;
        fireMat.ScaleCurve = GetRocketFireScaleTexture();

        // Orange-yellow fire cooling to dark red
        Gradient fireGrad = new Gradient();
        fireGrad.SetColor(0, new Color(1f, 0.88f, 0.4f, 1f));
        fireGrad.AddPoint(0.15f, new Color(1f, 0.7f, 0.2f, 0.9f));
        fireGrad.AddPoint(0.4f, new Color(1f, 0.45f, 0.08f, 0.65f));
        fireGrad.AddPoint(0.7f, new Color(0.85f, 0.2f, 0.02f, 0.3f));
        fireGrad.SetColor(1, new Color(0.6f, 0.1f, 0f, 0f));
        GradientTexture1D fireTex = new GradientTexture1D();
        fireTex.Gradient = fireGrad;
        fireMat.ColorRamp = fireTex;

        fireMat.AlphaCurve = GetFireAlphaTexture();

        // Subtle hue variation for flickering quality
        fireMat.HueVariationMin = -0.05f;
        fireMat.HueVariationMax = 0.05f;

        _secondary.ProcessMaterial = fireMat;
        _secondary.DrawPass1 = GetSmokeSphere(0.2f);
        AddChild(_secondary);

        // --- Tertiary: warm white-grey smoke exhaust that lingers behind ---
        _tertiary = new GpuParticles3D();
        _tertiary.Amount = 18;
        _tertiary.Lifetime = 1.8;
        _tertiary.Emitting = true;
        _tertiary.Randomness = 0.35f;

        ParticleProcessMaterial smokeMat = new ParticleProcessMaterial();
        smokeMat.Direction = new Vector3(0f, 0.5f, -0.5f);
        smokeMat.Spread = 25f;
        smokeMat.InitialVelocityMin = 0.1f;
        smokeMat.InitialVelocityMax = 0.4f;
        smokeMat.Gravity = new Vector3(0f, 0.3f, 0f); // gentle upward drift
        smokeMat.DampingMin = 3f;
        smokeMat.DampingMax = 5f;
        smokeMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        smokeMat.EmissionSphereRadius = 0.05f;

        // Turbulent rotation for billowing smoke
        smokeMat.AngularVelocityMin = -70f;
        smokeMat.AngularVelocityMax = 70f;

        smokeMat.ScaleMin = 0.12f;
        smokeMat.ScaleMax = 0.3f;
        smokeMat.ScaleCurve = GetRocketSmokeScaleTexture();

        // Warm white smoke fading to grey then transparent
        Gradient smokeGrad = new Gradient();
        smokeGrad.SetColor(0, new Color(0.92f, 0.90f, 0.85f, 0.6f));
        smokeGrad.AddPoint(0.1f, new Color(0.88f, 0.85f, 0.80f, 0.55f));
        smokeGrad.AddPoint(0.35f, new Color(0.75f, 0.72f, 0.66f, 0.35f));
        smokeGrad.AddPoint(0.65f, new Color(0.60f, 0.58f, 0.54f, 0.15f));
        smokeGrad.SetColor(1, new Color(0.50f, 0.48f, 0.45f, 0f));
        GradientTexture1D smokeTex = new GradientTexture1D();
        smokeTex.Gradient = smokeGrad;
        smokeMat.ColorRamp = smokeTex;

        smokeMat.AlphaCurve = GetSmokeAlphaTexture();

        smokeMat.HueVariationMin = -0.02f;
        smokeMat.HueVariationMax = 0.02f;

        _tertiary.ProcessMaterial = smokeMat;
        _tertiary.DrawPass1 = GetSmokeSphere(0.3f);
        AddChild(_tertiary);

        // --- Point light for dynamic rocket glow ---
        _glowLight = new OmniLight3D();
        _glowLight.LightColor = new Color(1f, 0.7f, 0.3f);
        _glowLight.LightEnergy = 3f;
        _glowLight.OmniRange = 3f;
        _glowLight.OmniAttenuation = 1.5f;
        _glowLight.ShadowEnabled = false;
        AddChild(_glowLight);
    }

    /// <summary>
    /// Drill trail: small grey/silver metal sparks that spiral outward from the
    /// flight path, simulating metal shavings thrown off by a spinning drill bit.
    /// Uses orbital velocity to create the spiraling effect.
    /// Primary = metal sparks with orbital spin.
    /// Secondary = faint heat glow near the drill tip.
    /// </summary>
    private void InitializeDrill()
    {
        // --- Primary: spiraling metal sparks ---
        _primary = new GpuParticles3D();
        _primary.Amount = 22;
        _primary.Lifetime = 0.4;
        _primary.Emitting = true;
        _primary.Randomness = 0.25f;

        ParticleProcessMaterial sparkMat = new ParticleProcessMaterial();
        sparkMat.Direction = new Vector3(0f, 0f, -1f); // trail behind
        sparkMat.Spread = 40f;
        sparkMat.InitialVelocityMin = 1.0f;
        sparkMat.InitialVelocityMax = 2.5f;
        sparkMat.Gravity = new Vector3(0f, -4f, 0f); // sparks fall with gravity
        sparkMat.DampingMin = 1f;
        sparkMat.DampingMax = 2f;
        sparkMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        sparkMat.EmissionSphereRadius = 0.07f;

        // Orbital velocity for spiraling effect
        sparkMat.OrbitVelocityMin = 2.5f;
        sparkMat.OrbitVelocityMax = 6f;

        sparkMat.ScaleMin = 0.02f;
        sparkMat.ScaleMax = 0.05f;
        sparkMat.ScaleCurve = GetDrillSparkScaleTexture();

        // Grey/silver metal sparks with hot white flash at birth
        Gradient sparkGrad = new Gradient();
        sparkGrad.SetColor(0, new Color(0.95f, 0.92f, 0.88f, 1f));
        sparkGrad.AddPoint(0.08f, new Color(0.85f, 0.82f, 0.78f, 0.9f));
        sparkGrad.AddPoint(0.3f, new Color(0.7f, 0.68f, 0.65f, 0.7f));
        sparkGrad.AddPoint(0.6f, new Color(0.55f, 0.53f, 0.50f, 0.35f));
        sparkGrad.SetColor(1, new Color(0.45f, 0.43f, 0.40f, 0f));
        GradientTexture1D sparkTex = new GradientTexture1D();
        sparkTex.Gradient = sparkGrad;
        sparkMat.ColorRamp = sparkTex;

        _primary.ProcessMaterial = sparkMat;
        _primary.DrawPass1 = GetSparkBox();
        AddChild(_primary);

        // --- Secondary: faint orange heat glow at the drill tip ---
        _secondary = new GpuParticles3D();
        _secondary.Amount = 6;
        _secondary.Lifetime = 0.18;
        _secondary.Emitting = true;
        _secondary.Randomness = 0.2f;

        ParticleProcessMaterial heatMat = new ParticleProcessMaterial();
        heatMat.Direction = new Vector3(0f, 0f, 0f);
        heatMat.Spread = 180f;
        heatMat.InitialVelocityMin = 0.02f;
        heatMat.InitialVelocityMax = 0.08f;
        heatMat.Gravity = Vector3.Zero;
        heatMat.DampingMin = 1f;
        heatMat.DampingMax = 1f;
        heatMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        heatMat.EmissionSphereRadius = 0.02f;

        heatMat.ScaleMin = 0.05f;
        heatMat.ScaleMax = 0.1f;

        // Warm orange heat shimmer
        Gradient heatGrad = new Gradient();
        heatGrad.SetColor(0, new Color(1f, 0.65f, 0.25f, 0.5f));
        heatGrad.AddPoint(0.3f, new Color(1f, 0.45f, 0.1f, 0.35f));
        heatGrad.SetColor(1, new Color(0.8f, 0.25f, 0.05f, 0f));
        GradientTexture1D heatTex = new GradientTexture1D();
        heatTex.Gradient = heatGrad;
        heatMat.ColorRamp = heatTex;

        _secondary.ProcessMaterial = heatMat;
        _secondary.DrawPass1 = GetSmokeSphere(0.06f);
        // Position slightly ahead toward the drill tip
        _secondary.Position = new Vector3(0f, 0f, -0.15f);
        AddChild(_secondary);
    }
}
