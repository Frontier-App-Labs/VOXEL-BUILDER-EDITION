using VoxelSiege.Networking.Steam;

namespace VoxelSiege.Networking;

public interface ISteamPlatform
{
    bool IsAvailable { get; }
    bool IsRunning { get; }
    bool Init();
    void PumpCallbacks();
    bool SetRichPresence(string key, string value);
}

public sealed class NullSteamPlatform : ISteamPlatform
{
    public bool IsAvailable => false;
    public bool IsRunning => false;
    public bool Init() => false;
    public void PumpCallbacks() { }
    public bool SetRichPresence(string key, string value) => false;
}

/// <summary>
/// Real Steam platform backed by Facepunch.Steamworks via SteamManager.
/// </summary>
public sealed class FacepunchSteamPlatform : ISteamPlatform
{
    private readonly SteamManager _steam;

    public FacepunchSteamPlatform(SteamManager steam)
    {
        _steam = steam;
    }

    public bool IsAvailable => _steam.IsInitialized;
    public bool IsRunning => _steam.IsInitialized;

    public bool Init() => _steam.IsInitialized;

    public void PumpCallbacks()
    {
        // SteamManager handles this in _Process
    }

    public bool SetRichPresence(string key, string value)
    {
        return _steam.SetRichPresence(key, value);
    }
}
