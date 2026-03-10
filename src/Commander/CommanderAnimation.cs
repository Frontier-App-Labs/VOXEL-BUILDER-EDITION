using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Commander;

public enum CommanderAnimationState
{
    Idle,
    Flinch,
    Panic,
    Falling,
    Dead,
}

/// <summary>
/// Code-driven animation controller for the Commander's skeleton hierarchy.
/// Drives breathing, head turns, flinch reactions, and panic trembling
/// entirely through _Process transforms on skeleton joints - no AnimationPlayer needed.
///
/// Works with the VoxelCharacterBuilder skeleton: Hips, Spine, Neck,
/// LeftShoulder, LeftElbow, RightShoulder, RightElbow,
/// LeftHip, LeftKnee, RightHip, RightKnee.
/// </summary>
public partial class CommanderAnimation : Node
{
    // Joint references (populated from the skeleton hierarchy)
    private Node3D? _hips;
    private Node3D? _spine;
    private Node3D? _neck;
    private Node3D? _leftShoulder;
    private Node3D? _rightShoulder;
    private Node3D? _leftElbow;
    private Node3D? _rightElbow;
    private Node3D? _leftHip;
    private Node3D? _rightHip;
    private Node3D? _leftKnee;
    private Node3D? _rightKnee;

    // Rest transforms stored when joints are found
    private readonly Dictionary<Node3D, Vector3> _restPositions = new();
    private readonly Dictionary<Node3D, Vector3> _restRotations = new();

    // Timing accumulators
    private float _breathTime;
    private float _headTime;
    private float _stateTime;

    // Head look
    private float _headLookAngle;
    private float _headLookTarget;
    private float _nextHeadLookChange;

    // Flinch state
    private float _flinchDecay;
    private Vector3 _flinchRotation;

    // Panic jitter
    private float _jitterSeed;

    public CommanderAnimationState CurrentState { get; private set; } = CommanderAnimationState.Idle;

    /// <summary>
    /// Initialize from a VoxelCharacterBuilder skeleton hierarchy.
    /// Scans for named joints and stores their rest transforms.
    /// </summary>
    public void Initialize(Node3D characterRoot)
    {
        _hips = characterRoot.GetNodeOrNull<Node3D>("Hips");
        _spine = _hips?.GetNodeOrNull<Node3D>("Spine");
        _neck = _spine?.GetNodeOrNull<Node3D>("Neck");
        _leftShoulder = _spine?.GetNodeOrNull<Node3D>("LeftShoulder");
        _rightShoulder = _spine?.GetNodeOrNull<Node3D>("RightShoulder");
        _leftElbow = _leftShoulder?.GetNodeOrNull<Node3D>("LeftElbow");
        _rightElbow = _rightShoulder?.GetNodeOrNull<Node3D>("RightElbow");
        _leftHip = _hips?.GetNodeOrNull<Node3D>("LeftHip");
        _rightHip = _hips?.GetNodeOrNull<Node3D>("RightHip");
        _leftKnee = _leftHip?.GetNodeOrNull<Node3D>("LeftKnee");
        _rightKnee = _rightHip?.GetNodeOrNull<Node3D>("RightKnee");

        // Store rest transforms
        StoreRest(_hips);
        StoreRest(_spine);
        StoreRest(_neck);
        StoreRest(_leftShoulder);
        StoreRest(_rightShoulder);
        StoreRest(_leftElbow);
        StoreRest(_rightElbow);
        StoreRest(_leftHip);
        StoreRest(_rightHip);
        StoreRest(_leftKnee);
        StoreRest(_rightKnee);

        _jitterSeed = GD.Randf() * 100f;
        _nextHeadLookChange = GD.Randf() * 3f + 1f;
    }

    private void StoreRest(Node3D? node)
    {
        if (node == null) return;
        _restPositions[node] = node.Position;
        _restRotations[node] = node.Rotation;
    }

