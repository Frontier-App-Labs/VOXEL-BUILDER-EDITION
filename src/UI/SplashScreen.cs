using Godot;
using System;
using System.Collections.Generic;

namespace VoxelSiege.UI;

public partial class SplashScreen : Control
{
    // --- Colors ---
    private static readonly Color BgDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color StoneBase = new Color("5a5045");
    private static readonly Color StoneMid = new Color("6b6358");
    private static readonly Color StoneLight = new Color("7a7268");
    private static readonly Color StoneDark = new Color("3e3830");
    private static readonly Color MortarColor = new Color("4a4238");
    private static readonly Color BattlementAccent = new Color("8a7e6e");
    private static readonly Color TextWhite = new Color("e8e4df");
    private static readonly Color TextDim = new Color("b0aaa0");

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Voxel title textures (lazy-loaded) ---
    private static Texture2D? _titleTexGreen;
    private static Texture2D? _titleTexGold;
    private static Texture2D TitleTexGreen => _titleTexGreen ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/metal_32.png");
    private static Texture2D TitleTexGold => _titleTexGold ??= ResourceLoader.Load<Texture2D>("res://assets/textures/voxels/obsidian_32.png");

    // Voxel title colors (matching MainMenu — steel blue + obsidian purple)
    private static readonly Color TitleGreen1 = new Color("4a9eff");
    private static readonly Color TitleGreen2 = new Color("6bb3ff");
    private static readonly Color TitleGold1 = new Color("6b2fa0");
    private static readonly Color TitleGold2 = new Color("8b4fcf");

    /// <summary>Emitted when the splash sequence is complete and the main menu should appear.</summary>
    [Signal]
    public delegate void SplashFinishedEventHandler();

    // --- Castle block data ---
    private struct CastleBlock
    {
        public ColorRect Rect;
        public float AppearTime;   // when this block should appear
        public bool Visible;
        public float BaseX;
        public float BaseY;
        // Explosion state
        public float VelX;
        public float VelY;
        public float RotSpeed;
        public float Rotation;
        public bool Exploding;
    }

    // --- Explosion debris (title reveal particles) ---
    private struct TitleBlock
    {
        public Control Rect;
        public float BaseX;
        public float BaseY;
        public int GridCol;
        public bool IsFirstWord;
    }

    private readonly List<CastleBlock> _castleBlocks = new();
    private readonly List<TitleBlock> _titleBlocks = new();
    private RandomNumberGenerator _rng = new();

    private Control? _castleLayer;
    private Control? _titleLayer;
    private Label? _studioLabel;
    private Label? _pressAnyKeyLabel;
    private Label? _loadingLabel;
    private Label? _builderEditionLabel;
    private Label? _thankYouLabel;
    private ColorRect? _backdrop;

    private float _elapsed;
    private bool _castleFullyBuilt;
    private float _holdTimer;
    private bool _explosionStarted;
    private float _explosionTimer;
    private bool _titleVisible;
    private float _titleFadeAlpha;
    private bool _finished;
    private float _fadeOutTimer;
    private bool _fadingOut;
    private float _loadingDotTimer;
    private int _loadingDotCount;
    private float _loadingProgress;
    private float _loadingProgressVisual;  // smoothly animated toward _loadingProgress
    private int _loadingBlocksRevealed;
    private bool _loadingExplosionTriggered;

    /// <summary>
    /// When true, the splash screen acts as a loading screen:
    /// title is visible immediately above the castle, a "LOADING..." label is shown,
    /// castle blocks appear proportional to loading progress, and the studio/press-any-key
    /// labels are hidden.
    /// </summary>
    public bool IsLoadingMode { get; set; }

    /// <summary>
    /// Sets the loading progress (0.0 to 1.0). Castle blocks appear proportionally.
    /// When progress reaches 1.0, the castle explodes and the splash fades out.
    /// </summary>
    public void SetLoadingProgress(float progress)
    {
        if (!IsLoadingMode) return;
        _loadingProgress = Mathf.Clamp(progress, 0f, 1f);

        // Update loading label with percentage
        if (_loadingLabel != null)
        {
            int pct = (int)(_loadingProgress * 100f);
            _loadingLabel.Text = "LOADING...";
        }

        // The explosion is triggered by ProcessCastleBuild once both the castle
        // is fully built AND _loadingProgress >= 1.0. No need to trigger here;
        // ProcessCastleBuild runs every frame and will detect the condition.
    }

