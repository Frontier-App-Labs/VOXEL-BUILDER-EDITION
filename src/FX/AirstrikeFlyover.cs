using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Art;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Spawns 1-3 voxel fighter jets that fly across the arena at high altitude,
/// dropping bombs as they pass over the target area. The planes fly in from
/// one edge, cross the map, and exit the other side before being cleaned up.
/// </summary>
public partial class AirstrikeFlyover : Node3D
{
    /// <summary>Flight altitude above the target in meters.</summary>
    private const float FlyoverAltitude = 20f;

    /// <summary>Plane speed in meters per second.</summary>
    private const float PlaneSpeed = 28f;

    /// <summary>Distance beyond the arena edge where planes spawn/despawn.</summary>
    private const float OvershootDistance = 30f;

    /// <summary>Spacing between planes in the formation (meters).</summary>
    private const float FormationSpacingX = 4f;

    /// <summary>Stagger depth between planes (meters along flight path).</summary>
    private const float FormationStaggerZ = 3f;

    private readonly struct PlaneInstance
    {
        public readonly MeshInstance3D Mesh;
        public readonly Vector3 StartPosition;
        public readonly Vector3 EndPosition;
        public readonly float BombDropT; // normalized 0-1 along the path where bombs drop

        public PlaneInstance(MeshInstance3D mesh, Vector3 start, Vector3 end, float bombDropT)
        {
            Mesh = mesh;
            StartPosition = start;
            EndPosition = end;
            BombDropT = bombDropT;
        }
    }

    private readonly List<PlaneInstance> _planes = new();
    private float _elapsedTime;
    private float _totalFlightTime;
    private bool _bombsDropped;
    private bool _finished;

    // Bomb drop callback
    private Action? _onBombDrop;

    /// <summary>
    /// Creates and starts an airstrike flyover. Planes fly over the target,
    /// and when they reach the target area, the onBombDrop callback fires
    /// to trigger the actual explosions.
    /// </summary>
    public static AirstrikeFlyover Spawn(
        Node parent,
        Vector3 targetWorld,
        Color teamColor,
        int planeCount,
        Action onBombDrop)
    {
        AirstrikeFlyover flyover = new AirstrikeFlyover();
        flyover.Name = "AirstrikeFlyover";
        parent.AddChild(flyover);
        flyover.GlobalPosition = Vector3.Zero;

        flyover._onBombDrop = onBombDrop;

        // Generate the plane mesh (shared by all planes in this flight)
        ArrayMesh planeMesh = PlaneModelGenerator.Generate(teamColor);
        StandardMaterial3D planeMaterial = PlaneModelGenerator.CreatePlaneMaterial();

        // Determine flight direction: pick a random cardinal approach
        RandomNumberGenerator rng = new();
        rng.Randomize();
        int approachDir = rng.RandiRange(0, 3);

        // Arena center is roughly at world origin (0, 0, 0)
        // Arena half-size in meters based on config
        float arenaHalf = GameConfig.PrototypeArenaWidth * GameConfig.MicrovoxelMeters * 0.5f;
        float spawnDist = arenaHalf + OvershootDistance;

        // Flight vector: planes fly straight across
        Vector3 flightDirection;
        Vector3 rightOffset; // perpendicular to flight for formation spread
        switch (approachDir)
        {
            case 0: // from -X to +X
                flightDirection = Vector3.Right;
                rightOffset = Vector3.Back;
                break;
            case 1: // from +X to -X
                flightDirection = Vector3.Left;
                rightOffset = Vector3.Forward;
                break;
            case 2: // from -Z to +Z (forward in Godot is -Z, so going toward +Z)
                flightDirection = Vector3.Back;
                rightOffset = Vector3.Right;
                break;
            default: // from +Z to -Z
                flightDirection = Vector3.Forward;
                rightOffset = Vector3.Left;
                break;
        }

        float altitude = targetWorld.Y + FlyoverAltitude;
        Vector3 flightStart = targetWorld - flightDirection * spawnDist;
        flightStart.Y = altitude;
        Vector3 flightEnd = targetWorld + flightDirection * spawnDist;
        flightEnd.Y = altitude;

        float totalDist = flightStart.DistanceTo(flightEnd);
        flyover._totalFlightTime = totalDist / PlaneSpeed;

        // The bomb drop happens when planes are over the target (normalized T)
        // Target is at the midpoint of the flight path
        float bombDropT = 0.5f;

        // Spawn planes in V-formation or staggered line
        planeCount = Mathf.Clamp(planeCount, 1, 3);

        for (int i = 0; i < planeCount; i++)
        {
            MeshInstance3D meshInst = new MeshInstance3D();
            meshInst.Name = $"FighterJet_{i}";
            meshInst.Mesh = planeMesh;
            meshInst.MaterialOverride = planeMaterial;
            meshInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            flyover.AddChild(meshInst);

            // Formation offset: center plane first, then flankers
            float lateralOffset = 0f;
            float depthOffset = 0f;
            if (planeCount == 2)
            {
                lateralOffset = (i == 0 ? -1f : 1f) * FormationSpacingX;
                depthOffset = FormationStaggerZ;
            }
            else if (planeCount == 3)
            {
                lateralOffset = (i - 1) * FormationSpacingX; // -1, 0, +1
                depthOffset = (i == 1) ? 0f : FormationStaggerZ; // lead plane ahead
            }

            Vector3 offset = rightOffset * lateralOffset - flightDirection * depthOffset;
            Vector3 planeStart = flightStart + offset;
            Vector3 planeEnd = flightEnd + offset;

            meshInst.GlobalPosition = planeStart;

            // Orient plane to face flight direction
            // The plane model faces -Z, so we use LookAt toward the end position
            meshInst.LookAt(planeEnd, Vector3.Up);

            flyover._planes.Add(new PlaneInstance(meshInst, planeStart, planeEnd, bombDropT));
        }

        return flyover;
    }

    public override void _Process(double delta)
    {
        if (_finished)
        {
            return;
        }

        _elapsedTime += (float)delta;
        float t = _elapsedTime / _totalFlightTime;

        if (t >= 1.0f)
        {
            // All planes have exited the map
            _finished = true;
            QueueFree();
            return;
        }

        // Move all planes along their flight paths
        for (int i = 0; i < _planes.Count; i++)
        {
            PlaneInstance plane = _planes[i];
            if (!GodotObject.IsInstanceValid(plane.Mesh))
            {
                continue;
            }

            Vector3 pos = plane.StartPosition.Lerp(plane.EndPosition, t);

            // Subtle banking oscillation for visual flair
            float bank = Mathf.Sin(_elapsedTime * 1.5f + i * 1.2f) * 0.03f;
            pos.Y += bank;

            plane.Mesh.GlobalPosition = pos;
        }

        // Drop bombs when the lead plane reaches the target
        if (!_bombsDropped && _planes.Count > 0 && t >= _planes[0].BombDropT)
        {
            _bombsDropped = true;
            _onBombDrop?.Invoke();
        }
    }
}
