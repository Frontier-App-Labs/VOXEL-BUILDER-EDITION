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
    private PanelContainer? _startPanel;
    private Label? _lobbyNameLabel;
    private Label? _gameCodeLabel;
    private Label? _gameCodeStatus;
    private PanelContainer? _gameCodePanel;
    private LineEdit? _commanderNameInput;
    private VBoxContainer? _contentContainer;

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

        // Content container — centered via _Process like MainMenu does
        _contentContainer = new VBoxContainer();
        _contentContainer.Name = "LobbyContent";
        _contentContainer.AddThemeConstantOverride("separation", 0);
        _contentContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_contentContainer);

        // Top spacer
        Control topSpacer = new Control();
        topSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        topSpacer.SizeFlagsStretchRatio = 0.3f;
        topSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(topSpacer);

        // Center content
        VBoxContainer centerBox = new VBoxContainer();
        centerBox.AddThemeConstantOverride("separation", 0);
        centerBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        centerBox.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(centerBox);

        // Lobby title
        _lobbyNameLabel = new Label();
        _lobbyNameLabel.Text = "MATCH LOBBY";
        _lobbyNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _lobbyNameLabel.AddThemeFontOverride("font", PixelFont);
        _lobbyNameLabel.AddThemeFontSizeOverride("font_size", 24);
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
        titleSpacer.CustomMinimumSize = new Vector2(0, 12);
        titleSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(titleSpacer);

        // === GAME CODE DISPLAY (host only — shows the code friends use to join) ===
        _gameCodeLabel = new Label();
        _gameCodeLabel.Text = "DISCOVERING...";
        _gameCodeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _gameCodeLabel.AddThemeFontOverride("font", PixelFont);
        _gameCodeLabel.AddThemeFontSizeOverride("font_size", 22);
        _gameCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
        _gameCodeLabel.MouseFilter = MouseFilterEnum.Ignore;

        _gameCodePanel = new PanelContainer();
        _gameCodePanel.CustomMinimumSize = new Vector2(400, 56);
        StyleBoxFlat codePanelStyle = CreateFlatStyle(new Color("0d1117"), 0);
        codePanelStyle.BorderWidthLeft = 4;
        codePanelStyle.BorderWidthTop = 2;
        codePanelStyle.BorderWidthRight = 2;
        codePanelStyle.BorderWidthBottom = 4;
        codePanelStyle.BorderColor = AccentGreen;
        codePanelStyle.ContentMarginLeft = 20;
        codePanelStyle.ContentMarginRight = 20;
        codePanelStyle.ContentMarginTop = 10;
        codePanelStyle.ContentMarginBottom = 10;
        _gameCodePanel.AddThemeStyleboxOverride("panel", codePanelStyle);
        _gameCodePanel.AddChild(_gameCodeLabel);

        HBoxContainer codeWrapper = new HBoxContainer();
        codeWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        codeWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        codeWrapper.MouseFilter = MouseFilterEnum.Ignore;
        codeWrapper.AddChild(_gameCodePanel);
        centerBox.AddChild(codeWrapper);

        _gameCodeStatus = new Label();
        _gameCodeStatus.Text = "SHARE THIS CODE WITH FRIENDS TO JOIN";
        _gameCodeStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _gameCodeStatus.AddThemeFontOverride("font", PixelFont);
        _gameCodeStatus.AddThemeFontSizeOverride("font_size", 9);
        _gameCodeStatus.AddThemeColorOverride("font_color", TextSecondary);
        _gameCodeStatus.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(_gameCodeStatus);

        Control codeSpacer = new Control();
        codeSpacer.CustomMinimumSize = new Vector2(0, 12);
        codeSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(codeSpacer);

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

        // Leave button — voxel style (square corners, thick borders)
        PanelContainer leavePanel = CreateActionButton("LEAVE", AccentRed, () => LeaveLobbyRequested?.Invoke());
        buttonRow.AddChild(leavePanel);

        // Ready button — voxel style matching main menu buttons
        PanelContainer readyPanel = new PanelContainer();
        readyPanel.CustomMinimumSize = new Vector2(220, 56);
        StyleBoxFlat readyStyle = CreateFlatStyle(BgDark, 0);
        readyStyle.BorderWidthLeft = 4;
        readyStyle.BorderWidthTop = 2;
        readyStyle.BorderWidthRight = 2;
        readyStyle.BorderWidthBottom = 4;
        readyStyle.BorderColor = AccentGreen;
        readyStyle.ContentMarginLeft = 20;
        readyStyle.ContentMarginRight = 20;
        readyStyle.ContentMarginTop = 10;
        readyStyle.ContentMarginBottom = 10;
        readyPanel.AddThemeStyleboxOverride("panel", readyStyle);
        buttonRow.AddChild(readyPanel);

        _readyButton = new Button();
        _readyButton.Text = "READY UP";
        _readyButton.Flat = true;
        _readyButton.SetAnchorsPreset(LayoutPreset.FullRect);
        _readyButton.AddThemeFontOverride("font", PixelFont);
        _readyButton.AddThemeFontSizeOverride("font_size", 14);
        _readyButton.AddThemeColorOverride("font_color", AccentGreen);
        _readyButton.AddThemeColorOverride("font_hover_color", TextPrimary);
        _readyButton.MouseFilter = MouseFilterEnum.Stop;
        _readyButton.Pressed += () => ReadyToggled?.Invoke();
        readyPanel.AddChild(_readyButton);

        // Start game button (host only) — voxel style
        _startPanel = new PanelContainer();
        PanelContainer startPanel = _startPanel;
        startPanel.CustomMinimumSize = new Vector2(200, 56);
        StyleBoxFlat startStyle = CreateFlatStyle(BgDark, 0);
        startStyle.BorderWidthLeft = 4;
        startStyle.BorderWidthTop = 2;
        startStyle.BorderWidthRight = 2;
        startStyle.BorderWidthBottom = 4;
        startStyle.BorderColor = AccentGold;
        startStyle.ContentMarginLeft = 20;
        startStyle.ContentMarginRight = 20;
        startStyle.ContentMarginTop = 10;
        startStyle.ContentMarginBottom = 10;
        startPanel.AddThemeStyleboxOverride("panel", startStyle);
        buttonRow.AddChild(startPanel);

        _startButton = new Button();
        _startButton.Text = "START GAME";
        _startButton.Flat = true;
        _startButton.SetAnchorsPreset(LayoutPreset.FullRect);
        _startButton.AddThemeFontOverride("font", PixelFont);
        _startButton.AddThemeFontSizeOverride("font_size", 14);
        _startButton.AddThemeColorOverride("font_color", AccentGold);
        _startButton.AddThemeColorOverride("font_hover_color", TextPrimary);
        _startButton.MouseFilter = MouseFilterEnum.Stop;
        _startButton.Pressed += () => StartGameRequested?.Invoke();
        startPanel.AddChild(_startButton);

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(bottomSpacer);
    }

    private bool _isReady;
    private bool _isHost;

    /// <summary>
    /// Sets whether the local player is the host (controls START button visibility).
    /// </summary>
    public void SetIsHost(bool isHost)
    {
        _isHost = isHost;

        // Hide game code panel for non-hosts (they already entered the code)
        if (!isHost && _gameCodePanel != null)
        {
            _gameCodePanel.Visible = false;
        }
        if (!isHost && _gameCodeStatus != null)
        {
            _gameCodeStatus.Visible = false;
        }

        if (!isHost) return;

        // Check if we're using Steam (code already available) or ENet (need IP discovery)
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager == null)
        {
            GD.PrintErr("[LobbyUI] NetworkManager not found!");
            return;
        }

        if (netManager.UsingSteam)
        {
            // Steam path — get the code from the SteamManager (already created)
            SteamPlatformNode? steamNode = GetTree().Root.GetNodeOrNull<SteamPlatformNode>("Main/SteamPlatform");
            Networking.Steam.SteamManager? steam = steamNode?.Steam;
            if (steam != null && steam.InLobby)
            {
                string? code = steam.CurrentLobby.GetData("game_code");
                GD.Print($"[LobbyUI] Steam lobby code: {code}");
                if (_gameCodeLabel != null)
                {
                    _gameCodeLabel.Text = $"CODE:  {code ?? "???"}";
                    _gameCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
                }
                if (_gameCodeStatus != null)
                {
                    _gameCodeStatus.Text = "SHARE THIS CODE  -  CONNECTED VIA STEAM";
                    _gameCodeStatus.AddThemeColorOverride("font_color", TextSecondary);
                }
            }
        }
        else
        {
            // ENet fallback — subscribe to IP discovery
            netManager.ExternalIpDiscovered += OnPublicIpDiscovered;
            GD.Print("[LobbyUI] Subscribed to ExternalIpDiscovered (ENet mode)");

            if (!string.IsNullOrEmpty(netManager.ExternalIp) && netManager.ExternalIp != "UNKNOWN")
            {
                OnPublicIpDiscovered(netManager.ExternalIp);
            }
        }
    }

    /// <summary>
    /// Called when the public IP is discovered (ENet fallback path).
    /// </summary>
    private void OnPublicIpDiscovered(string publicIp)
    {
        GD.Print($"[LobbyUI] Public IP received: {publicIp}");

        if (publicIp == "UNKNOWN")
        {
            if (_gameCodeLabel != null)
            {
                _gameCodeLabel.Text = "IP LOOKUP FAILED";
                _gameCodeLabel.AddThemeColorOverride("font_color", AccentRed);
            }
            if (_gameCodeStatus != null)
            {
                _gameCodeStatus.Text = "CHECK INTERNET CONNECTION";
                _gameCodeStatus.AddThemeColorOverride("font_color", AccentRed);
            }
            return;
        }

        string code = EncodeIpToCode(publicIp);
        GD.Print($"[LobbyUI] Public IP {publicIp} → code: {code}");

        if (_gameCodeLabel != null)
        {
            _gameCodeLabel.Text = $"CODE:  {code}";
            _gameCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
        }
        if (_gameCodeStatus != null)
        {
            _gameCodeStatus.Text = "SHARE THIS CODE  -  REQUIRES PORT FORWARDING";
            _gameCodeStatus.AddThemeColorOverride("font_color", AccentGold);
        }
    }

    public override void _ExitTree()
    {
        // Unsubscribe from NetworkManager
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager != null)
        {
            netManager.ExternalIpDiscovered -= OnPublicIpDiscovered;
        }
    }

    /// <summary>
    /// Encodes an IPv4 address into a 7-character alphanumeric code.
    /// </summary>
    private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private static string EncodeIpToCode(string ip)
    {
        string[] parts = ip.Split('.');
        if (parts.Length != 4) return "AAAAAAA";
        uint ipNum = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!uint.TryParse(parts[i], out uint octet) || octet > 255)
                return "AAAAAAA";
            ipNum = (ipNum << 8) | octet;
        }
        char[] code = new char[7];
        for (int i = 6; i >= 0; i--)
        {
            code[i] = CodeChars[ipNum % 32];
            ipNum /= 32;
        }
        return new string(code);
    }

    public override void _Process(double delta)
    {
        // Brute-force centering like MainMenu: position content container to center of screen
        if (_contentContainer != null)
        {
            Vector2 viewSize = GetViewportRect().Size;
            float contentW = viewSize.X * 0.55f;
            _contentContainer.Position = new Vector2((viewSize.X - contentW) * 0.5f, 0f);
            _contentContainer.Size = new Vector2(contentW, viewSize.Y);
        }

        UpdateLobbyDisplay();
    }

    private float _debugLogTimer;

    private void UpdateLobbyDisplay()
    {
        LobbyManager? lobby = LobbyManagerPath is null
            ? GetTree().Root.GetNodeOrNull<LobbyManager>("Main/LobbyManager")
            : GetNodeOrNull<LobbyManager>(LobbyManagerPath);

        if (lobby == null)
        {
            // Log once per second to avoid spam
            _debugLogTimer += (float)GetProcessDeltaTime();
            if (_debugLogTimer > 2f)
            {
                _debugLogTimer = 0f;
                GD.PrintErr("[LobbyUI] LobbyManager not found at 'Main/LobbyManager'!");
            }
            return;
        }

        // Periodic debug: log member count
        _debugLogTimer += (float)GetProcessDeltaTime();
        if (_debugLogTimer > 3f)
        {
            _debugLogTimer = 0f;
            GD.Print($"[LobbyUI] Lobby '{lobby.LobbyName}' has {lobby.Members.Count} member(s)");
            foreach (var kvp in lobby.Members)
            {
                GD.Print($"  Peer {kvp.Key}: {kvp.Value.DisplayName} (slot {kvp.Value.Slot}, ready={kvp.Value.Ready})");
            }
        }

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
            bool showStart = _isHost && lobby.AreAllPlayersReady();
            _startButton.Visible = showStart;
            if (_startPanel != null) _startPanel.Visible = showStart;
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
        btnPanel.CustomMinimumSize = new Vector2(140, 56);

        // Voxel style: square corners, thick pixelated borders, solid colors
        StyleBoxFlat style = CreateFlatStyle(BgDark, 0);
        style.BorderWidthLeft = 4;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 4;
        style.BorderColor = accentColor;
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        btnPanel.AddThemeStyleboxOverride("panel", style);

        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.SetAnchorsPreset(LayoutPreset.FullRect);
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 14);
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
