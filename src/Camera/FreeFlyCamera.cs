using System;
using Godot;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Camera;

/// <summary>
/// Build-phase free-fly camera with smooth acceleration, mouse look, zoom, and build-zone constraints.
/// Supports dynamic per-player bounds during building and full arena bounds during combat.
/// </summary>
public partial class FreeFlyCamera : Camera3D
{
    [Export]
    public float MoveSpeed { get; set; } = 16f;

    [Export]
    public float SprintMultiplier { get; set; } = 3f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.003f;

    [Export]
    public float Acceleration { get; set; } = 8f;

    [Export]
    public float Deceleration { get; set; } = 6f;

    [Export]
    public float ZoomSpeed { get; set; } = 2f;

    [Export]
    public float ZoomSmoothing { get; set; } = 8f;

    [Export]
    public float MinZoomFov { get; set; } = 25f;

    [Export]
    public float MaxZoomFov { get; set; } = 80f;

    [Export]
    public float RotationSmoothing { get; set; } = 12f;

    [Export]
    public float PositionSmoothing { get; set; } = 10f;

    /// <summary>Buffer (in meters) beyond the build zone the camera may travel.</summary>
    [Export]
    public float BoundsBuffer { get; set; } = 12f;

    private float _pitch;
    private float _yaw;
    private float _targetPitch;
    private float _targetYaw;
    private float _targetFov;
    private Vector3 _velocity;
    private Vector3 _targetPosition;
    private bool _isMouseCaptured;
    private bool _isActive;

    // Build-zone bounds (world-space AABB)
    private Vector3 _boundsMin;
    private Vector3 _boundsMax;

    // Whether custom bounds have been set (vs. full arena defaults)
    private bool _hasCustomBounds;

    // Smooth transition state
    private bool _isTransitioning;
    private float _transitionElapsed;
    private const float TransitionDuration = 0.6f;

    /// <summary>Minimum camera Y position to prevent going under the map.
    /// Ground surface is at Y = PrototypeGroundThickness * MicrovoxelMeters = 3.0m,
    /// so we keep the camera at least 1m above ground to avoid clipping.</summary>
    private const float MinCameraHeight = 4.0f;

    // Voxel collision state (active during Combat/GameOver to block enemy walls)
    private bool _voxelCollisionEnabled;

    /// <summary>Collision radius in world meters around the camera position.
    /// Multiple sample points are checked within this radius.</summary>
    private const float CollisionRadius = 0.4f;

    /// <summary>Axis-aligned sample offsets for collision detection (center + 6 directions).</summary>
    private static readonly Vector3[] CollisionOffsets =
    {
        Vector3.Zero,
        new Vector3(CollisionRadius, 0f, 0f),
        new Vector3(-CollisionRadius, 0f, 0f),
        new Vector3(0f, CollisionRadius, 0f),
        new Vector3(0f, -CollisionRadius, 0f),
        new Vector3(0f, 0f, CollisionRadius),
        new Vector3(0f, 0f, -CollisionRadius),
    };

