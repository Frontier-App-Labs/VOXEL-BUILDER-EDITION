using Godot;
using System.Collections.Generic;
using VoxelSiege.FX;
using VoxelSiege.Voxel;
using VoxelSiege.Core;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Army;

public sealed class DoorPlacement
{
    public PlayerSlot Owner { get; init; }
    public Vector3I BaseMicrovoxel { get; init; }  // bottom of the 1xDoorHeight column
    public List<Vector3I> OpeningVoxels { get; init; } = new(); // the 1x4 column of door-marked voxels

    /// <summary>
    /// Visual door panel mesh parented to the VoxelWorld node. Freed on removal.
    /// </summary>
    public MeshInstance3D? DoorMesh { get; set; }

    /// <summary>The outward-facing direction of the door (set during placement).</summary>
    public Vector3 OutwardNormal { get; init; } = Vector3.Forward;

    /// <summary>Hit points remaining. When zero, the door is destroyed.</summary>
    public int HitPoints { get; set; } = 30;
    public const int MaxHitPoints = 30;
    public bool IsDestroyed => HitPoints <= 0;

    /// <summary>Stored voxel grid for destruction breakup effect.</summary>
    public Color?[,,]? VoxelGrid { get; set; }
}

public class DoorRegistry
{
    private readonly Dictionary<PlayerSlot, List<DoorPlacement>> _doors = new();
    public const int MaxDoorsPerPlayer = 4;
    public const int DoorHeight = 4; // microvoxels tall (2 large blocks)
    public const int DoorWidth = 2;  // microvoxels wide (1 large block = 2 build units)

    /// <summary>
    /// Attempts to place a door at the given base microvoxel position.
    /// Marks a 1-wide x DoorHeight-tall column as a door (voxels stay solid for LOS blocking).
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
        int rotationHint,
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

        // Check: Y range is within build zone (with a small margin)
        if (baseMicrovoxel.Y < zoneMin.Y - 1 || baseMicrovoxel.Y + DoorHeight - 1 > zoneMax.Y + 1)
        {
            failReason = "Door opening extends outside build zone vertically.";
            return false;
        }

        // Determine which zone edge the door is on (if any) for mesh orientation.
        // Doors can now be placed anywhere, not just zone edges.
        bool onMinX = baseMicrovoxel.X == zoneMin.X;
        bool onMaxX = baseMicrovoxel.X == zoneMax.X;
        bool onMinZ = baseMicrovoxel.Z == zoneMin.Z;
        bool onMaxZ = baseMicrovoxel.Z == zoneMax.Z;

        // Determine door facing direction first (needed to compute width axis).
        // rotationHint: -1=auto-detect, 0=Forward, 1=Right, 2=Back, 3=Left
        Vector3 outwardNormal;
        if (rotationHint >= 0)
        {
            outwardNormal = rotationHint switch
            {
                0 => Vector3.Forward,
                1 => Vector3.Right,
                2 => Vector3.Back,
                3 => Vector3.Left,
                _ => Vector3.Forward,
            };
        }
        else
        {
            // Auto-detect: prefer zone edge, then adjacent blocks
            if (onMinX) outwardNormal = Vector3.Left;
            else if (onMaxX) outwardNormal = Vector3.Right;
            else if (onMinZ) outwardNormal = Vector3.Forward;
            else if (onMaxZ) outwardNormal = Vector3.Back;
            else
            {
                bool solidPosX = world.GetVoxel(baseMicrovoxel + new Vector3I(1, 0, 0)).IsSolid;
                bool solidNegX = world.GetVoxel(baseMicrovoxel + new Vector3I(-1, 0, 0)).IsSolid;
                bool solidPosZ = world.GetVoxel(baseMicrovoxel + new Vector3I(0, 0, 1)).IsSolid;
                bool solidNegZ = world.GetVoxel(baseMicrovoxel + new Vector3I(0, 0, -1)).IsSolid;

                // Wall runs along X (neighbors in X are solid) → door faces along Z (perpendicular)
                if (solidPosX || solidNegX)
                    outwardNormal = solidPosZ ? Vector3.Back : Vector3.Forward;
                // Wall runs along Z (neighbors in Z are solid) → door faces along X (perpendicular)
                // Face away from the solid side: if +Z is solid, face -X (Left); if -Z is solid, face +X (Right)
                else if (solidPosZ || solidNegZ)
                    outwardNormal = solidNegZ ? Vector3.Right : Vector3.Left;
                else
                    outwardNormal = Vector3.Forward;
            }
        }

