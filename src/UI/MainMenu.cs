using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Networking;

namespace VoxelSiege.UI;

public partial class MainMenu : Control
{
    // --- Theme Colors ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color("161b22");
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color AccentRed = new Color("d73a49");
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
    private static Texture2D TitleTexGreen => _titleTexGreen ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/dirt_32.png");
    private static Texture2D TitleTexGold => _titleTexGold ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/sand_32.png");

    // Voxel title colors
    private static readonly Color TitleGreen1 = new Color("2ea043");
    private static readonly Color TitleGreen2 = new Color("3cb653");
    private static readonly Color TitleGold1 = new Color("d4a029");
    private static readonly Color TitleGold2 = new Color("e8b84a");
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
    /// Fired when the player chooses to join a random open lobby.
    /// </summary>
    public event Action? JoinRandomRequested;

    /// <summary>
    /// Fired when the player enters a lobby code and clicks Join.
    /// The string parameter is the code entered.
    /// </summary>
    public event Action<string>? JoinWithCodeRequested;

    private int _botCount = 1;
    private Label? _botCountLabel;
    private LineEdit? _commanderNameInput;

    private readonly List<Control> _menuButtons = new List<Control>();
    private readonly List<Control> _titleBlocks = new List<Control>();
    private readonly List<FallingBlock> _fallingBlocks = new List<FallingBlock>();
    private Control? _contentContainer;
    private Control? _fallingBlockLayer;
    private Label? _subtitleLabel;
    private float _time;
    private float _subtitleRevealTimer;
    private int _subtitleRevealIndex;
    private const string SubtitleFull = "BUILD.  HIDE.  DESTROY.";
    private RandomNumberGenerator _rng = new RandomNumberGenerator();

