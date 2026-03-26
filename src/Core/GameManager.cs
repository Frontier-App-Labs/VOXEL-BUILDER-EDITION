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

    // Multiplayer build phase sync
    private readonly Dictionary<PlayerSlot, string> _remoteBuildSnapshots = new();
    private bool _localBuildReady;

    // Build phase interaction state
    private Vector3I _buildCursorBuildUnit;
    private Vector3I _buildCursorMicrovoxel; // exact microvoxel for HalfBlock mode
    private bool _buildCursorValid;
    private bool _hasBuildCursor;

    // Multi-voxel drag state for tools (Wall, Floor, Box, Line, Ramp)
    private bool _isDragBuilding;
    private Vector3I _dragStartBuildUnit;
    private int _buildRotation; // 0-3 representing 0/90/180/270 degrees

    // Unified undo stack for all build-phase actions (voxels, weapons, troops, doors)
    private enum UndoType { Voxel, Weapon, Troop, Door }
    private sealed class BuildUndoEntry
    {
        public UndoType Type { get; init; }
        public WeaponBase? Weapon { get; init; }
        public WeaponType WeaponKind { get; init; }
        public TroopType TroopKind { get; init; }
        public int Cost { get; init; }
        public Vector3I Position { get; init; }
        public bool Cancelled { get; set; } // set true when item is manually deleted/sold
    }
    private readonly Stack<BuildUndoEntry> _buildUndoStack = new();

    // Build placement mode (commander / weapon placement during build phase)
    private enum PlacementMode { Block, Commander, Weapon, Troop }
    private PlacementMode _placementMode = PlacementMode.Block;
    private WeaponType _selectedWeaponType = WeaponType.Cannon;
    private TroopType _selectedTroopType = TroopType.Infantry;

    // Cached preview meshes for ghost preview (generated once per weapon type)
    private readonly Dictionary<string, ArrayMesh> _weaponPreviewMeshes = new();
    private ArrayMesh? _commanderPreviewMesh;
    private readonly Dictionary<TroopType, ArrayMesh> _troopPreviewMeshes = new();

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

    // Troop attack camera sequence: defers turn advancement so troops attack visually after weapon impact
    private bool _deferTurnAdvanceForTroops;
    private bool _troopSequenceActive;
    private PlayerSlot _troopSequencePlayer; // which player's troops are attacking
    private bool _troopsMoved; // true if human player has moved troops this turn

    // Spectator view preference: when true, bot turns (and post-fire cinematics) use
    // top-down instead of free-fly. Sticky — persists until the player presses V again.
    private bool _spectatorTopDown;

    // Troop movement mode: when true, clicking terrain moves all player troops
    private bool _troopMoveMode;

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
    // Skip powerup tick on the next turn change caused by a player death removal
    private bool _skipNextPowerupTick;
    private PlayerSlot? _pendingWinner;
    // Set when a commander kill cancels an active troop sequence — tells
    // OnCombatCameraCinematicFinished to advance the turn after the kill cam.
    private bool _advanceTurnAfterKillCam;
    // Set while processing a host-authoritative commander death broadcast so that
    // OnCommanderKilled knows to proceed. Client ignores locally-computed kills.
    private bool _processingHostCommanderDeath;

    // Sandbox mode: build freely with no opponents, save/load builds
    private bool _isSandbox;
    private string? _sandboxLoadBuildName;
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
        _turnManager.TurnTimedOut += () => AdvanceTurnAuthoritative();
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

        // Wire shield damage multiplier into the explosion system
        Explosion.ShieldMultiplierCallback = (microvoxelPos) =>
        {
            foreach (PlayerData p in _players.Values)
            {
                if (p.Powerups.HasActiveEffect(PowerupType.ShieldGenerator) &&
                    p.AssignedBuildZone.ContainsMicrovoxel(microvoxelPos))
                {
                    return 0.5f;
                }
            }
            return 1.0f;
        };

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

        if (_syncManager != null)
        {
            _syncManager.BuildCompleteReceived += OnRemoteBuildComplete;
            _syncManager.WeaponFireReceived += OnRemoteWeaponFire;
            _syncManager.SkipTurnReceived += OnRemoteSkipTurn;
            _syncManager.TurnOrderReceived += OnRemoteTurnOrder;
            _syncManager.TurnAdvanceReceived += OnRemoteTurnAdvance;
            _syncManager.TroopMoveReceived += OnRemoteTroopMove;
            _syncManager.PowerupUsedReceived += OnRemotePowerupUsed;
            _syncManager.EmpResultReceived += OnRemoteEmpResult;
            _syncManager.AirstrikeResultReceived += OnRemoteAirstrikeResult;
            _syncManager.CommanderDeathReceived += OnRemoteCommanderDeath;
            _syncManager.GameOverReceived += OnRemoteGameOver;
            _syncManager.DisconnectReceived += OnRemoteDisconnect;
            _syncManager.VoxelDamageReceived += OnRemoteVoxelDamage;
        }

        // Host broadcasts authoritative voxel damage from explosions to clients
        Explosion.VoxelDamageApplied += OnExplosionVoxelDamage;

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

        Explosion.VoxelDamageApplied -= OnExplosionVoxelDamage;
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

        // Use filtered alive-weapon list matching CombatUI's indices
        List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
        if (aliveWeapons.Count == 0) return null;

        int safeIndex = _selectedWeaponIndex % aliveWeapons.Count;
        WeaponBase? weapon = aliveWeapons[safeIndex];
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
    public void OnWeaponSelectedFromUI(int weaponIndex, bool cycleOnly = false)
    {
        if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        // Only allow weapon interaction on the local player's turn
        if (!IsLocalPlayer(currentPlayer))
        {
            return;
        }

        // Cancel troop move mode if active — weapon targeting takes priority
        if (_troopMoveMode)
        {
            _troopMoveMode = false;
            CombatUI? troopUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            troopUI?.SetTroopsModeActive(false);
            troopUI?.HideSelectWeaponPrompt();
        }

        if (!_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        // Use same filtered alive-weapon list that CombatUI uses
        List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
        if (aliveWeapons.Count == 0) return;

        int newIndex = weaponIndex % aliveWeapons.Count;

        // If already in targeting/aiming mode, just swap the weapon without
        // resetting the camera or target so the player's aim is preserved.
        // Check _isAiming OR _hasTarget: covers all paths into targeting
        // (weapon-click, fire-button, etc.) regardless of _weaponConfirmed state.
        if (_isAiming || _hasTarget)
        {
            _selectedWeaponIndex = newIndex;
            _weaponConfirmed = true;
            UpdateWeaponHighlight();

            // If there's an active target, recalculate the trajectory preview
            // for the new weapon's projectile speed / arc.
            if (_hasTarget && _aimingSystem != null && _aimingSystem.HasTarget)
            {
                WeaponBase? newWeapon = aliveWeapons[newIndex];
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

        // Highlight the confirmed weapon in the 3D world
        UpdateWeaponHighlight();

        // Cycle-only: just update the selection and highlight without transitioning
        // to targeting mode. Used by right-click weapon cycling so the player can
        // see which weapon is selected in their base view before clicking to target.
        if (cycleOnly)
        {
            GD.Print($"[Combat] Weapon {_selectedWeaponIndex} cycled (highlight only).");
            return;
        }

        _weaponConfirmed = true;

        GD.Print($"[Combat] Weapon {_selectedWeaponIndex} confirmed. Entering targeting mode.");

        // Transition to targeting mode
        TransitionToTargeting(currentPlayer);
    }


    /// <summary>
    /// Starts sandbox mode: a single build zone with no opponents or timer.
    /// The player can build freely and save/load their designs.
    /// Optionally loads an existing build if <paramref name="loadBuildName"/> is provided.
    /// </summary>
    public void StartSandboxMode(string? loadBuildName = null)
    {
        if (CurrentPhase != GamePhase.Menu && CurrentPhase != GamePhase.GameOver)
        {
            GD.Print("[GameManager] StartSandboxMode ignored: match already in progress.");
            return;
        }

        _isSandbox = true;
        _sandboxLoadBuildName = loadBuildName;
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

        // Capture weapons (including rotation)
        blueprint.Weapons.Clear();
        if (_weapons.TryGetValue(PlayerSlot.Player1, out List<WeaponBase>? weaponList))
        {
            foreach (WeaponBase w in weaponList)
            {
                if (w == null || !GodotObject.IsInstanceValid(w)) continue;
                Vector3I microPos = MathHelpers.WorldToMicrovoxel(w.GlobalPosition);
                Vector3I buildPos = MathHelpers.MicrovoxelToBuild(microPos);
                Vector3I relPos = buildPos - zone.OriginBuildUnits;
                blueprint.Weapons.Add(new BlueprintWeaponData
                {
                    WeaponId = w.WeaponId,
                    BuildUnitPosition = relPos,
                    RotationY = w.Rotation.Y,
                });
            }
        }

        // Capture doors
        blueprint.Doors.Clear();
        if (_armyManager != null)
        {
            var doors = _armyManager.Doors.GetDoors(PlayerSlot.Player1);
            foreach (var door in doors)
            {
                Vector3I relMicro = door.BaseMicrovoxel - zone.OriginMicrovoxels;
                int rotHint = NormalToRotationHint(door.OutwardNormal);
                blueprint.Doors.Add(new BlueprintDoorData
                {
                    RelativeMicrovoxel = relMicro,
                    RotationHint = rotHint,
                });
            }
        }

        // Capture troops
        blueprint.Troops.Clear();
        if (_armyManager != null)
        {
            var troops = _armyManager.GetPurchasedTroops(PlayerSlot.Player1);
            foreach (var t in troops)
            {
                Vector3I? relPos = t.PlacedPosition.HasValue
                    ? t.PlacedPosition.Value - zone.OriginMicrovoxels
                    : null;
                blueprint.Troops.Add(new BlueprintTroopData
                {
                    Type = t.Type,
                    RelativeMicrovoxel = relPos,
                });
            }
        }

        // Capture powerups
        blueprint.Powerups.Clear();
        if (_players.TryGetValue(PlayerSlot.Player1, out PlayerData? p1Save))
        {
            foreach (PowerupType pt in p1Save.Powerups.OwnedPowerups)
            {
                blueprint.Powerups.Add(pt);
            }
        }

        // Capture commander
        blueprint.CommanderBuildUnitPosition = null;
        if (_commanders.TryGetValue(PlayerSlot.Player1, out CommanderActor? cmd) && GodotObject.IsInstanceValid(cmd))
        {
            Vector3I relCmd = cmd.BuildUnitPosition - zone.OriginBuildUnits;
            blueprint.CommanderBuildUnitPosition = relCmd;
            blueprint.CommanderRotationY = cmd.Rotation.Y;
        }

        _blueprintSystem.SaveBlueprint(blueprint);

        // Track in player profile (use ProgressionManager's save path)
        PlayerProfile? profile = _progressionManager?.Profile;
        if (profile != null)
        {
            if (!profile.SavedBuilds.Contains(buildName))
            {
                profile.SavedBuilds.Add(buildName);
            }
            SaveSystem.SaveJson("user://profile/player_profile.json", profile);

            // Verify the profile was saved with the build name
            string profilePath = ProjectSettings.GlobalizePath("user://profile/player_profile.json");
            GD.Print($"[Sandbox] Profile saved to {profilePath} — SavedBuilds: [{string.Join(", ", profile.SavedBuilds)}]");
        }
        else
        {
            GD.PrintErr("[Sandbox] WARNING: _progressionManager or Profile is null — build name NOT saved to profile!");
        }

        GD.Print($"[Sandbox] Build '{buildName}' saved ({blueprint.Voxels.Count} voxels, {blueprint.Weapons.Count} weapons, {blueprint.Doors.Count} doors, {blueprint.Troops.Count} troops, {blueprint.Powerups.Count} powerups, commander={blueprint.CommanderBuildUnitPosition.HasValue}).");
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
        PlayerSlot slot = _isSandbox ? PlayerSlot.Player1 : _activeBuilder;

        if (clearChanges.Count > 0)
        {
            _voxelWorld.ApplyBulkChanges(clearChanges, slot);
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
            _voxelWorld.ApplyBulkChanges(placeChanges, slot);
        }

        // Remove existing weapons for this player
        if (_weapons.TryGetValue(slot, out List<WeaponBase>? existingWeapons))
        {
            foreach (WeaponBase w in existingWeapons)
            {
                if (w != null && GodotObject.IsInstanceValid(w))
                    w.QueueFree();
            }
            existingWeapons.Clear();
        }

        // Restore weapons from blueprint (with rotation)
        if (_weaponPlacer != null && blueprint.Weapons.Count > 0)
        {
            if (!_weapons.ContainsKey(slot))
                _weapons[slot] = new List<WeaponBase>();

            foreach (BlueprintWeaponData wd in blueprint.Weapons)
            {
                Vector3I absBuildPos = wd.BuildUnitPosition + zone.OriginBuildUnits;
                WeaponBase? weapon = wd.WeaponId switch
                {
                    "cannon" => _weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld, absBuildPos, slot),
                    "mortar" => _weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld, absBuildPos, slot),
                    "missile" => _weaponPlacer.PlaceWeapon<MissileLauncher>(this, _voxelWorld, absBuildPos, slot),
                    "railgun" => _weaponPlacer.PlaceWeapon<Railgun>(this, _voxelWorld, absBuildPos, slot),
                    "drill" => _weaponPlacer.PlaceWeapon<Drill>(this, _voxelWorld, absBuildPos, slot),
                    _ => null,
                };
                if (weapon != null)
                {
                    // Apply saved rotation AFTER PlaceWeapon (which auto-orients)
                    weapon.Rotation = new Vector3(0f, wd.RotationY, 0f);
                    _weapons[slot].Add(weapon);
                }
            }
        }

        // Remove existing doors for this player and restore from blueprint
        if (_armyManager != null)
        {
            _armyManager.Doors.ClearPlayerDoors(slot);
            foreach (BlueprintDoorData dd in blueprint.Doors)
            {
                Vector3I absMicro = dd.RelativeMicrovoxel + zone.OriginMicrovoxels;
                _armyManager.Doors.TryPlaceDoor(
                    _voxelWorld, absMicro, slot,
                    zone.OriginMicrovoxels, zone.MaxMicrovoxelsInclusive,
                    dd.RotationHint, out _);
            }
        }

        // Remove existing troop markers and purchased troops, then restore
        if (_armyManager != null)
        {
            // Clear existing troop markers for this player
            string markerPrefix = $"TroopMarker_{slot}_";
            foreach (Node child in GetTree().GetNodesInGroup("TroopMarkers"))
            {
                if (child.Name.ToString().StartsWith(markerPrefix))
                    child.QueueFree();
            }

            _armyManager.ClearPurchasedTroops(slot);
            foreach (BlueprintTroopData td in blueprint.Troops)
            {
                Vector3I? absPos = td.RelativeMicrovoxel.HasValue
                    ? td.RelativeMicrovoxel.Value + zone.OriginMicrovoxels
                    : null;
                _armyManager.RestoreTroop(slot, td.Type, absPos);
                if (absPos.HasValue)
                {
                    SpawnTroopMarker(absPos.Value, td.Type);
                }
            }
        }

        // Restore powerups
        if (_players.TryGetValue(slot, out PlayerData? pLoad))
        {
            pLoad.Powerups.Clear();
            foreach (PowerupType pt in blueprint.Powerups)
            {
                pLoad.Powerups.AddFree(pt);
            }
        }

        // Remove existing commander for this player
        if (_commanders.TryGetValue(slot, out CommanderActor? existingCmd) && GodotObject.IsInstanceValid(existingCmd))
        {
            existingCmd.QueueFree();
            _commanders.Remove(slot);
        }

        // Restore commander from blueprint
        if (blueprint.CommanderBuildUnitPosition.HasValue)
        {
            Vector3I absCmdPos = blueprint.CommanderBuildUnitPosition.Value + zone.OriginBuildUnits;
            CommanderActor commander = new CommanderActor();
            commander.Name = $"Commander_{slot}";
            AddChild(commander);
            commander.OwnerSlot = slot;
            commander.PlaceCommander(_voxelWorld, absCmdPos);
            commander.Rotation = new Vector3(0f, blueprint.CommanderRotationY, 0f);
            _commanders[slot] = commander;

            if (_players.TryGetValue(slot, out PlayerData? player))
            {
                player.CommanderMicrovoxelPosition = absCmdPos * GameConfig.MicrovoxelsPerBuildUnit;
                player.CommanderHealth = GameConfig.CommanderHP;
            }
        }

        GD.Print($"[Sandbox] Build '{buildName}' loaded ({placeChanges.Count} voxels, {blueprint.Weapons.Count} weapons, {blueprint.Doors.Count} doors, {blueprint.Troops.Count} troops, {blueprint.Powerups.Count} powerups, commander={blueprint.CommanderBuildUnitPosition.HasValue}).");
    }

    /// <summary>
    /// Returns the list of saved sandbox build names, cross-referencing the
    /// player profile with actual blueprint files on disk. If files exist
    /// but aren't listed in the profile, they're recovered automatically.
    /// </summary>
    public List<string> GetSavedBuildNames()
    {
        PlayerProfile? profile = _progressionManager?.Profile;
        List<string> profileBuilds = profile?.SavedBuilds ?? new List<string>();

        // Scan disk for actual blueprint files as a fallback
        if (_blueprintSystem == null)
            _blueprintSystem = new BlueprintSystem();
        List<string> diskBuilds = _blueprintSystem.ScanBlueprintFiles();

        // Recover any builds that exist on disk but are missing from the profile
        bool dirty = false;
        foreach (string diskName in diskBuilds)
        {
            if (profile != null && !profileBuilds.Contains(diskName))
            {
                GD.Print($"[Sandbox] Recovering orphaned build '{diskName}' from disk into profile");
                profileBuilds.Add(diskName);
                dirty = true;
            }
        }

        // Save the recovered entries back to disk
        if (dirty && profile != null)
        {
            SaveSystem.SaveJson("user://profile/player_profile.json", profile);
        }

        return profileBuilds;
    }

    /// <summary>
    /// Deletes a saved build by name — removes the blueprint file and the profile entry.
    /// </summary>
    public bool DeleteSandboxBuild(string buildName)
    {
        if (_blueprintSystem == null)
            _blueprintSystem = new BlueprintSystem();

        // Delete the blueprint JSON file
        string path = _blueprintSystem.MakeBlueprintPath(buildName);
        string globalPath = ProjectSettings.GlobalizePath(path);
        if (System.IO.File.Exists(globalPath))
        {
            System.IO.File.Delete(globalPath);
        }

        // Remove from player profile
        PlayerProfile? profile = _progressionManager?.Profile;
        if (profile != null && profile.SavedBuilds.Remove(buildName))
        {
            SaveSystem.SaveJson("user://profile/player_profile.json", profile);
            GD.Print($"[Sandbox] Build '{buildName}' deleted.");
            return true;
        }

        // Also try reading directly from disk if ProgressionManager isn't available
        PlayerProfile? diskProfile = SaveSystem.LoadJson<PlayerProfile>("user://profile/player_profile.json");
        if (diskProfile != null && diskProfile.SavedBuilds.Remove(buildName))
        {
            SaveSystem.SaveJson("user://profile/player_profile.json", diskProfile);
            GD.Print($"[Sandbox] Build '{buildName}' deleted (from disk profile).");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Exports the current sandbox build to a .vsbuild file at the given path.
    /// </summary>
    public void ExportSandboxBuild(string absolutePath, UI.BuildUI? buildUI)
    {
        if (!_isSandbox || _voxelWorld == null)
        {
            GD.Print("[Export] Cannot export: not in sandbox mode.");
            buildUI?.ShowExportStatus("Not in sandbox mode", new Color("d73a49"));
            return;
        }

        if (!_buildZones.TryGetValue(PlayerSlot.Player1, out BuildZone zone))
        {
            GD.Print("[Export] Cannot export: no build zone found.");
            buildUI?.ShowExportStatus("No build zone found", new Color("d73a49"));
            return;
        }

        if (_blueprintSystem == null)
        {
            _blueprintSystem = new BlueprintSystem();
        }

        // Capture fresh from the current world state
        string name = System.IO.Path.GetFileNameWithoutExtension(absolutePath);
        BlueprintData blueprint = _blueprintSystem.Capture(_voxelWorld, zone, name);

        // Capture weapons (including rotation)
        blueprint.Weapons.Clear();
        if (_weapons.TryGetValue(PlayerSlot.Player1, out List<WeaponBase>? weaponList))
        {
            foreach (WeaponBase w in weaponList)
            {
                if (w == null || !GodotObject.IsInstanceValid(w)) continue;
                Vector3I microPos = MathHelpers.WorldToMicrovoxel(w.GlobalPosition);
                Vector3I buildPos = MathHelpers.MicrovoxelToBuild(microPos);
                Vector3I relPos = buildPos - zone.OriginBuildUnits;
                blueprint.Weapons.Add(new BlueprintWeaponData
                {
                    WeaponId = w.WeaponId,
                    BuildUnitPosition = relPos,
                    RotationY = w.Rotation.Y,
                });
            }
        }

        // Capture doors
        blueprint.Doors.Clear();
        if (_armyManager != null)
        {
            var doors = _armyManager.Doors.GetDoors(PlayerSlot.Player1);
            foreach (var door in doors)
            {
                Vector3I relMicro = door.BaseMicrovoxel - zone.OriginMicrovoxels;
                int rotHint = NormalToRotationHint(door.OutwardNormal);
                blueprint.Doors.Add(new BlueprintDoorData
                {
                    RelativeMicrovoxel = relMicro,
                    RotationHint = rotHint,
                });
            }
        }

        // Capture troops
        blueprint.Troops.Clear();
        if (_armyManager != null)
        {
            var troops = _armyManager.GetPurchasedTroops(PlayerSlot.Player1);
            foreach (var t in troops)
            {
                Vector3I? relPos = t.PlacedPosition.HasValue
                    ? t.PlacedPosition.Value - zone.OriginMicrovoxels
                    : null;
                blueprint.Troops.Add(new BlueprintTroopData
                {
                    Type = t.Type,
                    RelativeMicrovoxel = relPos,
                });
            }
        }

        // Capture powerups
        blueprint.Powerups.Clear();
        if (_players.TryGetValue(PlayerSlot.Player1, out PlayerData? p1Export))
        {
            foreach (PowerupType pt in p1Export.Powerups.OwnedPowerups)
            {
                blueprint.Powerups.Add(pt);
            }
        }

        // Capture commander
        blueprint.CommanderBuildUnitPosition = null;
        if (_commanders.TryGetValue(PlayerSlot.Player1, out CommanderActor? cmd) && GodotObject.IsInstanceValid(cmd))
        {
            Vector3I relCmd = cmd.BuildUnitPosition - zone.OriginBuildUnits;
            blueprint.CommanderBuildUnitPosition = relCmd;
            blueprint.CommanderRotationY = cmd.Rotation.Y;
        }

        if (_blueprintSystem.ExportBlueprint(blueprint, absolutePath))
        {
            buildUI?.ShowExportStatus($"Exported! ({blueprint.Voxels.Count} voxels, {blueprint.Weapons.Count} weapons, {blueprint.Doors.Count} doors, {blueprint.Troops.Count} troops)", new Color("2ea043"));
        }
        else
        {
            buildUI?.ShowExportStatus("Export failed — check logs", new Color("d73a49"));
        }
    }

    /// <summary>
    /// Imports a .vsbuild file and loads it into the current sandbox build zone.
    /// </summary>
    public void ImportSandboxBuild(string absolutePath, UI.BuildUI? buildUI)
    {
        if (_voxelWorld == null)
        {
            GD.Print("[Import] Cannot import: no voxel world.");
            buildUI?.ShowExportStatus("No voxel world", new Color("d73a49"));
            return;
        }

        if (!_buildZones.TryGetValue(_isSandbox ? PlayerSlot.Player1 : _activeBuilder, out BuildZone zone))
        {
            GD.Print("[Import] Cannot import: no build zone found.");
            buildUI?.ShowExportStatus("No build zone found", new Color("d73a49"));
            return;
        }

        if (_blueprintSystem == null)
        {
            _blueprintSystem = new BlueprintSystem();
        }

        BlueprintData? blueprint = _blueprintSystem.ImportBlueprint(absolutePath);
        if (blueprint == null)
        {
            buildUI?.ShowExportStatus("Import failed — invalid or corrupted file", new Color("d73a49"));
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

        // Place the imported blueprint voxels
        var placeChanges = new List<(Vector3I Position, Voxel.Voxel NewVoxel)>();
        foreach (BlueprintVoxelData bv in blueprint.Voxels)
        {
            Vector3I worldPos = zone.OriginMicrovoxels + new Vector3I(bv.X, bv.Y, bv.Z);
            Voxel.Voxel voxel = new Voxel.Voxel(bv.Data);
            if (voxel.Material != VoxelMaterialType.Foundation)
            {
                placeChanges.Add((worldPos, voxel));
            }
        }
        if (placeChanges.Count > 0)
        {
            _voxelWorld.ApplyBulkChanges(placeChanges, PlayerSlot.Player1);
        }

        PlayerSlot slot = _isSandbox ? PlayerSlot.Player1 : _activeBuilder;

        // Remove existing weapons for this player
        if (_weapons.TryGetValue(slot, out List<WeaponBase>? existingWeapons))
        {
            foreach (WeaponBase w in existingWeapons)
            {
                if (w != null && GodotObject.IsInstanceValid(w))
                    w.QueueFree();
            }
            existingWeapons.Clear();
        }

        // Restore weapons from blueprint (with rotation)
        if (_weaponPlacer != null && blueprint.Weapons.Count > 0)
        {
            if (!_weapons.ContainsKey(slot))
                _weapons[slot] = new List<WeaponBase>();

            foreach (BlueprintWeaponData wd in blueprint.Weapons)
            {
                Vector3I absBuildPos = wd.BuildUnitPosition + zone.OriginBuildUnits;
                WeaponBase? weapon = wd.WeaponId switch
                {
                    "cannon" => _weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld, absBuildPos, slot),
                    "mortar" => _weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld, absBuildPos, slot),
                    "missile" => _weaponPlacer.PlaceWeapon<MissileLauncher>(this, _voxelWorld, absBuildPos, slot),
                    "railgun" => _weaponPlacer.PlaceWeapon<Railgun>(this, _voxelWorld, absBuildPos, slot),
                    "drill" => _weaponPlacer.PlaceWeapon<Drill>(this, _voxelWorld, absBuildPos, slot),
                    _ => null,
                };
                if (weapon != null)
                {
                    // Apply saved rotation AFTER PlaceWeapon (which auto-orients)
                    weapon.Rotation = new Vector3(0f, wd.RotationY, 0f);
                    _weapons[slot].Add(weapon);
                }
            }
        }

        // Remove existing doors for this player and restore from blueprint
        if (_armyManager != null)
        {
            _armyManager.Doors.ClearPlayerDoors(slot);
            foreach (BlueprintDoorData dd in blueprint.Doors)
            {
                Vector3I absMicro = dd.RelativeMicrovoxel + zone.OriginMicrovoxels;
                _armyManager.Doors.TryPlaceDoor(
                    _voxelWorld, absMicro, slot,
                    zone.OriginMicrovoxels, zone.MaxMicrovoxelsInclusive,
                    dd.RotationHint, out _);
            }
        }

        // Remove existing troop markers and purchased troops, then restore
        if (_armyManager != null)
        {
            string markerPrefix = $"TroopMarker_{slot}_";
            foreach (Node child in GetTree().GetNodesInGroup("TroopMarkers"))
            {
                if (child.Name.ToString().StartsWith(markerPrefix))
                    child.QueueFree();
            }

            _armyManager.ClearPurchasedTroops(slot);
            foreach (BlueprintTroopData td in blueprint.Troops)
            {
                Vector3I? absPos = td.RelativeMicrovoxel.HasValue
                    ? td.RelativeMicrovoxel.Value + zone.OriginMicrovoxels
                    : null;
                _armyManager.RestoreTroop(slot, td.Type, absPos);
                if (absPos.HasValue)
                {
                    SpawnTroopMarker(absPos.Value, td.Type);
                }
            }
        }

        // Restore powerups
        if (_players.TryGetValue(slot, out PlayerData? pImport))
        {
            pImport.Powerups.Clear();
            foreach (PowerupType pt in blueprint.Powerups)
            {
                pImport.Powerups.AddFree(pt);
            }
        }

        // Remove existing commander for this player
        if (_commanders.TryGetValue(slot, out CommanderActor? existingCmd) && GodotObject.IsInstanceValid(existingCmd))
        {
            existingCmd.QueueFree();
            _commanders.Remove(slot);
        }

        // Restore commander from blueprint
        if (blueprint.CommanderBuildUnitPosition.HasValue)
        {
            Vector3I absCmdPos = blueprint.CommanderBuildUnitPosition.Value + zone.OriginBuildUnits;
            CommanderActor commander = new CommanderActor();
            commander.Name = $"Commander_{slot}";
            AddChild(commander);
            commander.OwnerSlot = slot;
            commander.PlaceCommander(_voxelWorld, absCmdPos);
            commander.Rotation = new Vector3(0f, blueprint.CommanderRotationY, 0f);
            _commanders[slot] = commander;

            if (_players.TryGetValue(slot, out PlayerData? player))
            {
                player.CommanderMicrovoxelPosition = absCmdPos * GameConfig.MicrovoxelsPerBuildUnit;
                player.CommanderHealth = GameConfig.CommanderHP;
            }
        }

        // Also save to local blueprints and profile so it appears in the build list
        _blueprintSystem.SaveBlueprint(blueprint);
        PlayerProfile? profile = _progressionManager?.Profile;
        if (profile != null)
        {
            if (!profile.SavedBuilds.Contains(blueprint.Name))
            {
                profile.SavedBuilds.Add(blueprint.Name);
            }
            SaveSystem.SaveJson("user://profile/player_profile.json", profile);
        }

        buildUI?.ShowExportStatus($"Imported '{blueprint.Name}' ({placeChanges.Count} voxels, {blueprint.Weapons.Count} weapons)", new Color("2ea043"));
        GD.Print($"[Import] Build '{blueprint.Name}' imported and loaded ({placeChanges.Count} voxels, {blueprint.Weapons.Count} weapons, {blueprint.Doors.Count} doors, {blueprint.Troops.Count} troops, {blueprint.Powerups.Count} powerups, commander={blueprint.CommanderBuildUnitPosition.HasValue}).");
    }

    /// <summary>
    /// Called from the menu UI to start a match. Shows a splash/loading screen first,
    /// then proceeds with the full flow: Menu -> Build -> FogReveal -> Combat -> GameOver.
    /// </summary>
    public void StartPrototypeMatch()
    {
        // Guard: don't start a new match if one is already in progress or loading
        if (CurrentPhase != GamePhase.Menu && CurrentPhase != GamePhase.GameOver
            && CurrentPhase != GamePhase.Lobby)
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

        // Initialize army manager with world reference and build zones for troop deployment
        if (_armyManager != null && _voxelWorld != null)
        {
            _armyManager.Initialize(_voxelWorld);
            _armyManager.SetBuildZones(_buildZones);
        }

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

        // In online mode, each player builds simultaneously in their own zone.
        if (_networkManager != null && _networkManager.IsOnline)
        {
            _localBuildReady = false;
            _remoteBuildSnapshots.Clear();
            PlayerSlot localSlot = GetLocalPlayerSlot();
            _activeBuilderIndex = Array.IndexOf(BuildOrder, localSlot);
            _activeBuilder = localSlot;
            GD.Print($"[Online] Active builder set to {localSlot} (index {_activeBuilderIndex}) for local peer {_networkManager.LocalPeerId}");
        }
        else
        {
            _activeBuilderIndex = 0;
            _activeBuilder = BuildOrder[_activeBuilderIndex];
        }
        PositionCameraAtBuildZone(_activeBuilder);

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
            buildUI?.EnableSandboxMode(GetSavedBuildNames(), _sandboxLoadBuildName);

            // Auto-load a saved build if one was selected from the slot menu
            if (!string.IsNullOrEmpty(_sandboxLoadBuildName))
            {
                // Deferred so the voxel world is fully initialized
                CallDeferred(nameof(DeferredSandboxLoad));
            }
        }
    }

    private void DeferredSandboxLoad()
    {
        if (!string.IsNullOrEmpty(_sandboxLoadBuildName))
        {
            LoadSandboxBuild(_sandboxLoadBuildName!);
            _sandboxLoadBuildName = null;
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

        // Catch-all: free any Commander children that might have escaped the dict
        // (e.g. if a commander was created but the dict entry was overwritten)
        foreach (Node child in GetChildren())
        {
            if (child is CommanderActor stray && GodotObject.IsInstanceValid(stray))
            {
                stray.QueueFree();
            }
        }

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

        // Reset pending game-over state so it doesn't bleed into the next match
        _pendingGameOver = false;
        _pendingWinner = null;

        // Reset multiplayer sync state
        _localBuildReady = false;
        _remoteBuildSnapshots.Clear();

        // Reset turn manager
        _turnManager?.StopTurnClock();

        // Reset aiming and placement state, release mouse if captured
        _isAiming = false;
        _selectedWeaponIndex = -1; // No weapon selected until player clicks one
        _placementMode = PlacementMode.Block;
        _weaponPreviewMeshes.Clear();
        _commanderPreviewMesh = null;
        _troopPreviewMeshes.Clear();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Hide ghost preview
        _ghostPreview?.Hide();

        // Clean up all powerup FX (smoke clouds, shields, EMP) so they don't persist to menu
        _powerupExecutor?.CleanupAllFX();

        // Clean up army manager (troops, doors) so they don't carry over between matches
        _armyManager?.ClearAll();

        // Clean up troop markers
        foreach (Node child in GetTree().GetNodesInGroup("TroopMarkers"))
        {
            child.QueueFree();
        }

        // Restore normal time scale (commander death slow-mo may still be active
        // if the user quits during the kill cam before the async reset fires)
        Engine.TimeScale = 1.0;
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

        // Always clear sandbox flag so the next match doesn't think it's sandbox
        _isSandbox = false;

        // Clean up sandbox UI if it's still showing
        BuildUI? sandboxBuildUI = GetNodeOrNull<BuildUI>("BuildHUD")
            ?? GetNodeOrNull<BuildUI>("%BuildUI")
            ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
        sandboxBuildUI?.DisableSandboxMode();

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

        // Clean up sandbox UI so it doesn't persist into the next game
        BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
            ?? GetNodeOrNull<BuildUI>("%BuildUI")
            ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
        buildUI?.DisableSandboxMode();

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

    // ─────────────────────────────────────────────────
    //  MULTIPLAYER SYNC HANDLERS
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the PlayerSlot assigned to the local machine's peer ID.
    /// Falls back to Player1 if offline or not found.
    /// </summary>
    private PlayerSlot GetLocalPlayerSlot()
    {
        if (_networkManager == null || !_networkManager.IsOnline)
            return PlayerSlot.Player1;

        long localPeer = _networkManager.LocalPeerId;
        foreach ((PlayerSlot slot, PlayerData data) in _players)
        {
            if (data.PeerId == localPeer)
            {
                GD.Print($"[Online] GetLocalPlayerSlot: peer {localPeer} → {slot}");
                return slot;
            }
        }
        GD.PrintErr($"[Online] GetLocalPlayerSlot: peer {localPeer} NOT found in _players ({_players.Count} entries). Falling back to Player1.");
        foreach ((PlayerSlot slot, PlayerData data) in _players)
        {
            GD.PrintErr($"  → {slot}: PeerId={data.PeerId}, Name={data.DisplayName}");
        }
        return PlayerSlot.Player1;
    }

    /// <summary>
    /// Captures the current build state for a player as BlueprintData JSON.
    /// Generalizes the SaveSandboxBuild logic for any player slot.
    /// </summary>
    private string CapturePlayerBuildJson(PlayerSlot slot)
    {
        if (_voxelWorld == null || !_buildZones.TryGetValue(slot, out BuildZone zone))
            return "{}";

        if (_blueprintSystem == null) _blueprintSystem = new BlueprintSystem();

        BlueprintData blueprint = _blueprintSystem.Capture(_voxelWorld, zone, $"online_{slot}");

        // Capture weapons
        blueprint.Weapons.Clear();
        if (_weapons.TryGetValue(slot, out List<WeaponBase>? weaponList))
        {
            foreach (WeaponBase w in weaponList)
            {
                if (w == null || !GodotObject.IsInstanceValid(w)) continue;
                Vector3I microPos = MathHelpers.WorldToMicrovoxel(w.GlobalPosition);
                Vector3I buildPos = MathHelpers.MicrovoxelToBuild(microPos);
                Vector3I relPos = buildPos - zone.OriginBuildUnits;
                blueprint.Weapons.Add(new BlueprintWeaponData
                {
                    WeaponId = w.WeaponId,
                    BuildUnitPosition = relPos,
                    RotationY = w.Rotation.Y,
                });
            }
        }

        // Capture doors
        blueprint.Doors.Clear();
        if (_armyManager != null)
        {
            var doors = _armyManager.Doors.GetDoors(slot);
            foreach (var door in doors)
            {
                Vector3I relMicro = door.BaseMicrovoxel - zone.OriginMicrovoxels;
                int rotHint = NormalToRotationHint(door.OutwardNormal);
                blueprint.Doors.Add(new BlueprintDoorData
                {
                    RelativeMicrovoxel = relMicro,
                    RotationHint = rotHint,
                });
            }
        }

        // Capture troops
        blueprint.Troops.Clear();
        if (_armyManager != null)
        {
            var troops = _armyManager.GetPurchasedTroops(slot);
            foreach (var t in troops)
            {
                Vector3I? relPos = t.PlacedPosition.HasValue
                    ? t.PlacedPosition.Value - zone.OriginMicrovoxels
                    : null;
                blueprint.Troops.Add(new BlueprintTroopData
                {
                    Type = t.Type,
                    RelativeMicrovoxel = relPos,
                });
            }
        }

        // Capture powerups
        blueprint.Powerups.Clear();
        if (_players.TryGetValue(slot, out PlayerData? pSave))
        {
            foreach (PowerupType pt in pSave.Powerups.OwnedPowerups)
            {
                blueprint.Powerups.Add(pt);
            }
        }

        // Capture commander
        blueprint.CommanderBuildUnitPosition = null;
        if (_commanders.TryGetValue(slot, out CommanderActor? cmd) && GodotObject.IsInstanceValid(cmd))
        {
            Vector3I relCmd = cmd.BuildUnitPosition - zone.OriginBuildUnits;
            blueprint.CommanderBuildUnitPosition = relCmd;
            blueprint.CommanderRotationY = cmd.Rotation.Y;
        }

        string json = System.Text.Json.JsonSerializer.Serialize(blueprint, new System.Text.Json.JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
        GD.Print($"[Online] Captured build for {slot}: {blueprint.Voxels.Count} voxels, {blueprint.Weapons.Count} weapons, commander={blueprint.CommanderBuildUnitPosition.HasValue}");
        return json;
    }

    /// <summary>
    /// Applies a remote player's build snapshot to their build zone.
    /// Reuses the same logic as LoadSandboxBuild.
    /// </summary>
    private void ApplyRemoteBuild(PlayerSlot slot, string blueprintJson)
    {
        if (_voxelWorld == null || !_buildZones.TryGetValue(slot, out BuildZone zone))
        {
            GD.PrintErr($"[Online] Cannot apply remote build for {slot}: no world or zone.");
            return;
        }

        BlueprintData? blueprint = System.Text.Json.JsonSerializer.Deserialize<BlueprintData>(
            blueprintJson, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        if (blueprint == null)
        {
            GD.PrintErr($"[Online] Failed to deserialize remote build for {slot}.");
            return;
        }

        // Place voxels
        var placeChanges = new List<(Vector3I Position, Voxel.Voxel NewVoxel)>();
        foreach (BlueprintVoxelData bv in blueprint.Voxels)
        {
            Vector3I worldPos = zone.OriginMicrovoxels + new Vector3I(bv.X, bv.Y, bv.Z);
            Voxel.Voxel voxel = new Voxel.Voxel(bv.Data);
            if (voxel.Material != VoxelMaterialType.Foundation)
            {
                placeChanges.Add((worldPos, voxel));
            }
        }
        if (placeChanges.Count > 0)
        {
            _voxelWorld.ApplyBulkChanges(placeChanges, slot);
        }

        // Restore weapons
        if (_weaponPlacer != null && blueprint.Weapons.Count > 0)
        {
            if (!_weapons.ContainsKey(slot))
                _weapons[slot] = new List<WeaponBase>();

            foreach (BlueprintWeaponData wd in blueprint.Weapons)
            {
                Vector3I absBuildPos = wd.BuildUnitPosition + zone.OriginBuildUnits;
                WeaponBase? weapon = wd.WeaponId switch
                {
                    "cannon" => _weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld, absBuildPos, slot),
                    "mortar" => _weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld, absBuildPos, slot),
                    "missile" => _weaponPlacer.PlaceWeapon<MissileLauncher>(this, _voxelWorld, absBuildPos, slot),
                    "railgun" => _weaponPlacer.PlaceWeapon<Railgun>(this, _voxelWorld, absBuildPos, slot),
                    "drill" => _weaponPlacer.PlaceWeapon<Drill>(this, _voxelWorld, absBuildPos, slot),
                    _ => null,
                };
                if (weapon != null)
                {
                    weapon.Rotation = new Vector3(0f, wd.RotationY, 0f);
                    _weapons[slot].Add(weapon);
                }
            }
        }

        // Restore doors
        if (_armyManager != null)
        {
            foreach (BlueprintDoorData dd in blueprint.Doors)
            {
                Vector3I absMicro = dd.RelativeMicrovoxel + zone.OriginMicrovoxels;
                _armyManager.Doors.TryPlaceDoor(
                    _voxelWorld, absMicro, slot,
                    zone.OriginMicrovoxels, zone.MaxMicrovoxelsInclusive,
                    dd.RotationHint, out _);
            }
        }

        // Restore troops
        if (_armyManager != null)
        {
            foreach (BlueprintTroopData td in blueprint.Troops)
            {
                Vector3I? absPos = td.RelativeMicrovoxel.HasValue
                    ? td.RelativeMicrovoxel.Value + zone.OriginMicrovoxels
                    : null;
                _armyManager.RestoreTroop(slot, td.Type, absPos);
            }
        }

        // Restore powerups
        if (_players.TryGetValue(slot, out PlayerData? pLoad))
        {
            pLoad.Powerups.Clear();
            foreach (PowerupType pt in blueprint.Powerups)
            {
                pLoad.Powerups.AddFree(pt);
            }
        }

        // Restore commander
        if (blueprint.CommanderBuildUnitPosition.HasValue)
        {
            if (_commanders.TryGetValue(slot, out CommanderActor? existingCmd) && GodotObject.IsInstanceValid(existingCmd))
            {
                existingCmd.QueueFree();
                _commanders.Remove(slot);
            }

            Vector3I absCmdPos = blueprint.CommanderBuildUnitPosition.Value + zone.OriginBuildUnits;
            CommanderActor commander = new CommanderActor();
            commander.Name = $"Commander_{slot}";
            AddChild(commander);
            commander.OwnerSlot = slot;
            commander.PlaceCommander(_voxelWorld, absCmdPos);
            commander.Rotation = new Vector3(0f, blueprint.CommanderRotationY, 0f);
            _commanders[slot] = commander;

            if (_players.TryGetValue(slot, out PlayerData? player))
            {
                player.CommanderMicrovoxelPosition = absCmdPos * GameConfig.MicrovoxelsPerBuildUnit;
                player.CommanderHealth = GameConfig.CommanderHP;
            }
        }

        GD.Print($"[Online] Applied remote build for {slot}: {blueprint.Voxels.Count} voxels, {blueprint.Weapons.Count} weapons");
    }

    /// <summary>
    /// Called when a remote player's build snapshot arrives via RPC.
    /// </summary>
    private void OnRemoteBuildComplete(BuildCompleteSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        GD.Print($"[Online] Received build snapshot from {slot}");
        _remoteBuildSnapshots[slot] = payload.BlueprintJson;
        CheckAllPlayersReadyForCombat();
    }

    /// <summary>
    /// Checks if all players have submitted their builds. If so, applies remote builds
    /// and starts combat.
    /// </summary>
    private void CheckAllPlayersReadyForCombat()
    {
        if (!_localBuildReady) return;

        PlayerSlot localSlot = GetLocalPlayerSlot();
        foreach (PlayerSlot slot in _players.Keys)
        {
            if (slot == localSlot) continue; // We already have our own build
            if (!_remoteBuildSnapshots.ContainsKey(slot))
            {
                GD.Print($"[Online] Still waiting for build from {slot}");
                return;
            }
        }

        // All builds received — apply remote snapshots and start combat
        GD.Print("[Online] All players ready! Applying remote builds and starting combat...");
        foreach ((PlayerSlot slot, string json) in _remoteBuildSnapshots)
        {
            ApplyRemoteBuild(slot, json);
        }

        StartCombatCountdown();
    }

    /// <summary>
    /// Called when a remote player fires a weapon. Replays the fire locally.
    /// </summary>
    private void OnRemoteWeaponFire(WeaponFireSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        if (IsLocalPlayer(slot)) return; // Ignore our own echoes

        if (_voxelWorld == null) return;
        if (!_weapons.TryGetValue(slot, out List<WeaponBase>? weaponList) || weaponList.Count == 0) return;

        // Find weapon by position (robust against list order divergence between peers)
        Vector3 senderWeaponPos = new Vector3(payload.WeaponPosX, payload.WeaponPosY, payload.WeaponPosZ);
        WeaponBase? weapon = null;
        float bestDist = float.MaxValue;
        foreach (WeaponBase w in weaponList)
        {
            if (w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed) continue;
            float dist = w.GlobalPosition.DistanceTo(senderWeaponPos);
            if (dist < bestDist)
            {
                bestDist = dist;
                weapon = w;
            }
        }
        if (weapon == null || bestDist > 3f)
        {
            GD.PrintErr($"[Online] Could not find matching weapon for remote fire at {senderWeaponPos} (bestDist={bestDist:F1})");
            return;
        }

        Vector3 launchVelocity = new Vector3(payload.VelocityX, payload.VelocityY, payload.VelocityZ);
        int round = _turnManager?.RoundNumber ?? 0;
        // Use sender's weapon position so the projectile starts at the exact same
        // world position on both peers — prevents trajectory divergence even if
        // local weapon positions have drifted slightly.
        ProjectileBase? projectile = weapon.FireRemote(launchVelocity, _voxelWorld, round, senderWeaponPos);

        GD.Print($"[Online] Replayed weapon fire from {slot}: {weapon.WeaponId} at {weapon.GlobalPosition}, velocity={launchVelocity}");

        // Track stats for the remote player
        if (_players.TryGetValue(slot, out PlayerData? remotePlayer))
            remotePlayer.Stats.ShotsFired++;

        // Hide attack UI and skip button during remote fire (same as local fire)
        {
            CombatUI? combatUI2 = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            combatUI2?.HideAttackUI();
        }
        if (_skipTurnButton != null) _skipTurnButton.Visible = false;

        // Set up troop sequence deferral same as local fire path
        _deferTurnAdvanceForTroops = _armyManager?.HasAliveTroops(slot) ?? false;
        _troopSequencePlayer = slot;

        // Follow the remote projectile with the combat camera
        if (_combatCamera != null && projectile != null && GodotObject.IsInstanceValid(projectile))
        {
            _camera?.Deactivate();
            _combatCamera.FollowProjectile(projectile);
        }

        // HOST: wait for projectile impact then advance the turn
        // (Client fires → host must advance on their behalf since client waits for broadcast)
        if (_networkManager?.IsHost == true && projectile != null)
        {
            WaitForProjectileThenAdvance(projectile, slot);
        }
        else if (_networkManager?.IsHost == true)
        {
            // Hitscan — advance after a short delay
            PlayerSlot firedSlot = slot;
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == firedSlot)
                    AdvanceTurnAuthoritative();
            };
        }
    }

    /// <summary>
    /// Called when a remote player skips their turn.
    /// </summary>
    private void OnRemoteSkipTurn(SkipTurnSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        if (IsLocalPlayer(slot)) return; // Ignore our own echoes

        GD.Print($"[Online] Remote player {slot} skipped turn");

        // Only the host processes skip requests — it will advance and broadcast
        if (_networkManager?.IsHost != true) return;

        GetTree().CreateTimer(0.0).Timeout += () =>
        {
            if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer != slot)
                return;
            AdvanceTurnAuthoritative();
        };
    }

    /// <summary>
    /// Called when the host broadcasts the turn order for combat.
    /// </summary>
    private void OnRemoteTurnOrder(TurnOrderSyncPayload payload)
    {
        if (_networkManager?.IsHost == true) return; // Host already has the order

        List<PlayerSlot> order = new();
        foreach (int slotIndex in payload.SlotOrder)
            order.Add((PlayerSlot)slotIndex);

        GD.Print($"[Online] Received turn order from host: {string.Join(", ", order)}");
        _turnManager?.ConfigureWithOrder(order, Settings.TurnTimeSeconds);
        _turnManager?.StopTurnClock(); // Don't tick during countdown
    }

    /// <summary>
    /// Host-authoritative turn advance. In multiplayer, only the host
    /// actually advances the TurnManager. Clients wait for the broadcast.
    /// </summary>
    private void AdvanceTurnAuthoritative()
    {
        if (_turnManager == null) return;

        // Offline / local: advance directly
        if (_networkManager == null || !_networkManager.IsOnline)
        {
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // Online client: do nothing — wait for host's TurnAdvance broadcast
        if (!_networkManager.IsHost)
            return;

        // Online host: advance locally + broadcast to clients
        _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
        if (_turnManager.CurrentPlayer is PlayerSlot newPlayer)
        {
            _syncManager?.SendTurnAdvance(newPlayer, _turnManager.RoundNumber, Settings.TurnTimeSeconds);
        }
    }

    /// <summary>
    /// Called when the host broadcasts a turn advance to clients.
    /// </summary>
    private void OnRemoteTurnAdvance(TurnAdvanceSyncPayload payload)
    {
        if (_networkManager?.IsHost == true) return; // Host already advanced locally

        PlayerSlot player = (PlayerSlot)payload.CurrentPlayerSlotIndex;
        GD.Print($"[Online] Host advanced turn to {player} (round {payload.RoundNumber})");
        _turnManager?.SyncToState(player, payload.RoundNumber, payload.TurnTimeSeconds);
    }

    /// <summary>
    /// Called when a remote player moves their troops.
    /// </summary>
    private void OnRemoteTroopMove(TroopMoveSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        if (IsLocalPlayer(slot)) return;

        Vector3I target = new Vector3I(payload.TargetX, payload.TargetY, payload.TargetZ);
        GD.Print($"[Online] Remote player {slot} moved troops to {target}");
        _armyManager?.MoveTroopsToward(slot, target);
    }

    /// <summary>
    /// Called when a remote player activates a powerup.
    /// Replays the powerup activation locally.
    /// </summary>
    private void OnRemotePowerupUsed(PowerupUsedSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        if (IsLocalPlayer(slot)) return;

        if (!_players.TryGetValue(slot, out PlayerData? player) || _powerupExecutor == null)
            return;

        PowerupType type = (PowerupType)payload.PowerupTypeId;
        int alivePlayerCount = _players.Values.Count(p => p.IsAlive);

        GD.Print($"[Online] Remote player {slot} used powerup {type}");

        bool success = false;
        switch (type)
        {
            case PowerupType.SmokeScreen:
                success = _powerupExecutor.ActivateSmokeScreen(player, alivePlayerCount);
                if (success) _powerupExecutor.ReenforceSmokeScreens(_players, _commanders);
                break;
            case PowerupType.Medkit:
                success = _powerupExecutor.ActivateMedkit(player);
                break;
            case PowerupType.ShieldGenerator:
                success = _powerupExecutor.ActivateShieldGenerator(player, alivePlayerCount);
                break;
            case PowerupType.AirstrikeBeacon:
                // Airstrike is non-deterministic (random impact positions).
                // The activating client sends the result via AirstrikeResultReceived.
                // Don't activate locally here; wait for that RPC.
                break;
            case PowerupType.EmpBlast:
                // EMP is non-deterministic — the activating client sends the result separately
                // via EmpResultReceived. Don't activate locally here; wait for that RPC.
                break;
        }

        if (success)
        {
            CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            if (_turnManager?.CurrentPlayer == slot)
                combatUI?.UpdatePowerupSlots(player.Powerups);
        }
    }

    /// <summary>
    /// Called when a remote EMP result is received. Applies the exact same weapon
    /// disables that the activating client rolled, ensuring deterministic state.
    /// </summary>
    private void OnRemoteEmpResult(EmpResultSyncPayload payload)
    {
        PlayerSlot activator = (PlayerSlot)payload.ActivatorSlotIndex;
        if (IsLocalPlayer(activator)) return; // We already applied it locally

        if (!_players.TryGetValue(activator, out PlayerData? activatorData) || _powerupExecutor == null)
            return;

        // Consume the EMP from inventory on the remote side
        activatorData.Powerups.TryConsume(PowerupType.EmpBlast);

        GD.Print($"[Online] Applying remote EMP result from {activator}: {payload.DisabledWeaponIndices.Length} weapons");

        for (int i = 0; i < payload.DisabledWeaponIndices.Length && i < payload.DisabledWeaponOwnerSlots.Length; i++)
        {
            PlayerSlot ownerSlot = (PlayerSlot)payload.DisabledWeaponOwnerSlots[i];
            int weaponIdx = payload.DisabledWeaponIndices[i];

            if (!_weapons.TryGetValue(ownerSlot, out List<WeaponBase>? weaponList)) continue;

            // Build same alive-weapons list
            List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
                w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
            if (weaponIdx < 0 || weaponIdx >= aliveWeapons.Count) continue;

            WeaponBase weapon = aliveWeapons[weaponIdx];
            var empData = new PowerupExecutor.EmpData(weapon.GetInstanceId(), weapon.WeaponId, ownerSlot);
            activatorData.Powerups.AddActiveEffect(PowerupType.EmpBlast, activator, 2, empData);
            PowerupFX.SpawnEmpEffect(GetTree().Root, weapon.GlobalPosition);
            GD.Print($"[Online] EMP disabled {weapon.WeaponId} on {ownerSlot}");
        }
    }

    /// <summary>
    /// Captures the current EMP result (which weapons were disabled) and sends
    /// it to remote clients so they apply the exact same disables.
    /// </summary>
    private void SendEmpResultToRemote(PlayerSlot activator)
    {
        if (!_players.TryGetValue(activator, out PlayerData? activatorData)) return;

        List<int> ownerSlots = new();
        List<int> weaponIndices = new();

        foreach (ActivePowerup effect in activatorData.Powerups.GetActiveEffects(PowerupType.EmpBlast))
        {
            if (effect.TargetData is PowerupExecutor.EmpData emp)
            {
                // Find the weapon's index in the owner's alive-weapons list
                if (!_weapons.TryGetValue(emp.TargetPlayer, out List<WeaponBase>? weaponList)) continue;
                List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
                    w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);

                for (int i = 0; i < aliveWeapons.Count; i++)
                {
                    if (aliveWeapons[i].GetInstanceId() == emp.WeaponInstanceId)
                    {
                        ownerSlots.Add((int)emp.TargetPlayer);
                        weaponIndices.Add(i);
                        break;
                    }
                }
            }
        }

        _syncManager?.SendEmpResult(activator, ownerSlots.ToArray(), weaponIndices.ToArray());
    }

    /// <summary>
    /// Called when a remote airstrike result is received. Replays the airstrike
    /// with the exact impact positions computed by the activating client.
    /// </summary>
    private void OnRemoteAirstrikeResult(AirstrikeResultSyncPayload payload)
    {
        PlayerSlot slot = (PlayerSlot)payload.PlayerSlotIndex;
        if (IsLocalPlayer(slot)) return; // We already activated it locally

        if (!_players.TryGetValue(slot, out PlayerData? player) || _powerupExecutor == null)
            return;

        PlayerSlot targetEnemy = (PlayerSlot)payload.TargetEnemySlotIndex;

        // Reconstruct impact positions
        int count = Math.Min(payload.ImpactXs.Length, Math.Min(payload.ImpactYs.Length, payload.ImpactZs.Length));
        Vector3[] impacts = new Vector3[count];
        for (int i = 0; i < count; i++)
            impacts[i] = new Vector3(payload.ImpactXs[i], payload.ImpactYs[i], payload.ImpactZs[i]);

        if (!_players.TryGetValue(targetEnemy, out PlayerData? enemy) || !enemy.IsAlive)
            return;

        BuildZone enemyZone = enemy.AssignedBuildZone;
        Vector3I target = enemyZone.OriginBuildUnits + enemyZone.SizeBuildUnits / 2;
        bool success = _powerupExecutor.ActivateAirstrikeRemote(player, target, targetEnemy, impacts, payload.PlaneCount);
        if (success)
        {
            player.AirstrikesUsedThisRound++;
            CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            if (_turnManager?.CurrentPlayer == slot)
                combatUI?.UpdatePowerupSlots(player.Powerups);
        }

        GD.Print($"[Online] Replayed remote airstrike from {slot}: {count} impacts, {payload.PlaneCount} planes");
    }

    /// <summary>
    /// Called when a remote game over event is received.
    /// </summary>
    private void OnRemoteGameOver(GameOverSyncPayload payload)
    {
        if (CurrentPhase == GamePhase.GameOver) return; // Already handled

        PlayerSlot? winner = payload.WinnerSlotIndex >= 0 ? (PlayerSlot)payload.WinnerSlotIndex : null;
        GD.Print($"[Online] Remote game over: winner={winner}");

        // Mark non-winners as eliminated
        if (winner.HasValue)
        {
            foreach ((PlayerSlot slot, PlayerData playerData) in _players)
            {
                if (slot != winner.Value && playerData.IsAlive)
                    playerData.IsAlive = false;
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
        if (winner.HasValue && IsLocalPlayer(winner.Value)
            && _players.TryGetValue(winner.Value, out PlayerData? wp))
        {
            _progressionManager?.CommitMatchEarnings(wp.Stats.MatchEarnings);
        }
    }

    /// <summary>
    /// Called when the host broadcasts a commander death (authoritative).
    /// If the local peer hasn't killed this commander yet, force the death.
    /// </summary>
    private void OnRemoteCommanderDeath(CommanderDeathSyncPayload payload)
    {
        if (_networkManager?.IsHost == true) return; // Host already processed it locally

        PlayerSlot victim = (PlayerSlot)payload.VictimSlotIndex;
        if (_players.TryGetValue(victim, out PlayerData? player) && player.IsAlive)
        {
            GD.Print($"[Online] Host says {victim} commander died — forcing death on client");
            PlayerSlot? killerSlot = payload.KillerSlotIndex >= 0 ? (PlayerSlot)payload.KillerSlotIndex : null;
            Vector3 deathPos = new Vector3(payload.PosX, payload.PosY, payload.PosZ);

            // Set flag so OnCommanderKilled processes this host-authoritative kill
            _processingHostCommanderDeath = true;
            EventBus.Instance?.EmitCommanderKilled(
                new CommanderKilledEvent(victim, killerSlot, deathPos));
            _processingHostCommanderDeath = false;
        }
    }

    /// <summary>
    /// Called on both peers when an explosion applies voxel damage.
    /// The host serializes the changes and broadcasts them to clients so both
    /// peers have identical voxel world state after each explosion.
    /// </summary>
    private void OnExplosionVoxelDamage(List<(Vector3I Position, VoxelSiege.Voxel.Voxel NewVoxel)> changes)
    {
        if (_networkManager?.IsOnline != true || !_networkManager.IsHost) return;
        if (_syncManager == null || changes.Count == 0) return;

        byte[] data = _syncManager.SerializeVoxelDelta(changes);
        _syncManager.SendVoxelDamage(data);
    }

    /// <summary>
    /// Client receives authoritative voxel damage from the host and applies it,
    /// overwriting any local discrepancies so both peers see identical destruction.
    /// </summary>
    private void OnRemoteVoxelDamage(byte[] data)
    {
        if (_networkManager?.IsHost == true) return; // Host already applied
        if (_voxelWorld == null || _syncManager == null) return;

        _syncManager.ApplyVoxelDelta(_voxelWorld, data);
    }

    /// <summary>
    /// Called when a remote disconnect notification is received.
    /// </summary>
    private void OnRemoteDisconnect(DisconnectSyncPayload payload)
    {
        PlayerSlot disconnected = (PlayerSlot)payload.DisconnectedSlotIndex;
        GD.Print($"[Online] Received disconnect notification for {disconnected}");
        HandleMidGameDisconnect(disconnected);
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

        // Handle mid-game disconnects: find which player slot was this peer
        if (CurrentPhase == GamePhase.Combat || CurrentPhase == GamePhase.Building || CurrentPhase == GamePhase.FogReveal)
        {
            PlayerSlot? disconnectedSlot = null;
            foreach ((PlayerSlot slot, PlayerData data) in _players)
            {
                if (data.PeerId == peerId)
                {
                    disconnectedSlot = slot;
                    break;
                }
            }

            if (disconnectedSlot.HasValue)
            {
                GD.Print($"[Online] Player {disconnectedSlot.Value} disconnected mid-game!");
                HandleMidGameDisconnect(disconnectedSlot.Value);
            }
        }
    }

    /// <summary>
    /// Handles a player disconnecting mid-game. Marks them as dead and
    /// checks if the game should end.
    /// </summary>
    private void HandleMidGameDisconnect(PlayerSlot disconnected)
    {
        if (_players.TryGetValue(disconnected, out PlayerData? data))
        {
            data.IsAlive = false;
            data.CommanderHealth = 0;
        }

        // Surrender their troops
        _armyManager?.SurrenderTroops(disconnected);

        // Remove from turn order
        _turnManager?.RemovePlayer(disconnected, Settings.TurnTimeSeconds);

        // Check if only one player remains
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

        if (aliveCount <= 1 && CurrentPhase == GamePhase.Combat)
        {
            GD.Print($"[Online] Only {aliveCount} player(s) remaining after disconnect. Game over.");
            _artilleryDominanceActive = false;
            _turnManager?.StopTurnClock();
            Engine.TimeScale = 1.0;
            SetPhase(GamePhase.GameOver);

            foreach ((PlayerSlot slot, PlayerData _) in _players)
            {
                _progressionManager?.AwardMatchCompleted(winner.HasValue && winner.Value == slot);
            }
            if (winner.HasValue && IsLocalPlayer(winner.Value)
                && _players.TryGetValue(winner.Value, out PlayerData? wp))
            {
                _progressionManager?.CommitMatchEarnings(wp.Stats.MatchEarnings);
            }
        }
        else if (CurrentPhase == GamePhase.Building)
        {
            // If we're still in build phase, check if all remaining players are ready
            CheckAllPlayersReadyForCombat();
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
                IsBot = false, // All online players are human
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

        // In online mode, each player builds simultaneously in their own zone.
        // Set _activeBuilder to the LOCAL player's slot.
        if (_networkManager != null && _networkManager.IsOnline)
        {
            _localBuildReady = false;
            _remoteBuildSnapshots.Clear();
            PlayerSlot localSlot = GetLocalPlayerSlot();
            _activeBuilderIndex = Array.IndexOf(BuildOrder, localSlot);
            _activeBuilder = localSlot;
        }
        else
        {
            // Local mode: start with Player1 (sequential builder rotation)
            _activeBuilderIndex = 0;
            _activeBuilder = BuildOrder[_activeBuilderIndex];
        }

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
                _buildUndoStack.Clear();
                _buildSystem?.ClearHistory();
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
                // Initialize powerup counts in the BuildUI for the local player
                PlayerSlot localBuildSlot = GetLocalPlayerSlot();
                if (_players.TryGetValue(localBuildSlot, out PlayerData? localBuildData))
                {
                    BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
                        ?? GetNodeOrNull<BuildUI>("%BuildUI")
                        ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
                    buildUI?.UpdatePowerupCounts(localBuildData.Powerups);
                }
                // Reset troop counts in BuildUI (ClearAll zeroed the army, UI must match)
                UpdateBuildUITroopCounts();
                break;

            case GamePhase.FogReveal:
                _ghostPreview?.Hide();
                if (_readyButton != null) _readyButton.Visible = false;
                _combatCamera?.Deactivate();
                break;

            case GamePhase.Combat:
                // Only run fortress/troop setup if the countdown overlay hasn't
                // already done it (PerformCombatSetupBehindOverlay).
                if (!_combatSetupDone)
                {
                    BuildPrototypeFortresses();
                    DeployAllTroops();
                    // In online mode, host configures turn order and broadcasts.
                    if (_networkManager?.IsOnline == true)
                    {
                        if (_networkManager.IsHost)
                        {
                            _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                            if (_turnManager != null)
                                _syncManager?.SendTurnOrder(_turnManager.TurnOrder);
                        }
                    }
                    else
                    {
                        _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                    }
                }
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

                // Populate CombatUI with the local player's weapons and set local slot
                {
                    PlayerSlot localCombatSlot = GetLocalPlayerSlot();
                    CombatUI? combatUISetup = GetNodeOrNull<CombatUI>("%CombatUI")
                        ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                    combatUISetup?.SetLocalPlayerSlot(localCombatSlot);
                    RefreshCombatUIWeapons(localCombatSlot);
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
            bool isDoorTool = _buildSystem?.CurrentToolMode == BuildToolMode.Door;
            if ((isEraser && _placementMode == PlacementMode.Block) || isDoorTool)
            {
                // Eraser and Door target the hit voxel itself (the solid block)
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
            _buildCursorMicrovoxel = targetMicrovoxel;

            // Keep BuildSystem in sync for HalfBlock placement
            if (_buildSystem != null)
                _buildSystem.HalfBlockMicrovoxel = targetMicrovoxel;

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

                // Add symmetry-mirrored blocks for the blueprint preview
                if (_buildSystem.SymmetryMode != BuildSymmetryMode.None)
                {
                    foreach (Vector3I offset in rotatedOffsets)
                    {
                        Vector3I buildUnit = _buildCursorBuildUnit + offset;
                        allMicrovoxels.AddRange(_buildSystem.GetSymmetryMirroredMicrovoxels(zone, buildUnit));
                    }
                }

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
            else if (!_isDragBuilding && _buildSystem != null && _ghostPreview != null
                     && IsMultiVoxelTool(_buildSystem.CurrentToolMode)
                     && _placementMode == PlacementMode.Block)
            {
                // Pre-drag preview for multi-voxel tools: show the tool shape at
                // the cursor using cursor as both start and end. This gives the
                // player a visual hint of the tool's shape before they click.
                BuildToolMode currentMode = _buildSystem.CurrentToolMode;
                List<Vector3I> allMicrovoxels = new List<Vector3I>();

                foreach (Vector3I buildUnit in BuildSystem.GenerateBuildUnitCells(currentMode, _buildCursorBuildUnit, _buildCursorBuildUnit, _buildSystem.HollowBoxMode))
                {
                    foreach (Vector3I micro in BuildSystem.ExpandBuildUnit(buildUnit, currentMode, _buildCursorBuildUnit, _buildCursorBuildUnit))
                    {
                        allMicrovoxels.Add(micro);
                    }
                    allMicrovoxels.AddRange(_buildSystem.GetSymmetryMirroredMicrovoxels(zone, buildUnit));
                }

                _ghostPreview.SetPreview(allMicrovoxels, _buildCursorValid);
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
            else if (_placementMode == PlacementMode.Troop && _ghostPreview != null)
            {
                // Troop preview: show the troop model at the cursor position
                // Offset Y by leg depth so preview matches actual spawn position
                float troopVoxelSize = _selectedTroopType == TroopType.Demolisher ? 0.07f : 0.06f;
                float legOffset = 4f * troopVoxelSize;
                ArrayMesh previewMesh = GetOrCreateTroopPreviewMesh(_selectedTroopType);
                Vector3I microBase = MathHelpers.BuildToMicrovoxel(_buildCursorBuildUnit);
                Vector3 worldPos = MathHelpers.MicrovoxelToWorld(microBase)
                    + new Vector3(GameConfig.BuildUnitMeters * 0.5f, legOffset, GameConfig.BuildUnitMeters * 0.5f);
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
            else if (_buildSystem?.CurrentToolMode == BuildToolMode.HalfBlock && _ghostPreview != null)
            {
                // HalfBlock preview: show a single microvoxel at the exact cursor position
                _ghostPreview.SetPreview(new[] { _buildCursorMicrovoxel }, _buildCursorValid);
            }
            else if (_buildSystem?.CurrentToolMode == BuildToolMode.Door && _ghostPreview != null)
            {
                // Door preview: show 2-wide x 4-tall rectangle at the hit solid block
                // Width axis depends on door facing (perpendicular to outward normal)
                int doorRot = _buildRotation == 0 ? -1 : _buildRotation - 1;
                // Determine if door faces X or Z to pick width axis
                bool doorFacesX = doorRot == 1 || doorRot == 3; // Right or Left
                if (doorRot < 0)
                {
                    // Auto-detect: check if on zone edge or neighbor solids
                    // Simplified: default width along Z unless cursor neighbors suggest X-wall
                    doorFacesX = false;
                    if (_voxelWorld != null)
                    {
                        bool solidPosX = _voxelWorld.GetVoxel(_buildCursorMicrovoxel + new Vector3I(1, 0, 0)).IsSolid;
                        bool solidNegX = _voxelWorld.GetVoxel(_buildCursorMicrovoxel + new Vector3I(-1, 0, 0)).IsSolid;
                        if (solidPosX || solidNegX) doorFacesX = false; // wall along X → door faces Z → width along X... no, perpendicular
                        // Wall along X → door faces Z → width runs along X
                        // Wall along Z → door faces X → width runs along Z
                        bool solidPosZ = _voxelWorld.GetVoxel(_buildCursorMicrovoxel + new Vector3I(0, 0, 1)).IsSolid;
                        bool solidNegZ = _voxelWorld.GetVoxel(_buildCursorMicrovoxel + new Vector3I(0, 0, -1)).IsSolid;
                        doorFacesX = solidPosZ || solidNegZ;
                    }
                }
                Vector3I widthStep = doorFacesX ? new Vector3I(0, 0, 1) : new Vector3I(1, 0, 0);
                // Snap preview to bottom of wall column (same logic as DoorRegistry.TryPlaceDoor)
                Vector3I doorBase = _buildCursorMicrovoxel;
                if (_voxelWorld != null)
                {
                    int scanY = doorBase.Y;
                    while (scanY > 0)
                    {
                        Vector3I below = new Vector3I(doorBase.X, scanY - 1, doorBase.Z);
                        if (!_voxelWorld.GetVoxel(below).IsSolid)
                            break;
                        scanY--;
                    }
                    doorBase = new Vector3I(doorBase.X, scanY, doorBase.Z);
                }
                var doorMicros = new List<Vector3I>();
                for (int dw = 0; dw < DoorRegistry.DoorWidth; dw++)
                    for (int dy = 0; dy < DoorRegistry.DoorHeight; dy++)
                        doorMicros.Add(doorBase + widthStep * dw + new Vector3I(0, dy, 0));
                _ghostPreview.SetPreview(doorMicros, _buildCursorValid);
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
            // Door mode needs 5 states: 0=auto, 1-4=explicit directions
            bool isDoorMode = _buildSystem?.CurrentToolMode == BuildToolMode.Door;
            int modulus = isDoorMode ? 5 : 4;
            _buildRotation = (_buildRotation + 1) % modulus;
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
                case PlacementMode.Troop:
                    TryPlaceTroopAtCursor();
                    break;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Right-click: try to erase weapon/troop/door under cursor first.
        // If nothing was erased and we're in a non-block placement mode, cancel back to block.
        // If in block mode and nothing else erased, erase block.
        if (@event.IsActionPressed("place_secondary"))
        {
            // Compute the distance to the nearest solid block along the cursor ray.
            // Entities behind a block should not be erased (the block occludes them).
            float blockRayT = float.MaxValue;
            if (_camera != null && _voxelWorld != null)
            {
                Vector2 mp = GetViewport().GetMousePosition();
                Vector3 ro = _camera.ProjectRayOrigin(mp);
                Vector3 rd = _camera.ProjectRayNormal(mp);
                if (_voxelWorld.RaycastVoxel(ro, rd, MaxRaycastDistance, out Vector3I hitVox, out Vector3I _))
                {
                    float microMeters = GameConfig.MicrovoxelMeters;
                    Vector3 hitCenter = new Vector3(
                        hitVox.X * microMeters + microMeters * 0.5f,
                        hitVox.Y * microMeters + microMeters * 0.5f,
                        hitVox.Z * microMeters + microMeters * 0.5f);
                    blockRayT = (hitCenter - ro).Dot(rd);
                }
            }

            bool erased = TryEraseWeaponAtCursor(blockRayT) || TryEraseTroopAtCursor(blockRayT) || TryEraseDoorAtCursor();
            if (!erased)
            {
                if (_placementMode != PlacementMode.Block)
                {
                    _placementMode = PlacementMode.Block;
                    GD.Print("[GameManager] Placement mode cancelled.");
                }
                else if (_hasBuildCursor)
                {
                    TryEraseBlock();
                }
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Undo with Ctrl+Z
        if (@event.IsActionPressed("undo_build"))
        {
            UndoLastBuildAction();
        }

        // Redo with Ctrl+Y
        if (@event.IsActionPressed("redo_build"))
        {
            if (_buildSystem.RedoLast(_activeBuilder))
            {
                _buildUndoStack.Push(new BuildUndoEntry { Type = UndoType.Voxel });
            }
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
            _buildUndoStack.Push(new BuildUndoEntry { Type = UndoType.Voxel });
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
            _buildUndoStack.Push(new BuildUndoEntry { Type = UndoType.Voxel });
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
            else
            {
                _buildUndoStack.Push(new BuildUndoEntry { Type = UndoType.Voxel });
            }

            _buildSystem.CurrentToolMode = previousMode;
        }
    }

    /// <summary>
    /// Right-click erase: removes the nearest weapon under the cursor.
    /// Returns true if a weapon was found and removed.
    /// </summary>
    private bool TryEraseWeaponAtCursor(float blockRayT = float.MaxValue)
    {
        if (_camera == null || !_weapons.TryGetValue(_activeBuilder, out var weaponList) || weaponList.Count == 0)
            return false;
        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
            return false;

        // Raycast from camera to get a world position
        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.ProjectRayNormal(mousePos);

        // Find nearest weapon to cursor ray (only if in front of the nearest block)
        WeaponBase? closest = null;
        float closestDist = 2.0f; // max selection distance in meters
        for (int i = weaponList.Count - 1; i >= 0; i--)
        {
            WeaponBase w = weaponList[i];
            if (w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed) continue;

            // Distance from weapon to camera ray
            Vector3 wPos = w.GlobalPosition;
            Vector3 toWeapon = wPos - rayOrigin;
            float t = toWeapon.Dot(rayDir);
            if (t < 0 || t > blockRayT) continue; // skip if behind a solid block
            Vector3 closestPointOnRay = rayOrigin + rayDir * t;
            float dist = closestPointOnRay.DistanceTo(wPos);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = w;
            }
        }

        if (closest == null) return false;

        WeaponType wType = closest switch
        {
            Cannon => WeaponType.Cannon,
            Mortar => WeaponType.Mortar,
            Railgun => WeaponType.Railgun,
            MissileLauncher => WeaponType.MissileLauncher,
            Drill => WeaponType.Drill,
            _ => WeaponType.Cannon,
        };

        CancelUndoEntry(UndoType.Weapon, weapon: closest);
        int refund = GetWeaponCost(wType);
        weaponList.Remove(closest);
        player.WeaponIds.Remove(closest.WeaponId);
        player.Refund(refund);
        closest.QueueFree();
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, refund));
        AudioDirector.Instance?.PlaySFX("ui_click");
        GD.Print($"[Build] Right-click erased {wType} for {_activeBuilder}, refund ${refund}.");
        return true;
    }

    /// <summary>
    /// Right-click erase: removes the nearest troop marker under the cursor.
    /// Returns true if a troop marker was found and removed.
    /// </summary>
    private bool TryEraseTroopAtCursor(float blockRayT = float.MaxValue)
    {
        if (_camera == null || _armyManager == null) return false;
        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player)) return false;

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.ProjectRayNormal(mousePos);

        // Find nearest troop marker to cursor ray (only if in front of the nearest block)
        Node? closestMarker = null;
        float closestDist = 1.5f;
        string prefix = $"TroopMarker_{_activeBuilder}_";

        foreach (Node child in GetTree().GetNodesInGroup("TroopMarkers"))
        {
            if (child is not Node3D marker) continue;
            if (!child.Name.ToString().StartsWith(prefix)) continue;

            Vector3 mPos = marker.GlobalPosition;
            Vector3 toMarker = mPos - rayOrigin;
            float t = toMarker.Dot(rayDir);
            if (t < 0 || t > blockRayT) continue; // skip if behind a solid block
            Vector3 closestPointOnRay = rayOrigin + rayDir * t;
            float dist = closestPointOnRay.DistanceTo(mPos);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestMarker = child;
            }
        }

        if (closestMarker == null) return false;

        // Extract troop type from marker name: "TroopMarker_{player}_{type}_{pos}"
        string markerName = closestMarker.Name.ToString();
        TroopType clickedType = TroopType.Infantry; // default
        foreach (TroopType tt in TroopDefinitions.AllTypes)
        {
            if (markerName.Contains(tt.ToString()))
            {
                clickedType = tt;
                break;
            }
        }

        if (_armyManager.TrySellTroop(_activeBuilder, clickedType, player))
        {
            Vector3I markerPos = ParseTroopMarkerPosition(markerName);
            CancelUndoEntry(UndoType.Troop, position: markerPos);
            closestMarker.QueueFree();
            UpdateBuildUITroopCounts();
            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, TroopDefinitions.Get(clickedType).Cost));
            AudioDirector.Instance?.PlaySFX("ui_click");
            GD.Print($"[Build] Right-click erased {clickedType} for {_activeBuilder}.");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Right-click erase: removes the nearest door under the cursor.
    /// Returns true if a door was found and removed.
    /// </summary>
    private bool TryEraseDoorAtCursor()
    {
        if (_camera == null || _armyManager == null || _voxelWorld == null) return false;

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.ProjectRayNormal(mousePos);

        if (!_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
            return false;

        // Check if the hit position (or adjacent) is part of a door
        var doors = _armyManager.Doors.GetDoors(_activeBuilder);
        foreach (var door in doors)
        {
            foreach (var voxel in door.OpeningVoxels)
            {
                // Check within 1 voxel of the hit
                int dx = System.Math.Abs(voxel.X - hitPos.X);
                int dy = System.Math.Abs(voxel.Y - hitPos.Y);
                int dz = System.Math.Abs(voxel.Z - hitPos.Z);
                if (dx <= 1 && dy <= 1 && dz <= 1)
                {
                    CancelUndoEntry(UndoType.Door, position: door.BaseMicrovoxel);
                    _armyManager.Doors.RemoveDoor(door.BaseMicrovoxel, _activeBuilder);
                    AudioDirector.Instance?.PlaySFX("ui_click");
                    GD.Print($"[Build] Right-click erased door at {door.BaseMicrovoxel} for {_activeBuilder}.");
                    return true;
                }
            }
        }
        return false;
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

        _buildUndoStack.Push(new BuildUndoEntry
        {
            Type = UndoType.Weapon,
            Weapon = weapon,
            WeaponKind = _selectedWeaponType,
            Cost = weaponCost,
        });

        AudioDirector.Instance?.PlaySFX("ui_confirm");
        GD.Print($"[Build] {_selectedWeaponType} placed for {_activeBuilder} at {_buildCursorBuildUnit} (cost: ${weaponCost}).");
        // Stay in weapon placement mode so the user can place multiple weapons in a row.
        // Right-click or selecting a build tool returns to block mode.
    }

    /// <summary>
    /// Places a door at the build cursor, carving a 1x4 opening through the wall.
    /// Doors allow own troops to pass through. Cursor already targets solid blocks
    /// in door mode (handled in UpdateBuildCursorPosition).
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

        Vector3I zoneMin = zone.OriginMicrovoxels;
        Vector3I zoneMax = zone.MaxMicrovoxelsInclusive;

        // Cursor already targets the solid block in door mode (like eraser)
        Vector3I doorBase = _buildCursorMicrovoxel;

        // _buildRotation 0 = auto-detect facing,
        // 1-4 = user pressed R (explicit: 0=Forward, 1=Right, 2=Back, 3=Left)
        int doorRotation = _buildRotation == 0 ? -1 : _buildRotation - 1;
        bool success = _armyManager.Doors.TryPlaceDoor(
            _voxelWorld, doorBase, _activeBuilder, zoneMin, zoneMax, doorRotation, out string failReason);

        if (success)
        {
            _buildUndoStack.Push(new BuildUndoEntry
            {
                Type = UndoType.Door,
                Position = doorBase,
            });
            GD.Print($"[Build] Door placed for {_activeBuilder} at {doorBase}.");
            AudioDirector.Instance?.PlaySFX("ui_click");
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

    private ArrayMesh GetOrCreateTroopPreviewMesh(TroopType type)
    {
        if (_troopPreviewMeshes.TryGetValue(type, out ArrayMesh? cached))
            return cached;

        // Build the actual character model and combine all mesh surfaces
        Color teamColor = _players.TryGetValue(_activeBuilder, out PlayerData? p)
            ? p.PlayerColor : new Color(0.2f, 0.6f, 1.0f);
        Art.CharacterDefinition charDef = type switch
        {
            TroopType.Infantry => Art.TroopModelGenerator.GenerateInfantry(teamColor),
            TroopType.Demolisher => Art.TroopModelGenerator.GenerateDemolisher(teamColor),
            _ => Art.TroopModelGenerator.GenerateInfantry(teamColor),
        };
        Node3D model = Art.VoxelCharacterBuilder.Build(charDef);

        // Collect all MeshInstance3D vertices into a single combined mesh
        var allVerts = new System.Collections.Generic.List<Vector3>();
        var allNormals = new System.Collections.Generic.List<Vector3>();
        var allIndices = new System.Collections.Generic.List<int>();
        CollectMeshesRecursive(model, Transform3D.Identity, allVerts, allNormals, allIndices);

        ArrayMesh mesh = new ArrayMesh();
        if (allVerts.Count > 0)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = allVerts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = allNormals.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = allIndices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }

        model.QueueFree(); // dispose the temp hierarchy
        _troopPreviewMeshes[type] = mesh;
        return mesh;
    }

    private static void CollectMeshesRecursive(Node node, Transform3D parentTransform,
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<int> indices)
    {
        Transform3D nodeTransform = parentTransform;
        if (node is Node3D n3d)
            nodeTransform = parentTransform * n3d.Transform;

        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var surfArrays = mi.Mesh.SurfaceGetArrays(s);
                if (surfArrays == null || surfArrays.Count == 0) continue;
                var surfVerts = surfArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var surfNormals = surfArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var surfIndices = surfArrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (surfVerts == null || surfVerts.Length == 0) continue;

                int baseIndex = verts.Count;
                for (int i = 0; i < surfVerts.Length; i++)
                {
                    verts.Add(nodeTransform * surfVerts[i]);
                    if (surfNormals != null && i < surfNormals.Length)
                        normals.Add((nodeTransform.Basis * surfNormals[i]).Normalized());
                    else
                        normals.Add(Vector3.Up);
                }
                if (surfIndices != null && surfIndices.Length > 0)
                {
                    for (int i = 0; i < surfIndices.Length; i++)
                        indices.Add(baseIndex + surfIndices[i]);
                }
            }
        }

        foreach (Node child in node.GetChildren())
            CollectMeshesRecursive(child, nodeTransform, verts, normals, indices);
    }

    private void TryPlaceTroopAtCursor()
    {
        if (_armyManager == null || _voxelWorld == null) return;
        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player)) return;
        if (!_buildZones.TryGetValue(_activeBuilder, out BuildZone zone)) return;

        // Validate position is on ground inside build zone
        Vector3I buildUnit = _buildCursorBuildUnit;
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnit);

        // Check build zone bounds
        Vector3I zoneMin = zone.OriginMicrovoxels;
        Vector3I zoneMax = zone.OriginMicrovoxels + zone.SizeMicrovoxels;
        if (microBase.X < zoneMin.X || microBase.X >= zoneMax.X ||
            microBase.Z < zoneMin.Z || microBase.Z >= zoneMax.Z)
        {
            GD.Print("[Army] Troop must be placed inside your build zone.");
            return;
        }

        // Find ground level at this position — scan down from the cursor Y
        // (not from zone top, which would hit the roof of enclosed rooms)
        int groundY = -1;
        for (int y = microBase.Y; y >= zoneMin.Y - 2; y--)
        {
            if (_voxelWorld.GetVoxel(new Vector3I(microBase.X, y, microBase.Z)).IsSolid)
            {
                groundY = y + 1; // spawn on top of the solid block
                break;
            }
        }

        if (groundY < 0)
        {
            GD.Print("[Army] No ground to place troop on.");
            return;
        }

        // Need 2 blocks of clearance above ground for troop to stand
        Vector3I spawnPos = new Vector3I(microBase.X, groundY, microBase.Z);
        if (_voxelWorld.GetVoxel(spawnPos).IsSolid ||
            _voxelWorld.GetVoxel(spawnPos + Vector3I.Up).IsSolid)
        {
            GD.Print("[Army] Not enough clearance to place troop here.");
            return;
        }

        // Check for overlap with already-placed troops
        foreach (var (existingType, existingPos) in _armyManager.GetPurchasedTroops(_activeBuilder))
        {
            if (existingPos.HasValue && existingPos.Value == spawnPos)
            {
                GD.Print("[Army] A troop is already placed at this position.");
                return;
            }
        }

        // Try to buy and place
        if (_armyManager.TryBuyAndPlaceTroop(_activeBuilder, _selectedTroopType, player, spawnPos))
        {
            TroopStats stats = TroopDefinitions.Get(_selectedTroopType);
            GD.Print($"[Army] {_activeBuilder}: Placed {stats.Name} at {spawnPos}. Budget: ${player.Budget}.");

            // Spawn a visual marker so the player sees where their troops are
            SpawnTroopMarker(spawnPos, _selectedTroopType, _buildRotation);

            // Update BuildUI troop counts
            UpdateBuildUITroopCounts();

            _buildUndoStack.Push(new BuildUndoEntry
            {
                Type = UndoType.Troop,
                TroopKind = _selectedTroopType,
                Cost = stats.Cost,
                Position = spawnPos,
            });

            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, -stats.Cost));

            AudioDirector.Instance?.PlaySFX("ui_click");
        }
        else
        {
            TroopStats stats = TroopDefinitions.Get(_selectedTroopType);
            GD.Print($"[Army] {_activeBuilder}: Can't place {stats.Name}.");
        }
    }

    private void SpawnTroopMarker(Vector3I microPos, TroopType type, int rotation = 0)
    {
        Color teamColor = _players.TryGetValue(_activeBuilder, out PlayerData? p)
            ? p.PlayerColor : new Color(0.2f, 0.6f, 1.0f);

        // Create a small visible character model at the placement position
        Art.CharacterDefinition charDef = type switch
        {
            TroopType.Infantry => Art.TroopModelGenerator.GenerateInfantry(teamColor),
            TroopType.Demolisher => Art.TroopModelGenerator.GenerateDemolisher(teamColor),
            _ => Art.TroopModelGenerator.GenerateInfantry(teamColor),
        };

        Node3D model = Art.VoxelCharacterBuilder.Build(charDef);
        Art.VoxelCharacterBuilder.ApplyToonMaterial(model, teamColor);
        model.Name = $"TroopMarker_{_activeBuilder}_{type}_{microPos}";
        model.AddToGroup("TroopMarkers");

        // Add to tree BEFORE setting GlobalPosition to avoid !is_inside_tree() error
        AddChild(model);

        float microMeters = GameConfig.MicrovoxelMeters;
        // Offset Y upward by leg depth so feet sit on ground (same as TroopEntity)
        const float legOffset = 4f * 0.06f; // 4 voxels * troop voxelSize
        model.GlobalPosition = new Vector3(
            microPos.X * microMeters + microMeters * 0.5f,
            microPos.Y * microMeters + legOffset,
            microPos.Z * microMeters + microMeters * 0.5f);

        // Apply rotation from R key
        model.RotationDegrees = new Vector3(0, rotation * 90f, 0);
    }

    private void UpdateBuildUITroopCounts()
    {
        BuildUI? buildUI = GetNodeOrNull<BuildUI>("BuildHUD")
            ?? GetNodeOrNull<BuildUI>("%BuildUI")
            ?? GetTree().Root.FindChild("BuildHUD", true, false) as BuildUI;
        if (buildUI != null && _armyManager != null)
        {
            foreach (TroopType tt in TroopDefinitions.AllTypes)
            {
                buildUI.UpdateTroopCount(tt, _armyManager.TroopCount(_activeBuilder, tt));
            }
        }
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

        // Refresh troop counts for the new builder (prevents stale counts after rematch)
        UpdateBuildUITroopCounts();

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

        // Local human players must place a commander and at least 1 weapon before readying up
        if (IsLocalPlayer(_activeBuilder))
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

        // Show naming popup for local human players before advancing.
        // If already awaiting a name (popup is open), ignore the second click.
        if (IsLocalPlayer(_activeBuilder))
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
        // ── Online multiplayer: capture + send build snapshot, wait for all players ──
        if (_networkManager != null && _networkManager.IsOnline)
        {
            if (_localBuildReady) return; // Already sent
            _localBuildReady = true;

            PlayerSlot localSlot = GetLocalPlayerSlot();
            string blueprintJson = CapturePlayerBuildJson(localSlot);
            _syncManager?.SendBuildComplete(localSlot, blueprintJson);

            // Show "waiting" message on the ready button
            if (_readyButton != null)
            {
                _readyButton.Text = "WAITING...";
                _readyButton.Disabled = true;
            }

            GD.Print($"[Online] Local build complete for {localSlot}, waiting for other players...");
            CheckAllPlayersReadyForCombat();
            return;
        }

        // ── Local / bot mode: sequential builder rotation ──
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

        // In online mode, only the host shuffles turn order, then broadcasts to clients.
        // Clients wait for the TurnOrder RPC and apply it via ConfigureWithOrder.
        if (_networkManager?.IsOnline == true)
        {
            if (_networkManager.IsHost)
            {
                _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                if (_turnManager != null)
                    _syncManager?.SendTurnOrder(_turnManager.TurnOrder);
            }
            else
            {
                // Client: host drives turn timing, client only responds to broadcasts
                if (_turnManager != null)
                    _turnManager.IsAuthoritative = false;
            }
        }
        else
        {
            _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
        }

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

        // Populate CombatUI with the local player's weapons and set local slot
        {
            PlayerSlot localCombatSlot2 = GetLocalPlayerSlot();
            CombatUI? combatUISetup2 = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            combatUISetup2?.SetLocalPlayerSlot(localCombatSlot2);
            RefreshCombatUIWeapons(localCombatSlot2);
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

        // Compute the final behind-zone position — slightly closer than regular
        // PositionFreeFlyBehindZone so the initial view feels more intimate
        Vector3 awayDir = new Vector3(fortCenter.X - arenaCenter.X, 0f, fortCenter.Z - arenaCenter.Z);
        if (awayDir.LengthSquared() < 0.01f) awayDir = new Vector3(0f, 0f, 1f);
        awayDir = awayDir.Normalized();
        Vector3 behindPos = fortCenter + new Vector3(0f, 22f, 0f) + awayDir * 30f;

        // Start position: directly above the fortress looking down
        Vector3 topDownPos = fortCenter + new Vector3(0f, 40f, 0f);

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

        // Cancel aiming with escape or right-click: return to top-down (also resets confirmation)
        // CombatCamera also handles right-click in targeting/POV via ExitWeaponPOVRequested.
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_isAiming || _weaponConfirmed || (_combatCamera != null && _combatCamera.IsInTargeting))
            {
                CancelTargeting();
            }
        }
        // Right-click to unaim: cancel weapon selection even before entering targeting cam
        if (@event is InputEventMouseButton rmbCancel && rmbCancel.Pressed && rmbCancel.ButtonIndex == MouseButton.Right)
        {
            if (_weaponConfirmed && !_isAiming && (_combatCamera == null || !_combatCamera.IsInTargeting))
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
            if (!IsLocalPlayer(currentPlayer))
            {
                // When toggling top-down ON, always switch immediately — even during
                // cinematic moments (projectile follow, impact, kill cam). The player
                // wants to stay in the overhead view and not track other players' projectiles.
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

        // T key: toggle troop movement mode (syncs with TROOPS button)
        if (@event is InputEventKey troopKey && troopKey.Pressed && troopKey.Keycode == Key.T)
        {
            OnTroopMoveToggled();
            _armyManager?.PrintTroopDebug();
        }

        // Troop move mode: click terrain to send troops there
        if (_troopMoveMode && @event is InputEventMouseButton troopClick &&
            troopClick.Pressed && troopClick.ButtonIndex == MouseButton.Left)
        {
            Vector2 mousePos = troopClick.Position;
            Camera3D? cam = GetViewport().GetCamera3D();
            if (cam != null && _voxelWorld != null)
            {
                Vector3 rayOrigin = cam.ProjectRayOrigin(mousePos);
                Vector3 rayDir = cam.ProjectRayNormal(mousePos);
                if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, 200f, out Vector3I hitPos, out Vector3I _))
                {
                    // Move troops to the air position above the hit voxel
                    Vector3I moveTarget = hitPos + Vector3I.Up;
                    _armyManager?.MoveTroopsToward(currentPlayer, moveTarget);
                    _troopMoveMode = false;
                    _troopsMoved = true; // hide button for rest of turn

                    // Sync troop movement to remote players
                    if (_networkManager?.IsOnline == true)
                    {
                        _syncManager?.SendTroopMove(currentPlayer, moveTarget);
                    }
                    CombatUI? troopUI2 = GetNodeOrNull<CombatUI>("%CombatUI")
                        ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                    troopUI2?.HideSelectWeaponPrompt();
                    troopUI2?.SetTroopsModeActive(false);
                    troopUI2?.SetTroopsButtonVisible(false); // hide button after use
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

        // Use filtered alive-weapon list matching CombatUI's indices
        List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
        if (aliveWeapons.Count == 0) return;

        int safeIndex = _selectedWeaponIndex % aliveWeapons.Count;
        WeaponBase? weapon = aliveWeapons[safeIndex];
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

        // Show "CLICK TO AIM — ENTER TO FIRE" prompt
        CombatUI? aimPromptUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        aimPromptUI?.ShowAimPrompt();

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

        // Second click fires if we already have a target set
        if (_hasTarget && _aimingSystem.HasTarget)
        {
            FireCurrentPlayerWeapon();
            return;
        }

        // First click: raycast to find a target
        Vector3 rayOrigin = _combatCamera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _combatCamera.ProjectRayNormal(mousePos);

        // Get the weapon early so we can pass weapon info to aim assist
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

        // --- Aim assist: snap to enemy commander if the click ray passes near one ---
        // Railgun penetrates walls, so skip LOS check for it
        Vector3? commanderSnapTarget = TrySnapToCommander(rayOrigin, rayDir, currentPlayer, weapon.WeaponId);

        // Determine the world-space target: commander snap takes priority over voxel hit
        Vector3? targetWorld = null;

        if (commanderSnapTarget.HasValue)
        {
            targetWorld = commanderSnapTarget.Value;
        }
        else if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
        {
            targetWorld = MathHelpers.MicrovoxelToWorld(hitPos)
                + new Vector3(
                    GameConfig.MicrovoxelMeters * 0.5f,
                    GameConfig.MicrovoxelMeters * 0.5f,
                    GameConfig.MicrovoxelMeters * 0.5f);
        }

        if (targetWorld.HasValue)
        {

            // Set the target on the aiming system (auto-calculates ballistic trajectory)
            bool inRange = _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetWorld.Value, weapon.ProjectileSpeed, weapon.WeaponId);
            _hasTarget = true;

            // Rotate the weapon to face the target (yaw only, stays upright)
            RotateWeaponToward(weapon, targetWorld.Value);

            // Show fire prompt
            CombatUI? firePromptUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            firePromptUI?.ShowFirePrompt();

            if (commanderSnapTarget.HasValue)
            {
                GD.Print($"[Combat] AIM ASSIST: Snapped to enemy commander at {targetWorld.Value}. Click again to fire.");
            }
            else if (inRange)
            {
                GD.Print($"[Combat] Target set at {targetWorld.Value}. Click again to fire.");
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
    private Vector3? TrySnapToCommander(Vector3 rayOrigin, Vector3 rayDir, PlayerSlot currentPlayer, string weaponId = "")
    {
        // World-space threshold: ray must pass within this distance
        // of the commander's center-mass to trigger the snap.
        const float snapRadius = 2.5f;

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

        // LOS check: verify no solid voxels block the path from weapon to commander.
        // Railgun penetrates walls, so skip LOS check for it.
        if (weaponId != "railgun")
        {
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

            if (_voxelWorld.RaycastVoxel(weaponPos, losDir, losDist, out Vector3I _, out Vector3I _2))
            {
                GD.Print($"[Combat] Aim assist: LOS to commander blocked by voxel.");
                return null;
            }
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
        _troopMoveMode = false;
        _aimingSystem?.ClearTarget();
        _combatCamera?.ExitWeaponPOV();
        // Return to FreeFlyCamera for WASD movement
        SwitchToFreeFlyCamera();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        HideTargetHighlight();

        // Remove the green selection highlight from all weapons
        ClearAllWeaponHighlights();

        // Hide the target enemy selector UI and reset prompt
        CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        combatUI?.HideTargetSelector();
        combatUI?.HideSelectWeaponPrompt();
        combatUI?.ResetPromptText();
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

        // Build filtered alive list matching CombatUI's indices
        List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);

        // Map the selected alive-list index back to identify which weapon to highlight
        WeaponBase? selectedWeapon = null;
        if (_selectedWeaponIndex >= 0 && aliveWeapons.Count > 0)
        {
            int safeIndex = _selectedWeaponIndex % aliveWeapons.Count;
            selectedWeapon = aliveWeapons[safeIndex];
        }

        // Highlight only the selected weapon in the full list
        for (int i = 0; i < weaponList.Count; i++)
        {
            WeaponBase? w = weaponList[i];
            if (w != null && GodotObject.IsInstanceValid(w))
            {
                w.SetHighlighted(w == selectedWeapon);
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

        // Build the same filtered alive-weapon list that CombatUI uses,
        // so _selectedWeaponIndex (set by CombatUI) maps correctly.
        List<WeaponBase> aliveWeapons = weaponList.FindAll(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsDestroyed);
        if (aliveWeapons.Count == 0) return;

        int safeIndex = _selectedWeaponIndex % aliveWeapons.Count;
        WeaponBase? weapon = aliveWeapons[safeIndex];
        bool isEmpd = weapon != null && GodotObject.IsInstanceValid(weapon) &&
            (_powerupExecutor?.IsWeaponEmpDisabled(weapon, _players) ?? false);
        if (weapon == null || !GodotObject.IsInstanceValid(weapon) || !weapon.CanFire(_turnManager.RoundNumber) || isEmpd)
        {
            // Try to find any weapon that can fire and isn't EMP'd
            weapon = aliveWeapons.Find(candidate =>
                GodotObject.IsInstanceValid(candidate) &&
                candidate.CanFire(_turnManager.RoundNumber) &&
                !(_powerupExecutor?.IsWeaponEmpDisabled(candidate, _players) ?? false));
            if (weapon == null)
            {
                GD.Print("[Combat] No weapons can fire this turn (all disabled or on cooldown).");
                return;
            }
        }

        int roundBefore = weapon.LastFiredRound;
        Vector3 launchVelocity = _aimingSystem.GetLaunchVelocity(weapon.ProjectileSpeed);
        ProjectileBase? projectile = weapon.Fire(_aimingSystem, _voxelWorld, _turnManager.RoundNumber);
        if (weapon.LastFiredRound != roundBefore)
        {
            // Sync weapon fire to remote players
            if (_networkManager?.IsOnline == true)
            {
                _syncManager?.SendWeaponFire(currentPlayer, safeIndex, launchVelocity, weapon.GlobalPosition);
            }

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

            // Hide all attack UI after firing — weapon bar, powerup panel, troops button, selectors, skip
            {
                CombatUI? combatUI2 = GetNodeOrNull<CombatUI>("%CombatUI")
                    ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
                combatUI2?.HideTargetSelector();
                combatUI2?.HideSelectWeaponPrompt();
                combatUI2?.ResetPromptText();
                combatUI2?.HideAttackUI();
            }
            if (_skipTurnButton != null) _skipTurnButton.Visible = false;

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

            // Check if current player has troops that can attack — if so, defer turn
            // advancement so the troop attack cam sequence plays after the impact cam.
            // Works for ALL players (human + bots) so you see everyone's troops attack.
            PlayerSlot firedPlayer = currentPlayer;
            _deferTurnAdvanceForTroops = _armyManager?.HasAliveTroops(firedPlayer) ?? false;
            _troopSequencePlayer = firedPlayer;
            GD.Print($"[TroopSeq] DeferTroopAttack={_deferTurnAdvanceForTroops} player={firedPlayer} hasAliveTroops={_armyManager?.HasAliveTroops(firedPlayer)}");

            // Wait for projectile to land, then linger on the impact before advancing
            if (projectile != null)
            {
                WaitForProjectileThenAdvance(projectile, firedPlayer);
            }
            else
            {
                // Hitscan — advance after a short delay
                GetTree().CreateTimer(2.0).Timeout += () =>
                {
                    if (_troopSequenceActive) return; // troop sequence already running

                    // If troops are deferred and CinematicFinished won't fire, start
                    // troop sequence directly from here.
                    if (_deferTurnAdvanceForTroops && _armyManager != null)
                    {
                        _deferTurnAdvanceForTroops = false;

                        // Skip troop attacks if backup bombardment is imminent
                        if (!_artilleryDominanceActive && AnyPlayerHasUsableWeapons())
                        {
                            PlayerSlot troopPlayer = _troopSequencePlayer;
                            var aliveP1 = new HashSet<PlayerSlot>();
                            foreach (var (s, d) in _players) { if (d.IsAlive) aliveP1.Add(s); }
                            var attackTargets = _armyManager.GetTroopsWithAttackTargets(troopPlayer, _commanders, _weapons, aliveP1);
                            if (attackTargets.Count > 0)
                            {
                                StartTroopAttackSequence(attackTargets, troopPlayer);
                                return;
                            }
                        }
                    }

                    if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == firedPlayer)
                    {
                        AdvanceTurnAuthoritative();
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

        // In online mode, only the local player can skip their own turn
        if (_turnManager.CurrentPlayer is PlayerSlot currentPlayer && !IsLocalPlayer(currentPlayer))
            return;

        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();
        _combatCamera?.ExitWeaponPOV();
        SwitchToFreeFlyCamera();

        // In online mode as client, tell the host to skip on our behalf
        if (_networkManager?.IsOnline == true && !_networkManager.IsHost && _turnManager.CurrentPlayer.HasValue)
        {
            _syncManager?.SendSkipTurn(_turnManager.CurrentPlayer.Value);
            return; // Client waits for host's TurnAdvance broadcast
        }

        AdvanceTurnAuthoritative();
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

        // If we have a target set, right-click clears the crosshair so the player
        // can click a new spot (stay in targeting mode). If no target, fully cancel.
        if (_hasTarget && _isAiming)
        {
            _hasTarget = false;
            _aimingSystem?.ClearTarget();
            HideTargetHighlight();
            CombatUI? ui = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            ui?.ShowTargetPrompt();
            GD.Print("[Combat] Target cleared — click to set a new target.");
        }
        else
        {
            CancelTargeting();
        }
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

        // Remove build-phase troop markers before deploying real troops
        foreach (Node marker in GetTree().GetNodesInGroup("TroopMarkers"))
        {
            marker.QueueFree();
        }

        PlayerSlot[] activePlayers = _players.Keys.ToArray();
        for (int i = 0; i < activePlayers.Length; i++)
        {
            PlayerSlot player = activePlayers[i];
            _armyManager.DeployTroops(player, this);
        }

        // Seed bots with random powerups
        SeedBotPowerups();
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
        // In online multiplayer, clients must only process commander deaths from the
        // host's authoritative broadcast (via OnRemoteCommanderDeath which sets the flag).
        // Local explosions on the client may compute a kill that the host hasn't confirmed,
        // causing turn order desync and game over mismatch.
        if (_networkManager?.IsOnline == true && !_networkManager.IsHost && !_processingHostCommanderDeath)
        {
            GD.Print($"[Online] Client detected local commander kill for {payload.Victim} — ignoring, waiting for host authority");
            return;
        }

        if (_players.TryGetValue(payload.Victim, out PlayerData? player))
        {
            player.IsAlive = false;
            player.CommanderHealth = 0;
        }

        // Host broadcasts commander death so both peers agree on who's dead
        if (_networkManager?.IsOnline == true && _networkManager.IsHost)
        {
            _syncManager?.SendCommanderDeath(payload.Victim,
                payload.Killer ?? payload.Victim, payload.WorldPosition);
        }

        // Track CommanderKills for the killer
        if (payload.Killer.HasValue && _players.TryGetValue(payload.Killer.Value, out PlayerData? killer))
        {
            killer.Stats.CommanderKills++;
        }

        // Troops whose commander died surrender — hands up, stop fighting
        _armyManager?.SurrenderTroops(payload.Victim);

        // Force-expire all active powerup effects for the dead player so smoke/shield/EMP
        // don't persist forever (dead players are removed from turn order and never tick)
        if (player != null && _powerupExecutor != null)
            _powerupExecutor.ExpireAllEffects(player, _players);

        // If the kill happened during a troop attack sequence, cancel remaining
        // troop attacks so we go straight to the kill cam without lingering.
        if (_troopSequenceActive)
        {
            _troopSequenceActive = false;
            _advanceTurnAfterKillCam = true;
            GD.Print("[TroopSeq] Commander killed during troop sequence — cancelling remaining attacks");
        }

        // Activate kill cam: orbit the death location.
        // Self-kills (killer == victim) use an overhead impact view instead of
        // the standard orbit cam, which glitches when there's no distinct killer.
        if (_combatCamera != null)
        {
            _camera?.Deactivate();
            if (payload.Killer.HasValue && payload.Killer.Value == payload.Victim)
            {
                // Self-kill: fixed overhead impact view looking down at the death position
                _combatCamera.ImpactCam(payload.WorldPosition, 5f);
            }
            else
            {
                _combatCamera.KillCam(payload.WorldPosition);
            }
        }

        // RemovePlayer may emit TurnChanged if the dead player was the current player.
        // Skip the powerup tick in that handler so surviving players' effects don't
        // expire prematurely from the death-triggered turn change.
        _skipNextPowerupTick = true;
        _turnManager?.RemovePlayer(payload.Victim, Settings.TurnTimeSeconds);
        _skipNextPowerupTick = false;

        // Re-enforce any remaining smoke screens — the turn order change and powerup
        // expiry above may have touched the shader state, so re-sync to ensure any
        // surviving player's smoke dissolve is still applied.
        _powerupExecutor?.ReenforceSmokeScreens(_players, _commanders);

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
        // count in that player's turns, not every player's turns).
        // Skip if this turn change was triggered by a player death removal —
        // otherwise surviving players' smoke/shield/EMP tick down prematurely.
        if (!_skipNextPowerupTick)
            _powerupExecutor?.TickAllPlayerEffects(_players, payload.CurrentPlayer);

        // Re-enforce smoke screen chunk hiding (chunks may have been remeshed by explosions)
        _powerupExecutor?.ReenforceSmokeScreens(_players, _commanders);

        // Tick army troops — all players' attacks are deferred to the visual troop
        // attack sequence that plays after their weapon fires. Skip attacks for the
        // current player here; the camera sequence handles them visually.
        var alivePlayers = new HashSet<PlayerSlot>();
        foreach (var (slot, data) in _players) { if (data.IsAlive) alivePlayers.Add(slot); }
        _armyManager?.TickAllTroops(payload.CurrentPlayer, this, skipAttacksFor: payload.CurrentPlayer,
            alivePlayers: alivePlayers, commanders: _commanders, weapons: _weapons);

        // Bot troops auto-move toward nearest enemy base each turn.
        // Human troops only move when the player clicks TROOPS + terrain.
        if (IsBot(payload.CurrentPlayer) && _armyManager != null)
        {
            _armyManager.BotMoveTroops(payload.CurrentPlayer);
        }

        // Update CombatUI powerup slots, troops button, and turn banner
        {
            CombatUI? combatUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            if (_players.TryGetValue(payload.CurrentPlayer, out PlayerData? currentPlayerData))
            {
                combatUI?.UpdatePowerupSlots(currentPlayerData.Powerups);

                // Show centered commander name banner during camera pan
                int colorIdx = (int)payload.CurrentPlayer;
                Color bannerColor = colorIdx < GameConfig.PlayerColors.Length
                    ? GameConfig.PlayerColors[colorIdx] : Colors.White;
                combatUI?.ShowTurnBanner(currentPlayerData.DisplayName, bannerColor);
            }

            // Show troops button only for local player with alive troops
            bool showTroops = IsLocalPlayer(payload.CurrentPlayer)
                && (_armyManager?.HasAliveTroops(payload.CurrentPlayer) ?? false);
            combatUI?.SetTroopsButtonVisible(showTroops);

            // Re-show weapon bar + powerup panel on local player's turn (safety net — they
            // get hidden by HideAttackUI after firing and must reappear next turn)
            if (IsLocalPlayer(payload.CurrentPlayer))
                combatUI?.ShowAttackUI();
        }

        // Reset aiming state and per-turn limits for the new turn
        _isAiming = false;
        _hasTarget = false;
        _troopMoveMode = false;
        _troopsMoved = false;
        _aimingSystem?.ClearTarget();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Clear weapon highlights from the previous player's turn
        ClearAllWeaponHighlights();

        // Only show skip turn button on the local player's turn
        if (_skipTurnButton != null)
            _skipTurnButton.Visible = IsLocalPlayer(payload.CurrentPlayer);

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

        // Refresh the CombatUI weapon bar — always show the LOCAL player's weapons,
        // not whoever's turn it is, so the client never sees/controls remote weapons.
        PlayerSlot localSlotForUI = GetLocalPlayerSlot();
        RefreshCombatUIWeapons(localSlotForUI);

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
                AdvanceTurnAuthoritative();
            };
            return;
        }

        // Always deactivate the combat camera when a new turn starts so its
        // cinematic mode (Impact/KillCam/etc.) from the previous turn doesn't
        // block camera positioning for this player.
        _combatCamera.Deactivate();

        if (!IsLocalPlayer(payload.CurrentPlayer) && _spectatorTopDown)
        {
            // Player has toggled top-down spectator mode — stay in top-down
            _camera?.Deactivate();
            _combatCamera.TopDown(ComputeArenaMidpoint());
        }
        else
        {
            // Position camera behind this player's build zone
            SwitchToFreeFlyCamera();
            PositionFreeFlyBehindZone(payload.CurrentPlayer);
        }

        // If the current player is a bot, schedule automatic play after a delay.
        // 2.5s gives the camera time to pan to their base and the player time to
        // see the commander name banner before the bot fires.
        if (IsBot(payload.CurrentPlayer))
        {
            PlayerSlot botSlot = payload.CurrentPlayer;
            GetTree().CreateTimer(2.5).Timeout += () => ExecuteBotTurn(botSlot);
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
        PlayerSlot localSlot2 = GetLocalPlayerSlot();
        if (winner.HasValue && winner.Value == localSlot2
            && _players.TryGetValue(localSlot2, out PlayerData? localP1))
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
    /// Returns true if the given player slot is controlled by AI.
    /// In local mode, only Player1 is human; in online mode, all lobby members are human.
    /// </summary>
    private bool IsBot(PlayerSlot slot)
    {
        return _players.TryGetValue(slot, out PlayerData? data) && data.IsBot;
    }

    /// <summary>
    /// Returns true if the given player slot belongs to THIS machine's local player.
    /// In local mode, that's always Player1. In online mode, it's whichever slot
    /// was assigned to our network peer ID.
    /// </summary>
    private bool IsLocalPlayer(PlayerSlot slot)
    {
        if (_networkManager == null || !_networkManager.IsOnline)
            return slot == PlayerSlot.Player1;

        return _players.TryGetValue(slot, out PlayerData? data)
            && data.PeerId == _networkManager.LocalPeerId;
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
            AdvanceTurnAuthoritative();
            return;
        }

        Random rng = new Random(System.Environment.TickCount ^ botSlot.GetHashCode() ^ (_turnManager?.RoundNumber ?? 0));

        // Find weapons that can fire this round
        if (!_weapons.TryGetValue(botSlot, out List<WeaponBase>? weaponList))
            weaponList = new List<WeaponBase>();

        // Purge destroyed/invalid weapons
        weaponList.RemoveAll(w => w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed);

        WeaponBase? weapon = weaponList.Find(w => w.CanFire(_turnManager.RoundNumber) &&
            !(_powerupExecutor?.IsWeaponEmpDisabled(w, _players) ?? false));

        // Still move troops even if no weapon can fire
        if (weapon == null)
        {
            _armyManager?.BotMoveTroops(botSlot);
            TryBotPowerupActivation(botSlot, rng);
            GD.Print($"[Bot] {botSlot} has no weapons ready — troops moved, skipping fire.");
            AdvanceTurnAuthoritative();
            return;
        }

        // Move bot's troops toward nearest enemy base
        _armyManager?.BotMoveTroops(botSlot);

        // Bot powerup usage: check if any should be activated this turn
        TryBotPowerupActivation(botSlot, rng);

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
            AdvanceTurnAuthoritative();
            return;
        }

        // Use the persistent BotController for difficulty-aware target selection
        AI.BotDifficulty difficulty = AI.BotDifficulty.Medium;
        if (_botControllers.TryGetValue(botSlot, out AI.BotController? botCtrl))
        {
            difficulty = botCtrl.Difficulty;
        }

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
            AdvanceTurnAuthoritative();
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

        // If the target zone is smoked, fire at a random position instead
        if (_powerupExecutor?.IsZoneSmoked(enemy.Slot, _players) == true)
        {
            Vector3 zoneWorldMin = MathHelpers.MicrovoxelToWorld(enemyZone.OriginMicrovoxels);
            Vector3 zoneWorldMax = MathHelpers.MicrovoxelToWorld(enemyZone.MaxMicrovoxelsInclusive);
            targetPos = new Vector3(
                (float)(zoneWorldMin.X + rng.NextDouble() * (zoneWorldMax.X - zoneWorldMin.X)),
                (float)(zoneWorldMin.Y + rng.NextDouble() * (zoneWorldMax.Y - zoneWorldMin.Y)),
                (float)(zoneWorldMin.Z + rng.NextDouble() * (zoneWorldMax.Z - zoneWorldMin.Z)));
            GD.Print($"[Bot] {botSlot} target zone is smoked — firing blind at {targetPos}");
        }

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

        // Check if this bot has troops — defer turn advancement for troop attack cam
        _deferTurnAdvanceForTroops = _armyManager?.HasAliveTroops(botSlot) ?? false;
        _troopSequencePlayer = botSlot;

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
                if (_troopSequenceActive) return; // troop sequence already running

                // If troops are deferred and CinematicFinished won't fire, start troop
                // sequence directly (same fallback as projectile path above).
                if (_deferTurnAdvanceForTroops && _armyManager != null)
                {
                    _deferTurnAdvanceForTroops = false;

                    // Skip troop attacks if backup bombardment is imminent
                    if (!_artilleryDominanceActive && AnyPlayerHasUsableWeapons())
                    {
                        PlayerSlot troopPlayer = _troopSequencePlayer;
                        var aliveP2 = new HashSet<PlayerSlot>();
                        foreach (var (s, d) in _players) { if (d.IsAlive) aliveP2.Add(s); }
                        var attackTargets = _armyManager.GetTroopsWithAttackTargets(troopPlayer, _commanders, _weapons, aliveP2);
                        if (attackTargets.Count > 0)
                        {
                            StartTroopAttackSequence(attackTargets, troopPlayer);
                            return;
                        }
                    }
                }

                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == botSlot)
                {
                    AdvanceTurnAuthoritative();
                }
            };
        }
    }

    /// <summary>
    /// Seeds bot players with random powerups at combat start.
    /// Easy: 0-1, Medium: 1-2, Hard: 2-3 powerups.
    /// </summary>
    private void SeedBotPowerups()
    {
        Random rng = new Random(System.Environment.TickCount);
        PowerupType[] allTypes = { PowerupType.SmokeScreen, PowerupType.Medkit, PowerupType.ShieldGenerator, PowerupType.AirstrikeBeacon, PowerupType.EmpBlast };

        foreach (var (slot, data) in _players)
        {
            if (!IsBot(slot)) continue;

            AI.BotDifficulty diff = AI.BotDifficulty.Medium;
            if (_botControllers.TryGetValue(slot, out AI.BotController? ctrl))
                diff = ctrl.Difficulty;

            int count = diff switch
            {
                AI.BotDifficulty.Easy => rng.Next(0, 2),
                AI.BotDifficulty.Medium => rng.Next(1, 3),
                AI.BotDifficulty.Hard => rng.Next(2, 4),
                _ => rng.Next(1, 3),
            };

            for (int i = 0; i < count; i++)
            {
                PowerupType type = allTypes[rng.Next(allTypes.Length)];
                data.Powerups.AddFree(type);
                GD.Print($"[Bot] {slot} received free {type}");
            }
        }
    }

    /// <summary>
    /// Bot AI: considers using a powerup this turn based on game state.
    /// Uses the same activation paths as human players.
    /// </summary>
    private void TryBotPowerupActivation(PlayerSlot botSlot, Random rng)
    {
        if (!_players.TryGetValue(botSlot, out PlayerData? botData) || _powerupExecutor == null) return;
        PowerupInventory inv = botData.Powerups;
        if (inv.OwnedPowerups.Count == 0) return;

        int alivePlayerCount = _players.Values.Count(p => p.IsAlive);

        // Medkit: use if commander HP < 50%
        if (inv.HasPowerup(PowerupType.Medkit) && _commanders.TryGetValue(botSlot, out var cmd))
        {
            Commander.CommanderHealth? health = cmd.GetNodeOrNull<Commander.CommanderHealth>("CommanderHealth");
            if (!cmd.IsDead && health != null && health.CurrentHealth < health.MaxHealth * 0.5f)
            {
                if (_powerupExecutor.ActivateMedkit(botData))
                    GD.Print($"[Bot] {botSlot} used Medkit");
                return;
            }
        }

        // Shield: 50% chance if not already active
        if (inv.HasPowerup(PowerupType.ShieldGenerator) && !inv.HasActiveEffect(PowerupType.ShieldGenerator) && rng.NextDouble() < 0.5)
        {
            if (_powerupExecutor.ActivateShieldGenerator(botData, alivePlayerCount))
                GD.Print($"[Bot] {botSlot} used Shield Generator");
            return;
        }

        // EMP: 60% chance
        if (inv.HasPowerup(PowerupType.EmpBlast) && rng.NextDouble() < 0.6)
        {
            if (_powerupExecutor.ActivateEmp(botData, _players, _weapons))
                GD.Print($"[Bot] {botSlot} used EMP Blast");
            return;
        }

        // Smoke: 30% chance
        if (inv.HasPowerup(PowerupType.SmokeScreen) && !inv.HasActiveEffect(PowerupType.SmokeScreen) && rng.NextDouble() < 0.3)
        {
            if (_powerupExecutor.ActivateSmokeScreen(botData, alivePlayerCount))
            {
                _powerupExecutor.ReenforceSmokeScreens(_players, _commanders);
                GD.Print($"[Bot] {botSlot} used Smoke Screen");
            }
            return;
        }

        // Airstrike: 70% chance, target nearest enemy zone center
        if (inv.HasPowerup(PowerupType.AirstrikeBeacon) && rng.NextDouble() < 0.7)
        {
            // Find nearest enemy zone center
            foreach (var (slot, data) in _players)
            {
                if (slot == botSlot || !data.IsAlive) continue;
                if (data.AssignedBuildZone is BuildZone zone)
                {
                    Vector3I target = zone.OriginBuildUnits + zone.SizeBuildUnits / 2;
                    if (_powerupExecutor.ActivateAirstrike(botData, target, slot))
                    {
                        botData.AirstrikesUsedThisRound++;
                        GD.Print($"[Bot] {botSlot} used Airstrike on {slot}");
                    }
                    break;
                }
            }
            return;
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
                if (_troopSequenceActive) return; // troop sequence already running

                // If troops are deferred and CinematicFinished won't fire (e.g. spectator
                // top-down mode skipped the projectile follow cam), start the troop
                // sequence directly from here instead of waiting for a signal that never comes.
                if (_deferTurnAdvanceForTroops && _armyManager != null)
                {
                    _deferTurnAdvanceForTroops = false;

                    // Skip troop attacks if backup bombardment is imminent
                    if (!_artilleryDominanceActive && AnyPlayerHasUsableWeapons())
                    {
                        PlayerSlot troopPlayer = _troopSequencePlayer;
                        var aliveP3 = new HashSet<PlayerSlot>();
                        foreach (var (s, d) in _players) { if (d.IsAlive) aliveP3.Add(s); }
                        var attackTargets = _armyManager.GetTroopsWithAttackTargets(troopPlayer, _commanders, _weapons, aliveP3);
                        if (attackTargets.Count > 0)
                        {
                            StartTroopAttackSequence(attackTargets, troopPlayer);
                            return;
                        }
                    }
                    // No targets — fall through to advance turn
                }

                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == botSlot)
                {
                    AdvanceTurnAuthoritative();
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

        // Re-enforce smoke screen chunk hiding after voxel destruction causes remesh
        if (payload.AfterData == 0 && payload.BeforeData != 0)
        {
            _powerupExecutor?.ReenforceSmokeScreens(_players, _commanders);
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

        // Refresh the CombatUI if the destroyed weapon belongs to the local player
        if (IsLocalPlayer(payload.Owner))
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
        // Deterministic seed — TickCount differs across machines in multiplayer.
        // Use salvo count + enemy slot hash so both clients produce the same height.
        Random rng = new Random(_bombardmentSalvoCount * 7919 + enemySlot.GetHashCode());
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
        _camera.ResetZoom();
        _camera.Activate();

        // Use the true map center (all zones, not just alive) so the camera stays
        // fixed over the middle of the map and doesn't shift as players die.
        // Straight top-down view showing the entire map — no Z offset.
        Vector3 mapCenter = ComputeMapCenter();
        float overviewHeight = 200f;
        Vector3 cameraPos = mapCenter + new Vector3(0f, overviewHeight, 0.1f); // tiny Z to avoid gimbal lock
        Vector3 lookTarget = mapCenter;

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
            // Award earnings to the local player (works for both online and local)
            PlayerSlot localSlot = GetLocalPlayerSlot();
            if (_pendingWinner.HasValue && _pendingWinner.Value == localSlot
                && _players.TryGetValue(localSlot, out PlayerData? localPlayer))
            {
                _progressionManager?.CommitMatchEarnings(localPlayer.Stats.MatchEarnings);
            }
            _pendingWinner = null;
            return;
        }

        if (CurrentPhase == GamePhase.Combat || CurrentPhase == GamePhase.GameOver)
        {
            // If the current player has troops, show the troop attack cam after a brief delay
            // so the player can watch the explosion before the camera moves to troops
            if (_deferTurnAdvanceForTroops && _armyManager != null)
            {
                _deferTurnAdvanceForTroops = false;

                // Skip troop attacks if backup bombardment is imminent (all weapons destroyed)
                if (_artilleryDominanceActive || !AnyPlayerHasUsableWeapons())
                {
                    GD.Print($"[TroopSeq] CinematicFinished — skipping troops (backup imminent)");
                    AdvanceTurnAuthoritative();
                    return;
                }

                PlayerSlot troopPlayer = _troopSequencePlayer;
                var aliveP4 = new HashSet<PlayerSlot>();
                foreach (var (s, d) in _players) { if (d.IsAlive) aliveP4.Add(s); }
                var attackTargets = _armyManager.GetTroopsWithAttackTargets(troopPlayer, _commanders, _weapons, aliveP4);
                GD.Print($"[TroopSeq] CinematicFinished — player={troopPlayer} attackTargets={attackTargets.Count}");
                if (attackTargets.Count > 0)
                {
                    // Mark troop sequence active IMMEDIATELY so WaitForProjectileThenAdvance
                    // timers don't race and advance the turn during the 1.2s delay.
                    _troopSequenceActive = true;

                    // Brief delay before switching to troop cam so impact lingers
                    GetTree().CreateTimer(0.5).Timeout += () =>
                    {
                        if (CurrentPhase != GamePhase.Combat)
                        {
                            _troopSequenceActive = false;
                            return;
                        }
                        StartTroopAttackSequence(attackTargets, troopPlayer);
                    };
                    return; // sequence handles camera return + turn advance
                }
                // No attack targets — advance turn now and return camera
                GD.Print("[TroopSeq] No attack targets, advancing turn immediately");
                AdvanceTurnAuthoritative();
            }

            // If a troop sequence is active (started by WaitForProjectileThenAdvance
            // which won the race against CinematicFinished), don't reposition the camera
            // — the troop sequence owns camera control until it finishes.
            if (_troopSequenceActive) return;

            // If a kill cam just finished that cancelled a troop sequence, advance the
            // turn now — the normal troop finish handler was suppressed.
            if (_advanceTurnAfterKillCam)
            {
                _advanceTurnAfterKillCam = false;
                AdvanceTurnAuthoritative();
                return;
            }

            // After a cinematic moment, respect the spectator top-down preference
            // so the player stays in their chosen view during non-local turns.
            bool isNonLocalTurn = _turnManager?.CurrentPlayer is PlayerSlot current && !IsLocalPlayer(current);
            if (isNonLocalTurn && _spectatorTopDown && _combatCamera != null)
            {
                _camera?.Deactivate();
                _combatCamera.TopDown(ComputeArenaMidpoint());
            }
            else
            {
                // Return to free-fly camera and position behind the current player's zone.
                SwitchToFreeFlyCamera();
                if (_turnManager?.CurrentPlayer is PlayerSlot currentSlot)
                {
                    PositionFreeFlyBehindZone(currentSlot);
                }
            }
        }
    }

    /// <summary>
    /// Plays a cinematic troop attack sequence: camera pans to the troop cluster,
    /// each troop attacks its target with staggered timing + VFX/SFX, then the
    /// turn advances and the camera returns to normal.
    /// </summary>
    private void StartTroopAttackSequence(List<(TroopEntity Troop, TroopAttackTarget Target)> attacks, PlayerSlot player)
    {
        _troopSequenceActive = true;
        _troopSequencePlayer = player;

        // Deactivate combat camera, use free-fly positioned to watch the troops
        _combatCamera?.Deactivate();
        SwitchToFreeFlyCamera();

        // Position camera to see the troop cluster — behind troops looking toward enemy base
        Vector3 clusterCenter = _armyManager?.GetTroopClusterCenter(player)
            ?? ComputePlayerFortressCenter(player);

        // Compute average target position (the enemy base/targets the troops are attacking)
        Vector3 targetCenter = Vector3.Zero;
        int targetCount = 0;
        foreach (var (_, target) in attacks)
        {
            targetCenter += target.WorldPosition;
            targetCount++;
        }
        if (targetCount > 0) targetCenter /= targetCount;
        else targetCenter = ComputeArenaMidpoint();

        // Direction from enemy targets toward troops (camera goes behind troops on this line)
        Vector3 troopToTarget = new Vector3(targetCenter.X - clusterCenter.X, 0f, targetCenter.Z - clusterCenter.Z);
        if (troopToTarget.LengthSquared() < 0.01f) troopToTarget = Vector3.Forward;
        troopToTarget = troopToTarget.Normalized();
        Vector3 sideDir = troopToTarget.Cross(Vector3.Up).Normalized();

        // Place camera BEHIND the troops (opposite side from enemy base), looking through
        // troops toward the targets — gives an over-the-shoulder dramatic view
        Vector3 cameraPos = clusterCenter - troopToTarget * 6f + Vector3.Up * 3f + sideDir * 1.5f;
        Vector3 lookTarget = (clusterCenter + targetCenter) * 0.5f + Vector3.Up * 0.5f;
        _camera?.TransitionToLookTarget(cameraPos, lookTarget);

        // Stagger each troop's attack with a short delay
        const float settleTime = 0.3f;
        const float delayBetween = 0.15f;

        for (int i = 0; i < attacks.Count; i++)
        {
            int idx = i;
            GetTree().CreateTimer(settleTime + delayBetween * i).Timeout += () =>
            {
                if (CurrentPhase != GamePhase.Combat) return;
                var (troop, target) = attacks[idx];
                if (!GodotObject.IsInstanceValid(troop) || troop.CurrentHP <= 0) return;

                // Re-find target if the original was destroyed by an earlier troop in sequence
                var liveTarget = target;
                if (_voxelWorld != null && _armyManager != null)
                {
                    bool targetStale = target.Kind switch
                    {
                        Army.TroopTargetKind.EnemyTroop => target.EnemyTroop == null || !IsInstanceValid(target.EnemyTroop) || target.EnemyTroop.CurrentHP <= 0,
                        Army.TroopTargetKind.Commander => target.EnemyCommander == null || !IsInstanceValid(target.EnemyCommander) || target.EnemyCommander.IsDead,
                        Army.TroopTargetKind.Weapon => target.EnemyWeapon == null || !IsInstanceValid(target.EnemyWeapon) || target.EnemyWeapon.IsDestroyed,
                        Army.TroopTargetKind.Voxel => !_voxelWorld.GetVoxel(target.VoxelPos).IsSolid,
                        _ => false
                    };
                    if (targetStale)
                    {
                        var aliveNow = new HashSet<PlayerSlot>();
                        foreach (var (s, d) in _players) { if (d.IsAlive) aliveNow.Add(s); }
                        var fresh = Army.TroopAI.FindBestTarget(troop, _voxelWorld, _buildZones,
                            _armyManager.GetDeployedTroops(), _commanders, _weapons, aliveNow);
                        if (fresh.HasValue) liveTarget = fresh.Value;
                        else return; // truly nothing to attack
                    }
                }

                // Execute the attack
                troop.FaceTarget(liveTarget.WorldPosition);
                troop.SetAIState(TroopAIState.Attacking);
                if (_voxelWorld != null)
                    Army.TroopAI.ExecuteAttack(troop, _voxelWorld, liveTarget);
                troop.PauseForAttack(0.4f);

                // Play SFX at the attack location
                Vector3 attackWorld = troop.GlobalPosition;
                AudioDirector.Instance?.PlaySFX("troop_attack", attackWorld);
            };
        }

        // After all attacks finish, linger briefly then advance turn and return camera
        float totalTime = settleTime + delayBetween * attacks.Count + 0.4f;
        GetTree().CreateTimer(totalTime).Timeout += FinishTroopAttackSequence;
    }

    private void FinishTroopAttackSequence()
    {
        // If the sequence was already cancelled (e.g. commander died → kill cam took over),
        // don't advance the turn — the kill cam's CinematicFinished handles flow from here.
        if (!_troopSequenceActive)
            return;

        _troopSequenceActive = false;

        if (CurrentPhase != GamePhase.Combat)
            return;

        // Advance the turn (was deferred for the troop sequence)
        AdvanceTurnAuthoritative();

        // Camera positioning is handled by OnTurnChanged (triggered by AdvanceTurn above).
        // No additional positioning needed here.
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
    /// Computes the true center of the entire map using ALL build zones (alive or dead).
    /// Unlike ComputeArenaMidpoint, this never shifts as players die — the camera stays
    /// fixed over the middle of the arena during bombardment.
    /// </summary>
    private Vector3 ComputeMapCenter()
    {
        if (_buildZones.Count == 0)
            return Vector3.Zero;

        Vector3 sum = Vector3.Zero;
        int count = 0;

        foreach ((PlayerSlot slot, BuildZone zone) in _buildZones)
        {
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
            combatUI.TroopMoveRequested += OnTroopMoveToggled;
        }
    }

    private void OnTroopMoveToggled()
    {
        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer || !IsLocalPlayer(currentPlayer)) return;
        if (!(_armyManager?.HasAliveTroops(currentPlayer) ?? false)) return;
        if (_troopsMoved) return; // already moved this turn

        // Cancel weapon targeting state if active — deactivate CombatCamera so it
        // stops consuming left-clicks, and switch to FreeFly without repositioning
        if (_isAiming || _weaponConfirmed || (_combatCamera != null && _combatCamera.IsInTargeting))
        {
            _isAiming = false;
            _hasTarget = false;
            _weaponConfirmed = false;
            _aimingSystem?.ClearTarget();
            _combatCamera?.ExitWeaponPOV();
            _combatCamera?.Deactivate(); // stop CombatCamera from consuming clicks
            if (_camera != null) _camera.Activate(); // activate FreeFly at current position (no reset)
            Input.MouseMode = Input.MouseModeEnum.Visible;
            HideTargetHighlight();
            ClearAllWeaponHighlights();
            CombatUI? cancelUI = GetNodeOrNull<CombatUI>("%CombatUI")
                ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
            cancelUI?.HideTargetSelector();
            cancelUI?.ResetPromptText();
        }

        _troopMoveMode = !_troopMoveMode;
        GD.Print($"[Combat] Troop move mode: {(_troopMoveMode ? "ON" : "OFF")}");

        CombatUI? ui = GetNodeOrNull<CombatUI>("%CombatUI")
            ?? GetTree().Root.FindChild("CombatUI", true, false) as CombatUI;
        ui?.SetTroopsModeActive(_troopMoveMode);

        if (_troopMoveMode)
            ui?.ShowTroopMovePrompt();
        else
            ui?.HideSelectWeaponPrompt();
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
            buildUI.SandboxExportRequested += (path) => ExportSandboxBuild(path, buildUI);
            buildUI.SandboxImportRequested += (path) => ImportSandboxBuild(path, buildUI);
            buildUI.PowerupBuyRequested += OnPowerupBuyRequested;
            buildUI.PowerupSellRequested += OnPowerupSellRequested;
            buildUI.TroopBuyRequested += OnTroopBuyRequested;
            buildUI.TroopSellRequested += OnTroopSellRequested;
            buildUI.BlueprintSelected += OnBlueprintSelected;
            buildUI.ReadyPressed += OnReadyPressed;
            buildUI.SymmetryChanged += (mode) => { if (_buildSystem != null) _buildSystem.SymmetryMode = mode; };
            buildUI.UndoRequested += () => UndoLastBuildAction();
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

        // Only the local player can fire during their turn
        if (!IsLocalPlayer(currentPlayer))
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

        // Clear stale ghost preview from previous placement mode (troop/commander)
        _ghostPreview?.Hide();

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

        // Clear stale ghost preview from previous placement mode (weapon/troop)
        _ghostPreview?.Hide();

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
                IsBot = i != 0, // Only Player1 (index 0) is human in local mode
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

        // Clear stale ghost preview from previous placement mode (weapon/commander)
        _ghostPreview?.Hide();

        // Clear any active blueprint so it doesn't interfere with troop placement
        if (_buildSystem != null)
        {
            _buildSystem.ActiveBlueprint = null;
            _buildSystem.CurrentToolMode = BuildToolMode.Single;
        }

        // Enter troop placement mode — the troop is bought when actually placed
        _selectedTroopType = type;
        _placementMode = PlacementMode.Troop;
        _isDragBuilding = false;
        TroopStats stats = TroopDefinitions.Get(type);
        GD.Print($"[Army] Entering {stats.Name} placement mode. Click to place.");
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
            // Remove marker first to get its position for undo cancellation
            Vector3I markerPos = RemoveLastTroopMarker(type);
            CancelUndoEntry(UndoType.Troop, position: markerPos);
            TroopStats stats = TroopDefinitions.Get(type);
            GD.Print($"[Army] {_activeBuilder}: Sold {stats.Name} for ${stats.Cost} refund. Budget: ${player.Budget}.");

            UpdateBuildUITroopCounts();

            // Emit budget changed so UI updates the budget display
            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, stats.Cost));
        }
    }

    /// <summary>
    /// Removes the last placed troop marker of the given type and returns its encoded position
    /// (for undo cancellation). Returns default if no marker found.
    /// </summary>
    private Vector3I RemoveLastTroopMarker(TroopType type)
    {
        string typeTag = $"_{type}_";
        string prefix = $"TroopMarker_{_activeBuilder}_";
        Node? lastMarker = null;
        foreach (Node child in GetChildren())
        {
            string name = child.Name.ToString();
            if (name.StartsWith(prefix) && name.Contains(typeTag) && child.IsInGroup("TroopMarkers"))
            {
                lastMarker = child;
            }
        }
        Vector3I pos = default;
        if (lastMarker != null)
        {
            pos = ParseTroopMarkerPosition(lastMarker.Name.ToString());
            lastMarker.QueueFree();
        }
        return pos;
    }

    /// <summary>
    /// Extracts the Vector3I position from a marker name like "TroopMarker_Player1_Infantry_(10, 0, 5)".
    /// </summary>
    private static Vector3I ParseTroopMarkerPosition(string markerName)
    {
        // Position is the last segment after the type: "TroopMarker_{player}_{type}_{pos}"
        // The pos part looks like "(10, 0, 5)" from Vector3I.ToString()
        int lastUnderscore = markerName.LastIndexOf('_');
        if (lastUnderscore < 0) return default;
        string posPart = markerName[(lastUnderscore + 1)..];
        // Parse "(X, Y, Z)" format
        posPart = posPart.Trim('(', ')');
        string[] parts = posPart.Split(',');
        if (parts.Length == 3 &&
            int.TryParse(parts[0].Trim(), out int x) &&
            int.TryParse(parts[1].Trim(), out int y) &&
            int.TryParse(parts[2].Trim(), out int z))
        {
            return new Vector3I(x, y, z);
        }
        return default;
    }

    /// <summary>
    /// Converts a door OutwardNormal vector to a rotation hint integer (0-3).
    /// 0=Forward(-Z), 1=Right(+X), 2=Back(+Z), 3=Left(-X).
    /// </summary>
    private static int NormalToRotationHint(Vector3 normal)
    {
        if (normal.X > 0.5f) return 1;  // Right
        if (normal.X < -0.5f) return 3; // Left
        if (normal.Z > 0.5f) return 2;  // Back
        return 0; // Forward (default)
    }

    /// <summary>
    /// Unified undo: pops the most recent build action (voxel, weapon, troop, or door)
    /// and reverses it, refunding costs as appropriate.
    /// </summary>
    private void UndoLastBuildAction()
    {
        // Skip cancelled entries (items already deleted via right-click/sell)
        while (_buildUndoStack.Count > 0)
        {
            BuildUndoEntry entry = _buildUndoStack.Pop();
            if (entry.Cancelled) continue;

            switch (entry.Type)
            {
                case UndoType.Voxel:
                    _buildSystem?.UndoLast(_activeBuilder);
                    return;
                case UndoType.Weapon:
                    UndoWeaponPlacement(entry);
                    return;
                case UndoType.Troop:
                    UndoTroopPlacement(entry);
                    return;
                case UndoType.Door:
                    UndoDoorPlacement(entry);
                    return;
            }
        }
    }

    private void UndoWeaponPlacement(BuildUndoEntry entry)
    {
        if (entry.Weapon == null || !GodotObject.IsInstanceValid(entry.Weapon)) return;
        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player)) return;
        if (!_weapons.TryGetValue(_activeBuilder, out var weaponList)) return;

        weaponList.Remove(entry.Weapon);
        player.WeaponIds.Remove(entry.Weapon.WeaponId);
        player.Refund(entry.Cost);
        entry.Weapon.QueueFree();
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, entry.Cost));
        AudioDirector.Instance?.PlaySFX("ui_click");
        GD.Print($"[Build] Undo: removed {entry.WeaponKind}, refund ${entry.Cost}.");
    }

    private void UndoTroopPlacement(BuildUndoEntry entry)
    {
        if (_armyManager == null) return;
        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player)) return;

        if (_armyManager.TrySellTroop(_activeBuilder, entry.TroopKind, player))
        {
            RemoveLastTroopMarker(entry.TroopKind);
            UpdateBuildUITroopCounts();
            EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(_activeBuilder, player.Budget, entry.Cost));
            AudioDirector.Instance?.PlaySFX("ui_click");
            GD.Print($"[Build] Undo: removed {entry.TroopKind}, refund ${entry.Cost}.");
        }
    }

    private void UndoDoorPlacement(BuildUndoEntry entry)
    {
        if (_armyManager == null) return;
        _armyManager.Doors.RemoveDoor(entry.Position, _activeBuilder);
        AudioDirector.Instance?.PlaySFX("ui_click");
        GD.Print($"[Build] Undo: removed door at {entry.Position}.");
    }

    /// <summary>
    /// Marks undo entries as cancelled when an item is manually deleted (right-click/sell).
    /// This prevents Ctrl+Z from "skipping" the next real action.
    /// </summary>
    private void CancelUndoEntry(UndoType type, WeaponBase? weapon = null, Vector3I position = default)
    {
        foreach (BuildUndoEntry entry in _buildUndoStack)
        {
            if (entry.Cancelled) continue;
            if (entry.Type != type) continue;

            bool match = type switch
            {
                UndoType.Weapon => entry.Weapon == weapon,
                UndoType.Door => entry.Position == position,
                UndoType.Troop => entry.Position == position,
                _ => false,
            };
            if (match)
            {
                entry.Cancelled = true;
                return;
            }
        }
    }

    private void OnWeaponSellRequested(WeaponType type)
    {
        GD.Print($"[Build] Weapon sell requested: {type} for {_activeBuilder} (phase={CurrentPhase})");

        if (CurrentPhase != GamePhase.Building)
        {
            GD.Print($"[Build] Sell rejected: wrong phase ({CurrentPhase})");
            return;
        }

        if (!_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            GD.Print($"[Build] Sell rejected: no player data for {_activeBuilder}");
            return;
        }

        if (!_weapons.TryGetValue(_activeBuilder, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            GD.Print($"[Build] {_activeBuilder}: No weapons to sell (list empty or missing).");
            ShowBuildWarning($"No {GetWeaponDisplayName(type)} to sell!");
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

        CancelUndoEntry(UndoType.Weapon, weapon: target);

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

        // Only the local player can activate powerups
        if (!IsLocalPlayer(currentPlayer))
            return;

        if (!_players.TryGetValue(currentPlayer, out PlayerData? player))
        {
            return;
        }

        if (!player.Powerups.HasPowerup(type))
        {
            GD.Print($"[Powerup] {currentPlayer}: No {type} in inventory.");
            return;
        }

        // Count alive players for rotation-based duration
        int alivePlayerCount = _players.Values.Count(p => p.IsAlive);

        bool success = false;
        switch (type)
        {
            case PowerupType.SmokeScreen:
                success = _powerupExecutor.ActivateSmokeScreen(player, alivePlayerCount);
                if (success) _powerupExecutor.ReenforceSmokeScreens(_players, _commanders);
                break;

            case PowerupType.Medkit:
                success = _powerupExecutor.ActivateMedkit(player);
                break;

            case PowerupType.ShieldGenerator:
                success = _powerupExecutor.ActivateShieldGenerator(player, alivePlayerCount);
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
                        success = _powerupExecutor.ActivateAirstrike(player, target, soloEnemy.Slot, out Vector3[] impacts, out int planes);
                        if (success)
                        {
                            player.AirstrikesUsedThisRound++;
                            // Sync airstrike result (with exact impact positions) to remote players
                            if (_networkManager?.IsOnline == true)
                            {
                                _syncManager?.SendAirstrikeResult(currentPlayer, soloEnemy.Slot, impacts, planes);
                            }
                        }
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
                success = _powerupExecutor.ActivateEmp(player, _players, _weapons);
                if (success && _networkManager?.IsOnline == true)
                {
                    // EMP is non-deterministic (random weapon selection).
                    // Send the exact result so remote clients apply the same disables.
                    SendEmpResultToRemote(currentPlayer);
                }
                break;
        }

        if (success)
        {
            // Sync powerup activation to remote players (except EMP which sends its own result)
            if (_networkManager?.IsOnline == true && type != PowerupType.EmpBlast)
            {
                _syncManager?.SendPowerupUsed(currentPlayer, (int)type);
            }

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
            bool success = _powerupExecutor.ActivateAirstrike(player, target, targetEnemy, out Vector3[] impacts, out int planes);
            if (success)
            {
                player.AirstrikesUsedThisRound++;

                // Sync airstrike result (with exact impact positions) to remote players
                if (_networkManager?.IsOnline == true)
                {
                    _syncManager?.SendAirstrikeResult(currentPlayer, targetEnemy, impacts, planes);
                }

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

        // Smoke screen: hide this player's weapons so the fortress looks fully invisible
        if (type == PowerupType.SmokeScreen)
            SetWeaponsVisible(slot, false);
    }

    private void OnPowerupExpired(PowerupType type, PlayerSlot slot)
    {
        EventBus.Instance?.EmitPowerupExpired(new PowerupExpiredEvent(type, slot));
        GD.Print($"[GameManager] Powerup {type} expired for {slot}");

        // Smoke screen cleared: show weapons again
        if (type == PowerupType.SmokeScreen)
            SetWeaponsVisible(slot, true);
    }

    private void SetWeaponsVisible(PlayerSlot slot, bool visible)
    {
        if (_weapons.TryGetValue(slot, out var weaponList))
        {
            foreach (var weapon in weaponList)
            {
                if (weapon != null && IsInstanceValid(weapon))
                    weapon.Visible = visible;
            }
        }
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
