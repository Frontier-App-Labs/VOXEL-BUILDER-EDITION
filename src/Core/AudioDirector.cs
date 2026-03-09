using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.UI;
using VoxelSiege.Utility;

namespace VoxelSiege.Core;

/// <summary>
/// Singleton audio manager. Listens to EventBus events and plays appropriate sounds.
/// Creates and manages audio buses (Master, Music, SFX, Ambience, WeaponsSFX, UiSFX)
/// and exposes volume control methods used by the Settings UI.
/// Loads MP3 assets from assets/audio/ and layers them with procedural retro SFX.
/// Music plays in random shuffle order (Minecraft-style).
/// </summary>
public partial class AudioDirector : Node
{
    private static AudioDirector? _instance;
    public static AudioDirector? Instance => _instance;

    private AudioStreamPlayer? _musicPlayer;
    private AudioStreamPlayer? _uiPlayer;
    private readonly Dictionary<string, AudioStream> _sfxCache = new Dictionary<string, AudioStream>();
    private readonly Dictionary<string, AudioStream> _musicCache = new Dictionary<string, AudioStream>();
    private readonly Dictionary<string, List<AudioStream>> _sfxLayers = new Dictionary<string, List<AudioStream>>();
    private readonly List<string> _musicPlaylist = new List<string>();
    private readonly Random _rng = new Random();
    private int _lastMusicIndex = -1;
    private string _currentMusicName = string.Empty;

    // Bus indices (populated in _Ready)
    private int _masterBusIndex;
    private int _musicBusIndex;
    private int _sfxBusIndex;
    private int _ambienceBusIndex;
    private int _weaponsSfxBusIndex;
    private int _uiSfxBusIndex;

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

        if (_musicPlayer != null)
        {
            _musicPlayer.Finished -= OnMusicFinished;
        }