    // Timing constants
    private const float CastleBuildDuration = 2.5f;  // seconds to build entire castle
    private const float HoldDuration = 1.5f;          // hold after castle built
    private const float ExplosionDuration = 2.0f;      // explosion + title reveal
    private const float TitleHoldDuration = 1.5f;      // hold title before transitioning
    private const float FadeOutDuration = 0.5f;        // fade to black before transition

    // Castle design: an imposing fortress silhouette (column indices for each row, bottom-up)
    // Block size 14x14 with 2px gap
    private static readonly int[][] CastlePattern = {
        // Row 0 (bottom) - wide foundation
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 1 - foundation
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 2 - foundation upper
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 3 - base wall
        new[] {3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37},
        // Row 4 - base wall
        new[] {3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37},
        // Row 5 - walls with gate opening
        new[] {3,4,5,6,7,8,9,10,11,12,13,14,15,      25,26,27,28,29,30,31,32,33,34,35,36,37},
        // Row 6 - walls with gate arch
        new[] {3,4,5,6,7,8,9,10,11,12,13,14,15,16,      24,25,26,27,28,29,30,31,32,33,34,35,36,37},
        // Row 7 - walls with gate arch narrowing
        new[] {3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,  23,24,25,26,27,28,29,30,31,32,33,34,35,36,37},
        // Row 8 - upper walls (towers start emerging)
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 9 - upper walls
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 10 - upper walls
        new[] {2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38},
        // Row 11 - wall tops / towers
        new[] {2,3,4,5,6,7,8,9,     13,14,15,16,17,18,19,20,21,22,23,     31,32,33,34,35,36,37,38},
        // Row 12 - tower bodies + center parapet
        new[] {2,3,4,5,6,7,8,9,     14,15,16,17,18,19,20,21,22,     31,32,33,34,35,36,37,38},
        // Row 13 - tower bodies
        new[] {2,3,4,5,6,7,8,9,     15,16,17,18,19,20,21,     31,32,33,34,35,36,37,38},
        // Row 14 - battlements
        new[] {2,3,4,5,6,7,8,9,     16,17,18,19,20,     32,33,34,35,36,37,38},
        // Row 15 - tower crenellations + center crenellations
        new[] {2,4,6,8,          16,18,20,          32,34,36,38},
        // Row 16 - tower peaks
        new[] {2,3,4,5,6,7,8,9,                        32,33,34,35,36,37,38},
        // Row 17 - tower peaks
        new[] {3,4,5,6,7,8,                            33,34,35,36,37},
        // Row 18 - tower upper
        new[] {3,4,5,6,7,8,                            33,34,35,36,37},
        // Row 19 - tower tips
        new[] {4,5,6,7,                                34,35,36},
        // Row 20 - tower tips
        new[] {4,6,                                    34,36},
        // Row 21 - flag poles
        new[] {5,6,                                    35,36},
        // Row 22 - flag poles
        new[] {5,6,                                    35,36},
        // Row 23 - flags
        new[] {6,7,                                    36,37},
    };

    // --- Pixel Font Letter Definitions (5 wide x 7 tall) --- copied from MainMenu
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

    public override void _Ready()
    {
        // When parented to a CanvasLayer (not a Control), anchors don't work because
        // CanvasLayer has no rect. Force size to viewport so the splash covers everything.
        Vector2 vpSize = GetViewportRect().Size;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Position = Vector2.Zero;
        Size = vpSize;
        MouseFilter = MouseFilterEnum.Stop;
        _rng.Randomize();

        // Full-screen dark backdrop
        _backdrop = new ColorRect();
        _backdrop.Name = "SplashBackdrop";
        _backdrop.Color = BgDark;
        _backdrop.ZIndex = -100;
        _backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_backdrop);
        _backdrop.Position = Vector2.Zero;
        _backdrop.Size = vpSize;

        // In loading mode, if not already inside a CanvasLayer, wrap ourselves in one
        // so the 3D scene's grey clear color doesn't bleed through. The initial splash
        // doesn't need this since nothing is rendering behind it yet.
        if (IsLoadingMode && GetParent() is not CanvasLayer)
        {
            CallDeferred(nameof(WrapInCanvasLayer));
        }

