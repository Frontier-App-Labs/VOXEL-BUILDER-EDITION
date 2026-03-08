using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.FX;

public partial class DebrisFX : Node3D
{
    private readonly Queue<MeshInstance3D> _pool = new Queue<MeshInstance3D>();
    private readonly List<(MeshInstance3D Debris, double Expiry)> _active = new List<(MeshInstance3D Debris, double Expiry)>();

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        for (int index = _active.Count - 1; index >= 0; index--)
        {
            if (_active[index].Expiry <= now)
            {
                MeshInstance3D debris = _active[index].Debris;
                debris.Visible = false;
                _pool.Enqueue(debris);
                _active.RemoveAt(index);
            }
        }
    }

    public void SpawnBurst(Vector3 origin, int pieces)
    {
        int clamped = Mathf.Clamp(pieces, 1, GameConfig.MaxDebrisObjects);
        double expiry = (Time.GetTicksMsec() / 1000.0) + GameConfig.DebrisDespawnTime;
        for (int index = 0; index < clamped; index++)
        {
            MeshInstance3D debris = _pool.Count > 0 ? _pool.Dequeue() : CreateDebris();
            debris.GlobalPosition = origin + new Vector3((float)GD.RandRange(-0.5, 0.5), (float)GD.RandRange(0, 0.75), (float)GD.RandRange(-0.5, 0.5));
            debris.Visible = true;
            _active.Add((debris, expiry));
        }
    }

    private MeshInstance3D CreateDebris()
    {
        MeshInstance3D debris = new MeshInstance3D();
        debris.Mesh = new BoxMesh { Size = Vector3.One * 0.15f };
        AddChild(debris);
        return debris;
    }
}
