using Godot;

namespace VoxelSiege.Commander;

public enum CommanderAnimationState
{
    Idle,
    Flinch,
    Panic,
    Dead,
}

/// <summary>
/// Code-driven animation controller for the Commander's body parts.
/// Drives breathing, head turns, flinch reactions, and panic trembling
/// entirely through _Process transforms - no AnimationPlayer needed.
/// </summary>
public partial class CommanderAnimation : Node
{
    // Body part references (set by Commander after model generation)
    private MeshInstance3D? _head;
    private MeshInstance3D? _torso;
    private MeshInstance3D? _leftArm;
    private MeshInstance3D? _rightArm;
    private MeshInstance3D? _leftLeg;
    private MeshInstance3D? _rightLeg;

    // Base positions stored from the model generator
    private Vector3 _headBase;
    private Vector3 _torsoBase;
    private Vector3 _leftArmBase;
    private Vector3 _rightArmBase;
    private Vector3 _leftLegBase;
    private Vector3 _rightLegBase;

    // Timing accumulators
    private float _breathTime;
    private float _headTime;
    private float _stateTime;

    // Head look
    private float _headLookAngle;
    private float _headLookTarget;
    private float _nextHeadLookChange;

    // Flinch state
    private Vector3 _flinchOffset;
    private float _flinchDecay;

    // Panic jitter
    private float _jitterSeed;

    public CommanderAnimationState CurrentState { get; private set; } = CommanderAnimationState.Idle;

    /// <summary>
    /// Assign the body part mesh references and their rest positions.
    /// Must be called after the model is generated.
    /// </summary>
    public void SetBodyParts(
        MeshInstance3D? head, MeshInstance3D? torso,
        MeshInstance3D? leftArm, MeshInstance3D? rightArm,
        MeshInstance3D? leftLeg, MeshInstance3D? rightLeg,
        Vector3 headBase, Vector3 torsoBase,
        Vector3 leftArmBase, Vector3 rightArmBase,
        Vector3 leftLegBase, Vector3 rightLegBase)
    {
        _head = head;
        _torso = torso;
        _leftArm = leftArm;
        _rightArm = rightArm;
        _leftLeg = leftLeg;
        _rightLeg = rightLeg;

        _headBase = headBase;
        _torsoBase = torsoBase;
        _leftArmBase = leftArmBase;
        _rightArmBase = rightArmBase;
        _leftLegBase = leftLegBase;
        _rightLegBase = rightLegBase;

        _jitterSeed = GD.Randf() * 100f;
        _nextHeadLookChange = GD.Randf() * 3f + 1f;
    }

