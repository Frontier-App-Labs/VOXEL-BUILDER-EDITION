using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Hitscan penetrating railgun. Fires an instant beam along the aim direction
/// that pierces through up to 5 blocks. First block is always destroyed.
/// Deeper blocks take reduced damage. Can hit commanders through walls.
/// Fire FX: bright cyan flash + electric arcs + capacitor discharge glow + recoil.
/// </summary>
public partial class Railgun : WeaponBase
{
    /// <summary>
    /// Maximum number of solid blocks the rail slug can penetrate.
    /// </summary>
    private const int MaxPenetrationDepth = 5;

    /// <summary>
    /// Maximum raycast range in microvoxels.
    /// </summary>
    private const int MaxRangeMicrovoxels = 96;

    /// <summary>
    /// Duration the beam visual persists (seconds).
    /// </summary>
    private const float BeamDuration = 0.3f;

    public Railgun()
    {
        WeaponId = "railgun";
        Cost = 800;
        BaseDamage = 50;
        BlastRadiusMicrovoxels = 0f;
        ProjectileSpeed = 0f; // hitscan, not used
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        Vector3 start = GlobalPosition;
        Vector3 direction = aimingSystem.GetDirection();
        Vector3 endPoint = start;
        int penetrationCount = 0;
        bool wasInsideSolid = false;

        // Walk the ray step by step, penetrating up to MaxPenetrationDepth solid blocks
        for (int step = 1; step <= MaxRangeMicrovoxels; step++)
        {
            Vector3 point = start + (direction * step * GameConfig.MicrovoxelMeters);
            Vector3I micro = MathHelpers.WorldToMicrovoxel(point);
            VoxelValue voxel = world.GetVoxel(micro);

            if (!voxel.IsAir)
            {
                // Only Foundation is railgun-proof (indestructible bedrock)
                if (voxel.Material == VoxelMaterialType.Foundation)
                {
                    endPoint = start + (direction * (step - 1) * GameConfig.MicrovoxelMeters);
                    break;
                }

                if (!wasInsideSolid)
                {
                    // Entering a new solid block
                    penetrationCount++;
                    wasInsideSolid = true;
                }

                // First block hit is always destroyed (rail punches clean through).
                // Deeper blocks take reduced damage based on penetration depth.
                int damage;
                bool destroyed;
                if (penetrationCount == 1)
                {
                    damage = voxel.HitPoints; // guaranteed kill
                    destroyed = true;
                }
                else
                {
                    damage = DamageCalculator.CalculateRailgunDamage(BaseDamage, penetrationCount, voxel.Material);
                    destroyed = voxel.HitPoints - damage <= 0;
                }
                int nextHp = destroyed ? 0 : voxel.HitPoints - damage;
                world.SetVoxel(micro, destroyed ? VoxelValue.Air : voxel.WithHitPoints(nextHp).WithDamaged(true), OwnerSlot);

                // Spawn debris flying outward from destroyed voxels
                if (destroyed)
                {
                    Color debrisColor = VoxelMaterials.GetPreviewColor(voxel.Material);
                    DebrisFX.SpawnDebris(GetTree().Root, point, debrisColor, point - direction * 0.5f, 2, voxel.Material);
                }

                // Ignite flammable voxels the beam passes through
                if (!destroyed && VoxelMaterials.GetDefinition(voxel.Material).IsFlammable)
                {
                    FireSystem.Instance?.IgniteAt(micro);
                }

                endPoint = point;

                if (penetrationCount >= MaxPenetrationDepth)
                {
                    break;
                }
            }
            else
            {
                wasInsideSolid = false;
                endPoint = point;
            }
        }

        // Damage commanders along the beam path
        float beamLength = start.DistanceTo(endPoint);
        foreach (Node node in GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is not Commander.Commander commander)
            {
                continue;
            }

            // Skip own commander to prevent friendly fire
            if (commander.OwnerSlot == OwnerSlot)
            {
                continue;
            }

            // Point-to-line distance from commander to beam
            Vector3 toCommander = commander.GlobalPosition - start;
            float projection = toCommander.Dot(direction);
            if (projection < 0 || projection > beamLength)
            {
                continue;
            }

            Vector3 closestPoint = start + direction * projection;
            float distance = commander.GlobalPosition.DistanceTo(closestPoint) / GameConfig.MicrovoxelMeters;
            if (distance < 3.5f) // within 3.5 microvoxels of the beam
            {
                // Railgun commander damage: tuned for a 3-shot kill (Commander HP=15).
                // 2 direct hits = 12 damage (3 HP left, super low), 3rd hit finishes.
                // With exposed multiplier (1.5x): 6*1.5=9, so 2 exposed hits = 18 → 2-shot
                // kill if commander has zero cover, which is a fair punishment.
                int commanderDamage = 6;
                // Exposed commanders take extra damage — they have no cover
                if (commander.IsExposed)
                {
                    commanderDamage = Mathf.RoundToInt(commanderDamage * GameConfig.CommanderExposedMultiplier);
                }
                commander.ApplyDamage(commanderDamage, OwnerSlot, closestPoint);
            }
        }

