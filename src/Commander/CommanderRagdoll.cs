using Godot;
using System.Collections.Generic;
using VoxelSiege.Art;

namespace VoxelSiege.Commander;

/// <summary>
/// Ragdoll body part descriptor used during setup (legacy flat model).
/// </summary>
public struct RagdollPartInfo
{
    public string Name;
    public MeshInstance3D SourceMesh;
    public CommanderBodyPartRegion Region;
    public float Mass;
}

/// <summary>
/// Ragdoll body part descriptor for skeleton-based models.
/// Uses the joint's global position for placement instead of region offsets.
/// </summary>
public struct RagdollSkeletonPart
{
    public string Name;
    public MeshInstance3D? SourceMesh;
    public Node3D? Joint;
    public float Mass;
}

/// <summary>
/// Spectacular ragdoll death system for the Commander.
/// Takes the 6 body part meshes and converts them into physics-driven RigidBody3D
/// nodes connected by joints. The result is a hilarious, satisfying ragdoll
/// that bounces off walls, tumbles through holes, and persists in the world.
/// </summary>
public partial class CommanderRagdoll : Node3D
{
    private const float VoxelSize = 0.08f;
    private const float SettledVelocityThreshold = 0.15f;
    private const float SettledAngularThreshold = 0.3f;
    private const float MinSettledTime = 1.5f;

    // Mass distribution - torso is the anchor, extremities are lighter
    private const float TorsoMass = 3.0f;
    private const float HeadMass = 1.0f;
    private const float ArmMass = 0.8f;
    private const float LegMass = 1.5f;

    // Physics tweaks
    private const float BounceFactor = 0.3f;
    private const float FrictionFactor = 0.7f;
    private const float RandomSpinMax = 6f;
    private const float DeathImpulseMultiplier = 1.0f;

    private readonly List<RigidBody3D> _bodies = new();
    private float _settledTimer;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Activate the ragdoll: create RigidBody3D for each body part,
    /// connect with joints, and launch with a spectacular impulse.
    /// </summary>
    /// <param name="bodyParts">The generated body part data (meshes and regions).</param>
    /// <param name="commanderGlobalTransform">The Commander's world transform at death.</param>
    /// <param name="impulseDirection">Direction the killing blow came from.</param>
    /// <param name="impulseForce">Magnitude of the death impulse.</param>
    /// <param name="sourceMeshes">The 6 MeshInstance3D body part nodes to steal meshes from.</param>
    public void Activate(
        CommanderBodyParts bodyParts,
        Transform3D commanderGlobalTransform,
        Vector3 impulseDirection,
        float impulseForce,
        MeshInstance3D?[] sourceMeshes)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _settledTimer = 0f;
        GlobalTransform = commanderGlobalTransform;

        // Build part descriptors
        RagdollPartInfo[] parts = new RagdollPartInfo[]
        {
            new() { Name = "Head", SourceMesh = sourceMeshes[0]!, Region = bodyParts.HeadRegion, Mass = HeadMass },
            new() { Name = "Torso", SourceMesh = sourceMeshes[1]!, Region = bodyParts.TorsoRegion, Mass = TorsoMass },
            new() { Name = "LeftArm", SourceMesh = sourceMeshes[2]!, Region = bodyParts.LeftArmRegion, Mass = ArmMass },
            new() { Name = "RightArm", SourceMesh = sourceMeshes[3]!, Region = bodyParts.RightArmRegion, Mass = ArmMass },
            new() { Name = "LeftLeg", SourceMesh = sourceMeshes[4]!, Region = bodyParts.LeftLegRegion, Mass = LegMass },
            new() { Name = "RightLeg", SourceMesh = sourceMeshes[5]!, Region = bodyParts.RightLegRegion, Mass = LegMass },
        };

        // Create RigidBody3D for each part
        Dictionary<string, RigidBody3D> bodyMap = new();
        foreach (RagdollPartInfo part in parts)
        {
            RigidBody3D body = CreateBodyPart(part);
            bodyMap[part.Name] = body;
            _bodies.Add(body);
            AddChild(body);
        }

