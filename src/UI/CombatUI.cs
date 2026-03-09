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
    private static readonly Color ImpactCrosshairColor = new Color(1f, 0.45f, 0.15f, 0.85f);

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

    /// <summary>
    /// Raised when the player selects an enemy target from the airstrike picker.
    /// The PlayerSlot is the chosen enemy target.
    /// </summary>
    public event Action<PlayerSlot>? AirstrikeTargetSelected;

    // --- Airstrike target picker ---
    private Control? _airstrikePickerOverlay;

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
    private string _commanderText = string.Empty;
    private readonly Dictionary<PlayerSlot, Label> _playerHealthLabels = new Dictionary<PlayerSlot, Label>();
    private readonly Dictionary<PlayerSlot, ColorRect> _playerHealthBars = new Dictionary<PlayerSlot, ColorRect>();
    private readonly Dictionary<PlayerSlot, Label> _playerStatusLabels = new Dictionary<PlayerSlot, Label>();
    private readonly Dictionary<PlayerSlot, VBoxContainer> _playerEntries = new Dictionary<PlayerSlot, VBoxContainer>();
    private int _selectedWeaponIndex;
    private readonly List<PanelContainer> _weaponButtons = new List<PanelContainer>();
    private HBoxContainer? _weaponRow;
    private readonly List<(string WeaponId, string Name, string Icon)> _activeWeapons = new();
    private readonly List<PanelContainer> _powerupSlots = new List<PanelContainer>();
    private readonly List<Label> _powerupCountLabels = new List<Label>();
    private VBoxContainer? _powerupContainer;

    // --- Deploy button state ---
    private PanelContainer? _deployPanel;
    private Button? _deployBtn;
    private Label? _deployWarningLabel;

    // --- Impact crosshair state ---
    private Vector2 _impactScreenPos;
    private bool _impactCrosshairVisible;

    // --- Center reticle (for weapon POV) ---
    private Control? _centerCrosshairContainer;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopBar();
        BuildPlayerStatusPanel();
        BuildWeaponBar();
        BuildPowerupPanel();
        BuildDeployPanel();
        // Power meter and fire button removed — click-to-target auto-calculates trajectory
        BuildCrosshair();

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

            Label statusLabel = new Label();
            statusLabel.Text = "ALIVE";
            statusLabel.AddThemeFontOverride("font", PixelFont);
            statusLabel.AddThemeFontSizeOverride("font_size", 10);
            statusLabel.AddThemeColorOverride("font_color", AccentGreen);
            statusLabel.MouseFilter = MouseFilterEnum.Ignore;
            nameRow.AddChild(statusLabel);
            _playerStatusLabels[slot] = statusLabel;

            // Health bar background — clip contents to prevent fill from overflowing
            PanelContainer healthBg = CreateStyledPanel(new Color(0.15f, 0.15f, 0.15f, 1f), 0);
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
        PanelContainer weaponBar = CreateBeveledPanel(PanelBg, AccentGold);
        weaponBar.SetAnchorsPreset(LayoutPreset.CenterBottom);
        weaponBar.OffsetLeft = -200;
        weaponBar.OffsetRight = 200;
        weaponBar.OffsetTop = -100;
        weaponBar.OffsetBottom = -14;
        weaponBar.CustomMinimumSize = new Vector2(400, 86);
        weaponBar.MouseFilter = MouseFilterEnum.Stop;
        AddChild(weaponBar);

        MarginContainer weapMargin = new MarginContainer();
        weapMargin.AddThemeConstantOverride("margin_left", 12);
        weapMargin.AddThemeConstantOverride("margin_right", 12);
        weapMargin.AddThemeConstantOverride("margin_top", 12);
        weapMargin.AddThemeConstantOverride("margin_bottom", 12);
        weapMargin.MouseFilter = MouseFilterEnum.Ignore;
        weaponBar.AddChild(weapMargin);

        _weaponRow = new HBoxContainer();
        _weaponRow.AddThemeConstantOverride("separation", 8);
        _weaponRow.MouseFilter = MouseFilterEnum.Ignore;
        weapMargin.AddChild(_weaponRow);

        // Initial placeholder — will be rebuilt by SetAvailableWeapons once combat starts
        RebuildWeaponButtons();
    }

    // ========== POWERUP PANEL (bottom left, beside weapon bar) ==========
    private void BuildPowerupPanel()
    {
        PanelContainer powerupBar = CreateBeveledPanel(PanelBg, new Color("3e96ff"));
        // Anchor to bottom-left so the panel grows upward as powerups are added
        powerupBar.AnchorLeft = 0.5f;
        powerupBar.AnchorRight = 0.5f;
        powerupBar.AnchorTop = 1;
        powerupBar.AnchorBottom = 1;
        powerupBar.OffsetLeft = -440;
        powerupBar.OffsetRight = -210;
        powerupBar.OffsetTop = -100;
        powerupBar.OffsetBottom = -14;
        powerupBar.CustomMinimumSize = new Vector2(230, 86);
        powerupBar.MouseFilter = MouseFilterEnum.Stop;
        powerupBar.ClipContents = true;
        AddChild(powerupBar);

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

        // ScrollContainer so the list can scroll when there are many powerups
        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        scrollContainer.CustomMinimumSize = new Vector2(0, 0);
        scrollContainer.MouseFilter = MouseFilterEnum.Pass;
        outerColumn.AddChild(scrollContainer);

        _powerupContainer = new VBoxContainer();
        _powerupContainer.AddThemeConstantOverride("separation", 2);
        _powerupContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _powerupContainer.MouseFilter = MouseFilterEnum.Ignore;
        scrollContainer.AddChild(_powerupContainer);

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

        // Remove old slot buttons (header is now outside _powerupContainer)
        for (int i = _powerupContainer.GetChildCount() - 1; i >= 0; i--)
        {
            _powerupContainer.GetChild(i).QueueFree();
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
            nameLabel.ClipText = true;
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
        _deployBtn.Pressed += () => DeployRequested?.Invoke();
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

        // Bar background — clip contents so the fill never extends beyond the bar
        PanelContainer barBg = CreateStyledPanel(new Color(0.1f, 0.1f, 0.12f, 1f), 0);
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
        fireBtn.Pressed += OnFirePressed;
        firePanel.AddChild(fireBtn);
        fireBtn.SetAnchorsPreset(LayoutPreset.FullRect);
        fireBtn.OffsetLeft = 0;
        fireBtn.OffsetRight = 0;
        fireBtn.OffsetTop = 0;
        fireBtn.OffsetBottom = 0;

        // Hover effect
        fireBtn.MouseEntered += () =>
        {
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
            if (_playerHealthLabels.TryGetValue(slot, out Label? hpLabel))
            {
                hpLabel.Text = $"{player.CommanderHealth} HP";
            }

            if (_playerHealthBars.TryGetValue(slot, out ColorRect? bar))
            {
                float healthPercent = Mathf.Clamp(player.CommanderHealth / (float)GameConfig.CommanderHP, 0f, 1f);
                bar.AnchorRight = healthPercent;
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
    /// Called by GameManager to set the list of actually-placed weapons for the current player.
    /// Each entry is a WeaponBase from the player's weapon list. Only non-destroyed weapons
    /// should be passed. The UI rebuilds its weapon bar to match.
    /// </summary>
    public void SetAvailableWeapons(List<WeaponBase>? weapons)
    {
        _activeWeapons.Clear();

        if (weapons != null)
        {
            foreach (WeaponBase w in weapons)
            {
                if (w == null || !GodotObject.IsInstanceValid(w) || w.IsDestroyed)
                {
                    continue;
                }

                string id = w.WeaponId;
                if (WeaponDisplayLookup.TryGetValue(id, out var display))
                {
                    _activeWeapons.Add((id, display.Name, display.Icon));
                }
                else
                {
                    // Unknown weapon type — show with generic icon
                    _activeWeapons.Add((id, id, "?"));
                }
            }
        }

        _selectedWeaponIndex = 0;
        RebuildWeaponButtons();
    }

    /// <summary>
    /// Rebuilds the weapon button row from _activeWeapons. If _activeWeapons
    /// is empty, falls back to a "No Weapons" placeholder.
    /// </summary>
    private void RebuildWeaponButtons()
    {
        if (_weaponRow == null)
        {
            return;
        }

        // Clear existing buttons
        foreach (PanelContainer btn in _weaponButtons)
        {
            btn.QueueFree();
        }
        _weaponButtons.Clear();

        // Remove any leftover children (e.g. placeholder labels)
        foreach (Node child in _weaponRow.GetChildren())
        {
            child.QueueFree();
        }

        if (_activeWeapons.Count == 0)
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

        for (int i = 0; i < _activeWeapons.Count; i++)
        {
            var weapon = _activeWeapons[i];
            int capturedIndex = i;

            PanelContainer weapBtn = CreateStyledPanel(
                i == _selectedWeaponIndex
                    ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.2f)
                    : new Color(0.1f, 0.1f, 0.15f, 0.8f),
                0);
            weapBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            weapBtn.CustomMinimumSize = new Vector2(80, 0);
            weapBtn.MouseFilter = MouseFilterEnum.Stop;
            _weaponButtons.Add(weapBtn);
            _weaponRow.AddChild(weapBtn);

            VBoxContainer weapContent = new VBoxContainer();
            weapContent.AddThemeConstantOverride("separation", 2);
            weapContent.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            weapContent.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            weapContent.MouseFilter = MouseFilterEnum.Ignore;
            weapBtn.AddChild(weapContent);

            Label weapIcon = new Label();
            weapIcon.Text = weapon.Icon;
            weapIcon.HorizontalAlignment = HorizontalAlignment.Center;
            weapIcon.AddThemeFontSizeOverride("font_size", 22);
            weapIcon.AddThemeColorOverride("font_color", i == _selectedWeaponIndex ? AccentGreen : TextSecondary);
            weapIcon.MouseFilter = MouseFilterEnum.Ignore;
            weapContent.AddChild(weapIcon);

            Label weapName = new Label();
            weapName.Text = weapon.Name;
            weapName.HorizontalAlignment = HorizontalAlignment.Center;
            weapName.AddThemeFontOverride("font", PixelFont);
            weapName.AddThemeFontSizeOverride("font_size", 10);
            weapName.AddThemeColorOverride("font_color", TextSecondary);
            weapName.MouseFilter = MouseFilterEnum.Ignore;
            weapContent.AddChild(weapName);

            Button weapClickArea = new Button();
            weapClickArea.Flat = true;
            weapClickArea.MouseFilter = MouseFilterEnum.Stop;
            weapClickArea.Modulate = new Color(1, 1, 1, 0);
            weapClickArea.Pressed += () => SelectWeapon(capturedIndex);
            weapBtn.AddChild(weapClickArea);
            weapClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            weapClickArea.OffsetLeft = 0;
            weapClickArea.OffsetRight = 0;
            weapClickArea.OffsetTop = 0;
            weapClickArea.OffsetBottom = 0;
        }
    }

    private void SelectWeapon(int index)
    {
        _selectedWeaponIndex = index;
        for (int i = 0; i < _weaponButtons.Count; i++)
        {
            Color bg = i == index
                ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.2f)
                : new Color(0.1f, 0.1f, 0.15f, 0.8f);
            _weaponButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }

        // Emit event so GameManager can update its weapon index
        WeaponSelected?.Invoke(index);

        // Notify GameManager that a weapon was selected via UI
        // The GameManager will handle transitioning the camera to weapon POV
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gm?.OnWeaponSelectedFromUI(index);
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
            _turnPlayerLabel.Text = payload.CurrentPlayer.ToString().ToUpper();
            int idx = (int)payload.CurrentPlayer;
            Color playerColor = idx < GameConfig.PlayerColors.Length ? GameConfig.PlayerColors[idx] : Colors.White;
            _turnPlayerLabel.AddThemeColorOverride("font_color", playerColor);
        }

        if (_roundLabel != null)
        {
            _roundLabel.Text = $"ROUND {payload.RoundNumber}";
        }
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Combat;
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
}
