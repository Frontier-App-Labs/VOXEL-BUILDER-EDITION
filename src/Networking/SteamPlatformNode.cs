using Godot;
using VoxelSiege.Networking.Steam;

namespace VoxelSiege.Networking;

/// <summary>
/// Wraps SteamManager and exposes the old ISteamPlatform interface for backward compat.
/// The real Steam init happens in SteamManager (child node created on demand).
/// </summary>
public partial class SteamPlatformNode : Node
{
    public ISteamPlatform Platform { get; private set; } = new NullSteamPlatform();
    public SteamManager? Steam { get; private set; }

    public bool Initialize()
    {
        // Idempotent — only init once even if called from multiple places
        if (Steam != null)
        {
            return Steam.IsInitialized;
        }

        // Create and add SteamManager as a child node
        Steam = new SteamManager();
        Steam.Name = "SteamManager";
        AddChild(Steam);

        if (Steam.IsInitialized)
        {
            Platform = new FacepunchSteamPlatform(Steam);
            GD.Print("[SteamPlatformNode] Steam initialized via Facepunch.Steamworks!");
            return true;
        }
        else
        {
            GD.PrintErr("[SteamPlatformNode] Steam not available — falling back to ENet.");
            Platform = new NullSteamPlatform();
            return false;
        }
    }

    public override void _Process(double delta)
    {
        // SteamManager handles its own _Process for RunCallbacks
    }
}
