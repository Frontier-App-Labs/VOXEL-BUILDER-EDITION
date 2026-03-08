using Godot;

namespace VoxelSiege.Core;

public partial class AudioDirector : Node
{
    private AudioStreamPlayer? _musicPlayer;
    private AudioStreamPlayer? _uiPlayer;

    public override void _Ready()
    {
        _musicPlayer = GetNodeOrNull<AudioStreamPlayer>("Music") ?? new AudioStreamPlayer();
        if (_musicPlayer.GetParent() == null)
        {
            _musicPlayer.Name = "Music";
            AddChild(_musicPlayer);
        }

        _uiPlayer = GetNodeOrNull<AudioStreamPlayer>("UI") ?? new AudioStreamPlayer();
        if (_uiPlayer.GetParent() == null)
        {
            _uiPlayer.Name = "UI";
            AddChild(_uiPlayer);
        }
    }

    public void PlayMenuMusic(AudioStream? stream)
    {
        if (_musicPlayer == null)
        {
            return;
        }

        _musicPlayer.Stream = stream;
        _musicPlayer.Play();
    }

    public void PlayUiSound(AudioStream? stream)
    {
        if (_uiPlayer == null)
        {
            return;
        }

        _uiPlayer.Stream = stream;
        _uiPlayer.Play();
    }
}
