using Godot;
using VoxelSiege.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Soft, puffy dust clouds that appear on impacts and destruction.
/// Now material-aware: tints dust to match the destroyed block type.
/// Grey for stone/concrete, brown for dirt, golden for sand, etc.
/// Particle resources are cached as static fields.
/// </summary>
public partial class DustFX : Node3D
{
    private GpuParticles3D _dust = null!;

    // Cached particle resources
    private static CurveTexture? _cachedScaleTex;
    private static SphereMesh? _cachedSphere;

    /// <summary>
    /// Spawns a dust cloud effect at the given world position with generic brown/grey tint.
    /// </summary>
    public static DustFX Spawn(Node parent, Vector3 position, float radius)
    {
        return Spawn(parent, position, radius, VoxelMaterialType.Stone);
    }

    /// <summary>
    /// Spawns a material-tinted dust cloud effect at the given world position.
    /// The dust color matches the destroyed material type for visual coherence.
    /// </summary>
    public static DustFX Spawn(Node parent, Vector3 position, float radius, VoxelMaterialType material)
    {
        DustFX fx = new DustFX();
        parent.AddChild(fx);
        fx.GlobalPosition = position;
        fx.Initialize(radius, material);
        return fx;
    }

    private static CurveTexture GetScaleTexture()
    {
        if (_cachedScaleTex == null)
        {
            Curve scaleCurve = new Curve();
            scaleCurve.AddPoint(new Vector2(0f, 0.2f));
            scaleCurve.AddPoint(new Vector2(0.2f, 0.8f));
            scaleCurve.AddPoint(new Vector2(0.5f, 1f));
            scaleCurve.AddPoint(new Vector2(1f, 1.2f));
            _cachedScaleTex = new CurveTexture();
            _cachedScaleTex.Curve = scaleCurve;
        }
        return _cachedScaleTex;
    }

    private static SphereMesh GetSphere()
    {
        if (_cachedSphere == null)
        {
            _cachedSphere = new SphereMesh();
            _cachedSphere.Radius = 0.4f;
            _cachedSphere.Height = 0.8f;
            _cachedSphere.RadialSegments = 16;
            _cachedSphere.Rings = 8;
            StandardMaterial3D sphereMat = new StandardMaterial3D();
            sphereMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            sphereMat.AlbedoColor = Colors.White;
            sphereMat.VertexColorUseAsAlbedo = true; // Required for ColorRamp to tint particles
            sphereMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            sphereMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            sphereMat.NoDepthTest = true;
            sphereMat.ProximityFadeEnabled = true;
            sphereMat.ProximityFadeDistance = 0.2f;
            _cachedSphere.Material = sphereMat;
        }
        return _cachedSphere;
    }

