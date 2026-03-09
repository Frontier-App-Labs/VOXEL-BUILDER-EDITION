using Godot;
using System;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

/// <summary>
/// ESC-key pause menu. Pauses the game tree while open and provides
/// Resume, Settings, Quit to Menu, and Quit to Desktop options.
/// ProcessMode = Always so input is handled even while paused.
/// </summary>
public partial class PauseMenu : Control
{
    // --- Theme Colors (matching MainMenu / GameOverUI) ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color(0.086f, 0.106f, 0.133f, 0.95f);
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color AccentRed = new Color("d73a49");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color BorderColor = new Color("30363d");
    private static readonly Color ButtonNormal = new Color("0d1117");
    private static readonly Color ButtonHover = new Color("1f2937");
    private static readonly Color OverlayColor = new Color(0, 0, 0, 0.6f);

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    private SettingsUI? _settingsUI;
    private Control? _menuPanel; // The button panel, hidden when settings is open
    private Control? _contentContainer; // Outer layout, brute-force centered in _Process

    public override void _Ready()
    {
        // Must process even while the tree is paused
        ProcessMode = ProcessModeEnum.Always;

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // block clicks through to game
        Visible = false;

        // Semi-transparent dark overlay
        ColorRect backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = OverlayColor;
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(backdrop);

        // Center layout - brute-force positioned in _Process (matching MainMenu approach)
        VBoxContainer outerLayout = new VBoxContainer();
        outerLayout.AddThemeConstantOverride("separation", 0);
        outerLayout.MouseFilter = MouseFilterEnum.Ignore;
        _contentContainer = outerLayout;
        AddChild(outerLayout);

        // Top spacer
        Control topSpacer = new Control();
        topSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        topSpacer.MouseFilter = MouseFilterEnum.Ignore;
        outerLayout.AddChild(topSpacer);

        // Center panel
        _menuPanel = new VBoxContainer();
        _menuPanel.AddThemeConstantOverride("separation", 0);
        _menuPanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _menuPanel.MouseFilter = MouseFilterEnum.Ignore;
        outerLayout.AddChild(_menuPanel);

        // Panel background
        PanelContainer panelBg = CreateStyledPanel(BgPanel, 0);
        panelBg.CustomMinimumSize = new Vector2(400, 0);
        panelBg.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        panelBg.MouseFilter = MouseFilterEnum.Stop;
        _menuPanel.AddChild(panelBg);

        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        panelBg.AddChild(margin);

        VBoxContainer buttonColumn = new VBoxContainer();
        buttonColumn.AddThemeConstantOverride("separation", 8);
        buttonColumn.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(buttonColumn);

        // Title
        Label title = new Label();
        title.Text = "PAUSED";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontOverride("font", PixelFont);
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", AccentGold);
        title.MouseFilter = MouseFilterEnum.Ignore;
        buttonColumn.AddChild(title);

        // Accent bar (segmented, matching MainMenu style)
        HBoxContainer accentBarWrapper = new HBoxContainer();
        accentBarWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        accentBarWrapper.Alignment = BoxContainer.AlignmentMode.Center;
        accentBarWrapper.MouseFilter = MouseFilterEnum.Ignore;
        buttonColumn.AddChild(accentBarWrapper);

        HBoxContainer accentBar = new HBoxContainer();
        accentBar.AddThemeConstantOverride("separation", 3);
        accentBar.MouseFilter = MouseFilterEnum.Ignore;
        accentBarWrapper.AddChild(accentBar);

        for (int i = 0; i < 24; i++)
        {
            ColorRect segment = new ColorRect();
            segment.CustomMinimumSize = new Vector2(6, 3);
            float t = (float)i / 24f;
            segment.Color = AccentGold.Lerp(AccentGreen, t * 0.4f);
            segment.MouseFilter = MouseFilterEnum.Ignore;
            accentBar.AddChild(segment);
        }

        // Spacer before buttons
        Control spacer1 = new Control();
        spacer1.CustomMinimumSize = new Vector2(0, 16);
        spacer1.MouseFilter = MouseFilterEnum.Ignore;
        buttonColumn.AddChild(spacer1);

        // Buttons
        AddMenuButton(buttonColumn, "RESUME", AccentGreen, OnResumePressed);
        AddMenuButton(buttonColumn, "SETTINGS", TextSecondary, OnSettingsPressed);
        AddMenuButton(buttonColumn, "QUIT TO MENU", AccentGold, OnQuitToMenuPressed);
        AddMenuButton(buttonColumn, "QUIT TO DESKTOP", AccentRed, OnQuitToDesktopPressed);

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        outerLayout.AddChild(bottomSpacer);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Brute-force centering: set position/size every frame to guarantee center alignment
        // (matching MainMenu approach -- Godot's anchor system doesn't reliably center)
        if (_contentContainer != null)
        {
            Vector2 viewSize = GetViewportRect().Size;
            float contentW = viewSize.X * 0.5f;
            _contentContainer.Position = new Vector2((viewSize.X - contentW) * 0.5f, 0f);
            _contentContainer.Size = new Vector2(contentW, viewSize.Y);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            // If settings sub-panel is open, close it instead of toggling the pause menu
            if (_settingsUI != null && _settingsUI.Visible)
            {
                CloseSettings();
                GetViewport().SetInputAsHandled();
                return;
            }

            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Opens or closes the pause menu (and pauses/unpauses the game tree).
    /// Only toggles during gameplay phases (Building, FogReveal, Combat, GameOver).
    /// </summary>
    public void Toggle()
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        // Only allow opening during active gameplay
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm != null)
        {
            GamePhase phase = gm.CurrentPhase;
            if (phase == GamePhase.Menu || phase == GamePhase.Lobby)
                return;
        }

        Visible = true;
        GetTree().Paused = true;

        // Show the button panel (in case settings was previously open)
        if (_menuPanel != null) _menuPanel.Visible = true;
    }

