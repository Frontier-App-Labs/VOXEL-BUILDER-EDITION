using Godot;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Networking;
using VoxelSiege.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Core;

public partial class GameManager : Node
{
    private readonly Dictionary<PlayerSlot, PlayerData> _players = new Dictionary<PlayerSlot, PlayerData>();
    private readonly Dictionary<PlayerSlot, CommanderActor> _commanders = new Dictionary<PlayerSlot, CommanderActor>();
    private readonly Dictionary<PlayerSlot, List<WeaponBase>> _weapons = new Dictionary<PlayerSlot, List<WeaponBase>>();
    private TurnManager? _turnManager;
    private VoxelWorld? _voxelWorld;
    private BuildSystem? _buildSystem;
    private WeaponPlacer? _weaponPlacer;
    private AimingSystem? _aimingSystem;
    private ProgressionManager? _progressionManager;
    private AchievementTracker? _achievementTracker;
    private SteamPlatformNode? _steamPlatform;
    private float _phaseCountdownSeconds;

    [Export]
    public bool AutoStartPrototypeMatch { get; set; }

    [Export]
    public float PrototypeBuildPhaseSeconds { get; set; } = 8f;

    [Export]
    public float PrototypeFogRevealSeconds { get; set; } = 3f;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Menu;
    public float PhaseCountdownSeconds => _phaseCountdownSeconds;
    public MatchSettings Settings { get; } = new MatchSettings();
    public IReadOnlyDictionary<PlayerSlot, PlayerData> Players => _players;

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