        // Damage enemy weapons along the beam path (same point-to-line check).
        // Railgun beams that pass within 1 build unit of a weapon score a direct
        // hit, dealing half base damage.
        foreach (Node node in GetTree().GetNodesInGroup("Weapons"))
        {
            if (node is not WeaponBase weapon || weapon.IsDestroyed)
            {
                continue;
            }

            // Skip friendly weapons
            if (weapon.OwnerSlot == OwnerSlot)
            {
                continue;
            }

            // Point-to-line distance from weapon to beam
            Vector3 toWeapon = weapon.GlobalPosition - start;
            float projection = toWeapon.Dot(direction);
            if (projection < 0 || projection > beamLength)
            {
                continue;
            }

            Vector3 closestPoint = start + direction * projection;
            float distance = weapon.GlobalPosition.DistanceTo(closestPoint);
            if (distance < 1.0f) // within 1m of the beam (weapon footprint)
            {
                int weaponDamage = Mathf.Max(1, BaseDamage / 2);
                weapon.ApplyDamage(weaponDamage);
            }
        }

        // Spawn beam visual effect
        SpawnBeamEffect(start, endPoint, direction);

        LastFiredRound = currentRound;

        // Railgun-specific FX: cyan flash + electric arcs + capacitor discharge
        SpawnWeaponFireFX(direction);

