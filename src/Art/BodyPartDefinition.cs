using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Defines a single body part's voxel grid, pivot point, and parent joint info.
/// Used by VoxelCharacterBuilder to assemble a skeleton hierarchy.
/// </summary>
public sealed class BodyPartDefinition
{
    /// <summary>Name of this body part (e.g., "Head", "LeftUpperArm").</summary>
    public string Name { get; init; } = "";

    /// <summary>3D voxel color array. null = air, non-null = solid voxel.</summary>
    public Color?[,,] Voxels { get; init; } = new Color?[0, 0, 0];

    /// <summary>
    /// Local pivot point in voxel-grid coordinates (before scaling to meters).
    /// This is where the joint/rotation center is, relative to the part's own grid origin.
    /// E.g., for an upper arm, the pivot is at the shoulder end.
    /// </summary>
    public Vector3 PivotVoxelCoords { get; init; }

    /// <summary>
    /// Offset from parent joint's pivot to this part's pivot, in voxel coords.
    /// Used to position this part relative to its parent in the skeleton hierarchy.
    /// </summary>
    public Vector3 AttachOffset { get; init; }
}

/// <summary>
/// Complete character definition: all body parts + skeleton layout.
/// </summary>
public sealed class CharacterDefinition
{
    public string Name { get; init; } = "Character";
    public float VoxelSize { get; init; } = 0.06f;
    public float Jitter { get; init; } = 0.002f;

    // Core body parts
    public BodyPartDefinition Head { get; init; } = null!;
    public BodyPartDefinition Torso { get; init; } = null!;
    public BodyPartDefinition LeftUpperArm { get; init; } = null!;
    public BodyPartDefinition LeftForearm { get; init; } = null!;
    public BodyPartDefinition RightUpperArm { get; init; } = null!;
    public BodyPartDefinition RightForearm { get; init; } = null!;
    public BodyPartDefinition LeftThigh { get; init; } = null!;
    public BodyPartDefinition LeftShin { get; init; } = null!;
    public BodyPartDefinition RightThigh { get; init; } = null!;
    public BodyPartDefinition RightShin { get; init; } = null!;

    // Optional extras (helmet, backpack, weapon held, etc.)
    public BodyPartDefinition? Accessory1 { get; init; }
    public BodyPartDefinition? Accessory2 { get; init; }
}
