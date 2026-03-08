using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public partial class ProjectileBase : Node3D
{
    private MeshInstance3D? _visual;
    private VoxelWorld? _world;
    private PlayerSlot _owner;
    private Vector3 _velocity;
    private int _baseDamage;
    private float _blastRadiusMicrovoxels;
    private bool _hasImpacted;

    [Export]
    public float GravityMultiplier { get; set; } = 1f;

    [Export]
    public float LifetimeSeconds { get; set; } = 10f;

    public void Initialize(VoxelWorld world, PlayerSlot owner, Vector3 velocity, int baseDamage, float blastRadiusMicrovoxels)
    {
        _world = world;
        _owner = owner;
        _velocity = velocity;
        _baseDamage = baseDamage;
        _blastRadiusMicrovoxels = blastRadiusMicrovoxels;
        EnsureVisual();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_hasImpacted || _world == null)
        {
            return;
        }

        LifetimeSeconds -= (float)delta;
        if (LifetimeSeconds <= 0f)
        {
            Impact(GlobalPosition);
            return;
        }

        float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity") * GravityMultiplier;
        Vector3 previousPosition = GlobalPosition;
        _velocity.Y -= gravity * (float)delta;
        GlobalPosition += _velocity * (float)delta;
        if (CheckCollisionAlongPath(previousPosition, GlobalPosition, out Vector3 impactPoint))
        {
            Impact(impactPoint);
        }
    }

    private bool CheckCollisionAlongPath(Vector3 start, Vector3 end, out Vector3 impactPoint)
    {
        Vector3 delta = end - start;
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.Length() / GameConfig.MicrovoxelMeters));
        for (int index = 1; index <= steps; index++)
        {
            float t = index / (float)steps;
            Vector3 samplePoint = start + (delta * t);
            if (_world!.GetVoxel(MathHelpers.WorldToMicrovoxel(samplePoint)).IsSolid)
            {
                impactPoint = samplePoint;
                return true;
            }
        }

        impactPoint = end;
        return false;
    }

    private void Impact(Vector3 point)
    {
        if (_hasImpacted || _world == null)
        {
            return;
        }

        _hasImpacted = true;
        Explosion.Trigger(GetParent() ?? _world, _world, point, _baseDamage, _blastRadiusMicrovoxels, _owner);
        QueueFree();
    }

    private void EnsureVisual()
    {
        _visual ??= GetNodeOrNull<MeshInstance3D>("Visual");
        if (_visual != null)
        {
            return;
        }

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f };
        StandardMaterial3D material = new StandardMaterial3D();
        material.AlbedoColor = new Color("f2d07d");
        material.EmissionEnabled = true;
        material.Emission = new Color("ff9f40");
        material.Roughness = 0.3f;
        _visual.MaterialOverride = material;
        AddChild(_visual);
    }
}
