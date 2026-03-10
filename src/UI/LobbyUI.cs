using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Networking;

namespace VoxelSiege.UI;

public partial class LobbyUI : Control
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
    private static readonly Color PanelBg = new Color(0.086f, 0.106f, 0.133f, 0.95f);
    private static readonly Color SlotEmpty = new Color(0.1f, 0.12f, 0.15f, 0.6f);
    private static readonly Color SlotFilled = new Color(0.1f, 0.14f, 0.12f, 0.8f);

    [Export]
    public NodePath? LobbyManagerPath { get; set; }

    public event Action? ReadyToggled;
    public event Action? StartGameRequested;
    public event Action? LeaveLobbyRequested;

    private readonly List<VBoxContainer> _playerSlots = new List<VBoxContainer>();
    private readonly List<Label> _playerNameLabels = new List<Label>();
    private readonly List<Label> _playerStatusLabels = new List<Label>();
    private readonly List<ColorRect> _readyIndicators = new List<ColorRect>();
    private Button? _readyButton;
    private Button? _startButton;
    private Label? _lobbyNameLabel;
    private LineEdit? _commanderNameInput;

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    /// <summary>
    /// The commander name entered by the human player. Returns the text or the default color name.
    /// </summary>
    public string CommanderName => string.IsNullOrWhiteSpace(_commanderNameInput?.Text)
        ? "Green"
        : _commanderNameInput!.Text.Trim();

    // Settings display
    private Label? _buildTimeLabel;
    private Label? _budgetLabel;
    private Label? _mapSizeLabel;
    private Label? _turnTimeLabel;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Full-screen dark backdrop
        ColorRect backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = BgDark;
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(backdrop);

        // Main layout
        VBoxContainer mainLayout = new VBoxContainer();
        mainLayout.SetAnchorsPreset(LayoutPreset.FullRect);
        mainLayout.AddThemeConstantOverride("separation", 0);
        mainLayout.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(mainLayout);

        // Top spacer
        Control topSpacer = new Control();
        topSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        topSpacer.SizeFlagsStretchRatio = 0.3f;
        topSpacer.MouseFilter = MouseFilterEnum.Ignore;
        mainLayout.AddChild(topSpacer);

        // Center content
        VBoxContainer centerBox = new VBoxContainer();
        centerBox.AddThemeConstantOverride("separation", 0);
        centerBox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        centerBox.MouseFilter = MouseFilterEnum.Ignore;
        mainLayout.AddChild(centerBox);

        // Lobby title
        _lobbyNameLabel = new Label();
        _lobbyNameLabel.Text = "MATCH LOBBY";
        _lobbyNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _lobbyNameLabel.AddThemeFontSizeOverride("font_size", 38);
        _lobbyNameLabel.AddThemeColorOverride("font_color", TextPrimary);
        _lobbyNameLabel.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(_lobbyNameLabel);

        ColorRect titleBar = new ColorRect();
        titleBar.CustomMinimumSize = new Vector2(500, 2);
        titleBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        titleBar.Color = AccentGreen;
        titleBar.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(titleBar);

        Control titleSpacer = new Control();
        titleSpacer.CustomMinimumSize = new Vector2(0, 16);
        titleSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(titleSpacer);

        // Commander name input row
        HBoxContainer nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 12);
        nameRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        nameRow.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(nameRow);

        Label nameFieldLabel = new Label();
        nameFieldLabel.Text = "COMMANDER NAME:";
        nameFieldLabel.AddThemeFontOverride("font", PixelFont);
        nameFieldLabel.AddThemeFontSizeOverride("font_size", 12);
        nameFieldLabel.AddThemeColorOverride("font_color", TextSecondary);
        nameFieldLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        nameFieldLabel.MouseFilter = MouseFilterEnum.Ignore;
        nameRow.AddChild(nameFieldLabel);

        _commanderNameInput = new LineEdit();
        _commanderNameInput.Text = "Green";
        _commanderNameInput.PlaceholderText = "Enter name...";
        _commanderNameInput.CustomMinimumSize = new Vector2(260, 40);
        _commanderNameInput.MaxLength = 20;
        _commanderNameInput.AddThemeFontOverride("font", PixelFont);
        _commanderNameInput.AddThemeFontSizeOverride("font_size", 12);
        _commanderNameInput.AddThemeColorOverride("font_color", TextPrimary);
        _commanderNameInput.AddThemeColorOverride("font_placeholder_color", TextSecondary);
        _commanderNameInput.MouseFilter = MouseFilterEnum.Stop;
        // Style the input with square corners and dark background
        StyleBoxFlat nameInputStyle = CreateFlatStyle(new Color(0.06f, 0.08f, 0.10f, 0.95f), 0);
        nameInputStyle.BorderWidthTop = 2;
        nameInputStyle.BorderWidthBottom = 2;
        nameInputStyle.BorderWidthLeft = 2;
        nameInputStyle.BorderWidthRight = 2;
        nameInputStyle.BorderColor = AccentGreen;
        nameInputStyle.ContentMarginLeft = 10;
        nameInputStyle.ContentMarginRight = 10;
        nameInputStyle.ContentMarginTop = 4;
        nameInputStyle.ContentMarginBottom = 4;
        _commanderNameInput.AddThemeStyleboxOverride("normal", nameInputStyle);
        // Focus style with brighter border
        StyleBoxFlat nameInputFocusStyle = CreateFlatStyle(new Color(0.08f, 0.10f, 0.13f, 0.95f), 0);
        nameInputFocusStyle.BorderWidthTop = 2;
        nameInputFocusStyle.BorderWidthBottom = 2;
        nameInputFocusStyle.BorderWidthLeft = 2;
        nameInputFocusStyle.BorderWidthRight = 2;
        nameInputFocusStyle.BorderColor = new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 1.0f);
        nameInputFocusStyle.ContentMarginLeft = 10;
        nameInputFocusStyle.ContentMarginRight = 10;
        nameInputFocusStyle.ContentMarginTop = 4;
        nameInputFocusStyle.ContentMarginBottom = 4;
        _commanderNameInput.AddThemeStyleboxOverride("focus", nameInputFocusStyle);
        nameRow.AddChild(_commanderNameInput);

        Control nameSpacer = new Control();
        nameSpacer.CustomMinimumSize = new Vector2(0, 12);
        nameSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(nameSpacer);

        // Content: Player slots + Settings
        HBoxContainer contentRow = new HBoxContainer();
        contentRow.AddThemeConstantOverride("separation", 24);
        contentRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        contentRow.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(contentRow);

        // Player slots panel
        PanelContainer slotsPanel = CreateStyledPanel(PanelBg, 0);
        slotsPanel.CustomMinimumSize = new Vector2(500, 320);
        slotsPanel.MouseFilter = MouseFilterEnum.Ignore;
        contentRow.AddChild(slotsPanel);

        MarginContainer slotsMargin = new MarginContainer();
        slotsMargin.AddThemeConstantOverride("margin_left", 20);
        slotsMargin.AddThemeConstantOverride("margin_right", 20);
        slotsMargin.AddThemeConstantOverride("margin_top", 16);
        slotsMargin.AddThemeConstantOverride("margin_bottom", 16);
        slotsMargin.MouseFilter = MouseFilterEnum.Ignore;
        slotsPanel.AddChild(slotsMargin);

        VBoxContainer slotsContainer = new VBoxContainer();
        slotsContainer.AddThemeConstantOverride("separation", 8);
        slotsContainer.MouseFilter = MouseFilterEnum.Ignore;
        slotsMargin.AddChild(slotsContainer);

        Label slotsHeader = new Label();
        slotsHeader.Text = "PLAYERS";
        slotsHeader.AddThemeFontSizeOverride("font_size", 16);
        slotsHeader.AddThemeColorOverride("font_color", TextSecondary);
        slotsHeader.MouseFilter = MouseFilterEnum.Ignore;
        slotsContainer.AddChild(slotsHeader);

        ColorRect slotsLine = new ColorRect();
        slotsLine.CustomMinimumSize = new Vector2(0, 1);
        slotsLine.Color = BorderColor;
        slotsLine.MouseFilter = MouseFilterEnum.Ignore;
        slotsContainer.AddChild(slotsLine);

        // Create 4 player slots
        for (int i = 0; i < GameConfig.MaxPlayers; i++)
        {
            Color playerColor = i < GameConfig.PlayerColors.Length ? GameConfig.PlayerColors[i] : Colors.White;
            VBoxContainer slotEntry = CreatePlayerSlot(i + 1, playerColor);
            slotsContainer.AddChild(slotEntry);
            _playerSlots.Add(slotEntry);
        }

        // Settings panel
        PanelContainer settingsPanel = CreateStyledPanel(PanelBg, 0);
        settingsPanel.CustomMinimumSize = new Vector2(280, 320);
        settingsPanel.MouseFilter = MouseFilterEnum.Ignore;
        contentRow.AddChild(settingsPanel);

        MarginContainer settingsMargin = new MarginContainer();
        settingsMargin.AddThemeConstantOverride("margin_left", 20);
        settingsMargin.AddThemeConstantOverride("margin_right", 20);
        settingsMargin.AddThemeConstantOverride("margin_top", 16);
        settingsMargin.AddThemeConstantOverride("margin_bottom", 16);
        settingsMargin.MouseFilter = MouseFilterEnum.Ignore;
        settingsPanel.AddChild(settingsMargin);

        VBoxContainer settingsContainer = new VBoxContainer();
        settingsContainer.AddThemeConstantOverride("separation", 10);
        settingsContainer.MouseFilter = MouseFilterEnum.Ignore;
        settingsMargin.AddChild(settingsContainer);

        Label settingsHeader = new Label();
        settingsHeader.Text = "MATCH SETTINGS";
        settingsHeader.AddThemeFontSizeOverride("font_size", 16);
        settingsHeader.AddThemeColorOverride("font_color", TextSecondary);
        settingsHeader.MouseFilter = MouseFilterEnum.Ignore;
        settingsContainer.AddChild(settingsHeader);

        ColorRect settingsLine = new ColorRect();
        settingsLine.CustomMinimumSize = new Vector2(0, 1);
        settingsLine.Color = BorderColor;
        settingsLine.MouseFilter = MouseFilterEnum.Ignore;
        settingsContainer.AddChild(settingsLine);

        _buildTimeLabel = CreateSettingRow(settingsContainer, "Build Time", "5:00");
        _budgetLabel = CreateSettingRow(settingsContainer, "Budget", "$1,000");
        _mapSizeLabel = CreateSettingRow(settingsContainer, "Arena Size", "Medium (96)");
        _turnTimeLabel = CreateSettingRow(settingsContainer, "Turn Timer", "60s");

        // Spacer before buttons
        Control btnSpacer = new Control();
        btnSpacer.CustomMinimumSize = new Vector2(0, 24);
        btnSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(btnSpacer);

        // Action buttons
        HBoxContainer buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 16);
        buttonRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        buttonRow.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(buttonRow);

        // Leave button
        PanelContainer leavePanel = CreateActionButton("LEAVE", AccentRed, () => LeaveLobbyRequested?.Invoke());
        buttonRow.AddChild(leavePanel);

        // Ready button
        PanelContainer readyPanel = new PanelContainer();
        readyPanel.CustomMinimumSize = new Vector2(200, 50);
        StyleBoxFlat readyStyle = CreateFlatStyle(new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.15f), 10);
        readyStyle.BorderWidthTop = 2;
        readyStyle.BorderWidthBottom = 2;
        readyStyle.BorderWidthLeft = 2;
        readyStyle.BorderWidthRight = 2;
        readyStyle.BorderColor = AccentGreen;
        readyPanel.AddThemeStyleboxOverride("panel", readyStyle);
        buttonRow.AddChild(readyPanel);

        _readyButton = new Button();
        _readyButton.Text = "READY UP";
        _readyButton.Flat = true;
        _readyButton.SetAnchorsPreset(LayoutPreset.FullRect);
        _readyButton.AddThemeFontSizeOverride("font_size", 20);
        _readyButton.AddThemeColorOverride("font_color", AccentGreen);
        _readyButton.AddThemeColorOverride("font_hover_color", TextPrimary);
        _readyButton.MouseFilter = MouseFilterEnum.Stop;
        _readyButton.Pressed += () => ReadyToggled?.Invoke();
        readyPanel.AddChild(_readyButton);

        // Start game button (host only)
        PanelContainer startPanel = new PanelContainer();
        startPanel.CustomMinimumSize = new Vector2(180, 50);
        StyleBoxFlat startStyle = CreateFlatStyle(new Color(AccentGold.R, AccentGold.G, AccentGold.B, 0.12f), 10);
        startStyle.BorderWidthTop = 2;
        startStyle.BorderWidthBottom = 2;
        startStyle.BorderWidthLeft = 2;
        startStyle.BorderWidthRight = 2;
        startStyle.BorderColor = AccentGold;
        startPanel.AddThemeStyleboxOverride("panel", startStyle);
        buttonRow.AddChild(startPanel);

        _startButton = new Button();
        _startButton.Text = "START GAME";
        _startButton.Flat = true;
        _startButton.SetAnchorsPreset(LayoutPreset.FullRect);
        _startButton.AddThemeFontSizeOverride("font_size", 18);
        _startButton.AddThemeColorOverride("font_color", AccentGold);
        _startButton.AddThemeColorOverride("font_hover_color", TextPrimary);
        _startButton.MouseFilter = MouseFilterEnum.Stop;
        _startButton.Pressed += () => StartGameRequested?.Invoke();
        startPanel.AddChild(_startButton);

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        mainLayout.AddChild(bottomSpacer);
    }

    private bool _isReady;
    private bool _isHost;

    /// <summary>
    /// Sets whether the local player is the host (controls START button visibility).
    /// </summary>
    public void SetIsHost(bool isHost)
    {
        _isHost = isHost;
    }

    public override void _Process(double delta)
    {
        UpdateLobbyDisplay();
    }

    private void UpdateLobbyDisplay()
    {
        LobbyManager? lobby = LobbyManagerPath is null
            ? GetTree().Root.GetNodeOrNull<LobbyManager>("Main/LobbyManager")
            : GetNodeOrNull<LobbyManager>(LobbyManagerPath);

        if (lobby == null) return;

        if (_lobbyNameLabel != null)
        {
            _lobbyNameLabel.Text = lobby.LobbyName.ToUpper();
        }

        // Update settings display
        if (_buildTimeLabel != null)
        {
            int mins = (int)(lobby.Settings.BuildTimeSeconds / 60f);
            int secs = (int)(lobby.Settings.BuildTimeSeconds % 60f);
            _buildTimeLabel.Text = $"{mins}:{secs:D2}";
        }
        if (_budgetLabel != null) _budgetLabel.Text = $"${lobby.Settings.StartingBudget:N0}";
        if (_mapSizeLabel != null)
        {
            string sizeName = lobby.Settings.ArenaSize switch
            {
                GameConfig.SmallArena => "Small",
                GameConfig.MediumArena => "Medium",
                GameConfig.LargeArena => "Large",
                _ => $"Custom ({lobby.Settings.ArenaSize})"
            };
            _mapSizeLabel.Text = sizeName;
        }
        if (_turnTimeLabel != null) _turnTimeLabel.Text = $"{lobby.Settings.TurnTimeSeconds:F0}s";

        // Update ready button text to reflect current state
        if (_readyButton != null)
        {
            _readyButton.Text = _isReady ? "UNREADY" : "READY UP";
        }

        // Show/hide START button: only visible for host when all players are ready
        if (_startButton != null)
        {
            _startButton.Visible = _isHost && lobby.AreAllPlayersReady();
        }

        // Update player slots
        // Build lookup by slot
        var slotMap = new Dictionary<int, LobbyMember>();
        foreach (LobbyMember member in lobby.Members.Values)
        {
            slotMap[(int)member.Slot] = member;
        }

        for (int i = 0; i < _playerSlots.Count; i++)
        {
            if (i < _playerNameLabels.Count && i < _playerStatusLabels.Count && i < _readyIndicators.Count)
            {
                if (slotMap.TryGetValue(i, out LobbyMember? member))
                {
                    _playerNameLabels[i].Text = member.DisplayName;
                    _playerStatusLabels[i].Text = member.Ready ? "READY" : "WAITING";
                    _playerStatusLabels[i].AddThemeColorOverride("font_color", member.Ready ? AccentGreen : AccentGold);
                    _readyIndicators[i].Color = member.Ready ? AccentGreen : AccentGold;
                }
                else
                {
                    _playerNameLabels[i].Text = "Empty Slot";
                    _playerStatusLabels[i].Text = "---";
                    _playerStatusLabels[i].AddThemeColorOverride("font_color", TextSecondary);
                    _readyIndicators[i].Color = new Color(0.2f, 0.2f, 0.2f);
                }
            }
        }
    }

    /// <summary>
    /// Toggles the local ready state and returns the new value.
    /// </summary>
    public bool ToggleReady()
    {
        _isReady = !_isReady;
        return _isReady;
    }

    private VBoxContainer CreatePlayerSlot(int slotNumber, Color playerColor)
    {
        VBoxContainer slotEntry = new VBoxContainer();
        slotEntry.AddThemeConstantOverride("separation", 0);
        slotEntry.MouseFilter = MouseFilterEnum.Ignore;

        PanelContainer slotPanel = CreateStyledPanel(SlotEmpty, 0);
        slotPanel.CustomMinimumSize = new Vector2(0, 56);
        slotPanel.MouseFilter = MouseFilterEnum.Ignore;
        slotEntry.AddChild(slotPanel);

        MarginContainer slotMargin = new MarginContainer();
        slotMargin.AddThemeConstantOverride("margin_left", 14);
        slotMargin.AddThemeConstantOverride("margin_right", 14);
        slotMargin.AddThemeConstantOverride("margin_top", 8);
        slotMargin.AddThemeConstantOverride("margin_bottom", 8);
        slotMargin.MouseFilter = MouseFilterEnum.Ignore;
        slotPanel.AddChild(slotMargin);

        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.MouseFilter = MouseFilterEnum.Ignore;
        slotMargin.AddChild(row);

        // Slot number with color
        Label slotLabel = new Label();
        slotLabel.Text = $"P{slotNumber}";
        slotLabel.CustomMinimumSize = new Vector2(30, 0);
        slotLabel.AddThemeFontSizeOverride("font_size", 16);
        slotLabel.AddThemeColorOverride("font_color", playerColor);
        slotLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(slotLabel);

        // Color bar
        ColorRect colorBar = new ColorRect();
        colorBar.CustomMinimumSize = new Vector2(3, 0);
        colorBar.Color = playerColor;
        colorBar.SizeFlagsVertical = SizeFlags.ExpandFill;
        colorBar.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(colorBar);

        // Player name
        Label nameLabel = new Label();
        nameLabel.Text = "Empty Slot";
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AddThemeFontSizeOverride("font_size", 17);
        nameLabel.AddThemeColorOverride("font_color", TextPrimary);
        nameLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLabel);
        _playerNameLabels.Add(nameLabel);

        // Ready indicator dot
        ColorRect readyDot = new ColorRect();
        readyDot.CustomMinimumSize = new Vector2(10, 10);
        readyDot.Size = new Vector2(10, 10);
        readyDot.Color = new Color(0.2f, 0.2f, 0.2f);
        readyDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        readyDot.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(readyDot);
        _readyIndicators.Add(readyDot);

        // Status text
        Label statusLabel = new Label();
        statusLabel.Text = "---";
        statusLabel.CustomMinimumSize = new Vector2(70, 0);
        statusLabel.AddThemeFontSizeOverride("font_size", 14);
        statusLabel.AddThemeColorOverride("font_color", TextSecondary);
        statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        statusLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        statusLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(statusLabel);
        _playerStatusLabels.Add(statusLabel);

        return slotEntry;
    }

    private Label CreateSettingRow(VBoxContainer container, string label, string defaultValue)
    {
        HBoxContainer row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(row);

        Label nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", TextSecondary);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLabel);

        Label valueLabel = new Label();
        valueLabel.Text = defaultValue;
        valueLabel.AddThemeFontSizeOverride("font_size", 16);
        valueLabel.AddThemeColorOverride("font_color", TextPrimary);
        valueLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(valueLabel);

        return valueLabel;
    }

    private PanelContainer CreateActionButton(string text, Color accentColor, Action handler)
    {
        PanelContainer btnPanel = new PanelContainer();
        btnPanel.CustomMinimumSize = new Vector2(120, 50);

        StyleBoxFlat style = CreateFlatStyle(new Color(accentColor.R, accentColor.G, accentColor.B, 0.1f), 10);
        style.BorderWidthTop = 1;
        style.BorderWidthBottom = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.3f);
        btnPanel.AddThemeStyleboxOverride("panel", style);

        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.SetAnchorsPreset(LayoutPreset.FullRect);
        btn.AddThemeFontSizeOverride("font_size", 16);
        btn.AddThemeColorOverride("font_color", accentColor);
        btn.AddThemeColorOverride("font_hover_color", TextPrimary);
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += handler;
        btnPanel.AddChild(btn);

        return btnPanel;
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
}