        EnsureDefaultInputMap();
        SeedLocalPlayers();
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled += OnCommanderKilled;
            EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
        }

        _steamPlatform?.Initialize();
        if (AutoStartPrototypeMatch)
        {
            StartPrototypeMatch();
        }
        else
        {
            SetPhase(GamePhase.Menu);
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled -= OnCommanderKilled;
            EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
        }
    }

    public override void _Process(double delta)
    {
        if (_phaseCountdownSeconds <= 0f)
        {
            return;
        }

        _phaseCountdownSeconds = Mathf.Max(0f, _phaseCountdownSeconds - (float)delta);
        if (_phaseCountdownSeconds > 0f)
        {
            return;
        }

        switch (CurrentPhase)
        {
            case GamePhase.Building:
                SetPhase(GamePhase.FogReveal, PrototypeFogRevealSeconds);
                break;
            case GamePhase.FogReveal:
                SetPhase(GamePhase.Combat, 0f);
                _turnManager?.Configure(_players.Keys, Settings.TurnTimeSeconds);
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("fire_weapon") && CurrentPhase == GamePhase.Combat)
        {
            FireCurrentPlayerWeapon();
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
        EventBus.Instance?.EmitPhaseChanged(new PhaseChangedEvent(previous, phase));
        _steamPlatform?.Platform.SetRichPresence("status", phase.ToString());
    }

    public PlayerData? GetPlayer(PlayerSlot slot)
    {
        return _players.TryGetValue(slot, out PlayerData? player) ? player : null;
    }

    public void StartPrototypeMatch()
    {
        foreach (PlayerData player in _players.Values)
        {
            player.ResetForMatch(Settings);
            player.Refund(100000);
        }

        BuildPrototypeFortresses();
        SetPhase(GamePhase.Building, PrototypeBuildPhaseSeconds);
    }

    private void BuildPrototypeFortresses()
    {
        if (_voxelWorld == null || _buildSystem == null || _weaponPlacer == null)
        {
            return;
        }

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

        foreach (CommanderActor commander in _commanders.Values)
        {
            if (GodotObject.IsInstanceValid(commander))
            {
                commander.QueueFree();
            }
        }

        _commanders.Clear();
        _weapons.Clear();
        _voxelWorld.ClearWorld();

        BuildZone zoneOne = new BuildZone(new Vector3I(-18, 2, -7), new Vector3I(12, 8, 12));
        BuildZone zoneTwo = new BuildZone(new Vector3I(6, 2, -7), new Vector3I(12, 8, 12));
        BuildPrototypeFortress(PlayerSlot.Player1, zoneOne, VoxelMaterialType.Stone, VoxelMaterialType.Metal);
        BuildPrototypeFortress(PlayerSlot.Player2, zoneTwo, VoxelMaterialType.Brick, VoxelMaterialType.ReinforcedSteel);
    }

    private void BuildPrototypeFortress(PlayerSlot slot, BuildZone zone, VoxelMaterialType shellMaterial, VoxelMaterialType chamberMaterial)
    {
        if (_voxelWorld == null || _weaponPlacer == null)
        {
            return;
        }

        Vector3I shellStart = zone.OriginBuildUnits + new Vector3I(1, 0, 1);
        Vector3I shellEnd = shellStart + new Vector3I(7, 4, 7);
        StampPrototypeBox(slot, shellMaterial, shellStart, shellEnd, hollow: true);
        StampPrototypeBox(slot, shellMaterial, shellStart + new Vector3I(1, 0, 1), shellEnd - new Vector3I(1, 2, 1), hollow: true);
        StampPrototypeBox(slot, chamberMaterial, shellStart + new Vector3I(2, 1, 2), shellStart + new Vector3I(4, 2, 4), hollow: true);
        StampPrototypeColumn(slot, chamberMaterial, shellStart + new Vector3I(3, 0, 3), shellStart + new Vector3I(3, 4, 3));
        StampPrototypeBox(slot, VoxelMaterialType.Glass, shellStart + new Vector3I(2, 3, 0), shellStart + new Vector3I(5, 3, 0), hollow: false);

        CommanderActor commander = new CommanderActor();
        commander.Name = $"Commander_{slot}";
        AddChild(commander);
        Vector3I commanderPosition = shellStart + new Vector3I(3, 1, 3);
        commander.OwnerSlot = slot;
        commander.PlaceCommander(_voxelWorld, commanderPosition);
        _commanders[slot] = commander;
        _players[slot].CommanderMicrovoxelPosition = commanderPosition * GameConfig.MicrovoxelsPerBuildUnit;
        _players[slot].CommanderHealth = GameConfig.CommanderHP;

        List<WeaponBase> weaponList = new List<WeaponBase>();
        weaponList.Add(_weaponPlacer.PlaceWeapon<Cannon>(this, _voxelWorld, shellStart + new Vector3I(3, 4, 0), slot));
        weaponList.Add(_weaponPlacer.PlaceWeapon<Mortar>(this, _voxelWorld, shellStart + new Vector3I(1, 4, 0), slot));
        weaponList.Add(_weaponPlacer.PlaceWeapon<Railgun>(this, _voxelWorld, shellStart + new Vector3I(5, 4, 0), slot));
        _weapons[slot] = weaponList;
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

        WeaponBase? weapon = weaponList.Find(candidate => GodotObject.IsInstanceValid(candidate) && candidate.CanFire(_turnManager.RoundNumber));
        if (weapon == null)
        {
            return;
        }

        PlayerSlot? targetSlot = FindTargetFor(currentPlayer);
        if (!targetSlot.HasValue || !_commanders.TryGetValue(targetSlot.Value, out CommanderActor? targetCommander))
        {
            return;
        }

        ConfigureAutoAim(weapon, targetCommander);
        int roundBefore = weapon.LastFiredRound;
        weapon.Fire(_aimingSystem, _voxelWorld, _turnManager.RoundNumber);
        if (weapon.LastFiredRound != roundBefore)
        {
            _players[currentPlayer].Stats.ShotsFired++;
            _turnManager.AdvanceTurn(Settings.TurnTimeSeconds);
        }
    }

    private void ConfigureAutoAim(WeaponBase weapon, CommanderActor targetCommander)
    {
        if (_aimingSystem == null)
        {
            return;
        }

        Vector3 delta = targetCommander.GlobalPosition - weapon.GlobalPosition;
        float flatDistance = new Vector2(delta.X, delta.Z).Length();
        _aimingSystem.YawRadians = Mathf.Atan2(delta.X, delta.Z);
        _aimingSystem.PitchRadians = -0.55f;
        _aimingSystem.PowerPercent = Mathf.Clamp(flatDistance / 24f, 0.45f, 1f);
    }

    private PlayerSlot? FindTargetFor(PlayerSlot attacker)
    {
        foreach ((PlayerSlot slot, PlayerData player) in _players)
        {
            if (slot != attacker && player.IsAlive)
            {
                return slot;
            }
        }

        return null;
    }

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

    private void StampPrototypeColumn(PlayerSlot slot, VoxelMaterialType material, Vector3I start, Vector3I end)
    {
        int minY = Mathf.Min(start.Y, end.Y);
        int maxY = Mathf.Max(start.Y, end.Y);
        for (int y = minY; y <= maxY; y++)
        {
            StampBuildUnit(slot, material, new Vector3I(start.X, y, start.Z));
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

    private void SeedLocalPlayers()
    {
        _players.Clear();
        _players[PlayerSlot.Player1] = new PlayerData
        {
            PeerId = 1,
            Slot = PlayerSlot.Player1,
            DisplayName = "Commander Green",
            PlayerColor = GameConfig.PlayerColors[0],
        };
        _players[PlayerSlot.Player2] = new PlayerData
        {
            PeerId = 2,
            Slot = PlayerSlot.Player2,
            DisplayName = "Commander Red",
            PlayerColor = GameConfig.PlayerColors[1],
        };
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

    private T CreateNode<T>(string name)
        where T : Node, new()
    {
        T node = new T();
        node.Name = name;
        AddChild(node);
        return node;
    }
}