    // Play Online sub-menu panels
    private VBoxContainer? _mainButtonContainer;
    private VBoxContainer? _playOnlinePanel;
    private VBoxContainer? _hostPanel;
    private VBoxContainer? _joinPanel;
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
        backdrop.Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.85f);
        AddChild(backdrop);

        // Falling voxel blocks layer (behind content)
        _fallingBlockLayer = new Control();
        _fallingBlockLayer.Name = "FallingBlockLayer";
        _fallingBlockLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        _fallingBlockLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_fallingBlockLayer);

        // Subtle gradient overlay at top
        ColorRect topGradient = new ColorRect();
        topGradient.SetAnchorsPreset(LayoutPreset.TopWide);
        topGradient.OffsetBottom = 250;
        topGradient.CustomMinimumSize = new Vector2(0, 250);
        topGradient.Color = new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.04f);
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

        // Top spacer (tall enough to clear the voxel title)
        Control topSpacer = new Control();
        topSpacer.CustomMinimumSize = new Vector2(0, 180);
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
        _subtitleLabel.AddThemeFontSizeOverride("font_size", 14);
        _subtitleLabel.AddThemeColorOverride("font_color", AccentGold);
        _subtitleLabel.MouseFilter = MouseFilterEnum.Ignore;

        // Wrap in an HBoxContainer that centers horizontally
        HBoxContainer subtitleWrapper = new HBoxContainer();
        subtitleWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        subtitleWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        subtitleWrapper.MouseFilter = MouseFilterEnum.Ignore;
        subtitleWrapper.AddChild(_subtitleLabel);
        centerBox.AddChild(subtitleWrapper);

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

        // Bot count selector row
        AddBotCountSelector(_mainButtonContainer);

        AddMenuButton(_mainButtonContainer, "PLAY VS BOTS", AccentGreen, OnPlayBotsPressed);
        AddMenuButton(_mainButtonContainer, "SETTINGS", TextSecondary, () => SettingsRequested?.Invoke());
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

        // Lobby code display (hidden until a lobby is created)
        _lobbyCodeLabel = new Label();
        _lobbyCodeLabel.Text = "";
        _lobbyCodeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _lobbyCodeLabel.AddThemeFontOverride("font", PixelFont);
        _lobbyCodeLabel.AddThemeFontSizeOverride("font_size", 20);
        _lobbyCodeLabel.AddThemeColorOverride("font_color", AccentGreen);
        _lobbyCodeLabel.MouseFilter = MouseFilterEnum.Ignore;
        _lobbyCodeLabel.Visible = false;
        HBoxContainer codeWrapper = new HBoxContainer();
        codeWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        codeWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        codeWrapper.MouseFilter = MouseFilterEnum.Ignore;
        codeWrapper.AddChild(_lobbyCodeLabel);
        _hostPanel.AddChild(codeWrapper);

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
        AddMenuButton(_joinPanel, "JOIN RANDOM", AccentGreen, OnJoinRandom);
        AddJoinCodeInput(_joinPanel);
        AddMenuButton(_joinPanel, "BACK", TextSecondary, OnBackToPlayOnline);

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
        versionLabel.Text = "VOXEL SIEGE  v0.1.0-alpha";
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

        const int blockSize = 8;
        const int gap = 2;        // gap between blocks in a letter
        const int letterGap = 12; // gap between letters
        const int wordGap = 28;   // gap between words
        const int letterW = 5;

        // Calculate total width
        int word1Width = word1.Length * (letterW * (blockSize + gap) - gap) + (word1.Length - 1) * letterGap;
        int word2Width = word2.Length * (letterW * (blockSize + gap) - gap) + (word2.Length - 1) * letterGap;
        int totalWidth = word1Width + wordGap + word2Width;

        float startX = (GetViewportRect().Size.X - totalWidth) / 2f;
        float startY = 60f;

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

                // Apply color modulation for variation + tinting
                Color baseColor = isFirstWord ? TitleGreen1 : TitleGold1;
                Color altColor = isFirstWord ? TitleGreen2 : TitleGold2;
                float variation = _rng.RandfRange(0f, 1f);
                Color tint = baseColor.Lerp(altColor, variation * 0.5f);
                float bright = _rng.RandfRange(0.9f, 1.1f);
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
            float waveOffset = Mathf.Sin(_time * 1.5f + col * 0.3f) * 3f;
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
        btn.Pressed += handler;

        // Hover styling via signals
        btn.MouseEntered += () => OnButtonHover(btnPanel, accentColor, true);
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
        _joinCodeInput.PlaceholderText = "ENTER CODE";
        _joinCodeInput.CustomMinimumSize = new Vector2(140, 32);
        _joinCodeInput.MaxLength = LobbyCodeLength;
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
        joinBtn.Pressed += OnJoinWithCode;
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
        minusBtn.Pressed += () => ChangeBotCount(-1);
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
        plusBtn.Pressed += () => ChangeBotCount(1);
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

        if (panelToShow != null) panelToShow.Visible = true;
    }

    // =====================================================================
    // LOBBY CODE GENERATION
    // =====================================================================
    private string GenerateLobbyCode()
    {
        char[] code = new char[LobbyCodeLength];
        for (int i = 0; i < LobbyCodeLength; i++)
        {
            code[i] = CodeChars[_rng.RandiRange(0, CodeChars.Length - 1)];
        }
        return new string(code);
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

    private void OnHostPressed()
    {
        // Reset host panel state
        if (_lobbyCodeLabel != null)
        {
            _lobbyCodeLabel.Text = "";
            _lobbyCodeLabel.Visible = false;
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
        _generatedLobbyCode = GenerateLobbyCode();
        StartHosting(MatchVisibility.Public);
    }

    private void OnHostPrivateLobby()
    {
        _generatedLobbyCode = GenerateLobbyCode();
        StartHosting(MatchVisibility.Private);
    }

    private void StartHosting(MatchVisibility visibility)
    {
        // Configure and start the network host
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        LobbyManager? lobbyManager = GetTree().Root.GetNodeOrNull<LobbyManager>("Main/LobbyManager");
        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");

        if (netManager != null)
        {
            Error err = netManager.Host();
            if (err != Error.Ok)
            {
                GD.PrintErr($"[MainMenu] Failed to host: {err}");
                if (_statusLabel != null)
                {
                    _statusLabel.Text = $"HOST FAILED: {err}";
                    _statusLabel.AddThemeColorOverride("font_color", AccentRed);
                    _statusLabel.Visible = true;
                }
                return;
            }
        }

        if (lobbyManager != null)
        {
            MatchSettings settings = new MatchSettings { Visibility = visibility };
            string lobbyName = visibility == MatchVisibility.Public
                ? "Open Lobby"
                : $"Private [{_generatedLobbyCode}]";
            lobbyManager.ConfigureLobby(lobbyName, settings);
        }

        // Show the lobby code prominently
        if (_lobbyCodeLabel != null)
        {
            _lobbyCodeLabel.Text = $"CODE: {_generatedLobbyCode}";
            _lobbyCodeLabel.Visible = true;
        }

        string visibilityText = visibility == MatchVisibility.Public ? "OPEN" : "PRIVATE";
        if (_statusLabel != null)
        {
            _statusLabel.Text = $"{visibilityText} LOBBY CREATED  -  WAITING FOR PLAYERS...";
            _statusLabel.AddThemeColorOverride("font_color", TextSecondary);
            _statusLabel.Visible = true;
        }

        GD.Print($"[MainMenu] Hosting {visibilityText} lobby with code: {_generatedLobbyCode}");
        HostGameRequested?.Invoke(visibility == MatchVisibility.Public);
    }

    private void OnJoinPressed()
    {
        ShowPanel(_joinPanel);
    }

    private void OnJoinRandom()
    {
        GD.Print("[MainMenu] Joining random open lobby...");

        // Attempt to connect to a known address (placeholder: localhost for now).
        // In a real implementation this would query a matchmaking server or Steam lobby list.
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager != null)
        {
            Error err = netManager.Join("127.0.0.1");
            if (err != Error.Ok)
            {
                GD.PrintErr($"[MainMenu] Failed to join random: {err}");
                return;
            }
        }

        JoinRandomRequested?.Invoke();
    }

    private void OnJoinWithCode()
    {
        string code = _joinCodeInput?.Text?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            GD.Print("[MainMenu] No lobby code entered.");
            return;
        }

        GD.Print($"[MainMenu] Joining lobby with code: {code}");

        // Attempt to connect (placeholder: localhost for now).
        // In a real implementation the code would be resolved to an IP/lobby ID
        // via a matchmaking service or Steam lobby metadata lookup.
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager != null)
        {
            Error err = netManager.Join("127.0.0.1");
            if (err != Error.Ok)
            {
                GD.PrintErr($"[MainMenu] Failed to join with code: {err}");
                return;
            }
        }

        JoinWithCodeRequested?.Invoke(code);
    }

    private void OnBackToMainMenu()
    {
        ShowPanel(_mainButtonContainer);
    }

    private void OnBackToPlayOnline()
    {
        // If we were hosting, shut down the network so we don't leave a dangling server
        NetworkManager? netManager = GetTree().Root.GetNodeOrNull<NetworkManager>("Main/NetworkManager");
        if (netManager != null && netManager.IsOnline)
        {
            netManager.Shutdown();
        }

        ShowPanel(_playOnlinePanel);
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
        }
    }
}