    public void SetState(CommanderAnimationState state)
    {
        if (CurrentState == CommanderAnimationState.Dead)
        {
            return; // Once dead, no state changes
        }

        CurrentState = state;
        _stateTime = 0f;

        if (state == CommanderAnimationState.Flinch)
        {
            // Trigger flinch impulse - a sharp rotation jerk
            _flinchRotation = new Vector3(
                (GD.Randf() - 0.5f) * 0.3f,
                (GD.Randf() - 0.5f) * 0.2f,
                (GD.Randf() - 0.5f) * 0.3f
            );
            _flinchDecay = 1.0f;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        switch (CurrentState)
        {
            case CommanderAnimationState.Idle:
                AnimateIdle(dt, 1.0f);
                break;
            case CommanderAnimationState.Flinch:
                AnimateFlinch(dt);
                break;
            case CommanderAnimationState.Panic:
                AnimatePanic(dt);
                break;
            case CommanderAnimationState.Falling:
                AnimateFalling(dt);
                break;
            case CommanderAnimationState.Dead:
                // No animation - ragdoll handles everything
                break;
        }
    }

    /// <summary>
    /// Idle animation: gentle breathing, occasional head turns, slight arm sway.
    /// </summary>
    private void AnimateIdle(float dt, float speedMultiplier)
    {
        _breathTime += dt * speedMultiplier;
        _headTime += dt * speedMultiplier;
        _stateTime += dt;

        // -- Breathing: hip bob --
        float breathOffset = Mathf.Sin(_breathTime * 2.5f) * 0.008f;
        SetPositionOffset(_hips, new Vector3(0, breathOffset, 0));

        // -- Spine breathing lean --
        SetRotation(_spine, new Vector3(
            Mathf.Sin(_breathTime * 2.5f) * 0.03f,
            0,
            Mathf.Sin(_breathTime * 0.7f) * 0.02f
        ));

        // -- Head turn: periodic random Y rotation with slight tilt --
        if (_headTime >= _nextHeadLookChange)
        {
            _headTime = 0;
            _headLookTarget = (GD.Randf() - 0.5f) * 0.25f; // ±14 degrees max
            _nextHeadLookChange = GD.Randf() * 3f + 2f;
        }
        _headLookAngle = Mathf.Lerp(_headLookAngle, _headLookTarget, dt * 1.5f * speedMultiplier);

        SetRotation(_neck, new Vector3(
            Mathf.Sin(_breathTime * 1.3f) * 0.04f,
            _headLookAngle,
            _headLookAngle * -0.15f
        ));

        // -- Arm sway: alternating like a subtle march --
        float armSwayZ = Mathf.Sin(_breathTime * 1.8f) * 0.06f;
        float armSwayX = Mathf.Sin(_breathTime * 1.2f) * 0.04f;
        SetRotation(_leftShoulder, new Vector3(armSwayX, 0, armSwayZ));
        SetRotation(_rightShoulder, new Vector3(-armSwayX, 0, -armSwayZ));

        // Elbows slightly bent at rest
        SetRotation(_leftElbow, new Vector3(-Deg(10), 0, 0));
        SetRotation(_rightElbow, new Vector3(-Deg(10), 0, 0));

        // -- Leg micro-shift: weight shifting side to side --
        float weightShift = Mathf.Sin(_breathTime * 0.6f) * 0.006f;
        SetPositionOffset(_leftHip, new Vector3(weightShift, 0, 0));
        SetRotation(_leftHip, new Vector3(Mathf.Sin(_breathTime * 0.8f) * 0.02f, 0, 0));

        SetPositionOffset(_rightHip, new Vector3(-weightShift, 0, 0));
        SetRotation(_rightHip, new Vector3(-Mathf.Sin(_breathTime * 0.8f) * 0.02f, 0, 0));

        // Knees at rest
        SetRotation(_leftKnee, Vector3.Zero);
        SetRotation(_rightKnee, Vector3.Zero);
    }

    /// <summary>
    /// Flinch: quick jerk that decays back to idle.
    /// </summary>
    private void AnimateFlinch(float dt)
    {
        _stateTime += dt;
        _breathTime += dt; // keep breathing timer going for smooth transition back
        _flinchDecay = Mathf.Max(0f, _flinchDecay - dt * 4f);

        // Blend between flinch and idle breathing based on decay
        float breathOffset = Mathf.Sin(_breathTime * 2.5f) * 0.008f;

        // Hips: flinch drop blended with breathing bob
        SetPositionOffset(_hips, new Vector3(0, -0.01f * _flinchDecay + breathOffset * (1f - _flinchDecay), 0));

        // Spine: flinch rotation blended with breathing lean
        Vector3 breathSpine = new Vector3(
            Mathf.Sin(_breathTime * 2.5f) * 0.03f,
            0,
            Mathf.Sin(_breathTime * 0.7f) * 0.02f
        );
        SetRotation(_spine, _flinchRotation * _flinchDecay + breathSpine * (1f - _flinchDecay));

        // Neck: flinch jolt blended with idle head look
        Vector3 flinchNeck = new Vector3(
            -_flinchDecay * 0.15f,
            _headLookAngle,
            _flinchDecay * (GD.Randf() - 0.5f) * 0.1f
        );
        Vector3 idleNeck = new Vector3(
            Mathf.Sin(_breathTime * 1.3f) * 0.04f,
            _headLookAngle,
            _headLookAngle * -0.15f
        );
        SetRotation(_neck, flinchNeck * _flinchDecay + idleNeck * (1f - _flinchDecay));

        // Arms: flinch flair blended with idle sway
        float armSwayZ = Mathf.Sin(_breathTime * 1.8f) * 0.06f * (1f - _flinchDecay);
        SetRotation(_leftShoulder, new Vector3(0, 0, _flinchDecay * 0.2f + armSwayZ));
        SetRotation(_rightShoulder, new Vector3(0, 0, -_flinchDecay * 0.2f - armSwayZ));
        SetRotation(_leftElbow, new Vector3(-Deg(10) - Deg(20) * _flinchDecay, 0, 0));
        SetRotation(_rightElbow, new Vector3(-Deg(10) - Deg(20) * _flinchDecay, 0, 0));

        // Legs at rest
        SetRotation(_leftHip, Vector3.Zero);
        SetRotation(_rightHip, Vector3.Zero);
        SetRotation(_leftKnee, Vector3.Zero);
        SetRotation(_rightKnee, Vector3.Zero);

        // Return to idle after flinch completes
        if (_flinchDecay <= 0.01f)
        {
            CurrentState = CommanderAnimationState.Idle;
        }
    }

    /// <summary>
    /// Panic: same as idle but 3x speed, rapid head turning, random jitter/trembling.
    /// The Commander knows they're exposed and is freaking out.
    /// </summary>
    private void AnimatePanic(float dt)
    {
        _stateTime += dt;
        _breathTime += dt * 1.8f; // slightly faster breathing (not 3x)
        _headTime += dt * 2.0f;
        _jitterSeed += dt * 6f; // slower jitter (was 15)

        // Override head with nervous looking (less frequent than before)
        if (_headTime >= _nextHeadLookChange)
        {
            _headTime = 0;
            _headLookTarget = (GD.Randf() - 0.5f) * 0.5f; // narrower range (was 0.8)
            _nextHeadLookChange = GD.Randf() * 1.0f + 0.5f; // less frequent (was 0.2-0.7)
        }
        _headLookAngle = Mathf.Lerp(_headLookAngle, _headLookTarget, dt * 3.0f);

        // Subtle nervous tremor (much reduced from before)
        float jitterX = Mathf.Sin(_jitterSeed * 17.3f) * 0.01f;
        float jitterZ = Mathf.Sin(_jitterSeed * 31.1f) * 0.01f;

        // Hips: slightly faster breathing bob
        float breathOffset = Mathf.Sin(_breathTime * 2.5f) * 0.008f;
        SetPositionOffset(_hips, new Vector3(0, breathOffset, 0));

        // Spine: breathing lean + subtle tremor
        SetRotation(_spine, new Vector3(
            Mathf.Sin(_breathTime * 2.5f) * 0.03f + jitterX,
            0,
            Mathf.Sin(_breathTime * 0.7f) * 0.02f + jitterZ
        ));

        // Neck: nervous head turns (reduced jitter)
        SetRotation(_neck, new Vector3(
            Mathf.Sin(_breathTime * 1.3f) * 0.04f + Mathf.Sin(_jitterSeed * 11f) * 0.01f,
            _headLookAngle,
            _headLookAngle * -0.15f + Mathf.Sin(_jitterSeed * 13f) * 0.01f
        ));

        // Arms: fast sway
        float armSwayZ = Mathf.Sin(_breathTime * 1.8f) * 0.06f;
        float armSwayX = Mathf.Sin(_breathTime * 1.2f) * 0.04f;
        SetRotation(_leftShoulder, new Vector3(armSwayX, 0, armSwayZ));
        SetRotation(_rightShoulder, new Vector3(-armSwayX, 0, -armSwayZ));
        SetRotation(_leftElbow, new Vector3(-Deg(10), 0, 0));
        SetRotation(_rightElbow, new Vector3(-Deg(10), 0, 0));

        // Legs: weight shifting
        float weightShift = Mathf.Sin(_breathTime * 0.6f) * 0.006f;
        SetPositionOffset(_leftHip, new Vector3(weightShift, 0, 0));
        SetRotation(_leftHip, new Vector3(Mathf.Sin(_breathTime * 0.8f) * 0.02f, 0, 0));
        SetPositionOffset(_rightHip, new Vector3(-weightShift, 0, 0));
        SetRotation(_rightHip, new Vector3(-Mathf.Sin(_breathTime * 0.8f) * 0.02f, 0, 0));
        SetRotation(_leftKnee, Vector3.Zero);
        SetRotation(_rightKnee, Vector3.Zero);
    }

    /// <summary>
    /// Falling: arms flailing upward, legs dangling, panicked head.
    /// </summary>
    private void AnimateFalling(float dt)
    {
        _stateTime += dt;
        _jitterSeed += dt * 20f;

        // Hips at rest
        SetPositionOffset(_hips, Vector3.Zero);
        SetRotation(_spine, Vector3.Zero);

        // Arms flail upward
        float flailAngle = Mathf.Sin(_stateTime * 12f) * 0.5f;
        SetRotation(_leftShoulder, new Vector3(flailAngle, 0, 0.3f + Mathf.Sin(_stateTime * 8f) * 0.15f));
        SetRotation(_rightShoulder, new Vector3(-flailAngle, 0, -0.3f + Mathf.Sin(_stateTime * 9f) * 0.15f));
        SetRotation(_leftElbow, new Vector3(-Deg(20), 0, 0));
        SetRotation(_rightElbow, new Vector3(-Deg(20), 0, 0));

        // Legs dangle
        float legDangle = Mathf.Sin(_stateTime * 6f) * 0.15f;
        SetRotation(_leftHip, new Vector3(legDangle, 0, 0));
        SetRotation(_rightHip, new Vector3(-legDangle, 0, 0));
        SetRotation(_leftKnee, new Vector3(Deg(15), 0, 0));
        SetRotation(_rightKnee, new Vector3(Deg(15), 0, 0));

        // Head looks down in panic
        SetRotation(_neck, new Vector3(
            0.2f + Mathf.Sin(_jitterSeed * 11f) * 0.05f,
            Mathf.Sin(_jitterSeed * 7f) * 0.3f,
            0
        ));
    }

    /// <summary>
    /// Reset all joints to their rest positions (used before ragdoll takes over).
    /// </summary>
    public void ResetToRest()
    {
        foreach (var kvp in _restPositions)
        {
            kvp.Key.Position = kvp.Value;
        }

        foreach (var kvp in _restRotations)
        {
            kvp.Key.Rotation = kvp.Value;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void SetRotation(Node3D? joint, Vector3 eulerRadians)
    {
        if (joint == null || !_restRotations.ContainsKey(joint)) return;
        joint.Rotation = _restRotations[joint] + eulerRadians;
    }

    private void SetPositionOffset(Node3D? joint, Vector3 offset)
    {
        if (joint == null || !_restPositions.ContainsKey(joint)) return;
        joint.Position = _restPositions[joint] + offset;
    }

    private static float Deg(float degrees) => Mathf.DegToRad(degrees);
}
