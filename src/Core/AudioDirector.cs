using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Core;

/// <summary>
/// Singleton audio manager. Listens to EventBus events and plays appropriate sounds.
/// Currently logs audio calls since no audio files exist yet.
/// Structure supports dropping in audio files later via the _sfxCache and _musicCache dictionaries.
/// </summary>
public partial class AudioDirector : Node
{
    private static AudioDirector? _instance;
    public static AudioDirector? Instance => _instance;

    private AudioStreamPlayer? _musicPlayer;
    private AudioStreamPlayer? _uiPlayer;
    private readonly Dictionary<string, AudioStream> _sfxCache = new Dictionary<string, AudioStream>();
    private readonly Dictionary<string, AudioStream> _musicCache = new Dictionary<string, AudioStream>();
    private string _currentMusicName = string.Empty;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }

        UnsubscribeEvents();
    }

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

        SubscribeEvents();
    }

    /// <summary>
    /// Play a sound effect by name. If position is provided, plays as 3D audio.
    /// Falls back to logging if no audio file is loaded for the given name.
    /// </summary>
    public void PlaySFX(string name, Vector3? position = null)
    {
        if (_sfxCache.TryGetValue(name, out AudioStream? stream) && stream != null)
        {
            if (position.HasValue)
            {
                AudioStreamPlayer3D player3D = new AudioStreamPlayer3D();
                player3D.Stream = stream;
                player3D.Autoplay = false;
                AddChild(player3D);
                player3D.GlobalPosition = position.Value;
                player3D.Finished += () => player3D.QueueFree();
                player3D.Play();
            }
            else
            {
                PlayUiSound(stream);
            }
        }
        else
        {
            string posStr = position.HasValue ? $" at {position.Value}" : string.Empty;
            GD.Print($"[AudioDirector] PlaySFX: {name}{posStr}");
        }
    }

    /// <summary>
    /// Play background music by name. Stops current music first.
    /// Falls back to logging if no audio file is loaded for the given name.
    /// </summary>
    public void PlayMusic(string name)
    {
        if (_currentMusicName == name)
        {
            return;
        }

        StopMusic();
        _currentMusicName = name;

        if (_musicCache.TryGetValue(name, out AudioStream? stream) && stream != null)
        {
            PlayMenuMusic(stream);
        }
        else
        {
            GD.Print($"[AudioDirector] PlayMusic: {name}");
        }
    }

    /// <summary>
    /// Stop the current music track.
    /// </summary>
    public void StopMusic()
    {
        _currentMusicName = string.Empty;
        if (_musicPlayer != null && _musicPlayer.Playing)
        {
            _musicPlayer.Stop();
        }
    }

    /// <summary>
    /// Register an audio file for later playback by name.
    /// </summary>
    public void RegisterSFX(string name, AudioStream stream)
    {
        _sfxCache[name] = stream;
    }

    /// <summary>
    /// Register a music track for later playback by name.
    /// </summary>
    public void RegisterMusic(string name, AudioStream stream)
    {
        _musicCache[name] = stream;
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

    private void SubscribeEvents()
    {
        if (EventBus.Instance == null)
        {
            return;
        }

        EventBus.Instance.PhaseChanged += OnPhaseChanged;
        EventBus.Instance.VoxelChanged += OnVoxelChanged;
        EventBus.Instance.WeaponFired += OnWeaponFired;
        EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
        EventBus.Instance.CommanderKilled += OnCommanderKilled;
    }

    private void UnsubscribeEvents()
    {
        if (EventBus.Instance == null)
        {
            return;
        }

        EventBus.Instance.PhaseChanged -= OnPhaseChanged;
        EventBus.Instance.VoxelChanged -= OnVoxelChanged;
        EventBus.Instance.WeaponFired -= OnWeaponFired;
        EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
        EventBus.Instance.CommanderKilled -= OnCommanderKilled;
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        switch (payload.CurrentPhase)
        {
            case GamePhase.Menu:
                PlayMusic("menu_music");
                break;
            case GamePhase.Building:
                PlayMusic("build_music");
                break;
            case GamePhase.FogReveal:
                PlaySFX("fog_reveal");
                break;
            case GamePhase.Combat:
                PlayMusic("combat_music");
                break;
            case GamePhase.GameOver:
                StopMusic();
                PlaySFX("game_over");
                break;
        }
    }

    private void OnVoxelChanged(VoxelChangeEvent payload)
    {
        // Skip audio for bulk/generation operations (null instigator = terrain gen, menu background, etc.)
        if (payload.Instigator == null) return;

        bool wasAir = payload.BeforeData == 0;
        bool isAir = payload.AfterData == 0;

        if (wasAir && !isAir)
        {
            PlaySFX("block_place", MathHelpers_Position(payload.Position));
        }
        else if (!wasAir && isAir)
        {
            PlaySFX("block_remove", MathHelpers_Position(payload.Position));
        }
    }

    private void OnWeaponFired(WeaponFiredEvent payload)
    {
        // Play weapon-specific fire sound, falling back to generic
        string sfxName = payload.WeaponId switch
        {
            "cannon" => "cannon_fire",
            "mortar" => "mortar_fire",
            "railgun" => "railgun_fire",
            "missile" => "missile_fire",
            "drill" => "drill_fire",
            _ => "weapon_fire",
        };
        PlaySFX(sfxName, payload.Origin);
    }

    private void OnCommanderDamaged(CommanderDamagedEvent payload)
    {
        PlaySFX("commander_hit", payload.WorldPosition);
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        PlaySFX("commander_death", payload.WorldPosition);
    }

    private static Vector3 MathHelpers_Position(Vector3I microvoxelPos)
    {
        float scale = GameConfig.MicrovoxelMeters;
        return new Vector3(microvoxelPos.X * scale, microvoxelPos.Y * scale, microvoxelPos.Z * scale);
    }
}
