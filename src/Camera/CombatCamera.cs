using System;
using Godot;
using VoxelSiege.Combat;
using VoxelSiege.Core;

namespace VoxelSiege.Camera;

/// <summary>
/// Cinematic combat camera with multiple modes: aim, follow-projectile, impact, kill-cam,
/// free-look, weapon POV (first-person aiming), targeting (click-to-target with free orbit),
/// and top-down spectator view. All transitions are smoothly interpolated. FOV shifts add drama.
/// </summary>
public partial class CombatCamera : Camera3D
{
    public enum Mode
    {
        Inactive,
        FreeLook,
        Aim,
        FollowProjectile,
        Impact,
        KillCam,
        WeaponPOV,
        TopDown,
        Targeting,
    }

    // --- Tuning exports ---

    [Export]
    public float TransitionSpeed { get; set; } = 5f;

    [Export]
    public float FollowLag { get; set; } = 0.08f;

    [Export]
    public float FollowLookAhead { get; set; } = 2f;

    [Export]
    public float ImpactHoldTime { get; set; } = 1.5f;

    [Export]
    public float KillCamDuration { get; set; } = 2.5f;

    [Export]
    public float KillCamOrbitSpeed { get; set; } = 1.8f;

    [Export]
    public float KillCamOrbitRadius { get; set; } = 6f;

    [Export]
    public float DefaultFov { get; set; } = 70f;

    [Export]
    public float AimFov { get; set; } = 55f;

    [Export]
    public float ImpactFov { get; set; } = 85f;

    [Export]
    public float KillCamFov { get; set; } = 60f;

    [Export]
    public float WeaponPovFov { get; set; } = 65f;

    [Export]
    public float TargetingFov { get; set; } = 65f;

    [Export]
    public float FovSmoothing { get; set; } = 6f;

    [Export]
    public float PositionSmoothing { get; set; } = 8f;

    // --- Free-look orbit settings ---
    [Export]
    public float FreeLookMouseSensitivity { get; set; } = 0.004f;

    [Export]
    public float FreeLookDistance { get; set; } = 25f;

    [Export]
    public float FreeLookMinPitch { get; set; } = -0.15f;

    [Export]
    public float FreeLookMaxPitch { get; set; } = -1.35f;

    // --- Weapon POV settings ---
    [Export]
    public float WeaponPovMouseSensitivity { get; set; } = 0.003f;

    // --- Targeting mode settings ---
    [Export]
    public float TargetingOrbitSensitivity { get; set; } = 0.004f;

    [Export]
    public float TargetingDistance { get; set; } = 20f;

    [Export]
    public float TargetingMinDistance { get; set; } = 8f;

    [Export]
    public float TargetingMaxDistance { get; set; } = 50f;

    [Export]
    public float TargetingMinPitch { get; set; } = -0.15f;

    [Export]
    public float TargetingMaxPitch { get; set; } = -1.35f;

    [Export]
    public float TargetingMinVoxelDistance { get; set; } = 8f;

    // --- Top-down settings ---
    [Export]
    public float TopDownHeight { get; set; } = 45f;

    [Export]
    public float TopDownMinHeight { get; set; } = 25f;

    [Export]
    public float TopDownMaxHeight { get; set; } = 60f;

    [Export]
    public float TopDownPanSensitivity { get; set; } = 0.15f;

    [Export]
    public float TopDownZoomSpeed { get; set; } = 2f;

    /// <summary>Minimum camera Y position to prevent going under the map.</summary>
    private const float MinCameraHeight = 2.0f;

    // --- Internal state ---
    public Mode CurrentMode { get; private set; } = Mode.Inactive;

    private float _targetFov;
    private Vector3 _targetPosition;
    private Vector3 _targetLookAt;
    private Vector3 _currentLookAt;
    private bool _isActive;

    // Aim mode
    private WeaponBase? _aimWeapon;
    private Vector3 _aimOffset = new Vector3(-1.2f, 1.8f, -3.5f);

    // Follow projectile
    private Node3D? _followTarget;
    private Vector3 _followOffset = new Vector3(0f, 2f, -6f);
    private Vector3 _lastProjectileVelocity;
    private Vector3 _lastProjectilePosition;

    // Impact mode
    private Vector3 _impactPoint;
    private float _impactRadius;
    private float _impactTimer;
    private float _preImpactTimeScale;

