using Godot;
using System.Text.Json;

namespace VoxelSiege.Utility;

public partial class SaveSystem : Node
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    public static void SaveJson<T>(string userRelativePath, T data)
    {
        string absolutePath = ProjectSettings.GlobalizePath(userRelativePath);
        string? parent = System.IO.Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            System.IO.Directory.CreateDirectory(parent);
        }

        string json = JsonSerializer.Serialize(data, JsonOptions);
        System.IO.File.WriteAllText(absolutePath, json);
    }

    public static T? LoadJson<T>(string userRelativePath)
    {
        string absolutePath = ProjectSettings.GlobalizePath(userRelativePath);
        if (!System.IO.File.Exists(absolutePath))
        {
            return default;
        }

        string json = System.IO.File.ReadAllText(absolutePath);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