        // Compute the width axis (perpendicular to outward normal, in the horizontal plane)
        bool facesX = Mathf.Abs(outwardNormal.X) > 0.5f;
        Vector3I widthStep = facesX ? new Vector3I(0, 0, 1) : new Vector3I(1, 0, 0);

        // Collect the 2-wide x 8-tall grid of opening voxels
        var openingVoxels = new List<Vector3I>(DoorHeight * DoorWidth);
        for (int dw = 0; dw < DoorWidth; dw++)
        {
            for (int dy = 0; dy < DoorHeight; dy++)
            {
                openingVoxels.Add(baseMicrovoxel + widthStep * dw + new Vector3I(0, dy, 0));
            }
        }

        // Check: at least one voxel is solid
        bool anySolid = false;
        foreach (Vector3I pos in openingVoxels)
        {
            VoxelValue voxel = world.GetVoxel(pos);
            if (voxel.Material == VoxelMaterialType.Foundation)
            {
                failReason = "Cannot carve a door through foundation blocks.";
                return false;
            }
            if (voxel.IsSolid) anySolid = true;
        }
        if (!anySolid)
        {
            failReason = "Need at least one solid block to carve a door.";
            return false;
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

        // Sample the wall material for the door panel color
        VoxelMaterialType wallMaterial = world.GetVoxel(openingVoxels[0]).Material;

        // Build the visual door mesh and parent it to the VoxelWorld node
        Color?[,,]? doorVoxelGrid;
        MeshInstance3D? doorMesh = BuildDoorMesh(baseMicrovoxel, wallMaterial, outwardNormal, out doorVoxelGrid);
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
            OutwardNormal = outwardNormal,
            DoorMesh = doorMesh,
            VoxelGrid = doorVoxelGrid,
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
        Vector3I step = GetDoorOutwardStep(door, zoneMin, zoneMax);
        return basePos + step;
    }

    /// <summary>
    /// Gets the inside position adjacent to a door (one step inside the base wall).
    /// The returned microvoxel is on the interior side of the door, at foot level.
    /// </summary>
    public Vector3I GetDoorInteriorPosition(DoorPlacement door, Vector3I zoneMin, Vector3I zoneMax)
    {
        Vector3I basePos = door.BaseMicrovoxel;
        Vector3I step = GetDoorOutwardStep(door, zoneMin, zoneMax);
        return basePos - step; // opposite of outward = inward
    }