    // Kill cam
    private Vector3 _killCamCenter;
    private float _killCamTimer;
    private float _killCamYaw;
    private float _preKillTimeScale;

    // Free look orbit
    private float _freeLookYaw;
    private float _freeLookPitch = -0.45f;
    private bool _freeLookDragging;
    private Vector3 _freeLookPivot;

    // Weapon POV
    private WeaponBase? _povWeapon;
    private AimingSystem? _povAiming;

    // Targeting mode
    private float _targetingYaw;
    private float _targetingPitch = -0.45f;
    private bool _targetingDragging;
    private Vector3 _targetingPivot;
    private bool _targetingRmbDown;
    private bool _targetingRmbDragged;

    // Top-down
    private Vector3 _topDownCenter;
    private bool _topDownDragging;

    public override void _Ready()
    {
        _targetFov = DefaultFov;
        Fov = DefaultFov;
        _targetPosition = GlobalPosition;
        _currentLookAt = GlobalPosition + Vector3.Forward * 10f;
        _targetLookAt = _currentLookAt;

        // Default free-look pivot: center of battlefield (arena is centered at origin)
        _freeLookPivot = new Vector3(0f, 4f, 0f);
        _targetingPivot = new Vector3(0f, 4f, 0f);
        _topDownCenter = Vector3.Zero;
    }

    // ------------------------------------------------------------------
    // Public API: mode entry points
    // ------------------------------------------------------------------

    public void Activate()
    {
        _isActive = true;
        Current = true;
        SetProcess(true);
        SetProcessUnhandledInput(true);
    }

    public void Deactivate()
    {
        ReleaseMouse();
        _isActive = false;
        CurrentMode = Mode.Inactive;
        _freeLookDragging = false;
        _topDownDragging = false;
        _targetingDragging = false;
        _targetingRmbDown = false;
        _targetingRmbDragged = false;
        _povWeapon = null;
        _povAiming = null;
        RestoreTimeScale();
        SetProcess(false);
        SetProcessUnhandledInput(false);
        if (Current)
        {
            Current = false;
        }
    }

    /// <summary>Lock behind the selected weapon for aiming (over-the-shoulder).</summary>
    public void AimMode(WeaponBase weapon)
    {
        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.Aim;
        _aimWeapon = weapon;
        _targetFov = AimFov;
    }

    /// <summary>Smoothly follow a launched projectile with cinematic lag.</summary>
    public void FollowProjectile(Node3D projectile)
    {
        if (CurrentMode == Mode.WeaponPOV || CurrentMode == Mode.Targeting)
        {
            ReleaseMouse();
        }

        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.FollowProjectile;
        _povWeapon = null;
        _povAiming = null;
        _followTarget = projectile;
        _targetFov = DefaultFov;
        _lastProjectileVelocity = Vector3.Forward;
        _lastProjectilePosition = projectile.GlobalPosition;

        // Cursor stays visible during projectile flight (user may click UI)
    }

    /// <summary>Transition to view the impact point. Applies brief slow-motion.</summary>
    public void ImpactCam(Vector3 impactPoint, float radius)
    {
        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.Impact;
        _impactPoint = impactPoint;
        _impactRadius = radius;
        _impactTimer = 0f;
        _targetFov = ImpactFov;

        // Brief slow-mo for dramatic effect
        _preImpactTimeScale = (float)Engine.TimeScale;
        Engine.TimeScale = 0.5;

        // Position camera at a good vantage to see the impact
        float viewDist = Mathf.Max(6f, radius * 2.5f);
        _targetPosition = impactPoint + new Vector3(viewDist * 0.5f, viewDist * 0.6f, viewDist * 0.5f);
        _targetLookAt = impactPoint;
    }

    /// <summary>Dramatic slow-motion orbit around a dying commander.</summary>
    public void KillCam(Node3D commander)
    {
        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.KillCam;
        _killCamCenter = commander.GlobalPosition;
        _killCamTimer = 0f;
        _killCamYaw = 0f;
        _targetFov = KillCamFov;

        _preKillTimeScale = (float)Engine.TimeScale;
        Engine.TimeScale = GameConfig.SlowMoTimeScale;
    }