        UnsubscribeEvents();
    }

    public override void _Ready()
    {
        // --- Setup audio buses ---
        SetupAudioBuses();

        _musicPlayer = GetNodeOrNull<AudioStreamPlayer>("Music") ?? new AudioStreamPlayer();
        if (_musicPlayer.GetParent() == null)
        {
            _musicPlayer.Name = "Music";
            AddChild(_musicPlayer);
        }
        _musicPlayer.Bus = "Music";
        _musicPlayer.Finished += OnMusicFinished;

        _uiPlayer = GetNodeOrNull<AudioStreamPlayer>("UI") ?? new AudioStreamPlayer();
        if (_uiPlayer.GetParent() == null)
        {
            _uiPlayer.Name = "UI";
            AddChild(_uiPlayer);
        }
        _uiPlayer.Bus = "UiSFX";

        LoadAudioAssets();
        SubscribeEvents();

        // Apply saved volume settings
        GameSettingsData settings = GameSettingsData.Current;
        SetMasterVolume(settings.MasterVolume);
        SetMusicVolume(settings.MusicVolume);
        SetSFXVolume(settings.SfxVolume);
        SetAmbienceVolume(settings.AmbienceVolume);
        SetWeaponsSfxVolume(settings.WeaponsSfxVolume);
        SetUiSfxVolume(settings.UiSfxVolume);
    }

    // ─────────────────────────────────────────────────
    //  AUDIO ASSET LOADING
    // ─────────────────────────────────────────────────

    private void LoadAudioAssets()
    {
        // Load music tracks into the random shuffle playlist
        TryLoadMusic("menu_music", "res://assets/audio/music/menu_music.mp3");
        TryLoadMusic("build_music", "res://assets/audio/music/build_music.mp3");
        TryLoadMusic("combat_music", "res://assets/audio/music/combat_music.mp3");
        TryLoadMusic("gameover_music", "res://assets/audio/music/gameover_music.mp3");

        // Load SFX MP3 variants for layering with retro sounds
        List<AudioStream> explosions = LoadSfxVariants("res://assets/audio/sfx/explosion_", 4);
        List<AudioStream> crashes = LoadSfxVariants("res://assets/audio/sfx/crash_", 2);
        List<AudioStream> wooshes = LoadSfxVariants("res://assets/audio/sfx/woosh_", 2);
        List<AudioStream> laser = LoadSfxSingle("res://assets/audio/sfx/laser.mp3");

        // Map MP3 layers to game sound events
        // Wooshes layer on top of weapon fire retro sounds
        if (wooshes.Count > 0)
        {
            _sfxLayers["cannon_fire"] = wooshes;
            _sfxLayers["mortar_fire"] = wooshes;
            _sfxLayers["missile_fire"] = wooshes;
            _sfxLayers["weapon_fire"] = wooshes;
        }
        // Laser layers on railgun zap
        if (laser.Count > 0)
        {
            _sfxLayers["railgun_fire"] = laser;
        }
        // Explosions layer on commander damage/death
        if (explosions.Count > 0)
        {
            _sfxLayers["commander_hit"] = explosions;
            _sfxLayers["commander_critical_hit"] = explosions;
            _sfxLayers["commander_death"] = explosions;
        }
        // Crash sounds layer on block place/remove and debris
        if (crashes.Count > 0)
        {
            _sfxLayers["block_place"] = crashes;
            _sfxLayers["block_remove"] = crashes;
            _sfxLayers["debris_impact"] = crashes;
        }

        GD.Print($"[AudioDirector] Loaded {_musicPlaylist.Count} music tracks, {_sfxLayers.Count} SFX layers.");
    }

    private void TryLoadMusic(string name, string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.Print($"[AudioDirector] Music not found: {path}");
            return;
        }

        AudioStream? stream = ResourceLoader.Load<AudioStream>(path);
        if (stream == null) return;

        // Disable looping so Finished signal fires for shuffle
        if (stream is AudioStreamMP3 mp3)
        {
            mp3.Loop = false;
        }

        RegisterMusic(name, stream);
        _musicPlaylist.Add(name);
    }

    private static List<AudioStream> LoadSfxVariants(string pathPrefix, int count)
    {
        List<AudioStream> list = new List<AudioStream>();
        for (int i = 1; i <= count; i++)
        {
            string path = $"{pathPrefix}{i}.mp3";
            if (!ResourceLoader.Exists(path)) continue;
            AudioStream? stream = ResourceLoader.Load<AudioStream>(path);
            if (stream != null) list.Add(stream);
        }
        return list;
    }

    private static List<AudioStream> LoadSfxSingle(string path)
    {
        List<AudioStream> list = new List<AudioStream>();
        if (!ResourceLoader.Exists(path)) return list;
        AudioStream? stream = ResourceLoader.Load<AudioStream>(path);
        if (stream != null) list.Add(stream);
        return list;
    }

    // ─────────────────────────────────────────────────
    //  AUDIO BUS SETUP
    // ─────────────────────────────────────────────────

    private void SetupAudioBuses()
    {
        // Master bus always exists at index 0
        _masterBusIndex = 0;

        // Add buses if they don't already exist
        _musicBusIndex = GetOrCreateBus("Music", "Master");
        _sfxBusIndex = GetOrCreateBus("SFX", "Master");
        _ambienceBusIndex = GetOrCreateBus("Ambience", "Master");
        _weaponsSfxBusIndex = GetOrCreateBus("WeaponsSFX", "SFX");
        _uiSfxBusIndex = GetOrCreateBus("UiSFX", "SFX");
    }

    private static int GetOrCreateBus(string busName, string sendTo)
    {
        int idx = AudioServer.GetBusIndex(busName);
        if (idx >= 0)
        {
            return idx;
        }

        AudioServer.AddBus();
        int newIdx = AudioServer.BusCount - 1;
        AudioServer.SetBusName(newIdx, busName);
        AudioServer.SetBusSend(newIdx, sendTo);
        return newIdx;
    }

    // ─────────────────────────────────────────────────
    //  VOLUME CONTROL (called by SettingsUI)
    // ─────────────────────────────────────────────────

    /// <summary>Set the Master bus volume (0.0 to 1.0 linear).</summary>
    public void SetMasterVolume(float linear)
    {
        SetBusVolume(_masterBusIndex, linear);
    }

    /// <summary>Set the Music bus volume (0.0 to 1.0 linear).</summary>
    public void SetMusicVolume(float linear)
    {
        SetBusVolume(_musicBusIndex, linear);
    }

    /// <summary>Set the SFX bus volume (0.0 to 1.0 linear).</summary>
    public void SetSFXVolume(float linear)
    {
        SetBusVolume(_sfxBusIndex, linear);
    }

    /// <summary>Set the Ambience bus volume (0.0 to 1.0 linear).</summary>
    public void SetAmbienceVolume(float linear)
    {
        SetBusVolume(_ambienceBusIndex, linear);
    }

    /// <summary>Set the Weapons SFX sub-bus volume (0.0 to 1.0 linear).</summary>
    public void SetWeaponsSfxVolume(float linear)
    {
        SetBusVolume(_weaponsSfxBusIndex, linear);
    }

    /// <summary>Set the UI SFX sub-bus volume (0.0 to 1.0 linear).</summary>
    public void SetUiSfxVolume(float linear)
    {
        SetBusVolume(_uiSfxBusIndex, linear);
    }

    private static void SetBusVolume(int busIndex, float linear)
    {
        linear = Mathf.Clamp(linear, 0f, 1f);
        float db = linear > 0.001f ? Mathf.LinearToDb(linear) : -80f;
        AudioServer.SetBusVolumeDb(busIndex, db);
        AudioServer.SetBusMute(busIndex, linear <= 0.001f);
    }

    // ─────────────────────────────────────────────────
    //  PLAYBACK
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Play a sound effect by name. Plays both the retro-generated sound AND
    /// any MP3 layer simultaneously for a richer audio experience.
    /// If position is provided, plays as 3D audio.
    /// </summary>
    public void PlaySFX(string name, Vector3? position = null)
    {
        bool hasRetro = _sfxCache.TryGetValue(name, out AudioStream? retroStream) && retroStream != null;
        bool hasLayer = _sfxLayers.TryGetValue(name, out List<AudioStream>? layers) && layers != null && layers.Count > 0;

        if (!hasRetro && !hasLayer)
        {
            string posStr = position.HasValue ? $" at {position.Value}" : string.Empty;
            GD.Print($"[AudioDirector] PlaySFX: {name}{posStr}");
            return;
        }

        if (position.HasValue)
        {
            // 3D positional audio
            if (hasRetro)
            {
                AudioStreamPlayer3D player3D = new AudioStreamPlayer3D();
                player3D.Stream = retroStream;
                player3D.Bus = "SFX";
                AddChild(player3D);
                player3D.GlobalPosition = position.Value;
                player3D.Finished += () => player3D.QueueFree();
                player3D.Play();
            }
            if (hasLayer)
            {
                AudioStream layerStream = layers![_rng.Next(layers.Count)];
                AudioStreamPlayer3D layerPlayer = new AudioStreamPlayer3D();
                layerPlayer.Stream = layerStream;
                layerPlayer.Bus = "SFX";
                layerPlayer.VolumeDb = hasRetro ? -3f : 0f;
                AddChild(layerPlayer);
                layerPlayer.GlobalPosition = position.Value;
                layerPlayer.Finished += () => layerPlayer.QueueFree();
                layerPlayer.Play();
            }
        }
        else
        {
            // 2D UI audio
            if (hasRetro)
            {
                PlayUiSound(retroStream);
            }
            if (hasLayer)
            {
                AudioStream layerStream = layers![_rng.Next(layers.Count)];
                AudioStreamPlayer tempPlayer = new AudioStreamPlayer();
                tempPlayer.Stream = layerStream;
                tempPlayer.Bus = "UiSFX";
                tempPlayer.VolumeDb = hasRetro ? -3f : 0f;
                AddChild(tempPlayer);
                tempPlayer.Finished += () => tempPlayer.QueueFree();
                tempPlayer.Play();
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  MUSIC (Random Shuffle)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Play the next random music track from the playlist (Minecraft-style shuffle).
    /// Never plays the same track twice in a row.
    /// </summary>
    public void PlayRandomMusic()
    {
        if (_musicPlaylist.Count == 0) return;

        int index;
        if (_musicPlaylist.Count == 1)
        {
            index = 0;
        }
        else
        {
            do { index = _rng.Next(_musicPlaylist.Count); }
            while (index == _lastMusicIndex);
        }

        _lastMusicIndex = index;
        string name = _musicPlaylist[index];
        _currentMusicName = name;

        if (_musicCache.TryGetValue(name, out AudioStream? stream) && stream != null && _musicPlayer != null)
        {
            _musicPlayer.Stream = stream;
            _musicPlayer.Play();
            GD.Print($"[AudioDirector] Now playing: {name}");
        }
    }

    private void OnMusicFinished()
    {
        // Automatically play next random track when current one ends
        PlayRandomMusic();
    }

    /// <summary>
    /// Play a specific music track by name (used for game over).
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
        _lastMusicIndex = -1;
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
            case GamePhase.Building:
            case GamePhase.Combat:
                // Start random shuffle if nothing is playing
                if (_musicPlayer == null || !_musicPlayer.Playing)
                {
                    PlayRandomMusic();
                }
                break;
            case GamePhase.FogReveal:
                PlaySFX("fog_reveal");
                break;
            case GamePhase.GameOver:
                // Play gameover music specifically
                if (_musicCache.ContainsKey("gameover_music"))
                {
                    PlayMusic("gameover_music");
                }
                else
                {
                    StopMusic();
                }
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
        if (payload.IsCriticalHit)
        {
            PlaySFX("commander_critical_hit", payload.WorldPosition);
        }
        else
        {
            PlaySFX("commander_hit", payload.WorldPosition);
        }
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
