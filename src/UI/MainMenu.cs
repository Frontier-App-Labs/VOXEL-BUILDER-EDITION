using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Networking;
using VoxelSiege.Networking.Steam;
using VoxelSiege.Utility;

namespace VoxelSiege.UI;

public partial class MainMenu : Control
{
    // --- Theme Colors ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color("161b22");
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color AccentRed = new Color("d73a49");
    private static readonly Color AccentCyan = new Color("3e96ff");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color ButtonHover = new Color("1f2937");
    private static readonly Color ButtonNormal = new Color("0d1117");
    private static readonly Color BorderColor = new Color("30363d");

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Voxel title textures (lazy-loaded) ---
    private static Texture2D? _titleTexGreen;
    private static Texture2D? _titleTexGold;
    private static Texture2D TitleTexGreen => _titleTexGreen ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/metal_32.png");
    private static Texture2D TitleTexGold => _titleTexGold ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/obsidian_32.png");

    // Voxel title colors (vibrant, high-contrast)
    private static readonly Color TitleGreen1 = new Color("5cb8ff");
    private static readonly Color TitleGreen2 = new Color("90d0ff");
    private static readonly Color TitleGold1 = new Color("9040d0");
    private static readonly Color TitleGold2 = new Color("b06ef0");
    private static readonly Color TitleShadowColor = new Color(0f, 0f, 0f, 0.7f);
    public event Action? PlayOnlineRequested;
    public event Action? PlayBotsRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    /// <summary>
    /// Fired when the player chooses to host a game.
    /// The bool parameter indicates whether the lobby is open (true) or private/code-only (false).
    /// </summary>
    public event Action<bool>? HostGameRequested;

    /// <summary>
    /// Fired when the player enters a lobby code and clicks Join.
    /// The string parameter is the code entered.
    /// </summary>
    public event Action<string>? JoinWithCodeRequested;

    private HelpScreen? _helpScreen;
    private int _botCount = 1;
    private Label? _botCountLabel;
    private LineEdit? _commanderNameInput;

    private readonly List<Control> _menuButtons = new List<Control>();
    private readonly List<Control> _titleBlocks = new List<Control>();
    private readonly List<FallingBlock> _fallingBlocks = new List<FallingBlock>();
    private Control? _contentContainer;
    private Control? _fallingBlockLayer;
    private Label? _subtitleLabel;
    private Label? _walletLabel;
    private float _time;
    private float _subtitleRevealTimer;
    private int _subtitleRevealIndex;
    private const string SubtitleFull = "BUILDER'S EDITION";
    private RandomNumberGenerator _rng = new RandomNumberGenerator();

    // Play Online sub-menu panels
    private VBoxContainer? _mainButtonContainer;
    private VBoxContainer? _playOnlinePanel;
    private VBoxContainer? _hostPanel;
    private PanelContainer? _lobbyCodePanel;
    private VBoxContainer? _joinPanel;
    private VBoxContainer? _sandboxSlotsPanel;
    private VBoxContainer? _sandboxSlotList;
    private Label? _sandboxWalletLabel;
    private Label? _lobbyCodeLabel;
    private Label? _statusLabel;

    // --- Lobby code generation ---
    private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private const int LobbyCodeLength = 6;
    private string _generatedLobbyCode = string.Empty;

