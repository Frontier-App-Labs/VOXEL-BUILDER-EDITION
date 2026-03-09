using Godot;
using System.Collections.Generic;
using VoxelSiege.Voxel;
using VoxelSiege.Core;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Army;

public sealed class DoorPlacement
{
    public PlayerSlot Owner { get; init; }
    public Vector3I BaseMicrovoxel { get; init; }  // bottom of the 1x3 opening
    public List<Vector3I> OpeningVoxels { get; init; } = new(); // the 1x3 column of carved air
}

public class DoorRegistry
{
    private readonly Dictionary<PlayerSlot, List<DoorPlacement>> _doors = new();
    public const int MaxDoorsPerPlayer = 4;
    public const int DoorHeight = 3; // microvoxels tall

    /// <summary>
    /// Attempts to place a door at the given base microvoxel position on the build zone perimeter.
    /// Carves a 1-wide x 3-tall opening through solid voxels.
    /// </summary>
    /// <param name="world">The voxel world to carve into.</param>
    /// <param name="baseMicrovoxel">Bottom microvoxel of the 1x3 opening.</param>
    /// <param name="owner">The player placing the door.</param>
    /// <param name="zoneMin">Minimum microvoxel corner of the player's build zone (inclusive).</param>
    /// <param name="zoneMax">Maximum microvoxel corner of the player's build zone (inclusive).</param>
    /// <param name="failReason">Reason for failure, or empty on success.</param>
    /// <returns>True if the door was placed successfully.</returns>
    public bool TryPlaceDoor(
        VoxelWorld world,
        Vector3I baseMicrovoxel,
        PlayerSlot owner,
        Vector3I zoneMin,
        Vector3I zoneMax,
        out string failReason)
    {
        failReason = string.Empty;

        // Check: player hasn't exceeded max doors
        if (!_doors.TryGetValue(owner, out List<DoorPlacement>? ownerDoors))
        {
            ownerDoors = new List<DoorPlacement>();
            _doors[owner] = ownerDoors;
        }

        if (ownerDoors.Count >= MaxDoorsPerPlayer)
        {
            failReason = $"Maximum of {MaxDoorsPerPlayer} doors already placed.";
            return false;
        }

        // Check: position is on zone edge (at min/max X or Z of zone, within Y range)
        bool onMinX = baseMicrovoxel.X == zoneMin.X;
        bool onMaxX = baseMicrovoxel.X == zoneMax.X;
        bool onMinZ = baseMicrovoxel.Z == zoneMin.Z;
        bool onMaxZ = baseMicrovoxel.Z == zoneMax.Z;

        if (!onMinX && !onMaxX && !onMinZ && !onMaxZ)
        {
            failReason = "Door must be placed on the edge of the build zone.";
            return false;
        }

        // Check: Y range is within build zone
        if (baseMicrovoxel.Y < zoneMin.Y || baseMicrovoxel.Y + DoorHeight - 1 > zoneMax.Y)
        {
            failReason = "Door opening extends outside build zone vertically.";
            return false;
        }

        // Collect the 3 voxel positions
        var openingVoxels = new List<Vector3I>(DoorHeight);
        for (int dy = 0; dy < DoorHeight; dy++)
        {
            openingVoxels.Add(new Vector3I(baseMicrovoxel.X, baseMicrovoxel.Y + dy, baseMicrovoxel.Z));
        }

        // Check: all 3 voxels are currently solid (something to carve through)
        foreach (Vector3I pos in openingVoxels)
        {
            VoxelValue voxel = world.GetVoxel(pos);
            if (!voxel.IsSolid)
            {
                failReason = "All door voxels must be solid (nothing to carve through).";
                return false;
            }

            // Don't allow carving through Foundation (indestructible)
            if (voxel.Material == VoxelMaterialType.Foundation)
            {
                failReason = "Cannot carve a door through foundation blocks.";
                return false;
            }
        }

        // Check: not overlapping an existing door
        foreach (DoorPlacement existing in ownerDoors)
        {
            foreach (Vector3I existingVoxel in existing.OpeningVoxels)
            {
                foreach (Vector3I newVoxel in openingVoxels)
                {
                    if (existingVoxel == newVoxel)
                    {
                        failReason = "Door overlaps an existing door opening.";
                        return false;
                    }
                }
            }
        }

        // Success: carve the opening
        foreach (Vector3I pos in openingVoxels)
        {
            world.SetVoxel(pos, VoxelValue.Air, owner);
        }

        // Record the placement
        var placement = new DoorPlacement
        {
            Owner = owner,
            BaseMicrovoxel = baseMicrovoxel,
            OpeningVoxels = openingVoxels,
        };
        ownerDoors.Add(placement);

        return true;
    }

    /// <summary>
    /// Removes a door placement by base position. Does NOT restore voxels
    /// (player can manually rebuild the wall).
    /// </summary>
    /// <returns>True if a matching door was found and removed.</returns>
    public bool RemoveDoor(Vector3I baseMicrovoxel, PlayerSlot owner)
    {
        if (!_doors.TryGetValue(owner, out List<DoorPlacement>? ownerDoors))
            return false;

        for (int i = 0; i < ownerDoors.Count; i++)
        {
            if (ownerDoors[i].BaseMicrovoxel == baseMicrovoxel)
            {
                ownerDoors.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a microvoxel position is part of a given owner's door opening.
    /// Useful for pathfinding — troops can walk through their owner's doors.
    /// </summary>
    public bool IsDoorVoxel(Vector3I microvoxel, PlayerSlot owner)
    {
        if (!_doors.TryGetValue(owner, out List<DoorPlacement>? ownerDoors))
            return false;

        foreach (DoorPlacement door in ownerDoors)
        {
            foreach (Vector3I pos in door.OpeningVoxels)
            {
                if (pos == microvoxel)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a read-only list of doors for the given player.
    /// </summary>
    public IReadOnlyList<DoorPlacement> GetDoors(PlayerSlot owner)
    {
        if (_doors.TryGetValue(owner, out List<DoorPlacement>? ownerDoors))
            return ownerDoors;

        return System.Array.Empty<DoorPlacement>();
    }

    /// <summary>
    /// Resets all door data for a new match.
    /// </summary>
    public void Clear()
    {
        _doors.Clear();
    }
}