        // Connect parts with joints
        CreateNeckJoint(bodyMap["Head"], bodyMap["Torso"], bodyParts);
        CreateShoulderJoint(bodyMap["LeftArm"], bodyMap["Torso"], bodyParts, true);
        CreateShoulderJoint(bodyMap["RightArm"], bodyMap["Torso"], bodyParts, false);
        CreateHipJoint(bodyMap["LeftLeg"], bodyMap["Torso"], bodyParts, true);
        CreateHipJoint(bodyMap["RightLeg"], bodyMap["Torso"], bodyParts, false);

        // Apply death impulse - directional force away from the damage source
        Vector3 dir = impulseDirection.Normalized();

        // Clamp the upward component so the body doesn't shoot straight up.
        // Allow a small upward lift but keep the force mostly horizontal/directional.
        float maxUpComponent = 0.35f;
        if (dir.Y > maxUpComponent)
        {
            dir.Y = maxUpComponent;
            dir = dir.Normalized();
        }

        Vector3 mainImpulse = dir * impulseForce * DeathImpulseMultiplier;
        // Add a modest upward nudge so the body lifts off the ground slightly
        mainImpulse += Vector3.Up * impulseForce * 0.15f;

        foreach (RigidBody3D body in _bodies)
        {
            // Main directional impulse (not scaled by mass - heavier parts
            // should move less, not more)
            body.ApplyCentralImpulse(mainImpulse);

            // Random spin for tumbling - each part spins differently
            Vector3 randomTorque = new Vector3(
                (GD.Randf() - 0.5f) * RandomSpinMax,
                (GD.Randf() - 0.5f) * RandomSpinMax,
                (GD.Randf() - 0.5f) * RandomSpinMax
            );
            body.ApplyTorqueImpulse(randomTorque);

            // Small per-part variation so they separate naturally
            Vector3 scatter = new Vector3(
                (GD.Randf() - 0.5f) * impulseForce * 0.15f,
                (GD.Randf() - 0.5f) * impulseForce * 0.08f,
                (GD.Randf() - 0.5f) * impulseForce * 0.15f
            );
            body.ApplyCentralImpulse(scatter);
        }

        // Head gets a bit of extra kick for dramatic flair (but not absurd)
        if (bodyMap.TryGetValue("Head", out RigidBody3D? head))
        {
            head.ApplyCentralImpulse(mainImpulse * 0.3f + Vector3.Up * impulseForce * 0.1f);
            head.ApplyTorqueImpulse(new Vector3(
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f,
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f,
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f
            ));
        }