    public override void _Ready()
    {
        _targetFov = Fov;
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
        _targetYaw = _yaw;
        _targetPitch = _pitch;
        _targetPosition = GlobalPosition;

        ComputeFullArenaBounds();

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

    /// <summary>
    /// Restricts camera movement to the specified world-space AABB (with BoundsBuffer padding).
    /// Call this when switching active builder during the build phase.
    /// </summary>
    public void SetBuildZoneBounds(Vector3 zoneMinWorld, Vector3 zoneMaxWorld)
    {
        _boundsMin = zoneMinWorld - new Vector3(BoundsBuffer, 0f, BoundsBuffer);
        _boundsMax = zoneMaxWorld + new Vector3(BoundsBuffer, BoundsBuffer, BoundsBuffer);
        // Keep camera above minimum height to prevent going under the map
        _boundsMin.Y = Mathf.Max(_boundsMin.Y, MinCameraHeight);
        _hasCustomBounds = true;
    }

    /// <summary>
    /// Resets camera bounds to the full arena (for combat phase or overview).
    /// </summary>
    public void ResetToFullArenaBounds()
    {
        ComputeFullArenaBounds();
        // Mark as having custom bounds so Activate() doesn't jump to a default
        // overhead position — the camera should stay where it is and just unlock
        // its movement range to the full arena.
        _hasCustomBounds = true;
    }

    /// <summary>
    /// Smoothly transitions the camera to a new position/target over a short duration.
    /// </summary>
    public void TransitionToLookTarget(Vector3 position, Vector3 target)
    {
        // Enforce minimum camera height to prevent going under the map
        position.Y = Mathf.Max(position.Y, MinCameraHeight);

        // Also clamp current position above ground before interpolation starts,
        // so the lerp path never dips below the map.
        Vector3 currentPos = GlobalPosition;
        if (currentPos.Y < MinCameraHeight)
        {
            currentPos.Y = MinCameraHeight;
            GlobalPosition = currentPos;
        }

        _targetPosition = position;
        Vector3 delta = (target - position).Normalized();
        // Godot's default forward is -Z, so the yaw that makes the camera face
        // direction 'delta' is Atan2(-delta.X, -delta.Z).
        _targetYaw = Mathf.Atan2(-delta.X, -delta.Z);
        float horizontalDist = new Vector2(delta.X, delta.Z).Length();
        _targetPitch = Mathf.Atan2(delta.Y, horizontalDist);
        _targetPitch = Mathf.Clamp(_targetPitch, -1.45f, 1.45f);

        _isTransitioning = true;
        _transitionElapsed = 0f;
        _velocity = Vector3.Zero;
    }

    public void Activate()
    {
        _isActive = true;
        Current = true;
        SetProcessUnhandledInput(true);
        SetProcess(true);

        // Ensure the camera is never below minimum height when activated
        if (GlobalPosition.Y < MinCameraHeight)
        {
            Vector3 pos = GlobalPosition;
            pos.Y = MinCameraHeight;
            GlobalPosition = pos;
            _targetPosition.Y = Mathf.Max(_targetPosition.Y, MinCameraHeight);
        }

        // If no custom bounds have been set, use a sensible default position
        // facing toward the arena center from behind the +Z side.
        if (!_hasCustomBounds)
        {
            Vector3 arenaCenter = (_boundsMin + _boundsMax) * 0.5f;
            _targetPosition = arenaCenter + new Vector3(0f, 15f, 20f);
            // Face toward the arena center from the camera position
            Vector3 toCenter = (arenaCenter - _targetPosition).Normalized();
            _targetYaw = Mathf.Atan2(-toCenter.X, -toCenter.Z);
            float hDist = new Vector2(toCenter.X, toCenter.Z).Length();
            _targetPitch = Mathf.Clamp(Mathf.Atan2(toCenter.Y, hDist), -1.45f, 1.45f);
            GlobalPosition = _targetPosition;
            _pitch = _targetPitch;
            _yaw = _targetYaw;
            Rotation = new Vector3(_pitch, _yaw, 0f);
        }
    }

    public void SetLookTarget(Vector3 position, Vector3 target)
    {
        // Enforce minimum camera height to prevent going under the map
        position.Y = Mathf.Max(position.Y, MinCameraHeight);

        _targetPosition = position;
        GlobalPosition = position;
        Vector3 delta = (target - position).Normalized();
        // Godot's default forward is -Z, so the yaw that makes the camera face
        // direction 'delta' is Atan2(-delta.X, -delta.Z).
        _targetYaw = Mathf.Atan2(-delta.X, -delta.Z);
        float horizontalDist = new Vector2(delta.X, delta.Z).Length();
        _targetPitch = Mathf.Atan2(delta.Y, horizontalDist);
        _targetPitch = Mathf.Clamp(_targetPitch, -1.45f, 1.45f);
        _yaw = _targetYaw;
        _pitch = _targetPitch;
        Rotation = new Vector3(_pitch, _yaw, 0f);
    }

    public void Deactivate()
    {
        _isActive = false;
        _isTransitioning = false;
        _isMouseCaptured = false;
        SetProcessUnhandledInput(false);
        SetProcess(false);
        if (Current)
        {
            Current = false;
        }
    }

    /// <summary>Reset zoom to default FOV (70) immediately.</summary>
    public void ResetZoom()
    {
        _targetFov = 70f;
        Fov = 70f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isActive)
        {
            return;
        }

        // Middle-click drag for mouse look (right-click is reserved for erase)
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _isMouseCaptured = mouseButton.Pressed;
            }

