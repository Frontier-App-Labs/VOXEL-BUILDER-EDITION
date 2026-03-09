using Godot;
using System;
using System.Reflection;

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

public sealed class ReflectionSteamPlatform : ISteamPlatform
{
    private readonly Type? _steamType;
    private readonly MethodInfo? _initMethod;
    private readonly MethodInfo? _runCallbacksMethod;
    private readonly MethodInfo? _isRunningMethod;
    private readonly MethodInfo? _setRichPresenceMethod;

    public ReflectionSteamPlatform()
    {
        _steamType = Type.GetType("Steam, GodotSteam") ?? Type.GetType("GodotSteam.Steam, GodotSteam");
        if (_steamType != null)
        {
            _initMethod = _steamType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
            _runCallbacksMethod = _steamType.GetMethod("RunCallbacks", BindingFlags.Public | BindingFlags.Static);
            _isRunningMethod = _steamType.GetMethod("IsSteamRunning", BindingFlags.Public | BindingFlags.Static);
            _setRichPresenceMethod = _steamType.GetMethod("SetRichPresence", BindingFlags.Public | BindingFlags.Static);
        }
    }

    public bool IsAvailable => _steamType != null;
    public bool IsRunning => _isRunningMethod?.Invoke(null, null) as bool? ?? false;

    public bool Init()
    {
        if (_initMethod == null)
        {
            return false;
        }

        object? result = _initMethod.Invoke(null, null);
        return result as bool? ?? true;
    }

    public void PumpCallbacks()
    {
        _runCallbacksMethod?.Invoke(null, null);
    }

    public bool SetRichPresence(string key, string value)
    {
        if (_setRichPresenceMethod == null)
        {
            return false;
        }

        object? result = _setRichPresenceMethod.Invoke(null, new object[] { key, value });
        return result as bool? ?? true;
    }
}

