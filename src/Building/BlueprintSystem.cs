using Godot;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoxelSiege.Army;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Building;

public sealed class BlueprintVoxelData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Data { get; set; }
}

public sealed class BlueprintWeaponData
{
    public string WeaponId { get; set; } = string.Empty;
    public Vector3I BuildUnitPosition { get; set; }
    /// <summary>Weapon Y rotation in radians (from manual R-key rotation).</summary>
    public float RotationY { get; set; }
}

public sealed class BlueprintDoorData
{
    /// <summary>Door base microvoxel position relative to zone origin.</summary>
    public Vector3I RelativeMicrovoxel { get; set; }
    /// <summary>Rotation hint: 0=Forward, 1=Right, 2=Back, 3=Left.</summary>
    public int RotationHint { get; set; }
}

public sealed class BlueprintTroopData
{
    public TroopType Type { get; set; }
    /// <summary>Placed microvoxel position relative to zone origin (null if unplaced).</summary>
    public Vector3I? RelativeMicrovoxel { get; set; }
}

public sealed class BlueprintData
{
    public string Name { get; set; } = string.Empty;
    public Vector3I DimensionsBuildUnits { get; set; }
    public List<BlueprintVoxelData> Voxels { get; set; } = new List<BlueprintVoxelData>();
    public List<BlueprintWeaponData> Weapons { get; set; } = new List<BlueprintWeaponData>();
    public List<BlueprintDoorData> Doors { get; set; } = new List<BlueprintDoorData>();
    public List<BlueprintTroopData> Troops { get; set; } = new List<BlueprintTroopData>();
    public List<PowerupType> Powerups { get; set; } = new List<PowerupType>();
    public Vector3I? CommanderBuildUnitPosition { get; set; }
    /// <summary>Commander Y rotation in radians.</summary>
    public float CommanderRotationY { get; set; }
}

/// <summary>
/// Portable export format using only primitive types — no Godot structs.
/// Guarantees identical deserialization across any machine.
/// </summary>
public sealed class ExportedBlueprint
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public int DimX { get; set; }
    public int DimY { get; set; }
    public int DimZ { get; set; }
    public List<BlueprintVoxelData> Voxels { get; set; } = new List<BlueprintVoxelData>();
    public List<ExportedWeaponData> Weapons { get; set; } = new List<ExportedWeaponData>();
    public List<ExportedDoorData> Doors { get; set; } = new List<ExportedDoorData>();
    public List<ExportedTroopData> Troops { get; set; } = new List<ExportedTroopData>();
    public List<string> Powerups { get; set; } = new List<string>();
    public int? CommanderX { get; set; }
    public int? CommanderY { get; set; }
    public int? CommanderZ { get; set; }
    /// <summary>Commander Y rotation in radians.</summary>
    public float CommanderRotationY { get; set; }
    /// <summary>SHA-256 of the voxel + weapon payload for integrity verification.</summary>
    public string Checksum { get; set; } = string.Empty;
}

public sealed class ExportedWeaponData
{
    public string WeaponId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Weapon Y rotation in radians.</summary>
    public float RotationY { get; set; }
}

public sealed class ExportedDoorData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Rotation hint: 0=Forward, 1=Right, 2=Back, 3=Left.</summary>
    public int RotationHint { get; set; }
}

public sealed class ExportedTroopData
{
    public string TroopType { get; set; } = string.Empty;
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Z { get; set; }
}

