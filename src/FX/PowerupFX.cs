using Godot;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.FX;

/// <summary>
/// Visual effects for powerup activations. Each powerup gets a distinct,
/// chunky, toy-warfare style particle/mesh effect.
/// </summary>
public static class PowerupFX
{
    // Cached resources
    private static SphereMesh? _cachedSphere;
    private static BoxMesh? _cachedBox;
    private static SphereMesh CachedSphere => _cachedSphere ??= new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };
    private static BoxMesh CachedBox => _cachedBox ??= new BoxMesh { Size = new Vector3(0.3f, 0.3f, 0.3f) };

    // ─────────────────────────────────────────────────
    //  SMOKE SCREEN
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns a semi-transparent grey smoke cloud covering the given build zone.
    /// The smoke persists as GPU particles for the duration (cleaned up by timer).
    /// </summary>
    public static void SpawnSmokeScreen(Node parent, Vector3 center, BuildZone zone)
    {
        Node3D root = new Node3D();
        root.Name = "SmokeScreenFX";
        parent.AddChild(root);
        // Lower the smoke center to sit closer to the ground — shift down by 40% of zone height
        Vector3 zoneSize = MathHelpers.MicrovoxelToWorld(zone.SizeMicrovoxels);
        float groundOffset = zoneSize.Y * 0.35f;
        root.GlobalPosition = center - new Vector3(0, groundOffset, 0);

        // Calculate zone world extents
        float halfX = zoneSize.X * 0.55f;  // slightly wider than zone for coverage
        float halfZ = zoneSize.Z * 0.55f;
        float height = zoneSize.Y * 0.45f; // lower, denser cloud

        // Dense smoke cloud particles — thick, slow-moving cloud that obscures the base
        GpuParticles3D smoke = new GpuParticles3D();
        smoke.Name = "SmokeCloud";
        smoke.Amount = 250;
        smoke.Lifetime = 10.0f;
        smoke.Explosiveness = 0.0f;
        smoke.OneShot = false;
        smoke.FixedFps = 30;
        smoke.DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth;

        ParticleProcessMaterial smokeMat = new ParticleProcessMaterial();
        smokeMat.Direction = new Vector3(0, 0.05f, 0);
        smokeMat.Spread = 180f;
        smokeMat.InitialVelocityMin = 0.005f;
        smokeMat.InitialVelocityMax = 0.03f;
        smokeMat.Gravity = new Vector3(0, -0.01f, 0); // slight downward pull — hugs the ground
        smokeMat.ScaleMin = 7.0f;
        smokeMat.ScaleMax = 16.0f;
        smokeMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        smokeMat.EmissionBoxExtents = new Vector3(halfX, height * 0.5f, halfZ);
        smokeMat.DampingMin = 10f;
        smokeMat.DampingMax = 15f;

        // Color ramp: thick, opaque grey smoke that lingers and fades slowly at edges
        Gradient colorGrad = new Gradient();
        colorGrad.SetColor(0, new Color(0.6f, 0.6f, 0.6f, 0.0f));
        colorGrad.SetColor(1, new Color(0.5f, 0.5f, 0.5f, 0.0f));
        colorGrad.SetOffset(0, 0f);
        colorGrad.SetOffset(1, 1f);
        colorGrad.AddPoint(0.05f, new Color(0.6f, 0.6f, 0.6f, 0.75f));
        colorGrad.AddPoint(0.2f, new Color(0.58f, 0.58f, 0.58f, 0.85f));
        colorGrad.AddPoint(0.5f, new Color(0.55f, 0.55f, 0.55f, 0.85f));
        colorGrad.AddPoint(0.8f, new Color(0.52f, 0.52f, 0.52f, 0.7f));
        colorGrad.AddPoint(0.95f, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        GradientTexture1D colorTex = new GradientTexture1D();
        colorTex.Gradient = colorGrad;
        smokeMat.ColorRamp = colorTex;

        smoke.ProcessMaterial = smokeMat;

        SphereMesh smokeSphere = new SphereMesh();
        smokeSphere.Radius = 2.5f;
        smokeSphere.Height = 5f;
        smokeSphere.RadialSegments = 16;
        smokeSphere.Rings = 8;

        StandardMaterial3D smokeVisual = new StandardMaterial3D();
        smokeVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        smokeVisual.AlbedoColor = new Color(0.65f, 0.65f, 0.65f, 0.8f);
        smokeVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        smokeVisual.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        smokeSphere.Material = smokeVisual;
        smoke.DrawPass1 = smokeSphere;

        root.AddChild(smoke);
        smoke.Emitting = true; // Start after AddChild

        // Fallback cleanup timer (safety net in case the effect isn't cleaned up on expiry)
        SceneTreeTimer timer = parent.GetTree().CreateTimer(180f);
        timer.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }

    // ─────────────────────────────────────────────────
    //  REPAIR KIT
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns a green healing particle burst at the repair center.
    /// </summary>
    public static void SpawnRepairEffect(Node parent, Vector3 center, int voxelsRepaired)
    {
        Node3D root = new Node3D();
        root.Name = "RepairFX";
        parent.AddChild(root);
        root.GlobalPosition = center;

        // Green healing particles rising up
        GpuParticles3D heal = new GpuParticles3D();
        heal.Name = "HealParticles";
        heal.Amount = Mathf.Min(voxelsRepaired * 3, 60);
        heal.Lifetime = 1.5f;
        heal.Explosiveness = 0.6f;
        heal.OneShot = true;

        ParticleProcessMaterial healMat = new ParticleProcessMaterial();
        healMat.Direction = new Vector3(0, 1, 0);
        healMat.Spread = 45f;
        healMat.InitialVelocityMin = 1f;
        healMat.InitialVelocityMax = 3f;
        healMat.Gravity = new Vector3(0, -0.5f, 0);
        healMat.ScaleMin = 0.1f;
        healMat.ScaleMax = 0.3f;
        healMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        healMat.EmissionSphereRadius = 2f;
        healMat.Color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        heal.ProcessMaterial = healMat;
        heal.DrawPass1 = CachedBox;

        StandardMaterial3D healVisual = new StandardMaterial3D();
        healVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        healVisual.AlbedoColor = new Color(0.2f, 1f, 0.3f, 0.8f);
        healVisual.EmissionEnabled = true;
        healVisual.Emission = new Color(0.2f, 1f, 0.3f);
        healVisual.EmissionEnergyMultiplier = 2f;
        healVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;

        BoxMesh healBox = new BoxMesh();
        healBox.Size = new Vector3(0.15f, 0.15f, 0.15f);
        healBox.Material = healVisual;
        heal.DrawPass1 = healBox;

        root.AddChild(heal);

        // Green flash light
        OmniLight3D flash = new OmniLight3D();
        flash.LightColor = new Color(0.2f, 1f, 0.3f);
        flash.LightEnergy = 3f;
        flash.OmniRange = 5f;
        root.AddChild(flash);

        // Cleanup
        parent.GetTree().CreateTimer(2.5f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }

    // ─────────────────────────────────────────────────
    //  MEDKIT
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns a green healing cross burst at the commander's position.
    /// </summary>
    public static void SpawnMedkitEffect(Node parent, Vector3 center)
    {
        Node3D root = new Node3D();
        root.Name = "MedkitFX";
        parent.AddChild(root);
        root.GlobalPosition = center;

        // Green healing particles rising up (cross-shaped burst)
        GpuParticles3D heal = new GpuParticles3D();
        heal.Name = "HealParticles";
        heal.Amount = 40;
        heal.Lifetime = 1.5f;
        heal.Explosiveness = 0.7f;
        heal.OneShot = true;

        ParticleProcessMaterial healMat = new ParticleProcessMaterial();
        healMat.Direction = new Vector3(0, 1, 0);
        healMat.Spread = 30f;
        healMat.InitialVelocityMin = 1.5f;
        healMat.InitialVelocityMax = 4f;
        healMat.Gravity = new Vector3(0, -0.5f, 0);
        healMat.ScaleMin = 0.1f;
        healMat.ScaleMax = 0.3f;
        healMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        healMat.EmissionSphereRadius = 1f;
        healMat.Color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        heal.ProcessMaterial = healMat;

        BoxMesh healBox = new BoxMesh();
        healBox.Size = new Vector3(0.15f, 0.15f, 0.15f);
        StandardMaterial3D healVisual = new StandardMaterial3D();
        healVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        healVisual.AlbedoColor = new Color(0.2f, 1f, 0.3f, 0.8f);
        healVisual.EmissionEnabled = true;
        healVisual.Emission = new Color(0.2f, 1f, 0.3f);
        healVisual.EmissionEnergyMultiplier = 2.5f;
        healVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        healBox.Material = healVisual;
        heal.DrawPass1 = healBox;
        root.AddChild(heal);

        // Green flash light
        OmniLight3D flash = new OmniLight3D();
        flash.LightColor = new Color(0.2f, 1f, 0.3f);
        flash.LightEnergy = 4f;
        flash.OmniRange = 6f;
        root.AddChild(flash);

        // Cleanup
        parent.GetTree().CreateTimer(2.5f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }

    // ─────────────────────────────────────────────────
    //  SHIELD GENERATOR
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns a blue shimmer effect on everything in the shielded zone.
    /// Rising sparkle particles + blue tint light instead of an opaque bubble.
    /// Visible to all players so enemies can see the shield is active.
    /// </summary>
    public static void SpawnShieldBubble(Node parent, Vector3 center, float radiusMeters)
    {
        Node3D root = new Node3D();
        root.Name = "ShieldBubbleFX";
        parent.AddChild(root);
        root.GlobalPosition = center;

        // Shimmer particles — small rising blue sparkles throughout the zone
        GpuParticles3D shimmer = new GpuParticles3D();
        shimmer.Name = "ShieldShimmer";
        shimmer.Amount = 80;
        shimmer.Lifetime = 2.5f;
        shimmer.Explosiveness = 0f;
        shimmer.OneShot = false;
        shimmer.FixedFps = 30;

        ParticleProcessMaterial shimmerMat = new ParticleProcessMaterial();
        shimmerMat.Direction = new Vector3(0, 1f, 0);
        shimmerMat.Spread = 30f;
        shimmerMat.InitialVelocityMin = 0.3f;
        shimmerMat.InitialVelocityMax = 0.8f;
        shimmerMat.Gravity = new Vector3(0, -0.05f, 0);
        shimmerMat.ScaleMin = 0.02f;
        shimmerMat.ScaleMax = 0.08f;
        shimmerMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        shimmerMat.EmissionBoxExtents = new Vector3(radiusMeters * 0.9f, radiusMeters * 0.5f, radiusMeters * 0.9f);

        // Sparkle color ramp: fade in blue, pulse bright, fade out
        Gradient shimmerGrad = new Gradient();
        shimmerGrad.SetColor(0, new Color(0.3f, 0.6f, 1f, 0f));
        shimmerGrad.SetColor(1, new Color(0.2f, 0.5f, 1f, 0f));
        shimmerGrad.AddPoint(0.15f, new Color(0.4f, 0.7f, 1f, 0.9f));
        shimmerGrad.AddPoint(0.5f, new Color(0.5f, 0.8f, 1f, 1f));
        shimmerGrad.AddPoint(0.85f, new Color(0.3f, 0.6f, 1f, 0.6f));
        GradientTexture1D shimmerTex = new GradientTexture1D();
        shimmerTex.Gradient = shimmerGrad;
        shimmerMat.ColorRamp = shimmerTex;

        shimmer.ProcessMaterial = shimmerMat;

        // Small diamond-shaped sparkle meshes
        BoxMesh sparkleMesh = new BoxMesh();
        sparkleMesh.Size = new Vector3(0.06f, 0.06f, 0.06f);
        StandardMaterial3D sparkleVisual = new StandardMaterial3D();
        sparkleVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        sparkleVisual.AlbedoColor = new Color(0.5f, 0.8f, 1f, 0.9f);
        sparkleVisual.EmissionEnabled = true;
        sparkleVisual.Emission = new Color(0.4f, 0.7f, 1f);
        sparkleVisual.EmissionEnergyMultiplier = 4f;
        sparkleVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        sparkleVisual.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        sparkleMesh.Material = sparkleVisual;
        shimmer.DrawPass1 = sparkleMesh;
        root.AddChild(shimmer);

        // Surface shimmer — larger, slower particles that cling to surfaces
        GpuParticles3D surfaceGlow = new GpuParticles3D();
        surfaceGlow.Name = "SurfaceShimmer";
        surfaceGlow.Amount = 40;
        surfaceGlow.Lifetime = 3f;
        surfaceGlow.Explosiveness = 0f;
        surfaceGlow.OneShot = false;
        surfaceGlow.FixedFps = 30;

        ParticleProcessMaterial surfaceMat = new ParticleProcessMaterial();
        surfaceMat.Direction = new Vector3(0, 0.2f, 0);
        surfaceMat.Spread = 180f;
        surfaceMat.InitialVelocityMin = 0.01f;
        surfaceMat.InitialVelocityMax = 0.05f;
        surfaceMat.Gravity = Vector3.Zero;
        surfaceMat.ScaleMin = 0.15f;
        surfaceMat.ScaleMax = 0.4f;
        surfaceMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        surfaceMat.EmissionBoxExtents = new Vector3(radiusMeters * 0.85f, radiusMeters * 0.4f, radiusMeters * 0.85f);
        surfaceMat.DampingMin = 5f;
        surfaceMat.DampingMax = 10f;
        surfaceMat.Color = new Color(0.3f, 0.6f, 1f, 0.35f);
        surfaceGlow.ProcessMaterial = surfaceMat;

        SphereMesh glowSphere = new SphereMesh();
        glowSphere.Radius = 0.3f;
        glowSphere.Height = 0.6f;
        glowSphere.RadialSegments = 8;
        glowSphere.Rings = 4;
        StandardMaterial3D surfaceVisual = new StandardMaterial3D();
        surfaceVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        surfaceVisual.AlbedoColor = new Color(0.3f, 0.6f, 1f, 0.25f);
        surfaceVisual.EmissionEnabled = true;
        surfaceVisual.Emission = new Color(0.3f, 0.6f, 1f);
        surfaceVisual.EmissionEnergyMultiplier = 2f;
        surfaceVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        surfaceVisual.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        glowSphere.Material = surfaceVisual;
        surfaceGlow.DrawPass1 = glowSphere;
        root.AddChild(surfaceGlow);

        // Blue ambient light to tint the whole zone
        OmniLight3D light = new OmniLight3D();
        light.LightColor = new Color(0.3f, 0.6f, 1f);
        light.LightEnergy = 1.5f;
        light.OmniRange = radiusMeters * 1.2f;
        root.AddChild(light);

        // Cleanup after 130 seconds (shield lasts 2 turns max)
        parent.GetTree().CreateTimer(130f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }

    // ─────────────────────────────────────────────────
    //  AIRSTRIKE BEACON
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns red targeting circles on the ground before airstrike shells impact.
    /// </summary>
    public static void SpawnAirstrikeTargeting(Node parent, Vector3 targetCenter)
    {
        Node3D root = new Node3D();
        root.Name = "AirstrikeTargetingFX";
        parent.AddChild(root);
        root.GlobalPosition = targetCenter;

        // Red targeting circle on the ground
        MeshInstance3D targetRing = new MeshInstance3D();
        CylinderMesh ringMesh = new CylinderMesh();
        ringMesh.TopRadius = 4f; // 8 build units = ~4m radius
        ringMesh.BottomRadius = 4f;
        ringMesh.Height = 0.04f;
        ringMesh.RadialSegments = 24;
        targetRing.Mesh = ringMesh;

        StandardMaterial3D ringMat = new StandardMaterial3D();
        ringMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        ringMat.AlbedoColor = new Color(1f, 0.2f, 0.1f, 0.5f);
        ringMat.EmissionEnabled = true;
        ringMat.Emission = new Color(1f, 0.2f, 0.1f);
        ringMat.EmissionEnergyMultiplier = 3f;
        ringMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        ringMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        targetRing.MaterialOverride = ringMat;
        root.AddChild(targetRing);

        // Inner crosshair
        MeshInstance3D crossX = new MeshInstance3D();
        BoxMesh crossMeshX = new BoxMesh();
        crossMeshX.Size = new Vector3(8f, 0.05f, 0.15f);
        crossX.Mesh = crossMeshX;
        crossX.MaterialOverride = ringMat;
        root.AddChild(crossX);

        MeshInstance3D crossZ = new MeshInstance3D();
        BoxMesh crossMeshZ = new BoxMesh();
        crossMeshZ.Size = new Vector3(0.15f, 0.05f, 8f);
        crossZ.Mesh = crossMeshZ;
        crossZ.MaterialOverride = ringMat;
        root.AddChild(crossZ);

        // Red flash light
        OmniLight3D flash = new OmniLight3D();
        flash.LightColor = new Color(1f, 0.2f, 0.1f);
        flash.LightEnergy = 3f;
        flash.OmniRange = 6f;
        flash.Position = new Vector3(0, 2f, 0);
        root.AddChild(flash);

        // Cleanup after the shells have impacted (~3 seconds)
        parent.GetTree().CreateTimer(3f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }

    // ─────────────────────────────────────────────────
    //  EMP BLAST
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns a blue electricity arc/spark effect at the disabled weapon.
    /// </summary>
    public static void SpawnEmpEffect(Node parent, Vector3 weaponPosition)
    {
        Node3D root = new Node3D();
        root.Name = "EmpFX";
        parent.AddChild(root);
        root.GlobalPosition = weaponPosition;

        // Blue electric spark particles
        GpuParticles3D sparks = new GpuParticles3D();
        sparks.Name = "EmpSparks";
        sparks.Amount = 30;
        sparks.Lifetime = 0.8f;
        sparks.Explosiveness = 0.8f;
        sparks.OneShot = false;
        sparks.FixedFps = 30;

        ParticleProcessMaterial sparkMat = new ParticleProcessMaterial();
        sparkMat.Direction = new Vector3(0, 1, 0);
        sparkMat.Spread = 180f;
        sparkMat.InitialVelocityMin = 1f;
        sparkMat.InitialVelocityMax = 3f;
        sparkMat.Gravity = new Vector3(0, 2f, 0);
        sparkMat.ScaleMin = 0.03f;
        sparkMat.ScaleMax = 0.08f;
        sparkMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        sparkMat.EmissionSphereRadius = 0.5f;
        sparkMat.Color = new Color(0.3f, 0.6f, 1f, 0.9f);
        sparks.ProcessMaterial = sparkMat;

        BoxMesh sparkBox = new BoxMesh();
        sparkBox.Size = new Vector3(0.04f, 0.12f, 0.04f);
        StandardMaterial3D sparkVisual = new StandardMaterial3D();
        sparkVisual.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        sparkVisual.AlbedoColor = new Color(0.4f, 0.7f, 1f, 0.9f);
        sparkVisual.EmissionEnabled = true;
        sparkVisual.Emission = new Color(0.4f, 0.7f, 1f);
        sparkVisual.EmissionEnergyMultiplier = 4f;
        sparkVisual.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        sparkBox.Material = sparkVisual;
        sparks.DrawPass1 = sparkBox;
        root.AddChild(sparks);

        // Blue flash
        OmniLight3D empLight = new OmniLight3D();
        empLight.LightColor = new Color(0.3f, 0.6f, 1f);
        empLight.LightEnergy = 5f;
        empLight.OmniRange = 4f;
        root.AddChild(empLight);

        // Cleanup after effect expires (~130 seconds for 2 turns)
        parent.GetTree().CreateTimer(130f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFree();
            }
        };
    }
}
