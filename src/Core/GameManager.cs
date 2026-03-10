using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Building;
using VoxelSiege.Camera;
using VoxelSiege.Combat;
using VoxelSiege.Networking;
using VoxelSiege.UI;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelSiege.Army;
using VoxelSiege.Art;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Core;

public partial class GameManager : Node
{
    private readonly Dictionary<PlayerSlot, PlayerData> _players = new Dictionary<PlayerSlot, PlayerData>();
    private readonly Dictionary<PlayerSlot, CommanderActor> _commanders = new Dictionary<PlayerSlot, CommanderActor>();
    private readonly Dictionary<PlayerSlot, List<WeaponBase>> _weapons = new Dictionary<PlayerSlot, List<WeaponBase>>();
    private readonly Dictionary<PlayerSlot, BuildZone> _buildZones = new Dictionary<PlayerSlot, BuildZone>();
    private readonly Dictionary<PlayerSlot, AI.BotController> _botControllers = new Dictionary<PlayerSlot, AI.BotController>();
    private TurnManager? _turnManager;
    private VoxelWorld? _voxelWorld;
    private BuildSystem? _buildSystem;
    private WeaponPlacer? _weaponPlacer;
    private AimingSystem? _aimingSystem;
    private ProgressionManager? _progressionManager;
    private AchievementTracker? _achievementTracker;
    private SteamPlatformNode? _steamPlatform;
    private ArmyManager? _armyManager;
    private GhostPreview? _ghostPreview;
    private FreeFlyCamera? _camera;
    private CombatCamera? _combatCamera;
    private VoxelGiSetup? _voxelGiSetup;
    private PowerupExecutor? _powerupExecutor;
    private float _phaseCountdownSeconds;

    // Networking
    private NetworkManager? _networkManager;
    private LobbyManager? _lobbyManager;
    private SyncManager? _syncManager;
    private LobbyUI? _lobbyUI;
    private CanvasLayer? _lobbyUILayer;

    // Build phase interaction state
    private Vector3I _buildCursorBuildUnit;
    private bool _buildCursorValid;
    private bool _hasBuildCursor;

    // Multi-voxel drag state for tools (Wall, Floor, Box, Line, Ramp)
    private bool _isDragBuilding;
    private Vector3I _dragStartBuildUnit;
    private int _buildRotation; // 0-3 representing 0/90/180/270 degrees

    // Build placement mode (commander / weapon placement during build phase)
    private enum PlacementMode { Block, Commander, Weapon }
    private PlacementMode _placementMode = PlacementMode.Block;
    private WeaponType _selectedWeaponType = WeaponType.Cannon;

    // Cached preview meshes for ghost preview (generated once per weapon type)
    private readonly Dictionary<string, ArrayMesh> _weaponPreviewMeshes = new();
    private ArrayMesh? _commanderPreviewMesh;

    // Combat phase interaction state
    private int _selectedWeaponIndex = -1; // -1 means no weapon selected
    private bool _weaponConfirmed; // true once the player confirms their weapon choice (before targeting)
    private bool _isAiming;
    private bool _hasTarget; // true when the player has clicked a target point
    private MeshInstance3D? _targetHighlight; // wireframe cube highlighting hovered voxel
    private Vector3I _lastHoveredMicrovoxel = new(-9999, -9999, -9999);

    // Target enemy selection (cycle through enemies during targeting)
    private readonly List<PlayerSlot> _targetEnemySlots = new List<PlayerSlot>();
    private int _targetEnemyIndex;

    // Tracks which players have already recorded a hit this round (prevents double-counting
    // ShotsHit when a single shot destroys multiple voxels or damages multiple commanders).
    private readonly HashSet<PlayerSlot> _hitRecordedThisRound = new HashSet<PlayerSlot>();

    // Remembers the last attacked enemy per player so targeting defaults to them next turn
    private readonly Dictionary<PlayerSlot, PlayerSlot> _lastAttackedEnemy = new Dictionary<PlayerSlot, PlayerSlot>();

    // Spectator view preference: when true, bot turns (and post-fire cinematics) use
    // top-down instead of free-fly. Sticky — persists until the player presses V again.
    private bool _spectatorTopDown;

    // UI (buttons only — labels handled by BuildUI / CombatUI)
    private Control? _hudRoot;
    private Button? _readyButton;
    private Button? _skipTurnButton;
    private SplashScreen? _splashScreen;
    private SplashScreen? _loadingSplash;
    private CanvasLayer? _loadingSplashLayer;
    private PauseMenu? _pauseMenu;
    private Label? _buildWarningLabel;
    private GameOverlayUI? _gameOverlayUI;
    private SettingsUI? _settingsUI;

    // Combat countdown overlay (shown between build and combat phases)
    private CanvasLayer? _countdownLayer;
    private ColorRect? _countdownBg;
    private Label? _countdownLabel;
    private bool _countdownActive;
    private float _countdownTimer;
    private int _countdownStep; // 0=preparing, 1=3, 2=2, 3=1, 4=FIGHT!, 5=fade out, 6=done
    private bool _combatSetupDone;

    // Commander naming popup (shown after build phase for human players)
    private PanelContainer? _namePopup;
    private LineEdit? _nameInput;
    private bool _awaitingName;

    // Track the active builder during build phase (hot-seat: each player builds in turn)
    private PlayerSlot _activeBuilder = PlayerSlot.Player1;
    private int _activeBuilderIndex;
    private static readonly PlayerSlot[] BuildOrder = { PlayerSlot.Player1, PlayerSlot.Player2, PlayerSlot.Player3, PlayerSlot.Player4 };

    // Artillery Dominance: when a player destroys ALL enemy weapons, an automated
    // bombardment rains projectiles on the enemy base(s) until all enemy commanders are dead.
    private bool _artilleryDominanceActive;
    private PlayerSlot _artilleryDominanceWinner;
    private float _bombardmentTimer;
    private int _bombardmentSalvoCount;
    private int _bombardmentTargetIndex; // which enemy fort to bomb next (sequential)
    private List<PlayerSlot>? _bombardmentTargets; // cached list of enemy forts to bomb
    private CanvasLayer? _dominanceBannerLayer;

    // Combat intro flyover: camera zooms from top-down into the starting player's base
    private bool _combatIntroActive;
    private float _combatIntroTimer;
    private const float CombatIntroDuration = 1.5f; // seconds for top-down zoom-in

    // Deferred game-over: when the final kill happens, let the kill cam play first
    private bool _pendingGameOver;
    private PlayerSlot? _pendingWinner;

    // Sandbox mode: build freely with no opponents, save/load builds
    private bool _isSandbox;
    private BlueprintSystem? _blueprintSystem;

    // Menu battle scene state
    private float _menuBattleTimer;
    private float _menuOrbitAngle;
    private readonly List<WeaponBase> _menuWeaponsA = new List<WeaponBase>();
    private readonly List<WeaponBase> _menuWeaponsB = new List<WeaponBase>();
    private readonly List<WeaponBase> _menuWeaponsC = new List<WeaponBase>();
    private readonly List<WeaponBase> _menuWeaponsD = new List<WeaponBase>();
    private Vector3 _menuBattleCenter;
    private int _menuFireRound;
    private bool _menuBattleActive;

    [Export]
    public bool AutoStartPrototypeMatch { get; set; }

    [Export]
    public float PrototypeBuildPhaseSeconds { get; set; } = 300f;

    [Export]
    public float PrototypeFogRevealSeconds { get; set; } = 3f;

    [Export]
    public float MaxRaycastDistance { get; set; } = 100f;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Menu;
    public float PhaseCountdownSeconds => _phaseCountdownSeconds;
    public MatchSettings Settings { get; } = new MatchSettings();
    public IReadOnlyDictionary<PlayerSlot, PlayerData> Players => _players;
    public IReadOnlyDictionary<PlayerSlot, BuildZone> BuildZones => _buildZones;
    public VoxelWorld? VoxelWorld => _voxelWorld;

    /// <summary>
    /// The display name entered by the human player (Player 1) before the match starts.
    /// If empty or null, defaults to the color name (e.g. "Green").
    /// </summary>
    public string? HumanPlayerName { get; set; }

    public override void _Ready()
    {
        _turnManager = GetNodeOrNull<TurnManager>("TurnManager") ?? CreateNode<TurnManager>("TurnManager");
        _voxelWorld = GetNodeOrNull<VoxelWorld>("GameWorld") ?? CreateNode<VoxelWorld>("GameWorld");
        _buildSystem = GetNodeOrNull<BuildSystem>("BuildSystem") ?? CreateNode<BuildSystem>("BuildSystem");
        _weaponPlacer = GetNodeOrNull<WeaponPlacer>("WeaponPlacer") ?? CreateNode<WeaponPlacer>("WeaponPlacer");
        _aimingSystem = GetNodeOrNull<AimingSystem>("AimingSystem") ?? CreateNode<AimingSystem>("AimingSystem");
        _progressionManager = GetNodeOrNull<ProgressionManager>("ProgressionManager") ?? CreateNode<ProgressionManager>("ProgressionManager");
        _achievementTracker = GetNodeOrNull<AchievementTracker>("AchievementTracker") ?? CreateNode<AchievementTracker>("AchievementTracker");
        _steamPlatform = GetNodeOrNull<SteamPlatformNode>("SteamPlatform");

        // Create fire spread system
        if (GetNodeOrNull<FireSystem>("FireSystem") == null)
        {
            FireSystem fireSystem = new FireSystem();
            fireSystem.Name = "FireSystem";
            AddChild(fireSystem);
        }

        // Create powerup executor for powerup system
        _powerupExecutor = GetNodeOrNull<PowerupExecutor>("PowerupExecutor");
        if (_powerupExecutor == null)
        {
            _powerupExecutor = new PowerupExecutor();
            _powerupExecutor.Name = "PowerupExecutor";
            AddChild(_powerupExecutor);
        }
        _powerupExecutor.PowerupActivated += OnPowerupActivated;
        _powerupExecutor.PowerupExpired += OnPowerupExpired;

        // Create ghost preview for build phase
        _ghostPreview = GetNodeOrNull<GhostPreview>("GhostPreview") ?? CreateNode<GhostPreview>("GhostPreview");

        // Create army manager for troop purchasing and deployment
        _armyManager = GetNodeOrNull<ArmyManager>("ArmyManager") ?? CreateNode<ArmyManager>("ArmyManager");

        // Find or create camera
        _camera = GetViewport().GetCamera3D() as FreeFlyCamera;
        if (_camera == null)
        {
            _camera = new FreeFlyCamera();
            _camera.Name = "PlayerCamera";
            AddChild(_camera);
        }

        // Create the combat camera (starts inactive; activated when combat phase begins)
        _combatCamera = GetNodeOrNull<CombatCamera>("CombatCamera");
        if (_combatCamera == null)
        {
            _combatCamera = new CombatCamera();
            _combatCamera.Name = "CombatCamera";
            AddChild(_combatCamera);
        }
        _combatCamera.Deactivate();
        _combatCamera.ExitWeaponPOVRequested += OnExitWeaponPOVRequested;
        _combatCamera.TargetClickRequested += OnTargetClickRequested;
        _combatCamera.TargetCycleRequested += OnTargetCycleRequested;
        _combatCamera.CinematicFinished += OnCombatCameraCinematicFinished;

        EnsureDefaultInputMap();
        SeedLocalPlayers();
        CreateHUD();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled += OnCommanderKilled;
            EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
            EventBus.Instance.TurnChanged += OnTurnChanged;
            EventBus.Instance.WeaponFired += OnWeaponFired;
            EventBus.Instance.WeaponDestroyed += OnWeaponDestroyed;
            EventBus.Instance.RailgunBeamFired += OnRailgunBeamFired;
            EventBus.Instance.VoxelChanged += OnVoxelChangedStats;
        }

        _steamPlatform?.Initialize();

        // Subscribe to CombatUI events (weapon selection + fire button)
        SubscribeToCombatUI();

        // Subscribe to BuildUI events (place weapon button)
        SubscribeToBuildUI();

        // Create SettingsUI and wire up main menu settings button
        _settingsUI = new SettingsUI();
        _settingsUI.Name = "SettingsUI";
        AddChild(_settingsUI);

        MainMenu? mainMenuNode = GetNodeOrNull<MainMenu>("MainMenu");
        if (mainMenuNode != null)
        {
            mainMenuNode.SettingsRequested += OnMainMenuSettingsRequested;
            mainMenuNode.HostGameRequested += OnHostGameRequested;
            mainMenuNode.JoinWithCodeRequested += OnJoinWithCodeRequested;
        }

        // Wire up networking
        _networkManager = GetNodeOrNull<NetworkManager>("NetworkManager");
        _lobbyManager = GetNodeOrNull<LobbyManager>("LobbyManager");
        _syncManager = GetNodeOrNull<SyncManager>("SyncManager");

        if (_networkManager != null)
        {
            _networkManager.PeerConnected += OnNetworkPeerConnected;
            _networkManager.PeerDisconnected += OnNetworkPeerDisconnected;
            _networkManager.PlayerAnnounced += OnPlayerAnnounced;
            _networkManager.PlayerReadyChanged += OnPlayerReadyChanged;
            _networkManager.LobbyStateReceived += OnLobbyStateReceived;
            _networkManager.MatchStartReceived += OnMatchStartReceived;
            _networkManager.ConnectedToServer += OnConnectedToServer;
            _networkManager.ConnectionFailed += OnConnectionFailed;
        }