    // --- Pixel Font Letter Definitions (5 wide x 7 tall) ---
    private static readonly bool[,] LetterV = {
        {true,false,false,false,true},
        {true,false,false,false,true},
        {true,false,false,false,true},
        {false,true,false,true,false},
        {false,true,false,true,false},
        {false,false,true,false,false},
        {false,false,true,false,false},
    };
    private static readonly bool[,] LetterO = {
        {false,true,true,true,false},
        {true,false,false,false,true},
        {true,false,false,false,true},
        {true,false,false,false,true},
        {true,false,false,false,true},
        {true,false,false,false,true},
        {false,true,true,true,false},
    };
    private static readonly bool[,] LetterX = {
        {true,false,false,false,true},
        {true,false,false,false,true},
        {false,true,false,true,false},
        {false,false,true,false,false},
        {false,true,false,true,false},
        {true,false,false,false,true},
        {true,false,false,false,true},
    };
    private static readonly bool[,] LetterE = {
        {true,true,true,true,true},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,true,true,true,false},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,true,true,true,true},
    };
    private static readonly bool[,] LetterL = {
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,true,true,true,true},
    };
    private static readonly bool[,] LetterS = {
        {true,true,true,true,true},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,true,true,true,true},
        {false,false,false,false,true},
        {false,false,false,false,true},
        {true,true,true,true,true},
    };
    private static readonly bool[,] LetterI = {
        {false,true,true,true,false},
        {false,false,true,false,false},
        {false,false,true,false,false},
        {false,false,true,false,false},
        {false,false,true,false,false},
        {false,false,true,false,false},
        {false,true,true,true,false},
    };
    private static readonly bool[,] LetterG = {
        {true,true,true,true,true},
        {true,false,false,false,false},
        {true,false,false,false,false},
        {true,false,true,true,false},
        {true,false,false,true,false},
        {true,false,false,true,false},
        {true,true,true,true,true},
    };

    // Tetromino shapes for falling blocks
    private static readonly int[][][] Tetrominoes = {
        // I-block
        new[] { new[] {0,0}, new[] {1,0}, new[] {2,0}, new[] {3,0} },
        // O-block
        new[] { new[] {0,0}, new[] {1,0}, new[] {0,1}, new[] {1,1} },
        // T-block
        new[] { new[] {0,0}, new[] {1,0}, new[] {2,0}, new[] {1,1} },
        // L-block
        new[] { new[] {0,0}, new[] {0,1}, new[] {0,2}, new[] {1,2} },
        // S-block
        new[] { new[] {1,0}, new[] {2,0}, new[] {0,1}, new[] {1,1} },
    };

    private struct FallingBlock
    {
        public List<ColorRect> Rects;
        public int ShapeIndex;
        public float X;
        public float Y;
        public float Speed;
        public float Rotation;
        public float RotSpeed;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _rng.Randomize();

        // Full-screen dark backdrop
        ColorRect backdrop = new ColorRect();
        backdrop.Name = "Backdrop";
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.0f);
        AddChild(backdrop);

        // Falling voxel blocks layer (behind content)
        _fallingBlockLayer = new Control();
        _fallingBlockLayer.Name = "FallingBlockLayer";
        _fallingBlockLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        _fallingBlockLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_fallingBlockLayer);

        // Subtle gradient overlay at top (helps frame the title)
        ColorRect topGradient = new ColorRect();
        topGradient.SetAnchorsPreset(LayoutPreset.TopWide);
        topGradient.OffsetBottom = 280;
        topGradient.CustomMinimumSize = new Vector2(0, 280);
        topGradient.Color = new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.06f);
        topGradient.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(topGradient);

        // Voxel block title layer (rendered above background)
        Control titleLayer = new Control();
        titleLayer.Name = "TitleLayer";
        titleLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        titleLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(titleLayer);
        BuildVoxelTitle(titleLayer);

        // Main content - centered vertical layout
        // Brute-force: skip anchors entirely, set Position/Size in _Process every frame
        // (Godot's anchor system failed to center reliably across 5+ attempts)
        _contentContainer = new VBoxContainer();
        _contentContainer.Name = "Content";
        _contentContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_contentContainer);

        // Top spacer (tall enough to clear the voxel title - increased for larger title)
        Control topSpacer = new Control();
        topSpacer.CustomMinimumSize = new Vector2(0, 210);
        topSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(topSpacer);

        VBoxContainer centerBox = new VBoxContainer();
        centerBox.AddThemeConstantOverride("separation", 0);
        centerBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        centerBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        centerBox.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(centerBox);

        // Accent bar under title (voxel-style segmented bar)
        HBoxContainer accentBar = new HBoxContainer();
        accentBar.AddThemeConstantOverride("separation", 3);
        accentBar.MouseFilter = MouseFilterEnum.Ignore;

        // Wrap in an HBoxContainer that centers horizontally (VBoxContainer ignores ShrinkCenter)
        HBoxContainer accentWrapper = new HBoxContainer();
        accentWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        accentWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        accentWrapper.MouseFilter = MouseFilterEnum.Ignore;
        accentWrapper.AddChild(accentBar);
        centerBox.AddChild(accentWrapper);

        for (int i = 0; i < 40; i++)
        {
            ColorRect segment = new ColorRect();
            segment.CustomMinimumSize = new Vector2(6, 4);
            float hueShift = (float)i / 40f * 0.1f;
            Color segColor = AccentGreen.Lerp(AccentGold, hueShift);
            segment.Color = segColor;
            segment.MouseFilter = MouseFilterEnum.Ignore;
            accentBar.AddChild(segment);
        }

        // Spacer
        Control titleSpacer = new Control();
        titleSpacer.CustomMinimumSize = new Vector2(0, 12);
        titleSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(titleSpacer);

        // Subtitle with letter-by-letter reveal
        _subtitleLabel = new Label();
        _subtitleLabel.Text = "";
        _subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitleLabel.AddThemeFontOverride("font", PixelFont);
        _subtitleLabel.AddThemeFontSizeOverride("font_size", 20);
        _subtitleLabel.AddThemeColorOverride("font_color", AccentRed);
        _subtitleLabel.MouseFilter = MouseFilterEnum.Ignore;

        // Wrap in an HBoxContainer that centers horizontally
        HBoxContainer subtitleWrapper = new HBoxContainer();
        subtitleWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        subtitleWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        subtitleWrapper.MouseFilter = MouseFilterEnum.Ignore;
        subtitleWrapper.AddChild(_subtitleLabel);
        centerBox.AddChild(subtitleWrapper);

        // Thank you message
        Control tySpacer = new Control();
        tySpacer.CustomMinimumSize = new Vector2(0, 6);
        tySpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(tySpacer);

        Label tyLabel = new Label();
        tyLabel.Text = "Thank you for your contribution to the game!";
        tyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tyLabel.AddThemeFontOverride("font", PixelFont);
        tyLabel.AddThemeFontSizeOverride("font_size", 11);
        tyLabel.AddThemeColorOverride("font_color", TextPrimary);
        tyLabel.MouseFilter = MouseFilterEnum.Ignore;
        HBoxContainer tyWrapper = new HBoxContainer();
        tyWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tyWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        tyWrapper.MouseFilter = MouseFilterEnum.Ignore;
        tyWrapper.AddChild(tyLabel);
        centerBox.AddChild(tyWrapper);

        // Wallet display (deferred to bottom-right corner, added after content container)

        // Spacer before buttons
        Control btnSpacer = new Control();
        btnSpacer.CustomMinimumSize = new Vector2(0, 40);
        btnSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(btnSpacer);

        // Button container (holds all switchable panels)
        VBoxContainer buttonArea = new VBoxContainer();
        buttonArea.AddThemeConstantOverride("separation", 0);
        buttonArea.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttonArea.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(buttonArea);

        // === MAIN BUTTON PANEL (default view) ===
        _mainButtonContainer = new VBoxContainer();
        _mainButtonContainer.AddThemeConstantOverride("separation", 6);
        _mainButtonContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _mainButtonContainer.MouseFilter = MouseFilterEnum.Ignore;
        buttonArea.AddChild(_mainButtonContainer);

        // Menu buttons with voxel-style borders
        AddMenuButton(_mainButtonContainer, "PLAY ONLINE", AccentGreen, OnPlayOnlinePressed);

        Control spacer1 = new Control();
        spacer1.CustomMinimumSize = new Vector2(0, 12);
        spacer1.MouseFilter = MouseFilterEnum.Ignore;
        _mainButtonContainer.AddChild(spacer1);

        // Bot count selector row
        AddBotCountSelector(_mainButtonContainer);

        AddMenuButton(_mainButtonContainer, "PLAY VS BOTS", AccentGreen, OnPlayBotsPressed);

        Control spacer2 = new Control();
        spacer2.CustomMinimumSize = new Vector2(0, 12);
        spacer2.MouseFilter = MouseFilterEnum.Ignore;
        _mainButtonContainer.AddChild(spacer2);

        AddMenuButton(_mainButtonContainer, "SANDBOX", AccentGold, OnSandboxPressed);

        Control spacer3 = new Control();
        spacer3.CustomMinimumSize = new Vector2(0, 12);
        spacer3.MouseFilter = MouseFilterEnum.Ignore;
        _mainButtonContainer.AddChild(spacer3);

        AddMenuButton(_mainButtonContainer, "SETTINGS", TextSecondary, () => SettingsRequested?.Invoke());
        AddMenuButton(_mainButtonContainer, "HELP", AccentCyan, OnHelpPressed);
        AddMenuButton(_mainButtonContainer, "QUIT", AccentRed, OnQuitPressed);

        // === PLAY ONLINE SUB-PANEL (Host / Join choice) ===
        _playOnlinePanel = new VBoxContainer();
        _playOnlinePanel.AddThemeConstantOverride("separation", 6);
        _playOnlinePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playOnlinePanel.MouseFilter = MouseFilterEnum.Ignore;
        _playOnlinePanel.Visible = false;
        buttonArea.AddChild(_playOnlinePanel);

        AddSubMenuHeader(_playOnlinePanel, "PLAY ONLINE");
        AddMenuButton(_playOnlinePanel, "HOST GAME", AccentGreen, OnHostPressed);
        AddMenuButton(_playOnlinePanel, "JOIN GAME", AccentGold, OnJoinPressed);
        AddMenuButton(_playOnlinePanel, "BACK", TextSecondary, OnBackToMainMenu);

        // === HOST PANEL (Open / Private choice + lobby code display) ===
        _hostPanel = new VBoxContainer();
        _hostPanel.AddThemeConstantOverride("separation", 6);
        _hostPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _hostPanel.MouseFilter = MouseFilterEnum.Ignore;
        _hostPanel.Visible = false;
        buttonArea.AddChild(_hostPanel);

        AddSubMenuHeader(_hostPanel, "HOST GAME");
        AddMenuButton(_hostPanel, "OPEN LOBBY", AccentGreen, OnHostOpenLobby);
        AddMenuButton(_hostPanel, "PRIVATE (CODE ONLY)", AccentGold, OnHostPrivateLobby);

        // Lobby code display (hidden until a lobby is created) — styled panel
        _lobbyCodeLabel = new Label();
        _lobbyCodeLabel.Text = "";
        _lobbyCodeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _lobbyCodeLabel.AddThemeFontOverride("font", PixelFont);
        _lobbyCodeLabel.AddThemeFontSizeOverride("font_size", 20);
        _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
        _lobbyCodeLabel.MouseFilter = MouseFilterEnum.Ignore;

        PanelContainer codePanel = new PanelContainer();
        codePanel.CustomMinimumSize = new Vector2(360, 56);
        StyleBoxFlat codePanelStyle = CreatePanelStyle(ButtonNormal, 0);
        codePanelStyle.BorderWidthLeft = 4;
        codePanelStyle.BorderWidthTop = 2;
        codePanelStyle.BorderWidthRight = 2;
        codePanelStyle.BorderWidthBottom = 4;
        codePanelStyle.BorderColor = AccentGreen;
        codePanelStyle.ContentMarginLeft = 20;
        codePanelStyle.ContentMarginRight = 20;
        codePanelStyle.ContentMarginTop = 10;
        codePanelStyle.ContentMarginBottom = 10;
        codePanel.AddThemeStyleboxOverride("panel", codePanelStyle);
        codePanel.AddChild(_lobbyCodeLabel);
        codePanel.Visible = false;

        HBoxContainer codeWrapper = new HBoxContainer();
        codeWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        codeWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        codeWrapper.MouseFilter = MouseFilterEnum.Ignore;
        codeWrapper.AddChild(codePanel);
        _hostPanel.AddChild(codeWrapper);
        // Store reference so we can toggle visibility on the panel
        _lobbyCodePanel = codePanel;

        // Status label for host (e.g. "Waiting for players...")
        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AddThemeFontOverride("font", PixelFont);
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
        _statusLabel.MouseFilter = MouseFilterEnum.Ignore;
        _statusLabel.Visible = false;
        HBoxContainer statusWrapper = new HBoxContainer();
        statusWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        statusWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        statusWrapper.MouseFilter = MouseFilterEnum.Ignore;
        statusWrapper.AddChild(_statusLabel);
        _hostPanel.AddChild(statusWrapper);

        AddMenuButton(_hostPanel, "BACK", TextSecondary, OnBackToPlayOnline);

        // === JOIN PANEL (Join Random / Enter Code) ===
        _joinPanel = new VBoxContainer();
        _joinPanel.AddThemeConstantOverride("separation", 6);
        _joinPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _joinPanel.MouseFilter = MouseFilterEnum.Ignore;
        _joinPanel.Visible = false;
        buttonArea.AddChild(_joinPanel);

        AddSubMenuHeader(_joinPanel, "JOIN GAME");
        AddJoinCodeInput(_joinPanel);
        AddMenuButton(_joinPanel, "BACK", TextSecondary, OnBackToPlayOnline);

        // === SANDBOX SLOTS PANEL (slot selection + purchase) ===
        _sandboxSlotsPanel = new VBoxContainer();
        _sandboxSlotsPanel.AddThemeConstantOverride("separation", 6);
        _sandboxSlotsPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sandboxSlotsPanel.MouseFilter = MouseFilterEnum.Ignore;
        _sandboxSlotsPanel.Visible = false;
        buttonArea.AddChild(_sandboxSlotsPanel);

        AddSubMenuHeader(_sandboxSlotsPanel, "SANDBOX BUILDS");

        // Builder's Edition label
        Label builderLabel = new Label();
        builderLabel.Text = "BUILDER'S EDITION — UNLIMITED SLOTS";
        builderLabel.HorizontalAlignment = HorizontalAlignment.Center;
        builderLabel.AddThemeFontOverride("font", PixelFont);
        builderLabel.AddThemeFontSizeOverride("font_size", 8);
        builderLabel.AddThemeColorOverride("font_color", AccentGold);
        builderLabel.MouseFilter = MouseFilterEnum.Ignore;
        _sandboxSlotsPanel.AddChild(builderLabel);

        // "+ NEW BUILD" button (always visible, above the scroll list)
        AddMenuButton(_sandboxSlotsPanel, "+ NEW BUILD", AccentGreen, () => OnSandboxSlotSelected(null));

        // Scrollable slot list for saved builds
        ScrollContainer sandboxScroll = new ScrollContainer();
        sandboxScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        sandboxScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sandboxScroll.CustomMinimumSize = new Vector2(0, 400);
        sandboxScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        sandboxScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        sandboxScroll.MouseFilter = MouseFilterEnum.Stop;
        _sandboxSlotsPanel.AddChild(sandboxScroll);

        _sandboxSlotList = new VBoxContainer();
        _sandboxSlotList.AddThemeConstantOverride("separation", 4);
        _sandboxSlotList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sandboxSlotList.MouseFilter = MouseFilterEnum.Ignore;
        sandboxScroll.AddChild(_sandboxSlotList);

        // Back button
        AddMenuButton(_sandboxSlotsPanel, "BACK", TextSecondary, OnBackToMainMenu);

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.SizeFlagsStretchRatio = 1.2f;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(bottomSpacer);

        // Version bar at bottom
        PanelContainer versionBar = new PanelContainer();
        StyleBoxFlat versionStyle = CreatePanelStyle(new Color(BgPanel.R, BgPanel.G, BgPanel.B, 0.8f), 0);
        versionBar.AddThemeStyleboxOverride("panel", versionStyle);
        versionBar.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer.AddChild(versionBar);

        MarginContainer versionMargin = new MarginContainer();
        versionMargin.AddThemeConstantOverride("margin_left", 20);
        versionMargin.AddThemeConstantOverride("margin_right", 20);
        versionMargin.AddThemeConstantOverride("margin_top", 8);
        versionMargin.AddThemeConstantOverride("margin_bottom", 8);
        versionMargin.MouseFilter = MouseFilterEnum.Ignore;
        versionBar.AddChild(versionMargin);

        HBoxContainer versionContent = new HBoxContainer();
        versionContent.MouseFilter = MouseFilterEnum.Ignore;
        versionMargin.AddChild(versionContent);

        Label versionLabel = new Label();
        versionLabel.Text = "VOXEL SIEGE  v0.1.0-alpha  BUILDER'S EDITION";
        versionLabel.AddThemeFontOverride("font", PixelFont);
        versionLabel.AddThemeFontSizeOverride("font_size", 12);
        versionLabel.AddThemeColorOverride("font_color", TextSecondary);
        versionLabel.MouseFilter = MouseFilterEnum.Ignore;
        versionContent.AddChild(versionLabel);

        Control versionSpc = new Control();
        versionSpc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        versionSpc.MouseFilter = MouseFilterEnum.Ignore;
        versionContent.AddChild(versionSpc);

        Label engineLabel = new Label();
        engineLabel.Text = "Godot 4.3+  |  C#";
        engineLabel.AddThemeFontOverride("font", PixelFont);
        engineLabel.AddThemeFontSizeOverride("font_size", 12);
        engineLabel.AddThemeColorOverride("font_color", TextSecondary);
        engineLabel.MouseFilter = MouseFilterEnum.Ignore;
        versionContent.AddChild(engineLabel);

        // Subscribe to phase changes
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
        }

        // Spawn initial falling blocks
        for (int i = 0; i < 6; i++)
        {
            SpawnFallingBlock(true);
        }

        // Wallet label - positioned in bottom-right corner with dark backing for readability
        PanelContainer walletPanel = new PanelContainer();
        walletPanel.MouseFilter = MouseFilterEnum.Ignore;
        walletPanel.AnchorLeft = 1.0f;
        walletPanel.AnchorTop = 1.0f;
        walletPanel.AnchorRight = 1.0f;
        walletPanel.AnchorBottom = 1.0f;
        walletPanel.OffsetLeft = -280;
        walletPanel.OffsetTop = -56;
        walletPanel.OffsetRight = -16;
        walletPanel.OffsetBottom = -16;
        StyleBoxFlat walletStyle = new StyleBoxFlat();
        walletStyle.BgColor = new Color(0.05f, 0.07f, 0.09f, 0.85f);
        walletStyle.SetCornerRadiusAll(4);
        walletStyle.SetContentMarginAll(10);
        walletPanel.AddThemeStyleboxOverride("panel", walletStyle);
        AddChild(walletPanel);

        _walletLabel = new Label();
        _walletLabel.Text = "";
        _walletLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _walletLabel.AddThemeFontOverride("font", PixelFont);
        _walletLabel.AddThemeFontSizeOverride("font_size", 16);
        _walletLabel.AddThemeColorOverride("font_color", AccentGold);
        _walletLabel.MouseFilter = MouseFilterEnum.Ignore;
        walletPanel.AddChild(_walletLabel);

        RefreshWalletDisplay();

        // Animate in: stagger button appearances
        AnimateEntrance();
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        float dt = (float)delta;
        _time += dt;

        // Brute-force centering: set position/size every frame to guarantee center alignment
        if (_contentContainer != null)
        {
            Vector2 viewSize = GetViewportRect().Size;
            float contentW = viewSize.X * 0.5f;
            _contentContainer.Position = new Vector2((viewSize.X - contentW) * 0.5f, 0f);
            _contentContainer.Size = new Vector2(contentW, viewSize.Y);
        }

        AnimateTitleWave(dt);
        AnimateFallingBlocks(dt);
        AnimateSubtitleReveal(dt);
    }

    public void RequestPlayOnline() => PlayOnlineRequested?.Invoke();
    public void RequestPlayBots() => PlayBotsRequested?.Invoke();
    public void RequestSettings() => SettingsRequested?.Invoke();
    public void RequestQuit() => QuitRequested?.Invoke();

    // =====================================================================
    // VOXEL BLOCK TITLE
    // =====================================================================
    private void BuildVoxelTitle(Control parent)
    {
        bool[][,] word1 = { LetterV, LetterO, LetterX, LetterE, LetterL };
        bool[][,] word2 = { LetterS, LetterI, LetterE, LetterG, LetterE };

        const int blockSize = 12;
        const int gap = 2;        // gap between blocks in a letter
        const int letterGap = 16; // gap between letters
        const int wordGap = 36;   // gap between words
        const int letterW = 5;
        const int letterH = 7;

        // Calculate total width and height for backdrop
        int word1Width = word1.Length * (letterW * (blockSize + gap) - gap) + (word1.Length - 1) * letterGap;
        int word2Width = word2.Length * (letterW * (blockSize + gap) - gap) + (word2.Length - 1) * letterGap;
        int totalWidth = word1Width + wordGap + word2Width;
        int totalHeight = letterH * (blockSize + gap) - gap;

        float startX = (GetViewportRect().Size.X - totalWidth) / 2f;
        float startY = 50f;

        // Dark semi-transparent backdrop panel behind the title for contrast
        const float backdropPadX = 32f;
        const float backdropPadY = 20f;
        PanelContainer titleBackdrop = new PanelContainer();
        titleBackdrop.Position = new Vector2(startX - backdropPadX, startY - backdropPadY);
        titleBackdrop.Size = new Vector2(totalWidth + backdropPadX * 2, totalHeight + backdropPadY * 2);
        titleBackdrop.MouseFilter = MouseFilterEnum.Ignore;
        StyleBoxFlat backdropStyle = new StyleBoxFlat();
        backdropStyle.BgColor = new Color(0.02f, 0.04f, 0.08f, 0.75f);
        backdropStyle.BorderWidthBottom = 2;
        backdropStyle.BorderWidthTop = 2;
        backdropStyle.BorderWidthLeft = 2;
        backdropStyle.BorderWidthRight = 2;
        backdropStyle.BorderColor = new Color(0.3f, 0.5f, 0.9f, 0.35f);
        backdropStyle.CornerRadiusTopLeft = 4;
        backdropStyle.CornerRadiusTopRight = 4;
        backdropStyle.CornerRadiusBottomLeft = 4;
        backdropStyle.CornerRadiusBottomRight = 4;
        backdropStyle.ShadowColor = new Color(0.2f, 0.4f, 1.0f, 0.15f);
        backdropStyle.ShadowSize = 12;
        titleBackdrop.AddThemeStyleboxOverride("panel", backdropStyle);
        parent.AddChild(titleBackdrop);

        float curX = startX;

        // Render word "VOXEL"
        foreach (bool[,] letter in word1)
        {
            RenderLetter(parent, letter, curX, startY, blockSize, gap, true);
            curX += letterW * (blockSize + gap) - gap + letterGap;
        }

        curX += wordGap - letterGap;

        // Render word "SIEGE"
        foreach (bool[,] letter in word2)
        {
            RenderLetter(parent, letter, curX, startY, blockSize, gap, false);
            curX += letterW * (blockSize + gap) - gap + letterGap;
        }
    }

    private void RenderLetter(Control parent, bool[,] grid, float originX, float originY,
                               int blockSize, int gap, bool isFirstWord)
    {
        int rows = grid.GetLength(0);
        int cols = grid.GetLength(1);

        // First pass: render shadow/outline blocks (offset dark copies for depth)
        const float shadowOffsetX = 3f;
        const float shadowOffsetY = 3f;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!grid[r, c]) continue;

                ColorRect shadow = new ColorRect();
                shadow.CustomMinimumSize = new Vector2(blockSize + 1, blockSize + 1);
                shadow.Size = new Vector2(blockSize + 1, blockSize + 1);

                float px = originX + c * (blockSize + gap);
                float py = originY + r * (blockSize + gap);
                shadow.Position = new Vector2(px + shadowOffsetX, py + shadowOffsetY);
                shadow.Color = TitleShadowColor;
                shadow.MouseFilter = MouseFilterEnum.Ignore;

                parent.AddChild(shadow);
            }
        }

        // Second pass: render main textured blocks on top
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!grid[r, c]) continue;

                TextureRect block = new TextureRect();
                block.CustomMinimumSize = new Vector2(blockSize, blockSize);
                block.Size = new Vector2(blockSize, blockSize);
                block.Texture = isFirstWord ? TitleTexGreen : TitleTexGold;
                block.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                block.StretchMode = TextureRect.StretchModeEnum.Tile;
                block.TextureFilter = CanvasItem.TextureFilterEnum.Nearest; // crisp pixels!

                float px = originX + c * (blockSize + gap);
                float py = originY + r * (blockSize + gap);
                block.Position = new Vector2(px, py);

                // Apply color modulation for variation + tinting (brighter range)
                Color baseColor = isFirstWord ? TitleGreen1 : TitleGold1;
                Color altColor = isFirstWord ? TitleGreen2 : TitleGold2;
                float variation = _rng.RandfRange(0f, 1f);
                Color tint = baseColor.Lerp(altColor, variation * 0.6f);
                float bright = _rng.RandfRange(1.0f, 1.2f);
                block.SelfModulate = new Color(
                    Mathf.Clamp(tint.R * bright, 0f, 1f),
                    Mathf.Clamp(tint.G * bright, 0f, 1f),
                    Mathf.Clamp(tint.B * bright, 0f, 1f),
                    1f
                );
                block.MouseFilter = MouseFilterEnum.Ignore;

                // Store grid position for wave animation
                block.SetMeta("base_x", px);
                block.SetMeta("base_y", py);
                block.SetMeta("grid_col", c + (isFirstWord ? 0 : 30)); // global column for wave phase

                parent.AddChild(block);
                _titleBlocks.Add(block);
            }
        }
    }

    private void AnimateTitleWave(float delta)
    {
        foreach (Control block in _titleBlocks)
        {
            float baseX = (float)block.GetMeta("base_x");
            float baseY = (float)block.GetMeta("base_y");
            int col = (int)block.GetMeta("grid_col");

            // Gentle floating wave
            float waveOffset = Mathf.Sin(_time * 1.5f + col * 0.3f) * 4f;
            block.Position = new Vector2(baseX, baseY + waveOffset);
        }
    }

    // =====================================================================
    // FALLING TETROMINO BLOCKS (background)
    // =====================================================================
    private void SpawnFallingBlock(bool randomY)
    {
        if (_fallingBlockLayer == null) return;

        int shapeIdx = _rng.RandiRange(0, Tetrominoes.Length - 1);
        int[][] shape = Tetrominoes[shapeIdx];

        Color[] fallingColors = { AccentGreen, AccentGold, TextSecondary, new Color("1a6b2d"), new Color("8b6914") };
        Color baseColor = fallingColors[_rng.RandiRange(0, fallingColors.Length - 1)];

        float blockSize = _rng.RandfRange(6f, 12f);
        float alpha = _rng.RandfRange(0.06f, 0.18f);

        float viewW = GetViewportRect().Size.X;
        float viewH = GetViewportRect().Size.Y;
        float xPos = _rng.RandfRange(0, viewW);
        float yPos = randomY ? _rng.RandfRange(-200, viewH) : _rng.RandfRange(-200, -50);

        List<ColorRect> rects = new List<ColorRect>();
        foreach (int[] cell in shape)
        {
            ColorRect rect = new ColorRect();
            rect.CustomMinimumSize = new Vector2(blockSize, blockSize);
            rect.Size = new Vector2(blockSize, blockSize);
            rect.Color = new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
            rect.MouseFilter = MouseFilterEnum.Ignore;
            _fallingBlockLayer.AddChild(rect);
            rects.Add(rect);
        }

        FallingBlock fb = new FallingBlock
        {
            Rects = rects,
            ShapeIndex = shapeIdx,
            X = xPos,
            Y = yPos,
            Speed = _rng.RandfRange(20f, 60f),
            Rotation = _rng.RandfRange(0f, Mathf.Tau),
            RotSpeed = _rng.RandfRange(-0.3f, 0.3f),
        };
        _fallingBlocks.Add(fb);
    }

    private void AnimateFallingBlocks(float delta)
    {
        Vector2 viewportSize = GetViewportRect().Size;

        for (int i = _fallingBlocks.Count - 1; i >= 0; i--)
        {
            FallingBlock fb = _fallingBlocks[i];
            fb.Y += fb.Speed * delta;
            fb.Rotation += fb.RotSpeed * delta;
            _fallingBlocks[i] = fb;

            if (fb.Y > viewportSize.Y + 100)
            {
                // Remove and respawn
                foreach (ColorRect r in fb.Rects)
                {
                    r.QueueFree();
                }
                _fallingBlocks.RemoveAt(i);
                SpawnFallingBlock(false);
                continue;
            }

            // Position each cell of the tetromino using the stored shape
            float blockSize = fb.Rects[0].Size.X;
            float cos = Mathf.Cos(fb.Rotation);
            float sin = Mathf.Sin(fb.Rotation);
            int[][] shape = Tetrominoes[fb.ShapeIndex];

            for (int j = 0; j < fb.Rects.Count; j++)
            {
                float localX = shape[j][0] * (blockSize + 2) - blockSize;
                float localY = shape[j][1] * (blockSize + 2) - blockSize;

                // Rotate
                float rx = localX * cos - localY * sin;
                float ry = localX * sin + localY * cos;

                fb.Rects[j].Position = new Vector2(fb.X + rx, fb.Y + ry);
            }
        }
    }

    // =====================================================================
    // SUBTITLE LETTER-BY-LETTER REVEAL
    // =====================================================================
    private void AnimateSubtitleReveal(float delta)
    {
        if (_subtitleLabel == null) return;
        if (_subtitleRevealIndex >= SubtitleFull.Length) return;

        _subtitleRevealTimer += delta;
        float interval = 0.06f;

        while (_subtitleRevealTimer >= interval && _subtitleRevealIndex < SubtitleFull.Length)
        {
            _subtitleRevealIndex++;
            _subtitleLabel.Text = SubtitleFull.Substring(0, _subtitleRevealIndex);
            _subtitleRevealTimer -= interval;
        }
    }

    // =====================================================================
    // BUTTONS (voxel-style)
    // =====================================================================
    private void AddMenuButton(VBoxContainer container, string text, Color accentColor, Action handler)
    {
        PanelContainer btnPanel = new PanelContainer();
        btnPanel.CustomMinimumSize = new Vector2(360, 48);

        // Voxel-style: square corners (0 radius), thick pixelated border
        StyleBoxFlat normalStyle = CreatePanelStyle(ButtonNormal, 0);
        normalStyle.BorderWidthLeft = 4;
        normalStyle.BorderWidthTop = 2;
        normalStyle.BorderWidthRight = 2;
        normalStyle.BorderWidthBottom = 4;
        normalStyle.BorderColor = accentColor;
        normalStyle.ContentMarginLeft = 20;
        normalStyle.ContentMarginRight = 20;
        normalStyle.ContentMarginTop = 10;
        normalStyle.ContentMarginBottom = 10;
        btnPanel.AddThemeStyleboxOverride("panel", normalStyle);

        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", accentColor);
        btn.AddThemeColorOverride("font_pressed_color", accentColor);
        btn.Alignment = HorizontalAlignment.Center;
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); handler(); };

        // Hover styling via signals
        btn.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); OnButtonHover(btnPanel, accentColor, true); };
        btn.MouseExited += () => OnButtonHover(btnPanel, accentColor, false);

        btnPanel.AddChild(btn);

        // Wrap in an HBoxContainer that centers horizontally
        // (VBoxContainer children always get full width; ShrinkCenter does not work)
        HBoxContainer wrapper = new HBoxContainer();
        wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wrapper.Alignment = BoxContainer.AlignmentMode.Center;
        wrapper.MouseFilter = MouseFilterEnum.Ignore;
        wrapper.AddChild(btnPanel);
        container.AddChild(wrapper);

        // Start invisible for stagger animation
        btnPanel.Modulate = new Color(1, 1, 1, 0);
        btnPanel.Scale = new Vector2(0.95f, 0.95f);
        _menuButtons.Add(btnPanel);
    }

    private void AddSubMenuHeader(VBoxContainer container, string text)
    {
        Label header = new Label();
        header.Text = text;
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeFontOverride("font", PixelFont);
        header.AddThemeFontSizeOverride("font_size", 16);
        header.AddThemeColorOverride("font_color", AccentGold);
        header.MouseFilter = MouseFilterEnum.Ignore;

        HBoxContainer headerWrapper = new HBoxContainer();
        headerWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        headerWrapper.MouseFilter = MouseFilterEnum.Ignore;
        headerWrapper.AddChild(header);
        container.AddChild(headerWrapper);

        // Small spacer below header
        Control spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 8);
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(spacer);
    }

    private LineEdit? _joinCodeInput;

    private void AddJoinCodeInput(VBoxContainer container)
    {
        // Outer wrapper to center the row horizontally
        HBoxContainer wrapper = new HBoxContainer();
        wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wrapper.Alignment = BoxContainer.AlignmentMode.Center;
        wrapper.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(wrapper);

        // Panel with voxel-style border matching other buttons
        PanelContainer panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(360, 48);

        StyleBoxFlat panelStyle = CreatePanelStyle(ButtonNormal, 0);
        panelStyle.BorderWidthLeft = 4;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderWidthBottom = 4;
        panelStyle.BorderColor = AccentGold;
        panelStyle.ContentMarginLeft = 12;
        panelStyle.ContentMarginRight = 12;
        panelStyle.ContentMarginTop = 6;
        panelStyle.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        wrapper.AddChild(panel);

        // Inner row: label + text input + join button
        HBoxContainer row = new HBoxContainer();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        Label codeLabel = new Label();
        codeLabel.Text = "CODE:";
        codeLabel.AddThemeFontOverride("font", PixelFont);
        codeLabel.AddThemeFontSizeOverride("font_size", 11);
        codeLabel.AddThemeColorOverride("font_color", TextSecondary);
        codeLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(codeLabel);

        _joinCodeInput = new LineEdit();
        _joinCodeInput.PlaceholderText = "GAME CODE";
        _joinCodeInput.CustomMinimumSize = new Vector2(200, 32);
        _joinCodeInput.MaxLength = 45; // supports both 7-char codes and raw IP
        _joinCodeInput.AddThemeFontOverride("font", PixelFont);
        _joinCodeInput.AddThemeFontSizeOverride("font_size", 12);
        _joinCodeInput.AddThemeColorOverride("font_color", TextPrimary);
        _joinCodeInput.AddThemeColorOverride("font_placeholder_color", TextSecondary);
        _joinCodeInput.MouseFilter = MouseFilterEnum.Stop;
        // Style with square corners and dark background
        StyleBoxFlat inputStyle = CreatePanelStyle(new Color(0.04f, 0.06f, 0.08f, 0.95f), 0);
        inputStyle.BorderWidthTop = 1;
        inputStyle.BorderWidthBottom = 1;
        inputStyle.BorderWidthLeft = 1;
        inputStyle.BorderWidthRight = 1;
        inputStyle.BorderColor = BorderColor;
        inputStyle.ContentMarginLeft = 8;
        inputStyle.ContentMarginRight = 8;
        inputStyle.ContentMarginTop = 4;
        inputStyle.ContentMarginBottom = 4;
        _joinCodeInput.AddThemeStyleboxOverride("normal", inputStyle);
        // Focus style
        StyleBoxFlat focusStyle = CreatePanelStyle(new Color(0.06f, 0.08f, 0.10f, 0.95f), 0);
        focusStyle.BorderWidthTop = 1;
        focusStyle.BorderWidthBottom = 1;
        focusStyle.BorderWidthLeft = 1;
        focusStyle.BorderWidthRight = 1;
        focusStyle.BorderColor = AccentGold;
        focusStyle.ContentMarginLeft = 8;
        focusStyle.ContentMarginRight = 8;
        focusStyle.ContentMarginTop = 4;
        focusStyle.ContentMarginBottom = 4;
        _joinCodeInput.AddThemeStyleboxOverride("focus", focusStyle);
        _joinCodeInput.TextSubmitted += (_) => OnJoinWithCode();
        row.AddChild(_joinCodeInput);

        // JOIN button
        Button joinBtn = new Button();
        joinBtn.Text = "JOIN";
        joinBtn.CustomMinimumSize = new Vector2(72, 32);
        joinBtn.AddThemeFontOverride("font", PixelFont);
        joinBtn.AddThemeFontSizeOverride("font_size", 11);
        joinBtn.AddThemeColorOverride("font_color", AccentGold);
        joinBtn.AddThemeColorOverride("font_hover_color", TextPrimary);
        joinBtn.MouseFilter = MouseFilterEnum.Stop;
        joinBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); OnJoinWithCode(); };
        row.AddChild(joinBtn);

        // Start invisible for stagger animation (matches menu buttons)
        panel.Modulate = new Color(1, 1, 1, 0);
        panel.Scale = new Vector2(0.95f, 0.95f);
        _menuButtons.Add(panel);
    }

    private void AddBotCountSelector(VBoxContainer container)
    {
        // Outer wrapper to center the row horizontally
        HBoxContainer wrapper = new HBoxContainer();
        wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wrapper.Alignment = BoxContainer.AlignmentMode.Center;
        wrapper.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(wrapper);

        // Panel with voxel-style border matching other buttons
        PanelContainer panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(360, 48);

        StyleBoxFlat panelStyle = CreatePanelStyle(ButtonNormal, 0);
        panelStyle.BorderWidthLeft = 4;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderWidthBottom = 4;
        panelStyle.BorderColor = AccentGreen;
        panelStyle.ContentMarginLeft = 20;
        panelStyle.ContentMarginRight = 20;
        panelStyle.ContentMarginTop = 6;
        panelStyle.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        wrapper.AddChild(panel);

        // Inner row: label  [-]  count  [+]
        HBoxContainer row = new HBoxContainer();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        Label botsLabel = new Label();
        botsLabel.Text = "BOTS:";
        botsLabel.AddThemeFontOverride("font", PixelFont);
        botsLabel.AddThemeFontSizeOverride("font_size", 12);
        botsLabel.AddThemeColorOverride("font_color", TextSecondary);
        botsLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(botsLabel);

        // Spacer to push controls to the right
        Control spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(spacer);

        Button minusBtn = new Button();
        minusBtn.Text = "-";
        minusBtn.CustomMinimumSize = new Vector2(36, 36);
        minusBtn.AddThemeFontOverride("font", PixelFont);
        minusBtn.AddThemeFontSizeOverride("font_size", 14);
        minusBtn.AddThemeColorOverride("font_color", TextPrimary);
        minusBtn.AddThemeColorOverride("font_hover_color", AccentGreen);
        minusBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); ChangeBotCount(-1); };
        row.AddChild(minusBtn);

        _botCountLabel = new Label();
        _botCountLabel.Text = _botCount.ToString();
        _botCountLabel.CustomMinimumSize = new Vector2(28, 0);
        _botCountLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _botCountLabel.AddThemeFontOverride("font", PixelFont);
        _botCountLabel.AddThemeFontSizeOverride("font_size", 14);
        _botCountLabel.AddThemeColorOverride("font_color", AccentGreen);
        _botCountLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_botCountLabel);

        Button plusBtn = new Button();
        plusBtn.Text = "+";
        plusBtn.CustomMinimumSize = new Vector2(36, 36);
        plusBtn.AddThemeFontOverride("font", PixelFont);
        plusBtn.AddThemeFontSizeOverride("font_size", 14);
        plusBtn.AddThemeColorOverride("font_color", TextPrimary);
        plusBtn.AddThemeColorOverride("font_hover_color", AccentGreen);
        plusBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); ChangeBotCount(1); };
        row.AddChild(plusBtn);

        // Start invisible for stagger animation (matches menu buttons)
        panel.Modulate = new Color(1, 1, 1, 0);
        panel.Scale = new Vector2(0.95f, 0.95f);
        _menuButtons.Add(panel);
    }

    private void ChangeBotCount(int delta)
    {
        _botCount = Math.Clamp(_botCount + delta, 1, 3);
        if (_botCountLabel != null)
        {
            _botCountLabel.Text = _botCount.ToString();
        }
    }

    private void OnButtonHover(PanelContainer panel, Color accent, bool entered)
    {
        // Voxel-style: square corners, thicker border on hover
        StyleBoxFlat style = CreatePanelStyle(entered ? ButtonHover : ButtonNormal, 0);
        style.BorderWidthLeft = entered ? 6 : 4;
        style.BorderWidthTop = entered ? 3 : 2;
        style.BorderWidthRight = entered ? 3 : 2;
        style.BorderWidthBottom = entered ? 6 : 4;
        style.BorderColor = accent;
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", style);

        Tween tween = panel.CreateTween();
        tween.TweenProperty(panel, "scale", entered ? new Vector2(1.03f, 1.03f) : Vector2.One, 0.12f)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    // --- Stagger Animation ---
    private void AnimateEntrance()
    {
        for (int i = 0; i < _menuButtons.Count; i++)
        {
            Control btn = _menuButtons[i];
            Tween tween = btn.CreateTween();
            tween.TweenProperty(btn, "modulate:a", 1.0f, 0.3f)
                 .SetDelay(0.4f + i * 0.08f)
                 .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            Tween scaleTween = btn.CreateTween();
            scaleTween.TweenProperty(btn, "scale", Vector2.One, 0.35f)
                      .SetDelay(0.4f + i * 0.08f)
                      .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    // --- Style Helpers ---
    private static StyleBoxFlat CreatePanelStyle(Color bgColor, int cornerRadius)
    {
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.CornerRadiusTopLeft = cornerRadius;
        style.CornerRadiusTopRight = cornerRadius;
        style.CornerRadiusBottomLeft = cornerRadius;
        style.CornerRadiusBottomRight = cornerRadius;
        return style;
    }

    // =====================================================================
    // PANEL SWITCHING (Main Menu <-> Play Online <-> Host / Join)
    // =====================================================================
    private void ShowPanel(VBoxContainer? panelToShow)
    {
        if (_mainButtonContainer != null) _mainButtonContainer.Visible = false;
        if (_playOnlinePanel != null) _playOnlinePanel.Visible = false;
        if (_hostPanel != null) _hostPanel.Visible = false;
        if (_joinPanel != null) _joinPanel.Visible = false;
        if (_sandboxSlotsPanel != null) _sandboxSlotsPanel.Visible = false;

        if (panelToShow != null) panelToShow.Visible = true;
    }

    // =====================================================================
    // LOBBY CODE: encodes the host's LAN IP into a 7-char code
    // so players share a code instead of a raw IP address.
    // 32 chars ^ 7 = 34 billion combinations (covers all IPv4).
    // =====================================================================
    private string GenerateLobbyCode()
    {
        string ip = GetLocalLanAddress();
        return EncodeIpToCode(ip);
    }

    /// <summary>
    /// Encodes an IPv4 address into a 7-character alphanumeric code.
    /// </summary>
    private static string EncodeIpToCode(string ip)
    {
        string[] parts = ip.Split('.');
        if (parts.Length != 4) return "AAAAAAA"; // fallback
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

    /// <summary>
    /// Decodes a 7-character code back to an IPv4 address string.
    /// Returns null if the code is invalid.
    /// </summary>
    private static string? DecodeCodeToIp(string code)
    {
        if (code.Length != 7) return null;
        uint ipNum = 0;
        foreach (char c in code.ToUpperInvariant())
        {
            int idx = System.Array.IndexOf(CodeChars, c);
            if (idx < 0) return null;
            ipNum = ipNum * 32 + (uint)idx;
        }
        // Validate: must be a plausible IP (first octet 1-255)
        byte a = (byte)((ipNum >> 24) & 0xFF);
        if (a == 0) return null;
        return $"{a}.{(ipNum >> 16) & 0xFF}.{(ipNum >> 8) & 0xFF}.{ipNum & 0xFF}";
    }

    /// <summary>
    /// Returns the best local LAN IPv4 address (prefers 192.168.x.x, 10.x.x.x).
    /// </summary>
    private static string GetLocalLanAddress()
    {
        string[] addresses = IP.GetLocalAddresses();
        string? best = null;
        foreach (string addr in addresses)
        {
            if (addr.Contains(':')) continue; // skip IPv6
            if (addr == "127.0.0.1") continue; // skip loopback
            if (addr.StartsWith("192.168.") || addr.StartsWith("10."))
            {
                return addr; // prefer LAN addresses
            }
            if (addr.StartsWith("172."))
            {
                best ??= addr;
                continue;
            }
            best ??= addr;
        }
        return best ?? "127.0.0.1";
    }

    // --- Button Handlers ---
    private void OnPlayOnlinePressed()
    {
        PlayOnlineRequested?.Invoke();
        ShowPanel(_playOnlinePanel);
    }

    private void OnPlayBotsPressed()
    {
        PlayBotsRequested?.Invoke();
        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gameManager != null)
        {
            gameManager.Settings.BotCount = _botCount;
            // Pass the commander name entered by the human player
            string enteredName = _commanderNameInput?.Text?.Trim() ?? string.Empty;
            gameManager.HumanPlayerName = string.IsNullOrWhiteSpace(enteredName) ? null : enteredName;
            gameManager.StartPrototypeMatch();
        }
        Visible = false;
    }

    private void OnSandboxPressed()
    {
        RefreshSandboxSlots();
        ShowPanel(_sandboxSlotsPanel);
    }

    private void RefreshSandboxSlots()
    {
        if (_sandboxSlotList == null) return;

        // Clear existing slot buttons
        foreach (Node child in _sandboxSlotList.GetChildren())
            child.QueueFree();

        // Read profile directly from disk to always get the latest saved builds
        PlayerProfile? profile = SaveSystem.LoadJson<PlayerProfile>("user://profile/player_profile.json");
        List<string> savedBuilds = profile?.SavedBuilds ?? new List<string>();

        // Fallback: scan disk for blueprint files not listed in profile
        string bpDir = ProjectSettings.GlobalizePath("user://blueprints");
        if (System.IO.Directory.Exists(bpDir))
        {
            foreach (string file in System.IO.Directory.GetFiles(bpDir, "*.json"))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(file);
                    var bp = System.Text.Json.JsonSerializer.Deserialize<Building.BlueprintData>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
                    if (bp != null && !string.IsNullOrEmpty(bp.Name) && !savedBuilds.Contains(bp.Name))
                    {
                        GD.Print($"[MainMenu] Recovered orphaned build '{bp.Name}' from {file}");
                        savedBuilds.Add(bp.Name);
                    }
                }
                catch { /* skip unreadable files */ }
            }
        }

        // Show all saved builds with load + delete buttons
        foreach (string buildName in savedBuilds)
        {
            string captured = buildName;

            HBoxContainer row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            row.MouseFilter = MouseFilterEnum.Ignore;

            // Load button (the build name)
            PanelContainer btnPanel = new PanelContainer();
            btnPanel.CustomMinimumSize = new Vector2(300, 48);
            StyleBoxFlat slotStyle = CreatePanelStyle(ButtonNormal, 0);
            slotStyle.BorderWidthLeft = 4;
            slotStyle.BorderWidthTop = 2;
            slotStyle.BorderWidthRight = 2;
            slotStyle.BorderWidthBottom = 4;
            slotStyle.BorderColor = AccentCyan;
            slotStyle.ContentMarginLeft = 20;
            slotStyle.ContentMarginRight = 20;
            slotStyle.ContentMarginTop = 10;
            slotStyle.ContentMarginBottom = 10;
            btnPanel.AddThemeStyleboxOverride("panel", slotStyle);

            Button btn = new Button();
            btn.Text = captured;
            btn.Flat = true;
            btn.AddThemeFontOverride("font", PixelFont);
            btn.AddThemeFontSizeOverride("font_size", 14);
            btn.AddThemeColorOverride("font_color", TextPrimary);
            btn.AddThemeColorOverride("font_hover_color", AccentCyan);
            btn.AddThemeColorOverride("font_pressed_color", AccentCyan);
            btn.Alignment = HorizontalAlignment.Center;
            btn.MouseFilter = MouseFilterEnum.Stop;
            btn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); OnSandboxSlotSelected(captured); };
            btn.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); OnButtonHover(btnPanel, AccentCyan, true); };
            btn.MouseExited += () => OnButtonHover(btnPanel, AccentCyan, false);
            btnPanel.AddChild(btn);
            row.AddChild(btnPanel);

            // Delete button
            PanelContainer delPanel = new PanelContainer();
            delPanel.CustomMinimumSize = new Vector2(48, 48);
            StyleBoxFlat delStyle = CreatePanelStyle(ButtonNormal, 0);
            delStyle.BorderWidthLeft = 2;
            delStyle.BorderWidthTop = 2;
            delStyle.BorderWidthRight = 4;
            delStyle.BorderWidthBottom = 4;
            delStyle.BorderColor = AccentRed;
            delStyle.ContentMarginLeft = 4;
            delStyle.ContentMarginRight = 4;
            delStyle.ContentMarginTop = 10;
            delStyle.ContentMarginBottom = 10;
            delPanel.AddThemeStyleboxOverride("panel", delStyle);

            Button delBtn = new Button();
            delBtn.Text = "\u2716";
            delBtn.Flat = true;
            delBtn.AddThemeFontOverride("font", PixelFont);
            delBtn.AddThemeFontSizeOverride("font_size", 14);
            delBtn.AddThemeColorOverride("font_color", AccentRed);
            delBtn.AddThemeColorOverride("font_hover_color", new Color("ff6666"));
            delBtn.Alignment = HorizontalAlignment.Center;
            delBtn.MouseFilter = MouseFilterEnum.Stop;
            delBtn.Pressed += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_click");
                GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
                gm?.DeleteSandboxBuild(captured);
                RefreshSandboxSlots();
            };
            delBtn.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); OnButtonHover(delPanel, AccentRed, true); };
            delBtn.MouseExited += () => OnButtonHover(delPanel, AccentRed, false);
            delPanel.AddChild(delBtn);
            row.AddChild(delPanel);

            HBoxContainer wrapper = new HBoxContainer();
            wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            wrapper.Alignment = BoxContainer.AlignmentMode.Center;
            wrapper.MouseFilter = MouseFilterEnum.Ignore;
            wrapper.AddChild(row);
            _sandboxSlotList.AddChild(wrapper);
        }

        if (savedBuilds.Count == 0)
        {
            Label emptyLabel = new Label();
            emptyLabel.Text = "No saved builds yet";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.AddThemeFontOverride("font", PixelFont);
            emptyLabel.AddThemeFontSizeOverride("font_size", 8);
            emptyLabel.AddThemeColorOverride("font_color", TextSecondary);
            emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
            _sandboxSlotList.AddChild(emptyLabel);
        }
    }

    private void OnSandboxSlotSelected(string? buildName)
    {
        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gameManager != null)
        {
            gameManager.StartSandboxMode(buildName);
        }
        Visible = false;
    }

    private void OnBuySandboxSlot()
    {
        // Builder's Edition: slots are unlimited, no purchase needed.
        // This method is retained for compatibility but does nothing.
    }

    private void OnHostPressed()
    {
        // Reset host panel state
        if (_lobbyCodePanel != null)
        {
            _lobbyCodePanel.Visible = false;
        }
        if (_lobbyCodeLabel != null)
        {
            _lobbyCodeLabel.Text = "";
        }
        if (_statusLabel != null)
        {
            _statusLabel.Text = "";
            _statusLabel.Visible = false;
        }
        ShowPanel(_hostPanel);
    }

    private void OnHostOpenLobby()
    {
        _generatedLobbyCode = "PENDING"; // Updated when UPnP discovers external IP
        StartHosting(MatchVisibility.Public);
    }

    private void OnHostPrivateLobby()
    {
        _generatedLobbyCode = "PENDING";
        StartHosting(MatchVisibility.Private);
    }

    private async void StartHosting(MatchVisibility visibility)
    {
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        LobbyManager? lobbyManager = GetTree().Root.GetNodeOrNull<LobbyManager>("Main/LobbyManager");
        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");

        if (netManager == null) return;

        // Set display name: prefer entered name, then Steam name, then fallback
        string enteredName = _commanderNameInput?.Text?.Trim() ?? string.Empty;
        string steamName = GetSteamManager()?.PlayerName ?? string.Empty;
        string displayName = !string.IsNullOrWhiteSpace(enteredName) ? enteredName
            : !string.IsNullOrWhiteSpace(steamName) ? steamName
            : "Host";
        netManager.LocalDisplayName = displayName;

        // Pass name to GameManager
        if (gameManager != null)
        {
            gameManager.HumanPlayerName = displayName;
        }

        string visibilityText = visibility == MatchVisibility.Public ? "OPEN" : "PRIVATE";
        _hostVisibility = visibility;

        // Show "creating lobby..." while we set up
        if (_lobbyCodeLabel != null) _lobbyCodeLabel.Text = "CREATING LOBBY...";
        if (_lobbyCodePanel != null) _lobbyCodePanel.Visible = true;
        if (_statusLabel != null)
        {
            _statusLabel.Text = $"{visibilityText} LOBBY  -  SETTING UP...";
            _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
            _statusLabel.Visible = true;
        }

        // Try Steam first — this gives us NAT traversal via Valve's relay servers
        SteamManager? steam = GetSteamManager();
        if (steam != null && steam.IsInitialized)
        {
            GD.Print("[MainMenu] Steam available — hosting via Steam Networking Sockets");

            // 1) Create Steam lobby (generates game code)
            string? gameCode = await steam.CreateLobbyAsync(GameConfig.MaxPlayers);
            if (gameCode == null)
            {
                GD.PrintErr("[MainMenu] Steam lobby creation failed");
                ShowHostError("STEAM LOBBY FAILED");
                return;
            }

            // 2) Start Steam relay server
            Error err = netManager.HostSteam(steam.PlayerSteamId);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[MainMenu] Steam host failed: {err}");
                steam.LeaveLobby();
                ShowHostError($"STEAM HOST FAILED: {err}");
                return;
            }

            // 3) Update UI with game code
            _generatedLobbyCode = gameCode;
            if (_lobbyCodeLabel != null)
            {
                _lobbyCodeLabel.Text = $"CODE:  {gameCode}";
                _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
            }
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"{visibilityText} LOBBY VIA STEAM  -  SHARE THIS CODE";
                _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
            }

            GD.Print($"[MainMenu] Steam lobby ready! Code: {gameCode}");
        }
        else
        {
            // Fallback: ENet + UPnP (requires port forwarding if UPnP fails)
            GD.Print("[MainMenu] Steam not available — falling back to ENet + UPnP");

            netManager.ExternalIpDiscovered -= OnExternalIpDiscovered;
            netManager.ExternalIpDiscovered += OnExternalIpDiscovered;

            Error err = netManager.Host();
            if (err != Error.Ok)
            {
                GD.PrintErr($"[MainMenu] Failed to host: {err}");
                ShowHostError($"HOST FAILED: {err}");
                return;
            }

            if (_lobbyCodeLabel != null) _lobbyCodeLabel.Text = "DISCOVERING...";
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"{visibilityText} LOBBY  -  GETTING PUBLIC IP...";
                _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
            }
        }

        // Configure lobby manager
        if (lobbyManager != null)
        {
            MatchSettings settings = new MatchSettings { Visibility = visibility };
            string lobbyName = visibility == MatchVisibility.Public ? "Open Lobby" : "Private Match";
            lobbyManager.ConfigureLobby(lobbyName, settings);

            string hostName = netManager.LocalDisplayName;
            long hostPeerId = netManager.LocalPeerId;
            GD.Print($"[MainMenu] Adding host to lobby: name='{hostName}', peerId={hostPeerId}, slot=Player1");
            lobbyManager.AddOrUpdateMember(hostPeerId, PlayerSlot.Player1, hostName, false);
            GD.Print($"[MainMenu] Lobby now has {lobbyManager.Members.Count} member(s)");
        }

        HostGameRequested?.Invoke(visibility == MatchVisibility.Public);
    }

    private MatchVisibility _hostVisibility;

    private void ShowHostError(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = message;
            _statusLabel.AddThemeColorOverride("font_color", AccentRed);
            _statusLabel.Visible = true;
        }
        if (_lobbyCodeLabel != null)
        {
            _lobbyCodeLabel.Text = "FAILED";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentRed);
        }
    }

    private SteamManager? GetSteamManager()
    {
        SteamPlatformNode? steamNode = GetTree().Root.GetNodeOrNull<SteamPlatformNode>("Main/SteamPlatform");
        return steamNode?.Steam;
    }

    /// <summary>
    /// Called when NetworkManager discovers the public IP (ENet fallback path).
    /// Updates the displayed game code with the encoded address.
    /// </summary>
    private void OnExternalIpDiscovered(string externalIp)
    {
        string visibilityText = _hostVisibility == MatchVisibility.Public ? "OPEN" : "PRIVATE";

        if (externalIp == "UNKNOWN")
        {
            GD.PrintErr("[MainMenu] Could not discover public IP.");
            if (_lobbyCodeLabel != null)
            {
                _lobbyCodeLabel.Text = "IP LOOKUP FAILED";
                _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentRed);
            }
            if (_statusLabel != null)
            {
                _statusLabel.Text = "COULD NOT DETERMINE PUBLIC IP  -  CHECK INTERNET CONNECTION";
                _statusLabel.AddThemeColorOverride("font_color", AccentRed);
            }
            return;
        }

        _generatedLobbyCode = EncodeIpToCode(externalIp);
        GD.Print($"[MainMenu] Public IP: {externalIp} → code: {_generatedLobbyCode}");

        if (_lobbyCodeLabel != null)
        {
            _lobbyCodeLabel.Text = $"CODE:  {_generatedLobbyCode}";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = $"{visibilityText} LOBBY (ENET)  -  REQUIRES PORT FORWARDING";
            _statusLabel.AddThemeColorOverride("font_color", AccentGold);
        }
    }

    private void OnJoinPressed()
    {
        ShowPanel(_joinPanel);
    }

    private async void OnJoinWithCode()
    {
        string input = _joinCodeInput?.Text?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            GD.Print("[MainMenu] No game code entered.");
            return;
        }

        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager == null) return;

        // Set display name: prefer entered name, then Steam name, then fallback
        string enteredName = _commanderNameInput?.Text?.Trim() ?? string.Empty;
        string steamName = GetSteamManager()?.PlayerName ?? string.Empty;
        string displayName = !string.IsNullOrWhiteSpace(enteredName) ? enteredName
            : !string.IsNullOrWhiteSpace(steamName) ? steamName
            : "Player";
        netManager.LocalDisplayName = displayName;

        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gameManager != null)
        {
            gameManager.HumanPlayerName = displayName;
        }

        // Try Steam first — search for a lobby matching this game code
        SteamManager? steam = GetSteamManager();
        if (steam != null && steam.IsInitialized)
        {
            GD.Print($"[MainMenu] Searching Steam lobbies for code: {input}");
            if (_statusLabel != null)
            {
                _statusLabel.Text = "SEARCHING FOR LOBBY...";
                _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
                _statusLabel.Visible = true;
            }

            bool found = await steam.JoinLobbyByCodeAsync(input);
            if (found)
            {
                // Brief delay to let the lobby state propagate through Valve's servers
                // before establishing the relay connection (reference impl uses 1s)
                await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);

                // Get the host's Steam ID from the lobby owner
                Steamworks.SteamId hostId = steam.GetLobbyHostId();
                GD.Print($"[MainMenu] Found lobby! Host Steam ID: {hostId}");

                Error err = netManager.JoinSteam(steam.PlayerSteamId, hostId);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[MainMenu] Steam join failed: {err}");
                    steam.LeaveLobby();
                    if (_statusLabel != null)
                    {
                        _statusLabel.Text = $"JOIN FAILED: {err}";
                        _statusLabel.AddThemeColorOverride("font_color", AccentRed);
                    }
                    return;
                }

                if (_statusLabel != null)
                {
                    _statusLabel.Text = "CONNECTED VIA STEAM!";
                    _statusLabel.AddThemeColorOverride("font_color", AccentGreen);
                }

                JoinWithCodeRequested?.Invoke(input);
                return;
            }
            else
            {
                GD.Print("[MainMenu] No Steam lobby found — trying ENet fallback");
            }
        }

        // Fallback: decode as IP-based code or raw IP
        string address;
        string? decoded = DecodeCodeToIp(input);
        if (decoded != null)
        {
            address = decoded;
            GD.Print($"[MainMenu] Decoded game code '{input}' → {address}");
        }
        else
        {
            address = input;
            GD.Print($"[MainMenu] Joining with raw address: {address}");
        }

        Error enetErr = netManager.Join(address);
        if (enetErr != Error.Ok)
        {
            GD.PrintErr($"[MainMenu] Failed to join: {enetErr}");
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"JOIN FAILED: {enetErr}";
                _statusLabel.AddThemeColorOverride("font_color", AccentRed);
                _statusLabel.Visible = true;
            }
            return;
        }

        JoinWithCodeRequested?.Invoke(input);
    }

    private void OnBackToMainMenu()
    {
        ShowPanel(_mainButtonContainer);
    }

    private void OnBackToPlayOnline()
    {
        // If we were hosting, shut down the network so we don't leave a dangling server
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager != null)
        {
            netManager.ExternalIpDiscovered -= OnExternalIpDiscovered;
            if (netManager.IsOnline)
            {
                netManager.Shutdown();
            }
        }

        // Leave Steam lobby if we were in one
        SteamManager? steam = GetSteamManager();
        steam?.LeaveLobby();

        ShowPanel(_playOnlinePanel);
    }

    private void OnHelpPressed()
    {
        if (_helpScreen == null)
        {
            _helpScreen = new HelpScreen();
            _helpScreen.Name = "HelpScreen";
            _helpScreen.HelpClosed += OnHelpClosed;
            AddChild(_helpScreen);
        }
        _helpScreen.Open();
        if (_mainButtonContainer != null) _mainButtonContainer.Visible = false;
    }

    private void OnHelpClosed()
    {
        if (_mainButtonContainer != null) _mainButtonContainer.Visible = true;
    }

    private void OnQuitPressed()
    {
        QuitRequested?.Invoke();
        GetTree().Quit();
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Menu;

        // Reset to main button view when returning to menu
        if (payload.CurrentPhase == GamePhase.Menu)
        {
            ShowPanel(_mainButtonContainer);
            RefreshWalletDisplay();
        }
    }

    /// <summary>
    /// Updates the wallet label with the current persistent balance from the player profile.
    /// </summary>
    private void RefreshWalletDisplay()
    {
        if (_walletLabel == null) return;
        ProgressionManager? pm = GetTree().Root.FindChild("ProgressionManager", true, false) as ProgressionManager;
        long balance = pm?.Profile.WalletBalance ?? 0;
        _walletLabel.Text = $"WALLET: ${balance:N0}";
    }
}
