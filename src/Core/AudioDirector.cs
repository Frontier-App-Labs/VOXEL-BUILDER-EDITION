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

        // If the game is already in Menu phase (GameManager._Ready ran first),
        // the PhaseChanged event was missed. Start music now.
        CallDeferred(MethodName.StartMusicIfMenuPhase);

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
        // Each weapon gets a FIXED sound for consistency (no random per-shot)
        if (wooshes.Count > 0)
        {
            _sfxLayers["cannon_fire"] = new List<AudioStream> { wooshes[0] };
            _sfxLayers["weapon_fire"] = wooshes; // generic fallback can vary
        }
        // Mortar gets a dedicated explosion boom (deep thump on launch)
        if (explosions.Count >= 2)
        {
            _sfxLayers["mortar_fire"] = new List<AudioStream> { explosions[0] };
            _sfxLayers["missile_fire"] = new List<AudioStream> { explosions[1] };
        }
        else if (explosions.Count == 1)
        {
            _sfxLayers["mortar_fire"] = new List<AudioStream> { explosions[0] };
            _sfxLayers["missile_fire"] = new List<AudioStream> { explosions[0] };
        }
        // Laser layers on railgun zap
        if (laser.Count > 0)
        {
            _sfxLayers["railgun_fire"] = laser;
        }
        // Explosion impact sound (plays when any projectile detonates)
        if (explosions.Count > 0)
        {
            _sfxLayers["explosion_impact"] = explosions;
        }
        // Explosions layer on commander damage/death (these CAN vary for drama)
        if (explosions.Count > 0)
        {
            _sfxLayers["commander_hit"] = explosions;
            _sfxLayers["commander_critical_hit"] = explosions;
            _sfxLayers["commander_death"] = explosions;
        }
        // Crash sounds: fixed per event for consistency
        if (crashes.Count >= 2)
        {
            _sfxLayers["block_place"] = new List<AudioStream> { crashes[0] };
            _sfxLayers["block_remove"] = new List<AudioStream> { crashes[0] };
            _sfxLayers["debris_impact"] = new List<AudioStream> { crashes[1] };
        }
        else if (crashes.Count > 0)
        {
            _sfxLayers["block_place"] = crashes;
            _sfxLayers["block_remove"] = crashes;
            _sfxLayers["debris_impact"] = crashes;
        }
        // Troop attacks use crash sounds layered with retro thud
        if (crashes.Count > 0)
        {
            _sfxLayers["troop_attack"] = new List<AudioStream> { crashes[0] };
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

        // Add a limiter effect on the SFX bus to prevent ear-blasting when
        // multiple explosions/impacts overlap. Ceiling at -3dB, soft knee.
        AddLimiterToBus(_sfxBusIndex);
    }

    private static void AddLimiterToBus(int busIndex)
    {
        // Check if a limiter already exists on this bus
        for (int i = 0; i < AudioServer.GetBusEffectCount(busIndex); i++)
        {
            if (AudioServer.GetBusEffect(busIndex, i) is AudioEffectHardLimiter)
                return;
        }

        AudioEffectHardLimiter limiter = new AudioEffectHardLimiter();
        limiter.CeilingDb = -3.0f;
        AudioServer.AddBusEffect(busIndex, limiter);
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
    // Rate-limit SFX: track last play time per sound name to prevent ear-blasting stacking
    private readonly Dictionary<string, double> _sfxLastPlayTime = new();
    private const double SfxCooldownSeconds = 0.08; // minimum gap between same-name sounds
    private const int MaxConcurrentSfx = 48; // max simultaneous SFX players
    // MenuSfxCapDb removed — PlaySFX now returns early when _menuSfxDucked is true
    private int _activeSfxCount;

    // Per-sound volume overrides (dB) for sounds that are too loud
    private static readonly Dictionary<string, float> SfxVolumeOverrides = new()
    {
        ["mortar_fire"] = -2f,
        ["missile_fire"] = -4f,
        ["cannon_fire"] = -2f,
        ["commander_hit"] = -6f,
        ["commander_critical_hit"] = -6f,
        ["commander_death"] = -4f,
        ["explosion_impact"] = -4f,
        ["debris_impact"] = -8f,
        ["railgun_fire"] = -2f,
        ["drill_fire"] = -2f,
        ["countdown_tick"] = -6f,
        ["countdown_fight"] = -4f,
        ["troop_attack"] = 0f,
    };

    // Sounds that should use non-positional (2D) playback even when a position
    // is supplied. Weapon fire sounds need this because the camera follows the
    // projectile away from the weapon, causing 3D-attenuated sounds to fade out.
    private static readonly HashSet<string> NonPositionalSounds = new()
    {
        "mortar_fire",
        "cannon_fire",
        "missile_fire",
        "railgun_fire",
        "drill_fire",
        "weapon_fire",
        "explosion_impact",
        "commander_hit",
        "commander_critical_hit",
        "commander_death",
        "drill_bore",
        "troop_attack",
    };

    public void PlaySFX(string name, Vector3? position = null)
    {
        // Skip ALL SFX during the menu phase. The menu background battle is
        // visual-only; previously we tried capping VolumeDb to -80dB per player
        // but weapon/rocket fire sounds still leaked through (Godot's
        // AudioStreamPlayer3D can amplify quiet signals when MaxDb > VolumeDb
        // and the listener is near the source). Returning early is both more
        // robust and more efficient (no orphaned silent audio players).
        if (_menuSfxDucked)
        {
            return;
        }

        bool hasRetro = _sfxCache.TryGetValue(name, out AudioStream? retroStream) && retroStream != null;
        bool hasLayer = _sfxLayers.TryGetValue(name, out List<AudioStream>? layers) && layers != null && layers.Count > 0;

        if (!hasRetro && !hasLayer)
        {
            return;
        }

        // Rate-limit: skip if same sound played too recently
        double now = Time.GetTicksMsec() / 1000.0;
        if (_sfxLastPlayTime.TryGetValue(name, out double lastTime) && now - lastTime < SfxCooldownSeconds)
        {
            return;
        }
        _sfxLastPlayTime[name] = now;

        // Cap total concurrent SFX players to prevent audio overload
        if (_activeSfxCount >= MaxConcurrentSfx)
        {
            return;
        }

        // Look up volume override for this sound
        float volumeOverride = SfxVolumeOverrides.TryGetValue(name, out float ov) ? ov : 0f;
        // Base volumes boosted so SFX are clearly audible
        float layerDb = (hasRetro ? 0f : 3f) + volumeOverride;
        float retroDb = 6f + volumeOverride;

        // Force weapon fire sounds to play as non-positional (2D) so they remain
        // audible when the camera follows the projectile away from the weapon.
        bool use2D = !position.HasValue || NonPositionalSounds.Contains(name);

        if (!use2D)
        {
            // 3D positional audio for impact sounds, debris, etc.
            // UnitSize 30 + MaxDistance 200 ensures explosions are clearly audible
            // even from the top-down spectator camera (~100 units up).
            if (hasRetro)
            {
                AudioStreamPlayer3D player3D = new AudioStreamPlayer3D();
                player3D.Stream = retroStream;
                player3D.Bus = "SFX";
                player3D.VolumeDb = retroDb;
                player3D.MaxDb = 3.0f; // prevent clipping
                player3D.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
                player3D.UnitSize = 30f;
                player3D.MaxDistance = 200f;
                AddChild(player3D);
                player3D.GlobalPosition = position!.Value;
                _activeSfxCount++;
                player3D.Finished += () => { player3D.QueueFree(); _activeSfxCount = Math.Max(0, _activeSfxCount - 1); };
                player3D.Play();
            }
            if (hasLayer)
            {
                AudioStream layerStream = layers![_rng.Next(layers.Count)];
                AudioStreamPlayer3D layerPlayer = new AudioStreamPlayer3D();
                layerPlayer.Stream = layerStream;
                layerPlayer.Bus = "SFX";
                layerPlayer.VolumeDb = layerDb;
                layerPlayer.MaxDb = 3.0f;
                layerPlayer.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
                layerPlayer.UnitSize = 30f;
                layerPlayer.MaxDistance = 200f;
                AddChild(layerPlayer);
                layerPlayer.GlobalPosition = position!.Value;
                _activeSfxCount++;
                layerPlayer.Finished += () => { layerPlayer.QueueFree(); _activeSfxCount = Math.Max(0, _activeSfxCount - 1); };
                layerPlayer.Play();
            }
        }
        else
        {
            // 2D non-positional audio: weapon fire sounds + UI sounds
            string bus = position.HasValue ? "WeaponsSFX" : "UiSFX";
            if (hasRetro)
            {
                AudioStreamPlayer retroPlayer = new AudioStreamPlayer();
                retroPlayer.Stream = retroStream;
                retroPlayer.Bus = bus;
                retroPlayer.VolumeDb = retroDb;
                AddChild(retroPlayer);
                _activeSfxCount++;
                retroPlayer.Finished += () => { retroPlayer.QueueFree(); _activeSfxCount = Math.Max(0, _activeSfxCount - 1); };
                retroPlayer.Play();
            }
            if (hasLayer)
            {
                AudioStream layerStream = layers![_rng.Next(layers.Count)];
                AudioStreamPlayer tempPlayer = new AudioStreamPlayer();
                tempPlayer.Stream = layerStream;
                tempPlayer.Bus = bus;
                tempPlayer.VolumeDb = layerDb;
                AddChild(tempPlayer);
                _activeSfxCount++;
                tempPlayer.Finished += () => { tempPlayer.QueueFree(); _activeSfxCount = Math.Max(0, _activeSfxCount - 1); };
                tempPlayer.Play();
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  MUSIC (Random Shuffle)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Called via CallDeferred after _Ready() to catch the case where
    /// GameManager.SetPhase(Menu) fired before AudioDirector was initialized.
    /// </summary>
    private void StartMusicIfMenuPhase()
    {
        GameManager? gm = GetTree()?.Root.GetNodeOrNull<GameManager>("Main");
        if (gm != null && gm.CurrentPhase == GamePhase.Menu)
        {
            if (_musicPlayer == null || !_musicPlayer.Playing)
            {
                PlayRandomMusic();
            }

            // Set the per-player cap flag if we missed the PhaseChanged event
            _menuSfxDucked = true;
        }
    }

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
        // When a track finishes, replay the same track for the current phase
        // instead of shuffling to a random track (which could play combat
        // music on the menu). This keeps the music appropriate to the phase.
        GameManager? gm = GetTree()?.Root.GetNodeOrNull<GameManager>("Main");
        GamePhase phase = gm?.CurrentPhase ?? GamePhase.Menu;

        string? targetTrack = phase switch
        {
            GamePhase.Menu => "menu_music",
            GamePhase.Building or GamePhase.FogReveal => "build_music",
            GamePhase.Combat => "combat_music",
            GamePhase.GameOver => "gameover_music",
            _ => null,
        };

        if (targetTrack != null && _musicCache.TryGetValue(targetTrack, out AudioStream? stream) && stream != null && _musicPlayer != null)
        {
            _currentMusicName = targetTrack;
            _musicPlayer.Stream = stream;
            _musicPlayer.Play();
        }
        else
        {
            PlayRandomMusic();
        }
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

    private bool _menuSfxDucked = true; // game always starts in menu phase

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        switch (payload.CurrentPhase)
        {
            case GamePhase.Menu:
                _menuSfxDucked = true; // PlaySFX returns early — menu battle is visual only
                // Start random shuffle if nothing is playing
                if (_musicPlayer == null || !_musicPlayer.Playing)
                {
                    PlayRandomMusic();
                }
                break;
            case GamePhase.Building:
            case GamePhase.Combat:
                _menuSfxDucked = false;
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