        // Look for splash screen — if present, hide main menu and let splash play first
        _splashScreen = GetNodeOrNull<SplashScreen>("SplashScreen");
        if (_splashScreen != null)
        {
            // Hide the main menu during the splash
            Control? mainMenu = GetNodeOrNull<Control>("MainMenu");
            if (mainMenu != null)
            {
                mainMenu.Visible = false;
            }
            _splashScreen.SplashFinished += OnSplashFinished;

            // Start generating menu background terrain DURING the splash so it's
            // ready by the time the splash finishes (instead of generating after).
            CallDeferred(nameof(GenerateMenuBackgroundTerrain));
        }
        else if (AutoStartPrototypeMatch)
        {
            // No splash screen in scene, go directly to match
            StartMatchAfterSplash();
        }
        else
        {
            SetPhase(GamePhase.Menu);
        }
    }

    private void OnSplashFinished()
    {
        // Remove splash screen (and its CanvasLayer wrapper if SplashScreen created one)
        if (_splashScreen != null)
        {
            _splashScreen.SplashFinished -= OnSplashFinished;
            Node? splashParent = _splashScreen.GetParent();
            _splashScreen.QueueFree();
            _splashScreen = null;
            // If the splash wrapped itself in a CanvasLayer, clean that up too
            if (splashParent is CanvasLayer wrapper && wrapper.Name == "SplashCanvasLayer")
            {
                wrapper.QueueFree();
            }
        }

        // Terrain was already generated during the splash (kicked off in _Ready),
        // so we just need to show the main menu and start the background battle.

        // Show the main menu
        Control? mainMenu = GetNodeOrNull<Control>("MainMenu");
        if (mainMenu != null)
        {
            mainMenu.Visible = true;
        }

        // Now that the splash is gone, activate the background battle
        _menuBattleActive = true;

        if (AutoStartPrototypeMatch)
        {
            // Skip the loading splash since we just played the initial splash
            StartMatchAfterSplash();
        }
        else
        {
            SetPhase(GamePhase.Menu);
        }
    }

    /// <summary>
    /// Generates a preview terrain so the semi-transparent main menu backdrop
    /// shows 3D voxel terrain behind it. Includes terrain features, vegetation,
    /// build zone borders, and preset structures to look like a game in progress.
    /// </summary>
    private void GenerateMenuBackgroundTerrain()
    {
        if (_voxelWorld == null) return;

        _voxelWorld.ClearWorld(true); // generates arena ground (Foundation + Dirt top)

        // Create temporary build zones for decoration (no players needed)
        var menuBuildZones = new Dictionary<PlayerSlot, BuildZone>();
        PlayerSlot[] slots = { PlayerSlot.Player1, PlayerSlot.Player2, PlayerSlot.Player3, PlayerSlot.Player4 };
        for (int i = 0; i < slots.Length && i < GameConfig.FourPlayerZoneOrigins.Length; i++)
        {
            menuBuildZones[slots[i]] = new BuildZone(
                GameConfig.FourPlayerZoneOrigins[i],
                GameConfig.FourPlayerBuildZoneSize);
        }

        // Generate mountain range border around the arena perimeter
        TerrainDecorator.GenerateMountainBorder(_voxelWorld);

        // Decorate arena: zone borders and vegetation (trees, grass tufts)
        TerrainDecorator.MarkBuildZoneBorders(_voxelWorld, menuBuildZones);
        TerrainDecorator.DecorateArena(_voxelWorld, menuBuildZones);

        // Place preset structures in each build zone to look like a game in progress
        int groundY = GameConfig.PrototypeGroundThickness;
        var rng = new Random(12345); // fixed seed for consistent menu background

        // Materials for each player's structures
        VoxelMaterialType[] wallMats = { VoxelMaterialType.Stone, VoxelMaterialType.Brick, VoxelMaterialType.Concrete, VoxelMaterialType.Wood };
        VoxelMaterialType[] accentMats = { VoxelMaterialType.Wood, VoxelMaterialType.Metal, VoxelMaterialType.Stone, VoxelMaterialType.Brick };

        for (int p = 0; p < slots.Length && p < GameConfig.FourPlayerZoneOrigins.Length; p++)
        {
            BuildZone zone = menuBuildZones[slots[p]];
            Vector3I zMin = zone.OriginMicrovoxels;
            Vector3I zSize = zone.SizeMicrovoxels;
            VoxelMaterialType wallMat = wallMats[p];
            VoxelMaterialType accentMat = accentMats[p];

            // --- Corner towers (4 small towers at zone corners) ---
            int[][] cornerOffsets = { new[] { 2, 2 }, new[] { zSize.X - 4, 2 }, new[] { 2, zSize.Z - 4 }, new[] { zSize.X - 4, zSize.Z - 4 } };
            foreach (int[] corner in cornerOffsets)
            {
                int towerH = rng.Next(5, 9);
                for (int y = 0; y < towerH; y++)
                {
                    for (int dx = 0; dx < 3; dx++)
                    {
                        for (int dz = 0; dz < 3; dz++)
                        {
                            // Hollow interior on upper floors
                            bool isWall = dx == 0 || dx == 2 || dz == 0 || dz == 2 || y < 2;
                            if (isWall)
                            {
                                _voxelWorld.SetVoxel(
                                    new Vector3I(zMin.X + corner[0] + dx, groundY + y, zMin.Z + corner[1] + dz),
                                    Voxel.Voxel.Create(wallMat));
                            }
                        }
                    }
                }
                // Battlement on top
                for (int dx = 0; dx < 3; dx += 2)
                {
                    for (int dz = 0; dz < 3; dz += 2)
                    {
                        _voxelWorld.SetVoxel(
                            new Vector3I(zMin.X + corner[0] + dx, groundY + towerH, zMin.Z + corner[1] + dz),
                            Voxel.Voxel.Create(accentMat));
                    }
                }
            }

            // --- Front wall connecting the two front towers ---
            int wallH = rng.Next(3, 5);
            int frontZ = zMin.Z + 2;
            for (int x = zMin.X + 5; x < zMin.X + zSize.X - 4; x++)
            {
                for (int y = 0; y < wallH; y++)
                {
                    _voxelWorld.SetVoxel(
                        new Vector3I(x, groundY + y, frontZ),
                        Voxel.Voxel.Create(wallMat));
                }
                // Crenellations every other block
                if ((x - zMin.X) % 2 == 0)
                {
                    _voxelWorld.SetVoxel(
                        new Vector3I(x, groundY + wallH, frontZ),
                        Voxel.Voxel.Create(accentMat));
                }
            }

            // --- Central keep (larger structure in the middle of the zone) ---
            int keepX = zMin.X + zSize.X / 2 - 3;
            int keepZ = zMin.Z + zSize.Z / 2 - 3;
            int keepH = rng.Next(6, 10);
            for (int y = 0; y < keepH; y++)
            {
                for (int dx = 0; dx < 6; dx++)
                {
                    for (int dz = 0; dz < 6; dz++)
                    {
                        bool isEdge = dx == 0 || dx == 5 || dz == 0 || dz == 5;
                        if (isEdge || y == 0)
                        {
                            _voxelWorld.SetVoxel(
                                new Vector3I(keepX + dx, groundY + y, keepZ + dz),
                                Voxel.Voxel.Create(wallMat));
                        }
                    }
                }
            }
            // Keep roof / battlements
            for (int dx = 0; dx < 6; dx++)
            {
                for (int dz = 0; dz < 6; dz++)
                {
                    bool isBattlement = (dx == 0 || dx == 5 || dz == 0 || dz == 5) && ((dx + dz) % 2 == 0);
                    if (isBattlement)
                    {
                        _voxelWorld.SetVoxel(
                            new Vector3I(keepX + dx, groundY + keepH, keepZ + dz),
                            Voxel.Voxel.Create(accentMat));
                    }
                }
            }

            // --- Scattered rubble / partial walls (looks like battle damage) ---
            int rubbleCount = rng.Next(3, 7);
            for (int r = 0; r < rubbleCount; r++)
            {
                int rx = zMin.X + rng.Next(4, zSize.X - 4);
                int rz = zMin.Z + rng.Next(4, zSize.Z - 4);
                int rh = rng.Next(1, 4);
                int rw = rng.Next(1, 3);
                for (int y = 0; y < rh; y++)
                {
                    for (int dx = 0; dx < rw; dx++)
                    {
                        _voxelWorld.SetVoxel(
                            new Vector3I(rx + dx, groundY + y, rz),
                            Voxel.Voxel.Create(rng.Next(2) == 0 ? wallMat : VoxelMaterialType.Stone));
                    }
                }
            }
        }

        SetupVoxelGiAndLighting();

        // Build two small battle fortresses and set up the orbiting camera
        SetupMenuBattleScene();
    }

    private void SetupMenuBattleScene()
    {
        if (_voxelWorld == null || _weaponPlacer == null) return;

        int groundY = GameConfig.PrototypeGroundThickness;
        int fortSizeX = 10;
        int fortSizeZ = 10;
        int fortHeight = 8;

        // Fortress A: top-left
        int axStart = -20;
        int azStart = -5;
        BuildMenuFortress(_voxelWorld, axStart, groundY, azStart, fortSizeX, fortHeight, fortSizeZ,
            VoxelMaterialType.Stone, VoxelMaterialType.Brick);

        // Fortress B: top-right
        int bxStart = 10;
        int bzStart = -5;
        BuildMenuFortress(_voxelWorld, bxStart, groundY, bzStart, fortSizeX, fortHeight, fortSizeZ,
            VoxelMaterialType.Brick, VoxelMaterialType.Metal);

        // Fortress C: bottom-left
        int cxStart = -20;
        int czStart = 15;
        BuildMenuFortress(_voxelWorld, cxStart, groundY, czStart, fortSizeX, fortHeight, fortSizeZ,
            VoxelMaterialType.Metal, VoxelMaterialType.Stone);

        // Fortress D: bottom-right
        int dxStart = 10;
        int dzStart = 15;
        BuildMenuFortress(_voxelWorld, dxStart, groundY, dzStart, fortSizeX, fortHeight, fortSizeZ,
            VoxelMaterialType.Brick, VoxelMaterialType.Stone);

        // Center of all 4 fortresses
        float midX = ((axStart + fortSizeX / 2) + (bxStart + fortSizeX / 2)
                     + (cxStart + fortSizeX / 2) + (dxStart + fortSizeX / 2)) * 0.25f * GameConfig.MicrovoxelMeters;
        float midZ = ((azStart + fortSizeZ / 2) + (bzStart + fortSizeZ / 2)
                     + (czStart + fortSizeZ / 2) + (dzStart + fortSizeZ / 2)) * 0.25f * GameConfig.MicrovoxelMeters;
        float midY = (groundY + fortHeight / 2) * GameConfig.MicrovoxelMeters;
        _menuBattleCenter = new Vector3(midX, midY, midZ);

        // --- Fortress A weapons (facing right, inner edge) ---
        int aTopY = groundY + fortHeight;
        int aWpn1BUx = (axStart + 8) / GameConfig.MicrovoxelsPerBuildUnit;
        int aWpn2BUx = (axStart + 8) / GameConfig.MicrovoxelsPerBuildUnit;
        int aWpnBUy = aTopY / GameConfig.MicrovoxelsPerBuildUnit;
        int aWpn1BUz = (azStart + 2) / GameConfig.MicrovoxelsPerBuildUnit;
        int aWpn2BUz = (azStart + 7) / GameConfig.MicrovoxelsPerBuildUnit;

        _menuWeaponsA.Add(_weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld,
            new Vector3I(aWpn1BUx, aWpnBUy, aWpn1BUz), PlayerSlot.Player1));
        _menuWeaponsA.Add(_weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld,
            new Vector3I(aWpn2BUx, aWpnBUy, aWpn2BUz), PlayerSlot.Player1));

        // --- Fortress B weapons (facing left, inner edge) ---
        int bTopY = groundY + fortHeight;
        int bWpn1BUx = (bxStart + 1) / GameConfig.MicrovoxelsPerBuildUnit;
        int bWpn2BUx = (bxStart + 1) / GameConfig.MicrovoxelsPerBuildUnit;
        int bWpnBUy = bTopY / GameConfig.MicrovoxelsPerBuildUnit;
        int bWpn1BUz = (bzStart + 2) / GameConfig.MicrovoxelsPerBuildUnit;
        int bWpn2BUz = (bzStart + 7) / GameConfig.MicrovoxelsPerBuildUnit;

        _menuWeaponsB.Add(_weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld,
            new Vector3I(bWpn1BUx, bWpnBUy, bWpn1BUz), PlayerSlot.Player2));
        _menuWeaponsB.Add(_weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld,
            new Vector3I(bWpn2BUx, bWpnBUy, bWpn2BUz), PlayerSlot.Player2));

        // --- Fortress C weapons (facing right, inner edge) ---
        int cTopY = groundY + fortHeight;
        int cWpn1BUx = (cxStart + 8) / GameConfig.MicrovoxelsPerBuildUnit;
        int cWpn2BUx = (cxStart + 8) / GameConfig.MicrovoxelsPerBuildUnit;
        int cWpnBUy = cTopY / GameConfig.MicrovoxelsPerBuildUnit;
        int cWpn1BUz = (czStart + 2) / GameConfig.MicrovoxelsPerBuildUnit;
        int cWpn2BUz = (czStart + 7) / GameConfig.MicrovoxelsPerBuildUnit;

        _menuWeaponsC.Add(_weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld,
            new Vector3I(cWpn1BUx, cWpnBUy, cWpn1BUz), PlayerSlot.Player3));
        _menuWeaponsC.Add(_weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld,
            new Vector3I(cWpn2BUx, cWpnBUy, cWpn2BUz), PlayerSlot.Player3));

        // --- Fortress D weapons (facing left, inner edge) ---
        int dTopY = groundY + fortHeight;
        int dWpn1BUx = (dxStart + 1) / GameConfig.MicrovoxelsPerBuildUnit;
        int dWpn2BUx = (dxStart + 1) / GameConfig.MicrovoxelsPerBuildUnit;
        int dWpnBUy = dTopY / GameConfig.MicrovoxelsPerBuildUnit;
        int dWpn1BUz = (dzStart + 2) / GameConfig.MicrovoxelsPerBuildUnit;
        int dWpn2BUz = (dzStart + 7) / GameConfig.MicrovoxelsPerBuildUnit;

        _menuWeaponsD.Add(_weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld,
            new Vector3I(dWpn1BUx, dWpnBUy, dWpn1BUz), PlayerSlot.Player4));
        _menuWeaponsD.Add(_weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld,
            new Vector3I(dWpn2BUx, dWpnBUy, dWpn2BUz), PlayerSlot.Player4));

        _menuBattleTimer = 3.0f; // delay before first shot after menu appears
        _menuOrbitAngle = 0f;
        _menuFireRound = 0;
        // Don't activate battle yet — wait until splash finishes so weapons
        // don't start firing while the splash screen is still visible.
        // OnSplashFinished() or ReturnToMainMenu() will set _menuBattleActive = true.

        // Position camera but do NOT activate it — prevents player from moving
        // camera during menu. We set position directly via Current + GlobalPosition.
        if (_camera != null)
        {
            _camera.Current = true;
        }
        UpdateMenuOrbitCamera(0f);
    }

    private static void BuildMenuFortress(VoxelWorld world, int startX, int startY, int startZ,
        int sizeX, int sizeY, int sizeZ, VoxelMaterialType wallMat, VoxelMaterialType accentMat)
    {
        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                {
                    bool isEdge = x == 0 || x == sizeX - 1 || z == 0 || z == sizeZ - 1;
                    if (isEdge || y == 0)
                        world.SetVoxel(new Vector3I(startX + x, startY + y, startZ + z), Voxel.Voxel.Create(wallMat));
                }

        int[][] corners = { new[] { 0, 0 }, new[] { sizeX - 1, 0 }, new[] { 0, sizeZ - 1 }, new[] { sizeX - 1, sizeZ - 1 } };
        foreach (int[] c in corners)
            for (int y = sizeY; y < sizeY + 2; y++)
                world.SetVoxel(new Vector3I(startX + c[0], startY + y, startZ + c[1]), Voxel.Voxel.Create(accentMat));

        for (int x = 1; x < sizeX - 1; x++)
            if (x % 2 == 0)
            {
                world.SetVoxel(new Vector3I(startX + x, startY + sizeY, startZ), Voxel.Voxel.Create(accentMat));
                world.SetVoxel(new Vector3I(startX + x, startY + sizeY, startZ + sizeZ - 1), Voxel.Voxel.Create(accentMat));
            }
        for (int z = 1; z < sizeZ - 1; z++)
            if (z % 2 == 0)
            {
                world.SetVoxel(new Vector3I(startX, startY + sizeY, startZ + z), Voxel.Voxel.Create(accentMat));
                world.SetVoxel(new Vector3I(startX + sizeX - 1, startY + sizeY, startZ + z), Voxel.Voxel.Create(accentMat));
            }

        int midZ = sizeZ / 2;
        for (int y = 0; y < sizeY - 2; y++)
            for (int x = 2; x < sizeX - 2; x++)
                world.SetVoxel(new Vector3I(startX + x, startY + y, startZ + midZ), Voxel.Voxel.Create(accentMat));
    }

    private void ProcessMenuBattle(double delta)
    {
        if (!_menuBattleActive || _voxelWorld == null || _aimingSystem == null) return;

        float dt = (float)delta;
        UpdateMenuOrbitCamera(dt);

        _menuBattleTimer -= dt;
        if (_menuBattleTimer <= 0f)
        {
            _menuBattleTimer = 2.0f + (float)new Random().NextDouble() * 1.5f;
            _menuFireRound++;

            List<WeaponBase>[] teams = { _menuWeaponsA, _menuWeaponsB, _menuWeaponsC, _menuWeaponsD };
            Random rng = new Random(System.Environment.TickCount ^ _menuFireRound);

            // Pick a random attacker team, then a different defender team
            int attackIdx = rng.Next(teams.Length);
            int defendIdx = attackIdx;
            while (defendIdx == attackIdx)
                defendIdx = rng.Next(teams.Length);

            List<WeaponBase> attackers = teams[attackIdx];
            List<WeaponBase> defenders = teams[defendIdx];
            if (attackers.Count == 0 || defenders.Count == 0) return;

            WeaponBase weapon = attackers[rng.Next(attackers.Count)];
            if (!GodotObject.IsInstanceValid(weapon)) return;

            WeaponBase target = defenders[rng.Next(defenders.Count)];
            if (!GodotObject.IsInstanceValid(target)) return;

            Vector3 targetPos = target.GlobalPosition + new Vector3(
                ((float)rng.NextDouble() - 0.5f) * 3f,
                ((float)rng.NextDouble() - 0.5f) * 1f,
                ((float)rng.NextDouble() - 0.5f) * 3f);

            _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetPos, weapon.ProjectileSpeed, weapon.WeaponId);
            // Round number increments each fire, so CanFire always passes
            weapon.Fire(_aimingSystem, _voxelWorld, _menuFireRound + 1000);
        }
    }

    private void UpdateMenuOrbitCamera(float delta)
    {
        if (_camera == null) return;
        // Top-down view centered on the battle, no orbiting
        float topDownHeight = 45f;
        Vector3 camPos = new Vector3(_menuBattleCenter.X, _menuBattleCenter.Y + topDownHeight, _menuBattleCenter.Z + 0.01f);
        _camera.SetLookTarget(camPos, _menuBattleCenter);
    }

    private void CleanupMenuBattle()
    {
        _menuBattleActive = false;

        // Free all menu weapons immediately (Free, not QueueFree, to prevent deferred impacts)
        foreach (WeaponBase w in _menuWeaponsA)
            if (GodotObject.IsInstanceValid(w)) w.Free();
        foreach (WeaponBase w in _menuWeaponsB)
            if (GodotObject.IsInstanceValid(w)) w.Free();
        foreach (WeaponBase w in _menuWeaponsC)
            if (GodotObject.IsInstanceValid(w)) w.Free();
        foreach (WeaponBase w in _menuWeaponsD)
            if (GodotObject.IsInstanceValid(w)) w.Free();
        _menuWeaponsA.Clear();
        _menuWeaponsB.Clear();
        _menuWeaponsC.Clear();
        _menuWeaponsD.Clear();

        // Destroy any in-flight projectiles so they don't impact the new arena
        foreach (Node node in GetTree().GetNodesInGroup("Projectiles"))
            if (GodotObject.IsInstanceValid(node)) node.Free();

        // Clear all army troops
        _armyManager?.ClearAll();

        // ── Clean up ALL FX that may persist from the menu battle ──

        // Extinguish all fires (particles, lights, burning state)
        FireSystem.Instance?.ExtinguishAll();

        // Clear pooled explosion effects, then free any active ones from the scene tree
        ExplosionFX.ClearAll();

        // Clear all debris, ruins, and queued debris spawns
        DebrisFX.ClearAll();

        // Clear scorch mark decals
        ImpactDecals.ClearAll();

        // Clear falling chunks from structural collapse
        FallingChunk.ClearAll();

        // Clear pooled dust effects
        DustFX.ClearAll();

        // Free any remaining FX nodes parented to the scene root
        // (ExplosionFX, DustFX, and other stray effects spawned with GetTree().Root as parent)
        Node root = GetTree().Root;
        foreach (Node child in root.GetChildren())
        {
            if (child is ExplosionFX or DustFX)
            {
                child.Free();
            }
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled -= OnCommanderKilled;
            EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
            EventBus.Instance.TurnChanged -= OnTurnChanged;
            EventBus.Instance.WeaponFired -= OnWeaponFired;
            EventBus.Instance.WeaponDestroyed -= OnWeaponDestroyed;
            EventBus.Instance.RailgunBeamFired -= OnRailgunBeamFired;
            EventBus.Instance.VoxelChanged -= OnVoxelChangedStats;
        }

        if (_combatCamera != null)
        {
            _combatCamera.ExitWeaponPOVRequested -= OnExitWeaponPOVRequested;
            _combatCamera.TargetClickRequested -= OnTargetClickRequested;
            _combatCamera.TargetCycleRequested -= OnTargetCycleRequested;
            _combatCamera.CinematicFinished -= OnCombatCameraCinematicFinished;
        }

        if (_powerupExecutor != null)
        {
            _powerupExecutor.PowerupActivated -= OnPowerupActivated;
            _powerupExecutor.PowerupExpired -= OnPowerupExpired;
        }
    }

    public override void _Process(double delta)
    {
        UpdateHUD();

        switch (CurrentPhase)
        {
            case GamePhase.Menu:
                ProcessMenuBattle(delta);
                break;
            case GamePhase.Lobby:
                // Lobby UI handles its own rendering via _Process
                break;
            case GamePhase.Building:
                ProcessBuildPhase(delta);
                break;
            case GamePhase.FogReveal:
                ProcessFogRevealPhase(delta);
                break;
            case GamePhase.Combat:
                ProcessCombatPhase(delta);
                break;
        }

        // Update combat countdown overlay (runs during FogReveal phase)
        if (_countdownActive)
        {
            ProcessCombatCountdown((float)delta);
        }

        // Countdown timer for timed phases (disabled in sandbox mode)
        if (_phaseCountdownSeconds > 0f && !_isSandbox)
        {
            _phaseCountdownSeconds = Mathf.Max(0f, _phaseCountdownSeconds - (float)delta);
            if (_phaseCountdownSeconds <= 0f)
            {
                OnPhaseTimerExpired();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (CurrentPhase)
        {
            case GamePhase.Building:
                HandleBuildInput(@event);
                break;
            case GamePhase.Combat:
                HandleCombatInput(@event);
                break;
        }
    }

    public void SetPhase(GamePhase phase, float countdownSeconds = 0f)
    {
        if (CurrentPhase == phase && Mathf.IsEqualApprox(_phaseCountdownSeconds, countdownSeconds))
        {
            return;
        }

        GamePhase previous = CurrentPhase;
        CurrentPhase = phase;
        _phaseCountdownSeconds = countdownSeconds;
        OnPhaseEntered(phase);
        EventBus.Instance?.EmitPhaseChanged(new PhaseChangedEvent(previous, phase));
        _steamPlatform?.Platform.SetRichPresence("status", phase.ToString());
    }

    public PlayerData? GetPlayer(PlayerSlot slot)
    {
        return _players.TryGetValue(slot, out PlayerData? player) ? player : null;
    }

    /// <summary>
    /// Returns the currently selected weapon for the current player, or null.
    /// Used by CombatUI for trajectory/impact crosshair calculation.
    /// </summary>
    public WeaponBase? GetSelectedWeapon()
    {
        if (_selectedWeaponIndex < 0)
        {
            return null; // No weapon selected yet
        }

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return null;
        }

        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return null;
        }

        int safeIndex = _selectedWeaponIndex % weaponList.Count;
        WeaponBase? weapon = weaponList[safeIndex];
        if (weapon == null || !GodotObject.IsInstanceValid(weapon))
        {
            return null;
        }

        return weapon;
    }

    /// <summary>
    /// Called from CombatUI when a weapon button is clicked.
    /// Selects the weapon and enters targeting mode immediately.
    /// If already in targeting mode, switches the weapon without resetting
    /// the camera or target point so the player's aim is preserved.
    /// </summary>
    public void OnWeaponSelectedFromUI(int weaponIndex)
    {
        if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        int newIndex = weaponIndex % weaponList.Count;

        // If already in targeting mode, just swap the weapon without resetting
        // the camera or target so the player's aim is preserved.
        if (_isAiming && _weaponConfirmed)
        {
            _selectedWeaponIndex = newIndex;
            UpdateWeaponHighlight();

            // If there's an active target, recalculate the trajectory preview
            // for the new weapon's projectile speed / arc.
            if (_hasTarget && _aimingSystem != null && _aimingSystem.HasTarget)
            {
                WeaponBase? newWeapon = weaponList[newIndex];
                if (newWeapon != null && GodotObject.IsInstanceValid(newWeapon))
                {
                    _aimingSystem.SetTargetPoint(
                        newWeapon.GlobalPosition,
                        _aimingSystem.TargetPoint,
                        newWeapon.ProjectileSpeed,
                        newWeapon.WeaponId);
                }
            }

            GD.Print($"[Combat] Switched to weapon {_selectedWeaponIndex} (target preserved).");
            return;
        }

        _selectedWeaponIndex = newIndex;
        _weaponConfirmed = true;

        // Highlight the confirmed weapon in the 3D world
        UpdateWeaponHighlight();

        GD.Print($"[Combat] Weapon {_selectedWeaponIndex} confirmed. Entering targeting mode.");

        // Transition to targeting mode
        TransitionToTargeting(currentPlayer);
    }


    /// <summary>
    /// Starts sandbox mode: a single build zone with no opponents or timer.
    /// The player can build freely and save/load their designs.
    /// </summary>
    public void StartSandboxMode()
    {
        if (CurrentPhase != GamePhase.Menu && CurrentPhase != GamePhase.GameOver)
        {
            GD.Print("[GameManager] StartSandboxMode ignored: match already in progress.");
            return;
        }

        _isSandbox = true;
        Settings.BotCount = 0;
        Settings.StartingBudget = GameConfig.SandboxBudget;

        // Reuse the normal match flow but with sandbox flag
        StartPrototypeMatch();
    }

    /// <summary>
    /// Saves the current sandbox build using BlueprintSystem.
    /// </summary>
    public void SaveSandboxBuild(string buildName)
    {
        if (!_isSandbox || _voxelWorld == null)
        {
            GD.Print("[Sandbox] Cannot save: not in sandbox mode.");
            return;
        }

        if (!_buildZones.TryGetValue(PlayerSlot.Player1, out BuildZone zone))
        {
            GD.Print("[Sandbox] Cannot save: no build zone found.");
            return;
        }

        if (_blueprintSystem == null)
        {
            _blueprintSystem = new BlueprintSystem();
        }

        BlueprintData blueprint = _blueprintSystem.Capture(_voxelWorld, zone, buildName);
        _blueprintSystem.SaveBlueprint(blueprint);

        // Track in player profile
        PlayerProfile? profile = _progressionManager?.Profile;
        if (profile != null)
        {
            if (!profile.SavedBuilds.Contains(buildName))
            {
                profile.SavedBuilds.Add(buildName);
            }
            SaveSystem.SaveJson("user://profile.json", profile);
        }

        GD.Print($"[Sandbox] Build '{buildName}' saved ({blueprint.Voxels.Count} voxels).");
    }

    /// <summary>
    /// Loads a sandbox build into the current build zone.
    /// </summary>
    public void LoadSandboxBuild(string buildName)
    {
        if (_voxelWorld == null)
        {
            GD.Print("[Sandbox] Cannot load: no voxel world.");
            return;
        }

        if (!_buildZones.TryGetValue(_isSandbox ? PlayerSlot.Player1 : _activeBuilder, out BuildZone zone))
        {
            GD.Print("[Sandbox] Cannot load: no build zone found.");
            return;
        }

        if (_blueprintSystem == null)
        {
            _blueprintSystem = new BlueprintSystem();
        }

        BlueprintData? blueprint = _blueprintSystem.LoadBlueprint(buildName);
        if (blueprint == null)
        {
            GD.Print($"[Sandbox] Build '{buildName}' not found.");
            return;
        }

        // Clear existing voxels in the zone (only player-placed, not foundation)
        var clearChanges = new List<(Vector3I Position, Voxel.Voxel NewVoxel)>();
        for (int z = zone.OriginMicrovoxels.Z; z <= zone.MaxMicrovoxelsInclusive.Z; z++)
        {
            for (int y = zone.OriginMicrovoxels.Y; y <= zone.MaxMicrovoxelsInclusive.Y; y++)
            {
                for (int x = zone.OriginMicrovoxels.X; x <= zone.MaxMicrovoxelsInclusive.X; x++)
                {
                    Vector3I pos = new Vector3I(x, y, z);
                    Voxel.Voxel v = _voxelWorld.GetVoxel(pos);
                    if (!v.IsAir && v.Material != VoxelMaterialType.Foundation)
                    {
                        clearChanges.Add((pos, Voxel.Voxel.Air));
                    }
                }
            }
        }
        if (clearChanges.Count > 0)
        {
            _voxelWorld.ApplyBulkChanges(clearChanges, PlayerSlot.Player1);
        }

        // Place the blueprint voxels
        var placeChanges = new List<(Vector3I Position, Voxel.Voxel NewVoxel)>();
        foreach (BlueprintVoxelData bv in blueprint.Voxels)
        {
            Vector3I worldPos = zone.OriginMicrovoxels + new Vector3I(bv.X, bv.Y, bv.Z);
            Voxel.Voxel voxel = new Voxel.Voxel(bv.Data);
            if (voxel.Material != VoxelMaterialType.Foundation) // Don't overwrite foundation
            {
                placeChanges.Add((worldPos, voxel));
            }
        }
        if (placeChanges.Count > 0)
        {
            _voxelWorld.ApplyBulkChanges(placeChanges, PlayerSlot.Player1);
        }

        GD.Print($"[Sandbox] Build '{buildName}' loaded ({placeChanges.Count} voxels placed).");
    }

    /// <summary>
    /// Returns the list of saved sandbox build names from the player profile.
    /// </summary>
    public List<string> GetSavedBuildNames()
    {
        return _progressionManager?.Profile?.SavedBuilds ?? new List<string>();
    }

    /// <summary>
    /// Called from the menu UI to start a match. Shows a splash/loading screen first,
    /// then proceeds with the full flow: Menu -> Build -> FogReveal -> Combat -> GameOver.
    /// </summary>
    public void StartPrototypeMatch()
    {
        // Guard: don't start a new match if one is already in progress or loading
        if (CurrentPhase != GamePhase.Menu && CurrentPhase != GamePhase.GameOver)
        {
            GD.Print("[GameManager] StartPrototypeMatch ignored: match already in progress.");
            return;
        }
        if (_loadingSplash != null)
        {
            GD.Print("[GameManager] StartPrototypeMatch ignored: loading splash already active.");
            return;
        }

        // Clean up any existing match state before starting a new one
        CleanupMatchState();

        // Immediately move the camera above ground level to prevent it from
        // briefly dipping under the map during the menu-to-game transition.
        if (_camera != null)
        {
            Vector3 safePos = _camera.GlobalPosition;
            if (safePos.Y < 5f)
            {
                safePos.Y = 15f;
                _camera.GlobalPosition = safePos;
                _camera.SetLookTarget(safePos, safePos + Vector3.Forward * 10f);
            }
        }

        // Show a loading splash screen before starting the match.
        // Wrap in a CanvasLayer so the Control actually renders (GameManager is a Node, not CanvasItem).
        _loadingSplashLayer = new CanvasLayer();
        _loadingSplashLayer.Name = "LoadingSplashLayer";
        _loadingSplashLayer.Layer = 99;
        AddChild(_loadingSplashLayer);

        _loadingSplash = new SplashScreen();
        _loadingSplash.Name = "LoadingSplash";
        _loadingSplash.IsLoadingMode = true;
        _loadingSplashLayer.AddChild(_loadingSplash);

        // Hide the HUD and world during the splash so the loading screen covers everything
        if (_hudRoot != null) _hudRoot.Visible = false;
        if (_voxelWorld != null) _voxelWorld.Visible = false;

        // When the splash finishes (after explosion + fade), remove it and show HUD
        _loadingSplash.SplashFinished += OnLoadingSplashFinished;

        // Do setup on next frame so loading screen has a chance to render
        CallDeferred(nameof(PerformMatchSetupDuringLoading));
    }

    private void PerformMatchSetupDuringLoading()
    {
        // Step 1: Seed players and reset match state
        if (_loadingSplash == null) return;

        // Only seed local players if we're not in an online match
        // (online players were already seeded by SeedOnlinePlayers)
        if (_networkManager == null || !_networkManager.IsOnline)
        {
            SeedLocalPlayers();
        }
        foreach (PlayerData player in _players.Values)
        {
            player.ResetForMatch(Settings);
        }

        _loadingSplash.SetLoadingProgress(0.2f);

        // Yield to engine so the loading screen can render, then continue
        CallDeferred(nameof(LoadingStep2_BuildZones));
    }

    private void LoadingStep2_BuildZones()
    {
        if (_loadingSplash == null) return;

        SetupBuildZones();
        _loadingSplash.SetLoadingProgress(0.4f);

        CallDeferred(nameof(LoadingStep3_Terrain));
    }

    private void LoadingStep3_Terrain()
    {
        if (_loadingSplash == null) return;

        GenerateBuildFoundations();
        _loadingSplash.SetLoadingProgress(0.7f);

        CallDeferred(nameof(LoadingStep4_Lighting));
    }

    private void LoadingStep4_Lighting()
    {
        if (_loadingSplash == null) return;

        SetupVoxelGiAndLighting();
        _loadingSplash.SetLoadingProgress(0.9f);

        CallDeferred(nameof(LoadingStep5_Finalize));
    }

    private void LoadingStep5_Finalize()
    {
        if (_loadingSplash == null) return;

        _activeBuilderIndex = 0;
        _activeBuilder = BuildOrder[_activeBuilderIndex];
        PositionCameraAtBuildZone(PlayerSlot.Player1);

        // Signal loading complete -- triggers castle explosion, then fade, then SplashFinished
        _loadingSplash.SetLoadingProgress(1.0f);
    }

    private void OnLoadingSplashFinished()
    {
        // Remove the loading splash and its canvas layer
        if (_loadingSplash != null)
        {
            _loadingSplash.SplashFinished -= OnLoadingSplashFinished;
            _loadingSplash.QueueFree();
            _loadingSplash = null;
        }
        if (_loadingSplashLayer != null)
        {
            _loadingSplashLayer.QueueFree();
            _loadingSplashLayer = null;
        }

        // Show the world now that loading is complete
        if (_voxelWorld != null) _voxelWorld.Visible = true;

        // Start the build phase FIRST so BuildUI receives PhaseChanged/BudgetChanged
        // events and populates itself before the HUD becomes visible
        SetPhase(GamePhase.Building, _isSandbox ? 0f : PrototypeBuildPhaseSeconds);
        _camera?.Activate();

        // Re-apply camera position AFTER activation to ensure it takes effect
        PositionCameraAtBuildZone(_activeBuilder);

        // Show the HUD after phase events have propagated so UI is already populated
        if (_hudRoot != null) _hudRoot.Visible = true;

        // In sandbox mode, hide the ready button and show sandbox controls
        if (_isSandbox)
        {
            if (_readyButton != null) _readyButton.Visible = false;
            BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                ?? GetNodeOrNull<BuildUI>("%BuildUI")
                ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
            buildUI?.EnableSandboxMode(GetSavedBuildNames());
        }
    }

    /// <summary>
    /// Cleans up all match state so a new match can start cleanly.
    /// </summary>
    public void CleanupMatchState()
    {
        // Clean up menu battle scene if active
        CleanupMenuBattle();

        // Clean up existing commanders
        foreach (CommanderActor commander in _commanders.Values)
        {
            if (GodotObject.IsInstanceValid(commander))
            {
                commander.QueueFree();
            }
        }
        _commanders.Clear();

        // Clean up existing weapons
        foreach (List<WeaponBase> weaponList in _weapons.Values)
        {
            foreach (WeaponBase weapon in weaponList)
            {
                if (GodotObject.IsInstanceValid(weapon))
                {
                    weapon.QueueFree();
                }
            }
        }
        _weapons.Clear();

        // Clean up bot controllers
        foreach (AI.BotController bot in _botControllers.Values)
        {
            if (GodotObject.IsInstanceValid(bot))
            {
                bot.QueueFree();
            }
        }
        _botControllers.Clear();

        // Clean up combat countdown overlay if active
        _countdownActive = false;
        if (_countdownLayer != null)
        {
            _countdownLayer.QueueFree();
            _countdownLayer = null;
        }
        _countdownBg = null;
        _countdownLabel = null;

        // Clean up artillery dominance state
        _artilleryDominanceActive = false;
        if (_dominanceBannerLayer != null && IsInstanceValid(_dominanceBannerLayer))
        {
            _dominanceBannerLayer.QueueFree();
            _dominanceBannerLayer = null;
        }

        // Clean up loading splash if active
        if (_loadingSplash != null)
        {
            _loadingSplash.QueueFree();
            _loadingSplash = null;
        }
        if (_loadingSplashLayer != null)
        {
            _loadingSplashLayer.QueueFree();
            _loadingSplashLayer = null;
        }

        // Reset turn manager
        _turnManager?.StopTurnClock();

        // Reset aiming and placement state, release mouse if captured
        _isAiming = false;
        _selectedWeaponIndex = -1; // No weapon selected until player clicks one
        _placementMode = PlacementMode.Block;
        _weaponPreviewMeshes.Clear();
        _commanderPreviewMesh = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Hide ghost preview
        _ghostPreview?.Hide();
    }

    /// <summary>
    /// Fully returns to the main menu: cleans up all match state, regenerates
    /// the menu background terrain with the 4-CPU demo battle, and switches
    /// back to the Menu phase so the MainMenu UI becomes visible.
    /// Called from PauseMenu "Quit to Menu" and GameOverUI "Return to Menu".
    /// </summary>
    public void ReturnToMainMenu()
    {
        // Restore normal time scale (may have been slowed for bombardment)
        Engine.TimeScale = 1.0;
        _artilleryDominanceActive = false;

        // 1. Clean up all match objects (commanders, weapons, bots, projectiles, FX, etc.)
        CleanupMatchState();

        // 2. Regenerate the menu background terrain and restart the 4-CPU demo battle.
        //    GenerateMenuBackgroundTerrain clears the voxel world, rebuilds the arena
        //    with decorative fortresses, and calls SetupMenuBattleScene() at the end.
        GenerateMenuBackgroundTerrain();
        _menuBattleActive = true; // No splash when returning, activate battle immediately

        // 3. Switch camera back to the passive FreeFlyCamera (menu orbit) and deactivate CombatCamera.
        //    Reset FOV to default so bombardment/combat zoom doesn't persist on the menu.
        _combatCamera?.Deactivate();
        if (_camera != null)
        {
            _camera.Fov = 70f; // Default FOV
            _camera.Current = true;
        }

        // 4. Shut down networking if online
        _networkManager?.Shutdown();
        _lobbyManager?.Clear();
        HideLobbyUI();

        // 5. Set phase to Menu — this shows the MainMenu UI, hides combat elements,
        //    and suppresses camera shake so the demo battle runs quietly in the background.
        SetPhase(GamePhase.Menu);
    }

    private void ExitSandboxMode()
    {
        _isSandbox = false;
        ReturnToMainMenu();
    }

    // ─────────────────────────────────────────────────
    //  MULTIPLAYER NETWORKING
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Called when the host clicks "HOST GAME" in the main menu.
    /// The lobby manager is already populated and the network host is started by MainMenu.
    /// We just need to transition to the lobby screen.
    /// </summary>
    private void OnHostGameRequested(bool isPublic)
    {
        GD.Print($"[GameManager] Host game requested (public={isPublic})");
        ShowLobbyUI(isHost: true);
        SetPhase(GamePhase.Lobby);
    }

    /// <summary>
    /// Called when the player clicks "JOIN" with an IP address in the main menu.
    /// NetworkManager.Join() has already been called by MainMenu.
    /// We show the lobby screen and wait for server connection.
    /// </summary>
    private void OnJoinWithCodeRequested(string code)
    {
        GD.Print($"[GameManager] Join with code requested: {code}");
        ShowLobbyUI(isHost: false);
        SetPhase(GamePhase.Lobby);
    }

    /// <summary>
    /// Client successfully connected to the server.
    /// </summary>
    private void OnConnectedToServer()
    {
        GD.Print("[GameManager] Successfully connected to server.");
    }

    /// <summary>
    /// Client failed to connect to the server.
    /// </summary>
    private void OnConnectionFailed()
    {
        GD.Print("[GameManager] Connection failed, returning to menu.");
        HideLobbyUI();
        _lobbyManager?.Clear();
        SetPhase(GamePhase.Menu);
    }

    /// <summary>
    /// A new peer connected to the network session.
    /// On the host, this means a new player joined.
    /// On clients, this fires for every peer (including when they connect).
    /// </summary>
    private void OnNetworkPeerConnected(long peerId)
    {
        GD.Print($"[GameManager] *** Peer connected: {peerId} (IsHost={_networkManager?.IsHost}) ***");

        // Host: add peer to lobby when they announce their name (see OnPlayerAnnounced).
        // The host also sees itself connect (peerId=1), which is already handled in StartHosting.

        // If we're the host, broadcast current lobby state to the new peer
        if (_networkManager?.IsHost == true && _lobbyManager != null)
        {
            GD.Print($"[GameManager] Broadcasting lobby state to new peer {peerId}");
            BroadcastLobbyState();
        }
    }

    /// <summary>
    /// A peer disconnected from the network session.
    /// </summary>
    private void OnNetworkPeerDisconnected(long peerId)
    {
        GD.Print($"[GameManager] Peer disconnected: {peerId}");

        if (_lobbyManager == null || _networkManager == null) return;

        _lobbyManager.RemoveMember(peerId);

        // If we're the host, broadcast updated lobby state to remaining peers
        if (_networkManager.IsHost)
        {
            BroadcastLobbyState();
        }
    }

    /// <summary>
    /// A remote player announced their display name (host-side only).
    /// </summary>
    private void OnPlayerAnnounced(long peerId, string displayName)
    {
        if (_lobbyManager == null || _networkManager == null) return;

        if (!_networkManager.IsHost)
        {
            // Clients should not process announcements directly
            return;
        }

        GD.Print($"[GameManager] Player announced: '{displayName}' (peer {peerId})");

        // Assign the next available slot
        PlayerSlot? slot = _lobbyManager.GetNextAvailableSlot();
        if (slot == null)
        {
            GD.Print($"[GameManager] Lobby is full, rejecting peer {peerId}");
            // Could disconnect the peer here, but for now just ignore
            return;
        }

        _lobbyManager.AddOrUpdateMember(peerId, slot.Value, displayName, false);

        // Broadcast updated lobby state to all peers
        BroadcastLobbyState();
    }

    /// <summary>
    /// A remote peer changed their ready state (host-side only).
    /// </summary>
    private void OnPlayerReadyChanged(long peerId, bool ready)
    {
        if (_lobbyManager == null || _networkManager == null) return;

        if (!_networkManager.IsHost)
        {
            return;
        }

        GD.Print($"[GameManager] Player ready changed: peer {peerId} -> {ready}");
        _lobbyManager.SetReady(peerId, ready);

        // Broadcast updated lobby state
        BroadcastLobbyState();
    }

    /// <summary>
    /// Received a full lobby state from the host (client-side).
    /// </summary>
    private void OnLobbyStateReceived(LobbyStatePayload payload)
    {
        if (_lobbyManager == null) return;

        GD.Print($"[GameManager] Lobby state received with {payload.Players.Length} players");
        _lobbyManager.ApplyStatePayload(payload);
    }

    /// <summary>
    /// Host told us to start the match.
    /// </summary>
    private void OnMatchStartReceived(MatchStartPayload settings)
    {
        GD.Print("[GameManager] Match start received from host.");

        // Apply match settings from the host
        Settings.BuildTimeSeconds = settings.BuildTimeSeconds;
        Settings.StartingBudget = settings.StartingBudget;
        Settings.ArenaSize = settings.ArenaSize;
        Settings.TurnTimeSeconds = settings.TurnTimeSeconds;
        Settings.BotCount = 0; // No bots in online play

        // Seed players from the lobby members
        SeedOnlinePlayers();

        HideLobbyUI();
        StartPrototypeMatch();
    }

    /// <summary>
    /// Broadcasts the current lobby state from host to all peers.
    /// </summary>
    private void BroadcastLobbyState()
    {
        if (_lobbyManager == null || _networkManager == null || !_networkManager.IsHost) return;

        LobbyStatePayload payload = _lobbyManager.BuildStatePayload();
        GD.Print($"[GameManager] Broadcasting lobby state: {payload.Players.Length} player(s)");
        foreach (var p in payload.Players)
        {
            GD.Print($"  → Peer {p.PeerId}: '{p.DisplayName}' slot={p.SlotIndex} ready={p.IsReady}");
        }
        byte[] data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        _networkManager.Rpc(nameof(NetworkManager.BroadcastLobbyState), data);
    }

    /// <summary>
    /// Creates and shows the lobby UI overlay.
    /// </summary>
    private void ShowLobbyUI(bool isHost)
    {
        HideLobbyUI(); // Clean up any existing lobby UI

        _lobbyUILayer = new CanvasLayer();
        _lobbyUILayer.Name = "LobbyUILayer";
        _lobbyUILayer.Layer = 50;
        AddChild(_lobbyUILayer);

        _lobbyUI = new LobbyUI();
        _lobbyUI.Name = "LobbyUI";
        _lobbyUILayer.AddChild(_lobbyUI);
        // SetIsHost must be called AFTER AddChild so GetTree() works
        _lobbyUI.SetIsHost(isHost);

        // Wire up lobby UI events
        _lobbyUI.ReadyToggled += OnLobbyReadyToggled;
        _lobbyUI.StartGameRequested += OnLobbyStartGameRequested;
        _lobbyUI.LeaveLobbyRequested += OnLobbyLeaveRequested;

        // Hide the main menu
        Control? mainMenu = GetNodeOrNull<Control>("MainMenu");
        if (mainMenu != null)
        {
            mainMenu.Visible = false;
        }
    }

    /// <summary>
    /// Removes the lobby UI overlay.
    /// </summary>
    private void HideLobbyUI()
    {
        if (_lobbyUI != null)
        {
            _lobbyUI.ReadyToggled -= OnLobbyReadyToggled;
            _lobbyUI.StartGameRequested -= OnLobbyStartGameRequested;
            _lobbyUI.LeaveLobbyRequested -= OnLobbyLeaveRequested;
            _lobbyUI.QueueFree();
            _lobbyUI = null;
        }
        if (_lobbyUILayer != null)
        {
            _lobbyUILayer.QueueFree();
            _lobbyUILayer = null;
        }
    }

    /// <summary>
    /// Player toggled their ready state in the lobby UI.
    /// </summary>
    private void OnLobbyReadyToggled()
    {
        if (_networkManager == null || _lobbyManager == null || _lobbyUI == null) return;

        bool newReady = _lobbyUI.ToggleReady();

        if (_networkManager.IsHost)
        {
            // Host updates their own ready state directly
            _lobbyManager.SetReady(_networkManager.LocalPeerId, newReady);
            BroadcastLobbyState();
        }
        else
        {
            // Client sends ready state to the host
            _networkManager.RpcId(1, nameof(NetworkManager.SetPlayerReady), newReady);
        }
    }

    /// <summary>
    /// Host clicked START in the lobby UI.
    /// </summary>
    private void OnLobbyStartGameRequested()
    {
        if (_networkManager == null || _lobbyManager == null) return;

        if (!_networkManager.IsHost)
        {
            GD.Print("[GameManager] Only the host can start the game.");
            return;
        }

        if (!_lobbyManager.AreAllPlayersReady())
        {
            GD.Print("[GameManager] Not all players are ready.");
            return;
        }

        GD.Print("[GameManager] Host starting the match!");

        // Set bot count to 0 for online play
        Settings.BotCount = 0;

        // Build match start payload with current settings
        MatchStartPayload matchPayload = new MatchStartPayload(
            Settings.BuildTimeSeconds,
            Settings.StartingBudget,
            Settings.ArenaSize,
            Settings.TurnTimeSeconds);

        // Broadcast start to all peers (including self)
        byte[] data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(matchPayload);
        _networkManager.Rpc(nameof(NetworkManager.StartMatch), data);
    }

    /// <summary>
    /// Player clicked LEAVE in the lobby UI.
    /// </summary>
    private void OnLobbyLeaveRequested()
    {
        GD.Print("[GameManager] Leaving lobby.");
        _networkManager?.Shutdown();
        _lobbyManager?.Clear();
        HideLobbyUI();

        // Show main menu again
        Control? mainMenu = GetNodeOrNull<Control>("MainMenu");
        if (mainMenu != null)
        {
            mainMenu.Visible = true;
        }

        SetPhase(GamePhase.Menu);
    }

    /// <summary>
    /// Seeds the player list from lobby members for an online match.
    /// Unlike SeedLocalPlayers (which creates bots), this creates one PlayerData
    /// per connected lobby member.
    /// </summary>
    private void SeedOnlinePlayers()
    {
        if (_lobbyManager == null) return;

        _players.Clear();

        foreach (LobbyMember member in _lobbyManager.Members.Values)
        {
            int slotIndex = (int)member.Slot;
            Color color = slotIndex < GameConfig.PlayerColors.Length
                ? GameConfig.PlayerColors[slotIndex]
                : Colors.White;

            _players[member.Slot] = new PlayerData
            {
                PeerId = member.PeerId,
                Slot = member.Slot,
                DisplayName = member.DisplayName,
                PlayerColor = color,
            };
        }

        GD.Print($"[GameManager] Seeded {_players.Count} online players.");
    }

    /// <summary>
    /// Performs the actual match setup after the loading splash finishes.
    /// </summary>
    private void StartMatchAfterSplash()
    {
        // Re-seed players so HumanPlayerName (set from the menu) is picked up.
        // Skip if players were already seeded for an online match.
        if (_networkManager == null || !_networkManager.IsOnline)
        {
            SeedLocalPlayers();
        }

        foreach (PlayerData player in _players.Values)
        {
            player.ResetForMatch(Settings);
        }

        SetupBuildZones();
        GenerateBuildFoundations();

        // Initialize army manager with world reference and build zones for troop deployment
        if (_armyManager != null && _voxelWorld != null)
        {
            _armyManager.Initialize(_voxelWorld);
            _armyManager.SetBuildZones(_buildZones);
        }

        // Set up VoxelGI, environment lighting, and sun after terrain is in scene
        SetupVoxelGiAndLighting();

        // Start with Player1 building
        _activeBuilderIndex = 0;
        _activeBuilder = BuildOrder[_activeBuilderIndex];

        // Position camera BEFORE SetPhase so that SetBuildZoneBounds sets
        // _hasCustomBounds = true. This ensures FreeFlyCamera.Activate()
        // (triggered by the PhaseChanged event) does NOT override the
        // camera orientation with the default yaw=0 fallback.
        PositionCameraAtBuildZone(_activeBuilder);

        SetPhase(GamePhase.Building, PrototypeBuildPhaseSeconds);
        _camera?.Activate();

        // Re-apply camera position after activation to ensure it takes effect
        PositionCameraAtBuildZone(_activeBuilder);
    }

    // ─────────────────────────────────────────────────
    //  PHASE LIFECYCLE
    // ─────────────────────────────────────────────────

    private void OnPhaseEntered(GamePhase phase)
    {
        // Toggle voxel edge grid: only visible during build mode
        RenderingServer.GlobalShaderParameterSet("edge_grid_enabled", phase == GamePhase.Building ? 1.0f : 0.0f);

        // Show/hide the CombatHUD CanvasLayer based on phase
        // (individual children like CombatUI, ScoreboardUI, BattleLog manage their own
        //  visibility, but the CanvasLayer must be visible for them to render)
        CanvasLayer? combatHUD = GetNodeOrNull<CanvasLayer>("CombatHUD");
        if (combatHUD != null)
        {
            combatHUD.Visible = phase == GamePhase.Combat || phase == GamePhase.Building;
        }

        switch (phase)
        {
            case GamePhase.Menu:
                _ghostPreview?.Hide();
                if (_readyButton != null) _readyButton.Visible = false;
                if (_skipTurnButton != null) _skipTurnButton.Visible = false;
                _combatCamera?.Deactivate();
                // Suppress camera shake during menu — explosions in the background battle cause jitter
                if (CameraShake.Instance != null) CameraShake.Instance.Enabled = false;
                break;

            case GamePhase.Lobby:
                _ghostPreview?.Hide();
                if (_readyButton != null) _readyButton.Visible = false;
                if (_skipTurnButton != null) _skipTurnButton.Visible = false;
                _combatCamera?.Deactivate();
                if (CameraShake.Instance != null) CameraShake.Instance.Enabled = false;
                break;

            case GamePhase.Building:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                _isDragBuilding = false;
                _buildRotation = 0;
                // Ready button is now integrated into BuildUI panel — keep floating button hidden
                if (_readyButton != null) _readyButton.Visible = false;
                if (_skipTurnButton != null) _skipTurnButton.Visible = false;
                _combatCamera?.Deactivate();
                if (CameraShake.Instance != null) CameraShake.Instance.Enabled = true;
                // Emit initial budget so BuildUI displays the correct starting value
                foreach ((PlayerSlot slot, PlayerData playerData) in _players)
                {
                    EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(slot, playerData.Budget, 0));
                }
                break;

            case GamePhase.FogReveal:
                _ghostPreview?.Hide();
                if (_readyButton != null) _readyButton.Visible = false;
                _combatCamera?.Deactivate();
                break;

            case GamePhase.Combat:
                BuildPrototypeFortresses();
                DeployAllTroops();
                _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                _selectedWeaponIndex = -1; // No weapon selected until player clicks one
                _isAiming = false;
                _hasTarget = false;
                _aimingSystem?.ClearTarget();
                Input.MouseMode = Input.MouseModeEnum.Visible;
                if (_skipTurnButton != null) _skipTurnButton.Visible = true;
                if (_readyButton != null) _readyButton.Visible = false;

                // Keep FreeFlyCamera active during combat for WASD/mouse free-fly movement.
                // Reset to full arena bounds so the player can fly anywhere.
                // CombatCamera is only used temporarily for cinematic moments
                // (projectile follow, impact cam, kill cam, targeting).
                _camera?.ResetToFullArenaBounds();
                _camera?.Activate();

                // Start the intro flyover — camera orbits the arena before settling
                _combatIntroActive = true;
                _combatIntroTimer = 0f;
                _turnManager?.StopTurnClock(); // Don't tick turns during intro

                // Populate CombatUI with actual placed weapons for the first player
                if (_turnManager?.CurrentPlayer is PlayerSlot firstPlayer)
                {
                    RefreshCombatUIWeapons(firstPlayer);
                }
                break;

            case GamePhase.GameOver:
                _turnManager?.StopTurnClock();
                _ghostPreview?.Hide();
                if (_skipTurnButton != null) _skipTurnButton.Visible = false;
                // Return to FreeFlyCamera for post-game viewing
                SwitchToFreeFlyCamera();
                break;
        }
    }

    private void OnPhaseTimerExpired()
    {
        switch (CurrentPhase)
        {
            case GamePhase.Building:
                // Advance to next player, or proceed to countdown if all done
                if (_activeBuilderIndex < _players.Count - 1)
                {
                    AdvanceToNextBuilder();
                    return;
                }
                StartCombatCountdown();
                break;

            case GamePhase.FogReveal:
                // FogReveal timer no longer auto-transitions; the countdown overlay handles it.
                // If the countdown is not active (e.g. direct SetPhase call), fall through to combat.
                if (!_countdownActive)
                {
                    SetPhase(GamePhase.Combat, 0f);
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────
    //  BUILD PHASE
    // ─────────────────────────────────────────────────

    private void SetupBuildZones()
    {
        // One zone per active player, positioned in each quadrant of the arena
        for (int i = 0; i < BuildOrder.Length; i++)
        {
            PlayerSlot slot = BuildOrder[i];
            if (!_players.ContainsKey(slot)) continue;

            BuildZone zone = new BuildZone(
                GameConfig.FourPlayerZoneOrigins[i],
                GameConfig.FourPlayerBuildZoneSize);
            _buildZones[slot] = zone;

            if (_players.TryGetValue(slot, out PlayerData? player))
            {
                player.AssignedBuildZone = zone;
            }
        }
    }

    private void GenerateBuildFoundations()
    {
        if (_voxelWorld == null)
        {
            return;
        }

        // Clear existing world and regenerate arena ground
        _voxelWorld.ClearWorld(true);

        // Generate mountain range border around the arena perimeter
        TerrainDecorator.GenerateMountainBorder(_voxelWorld);

        // Decorate terrain with zone borders and vegetation
        TerrainDecorator.MarkBuildZoneBorders(_voxelWorld, _buildZones);
        TerrainDecorator.DecorateArena(_voxelWorld, _buildZones);
    }

    private void SetupVoxelGiAndLighting()
    {
        // Remove any previous setup (e.g. match restart)
        // Use Free() instead of QueueFree() so the old node is removed immediately,
        // preventing two VoxelGiSetup instances existing in the same frame.
        if (_voxelGiSetup != null && GodotObject.IsInstanceValid(_voxelGiSetup))
        {
            _voxelGiSetup.Free();
            _voxelGiSetup = null;
        }

        _voxelGiSetup = new VoxelGiSetup();
        _voxelGiSetup.Name = "VoxelGiSetup";
        AddChild(_voxelGiSetup);

        // Force initialization now — _Ready() may not fire within the same
        // deferred callback chain, leaving the sky uninitialized until the
        // next idle frame (which may never come if another CallDeferred runs first).
        _voxelGiSetup.ForceInitialize();

        // Bake GI now that terrain geometry is present
        _voxelGiSetup.BakeGi();
    }

    private void ProcessBuildPhase(double delta)
    {
        if (_voxelWorld == null || _camera == null)
        {
            return;
        }

        // Update ghost preview based on mouse raycast
        UpdateBuildCursor();
    }

    private void UpdateBuildCursor()
    {
        if (_voxelWorld == null || _camera == null || !_buildZones.ContainsKey(_activeBuilder))
        {
            _hasBuildCursor = false;
            _ghostPreview?.Hide();
            return;
        }

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.ProjectRayNormal(mousePos);

        BuildZone zone = _buildZones[_activeBuilder];
        bool isEraser = _buildSystem?.CurrentToolMode == BuildToolMode.Eraser;

        if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I hitNormal))
        {
            Vector3I targetMicrovoxel;
            if (isEraser && _placementMode == PlacementMode.Block)
            {
                // Eraser targets the hit voxel itself (only in block mode)
                targetMicrovoxel = hitPos;
            }
            else
            {
                // Place mode (blocks, weapons, commander): target the empty
                // cell adjacent to the hit face so the item sits on top of /
                // next to the surface the player clicked. Without this,
                // weapon/commander placement on the ground floor would target
                // the solid ground voxel itself, which falls below the build
                // zone and is rejected.
                targetMicrovoxel = hitPos + hitNormal;
            }

            // Convert to build unit
            _buildCursorBuildUnit = MathHelpers.MicrovoxelToBuild(targetMicrovoxel);

            // Validate placement within build zone
            if (_placementMode == PlacementMode.Weapon)
            {
                // Weapon placement: must be inside build zone + structural checks
                _buildCursorValid = zone.ContainsBuildUnit(_buildCursorBuildUnit)
                    && WeaponPlacer.ValidatePlacement(_voxelWorld, _buildCursorBuildUnit, GetTree());
            }
            else
            {
                _buildCursorValid = zone.ContainsBuildUnit(_buildCursorBuildUnit);
            }

            // Also check budget for placement
            if (_buildCursorValid && !isEraser && _players.TryGetValue(_activeBuilder, out PlayerData? player))
            {
                int cost;
                if (_placementMode == PlacementMode.Weapon)
                {
                    cost = GetWeaponCost(_selectedWeaponType);
                }
                else
                {
                    cost = VoxelMaterials.GetDefinition(_buildSystem?.CurrentMaterial ?? VoxelMaterialType.Stone).Cost;
                }
                _buildCursorValid = player.CanSpend(cost);
            }

            _hasBuildCursor = true;

            // Show ghost preview — blueprint shape, multi-block drag shape, or single block
            if (_buildSystem?.CurrentToolMode == BuildToolMode.Blueprint && _buildSystem.ActiveBlueprint != null && _ghostPreview != null)
            {
                // Blueprint mode: show the full blueprint shape at the cursor position
                List<Vector3I> rotatedOffsets = _buildSystem.ActiveBlueprint.GetRotatedOffsets(_buildRotation);
                List<Vector3I> allMicrovoxels = BuildSystem.GenerateBlueprintMicrovoxels(_buildCursorBuildUnit, rotatedOffsets);
                bool allValid = _buildCursorValid && BuildSystem.ValidateBlueprintInZone(zone, _buildCursorBuildUnit, rotatedOffsets);
                _ghostPreview.SetPreview(allMicrovoxels, allValid);
                _buildCursorValid = allValid;
            }
            else if (_isDragBuilding && _buildSystem != null && _ghostPreview != null)
            {
                BuildToolMode currentMode = _buildSystem.CurrentToolMode;
                Vector3I dragEnd = GetDragEnd(currentMode, _buildCursorBuildUnit);
                List<Vector3I> allMicrovoxels = new List<Vector3I>();

                foreach (Vector3I buildUnit in BuildSystem.GenerateBuildUnitCells(currentMode, _dragStartBuildUnit, dragEnd, _buildSystem.HollowBoxMode))
                {
                    foreach (Vector3I micro in BuildSystem.ExpandBuildUnit(buildUnit, currentMode, _dragStartBuildUnit, dragEnd))
                    {
                        allMicrovoxels.Add(micro);
                    }
                    // Add symmetry-mirrored blocks for ghost preview
                    allMicrovoxels.AddRange(_buildSystem.GetSymmetryMirroredMicrovoxels(zone, buildUnit));
                }

                // Validate that all build units are within the zone
                bool allValid = _buildCursorValid;
                if (allValid)
                {
                    foreach (Vector3I buildUnit in BuildSystem.GenerateBuildUnitCells(currentMode, _dragStartBuildUnit, dragEnd, _buildSystem.HollowBoxMode))
                    {
                        if (!zone.ContainsBuildUnit(buildUnit))
                        {
                            allValid = false;
                            break;
                        }
                    }
                }

                _ghostPreview.SetPreview(allMicrovoxels, allValid);
                _buildCursorValid = allValid;
            }
            else if (_placementMode == PlacementMode.Weapon && _ghostPreview != null)
            {
                // Weapon preview: show the actual weapon model mesh at the cursor position
                ArrayMesh previewMesh = GetOrCreateWeaponPreviewMesh(_selectedWeaponType);
                Vector3I microBase = MathHelpers.BuildToMicrovoxel(_buildCursorBuildUnit);
                Vector3 worldPos = MathHelpers.MicrovoxelToWorld(microBase)
                    + new Vector3(GameConfig.BuildUnitMeters * 0.5f, 0f, GameConfig.BuildUnitMeters * 0.5f);
                // Use manual rotation from R key (90° increments)
                float yaw = _buildRotation * Mathf.Pi * 0.5f;
                _ghostPreview.SetModelPreview(previewMesh, worldPos, yaw, _buildCursorValid);
            }
            else if (_placementMode == PlacementMode.Commander && _ghostPreview != null)
            {
                // Commander preview: show the actual commander model mesh
                ArrayMesh previewMesh = GetOrCreateCommanderPreviewMesh();
                Vector3I microBase = MathHelpers.BuildToMicrovoxel(_buildCursorBuildUnit);
                Vector3 worldPos = MathHelpers.MicrovoxelToWorld(microBase)
                    + new Vector3(GameConfig.BuildUnitMeters * 0.5f, 0f, GameConfig.BuildUnitMeters * 0.5f);
                // Use manual rotation from R key (90° increments)
                float yaw = _buildRotation * Mathf.Pi * 0.5f;
                _ghostPreview.SetModelPreview(previewMesh, worldPos, yaw, _buildCursorValid);
            }
            else
            {
                // Show single block + symmetry-mirrored blocks
                if (_buildSystem != null && _ghostPreview != null && _buildSystem.SymmetryMode != BuildSymmetryMode.None)
                {
                    List<Vector3I> allMicrovoxels = new List<Vector3I>();
                    // Original block
                    Vector3I microBase = _buildCursorBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
                    for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
                        for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
                            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                                allMicrovoxels.Add(microBase + new Vector3I(x, y, z));
                    // Mirrored blocks
                    allMicrovoxels.AddRange(_buildSystem.GetSymmetryMirroredMicrovoxels(zone, _buildCursorBuildUnit));
                    _ghostPreview.SetPreview(allMicrovoxels, _buildCursorValid);
                }
                else
                {
                    _ghostPreview?.ShowSingleBlock(_buildCursorBuildUnit, _buildCursorValid, _buildSystem?.CurrentToolMode ?? BuildToolMode.Single);
                }
            }
        }
        else
        {
            _hasBuildCursor = false;
            _ghostPreview?.Hide();
        }
    }

    private void HandleBuildInput(InputEvent @event)
    {
        if (_buildSystem == null || _voxelWorld == null)
        {
            return;
        }

        // Rotate build piece with R
        if (@event.IsActionPressed("rotate_piece"))
        {
            _buildRotation = (_buildRotation + 1) % 4;
            GD.Print($"[Build] Rotation: {_buildRotation * 90}°");
            GetViewport().SetInputAsHandled();
            return;
        }

        // Cancel drag with right-click or ESC while dragging
        if (_isDragBuilding && (@event.IsActionPressed("place_secondary") || @event.IsActionPressed("ui_cancel")))
        {
            _isDragBuilding = false;
            GD.Print("[Build] Drag cancelled.");
            GetViewport().SetInputAsHandled();
            return;
        }

        // Place with left click — block, commander, or weapon depending on mode
        if (@event.IsActionPressed("place_primary") && _hasBuildCursor)
        {
            switch (_placementMode)
            {
                case PlacementMode.Block:
                    HandleBlockPlacement();
                    break;
                case PlacementMode.Commander:
                    TryPlaceCommanderAtCursor();
                    break;
                case PlacementMode.Weapon:
                    TryPlaceWeaponAtCursor();
                    break;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Erase block or cancel placement mode with right click
        if (@event.IsActionPressed("place_secondary"))
        {
            if (_placementMode != PlacementMode.Block)
            {
                _placementMode = PlacementMode.Block;
                GD.Print("[GameManager] Placement mode cancelled.");
                GetViewport().SetInputAsHandled();
            }
            else if (_hasBuildCursor)
            {
                TryEraseBlock();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // Undo with Ctrl+Z
        if (@event.IsActionPressed("undo_build"))
        {
            _buildSystem.UndoLast(_activeBuilder);
        }

        // Redo with Ctrl+Y
        if (@event.IsActionPressed("redo_build"))
        {
            _buildSystem.RedoLast(_activeBuilder);
        }

        // Scroll wheel is reserved for camera zoom only (FreeFlyCamera handles it)
    }

    /// <summary>
    /// Returns true if the current build tool requires a start/end drag to define a shape.
    /// Single and Eraser are immediate (no drag needed).
    /// </summary>
    private static bool IsMultiVoxelTool(BuildToolMode mode)
    {
        return mode switch
        {
            BuildToolMode.Line => true,
            BuildToolMode.Wall => true,
            BuildToolMode.Box => true,
            BuildToolMode.Floor => true,
            BuildToolMode.Ramp => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns true if the tool produces an asymmetric shape that benefits from rotation.
    /// Symmetric drag tools (Floor, Box, Wall) generate axis-aligned rectangles where
    /// rotation around the start point would invert the drag direction and confuse the user.
    /// </summary>
    private static bool SupportsRotation(BuildToolMode mode)
    {
        return mode switch
        {
            BuildToolMode.Line => true,
            BuildToolMode.Ramp => true,
            BuildToolMode.Blueprint => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns the effective end build-unit for a drag operation, applying rotation only
    /// for tools that support it (asymmetric shapes like Ramp and Line).
    /// </summary>
    private Vector3I GetDragEnd(BuildToolMode mode, Vector3I cursorBuildUnit)
    {
        if (SupportsRotation(mode))
        {
            return ApplyBuildRotation(_dragStartBuildUnit, cursorBuildUnit, _buildRotation);
        }

        return cursorBuildUnit;
    }

    /// <summary>
    /// Handles left-click for block placement. For single-voxel tools, places immediately.
    /// For multi-voxel tools, first click sets the drag start, second click executes the build.
    /// For blueprint tools, places the full blueprint immediately at the cursor.
    /// </summary>
    private void HandleBlockPlacement()
    {
        if (_buildSystem == null)
        {
            return;
        }

        BuildToolMode currentMode = _buildSystem.CurrentToolMode;

        // Blueprint mode: immediate full-structure placement
        if (currentMode == BuildToolMode.Blueprint)
        {
            TryPlaceBlueprint();
            return;
        }

        // Door mode: carve a 1x3 door opening on a zone edge wall
        if (currentMode == BuildToolMode.Door)
        {
            TryPlaceDoorAtCursor();
            return;
        }

        if (IsMultiVoxelTool(currentMode))
        {
            if (!_isDragBuilding)
            {
                // First click: record start position and enter drag mode
                _isDragBuilding = true;
                _dragStartBuildUnit = _buildCursorBuildUnit;
                GD.Print($"[Build] Drag start at {_dragStartBuildUnit} ({currentMode})");
            }
            else
            {
                // Second click: execute the build with start→end
                TryPlaceBlock();
                _isDragBuilding = false;
            }
        }
        else
        {
            // Single mode: immediate placement
            TryPlaceBlock();
        }
    }

    /// <summary>
    /// Applies rotation to an end position relative to a start position.
    /// Rotation 0 = identity, 1 = 90° CW, 2 = 180°, 3 = 270° CW (when viewed from above).
    /// </summary>
    private static Vector3I ApplyBuildRotation(Vector3I start, Vector3I end, int rotation)
    {
        if (rotation == 0)
        {
            return end;
        }

        // Calculate offset from start
        int dx = end.X - start.X;
        int dz = end.Z - start.Z;

        // Rotate the offset around the start position (Y axis)
        int rotatedDx;
        int rotatedDz;
        switch (rotation)
        {
            case 1: // 90° CW
                rotatedDx = -dz;
                rotatedDz = dx;
                break;
            case 2: // 180°
                rotatedDx = -dx;
                rotatedDz = -dz;
                break;
            case 3: // 270° CW
                rotatedDx = dz;
                rotatedDz = -dx;
                break;
            default:
                rotatedDx = dx;
                rotatedDz = dz;
                break;
        }

        return new Vector3I(start.X + rotatedDx, end.Y, start.Z + rotatedDz);
    }

    private void TryPlaceBlock()
    {
        if (_buildSystem == null || !_hasBuildCursor || !_buildCursorValid)
        {
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        // Determine start and end positions based on drag state
        Vector3I startBuildUnit;
        Vector3I endBuildUnit;

        if (_isDragBuilding)
        {
            // Multi-voxel tool: use drag start as start, current cursor as end.
            // Rotation is only applied for tools with asymmetric shapes (Ramp, Line).
            startBuildUnit = _dragStartBuildUnit;
            endBuildUnit = GetDragEnd(_buildSystem.CurrentToolMode, _buildCursorBuildUnit);
        }
        else
        {
            // Single mode: same position for start and end
            startBuildUnit = _buildCursorBuildUnit;
            endBuildUnit = _buildCursorBuildUnit;
        }

        // Use the actual current tool mode (no longer forcing Single)
        bool success = _buildSystem.TryApply(_activeBuilder, zone, startBuildUnit, endBuildUnit, out string failureReason);
        if (!success)
        {
            GD.Print($"[Build] Failed: {failureReason}");
        }
        else
        {
            if (_players.TryGetValue(_activeBuilder, out PlayerData? player))
            {
                player.Stats.VoxelsPlaced++;
            }
        }
    }

    /// <summary>
    /// Places the active blueprint at the build cursor using the player's selected material.
    /// Rotation is applied via the current _buildRotation.
    /// </summary>
    private void TryPlaceBlueprint()
    {
        if (_buildSystem == null || !_hasBuildCursor || !_buildCursorValid)
        {
            return;
        }

        if (_buildSystem.ActiveBlueprint == null)
        {
            GD.Print("[Build] No blueprint selected.");
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        List<Vector3I> rotatedOffsets = _buildSystem.ActiveBlueprint.GetRotatedOffsets(_buildRotation);
        bool success = _buildSystem.TryApplyBlueprint(_activeBuilder, zone, _buildCursorBuildUnit, rotatedOffsets, out string failureReason);
        if (!success)
        {
            GD.Print($"[Build] Blueprint failed: {failureReason}");
        }
        else
        {
            if (_players.TryGetValue(_activeBuilder, out PlayerData? player))
            {
                player.Stats.VoxelsPlaced += _buildSystem.ActiveBlueprint.BlockCount;
            }
            GD.Print($"[Build] Blueprint '{_buildSystem.ActiveBlueprint.Name}' placed at {_buildCursorBuildUnit}.");
        }
    }

    private void TryEraseBlock()
    {
        if (_buildSystem == null || !_hasBuildCursor)
        {
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        // For erase, we need to raycast to hit an existing voxel
        if (_camera == null || _voxelWorld == null)
        {
            return;
        }

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.ProjectRayNormal(mousePos);

        if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
        {
            Vector3I eraseBuildUnit = MathHelpers.MicrovoxelToBuild(hitPos);
            if (!zone.ContainsBuildUnit(eraseBuildUnit))
            {
                return;
            }

            // Check that the voxel isn't foundation
            Voxel.Voxel voxel = _voxelWorld.GetVoxel(hitPos);
            if (voxel.Material == VoxelMaterialType.Foundation)
            {
                return;
            }

            BuildToolMode previousMode = _buildSystem.CurrentToolMode;
            _buildSystem.CurrentToolMode = BuildToolMode.Eraser;

            bool success = _buildSystem.TryApply(_activeBuilder, zone, eraseBuildUnit, eraseBuildUnit, out string failureReason);
            if (!success)
            {
                GD.Print($"[Erase] Failed: {failureReason}");
            }

            _buildSystem.CurrentToolMode = previousMode;
        }
    }

    private void TryPlaceCommanderAtCursor()
    {
        if (_voxelWorld == null || !_hasBuildCursor || !_buildCursorValid)
        {
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        if (!zone.ContainsBuildUnit(_buildCursorBuildUnit))
        {
            GD.Print("[Build] Commander must be placed inside your build zone.");
            return;
        }

        // Remove existing commander for this player if already placed
        if (_commanders.TryGetValue(_activeBuilder, out CommanderActor? existingCmd) && GodotObject.IsInstanceValid(existingCmd))
        {
            existingCmd.QueueFree();
        }

        CommanderActor commander = new CommanderActor();
        commander.Name = $"Commander_{_activeBuilder}";
        AddChild(commander);
        commander.OwnerSlot = _activeBuilder;
        commander.PlaceCommander(_voxelWorld, _buildCursorBuildUnit);

        // Apply manual rotation from R key
        float cmdYaw = _buildRotation * Mathf.Pi * 0.5f;
        commander.Rotation = new Vector3(0f, cmdYaw, 0f);

        _commanders[_activeBuilder] = commander;

        if (_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            player.CommanderMicrovoxelPosition = _buildCursorBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
            player.CommanderHealth = GameConfig.CommanderHP;
        }

        AudioDirector.Instance?.PlaySFX("ui_confirm");
        GD.Print($"[Build] Commander placed for {_activeBuilder} at {_buildCursorBuildUnit}.");
        // Stay in Commander mode so clicking again moves the commander
    }

    private void TryPlaceWeaponAtCursor()
    {
        if (_voxelWorld == null || _weaponPlacer == null || !_hasBuildCursor || !_buildCursorValid)
        {
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        // Weapons must be placed inside the player's build zone.
        if (!zone.ContainsBuildUnit(_buildCursorBuildUnit))
        {
            GD.Print("[Build] Weapon must be placed inside your build zone.");
            ShowBuildWarning("Must place weapon inside your build zone!");
            return;
        }

        if (!WeaponPlacer.ValidatePlacement(_voxelWorld, _buildCursorBuildUnit, GetTree()))
        {
            GD.Print("[Build] Invalid weapon placement: needs structural support and an exposed face.");
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        // Get cost of the selected weapon type
        int weaponCost = GetWeaponCost(_selectedWeaponType);
        if (!player.CanSpend(weaponCost))
        {
            GD.Print($"[Build] Not enough budget for weapon (need ${weaponCost}, have ${player.Budget}).");
            ShowBuildWarning($"Not enough budget! Need ${weaponCost}");
            return;
        }

        // Initialize weapon list for this player if needed
        if (!_weapons.ContainsKey(_activeBuilder))
        {
            _weapons[_activeBuilder] = new List<WeaponBase>();
        }

        // Place the selected weapon type
        WeaponBase weapon = PlaceSelectedWeaponType(_buildCursorBuildUnit);

        // Apply manual rotation from R key (overrides auto-outward direction)
        float weaponYaw = _buildRotation * Mathf.Pi * 0.5f;
        weapon.Rotation = new Vector3(0f, weaponYaw, 0f);

        _weapons[_activeBuilder].Add(weapon);
        player.WeaponIds.Add(weapon.WeaponId);

        // Deduct the cost
        player.TrySpend(weaponCost);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, -weaponCost));

        AudioDirector.Instance?.PlaySFX("ui_confirm");
        GD.Print($"[Build] {_selectedWeaponType} placed for {_activeBuilder} at {_buildCursorBuildUnit} (cost: ${weaponCost}).");
        // Stay in weapon placement mode so the user can place multiple weapons in a row.
        // Right-click or selecting a build tool returns to block mode.
    }

    /// <summary>
    /// Places a door at the build cursor, carving a 1x3 opening through the zone edge wall.
    /// Doors allow troops to exit the base. Placed via the Door build tool.
    /// </summary>
    private void TryPlaceDoorAtCursor()
    {
        if (_voxelWorld == null || !_hasBuildCursor || _armyManager == null)
        {
            return;
        }

        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone))
        {
            return;
        }

        // Convert build cursor to microvoxel at the ground level of the build unit
        Vector3I baseMicro = MathHelpers.BuildToMicrovoxel(_buildCursorBuildUnit);

        // Door needs to be placed at ground level — find the lowest solid block in this column
        // within the zone so the door starts at floor level
        Vector3I zoneMin = zone.OriginMicrovoxels;
        Vector3I zoneMax = zone.MaxMicrovoxelsInclusive;

        // Start from the zone floor and look for the first solid voxel at this XZ
        int doorY = baseMicro.Y;
        for (int y = zoneMin.Y; y <= zoneMax.Y - DoorRegistry.DoorHeight + 1; y++)
        {
            Vector3I check = new Vector3I(baseMicro.X, y, baseMicro.Z);
            if (_voxelWorld.GetVoxel(check).IsSolid)
            {
                doorY = y;
                break;
            }
        }

        Vector3I doorBase = new Vector3I(baseMicro.X, doorY, baseMicro.Z);

        bool success = _armyManager.Doors.TryPlaceDoor(
            _voxelWorld, doorBase, _activeBuilder, zoneMin, zoneMax, out string failReason);

        if (success)
        {
            GD.Print($"[Build] Door placed for {_activeBuilder} at {doorBase}.");
        }
        else
        {
            GD.Print($"[Build] Door failed: {failReason}");
            ShowBuildWarning(failReason);
        }
    }

    /// <summary>
    /// Returns the display name of a weapon type.
    /// </summary>
    public static string GetWeaponDisplayName(WeaponType type)
    {
        return type switch
        {
            WeaponType.Cannon => "Cannon",
            WeaponType.Mortar => "Mortar",
            WeaponType.Railgun => "Railgun",
            WeaponType.MissileLauncher => "Missile",
            WeaponType.Drill => "Drill",
            _ => "Cannon",
        };
    }

    /// <summary>
    /// Returns the cost of a given weapon type.
    /// </summary>
    public static int GetWeaponCost(WeaponType type)
    {
        return type switch
        {
            WeaponType.Cannon => 500,
            WeaponType.Mortar => 600,
            WeaponType.Railgun => 800,
            WeaponType.MissileLauncher => 850,
            WeaponType.Drill => 550,
            _ => 500,
        };
    }

    private static string GetWeaponId(WeaponType type)
    {
        return type switch
        {
            WeaponType.Cannon => "cannon",
            WeaponType.Mortar => "mortar",
            WeaponType.Railgun => "railgun",
            WeaponType.MissileLauncher => "missile",
            WeaponType.Drill => "drill",
            _ => "cannon",
        };
    }

    private ArrayMesh GetOrCreateWeaponPreviewMesh(WeaponType type)
    {
        string id = GetWeaponId(type);
        if (_weaponPreviewMeshes.TryGetValue(id, out ArrayMesh? cached))
            return cached;

        Color teamColor = _players.TryGetValue(_activeBuilder, out PlayerData? p)
            ? p.PlayerColor : new Color(0.2f, 0.6f, 1.0f);
        Art.WeaponModelResult result = Art.WeaponModelGenerator.Generate(id, teamColor);
        _weaponPreviewMeshes[id] = result.Mesh;
        return result.Mesh;
    }

    private ArrayMesh GetOrCreateCommanderPreviewMesh()
    {
        if (_commanderPreviewMesh != null) return _commanderPreviewMesh;

        Color teamColor = _players.TryGetValue(_activeBuilder, out PlayerData? p)
            ? p.PlayerColor : new Color(0.2f, 0.6f, 1.0f);
        Art.CommanderBodyParts parts = Art.CommanderModelGenerator.Generate(teamColor);
        _commanderPreviewMesh = parts.FullMesh;
        return _commanderPreviewMesh;
    }

    /// <summary>
    /// Places a weapon of the currently selected type at the given position.
    /// </summary>
    private WeaponBase PlaceSelectedWeaponType(Vector3I buildUnitPosition)
    {
        return _selectedWeaponType switch
        {
            WeaponType.Cannon => _weaponPlacer!.PlaceWeapon<Cannon>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
            WeaponType.Mortar => _weaponPlacer!.PlaceWeapon<Mortar>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
            WeaponType.Railgun => _weaponPlacer!.PlaceWeapon<Railgun>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
            WeaponType.MissileLauncher => _weaponPlacer!.PlaceWeapon<MissileLauncher>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
            WeaponType.Drill => _weaponPlacer!.PlaceWeapon<Drill>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
            _ => _weaponPlacer!.PlaceWeapon<Cannon>(this, _voxelWorld!, buildUnitPosition, _activeBuilder),
        };
    }

    private void CycleBuildMaterial(int direction)
    {
        if (_buildSystem == null)
        {
            return;
        }

        // Cycle through placeable materials (skip Air and Foundation)
        VoxelMaterialType[] materials = {
            VoxelMaterialType.Dirt, VoxelMaterialType.Wood, VoxelMaterialType.Stone,
            VoxelMaterialType.Brick, VoxelMaterialType.Concrete, VoxelMaterialType.Metal,
            VoxelMaterialType.ReinforcedSteel, VoxelMaterialType.Glass, VoxelMaterialType.Obsidian,
            VoxelMaterialType.Sand, VoxelMaterialType.Ice, VoxelMaterialType.ArmorPlate,
            VoxelMaterialType.Leaves, VoxelMaterialType.Bark,
        };

        int currentIndex = 0;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == _buildSystem.CurrentMaterial)
            {
                currentIndex = i;
                break;
            }
        }

        currentIndex = ((currentIndex + direction) % materials.Length + materials.Length) % materials.Length;
        _buildSystem.CurrentMaterial = materials[currentIndex];

        // Sync the BuildUI's visual selection so the material panel highlights
        // the correct material after scroll-wheel cycling.
        BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
            ?? GetNodeOrNull<BuildUI>("%BuildUI")
            ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
        buildUI?.SetSelectedMaterialVisual(materials[currentIndex]);
    }

    private void AdvanceToNextBuilder()
    {
        _activeBuilderIndex++;
        _activeBuilder = BuildOrder[_activeBuilderIndex];

        // Reset placement mode for the new builder
        _placementMode = PlacementMode.Block;

        // Reset countdown for the next builder
        _phaseCountdownSeconds = PrototypeBuildPhaseSeconds;

        // Smoothly transition camera to the new builder's zone with updated bounds
        PositionCameraAtBuildZone(_activeBuilder, animate: true);

        // Bots build their fortress, then auto-ready
        if (IsBot(_activeBuilder))
        {
            RunBotBuildPhase(_activeBuilder);
            GetTree().CreateTimer(0.1).Timeout += OnReadyPressed;
        }
    }

    private void OnReadyPressed()
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        // In sandbox mode, "Ready" returns to the main menu
        if (_isSandbox)
        {
            ExitSandboxMode();
            return;
        }

        // Human players must place a commander and at least 1 weapon before readying up
        if (!IsBot(_activeBuilder))
        {
            bool hasCommander = _commanders.TryGetValue(_activeBuilder, out var cmd)
                                && cmd != null
                                && IsInstanceValid(cmd);
            bool hasWeapons = _weapons.TryGetValue(_activeBuilder, out var wpns)
                              && wpns != null
                              && wpns.Exists(w => GodotObject.IsInstanceValid(w));
            if (!hasCommander && !hasWeapons)
            {
                ShowBuildWarning($"{_activeBuilder} must place a Commander and at least 1 weapon!");
                return;
            }
            if (!hasCommander)
            {
                ShowBuildWarning($"{_activeBuilder} must place a Commander before readying up!");
                return;
            }
            if (!hasWeapons)
            {
                ShowBuildWarning($"{_activeBuilder} must place at least 1 weapon before readying up!");
                return;
            }
        }

        // Show naming popup for human players before advancing.
        // If already awaiting a name (popup is open), ignore the second click.
        if (!IsBot(_activeBuilder))
        {
            if (!_awaitingName)
            {
                ShowCommanderNamePopup();
            }
            return; // Always return — FinalizeBuildReady is called by the popup's confirm callback
        }

        FinalizeBuildReady();
    }

    private void FinalizeBuildReady()
    {
        if (_activeBuilderIndex < _players.Count - 1)
        {
            // Check if all remaining players are bots — if so, skip camera
            // animations and go straight to the countdown overlay. Bot builds
            // will run behind the overlay in PerformCombatSetupBehindOverlay.
            bool allRemainingAreBots = true;
            for (int i = _activeBuilderIndex + 1; i < BuildOrder.Length; i++)
            {
                if (_players.ContainsKey(BuildOrder[i]) && !IsBot(BuildOrder[i]))
                {
                    allRemainingAreBots = false;
                    break;
                }
            }

            if (allRemainingAreBots)
            {
                // Run all remaining bot build phases now (no camera movement)
                for (int i = _activeBuilderIndex + 1; i < BuildOrder.Length; i++)
                {
                    PlayerSlot botSlot = BuildOrder[i];
                    if (_players.ContainsKey(botSlot) && IsBot(botSlot))
                    {
                        RunBotBuildPhase(botSlot);
                    }
                }
                _activeBuilderIndex = _players.Count - 1;
                StartCombatCountdown();
            }
            else
            {
                // Next player is human — advance normally
                AdvanceToNextBuilder();
            }
        }
        else
        {
            // Last player clicked ready — show countdown overlay and prepare combat
            StartCombatCountdown();
        }
    }

    private void ShowCommanderNamePopup()
    {
        _awaitingName = true;
        if (_readyButton != null) _readyButton.Visible = false;

        // Get default name for this player — prefer the display name already set
        // (e.g. from the main menu name input) over the fallback color name.
        string[] defaultNames = { "Green", "Red", "Blue", "Grey" };
        int idx = Array.IndexOf(BuildOrder, _activeBuilder);
        string defaultName = idx >= 0 && idx < defaultNames.Length ? defaultNames[idx] : "Commander";
        if (_players.TryGetValue(_activeBuilder, out PlayerData? currentPd)
            && !string.IsNullOrWhiteSpace(currentPd.DisplayName))
        {
            defaultName = currentPd.DisplayName;
        }

        // Create popup panel
        _namePopup = new PanelContainer();
        _namePopup.SetAnchorsPreset(Control.LayoutPreset.Center);
        _namePopup.CustomMinimumSize = new Vector2(400, 0);
        _namePopup.OffsetLeft = -200;
        _namePopup.OffsetRight = 200;
        _namePopup.OffsetTop = -80;
        _namePopup.OffsetBottom = 80;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        style.BorderColor = GameConfig.PlayerColors[idx >= 0 ? idx : 0];
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(20);
        _namePopup.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        _namePopup.AddChild(vbox);

        var titleLabel = new Label();
        titleLabel.Text = "NAME YOUR COMMANDER";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(titleLabel);

        _nameInput = new LineEdit();
        _nameInput.Text = defaultName;
        _nameInput.PlaceholderText = defaultName;
        _nameInput.MaxLength = 16;
        _nameInput.Alignment = HorizontalAlignment.Center;
        _nameInput.AddThemeFontSizeOverride("font_size", 14);
        _nameInput.CaretBlink = true;
        vbox.AddChild(_nameInput);

        var confirmBtn = new Button();
        confirmBtn.Text = "CONFIRM";
        confirmBtn.AddThemeFontSizeOverride("font_size", 12);
        confirmBtn.Pressed += OnNameConfirmed;
        vbox.AddChild(confirmBtn);

        // Apply pixel font if available
        var pixelFont = ResourceLoader.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");
        if (pixelFont != null)
        {
            titleLabel.AddThemeFontOverride("font", pixelFont);
            _nameInput.AddThemeFontOverride("font", pixelFont);
            confirmBtn.AddThemeFontOverride("font", pixelFont);
        }

        GetTree().Root.AddChild(_namePopup);
        _nameInput.GrabFocus();
        _nameInput.SelectAll();

        // Allow Enter key to confirm
        _nameInput.TextSubmitted += _ => OnNameConfirmed();
    }

    private void OnNameConfirmed()
    {
        if (!_awaitingName) return;
        _awaitingName = false;

        string enteredName = _nameInput?.Text?.Trim() ?? "";
        string[] defaultNames = { "Green", "Red", "Blue", "Grey" };
        int idx = Array.IndexOf(BuildOrder, _activeBuilder);

        if (string.IsNullOrWhiteSpace(enteredName))
        {
            enteredName = idx >= 0 && idx < defaultNames.Length ? defaultNames[idx] : "Commander";
        }

        // Apply the name to the player data
        if (_players.TryGetValue(_activeBuilder, out PlayerData? pd))
        {
            pd.DisplayName = enteredName;
        }

        // Also set HumanPlayerName for Player1
        if (_activeBuilder == PlayerSlot.Player1)
        {
            HumanPlayerName = enteredName;
        }

        // Clean up popup
        if (_namePopup != null)
        {
            _namePopup.QueueFree();
            _namePopup = null;
            _nameInput = null;
        }

        // Ready button is now in BuildUI — no need to show floating button
        FinalizeBuildReady();
    }

    private void ShowBuildWarning(string message)
    {
        if (_buildWarningLabel == null) return;
        _buildWarningLabel.Text = message;
        _buildWarningLabel.Visible = true;
        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            if (_buildWarningLabel != null && IsInstanceValid(_buildWarningLabel))
                _buildWarningLabel.Visible = false;
        };
    }

    // ─────────────────────────────────────────────────
    //  FOG REVEAL PHASE
    // ─────────────────────────────────────────────────

    private void ProcessFogRevealPhase(double delta)
    {
        // Dramatic pause while fog clears - just count down
        // Camera could pan between the two fortresses here
    }

    // ─────────────────────────────────────────────────
    //  COMBAT COUNTDOWN OVERLAY
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Shows a fullscreen black overlay with "PREPARING BATTLE..." text,
    /// performs combat setup behind it, then runs a 3-2-1-FIGHT countdown
    /// before fading out to reveal the arena.
    /// </summary>
    private void StartCombatCountdown()
    {
        if (_countdownActive) return;

        _countdownActive = true;
        _countdownStep = 0; // preparing
        _countdownTimer = 0f;
        _combatSetupDone = false;

        // Freeze the camera FIRST — prevent any visible camera movement before
        // the overlay renders. Disable processing so the camera holds its current frame.
        _camera?.SetProcess(false);
        _camera?.SetProcessUnhandledInput(false);
        _combatCamera?.SetProcess(false);

        // Create the overlay CanvasLayer at layer 100 (above everything)
        _countdownLayer = new CanvasLayer();
        _countdownLayer.Name = "CombatCountdownLayer";
        _countdownLayer.Layer = 100;
        AddChild(_countdownLayer);

        // Black background covering the full viewport
        _countdownBg = new ColorRect();
        _countdownBg.Name = "CountdownBg";
        _countdownBg.Color = new Color(0f, 0f, 0f, 1f);
        _countdownBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _countdownLayer.AddChild(_countdownBg);
        // Force offsets to zero after anchors are set
        _countdownBg.OffsetLeft = 0;
        _countdownBg.OffsetRight = 0;
        _countdownBg.OffsetTop = 0;
        _countdownBg.OffsetBottom = 0;

        // Label for countdown text
        _countdownLabel = new Label();
        _countdownLabel.Name = "CountdownLabel";
        _countdownLabel.Text = "PREPARING BATTLE...";
        _countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _countdownLabel.VerticalAlignment = VerticalAlignment.Center;
        _countdownLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _countdownLabel.OffsetLeft = 0;
        _countdownLabel.OffsetRight = 0;
        _countdownLabel.OffsetTop = 0;
        _countdownLabel.OffsetBottom = 0;
        _countdownLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Use the game's pixel font
        Font? pixelFont = GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");
        if (pixelFont != null)
        {
            _countdownLabel.AddThemeFontOverride("font", pixelFont);
        }
        _countdownLabel.AddThemeFontSizeOverride("font_size", 28);
        _countdownLabel.AddThemeColorOverride("font_color", new Color("e8e4df"));

        _countdownLayer.AddChild(_countdownLabel);

        // Hide the HUD during the countdown
        if (_hudRoot != null) _hudRoot.Visible = false;

        // Defer phase change so the overlay renders first — no camera movement
        // is visible because cameras are frozen above.
        CallDeferred(nameof(DeferredCombatSetupStart));
    }

    /// <summary>
    /// Called one frame after the countdown overlay was created so it has rendered.
    /// Now safe to change phase and set up cameras behind the overlay.
    /// </summary>
    private void DeferredCombatSetupStart()
    {
        // Transition to FogReveal phase (hides build UI, ghost preview, etc.)
        SetPhase(GamePhase.FogReveal, 0f);

        // Perform combat setup behind the overlay
        PerformCombatSetupBehindOverlay();
    }

    /// <summary>
    /// Runs the heavy combat setup work (fortress finalization, troop deployment, etc.)
    /// while the countdown overlay is covering the screen.
    /// </summary>
    private void PerformCombatSetupBehindOverlay()
    {
        // Do all the work that OnPhaseEntered(Combat) normally does,
        // but without actually entering combat phase yet.
        BuildPrototypeFortresses();
        DeployAllTroops();
        _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
        _turnManager?.StopTurnClock(); // Don't tick the turn clock during countdown
        _selectedWeaponIndex = -1;
        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();

        // Set up camera so it's ready when the overlay drops
        _camera?.ResetToFullArenaBounds();
        _camera?.Activate();
        PositionFreeFlyBehindZone(_turnManager?.CurrentPlayer ?? PlayerSlot.Player1);

        _combatSetupDone = true;
        GD.Print("[GameManager] Combat setup complete behind overlay, starting countdown.");
    }

    /// <summary>
    /// Drives the countdown overlay state machine each frame.
    /// Steps: 0=preparing, 1=show "3", 2=show "2", 3=show "1", 4=show "FIGHT!", 5=fade out, 6=done.
    /// </summary>
    private void ProcessCombatCountdown(float delta)
    {
        _countdownTimer += delta;

        switch (_countdownStep)
        {
            case 0: // PREPARING BATTLE...
                // Wait until combat setup is done, with a minimum display time of 0.5s
                if (_combatSetupDone && _countdownTimer >= 0.5f)
                {
                    _countdownStep = 1;
                    _countdownTimer = 0f;
                    UpdateCountdownDisplay("3", 64);
                    AudioDirector.Instance?.PlaySFX("countdown_tick");
                }
                break;

            case 1: // "3"
                if (_countdownTimer >= 1.0f)
                {
                    _countdownStep = 2;
                    _countdownTimer = 0f;
                    UpdateCountdownDisplay("2", 64);
                    AudioDirector.Instance?.PlaySFX("countdown_tick");
                }
                break;

            case 2: // "2"
                if (_countdownTimer >= 1.0f)
                {
                    _countdownStep = 3;
                    _countdownTimer = 0f;
                    UpdateCountdownDisplay("1", 64);
                    AudioDirector.Instance?.PlaySFX("countdown_tick");
                }
                break;

            case 3: // "1"
                if (_countdownTimer >= 1.0f)
                {
                    _countdownStep = 4;
                    _countdownTimer = 0f;
                    UpdateCountdownDisplay("FIGHT!", 48);
                    AudioDirector.Instance?.PlaySFX("countdown_fight");
                }
                break;

            case 4: // "FIGHT!"
                if (_countdownTimer >= 0.8f)
                {
                    _countdownStep = 5;
                    _countdownTimer = 0f;
                }
                break;

            case 5: // Fade out
            {
                float fadeDuration = 0.5f;
                float alpha = Mathf.Clamp(1f - _countdownTimer / fadeDuration, 0f, 1f);
                if (_countdownBg != null) _countdownBg.Modulate = new Color(1f, 1f, 1f, alpha);
                if (_countdownLabel != null) _countdownLabel.Modulate = new Color(1f, 1f, 1f, alpha);

                if (_countdownTimer >= fadeDuration)
                {
                    _countdownStep = 6;
                    FinishCombatCountdown();
                }
                break;
            }
        }

        // Pulse/scale effect on the countdown numbers (steps 1-4)
        if (_countdownStep >= 1 && _countdownStep <= 4 && _countdownLabel != null)
        {
            // Quick scale-in at the start of each number
            float scaleProgress = Mathf.Clamp(_countdownTimer / 0.15f, 0f, 1f);
            float scale = Mathf.Lerp(1.5f, 1.0f, scaleProgress);
            _countdownLabel.PivotOffset = _countdownLabel.Size / 2f;
            _countdownLabel.Scale = new Vector2(scale, scale);
        }
    }

    private void UpdateCountdownDisplay(string text, int fontSize)
    {
        if (_countdownLabel == null) return;
        _countdownLabel.Text = text;
        _countdownLabel.AddThemeFontSizeOverride("font_size", fontSize);
        // Reset scale for the new pulse
        _countdownLabel.Scale = new Vector2(1.5f, 1.5f);
    }

    /// <summary>
    /// Cleans up the countdown overlay and enters combat phase proper.
    /// </summary>
    private void FinishCombatCountdown()
    {
        _countdownActive = false;

        // Remove the overlay
        if (_countdownLayer != null)
        {
            _countdownLayer.QueueFree();
            _countdownLayer = null;
        }
        _countdownBg = null;
        _countdownLabel = null;

        // Show the HUD again
        if (_hudRoot != null) _hudRoot.Visible = true;

        // Now enter combat phase — but skip the heavy setup work since we already did it.
        // We directly set the phase and handle the remaining UI setup.
        CurrentPhase = GamePhase.Combat;
        _phaseCountdownSeconds = 0f;

        // Toggle voxel edge grid off
        RenderingServer.GlobalShaderParameterSet("edge_grid_enabled", 0.0f);

        // Show combat HUD
        CanvasLayer? combatHUD = GetNodeOrNull<CanvasLayer>("CombatHUD");
        if (combatHUD != null) combatHUD.Visible = true;

        // UI state
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_skipTurnButton != null) _skipTurnButton.Visible = true;
        if (_readyButton != null) _readyButton.Visible = false;

        // Populate CombatUI with actual placed weapons for the first player
        if (_turnManager?.CurrentPlayer is PlayerSlot firstPlayer)
        {
            RefreshCombatUIWeapons(firstPlayer);
        }

        // Start the turn clock now that the countdown is done
        _turnManager?.StartTurnClock(Settings.TurnTimeSeconds);

        // Emit phase changed event
        EventBus.Instance?.EmitPhaseChanged(new PhaseChangedEvent(GamePhase.FogReveal, GamePhase.Combat));
        _steamPlatform?.Platform.SetRichPresence("status", GamePhase.Combat.ToString());

        GD.Print("[GameManager] Combat countdown complete, combat phase started.");
    }

    // ─────────────────────────────────────────────────
    //  COMBAT PHASE
    // ─────────────────────────────────────────────────

    private void ProcessCombatPhase(double delta)
    {
        // Combat intro flyover — orbit the arena before handing control to the player
        if (_combatIntroActive)
        {
            ProcessCombatIntro((float)delta);
            return;
        }

        // When Artillery Dominance is active, run the automated bombardment
        // instead of normal turn-based combat
        if (_artilleryDominanceActive)
        {
            ProcessBombardment(delta);
            return;
        }

        // Update voxel hover highlight during targeting mode
        UpdateTargetingHighlight();
    }

    private void ProcessCombatIntro(float dt)
    {
        _combatIntroTimer += dt;

        if (_camera == null)
        {
            _combatIntroActive = false;
            return;
        }

        PlayerSlot firstPlayer = _turnManager?.CurrentPlayer ?? PlayerSlot.Player1;
        Vector3 fortCenter = ComputePlayerFortressCenter(firstPlayer) + new Vector3(0f, 4f, 0f);
        Vector3 arenaCenter = ComputeArenaMidpoint();

        // Compute the final behind-zone position (same as PositionFreeFlyBehindZone)
        Vector3 awayDir = new Vector3(fortCenter.X - arenaCenter.X, 0f, fortCenter.Z - arenaCenter.Z);
        if (awayDir.LengthSquared() < 0.01f) awayDir = new Vector3(0f, 0f, 1f);
        awayDir = awayDir.Normalized();
        Vector3 behindPos = fortCenter + new Vector3(0f, 30f, 0f) + awayDir * 38f;

        // Start position: directly above the fortress looking down
        Vector3 topDownPos = fortCenter + new Vector3(0f, 50f, 0f);

        float progress = Mathf.Clamp(_combatIntroTimer / CombatIntroDuration, 0f, 1f);
        float t = progress * progress * (3f - 2f * progress); // smoothstep easing

        // Smoothly zoom from top-down to behind-zone position
        Vector3 currentPos = topDownPos.Lerp(behindPos, t);
        Vector3 lookTarget = fortCenter; // always look at the fortress
        _camera.SetLookTarget(currentPos, lookTarget);

        if (progress >= 1f)
        {
            // Intro complete — hand control to the player
            _combatIntroActive = false;
            PositionFreeFlyBehindZone(firstPlayer);
            _turnManager?.StartTurnClock(Settings.TurnTimeSeconds);
        }
    }

    private void HandleCombatInput(InputEvent @event)
    {
        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        // Space bar / fire action: fire if target is set
        if (@event.IsActionPressed("fire_weapon"))
        {
            if (_hasTarget && _aimingSystem != null && _aimingSystem.HasTarget)
            {
                FireCurrentPlayerWeapon();
            }
        }

        // Cancel aiming with escape: return to top-down (also resets confirmation)
        // (right-click and ESC in targeting mode are handled by CombatCamera events)
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_isAiming || (_combatCamera != null && _combatCamera.IsInTargeting))
            {
                CancelTargeting();
            }
        }

        // Spectator view toggle (V key): toggle top-down spectator preference.
        // The preference is sticky — it persists across all turns until the player
        // presses V again. During bot turns the camera switches immediately;
        // during the human's own turn, only the preference is toggled (the camera
        // stays in aiming position, but top-down will activate after they fire).
        if (@event is InputEventKey viewKey && viewKey.Pressed && viewKey.Keycode == Key.V)
        {
            _spectatorTopDown = !_spectatorTopDown;
            if (IsBot(currentPlayer))
            {
                // When toggling top-down ON, always switch immediately — even during
                // cinematic moments (projectile follow, impact, kill cam). The player
                // wants to stay in the overhead view and not track bot projectiles.
                if (_spectatorTopDown)
                {
                    _camera?.Deactivate();
                    _combatCamera?.Deactivate();
                    _combatCamera?.TopDown(ComputeArenaMidpoint());
                }
                else
                {
                    // Toggling OFF: only switch if not mid-cinematic
                    if (_combatCamera != null &&
                        _combatCamera.CurrentMode != CombatCamera.Mode.FollowProjectile &&
                        _combatCamera.CurrentMode != CombatCamera.Mode.Impact &&
                        _combatCamera.CurrentMode != CombatCamera.Mode.KillCam &&
                        _combatCamera.CurrentMode != CombatCamera.Mode.RailgunBeam)
                    {
                        SwitchToFreeFlyCamera();
                    }
                }
            }
        }

        // Cycle target enemy with Tab/E/Q during targeting mode is handled by
        // CombatCamera.TargetCycleRequested event -> OnTargetCycleRequested()

        // Scroll-wheel weapon cycling removed — weapons are selected only
        // by clicking the weapon buttons in the CombatUI bar.
    }

    /// <summary>
    /// Transitions the combat camera to targeting mode where the player
    /// can orbit around the enemy fortress and click to set a target point.
    /// Builds a list of alive enemy players so the player can cycle through
    /// them with Tab/Q/E before clicking a precise target.
    /// </summary>
    private void TransitionToTargeting(PlayerSlot player)
    {
        if (_selectedWeaponIndex < 0 || _combatCamera == null || _aimingSystem == null)
        {
            return;
        }

        if (!_weapons.TryGetValue(player, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        int safeIndex = _selectedWeaponIndex % weaponList.Count;
        WeaponBase? weapon = weaponList[safeIndex];
        if (weapon == null || !GodotObject.IsInstanceValid(weapon))
        {
            return;
        }

        // Deactivate FreeFly camera if it's still active
        _camera?.Deactivate();

        // Highlight the selected weapon in the 3D world
        UpdateWeaponHighlight();

        // Clear any previous target
        _hasTarget = false;
        _aimingSystem.ClearTarget();

        // Build the list of alive enemies for target cycling
        BuildTargetEnemyList(player);

        // Default to the last attacked enemy if they're still alive, otherwise nearest
        _targetEnemyIndex = FindLastAttackedEnemyIndex(player);
        if (_targetEnemyIndex < 0)
            _targetEnemyIndex = FindNearestEnemyIndex(player);

        // Get the pivot for the selected enemy
        Vector3 pivot = GetEnemyPivot(_targetEnemySlots.Count > 0 ? _targetEnemySlots[_targetEnemyIndex] : player);

        // Activate combat camera in targeting mode
        _combatCamera.Activate();
        _combatCamera.EnterTargeting(pivot);
        _isAiming = true;

        // Show the target enemy selector on the CombatUI
        UpdateTargetEnemyUI();

        GD.Print($"[Combat] Entering targeting mode. Use Tab/Q/E to cycle enemies, click to set target.");
    }

    /// <summary>
    /// Populates _targetEnemySlots with all alive enemy players.
    /// </summary>
    private void BuildTargetEnemyList(PlayerSlot currentPlayer)
    {
        _targetEnemySlots.Clear();
        foreach ((PlayerSlot slot, BuildZone _) in _buildZones)
        {
            if (slot == currentPlayer) continue;
            if (_players.TryGetValue(slot, out PlayerData? data) && !data.IsAlive) continue;
            _targetEnemySlots.Add(slot);
        }
    }

    /// <summary>
    /// Returns the index into _targetEnemySlots of the enemy whose build zone
    /// center is closest to the current player's weapon position.
    /// </summary>
    private int FindNearestEnemyIndex(PlayerSlot currentPlayer)
    {
        if (_targetEnemySlots.Count == 0) return 0;

        Vector3 weaponPos = Vector3.Zero;
        if (_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? wl) && wl.Count > 0)
        {
            int si = _selectedWeaponIndex % wl.Count;
            WeaponBase? w = wl[si];
            if (w != null && GodotObject.IsInstanceValid(w))
                weaponPos = w.GlobalPosition;
        }

        int bestIdx = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _targetEnemySlots.Count; i++)
        {
            Vector3 pivot = GetEnemyPivot(_targetEnemySlots[i]);
            float d = weaponPos.DistanceTo(pivot);
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Returns the index into _targetEnemySlots of the last enemy this player attacked,
    /// or -1 if no memory exists or that enemy is no longer in the alive list.
    /// </summary>
    private int FindLastAttackedEnemyIndex(PlayerSlot currentPlayer)
    {
        if (!_lastAttackedEnemy.TryGetValue(currentPlayer, out PlayerSlot lastEnemy))
            return -1;

        for (int i = 0; i < _targetEnemySlots.Count; i++)
        {
            if (_targetEnemySlots[i] == lastEnemy)
                return i;
        }

        // Last attacked enemy is eliminated or not in the list
        return -1;
    }

    /// <summary>
    /// Returns the world-space center of a specific enemy's build zone,
    /// elevated to mid-fortress height for a good camera orbit pivot.
    /// </summary>
    private Vector3 GetEnemyPivot(PlayerSlot enemySlot)
    {
        if (_buildZones.TryGetValue(enemySlot, out BuildZone zone))
        {
            Vector3I centerBU = zone.OriginBuildUnits + zone.SizeBuildUnits / 2;
            Vector3 centerWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(centerBU));
            centerWorld.Y = Mathf.Max(centerWorld.Y, 4f);
            return centerWorld;
        }
        return new Vector3(0f, 4f, 0f);
    }

    /// <summary>
    /// Cycles to the next or previous enemy target during targeting mode.
    /// Updates the camera pivot and the UI indicator.
    /// </summary>
    private void CycleTargetEnemy(int direction)
    {
        if (_targetEnemySlots.Count <= 1 || _combatCamera == null) return;

        _targetEnemyIndex = ((_targetEnemyIndex + direction) % _targetEnemySlots.Count + _targetEnemySlots.Count) % _targetEnemySlots.Count;

        // Move camera pivot to the newly selected enemy's base
        Vector3 pivot = GetEnemyPivot(_targetEnemySlots[_targetEnemyIndex]);
        _combatCamera.SetTargetingPivot(pivot);

        // Clear any previously clicked target point since we changed enemy
        _hasTarget = false;
        _aimingSystem?.ClearTarget();

        // Update UI
        UpdateTargetEnemyUI();

        PlayerSlot targetSlot = _targetEnemySlots[_targetEnemyIndex];
        string targetName = _players.TryGetValue(targetSlot, out PlayerData? pd) ? pd.DisplayName : targetSlot.ToString();
        GD.Print($"[Combat] Target switched to: {targetName}");
    }

    /// <summary>
    /// Pushes the current target enemy info to the CombatUI so it can display
    /// the target selector banner with the enemy name, color, and cycle arrows.
    /// </summary>
    private void UpdateTargetEnemyUI()
    {
        CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        if (combatUI == null) return;

        if (_targetEnemySlots.Count == 0)
        {
            combatUI.HideTargetSelector();
            return;
        }

        PlayerSlot targetSlot = _targetEnemySlots[_targetEnemyIndex];
        string name = _players.TryGetValue(targetSlot, out PlayerData? pd) ? pd.DisplayName : targetSlot.ToString();
        Color color = pd?.PlayerColor ?? Colors.White;
        bool canCycle = _targetEnemySlots.Count > 1;

        combatUI.ShowTargetSelector(name, color, canCycle, _targetEnemyIndex + 1, _targetEnemySlots.Count);
    }

    /// <summary>
    /// Finds the center of the nearest enemy build zone for use as the
    /// targeting camera pivot point. Delegates to the target enemy list system.
    /// </summary>
    private Vector3 FindEnemyPivot(PlayerSlot currentPlayer)
    {
        BuildTargetEnemyList(currentPlayer);
        int idx = FindNearestEnemyIndex(currentPlayer);
        if (_targetEnemySlots.Count > 0)
            return GetEnemyPivot(_targetEnemySlots[idx]);
        return new Vector3(0f, 4f, 0f);
    }

    /// <summary>
    /// Called by CombatCamera when the player left-clicks during targeting mode.
    /// Raycasts from the camera through the mouse position into the voxel world
    /// to find a target point. If a voxel is hit, sets it as the aiming target.
    /// On a second click (target already set), fires the weapon.
    /// </summary>
    private void OnTargetClickRequested(Vector2 mousePos)
    {
        if (CurrentPhase != GamePhase.Combat || _aimingSystem == null || _voxelWorld == null || _combatCamera == null)
        {
            return;
        }

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        // If we already have a target, a second click fires
        if (_hasTarget && _aimingSystem.HasTarget)
        {
            FireCurrentPlayerWeapon();
            return;
        }

        // Raycast from camera through the mouse position
        Vector3 rayOrigin = _combatCamera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _combatCamera.ProjectRayNormal(mousePos);

        // --- Aim assist: snap to enemy commander if the click ray passes near one ---
        Vector3? commanderSnapTarget = TrySnapToCommander(rayOrigin, rayDir, currentPlayer);

        // Determine the world-space target: commander snap takes priority over voxel hit
        Vector3? targetWorld = null;

        if (commanderSnapTarget.HasValue)
        {
            targetWorld = commanderSnapTarget.Value;
        }
        else if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
        {
            // Convert microvoxel position to world-space center of the clicked voxel.
            // MicrovoxelToWorld returns the corner; offset by half a microvoxel so
            // the ballistic solution targets the voxel center, eliminating the
            // systematic ~0.25m bias that made projectiles land off-target.
            targetWorld = MathHelpers.MicrovoxelToWorld(hitPos)
                + new Vector3(
                    GameConfig.MicrovoxelMeters * 0.5f,
                    GameConfig.MicrovoxelMeters * 0.5f,
                    GameConfig.MicrovoxelMeters * 0.5f);
        }

        if (targetWorld.HasValue)
        {
            // Get the weapon for ballistic calculations
            if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
            {
                return;
            }

            int safeIndex = _selectedWeaponIndex % weaponList.Count;
            WeaponBase? weapon = weaponList[safeIndex];
            if (weapon == null || !GodotObject.IsInstanceValid(weapon))
            {
                return;
            }

            // Set the target on the aiming system (auto-calculates ballistic trajectory)
            bool inRange = _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetWorld.Value, weapon.ProjectileSpeed, weapon.WeaponId);
            _hasTarget = true;

            // Rotate the weapon to face the target (yaw only, stays upright)
            RotateWeaponToward(weapon, targetWorld.Value);

            if (commanderSnapTarget.HasValue)
            {
                GD.Print($"[Combat] AIM ASSIST: Snapped to enemy commander at {targetWorld.Value}. Click again to fire.");
            }
            else if (inRange)
            {
                GD.Print($"[Combat] Target set at {targetWorld.Value}. Click again or press SPACE/FIRE to shoot.");
            }
            else
            {
                GD.Print($"[Combat] Target at {targetWorld.Value} is OUT OF RANGE. Aiming for maximum distance.");
            }
        }
    }

    /// <summary>
    /// Aim assist: checks if the camera ray passes near any enemy commander.
    /// If so, and there is clear line-of-sight from the current weapon to the
    /// commander (no solid voxels blocking), returns the commander's chest
    /// position as a snap target. Otherwise returns null.
    /// </summary>
    /// <param name="rayOrigin">Camera ray origin (screen click projected into world).</param>
    /// <param name="rayDir">Camera ray direction (normalized).</param>
    /// <param name="currentPlayer">The player who is aiming (to exclude own commander).</param>
    /// <returns>Snap target position, or null if no commander is close enough.</returns>
    private Vector3? TrySnapToCommander(Vector3 rayOrigin, Vector3 rayDir, PlayerSlot currentPlayer)
    {
        // Generous world-space threshold: ray must pass within this distance
        // of the commander's center-mass to trigger the snap. 1.5m is generous
        // enough to feel helpful without being jarring.
        const float snapRadius = 1.5f;

        // Chest offset above GlobalPosition (which is at the hips).
        // The spine/torso center is roughly 0.2m above the hips for the 0.08m
        // voxel-size commander model.
        const float chestOffsetY = 0.2f;

        CommanderActor? bestCommander = null;
        float bestDistSq = float.MaxValue;

        foreach (var kvp in _commanders)
        {
            // Skip own commander and dead commanders
            if (kvp.Key == currentPlayer) continue;
            CommanderActor commander = kvp.Value;
            if (!GodotObject.IsInstanceValid(commander) || commander.IsDead) continue;

            Vector3 chestPos = commander.GlobalPosition + Vector3.Up * chestOffsetY;

            // Point-to-line distance from commander chest to the camera ray
            Vector3 toChest = chestPos - rayOrigin;
            float projection = toChest.Dot(rayDir);

            // Commander must be in front of the camera (positive projection)
            if (projection < 0f) continue;

            Vector3 closestPointOnRay = rayOrigin + rayDir * projection;
            float distSq = chestPos.DistanceSquaredTo(closestPointOnRay);

            if (distSq < snapRadius * snapRadius && distSq < bestDistSq)
            {
                bestCommander = commander;
                bestDistSq = distSq;
            }
        }

        if (bestCommander == null || _voxelWorld == null) return null;

        Vector3 snapTarget = bestCommander.GlobalPosition + Vector3.Up * chestOffsetY;

        // LOS check: get the current weapon position and verify no solid voxels
        // block the path from the weapon to the commander.
        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return null;
        }

        int safeIndex = _selectedWeaponIndex % weaponList.Count;
        WeaponBase? weapon = (safeIndex >= 0 && safeIndex < weaponList.Count) ? weaponList[safeIndex] : null;
        if (weapon == null || !GodotObject.IsInstanceValid(weapon)) return null;

        Vector3 weaponPos = weapon.GlobalPosition;
        Vector3 losDir = (snapTarget - weaponPos).Normalized();
        float losDist = weaponPos.DistanceTo(snapTarget);

        // If the voxel raycast hits something before reaching the commander,
        // LOS is blocked — don't snap.
        if (_voxelWorld.RaycastVoxel(weaponPos, losDir, losDist, out Vector3I _, out Vector3I _2))
        {
            // A solid voxel was hit before the commander — check if it's closer
            // than the commander. The raycast always returns the first hit, so
            // if it returns true at all, something is in the way.
            GD.Print($"[Combat] Aim assist: LOS to commander blocked by voxel.");
            return null;
        }

        GD.Print($"[Combat] Aim assist: clear LOS to enemy commander at {snapTarget}");
        return snapTarget;
    }

    /// <summary>
    /// Cancels the current targeting operation and returns to behind-zone view.
    /// </summary>
    private void CancelTargeting()
    {
        _isAiming = false;
        _hasTarget = false;
        _weaponConfirmed = false;
        _aimingSystem?.ClearTarget();
        _combatCamera?.ExitWeaponPOV();
        // Return to FreeFlyCamera for WASD movement
        SwitchToFreeFlyCamera();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        HideTargetHighlight();

        // Remove the green selection highlight from all weapons
        ClearAllWeaponHighlights();

        // Hide the target enemy selector UI
        CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        combatUI?.HideTargetSelector();
    }

    /// <summary>
    /// Highlights the currently selected weapon with a green glow and removes
    /// the highlight from all other weapons owned by the same player.
    /// Call this whenever the weapon selection changes.
    /// </summary>
    private void UpdateWeaponHighlight()
    {
        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        // When no weapon is selected (-1), unhighlight all weapons
        int safeIndex = _selectedWeaponIndex < 0 ? -1 : _selectedWeaponIndex % weaponList.Count;

        for (int i = 0; i < weaponList.Count; i++)
        {
            WeaponBase? w = weaponList[i];
            if (w != null && GodotObject.IsInstanceValid(w))
            {
                w.SetHighlighted(i == safeIndex);
            }
        }
    }

    /// <summary>
    /// Removes the green selection highlight from all weapons across all players.
    /// Called on turn change, phase change, and when cancelling targeting.
    /// </summary>
    private void ClearAllWeaponHighlights()
    {
        foreach ((PlayerSlot _, List<WeaponBase> weaponList) in _weapons)
        {
            foreach (WeaponBase? w in weaponList)
            {
                if (w != null && GodotObject.IsInstanceValid(w))
                {
                    w.SetHighlighted(false);
                }
            }
        }
    }

    /// <summary>
    /// Rotates a weapon node to face a world-space target point (yaw only, stays upright).
    /// Called when the player sets a target so the weapon visually aims at it.
    /// </summary>
    private static void RotateWeaponToward(WeaponBase weapon, Vector3 targetWorld)
    {
        Vector3 toTarget = targetWorld - weapon.GlobalPosition;
        toTarget.Y = 0f; // yaw only
        if (toTarget.LengthSquared() < 0.01f) return;
        toTarget = toTarget.Normalized();
        weapon.LookAt(weapon.GlobalPosition + toTarget, Vector3.Up);
    }

    /// <summary>
    /// Per-frame hover highlight: raycasts from mouse position and shows a
    /// pulsing wireframe cube on the voxel under the cursor during targeting.
    /// </summary>
    private void UpdateTargetingHighlight()
    {
        // Disabled: the green highlight/glow on hovered blocks made aiming too easy.
        // The crosshair and click-to-target (OnTargetClickRequested) are unaffected
        // since they do their own independent raycast.
        HideTargetHighlight();
    }

    private void ShowTargetHighlight(Vector3I microvoxelPos)
    {
        if (_targetHighlight == null)
        {
            _targetHighlight = new MeshInstance3D();
            _targetHighlight.Name = "TargetHighlight";

            // Wireframe cube slightly larger than a microvoxel (0.5m)
            BoxMesh box = new BoxMesh();
            box.Size = new Vector3(
                GameConfig.MicrovoxelMeters * 1.05f,
                GameConfig.MicrovoxelMeters * 1.05f,
                GameConfig.MicrovoxelMeters * 1.05f);
            _targetHighlight.Mesh = box;

            // Use the outline highlight shader for a pulsing glow effect
            ShaderMaterial? shaderMat = null;
            if (ResourceLoader.Exists("res://assets/shaders/outline_highlight.gdshader"))
            {
                Shader shader = GD.Load<Shader>("res://assets/shaders/outline_highlight.gdshader");
                shaderMat = new ShaderMaterial();
                shaderMat.Shader = shader;
                shaderMat.SetShaderParameter("highlight_color", new Color(0.2f, 1f, 0.3f, 0.9f));
                shaderMat.SetShaderParameter("pulse_speed", 3.5f);
                shaderMat.SetShaderParameter("outline_width", 0.02f);
                shaderMat.SetShaderParameter("glow_intensity", 2.5f);
            }

            if (shaderMat != null)
            {
                _targetHighlight.MaterialOverride = shaderMat;
            }
            else
            {
                // Fallback: simple red translucent material
                StandardMaterial3D mat = new StandardMaterial3D();
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.AlbedoColor = new Color(1f, 0.3f, 0.2f, 0.4f);
                mat.EmissionEnabled = true;
                mat.Emission = new Color(1f, 0.3f, 0.2f);
                mat.EmissionEnergyMultiplier = 2f;
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                _targetHighlight.MaterialOverride = mat;
            }

            GetTree().Root.AddChild(_targetHighlight);
        }

        _targetHighlight.Visible = true;
        _targetHighlight.GlobalPosition = MathHelpers.MicrovoxelToWorld(microvoxelPos);
    }

    private void HideTargetHighlight()
    {
        if (_targetHighlight != null)
        {
            _targetHighlight.Visible = false;
        }
        _lastHoveredMicrovoxel = new Vector3I(-9999, -9999, -9999);
    }

    private void FireCurrentPlayerWeapon()
    {
        if (_selectedWeaponIndex < 0)
        {
            GD.Print("[Combat] Cannot fire — no weapon selected.");
            return;
        }

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer || _aimingSystem == null || _voxelWorld == null)
        {
            return;
        }

        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        int safeIndex = _selectedWeaponIndex % weaponList.Count;
        WeaponBase? weapon = weaponList[safeIndex];
        if (weapon == null || !GodotObject.IsInstanceValid(weapon) || !weapon.CanFire(_turnManager.RoundNumber))
        {
            // Try to find any weapon that can fire
            weapon = weaponList.Find(candidate => GodotObject.IsInstanceValid(candidate) && candidate.CanFire(_turnManager.RoundNumber));
            if (weapon == null)
            {
                GD.Print("[Combat] No weapons can fire this turn.");
                return;
            }
        }

        // Check if weapon is EMP-disabled
        if (_powerupExecutor != null && _powerupExecutor.IsWeaponEmpDisabled(weapon.WeaponId, currentPlayer, _players))
        {
            GD.Print($"[Combat] {weapon.WeaponId} is EMP-disabled and cannot fire!");
            return;
        }

        int roundBefore = weapon.LastFiredRound;
        ProjectileBase? projectile = weapon.Fire(_aimingSystem, _voxelWorld, _turnManager.RoundNumber);
        if (weapon.LastFiredRound != roundBefore)
        {
            _players[currentPlayer].Stats.ShotsFired++;

            // Remember which enemy we attacked so targeting defaults to them next turn
            if (_targetEnemySlots.Count > 0 && _targetEnemyIndex >= 0 && _targetEnemyIndex < _targetEnemySlots.Count)
            {
                _lastAttackedEnemy[currentPlayer] = _targetEnemySlots[_targetEnemyIndex];
            }

            _isAiming = false;
            _hasTarget = false;
            _aimingSystem.ClearTarget();

            // Clear the weapon highlight after firing
            ClearAllWeaponHighlights();

            // Hide the target enemy selector UI after firing
            {
                CombatUI? combatUI2 = GetNodeOrNull<CombatUI>("%CombatUI")
                    ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                combatUI2?.HideTargetSelector();
            }

            // Transition camera to follow projectile (cursor stays visible).
            // CombatCamera takes over temporarily for the cinematic follow/impact;
            // it will fire CinematicFinished when done, returning to FreeFlyCamera.
            if (_combatCamera != null && projectile != null && GodotObject.IsInstanceValid(projectile))
            {
                _camera?.Deactivate();
                _combatCamera.FollowProjectile(projectile);
            }
            else
            {
                // For hitscan weapons (railgun), the RailgunBeamFired event
                // triggers the cinematic beam camera. No action needed here;
                // the camera transition is handled in OnRailgunBeamFired.
            }

            // Wait for projectile to land, then linger on the impact before advancing
            PlayerSlot firedPlayer = currentPlayer;
            if (projectile != null)
            {
                WaitForProjectileThenAdvance(projectile, firedPlayer);
            }
            else
            {
                // Hitscan — advance after a short delay
                GetTree().CreateTimer(2.0).Timeout += () =>
                {
                    if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == firedPlayer)
                    {
                        _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
                    }
                };
            }
        }
    }

    private void OnSkipTurnPressed()
    {
        if (CurrentPhase != GamePhase.Combat || _turnManager == null)
        {
            return;
        }

        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();
        _combatCamera?.ExitWeaponPOV();
        SwitchToFreeFlyCamera();
        _turnManager.SkipTurn(Settings.TurnTimeSeconds);
    }

    /// <summary>
    /// Called by CombatCamera when the player presses ESC or right-click during
    /// weapon POV or targeting mode. Cancels aiming and returns to top-down view.
    /// </summary>
    private void OnExitWeaponPOVRequested()
    {
        if (CurrentPhase != GamePhase.Combat)
        {
            return;
        }

        CancelTargeting();
    }

    /// <summary>
    /// Called by CombatCamera when the player presses Tab/E/Q during targeting
    /// to cycle to the next or previous enemy base.
    /// </summary>
    private void OnTargetCycleRequested(int direction)
    {
        if (CurrentPhase != GamePhase.Combat || !_isAiming)
        {
            return;
        }

        CycleTargetEnemy(direction);
    }

    // ─────────────────────────────────────────────────
    //  FORTRESS BUILDING (for placing commanders & weapons after build phase)
    // ─────────────────────────────────────────────────

    private void BuildPrototypeFortresses()
    {
        if (_voxelWorld == null || _weaponPlacer == null)
        {
            return;
        }

        // For each player, auto-place commander and weapons only if they weren't
        // manually placed during the build phase.
        // Human players must place weapons manually — only bots get auto-placed weapons.
        foreach ((PlayerSlot slot, BuildZone zone) in _buildZones)
        {
            bool hasCommander = _commanders.TryGetValue(slot, out CommanderActor? existingCmd)
                && existingCmd != null && GodotObject.IsInstanceValid(existingCmd);
            bool hasWeapons = _weapons.TryGetValue(slot, out List<WeaponBase>? existingWeapons)
                && existingWeapons != null && existingWeapons.Count > 0;

            // Only auto-place weapons for bot players; human players must place their own
            bool shouldAutoPlaceWeapons = !hasWeapons && IsBot(slot);

            if (!hasCommander || shouldAutoPlaceWeapons)
            {
                PlaceCommanderAndWeapons(slot, zone, placeCommander: !hasCommander, placeWeapons: shouldAutoPlaceWeapons);
            }
        }
    }

    /// <summary>
    /// Deploys all troops purchased during the build phase for every player.
    /// Each player's troops are deployed against the next player in the build order.
    /// </summary>
    private void DeployAllTroops()
    {
        if (_armyManager == null)
        {
            return;
        }

        PlayerSlot[] activePlayers = _players.Keys.ToArray();
        for (int i = 0; i < activePlayers.Length; i++)
        {
            PlayerSlot player = activePlayers[i];
            // Target the next player in rotation
            PlayerSlot target = activePlayers[(i + 1) % activePlayers.Length];
            _armyManager.DeployTroops(player, target, this);
        }
    }

    private void PlaceCommanderAndWeapons(PlayerSlot slot, BuildZone zone, bool placeCommander = true, bool placeWeapons = true)
    {
        if (_voxelWorld == null || _weaponPlacer == null)
        {
            return;
        }

        if (placeCommander)
        {
            // Place commander at the center of the build zone (player already built around it)
            Vector3I commanderBU = zone.OriginBuildUnits + new Vector3I(
                zone.SizeBuildUnits.X / 2,
                1, // one unit above the ground layer
                zone.SizeBuildUnits.Z / 2);

            CommanderActor commander = new CommanderActor();
            commander.Name = $"Commander_{slot}";
            AddChild(commander);
            commander.OwnerSlot = slot;
            commander.PlaceCommander(_voxelWorld, commanderBU);
            _commanders[slot] = commander;
            _players[slot].CommanderMicrovoxelPosition = commanderBU * GameConfig.MicrovoxelsPerBuildUnit;
            _players[slot].CommanderHealth = GameConfig.CommanderHP;
        }

        if (placeWeapons)
        {
            // Place weapons at the front of the build zone
            int frontZ = zone.OriginBuildUnits.Z;
            int topY = zone.OriginBuildUnits.Y + 3;
            int centerX = zone.OriginBuildUnits.X + zone.SizeBuildUnits.X / 2;

            if (!_weapons.ContainsKey(slot))
            {
                _weapons[slot] = new List<WeaponBase>();
            }

            List<WeaponBase> weaponList = _weapons[slot];
            PlayerData? pdata = _players.TryGetValue(slot, out PlayerData? pd) ? pd : null;
            WeaponBase w1 = _weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld, new Vector3I(centerX, topY, frontZ), slot);
            weaponList.Add(w1);
            pdata?.WeaponIds.Add(w1.WeaponId);
            WeaponBase w2 = _weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld, new Vector3I(centerX - 2, topY, frontZ), slot);
            weaponList.Add(w2);
            pdata?.WeaponIds.Add(w2.WeaponId);
            WeaponBase w3 = _weaponPlacer.PlaceWeapon<Railgun>(this, _voxelWorld, new Vector3I(centerX + 2, topY, frontZ), slot);
            weaponList.Add(w3);
            pdata?.WeaponIds.Add(w3.WeaponId);
        }
    }

    /// <summary>
    /// Runs the full bot build phase: generates a fortress design using
    /// BotBuildPlanner, stamps voxels into the world, and places commander
    /// and weapons from the plan.
    /// </summary>
    private void RunBotBuildPhase(PlayerSlot botSlot)
    {
        if (_voxelWorld == null || _weaponPlacer == null)
        {
            return;
        }

        if (!_buildZones.TryGetValue(botSlot, out BuildZone zone))
        {
            return;
        }

        if (!_players.TryGetValue(botSlot, out PlayerData? player))
        {
            return;
        }

        // Determine difficulty based on settings (default Medium)
        AI.BotDifficulty difficulty = AI.BotDifficulty.Medium;

        // Get or create a persistent BotController (reused for combat phase)
        if (!_botControllers.TryGetValue(botSlot, out AI.BotController? botBuilder))
        {
            botBuilder = new AI.BotController();
            botBuilder.Difficulty = difficulty;
            botBuilder.PlayerSlot = botSlot;
            AddChild(botBuilder);
            _botControllers[botSlot] = botBuilder;
        }

        botBuilder.ResetForMatch();
        AI.BotBuildPlan plan = botBuilder.RunBuildPhase(_voxelWorld, zone, player);

        // Place commander from the plan
        if (plan.CommanderBuildUnit != Vector3I.Zero)
        {
            // Remove existing commander if any
            if (_commanders.TryGetValue(botSlot, out CommanderActor? existingCmd)
                && existingCmd != null && GodotObject.IsInstanceValid(existingCmd))
            {
                existingCmd.QueueFree();
            }

            CommanderActor commander = new CommanderActor();
            commander.Name = $"Commander_{botSlot}";
            AddChild(commander);
            commander.OwnerSlot = botSlot;
            commander.PlaceCommander(_voxelWorld, plan.CommanderBuildUnit);
            _commanders[botSlot] = commander;
            player.CommanderMicrovoxelPosition = plan.CommanderBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
            player.CommanderHealth = GameConfig.CommanderHP;
        }

        // Place weapons from the plan
        if (plan.WeaponPlacements.Count > 0)
        {
            if (!_weapons.ContainsKey(botSlot))
            {
                _weapons[botSlot] = new List<WeaponBase>();
            }

            List<WeaponBase> weaponList = _weapons[botSlot];
            PlayerData? botPd = _players.TryGetValue(botSlot, out PlayerData? bpd) ? bpd : null;
            foreach ((Vector3I pos, string weaponId) in plan.WeaponPlacements)
            {
                WeaponBase weapon = PlaceBotWeapon(weaponId, pos, botSlot);
                weaponList.Add(weapon);
                botPd?.WeaponIds.Add(weapon.WeaponId);
            }
        }

        GD.Print($"[Bot] {botSlot} built fortress with {plan.Actions.Count} actions, " +
                 $"{plan.WeaponPlacements.Count} weapons, commander at {plan.CommanderBuildUnit}.");
    }

    /// <summary>
    /// Places a weapon by string ID at the given build-unit position for a bot.
    /// </summary>
    private WeaponBase PlaceBotWeapon(string weaponId, Vector3I buildUnitPosition, PlayerSlot owner)
    {
        return weaponId switch
        {
            "cannon" => _weaponPlacer!.PlaceWeapon<Cannon>(this, _voxelWorld!, buildUnitPosition, owner),
            "mortar" => _weaponPlacer!.PlaceWeapon<Mortar>(this, _voxelWorld!, buildUnitPosition, owner),
            "railgun" => _weaponPlacer!.PlaceWeapon<Railgun>(this, _voxelWorld!, buildUnitPosition, owner),
            "missile" => _weaponPlacer!.PlaceWeapon<MissileLauncher>(this, _voxelWorld!, buildUnitPosition, owner),
            "drill" => _weaponPlacer!.PlaceWeapon<Drill>(this, _voxelWorld!, buildUnitPosition, owner),
            _ => _weaponPlacer!.PlaceWeapon<Cannon>(this, _voxelWorld!, buildUnitPosition, owner),
        };
    }

    // ─────────────────────────────────────────────────
    //  EVENT HANDLERS
    // ─────────────────────────────────────────────────

    private void OnCommanderDamaged(CommanderDamagedEvent payload)
    {
        if (_players.TryGetValue(payload.Player, out PlayerData? player))
        {
            player.CommanderHealth = payload.RemainingHealth;
        }

        // Track who dealt damage to whom so bots can build threat profiles.
        // The current-turn player is the attacker.
        PlayerSlot? attacker = _turnManager?.CurrentPlayer;
        if (attacker.HasValue && attacker.Value != payload.Player)
        {
            // Record DamageDealt stat for the attacker
            if (_players.TryGetValue(attacker.Value, out PlayerData? attackerData))
            {
                attackerData.Stats.DamageDealt += payload.Damage;

                // Record ShotsHit (once per round to avoid double-counting from
                // a single shot that damages multiple targets)
                if (_hitRecordedThisRound.Add(attacker.Value))
                {
                    attackerData.Stats.ShotsHit++;
                }
            }

            // Notify every bot that was damaged about who attacked them
            if (_botControllers.TryGetValue(payload.Player, out AI.BotController? victimBot))
            {
                victimBot.RecordDamageReceived(attacker.Value, payload.Damage);
            }
        }
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        if (_players.TryGetValue(payload.Victim, out PlayerData? player))
        {
            player.IsAlive = false;
            player.CommanderHealth = 0;
        }

        // Track CommanderKills for the killer
        if (payload.Killer.HasValue && _players.TryGetValue(payload.Killer.Value, out PlayerData? killer))
        {
            killer.Stats.CommanderKills++;
        }

        // Activate kill cam: orbit the death location
        if (_combatCamera != null)
        {
            _camera?.Deactivate();
            _combatCamera.KillCam(payload.WorldPosition);
        }

        _turnManager?.RemovePlayer(payload.Victim, Settings.TurnTimeSeconds);
        int aliveCount = 0;
        PlayerSlot? winner = null;
        foreach ((PlayerSlot slot, PlayerData playerData) in _players)
        {
            if (playerData.IsAlive)
            {
                aliveCount++;
                winner = slot;
            }
        }

        if (aliveCount <= 1)
        {
            _artilleryDominanceActive = false;
            // Don't go to GameOver immediately — let the kill cam play out first.
            // OnCombatCameraCinematicFinished will trigger the actual GameOver.
            _pendingGameOver = true;
            _pendingWinner = winner;
        }
    }

    /// <summary>
    /// Handles turn changes: switch camera to top-down overview when the turn advances.
    /// The player can then select a weapon to enter weapon POV.
    /// If the current player is a bot, schedule automatic play.
    /// </summary>
    private void OnTurnChanged(TurnChangedEvent payload)
    {
        if (CurrentPhase != GamePhase.Combat || _combatCamera == null)
        {
            return;
        }

        // During artillery dominance, don't reposition camera or toggle UI —
        // the top-down overview and hidden combat HUD should remain locked.
        if (_artilleryDominanceActive)
        {
            return;
        }

        // Reset per-round hit tracking so a new shot can be counted as a hit
        _hitRecordedThisRound.Clear();

        // Tick active powerup effects for the current player only (so durations
        // count in that player's turns, not every player's turns)
        _powerupExecutor?.TickAllPlayerEffects(_players, payload.CurrentPlayer);

        // Tick army troops — only the current player's troops move/attack per turn
        _armyManager?.TickTroops(payload.CurrentPlayer, this);

        // Update CombatUI powerup slots for the new current player
        if (_players.TryGetValue(payload.CurrentPlayer, out PlayerData? currentPlayerData))
        {
            CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            combatUI?.UpdatePowerupSlots(currentPlayerData.Powerups);
        }

        // Reset aiming state and per-turn limits for the new turn
        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Clear weapon highlights from the previous player's turn
        ClearAllWeaponHighlights();

        // Only show skip turn button on the local player's turn
        if (_skipTurnButton != null)
            _skipTurnButton.Visible = !IsBot(payload.CurrentPlayer);

        // Hide target selector from the previous turn
        {
            CombatUI? combatUI2 = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            combatUI2?.HideTargetSelector();
        }
        if (_players.TryGetValue(payload.CurrentPlayer, out PlayerData? turnPlayer))
        {
            turnPlayer.AirstrikesUsedThisRound = 0;
        }

        // Refresh the CombatUI weapon bar for the new player
        RefreshCombatUIWeapons(payload.CurrentPlayer);

        // --- Check if the current player has any usable weapons BEFORE camera animation ---
        // This avoids wasting time animating the camera to a player who can't do anything.
        bool hasUsableWeapons = PlayerHasUsableWeapons(payload.CurrentPlayer);

        if (!hasUsableWeapons)
        {
            GD.Print($"[Combat] {payload.CurrentPlayer} has no usable weapons — auto-skipping turn.");

            // Before skipping, check if ANY player in the turn order has usable weapons.
            // If nobody can fire, trigger backup bombardment instead of abruptly ending.
            if (!AnyPlayerHasUsableWeapons())
            {
                GD.Print("[Combat] No player has usable weapons — triggering backup bombardment.");
                if (!TriggerBackupForBestSurvivor())
                {
                    // No valid bombardment target (e.g., only 1 player alive) — end normally
                    DeclareWinnerByHealth();
                }
                return;
            }

            // Someone has weapons but this player doesn't — check if the armed
            // player has ALL enemies disarmed (artillery dominance). This catches
            // the case where weapons were destroyed in a previous round and no new
            // WeaponDestroyed event fires to re-trigger the check.
            if (!_artilleryDominanceActive)
            {
                CheckArtilleryDominance();
                if (_artilleryDominanceActive) return; // dominance was triggered
            }

            // Skip immediately — no camera animation, no delay.
            // Use a zero-length timer to avoid re-entrancy (AdvanceTurn emits TurnChanged
            // synchronously, which would call OnTurnChanged recursively).
            PlayerSlot skipSlot = payload.CurrentPlayer;
            GetTree().CreateTimer(0.0).Timeout += () =>
            {
                if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer != skipSlot)
                    return;
                _turnManager.SkipTurn(Settings.TurnTimeSeconds);
            };
            return;
        }

        // If the combat camera is following a projectile or showing impact, let that finish
        // naturally (it will fire CinematicFinished, which switches back to FreeFlyCamera).
        // For immediate turns, position the camera appropriately.
        if (_combatCamera.CurrentMode != CombatCamera.Mode.FollowProjectile &&
            _combatCamera.CurrentMode != CombatCamera.Mode.Impact &&
            _combatCamera.CurrentMode != CombatCamera.Mode.KillCam &&
            _combatCamera.CurrentMode != CombatCamera.Mode.RailgunBeam)
        {
            if (IsBot(payload.CurrentPlayer) && _spectatorTopDown)
            {
                // Player has toggled top-down spectator mode — stay in top-down
                _camera?.Deactivate();
                _combatCamera.TopDown(ComputeArenaMidpoint());
            }
            else if (IsBot(payload.CurrentPlayer))
            {
                // Bot turn: show the bot's base briefly before it fires
                SwitchToFreeFlyCamera();
                PositionFreeFlyBehindZone(payload.CurrentPlayer);
            }
            else
            {
                // Human turn: position behind their build zone so they can aim
                SwitchToFreeFlyCamera();
                PositionFreeFlyBehindZone(payload.CurrentPlayer);
            }
        }

        // If the current player is a bot, schedule automatic play after a short delay
        if (IsBot(payload.CurrentPlayer))
        {
            PlayerSlot botSlot = payload.CurrentPlayer;
            GetTree().CreateTimer(1.0).Timeout += () => ExecuteBotTurn(botSlot);
        }
    }

    /// <summary>
    /// Returns true if the given player has at least one weapon that is valid,
    /// not destroyed, and can fire this round.
    /// </summary>
    private bool PlayerHasUsableWeapons(PlayerSlot player)
    {
        if (_turnManager == null)
        {
            return false;
        }

        if (!_weapons.TryGetValue(player, out List<WeaponBase>? weaponList) || weaponList == null)
        {
            return false;
        }

        return weaponList.Exists(w =>
            GodotObject.IsInstanceValid(w) && !w.IsDestroyed && w.CanFire(_turnManager.RoundNumber));
    }

    /// <summary>
    /// Returns true if the player has at least one intact (not destroyed) weapon,
    /// regardless of cooldown. Used for game-over decisions — the game should NOT
    /// end just because weapons are on cooldown (they'll be ready next round).
    /// </summary>
    private bool PlayerHasIntactWeapons(PlayerSlot player)
    {
        if (!_weapons.TryGetValue(player, out List<WeaponBase>? weaponList) || weaponList == null)
        {
            return false;
        }

        return weaponList.Exists(w =>
            GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
    }

    /// <summary>
    /// Returns true if any alive player in the current turn order has at least
    /// one intact weapon (regardless of cooldown). Used to detect the "all weapons
    /// destroyed" end condition. Weapons on cooldown will be ready next round, so
    /// the game should NOT end just because everything is on cooldown — only when
    /// all weapons are truly destroyed.
    /// </summary>
    private bool AnyPlayerHasUsableWeapons()
    {
        if (_turnManager == null)
        {
            return false;
        }

        foreach (PlayerSlot slot in _turnManager.TurnOrder)
        {
            if (PlayerHasIntactWeapons(slot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ends the game when no player has usable weapons remaining.
    /// The winner is determined by highest remaining commander health.
    /// If all commanders are dead (shouldn't happen normally), it's a draw.
    /// </summary>
    private void DeclareWinnerByHealth()
    {
        PlayerSlot? winner = null;
        int highestHealth = -1;

        foreach ((PlayerSlot slot, PlayerData playerData) in _players)
        {
            if (playerData.IsAlive && playerData.CommanderHealth > highestHealth)
            {
                highestHealth = playerData.CommanderHealth;
                winner = slot;
            }
        }

        GD.Print($"[Combat] Game over — no weapons remain. Winner by health: {winner?.ToString() ?? "none (draw)"}");

        // Mark all non-winners as eliminated so GameOverUI shows a single winner
        if (winner.HasValue)
        {
            foreach ((PlayerSlot slot, PlayerData playerData) in _players)
            {
                if (slot != winner.Value && playerData.IsAlive)
                {
                    playerData.IsAlive = false;
                }
            }
        }

        _turnManager?.StopTurnClock();
        Engine.TimeScale = 1.0;
        _artilleryDominanceActive = false;
        SetPhase(GamePhase.GameOver);

        foreach ((PlayerSlot slot, PlayerData _) in _players)
        {
            _progressionManager?.AwardMatchCompleted(winner.HasValue && winner.Value == slot);
        }
        // Commit match earnings to wallet only if the local player won.
        // Losing a match earns no currency.
        if (winner.HasValue && winner.Value == PlayerSlot.Player1
            && _players.TryGetValue(PlayerSlot.Player1, out PlayerData? localP1))
        {
            _progressionManager?.CommitMatchEarnings(localP1.Stats.MatchEarnings);
        }
    }

    /// <summary>
    /// Finds the best surviving player and triggers backup bombardment for them.
    /// Used when no player has usable weapons but living enemies remain to bombard.
    /// Returns true if bombardment was triggered, false if game should end normally.
    /// </summary>
    private bool TriggerBackupForBestSurvivor()
    {
        if (_artilleryDominanceActive) return false;

        // Find the best living player (highest health, or first alive if tied)
        PlayerSlot? winner = null;
        int highestHealth = -1;
        bool hasLivingEnemies = false;

        foreach ((PlayerSlot slot, PlayerData playerData) in _players)
        {
            if (!playerData.IsAlive) continue;
            if (playerData.CommanderHealth > highestHealth)
            {
                highestHealth = playerData.CommanderHealth;
                winner = slot;
            }
        }

        if (!winner.HasValue) return false;

        // Check if there are living enemies to bombard
        foreach ((PlayerSlot slot, PlayerData playerData) in _players)
        {
            if (slot != winner.Value && playerData.IsAlive)
            {
                hasLivingEnemies = true;
                break;
            }
        }

        if (!hasLivingEnemies) return false;

        // Trigger artillery dominance for the winner
        _artilleryDominanceActive = true;
        _artilleryDominanceWinner = winner.Value;
        _bombardmentTimer = 2.0f;
        _bombardmentSalvoCount = 0;
        _bombardmentTargetIndex = 0;
        _bombardmentTargets = null;
        Engine.TimeScale = 0.5;

        string winnerName = _players.TryGetValue(winner.Value, out PlayerData? wp) ? wp.DisplayName : winner.Value.ToString();
        GD.Print($"[Combat] BACKUP CALLED IN! {winnerName} is the last player standing — bombardment begins!");

        CanvasLayer? combatHUD = GetNodeOrNull<CanvasLayer>("CombatHUD");
        if (combatHUD != null) combatHUD.Visible = false;
        if (_skipTurnButton != null) _skipTurnButton.Visible = false;

        _turnManager?.StopTurnClock();
        ShowDominanceBanner();
        SwitchToTopDownOverview();

        return true;
    }

    /// <summary>
    /// Returns true if the given player slot is controlled by a bot.
    /// In prototype / local mode, only Player1 is human; all others are bots.
    /// </summary>
    private static bool IsBot(PlayerSlot slot)
    {
        return slot != PlayerSlot.Player1;
    }

    /// <summary>
    /// Executes an automated turn for a bot player: selects a target by scanning
    /// for actual solid voxels, chooses the best weapon, aims using ballistic
    /// math, fires, and ends the turn.
    /// </summary>
    private void ExecuteBotTurn(PlayerSlot botSlot)
    {
        // Guard: make sure it's still this bot's turn and we're still in combat
        if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer != botSlot)
        {
            return;
        }

        if (_aimingSystem == null || _voxelWorld == null)
        {
            _turnManager?.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // Find weapons that can fire this round
        if (!_weapons.TryGetValue(botSlot, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            GD.Print($"[Bot] {botSlot} has no weapons — skipping turn.");
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // Purge destroyed/invalid weapons
        weaponList.RemoveAll(w => w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed);

        WeaponBase? weapon = weaponList.Find(w => w.CanFire(_turnManager.RoundNumber));
        if (weapon == null)
        {
            GD.Print($"[Bot] {botSlot} has no weapons ready — skipping turn.");
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // Gather alive enemies
        List<(PlayerSlot Slot, PlayerData Data)> enemies = new List<(PlayerSlot, PlayerData)>();
        foreach ((PlayerSlot slot, PlayerData data) in _players)
        {
            if (slot != botSlot && data.IsAlive)
            {
                enemies.Add((slot, data));
            }
        }

        if (enemies.Count == 0)
        {
            GD.Print($"[Bot] {botSlot} found no enemies — skipping turn.");
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // Use the persistent BotController for difficulty-aware target selection
        AI.BotDifficulty difficulty = AI.BotDifficulty.Medium;
        if (_botControllers.TryGetValue(botSlot, out AI.BotController? botCtrl))
        {
            difficulty = botCtrl.Difficulty;
        }

        Random rng = new Random(System.Environment.TickCount ^ botSlot.GetHashCode() ^ _turnManager.RoundNumber);

        // --- Target selection (weighted random, difficulty-aware) ---
        // Gather threat scores from the BotController if available
        Dictionary<PlayerSlot, int>? threatScores = botCtrl?.GetThreatScores(enemies);

        // Compute this bot's zone center for distance weighting
        Vector3? botZoneCenter = null;
        if (_buildZones.TryGetValue(botSlot, out BuildZone botZone))
        {
            Vector3I mid = botZone.OriginMicrovoxels + botZone.SizeMicrovoxels / 2;
            botZoneCenter = Utility.MathHelpers.MicrovoxelToWorld(mid);
        }

        // Determine which enemies still have usable weapons — armed enemies are
        // real threats and should be prioritized over defenceless ones.
        HashSet<PlayerSlot> armedEnemies = new HashSet<PlayerSlot>();
        foreach (var (enemySlot, _) in enemies)
        {
            if (_weapons.TryGetValue(enemySlot, out List<WeaponBase>? enemyWeapons))
            {
                // Purge destroyed weapons before checking
                enemyWeapons.RemoveAll(w => w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed);
                if (enemyWeapons.Count > 0)
                {
                    armedEnemies.Add(enemySlot);
                }
            }
        }

        var enemy = AI.BotCombatPlanner.SelectTargetStatic(enemies, difficulty, rng, threatScores, botZoneCenter, armedEnemies);

        if (!_buildZones.TryGetValue(enemy.Slot, out BuildZone enemyZone))
        {
            GD.Print($"[Bot] {botSlot} can't find enemy build zone — skipping turn.");
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // --- Find the actual target position using prioritized targeting ---
        // Priority chain: Commander > Weapons > Structural supports > Large masses > Random solid
        CommanderActor? enemyCommander = null;
        if (_commanders.TryGetValue(enemy.Slot, out CommanderActor? cmdRef)
            && cmdRef != null && GodotObject.IsInstanceValid(cmdRef))
        {
            enemyCommander = cmdRef;
        }

        AI.BotCombatPlanner.PrioritizedTarget prioritizedResult =
            AI.BotCombatPlanner.FindPrioritizedTarget(
                _voxelWorld,
                enemyZone,
                enemy.Slot,
                enemy.Data,
                weapon,
                GetTree(),
                difficulty,
                rng,
                enemyCommander,
                botCtrl?.GetHitHistory());

        Vector3 targetPos = prioritizedResult.Position;
        GD.Print($"[Bot] {botSlot} targeting {enemy.Slot} — priority={prioritizedResult.Priority}, reason=\"{prioritizedResult.Description}\"");

        // Apply difficulty-based scatter to the target position
        // Reduce scatter when targeting the commander for a more lethal shot
        float baseScatter = difficulty switch
        {
            AI.BotDifficulty.Easy => 2.0f,
            AI.BotDifficulty.Medium => 1.0f,
            AI.BotDifficulty.Hard => 0.3f,
            _ => 1.5f,
        };
        // Reduce scatter when aiming at high-priority targets (commander, weapons)
        float scatter = baseScatter;
        if (prioritizedResult.Priority == AI.BotCombatPlanner.TargetPriority.Commander)
        {
            scatter *= 0.5f; // Half scatter for commander — go for the kill
        }
        else if (prioritizedResult.Priority == AI.BotCombatPlanner.TargetPriority.Weapon)
        {
            scatter *= 0.7f; // Slightly reduced scatter for weapon targeting
        }
        float scatterX = ((float)rng.NextDouble() - 0.5f) * 2f * scatter;
        float scatterZ = ((float)rng.NextDouble() - 0.5f) * 2f * scatter;
        float scatterY = ((float)rng.NextDouble() - 0.5f) * scatter * 0.5f;
        targetPos += new Vector3(scatterX, scatterY, scatterZ);

        // Use SetTargetPoint for accurate ballistic solution — identical to a player click.
        // No pitch hacks or adjustments. The math handles all weapon types correctly.
        _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetPos, weapon.ProjectileSpeed, weapon.WeaponId);

        // Rotate the weapon to face the target
        RotateWeaponToward(weapon, targetPos);

        GD.Print($"[Bot] {botSlot} aiming {weapon.WeaponId} at {enemy.Slot} target ({targetPos})");

        // Fire the weapon
        int roundBefore = weapon.LastFiredRound;
        ProjectileBase? projectile = weapon.Fire(_aimingSystem, _voxelWorld, _turnManager.RoundNumber);
        bool didFire = weapon.LastFiredRound != roundBefore;

        if (didFire)
        {
            if (_players.TryGetValue(botSlot, out PlayerData? botPlayer))
            {
                botPlayer.Stats.ShotsFired++;
            }
        }

        if (projectile != null)
        {
            // Follow the projectile with the combat camera (cinematic moment),
            // but skip the follow cam when spectator top-down is active so the
            // player keeps their overhead view during bot turns.
            if (_combatCamera != null && GodotObject.IsInstanceValid(projectile) && !_spectatorTopDown)
            {
                _camera?.Deactivate();
                _combatCamera.FollowProjectile(projectile);
            }

            GD.Print($"[Bot] {botSlot} fired {weapon.WeaponId} at {enemy.Slot}.");
        }
        else if (didFire)
        {
            GD.Print($"[Bot] {botSlot} fired {weapon.WeaponId} (hitscan) at {enemy.Slot}.");
        }

        // Wait for the projectile to actually land before advancing the turn.
        // Poll every 0.5s until the projectile is gone, then wait an extra 2s
        // so the camera can linger on the impact before moving on.
        if (projectile != null)
        {
            WaitForProjectileThenAdvance(projectile, botSlot);
        }
        else
        {
            // Hitscan or failed fire — advance after a short delay
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == botSlot)
                {
                    _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
                }
            };
        }
    }

    /// <summary>
    /// Polls until the projectile has landed (freed from the scene tree), then waits
    /// an additional 2 seconds for the camera to linger on the impact before advancing.
    /// Has a safety timeout of 15 seconds in case the projectile gets stuck.
    /// </summary>
    private void WaitForProjectileThenAdvance(ProjectileBase projectile, PlayerSlot botSlot)
    {
        float elapsed = 0f;
        const float pollInterval = 0.3f;
        const float maxWait = 15f;
        const float postImpactDelay = 2.5f;

        void Poll()
        {
            if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer != botSlot)
                return;

            elapsed += pollInterval;

            // Projectile still alive and we haven't timed out
            if (GodotObject.IsInstanceValid(projectile) && elapsed < maxWait)
            {
                GetTree().CreateTimer(pollInterval).Timeout += Poll;
                return;
            }

            // Projectile landed (or timed out) — linger on impact, then advance
            GetTree().CreateTimer(postImpactDelay).Timeout += () =>
            {
                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == botSlot)
                {
                    _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
                }
            };
        }

        // Start polling
        GetTree().CreateTimer(pollInterval).Timeout += Poll;
    }

    /// <summary>
    /// Handles weapon fired events: camera transitions to follow the projectile.
    /// </summary>
    private void OnWeaponFired(WeaponFiredEvent payload)
    {
        // Projectile follow is already handled in FireCurrentPlayerWeapon.
        // This handler is available for additional effects if needed.
    }

    /// <summary>
    /// Tracks VoxelsDestroyed and ShotsHit per player when voxels are destroyed during combat.
    /// Only counts voxels that go from solid (non-zero) to air (zero) with a known instigator.
    /// </summary>
    private void OnVoxelChangedStats(VoxelChangeEvent payload)
    {
        // Only track during combat phase (build-phase voxel changes are not destruction stats)
        if (CurrentPhase != GamePhase.Combat)
        {
            return;
        }

        // Only count voxel destruction (solid -> air), not placement or damage
        if (payload.BeforeData == 0 || payload.AfterData != 0)
        {
            return;
        }

        if (!payload.Instigator.HasValue)
        {
            return;
        }

        PlayerSlot instigator = payload.Instigator.Value;
        if (_players.TryGetValue(instigator, out PlayerData? instigatorData))
        {
            instigatorData.Stats.VoxelsDestroyed++;

            // Only earn currency from destroying player-built structures (voxels inside
            // a build zone), not from terrain, trees, or bombardment. This prevents
            // farming currency from the map and getting huge payouts from backup bombs.
            bool isInBuildZone = false;
            if (!_artilleryDominanceActive)
            {
                foreach (var kvp in _buildZones)
                {
                    if (kvp.Value.ContainsMicrovoxel(payload.Position))
                    {
                        isInBuildZone = true;
                        break;
                    }
                }
            }

            if (isInBuildZone)
            {
                var destroyedMaterial = (Voxel.VoxelMaterialType)(payload.BeforeData & 0xFF);
                if (GameConfig.MaterialEarnValues.TryGetValue(destroyedMaterial, out int earnValue))
                {
                    instigatorData.Stats.MatchEarnings += earnValue;
                }
            }

            // Record ShotsHit (once per round to avoid double-counting from
            // a single shot that destroys multiple voxels)
            if (_hitRecordedThisRound.Add(instigator))
            {
                instigatorData.Stats.ShotsHit++;
            }
        }
    }

    /// <summary>
    /// Handles railgun beam fired events: activates the cinematic beam camera
    /// that snaps to a side view of the beam path, holds briefly, then
    /// transitions to impact mode at the endpoint.
    /// </summary>
    private void OnRailgunBeamFired(RailgunBeamFiredEvent payload)
    {
        if (CurrentPhase != GamePhase.Combat || _combatCamera == null)
        {
            return;
        }

        // Don't override the kill cam — if the railgun killed a commander,
        // the KillCam was already activated by OnCommanderKilled.
        if (_combatCamera.CurrentMode == CombatCamera.Mode.KillCam)
        {
            return;
        }

        // Activate cinematic beam camera (deactivate the free-fly camera first)
        _camera?.Deactivate();
        _combatCamera.RailgunBeamCam(payload.Start, payload.End);
    }

    /// <summary>
    /// Handles weapon destruction: removes the weapon from the player's list
    /// and refreshes the CombatUI weapon bar.
    /// </summary>
    private void OnWeaponDestroyed(WeaponDestroyedEvent payload)
    {
        // Remove destroyed weapons from the player's list
        if (_weapons.TryGetValue(payload.Owner, out List<WeaponBase>? weaponList))
        {
            weaponList.RemoveAll(w => w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed);
        }

        // Also remove from PlayerData.WeaponIds so the scoreboard count updates
        if (_players.TryGetValue(payload.Owner, out PlayerData? ownerData))
        {
            ownerData.WeaponIds.Remove(payload.WeaponId);
        }

        // Refresh the CombatUI if this is the current player's weapon
        if (_turnManager?.CurrentPlayer == payload.Owner)
        {
            RefreshCombatUIWeapons(payload.Owner);
        }

        GD.Print($"[Combat] {payload.WeaponId} belonging to {payload.Owner} was destroyed at {payload.WorldPosition}.");

        // Check for Artillery Dominance: if any living player has destroyed ALL
        // enemy weapons, an automated bombardment rains projectiles on the enemy.
        if (CurrentPhase == GamePhase.Combat && !_artilleryDominanceActive)
        {
            CheckArtilleryDominance();
        }
    }

    /// <summary>
    /// Checks if any player has achieved Artillery Dominance (all enemy weapons destroyed).
    /// If so, an automated bombardment begins: projectiles rain down on all enemy bases
    /// from above until every enemy commander is dead. The player watches from a top-down camera.
    /// </summary>
    private void CheckArtilleryDominance()
    {
        foreach (var kvp in _players)
        {
            PlayerSlot candidate = kvp.Key;
            if (!kvp.Value.IsAlive) continue;

            // Check own weapon count (artillery dominance still triggers even with 0 weapons
            // — the "backup has arrived" bombardment is the reward for being the last survivor)
            int ownAlive = 0;
            if (_weapons.TryGetValue(candidate, out List<WeaponBase>? ownWeapons))
            {
                ownAlive = ownWeapons.FindAll(w => w != null && IsInstanceValid(w) && !w.IsDestroyed).Count;
            }

            // Check if ALL other living players have zero weapons
            bool allEnemiesDisarmed = true;
            bool hasLivingEnemies = false;
            foreach (var other in _players)
            {
                if (other.Key == candidate || !other.Value.IsAlive) continue;
                hasLivingEnemies = true;
                if (_weapons.TryGetValue(other.Key, out List<WeaponBase>? enemyWeapons))
                {
                    int enemyAlive = enemyWeapons.FindAll(w => w != null && IsInstanceValid(w) && !w.IsDestroyed).Count;
                    if (enemyAlive > 0)
                    {
                        allEnemiesDisarmed = false;
                        break;
                    }
                }
            }

            // Need living enemies to bombard — if nobody else is alive, game should end via OnCommanderKilled
            if (!hasLivingEnemies) continue;

            if (allEnemiesDisarmed)
            {
                _artilleryDominanceActive = true;
                _artilleryDominanceWinner = candidate;
                _bombardmentTimer = 2.0f; // brief pause before first bomb so player can read the banner
                _bombardmentSalvoCount = 0;
                _bombardmentTargetIndex = 0;
                _bombardmentTargets = null; // will be built on first ProcessBombardment call
                Engine.TimeScale = 0.5; // half speed for dramatic effect + reduced physics load
                GD.Print($"[Combat] ARTILLERY DOMINANCE! {kvp.Value.DisplayName} controls the battlefield — automated bombardment begins!");

                // Hide combat UI (weapon buttons, crosshairs, etc.)
                CanvasLayer? combatHUD = GetNodeOrNull<CanvasLayer>("CombatHUD");
                if (combatHUD != null) combatHUD.Visible = false;
                if (_skipTurnButton != null) _skipTurnButton.Visible = false;

                // Stop the turn clock
                _turnManager?.StopTurnClock();

                // Show "BACKUP HAS ARRIVED!" banner
                ShowDominanceBanner();

                // Switch to a top-down overview camera so the player can watch the barrage
                SwitchToTopDownOverview();

                return;
            }
        }
    }

    /// <summary>
    /// Runs the automated Artillery Dominance bombardment. Called each frame from ProcessCombatPhase
    /// while <see cref="_artilleryDominanceActive"/> is true. Spawns salvos of 3-5 projectiles
    /// every 1-2 seconds, targeting positions around enemy commanders with random spread.
    /// The bombardment continues until all enemy commanders are dead (OnCommanderKilled handles GameOver).
    /// </summary>
    private void ProcessBombardment(double delta)
    {
        _bombardmentTimer -= (float)delta;
        if (_bombardmentTimer > 0f)
            return;

        if (_voxelWorld == null)
            return;

        // Build/refresh target list on first call or when targets die
        if (_bombardmentTargets == null || _bombardmentTargets.Count == 0)
        {
            _bombardmentTargets = new List<PlayerSlot>();
            foreach (var kvp in _players)
            {
                if (kvp.Key == _artilleryDominanceWinner) continue;
                if (!kvp.Value.IsAlive) continue;
                _bombardmentTargets.Add(kvp.Key);
            }
            _bombardmentTargetIndex = 0;
        }

        // Remove dead targets
        _bombardmentTargets.RemoveAll(s => !_players.ContainsKey(s) || !_players[s].IsAlive);
        if (_bombardmentTargets.Count == 0)
            return;

        // Pick the next enemy fort sequentially
        if (_bombardmentTargetIndex >= _bombardmentTargets.Count)
            _bombardmentTargetIndex = 0;

        PlayerSlot enemySlot = _bombardmentTargets[_bombardmentTargetIndex];
        _bombardmentTargetIndex++;
        _bombardmentSalvoCount++;

        // 3 second gap before next bomb
        _bombardmentTimer = 3.0f;

        GD.Print($"[Combat] Bombardment bomb #{_bombardmentSalvoCount} targeting {enemySlot}!");

        // Target the center of the enemy's build zone
        Vector3 fortCenter = ComputePlayerFortressCenter(enemySlot);

        // Spawn projectile high above the fortress center
        Random rng = new Random(System.Environment.TickCount ^ _bombardmentSalvoCount);
        float spawnHeight = 50f + (float)rng.NextDouble() * 10f;
        Vector3 spawnPos = new Vector3(fortCenter.X, spawnHeight, fortCenter.Z);

        // Straight down velocity
        Vector3 velocity = new Vector3(0f, -20f, 0f);

        // One bomb at a time: 200 damage, 12 microvoxel blast radius (6m), 1.5x scale
        // Guaranteed 100% destruction within radius — multiple salvos ensure total annihilation
        ProjectileBase projectile = new ProjectileBase();
        projectile.SetProjectileType("mortar_shell");
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = spawnPos;
        projectile.Initialize(_voxelWorld, _artilleryDominanceWinner, velocity, 200, 12f);
        projectile.Scale = Vector3.One * 1.5f;

        // Smoke trail for dramatic effect
        TrailFX.CreateSmokeTrail(projectile);

        AudioDirector.Instance?.PlaySFX("missile_fire", spawnPos);
    }

    /// <summary>
    /// Pushes the current player's placed weapons to the CombatUI so only
    /// actually available (non-destroyed) weapons appear as selectable buttons.
    /// </summary>
    private void RefreshCombatUIWeapons(PlayerSlot player)
    {
        CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        if (combatUI == null)
        {
            return;
        }

        if (_weapons.TryGetValue(player, out List<WeaponBase>? weaponList))
        {
            // Filter out destroyed / invalid weapons
            List<WeaponBase> alive = weaponList.FindAll(w =>
                w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
            combatUI.SetAvailableWeapons(alive);
        }
        else
        {
            combatUI.SetAvailableWeapons(null);
        }
    }

    // ─────────────────────────────────────────────────
    //  CAMERA
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Deactivates CombatCamera and re-activates FreeFlyCamera with full arena bounds.
    /// Used to return to WASD/mouse free-fly movement after cinematic camera moments
    /// or when cancelling targeting mode during combat.
    /// </summary>
    private void SwitchToFreeFlyCamera()
    {
        _combatCamera?.Deactivate();
        if (_camera != null)
        {
            _camera.ResetToFullArenaBounds();
            _camera.Activate();
        }
    }

    /// <summary>
    /// Switches to a top-down overview camera looking down at the entire arena.
    /// Used during Artillery Dominance so the player can see the whole battlefield
    /// while bombarding defenseless enemies.
    /// </summary>
    private void SwitchToTopDownOverview()
    {
        _combatCamera?.Deactivate();
        if (_camera == null) return;

        _camera.ResetToFullArenaBounds();
        _camera.Activate();

        // Position camera high above the arena center, looking nearly straight down.
        // Height is tuned to show the full map in frame regardless of player count.
        Vector3 arenaCenter = ComputeArenaMidpoint();
        float overviewHeight = 70f;
        Vector3 cameraPos = arenaCenter + new Vector3(0f, overviewHeight, 20f);
        Vector3 lookTarget = arenaCenter;

        _camera.GlobalPosition = cameraPos;
        _camera.TransitionToLookTarget(cameraPos, lookTarget);

        // Lock the camera so the player can't fly away during bombardment
        _camera.SetProcessUnhandledInput(false);

        GD.Print($"[Combat] Camera switched to top-down overview at {cameraPos}");
    }

    private void ShowDominanceBanner()
    {
        _dominanceBannerLayer = new CanvasLayer();
        _dominanceBannerLayer.Name = "DominanceBanner";
        _dominanceBannerLayer.Layer = 90;
        AddChild(_dominanceBannerLayer);

        // Container centered on screen — slides in from left
        Control container = new Control();
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dominanceBannerLayer.AddChild(container);

        // Red flash overlay (full screen, flashes then fades)
        ColorRect flashRect = new ColorRect();
        flashRect.Name = "RedFlash";
        flashRect.Color = new Color(0.9f, 0.05f, 0.05f, 0.5f);
        flashRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        flashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        container.AddChild(flashRect);

        // Main text label — starts off-screen to the left
        Label bannerLabel = new Label();
        bannerLabel.Name = "BannerText";
        bannerLabel.Text = "BACKUP HAS ARRIVED";
        bannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bannerLabel.VerticalAlignment = VerticalAlignment.Center;
        bannerLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bannerLabel.AnchorTop = 0.38f;
        bannerLabel.AnchorBottom = 0.52f;
        bannerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        bannerLabel.Position = new Vector2(-1200f, 0f); // start off-screen left

        Font? pixelFont = GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");
        if (pixelFont != null)
            bannerLabel.AddThemeFontOverride("font", pixelFont);
        bannerLabel.AddThemeFontSizeOverride("font_size", 36);
        bannerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.2f, 0.1f));
        bannerLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        bannerLabel.AddThemeConstantOverride("outline_size", 4);
        container.AddChild(bannerLabel);

        // Play alert SFX
        AudioDirector.Instance?.PlaySFX("backup_alert");

        // Animate: slide in → hold → flash red → slide out
        Tween tween = CreateTween();
        tween.SetProcessMode(Tween.TweenProcessMode.Physics);

        // 1) Slide label in from left to center (0.3s, punchy overshoot)
        tween.TweenProperty(bannerLabel, "position", new Vector2(0f, 0f), 0.3f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        // 2) Flash the red overlay: fade it from 0.5 → 0 quickly
        tween.Parallel().TweenProperty(flashRect, "color:a", 0f, 0.4f)
            .From(0.5f);

        // 3) Second red flash pulse at 0.5s
        tween.TweenProperty(flashRect, "color:a", 0.3f, 0.1f);
        tween.TweenProperty(flashRect, "color:a", 0f, 0.3f);

        // 4) Hold in center for 1 second
        tween.TweenInterval(1.0f);

        // 5) Slide out to the right and fade
        tween.TweenProperty(bannerLabel, "position", new Vector2(1200f, 0f), 0.25f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(bannerLabel, "modulate:a", 0f, 0.25f);

        // 6) Clean up
        tween.TweenCallback(Callable.From(() =>
        {
            if (_dominanceBannerLayer != null && IsInstanceValid(_dominanceBannerLayer))
            {
                _dominanceBannerLayer.QueueFree();
                _dominanceBannerLayer = null;
            }
        }));
    }

    /// <summary>
    /// Positions the FreeFlyCamera behind the specified player's build zone,
    /// looking toward the arena center. Used during combat phase as the default
    /// free-fly starting position for each turn.
    /// </summary>
    private void PositionFreeFlyBehindZone(PlayerSlot slot)
    {
        if (_camera == null)
        {
            return;
        }

        Vector3 pivot = ComputePlayerFortressCenter(slot) + new Vector3(0f, 4f, 0f);
        Vector3 arenaCenter = ComputeArenaMidpoint();

        // Compute position behind the zone (away from arena center)
        Vector3 awayFromCenter = new Vector3(pivot.X - arenaCenter.X, 0f, pivot.Z - arenaCenter.Z);
        if (awayFromCenter.LengthSquared() < 0.01f)
        {
            awayFromCenter = new Vector3(0f, 0f, 1f);
        }
        awayFromCenter = awayFromCenter.Normalized();

        float cameraHeight = 30f;
        float cameraBack = 38f;
        Vector3 cameraPos = pivot + new Vector3(0f, cameraHeight, 0f) + awayFromCenter * cameraBack;

        _camera.TransitionToLookTarget(cameraPos, pivot);
    }

    /// <summary>
    /// Called when CombatCamera finishes a cinematic moment (impact cam, kill cam).
    /// Switches back to FreeFlyCamera for WASD movement.
    /// </summary>
    private void OnCombatCameraCinematicFinished()
    {
        // If the kill cam just finished and the game is over, transition now
        if (_pendingGameOver)
        {
            _pendingGameOver = false;
            Engine.TimeScale = 1.0;
            SetPhase(GamePhase.GameOver);
            foreach ((PlayerSlot slot, PlayerData _) in _players)
            {
                _progressionManager?.AwardMatchCompleted(_pendingWinner.HasValue && _pendingWinner.Value == slot);
            }
            if (_pendingWinner.HasValue && _pendingWinner.Value == PlayerSlot.Player1
                && _players.TryGetValue(PlayerSlot.Player1, out PlayerData? localPlayer))
            {
                _progressionManager?.CommitMatchEarnings(localPlayer.Stats.MatchEarnings);
            }
            _pendingWinner = null;
            return;
        }

        if (CurrentPhase == GamePhase.Combat || CurrentPhase == GamePhase.GameOver)
        {
            // After a cinematic moment, respect the spectator top-down preference
            // so the player stays in their chosen view during bot turns.
            bool isBotTurn = _turnManager?.CurrentPlayer is PlayerSlot current && IsBot(current);
            if (isBotTurn && _spectatorTopDown && _combatCamera != null)
            {
                _camera?.Deactivate();
                _combatCamera.TopDown(ComputeArenaMidpoint());
            }
            else
            {
                // Return to free-fly camera. If it's the human player's turn,
                // position behind their zone so they don't end up at some random
                // post-impact location after their projectile cinematic ends.
                SwitchToFreeFlyCamera();
                if (_turnManager?.CurrentPlayer is PlayerSlot humanPlayer && !IsBot(humanPlayer))
                {
                    PositionFreeFlyBehindZone(humanPlayer);
                }
            }
        }
    }

    private void PositionCameraAtBuildZone(PlayerSlot slot, bool animate = false)
    {
        if (_camera == null || !_buildZones.TryGetValue(slot, out BuildZone zone))
        {
            return;
        }

        // Compute zone center in world space (XZ center, but ground-level Y for the look target)
        Vector3I centerBU = zone.OriginBuildUnits + new Vector3I(zone.SizeBuildUnits.X / 2, 0, zone.SizeBuildUnits.Z / 2);
        Vector3 centerWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(centerBU));

        // Look target: center of the zone at a few meters above ground (where building happens)
        Vector3 lookTarget = centerWorld + new Vector3(0f, 4f, 0f);

        // Compute zone min/max in world space for camera bounds
        Vector3 zoneMinWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(zone.OriginBuildUnits));
        Vector3 zoneMaxWorld = MathHelpers.MicrovoxelToWorld(
            MathHelpers.BuildToMicrovoxel(zone.OriginBuildUnits + zone.SizeBuildUnits));

        // Restrict camera bounds to this player's zone
        _camera.SetBuildZoneBounds(zoneMinWorld, zoneMaxWorld);

        // Position camera behind the player's zone (away from arena center), looking
        // toward the zone center so the player sees their build area with the arena beyond.
        float zoneWidth = zone.SizeBuildUnits.X * GameConfig.BuildUnitMeters;
        float cameraHeight = zoneWidth * 1.1f;    // ~26m for 24m zone — higher to see more
        float cameraBack  = zoneWidth * 1.1f;     // ~26m back for a wider view of the build zone

        // Direction from arena center to zone center (XZ plane) — camera goes behind the zone
        Vector3 arenaCenter = ComputeArenaMidpoint();
        Vector3 awayFromCenter = new Vector3(centerWorld.X - arenaCenter.X, 0f, centerWorld.Z - arenaCenter.Z);
        if (awayFromCenter.LengthSquared() < 0.01f)
        {
            awayFromCenter = new Vector3(0f, 0f, 1f); // fallback
        }
        awayFromCenter = awayFromCenter.Normalized();

        Vector3 cameraPos = lookTarget + new Vector3(0f, cameraHeight, 0f) + awayFromCenter * cameraBack;

        if (animate)
        {
            _camera.TransitionToLookTarget(cameraPos, lookTarget);
        }
        else
        {
            _camera.SetLookTarget(cameraPos, lookTarget);
        }
    }

    /// <summary>
    /// Positions the combat camera behind the specified player's build zone,
    /// looking toward the arena center. Uses the same directional logic as
    /// <see cref="PositionCameraAtBuildZone"/> so all camera views consistently
    /// face the player's area of interest.
    /// </summary>
    private void PositionCombatCameraBehindZone(PlayerSlot slot)
    {
        if (_combatCamera == null)
        {
            return;
        }

        Vector3 pivot = ComputePlayerFortressCenter(slot) + new Vector3(0f, 4f, 0f);
        Vector3 arenaCenter = ComputeArenaMidpoint();
        _combatCamera.PositionBehindZone(pivot, arenaCenter);
    }

    /// <summary>
    /// Computes the midpoint of all active players' build zones in world space.
    /// Used to center the combat camera on the actual action area.
    /// </summary>
    private Vector3 ComputeArenaMidpoint()
    {
        if (_buildZones.Count == 0)
        {
            return Vector3.Zero;
        }

        Vector3 sum = Vector3.Zero;
        int count = 0;

        foreach ((PlayerSlot slot, BuildZone zone) in _buildZones)
        {
            // Only include zones of active (alive) players
            if (_players.TryGetValue(slot, out PlayerData? data) && !data.IsAlive)
            {
                continue;
            }

            // Use XZ center at ground level (not volumetric center) for better camera framing
            Vector3I centerBU = zone.OriginBuildUnits + new Vector3I(zone.SizeBuildUnits.X / 2, 0, zone.SizeBuildUnits.Z / 2);
            Vector3 centerWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(centerBU));
            sum += centerWorld;
            count++;
        }

        return count > 0 ? sum / count : Vector3.Zero;
    }

    /// <summary>
    /// Computes the world-space center of a specific player's build zone (ground-level XZ center).
    /// Used to center the combat camera on the current player's fortress during turn changes.
    /// Falls back to the arena midpoint if the player has no assigned zone.
    /// </summary>
    private Vector3 ComputePlayerFortressCenter(PlayerSlot slot)
    {
        if (!_buildZones.TryGetValue(slot, out BuildZone zone))
        {
            return ComputeArenaMidpoint();
        }

        // XZ center of the zone at ground level (Y=0) for a good top-down view
        Vector3I centerBU = zone.OriginBuildUnits + new Vector3I(zone.SizeBuildUnits.X / 2, 0, zone.SizeBuildUnits.Z / 2);
        Vector3 centerWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(centerBU));
        return centerWorld;
    }

    // ─────────────────────────────────────────────────
    //  UI EVENT SUBSCRIPTIONS
    // ─────────────────────────────────────────────────

    private void SubscribeToCombatUI()
    {
        // CombatUI may be a sibling or child node in the scene tree
        CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        if (combatUI != null)
        {
            combatUI.WeaponSelected += (index) =>
            {
                _selectedWeaponIndex = index;
                UpdateOverlayWeaponInfo();
                UpdateWeaponHighlight();
            };
            combatUI.FireRequested += OnFireRequestedFromUI;
            combatUI.PowerupActivateRequested += OnPowerupActivateRequested;
            combatUI.AirstrikeTargetSelected += OnAirstrikeTargetSelected;
            combatUI.TargetCycleRequested += OnTargetCycleRequested;
        }
    }

    private void SubscribeToBuildUI()
    {
        // The scene node is named "BuildHUD" (see BuildHUD.tscn / Main.tscn),
        // but its script type is BuildUI.  Search by node name first, then fall
        // back to the type-based unique-name lookup.
        BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
            ?? GetNodeOrNull<BuildUI>("%BuildUI")
            ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
        if (buildUI != null)
        {
            buildUI.PlaceWeaponRequested += OnPlaceWeaponRequested;
            buildUI.PlaceCommanderRequested += OnPlaceCommanderRequested;
            buildUI.ToolSelected += OnBuildToolSelected;
            buildUI.MaterialSelected += OnBuildMaterialSelected;
            buildUI.WeaponTypeSelected += OnWeaponTypeSelected;
            buildUI.WeaponSellRequested += OnWeaponSellRequested;
            buildUI.SandboxSaveRequested += (name) => SaveSandboxBuild(name);
            buildUI.SandboxLoadRequested += (name) => LoadSandboxBuild(name);
            buildUI.PowerupBuyRequested += OnPowerupBuyRequested;
            buildUI.PowerupSellRequested += OnPowerupSellRequested;
            buildUI.TroopBuyRequested += OnTroopBuyRequested;
            buildUI.TroopSellRequested += OnTroopSellRequested;
            buildUI.BlueprintSelected += OnBlueprintSelected;
            buildUI.ReadyPressed += OnReadyPressed;
            buildUI.SymmetryChanged += (mode) => { if (_buildSystem != null) _buildSystem.SymmetryMode = mode; };
            buildUI.UndoRequested += () => _buildSystem?.UndoLast(_activeBuilder);
            buildUI.RedoRequested += () => _buildSystem?.RedoLast(_activeBuilder);
            GD.Print("[GameManager] BuildUI subscribed successfully.");
        }
        else
        {
            GD.PrintErr("[GameManager] BuildUI not found! Commander/weapon placement buttons won't work.");
        }
    }

    private void OnFireRequestedFromUI()
    {
        if (CurrentPhase != GamePhase.Combat)
            return;

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
            return;

        if (_hasTarget && _aimingSystem != null && _aimingSystem.HasTarget)
        {
            // Target is set -- fire the weapon
            FireCurrentPlayerWeapon();
        }
        else if (!_isAiming)
        {
            // No target yet -- enter targeting mode
            _isAiming = true;
            TransitionToTargeting(currentPlayer);
        }
    }

    private void OnPlaceWeaponRequested()
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        _placementMode = PlacementMode.Weapon;
        _isDragBuilding = false;

        // Clear any active blueprint so the ghost preview and cursor validation
        // don't continue using the blueprint shape instead of a single block.
        if (_buildSystem != null)
        {
            _buildSystem.ActiveBlueprint = null;
            _buildSystem.CurrentToolMode = BuildToolMode.Single;
        }

        GD.Print($"[GameManager] Weapon placement mode activated. Click to place a {GetWeaponDisplayName(_selectedWeaponType)} (${GetWeaponCost(_selectedWeaponType)}).");
    }

    private void OnWeaponTypeSelected(WeaponType type)
    {
        _selectedWeaponType = type;
        GD.Print($"[GameManager] Selected weapon type: {GetWeaponDisplayName(type)} (${GetWeaponCost(type)}).");
    }

    private void OnPlaceCommanderRequested()
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        _placementMode = PlacementMode.Commander;
        _isDragBuilding = false;

        // Clear any active blueprint so the ghost preview and cursor validation
        // don't continue using the blueprint shape instead of a single block.
        if (_buildSystem != null)
        {
            _buildSystem.ActiveBlueprint = null;
            _buildSystem.CurrentToolMode = BuildToolMode.Single;
        }

        GD.Print("[GameManager] Commander placement mode activated. Click to place Commander.");
    }

    private void OnBuildToolSelected(BuildToolMode mode)
    {
        // Selecting a build tool exits placement mode and cancels any active drag
        _placementMode = PlacementMode.Block;
        _isDragBuilding = false;
        _buildRotation = 0;
        if (_buildSystem != null)
        {
            _buildSystem.CurrentToolMode = mode;
            // Clear active blueprint when switching to a non-blueprint tool
            if (mode != BuildToolMode.Blueprint)
            {
                _buildSystem.ActiveBlueprint = null;
            }
        }
    }

    private void OnBuildMaterialSelected(VoxelMaterialType material)
    {
        if (_buildSystem != null)
        {
            _buildSystem.CurrentMaterial = material;
            GD.Print($"[GameManager] Material changed to {material}.");
        }
    }

    private void OnBlueprintSelected(BlueprintDefinition blueprint)
    {
        if (_buildSystem == null)
        {
            return;
        }

        _placementMode = PlacementMode.Block;
        _isDragBuilding = false;
        _buildSystem.CurrentToolMode = BuildToolMode.Blueprint;
        _buildSystem.ActiveBlueprint = blueprint;
        GD.Print($"[GameManager] Blueprint selected: {blueprint.Name} ({blueprint.BlockCount} blocks).");
    }

    // ─────────────────────────────────────────────────
    //  HUD (buttons only — labels handled by BuildUI / CombatUI)
    // ─────────────────────────────────────────────────

    private void CreateHUD()
    {
        _hudRoot = new Control();
        _hudRoot.Name = "HUD";
        _hudRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hudRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_hudRoot);

        // Ready button (bottom right, during build phase)
        _readyButton = new Button();
        _readyButton.Name = "ReadyButton";
        _readyButton.Text = "READY";
        _readyButton.CustomMinimumSize = new Vector2(140, 44);
        _readyButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _readyButton.OffsetLeft = -160;
        _readyButton.OffsetTop = -140;
        _readyButton.OffsetRight = -16;
        _readyButton.OffsetBottom = -96;
        _readyButton.Pressed += OnReadyPressed;
        _readyButton.Visible = false;
        AddThemeToButton(_readyButton, new Color("2ea043"));
        _hudRoot.AddChild(_readyButton);

        // Skip Turn button (bottom right, during combat)
        _skipTurnButton = new Button();
        _skipTurnButton.Name = "SkipTurnButton";
        _skipTurnButton.Text = "SKIP TURN";
        _skipTurnButton.CustomMinimumSize = new Vector2(140, 44);
        _skipTurnButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _skipTurnButton.OffsetLeft = -160;
        _skipTurnButton.OffsetTop = -140;
        _skipTurnButton.OffsetRight = -16;
        _skipTurnButton.OffsetBottom = -96;
        _skipTurnButton.Pressed += OnSkipTurnPressed;
        _skipTurnButton.Visible = false;
        AddThemeToButton(_skipTurnButton, new Color("d4a029"));
        _hudRoot.AddChild(_skipTurnButton);

        // Warning label (centered, shown briefly when commander not placed)
        _buildWarningLabel = new Label();
        _buildWarningLabel.Name = "BuildWarningLabel";
        _buildWarningLabel.Text = "Place a Commander before readying up!";
        _buildWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _buildWarningLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _buildWarningLabel.OffsetTop = 80;
        _buildWarningLabel.OffsetLeft = -200;
        _buildWarningLabel.OffsetRight = 200;
        _buildWarningLabel.AddThemeFontSizeOverride("font_size", 18);
        _buildWarningLabel.AddThemeColorOverride("font_color", new Color("ff4444"));
        _buildWarningLabel.Visible = false;
        _hudRoot.AddChild(_buildWarningLabel);

        // Game overlay toolbar (camera presets, weapon info, shortcuts, settings)
        _gameOverlayUI = new GameOverlayUI();
        _gameOverlayUI.SetCameras(_combatCamera, _camera);
        _gameOverlayUI.SettingsRequested += OnOverlaySettingsRequested;
        _hudRoot.AddChild(_gameOverlayUI);

        // Pause menu (ESC key) — added last so it draws on top of everything
        _pauseMenu = new PauseMenu();
        _pauseMenu.Name = "PauseMenu";
        _hudRoot.AddChild(_pauseMenu);
    }

    private void OnOverlaySettingsRequested()
    {
        if (_settingsUI != null)
        {
            _settingsUI.Visible = !_settingsUI.Visible;
        }
    }

    private void OnMainMenuSettingsRequested()
    {
        if (_settingsUI != null)
        {
            _settingsUI.Visible = !_settingsUI.Visible;
        }
    }

    private void UpdateOverlayWeaponInfo()
    {
        if (_gameOverlayUI == null) return;

        WeaponBase? weapon = GetSelectedWeapon();
        if (weapon != null)
        {
            // Derive display name from the weapon's class name
            string className = weapon.GetType().Name;
            string displayName = className switch
            {
                "MissileLauncher" => "Missile",
                _ => className,
            };
            _gameOverlayUI.SetWeaponInfo(displayName);
        }
        else
        {
            _gameOverlayUI.ClearWeaponInfo();
        }
    }

    private static void AddThemeToButton(Button btn, Color accent)
    {
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.AddThemeColorOverride("font_color", new Color("e6edf3"));
        btn.AddThemeColorOverride("font_hover_color", accent);
        StyleBoxFlat normal = new StyleBoxFlat();
        normal.BgColor = new Color("0d1117");
        normal.BorderWidthBottom = 2;
        normal.BorderWidthTop = 2;
        normal.BorderWidthLeft = 2;
        normal.BorderWidthRight = 2;
        normal.BorderColor = accent;
        normal.CornerRadiusTopLeft = 4;
        normal.CornerRadiusTopRight = 4;
        normal.CornerRadiusBottomLeft = 4;
        normal.CornerRadiusBottomRight = 4;
        normal.ContentMarginLeft = 16;
        normal.ContentMarginRight = 16;
        normal.ContentMarginTop = 8;
        normal.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("normal", normal);
        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color("1f2937");
        btn.AddThemeStyleboxOverride("hover", hover);
        StyleBoxFlat pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(accent.R, accent.G, accent.B, 0.2f);
        btn.AddThemeStyleboxOverride("pressed", pressed);
    }

    private void UpdateHUD()
    {
        // HUD labels are now handled by BuildUI and CombatUI.
        // Only manage button visibility here.
    }

    // ─────────────────────────────────────────────────
    //  PROTOTYPE BUILDING HELPERS
    // ─────────────────────────────────────────────────

    private void StampPrototypeBox(PlayerSlot slot, VoxelMaterialType material, Vector3I start, Vector3I end, bool hollow)
    {
        if (_voxelWorld == null)
        {
            return;
        }

        Vector3I min = new Vector3I(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y), Mathf.Min(start.Z, end.Z));
        Vector3I max = new Vector3I(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y), Mathf.Max(start.Z, end.Z));
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    bool isShell = x == min.X || x == max.X || y == min.Y || y == max.Y || z == min.Z || z == max.Z;
                    if (!hollow || isShell)
                    {
                        StampBuildUnit(slot, material, new Vector3I(x, y, z));
                    }
                }
            }
        }
    }

    private void StampBuildUnit(PlayerSlot slot, VoxelMaterialType material, Vector3I buildUnitPosition)
    {
        if (_voxelWorld == null)
        {
            return;
        }

        Vector3I microBase = buildUnitPosition * GameConfig.MicrovoxelsPerBuildUnit;
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                {
                    _voxelWorld.SetVoxel(microBase + new Vector3I(x, y, z), Voxel.Voxel.Create(material), slot);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  SETUP
    // ─────────────────────────────────────────────────

    private void SeedLocalPlayers()
    {
        _players.Clear();
        int totalPlayers = 1 + Settings.BotCount; // 1 human + N bots
        string[] defaultNames = { "Green", "Red", "Blue", "Grey" };

        // Get unique random names for all bots
        string[] botNames = PlayerData.GetRandomBotNames(Settings.BotCount);
        int botIndex = 0;

        for (int i = 0; i < totalPlayers && i < BuildOrder.Length; i++)
        {
            PlayerSlot slot = BuildOrder[i];
            string displayName;
            if (i == 0 && !string.IsNullOrWhiteSpace(HumanPlayerName))
            {
                // Human player uses the name entered in the lobby/menu
                displayName = HumanPlayerName.Trim();
            }
            else if (i == 0)
            {
                // Human player with no name entered — fall back to color
                displayName = defaultNames[i];
            }
            else
            {
                // Bot players get random military-themed names
                displayName = botIndex < botNames.Length ? botNames[botIndex] : defaultNames[i];
                botIndex++;
            }
            _players[slot] = new PlayerData
            {
                PeerId = i + 1,
                Slot = slot,
                DisplayName = displayName,
                PlayerColor = GameConfig.PlayerColors[i],
            };
        }
    }

    private static void EnsureDefaultInputMap()
    {
        RegisterKeyAction("move_forward", Key.W);
        RegisterKeyAction("move_back", Key.S);
        RegisterKeyAction("move_left", Key.A);
        RegisterKeyAction("move_right", Key.D);
        RegisterKeyAction("move_up", Key.E);
        RegisterKeyAction("move_down", Key.Q);
        RegisterKeyAction("rotate_piece", Key.R);
        RegisterKeyAction("undo_build", Key.Z, ctrl: true);
        RegisterKeyAction("redo_build", Key.Y, ctrl: true);
        RegisterKeyAction("fire_weapon", Key.Space);
        RegisterMouseAction("place_primary", MouseButton.Left);
        RegisterMouseAction("place_secondary", MouseButton.Right);
    }

    private static void RegisterKeyAction(string actionName, Key key, bool ctrl = false)
    {
        if (!InputMap.HasAction(actionName))
        {
            InputMap.AddAction(actionName);
        }

        foreach (InputEvent existingEvent in InputMap.ActionGetEvents(actionName))
        {
            if (existingEvent is InputEventKey keyEvent && keyEvent.PhysicalKeycode == key && keyEvent.CtrlPressed == ctrl)
            {
                return;
            }
        }

        InputEventKey inputEvent = new InputEventKey();
        inputEvent.PhysicalKeycode = key;
        inputEvent.CtrlPressed = ctrl;
        InputMap.ActionAddEvent(actionName, inputEvent);
    }

    private static void RegisterMouseAction(string actionName, MouseButton button)
    {
        if (!InputMap.HasAction(actionName))
        {
            InputMap.AddAction(actionName);
        }

        foreach (InputEvent existingEvent in InputMap.ActionGetEvents(actionName))
        {
            if (existingEvent is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == button)
            {
                return;
            }
        }

        InputEventMouseButton inputEvent = new InputEventMouseButton();
        inputEvent.ButtonIndex = button;
        InputMap.ActionAddEvent(actionName, inputEvent);
    }

    // ─────────────────────────────────────────────────
    //  POWERUP SYSTEM
    // ─────────────────────────────────────────────────

    private void OnPowerupBuyRequested(PowerupType type)
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        PowerupDefinition def = PowerupDefinitions.Get(type);
        if (!player.CanSpend(def.Cost))
        {
            GD.Print($"[Powerup] {_activeBuilder}: Can't afford {type} (${def.Cost}, budget: ${player.Budget}).");
            return;
        }

        if (player.Powerups.TryBuy(type, player))
        {
            GD.Print($"[Powerup] {_activeBuilder}: Bought {type} for ${def.Cost}. Budget: ${player.Budget}.");

            // Update BuildUI powerup counts
            BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                ?? GetNodeOrNull<BuildUI>("%BuildUI")
                ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
            buildUI?.UpdatePowerupCounts(player.Powerups);
        }
    }

    private void OnPowerupSellRequested(PowerupType type)
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        if (player.Powerups.TrySell(type, player))
        {
            PowerupDefinition def = PowerupDefinitions.Get(type);
            GD.Print($"[Powerup] {_activeBuilder}: Sold {type} for ${def.Cost} refund. Budget: ${player.Budget}.");

            BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                ?? GetNodeOrNull<BuildUI>("%BuildUI")
                ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
            buildUI?.UpdatePowerupCounts(player.Powerups);
        }
    }

    private void OnTroopBuyRequested(TroopType type)
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        if (_armyManager == null)
        {
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        if (_armyManager.TryBuyTroop(_activeBuilder, type, player))
        {
            TroopStats stats = TroopDefinitions.Get(type);
            GD.Print($"[Army] {_activeBuilder}: Bought {stats.Name} for ${stats.Cost}. Budget: ${player.Budget}.");

            // Update BuildUI troop counts
            BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                ?? GetNodeOrNull<BuildUI>("%BuildUI")
                ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
            if (buildUI != null)
            {
                foreach (TroopType tt in TroopDefinitions.AllTypes)
                {
                    buildUI.UpdateTroopCount(tt, _armyManager.TroopCount(_activeBuilder, tt));
                }
            }

            // Emit budget changed so UI updates the budget display
            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, -stats.Cost));
        }
        else
        {
            TroopStats stats = TroopDefinitions.Get(type);
            GD.Print($"[Army] {_activeBuilder}: Can't buy {stats.Name} (${stats.Cost}, budget: ${player.Budget}, troops: {_armyManager.TroopCount(_activeBuilder, type)}/{GameConfig.MaxTroopsPerPlayer}).");
        }
    }

    private void OnTroopSellRequested(TroopType type)
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        if (_armyManager == null)
        {
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        if (_armyManager.TrySellTroop(_activeBuilder, type, player))
        {
            TroopStats stats = TroopDefinitions.Get(type);
            GD.Print($"[Army] {_activeBuilder}: Sold {stats.Name} for ${stats.Cost} refund. Budget: ${player.Budget}.");

            // Update BuildUI troop counts
            BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                ?? GetNodeOrNull<BuildUI>("%BuildUI")
                ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
            if (buildUI != null)
            {
                foreach (TroopType tt in TroopDefinitions.AllTypes)
                {
                    buildUI.UpdateTroopCount(tt, _armyManager.TroopCount(_activeBuilder, tt));
                }
            }

            // Emit budget changed so UI updates the budget display
            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, stats.Cost));
        }
    }

    private void OnWeaponSellRequested(WeaponType type)
    {
        if (CurrentPhase != GamePhase.Building)
        {
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            return;
        }

        if (!_weapons.TryGetValue(_activeBuilder, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            GD.Print($"[Build] {_activeBuilder}: No weapons to sell.");
            return;
        }

        // Find the last placed weapon of the requested type
        WeaponBase? target = null;
        for (int i = weaponList.Count - 1; i >= 0; i--)
        {
            WeaponBase w = weaponList[i];
            if (w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed)
            {
                continue;
            }

            WeaponType wType = w switch
            {
                Cannon => WeaponType.Cannon,
                Mortar => WeaponType.Mortar,
                Railgun => WeaponType.Railgun,
                MissileLauncher => WeaponType.MissileLauncher,
                Drill => WeaponType.Drill,
                _ => WeaponType.Cannon,
            };

            if (wType == type)
            {
                target = w;
                weaponList.RemoveAt(i);
                break;
            }
        }

        if (target == null)
        {
            GD.Print($"[Build] {_activeBuilder}: No {GetWeaponDisplayName(type)} to sell.");
            return;
        }

        // Refund the cost
        int refund = GetWeaponCost(type);
        player.Refund(refund);
        player.WeaponIds.Remove(target.WeaponId);

        GD.Print($"[Build] {_activeBuilder}: Sold {GetWeaponDisplayName(type)} for ${refund} refund. Budget: ${player.Budget}.");

        // Remove the weapon from the scene
        target.QueueFree();

        // Emit budget changed so UI updates
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, refund));
    }

    private void OnPowerupActivateRequested(PowerupType type)
    {
        if (CurrentPhase != GamePhase.Combat || _powerupExecutor == null)
        {
            return;
        }

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        if (!_players.TryGetValue(currentPlayer, out PlayerData? player))
        {
            return;
        }

        if (!player.Powerups.HasPowerup(type))
        {
            GD.Print($"[Powerup] {currentPlayer}: No {type} in inventory.");
            return;
        }

        bool success = false;
        switch (type)
        {
            case PowerupType.SmokeScreen:
                success = _powerupExecutor.ActivateSmokeScreen(player);
                break;

            case PowerupType.RepairKit:
                success = _powerupExecutor.ActivateRepairKit(player);
                break;

            case PowerupType.SpyDrone:
                _powerupExecutor.ActivateSpyDrone(player, _players);
                success = true;
                break;

            case PowerupType.ShieldGenerator:
                if (player.AssignedBuildZone is BuildZone shieldZone)
                {
                    Vector3I center = shieldZone.OriginBuildUnits + shieldZone.SizeBuildUnits / 2;
                    success = _powerupExecutor.ActivateShieldGenerator(player, center);
                }
                break;

            case PowerupType.AirstrikeBeacon:
                if (player.AirstrikesUsedThisRound >= 1)
                {
                    GD.Print($"[Powerup] {currentPlayer}: Already used an airstrike this round (max 1).");
                    return;
                }

                // Collect alive enemies
                List<PlayerData> aliveEnemies = new();
                foreach (PlayerData enemy in _players.Values)
                {
                    if (enemy.Slot != currentPlayer && enemy.IsAlive)
                    {
                        aliveEnemies.Add(enemy);
                    }
                }

                if (aliveEnemies.Count == 0)
                {
                    GD.Print($"[Powerup] {currentPlayer}: No alive enemies for airstrike.");
                    return;
                }

                if (aliveEnemies.Count == 1)
                {
                    // Only one enemy -- auto-target
                    PlayerData soloEnemy = aliveEnemies[0];
                    if (soloEnemy.AssignedBuildZone is BuildZone soloZone)
                    {
                        Vector3I target = soloZone.OriginBuildUnits + soloZone.SizeBuildUnits / 2;
                        success = _powerupExecutor.ActivateAirstrike(player, target, soloEnemy.Slot);
                        if (success) player.AirstrikesUsedThisRound++;
                    }
                }
                else
                {
                    // Multiple enemies -- show target picker UI
                    CombatUI? pickerUI = GetNodeOrNull<CombatUI>("%CombatUI")
                        ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                    if (pickerUI != null)
                    {
                        List<(PlayerSlot Slot, string Name, Color PlayerColor)> enemyList = new();
                        foreach (PlayerData enemy in aliveEnemies)
                        {
                            int colorIndex = (int)enemy.Slot;
                            Color color = colorIndex < GameConfig.PlayerColors.Length
                                ? GameConfig.PlayerColors[colorIndex]
                                : Colors.White;
                            enemyList.Add((enemy.Slot, enemy.DisplayName, color));
                        }
                        pickerUI.ShowAirstrikeTargetPicker(enemyList);
                        GD.Print($"[Powerup] {currentPlayer}: Showing airstrike target picker ({aliveEnemies.Count} enemies).");
                    }
                    return; // Don't mark success yet -- wait for target selection
                }
                break;

            case PowerupType.EmpBlast:
                foreach (PlayerData enemy in _players.Values)
                {
                    if (enemy.Slot == currentPlayer || !enemy.IsAlive)
                    {
                        continue;
                    }

                    if (_weapons.TryGetValue(enemy.Slot, out List<WeaponBase>? enemyWeapons) && enemyWeapons.Count > 0)
                    {
                        WeaponBase targetWeapon = enemyWeapons[0];
                        if (GodotObject.IsInstanceValid(targetWeapon))
                        {
                            success = _powerupExecutor.ActivateEmp(player, targetWeapon, enemy.Slot);
                            break;
                        }
                    }
                }
                break;
        }

        if (success)
        {
            CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            combatUI?.UpdatePowerupSlots(player.Powerups);
        }
    }

    /// <summary>
    /// Called when the player selects an enemy target from the airstrike picker UI.
    /// Executes the airstrike on the chosen enemy's fortress.
    /// </summary>
    private void OnAirstrikeTargetSelected(PlayerSlot targetEnemy)
    {
        if (CurrentPhase != GamePhase.Combat || _powerupExecutor == null)
        {
            return;
        }

        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        if (!_players.TryGetValue(currentPlayer, out PlayerData? player))
        {
            return;
        }

        if (!player.Powerups.HasPowerup(PowerupType.AirstrikeBeacon))
        {
            GD.Print($"[Powerup] {currentPlayer}: No AirstrikeBeacon in inventory (target selection).");
            return;
        }

        if (player.AirstrikesUsedThisRound >= 1)
        {
            GD.Print($"[Powerup] {currentPlayer}: Already used an airstrike this round (max 1).");
            return;
        }

        if (!_players.TryGetValue(targetEnemy, out PlayerData? enemy) || !enemy.IsAlive)
        {
            GD.Print($"[Powerup] {currentPlayer}: Target enemy {targetEnemy} not found or not alive.");
            return;
        }

        if (enemy.AssignedBuildZone is BuildZone enemyZone)
        {
            Vector3I target = enemyZone.OriginBuildUnits + enemyZone.SizeBuildUnits / 2;
            bool success = _powerupExecutor.ActivateAirstrike(player, target, targetEnemy);
            if (success)
            {
                player.AirstrikesUsedThisRound++;
                CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                    ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                combatUI?.UpdatePowerupSlots(player.Powerups);
            }
        }
    }

    private void OnPowerupActivated(PowerupType type, PlayerSlot slot, Vector3 position)
    {
        EventBus.Instance?.EmitPowerupActivated(new PowerupActivatedEvent(type, slot, position));
        GD.Print($"[GameManager] Powerup {type} activated by {slot} at {position}");
    }

    private void OnPowerupExpired(PowerupType type, PlayerSlot slot)
    {
        EventBus.Instance?.EmitPowerupExpired(new PowerupExpiredEvent(type, slot));
        GD.Print($"[GameManager] Powerup {type} expired for {slot}");
    }

    private T CreateNode<T>(string name)
        where T : Node, new()
    {
        T node = new T();
        node.Name = name;
        AddChild(node);
        return node;
    }

}
