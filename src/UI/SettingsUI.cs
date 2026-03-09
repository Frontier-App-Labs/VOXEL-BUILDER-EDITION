using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Utility;

namespace VoxelSiege.UI;

public sealed class GameSettingsData
{
    public string QualityPreset { get; set; } = "High";
    public bool VSync { get; set; } = true;
    public bool Bloom { get; set; } = true;
    public bool DepthOfField { get; set; }
    public bool Shadows { get; set; } = true;
    public bool AmbientOcclusion { get; set; } = true;
    public string AntiAliasing { get; set; } = "FXAA";
    public float ResolutionScale { get; set; } = 1.0f;
    public int FpsCap { get; set; } = 120;
    public int ParticleQuality { get; set; } = 2;
    public string CameraShake { get; set; } = "Full";

    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.6f;
    public float SfxVolume { get; set; } = 0.8f;
    public float AmbienceVolume { get; set; } = 0.5f;
}

public partial class SettingsUI : Control
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
    private static readonly Color TabActive = new Color("2ea043");
    private static readonly Color TabInactive = new Color("30363d");

    private const string SettingsPath = "user://settings/game_settings.json";

    public GameSettingsData CurrentSettings { get; private set; } = new GameSettingsData();

    public event Action? SettingsClosed;

    private int _activeTab;
    private readonly List<PanelContainer> _tabButtons = new List<PanelContainer>();
    private readonly List<Control> _tabContents = new List<Control>();

    // Graphics controls
    private OptionButton? _qualityPreset;
    private HSlider? _resolutionScale;
    private Label? _resScaleLabel;
    private CheckButton? _shadowsToggle;
    private OptionButton? _aaOption;
    private CheckButton? _aoToggle;
    private CheckButton? _bloomToggle;
    private OptionButton? _particleQuality;

    // Audio controls
    private HSlider? _masterSlider;
    private HSlider? _musicSlider;
    private HSlider? _sfxSlider;
    private HSlider? _ambienceSlider;
    private Label? _masterLabel;
    private Label? _musicLabel;
    private Label? _sfxLabel;
    private Label? _ambienceLabel;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        CurrentSettings = SaveSystem.LoadJson<GameSettingsData>(SettingsPath) ?? new GameSettingsData();

        // Dark overlay
        ColorRect backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(0, 0, 0, 0.7f);
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

        // Center panel
        PanelContainer mainPanel = CreateStyledPanel(PanelBg, 0);
        mainPanel.CustomMinimumSize = new Vector2(700, 520);
        mainPanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        mainPanel.MouseFilter = MouseFilterEnum.Stop;
        mainLayout.AddChild(mainPanel);

        MarginContainer panelMargin = new MarginContainer();
        panelMargin.AddThemeConstantOverride("margin_left", 24);
        panelMargin.AddThemeConstantOverride("margin_right", 24);
        panelMargin.AddThemeConstantOverride("margin_top", 20);
        panelMargin.AddThemeConstantOverride("margin_bottom", 20);
        panelMargin.MouseFilter = MouseFilterEnum.Ignore;
        mainPanel.AddChild(panelMargin);

        VBoxContainer panelContent = new VBoxContainer();
        panelContent.AddThemeConstantOverride("separation", 16);
        panelContent.MouseFilter = MouseFilterEnum.Ignore;
        panelMargin.AddChild(panelContent);

        // Header
        HBoxContainer headerRow = new HBoxContainer();
        headerRow.MouseFilter = MouseFilterEnum.Ignore;
        panelContent.AddChild(headerRow);

        Label title = new Label();
        title.Text = "SETTINGS";
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", TextPrimary);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.MouseFilter = MouseFilterEnum.Ignore;
        headerRow.AddChild(title);

        // Close button
        Button closeBtn = new Button();
        closeBtn.Text = "\u2715";
        closeBtn.Flat = true;
        closeBtn.AddThemeFontSizeOverride("font_size", 22);
        closeBtn.AddThemeColorOverride("font_color", TextSecondary);
        closeBtn.AddThemeColorOverride("font_hover_color", AccentRed);
        closeBtn.MouseFilter = MouseFilterEnum.Stop;
        closeBtn.Pressed += OnCancelPressed;
        headerRow.AddChild(closeBtn);

        // Tab bar
        HBoxContainer tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 4);
        tabBar.MouseFilter = MouseFilterEnum.Ignore;
        panelContent.AddChild(tabBar);

        tabBar.AddChild(CreateTab("GRAPHICS", 0));
        tabBar.AddChild(CreateTab("AUDIO", 1));
        tabBar.AddChild(CreateTab("CONTROLS", 2));

        // Separator
        ColorRect tabLine = new ColorRect();
        tabLine.CustomMinimumSize = new Vector2(0, 1);
        tabLine.Color = BorderColor;
        tabLine.MouseFilter = MouseFilterEnum.Ignore;
        panelContent.AddChild(tabLine);

        // Tab content area
        Control tabArea = new Control();
        tabArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        tabArea.MouseFilter = MouseFilterEnum.Ignore;
        panelContent.AddChild(tabArea);

        // Create tab contents
        _tabContents.Add(CreateGraphicsTab(tabArea));
        _tabContents.Add(CreateAudioTab(tabArea));
        _tabContents.Add(CreateControlsTab(tabArea));

        // Bottom buttons
        HBoxContainer buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 12);
        buttonRow.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        buttonRow.MouseFilter = MouseFilterEnum.Ignore;
        panelContent.AddChild(buttonRow);

        buttonRow.AddChild(CreateActionButton("CANCEL", TextSecondary, OnCancelPressed));
        buttonRow.AddChild(CreateActionButton("APPLY", AccentGreen, OnApplyPressed));

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        mainLayout.AddChild(bottomSpacer);

        // Show first tab
        SwitchTab(0);
    }

    // ========== GRAPHICS TAB ==========
    private Control CreateGraphicsTab(Control parent)
    {
        ScrollContainer scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(scroll);

        VBoxContainer container = new VBoxContainer();
        container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        container.AddThemeConstantOverride("separation", 14);
        container.MouseFilter = MouseFilterEnum.Ignore;
        scroll.AddChild(container);

        // Quality Preset
        _qualityPreset = new OptionButton();
        _qualityPreset.AddItem("Low");
        _qualityPreset.AddItem("Medium");
        _qualityPreset.AddItem("High");
        _qualityPreset.AddItem("Ultra");
        _qualityPreset.Selected = CurrentSettings.QualityPreset switch { "Low" => 0, "Medium" => 1, "Ultra" => 3, _ => 2 };
        AddSettingRow(container, "Quality Preset", _qualityPreset);

        // Resolution Scale
        HBoxContainer resRow = new HBoxContainer();
        resRow.AddThemeConstantOverride("separation", 12);
        resRow.MouseFilter = MouseFilterEnum.Ignore;

        _resolutionScale = new HSlider();
        _resolutionScale.MinValue = 0.5;
        _resolutionScale.MaxValue = 1.0;
        _resolutionScale.Step = 0.05;
        _resolutionScale.Value = CurrentSettings.ResolutionScale;
        _resolutionScale.CustomMinimumSize = new Vector2(200, 0);
        _resolutionScale.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        resRow.AddChild(_resolutionScale);

        _resScaleLabel = new Label();
        _resScaleLabel.Text = $"{CurrentSettings.ResolutionScale * 100:F0}%";
        _resScaleLabel.CustomMinimumSize = new Vector2(50, 0);
        _resScaleLabel.AddThemeFontSizeOverride("font_size", 16);
        _resScaleLabel.AddThemeColorOverride("font_color", AccentGold);
        _resScaleLabel.MouseFilter = MouseFilterEnum.Ignore;
        resRow.AddChild(_resScaleLabel);
        _resolutionScale.ValueChanged += (value) => { if (_resScaleLabel != null) _resScaleLabel.Text = $"{value * 100:F0}%"; };

        AddSettingRow(container, "Resolution Scale", resRow);

        // Shadows
        _shadowsToggle = CreateStyledToggle(CurrentSettings.Shadows);
        AddSettingRow(container, "Shadows", _shadowsToggle);

        // Anti-Aliasing
        _aaOption = new OptionButton();
        _aaOption.AddItem("None");
        _aaOption.AddItem("FXAA");
        _aaOption.AddItem("MSAA 2x");
        _aaOption.AddItem("MSAA 4x");
        _aaOption.Selected = CurrentSettings.AntiAliasing switch { "None" => 0, "MSAA 2x" => 2, "MSAA 4x" => 3, _ => 1 };
        AddSettingRow(container, "Anti-Aliasing", _aaOption);

        // AO
        _aoToggle = CreateStyledToggle(CurrentSettings.AmbientOcclusion);
        AddSettingRow(container, "Ambient Occlusion", _aoToggle);

        // Bloom
        _bloomToggle = CreateStyledToggle(CurrentSettings.Bloom);
        AddSettingRow(container, "Bloom", _bloomToggle);

        // Particle Quality
        _particleQuality = new OptionButton();
        _particleQuality.AddItem("Low");
        _particleQuality.AddItem("Medium");
        _particleQuality.AddItem("High");
        _particleQuality.Selected = Mathf.Clamp(CurrentSettings.ParticleQuality, 0, 2);
        AddSettingRow(container, "Particle Quality", _particleQuality);

        return scroll;
    }

    // ========== AUDIO TAB ==========
    private Control CreateAudioTab(Control parent)
    {
        ScrollContainer scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.Visible = false;
        scroll.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(scroll);

        VBoxContainer container = new VBoxContainer();
        container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        container.AddThemeConstantOverride("separation", 18);
        container.MouseFilter = MouseFilterEnum.Ignore;
        scroll.AddChild(container);

        (_masterSlider, _masterLabel) = CreateVolumeRow(container, "Master Volume", CurrentSettings.MasterVolume);
        (_musicSlider, _musicLabel) = CreateVolumeRow(container, "Music", CurrentSettings.MusicVolume);
        (_sfxSlider, _sfxLabel) = CreateVolumeRow(container, "Sound Effects", CurrentSettings.SfxVolume);
        (_ambienceSlider, _ambienceLabel) = CreateVolumeRow(container, "Ambience", CurrentSettings.AmbienceVolume);

        return scroll;
    }

    // ========== CONTROLS TAB ==========
    private Control CreateControlsTab(Control parent)
    {
        ScrollContainer scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.Visible = false;
        scroll.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(scroll);

        VBoxContainer container = new VBoxContainer();
        container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        container.AddThemeConstantOverride("separation", 8);
        container.MouseFilter = MouseFilterEnum.Ignore;
        scroll.AddChild(container);

        // Display current key bindings
        (string Action, string Key)[] bindings =
        {
            ("Move Forward", "W"),
            ("Move Back", "S"),
            ("Move Left", "A"),
            ("Move Right", "D"),
            ("Move Up", "E"),
            ("Move Down", "Q"),
            ("Rotate Piece", "R"),
            ("Place Block", "LMB"),
            ("Remove Block", "RMB"),
            ("Fire Weapon", "Space"),
            ("Undo", "Ctrl+Z"),
            ("Redo", "Ctrl+Y"),
        };

        foreach (var binding in bindings)
        {
            HBoxContainer row = new HBoxContainer();
            row.MouseFilter = MouseFilterEnum.Ignore;

            Label actionLabel = new Label();
            actionLabel.Text = binding.Action;
            actionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            actionLabel.AddThemeFontSizeOverride("font_size", 16);
            actionLabel.AddThemeColorOverride("font_color", TextSecondary);
            actionLabel.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(actionLabel);

            PanelContainer keyPanel = CreateStyledPanel(new Color(0.15f, 0.17f, 0.2f), 0);
            keyPanel.CustomMinimumSize = new Vector2(80, 28);
            keyPanel.MouseFilter = MouseFilterEnum.Ignore;

            Label keyLabel = new Label();
            keyLabel.Text = binding.Key;
            keyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            keyLabel.AddThemeFontSizeOverride("font_size", 16);
            keyLabel.AddThemeColorOverride("font_color", TextPrimary);
            keyLabel.MouseFilter = MouseFilterEnum.Ignore;
            keyPanel.AddChild(keyLabel);

            row.AddChild(keyPanel);
            container.AddChild(row);
        }

        return scroll;
    }

    // ========== Helpers ==========
    private void SwitchTab(int index)
    {
        _activeTab = index;

        for (int i = 0; i < _tabContents.Count; i++)
        {
            _tabContents[i].Visible = i == index;
        }

        for (int i = 0; i < _tabButtons.Count; i++)
        {
            Color bg = i == index ? new Color(TabActive.R, TabActive.G, TabActive.B, 0.15f) : new Color(0, 0, 0, 0);
            StyleBoxFlat style = CreateFlatStyle(bg, 0);
            if (i == index)
            {
                style.BorderWidthBottom = 2;
                style.BorderColor = TabActive;
            }
            _tabButtons[i].AddThemeStyleboxOverride("panel", style);
        }
    }

    private PanelContainer CreateTab(string text, int index)
    {
        PanelContainer tabPanel = new PanelContainer();
        tabPanel.CustomMinimumSize = new Vector2(120, 36);
        _tabButtons.Add(tabPanel);

        Button tabBtn = new Button();
        tabBtn.Text = text;
        tabBtn.Flat = true;
        tabBtn.AddThemeFontSizeOverride("font_size", 16);
        tabBtn.AddThemeColorOverride("font_color", index == 0 ? AccentGreen : TextSecondary);
        tabBtn.AddThemeColorOverride("font_hover_color", TextPrimary);
        tabBtn.MouseFilter = MouseFilterEnum.Stop;
        tabBtn.Pressed += () => SwitchTab(index);
        tabPanel.AddChild(tabBtn);
        tabBtn.SetAnchorsPreset(LayoutPreset.FullRect);
        tabBtn.OffsetLeft = 0;
        tabBtn.OffsetRight = 0;
        tabBtn.OffsetTop = 0;
        tabBtn.OffsetBottom = 0;

        return tabPanel;
    }

    private static void AddSettingRow(VBoxContainer container, string label, Control control)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.MouseFilter = MouseFilterEnum.Ignore;

        Label nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.CustomMinimumSize = new Vector2(200, 0);
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", TextSecondary);
        nameLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLabel);

        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);

        container.AddChild(row);
    }

    private (HSlider slider, Label label) CreateVolumeRow(VBoxContainer container, string name, float initialValue)
    {
        VBoxContainer rowBox = new VBoxContainer();
        rowBox.AddThemeConstantOverride("separation", 4);
        rowBox.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(rowBox);

        HBoxContainer labelRow = new HBoxContainer();
        labelRow.MouseFilter = MouseFilterEnum.Ignore;
        rowBox.AddChild(labelRow);

        Label nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", TextSecondary);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        labelRow.AddChild(nameLabel);

        Label valueLabel = new Label();
        valueLabel.Text = $"{(int)(initialValue * 100)}%";
        valueLabel.AddThemeFontSizeOverride("font_size", 16);
        valueLabel.AddThemeColorOverride("font_color", AccentGold);
        valueLabel.MouseFilter = MouseFilterEnum.Ignore;
        labelRow.AddChild(valueLabel);

        HSlider slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 1;
        slider.Step = 0.01;
        slider.Value = initialValue;
        slider.CustomMinimumSize = new Vector2(0, 24);
        slider.MouseFilter = MouseFilterEnum.Stop;
        rowBox.AddChild(slider);

        Label capturedLabel = valueLabel;
        slider.ValueChanged += (value) => { capturedLabel.Text = $"{(int)(value * 100)}%"; };

        return (slider, valueLabel);
    }

    private static CheckButton CreateStyledToggle(bool initialValue)
    {
        CheckButton toggle = new CheckButton();
        toggle.ButtonPressed = initialValue;
        toggle.Text = initialValue ? "ON" : "OFF";
        toggle.AddThemeFontSizeOverride("font_size", 16);
        toggle.AddThemeColorOverride("font_color", TextSecondary);
        toggle.MouseFilter = MouseFilterEnum.Stop;
        toggle.Toggled += (pressed) => { toggle.Text = pressed ? "ON" : "OFF"; };
        return toggle;
    }

    private PanelContainer CreateActionButton(string text, Color accentColor, Action handler)
    {
        PanelContainer btnPanel = new PanelContainer();
        btnPanel.CustomMinimumSize = new Vector2(120, 38);

        StyleBoxFlat style = CreateFlatStyle(new Color(accentColor.R, accentColor.G, accentColor.B, 0.12f), 0);
        style.BorderWidthTop = 1;
        style.BorderWidthBottom = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.3f);
        btnPanel.AddThemeStyleboxOverride("panel", style);

        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.AddThemeFontSizeOverride("font_size", 16);
        btn.AddThemeColorOverride("font_color", accentColor);
        btn.AddThemeColorOverride("font_hover_color", TextPrimary);
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += handler;
        btnPanel.AddChild(btn);
        btn.SetAnchorsPreset(LayoutPreset.FullRect);
        btn.OffsetLeft = 0;
        btn.OffsetRight = 0;
        btn.OffsetTop = 0;
        btn.OffsetBottom = 0;

        return btnPanel;
    }

    private void OnApplyPressed()
    {
        // Gather values from controls
        if (_qualityPreset != null)
        {
            CurrentSettings.QualityPreset = _qualityPreset.Selected switch { 0 => "Low", 1 => "Medium", 3 => "Ultra", _ => "High" };
        }
        if (_resolutionScale != null) CurrentSettings.ResolutionScale = (float)_resolutionScale.Value;
        if (_shadowsToggle != null) CurrentSettings.Shadows = _shadowsToggle.ButtonPressed;
        if (_aaOption != null)
        {
            CurrentSettings.AntiAliasing = _aaOption.Selected switch { 0 => "None", 2 => "MSAA 2x", 3 => "MSAA 4x", _ => "FXAA" };
        }
        if (_aoToggle != null) CurrentSettings.AmbientOcclusion = _aoToggle.ButtonPressed;
        if (_bloomToggle != null) CurrentSettings.Bloom = _bloomToggle.ButtonPressed;
        if (_particleQuality != null) CurrentSettings.ParticleQuality = _particleQuality.Selected;

        if (_masterSlider != null) CurrentSettings.MasterVolume = (float)_masterSlider.Value;
        if (_musicSlider != null) CurrentSettings.MusicVolume = (float)_musicSlider.Value;
        if (_sfxSlider != null) CurrentSettings.SfxVolume = (float)_sfxSlider.Value;
        if (_ambienceSlider != null) CurrentSettings.AmbienceVolume = (float)_ambienceSlider.Value;

        SaveCurrentSettings();
        Visible = false;
        SettingsClosed?.Invoke();
    }

    private void OnCancelPressed()
    {
        Visible = false;
        SettingsClosed?.Invoke();
    }

    public void SaveCurrentSettings()
    {
        SaveSystem.SaveJson(SettingsPath, CurrentSettings);
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