    public void SetState(CommanderAnimationState state)
    {
        if (CurrentState == CommanderAnimationState.Dead)
        {
            return; // Once dead, no state changes
        }

        CommanderAnimationState previous = CurrentState;
        CurrentState = state;
        _stateTime = 0f;

        if (state == CommanderAnimationState.Flinch)
        {
            // Trigger flinch impulse - a sharp backward jerk
            _flinchOffset = new Vector3(
                (GD.Randf() - 0.5f) * 0.02f,
                0.01f,
                (GD.Randf() - 0.5f) * 0.02f
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

        // -- Breathing: sinusoidal Y oscillation --
        float breathOffset = Mathf.Sin(_breathTime * 2.5f) * 0.003f;

        if (_torso != null)
        {
            _torso.Position = new Vector3(_torsoBase.X, _torsoBase.Y + breathOffset, _torsoBase.Z);
        }

        if (_head != null)
        {
            _head.Position = new Vector3(
                _headBase.X,
                _headBase.Y + breathOffset * 1.5f,
                _headBase.Z
            );
        }

        // -- Head turn: periodic random Y rotation --
        if (_headTime >= _nextHeadLookChange)
        {
            _headTime = 0;
            _headLookTarget = (GD.Randf() - 0.5f) * 0.4f;
            _nextHeadLookChange = GD.Randf() * 4f + 2f;
        }

        _headLookAngle = Mathf.Lerp(_headLookAngle, _headLookTarget, dt * 2f * speedMultiplier);
        if (_head != null)
        {
            _head.Rotation = new Vector3(0, _headLookAngle, 0);
        }

        // -- Arm sway --
        float armSway = Mathf.Sin(_breathTime * 1.8f) * 0.02f;
        if (_leftArm != null)
        {
            _leftArm.Rotation = new Vector3(0, 0, armSway);
        }

        if (_rightArm != null)
        {
            _rightArm.Rotation = new Vector3(0, 0, -armSway);
        }

        // -- Slight body sway --
        float bodySway = Mathf.Sin(_breathTime * 0.7f) * 0.002f;
        if (_torso != null)
        {
            Vector3 pos = _torso.Position;
            _torso.Position = new Vector3(pos.X + bodySway, pos.Y, pos.Z);
        }
    }

    /// <summary>
    /// Flinch: quick jerk that decays back to idle.
    /// </summary>
    private void AnimateFlinch(float dt)
    {
        _stateTime += dt;
        _flinchDecay = Mathf.Max(0f, _flinchDecay - dt * 4f);

        // Apply decaying flinch offset to all body parts
        Vector3 currentFlinch = _flinchOffset * _flinchDecay;

        if (_torso != null)
        {
            _torso.Position = _torsoBase + currentFlinch;
        }

        if (_head != null)
        {
            _head.Position = _headBase + currentFlinch * 1.5f;
            // Head snaps back with a slight rotation
            _head.Rotation = new Vector3(
                -_flinchDecay * 0.15f,
                _headLookAngle,
                _flinchDecay * (GD.Randf() - 0.5f) * 0.1f
            );
        }

        if (_leftArm != null)
        {
            _leftArm.Position = _leftArmBase + currentFlinch;
            _leftArm.Rotation = new Vector3(0, 0, _flinchDecay * 0.2f);
        }

        if (_rightArm != null)
        {
            _rightArm.Position = _rightArmBase + currentFlinch;
            _rightArm.Rotation = new Vector3(0, 0, -_flinchDecay * 0.2f);
        }

        // Continue idle breathing underneath
        AnimateIdle(dt, 1.0f);

        // Return to idle (or panic) after flinch completes
        if (_flinchDecay <= 0.01f)
        {
            // Don't call SetState to avoid resetting flinch - just change directly
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
        _jitterSeed += dt * 15f;

        // Run idle at 3x speed for fast breathing
        AnimateIdle(dt, 3.0f);

        // Override head with rapid, nervous looking
        if (_headTime >= _nextHeadLookChange)
        {
            _headTime = 0;
            _headLookTarget = (GD.Randf() - 0.5f) * 0.8f; // Wider range
            _nextHeadLookChange = GD.Randf() * 0.5f + 0.2f; // Much more frequent
        }

        // Add trembling jitter to everything
        float jitterX = Mathf.Sin(_jitterSeed * 17.3f) * 0.002f;
        float jitterY = Mathf.Sin(_jitterSeed * 23.7f) * 0.001f;
        float jitterZ = Mathf.Sin(_jitterSeed * 31.1f) * 0.002f;
        Vector3 jitter = new Vector3(jitterX, jitterY, jitterZ);

        if (_torso != null)
        {
            _torso.Position += jitter;
        }

        if (_head != null)
        {
            _head.Position += jitter * 1.5f;
            // Add nervous head tilt
            Vector3 rot = _head.Rotation;
            _head.Rotation = new Vector3(
                rot.X + Mathf.Sin(_jitterSeed * 11f) * 0.03f,
                rot.Y,
                Mathf.Sin(_jitterSeed * 13f) * 0.04f
            );
        }

        if (_leftArm != null)
        {
            _leftArm.Position += jitter;
        }

        if (_rightArm != null)
        {
            _rightArm.Position += jitter;
        }

        if (_leftLeg != null)
        {
            _leftLeg.Position += jitter * 0.5f;
        }

        if (_rightLeg != null)
        {
            _rightLeg.Position += jitter * 0.5f;
        }
    }

    /// <summary>
    /// Reset all body parts to their rest positions (used before ragdoll takes over).
    /// </summary>
    public void ResetToRest()
    {
        if (_head != null)
        {
            _head.Position = _headBase;
            _head.Rotation = Vector3.Zero;
        }

        if (_torso != null)
        {
            _torso.Position = _torsoBase;
            _torso.Rotation = Vector3.Zero;
        }

        if (_leftArm != null)
        {
            _leftArm.Position = _leftArmBase;
            _leftArm.Rotation = Vector3.Zero;
        }

        if (_rightArm != null)
        {
            _rightArm.Position = _rightArmBase;
            _rightArm.Rotation = Vector3.Zero;
        }

        if (_leftLeg != null)
        {
            _leftLeg.Position = _leftLegBase;
            _leftLeg.Rotation = Vector3.Zero;
        }

        if (_rightLeg != null)
        {
            _rightLeg.Position = _rightLegBase;
            _rightLeg.Rotation = Vector3.Zero;
        }
    }
}