        // Castle layer
        _castleLayer = new Control();
        _castleLayer.Name = "CastleLayer";
        _castleLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        _castleLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_castleLayer);

        // Title layer (for VOXEL SIEGE text)
        _titleLayer = new Control();
        _titleLayer.Name = "TitleLayer";
        _titleLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        _titleLayer.MouseFilter = MouseFilterEnum.Ignore;
        _titleLayer.Modulate = new Color(1, 1, 1, 0); // start invisible
        AddChild(_titleLayer);

        // "Made by Frontier App Labs" label
        _studioLabel = new Label();
        _studioLabel.Name = "StudioLabel";
        _studioLabel.Text = "Made by Frontier App Labs";
        _studioLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _studioLabel.VerticalAlignment = VerticalAlignment.Center;
        _studioLabel.AddThemeFontOverride("font", PixelFont);
        _studioLabel.AddThemeFontSizeOverride("font_size", 14);
        _studioLabel.AddThemeColorOverride("font_color", TextWhite);
        _studioLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        _studioLabel.MouseFilter = MouseFilterEnum.Ignore;
        _studioLabel.Modulate = new Color(1, 1, 1, 0); // start invisible
        AddChild(_studioLabel);

        // Bottom label (empty — splash auto-advances)
        _pressAnyKeyLabel = new Label();
        _pressAnyKeyLabel.Name = "PressAnyKeyLabel";
        _pressAnyKeyLabel.Text = "";
        _pressAnyKeyLabel.SetAnchorsPreset(LayoutPreset.BottomWide);
        _pressAnyKeyLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_pressAnyKeyLabel);

        BuildCastle();
        BuildVoxelTitle();

        // "BUILDER'S EDITION" subtitle — appears with the title after explosion
        float vpW = vpSize.X;
        float vpH = vpSize.Y;

        _builderEditionLabel = new Label();
        _builderEditionLabel.Name = "BuilderEditionLabel";
        _builderEditionLabel.Text = "BUILDER'S EDITION";
        _builderEditionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _builderEditionLabel.AddThemeFontOverride("font", PixelFont);
        _builderEditionLabel.AddThemeFontSizeOverride("font_size", 20);
        _builderEditionLabel.AddThemeColorOverride("font_color", new Color("d74f4f"));
        _builderEditionLabel.Position = new Vector2(0, vpH * 0.5f + 130f);
        _builderEditionLabel.Size = new Vector2(vpW, 50);
        _builderEditionLabel.MouseFilter = MouseFilterEnum.Ignore;
        _builderEditionLabel.Modulate = new Color(1, 1, 1, 1); // visible when title layer fades in
        _titleLayer?.AddChild(_builderEditionLabel);

        _thankYouLabel = new Label();
        _thankYouLabel.Name = "ThankYouLabel";
        _thankYouLabel.Text = "Thank you for your contribution to the game!";
        _thankYouLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _thankYouLabel.AddThemeFontOverride("font", PixelFont);
        _thankYouLabel.AddThemeFontSizeOverride("font_size", 11);
        _thankYouLabel.AddThemeColorOverride("font_color", TextWhite);
        _thankYouLabel.Position = new Vector2(0, vpH * 0.5f + 170f);
        _thankYouLabel.Size = new Vector2(vpW, 35);
        _thankYouLabel.MouseFilter = MouseFilterEnum.Ignore;
        _thankYouLabel.Modulate = new Color(1, 1, 1, 1); // visible when title layer fades in
        _titleLayer?.AddChild(_thankYouLabel);

        if (IsLoadingMode)
        {
            // Title starts hidden — revealed by the explosion, same as the intro splash
            // (no more title-above-castle with wasted blank space after explosion)

            // Hide studio and press-any-key labels
            if (_studioLabel != null)
                _studioLabel.Visible = false;
            if (_pressAnyKeyLabel != null)
                _pressAnyKeyLabel.Visible = false;

            // Castle blocks are animated normally via ProcessCastleBuild (same as initial splash)

            // Add "LOADING" label at the bottom
            _loadingLabel = new Label();
            _loadingLabel.Name = "LoadingLabel";
            _loadingLabel.Text = "LOADING...";
            _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _loadingLabel.AddThemeFontOverride("font", PixelFont);
            _loadingLabel.AddThemeFontSizeOverride("font_size", 10);
            _loadingLabel.AddThemeColorOverride("font_color", TextDim);
            _loadingLabel.SetAnchorsPreset(LayoutPreset.BottomWide);
            _loadingLabel.OffsetTop = -60;
            _loadingLabel.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_loadingLabel);
        }
    }

    public override void _ExitTree()
    {
        // Clean up any running tweens or references
        _castleBlocks.Clear();
        _titleBlocks.Clear();
    }

    /// <summary>
    /// Wraps this SplashScreen in a CanvasLayer so that it renders on top of
    /// any 3D scene, preventing the grey default background from bleeding through.
    /// Called deferred from _Ready when the parent is not already a CanvasLayer.
    /// </summary>
    private void WrapInCanvasLayer()
    {
        Node? parent = GetParent();
        if (parent == null || parent is CanvasLayer) return;

        CanvasLayer layer = new CanvasLayer();
        layer.Name = "SplashCanvasLayer";
        layer.Layer = 100; // render on top of everything

        // Reparent: remove from current parent, add layer, add self to layer
        parent.RemoveChild(this);
        parent.AddChild(layer);
        layer.AddChild(this);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_finished || _fadingOut || IsLoadingMode) return;

        // Skip splash on any key press or mouse click
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            _fadingOut = true;
            _fadeOutTimer = 0f;
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            _fadingOut = true;
            _fadeOutTimer = 0f;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_finished) return;

        float dt = (float)delta;
        _elapsed += dt;

        // Animate loading mode (title wave, loading label)
        if (IsLoadingMode)
        {
            if (_titleVisible)
                AnimateTitleWave(dt);

            // Fade out loading label when explosion starts
            if (_loadingLabel != null && !_loadingExplosionTriggered)
            {
                _loadingDotTimer += dt;
                if (_loadingDotTimer >= 0.4f)
                {
                    _loadingDotTimer -= 0.4f;
                    _loadingDotCount = (_loadingDotCount + 1) % 4;
                    // Only animate dots if progress label hasn't overridden it
                    if (_loadingProgress <= 0f)
                        _loadingLabel.Text = "LOADING" + new string('.', _loadingDotCount);
                }
            }
        }

        if (_fadingOut)
        {
            ProcessFadeOut(dt);
            return;
        }

        if (IsLoadingMode)
        {
            // In loading mode, run the same castle build animation as the initial splash.
            // The explosion is triggered when both the castle is fully built AND loading is done.
            if (!_explosionStarted)
            {
                ProcessCastleBuild(dt);
            }
            else
            {
                ProcessExplosion(dt);
            }
            return;
        }

        if (!_explosionStarted)
        {
            ProcessCastleBuild(dt);
        }
        else
        {
            ProcessExplosion(dt);
        }
    }

    // =====================================================================
    // CASTLE BUILDING
    // =====================================================================
    private void BuildCastle()
    {
        if (_castleLayer == null) return;

        const float blockSize = 14f;
        const float gap = 2f;
        float step = blockSize + gap;

        // Find the max column to center the castle
        int maxCol = 0;
        int minCol = int.MaxValue;
        foreach (int[] row in CastlePattern)
        {
            foreach (int col in row)
            {
                if (col > maxCol) maxCol = col;
                if (col < minCol) minCol = col;
            }
        }

        float castleWidth = (maxCol - minCol + 1) * step;
        float viewportWidth = GetViewportRect().Size.X;
        float viewportHeight = GetViewportRect().Size.Y;
        float castleHeight = CastlePattern.Length * step;
        float castleX = (viewportWidth - castleWidth) / 2f - minCol * step;
        float castleBaseY = viewportHeight * 0.5f + castleHeight * 0.35f; // centered slightly above middle

        // Count total blocks for timing
        int totalBlocks = 0;
        foreach (int[] row in CastlePattern)
            totalBlocks += row.Length;

        float timePerBlock = CastleBuildDuration / totalBlocks;
        int blockIndex = 0;

        for (int row = 0; row < CastlePattern.Length; row++)
        {
            int[] cols = CastlePattern[row];
            // Sort columns left to right for a nice sweep effect
            int[] sortedCols = (int[])cols.Clone();
            Array.Sort(sortedCols);

            foreach (int col in sortedCols)
            {
                ColorRect rect = new ColorRect();
                rect.CustomMinimumSize = new Vector2(blockSize, blockSize);
                rect.Size = new Vector2(blockSize, blockSize);

                float px = castleX + col * step;
                float py = castleBaseY - row * step;
                rect.Position = new Vector2(px, py);

                // Color: vary between stone shades, with some accent on upper rows
                Color blockColor;
                if (row <= 2)
                {
                    // Foundation: darker
                    blockColor = StoneDark.Lerp(MortarColor, _rng.RandfRange(0f, 0.4f));
                }
                else if (row >= 15)
                {
                    // Battlements/towers: lighter accent
                    blockColor = BattlementAccent.Lerp(StoneLight, _rng.RandfRange(0f, 0.5f));
                }
                else
                {
                    // Main walls: normal stone
                    float v = _rng.RandfRange(0f, 1f);
                    blockColor = StoneBase.Lerp(StoneMid, v * 0.6f);
                    // slight random brightness variation
                    float bright = _rng.RandfRange(0.9f, 1.1f);
                    blockColor = new Color(
                        Mathf.Clamp(blockColor.R * bright, 0f, 1f),
                        Mathf.Clamp(blockColor.G * bright, 0f, 1f),
                        Mathf.Clamp(blockColor.B * bright, 0f, 1f),
                        1f
                    );
                }

                rect.Color = blockColor;
                rect.MouseFilter = MouseFilterEnum.Ignore;
                rect.Modulate = new Color(1, 1, 1, 0); // start invisible

                _castleLayer.AddChild(rect);

                _castleBlocks.Add(new CastleBlock
                {
                    Rect = rect,
                    AppearTime = blockIndex * timePerBlock,
                    Visible = false,
                    BaseX = px,
                    BaseY = py,
                    VelX = 0,
                    VelY = 0,
                    RotSpeed = 0,
                    Rotation = 0,
                    Exploding = false,
                });
                blockIndex++;
            }
        }
    }

    private void ProcessCastleBuild(float dt)
    {
        // Smoothly animate the visual progress toward the actual loading progress.
        // This prevents the castle from appearing instantly when loading steps happen
        // in rapid succession (each CallDeferred runs on the next frame).
        if (IsLoadingMode && _loadingProgressVisual < _loadingProgress)
        {
            // Ramp at ~0.28/sec so the castle takes ~3.5 seconds to fully build,
            // giving the player time to enjoy the animation before the explosion.
            _loadingProgressVisual = Mathf.Min(_loadingProgress,
                _loadingProgressVisual + dt * 0.28f);
        }

        // Animate blocks appearing
        bool allVisible = true;
        for (int i = 0; i < _castleBlocks.Count; i++)
        {
            CastleBlock cb = _castleBlocks[i];

            if (IsLoadingMode)
            {
                // In loading mode, reveal blocks proportional to smoothed visual progress (0..0.95).
                // Reserve the last 5% so the castle is fully built just before progress hits 1.0.
                float revealThreshold = (float)i / _castleBlocks.Count * 0.95f;
                if (!cb.Visible && _loadingProgressVisual >= revealThreshold)
                {
                    cb.Visible = true;
                    cb.Rect.Modulate = new Color(1, 1, 1, 0.01f);
                    _castleBlocks[i] = cb;
                }
            }
            else
            {
                if (!cb.Visible && _elapsed >= cb.AppearTime)
                {
                    cb.Visible = true;
                    cb.Rect.Modulate = new Color(1, 1, 1, 0.01f);
                    _castleBlocks[i] = cb;
                }
            }

            if (cb.Visible)
            {
                // Fade in quickly
                float alpha = cb.Rect.Modulate.A;
                if (alpha < 1f)
                {
                    alpha = Mathf.Min(1f, alpha + dt * 6f);
                    cb.Rect.Modulate = new Color(1, 1, 1, alpha);

                    // Slight pop-in scale effect: start slightly above and settle
                    float progress = alpha;
                    float offsetY = (1f - progress) * -4f;
                    cb.Rect.Position = new Vector2(cb.BaseX, cb.BaseY + offsetY);
                }
            }
            else
            {
                allVisible = false;
            }
        }

        // Show the studio label after a short delay (not in loading mode)
        if (!IsLoadingMode && _studioLabel != null && _elapsed > 0.8f)
        {
            float labelAlpha = _studioLabel.Modulate.A;
            if (labelAlpha < 1f)
            {
                labelAlpha = Mathf.Min(1f, labelAlpha + dt * 2.0f);
                _studioLabel.Modulate = new Color(1, 1, 1, labelAlpha);
            }
            // Position it below center (below the castle)
            float viewportHeight = GetViewportRect().Size.Y;
            float viewportWidth = GetViewportRect().Size.X;
            _studioLabel.Position = new Vector2(0, viewportHeight * 0.72f);
            _studioLabel.Size = new Vector2(viewportWidth, 60);
        }

        if (allVisible && !_castleFullyBuilt)
        {
            _castleFullyBuilt = true;
            _holdTimer = 0f;
        }

        if (_castleFullyBuilt)
        {
            if (IsLoadingMode)
            {
                // In loading mode, wait for loading to reach 1.0 AND for the visual
                // progress animation to catch up, then hold briefly before exploding.
                if (_loadingProgress >= 1f && _loadingProgressVisual >= 0.95f && !_loadingExplosionTriggered)
                {
                    _holdTimer += dt;
                    if (_holdTimer >= HoldDuration)
                    {
                        _loadingExplosionTriggered = true;
                        StartExplosion();
                    }
                }
            }
            else
            {
                _holdTimer += dt;
                if (_holdTimer >= HoldDuration)
                {
                    StartExplosion();
                }
            }
        }
    }

    // =====================================================================
    // EXPLOSION
    // =====================================================================
    private void StartExplosion()
    {
        _explosionStarted = true;
        _explosionTimer = 0f;

        // Calculate castle center for explosion origin
        float centerX = 0f;
        float centerY = 0f;
        int count = 0;
        foreach (CastleBlock cb in _castleBlocks)
        {
            centerX += cb.BaseX;
            centerY += cb.BaseY;
            count++;
        }
        if (count > 0)
        {
            centerX /= count;
            centerY /= count;
        }

        // Give each block explosion velocity
        for (int i = 0; i < _castleBlocks.Count; i++)
        {
            CastleBlock cb = _castleBlocks[i];
            cb.Exploding = true;

            // Direction away from center
            float dx = cb.BaseX - centerX;
            float dy = cb.BaseY - centerY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) dist = 1f;

            // Normalize and scale with randomness
            float speed = _rng.RandfRange(250f, 600f);
            cb.VelX = (dx / dist) * speed + _rng.RandfRange(-100f, 100f);
            cb.VelY = (dy / dist) * speed - _rng.RandfRange(100f, 350f); // upward bias
            cb.RotSpeed = _rng.RandfRange(-8f, 8f);
            cb.Rotation = 0f;

            _castleBlocks[i] = cb;
        }

        // Fade out studio label
        if (_studioLabel != null)
        {
            Tween labelTween = _studioLabel.CreateTween();
            labelTween.TweenProperty(_studioLabel, "modulate:a", 0.0f, 0.4f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        }

        // Fade out loading label (loading mode)
        if (_loadingLabel != null)
        {
            Tween loadingTween = _loadingLabel.CreateTween();
            loadingTween.TweenProperty(_loadingLabel, "modulate:a", 0.0f, 0.4f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        }
    }

    private void ProcessExplosion(float dt)
    {
        _explosionTimer += dt;

        // Animate exploding castle blocks
        for (int i = 0; i < _castleBlocks.Count; i++)
        {
            CastleBlock cb = _castleBlocks[i];
            if (!cb.Exploding) continue;

            // Apply gravity
            cb.VelY += 500f * dt;

            // Update position
            float newX = cb.Rect.Position.X + cb.VelX * dt;
            float newY = cb.Rect.Position.Y + cb.VelY * dt;
            cb.Rect.Position = new Vector2(newX, newY);

            // Rotate
            cb.Rotation += cb.RotSpeed * dt;
            cb.Rect.Rotation = cb.Rotation;

            // Fade out over explosion duration
            float fadeProgress = _explosionTimer / 1.5f;
            float alpha = Mathf.Clamp(1f - fadeProgress, 0f, 1f);
            cb.Rect.Modulate = new Color(1, 1, 1, alpha);

            _castleBlocks[i] = cb;
        }

        // Fade in title during explosion (starts at 0.3s into explosion)
        if (_explosionTimer > 0.3f && !_titleVisible)
        {
            _titleVisible = true;
            _titleFadeAlpha = 0f;
        }

        if (_titleVisible && _titleLayer != null)
        {
            _titleFadeAlpha = Mathf.Min(1f, _titleFadeAlpha + dt * 1.8f);
            _titleLayer.Modulate = new Color(1, 1, 1, _titleFadeAlpha);

            // Animate title blocks with a subtle wave
            AnimateTitleWave(dt);
        }

        // After explosion + title hold, start fade out
        float totalExplosionAndHold = ExplosionDuration + TitleHoldDuration;
        if (_explosionTimer >= totalExplosionAndHold && !_fadingOut)
        {
            _fadingOut = true;
            _fadeOutTimer = 0f;
        }
    }

    // =====================================================================
    // VOXEL TITLE (reuses MainMenu approach)
    // =====================================================================
    private void BuildVoxelTitle()
    {
        if (_titleLayer == null) return;

        bool[][,] word1 = { LetterV, LetterO, LetterX, LetterE, LetterL };
        bool[][,] word2 = { LetterS, LetterI, LetterE, LetterG, LetterE };

        const int blockSize = 16;
        const int gap = 3;
        const int letterGap = 22;
        const int wordGap = 56;
        const int letterW = 5;

        int word1Width = word1.Length * (letterW * (blockSize + gap) - gap) + (word1.Length - 1) * letterGap;
        int word2Width = word2.Length * (letterW * (blockSize + gap) - gap) + (word2.Length - 1) * letterGap;
        int totalWidth = word1Width + wordGap + word2Width;

        float startX = (GetViewportRect().Size.X - totalWidth) / 2f;
        float startY = GetViewportRect().Size.Y / 2f - 7f * (blockSize + gap) / 2f; // vertically centered

        float curX = startX;

        foreach (bool[,] letter in word1)
        {
            RenderLetter(_titleLayer, letter, curX, startY, blockSize, gap, true);
            curX += letterW * (blockSize + gap) - gap + letterGap;
        }

        curX += wordGap - letterGap;

        foreach (bool[,] letter in word2)
        {
            RenderLetter(_titleLayer, letter, curX, startY, blockSize, gap, false);
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

                block.SetMeta("base_x", px);
                block.SetMeta("base_y", py);
                block.SetMeta("grid_col", c + (isFirstWord ? 0 : 30));

                parent.AddChild(block);
                _titleBlocks.Add(new TitleBlock
                {
                    Rect = block,
                    BaseX = px,
                    BaseY = py,
                    GridCol = c + (isFirstWord ? 0 : 30),
                    IsFirstWord = isFirstWord,
                });
            }
        }
    }

    private void AnimateTitleWave(float dt)
    {
        foreach (TitleBlock tb in _titleBlocks)
        {
            float waveOffset = Mathf.Sin(_elapsed * 2.0f + tb.GridCol * 0.3f) * 3f;
            tb.Rect.Position = new Vector2(tb.BaseX, tb.BaseY + waveOffset);
        }
    }

    // =====================================================================
    // FADE OUT + TRANSITION
    // =====================================================================
    private void ProcessFadeOut(float dt)
    {
        _fadeOutTimer += dt;

        // Fade everything out
        float fadeProgress = _fadeOutTimer / FadeOutDuration;
        float alpha = Mathf.Clamp(1f - fadeProgress, 0f, 1f);

        if (_titleLayer != null)
            _titleLayer.Modulate = new Color(1, 1, 1, alpha);

        // Fade remaining castle blocks
        for (int i = 0; i < _castleBlocks.Count; i++)
        {
            CastleBlock cb = _castleBlocks[i];
            cb.Rect.Modulate = new Color(1, 1, 1, Mathf.Min(cb.Rect.Modulate.A, alpha));
        }

        if (_fadeOutTimer >= FadeOutDuration && !_finished)
        {
            _finished = true;
            EmitSignal(SignalName.SplashFinished);
        }
    }
}