    /// <summary>Free orbit look between turns for inspecting bases.</summary>
    public void FreeLook()
    {
        if (CurrentMode == Mode.WeaponPOV || CurrentMode == Mode.Targeting)
        {
            ReleaseMouse();
        }

        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.FreeLook;
        _povWeapon = null;
        _povAiming = null;
        _targetFov = DefaultFov;

        // Restore visible cursor
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// First-person weapon POV: camera sits at the weapon barrel looking along the aim direction.
    /// Mouse input directly controls yaw/pitch on the aiming system.
    /// The mouse cursor is captured (hidden, relative movement) for unlimited 360-degree aiming.
    /// </summary>
    public void EnterWeaponPOV(WeaponBase weapon, AimingSystem aiming)
    {
        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.WeaponPOV;
        _povWeapon = weapon;
        _povAiming = aiming;
        _targetFov = WeaponPovFov;

        // Confine cursor to window so it stays visible but can't leave during aiming
        Input.MouseMode = Input.MouseModeEnum.Confined;

        // Initialize aiming angles from weapon's current facing direction
        Vector3 forward = -weapon.GlobalTransform.Basis.Z.Normalized();
        aiming.YawRadians = Mathf.Atan2(-forward.X, -forward.Z);
        aiming.PitchRadians = Mathf.Asin(Mathf.Clamp(-forward.Y, -1f, 1f));
    }

    /// <summary>
    /// Enters targeting mode: free-orbit camera with visible cursor.
    /// Player clicks to set a target point on the voxel terrain.
    /// The pivot defaults to the enemy's approximate center.
    /// </summary>
    public void EnterTargeting(Vector3 pivot)
    {
        if (CurrentMode == Mode.WeaponPOV)
        {
            ReleaseMouse();
        }

        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.Targeting;
        _povWeapon = null;
        _povAiming = null;
        _targetingPivot = pivot;
        _targetingDragging = false;
        _targetingRmbDown = false;
        _targetingRmbDragged = false;
        _targetFov = TargetingFov;

        // Show cursor for click-to-target
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// Top-down spectator view: overhead camera centered on the arena.
    /// Allows panning and zoom but no aiming.
    /// </summary>
    public void TopDown(Vector3 center = default)
    {
        if (CurrentMode == Mode.WeaponPOV || CurrentMode == Mode.Targeting)
        {
            ReleaseMouse();
        }

        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.TopDown;
        _povWeapon = null;
        _povAiming = null;
        _topDownCenter = center;
        _topDownDragging = false;
        _targetFov = DefaultFov;
    }

    /// <summary>Whether the camera is currently in weapon POV mode (for UI queries).</summary>
    public bool IsInWeaponPOV => CurrentMode == Mode.WeaponPOV;

    /// <summary>Whether the camera is in targeting mode (click-to-target).</summary>
    public bool IsInTargeting => CurrentMode == Mode.Targeting;

    /// <summary>
    /// Exits weapon POV mode and releases the mouse cursor.
    /// Call this when the player cancels aiming (ESC/right-click) or fires.
    /// </summary>
    public void ExitWeaponPOV()
    {
        if (CurrentMode == Mode.WeaponPOV)
        {
            ReleaseMouse();
        }
        _povWeapon = null;
        _povAiming = null;
    }

    /// <summary>Releases the confined/captured mouse and restores the visible cursor.</summary>
    private void ReleaseMouse()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Fired when the player presses ESC or right-click during weapon POV or targeting to cancel aiming.
    /// GameManager subscribes to this to handle the state transition.
    /// </summary>
    public event Action? ExitWeaponPOVRequested;

    /// <summary>
    /// Fired when the player left-clicks during targeting mode.
    /// Passes the mouse position for raycasting by the GameManager.
    /// </summary>
    public event Action<Vector2>? TargetClickRequested;

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isActive)
        {
            return;
        }

        switch (CurrentMode)
        {
            case Mode.FreeLook:
                HandleFreeLookInput(@event);
                break;
            case Mode.WeaponPOV:
                HandleWeaponPOVInput(@event);
                break;
            case Mode.TopDown:
                HandleTopDownInput(@event);
                break;
            case Mode.Targeting:
                HandleTargetingInput(@event);
                break;
        }
    }