    public void Close()
    {
        // Close settings sub-panel if open
        if (_settingsUI != null && _settingsUI.Visible)
        {
            CloseSettings();
        }

        Visible = false;
        GetTree().Paused = false;
    }

    // =====================================================================
    //  BUTTON HANDLERS
    // =====================================================================

    private void OnResumePressed()
    {
        Close();
    }

    private void OnSettingsPressed()
    {
        // Create SettingsUI on demand (lazy)
        if (_settingsUI == null)
        {
            _settingsUI = new SettingsUI();
            _settingsUI.Name = "PauseSettingsUI";
            _settingsUI.ProcessMode = ProcessModeEnum.Always;
            _settingsUI.SettingsClosed += OnSettingsClosed;
            AddChild(_settingsUI);
        }

        // Hide the button panel, show settings
        if (_menuPanel != null) _menuPanel.Visible = false;
        _settingsUI.Visible = true;
    }

    private void OnSettingsClosed()
    {
        CloseSettings();
    }

    private void CloseSettings()
    {
        if (_settingsUI != null)
        {
            _settingsUI.Visible = false;
        }
        // Restore button panel
        if (_menuPanel != null) _menuPanel.Visible = true;
    }

    private void OnQuitToMenuPressed()
    {
        // Unpause first so the game tree processes normally
        GetTree().Paused = false;
        Visible = false;

        // Fully return to the main menu: cleans up all match state, regenerates
        // the menu background terrain with the 4-CPU demo battle, and shows the menu UI.
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gm?.ReturnToMainMenu();
    }

    private void OnQuitToDesktopPressed()
    {
        GetTree().Paused = false;
        GetTree().Quit();
    }

    // =====================================================================
    //  BUTTON FACTORY (matching MainMenu voxel-style beveled borders)
    // =====================================================================

    private void AddMenuButton(VBoxContainer container, string text, Color accentColor, Action handler)
    {
        PanelContainer btnPanel = new PanelContainer();
        btnPanel.CustomMinimumSize = new Vector2(320, 44);

        // Voxel-style: square corners (0 radius), beveled border
        StyleBoxFlat normalStyle = CreateFlatStyle(ButtonNormal, 0);
        normalStyle.BorderWidthLeft = 4;
        normalStyle.BorderWidthTop = 2;
        normalStyle.BorderWidthRight = 2;
        normalStyle.BorderWidthBottom = 4;
        normalStyle.BorderColor = accentColor;
        normalStyle.ContentMarginLeft = 16;
        normalStyle.ContentMarginRight = 16;
        normalStyle.ContentMarginTop = 8;
        normalStyle.ContentMarginBottom = 8;
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

        // Hover styling
        Color capturedAccent = accentColor;
        PanelContainer capturedPanel = btnPanel;
        btn.MouseEntered += () => OnButtonHover(capturedPanel, capturedAccent, true);
        btn.MouseExited += () => OnButtonHover(capturedPanel, capturedAccent, false);

        btnPanel.AddChild(btn);

        // Wrap in HBoxContainer centered horizontally
        HBoxContainer wrapper = new HBoxContainer();
        wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wrapper.Alignment = BoxContainer.AlignmentMode.Center;
        wrapper.MouseFilter = MouseFilterEnum.Ignore;
        wrapper.AddChild(btnPanel);
        container.AddChild(wrapper);
    }

    private void OnButtonHover(PanelContainer panel, Color accent, bool entered)
    {
        StyleBoxFlat style = CreateFlatStyle(entered ? ButtonHover : ButtonNormal, 0);
        style.BorderWidthLeft = entered ? 6 : 4;
        style.BorderWidthTop = entered ? 3 : 2;
        style.BorderWidthRight = entered ? 3 : 2;
        style.BorderWidthBottom = entered ? 6 : 4;
        style.BorderColor = accent;
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", style);

        Tween tween = panel.CreateTween();
        tween.TweenProperty(panel, "scale", entered ? new Vector2(1.03f, 1.03f) : Vector2.One, 0.12f)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    // =====================================================================
    //  STYLE HELPERS
    // =====================================================================

    private static PanelContainer CreateStyledPanel(Color bgColor, int cornerRadius)
    {
        PanelContainer panel = new PanelContainer();
        StyleBoxFlat style = CreateFlatStyle(bgColor, cornerRadius);
        // Add beveled border to the main panel
        style.BorderWidthLeft = 4;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 4;
        style.BorderColor = new Color("30363d");
        panel.AddThemeStyleboxOverride("panel", style);
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
