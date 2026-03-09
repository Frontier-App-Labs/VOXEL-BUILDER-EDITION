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
    private GhostPreview? _ghostPreview;
    private FreeFlyCamera? _camera;
    private CombatCamera? _combatCamera;
    private VoxelGiSetup? _voxelGiSetup;
    private PowerupExecutor? _powerupExecutor;
    private float _phaseCountdownSeconds;

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

    // Combat phase interaction state
    private int _selectedWeaponIndex;
    private bool _isAiming;
    private bool _hasTarget; // true when the player has clicked a target point
    private MeshInstance3D? _targetHighlight; // wireframe cube highlighting hovered voxel
    private Vector3I _lastHoveredMicrovoxel = new(-9999, -9999, -9999);

    // UI (buttons only — labels handled by BuildUI / CombatUI)
    private Control? _hudRoot;
    private Button? _readyButton;
    private Button? _skipTurnButton;
    private SplashScreen? _splashScreen;
    private SplashScreen? _loadingSplash;
    private PauseMenu? _pauseMenu;
    private Label? _buildWarningLabel;
    private GameOverlayUI? _gameOverlayUI;

    // Commander naming popup (shown after build phase for human players)
    private PanelContainer? _namePopup;
    private LineEdit? _nameInput;
    private bool _awaitingName;

    // Track the active builder during build phase (hot-seat: each player builds in turn)
    private PlayerSlot _activeBuilder = PlayerSlot.Player1;
    private int _activeBuilderIndex;
    private static readonly PlayerSlot[] BuildOrder = { PlayerSlot.Player1, PlayerSlot.Player2, PlayerSlot.Player3, PlayerSlot.Player4 };

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
        }

        _steamPlatform?.Initialize();

        // Subscribe to CombatUI events (weapon selection + fire button)
        SubscribeToCombatUI();

        // Subscribe to BuildUI events (place weapon button)
        SubscribeToBuildUI();

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
        // Remove splash screen
        if (_splashScreen != null)
        {
            _splashScreen.SplashFinished -= OnSplashFinished;
            _splashScreen.QueueFree();
            _splashScreen = null;
        }

        // Generate preview terrain so the semi-transparent main menu has something behind it
        GenerateMenuBackgroundTerrain();

        // Show the main menu
        Control? mainMenu = GetNodeOrNull<Control>("MainMenu");
        if (mainMenu != null)
        {
            mainMenu.Visible = true;
        }

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

        _menuBattleTimer = 1.5f;
        _menuOrbitAngle = 0f;
        _menuFireRound = 0;
        _menuBattleActive = true;

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
        }

        if (_combatCamera != null)
        {
            _combatCamera.ExitWeaponPOVRequested -= OnExitWeaponPOVRequested;
            _combatCamera.TargetClickRequested -= OnTargetClickRequested;
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

        // Countdown timer for timed phases
        if (_phaseCountdownSeconds > 0f)
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
    /// Transitions the combat camera to targeting mode so the player
    /// can click a point on the enemy fortress to aim at.
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

        _selectedWeaponIndex = weaponIndex % weaponList.Count;

        // Transition to targeting mode
        TransitionToTargeting(currentPlayer);
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

        // Show a loading splash screen before starting the match
        _loadingSplash = new SplashScreen();
        _loadingSplash.Name = "LoadingSplash";
        _loadingSplash.IsLoadingMode = true;
        AddChild(_loadingSplash);

        // Hide the HUD during the splash
        if (_hudRoot != null) _hudRoot.Visible = false;

        // When the splash finishes (after explosion + fade), remove it and show HUD
        _loadingSplash.SplashFinished += OnLoadingSplashFinished;

        // Do setup on next frame so loading screen has a chance to render
        CallDeferred(nameof(PerformMatchSetupDuringLoading));
    }

    private void PerformMatchSetupDuringLoading()
    {
        if (_loadingSplash == null) return;

        // Reset players
        foreach (PlayerData player in _players.Values)
        {
            player.ResetForMatch(Settings);
        }

        _loadingSplash.SetLoadingProgress(0.2f);

        // Set up build zones
        SetupBuildZones();

        _loadingSplash.SetLoadingProgress(0.4f);

        // Clear world, generate terrain, decorate with zone borders and vegetation
        GenerateBuildFoundations();

        _loadingSplash.SetLoadingProgress(0.7f);

        // Set up VoxelGI and lighting
        SetupVoxelGiAndLighting();

        _loadingSplash.SetLoadingProgress(0.9f);

        // Position camera and prepare for build phase
        _activeBuilderIndex = 0;
        _activeBuilder = BuildOrder[_activeBuilderIndex];
        PositionCameraAtBuildZone(PlayerSlot.Player1);

        // Signal loading complete -- triggers castle explosion, then fade, then SplashFinished
        _loadingSplash.SetLoadingProgress(1.0f);
    }

    private void OnLoadingSplashFinished()
    {
        // Remove the loading splash
        if (_loadingSplash != null)
        {
            _loadingSplash.SplashFinished -= OnLoadingSplashFinished;
            _loadingSplash.QueueFree();
            _loadingSplash = null;
        }

        // Show the HUD again
        if (_hudRoot != null) _hudRoot.Visible = true;

        // Start the build phase
        SetPhase(GamePhase.Building, PrototypeBuildPhaseSeconds);
        _camera?.Activate();
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

        // Reset turn manager
        _turnManager?.StopTurnClock();

        // Reset aiming and placement state, release mouse if captured
        _isAiming = false;
        _selectedWeaponIndex = 0;
        _placementMode = PlacementMode.Block;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Hide ghost preview
        _ghostPreview?.Hide();
    }

    /// <summary>
    /// Performs the actual match setup after the loading splash finishes.
    /// </summary>
    private void StartMatchAfterSplash()
    {
        foreach (PlayerData player in _players.Values)
        {
            player.ResetForMatch(Settings);
        }

        SetupBuildZones();
        GenerateBuildFoundations();

        // Set up VoxelGI, environment lighting, and sun after terrain is in scene
        SetupVoxelGiAndLighting();

        // Start with Player1 building
        _activeBuilderIndex = 0;
        _activeBuilder = BuildOrder[_activeBuilderIndex];

        // Position camera at Player1's build zone
        PositionCameraAtBuildZone(PlayerSlot.Player1);

        SetPhase(GamePhase.Building, PrototypeBuildPhaseSeconds);

        // Explicitly activate the camera after phase change to ensure it's ready
        _camera?.Activate();
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
                _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                _selectedWeaponIndex = 0;
                _isAiming = false;
                _hasTarget = false;
                _aimingSystem?.ClearTarget();
                Input.MouseMode = Input.MouseModeEnum.Visible;
                if (_skipTurnButton != null) _skipTurnButton.Visible = true;
                if (_readyButton != null) _readyButton.Visible = false;

                // Deactivate FreeFlyCamera and switch to CombatCamera
                // Position behind the current player's fortress, facing arena center
                _camera?.Deactivate();
                if (_combatCamera != null)
                {
                    _combatCamera.Activate();
                    PlayerSlot firstCombatPlayer = _turnManager?.CurrentPlayer ?? PlayerSlot.Player1;
                    PositionCombatCameraBehindZone(firstCombatPlayer);
                }

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
                // Return to free look for post-game viewing
                _combatCamera?.FreeLook();
                break;
        }
    }

    private void OnPhaseTimerExpired()
    {
        switch (CurrentPhase)
        {
            case GamePhase.Building:
                // Advance to next player, or proceed to fog reveal if all done
                if (_activeBuilderIndex < _players.Count - 1)
                {
                    AdvanceToNextBuilder();
                    return;
                }
                SetPhase(GamePhase.FogReveal, PrototypeFogRevealSeconds);
                break;

            case GamePhase.FogReveal:
                SetPhase(GamePhase.Combat, 0f);
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
            if (isEraser)
            {
                // Eraser targets the hit voxel itself
                targetMicrovoxel = hitPos;
            }
            else
            {
                // Place mode: target the empty cell adjacent to the hit face
                targetMicrovoxel = hitPos + hitNormal;
            }

            // Convert to build unit
            _buildCursorBuildUnit = MathHelpers.MicrovoxelToBuild(targetMicrovoxel);

            // Validate placement within build zone
            _buildCursorValid = zone.ContainsBuildUnit(_buildCursorBuildUnit);

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
            else
            {
                _ghostPreview?.ShowSingleBlock(_buildCursorBuildUnit, _buildCursorValid, _buildSystem?.CurrentToolMode ?? BuildToolMode.Single);
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

        // Cycle material with scroll wheel
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                CycleBuildMaterial(1);
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                CycleBuildMaterial(-1);
                GetViewport().SetInputAsHandled();
            }
        }
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
        _commanders[_activeBuilder] = commander;

        if (_players.TryGetValue(_activeBuilder, out PlayerData? player))
        {
            player.CommanderMicrovoxelPosition = _buildCursorBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
            player.CommanderHealth = GameConfig.CommanderHP;
        }

        GD.Print($"[Build] Commander placed for {_activeBuilder} at {_buildCursorBuildUnit}.");
        _placementMode = PlacementMode.Block;
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

        if (!zone.ContainsBuildUnit(_buildCursorBuildUnit))
        {
            GD.Print("[Build] Weapon must be placed inside your build zone.");
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
        _weapons[_activeBuilder].Add(weapon);
        player.WeaponIds.Add(weapon.WeaponId);

        // Deduct the cost
        player.TrySpend(weaponCost);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, -weaponCost));

        GD.Print($"[Build] {_selectedWeaponType} placed for {_activeBuilder} at {_buildCursorBuildUnit} (cost: ${weaponCost}).");
        // Stay in weapon placement mode so the user can place more weapons
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
            WeaponType.MissileLauncher => 1000,
            WeaponType.Drill => 400,
            _ => 500,
        };
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

        // Show naming popup for human players before advancing
        if (!IsBot(_activeBuilder) && !_awaitingName)
        {
            ShowCommanderNamePopup();
            return;
        }

        FinalizeBuildReady();
    }

    private void FinalizeBuildReady()
    {
        if (_activeBuilderIndex < _players.Count - 1)
        {
            // Current player clicked ready, advance to next player
            AdvanceToNextBuilder();
        }
        else
        {
            // Last player clicked ready, proceed to fog reveal
            SetPhase(GamePhase.FogReveal, PrototypeFogRevealSeconds);
        }
    }

    private void ShowCommanderNamePopup()
    {
        _awaitingName = true;
        if (_readyButton != null) _readyButton.Visible = false;

        // Get default name for this player
        string[] defaultNames = { "Green", "Red", "Blue", "Grey" };
        int idx = Array.IndexOf(BuildOrder, _activeBuilder);
        string defaultName = idx >= 0 && idx < defaultNames.Length ? defaultNames[idx] : "Commander";

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
    //  COMBAT PHASE
    // ─────────────────────────────────────────────────

    private void ProcessCombatPhase(double delta)
    {
        // Update voxel hover highlight during targeting mode
        UpdateTargetingHighlight();
    }

    private void HandleCombatInput(InputEvent @event)
    {
        if (_turnManager?.CurrentPlayer is not PlayerSlot currentPlayer)
        {
            return;
        }

        // Space bar: fire if target is set, otherwise enter targeting mode
        if (@event.IsActionPressed("fire_weapon"))
        {
            if (_hasTarget && _aimingSystem != null && _aimingSystem.HasTarget)
            {
                FireCurrentPlayerWeapon();
            }
            else if (!_isAiming)
            {
                _isAiming = true;
                TransitionToTargeting(currentPlayer);
            }
        }

        // Cancel aiming with escape: return to top-down
        // (right-click and ESC in targeting mode are handled by CombatCamera events)
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_isAiming || (_combatCamera != null && _combatCamera.IsInTargeting))
            {
                CancelTargeting();
            }
        }

        // Cycle weapons with number keys while NOT in targeting mode
        if (_combatCamera?.IsInTargeting != true)
        {
            if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    CycleWeapon(currentPlayer, 1);
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    CycleWeapon(currentPlayer, -1);
                }
            }
        }
    }

    /// <summary>
    /// Transitions the combat camera to targeting mode where the player
    /// can orbit around the enemy fortress and click to set a target point.
    /// </summary>
    private void TransitionToTargeting(PlayerSlot player)
    {
        if (_combatCamera == null || _aimingSystem == null)
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

        // Clear any previous target
        _hasTarget = false;
        _aimingSystem.ClearTarget();

        // Find the pivot point: center of an enemy's build zone
        Vector3 pivot = FindEnemyPivot(player);

        // Activate combat camera in targeting mode
        _combatCamera.Activate();
        _combatCamera.EnterTargeting(pivot);
        _isAiming = true;

        GD.Print($"[Combat] Entering targeting mode. Click on enemy structures to set target.");
    }

    /// <summary>
    /// Finds the center of the nearest enemy build zone for use as the
    /// targeting camera pivot point.
    /// </summary>
    private Vector3 FindEnemyPivot(PlayerSlot currentPlayer)
    {
        Vector3 bestPivot = Vector3.Zero;
        float bestDist = float.MaxValue;

        // Get current weapon position for distance comparison
        Vector3 weaponPos = Vector3.Zero;
        if (_weapons.TryGetValue(currentPlayer, out List<WeaponBase>? weaponList) && weaponList.Count > 0)
        {
            int safeIndex = _selectedWeaponIndex % weaponList.Count;
            WeaponBase? weapon = weaponList[safeIndex];
            if (weapon != null && GodotObject.IsInstanceValid(weapon))
            {
                weaponPos = weapon.GlobalPosition;
            }
        }

        foreach ((PlayerSlot slot, BuildZone zone) in _buildZones)
        {
            if (slot == currentPlayer)
            {
                continue;
            }

            if (_players.TryGetValue(slot, out PlayerData? data) && !data.IsAlive)
            {
                continue;
            }

            Vector3I centerBU = zone.OriginBuildUnits + zone.SizeBuildUnits / 2;
            Vector3 centerWorld = MathHelpers.MicrovoxelToWorld(MathHelpers.BuildToMicrovoxel(centerBU));
            float dist = weaponPos.DistanceTo(centerWorld);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPivot = centerWorld;
            }
        }

        // Elevate pivot slightly so the camera orbits around the mid-height of the fortress,
        // not at ground level (which would cause the orbit to clip the terrain)
        bestPivot.Y = Mathf.Max(bestPivot.Y, 4f);

        return bestPivot;
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

        if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
        {
            // Convert microvoxel position to world position
            Vector3 targetWorld = MathHelpers.MicrovoxelToWorld(hitPos);

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
            bool inRange = _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetWorld, weapon.ProjectileSpeed, weapon.WeaponId);
            _hasTarget = true;

            if (inRange)
            {
                GD.Print($"[Combat] Target set at {targetWorld}. Click again or press SPACE/FIRE to shoot.");
            }
            else
            {
                GD.Print($"[Combat] Target at {targetWorld} is OUT OF RANGE. Aiming for maximum distance.");
            }
        }
    }

    /// <summary>
    /// Cancels the current targeting operation and returns to behind-zone view.
    /// </summary>
    private void CancelTargeting()
    {
        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();
        _combatCamera?.ExitWeaponPOV();
        if (_combatCamera != null)
        {
            // Return to behind-zone view facing arena center
            PlayerSlot currentSlot = _turnManager?.CurrentPlayer ?? PlayerSlot.Player1;
            PositionCombatCameraBehindZone(currentSlot);
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        HideTargetHighlight();
    }

    /// <summary>
    /// Per-frame hover highlight: raycasts from mouse position and shows a
    /// pulsing wireframe cube on the voxel under the cursor during targeting.
    /// </summary>
    private void UpdateTargetingHighlight()
    {
        if (_combatCamera == null || _voxelWorld == null)
        {
            HideTargetHighlight();
            return;
        }

        // Only show highlight when in targeting mode (not during projectile flight)
        // Show highlight in any combat camera mode where the player can see/click voxels
        bool isTargeting = CurrentPhase == GamePhase.Combat &&
            (_combatCamera.IsInTargeting ||
             _combatCamera.CurrentMode == CombatCamera.Mode.FreeLook ||
             _combatCamera.CurrentMode == CombatCamera.Mode.TopDown);

        if (!isTargeting)
        {
            HideTargetHighlight();
            return;
        }

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = _combatCamera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _combatCamera.ProjectRayNormal(mousePos);

        if (_voxelWorld.RaycastVoxel(rayOrigin, rayDir, MaxRaycastDistance, out Vector3I hitPos, out Vector3I _))
        {
            if (hitPos != _lastHoveredMicrovoxel)
            {
                _lastHoveredMicrovoxel = hitPos;
                ShowTargetHighlight(hitPos);
            }
        }
        else
        {
            HideTargetHighlight();
        }
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

    private void CycleWeapon(PlayerSlot player, int direction)
    {
        if (!_weapons.TryGetValue(player, out List<WeaponBase>? weaponList) || weaponList.Count == 0)
        {
            return;
        }

        _selectedWeaponIndex = ((_selectedWeaponIndex + direction) % weaponList.Count + weaponList.Count) % weaponList.Count;
        _isAiming = false;
        _hasTarget = false;
        _aimingSystem?.ClearTarget();
    }

    private void FireCurrentPlayerWeapon()
    {
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
            _isAiming = false;
            _hasTarget = false;
            _aimingSystem.ClearTarget();

            // Transition camera to follow projectile (cursor stays visible)
            if (_combatCamera != null && projectile != null && GodotObject.IsInstanceValid(projectile))
            {
                _combatCamera.FollowProjectile(projectile);
            }
            else if (_combatCamera != null)
            {
                // For hitscan weapons (railgun), just go to free look
                _combatCamera.FreeLook();
            }

            // Delay turn advance so the player can watch the projectile land and destruction play out
            PlayerSlot firedPlayer = currentPlayer;
            GetTree().CreateTimer(4.5).Timeout += () =>
            {
                if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == firedPlayer)
                {
                    _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
                }
            };
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
        foreach ((PlayerSlot slot, BuildZone zone) in _buildZones)
        {
            bool hasCommander = _commanders.TryGetValue(slot, out CommanderActor? existingCmd)
                && existingCmd != null && GodotObject.IsInstanceValid(existingCmd);
            bool hasWeapons = _weapons.TryGetValue(slot, out List<WeaponBase>? existingWeapons)
                && existingWeapons != null && existingWeapons.Count > 0;

            if (!hasCommander || !hasWeapons)
            {
                PlaceCommanderAndWeapons(slot, zone, placeCommander: !hasCommander, placeWeapons: !hasWeapons);
            }
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
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        if (_players.TryGetValue(payload.Victim, out PlayerData? player))
        {
            player.IsAlive = false;
            player.CommanderHealth = 0;
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
            SetPhase(GamePhase.GameOver);
            foreach ((PlayerSlot slot, PlayerData _) in _players)
            {
                _progressionManager?.AwardMatchCompleted(winner.HasValue && winner.Value == slot);
            }
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

        // Tick active powerup effects for the current player only (so durations
        // count in that player's turns, not every player's turns)
        _powerupExecutor?.TickAllPlayerEffects(_players, payload.CurrentPlayer);

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
        if (_players.TryGetValue(payload.CurrentPlayer, out PlayerData? turnPlayer))
        {
            turnPlayer.AirstrikesUsedThisRound = 0;
        }

        // Refresh the CombatUI weapon bar for the new player
        RefreshCombatUIWeapons(payload.CurrentPlayer);

        // If the combat camera is following a projectile or showing impact, let that finish
        // naturally (it will auto-transition to FreeLook, which we then override).
        // For immediate turns, position behind the current player's fortress facing arena center.
        if (_combatCamera.CurrentMode != CombatCamera.Mode.FollowProjectile &&
            _combatCamera.CurrentMode != CombatCamera.Mode.Impact &&
            _combatCamera.CurrentMode != CombatCamera.Mode.KillCam)
        {
            PositionCombatCameraBehindZone(payload.CurrentPlayer);
        }

        // Check if the current player has any usable weapons (not destroyed, can fire this round)
        bool hasUsableWeapons = false;
        if (_weapons.TryGetValue(payload.CurrentPlayer, out List<WeaponBase>? turnWeapons) && turnWeapons != null)
        {
            hasUsableWeapons = turnWeapons.Exists(w =>
                GodotObject.IsInstanceValid(w) && w.CanFire(_turnManager!.RoundNumber));
        }

        if (!hasUsableWeapons)
        {
            GD.Print($"[Combat] {payload.CurrentPlayer} has no usable weapons — auto-skipping turn.");
            PlayerSlot skipSlot = payload.CurrentPlayer;
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                if (CurrentPhase != GamePhase.Combat || _turnManager?.CurrentPlayer != skipSlot)
                    return;
                _turnManager.SkipTurn(Settings.TurnTimeSeconds);
            };
            return;
        }

        // If the current player is a bot, schedule automatic play after a short delay
        if (IsBot(payload.CurrentPlayer))
        {
            PlayerSlot botSlot = payload.CurrentPlayer;
            GetTree().CreateTimer(1.0).Timeout += () => ExecuteBotTurn(botSlot);
        }
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

        // --- Target selection (difficulty-aware) ---
        var enemy = AI.BotCombatPlanner.SelectTargetStatic(enemies, difficulty, rng);

        if (!_buildZones.TryGetValue(enemy.Slot, out BuildZone enemyZone))
        {
            GD.Print($"[Bot] {botSlot} can't find enemy build zone — skipping turn.");
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            return;
        }

        // --- Find the actual target position ---
        Vector3 targetPos = Vector3.Zero;

        // Priority 1: Check if the enemy commander is visible (line of sight)
        bool aimingAtCommander = false;
        if (_commanders.TryGetValue(enemy.Slot, out CommanderActor? enemyCommander)
            && enemyCommander != null && GodotObject.IsInstanceValid(enemyCommander))
        {
            Vector3 toCommander = enemyCommander.GlobalPosition - weapon.GlobalPosition;
            float distToCommander = toCommander.Length();
            Vector3 dirToCommander = toCommander.Normalized();

            if (!_voxelWorld.RaycastVoxel(weapon.GlobalPosition, dirToCommander, distToCommander, out _, out _))
            {
                // Clear line of sight to commander
                targetPos = enemyCommander.GlobalPosition;
                aimingAtCommander = true;
                GD.Print($"[Bot] {botSlot} has line of sight to {enemy.Slot} commander! Aiming directly.");
            }
        }

        if (!aimingAtCommander)
        {
            // Priority 2: Scan the enemy build zone for actual solid voxels
            targetPos = AI.BotCombatPlanner.FindSolidTargetInZone(
                _voxelWorld, enemyZone, weapon.GlobalPosition, difficulty, rng);
        }

        // Apply difficulty-based scatter to the target position
        float scatter = difficulty switch
        {
            AI.BotDifficulty.Easy => 3.0f,
            AI.BotDifficulty.Medium => 1.5f,
            AI.BotDifficulty.Hard => 0.5f,
            _ => 2.0f,
        };
        float scatterX = ((float)rng.NextDouble() - 0.5f) * 2f * scatter;
        float scatterZ = ((float)rng.NextDouble() - 0.5f) * 2f * scatter;
        float scatterY = ((float)rng.NextDouble() - 0.5f) * scatter * 0.5f;
        targetPos += new Vector3(scatterX, scatterY, scatterZ);

        // Use SetTargetPoint for accurate ballistic solution (same as player click-to-target)
        _aimingSystem.SetTargetPoint(weapon.GlobalPosition, targetPos, weapon.ProjectileSpeed, weapon.WeaponId);

        GD.Print($"[Bot] {botSlot} aiming {weapon.WeaponId} at {enemy.Slot} target ({targetPos})");

        // Fire the weapon
        ProjectileBase? projectile = weapon.Fire(_aimingSystem, _voxelWorld, _turnManager.RoundNumber);
        if (projectile != null)
        {
            if (_players.TryGetValue(botSlot, out PlayerData? botPlayer))
            {
                botPlayer.Stats.ShotsFired++;
            }

            // Follow the projectile with the camera
            if (_combatCamera != null && GodotObject.IsInstanceValid(projectile))
            {
                _combatCamera.FollowProjectile(projectile);
            }

            GD.Print($"[Bot] {botSlot} fired {weapon.WeaponId} at {enemy.Slot}.");
        }

        // End turn after a delay so the projectile has time to land
        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            if (CurrentPhase == GamePhase.Combat && _turnManager?.CurrentPlayer == botSlot)
            {
                _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
            }
        };
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

        // Refresh the CombatUI if this is the current player's weapon
        if (_turnManager?.CurrentPlayer == payload.Owner)
        {
            RefreshCombatUIWeapons(payload.Owner);
        }

        GD.Print($"[Combat] {payload.WeaponId} belonging to {payload.Owner} was destroyed at {payload.WorldPosition}.");
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
        float cameraHeight = zoneWidth * 0.85f;   // ~20m for 24m zone
        float cameraBack  = zoneWidth * 0.85f;    // ~20m back for ~45° angle

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
            };
            combatUI.FireRequested += OnFireRequestedFromUI;
            combatUI.PowerupActivateRequested += OnPowerupActivateRequested;
            combatUI.AirstrikeTargetSelected += OnAirstrikeTargetSelected;
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
            buildUI.PowerupBuyRequested += OnPowerupBuyRequested;
            buildUI.PowerupSellRequested += OnPowerupSellRequested;
            buildUI.BlueprintSelected += OnBlueprintSelected;
            buildUI.ReadyPressed += OnReadyPressed;
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
        // Find the SettingsUI and toggle its visibility
        SettingsUI? settingsUI = GetNodeOrNull<SettingsUI>("%SettingsUI")
            ?? GetTree().Root.FindChild("SettingsUI", true, false) as SettingsUI;
        if (settingsUI != null)
        {
            settingsUI.Visible = !settingsUI.Visible;
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
