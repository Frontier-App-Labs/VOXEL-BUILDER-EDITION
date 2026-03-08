using Godot;
using VoxelSiege.Utility;

namespace VoxelSiege.UI;

public sealed class GameSettingsData
{
    public string QualityPreset { get; set; } = "High";
    public bool VSync { get; set; } = true;
    public bool Bloom { get; set; } = true;
    public bool DepthOfField { get; set; }
    public int FpsCap { get; set; } = 120;
    public string CameraShake { get; set; } = "Full";
}

public partial class SettingsUI : Control
{
    private const string SettingsPath = "user://settings/game_settings.json";

    public GameSettingsData CurrentSettings { get; private set; } = new GameSettingsData();

    public override void _Ready()
    {
        CurrentSettings = SaveSystem.LoadJson<GameSettingsData>(SettingsPath) ?? new GameSettingsData();
    }

    public void SaveCurrentSettings()
    {
        SaveSystem.SaveJson(SettingsPath, CurrentSettings);
    }
}
