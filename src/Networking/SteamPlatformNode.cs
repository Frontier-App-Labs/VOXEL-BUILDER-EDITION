using Godot;

namespace VoxelSiege.Networking;

public partial class SteamPlatformNode : Node
{
    public ISteamPlatform Platform { get; private set; } = new ReflectionSteamPlatform();

    public bool Initialize()
    {
        if (!Platform.IsAvailable)
        {
            Platform = new NullSteamPlatform();
            return false;
        }

        bool initialized = Platform.Init();
        if (!initialized)
        {
            Platform = new NullSteamPlatform();
        }

        return initialized;
    }

    public override void _Process(double delta)
    {
        Platform.PumpCallbacks();
    }
}
