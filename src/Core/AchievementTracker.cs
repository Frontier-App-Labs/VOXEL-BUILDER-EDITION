using Godot;
using System.Collections.Generic;
using VoxelSiege.Networking;
using VoxelSiege.Utility;

namespace VoxelSiege.Core;

public partial class AchievementTracker : Node
{
    private const string AchievementPath = "user://profile/achievements.json";
    private readonly HashSet<string> _unlocked = new HashSet<string>();

    [Export]
    public NodePath? SteamPlatformPath { get; set; }

    public override void _Ready()
    {
        HashSet<string>? stored = SaveSystem.LoadJson<HashSet<string>>(AchievementPath);
        if (stored != null)
        {
            _unlocked.UnionWith(stored);
        }

        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled += OnCommanderKilled;
            EventBus.Instance.VoxelChanged += OnVoxelChanged;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled -= OnCommanderKilled;
            EventBus.Instance.VoxelChanged -= OnVoxelChanged;
        }

        SaveSystem.SaveJson(AchievementPath, _unlocked);
    }

    public bool IsUnlocked(string id) => _unlocked.Contains(id);

    public void Unlock(string id)
    {
        if (!_unlocked.Add(id))
        {
            return;
        }

        SteamPlatformNode? steam = SteamPlatformPath is null ? GetTree().Root.GetNodeOrNull<SteamPlatformNode>("Main/SteamPlatform") : GetNodeOrNull<SteamPlatformNode>(SteamPlatformPath);
        steam?.Platform.SetRichPresence("achievement", id);
        SaveSystem.SaveJson(AchievementPath, _unlocked);
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        Unlock("ragdoll_royale");
    }

    private void OnVoxelChanged(VoxelChangeEvent payload)
    {
        if (payload.BeforeData != 0 && payload.AfterData == 0)
        {
            Unlock("first_blood");
        }
    }
}