    /// <summary>
    /// Returns dust tint colors (start, mid, end) for a given material type.
    /// Colors are desaturated and lightened versions of the material's base color
    /// to look like realistic dust/powder from that material.
    /// </summary>
    private static (Color start, Color mid, Color end) GetDustColors(VoxelMaterialType material)
    {
        return material switch
        {
            // Dirt/grass: earthy brown dust
            VoxelMaterialType.Dirt => (
                new Color(0.50f, 0.40f, 0.28f, 0.55f),
                new Color(0.48f, 0.38f, 0.26f, 0.40f),
                new Color(0.45f, 0.36f, 0.25f, 0f)),

            // Wood: warm tan sawdust
            VoxelMaterialType.Wood or VoxelMaterialType.Bark => (
                new Color(0.58f, 0.45f, 0.30f, 0.50f),
                new Color(0.55f, 0.42f, 0.28f, 0.35f),
                new Color(0.50f, 0.40f, 0.28f, 0f)),

            // Stone: cool grey dust
            VoxelMaterialType.Stone => (
                new Color(0.55f, 0.53f, 0.50f, 0.55f),
                new Color(0.52f, 0.50f, 0.48f, 0.40f),
                new Color(0.48f, 0.46f, 0.44f, 0f)),

            // Brick: reddish-brown dust
            VoxelMaterialType.Brick => (
                new Color(0.58f, 0.38f, 0.30f, 0.55f),
                new Color(0.55f, 0.36f, 0.28f, 0.40f),
                new Color(0.50f, 0.34f, 0.26f, 0f)),

            // Concrete: light grey dust
            VoxelMaterialType.Concrete => (
                new Color(0.62f, 0.60f, 0.58f, 0.60f),
                new Color(0.58f, 0.56f, 0.55f, 0.45f),
                new Color(0.55f, 0.53f, 0.52f, 0f)),

            // Metal: dark blue-grey sparky dust
            VoxelMaterialType.Metal or VoxelMaterialType.ReinforcedSteel or VoxelMaterialType.ArmorPlate => (
                new Color(0.45f, 0.48f, 0.52f, 0.45f),
                new Color(0.42f, 0.44f, 0.48f, 0.30f),
                new Color(0.38f, 0.40f, 0.44f, 0f)),

            // Glass: white/pale blue glittery mist
            VoxelMaterialType.Glass or VoxelMaterialType.Ice => (
                new Color(0.70f, 0.78f, 0.85f, 0.40f),
                new Color(0.65f, 0.72f, 0.80f, 0.25f),
                new Color(0.60f, 0.68f, 0.75f, 0f)),

            // Sand: golden dust
            VoxelMaterialType.Sand => (
                new Color(0.72f, 0.62f, 0.40f, 0.55f),
                new Color(0.68f, 0.58f, 0.38f, 0.40f),
                new Color(0.65f, 0.55f, 0.35f, 0f)),

            // Obsidian: dark purple-black dust
            VoxelMaterialType.Obsidian => (
                new Color(0.28f, 0.22f, 0.32f, 0.50f),
                new Color(0.25f, 0.20f, 0.28f, 0.35f),
                new Color(0.22f, 0.18f, 0.25f, 0f)),

            // Leaves: green puff
            VoxelMaterialType.Leaves => (
                new Color(0.35f, 0.50f, 0.28f, 0.45f),
                new Color(0.32f, 0.45f, 0.25f, 0.30f),
                new Color(0.30f, 0.42f, 0.22f, 0f)),

            // Default: warm brown/grey (original)
            _ => (
                new Color(0.55f, 0.45f, 0.35f, 0.50f),
                new Color(0.52f, 0.44f, 0.36f, 0.40f),
                new Color(0.50f, 0.42f, 0.35f, 0f)),
        };
    }

    private void Initialize(float radius, VoxelMaterialType material)
    {
        float scale = Mathf.Clamp(radius, 0.3f, 6f);

        _dust = new GpuParticles3D();
        _dust.Amount = 12;
        _dust.Lifetime = 2.5;
        _dust.Explosiveness = 0.8f;
        _dust.OneShot = true;
        _dust.Emitting = true;

        ParticleProcessMaterial mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0f, 0.4f, 0f);
        mat.Spread = 180f;
        mat.InitialVelocityMin = 0.3f * scale;
        mat.InitialVelocityMax = 1.2f * scale;
        mat.Gravity = new Vector3(0f, 0.15f, 0f); // Slight upward float
        mat.DampingMin = 4f;
        mat.DampingMax = 4f;
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 0.3f * scale;

        // Large puffy scale
        mat.ScaleMin = 0.4f * scale;
        mat.ScaleMax = 0.8f * scale;
        mat.ScaleCurve = GetScaleTexture();

        // Material-tinted dust colors
        (Color start, Color mid, Color end) = GetDustColors(material);
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, start);
        colorGrad.SetColor(1, end);
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.4f, mid);
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        mat.ColorRamp = colorTex;

        _dust.ProcessMaterial = mat;
        _dust.DrawPass1 = GetSphere();

        AddChild(_dust);

        // Use a scene-tree timer instead of per-frame _Process polling
        GetTree().CreateTimer(3.5).Timeout += () => QueueFree();
    }
}
