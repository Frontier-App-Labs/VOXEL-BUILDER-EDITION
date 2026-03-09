using Godot;
using System;
using VoxelSiege.Camera;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

/// <summary>
/// Floating toolbar overlay visible during Build and Combat phases.
/// Provides camera angle preset buttons, weapon info, keyboard shortcut hints,
/// and a settings button. Positioned at the right edge of the screen.
/// </summary>
public partial class GameOverlayUI : Control
{
    // --- Theme Colors ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color(0.086f, 0.106f, 0.133f, 0.88f);
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color BorderColor = new Color("30363d");
    private static readonly Color BorderHighlight = new Color("484f58");
    private static readonly Color ButtonHoverBg = new Color("1f2937");
    private static readonly Color ButtonActiveBg = new Color(0.18f, 0.28f, 0.15f, 0.9f);

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Camera preset definitions ---
    private static readonly (string Label, string Shortcut)[] CameraPresets =
    {
        ("TOP", "1"),
        ("FRONT", "2"),
        ("SIDE", "3"),
        ("LOW", "4"),
        ("FREE", "5"),
    };

    // --- References ---
    private CombatCamera? _combatCamera;
    private FreeFlyCamera? _freeFlyCamera;
    private GamePhase _currentPhase = GamePhase.Menu;

    // --- UI elements ---
    private VBoxContainer? _rootContainer;
    private Label? _weaponInfoLabel;
    private PanelContainer? _weaponInfoPanel;
    private Label? _shortcutHintsLabel;
    private readonly Button[] _cameraButtons = new Button[5];
    private int _activeCameraPreset = 4; // Free look by default

    // --- Events ---
    public event Action? SettingsRequested;

    public override void _Ready()
    {
        Name = "GameOverlayUI";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        BuildUI();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.TurnChanged += OnTurnChanged;
        }

