using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Camera;
using VoxelSiege.Combat;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class CombatUI : Control
{
    // --- Theme Colors ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color("161b22");
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color AccentRed = new Color("d73a49");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color BorderColor = new Color("30363d");
    private static readonly Color PanelBg = new Color(0.086f, 0.106f, 0.133f, 0.92f);

    // --- Impact crosshair color ---
    private static readonly Color ImpactCrosshairColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Weapon display data (icon lookup by weapon ID) ---
    private static readonly Dictionary<string, (string Name, string Icon)> WeaponDisplayLookup = new()
    {
        { "cannon",  ("Cannon",  "\u25cf") },
        { "mortar",  ("Mortar",  "\u25b2") },
        { "drill",   ("Drill",   "\u2699") },
        { "railgun", ("Railgun", "\u2501") },
        { "missile", ("Missile", "\u25c6") },
    };

    // --- Static fallback for initial build (all types) ---
    private static readonly (string Name, string Icon)[] WeaponDisplay =
    {
        ("Cannon", "\u25cf"),
        ("Mortar", "\u25b2"),
        ("Drill", "\u2699"),
        ("Railgun", "\u2501"),
        ("Missile", "\u25c6"),
    };

    // --- Events ---
    public event Action<int>? WeaponSelected;
    public event Action? FireRequested;
    public event Action<PowerupType>? PowerupActivateRequested;
    public event Action? DeployRequested;
    public event Action? TroopMoveRequested;

    /// <summary>
    /// Raised when the player selects an enemy target from the airstrike picker.
    /// The PlayerSlot is the chosen enemy target.
    /// </summary>
    public event Action<PlayerSlot>? AirstrikeTargetSelected;

    // --- Airstrike target picker ---
    private Control? _airstrikePickerOverlay;

    // --- Target enemy selector (shown during targeting mode) ---
    private PanelContainer? _targetSelectorPanel;

    /// <summary>
    /// Raised when the player clicks the left/right arrows in the target selector
    /// to cycle through enemy bases. The int parameter is +1 (next) or -1 (prev).
    /// </summary>
    public event Action<int>? TargetCycleRequested;

    // --- State ---
    private Label? _turnPlayerLabel;
    private Label? _roundLabel;
    private Label? _turnTimerLabel;
    private Label? _powerLabel;
    private Label? _angleLabel;
    private ColorRect? _powerFill;
    private float _turnTimer;
    private int _roundNumber;
    private PlayerSlot _currentPlayer;
    private PlayerSlot _localPlayerSlot = PlayerSlot.Player1;
    private string _commanderText = string.Empty;
    private readonly Dictionary<PlayerSlot, Label> _playerHealthLabels = new Dictionary<PlayerSlot, Label>();
    private readonly Dictionary<PlayerSlot, ColorRect> _playerHealthBars = new Dictionary<PlayerSlot, ColorRect>();
    private readonly Dictionary<PlayerSlot, Label> _playerStatusLabels = new Dictionary<PlayerSlot, Label>();
    private readonly Dictionary<PlayerSlot, Label> _playerNameLabels = new Dictionary<PlayerSlot, Label>();
    private readonly Dictionary<PlayerSlot, VBoxContainer> _playerEntries = new Dictionary<PlayerSlot, VBoxContainer>();
    private int _selectedWeaponIndex = -1; // -1 means no weapon selected
    private int _selectedGroupIndex = -1; // which weapon group is currently selected
    private readonly List<PanelContainer> _weaponButtons = new List<PanelContainer>();
    private HBoxContainer? _weaponRow;
    private readonly List<(string WeaponId, string Name, string Icon)> _activeWeapons = new();

    private readonly List<PanelContainer> _powerupSlots = new List<PanelContainer>();
    private readonly List<Label> _powerupCountLabels = new List<Label>();
    private GridContainer? _powerupContainer;

    // --- Weapon/Powerup bar visibility (hidden during bot turns) ---
    private Panel? _weaponBarPanel;
    private PanelContainer? _powerupBarPanel;

    // --- "SELECT WEAPON" flashing prompt ---
    private Label? _selectWeaponLabel;
    private Tween? _selectWeaponTween;

    // --- Deploy button state ---
    private PanelContainer? _deployPanel;
    private Button? _deployBtn;
    private Label? _deployWarningLabel;

    // --- Troops button (combat phase) ---
    private PanelContainer? _troopsPanel;
    private Button? _troopsBtn;
    private bool _troopsModeActive;

    // --- Impact crosshair state ---
    private Vector2 _impactScreenPos;
    private bool _impactCrosshairVisible;

    // --- Center reticle (for weapon POV) ---
    private Control? _centerCrosshairContainer;

    // --- Turn banner (centered name announcement on turn change) ---
    private Label? _turnBannerLabel;
    private Tween? _turnBannerTween;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopBar();
        BuildWeaponBar();
        BuildPowerupPanel();
        BuildDeployPanel();
        BuildTroopsButton();
        // Power meter and fire button removed — click-to-target auto-calculates trajectory
        BuildCrosshair();
        BuildTurnBanner();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.TurnChanged += OnTurnChanged;
            EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
        }

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
            EventBus.Instance.TurnChanged -= OnTurnChanged;
            EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
        }
    }

    /// <summary>
    /// Sets the local player slot so the UI knows which player is "you" in online mode.
    /// Must be called before combat starts.
    /// </summary>
    public void SetLocalPlayerSlot(PlayerSlot slot)
    {
        _localPlayerSlot = slot;
    }

    public override void _Process(double delta)
    {
        UpdateTurnTimer();
        UpdatePlayerStatus();
        UpdateImpactCrosshair();
    }

    public override void _Draw()
    {
        base._Draw();

        if (!_impactCrosshairVisible)
        {
            return;
        }

        // Draw impact crosshair at the predicted impact point (projected to screen)
        float halfSize = 10f;
        float thickness = 2f;

        // Horizontal line of "+"
        DrawLine(
            _impactScreenPos + new Vector2(-halfSize, 0),
            _impactScreenPos + new Vector2(halfSize, 0),
            ImpactCrosshairColor, thickness);

        // Vertical line of "+"
        DrawLine(
            _impactScreenPos + new Vector2(0, -halfSize),
            _impactScreenPos + new Vector2(0, halfSize),
            ImpactCrosshairColor, thickness);

        // Small diamond around the crosshair for visibility
        float diamondSize = 14f;
        DrawLine(
            _impactScreenPos + new Vector2(0, -diamondSize),
            _impactScreenPos + new Vector2(diamondSize, 0),
            ImpactCrosshairColor, 1f);
        DrawLine(
            _impactScreenPos + new Vector2(diamondSize, 0),
            _impactScreenPos + new Vector2(0, diamondSize),
            ImpactCrosshairColor, 1f);
        DrawLine(
            _impactScreenPos + new Vector2(0, diamondSize),
            _impactScreenPos + new Vector2(-diamondSize, 0),
            ImpactCrosshairColor, 1f);
        DrawLine(
            _impactScreenPos + new Vector2(-diamondSize, 0),
            _impactScreenPos + new Vector2(0, -diamondSize),
            ImpactCrosshairColor, 1f);
    }

    // ========== TOP BAR ==========
    private void BuildTopBar()
    {
        PanelContainer topBar = CreateBeveledPanel(PanelBg, AccentGold);
        topBar.SetAnchorsPreset(LayoutPreset.TopWide);
        topBar.OffsetBottom = 56;
        topBar.CustomMinimumSize = new Vector2(0, 56);
        topBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(topBar);

        MarginContainer topMargin = new MarginContainer();
        topMargin.AddThemeConstantOverride("margin_left", 20);
        topMargin.AddThemeConstantOverride("margin_right", 20);
        topMargin.AddThemeConstantOverride("margin_top", 8);
        topMargin.AddThemeConstantOverride("margin_bottom", 8);
        topMargin.MouseFilter = MouseFilterEnum.Ignore;
        topBar.AddChild(topMargin);

        HBoxContainer topContent = new HBoxContainer();
        topContent.AddThemeConstantOverride("separation", 24);
        topContent.MouseFilter = MouseFilterEnum.Ignore;
        topMargin.AddChild(topContent);

        // Turn indicator
        PanelContainer turnPanel = CreateStyledPanel(new Color(0, 0, 0, 0.3f), 0);
        turnPanel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(turnPanel);

        MarginContainer turnMargin = new MarginContainer();
        turnMargin.AddThemeConstantOverride("margin_left", 14);
        turnMargin.AddThemeConstantOverride("margin_right", 14);
        turnMargin.AddThemeConstantOverride("margin_top", 4);
        turnMargin.AddThemeConstantOverride("margin_bottom", 4);
        turnMargin.MouseFilter = MouseFilterEnum.Ignore;
        turnPanel.AddChild(turnMargin);

        HBoxContainer turnRow = new HBoxContainer();
        turnRow.AddThemeConstantOverride("separation", 10);
        turnRow.MouseFilter = MouseFilterEnum.Ignore;
        turnMargin.AddChild(turnRow);

        Label turnIcon = new Label();
        turnIcon.Text = "\u2694";
        turnIcon.AddThemeFontSizeOverride("font_size", 18);
        turnIcon.AddThemeColorOverride("font_color", AccentGold);
        turnIcon.MouseFilter = MouseFilterEnum.Ignore;
        turnRow.AddChild(turnIcon);

        _turnPlayerLabel = new Label();
        _turnPlayerLabel.Text = "PLAYER 1";
        _turnPlayerLabel.AddThemeFontOverride("font", PixelFont);
        _turnPlayerLabel.AddThemeFontSizeOverride("font_size", 12);
        _turnPlayerLabel.AddThemeColorOverride("font_color", TextPrimary);
        _turnPlayerLabel.MouseFilter = MouseFilterEnum.Ignore;
        turnRow.AddChild(_turnPlayerLabel);

        // Round number
        _roundLabel = new Label();
        _roundLabel.Text = "ROUND 1";
        _roundLabel.AddThemeFontOverride("font", PixelFont);
        _roundLabel.AddThemeFontSizeOverride("font_size", 10);
        _roundLabel.AddThemeColorOverride("font_color", TextSecondary);
        _roundLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _roundLabel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(_roundLabel);

        // Spacer
        Control spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(spacer);

        // Turn timer
        PanelContainer timerPanel = CreateStyledPanel(new Color(0, 0, 0, 0.3f), 0);
        timerPanel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(timerPanel);

        MarginContainer timerMargin = new MarginContainer();
        timerMargin.AddThemeConstantOverride("margin_left", 14);
        timerMargin.AddThemeConstantOverride("margin_right", 14);
        timerMargin.AddThemeConstantOverride("margin_top", 4);
        timerMargin.AddThemeConstantOverride("margin_bottom", 4);
        timerMargin.MouseFilter = MouseFilterEnum.Ignore;
        timerPanel.AddChild(timerMargin);

        HBoxContainer timerRow = new HBoxContainer();
        timerRow.AddThemeConstantOverride("separation", 8);
        timerRow.MouseFilter = MouseFilterEnum.Ignore;
        timerMargin.AddChild(timerRow);

        Label timerIcon = new Label();
        timerIcon.Text = "\u23f1";
        timerIcon.AddThemeFontSizeOverride("font_size", 16);
        timerIcon.AddThemeColorOverride("font_color", TextSecondary);
        timerIcon.MouseFilter = MouseFilterEnum.Ignore;
        timerRow.AddChild(timerIcon);

        _turnTimerLabel = new Label();
        _turnTimerLabel.Text = "60";
        _turnTimerLabel.AddThemeFontOverride("font", PixelFont);
        _turnTimerLabel.AddThemeFontSizeOverride("font_size", 14);
        _turnTimerLabel.AddThemeColorOverride("font_color", TextPrimary);
        _turnTimerLabel.MouseFilter = MouseFilterEnum.Ignore;
        timerRow.AddChild(_turnTimerLabel);
    }

    // ========== PLAYER STATUS PANEL (left side) ==========
    private void BuildPlayerStatusPanel()
    {
        PanelContainer statusPanel = CreateBeveledPanel(PanelBg, AccentGreen);
        // Use TopLeft anchor so the panel auto-sizes to its content height
        // instead of stretching full height with LeftWide
        statusPanel.AnchorLeft = 0;
        statusPanel.AnchorTop = 0;
        statusPanel.AnchorRight = 0;
        statusPanel.AnchorBottom = 0;
        statusPanel.OffsetLeft = 12;
        statusPanel.OffsetRight = 232;
        statusPanel.OffsetTop = 70;
        statusPanel.CustomMinimumSize = new Vector2(220, 0);
        statusPanel.ClipContents = true;
        statusPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(statusPanel);

        MarginContainer statusMargin = new MarginContainer();
        statusMargin.AddThemeConstantOverride("margin_left", 14);
        statusMargin.AddThemeConstantOverride("margin_right", 14);
        statusMargin.AddThemeConstantOverride("margin_top", 12);
        statusMargin.AddThemeConstantOverride("margin_bottom", 12);
        statusMargin.MouseFilter = MouseFilterEnum.Ignore;
        statusPanel.AddChild(statusMargin);

        VBoxContainer statusContainer = new VBoxContainer();
        statusContainer.AddThemeConstantOverride("separation", 6);
        statusContainer.MouseFilter = MouseFilterEnum.Ignore;
        statusMargin.AddChild(statusContainer);

        // Header
        Label statusHeader = new Label();
        statusHeader.Text = "COMMANDERS";
        statusHeader.AddThemeFontOverride("font", PixelFont);
        statusHeader.AddThemeFontSizeOverride("font_size", 10);
        statusHeader.AddThemeColorOverride("font_color", TextSecondary);
        statusHeader.MouseFilter = MouseFilterEnum.Ignore;
        statusContainer.AddChild(statusHeader);

        ColorRect headerLine = new ColorRect();
        headerLine.CustomMinimumSize = new Vector2(0, 1);
        headerLine.Color = BorderColor;
        headerLine.MouseFilter = MouseFilterEnum.Ignore;
        statusContainer.AddChild(headerLine);

        // Player entries (created for all 4 slots, hidden by default — shown dynamically)
        for (int i = 0; i < 4; i++)
        {
            PlayerSlot slot = (PlayerSlot)i;
            Color playerColor = i < GameConfig.PlayerColors.Length ? GameConfig.PlayerColors[i] : Colors.White;

            VBoxContainer playerEntry = new VBoxContainer();
            playerEntry.AddThemeConstantOverride("separation", 2);
            playerEntry.MouseFilter = MouseFilterEnum.Ignore;
            playerEntry.Visible = false; // Hidden until we confirm this player exists
            statusContainer.AddChild(playerEntry);
            _playerEntries[slot] = playerEntry;

            HBoxContainer nameRow = new HBoxContainer();
            nameRow.AddThemeConstantOverride("separation", 8);
            nameRow.MouseFilter = MouseFilterEnum.Ignore;
            playerEntry.AddChild(nameRow);

            // Color indicator
            ColorRect colorDot = new ColorRect();
            colorDot.CustomMinimumSize = new Vector2(8, 8);
            colorDot.Color = playerColor;
            colorDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            colorDot.MouseFilter = MouseFilterEnum.Ignore;
            nameRow.AddChild(colorDot);

            Label nameLabel = new Label();
            nameLabel.Text = $"Player {i + 1}";
            nameLabel.AddThemeFontOverride("font", PixelFont);
            nameLabel.AddThemeFontSizeOverride("font_size", 10);
            nameLabel.AddThemeColorOverride("font_color", TextPrimary);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLabel.ClipText = true;
            nameLabel.MouseFilter = MouseFilterEnum.Ignore;
            nameRow.AddChild(nameLabel);
            _playerNameLabels[slot] = nameLabel;

            Label statusLabel = new Label();
            statusLabel.Text = "ALIVE";
            statusLabel.AddThemeFontOverride("font", PixelFont);
            statusLabel.AddThemeFontSizeOverride("font_size", 10);
            statusLabel.AddThemeColorOverride("font_color", AccentGreen);
            statusLabel.MouseFilter = MouseFilterEnum.Ignore;
            nameRow.AddChild(statusLabel);
            _playerStatusLabels[slot] = statusLabel;

            // Health bar background — use Panel (not PanelContainer) so child anchors work
            Panel healthBg = new Panel();
            StyleBoxFlat healthBgStyle = new StyleBoxFlat();
            healthBgStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            healthBg.AddThemeStyleboxOverride("panel", healthBgStyle);
            healthBg.CustomMinimumSize = new Vector2(0, 8);
            healthBg.ClipContents = true;
            healthBg.MouseFilter = MouseFilterEnum.Ignore;
            playerEntry.AddChild(healthBg);

            ColorRect healthFill = new ColorRect();
            healthFill.AnchorLeft = 0;
            healthFill.AnchorRight = 1;
            healthFill.AnchorTop = 0;
            healthFill.AnchorBottom = 1;
            healthFill.OffsetLeft = 0;
            healthFill.OffsetRight = 0;
            healthFill.OffsetTop = 0;
            healthFill.OffsetBottom = 0;
            healthFill.Color = playerColor;
            healthFill.MouseFilter = MouseFilterEnum.Ignore;
            healthBg.AddChild(healthFill);
            _playerHealthBars[slot] = healthFill;

            Label hpLabel = new Label();
            hpLabel.Text = "100 HP";
            hpLabel.AddThemeFontOverride("font", PixelFont);
            hpLabel.AddThemeFontSizeOverride("font_size", 10);
            hpLabel.AddThemeColorOverride("font_color", TextSecondary);
            hpLabel.MouseFilter = MouseFilterEnum.Ignore;
            playerEntry.AddChild(hpLabel);
            _playerHealthLabels[slot] = hpLabel;
        }
    }

    // ========== WEAPON BAR (bottom center) ==========
    private void BuildWeaponBar()
    {
        // Use a Panel (NOT PanelContainer) so it never auto-resizes to children.
        // PanelContainer was causing the bar to shrink when buttons were rebuilt.
        Panel weaponBar = new Panel();
        StyleBoxFlat wbStyle = new StyleBoxFlat();
        wbStyle.BgColor = PanelBg;
        wbStyle.CornerRadiusTopLeft = 0;
        wbStyle.CornerRadiusTopRight = 0;
        wbStyle.CornerRadiusBottomLeft = 0;
        wbStyle.CornerRadiusBottomRight = 0;
        wbStyle.BorderWidthLeft = 4;
        wbStyle.BorderWidthBottom = 4;
        wbStyle.BorderWidthTop = 2;
        wbStyle.BorderWidthRight = 2;
        wbStyle.BorderColor = AccentGold;
        weaponBar.AddThemeStyleboxOverride("panel", wbStyle);
        weaponBar.AnchorLeft = 0.5f;
        weaponBar.AnchorRight = 0.5f;
        weaponBar.AnchorTop = 1;
        weaponBar.AnchorBottom = 1;
        weaponBar.GrowHorizontal = GrowDirection.Both;
        weaponBar.GrowVertical = GrowDirection.Begin;
        weaponBar.OffsetLeft = -260;
        weaponBar.OffsetRight = 260;
        weaponBar.OffsetTop = -100;
        weaponBar.OffsetBottom = -14;
        weaponBar.MouseFilter = MouseFilterEnum.Stop;
        weaponBar.ClipContents = false;
        AddChild(weaponBar);
        _weaponBarPanel = weaponBar;

        MarginContainer weapMargin = new MarginContainer();
        weapMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        weapMargin.AddThemeConstantOverride("margin_left", 12);
        weapMargin.AddThemeConstantOverride("margin_right", 12);
        weapMargin.AddThemeConstantOverride("margin_top", 12);
        weapMargin.AddThemeConstantOverride("margin_bottom", 12);
        weapMargin.MouseFilter = MouseFilterEnum.Ignore;
        weaponBar.AddChild(weapMargin);

        _weaponRow = new HBoxContainer();
        _weaponRow.AddThemeConstantOverride("separation", 8);
        _weaponRow.MouseFilter = MouseFilterEnum.Ignore;
        _weaponRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _weaponRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        weapMargin.AddChild(_weaponRow);

        // "SELECT WEAPON" flashing prompt above the weapon bar
        _selectWeaponLabel = new Label();
        _selectWeaponLabel.Text = "SELECT WEAPON";
        _selectWeaponLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _selectWeaponLabel.AddThemeFontOverride("font", PixelFont);
        _selectWeaponLabel.AddThemeFontSizeOverride("font_size", 14);
        _selectWeaponLabel.AddThemeColorOverride("font_color", AccentRed);
        _selectWeaponLabel.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _selectWeaponLabel.OffsetLeft = -120;
        _selectWeaponLabel.OffsetRight = 120;
        _selectWeaponLabel.OffsetTop = -130;
        _selectWeaponLabel.OffsetBottom = -108;
        _selectWeaponLabel.MouseFilter = MouseFilterEnum.Ignore;
        _selectWeaponLabel.Visible = false;
        AddChild(_selectWeaponLabel);

        // Initial placeholder — will be rebuilt by SetAvailableWeapons once combat starts
        RebuildWeaponButtons();
    }

    // ========== POWERUP PANEL (bottom left, beside weapon bar) ==========
    private void BuildPowerupPanel()
    {
        PanelContainer powerupBar = CreateBeveledPanel(PanelBg, new Color("3e96ff"));
        // Anchor to bottom-center and grow upward. Explicit anchors avoid
        // preset quirks that caused the panel to drift off-screen.
        powerupBar.AnchorLeft = 0.5f;
        powerupBar.AnchorRight = 0.5f;
        powerupBar.AnchorTop = 1f;
        powerupBar.AnchorBottom = 1f;
        powerupBar.OffsetLeft = -520;
        powerupBar.OffsetRight = -270;
        powerupBar.OffsetTop = -14;   // initial; DeferredPowerupResize sets final position
        powerupBar.OffsetBottom = -14;
        powerupBar.CustomMinimumSize = new Vector2(250, 0);
        powerupBar.MouseFilter = MouseFilterEnum.Stop;
        powerupBar.ClipContents = false;
        AddChild(powerupBar);
        _powerupBarPanel = powerupBar;

        MarginContainer pwrMargin = new MarginContainer();
        pwrMargin.AddThemeConstantOverride("margin_left", 8);
        pwrMargin.AddThemeConstantOverride("margin_right", 8);
        pwrMargin.AddThemeConstantOverride("margin_top", 6);
        pwrMargin.AddThemeConstantOverride("margin_bottom", 6);
        pwrMargin.MouseFilter = MouseFilterEnum.Ignore;
        powerupBar.AddChild(pwrMargin);

        VBoxContainer outerColumn = new VBoxContainer();
        outerColumn.AddThemeConstantOverride("separation", 2);
        outerColumn.MouseFilter = MouseFilterEnum.Ignore;
        pwrMargin.AddChild(outerColumn);

        // Header stays outside the scroll area so it is always visible
        Label pwrHeader = new Label();
        pwrHeader.Text = "POWERUPS";
        pwrHeader.AddThemeFontOverride("font", PixelFont);
        pwrHeader.AddThemeFontSizeOverride("font_size", 10);
        pwrHeader.AddThemeColorOverride("font_color", TextSecondary);
        pwrHeader.MouseFilter = MouseFilterEnum.Ignore;
        outerColumn.AddChild(pwrHeader);

        // Single-column list so powerup names are fully readable
        _powerupContainer = new GridContainer();
        _powerupContainer.Columns = 1;
        _powerupContainer.AddThemeConstantOverride("h_separation", 4);
        _powerupContainer.AddThemeConstantOverride("v_separation", 2);
        _powerupContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _powerupContainer.MouseFilter = MouseFilterEnum.Ignore;
        outerColumn.AddChild(_powerupContainer);

        // Powerup slots are populated dynamically based on inventory
        // Initial empty state
        Label emptyLabel = new Label();
        emptyLabel.Text = "No powerups";
        emptyLabel.AddThemeFontOverride("font", PixelFont);
        emptyLabel.AddThemeFontSizeOverride("font_size", 10);
        emptyLabel.AddThemeColorOverride("font_color", TextSecondary);
        emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
        emptyLabel.Name = "EmptyLabel";
        _powerupContainer.AddChild(emptyLabel);
    }

    /// <summary>
    /// Updates the powerup activation panel to reflect the current player's inventory.
    /// Called when the turn changes or after a powerup is used.
    /// </summary>
    public void UpdatePowerupSlots(PowerupInventory? inventory)
    {
        if (_powerupContainer == null)
        {
            return;
        }

        // Remove old slot buttons immediately (not QueueFree) so the PanelContainer
        // recalculates its size correctly before the new children are added.
        for (int i = _powerupContainer.GetChildCount() - 1; i >= 0; i--)
        {
            Node child = _powerupContainer.GetChild(i);
            _powerupContainer.RemoveChild(child);
            child.QueueFree();
        }
        _powerupSlots.Clear();
        _powerupCountLabels.Clear();

        if (inventory == null || inventory.OwnedPowerups.Count == 0)
        {
            Label emptyLabel = new Label();
            emptyLabel.Text = "No powerups";
            emptyLabel.AddThemeFontOverride("font", PixelFont);
            emptyLabel.AddThemeFontSizeOverride("font_size", 10);
            emptyLabel.AddThemeColorOverride("font_color", TextSecondary);
            emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
            _powerupContainer.AddChild(emptyLabel);
            return;
        }

        // Group by type and show unique types with count
        Dictionary<PowerupType, int> counts = new();
        foreach (PowerupType p in inventory.OwnedPowerups)
        {
            counts.TryGetValue(p, out int c);
            counts[p] = c + 1;
        }

        foreach ((PowerupType pType, int count) in counts)
        {
            PowerupDefinition def = PowerupDefinitions.Get(pType);

            PanelContainer slot = CreateStyledPanel(new Color(0, 0, 0, 0.3f), 0);
            slot.CustomMinimumSize = new Vector2(0, 24);
            slot.MouseFilter = MouseFilterEnum.Stop;
            _powerupSlots.Add(slot);

            HBoxContainer slotRow = new HBoxContainer();
            slotRow.AddThemeConstantOverride("separation", 4);
            slotRow.MouseFilter = MouseFilterEnum.Ignore;
            slot.AddChild(slotRow);

            MarginContainer iconMargin = new MarginContainer();
            iconMargin.AddThemeConstantOverride("margin_left", 4);
            iconMargin.MouseFilter = MouseFilterEnum.Ignore;
            slotRow.AddChild(iconMargin);

            Label icon = new Label();
            icon.Text = def.IconGlyph;
            icon.AddThemeFontSizeOverride("font_size", 12);
            icon.AddThemeColorOverride("font_color", def.AccentColor);
            icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            icon.MouseFilter = MouseFilterEnum.Ignore;
            iconMargin.AddChild(icon);

            Label nameLabel = new Label();
            nameLabel.Text = def.Name;
            nameLabel.AddThemeFontOverride("font", PixelFont);
            nameLabel.AddThemeFontSizeOverride("font_size", 10);
            nameLabel.AddThemeColorOverride("font_color", TextPrimary);
            nameLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLabel.ClipText = false;
            nameLabel.MouseFilter = MouseFilterEnum.Ignore;
            slotRow.AddChild(nameLabel);

            Label countLabel = new Label();
            countLabel.Text = $"x{count}";
            countLabel.AddThemeFontOverride("font", PixelFont);
            countLabel.AddThemeFontSizeOverride("font_size", 10);
            countLabel.AddThemeColorOverride("font_color", AccentGold);
            countLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            countLabel.MouseFilter = MouseFilterEnum.Ignore;
            slotRow.AddChild(countLabel);
            _powerupCountLabels.Add(countLabel);

            PowerupType capturedType = pType;
            Button clickArea = new Button();
            clickArea.Flat = true;
            clickArea.MouseFilter = MouseFilterEnum.Stop;
            clickArea.Modulate = new Color(1, 1, 1, 0);
            clickArea.Pressed += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_click");
                PowerupActivateRequested?.Invoke(capturedType);
                // Flash feedback
                slot.AddThemeStyleboxOverride("panel",
                    CreateFlatStyle(new Color(def.AccentColor.R, def.AccentColor.G, def.AccentColor.B, 0.3f), 0));
                GetTree().CreateTimer(0.3).Timeout += () =>
                {
                    if (GodotObject.IsInstanceValid(slot))
                    {
                        slot.AddThemeStyleboxOverride("panel",
                            CreateFlatStyle(new Color(0, 0, 0, 0.3f), 0));
                    }
                };
            };
            slot.AddChild(clickArea);
            clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            clickArea.OffsetLeft = 0;
            clickArea.OffsetRight = 0;
            clickArea.OffsetTop = 0;
            clickArea.OffsetBottom = 0;

            _powerupContainer.AddChild(slot);
        }

        // Defer positioning to next frame so children have computed their minimum sizes.
        // We do NOT call ResetSize() — it uses GrowDirection internally which is broken
        // for our bottom-anchored panel and overwrites OffsetBottom, pushing content off-screen.
        if (_powerupBarPanel != null)
        {
            CallDeferred(nameof(DeferredPowerupResize));
        }
    }

    private void DeferredPowerupResize()
    {
        if (_powerupBarPanel != null && IsInstanceValid(_powerupBarPanel))
        {
            // Manually position the panel above the bottom of the screen.
            // OffsetBottom = -14 (14px above screen bottom, fixed).
            // OffsetTop = -14 - contentHeight (panel extends upward).
            // This bypasses GrowDirection entirely.
            float contentHeight = _powerupBarPanel.GetCombinedMinimumSize().Y;
            if (contentHeight < 10f) contentHeight = 60f; // safety fallback
            _powerupBarPanel.OffsetBottom = -14f;
            _powerupBarPanel.OffsetTop = -14f - contentHeight;
        }
    }

    // ========== DEPLOY PANEL (bottom right, beside weapon bar) ==========
    private void BuildDeployPanel()
    {
        _deployPanel = CreateBeveledPanel(PanelBg, AccentGreen);
        // Anchor to bottom-right, mirroring the powerup panel position on the left
        _deployPanel.AnchorLeft = 0.5f;
        _deployPanel.AnchorRight = 0.5f;
        _deployPanel.AnchorTop = 1;
        _deployPanel.AnchorBottom = 1;
        _deployPanel.OffsetLeft = 210;
        _deployPanel.OffsetRight = 380;
        _deployPanel.OffsetTop = -100;
        _deployPanel.OffsetBottom = -14;
        _deployPanel.CustomMinimumSize = new Vector2(170, 86);
        _deployPanel.MouseFilter = MouseFilterEnum.Stop;
        _deployPanel.Visible = false; // Hidden by default until player has troops
        AddChild(_deployPanel);

        MarginContainer deployMargin = new MarginContainer();
        deployMargin.AddThemeConstantOverride("margin_left", 10);
        deployMargin.AddThemeConstantOverride("margin_right", 10);
        deployMargin.AddThemeConstantOverride("margin_top", 8);
        deployMargin.AddThemeConstantOverride("margin_bottom", 8);
        deployMargin.MouseFilter = MouseFilterEnum.Ignore;
        _deployPanel.AddChild(deployMargin);

        VBoxContainer deployCol = new VBoxContainer();
        deployCol.AddThemeConstantOverride("separation", 4);
        deployCol.MouseFilter = MouseFilterEnum.Ignore;
        deployMargin.AddChild(deployCol);

        _deployBtn = new Button();
        _deployBtn.Text = "DEPLOY [0]";
        _deployBtn.Flat = true;
        _deployBtn.AddThemeFontOverride("font", PixelFont);
        _deployBtn.AddThemeFontSizeOverride("font_size", 12);
        _deployBtn.AddThemeColorOverride("font_color", AccentGreen);
        _deployBtn.AddThemeColorOverride("font_hover_color", TextPrimary);
        _deployBtn.MouseFilter = MouseFilterEnum.Stop;
        _deployBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); DeployRequested?.Invoke(); };
        deployCol.AddChild(_deployBtn);

        // Warning label (shown when troops exist but no doors placed)
        _deployWarningLabel = new Label();
        _deployWarningLabel.Text = "NO DOORS";
        _deployWarningLabel.AddThemeFontOverride("font", PixelFont);
        _deployWarningLabel.AddThemeFontSizeOverride("font_size", 10);
        _deployWarningLabel.AddThemeColorOverride("font_color", AccentRed);
        _deployWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _deployWarningLabel.MouseFilter = MouseFilterEnum.Ignore;
        _deployWarningLabel.Visible = false;
        deployCol.AddChild(_deployWarningLabel);
    }

    /// <summary>
    /// Shows/hides the deploy button based on troop count and door availability.
    /// If troopCount > 0 and hasDoors, button is enabled.
    /// If troopCount > 0 but !hasDoors, shows "NO DOORS" warning text.
    /// If troopCount == 0, hides the button entirely.
    /// </summary>
    public void UpdateDeployButton(int troopCount, bool hasDoors)
    {
        if (_deployPanel == null || _deployBtn == null || _deployWarningLabel == null)
        {
            return;
        }

        if (troopCount <= 0)
        {
            _deployPanel.Visible = false;
            return;
        }

        _deployPanel.Visible = true;
        _deployBtn.Text = $"DEPLOY [{troopCount}]";

        if (hasDoors)
        {
            _deployBtn.Disabled = false;
            _deployBtn.AddThemeColorOverride("font_color", AccentGreen);
            _deployWarningLabel.Visible = false;
        }
        else
        {
            _deployBtn.Disabled = true;
            _deployBtn.AddThemeColorOverride("font_color", TextSecondary);
            _deployWarningLabel.Visible = true;
        }
    }

    // ========== TROOPS BUTTON (bottom right, combat phase) ==========
    private void BuildTroopsButton()
    {
        _troopsPanel = CreateBeveledPanel(PanelBg, AccentGreen);
        // Same right-side position as deploy panel — they never overlap
        // (deploy is build-phase only, troops is combat-phase only)
        _troopsPanel.AnchorLeft = 0.5f;
        _troopsPanel.AnchorRight = 0.5f;
        _troopsPanel.AnchorTop = 1;
        _troopsPanel.AnchorBottom = 1;
        _troopsPanel.OffsetLeft = 270;
        _troopsPanel.OffsetRight = 400;
        _troopsPanel.OffsetTop = -68;
        _troopsPanel.OffsetBottom = -14;
        _troopsPanel.CustomMinimumSize = new Vector2(130, 54);
        _troopsPanel.MouseFilter = MouseFilterEnum.Stop;
        _troopsPanel.Visible = false;
        AddChild(_troopsPanel);

        MarginContainer troopMargin = new MarginContainer();
        troopMargin.AddThemeConstantOverride("margin_left", 10);
        troopMargin.AddThemeConstantOverride("margin_right", 10);
        troopMargin.AddThemeConstantOverride("margin_top", 6);
        troopMargin.AddThemeConstantOverride("margin_bottom", 6);
        troopMargin.MouseFilter = MouseFilterEnum.Ignore;
        _troopsPanel.AddChild(troopMargin);

        VBoxContainer troopCol = new VBoxContainer();
        troopCol.AddThemeConstantOverride("separation", 2);
        troopCol.MouseFilter = MouseFilterEnum.Ignore;
        troopCol.Alignment = BoxContainer.AlignmentMode.Center;
        troopMargin.AddChild(troopCol);

        _troopsBtn = new Button();
        _troopsBtn.Text = "TROOPS";
        _troopsBtn.Flat = true;
        _troopsBtn.AddThemeFontOverride("font", PixelFont);
        _troopsBtn.AddThemeFontSizeOverride("font_size", 11);
        _troopsBtn.AddThemeColorOverride("font_color", AccentGreen);
        _troopsBtn.AddThemeColorOverride("font_hover_color", TextPrimary);
        _troopsBtn.MouseFilter = MouseFilterEnum.Stop;
        _troopsBtn.Pressed += () =>
        {
            AudioDirector.Instance?.PlaySFX("ui_click");
            _troopsModeActive = !_troopsModeActive;
            UpdateTroopsButtonHighlight();
            TroopMoveRequested?.Invoke();
        };
        troopCol.AddChild(_troopsBtn);

        Label hint = new Label();
        hint.Text = "[T]";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontOverride("font", PixelFont);
        hint.AddThemeFontSizeOverride("font_size", 8);
        hint.AddThemeColorOverride("font_color", TextSecondary);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        troopCol.AddChild(hint);
    }

    /// <summary>
    /// Shows/hides the TROOPS button. Only visible during combat when the player has alive troops.
    /// </summary>
    public void SetTroopsButtonVisible(bool visible)
    {
        if (_troopsPanel != null)
        {
            _troopsPanel.Visible = visible;
        }
        if (!visible)
        {
            _troopsModeActive = false;
            UpdateTroopsButtonHighlight();
        }
    }

    /// <summary>
    /// Resets the TROOPS button state (e.g. when troop move completes or turn changes).
    /// </summary>
    public void SetTroopsModeActive(bool active)
    {
        _troopsModeActive = active;
        UpdateTroopsButtonHighlight();
    }

    private void UpdateTroopsButtonHighlight()
    {
        if (_troopsBtn == null) return;
        _troopsBtn.AddThemeColorOverride("font_color", _troopsModeActive ? TextPrimary : AccentGreen);
        // Tint the panel background when active
        if (_troopsPanel != null)
        {
            StyleBoxFlat style = CreateFlatStyle(
                _troopsModeActive
                    ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.25f)
                    : PanelBg,
                2);
            style.BorderColor = AccentGreen;
            style.BorderWidthBottom = 1;
            style.BorderWidthTop = 1;
            style.BorderWidthLeft = 1;
            style.BorderWidthRight = 1;
            _troopsPanel.AddThemeStyleboxOverride("panel", style);
        }
    }

    /// <summary>
    /// Hides the weapon bar, powerup panel, and troops button.
    /// Called after the player fires so the UI is clean during projectile/impact cam.
    /// </summary>
    public void HideAttackUI()
    {
        if (_weaponBarPanel != null) _weaponBarPanel.Visible = false;
        if (_powerupBarPanel != null) _powerupBarPanel.Visible = false;
        if (_troopsPanel != null) _troopsPanel.Visible = false;
    }

    /// <summary>
    /// Shows the weapon bar, powerup panel, and troops button.
    /// Called when the human player's turn starts.
    /// </summary>
    public void ShowAttackUI()
    {
        if (_weaponBarPanel != null) _weaponBarPanel.Visible = true;
        if (_powerupBarPanel != null)
        {
            _powerupBarPanel.Visible = true;
            // Reposition after becoming visible — layout may not have updated while hidden
            CallDeferred(nameof(DeferredPowerupResize));
        }
        // Troops button visibility is controlled separately via SetTroopsButtonVisible
    }

    // ========== POWER METER (right side vertical bar) ==========
    private void BuildPowerMeter()
    {
        PanelContainer powerPanel = CreateStyledPanel(PanelBg, 0);
        powerPanel.SetAnchorsPreset(LayoutPreset.CenterRight);
        powerPanel.OffsetLeft = -72;
        powerPanel.OffsetRight = -12;
        powerPanel.OffsetTop = -150;
        powerPanel.OffsetBottom = 150;
        powerPanel.CustomMinimumSize = new Vector2(60, 300);
        powerPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(powerPanel);

        MarginContainer powerMargin = new MarginContainer();
        powerMargin.AddThemeConstantOverride("margin_left", 8);
        powerMargin.AddThemeConstantOverride("margin_right", 8);
        powerMargin.AddThemeConstantOverride("margin_top", 8);
        powerMargin.AddThemeConstantOverride("margin_bottom", 28);
        powerMargin.MouseFilter = MouseFilterEnum.Ignore;
        powerPanel.AddChild(powerMargin);

        VBoxContainer powerCol = new VBoxContainer();
        powerCol.AddThemeConstantOverride("separation", 4);
        powerCol.MouseFilter = MouseFilterEnum.Ignore;
        powerMargin.AddChild(powerCol);

        Label powerHeader = new Label();
        powerHeader.Text = "PWR";
        powerHeader.HorizontalAlignment = HorizontalAlignment.Center;
        powerHeader.AddThemeFontOverride("font", PixelFont);
        powerHeader.AddThemeFontSizeOverride("font_size", 10);
        powerHeader.AddThemeColorOverride("font_color", TextSecondary);
        powerHeader.MouseFilter = MouseFilterEnum.Ignore;
        powerCol.AddChild(powerHeader);

        // Bar background — use Panel (not PanelContainer) so child anchors work for fill sizing
        Panel barBg = new Panel();
        StyleBoxFlat barBgPowerStyle = new StyleBoxFlat();
        barBgPowerStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 1f);
        barBg.AddThemeStyleboxOverride("panel", barBgPowerStyle);
        barBg.SizeFlagsVertical = SizeFlags.ExpandFill;
        barBg.CustomMinimumSize = new Vector2(36, 0);
        barBg.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        barBg.ClipContents = true;
        barBg.MouseFilter = MouseFilterEnum.Ignore;
        powerCol.AddChild(barBg);

        _powerFill = new ColorRect();
        _powerFill.AnchorLeft = 0;
        _powerFill.AnchorRight = 1;
        _powerFill.AnchorTop = 1;
        _powerFill.AnchorBottom = 1;
        _powerFill.OffsetLeft = 0;
        _powerFill.OffsetRight = 0;
        _powerFill.OffsetTop = 0;
        _powerFill.OffsetBottom = 0;
        _powerFill.Color = AccentGreen;
        _powerFill.MouseFilter = MouseFilterEnum.Ignore;
        barBg.AddChild(_powerFill);

        _powerLabel = new Label();
        _powerLabel.Text = "75%";
        _powerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _powerLabel.AddThemeFontOverride("font", PixelFont);
        _powerLabel.AddThemeFontSizeOverride("font_size", 12);
        _powerLabel.AddThemeColorOverride("font_color", AccentGold);
        _powerLabel.MouseFilter = MouseFilterEnum.Ignore;
        powerCol.AddChild(_powerLabel);

        // Angle display below power
        _angleLabel = new Label();
        _angleLabel.Text = "ANG: 45\u00b0";
        _angleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _angleLabel.AddThemeFontOverride("font", PixelFont);
        _angleLabel.AddThemeFontSizeOverride("font_size", 10);
        _angleLabel.AddThemeColorOverride("font_color", TextSecondary);
        _angleLabel.MouseFilter = MouseFilterEnum.Ignore;
        powerCol.AddChild(_angleLabel);
    }

    // ========== CROSSHAIR (center aiming reticle) ==========
    private void BuildCrosshair()
    {
        _centerCrosshairContainer = new Control();
        _centerCrosshairContainer.SetAnchorsPreset(LayoutPreset.Center);
        _centerCrosshairContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_centerCrosshairContainer);

        // Horizontal line
        ColorRect hLine = new ColorRect();
        hLine.CustomMinimumSize = new Vector2(24, 2);
        hLine.Size = new Vector2(24, 2);
        hLine.Position = new Vector2(-12, -1);
        hLine.Color = new Color(1, 1, 1, 0.5f);
        hLine.MouseFilter = MouseFilterEnum.Ignore;
        _centerCrosshairContainer.AddChild(hLine);

        // Vertical line
        ColorRect vLine = new ColorRect();
        vLine.CustomMinimumSize = new Vector2(2, 24);
        vLine.Size = new Vector2(2, 24);
        vLine.Position = new Vector2(-1, -12);
        vLine.Color = new Color(1, 1, 1, 0.5f);
        vLine.MouseFilter = MouseFilterEnum.Ignore;
        _centerCrosshairContainer.AddChild(vLine);

        // Center dot
        ColorRect centerDot = new ColorRect();
        centerDot.CustomMinimumSize = new Vector2(4, 4);
        centerDot.Size = new Vector2(4, 4);
        centerDot.Position = new Vector2(-2, -2);
        centerDot.Color = AccentRed;
        centerDot.MouseFilter = MouseFilterEnum.Ignore;
        _centerCrosshairContainer.AddChild(centerDot);
    }

    // ========== TURN BANNER (centered name on turn change) ==========
    private void BuildTurnBanner()
    {
        _turnBannerLabel = new Label();
        _turnBannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _turnBannerLabel.VerticalAlignment = VerticalAlignment.Center;
        _turnBannerLabel.AddThemeFontOverride("font", PixelFont);
        _turnBannerLabel.AddThemeFontSizeOverride("font_size", 22);
        _turnBannerLabel.AddThemeColorOverride("font_color", AccentGold);
        _turnBannerLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
        _turnBannerLabel.OffsetLeft = -300;
        _turnBannerLabel.OffsetRight = 300;
        _turnBannerLabel.OffsetTop = 80;
        _turnBannerLabel.OffsetBottom = 120;
        _turnBannerLabel.MouseFilter = MouseFilterEnum.Ignore;
        _turnBannerLabel.Visible = false;
        AddChild(_turnBannerLabel);
    }

    /// <summary>
    /// Shows a centered commander name banner that fades out after ~2 seconds.
    /// Called on turn change so the player sees whose turn it is during the camera pan.
    /// </summary>
    public void ShowTurnBanner(string commanderName, Color playerColor)
    {
        if (_turnBannerLabel == null) return;

        _turnBannerLabel.Text = commanderName.ToUpper();
        _turnBannerLabel.AddThemeColorOverride("font_color", playerColor);
        _turnBannerLabel.Visible = true;
        _turnBannerLabel.Modulate = new Color(1, 1, 1, 1);

        _turnBannerTween?.Kill();
        _turnBannerTween = CreateTween();
        // Hold visible for 1.5s, then fade out over 0.8s
        _turnBannerTween.TweenInterval(1.5);
        _turnBannerTween.TweenProperty(_turnBannerLabel, "modulate:a", 0f, 0.8f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        _turnBannerTween.TweenCallback(Callable.From(() =>
        {
            if (_turnBannerLabel != null) _turnBannerLabel.Visible = false;
        }));
    }

    // ========== FIRE BUTTON ==========
    private void BuildFireButton()
    {
        PanelContainer firePanel = CreateStyledPanel(new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.25f), 0);
        firePanel.SetAnchorsPreset(LayoutPreset.CenterBottom);
        firePanel.OffsetLeft = 220;
        firePanel.OffsetRight = 360;
        firePanel.OffsetTop = -70;
        firePanel.OffsetBottom = -20;
        firePanel.CustomMinimumSize = new Vector2(140, 50);
        firePanel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(firePanel);

        Button fireBtn = new Button();
        fireBtn.Text = "FIRE  [SPACE]";
        fireBtn.Flat = true;
        fireBtn.AddThemeFontOverride("font", PixelFont);
        fireBtn.AddThemeFontSizeOverride("font_size", 12);
        fireBtn.AddThemeColorOverride("font_color", AccentRed);
        fireBtn.AddThemeColorOverride("font_hover_color", TextPrimary);
        fireBtn.MouseFilter = MouseFilterEnum.Stop;
        fireBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_confirm"); OnFirePressed(); };
        firePanel.AddChild(fireBtn);
        fireBtn.SetAnchorsPreset(LayoutPreset.FullRect);
        fireBtn.OffsetLeft = 0;
        fireBtn.OffsetRight = 0;
        fireBtn.OffsetTop = 0;
        fireBtn.OffsetBottom = 0;

        // Hover effect
        fireBtn.MouseEntered += () =>
        {
            AudioDirector.Instance?.PlaySFX("ui_hover");
            StyleBoxFlat hover = CreateFlatStyle(new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.4f), 0);
            hover.BorderWidthTop = 2;
            hover.BorderWidthBottom = 2;
            hover.BorderWidthLeft = 2;
            hover.BorderWidthRight = 2;
            hover.BorderColor = AccentRed;
            firePanel.AddThemeStyleboxOverride("panel", hover);
        };
        fireBtn.MouseExited += () =>
        {
            firePanel.AddThemeStyleboxOverride("panel", CreateFlatStyle(new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.25f), 0));
        };
    }

    // ========== Updates ==========
    private void UpdateTurnTimer()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm != null)
        {
            // During combat, the turn timer lives in TurnManager, not the phase countdown
            TurnManager? tm = gm.GetNodeOrNull<TurnManager>("TurnManager");
            if (tm != null && tm.IsRunning)
            {
                _turnTimer = tm.RemainingTurnTime;
            }
            else
            {
                _turnTimer = gm.PhaseCountdownSeconds;
            }
        }

        if (_turnTimerLabel != null)
        {
            if (float.IsInfinity(_turnTimer))
            {
                _turnTimerLabel.Text = "\u221e";
            }
            else
            {
                _turnTimerLabel.Text = $"{Mathf.CeilToInt(_turnTimer)}s";
            }
            bool urgent = _turnTimer < 10f && _turnTimer > 0f && !float.IsInfinity(_turnTimer);
            _turnTimerLabel.AddThemeColorOverride("font_color", urgent ? AccentRed : TextPrimary);
        }
    }

    private void UpdatePlayerStatus()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm == null) return;

        // Show only player entries that actually exist in the game; hide the rest
        for (int i = 0; i < 4; i++)
        {
            PlayerSlot slot = (PlayerSlot)i;
            bool isActive = gm.Players.ContainsKey(slot);
            if (_playerEntries.TryGetValue(slot, out VBoxContainer? entry))
            {
                entry.Visible = isActive;
            }
        }

        foreach (var (slot, player) in gm.Players)
        {
            if (_playerNameLabels.TryGetValue(slot, out Label? nameLabel))
            {
                nameLabel.Text = player.DisplayName;
            }

            if (_playerHealthLabels.TryGetValue(slot, out Label? hpLabel))
            {
                hpLabel.Text = $"{player.CommanderHealth} HP";
            }

            if (_playerHealthBars.TryGetValue(slot, out ColorRect? bar))
            {
                float healthPercent = Mathf.Clamp(player.CommanderHealth / (float)GameConfig.CommanderHP, 0f, 1f);
                bar.AnchorRight = healthPercent;
                // Reset offset after anchor change — Godot 4 preserves the absolute
                // position by adjusting offsets when anchors change, which prevents
                // the bar from visually shrinking.
                bar.OffsetRight = 0;
                bar.Color = healthPercent > 0.5f
                    ? player.PlayerColor
                    : healthPercent > 0.25f ? AccentGold : AccentRed;
            }

            if (_playerStatusLabels.TryGetValue(slot, out Label? statusLabel))
            {
                statusLabel.Text = player.IsAlive ? "ALIVE" : "DEAD";
                statusLabel.AddThemeColorOverride("font_color", player.IsAlive ? AccentGreen : AccentRed);
            }
        }
    }

    private void UpdatePowerMeter()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        Node? aimNode = gm?.GetNodeOrNull("AimingSystem");

        float power = 0.75f;
        float pitch = -0.2f;
        if (aimNode != null)
        {
            power = (float)aimNode.Get("PowerPercent");
            pitch = (float)aimNode.Get("PitchRadians");
        }

        // Clamp power to 0-1 range to prevent the fill bar from exceeding its container
        power = Mathf.Clamp(power, 0f, 1f);

        if (_powerLabel != null)
        {
            _powerLabel.Text = $"{(int)(power * 100)}%";
        }

        if (_powerFill != null)
        {
            // Use AnchorTop to size the fill from the bottom (avoids non-equal anchor warning)
            _powerFill.AnchorTop = 1.0f - power;
            _powerFill.Color = power > 0.8f ? AccentRed : power > 0.5f ? AccentGold : AccentGreen;
        }

        if (_angleLabel != null)
        {
            float degrees = Mathf.Abs(Mathf.RadToDeg(pitch));
            _angleLabel.Text = $"ANG: {degrees:F0}\u00b0";
        }
    }

    /// <summary>
    /// Updates the impact crosshair position. In targeting mode, shows the
    /// crosshair at the target point that the player has clicked on.
    /// Falls back to trajectory-based prediction for weapon POV mode.
    /// </summary>
    private void UpdateImpactCrosshair()
    {
        _impactCrosshairVisible = false;

        CombatCamera? combatCam = GetViewport()?.GetCamera3D() as CombatCamera;
        if (combatCam == null)
        {
            if (_centerCrosshairContainer != null)
            {
                _centerCrosshairContainer.Visible = false;
            }
            QueueRedraw();
            return;
        }

        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm == null)
        {
            if (_centerCrosshairContainer != null)
            {
                _centerCrosshairContainer.Visible = false;
            }
            QueueRedraw();
            return;
        }

        AimingSystem? aiming = gm.GetNodeOrNull<AimingSystem>("AimingSystem");
        WeaponBase? weapon = gm.GetSelectedWeapon();

        // In targeting mode, show the crosshair at the clicked target point
        if (combatCam.IsInTargeting && aiming != null && aiming.HasTarget && weapon != null)
        {
            // Hide center reticle (cursor is visible in targeting mode)
            if (_centerCrosshairContainer != null)
            {
                _centerCrosshairContainer.Visible = false;
            }

            // Show impact crosshair at the target point
            Vector3 targetPoint = aiming.TargetPoint;
            if (!combatCam.IsPositionBehind(targetPoint))
            {
                _impactScreenPos = combatCam.UnprojectPosition(targetPoint);
                _impactCrosshairVisible = true;
            }

            QueueRedraw();
            return;
        }

        // In weapon POV mode, show center reticle and trajectory-based impact crosshair
        if (combatCam.IsInWeaponPOV)
        {
            if (_centerCrosshairContainer != null)
            {
                _centerCrosshairContainer.Visible = true;
            }

            if (aiming != null && weapon != null)
            {
                Vector3[] trajectoryPoints = aiming.SampleTrajectory(weapon.GlobalPosition, weapon.ProjectileSpeed, 60, 0.08f);
                if (trajectoryPoints.Length > 0)
                {
                    Vector3 impactPoint = trajectoryPoints[trajectoryPoints.Length - 1];
                    if (!combatCam.IsPositionBehind(impactPoint))
                    {
                        Vector3 camForward = -combatCam.GlobalTransform.Basis.Z;
                        Vector3 toImpact = (impactPoint - combatCam.GlobalPosition).Normalized();
                        if (camForward.Dot(toImpact) >= 0f)
                        {
                            _impactScreenPos = combatCam.UnprojectPosition(impactPoint);
                            _impactCrosshairVisible = true;
                        }
                    }
                }
            }

            QueueRedraw();
            return;
        }

        // Not in targeting or weapon POV -- hide everything
        if (_centerCrosshairContainer != null)
        {
            _centerCrosshairContainer.Visible = false;
        }
        QueueRedraw();
    }

    /// <summary>
    /// Weapon group: tracks a weapon type's display info and which flat indices
    /// in the weapon list correspond to instances of this type.
    /// </summary>
    private struct WeaponGroup
    {
        public string WeaponId;
        public string Name;
        public string Icon;
        public List<int> FlatIndices; // indices into GameManager's weapon list
        public int SelectedInstance;  // which instance within this group is selected (0-based)
    }

    private readonly List<WeaponGroup> _weaponGroups = new();

    /// <summary>
    /// Called by GameManager to set the list of actually-placed weapons for the current player.
    /// Groups weapons by type so the UI shows one tile per type with a count badge.
    /// </summary>
    public void SetAvailableWeapons(List<WeaponBase>? weapons)
    {
        _activeWeapons.Clear();
        _weaponGroups.Clear();

        if (weapons != null)
        {
            // Build flat list and group by weapon ID
            var groupMap = new Dictionary<string, int>(); // weaponId -> index in _weaponGroups
            int flatIndex = 0;

            foreach (WeaponBase w in weapons)
            {
                if (w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed)
                {
                    flatIndex++;
                    continue;
                }

                string id = w.WeaponId;
                string name = id;
                string icon = "?";
                if (WeaponDisplayLookup.TryGetValue(id, out var display))
                {
                    name = display.Name;
                    icon = display.Icon;
                }

                _activeWeapons.Add((id, name, icon));

                if (groupMap.TryGetValue(id, out int groupIdx))
                {
                    var group = _weaponGroups[groupIdx];
                    group.FlatIndices.Add(flatIndex);
                    _weaponGroups[groupIdx] = group;
                }
                else
                {
                    groupMap[id] = _weaponGroups.Count;
                    _weaponGroups.Add(new WeaponGroup
                    {
                        WeaponId = id,
                        Name = name,
                        Icon = icon,
                        FlatIndices = new List<int> { flatIndex },
                        SelectedInstance = 0,
                    });
                }

                flatIndex++;
            }
        }

        _selectedWeaponIndex = -1; // No weapon selected until player clicks one
        _selectedGroupIndex = -1;
        RebuildWeaponButtons();

        // Do NOT show the "SELECT WEAPON" prompt here.  OnTurnChanged is the
        // single authority that decides when the prompt appears (only at the
        // start of the local human player's combat turn).  Showing it here
        // would cause it to flash during bot turns or persist incorrectly.
    }

    /// <summary>
    /// Rebuilds the weapon button row from grouped weapons. Each weapon type
    /// gets one tile with a count badge (e.g., "x3") and left/right arrows
    /// to cycle through instances when there are multiples.
    /// </summary>
    private void RebuildWeaponButtons()
    {
        if (_weaponRow == null)
        {
            return;
        }

        // Remove from layout immediately but defer destruction to avoid
        // "Object freed while signal emitted" errors during button callbacks
        _weaponButtons.Clear();
        var oldChildren = _weaponRow.GetChildren();
        for (int i = oldChildren.Count - 1; i >= 0; i--)
        {
            _weaponRow.RemoveChild(oldChildren[i]);
            oldChildren[i].QueueFree();
        }

        if (_weaponGroups.Count == 0)
        {
            // Show "No Weapons" placeholder
            Label noWeapons = new Label();
            noWeapons.Text = "NO WEAPONS";
            noWeapons.HorizontalAlignment = HorizontalAlignment.Center;
            noWeapons.AddThemeFontOverride("font", PixelFont);
            noWeapons.AddThemeFontSizeOverride("font_size", 12);
            noWeapons.AddThemeColorOverride("font_color", TextSecondary);
            noWeapons.MouseFilter = MouseFilterEnum.Ignore;
            noWeapons.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            noWeapons.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _weaponRow.AddChild(noWeapons);
            return;
        }

        for (int g = 0; g < _weaponGroups.Count; g++)
        {
            WeaponGroup group = _weaponGroups[g];
            int currentFlatIndex = group.FlatIndices[group.SelectedInstance];
            bool isSelected = currentFlatIndex == _selectedWeaponIndex;
            int capturedGroup = g;

            PanelContainer weapBtn = CreateStyledPanel(
                isSelected
                    ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.25f)
                    : new Color(0.1f, 0.1f, 0.15f, 0.8f),
                0);
            weapBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            weapBtn.CustomMinimumSize = new Vector2(90, 0);
            weapBtn.MouseFilter = MouseFilterEnum.Stop;
            _weaponButtons.Add(weapBtn);
            _weaponRow.AddChild(weapBtn);

            // Use a layered layout: VBox for icon+name, overlay for count badge
            VBoxContainer weapContent = new VBoxContainer();
            weapContent.AddThemeConstantOverride("separation", 2);
            weapContent.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            weapContent.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            weapContent.MouseFilter = MouseFilterEnum.Ignore;
            weapBtn.AddChild(weapContent);

            Label weapIcon = new Label();
            weapIcon.Text = group.Icon;
            weapIcon.HorizontalAlignment = HorizontalAlignment.Center;
            weapIcon.AddThemeFontSizeOverride("font_size", 22);
            weapIcon.AddThemeColorOverride("font_color", isSelected ? AccentGreen : TextSecondary);
            weapIcon.MouseFilter = MouseFilterEnum.Ignore;
            weapContent.AddChild(weapIcon);

            // Name + instance indicator (e.g., "CANNON 2/3" when multiple)
            string nameText = group.Name;
            if (group.FlatIndices.Count > 1)
            {
                nameText = $"{group.Name} {group.SelectedInstance + 1}/{group.FlatIndices.Count}";
            }

            Label weapName = new Label();
            weapName.Text = nameText;
            weapName.HorizontalAlignment = HorizontalAlignment.Center;
            weapName.AddThemeFontOverride("font", PixelFont);
            weapName.AddThemeFontSizeOverride("font_size", 9);
            weapName.AddThemeColorOverride("font_color", isSelected ? AccentGreen : TextSecondary);
            weapName.MouseFilter = MouseFilterEnum.Ignore;
            weapName.ClipText = false;
            weapContent.AddChild(weapName);

            // Count badge in the top-right corner (only if more than 1)
            if (group.FlatIndices.Count > 1)
            {
                Label countBadge = new Label();
                countBadge.Text = $"x{group.FlatIndices.Count}";
                countBadge.AddThemeFontOverride("font", PixelFont);
                countBadge.AddThemeFontSizeOverride("font_size", 9);
                countBadge.AddThemeColorOverride("font_color", AccentGold);
                countBadge.MouseFilter = MouseFilterEnum.Ignore;
                countBadge.SetAnchorsPreset(LayoutPreset.TopRight);
                countBadge.OffsetLeft = -30;
                countBadge.OffsetRight = -4;
                countBadge.OffsetTop = 2;
                weapBtn.AddChild(countBadge);
            }

            // Click area: left-click selects this group's current instance.
            // Right-click cycles to next instance within the group.
            Button weapClickArea = new Button();
            weapClickArea.Flat = true;
            weapClickArea.MouseFilter = MouseFilterEnum.Stop;
            weapClickArea.Modulate = new Color(1, 1, 1, 0);
            weapClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); SelectWeaponGroup(capturedGroup); };
            weapBtn.AddChild(weapClickArea);
            weapClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            weapClickArea.OffsetLeft = 0;
            weapClickArea.OffsetRight = 0;
            weapClickArea.OffsetTop = 0;
            weapClickArea.OffsetBottom = 0;

            // Right-click handler to cycle instances.
            // Consume both press AND release to prevent the release from leaking
            // through to CombatCamera._UnhandledInput and cancelling targeting.
            weapClickArea.GuiInput += (InputEvent @event) =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
                {
                    if (mb.Pressed)
                    {
                        CycleWeaponInstance(capturedGroup);
                    }
                    weapClickArea.GetViewport().SetInputAsHandled();
                }
            };
        }

        // Dynamically size the weapon bar based on actual weapon count.
        // Panel doesn't auto-resize, so we set the width explicitly.
        if (_weaponBarPanel != null)
        {
            int btnCount = System.Math.Max(_weaponGroups.Count, 1);
            int barWidth = btnCount * 96 + 24; // 90px button + 8px gap each, plus margins
            int halfWidth = barWidth / 2;
            _weaponBarPanel.OffsetLeft = -halfWidth;
            _weaponBarPanel.OffsetRight = halfWidth;
            _weaponBarPanel.OffsetTop = -100;
            _weaponBarPanel.OffsetBottom = -14;
        }
    }

    /// <summary>
    /// Selects a weapon group's current instance and enters targeting mode.
    /// If the same group is already selected, clicking again cycles to the
    /// next instance within that group.
    /// </summary>
    private void SelectWeaponGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _weaponGroups.Count) return;

        // If clicking the same group that's already selected,
        // cycle to the next instance within the group
        if (groupIndex == _selectedGroupIndex)
        {
            WeaponGroup grp = _weaponGroups[groupIndex];
            if (grp.FlatIndices.Count > 1)
            {
                grp.SelectedInstance = (grp.SelectedInstance + 1) % grp.FlatIndices.Count;
                _weaponGroups[groupIndex] = grp;
                RebuildWeaponButtons();
            }
        }

        _selectedGroupIndex = groupIndex;

        WeaponGroup group = _weaponGroups[groupIndex];
        int flatIndex = group.FlatIndices[group.SelectedInstance];
        _selectedWeaponIndex = flatIndex;

        // Hide the "SELECT WEAPON" prompt now that the player has chosen
        HideSelectWeaponPrompt();

        // Update button visuals
        UpdateWeaponButtonHighlights();

        // Emit WeaponSelected so GameManager highlights the weapon in 3D
        WeaponSelected?.Invoke(flatIndex);

        // Notify GameManager to select the weapon and enter targeting mode
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gm?.OnWeaponSelectedFromUI(flatIndex);
    }

    /// <summary>
    /// Cycles to the next instance within a weapon group (e.g., next cannon).
    /// Right-click on a weapon tile to cycle.
    /// </summary>
    private void CycleWeaponInstance(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _weaponGroups.Count) return;

        WeaponGroup group = _weaponGroups[groupIndex];
        if (group.FlatIndices.Count <= 1) return;

        group.SelectedInstance = (group.SelectedInstance + 1) % group.FlatIndices.Count;
        _weaponGroups[groupIndex] = group;

        _selectedGroupIndex = groupIndex;

        int flatIndex = group.FlatIndices[group.SelectedInstance];
        _selectedWeaponIndex = flatIndex;

        HideSelectWeaponPrompt();
        UpdateWeaponButtonHighlights();
        WeaponSelected?.Invoke(flatIndex);

        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        // cycleOnly: true — just update the highlight and selection without
        // transitioning to targeting mode. The green glow shows in base view.
        gm?.OnWeaponSelectedFromUI(flatIndex, cycleOnly: true);

        // Defer rebuild so the button that received the right-click isn't destroyed
        // while it still has mouse capture. Immediate destruction would cause Godot to
        // lose track of the mouse-focus control and leak the release event to _UnhandledInput.
        Callable.From(RebuildWeaponButtons).CallDeferred();
    }

    /// <summary>
    /// Updates weapon button background colors to reflect the current selection.
    /// </summary>
    private void UpdateWeaponButtonHighlights()
    {
        for (int g = 0; g < _weaponGroups.Count && g < _weaponButtons.Count; g++)
        {
            WeaponGroup group = _weaponGroups[g];
            int currentFlatIndex = group.FlatIndices[group.SelectedInstance];
            bool isSelected = currentFlatIndex == _selectedWeaponIndex;

            Color bg = isSelected
                ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.25f)
                : new Color(0.1f, 0.1f, 0.15f, 0.8f);
            _weaponButtons[g].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }
    }

    /// <summary>
    /// Shows the flashing "SELECT WEAPON" prompt above the weapon bar.
    /// Uses a looping tween to flash the label between full red and transparent.
    /// </summary>
    private void ShowSelectWeaponPrompt()
    {
        if (_selectWeaponLabel == null || _weaponGroups.Count == 0)
            return;

        _selectWeaponLabel.Visible = true;
        _selectWeaponLabel.Modulate = new Color(1, 1, 1, 1);

        // Kill any existing tween before creating a new one
        _selectWeaponTween?.Kill();
        _selectWeaponTween = CreateTween();
        _selectWeaponTween.SetLoops(); // loop forever
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 0.15f, 0.5f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 1.0f, 0.5f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Hides the flashing "SELECT WEAPON" prompt (called when a weapon is selected).
    /// </summary>
    public void HideSelectWeaponPrompt()
    {
        _selectWeaponTween?.Kill();
        _selectWeaponTween = null;
        if (_selectWeaponLabel != null)
            _selectWeaponLabel.Visible = false;
    }

    /// <summary>
    /// Shows "CLICK TO AIM" prompt above the weapon bar.
    /// Called after a weapon is selected and targeting mode is active.
    /// </summary>
    public void ShowAimPrompt()
    {
        if (_selectWeaponLabel == null) return;
        _selectWeaponLabel.Text = "CLICK TO AIM";
        _selectWeaponLabel.Visible = true;
        _selectWeaponLabel.Modulate = new Color(1, 1, 1, 1);
        _selectWeaponTween?.Kill();
        _selectWeaponTween = CreateTween();
        _selectWeaponTween.SetLoops();
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 0.3f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 1.0f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Shows "CLICK TO FIRE" prompt (target is set, ready to fire).
    /// </summary>
    public void ShowFirePrompt()
    {
        if (_selectWeaponLabel == null) return;
        _selectWeaponLabel.Text = "CLICK TO FIRE";
        _selectWeaponLabel.AddThemeColorOverride("font_color", AccentGold);
        _selectWeaponLabel.Visible = true;
        _selectWeaponLabel.Modulate = new Color(1, 1, 1, 1);
        _selectWeaponTween?.Kill();
        _selectWeaponTween = CreateTween();
        _selectWeaponTween.SetLoops();
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 0.3f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 1.0f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Shows "PRESS T — CLICK TO MOVE TROOPS" prompt.
    /// </summary>
    public void ShowTroopMovePrompt()
    {
        if (_selectWeaponLabel == null) return;
        _selectWeaponLabel.Text = "CLICK TERRAIN TO MOVE TROOPS";
        _selectWeaponLabel.AddThemeColorOverride("font_color", AccentGreen);
        _selectWeaponLabel.Visible = true;
        _selectWeaponLabel.Modulate = new Color(1, 1, 1, 1);
        _selectWeaponTween?.Kill();
        _selectWeaponTween = CreateTween();
        _selectWeaponTween.SetLoops();
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 0.3f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 1.0f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Resets the prompt label text and color back to "SELECT WEAPON" for the next turn.
    /// </summary>
    public void ResetPromptText()
    {
        if (_selectWeaponLabel == null) return;
        _selectWeaponLabel.Text = "SELECT WEAPON";
        _selectWeaponLabel.AddThemeColorOverride("font_color", AccentRed);
    }

    /// <summary>
    /// Shows "CLICK TO SET TARGET" prompt (after right-click clears crosshair).
    /// </summary>
    public void ShowTargetPrompt()
    {
        if (_selectWeaponLabel == null) return;
        _selectWeaponLabel.Text = "CLICK TO SET TARGET";
        _selectWeaponLabel.AddThemeColorOverride("font_color", AccentGold);
        _selectWeaponLabel.Visible = true;
        _selectWeaponLabel.Modulate = new Color(1, 1, 1, 1);
        _selectWeaponTween?.Kill();
        _selectWeaponTween = CreateTween();
        _selectWeaponTween.SetLoops();
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 0.3f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _selectWeaponTween.TweenProperty(_selectWeaponLabel, "modulate:a", 1.0f, 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    private void OnFirePressed()
    {
        // Emit event so GameManager can handle fire/aim toggle directly
        FireRequested?.Invoke();
    }

    // ========== Events ==========
    private void OnTurnChanged(TurnChangedEvent payload)
    {
        _currentPlayer = payload.CurrentPlayer;
        _roundNumber = payload.RoundNumber;
        _turnTimer = payload.TurnTimeSeconds;

        if (_turnPlayerLabel != null)
        {
            // Show display name if available, fall back to slot name
            GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
            string displayName = payload.CurrentPlayer.ToString().ToUpper();
            if (gm != null && gm.Players.TryGetValue(payload.CurrentPlayer, out var pData))
                displayName = pData.DisplayName.ToUpper();
            bool isLocal = payload.CurrentPlayer == _localPlayerSlot;
            _turnPlayerLabel.Text = isLocal ? $"\u25b6 YOUR TURN" : $"\u25b6 {displayName}'S TURN";
            _turnPlayerLabel.AddThemeFontSizeOverride("font_size", 14);
            int idx = (int)payload.CurrentPlayer;
            Color playerColor = idx < GameConfig.PlayerColors.Length ? GameConfig.PlayerColors[idx] : Colors.White;
            _turnPlayerLabel.AddThemeColorOverride("font_color", isLocal ? AccentGold : playerColor);
        }

        if (_roundLabel != null)
        {
            _roundLabel.Text = $"ROUND {payload.RoundNumber}";
        }

        // Only show weapon bar and powerup panel on the local player's turn.
        // During bot/remote turns these should be hidden since the human can't use them.
        bool isLocalTurn = payload.CurrentPlayer == _localPlayerSlot;
        if (_weaponBarPanel != null) _weaponBarPanel.Visible = isLocalTurn;
        if (_powerupBarPanel != null)
        {
            _powerupBarPanel.Visible = isLocalTurn;
            if (isLocalTurn)
            {
                CallDeferred(nameof(DeferredPowerupResize));
            }
        }

        // Reset weapon selection for the new turn — no weapon pre-selected
        _selectedWeaponIndex = -1;
        _selectedGroupIndex = -1;
        if (isLocalTurn)
        {
            ShowSelectWeaponPrompt();
        }
        else
        {
            HideSelectWeaponPrompt();
        }
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Combat;
        if (payload.CurrentPhase != GamePhase.Combat)
        {
            HideSelectWeaponPrompt();
            _selectedGroupIndex = -1;
        }
    }

    private void OnCommanderDamaged(CommanderDamagedEvent payload)
    {
        _commanderText = $"Commander hit: {payload.Player} HP {payload.RemainingHealth}";
    }

    // ========== Helpers ==========
    // ========== AIRSTRIKE TARGET PICKER ==========

    /// <summary>
    /// Shows a centered popup overlay letting the player pick which enemy to airstrike.
    /// Each alive enemy is shown as a button with their player color and name.
    /// ESC or clicking the dimmed background cancels without consuming the powerup.
    /// </summary>
    public void ShowAirstrikeTargetPicker(List<(PlayerSlot Slot, string Name, Color PlayerColor)> enemies)
    {
        // Remove any existing picker
        HideAirstrikeTargetPicker();

        // Full-screen dimmed overlay that catches clicks outside the popup
        _airstrikePickerOverlay = new Control();
        _airstrikePickerOverlay.Name = "AirstrikePickerOverlay";
        _airstrikePickerOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
        _airstrikePickerOverlay.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_airstrikePickerOverlay);

        // Dim background
        ColorRect dimBg = new ColorRect();
        dimBg.SetAnchorsPreset(LayoutPreset.FullRect);
        dimBg.Color = new Color(0, 0, 0, 0.5f);
        dimBg.MouseFilter = MouseFilterEnum.Stop;
        _airstrikePickerOverlay.AddChild(dimBg);

        // Click on background cancels
        Button bgClickCatcher = new Button();
        bgClickCatcher.Flat = true;
        bgClickCatcher.SetAnchorsPreset(LayoutPreset.FullRect);
        bgClickCatcher.Modulate = new Color(1, 1, 1, 0);
        bgClickCatcher.MouseFilter = MouseFilterEnum.Stop;
        bgClickCatcher.Pressed += HideAirstrikeTargetPicker;
        _airstrikePickerOverlay.AddChild(bgClickCatcher);

        // Center container for the popup panel
        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.MouseFilter = MouseFilterEnum.Ignore;
        _airstrikePickerOverlay.AddChild(center);

        // Main popup panel with beveled border
        PanelContainer popup = CreateBeveledPanel(PanelBg, AccentRed);
        popup.CustomMinimumSize = new Vector2(320, 0);
        popup.MouseFilter = MouseFilterEnum.Stop;
        center.AddChild(popup);

        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        popup.AddChild(margin);

        VBoxContainer vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(vbox);

        // Title row: airplane icon + "SELECT TARGET"
        HBoxContainer titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 8);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        titleRow.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(titleRow);

        Label airstrikeIcon = new Label();
        airstrikeIcon.Text = "\u2708"; // airplane glyph
        airstrikeIcon.AddThemeFontSizeOverride("font_size", 16);
        airstrikeIcon.AddThemeColorOverride("font_color", AccentRed);
        airstrikeIcon.MouseFilter = MouseFilterEnum.Ignore;
        titleRow.AddChild(airstrikeIcon);

        Label title = new Label();
        title.Text = "SELECT TARGET";
        title.AddThemeFontOverride("font", PixelFont);
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", TextPrimary);
        title.MouseFilter = MouseFilterEnum.Ignore;
        titleRow.AddChild(title);

        // Subtitle
        Label subtitle = new Label();
        subtitle.Text = "Choose enemy fortress to strike";
        subtitle.AddThemeFontOverride("font", PixelFont);
        subtitle.AddThemeFontSizeOverride("font_size", 8);
        subtitle.AddThemeColorOverride("font_color", TextSecondary);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(subtitle);

        // Separator
        ColorRect separator = new ColorRect();
        separator.CustomMinimumSize = new Vector2(0, 2);
        separator.Color = BorderColor;
        separator.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(separator);

        // Enemy buttons
        foreach ((PlayerSlot slot, string name, Color color) in enemies)
        {
            PanelContainer btnPanel = CreateStyledPanel(new Color(0, 0, 0, 0.4f), 0);
            btnPanel.CustomMinimumSize = new Vector2(0, 40);
            btnPanel.MouseFilter = MouseFilterEnum.Stop;
            vbox.AddChild(btnPanel);

            HBoxContainer btnRow = new HBoxContainer();
            btnRow.AddThemeConstantOverride("separation", 10);
            btnRow.MouseFilter = MouseFilterEnum.Ignore;
            btnPanel.AddChild(btnRow);

            MarginContainer btnMargin = new MarginContainer();
            btnMargin.AddThemeConstantOverride("margin_left", 10);
            btnMargin.AddThemeConstantOverride("margin_top", 6);
            btnMargin.AddThemeConstantOverride("margin_bottom", 6);
            btnMargin.MouseFilter = MouseFilterEnum.Ignore;
            btnRow.AddChild(btnMargin);

            // Player color swatch
            HBoxContainer innerRow = new HBoxContainer();
            innerRow.AddThemeConstantOverride("separation", 10);
            innerRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            innerRow.MouseFilter = MouseFilterEnum.Ignore;
            btnMargin.AddChild(innerRow);

            ColorRect colorSwatch = new ColorRect();
            colorSwatch.CustomMinimumSize = new Vector2(16, 16);
            colorSwatch.Color = color;
            colorSwatch.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            colorSwatch.MouseFilter = MouseFilterEnum.Ignore;
            innerRow.AddChild(colorSwatch);

            Label nameLabel = new Label();
            nameLabel.Text = name;
            nameLabel.AddThemeFontOverride("font", PixelFont);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.AddThemeColorOverride("font_color", color);
            nameLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLabel.MouseFilter = MouseFilterEnum.Ignore;
            innerRow.AddChild(nameLabel);

            // Target icon on the right
            Label targetIcon = new Label();
            targetIcon.Text = "\u25ce"; // bullseye
            targetIcon.AddThemeFontSizeOverride("font_size", 14);
            targetIcon.AddThemeColorOverride("font_color", AccentRed);
            targetIcon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            targetIcon.MouseFilter = MouseFilterEnum.Ignore;
            innerRow.AddChild(targetIcon);

            // Invisible button overlay for click handling
            PlayerSlot capturedSlot = slot;
            Button clickArea = new Button();
            clickArea.Flat = true;
            clickArea.MouseFilter = MouseFilterEnum.Stop;
            clickArea.Modulate = new Color(1, 1, 1, 0);
            clickArea.Pressed += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_confirm");
                AirstrikeTargetSelected?.Invoke(capturedSlot);
                HideAirstrikeTargetPicker();
            };
            btnPanel.AddChild(clickArea);
            clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            clickArea.OffsetLeft = 0;
            clickArea.OffsetRight = 0;
            clickArea.OffsetTop = 0;
            clickArea.OffsetBottom = 0;

            // Hover effect
            Color capturedColor = color;
            clickArea.MouseEntered += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_hover");
                if (GodotObject.IsInstanceValid(btnPanel))
                {
                    btnPanel.AddThemeStyleboxOverride("panel",
                        CreateFlatStyle(new Color(capturedColor.R, capturedColor.G, capturedColor.B, 0.2f), 0));
                }
            };
            clickArea.MouseExited += () =>
            {
                if (GodotObject.IsInstanceValid(btnPanel))
                {
                    btnPanel.AddThemeStyleboxOverride("panel",
                        CreateFlatStyle(new Color(0, 0, 0, 0.4f), 0));
                }
            };
        }

        // Cancel hint at the bottom
        Label cancelHint = new Label();
        cancelHint.Text = "[ESC] Cancel";
        cancelHint.AddThemeFontOverride("font", PixelFont);
        cancelHint.AddThemeFontSizeOverride("font_size", 8);
        cancelHint.AddThemeColorOverride("font_color", TextSecondary);
        cancelHint.HorizontalAlignment = HorizontalAlignment.Center;
        cancelHint.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(cancelHint);
    }

    /// <summary>
    /// Hides and removes the airstrike target picker overlay.
    /// </summary>
    public void HideAirstrikeTargetPicker()
    {
        if (_airstrikePickerOverlay != null && GodotObject.IsInstanceValid(_airstrikePickerOverlay))
        {
            _airstrikePickerOverlay.QueueFree();
            _airstrikePickerOverlay = null;
        }
    }

    /// <summary>
    /// Returns true if the airstrike target picker is currently visible.
    /// </summary>
    public bool IsAirstrikePickerVisible => _airstrikePickerOverlay != null
        && GodotObject.IsInstanceValid(_airstrikePickerOverlay);

    public override void _UnhandledInput(InputEvent @event)
    {
        // ESC cancels the airstrike target picker
        if (IsAirstrikePickerVisible && @event is InputEventKey keyEvent
            && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            HideAirstrikeTargetPicker();
            GetViewport().SetInputAsHandled();
        }
    }

    private static PanelContainer CreateStyledPanel(Color bgColor, int cornerRadius)
    {
        PanelContainer panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", CreateFlatStyle(bgColor, cornerRadius));
        return panel;
    }

    private static StyleBoxFlat CreateFlatStyle(Color bgColor, int cornerRadius)
    {
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.CornerRadiusTopLeft = cornerRadius;
        style.CornerRadiusTopRight = cornerRadius;
        style.CornerRadiusBottomLeft = cornerRadius;
        style.CornerRadiusBottomRight = cornerRadius;
        return style;
    }

    private static PanelContainer CreateBeveledPanel(Color bgColor, Color borderColor)
    {
        PanelContainer panel = new PanelContainer();
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.CornerRadiusTopLeft = 0;
        style.CornerRadiusTopRight = 0;
        style.CornerRadiusBottomLeft = 0;
        style.CornerRadiusBottomRight = 0;
        style.BorderWidthLeft = 4;
        style.BorderWidthBottom = 4;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderColor = borderColor;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    // ========== TARGET ENEMY SELECTOR ==========

    /// <summary>
    /// Shows the target enemy selector banner near the top of the screen.
    /// Displays the enemy's name and color, with left/right arrow buttons
    /// to cycle through enemies (when there are multiple alive enemies).
    /// Also shows hint text for keyboard shortcuts.
    /// </summary>
    /// <param name="enemyName">Display name of the currently targeted enemy.</param>
    /// <param name="enemyColor">Player color of the targeted enemy.</param>
    /// <param name="canCycle">True if there are multiple enemies to cycle through.</param>
    /// <param name="currentIndex">1-based index of the current target (e.g. 2 of 3).</param>
    /// <param name="totalEnemies">Total number of alive enemies.</param>
    public void ShowTargetSelector(string enemyName, Color enemyColor, bool canCycle, int currentIndex, int totalEnemies)
    {
        // Remove existing selector if any
        HideTargetSelector();

        // Create the panel container (positioned at top-center)
        _targetSelectorPanel = new PanelContainer();
        _targetSelectorPanel.Name = "TargetSelectorPanel";

        // Style: dark panel with the enemy's color as border
        StyleBoxFlat panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.06f, 0.08f, 0.1f, 0.92f);
        panelStyle.CornerRadiusTopLeft = 0;
        panelStyle.CornerRadiusTopRight = 0;
        panelStyle.CornerRadiusBottomLeft = 4;
        panelStyle.CornerRadiusBottomRight = 4;
        panelStyle.BorderWidthBottom = 3;
        panelStyle.BorderWidthLeft = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderWidthTop = 0;
        panelStyle.BorderColor = enemyColor;
        panelStyle.ContentMarginLeft = 16;
        panelStyle.ContentMarginRight = 16;
        panelStyle.ContentMarginTop = 8;
        panelStyle.ContentMarginBottom = 8;
        _targetSelectorPanel.AddThemeStyleboxOverride("panel", panelStyle);

        // Position at top-center
        _targetSelectorPanel.SetAnchorsPreset(LayoutPreset.CenterTop);
        _targetSelectorPanel.GrowHorizontal = GrowDirection.Both;
        _targetSelectorPanel.GrowVertical = GrowDirection.End;
        _targetSelectorPanel.OffsetTop = 0;
        _targetSelectorPanel.MouseFilter = MouseFilterEnum.Ignore;

        // Outer vertical layout: main row + hint text
        VBoxContainer outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 4);
        outerVBox.MouseFilter = MouseFilterEnum.Ignore;
        _targetSelectorPanel.AddChild(outerVBox);

        // Inner layout: horizontal row with optional arrows, icon, name, and counter
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.MouseFilter = MouseFilterEnum.Ignore;
        outerVBox.AddChild(row);

        // Left arrow button (only if multiple enemies)
        if (canCycle)
        {
            Button leftBtn = new Button();
            leftBtn.Text = "\u25c0"; // left triangle
            leftBtn.Flat = true;
            leftBtn.AddThemeFontSizeOverride("font_size", 14);
            leftBtn.AddThemeColorOverride("font_color", TextPrimary);
            leftBtn.AddThemeColorOverride("font_hover_color", AccentGold);
            leftBtn.CustomMinimumSize = new Vector2(28, 28);
            leftBtn.MouseFilter = MouseFilterEnum.Stop;
            leftBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); TargetCycleRequested?.Invoke(-1); };
            row.AddChild(leftBtn);
        }

        // Colored square icon representing the enemy
        ColorRect colorIcon = new ColorRect();
        colorIcon.Color = enemyColor;
        colorIcon.CustomMinimumSize = new Vector2(12, 12);
        colorIcon.MouseFilter = MouseFilterEnum.Ignore;

        // Wrap the color icon in a CenterContainer for vertical centering
        CenterContainer iconCenter = new CenterContainer();
        iconCenter.MouseFilter = MouseFilterEnum.Ignore;
        iconCenter.AddChild(colorIcon);
        row.AddChild(iconCenter);

        // "TARGETING:" label
        Label targetingLabel = new Label();
        targetingLabel.Text = "TARGETING:";
        targetingLabel.AddThemeFontOverride("font", PixelFont);
        targetingLabel.AddThemeFontSizeOverride("font_size", 10);
        targetingLabel.AddThemeColorOverride("font_color", TextSecondary);
        targetingLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(targetingLabel);

        // Enemy name label
        Label nameLabel = new Label();
        nameLabel.Text = enemyName.ToUpper();
        nameLabel.AddThemeFontOverride("font", PixelFont);
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        nameLabel.AddThemeColorOverride("font_color", enemyColor);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLabel);

        // Counter label (e.g. "2/3") if multiple enemies
        if (canCycle)
        {
            Label counterLabel = new Label();
            counterLabel.Text = $"({currentIndex}/{totalEnemies})";
            counterLabel.AddThemeFontOverride("font", PixelFont);
            counterLabel.AddThemeFontSizeOverride("font_size", 9);
            counterLabel.AddThemeColorOverride("font_color", TextSecondary);
            counterLabel.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(counterLabel);
        }

        // Right arrow button (only if multiple enemies)
        if (canCycle)
        {
            Button rightBtn = new Button();
            rightBtn.Text = "\u25b6"; // right triangle
            rightBtn.Flat = true;
            rightBtn.AddThemeFontSizeOverride("font_size", 14);
            rightBtn.AddThemeColorOverride("font_color", TextPrimary);
            rightBtn.AddThemeColorOverride("font_hover_color", AccentGold);
            rightBtn.CustomMinimumSize = new Vector2(28, 28);
            rightBtn.MouseFilter = MouseFilterEnum.Stop;
            rightBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); TargetCycleRequested?.Invoke(1); };
            row.AddChild(rightBtn);
        }

        // Hint label below the main row for keyboard shortcuts
        Label hintLabel = new Label();
        hintLabel.Name = "TargetSelectorHint";
        hintLabel.Text = canCycle ? "[Tab/E] Next  [Q] Prev  [Click] Set Target  [Esc] Cancel" : "[Click] Set Target  [Esc] Cancel";
        hintLabel.AddThemeFontOverride("font", PixelFont);
        hintLabel.AddThemeFontSizeOverride("font_size", 8);
        hintLabel.AddThemeColorOverride("font_color", TextSecondary);
        hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        hintLabel.MouseFilter = MouseFilterEnum.Ignore;
        outerVBox.AddChild(hintLabel);

        AddChild(_targetSelectorPanel);
    }

    /// <summary>
    /// Hides and removes the target enemy selector banner.
    /// </summary>
    public void HideTargetSelector()
    {
        if (_targetSelectorPanel != null && GodotObject.IsInstanceValid(_targetSelectorPanel))
        {
            _targetSelectorPanel.QueueFree();
            _targetSelectorPanel = null;
        }
    }

    /// <summary>
    /// Returns true if the target selector is currently visible.
    /// </summary>
    public bool IsTargetSelectorVisible => _targetSelectorPanel != null
        && GodotObject.IsInstanceValid(_targetSelectorPanel);
}
