using Godot;
using System;
using System.Collections.Generic;

namespace VoxelSiege.Art;

/// <summary>
/// Procedural animation system for voxel characters built with VoxelCharacterBuilder.
/// Drives joint rotations and position offsets based on animation state.
/// Supports smooth blending between states.
/// </summary>
public partial class VoxelAnimator : Node
{
    public enum AnimState { Idle, Walk, Attack, Shoot, Flinch, Panic, Celebrate, Dead }

    private AnimState _state = AnimState.Idle;
    private float _stateTime;
    private float _blendAlpha = 1f;
    private float _walkSpeed = 1f;

    // Joint references (populated by scanning the character hierarchy)
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

    // Rest positions (stored when joints are found)
    private readonly Dictionary<Node3D, Vector3> _restPositions = new();
    private readonly Dictionary<Node3D, Vector3> _restRotations = new();

    /// <summary>Call after the character is added to the scene tree.</summary>
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
    }

    private void StoreRest(Node3D? node)
    {
        if (node == null) return;
        _restPositions[node] = node.Position;
        _restRotations[node] = node.Rotation;
    }

    public void SetState(AnimState state, float walkSpeed = 1f)
    {
        if (_state == state) return;
        _state = state;
        _stateTime = 0f;
        _blendAlpha = 0f;
        _walkSpeed = walkSpeed;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _stateTime += dt;
        _blendAlpha = Mathf.Min(_blendAlpha + dt * 5f, 1f); // blend in over 0.2s

        switch (_state)
        {
            case AnimState.Idle: AnimateIdle(dt); break;
            case AnimState.Walk: AnimateWalk(dt); break;
            case AnimState.Attack: AnimateAttack(dt); break;
            case AnimState.Shoot: AnimateShoot(dt); break;
            case AnimState.Flinch: AnimateFlinch(dt); break;
            case AnimState.Panic: AnimatePanic(dt); break;
            case AnimState.Celebrate: AnimateCelebrate(dt); break;
            case AnimState.Dead: break; // ragdoll takes over
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  IDLE - gentle breathing, subtle sway
    // ═══════════════════════════════════════════════════════════════

    private void AnimateIdle(float dt)
    {
        float t = _stateTime;

        // Breathing bob
        SetPositionOffset(_hips, new Vector3(0, Mathf.Sin(t * 2.5f) * 0.005f, 0));

        // Spine breathing
        SetRotation(_spine, new Vector3(Mathf.Sin(t * 1.5f) * Deg(2), 0, Mathf.Sin(t * 0.7f) * Deg(1)));

        // Subtle arm sway
        SetRotation(_leftShoulder, new Vector3(0, 0, Mathf.Sin(t * 1.0f) * Deg(3)));
        SetRotation(_rightShoulder, new Vector3(0, 0, -Mathf.Sin(t * 1.0f) * Deg(3)));
        SetRotation(_leftElbow, new Vector3(Deg(10), 0, 0));
        SetRotation(_rightElbow, new Vector3(Deg(10), 0, 0));

        // Head look (slow random-ish via sin with irrational frequency)
        SetRotation(_neck, new Vector3(0, Mathf.Sin(t * 0.37f) * Deg(25), 0));

        // Legs at rest
        SetRotation(_leftHip, Vector3.Zero);
        SetRotation(_rightHip, Vector3.Zero);
        SetRotation(_leftKnee, Vector3.Zero);
        SetRotation(_rightKnee, Vector3.Zero);
    }

    // ═══════════════════════════════════════════════════════════════
    //  WALK - full stride cycle with arm swing
    // ═══════════════════════════════════════════════════════════════

    private void AnimateWalk(float dt)
    {
        float t = _stateTime * _walkSpeed;
        float cycle = t * Mathf.Tau; // one full cycle per second at speed=1

        // Hip bob
        float bob = Mathf.Abs(Mathf.Sin(cycle)) * 0.02f;
        SetPositionOffset(_hips, new Vector3(0, -bob, 0));

        // Spine lean
        float sway = Mathf.Sin(cycle) * Deg(2);
        SetRotation(_spine, new Vector3(Deg(-5), 0, sway));

        // Leg swing (opposite phases)
        float legSwing = Deg(30);
        SetRotation(_leftHip, new Vector3(Mathf.Sin(cycle) * legSwing, 0, 0));
        SetRotation(_rightHip, new Vector3(-Mathf.Sin(cycle) * legSwing, 0, 0));

        // Knee bend (only bends backward, so clamp to positive)
        float leftKneeBend = Mathf.Max(0, -Mathf.Sin(cycle) * Deg(40) + Deg(10));
        float rightKneeBend = Mathf.Max(0, Mathf.Sin(cycle) * Deg(40) + Deg(10));
        SetRotation(_leftKnee, new Vector3(leftKneeBend, 0, 0));
        SetRotation(_rightKnee, new Vector3(rightKneeBend, 0, 0));

        // Arm swing (opposite to legs)
        float armSwing = Deg(25);
        SetRotation(_leftShoulder, new Vector3(-Mathf.Sin(cycle) * armSwing, 0, 0));
        SetRotation(_rightShoulder, new Vector3(Mathf.Sin(cycle) * armSwing, 0, 0));

        // Elbow bend during swing
        float leftElbowBend = Deg(15) + Mathf.Max(0, Mathf.Sin(cycle)) * Deg(25);
        float rightElbowBend = Deg(15) + Mathf.Max(0, -Mathf.Sin(cycle)) * Deg(25);
        SetRotation(_leftElbow, new Vector3(leftElbowBend, 0, 0));
        SetRotation(_rightElbow, new Vector3(rightElbowBend, 0, 0));

        // Head stays mostly forward with slight bounce
        SetRotation(_neck, new Vector3(Mathf.Sin(cycle * 2) * Deg(3), 0, 0));
    }

    // ═══════════════════════════════════════════════════════════════
    //  ATTACK - right arm punch
    // ═══════════════════════════════════════════════════════════════

    private void AnimateAttack(float dt)
    {
        float t = Mathf.Min(_stateTime / 0.4f, 1f); // 0.4s duration

        // Wind up → thrust → recoil
        float shoulderX;
        float elbowX;
        float spineX;
        if (t < 0.25f)
        {
            // Wind up
            float p = t / 0.25f;
            shoulderX = Mathf.Lerp(0, Deg(-60), p);
            elbowX = Mathf.Lerp(Deg(10), Deg(30), p);
            spineX = Mathf.Lerp(0, Deg(5), p);
        }
        else if (t < 0.5f)
        {
            // Thrust
            float p = (t - 0.25f) / 0.25f;
            shoulderX = Mathf.Lerp(Deg(-60), Deg(-90), p);
            elbowX = Mathf.Lerp(Deg(30), Deg(5), p);
            spineX = Mathf.Lerp(Deg(5), Deg(-8), p);
        }
        else
        {
            // Recoil and return
            float p = (t - 0.5f) / 0.5f;
            shoulderX = Mathf.Lerp(Deg(-90), 0, p * p);
            elbowX = Mathf.Lerp(Deg(5), Deg(10), p);
            spineX = Mathf.Lerp(Deg(-8), 0, p);
        }

        SetRotation(_rightShoulder, new Vector3(shoulderX, 0, 0));
        SetRotation(_rightElbow, new Vector3(elbowX, 0, 0));
        SetRotation(_spine, new Vector3(spineX, 0, 0));

        // Left arm stays down
        SetRotation(_leftShoulder, new Vector3(0, 0, Deg(5)));
        SetRotation(_leftElbow, new Vector3(Deg(15), 0, 0));

        // Return to idle after completion
        if (t >= 1f) SetState(AnimState.Idle);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SHOOT - arm extended forward pointing gun, recoil kick
    // ═══════════════════════════════════════════════════════════════

    private void AnimateShoot(float dt)
    {
        float t = Mathf.Min(_stateTime / 0.5f, 1f); // 0.5s total

        float shoulderX;
        float elbowX;
        float spineX;
        if (t < 0.15f)
        {
            // Raise arm to aiming pose
            float p = t / 0.15f;
            shoulderX = Mathf.Lerp(0, Deg(-75), p);
            elbowX = Mathf.Lerp(Deg(10), Deg(0), p);
            spineX = Mathf.Lerp(0, Deg(-3), p);
        }
        else if (t < 0.25f)
        {
            // Brief hold on target before firing
            shoulderX = Deg(-75);
            elbowX = Deg(0);
            spineX = Deg(-3);
        }
        else if (t < 0.35f)
        {
            // FIRE — sharp recoil kick: arm jerks up, spine rocks back
            float p = (t - 0.25f) / 0.1f; // fast 0→1 over 10% of anim
            float kick = Mathf.Sin(p * Mathf.Pi); // spike up then back down
            shoulderX = Deg(-75) + kick * Deg(25); // arm kicks up hard
            elbowX = kick * Deg(12); // elbow flexes from recoil
            spineX = Deg(-3) - kick * Deg(6); // spine rocks backward
        }
        else if (t < 0.50f)
        {
            // Recoil settle — arm drifts back to aim
            float p = (t - 0.35f) / 0.15f;
            shoulderX = Mathf.Lerp(Deg(-75) + Deg(8), Deg(-75), p); // slight overshoot
            elbowX = Mathf.Lerp(Deg(5), Deg(0), p);
            spineX = Mathf.Lerp(Deg(-5), Deg(-3), p);
        }
        else
        {
            // Return to idle
            float p = (t - 0.50f) / 0.50f;
            float ease = p * p;
            shoulderX = Mathf.Lerp(Deg(-75), 0, ease);
            elbowX = Mathf.Lerp(Deg(0), Deg(10), ease);
            spineX = Mathf.Lerp(Deg(-3), 0, ease);
        }

        SetRotation(_rightShoulder, new Vector3(shoulderX, 0, 0));
        SetRotation(_rightElbow, new Vector3(elbowX, 0, 0));
        SetRotation(_spine, new Vector3(spineX, 0, 0));

        // Left arm relaxed at side
        SetRotation(_leftShoulder, new Vector3(0, 0, Deg(5)));
        SetRotation(_leftElbow, new Vector3(Deg(15), 0, 0));

        if (t >= 1f) SetState(AnimState.Idle);
    }

    // ═══════════════════════════════════════════════════════════════
    //  FLINCH - sharp jerk, quick recovery
    // ═══════════════════════════════════════════════════════════════

    private void AnimateFlinch(float dt)
    {
        float t = Mathf.Min(_stateTime / 0.25f, 1f);
        float decay = 1f - t * t; // quadratic falloff

        SetPositionOffset(_hips, new Vector3(0, -0.01f * decay, 0));
        SetRotation(_spine, new Vector3(Deg(15) * decay, 0, 0));
        SetRotation(_neck, new Vector3(Deg(-20) * decay, 0, 0));
        SetRotation(_leftShoulder, new Vector3(0, 0, Deg(15) * decay));
        SetRotation(_rightShoulder, new Vector3(0, 0, Deg(-15) * decay));
        SetRotation(_leftElbow, new Vector3(Deg(30) * decay, 0, 0));
        SetRotation(_rightElbow, new Vector3(Deg(30) * decay, 0, 0));

        if (t >= 1f) SetState(AnimState.Idle);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PANIC - fast jittery idle
    // ═══════════════════════════════════════════════════════════════

    private void AnimatePanic(float dt)
    {
        float t = _stateTime * 3f; // 3x speed

        SetPositionOffset(_hips, new Vector3(0, Mathf.Sin(t * 8f) * 0.008f, 0));
        SetRotation(_spine, new Vector3(Mathf.Sin(t * 4f) * Deg(5), 0, Mathf.Sin(t * 3f) * Deg(3)));
        SetRotation(_neck, new Vector3(0, Mathf.Sin(t * 2.3f) * Deg(40), Mathf.Sin(t * 5.7f) * Deg(5)));
        SetRotation(_leftShoulder, new Vector3(Mathf.Sin(t * 3.5f) * Deg(15), 0, Deg(-10)));
        SetRotation(_rightShoulder, new Vector3(-Mathf.Sin(t * 3.5f) * Deg(15), 0, Deg(10)));
        SetRotation(_leftElbow, new Vector3(Deg(30) + Mathf.Sin(t * 5f) * Deg(15), 0, 0));
        SetRotation(_rightElbow, new Vector3(Deg(30) - Mathf.Sin(t * 5f) * Deg(15), 0, 0));
    }

    // ═══════════════════════════════════════════════════════════════
    //  CELEBRATE - arm pumps and bounce
    // ═══════════════════════════════════════════════════════════════

    private void AnimateCelebrate(float dt)
    {
        float t = _stateTime;

        float bounce = Mathf.Abs(Mathf.Sin(t * 8f)) * 0.03f;
        SetPositionOffset(_hips, new Vector3(0, bounce, 0));

        SetRotation(_spine, new Vector3(Deg(-5), 0, 0));
        SetRotation(_neck, new Vector3(Deg(-15), 0, 0));

        // Alternating arm pumps
        float leftPump = Mathf.Max(0, Mathf.Sin(t * 6f));
        float rightPump = Mathf.Max(0, -Mathf.Sin(t * 6f));
        SetRotation(_leftShoulder, new Vector3(0, 0, -Deg(90) * leftPump));
        SetRotation(_rightShoulder, new Vector3(0, 0, Deg(90) * rightPump));
        SetRotation(_leftElbow, new Vector3(Deg(20), 0, 0));
        SetRotation(_rightElbow, new Vector3(Deg(20), 0, 0));
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void SetRotation(Node3D? joint, Vector3 eulerRadians)
    {
        if (joint == null || !_restRotations.ContainsKey(joint)) return;
        joint.Rotation = _restRotations[joint] + eulerRadians * _blendAlpha;
    }

    private void SetPositionOffset(Node3D? joint, Vector3 offset)
    {
        if (joint == null || !_restPositions.ContainsKey(joint)) return;
        joint.Position = _restPositions[joint] + offset * _blendAlpha;
    }

    private static float Deg(float degrees) => Mathf.DegToRad(degrees);
}