        // Start hidden
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
            EventBus.Instance.TurnChanged -= OnTurnChanged;
        }
    }

    /// <summary>Inject camera references from GameManager after creation.</summary>
    public void SetCameras(CombatCamera? combatCamera, FreeFlyCamera? freeFlyCamera)
    {
        _combatCamera = combatCamera;
        _freeFlyCamera = freeFlyCamera;
    }

    /// <summary>Update the weapon info display text.</summary>
    public void SetWeaponInfo(string weaponName, int ammo = -1)
    {
        if (_weaponInfoLabel == null) return;

        if (ammo >= 0)
        {
            _weaponInfoLabel.Text = $"{weaponName}\nAmmo: {ammo}";
        }
        else
        {
            _weaponInfoLabel.Text = weaponName;
        }
    }

    /// <summary>Clear weapon info (hide the weapon panel).</summary>
    public void ClearWeaponInfo()
    {
        if (_weaponInfoLabel != null)
        {
            _weaponInfoLabel.Text = "";
        }

        if (_weaponInfoPanel != null)
        {
            _weaponInfoPanel.Visible = false;
        }
    }

    // ------------------------------------------------------------------
    // Input: keyboard shortcuts for camera presets
    // ------------------------------------------------------------------

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible) return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            int presetIndex = keyEvent.Keycode switch
            {
                Key.Key1 => 0,
                Key.Key2 => 1,
                Key.Key3 => 2,
                Key.Key4 => 3,
                Key.Key5 => 4,
                _ => -1,
            };

            if (presetIndex >= 0)
            {
                ApplyCameraPreset(presetIndex);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // ------------------------------------------------------------------
    // Build the UI hierarchy
    // ------------------------------------------------------------------

    private void BuildUI()
    {
        // Outer panel anchored to right edge, vertically centered
        PanelContainer outerPanel = new PanelContainer();
        outerPanel.Name = "OverlayPanel";
        outerPanel.SetAnchorsPreset(LayoutPreset.CenterRight);
        outerPanel.GrowHorizontal = GrowDirection.Begin;
        outerPanel.GrowVertical = GrowDirection.Both;
        outerPanel.OffsetLeft = -140;
        outerPanel.OffsetRight = -8;
        outerPanel.OffsetTop = -180;
        outerPanel.OffsetBottom = 180;
        outerPanel.MouseFilter = MouseFilterEnum.Stop;

        // Style the outer panel
        StyleBoxFlat panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = BgPanel;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderWidthLeft = 4;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderColor = BorderColor;
        panelStyle.CornerRadiusTopLeft = 0;
        panelStyle.CornerRadiusTopRight = 0;
        panelStyle.CornerRadiusBottomLeft = 0;
        panelStyle.CornerRadiusBottomRight = 0;
        panelStyle.ContentMarginLeft = 8;
        panelStyle.ContentMarginRight = 8;
        panelStyle.ContentMarginTop = 10;
        panelStyle.ContentMarginBottom = 10;
        outerPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(outerPanel);

        // Root VBox
        _rootContainer = new VBoxContainer();
        _rootContainer.Name = "RootVBox";
        _rootContainer.AddThemeConstantOverride("separation", 6);
        outerPanel.AddChild(_rootContainer);

        // --- Section: Camera label ---
        Label cameraLabel = CreateSectionLabel("CAMERA");
        _rootContainer.AddChild(cameraLabel);

        // --- Camera preset buttons ---
        for (int i = 0; i < CameraPresets.Length; i++)
        {
            int presetIdx = i; // capture for lambda
            Button btn = CreatePresetButton(CameraPresets[i].Label, CameraPresets[i].Shortcut);
            btn.Pressed += () => ApplyCameraPreset(presetIdx);
            _cameraButtons[i] = btn;
            _rootContainer.AddChild(btn);
        }

        // Highlight "FREE" by default
        HighlightCameraButton(4);

        // --- Separator ---
        HSeparator sep1 = new HSeparator();
        sep1.AddThemeConstantOverride("separation", 6);
        StyleBoxFlat sepStyle = new StyleBoxFlat();
        sepStyle.BgColor = BorderColor;
        sepStyle.ContentMarginTop = 2;
        sepStyle.ContentMarginBottom = 2;
        sep1.AddThemeStyleboxOverride("separator", sepStyle);
        _rootContainer.AddChild(sep1);

        // --- Weapon info panel (combat only) ---
        _weaponInfoPanel = new PanelContainer();
        _weaponInfoPanel.Name = "WeaponInfoPanel";
        StyleBoxFlat weaponPanelStyle = new StyleBoxFlat();
        weaponPanelStyle.BgColor = new Color(0.06f, 0.08f, 0.1f, 0.8f);
        weaponPanelStyle.BorderWidthTop = 1;
        weaponPanelStyle.BorderWidthBottom = 1;
        weaponPanelStyle.BorderWidthLeft = 1;
        weaponPanelStyle.BorderWidthRight = 1;
        weaponPanelStyle.BorderColor = AccentGold;
        weaponPanelStyle.ContentMarginLeft = 6;
        weaponPanelStyle.ContentMarginRight = 6;
        weaponPanelStyle.ContentMarginTop = 4;
        weaponPanelStyle.ContentMarginBottom = 4;
        _weaponInfoPanel.AddThemeStyleboxOverride("panel", weaponPanelStyle);
        _weaponInfoPanel.Visible = false;
        _rootContainer.AddChild(_weaponInfoPanel);

        _weaponInfoLabel = new Label();
        _weaponInfoLabel.Name = "WeaponInfoLabel";
        _weaponInfoLabel.AddThemeFontOverride("font", PixelFont);
        _weaponInfoLabel.AddThemeFontSizeOverride("font_size", 10);
        _weaponInfoLabel.AddThemeColorOverride("font_color", AccentGold);
        _weaponInfoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _weaponInfoPanel.AddChild(_weaponInfoLabel);

        // --- Separator ---
        HSeparator sep2 = new HSeparator();
        sep2.AddThemeConstantOverride("separation", 6);
        StyleBoxFlat sep2Style = new StyleBoxFlat();
        sep2Style.BgColor = BorderColor;
        sep2Style.ContentMarginTop = 2;
        sep2Style.ContentMarginBottom = 2;
        sep2.AddThemeStyleboxOverride("separator", sep2Style);
        _rootContainer.AddChild(sep2);

        // --- Shortcut hints ---
        _shortcutHintsLabel = new Label();
        _shortcutHintsLabel.Name = "ShortcutHints";
        _shortcutHintsLabel.AddThemeFontOverride("font", PixelFont);
        _shortcutHintsLabel.AddThemeFontSizeOverride("font_size", 8);
        _shortcutHintsLabel.AddThemeColorOverride("font_color", TextSecondary);
        _shortcutHintsLabel.Text = "1-5: Camera\nClick: Fire\nEsc: Cancel";
        _shortcutHintsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _rootContainer.AddChild(_shortcutHintsLabel);

        // --- Settings button ---
        Button settingsBtn = new Button();
        settingsBtn.Name = "SettingsButton";
        settingsBtn.Text = "SETTINGS";
        settingsBtn.CustomMinimumSize = new Vector2(0, 28);
        settingsBtn.AddThemeFontOverride("font", PixelFont);
        settingsBtn.AddThemeFontSizeOverride("font_size", 10);
        settingsBtn.AddThemeColorOverride("font_color", TextPrimary);
        settingsBtn.AddThemeColorOverride("font_hover_color", AccentGreen);

        StyleBoxFlat settingsNormal = new StyleBoxFlat();
        settingsNormal.BgColor = BgDark;
        settingsNormal.BorderWidthTop = 1;
        settingsNormal.BorderWidthBottom = 1;
        settingsNormal.BorderWidthLeft = 1;
        settingsNormal.BorderWidthRight = 1;
        settingsNormal.BorderColor = BorderColor;
        settingsNormal.ContentMarginLeft = 6;
        settingsNormal.ContentMarginRight = 6;
        settingsNormal.ContentMarginTop = 4;
        settingsNormal.ContentMarginBottom = 4;
        settingsBtn.AddThemeStyleboxOverride("normal", settingsNormal);

        StyleBoxFlat settingsHover = (StyleBoxFlat)settingsNormal.Duplicate();
        settingsHover.BgColor = ButtonHoverBg;
        settingsBtn.AddThemeStyleboxOverride("hover", settingsHover);

        StyleBoxFlat settingsPressed = (StyleBoxFlat)settingsNormal.Duplicate();
        settingsPressed.BgColor = new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.2f);
        settingsBtn.AddThemeStyleboxOverride("pressed", settingsPressed);

        settingsBtn.Pressed += OnSettingsPressed;
        _rootContainer.AddChild(settingsBtn);
    }

    // ------------------------------------------------------------------
    // UI factory helpers
    // ------------------------------------------------------------------

    private Label CreateSectionLabel(string text)
    {
        Label label = new Label();
        label.Text = text;
        label.AddThemeFontOverride("font", PixelFont);
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", TextSecondary);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    private Button CreatePresetButton(string label, string shortcut)
    {
        Button btn = new Button();
        btn.Text = $"[{shortcut}] {label}";
        btn.CustomMinimumSize = new Vector2(0, 24);
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 10);
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", AccentGreen);

        StyleBoxFlat normal = new StyleBoxFlat();
        normal.BgColor = BgDark;
        normal.BorderWidthTop = 1;
        normal.BorderWidthBottom = 1;
        normal.BorderWidthLeft = 1;
        normal.BorderWidthRight = 1;
        normal.BorderColor = BorderColor;
        normal.ContentMarginLeft = 4;
        normal.ContentMarginRight = 4;
        normal.ContentMarginTop = 2;
        normal.ContentMarginBottom = 2;
        btn.AddThemeStyleboxOverride("normal", normal);

        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = ButtonHoverBg;
        btn.AddThemeStyleboxOverride("hover", hover);

        StyleBoxFlat pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.2f);
        btn.AddThemeStyleboxOverride("pressed", pressed);

        return btn;
    }

    private void HighlightCameraButton(int index)
    {
        for (int i = 0; i < _cameraButtons.Length; i++)
        {
            Button? btn = _cameraButtons[i];
            if (btn == null) continue;

            if (i == index)
            {
                // Active button: green-tinted background, bright border
                StyleBoxFlat active = new StyleBoxFlat();
                active.BgColor = ButtonActiveBg;
                active.BorderWidthTop = 1;
                active.BorderWidthBottom = 1;
                active.BorderWidthLeft = 2;
                active.BorderWidthRight = 1;
                active.BorderColor = AccentGreen;
                active.ContentMarginLeft = 4;
                active.ContentMarginRight = 4;
                active.ContentMarginTop = 2;
                active.ContentMarginBottom = 2;
                btn.AddThemeStyleboxOverride("normal", active);
                btn.AddThemeColorOverride("font_color", AccentGreen);
            }
            else
            {
                // Inactive button: dark background
                StyleBoxFlat normal = new StyleBoxFlat();
                normal.BgColor = BgDark;
                normal.BorderWidthTop = 1;
                normal.BorderWidthBottom = 1;
                normal.BorderWidthLeft = 1;
                normal.BorderWidthRight = 1;
                normal.BorderColor = BorderColor;
                normal.ContentMarginLeft = 4;
                normal.ContentMarginRight = 4;
                normal.ContentMarginTop = 2;
                normal.ContentMarginBottom = 2;
                btn.AddThemeStyleboxOverride("normal", normal);
                btn.AddThemeColorOverride("font_color", TextPrimary);
            }
        }

        _activeCameraPreset = index;
    }

    // ------------------------------------------------------------------
    // Camera preset application
    // ------------------------------------------------------------------

    private void ApplyCameraPreset(int index)
    {
        HighlightCameraButton(index);

        if (_freeFlyCamera == null)
        {
            return;
        }

        // During combat, ensure FreeFlyCamera is the active camera when applying presets.
        // Deactivate CombatCamera if it was active (e.g. during targeting).
        if (_currentPhase == GamePhase.Combat && _combatCamera != null)
        {
            _combatCamera.Deactivate();
            _freeFlyCamera.ResetToFullArenaBounds();
            _freeFlyCamera.Activate();
        }

        // Apply presets to FreeFlyCamera using TransitionToLookTarget
        Vector3 arenaCenter = new Vector3(0f, 6f, 0f);
        switch (index)
        {
            case 0: // Top Down
                _freeFlyCamera.TransitionToLookTarget(
                    new Vector3(arenaCenter.X, 40f, arenaCenter.Z + 0.1f),
                    arenaCenter);
                break;
            case 1: // Front
                _freeFlyCamera.TransitionToLookTarget(
                    arenaCenter + new Vector3(0f, 4f, 28f),
                    arenaCenter);
                break;
            case 2: // Side
                _freeFlyCamera.TransitionToLookTarget(
                    arenaCenter + new Vector3(28f, 4f, 0f),
                    arenaCenter);
                break;
            case 3: // Low Angle
                _freeFlyCamera.TransitionToLookTarget(
                    new Vector3(arenaCenter.X + 5f, 1.5f, arenaCenter.Z + 15f),
                    arenaCenter);
                break;
            case 4: // Free Look (no transition, just release)
                break;
        }
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnPhaseChanged(PhaseChangedEvent e)
    {
        _currentPhase = e.CurrentPhase;

        bool shouldShow = e.CurrentPhase == GamePhase.Building || e.CurrentPhase == GamePhase.Combat;
        Visible = shouldShow;

        if (_weaponInfoPanel != null)
        {
            _weaponInfoPanel.Visible = e.CurrentPhase == GamePhase.Combat;
        }

        // Update shortcut hints based on phase
        if (_shortcutHintsLabel != null)
        {
            if (e.CurrentPhase == GamePhase.Combat)
            {
                _shortcutHintsLabel.Text = "1-5: Camera\nClick: Fire\nEsc: Cancel";
            }
            else if (e.CurrentPhase == GamePhase.Building)
            {
                _shortcutHintsLabel.Text = "1-5: Camera\nR-Click: Erase\nMid: Look";
            }
        }
    }

    private void OnTurnChanged(TurnChangedEvent e)
    {
        // Reset weapon display on new turn
        ClearWeaponInfo();
        if (_weaponInfoPanel != null && _currentPhase == GamePhase.Combat)
        {
            _weaponInfoPanel.Visible = true;
        }
    }

    private void OnSettingsPressed()
    {
        SettingsRequested?.Invoke();
    }
}