    private void HandleFreeLookInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _freeLookDragging = mouseButton.Pressed;
            }

            // Scroll zoom
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    FreeLookDistance = Mathf.Max(6f, FreeLookDistance - 1.5f);
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    FreeLookDistance = Mathf.Min(50f, FreeLookDistance + 1.5f);
                }
            }
        }

        if (@event is InputEventMouseMotion motion && _freeLookDragging)
        {
            _freeLookYaw -= motion.Relative.X * FreeLookMouseSensitivity;
            _freeLookPitch = Mathf.Clamp(_freeLookPitch - motion.Relative.Y * FreeLookMouseSensitivity, FreeLookMaxPitch, FreeLookMinPitch);
        }
    }

    private void HandleWeaponPOVInput(InputEvent @event)
    {
        if (_povAiming == null)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            // Mouse movement directly adjusts the aiming system angles
            _povAiming.YawRadians -= motion.Relative.X * WeaponPovMouseSensitivity;
            _povAiming.PitchRadians -= motion.Relative.Y * WeaponPovMouseSensitivity;
        }

        // Scroll wheel adjusts power
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _povAiming.PowerPercent = Mathf.Clamp(_povAiming.PowerPercent + 0.05f, 0f, 1f);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _povAiming.PowerPercent = Mathf.Clamp(_povAiming.PowerPercent - 0.05f, 0f, 1f);
            }
        }

        // ESC or right-click cancels weapon POV — handled via event signal
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            ExitWeaponPOVRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton rmbEvent && rmbEvent.Pressed && rmbEvent.ButtonIndex == MouseButton.Right)
        {
            ExitWeaponPOVRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleTargetingInput(InputEvent @event)
    {
        // Middle mouse or right mouse drag to orbit around the pivot
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _targetingDragging = mouseButton.Pressed;
            }

            // Right-click: start drag orbit. Only cancel on release if the player
            // did NOT drag (i.e. a simple click-release cancels, drag-release does not).
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                if (mouseButton.Pressed)
                {
                    _targetingRmbDown = true;
                    _targetingRmbDragged = false;
                    _targetingDragging = true;
                }
                else
                {
                    _targetingRmbDown = false;
                    // Only cancel targeting if the player didn't drag
                    if (!_targetingRmbDragged)
                    {
                        ExitWeaponPOVRequested?.Invoke();
                        GetViewport().SetInputAsHandled();
                    }
                    _targetingDragging = false;
                }
            }

            // Scroll zoom
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    TargetingDistance = Mathf.Max(TargetingMinDistance, TargetingDistance - 1.5f);
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    TargetingDistance = Mathf.Min(TargetingMaxDistance, TargetingDistance + 1.5f);
                }
            }

            // Left-click: request target set or confirm
            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
            {
                TargetClickRequested?.Invoke(mouseButton.Position);
                GetViewport().SetInputAsHandled();
            }
        }

        // Mouse motion: orbit when dragging (middle or right mouse)
        if (@event is InputEventMouseMotion motion && _targetingDragging)
        {
            _targetingYaw -= motion.Relative.X * TargetingOrbitSensitivity;
            _targetingPitch = Mathf.Clamp(
                _targetingPitch - motion.Relative.Y * TargetingOrbitSensitivity,
                TargetingMaxPitch, TargetingMinPitch);

            // Track whether right-click resulted in an actual drag
            if (_targetingRmbDown && (Mathf.Abs(motion.Relative.X) > 1f || Mathf.Abs(motion.Relative.Y) > 1f))
            {
                _targetingRmbDragged = true;
            }
        }

        // ESC cancels targeting
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            ExitWeaponPOVRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleTopDownInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            // Right or middle mouse drag to pan
            if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _topDownDragging = mouseButton.Pressed;
            }

            // Scroll wheel to zoom (change height)
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    TopDownHeight = Mathf.Max(TopDownMinHeight, TopDownHeight - TopDownZoomSpeed);
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    TopDownHeight = Mathf.Min(TopDownMaxHeight, TopDownHeight + TopDownZoomSpeed);
                }
            }
        }

        if (@event is InputEventMouseMotion motion && _topDownDragging)
        {
            // Pan the top-down view center in world XZ plane
            _topDownCenter.X -= motion.Relative.X * TopDownPanSensitivity * (TopDownHeight / 45f);
            _topDownCenter.Z -= motion.Relative.Y * TopDownPanSensitivity * (TopDownHeight / 45f);
        }
    }

    // ------------------------------------------------------------------
    // Process
    // ------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!_isActive)
        {
            return;
        }

        // Godot's _Process delta is already scaled by Engine.TimeScale.
        // We want the camera to move at real-time speed even during slow-mo,
        // so we recover the unscaled delta.
        float timeScale = Mathf.Max((float)Engine.TimeScale, 0.01f);
        float realDt = (float)(delta / timeScale);

        switch (CurrentMode)
        {
            case Mode.Aim:
                ProcessAim(realDt);
                break;
            case Mode.FollowProjectile:
                ProcessFollowProjectile(realDt);
                break;
            case Mode.Impact:
                ProcessImpact(realDt);
                break;
            case Mode.KillCam:
                ProcessKillCam(realDt);
                break;
            case Mode.FreeLook:
                ProcessFreeLook(realDt);
                break;
            case Mode.WeaponPOV:
                ProcessWeaponPOV(realDt);
                break;
            case Mode.TopDown:
                ProcessTopDown(realDt);
                break;
            case Mode.Targeting:
                ProcessTargeting(realDt);
                break;
        }

        // Smooth FOV
        Fov = Mathf.Lerp(Fov, _targetFov, 1f - Mathf.Exp(-FovSmoothing * realDt));

        // Enforce minimum camera height to prevent going under the map
        _targetPosition.Y = Mathf.Max(_targetPosition.Y, MinCameraHeight);

        if (CurrentMode == Mode.WeaponPOV)
        {
            // In weapon POV we set rotation directly from aim direction rather than
            // using LookAt on _currentLookAt, to avoid smoothing lag fighting aiming input.
            Vector3 wpvPos = GlobalPosition.Lerp(_targetPosition, 1f - Mathf.Exp(-PositionSmoothing * realDt));
            wpvPos.Y = Mathf.Max(wpvPos.Y, MinCameraHeight);
            GlobalPosition = wpvPos;
            // Rotation is set in ProcessWeaponPOV via LookAt
        }
        else
        {
            // Smooth position
            Vector3 smoothedPos = GlobalPosition.Lerp(_targetPosition, 1f - Mathf.Exp(-PositionSmoothing * realDt));
            smoothedPos.Y = Mathf.Max(smoothedPos.Y, MinCameraHeight);
            GlobalPosition = smoothedPos;

            // Smooth look-at
            _currentLookAt = _currentLookAt.Lerp(_targetLookAt, 1f - Mathf.Exp(-PositionSmoothing * realDt));
            if (GlobalPosition.DistanceSquaredTo(_currentLookAt) > 0.01f)
            {
                // Use a fallback up vector when the look direction is nearly vertical
                // to avoid the "Target and up vectors are colinear" warning.
                Vector3 lookDir = (_currentLookAt - GlobalPosition).Normalized();
                float dotUp = Mathf.Abs(lookDir.Dot(Vector3.Up));
                Vector3 upVec = dotUp > 0.99f ? Vector3.Forward : Vector3.Up;
                LookAt(_currentLookAt, upVec);
            }
        }
    }

    // ------------------------------------------------------------------
    // Mode processors
    // ------------------------------------------------------------------

    private void ProcessAim(float dt)
    {
        if (_aimWeapon == null || !GodotObject.IsInstanceValid(_aimWeapon))
        {
            FreeLook();
            return;
        }

        // Over-the-shoulder: offset relative to weapon's facing direction
        Transform3D weaponTransform = _aimWeapon.GlobalTransform;
        Vector3 behind = -weaponTransform.Basis.Z.Normalized();
        Vector3 right = weaponTransform.Basis.X.Normalized();
        Vector3 up = Vector3.Up;

        _targetPosition = _aimWeapon.GlobalPosition
                          + behind * _aimOffset.Z
                          + right * _aimOffset.X
                          + up * _aimOffset.Y;

        // Look slightly ahead of the weapon's forward direction
        _targetLookAt = _aimWeapon.GlobalPosition + (-behind) * 20f;
    }

    private void ProcessFollowProjectile(float dt)
    {
        if (_followTarget == null || !GodotObject.IsInstanceValid(_followTarget))
        {
            // Projectile gone (impacted) -- linger at the impact point before returning
            Input.MouseMode = Input.MouseModeEnum.Visible;
            ImpactCam(_lastProjectilePosition, 3f);
            return;
        }

        // Estimate velocity from position change (for look-ahead)
        Vector3 pos = _followTarget.GlobalPosition;
        _lastProjectilePosition = pos;

        // Trailing follow with slight lag
        Vector3 trailDir = _lastProjectileVelocity.Normalized();
        if (trailDir.LengthSquared() < 0.001f)
        {
            trailDir = Vector3.Forward;
        }

        _followOffset = (-trailDir * 5f) + (Vector3.Up * 2.5f);
        _targetPosition = pos + _followOffset;
        _targetLookAt = pos + trailDir * FollowLookAhead;

        // Track velocity for next frame (use basis forward as proxy since we can't diff reliably with slow-mo)
        _lastProjectileVelocity = -_followTarget.GlobalTransform.Basis.Z;
    }

    private void ProcessImpact(float dt)
    {
        _impactTimer += dt;
        _targetLookAt = _impactPoint;

        if (_impactTimer >= ImpactHoldTime)
        {
            // Restore time scale and transition out
            Engine.TimeScale = _preImpactTimeScale > 0.01f ? _preImpactTimeScale : 1f;
            FreeLook();
        }
    }

    private void ProcessKillCam(float dt)
    {
        _killCamTimer += dt;
        _killCamYaw += KillCamOrbitSpeed * dt;

        // Orbit around the kill point
        _targetPosition = _killCamCenter + new Vector3(
            Mathf.Sin(_killCamYaw) * KillCamOrbitRadius,
            KillCamOrbitRadius * 0.6f,
            Mathf.Cos(_killCamYaw) * KillCamOrbitRadius
        );
        _targetLookAt = _killCamCenter;

        if (_killCamTimer >= KillCamDuration)
        {
            Engine.TimeScale = _preKillTimeScale > 0.01f ? _preKillTimeScale : 1f;
            FreeLook();
        }
    }

    private void ProcessFreeLook(float dt)
    {
        Vector3 offset = new Vector3(
            Mathf.Sin(_freeLookYaw) * Mathf.Cos(_freeLookPitch),
            -Mathf.Sin(_freeLookPitch),
            Mathf.Cos(_freeLookYaw) * Mathf.Cos(_freeLookPitch)
        ).Normalized() * FreeLookDistance;

        _targetPosition = _freeLookPivot + offset;
        _targetLookAt = _freeLookPivot;
    }

    private void ProcessWeaponPOV(float dt)
    {
        if (_povWeapon == null || !GodotObject.IsInstanceValid(_povWeapon) || _povAiming == null)
        {
            FreeLook();
            return;
        }

        // Camera position: at the weapon with a small offset up and back from the barrel
        Vector3 aimDir = _povAiming.GetDirection();
        Vector3 weaponPos = _povWeapon.GlobalPosition;

        // Offset: slightly behind the barrel and above
        Vector3 backOffset = -aimDir * 0.5f;
        _targetPosition = weaponPos + backOffset + Vector3.Up * 0.8f;

        // Look along the aim direction; compute a point far ahead
        Vector3 lookTarget = _targetPosition + aimDir * 50f;

        // Use direct LookAt for snappy aiming response (no smoothing on rotation)
        Vector3 smoothedPos = GlobalPosition.Lerp(_targetPosition, 1f - Mathf.Exp(-PositionSmoothing * dt));
        GlobalPosition = smoothedPos;

        if (GlobalPosition.DistanceSquaredTo(lookTarget) > 0.01f)
        {
            Vector3 lookDir = (lookTarget - GlobalPosition).Normalized();
            float dotUp = Mathf.Abs(lookDir.Dot(Vector3.Up));
            Vector3 upVec = dotUp > 0.99f ? Vector3.Forward : Vector3.Up;
            LookAt(lookTarget, upVec);
        }
    }

    private void ProcessTargeting(float dt)
    {
        // Orbit around the targeting pivot (enemy fortress area)
        Vector3 offset = new Vector3(
            Mathf.Sin(_targetingYaw) * Mathf.Cos(_targetingPitch),
            -Mathf.Sin(_targetingPitch),
            Mathf.Cos(_targetingYaw) * Mathf.Cos(_targetingPitch)
        ).Normalized() * TargetingDistance;

        Vector3 desiredPos = _targetingPivot + offset;

        // Enforce minimum height: don't go below ground level
        desiredPos.Y = Mathf.Max(desiredPos.Y, 2.0f);

        // Enforce arena bounds (typical arena is roughly -30..30 on XZ)
        float arenaHalf = 35f;
        desiredPos.X = Mathf.Clamp(desiredPos.X, -arenaHalf, arenaHalf);
        desiredPos.Z = Mathf.Clamp(desiredPos.Z, -arenaHalf, arenaHalf);

        _targetPosition = desiredPos;
        _targetLookAt = _targetingPivot;
    }

    private void ProcessTopDown(float dt)
    {
        // Position camera directly above the arena center, looking straight down
        _targetPosition = new Vector3(_topDownCenter.X, TopDownHeight, _topDownCenter.Z);

        // Look at the center of the arena (directly below)
        _targetLookAt = _topDownCenter;

        // Since LookAt with exactly down direction can be unstable, we use a slight tilt
        // Set a small forward offset so the up-vector doesn't collapse
        _targetLookAt = new Vector3(_topDownCenter.X, 0f, _topDownCenter.Z + 0.01f);
    }

    // ------------------------------------------------------------------
    // Camera Angle Presets
    // ------------------------------------------------------------------

    /// <summary>
    /// Smoothly transition to a predefined orbit position using the free-look orbit system.
    /// Sets yaw, pitch, and distance then enters FreeLook mode.
    /// </summary>
    private void ApplyPreset(float yaw, float pitch, float distance, Vector3? pivot = null)
    {
        if (CurrentMode == Mode.WeaponPOV || CurrentMode == Mode.Targeting)
        {
            ReleaseMouse();
        }

        if (!_isActive)
        {
            Activate();
        }

        CurrentMode = Mode.FreeLook;
        _povWeapon = null;
        _povAiming = null;
        _targetFov = DefaultFov;

        _freeLookYaw = yaw;
        _freeLookPitch = pitch;
        FreeLookDistance = distance;
        if (pivot.HasValue)
        {
            _freeLookPivot = pivot.Value;
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Bird's-eye view looking straight down at the arena.</summary>
    public void PresetTopDown()
    {
        ApplyPreset(0f, -1.5f, 40f, new Vector3(0f, 4f, 0f));
    }

    /// <summary>Front view looking at the enemy fortress from the player's side.</summary>
    public void PresetFront()
    {
        ApplyPreset(0f, -0.35f, 30f, new Vector3(0f, 6f, 0f));
    }

    /// <summary>Side view (90 degrees from front) showing both fortresses in profile.</summary>
    public void PresetSide()
    {
        ApplyPreset(Mathf.Pi * 0.5f, -0.35f, 30f, new Vector3(0f, 6f, 0f));
    }

    /// <summary>Dramatic low-angle camera looking up at the fortress.</summary>
    public void PresetLowAngle()
    {
        ApplyPreset(0.4f, -0.12f, 18f, new Vector3(0f, 2f, 0f));
    }

    /// <summary>Release to current position in free-look (unlocked orbit).</summary>
    public void PresetFreeLook()
    {
        FreeLook();
    }

    /// <summary>
    /// Positions the camera behind a player's zone, facing toward the arena center.
    /// Uses the free-look orbit system with computed yaw so the camera is always
    /// behind the zone looking inward, matching the build-phase camera logic.
    /// </summary>
    /// <param name="zonePivot">Center of the player's build zone at a comfortable view height.</param>
    /// <param name="arenaCenter">Center of the arena (used to compute the "behind" direction).</param>
    /// <param name="distance">Orbit distance from pivot (default 30).</param>
    /// <param name="pitch">Orbit pitch angle (default -0.45 for a comfortable elevated angle).</param>
    public void PositionBehindZone(Vector3 zonePivot, Vector3 arenaCenter, float distance = 30f, float pitch = -0.45f)
    {
        // Compute the direction from arena center to zone center (XZ plane).
        // The camera should be on the far side of the zone from center (behind the zone).
        Vector3 awayFromCenter = new Vector3(zonePivot.X - arenaCenter.X, 0f, zonePivot.Z - arenaCenter.Z);
        if (awayFromCenter.LengthSquared() < 0.01f)
        {
            awayFromCenter = new Vector3(0f, 0f, 1f); // fallback
        }
        awayFromCenter = awayFromCenter.Normalized();

        // The yaw for the orbit system: the orbit offset is computed as
        //   (sin(yaw)*cos(pitch), -sin(pitch), cos(yaw)*cos(pitch)) * distance
        // We want the offset direction to match awayFromCenter, so:
        //   sin(yaw) = awayFromCenter.X,  cos(yaw) = awayFromCenter.Z
        float yaw = Mathf.Atan2(awayFromCenter.X, awayFromCenter.Z);

        ApplyPreset(yaw, pitch, distance, zonePivot);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void RestoreTimeScale()
    {
        if (Engine.TimeScale < 0.99f)
        {
            Engine.TimeScale = 1f;
        }
    }
}