public partial class BlueprintSystem : Node
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    public string MakeBlueprintPath(string name)
    {
        return $"user://blueprints/{name.ToLowerInvariant().Replace(' ', '_')}.json";
    }

    public BlueprintData Capture(VoxelWorld world, BuildZone zone, string name)
    {
        BlueprintData blueprint = new BlueprintData
        {
            Name = name,
            DimensionsBuildUnits = zone.SizeBuildUnits,
        };

        for (int z = zone.OriginMicrovoxels.Z; z <= zone.MaxMicrovoxelsInclusive.Z; z++)
        {
            for (int y = zone.OriginMicrovoxels.Y; y <= zone.MaxMicrovoxelsInclusive.Y; y++)
            {
                for (int x = zone.OriginMicrovoxels.X; x <= zone.MaxMicrovoxelsInclusive.X; x++)
                {
                    VoxelValue voxel = world.GetVoxel(new Vector3I(x, y, z));
                    if (voxel.IsAir)
                    {
                        continue;
                    }

                    blueprint.Voxels.Add(new BlueprintVoxelData
                    {
                        X = x - zone.OriginMicrovoxels.X,
                        Y = y - zone.OriginMicrovoxels.Y,
                        Z = z - zone.OriginMicrovoxels.Z,
                        Data = voxel.Data,
                    });
                }
            }
        }

        return blueprint;
    }

    public void SaveBlueprint(BlueprintData blueprint)
    {
        string path = MakeBlueprintPath(blueprint.Name);
        SaveSystem.SaveJson(path, blueprint);

        // Verify the file actually made it to disk
        string globalPath = ProjectSettings.GlobalizePath(path);
        if (System.IO.File.Exists(globalPath))
        {
            GD.Print($"[Blueprint] Saved '{blueprint.Name}' → {globalPath} ({new System.IO.FileInfo(globalPath).Length} bytes)");
        }
        else
        {
            GD.PrintErr($"[Blueprint] SAVE FAILED — file not found after write: {globalPath}");
        }
    }

    public BlueprintData? LoadBlueprint(string name)
    {
        return SaveSystem.LoadJson<BlueprintData>(MakeBlueprintPath(name));
    }

    /// <summary>
    /// Scans the user://blueprints/ directory for existing blueprint JSON files
    /// and returns their build names. Used as a fallback when the profile's
    /// SavedBuilds list is empty or out of sync.
    /// </summary>
    public List<string> ScanBlueprintFiles()
    {
        List<string> names = new();
        string dir = ProjectSettings.GlobalizePath("user://blueprints");
        if (!System.IO.Directory.Exists(dir))
            return names;

        foreach (string file in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                string json = System.IO.File.ReadAllText(file);
                BlueprintData? bp = System.Text.Json.JsonSerializer.Deserialize<BlueprintData>(json, ExportJsonOptions);
                if (bp != null && !string.IsNullOrEmpty(bp.Name))
                {
                    names.Add(bp.Name);
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Blueprint] Failed to read {file}: {ex.Message}");
            }
        }

        GD.Print($"[Blueprint] Disk scan found {names.Count} blueprint files in {dir}");
        return names;
    }

    // ── Export / Import ──────────────────────────────────────────────

    /// <summary>
    /// Converts internal BlueprintData to the portable export format and writes it to the given path.
    /// Returns true on success.
    /// </summary>
    public bool ExportBlueprint(BlueprintData blueprint, string absolutePath)
    {
        ExportedBlueprint export = new ExportedBlueprint
        {
            FormatVersion = 1,
            Name = blueprint.Name,
            DimX = blueprint.DimensionsBuildUnits.X,
            DimY = blueprint.DimensionsBuildUnits.Y,
            DimZ = blueprint.DimensionsBuildUnits.Z,
            Voxels = blueprint.Voxels,
        };

        // Convert weapons to primitive-only format
        foreach (BlueprintWeaponData w in blueprint.Weapons)
        {
            export.Weapons.Add(new ExportedWeaponData
            {
                WeaponId = w.WeaponId,
                X = w.BuildUnitPosition.X,
                Y = w.BuildUnitPosition.Y,
                Z = w.BuildUnitPosition.Z,
                RotationY = w.RotationY,
            });
        }

        // Convert doors to primitive-only format
        foreach (BlueprintDoorData d in blueprint.Doors)
        {
            export.Doors.Add(new ExportedDoorData
            {
                X = d.RelativeMicrovoxel.X,
                Y = d.RelativeMicrovoxel.Y,
                Z = d.RelativeMicrovoxel.Z,
                RotationHint = d.RotationHint,
            });
        }

        // Convert troops to primitive-only format
        foreach (BlueprintTroopData t in blueprint.Troops)
        {
            export.Troops.Add(new ExportedTroopData
            {
                TroopType = t.Type.ToString(),
                X = t.RelativeMicrovoxel?.X,
                Y = t.RelativeMicrovoxel?.Y,
                Z = t.RelativeMicrovoxel?.Z,
            });
        }

        // Convert powerups to string names
        foreach (PowerupType p in blueprint.Powerups)
        {
            export.Powerups.Add(p.ToString());
        }

        // Commander position + rotation
        if (blueprint.CommanderBuildUnitPosition.HasValue)
        {
            export.CommanderX = blueprint.CommanderBuildUnitPosition.Value.X;
            export.CommanderY = blueprint.CommanderBuildUnitPosition.Value.Y;
            export.CommanderZ = blueprint.CommanderBuildUnitPosition.Value.Z;
            export.CommanderRotationY = blueprint.CommanderRotationY;
        }

        // Compute checksum BEFORE setting it (checksum field excluded from hash)
        export.Checksum = ComputeChecksum(export);

        try
        {
            string? parent = System.IO.Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                System.IO.Directory.CreateDirectory(parent);
            }
            string json = JsonSerializer.Serialize(export, ExportJsonOptions);
            System.IO.File.WriteAllText(absolutePath, json);
            GD.Print($"[Export] Blueprint '{blueprint.Name}' exported to: {absolutePath}");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Export] Failed to export '{blueprint.Name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads an exported .vsbuild file and converts it back to internal BlueprintData.
    /// Returns null if the file is invalid or the checksum doesn't match.
    /// </summary>
    public BlueprintData? ImportBlueprint(string absolutePath)
    {
        try
        {
            if (!System.IO.File.Exists(absolutePath))
            {
                GD.PrintErr($"[Import] File not found: {absolutePath}");
                return null;
            }

            string json = System.IO.File.ReadAllText(absolutePath);
            ExportedBlueprint? export = JsonSerializer.Deserialize<ExportedBlueprint>(json, ExportJsonOptions);
            if (export == null)
            {
                GD.PrintErr("[Import] Failed to deserialize export file.");
                return null;
            }

            // Verify checksum
            string savedChecksum = export.Checksum;
            export.Checksum = string.Empty; // clear before recomputing
            string computedChecksum = ComputeChecksum(export);
            if (savedChecksum != computedChecksum)
            {
                GD.PrintErr($"[Import] Checksum mismatch! File may be corrupted or tampered with.");
                GD.PrintErr($"  Expected: {savedChecksum}");
                GD.PrintErr($"  Computed: {computedChecksum}");
                return null;
            }

            // Convert back to internal format
            BlueprintData blueprint = new BlueprintData
            {
                Name = export.Name,
                DimensionsBuildUnits = new Vector3I(export.DimX, export.DimY, export.DimZ),
                Voxels = export.Voxels,
            };

            foreach (ExportedWeaponData w in export.Weapons)
            {
                blueprint.Weapons.Add(new BlueprintWeaponData
                {
                    WeaponId = w.WeaponId,
                    BuildUnitPosition = new Vector3I(w.X, w.Y, w.Z),
                    RotationY = w.RotationY,
                });
            }

            // Convert doors back to internal format
            foreach (ExportedDoorData d in export.Doors)
            {
                blueprint.Doors.Add(new BlueprintDoorData
                {
                    RelativeMicrovoxel = new Vector3I(d.X, d.Y, d.Z),
                    RotationHint = d.RotationHint,
                });
            }

            // Convert troops back to internal format
            foreach (ExportedTroopData t in export.Troops)
            {
                if (System.Enum.TryParse<TroopType>(t.TroopType, out TroopType troopType))
                {
                    Vector3I? pos = (t.X.HasValue && t.Y.HasValue && t.Z.HasValue)
                        ? new Vector3I(t.X.Value, t.Y.Value, t.Z.Value)
                        : null;
                    blueprint.Troops.Add(new BlueprintTroopData
                    {
                        Type = troopType,
                        RelativeMicrovoxel = pos,
                    });
                }
            }

            // Convert powerups back to internal format
            foreach (string pName in export.Powerups)
            {
                if (System.Enum.TryParse<PowerupType>(pName, out PowerupType pType))
                {
                    blueprint.Powerups.Add(pType);
                }
            }

            if (export.CommanderX.HasValue && export.CommanderY.HasValue && export.CommanderZ.HasValue)
            {
                blueprint.CommanderBuildUnitPosition = new Vector3I(
                    export.CommanderX.Value, export.CommanderY.Value, export.CommanderZ.Value);
                blueprint.CommanderRotationY = export.CommanderRotationY;
            }

            GD.Print($"[Import] Blueprint '{blueprint.Name}' imported ({blueprint.Voxels.Count} voxels). Checksum verified.");
            return blueprint;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Import] Failed to import blueprint: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Computes a SHA-256 checksum over the deterministic content of an exported blueprint.
    /// The Checksum field itself must be empty when computing.
    /// </summary>
    private static string ComputeChecksum(ExportedBlueprint export)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"v{export.FormatVersion}|{export.Name}|{export.DimX},{export.DimY},{export.DimZ}|");

        // Voxels in list order (already deterministic from Z→Y→X capture loop)
        foreach (BlueprintVoxelData v in export.Voxels)
        {
            sb.Append($"{v.X},{v.Y},{v.Z},{v.Data};");
        }

        sb.Append('|');

        // Weapons (including rotation)
        foreach (ExportedWeaponData w in export.Weapons)
        {
            sb.Append($"{w.WeaponId},{w.X},{w.Y},{w.Z},{w.RotationY:R};");
        }

        sb.Append('|');

        // Doors
        foreach (ExportedDoorData d in export.Doors)
        {
            sb.Append($"{d.X},{d.Y},{d.Z},{d.RotationHint};");
        }

        sb.Append('|');

        // Troops
        foreach (ExportedTroopData t in export.Troops)
        {
            sb.Append($"{t.TroopType},{t.X},{t.Y},{t.Z};");
        }

        sb.Append('|');

        // Powerups
        foreach (string p in export.Powerups)
        {
            sb.Append($"{p};");
        }

        sb.Append('|');

        // Commander
        if (export.CommanderX.HasValue)
        {
            sb.Append($"{export.CommanderX},{export.CommanderY},{export.CommanderZ},{export.CommanderRotationY:R}");
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }
}
