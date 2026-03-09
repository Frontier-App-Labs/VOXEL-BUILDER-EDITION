using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Builds a hierarchical skeleton from a CharacterDefinition.
/// Each body part becomes a MeshInstance3D child of a joint Node3D.
/// The resulting scene tree supports procedural animation via joint rotations.
/// </summary>
public static class VoxelCharacterBuilder
{
    /// <summary>
    /// Builds a complete character scene from a definition.
    /// Returns the root Node3D with the full skeleton hierarchy.
    /// </summary>
    public static Node3D Build(CharacterDefinition def)
    {
        Node3D root = new Node3D();
        root.Name = def.Name;

        float vs = def.VoxelSize;

        // === HIPS (root pivot) ===
        Node3D hips = new Node3D();
        hips.Name = "Hips";
        root.AddChild(hips);

        // === SPINE → TORSO ===
        Node3D spine = new Node3D();
        spine.Name = "Spine";
        spine.Position = new Vector3(0, def.Torso.PivotVoxelCoords.Y * vs, 0);
        hips.AddChild(spine);

        MeshInstance3D torsoMesh = BuildPart(def.Torso, vs, def.Jitter);
        spine.AddChild(torsoMesh);

        // === NECK → HEAD ===
        Node3D neck = new Node3D();
        neck.Name = "Neck";
        neck.Position = def.Head.AttachOffset * vs;
        spine.AddChild(neck);

        MeshInstance3D headMesh = BuildPart(def.Head, vs, def.Jitter);
        neck.AddChild(headMesh);

        // === LEFT SHOULDER → UPPER ARM → ELBOW → FOREARM ===
        BuildArmChain(spine, def.LeftUpperArm, def.LeftForearm, vs, def.Jitter, "Left");

        // === RIGHT SHOULDER → UPPER ARM → ELBOW → FOREARM ===
        BuildArmChain(spine, def.RightUpperArm, def.RightForearm, vs, def.Jitter, "Right");

        // === LEFT HIP → THIGH → KNEE → SHIN ===
        BuildLegChain(hips, def.LeftThigh, def.LeftShin, vs, def.Jitter, "Left");

        // === RIGHT HIP → THIGH → KNEE → SHIN ===
        BuildLegChain(hips, def.RightThigh, def.RightShin, vs, def.Jitter, "Right");

        // === ACCESSORIES ===
        if (def.Accessory1 != null)
        {
            MeshInstance3D acc = BuildPart(def.Accessory1, vs, def.Jitter);
            acc.Position = def.Accessory1.AttachOffset * vs;
            // Attach to head for helmets, spine for backpacks
            if (def.Accessory1.Name.Contains("Helmet") || def.Accessory1.Name.Contains("Hat"))
                neck.AddChild(acc);
            else
                spine.AddChild(acc);
        }

        if (def.Accessory2 != null)
        {
            MeshInstance3D acc = BuildPart(def.Accessory2, vs, def.Jitter);
            acc.Position = def.Accessory2.AttachOffset * vs;
            spine.AddChild(acc);
        }

        return root;
    }

    private static void BuildArmChain(Node3D parent, BodyPartDefinition upper, BodyPartDefinition forearm,
        float vs, float jitter, string side)
    {
        Node3D shoulder = new Node3D();
        shoulder.Name = $"{side}Shoulder";
        shoulder.Position = upper.AttachOffset * vs;
        parent.AddChild(shoulder);

        MeshInstance3D upperMesh = BuildPart(upper, vs, jitter);
        shoulder.AddChild(upperMesh);

        Node3D elbow = new Node3D();
        elbow.Name = $"{side}Elbow";
        elbow.Position = forearm.AttachOffset * vs;
        shoulder.AddChild(elbow);

        MeshInstance3D forearmMesh = BuildPart(forearm, vs, jitter);
        elbow.AddChild(forearmMesh);
    }

    private static void BuildLegChain(Node3D parent, BodyPartDefinition thigh, BodyPartDefinition shin,
        float vs, float jitter, string side)
    {
        Node3D hip = new Node3D();
        hip.Name = $"{side}Hip";
        hip.Position = thigh.AttachOffset * vs;
        parent.AddChild(hip);

        MeshInstance3D thighMesh = BuildPart(thigh, vs, jitter);
        hip.AddChild(thighMesh);

        Node3D knee = new Node3D();
        knee.Name = $"{side}Knee";
        knee.Position = shin.AttachOffset * vs;
        hip.AddChild(knee);

        MeshInstance3D shinMesh = BuildPart(shin, vs, jitter);
        knee.AddChild(shinMesh);
    }

    private static MeshInstance3D BuildPart(BodyPartDefinition part, float voxelSize, float jitter)
    {
        VoxelModelBuilder builder = new VoxelModelBuilder();
        builder.VoxelSize = voxelSize;
        builder.JitterAmount = jitter;

        ArrayMesh mesh = builder.BuildMesh(part.Voxels);

        MeshInstance3D meshInst = new MeshInstance3D();
        meshInst.Name = part.Name;
        meshInst.Mesh = mesh;

        // Back-face culling enabled — winding order is correct (clockwise front faces)
        StandardMaterial3D mat = VoxelModelBuilder.CreateVoxelMaterial();
        mat.CullMode = BaseMaterial3D.CullModeEnum.Back;
        meshInst.SetSurfaceOverrideMaterial(0, mat);

        // Offset the mesh so the pivot point is at the joint
        meshInst.Position = -part.PivotVoxelCoords * voxelSize;

        return meshInst;
    }
}
