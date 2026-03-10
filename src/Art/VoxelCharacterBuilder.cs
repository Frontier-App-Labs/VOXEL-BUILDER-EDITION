using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Builds a hierarchical skeleton from a CharacterDefinition.
/// Each body part becomes a MeshInstance3D child of a joint Node3D.
/// The resulting scene tree supports procedural animation via joint rotations.
///
/// All body parts share a single VoxelPalette texture generated from the
/// character's colors. This renders characters through the same texture
/// pipeline as world blocks — consistent lighting, no toon-shader wash-out.
/// </summary>
public static class VoxelCharacterBuilder
{
    /// <summary>
    /// Builds a complete character scene from a definition.
    /// Returns the root Node3D with the full skeleton hierarchy.
    /// All mesh instances use a shared palette texture for uniform rendering.
    /// </summary>
    public static Node3D Build(CharacterDefinition def)
    {
        // Collect all unique colors across every body part into one shared palette
        VoxelPalette palette = new();
        palette.AddColors(def.Head.Voxels);
        palette.AddColors(def.Torso.Voxels);
        palette.AddColors(def.LeftUpperArm.Voxels);
        palette.AddColors(def.LeftForearm.Voxels);
        palette.AddColors(def.RightUpperArm.Voxels);
        palette.AddColors(def.RightForearm.Voxels);
        palette.AddColors(def.LeftThigh.Voxels);
        palette.AddColors(def.LeftShin.Voxels);
        palette.AddColors(def.RightThigh.Voxels);
        palette.AddColors(def.RightShin.Voxels);
        if (def.Accessory1 != null) palette.AddColors(def.Accessory1.Voxels);
        if (def.Accessory2 != null) palette.AddColors(def.Accessory2.Voxels);
        palette.Build();

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

        MeshInstance3D torsoMesh = BuildPart(def.Torso, vs, def.Jitter, palette);
        spine.AddChild(torsoMesh);

        // === NECK → HEAD ===
        Node3D neck = new Node3D();
        neck.Name = "Neck";
        neck.Position = def.Head.AttachOffset * vs;
        spine.AddChild(neck);

        MeshInstance3D headMesh = BuildPart(def.Head, vs, def.Jitter, palette);
        neck.AddChild(headMesh);

        // === LEFT SHOULDER → UPPER ARM → ELBOW → FOREARM ===
        BuildArmChain(spine, def.LeftUpperArm, def.LeftForearm, vs, def.Jitter, "Left", palette);

        // === RIGHT SHOULDER → UPPER ARM → ELBOW → FOREARM ===
        BuildArmChain(spine, def.RightUpperArm, def.RightForearm, vs, def.Jitter, "Right", palette);

        // === LEFT HIP → THIGH → KNEE → SHIN ===
        BuildLegChain(hips, def.LeftThigh, def.LeftShin, vs, def.Jitter, "Left", palette);

        // === RIGHT HIP → THIGH → KNEE → SHIN ===
        BuildLegChain(hips, def.RightThigh, def.RightShin, vs, def.Jitter, "Right", palette);

        // === ACCESSORIES ===
        if (def.Accessory1 != null)
        {
            MeshInstance3D acc = BuildPart(def.Accessory1, vs, def.Jitter, palette);
            acc.Position = def.Accessory1.AttachOffset * vs;
            if (def.Accessory1.Name.Contains("Helmet") || def.Accessory1.Name.Contains("Hat"))
                neck.AddChild(acc);
            else
                spine.AddChild(acc);
        }

        if (def.Accessory2 != null)
        {
            MeshInstance3D acc = BuildPart(def.Accessory2, vs, def.Jitter, palette);
            acc.Position = def.Accessory2.AttachOffset * vs;
            spine.AddChild(acc);
        }

        return root;
    }

    private static void BuildArmChain(Node3D parent, BodyPartDefinition upper, BodyPartDefinition forearm,
        float vs, float jitter, string side, VoxelPalette palette)
    {
        Node3D shoulder = new Node3D();
        shoulder.Name = $"{side}Shoulder";
        shoulder.Position = upper.AttachOffset * vs;
        parent.AddChild(shoulder);

        MeshInstance3D upperMesh = BuildPart(upper, vs, jitter, palette);
        shoulder.AddChild(upperMesh);

        Node3D elbow = new Node3D();
        elbow.Name = $"{side}Elbow";
        elbow.Position = forearm.AttachOffset * vs;
        shoulder.AddChild(elbow);

        MeshInstance3D forearmMesh = BuildPart(forearm, vs, jitter, palette);
        elbow.AddChild(forearmMesh);
    }

    private static void BuildLegChain(Node3D parent, BodyPartDefinition thigh, BodyPartDefinition shin,
        float vs, float jitter, string side, VoxelPalette palette)
    {
        Node3D hip = new Node3D();
        hip.Name = $"{side}Hip";
        hip.Position = thigh.AttachOffset * vs;
        parent.AddChild(hip);

        MeshInstance3D thighMesh = BuildPart(thigh, vs, jitter, palette);
        hip.AddChild(thighMesh);

        Node3D knee = new Node3D();
        knee.Name = $"{side}Knee";
        knee.Position = shin.AttachOffset * vs;
        hip.AddChild(knee);

        MeshInstance3D shinMesh = BuildPart(shin, vs, jitter, palette);
        knee.AddChild(shinMesh);
    }

    /// <summary>
    /// Applies the palette texture material to all MeshInstance3D descendants.
    /// Replaces the old toon shader approach — characters now render through the
    /// same texture pipeline as world blocks for consistent appearance.
    /// The teamColor parameter is kept for API compatibility but is no longer used
    /// (team colors are baked into the palette texture).
    /// </summary>
    public static void ApplyToonMaterial(Node3D root, Color teamColor)
    {
        // No-op: palette materials are now applied during Build().
        // This method is kept so callers don't need to be modified immediately.
    }

    private static MeshInstance3D BuildPart(BodyPartDefinition part, float voxelSize, float jitter, VoxelPalette palette)
    {
        VoxelModelBuilder builder = new VoxelModelBuilder();
        builder.VoxelSize = voxelSize;
        builder.JitterAmount = jitter;

        ArrayMesh mesh = builder.BuildMesh(part.Voxels, palette);

        MeshInstance3D meshInst = new MeshInstance3D();
        meshInst.Name = part.Name;
        meshInst.Mesh = mesh;

        // Use palette texture material — same pipeline as world blocks
        meshInst.MaterialOverride = palette.CreateMaterial();

        // Offset the mesh so the pivot point is at the joint
        meshInst.Position = -part.PivotVoxelCoords * voxelSize;

        return meshInst;
    }
}