        AudioDirector.Instance?.PlaySFX("railgun_fire", GlobalPosition);
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, direction));
        EventBus.Instance?.EmitRailgunBeamFired(new RailgunBeamFiredEvent(OwnerSlot, start, endPoint));
        return null; // hitscan -- no projectile
    }

    protected override void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        WeaponFX.SpawnRailgunFireFX(this, GlobalPosition, aimDirection);

        // Sharp recoil: railgun has a strong electromagnetic kick
        if (WeaponMesh != null)
            WeaponFX.AnimateRecoil(WeaponMesh, aimDirection, distance: 0.15f, duration: 0.3f);

        EnableIdleSmoke();
    }

    /// <summary>
    /// Creates a bright energy beam from start to end with three layers:
    /// 1. Inner core: bright white-cyan, thin cylinder
    /// 2. Outer glow: softer cyan, wider semi-transparent cylinder
    /// 3. Electric crackle: small cyan spark particles scattered along the beam line
    /// Plus impact glow light and muzzle flash light.
    /// All elements fade out over BeamDuration.
    /// </summary>
    private void SpawnBeamEffect(Vector3 start, Vector3 end, Vector3 direction)
    {
        Node sceneRoot = GetTree().Root;
        float beamLength = start.DistanceTo(end);
        if (beamLength < 0.01f)
        {
            return;
        }

        // --- Compute shared orientation basis ---
        Vector3 midpoint = (start + end) * 0.5f;
        Vector3 up = direction.Normalized();
        Vector3 right = up.Cross(Vector3.Up).Normalized();
        if (right.LengthSquared() < 0.001f)
        {
            right = up.Cross(Vector3.Right).Normalized();
        }
        Vector3 forward = right.Cross(up).Normalized();
        Basis beamBasis = new Basis(right, up, forward);

        // --- 1. Inner core: bright white-cyan, thin ---
        MeshInstance3D beamCore = new MeshInstance3D();
        CylinderMesh coreCylinder = new CylinderMesh();
        coreCylinder.TopRadius = 0.025f;
        coreCylinder.BottomRadius = 0.025f;
        coreCylinder.Height = beamLength;
        coreCylinder.RadialSegments = 6;
        beamCore.Mesh = coreCylinder;

        StandardMaterial3D coreMat = new StandardMaterial3D();
        coreMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        coreMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        coreMat.AlbedoColor = new Color(0.7f, 0.95f, 1f, 0.95f);
        coreMat.EmissionEnabled = true;
        coreMat.Emission = new Color(0.5f, 0.95f, 1f);
        coreMat.EmissionEnergyMultiplier = 5f;
        beamCore.MaterialOverride = coreMat;
        beamCore.GlobalTransform = new Transform3D(beamBasis, midpoint);
        sceneRoot.AddChild(beamCore);

        // --- 2. Outer glow: softer cyan, wider, semi-transparent ---
        MeshInstance3D beamGlow = new MeshInstance3D();
        CylinderMesh glowCylinder = new CylinderMesh();
        glowCylinder.TopRadius = 0.08f;
        glowCylinder.BottomRadius = 0.08f;
        glowCylinder.Height = beamLength;
        glowCylinder.RadialSegments = 8;
        beamGlow.Mesh = glowCylinder;

        StandardMaterial3D glowMat = new StandardMaterial3D();
        glowMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        glowMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        glowMat.AlbedoColor = new Color(0.2f, 0.7f, 1f, 0.3f);
        glowMat.EmissionEnabled = true;
        glowMat.Emission = new Color(0.2f, 0.6f, 1f);
        glowMat.EmissionEnergyMultiplier = 2f;
        beamGlow.MaterialOverride = glowMat;
        beamGlow.GlobalTransform = new Transform3D(beamBasis, midpoint);
        sceneRoot.AddChild(beamGlow);

        // --- 3. Electric crackle particles along the beam line ---
        GpuParticles3D crackle = new GpuParticles3D();
        crackle.Amount = 30;
        crackle.Lifetime = 0.25;
        crackle.Explosiveness = 0.85f;
        crackle.OneShot = true;
        crackle.Emitting = true;

        ParticleProcessMaterial crackleMat = new ParticleProcessMaterial();
        // Emit in a box along the beam direction
        crackleMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        crackleMat.EmissionBoxExtents = new Vector3(0.15f, 0.15f, 0.15f);
        crackleMat.Direction = new Vector3(0f, 0f, 0f);
        crackleMat.Spread = 180f; // random directions
        crackleMat.InitialVelocityMin = 0.5f;
        crackleMat.InitialVelocityMax = 2f;
        crackleMat.Gravity = Vector3.Zero; // energy, not affected by gravity
        crackleMat.DampingMin = 5f;
        crackleMat.DampingMax = 8f;

        crackleMat.ScaleMin = 0.01f;
        crackleMat.ScaleMax = 0.03f;

        // Bright cyan sparks fading to blue
        Gradient crackleGrad = new Gradient();
        crackleGrad.SetColor(0, new Color(0.5f, 1f, 1f, 1f));
        crackleGrad.SetColor(1, new Color(0.1f, 0.3f, 1f, 0f));
        crackleGrad.AddPoint(0.2f, new Color(0.4f, 0.9f, 1f, 0.9f));
        GradientTexture1D crackleTex = new GradientTexture1D();
        crackleTex.Gradient = crackleGrad;
        crackleMat.ColorRamp = crackleTex;

        crackle.ProcessMaterial = crackleMat;

        // Small billboard sphere for spark particles
        SphereMesh sparkMesh = new SphereMesh();
        sparkMesh.Radius = 0.02f;
        sparkMesh.Height = 0.04f;
        sparkMesh.RadialSegments = 4;
        sparkMesh.Rings = 2;
        StandardMaterial3D sparkMat = new StandardMaterial3D();
        sparkMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        sparkMat.AlbedoColor = Colors.White;
        sparkMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        sparkMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        sparkMesh.Material = sparkMat;
        crackle.DrawPass1 = sparkMesh;

        crackle.GlobalPosition = midpoint;
        sceneRoot.AddChild(crackle);

        // --- Muzzle flash light at the barrel ---
        OmniLight3D muzzleGlow = new OmniLight3D();
        muzzleGlow.LightColor = new Color(0.4f, 0.8f, 1f);
        muzzleGlow.LightEnergy = 5f;
        muzzleGlow.OmniRange = 2.5f;
        muzzleGlow.OmniAttenuation = 1.5f;
        muzzleGlow.ShadowEnabled = false;
        muzzleGlow.GlobalPosition = start;
        sceneRoot.AddChild(muzzleGlow);

        // --- Impact glow light at the endpoint ---
        OmniLight3D impactGlow = new OmniLight3D();
        impactGlow.LightColor = new Color(0.3f, 0.7f, 1f);
        impactGlow.LightEnergy = 6f;
        impactGlow.OmniRange = 2.5f;
        impactGlow.OmniAttenuation = 1.5f;
        impactGlow.ShadowEnabled = false;
        impactGlow.GlobalPosition = end;
        sceneRoot.AddChild(impactGlow);

        // --- Fade everything out over BeamDuration ---
        Tween tween = beamCore.CreateTween();

        // Core fades last (stays bright longest)
        tween.TweenProperty(coreMat, "albedo_color:a", 0f, BeamDuration);
        tween.Parallel().TweenProperty(coreMat, "emission_energy_multiplier", 0f, BeamDuration);

        // Outer glow fades faster
        tween.Parallel().TweenProperty(glowMat, "albedo_color:a", 0f, BeamDuration * 0.7f);
        tween.Parallel().TweenProperty(glowMat, "emission_energy_multiplier", 0f, BeamDuration * 0.7f);

        // Lights fade out
        tween.Parallel().TweenProperty(muzzleGlow, "light_energy", 0f, BeamDuration * 0.5f);
        tween.Parallel().TweenProperty(impactGlow, "light_energy", 0f, BeamDuration);

        // Clean up all nodes after the tween completes
        tween.TweenCallback(Callable.From(() =>
        {
            beamCore.QueueFree();
            beamGlow.QueueFree();
            crackle.QueueFree();
            muzzleGlow.QueueFree();
            impactGlow.QueueFree();
        }));
    }

    protected override string GetProjectileType()
    {
        return "rail_slug";
    }
}
