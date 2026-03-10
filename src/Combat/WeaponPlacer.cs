using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Handles placing weapon nodes into the world. Validates that the
/// placement is structurally supported and on the exterior of the
/// structure, then positions and orients the weapon to face outward.
/// </summary>
public partial class WeaponPlacer : Node
{
    /// <summary>
    /// Creates a weapon of type T, positions it in the world at the given
    /// build-unit location, assigns ownership, and rotates it to face outward.
    /// Returns the weapon node (already added to the scene tree).
    /// </summary>
    public T PlaceWeapon<T>(Node parent, VoxelWorld world, Vector3I buildUnitPosition, PlayerSlot owner)
        where T : WeaponBase, new()
    {
        T weapon = new T();
        weapon.AssignOwner(owner);

        // Position the weapon at the bottom-center of the build unit so it sits
        // flush on top of whatever surface it was placed on (floor or wall top).
        // The weapon model's Y=0 is its base, so the origin goes at the bottom of
        // the build unit, centered horizontally.
        Vector3 worldPos = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(buildUnitPosition))
            + new Vector3(
                GameConfig.BuildUnitMeters * 0.5f,
                0f,
                GameConfig.BuildUnitMeters * 0.5f);
        weapon.Position = worldPos;

        // Add to scene tree first (required for LookAt)
        parent.AddChild(weapon);

        // Face outward: determine the dominant open-air direction, but keep the
        // weapon upright — only rotate around Y (yaw).  Weapons should never tilt.
        Vector3 outwardDir = ComputeOutwardDirection(world, buildUnitPosition);
        outwardDir.Y = 0f; // always stay level
        if (outwardDir.LengthSquared() < 0.001f)
        {
            // All horizontal directions cancelled out (open air on all sides) —
            // default to facing forward (-Z).
            outwardDir = Vector3.Forward;
        }
        outwardDir = outwardDir.Normalized();
        weapon.LookAt(worldPos + outwardDir, Vector3.Up);

        // Mark underlying voxels as weapon-mount
        foreach (Vector3I mountVoxel in EnumerateMountVoxels(buildUnitPosition))
        {
            Voxel.Voxel current = world.GetVoxel(mountVoxel);
            if (!current.IsAir)
            {
                world.SetVoxel(mountVoxel, current.WithWeaponMount(true), owner);
            }
        }

        return weapon;
    }

    /// <summary>
    /// Checks whether the build-unit position is valid for weapon placement:
    /// at least one supporting solid voxel below, at least one exposed
    /// (air) face so the weapon can fire outward, and no existing weapon
    /// already placed at this position.
    /// </summary>
    public static bool ValidatePlacement(VoxelWorld world, Vector3I buildUnitPosition, SceneTree? sceneTree = null)
    {
        // Check for weapon stacking: reject if a weapon already exists at this position
        if (sceneTree != null)
        {
            Vector3 targetPos = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(buildUnitPosition))
                + new Vector3(
                    GameConfig.BuildUnitMeters * 0.5f,
                    0f,
                    GameConfig.BuildUnitMeters * 0.5f);

            foreach (Node node in sceneTree.GetNodesInGroup("Weapons"))
            {
                if (node is WeaponBase existingWeapon
                    && existingWeapon.GlobalPosition.DistanceTo(targetPos) < 0.01f)
                {
                    return false;
                }
            }
        }

        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        bool hasSupport = false;
        bool hasExposedFace = false;

        // Check structural support: at least one solid voxel directly below
        // OR within the base row of the build unit itself. When clicking on a
        // floor surface, the raycast targets the air above, so the build unit
        // may start at the same Y as the floor voxel. Checking only y=-1
        // missed support when the floor voxel sits at the build unit's base row.
        for (int y = -1; y < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; y++)
        {
            for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; z++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                {
                    Vector3I check = microBase + new Vector3I(x, y, z);
                    if (world.GetVoxel(check).IsSolid)
                    {
                        hasSupport = true;
                        break;
                    }
                }
            }
        }

        if (!hasSupport)
        {
            return false;
        }

        // Check exterior exposure: at least one face of the build unit
        // borders an air voxel so the weapon can actually fire outward.
        // Check all 6 cardinal directions (+X, -X, +Y, -Y, +Z, -Z).
        Vector3I[] cardinalOffsets =
        {
            new Vector3I(GameConfig.MicrovoxelsPerBuildUnit, 0, 0),
            new Vector3I(-1, 0, 0),
            new Vector3I(0, 0, GameConfig.MicrovoxelsPerBuildUnit),
            new Vector3I(0, 0, -1),
            new Vector3I(0, GameConfig.MicrovoxelsPerBuildUnit, 0),
            new Vector3I(0, -1, 0),
        };

        foreach (Vector3I offset in cardinalOffsets)
        {
            Vector3I checkPos = microBase + offset;
            if (world.GetVoxel(checkPos).IsAir)
            {
                hasExposedFace = true;
                break;
            }
        }

        return hasExposedFace;
    }

    /// <summary>
    /// Public accessor for ghost preview orientation.
    /// </summary>
    public static Vector3 ComputeOutwardDirectionPublic(VoxelWorld world, Vector3I buildUnitPosition)
        => ComputeOutwardDirection(world, buildUnitPosition);

    /// <summary>
    /// Determines the outward-facing direction by sampling air voxels around
    /// the build unit. Returns a normalized direction toward the most open side.
    /// </summary>
    private static Vector3 ComputeOutwardDirection(VoxelWorld world, Vector3I buildUnitPosition)
    {
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        Vector3 outward = Vector3.Zero;

        // Sample all 6 cardinal directions and accumulate open-air directions
        (Vector3I offset, Vector3 dir)[] sides =
        {
            (new Vector3I(GameConfig.MicrovoxelsPerBuildUnit, 0, 0), Vector3.Right),
            (new Vector3I(-1, 0, 0), Vector3.Left),
            (new Vector3I(0, 0, GameConfig.MicrovoxelsPerBuildUnit), Vector3.Back),
            (new Vector3I(0, 0, -1), Vector3.Forward),
            (new Vector3I(0, GameConfig.MicrovoxelsPerBuildUnit, 0), Vector3.Up),
            (new Vector3I(0, -1, 0), Vector3.Down),
        };

        foreach ((Vector3I offset, Vector3 dir) in sides)
        {
            Vector3I checkPos = microBase + offset;
            if (world.GetVoxel(checkPos).IsAir)
            {
                outward += dir;
            }
        }

        return outward.LengthSquared() > 0.001f ? outward.Normalized() : Vector3.Forward;
    }

    private static System.Collections.Generic.IEnumerable<Vector3I> EnumerateMountVoxels(Vector3I buildUnitPosition)
    {
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
            {
                yield return microBase + new Vector3I(x, 0, z);
            }
        }
    }
}
