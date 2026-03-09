using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.Networking;

public partial class SteamLobbyAdapter : Node
{
    [Export]
    public NodePath? SteamPlatformPath { get; set; }

    public bool IsSteamBacked { get; private set; }

    public override void _Ready()
    {
        SteamPlatformNode? steamNode = SteamPlatformPath is null ? GetTree().Root.GetNodeOrNull<SteamPlatformNode>("Main/SteamPlatform") : GetNodeOrNull<SteamPlatformNode>(SteamPlatformPath);
        IsSteamBacked = steamNode?.Initialize() ?? false;
    }

    public bool UpdatePresence(GamePhase phase, int round)
    {
        SteamPlatformNode? steamNode = SteamPlatformPath is null ? GetTree().Root.GetNodeOrNull<SteamPlatformNode>("Main/SteamPlatform") : GetNodeOrNull<SteamPlatformNode>(SteamPlatformPath);
        if (steamNode == null)
        {
            return false;
        }

        return steamNode.Platform.SetRichPresence("status", $"{phase} Round {round}");
    }
}
