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

    /// <summary>
    /// Visual door panel mesh parented to the VoxelWorld node. Freed on removal.
    /// </summary>
    public MeshInstance3D? DoorMesh { get; set; }
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

        // Sample the wall material BEFORE carving so we can color-match the door panel
        VoxelMaterialType wallMaterial = world.GetVoxel(openingVoxels[0]).Material;

        // Success: carve the opening
        foreach (Vector3I pos in openingVoxels)
        {
            world.SetVoxel(pos, VoxelValue.Air, owner);
        }

        // Determine which zone edge the door sits on and compute the outward-facing normal
        Vector3 outwardNormal = Vector3.Zero;
        if (onMinX) outwardNormal = Vector3.Left;      // -X faces outward
        else if (onMaxX) outwardNormal = Vector3.Right; // +X faces outward
        else if (onMinZ) outwardNormal = Vector3.Forward; // -Z faces outward (Godot forward)
        else if (onMaxZ) outwardNormal = Vector3.Back;    // +Z faces outward

        // Build the visual door mesh and parent it to the VoxelWorld node
        MeshInstance3D? doorMesh = BuildDoorMesh(baseMicrovoxel, wallMaterial, outwardNormal);
        if (doorMesh != null)
        {
            world.AddChild(doorMesh);
        }

        // Record the placement
        var placement = new DoorPlacement
        {
            Owner = owner,
            BaseMicrovoxel = baseMicrovoxel,
            OpeningVoxels = openingVoxels,
            DoorMesh = doorMesh,
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
                FreeDoorMesh(ownerDoors[i]);
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
    /// Checks if a microvoxel position is part of ANY player's door opening.
    /// Useful for pathfinding — troops can walk through any door to enter/exit bases.
    /// </summary>
    public bool IsDoorVoxelAnyPlayer(Vector3I microvoxel)
    {
        foreach ((PlayerSlot _, List<DoorPlacement> doors) in _doors)
        {
            foreach (DoorPlacement door in doors)
            {
                foreach (Vector3I pos in door.OpeningVoxels)
                {
                    if (pos == microvoxel)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the nearest door for the given player to the specified microvoxel position.
    /// Returns null if the player has no doors.
    /// </summary>
    public DoorPlacement? FindNearestDoor(PlayerSlot owner, Vector3I fromMicrovoxel)
    {
        if (!_doors.TryGetValue(owner, out List<DoorPlacement>? ownerDoors) || ownerDoors.Count == 0)
            return null;

        DoorPlacement? nearest = null;
        int bestDistSq = int.MaxValue;

        foreach (DoorPlacement door in ownerDoors)
        {
            Vector3I doorPos = door.BaseMicrovoxel;
            int dx = doorPos.X - fromMicrovoxel.X;
            int dy = doorPos.Y - fromMicrovoxel.Y;
            int dz = doorPos.Z - fromMicrovoxel.Z;
            int distSq = dx * dx + dy * dy + dz * dz;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                nearest = door;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Gets the outside position adjacent to a door (one step outside the base wall).
    /// The returned microvoxel is on the exterior side of the door, at foot level.
    /// </summary>
    public Vector3I GetDoorExteriorPosition(DoorPlacement door, Vector3I zoneMin, Vector3I zoneMax)
    {
        Vector3I basePos = door.BaseMicrovoxel;
        // Determine which zone edge the door sits on and step one voxel outward
        if (basePos.X == zoneMin.X) return basePos + new Vector3I(-1, 0, 0);
        if (basePos.X == zoneMax.X) return basePos + new Vector3I(1, 0, 0);
        if (basePos.Z == zoneMin.Z) return basePos + new Vector3I(0, 0, -1);
        if (basePos.Z == zoneMax.Z) return basePos + new Vector3I(0, 0, 1);
        // Fallback: step in -X direction
        return basePos + new Vector3I(-1, 0, 0);
    }

    /// <summary>
    /// Gets the inside position adjacent to a door (one step inside the base wall).
    /// The returned microvoxel is on the interior side of the door, at foot level.
    /// </summary>
    public Vector3I GetDoorInteriorPosition(DoorPlacement door, Vector3I zoneMin, Vector3I zoneMax)
    {
        Vector3I basePos = door.BaseMicrovoxel;
        // Determine which zone edge the door sits on and step one voxel inward
        if (basePos.X == zoneMin.X) return basePos + new Vector3I(1, 0, 0);
        if (basePos.X == zoneMax.X) return basePos + new Vector3I(-1, 0, 0);
        if (basePos.Z == zoneMin.Z) return basePos + new Vector3I(0, 0, 1);
        if (basePos.Z == zoneMax.Z) return basePos + new Vector3I(0, 0, -1);
        // Fallback: step in +X direction
        return basePos + new Vector3I(1, 0, 0);
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
        foreach ((PlayerSlot _, List<DoorPlacement> doors) in _doors)
        {
            foreach (DoorPlacement door in doors)
            {
                FreeDoorMesh(door);
            }
        }

        _doors.Clear();
    }

    // ---- Door mesh construction ----

    private static readonly Color HandleColor = new Color("c9a84c"); // brass/gold

    /// <summary>
    /// Frees a door's visual mesh if it exists.
    /// </summary>
    private static void FreeDoorMesh(DoorPlacement door)
    {
        if (door.DoorMesh != null && GodotObject.IsInstanceValid(door.DoorMesh))
        {
            door.DoorMesh.QueueFree();
            door.DoorMesh = null;
        }
    }

    /// <summary>
    /// Builds a procedural door panel MeshInstance3D with a handle detail.
    /// The door fills the 1-wide x DoorHeight-tall opening and is 0.1 microvoxels thick.
    /// A small brass handle is attached at mid-height.
    /// </summary>
    /// <param name="baseMicrovoxel">Bottom microvoxel of the door opening.</param>
    /// <param name="wallMaterial">Material type of the surrounding wall (for color matching).</param>
    /// <param name="outwardNormal">Outward-facing direction from the build zone.</param>
    /// <returns>A MeshInstance3D ready to be added to the scene tree, or null on failure.</returns>
    private static MeshInstance3D? BuildDoorMesh(
        Vector3I baseMicrovoxel,
        VoxelMaterialType wallMaterial,
        Vector3 outwardNormal)
    {
        float mv = GameConfig.MicrovoxelMeters; // 0.5 m per microvoxel

        // Door panel dimensions in meters
        float panelWidth = 1f * mv;                    // 1 microvoxel wide
        float panelHeight = DoorHeight * mv;           // 3 microvoxels tall
        float panelDepth = 0.1f * mv;                  // thin slab (0.1 microvoxels deep)

        // Handle dimensions in meters (1/4 scale of a voxel)
        float handleSize = 0.25f * mv;
        float handleProtrusion = 0.15f * mv;           // how far the handle sticks out

        // World position of the base microvoxel center-bottom
        Vector3 worldBase = new Vector3(
            baseMicrovoxel.X * mv,
            baseMicrovoxel.Y * mv,
            baseMicrovoxel.Z * mv);

        // --- Determine panel orientation ---
        // The panel should be placed flush against the outward face of the opening.
        // "depth" axis = outward normal direction, "width" and "height" are the other two axes.

        bool facesX = Mathf.Abs(outwardNormal.X) > 0.5f;
        // facesX => panel is in the YZ plane; else panel is in the XY plane (faces Z)

        // Panel center relative to worldBase:
        // Height center is halfway up the 3-tall opening.
        // Width center is at the voxel column center (+ 0.5 microvoxel).
        // Depth is flush with the outward face of the voxel.
        Vector3 panelCenter;
        Vector3 panelScale;
        Vector3 handleOffset;

        if (facesX)
        {
            // Door faces along X axis. Panel lies in the YZ plane.
            // Width runs along Z, height along Y, depth along X.
            float xFace = outwardNormal.X > 0
                ? worldBase.X + mv             // +X face: far side of the voxel
                : worldBase.X;                 // -X face: near side of the voxel
            float depthSign = outwardNormal.X > 0 ? 1f : -1f;

            panelCenter = new Vector3(
                xFace + depthSign * panelDepth * 0.5f,
                worldBase.Y + panelHeight * 0.5f,
                worldBase.Z + mv * 0.5f);

            // BoxMesh Size = (depth, height, width) in local space, then we don't rotate
            panelScale = new Vector3(panelDepth, panelHeight, panelWidth);

            // Handle at mid-height, offset to the right side of the door (positive Z),
            // protruding outward along X
            handleOffset = new Vector3(
                depthSign * (panelDepth * 0.5f + handleProtrusion * 0.5f),
                0f,
                panelWidth * 0.2f);
        }
        else
        {
            // Door faces along Z axis. Panel lies in the XY plane.
            // Width runs along X, height along Y, depth along Z.
            float zFace = outwardNormal.Z > 0
                ? worldBase.Z + mv             // +Z face: far side of the voxel
                : worldBase.Z;                 // -Z face: near side of the voxel
            float depthSign = outwardNormal.Z > 0 ? 1f : -1f;

            panelCenter = new Vector3(
                worldBase.X + mv * 0.5f,
                worldBase.Y + panelHeight * 0.5f,
                zFace + depthSign * panelDepth * 0.5f);

            panelScale = new Vector3(panelWidth, panelHeight, panelDepth);

            handleOffset = new Vector3(
                panelWidth * 0.2f,
                0f,
                depthSign * (panelDepth * 0.5f + handleProtrusion * 0.5f));
        }

        // --- Build the combined mesh ---
        Color panelColor = VoxelMaterials.GetPreviewColor(wallMaterial);

        // Panel geometry (box)
        BoxMesh panelBox = new BoxMesh();
        panelBox.Size = panelScale;

        // Handle geometry (small box)
        BoxMesh handleBox = new BoxMesh();
        handleBox.Size = new Vector3(handleSize, handleSize, handleSize);

        // Combine into a single ArrayMesh with two surfaces:
        // Surface 0 = door panel, Surface 1 = handle
        ArrayMesh combinedMesh = new ArrayMesh();

        // Surface 0: door panel
        {
            Godot.Collections.Array panelArrays = panelBox.SurfaceGetArrays(0);
            // Offset vertices are at origin; the MeshInstance3D position handles placement.
            combinedMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, panelArrays);

            StandardMaterial3D panelMat = new StandardMaterial3D();
            panelMat.AlbedoColor = panelColor;
            panelMat.Roughness = 0.75f;
            panelMat.Metallic = 0.0f;
            combinedMesh.SurfaceSetMaterial(0, panelMat);
        }

        // Build the door MeshInstance3D
        MeshInstance3D doorMeshInstance = new MeshInstance3D();
        doorMeshInstance.Name = "DoorPanel";
        doorMeshInstance.Mesh = combinedMesh;
        doorMeshInstance.Position = panelCenter;
        doorMeshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // Surface 1: handle — as a child MeshInstance3D so we can offset it easily
        {
            MeshInstance3D handleInstance = new MeshInstance3D();
            handleInstance.Name = "DoorHandle";
            handleInstance.Mesh = handleBox;
            handleInstance.Position = handleOffset;
            handleInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            StandardMaterial3D handleMat = new StandardMaterial3D();
            handleMat.AlbedoColor = HandleColor;
            handleMat.Roughness = 0.4f;
            handleMat.Metallic = 0.7f;
            handleInstance.MaterialOverride = handleMat;

            doorMeshInstance.AddChild(handleInstance);
        }

        return doorMeshInstance;
    }
}
