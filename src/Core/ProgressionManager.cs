using Godot;
using VoxelSiege.Utility;

namespace VoxelSiege.Core;

public partial class ProgressionManager : Node
{
    private const string ProfilePath = "user://profile/player_profile.json";

    public PlayerProfile Profile { get; private set; } = new PlayerProfile();

    public override void _Ready()
    {
        Profile = SaveSystem.LoadJson<PlayerProfile>(ProfilePath) ?? new PlayerProfile();
        if (EventBus.Instance != null)
        {
            EventBus.Instance.VoxelChanged += OnVoxelChanged;
            EventBus.Instance.CommanderKilled += OnCommanderKilled;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.VoxelChanged -= OnVoxelChanged;
            EventBus.Instance.CommanderKilled -= OnCommanderKilled;
        }

        Save();
    }

    public void AwardMatchCompleted(bool won)
    {
        AddXp(GameConfig.XPPerMatch + (won ? GameConfig.XPPerWin : 0));
        if (won)
        {
            Profile.Wins++;
        }
        else
        {
            Profile.Losses++;
        }

        Save();
    }

    private void OnVoxelChanged(VoxelChangeEvent payload)
    {
        if (payload.BeforeData == 0 && payload.AfterData != 0)
        {
            Profile.TotalBlocksPlaced++;
        }
        else if (payload.BeforeData != 0 && payload.AfterData == 0)
        {
            Profile.TotalBlocksDestroyed++;
            if (Profile.TotalBlocksDestroyed % 100 == 0)
            {
                AddXp(GameConfig.XPPerVoxelDestroyed);
            }
        }
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        AddXp(GameConfig.XPPerKill);
    }

    private void AddXp(int xp)
    {
        Profile.TotalXp += xp;
        Profile.Level = System.Math.Max(1, (Profile.TotalXp / GameConfig.XPPerLevel) + 1);
    }

    private void Save()
    {
        SaveSystem.SaveJson(ProfilePath, Profile);
    }
}