            // Scroll wheel zoom
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    _targetFov = Mathf.Clamp(_targetFov - ZoomSpeed, MinZoomFov, MaxZoomFov);
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    _targetFov = Mathf.Clamp(_targetFov + ZoomSpeed, MinZoomFov, MaxZoomFov);
                }
            }
        }

        // Mouse look while middle-button dragging
        if (@event is InputEventMouseMotion motion && _isMouseCaptured)
        {
            _targetYaw -= motion.Relative.X * MouseSensitivity;
            _targetPitch = Mathf.Clamp(_targetPitch - motion.Relative.Y * MouseSensitivity, -1.45f, 1.45f);
        }

        // Escape to release mouse look
        if (@event.IsActionPressed("ui_cancel"))
        {
            _isMouseCaptured = false;
        }
    }

    public override void _Process(double delta)
    {
        if (!_isActive)
        {
            return;
        }

        float dt = (float)delta;

        // Handle smooth transition (blocks player input during transition)
        if (_isTransitioning)
        {
            _transitionElapsed += dt;
            float t = Mathf.Clamp(_transitionElapsed / TransitionDuration, 0f, 1f);
            // Smooth-step easing for a pleasant feel
            float smooth = t * t * (3f - 2f * t);

            Vector3 interpolated = GlobalPosition.Lerp(_targetPosition, Mathf.Clamp(smooth * 4f * dt + 0.05f, 0f, 1f));
            // Clamp Y to prevent camera from dipping under the map during transition
            interpolated.Y = Mathf.Max(interpolated.Y, MinCameraHeight);
            GlobalPosition = interpolated;
            float rotLerp = Mathf.Clamp(smooth * 4f * dt + 0.05f, 0f, 1f);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, rotLerp);
            _yaw = Mathf.Lerp(_yaw, _targetYaw, rotLerp);
            Rotation = new Vector3(_pitch, _yaw, 0f);

            if (t >= 1f)
            {
                _isTransitioning = false;
                GlobalPosition = _targetPosition;
                _pitch = _targetPitch;
                _yaw = _targetYaw;
                Rotation = new Vector3(_pitch, _yaw, 0f);
            }

            // Still interpolate FOV during transition
            Fov = Mathf.Lerp(Fov, _targetFov, 1f - Mathf.Exp(-ZoomSmoothing * dt));
            return;
        }

        // Gather movement input
        Vector3 inputDir = Vector3.Zero;
        if (Input.IsActionPressed("move_forward"))
        {
            inputDir -= Transform.Basis.Z;
        }

        if (Input.IsActionPressed("move_back"))
        {
            inputDir += Transform.Basis.Z;
        }

        if (Input.IsActionPressed("move_left"))
        {
            inputDir -= Transform.Basis.X;
        }

        if (Input.IsActionPressed("move_right"))
        {
            inputDir += Transform.Basis.X;
        }

        // Vertical movement (world-space up/down)
        if (Input.IsKeyPressed(Key.Q))
        {
            inputDir += Vector3.Up;
        }

        if (Input.IsKeyPressed(Key.E))
        {
            inputDir += Vector3.Down;
        }

        // Calculate target speed
        float speed = MoveSpeed;
        if (Input.IsKeyPressed(Key.Shift))
        {
            speed *= SprintMultiplier;
        }

        // Smooth acceleration / deceleration
        if (inputDir.LengthSquared() > 0.001f)
        {
            inputDir = inputDir.Normalized();
            _velocity = _velocity.Lerp(inputDir * speed, 1f - Mathf.Exp(-Acceleration * dt));
        }
        else
        {
            _velocity = _velocity.Lerp(Vector3.Zero, 1f - Mathf.Exp(-Deceleration * dt));
        }

        // Update target position
        _targetPosition += _velocity * dt;

        // Clamp to current bounds
        _targetPosition = new Vector3(
            Mathf.Clamp(_targetPosition.X, _boundsMin.X, _boundsMax.X),
            Mathf.Clamp(_targetPosition.Y, _boundsMin.Y, _boundsMax.Y),
            Mathf.Clamp(_targetPosition.Z, _boundsMin.Z, _boundsMax.Z)
        );

        // Voxel collision: block camera from passing through enemy walls and terrain
        if (_voxelCollisionEnabled)
        {
            _targetPosition = ApplyVoxelCollision(GlobalPosition, _targetPosition);
        }

        // Smooth interpolation for position
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, 1f - Mathf.Exp(-PositionSmoothing * dt));

        // Smooth interpolation for rotation (slerp-like via exponential smoothing on angles)
        float rotSmooth = 1f - Mathf.Exp(-RotationSmoothing * dt);
        _pitch = Mathf.Lerp(_pitch, _targetPitch, rotSmooth);
        _yaw = Mathf.Lerp(_yaw, _targetYaw, rotSmooth);
        Rotation = new Vector3(_pitch, _yaw, 0f);

        // Smooth FOV zoom
        Fov = Mathf.Lerp(Fov, _targetFov, 1f - Mathf.Exp(-ZoomSmoothing * dt));
    }

    // Mouse look is now purely state-tracked via _isMouseCaptured flag.
    // The cursor is NEVER hidden/captured — middle-click drag uses relative motion
    // while keeping the cursor visible at all times.

    private void ComputeFullArenaBounds()
    {
        // Derive from GameConfig prototype build zones with generous buffer for full arena.
        // The mountains extend to halfWidth + MountainStartOffset + MountainBorderWidth
        // (64 + 20 + 30 = 114 microvoxels = 57m). Add a small buffer beyond the outer
        // mountain edge so the camera can fly over and past them.
        float halfWidth = GameConfig.PrototypeArenaWidth * GameConfig.MicrovoxelMeters * 0.5f;
        float halfDepth = GameConfig.PrototypeArenaDepth * GameConfig.MicrovoxelMeters * 0.5f;
        float height = GameConfig.PrototypeBuildZoneHeight * GameConfig.BuildUnitMeters;
        float mountainExtent = (TerrainDecorator.MountainStartOffset + 30) * GameConfig.MicrovoxelMeters;
        float arenaBuffer = 8f; // small extra buffer beyond mountain outer edge

        _boundsMin = new Vector3(-halfWidth - mountainExtent - arenaBuffer, MinCameraHeight, -halfDepth - mountainExtent - arenaBuffer);
        _boundsMax = new Vector3(halfWidth + mountainExtent + arenaBuffer, height + 30f, halfDepth + mountainExtent + arenaBuffer);
    }

    private void OnPhaseChanged(PhaseChangedEvent e)
    {
        if (e.CurrentPhase == GamePhase.Building)
        {
            _voxelCollisionEnabled = false;
            Activate();
        }
        else if (e.CurrentPhase == GamePhase.Combat)
        {
            // During Combat, FreeFlyCamera stays available for WASD/mouse free-fly movement.
            // CombatCamera temporarily takes over for cinematic moments (projectile follow,
            // impact cam, kill cam, targeting) and then returns control to FreeFlyCamera.
            ResetToFullArenaBounds();
            _voxelCollisionEnabled = true;
            Activate();
        }
        else if (e.CurrentPhase == GamePhase.GameOver)
        {
            // During GameOver, FreeFlyCamera stays available for post-game viewing.
            // GameManager calls SwitchToFreeFlyCamera() to activate it.
            ResetToFullArenaBounds();
            _voxelCollisionEnabled = true;
        }
        else if (e.CurrentPhase == GamePhase.FogReveal)
        {
            _voxelCollisionEnabled = false;
            Deactivate();
        }
        else if (e.CurrentPhase == GamePhase.Menu)
        {
            // Disable camera input on the main menu so scroll wheel doesn't zoom,
            // but keep the camera current so the menu background battle is visible.
            _voxelCollisionEnabled = false;
            _isActive = false;
            SetProcessUnhandledInput(false);
            // Keep Current = true and SetProcess enabled so UpdateMenuOrbitCamera
            // can position the camera each frame via SetLookTarget.
        }
    }

    /// <summary>
    /// Checks whether a world-space position collides with an enemy or terrain voxel.
    /// Returns true if the position is blocked (camera should not move there).
    /// Own-zone voxels are passable; enemy-zone and natural terrain voxels are solid.
    /// </summary>
    private bool IsBlockedByVoxel(VoxelWorld voxelWorld, IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones, Vector3 worldPos)
    {
        Vector3I micro = MathHelpers.WorldToMicrovoxel(worldPos);
        Voxel.Voxel voxel = voxelWorld.GetVoxel(micro);
        if (voxel.IsAir)
        {
            return false;
        }

        // Solid voxel found — check if it belongs to the local player's build zone.
        // Player1 is always the local human player.
        if (buildZones.TryGetValue(PlayerSlot.Player1, out BuildZone ownZone))
        {
            if (ownZone.ContainsMicrovoxel(micro))
            {
                // Inside our own build zone: allow camera to pass through
                return false;
            }
        }

        // Solid voxel in enemy zone or natural terrain — block
        return true;
    }

    /// <summary>
    /// Applies per-axis voxel collision between the current and desired camera position.
    /// Uses a set of sample points at CollisionRadius offsets around the camera center.
    /// Movement is resolved axis-by-axis so the camera slides along walls smoothly
    /// instead of stopping dead when approaching at an angle.
    /// </summary>
    private Vector3 ApplyVoxelCollision(Vector3 currentPos, Vector3 desiredPos)
    {
        GameManager? gm = GetTree()?.Root.GetNodeOrNull<GameManager>("Main");
        if (gm?.VoxelWorld == null)
        {
            return desiredPos;
        }

        VoxelWorld voxelWorld = gm.VoxelWorld;
        IReadOnlyDictionary<PlayerSlot, BuildZone> buildZones = gm.BuildZones;

        // Resolve movement axis-by-axis for smooth wall sliding
        Vector3 result = currentPos;

        // Try X axis
        Vector3 testX = new Vector3(desiredPos.X, currentPos.Y, currentPos.Z);
        bool blockedX = false;
        for (int i = 0; i < CollisionOffsets.Length; i++)
        {
            if (IsBlockedByVoxel(voxelWorld, buildZones, testX + CollisionOffsets[i]))
            {
                blockedX = true;
                break;
            }
        }
        result.X = blockedX ? currentPos.X : desiredPos.X;

        // Try Y axis
        Vector3 testY = new Vector3(result.X, desiredPos.Y, currentPos.Z);
        bool blockedY = false;
        for (int i = 0; i < CollisionOffsets.Length; i++)
        {
            if (IsBlockedByVoxel(voxelWorld, buildZones, testY + CollisionOffsets[i]))
            {
                blockedY = true;
                break;
            }
        }
        result.Y = blockedY ? currentPos.Y : desiredPos.Y;

        // Try Z axis
        Vector3 testZ = new Vector3(result.X, result.Y, desiredPos.Z);
        bool blockedZ = false;
        for (int i = 0; i < CollisionOffsets.Length; i++)
        {
            if (IsBlockedByVoxel(voxelWorld, buildZones, testZ + CollisionOffsets[i]))
            {
                blockedZ = true;
                break;
            }
        }
        result.Z = blockedZ ? currentPos.Z : desiredPos.Z;

        // Kill velocity on blocked axes to prevent jitter from accumulated momentum
        if (blockedX)
        {
            _velocity.X = 0f;
        }
        if (blockedY)
        {
            _velocity.Y = 0f;
        }
        if (blockedZ)
        {
            _velocity.Z = 0f;
        }

        return result;
    }
}
