using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.Camera;

/// <summary>
/// Orbit camera for combat overview. Rotates around a configurable pivot with mouse drag,
/// scroll zoom, auto-rotate, and pivot cycling between player bases.
/// </summary>
public partial class OrbitCamera : Camera3D
{
    [Export]
    public float MinDistance { get; set; } = 8f;

    [Export]
    public float MaxDistance { get; set; } = 60f;

    [Export]
    public float DefaultDistance { get; set; } = 28f;

    [Export]
    public float MinPitch { get; set; } = -0.15f;

    [Export]
    public float MaxPitch { get; set; } = -1.35f;

    [Export]
    public float DefaultPitch { get; set; } = -0.55f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.004f;

    [Export]
    public float ZoomSpeed { get; set; } = 1.5f;

    [Export]
    public float OrbitSmoothing { get; set; } = 8f;

    [Export]
    public float ZoomSmoothing { get; set; } = 8f;

    [Export]
    public float PivotSmoothing { get; set; } = 4f;

    [Export]
    public float AutoRotateSpeed { get; set; } = 0.15f;

    [Export]
    public float AutoRotateDelay { get; set; } = 3f;

    [Export]
    public float SnapDuration { get; set; } = 0.6f;

    private float _yaw;
    private float _pitch;
    private float _targetYaw;
    private float _targetPitch;
    private float _distance;
    private float _targetDistance;
    private Vector3 _pivot;
    private Vector3 _targetPivot;
    private bool _isDragging;
    private float _idleTimer;
    private bool _isActive;
    private float _lastClickTime;
    private Vector2 _lastClickPosition;

    // Pivot cycling
    private readonly List<Vector3> _pivotPoints = new List<Vector3>();
    private int _currentPivotIndex;

    public override void _Ready()
    {
        _yaw = 0f;
        _pitch = DefaultPitch;
        _targetYaw = _yaw;
        _targetPitch = _pitch;
        _distance = DefaultDistance;
        _targetDistance = DefaultDistance;

        // Default pivot is center of battlefield (arena is centered at origin)
        float centerX = 0f;
        float centerZ = 0f;
        float centerY = 4f;
        _pivot = new Vector3(centerX, centerY, centerZ);
        _targetPivot = _pivot;

        // Populate pivot points (battlefield center + each player base center)
        _pivotPoints.Add(_pivot);
        foreach (Vector3I zoneOrigin in GameConfig.FourPlayerZoneOrigins)
        {
            _pivotPoints.Add(ComputeBaseCenter(zoneOrigin));
        }
        _currentPivotIndex = 0;

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
        }
    }

    public void Activate()
    {
        _isActive = true;
        Current = true;
        SetProcessUnhandledInput(true);
        SetProcess(true);
        _idleTimer = 0f;
    }

    public void Deactivate()
    {
        _isActive = false;
        _isDragging = false;
        SetProcessUnhandledInput(false);
        SetProcess(false);
        if (Current)
        {
            Current = false;
        }
    }

    /// <summary>Set the orbit pivot to a world position (smoothly transitions).</summary>
    public void SetPivot(Vector3 worldPosition)
    {
        _targetPivot = worldPosition;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isActive)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            // Middle or right click drag to orbit
            if (mouseButton.ButtonIndex == MouseButton.Middle || mouseButton.ButtonIndex == MouseButton.Right)
            {
                _isDragging = mouseButton.Pressed;
                if (_isDragging)
                {
                    _idleTimer = 0f;
                }
            }

            // Left click: detect double-click to snap to pivot
            if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
            {
                float now = (float)Time.GetTicksMsec() / 1000f;
                float timeSinceLast = now - _lastClickTime;
                float distFromLast = mouseButton.Position.DistanceTo(_lastClickPosition);
                if (timeSinceLast < 0.4f && distFromLast < 10f)
                {
                    CyclePivot();
                }

                _lastClickTime = now;
                _lastClickPosition = mouseButton.Position;
            }

            // Scroll zoom
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    _targetDistance = Mathf.Clamp(_targetDistance - ZoomSpeed, MinDistance, MaxDistance);
                    _idleTimer = 0f;
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    _targetDistance = Mathf.Clamp(_targetDistance + ZoomSpeed, MinDistance, MaxDistance);
                    _idleTimer = 0f;
                }
            }
        }

        // Mouse drag rotation
        if (@event is InputEventMouseMotion motion && _isDragging)
        {
            _targetYaw -= motion.Relative.X * MouseSensitivity;
            _targetPitch = Mathf.Clamp(_targetPitch - motion.Relative.Y * MouseSensitivity, MaxPitch, MinPitch);
            _idleTimer = 0f;
        }

        // Tab to cycle pivot between bases
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.PhysicalKeycode == Key.Tab)
        {
            CyclePivot();
        }
    }

    public override void _Process(double delta)
    {
        if (!_isActive)
        {
            return;
        }

        float dt = (float)delta;

        // Auto-rotate when idle
        _idleTimer += dt;
        if (!_isDragging && _idleTimer > AutoRotateDelay)
        {
            _targetYaw += AutoRotateSpeed * dt;
        }

        // Smooth interpolation using exponential decay for frame-rate independence
        float orbitSmooth = 1f - Mathf.Exp(-OrbitSmoothing * dt);
        float zoomSmooth = 1f - Mathf.Exp(-ZoomSmoothing * dt);
        float pivotSmooth = 1f - Mathf.Exp(-PivotSmoothing * dt);

        _yaw = Mathf.Lerp(_yaw, _targetYaw, orbitSmooth);
        _pitch = Mathf.Lerp(_pitch, _targetPitch, orbitSmooth);
        _distance = Mathf.Lerp(_distance, _targetDistance, zoomSmooth);
        _pivot = _pivot.Lerp(_targetPivot, pivotSmooth);

        // Compute orbit position
        Vector3 offset = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            -Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)
        ).Normalized() * _distance;

        GlobalPosition = _pivot + offset;
        LookAt(_pivot, Vector3.Up);
    }

    private void CyclePivot()
    {
        if (_pivotPoints.Count == 0)
        {
            return;
        }

        _currentPivotIndex = (_currentPivotIndex + 1) % _pivotPoints.Count;
        _targetPivot = _pivotPoints[_currentPivotIndex];
        _idleTimer = 0f;
    }

    private static Vector3 ComputeBaseCenter(Vector3I zoneOrigin)
    {
        float x = (zoneOrigin.X + GameConfig.FourPlayerBuildZoneWidth * 0.5f) * GameConfig.BuildUnitMeters;
        float y = (zoneOrigin.Y + GameConfig.FourPlayerBuildZoneHeight * 0.25f) * GameConfig.BuildUnitMeters;
        float z = (zoneOrigin.Z + GameConfig.FourPlayerBuildZoneDepth * 0.5f) * GameConfig.BuildUnitMeters;
        return new Vector3(x, y, z);
    }

    private void OnPhaseChanged(PhaseChangedEvent e)
    {
        if (e.CurrentPhase == GamePhase.FogReveal)
        {
            Activate();
        }
        else if (e.PreviousPhase == GamePhase.FogReveal)
        {
            // Deactivate when leaving FogReveal; FreeFlyCamera takes over for Combat
            Deactivate();
        }
    }
}