        Visible = true;
    }

    /// <summary>
    /// Activate the ragdoll from skeleton-based body parts.
    /// Each part's position is derived from the joint's global transform relative to the
    /// Commander's transform, instead of region center offsets.
    /// </summary>
    public void ActivateFromSkeleton(
        RagdollSkeletonPart[] parts,
        Transform3D commanderGlobalTransform,
        Vector3 impulseDirection,
        float impulseForce)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _settledTimer = 0f;
        GlobalTransform = commanderGlobalTransform;

        // Create RigidBody3D for each part
        Dictionary<string, RigidBody3D> bodyMap = new();
        foreach (RagdollSkeletonPart part in parts)
        {
            if (part.SourceMesh == null || part.Joint == null)
            {
                continue;
            }

            RigidBody3D body = CreateSkeletonBodyPart(part, commanderGlobalTransform);
            bodyMap[part.Name] = body;
            _bodies.Add(body);
            AddChild(body);
        }

        // Connect parts with joints (simple distance-based connections)
        bodyMap.TryGetValue("Torso", out RigidBody3D? torsoBody);

        if (torsoBody != null && bodyMap.TryGetValue("Head", out RigidBody3D? headBody))
        {
            CreateSimpleJoint("NeckJoint", headBody, torsoBody,
                (headBody.Position + torsoBody.Position) * 0.5f,
                Mathf.DegToRad(30f), Mathf.DegToRad(45f), Mathf.DegToRad(30f));
        }

        if (torsoBody != null && bodyMap.TryGetValue("LeftArm", out RigidBody3D? leftArmBody))
        {
            CreateSimpleJoint("LeftShoulderJoint", leftArmBody, torsoBody,
                (leftArmBody.Position + torsoBody.Position) * 0.5f,
                Mathf.DegToRad(90f), Mathf.DegToRad(90f), Mathf.DegToRad(45f));
        }

        if (torsoBody != null && bodyMap.TryGetValue("RightArm", out RigidBody3D? rightArmBody))
        {
            CreateSimpleJoint("RightShoulderJoint", rightArmBody, torsoBody,
                (rightArmBody.Position + torsoBody.Position) * 0.5f,
                Mathf.DegToRad(90f), Mathf.DegToRad(90f), Mathf.DegToRad(45f));
        }

        if (torsoBody != null && bodyMap.TryGetValue("LeftLeg", out RigidBody3D? leftLegBody))
        {
            CreateSimpleJoint("LeftHipJoint", leftLegBody, torsoBody,
                (leftLegBody.Position + torsoBody.Position) * 0.5f,
                Mathf.DegToRad(60f), Mathf.DegToRad(15f), Mathf.DegToRad(20f));
        }

        if (torsoBody != null && bodyMap.TryGetValue("RightLeg", out RigidBody3D? rightLegBody))
        {
            CreateSimpleJoint("RightHipJoint", rightLegBody, torsoBody,
                (rightLegBody.Position + torsoBody.Position) * 0.5f,
                Mathf.DegToRad(60f), Mathf.DegToRad(15f), Mathf.DegToRad(20f));
        }

        // Apply death impulse
        ApplyDeathImpulse(impulseDirection, impulseForce, bodyMap);

        Visible = true;
    }

    /// <summary>
    /// Create a ragdoll body part from a skeleton joint and its mesh child.
    /// </summary>
    private RigidBody3D CreateSkeletonBodyPart(RagdollSkeletonPart part, Transform3D commanderTransform)
    {
        RigidBody3D body = new();
        body.Name = $"Ragdoll_{part.Name}";
        body.Mass = part.Mass;
        body.GravityScale = 1.0f;
        body.ContinuousCd = true;
        body.ContactMonitor = true;
        body.MaxContactsReported = 4;

        PhysicsMaterial physicsMat = new();
        physicsMat.Bounce = BounceFactor;
        physicsMat.Friction = FrictionFactor;
        body.PhysicsMaterialOverride = physicsMat;

        body.LinearDamp = 0.3f;
        body.AngularDamp = 0.5f;

        // Position from the joint's global position relative to the commander
        Vector3 localPos = commanderTransform.AffineInverse() * part.Joint!.GlobalPosition;
        body.Position = localPos;

        // Clone the mesh
        MeshInstance3D meshInstance = new();
        meshInstance.Name = "Mesh";
        meshInstance.Mesh = part.SourceMesh!.Mesh;
        if (part.SourceMesh.MaterialOverride != null)
        {
            meshInstance.MaterialOverride = (Material)part.SourceMesh.MaterialOverride.Duplicate();
        }
        // The mesh position from the skeleton (pivot offset)
        meshInstance.Position = part.SourceMesh.Position;
        body.AddChild(meshInstance);

        // Create collision shape from mesh AABB
        Aabb aabb = part.SourceMesh.Mesh?.GetAabb() ?? new Aabb(Vector3.Zero, Vector3.One * 0.1f);
        // Account for the mesh offset
        Vector3 meshOffset = part.SourceMesh.Position;
        CollisionShape3D collisionShape = new();
        collisionShape.Name = "Collision";
        collisionShape.Shape = new BoxShape3D { Size = aabb.Size };
        collisionShape.Position = meshOffset + aabb.GetCenter();
        body.AddChild(collisionShape);

        body.CollisionLayer = 4;
        body.CollisionMask = 1 | 2;

        return body;
    }

    /// <summary>
    /// Create a simple 6DOF joint between two ragdoll bodies at a given position.
    /// </summary>
    private void CreateSimpleJoint(string name, RigidBody3D bodyA, RigidBody3D bodyB,
        Vector3 position, float xRange, float yRange, float zRange)
    {
        Generic6DofJoint3D joint = new();
        joint.Name = name;
        joint.Position = position;
        joint.NodeA = bodyB.GetPath();
        joint.NodeB = bodyA.GetPath();
        SetLinearLimits(joint, 0f);
        SetAngularLimits(joint, xRange, yRange, zRange);
        AddChild(joint);
    }

    /// <summary>
    /// Apply the death impulse to all ragdoll bodies.
    /// </summary>
    private void ApplyDeathImpulse(Vector3 impulseDirection, float impulseForce, Dictionary<string, RigidBody3D> bodyMap)
    {
        Vector3 dir = impulseDirection.Normalized();
        float maxUpComponent = 0.35f;
        if (dir.Y > maxUpComponent)
        {
            dir.Y = maxUpComponent;
            dir = dir.Normalized();
        }

        Vector3 mainImpulse = dir * impulseForce * DeathImpulseMultiplier;
        mainImpulse += Vector3.Up * impulseForce * 0.15f;

        foreach (RigidBody3D body in _bodies)
        {
            body.ApplyCentralImpulse(mainImpulse);

            Vector3 randomTorque = new Vector3(
                (GD.Randf() - 0.5f) * RandomSpinMax,
                (GD.Randf() - 0.5f) * RandomSpinMax,
                (GD.Randf() - 0.5f) * RandomSpinMax
            );
            body.ApplyTorqueImpulse(randomTorque);

            Vector3 scatter = new Vector3(
                (GD.Randf() - 0.5f) * impulseForce * 0.15f,
                (GD.Randf() - 0.5f) * impulseForce * 0.08f,
                (GD.Randf() - 0.5f) * impulseForce * 0.15f
            );
            body.ApplyCentralImpulse(scatter);
        }

        // Head gets extra kick for dramatic flair
        if (bodyMap.TryGetValue("Head", out RigidBody3D? head))
        {
            head.ApplyCentralImpulse(mainImpulse * 0.3f + Vector3.Up * impulseForce * 0.1f);
            head.ApplyTorqueImpulse(new Vector3(
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f,
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f,
                (GD.Randf() - 0.5f) * RandomSpinMax * 1.5f
            ));
        }
    }

    /// <summary>
    /// Apply an additional impulse to the ragdoll (e.g., from a subsequent explosion).
    /// The body persists in the world and can keep being knocked around.
    /// </summary>
    public void ApplyExplosionImpulse(Vector3 explosionOrigin, float force)
    {
        if (!IsActive)
        {
            return;
        }

        foreach (RigidBody3D body in _bodies)
        {
            if (!IsInstanceValid(body))
            {
                continue;
            }

            Vector3 direction = (body.GlobalPosition - explosionOrigin).Normalized();
            float distance = body.GlobalPosition.DistanceTo(explosionOrigin);
            float falloff = Mathf.Max(0.1f, 1f / (1f + distance * distance));
            body.ApplyCentralImpulse(direction * force * falloff * body.Mass);
            body.ApplyTorqueImpulse(new Vector3(
                (GD.Randf() - 0.5f) * force * 0.5f,
                (GD.Randf() - 0.5f) * force * 0.5f,
                (GD.Randf() - 0.5f) * force * 0.5f
            ) * falloff);
        }

        _settledTimer = 0f; // Reset settled check
    }

    /// <summary>
    /// Returns true when all body parts have come to rest.
    /// Uses a minimum settling time to let the comedy play out.
    /// </summary>
    public bool IsSettled()
    {
        if (!IsActive || _bodies.Count == 0)
        {
            return false;
        }

        foreach (RigidBody3D body in _bodies)
        {
            if (!IsInstanceValid(body))
            {
                continue;
            }

            if (body.LinearVelocity.Length() > SettledVelocityThreshold ||
                body.AngularVelocity.Length() > SettledAngularThreshold)
            {
                _settledTimer = 0f;
                return false;
            }
        }

        return _settledTimer >= MinSettledTime;
    }

    /// <summary>
    /// Returns the global position of the torso body (center of mass),
    /// or the ragdoll node's own position if no torso is found.
    /// </summary>
    public Vector3 GetTorsoPosition()
    {
        foreach (RigidBody3D body in _bodies)
        {
            if (IsInstanceValid(body) && body.Name == "Ragdoll_Torso")
                return body.GlobalPosition;
        }
        return GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsActive)
        {
            return;
        }

        _settledTimer += (float)delta;
    }

    /// <summary>
    /// Create a single ragdoll body part: RigidBody3D with mesh and collision shape.
    /// </summary>
    private RigidBody3D CreateBodyPart(RagdollPartInfo part)
    {
        RigidBody3D body = new();
        body.Name = $"Ragdoll_{part.Name}";
        body.Mass = part.Mass;
        body.GravityScale = 1.0f; // Normal gravity for realistic ragdoll falls
        body.ContinuousCd = true; // Prevent tunneling through thin walls
        body.ContactMonitor = true;
        body.MaxContactsReported = 4;

        // Physics material for bouncy, comedic feel
        PhysicsMaterial physicsMat = new();
        physicsMat.Bounce = BounceFactor;
        physicsMat.Friction = FrictionFactor;
        body.PhysicsMaterialOverride = physicsMat;

        // Position the body at the part's center offset (local to Commander)
        body.Position = part.Region.CenterOffset;

        // Linear and angular damping to eventually settle
        body.LinearDamp = 0.3f;
        body.AngularDamp = 0.5f;

        // Create mesh instance (clone from the source)
        MeshInstance3D meshInstance = new();
        meshInstance.Name = "Mesh";
        meshInstance.Mesh = part.SourceMesh.Mesh;
        if (part.SourceMesh.MaterialOverride != null)
        {
            meshInstance.MaterialOverride = (Material)part.SourceMesh.MaterialOverride.Duplicate();
        }

        // The mesh was built with vertices relative to the model origin,
        // but the RigidBody3D is positioned at the part center.
        // Offset the mesh so it renders correctly relative to the RigidBody3D.
        meshInstance.Position = -part.Region.CenterOffset;
        body.AddChild(meshInstance);

        // Create collision shape matching the body part bounds
        CollisionShape3D collisionShape = new();
        collisionShape.Name = "Collision";
        Vector3 size = new Vector3(
            (part.Region.Max.X - part.Region.Min.X) * VoxelSize,
            (part.Region.Max.Y - part.Region.Min.Y) * VoxelSize,
            (part.Region.Max.Z - part.Region.Min.Z) * VoxelSize
        );
        collisionShape.Shape = new BoxShape3D { Size = size };
        // Collision shape is centered on the RigidBody3D (which is at part center) - no offset needed
        body.AddChild(collisionShape);

        // Set collision layer/mask to interact with voxel world
        body.CollisionLayer = 4;  // Ragdoll layer
        body.CollisionMask = 1 | 2; // Interact with world (1) and other objects (2)

        return body;
    }

    /// <summary>
    /// Neck joint: Head to Torso. ConeTwistJoint3D with limited rotation
    /// so the head wobbles realistically but doesn't spin 360.
    /// Using Generic6DofJoint3D to emulate cone-twist since Godot 4.3 uses it.
    /// </summary>
    private void CreateNeckJoint(RigidBody3D head, RigidBody3D torso, CommanderBodyParts parts)
    {
        Generic6DofJoint3D joint = new();
        joint.Name = "NeckJoint";

        // Position at the bottom of the head / top of torso
        Vector3 jointPos = new Vector3(
            (parts.HeadRegion.CenterOffset.X + parts.TorsoRegion.CenterOffset.X) * 0.5f,
            parts.TorsoRegion.CenterOffset.Y + (parts.TorsoRegion.Max.Y - parts.TorsoRegion.Min.Y) * VoxelSize * 0.5f,
            (parts.HeadRegion.CenterOffset.Z + parts.TorsoRegion.CenterOffset.Z) * 0.5f
        );
        joint.Position = jointPos;

        joint.NodeA = torso.GetPath();
        joint.NodeB = head.GetPath();

        // Lock linear axes (no separation)
        SetLinearLimits(joint, 0f);

        // Allow limited angular movement
        float neckRange = Mathf.DegToRad(30f);
        SetAngularLimits(joint, neckRange, Mathf.DegToRad(45f), neckRange);

        AddChild(joint);
    }

    /// <summary>
    /// Shoulder joint: Arm to Torso. Wide range of motion for flailing arms.
    /// </summary>
    private void CreateShoulderJoint(RigidBody3D arm, RigidBody3D torso, CommanderBodyParts parts, bool isLeft)
    {
        Generic6DofJoint3D joint = new();
        joint.Name = isLeft ? "LeftShoulderJoint" : "RightShoulderJoint";

        CommanderBodyPartRegion armRegion = isLeft ? parts.LeftArmRegion : parts.RightArmRegion;

        // Joint at inner edge of arm, top
        float armInnerX = isLeft
            ? armRegion.CenterOffset.X + (armRegion.Max.X - armRegion.Min.X) * VoxelSize * 0.5f
            : armRegion.CenterOffset.X - (armRegion.Max.X - armRegion.Min.X) * VoxelSize * 0.5f;

        Vector3 jointPos = new Vector3(
            armInnerX,
            armRegion.CenterOffset.Y + (armRegion.Max.Y - armRegion.Min.Y) * VoxelSize * 0.3f,
            armRegion.CenterOffset.Z
        );
        joint.Position = jointPos;

        joint.NodeA = torso.GetPath();
        joint.NodeB = arm.GetPath();

        SetLinearLimits(joint, 0f);

        // Wide range for flailing
        float shoulderRange = Mathf.DegToRad(90f);
        SetAngularLimits(joint, shoulderRange, shoulderRange, Mathf.DegToRad(45f));

        AddChild(joint);
    }

    /// <summary>
    /// Hip joint: Leg to Torso. Hinge-like motion (primarily forward/backward).
    /// Using Generic6DofJoint3D with restricted axes to emulate a hinge.
    /// </summary>
    private void CreateHipJoint(RigidBody3D leg, RigidBody3D torso, CommanderBodyParts parts, bool isLeft)
    {
        Generic6DofJoint3D joint = new();
        joint.Name = isLeft ? "LeftHipJoint" : "RightHipJoint";

        CommanderBodyPartRegion legRegion = isLeft ? parts.LeftLegRegion : parts.RightLegRegion;

        // Joint at top of leg / bottom of torso
        Vector3 jointPos = new Vector3(
            legRegion.CenterOffset.X,
            parts.TorsoRegion.CenterOffset.Y - (parts.TorsoRegion.Max.Y - parts.TorsoRegion.Min.Y) * VoxelSize * 0.5f,
            legRegion.CenterOffset.Z
        );
        joint.Position = jointPos;

        joint.NodeA = torso.GetPath();
        joint.NodeB = leg.GetPath();

        SetLinearLimits(joint, 0f);

        // Hinge-like: wide X rotation (forward/back), limited Y and Z
        SetAngularLimits(joint, Mathf.DegToRad(60f), Mathf.DegToRad(15f), Mathf.DegToRad(20f));

        AddChild(joint);
    }

    /// <summary>
    /// Lock all linear axes on a Generic6DofJoint3D within the given tolerance.
    /// </summary>
    private static void SetLinearLimits(Generic6DofJoint3D joint, float tolerance)
    {
        // X axis
        joint.SetParamX(Generic6DofJoint3D.Param.LinearLowerLimit, -tolerance);
        joint.SetParamX(Generic6DofJoint3D.Param.LinearUpperLimit, tolerance);
        // Y axis
        joint.SetParamY(Generic6DofJoint3D.Param.LinearLowerLimit, -tolerance);
        joint.SetParamY(Generic6DofJoint3D.Param.LinearUpperLimit, tolerance);
        // Z axis
        joint.SetParamZ(Generic6DofJoint3D.Param.LinearLowerLimit, -tolerance);
        joint.SetParamZ(Generic6DofJoint3D.Param.LinearUpperLimit, tolerance);
    }

    /// <summary>
    /// Set angular limits on all three axes of a Generic6DofJoint3D.
    /// </summary>
    private static void SetAngularLimits(Generic6DofJoint3D joint, float xRange, float yRange, float zRange)
    {
        joint.SetParamX(Generic6DofJoint3D.Param.AngularLowerLimit, -xRange);
        joint.SetParamX(Generic6DofJoint3D.Param.AngularUpperLimit, xRange);
        joint.SetParamY(Generic6DofJoint3D.Param.AngularLowerLimit, -yRange);
        joint.SetParamY(Generic6DofJoint3D.Param.AngularUpperLimit, yRange);
        joint.SetParamZ(Generic6DofJoint3D.Param.AngularLowerLimit, -zRange);
        joint.SetParamZ(Generic6DofJoint3D.Param.AngularUpperLimit, zRange);
    }

    // Legacy method kept for scene compatibility
    public void ActivateRagdoll(Vector3 impulse)
    {
        // This is now called through the full Activate method by Commander.cs
        // Kept as a no-op for backward compat if the scene calls it
    }
}
