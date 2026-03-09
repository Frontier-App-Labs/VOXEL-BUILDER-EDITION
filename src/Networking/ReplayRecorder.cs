using Godot;
using System.Collections.Generic;
using VoxelSiege.Utility;

namespace VoxelSiege.Networking;

public sealed class ReplayFrame
{
    public double TimeSeconds { get; set; }
    public NetMessage Message { get; set; }
}

public partial class ReplayRecorder : Node
{
    private readonly List<ReplayFrame> _frames = new List<ReplayFrame>();
    private double _startTime;

    public void BeginRecording()
    {
        _frames.Clear();
        _startTime = Time.GetTicksMsec() / 1000.0;
    }

    public void Record(NetMessage message)
    {
        _frames.Add(new ReplayFrame
        {
            TimeSeconds = (Time.GetTicksMsec() / 1000.0) - _startTime,
            Message = message,
        });
    }

    public void Save(string replayName)
    {
        SaveSystem.SaveJson($"user://replays/{replayName}.json", _frames);
    }

    public List<ReplayFrame>? Load(string replayName)
    {
        return SaveSystem.LoadJson<List<ReplayFrame>>($"user://replays/{replayName}.json");
    }
}