    /// <summary>
    /// Returns a single-voxel step in the outward direction from the door.
    /// Checks zone edges first, then uses the stored door facing direction.
    /// </summary>
    private static Vector3I GetDoorOutwardStep(DoorPlacement door, Vector3I zoneMin, Vector3I zoneMax)
    {
        Vector3I basePos = door.BaseMicrovoxel;
        if (basePos.X == zoneMin.X) return new Vector3I(-1, 0, 0);
        if (basePos.X == zoneMax.X) return new Vector3I(1, 0, 0);
        if (basePos.Z == zoneMin.Z) return new Vector3I(0, 0, -1);
        if (basePos.Z == zoneMax.Z) return new Vector3I(0, 0, 1);

        // Interior door: use the stored outward normal from placement
        Vector3 n = door.OutwardNormal;
        return new Vector3I(
            Mathf.RoundToInt(n.X),
            0,
            Mathf.RoundToInt(n.Z));
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
    /// Damages all doors within the blast radius. Destroyed doors break into voxel debris.
    /// </summary>
    public void DamageDoorsInRadius(Vector3 explosionPos, float radiusMicrovoxels, int baseDamage, Node sceneRoot)
    {
        float radiusMeters = radiusMicrovoxels * GameConfig.MicrovoxelMeters;

        foreach ((PlayerSlot _, List<DoorPlacement> doors) in _doors)
        {
            for (int i = doors.Count - 1; i >= 0; i--)
            {
                DoorPlacement door = doors[i];
                if (door.IsDestroyed) continue;

                // Door world position (center of the opening)
                float mv = GameConfig.MicrovoxelMeters;
                Vector3 doorCenter = new Vector3(
                    door.BaseMicrovoxel.X * mv + mv * 0.5f,
                    (door.BaseMicrovoxel.Y + DoorHeight * 0.5f) * mv,
                    door.BaseMicrovoxel.Z * mv + mv * 0.5f);

                float dist = doorCenter.DistanceTo(explosionPos);
                if (dist > radiusMeters) continue;

                // Linear falloff damage
                float falloff = 1f - Mathf.Clamp(dist / radiusMeters, 0f, 1f);
                int damage = (int)(baseDamage * falloff);
                if (damage <= 0) continue;

                door.HitPoints -= damage;
                if (door.HitPoints <= 0)
                {
                    door.HitPoints = 0;
                    GD.Print($"[Door] Door at {door.BaseMicrovoxel} destroyed!");

                    // Break into voxel pieces using effective voxel size
                    // (door mesh is scaled up from 0.04m voxels to fill the opening)
                    if (door.VoxelGrid != null && door.DoorMesh != null)
                    {
                        Vector3 meshPos = door.DoorMesh.GlobalPosition;
                        float effectiveVoxelSize = GameConfig.MicrovoxelMeters / 4f; // 4 voxels span 1 microvoxel width
                        FallingChunk.CreateFromWeaponVoxels(
                            door.VoxelGrid, effectiveVoxelSize, meshPos, explosionPos, sceneRoot);
                    }

                    FreeDoorMesh(door);
                    doors.RemoveAt(i);
                }
            }
        }
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
    private static readonly Color HingeColor = new Color(0.35f, 0.35f, 0.38f); // dark metal
    private static readonly Color PlanksLight = new Color(0.55f, 0.38f, 0.22f); // warm wood
    private static readonly Color PlanksDark = new Color(0.40f, 0.26f, 0.14f); // darker wood grain
    private static readonly Color IronBand = new Color(0.28f, 0.28f, 0.30f); // iron reinforcement

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
    /// Builds a procedural voxel-style door panel using VoxelModelBuilder.
    /// Creates a chunky medieval/fortified door with wooden planks, iron bands,
    /// and a brass handle. Matches the game's voxel art style.
    /// </summary>
    private static MeshInstance3D? BuildDoorMesh(
        Vector3I baseMicrovoxel,
        VoxelMaterialType wallMaterial,
        Vector3 outwardNormal,
        out Color?[,,]? voxelGridOut)
    {
        float mv = GameConfig.MicrovoxelMeters;

        // Build a voxel door: 8 wide x 12 tall x 2 deep (at 0.04m per voxel)
        // Fills a 2-microvoxel-wide x 4-microvoxel-tall opening (1 large block wide, 2 large blocks tall)
        const int dw = 8;  // width (across opening) — 2 microvoxels
        const int dh = 12; // height (4 microvoxels * 3 voxels each)
        const int dd = 2;  // depth (thin door)
        const float voxelSize = 0.04f;

        Color?[,,] v = new Color?[dw, dh, dd];

        // Fill base planks (alternating light/dark for wood grain effect)
        for (int x = 0; x < dw; x++)
        {
            for (int y = 0; y < dh; y++)
            {
                for (int z = 0; z < dd; z++)
                {
                    v[x, y, z] = (x + y) % 2 == 0 ? PlanksLight : PlanksDark;
                }
            }
        }

        // Iron reinforcement bands across the door (evenly spaced)
        for (int band = 1; band < dh; band += 4)
        {
            for (int x = 0; x < dw; x++)
            {
                v[x, band, 0] = IronBand;
                v[x, band, 1] = IronBand;
            }
        }

        // Hinges on one side (left edge, at band positions)
        v[0, 1, 0] = HingeColor;
        v[0, 5, 0] = HingeColor;
        v[0, 9, 0] = HingeColor;

        // Brass door handle (right side, mid-height, front face)
        v[dw - 1, 5, 0] = HandleColor;
        v[dw - 1, 6, 0] = HandleColor;

        // Small keyhole below handle
        v[dw - 1, 4, 0] = new Color(0.08f, 0.08f, 0.10f);

        voxelGridOut = (Color?[,,])v.Clone();

        // Use VoxelPalette texture system (same as weapons/commander) for
        // consistent rendering with all other voxel art in the game.
        var palette = new Art.VoxelPalette();
        palette.AddColors(v);
        palette.Build();

        var builder = new Art.VoxelModelBuilder()
        {
            VoxelSize = voxelSize,
            JitterAmount = 0.002f,
            OriginOffset = new Vector3(-dw * 0.5f * voxelSize, 0, -dd * 0.5f * voxelSize),
        };

        ArrayMesh doorMesh = builder.BuildMesh(v, palette);

        // Position the door in the opening
        float panelHeight = DoorHeight * mv;

        Vector3 worldBase = new Vector3(
            baseMicrovoxel.X * mv,
            baseMicrovoxel.Y * mv,
            baseMicrovoxel.Z * mv);

        bool facesX = Mathf.Abs(outwardNormal.X) > 0.5f;

        MeshInstance3D doorMeshInstance = new MeshInstance3D();
        doorMeshInstance.Name = "DoorPanel";
        doorMeshInstance.Mesh = doorMesh;
        doorMeshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // Small offset to push door mesh slightly out from the wall face (prevents Z-fighting)
        const float wallOffset = 0.02f;

        float doorWidthMeters = DoorWidth * mv;
        float halfWidth = doorWidthMeters * 0.5f;

        if (facesX)
        {
            float xFace = outwardNormal.X > 0 ? worldBase.X + mv + wallOffset : worldBase.X - wallOffset;
            doorMeshInstance.Position = new Vector3(
                xFace,
                worldBase.Y,
                worldBase.Z + halfWidth);
            // Rotate 90° around Y so door width runs along Z
            doorMeshInstance.RotationDegrees = new Vector3(0, 90, 0);
        }
        else
        {
            float zFace = outwardNormal.Z > 0 ? worldBase.Z + mv + wallOffset : worldBase.Z - wallOffset;
            doorMeshInstance.Position = new Vector3(
                worldBase.X + halfWidth,
                worldBase.Y,
                zFace);
            // No rotation needed — door width runs along X by default
        }

        // Scale to fit the opening (door voxels are small, scale up to match microvoxels)
        float scaleX = doorWidthMeters / (dw * voxelSize);
        float scaleY = panelHeight / (dh * voxelSize);
        float scaleZ = (mv * 0.15f) / (dd * voxelSize); // thin depth
        doorMeshInstance.Scale = new Vector3(scaleX, scaleY, scaleZ);

        // Use palette texture material (same pipeline as weapons/commander)
        // with slight emission so doors are visible in shadows
        StandardMaterial3D mat = palette.CreateMaterial();
        mat.EmissionEnabled = true;
        mat.Emission = PlanksLight;
        mat.EmissionEnergyMultiplier = 0.05f;
        doorMeshInstance.MaterialOverride = mat;

        return doorMeshInstance;
    }
}
