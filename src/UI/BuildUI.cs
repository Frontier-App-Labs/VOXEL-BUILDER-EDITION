using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Army;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.UI;

public partial class BuildUI : Control
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

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Tool definitions ---
    private static readonly (string Name, string Icon, BuildToolMode Mode)[] Tools =
    {
        ("Single", "\u25a0", BuildToolMode.Single),
        ("Half", "\u25aa", BuildToolMode.HalfBlock),
        ("Line", "\u2500", BuildToolMode.Line),
        ("Wall", "\u2588", BuildToolMode.Wall),
        ("Box", "\u25a1", BuildToolMode.Box),
        ("Floor", "\u2582", BuildToolMode.Floor),
        ("Ramp", "\u25e2", BuildToolMode.Ramp),
        ("Door", "\u2586", BuildToolMode.Door),
        ("Eraser", "\u2716", BuildToolMode.Eraser),
    };

    // --- Material palette (non-Air, non-Foundation) ---
    private static readonly VoxelMaterialType[] BuildMaterials =
    {
        VoxelMaterialType.Dirt,
        VoxelMaterialType.Wood,
        VoxelMaterialType.Stone,
        VoxelMaterialType.Brick,
        VoxelMaterialType.Concrete,
        VoxelMaterialType.Metal,
        VoxelMaterialType.ReinforcedSteel,
        VoxelMaterialType.Glass,
        VoxelMaterialType.Obsidian,
        VoxelMaterialType.Sand,
        VoxelMaterialType.Ice,
        VoxelMaterialType.ArmorPlate,
        VoxelMaterialType.Leaves,
        VoxelMaterialType.Bark,
    };

    // --- Weapon type definitions for the build UI ---
    private static readonly (string Name, WeaponType Type, int Cost)[] WeaponOptions =
    {
        ("Cannon", WeaponType.Cannon, 500),
        ("Mortar", WeaponType.Mortar, 600),
        ("Drill", WeaponType.Drill, 400),
        ("Railgun", WeaponType.Railgun, 800),
        ("Missile", WeaponType.MissileLauncher, 1000),
    };

    private Label? _budgetLabel;
    private Label? _timerLabel;
    private int _currentBudget;
    private float _countdown;
    private string _phaseText = "BUILD PHASE";
    private int _selectedToolIndex;
    private int _selectedMaterialIndex;
    private int _selectedWeaponTypeIndex;
    private readonly List<PanelContainer> _toolButtons = new List<PanelContainer>();
    private readonly List<PanelContainer> _materialButtons = new List<PanelContainer>();
    private readonly List<PanelContainer> _weaponTypeButtons = new List<PanelContainer>();
    private PanelContainer? _timerPanel;
    private bool _timerUrgent;
    private readonly List<PanelContainer> _powerupButtons = new List<PanelContainer>();
    private readonly List<Label> _powerupCountLabels = new List<Label>();
    private readonly List<PanelContainer> _troopButtons = new List<PanelContainer>();
    private readonly List<Label> _troopCountLabels = new List<Label>();
    private readonly List<PanelContainer> _blueprintButtons = new List<PanelContainer>();
    private readonly List<Button> _symmetryButtons = new List<Button>();
    private int _selectedBlueprintIndex = -1;
    private TooltipSystem? _tooltipSystem;

    public event Action<BuildToolMode>? ToolSelected;
    public event Action<VoxelMaterialType>? MaterialSelected;
    public event Action<BuildSymmetryMode>? SymmetryChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? PlaceCommanderRequested;
    public event Action? PlaceWeaponRequested;
    public event Action<WeaponType>? WeaponTypeSelected;
    public event Action<WeaponType>? WeaponSellRequested;
    public event Action<PowerupType>? PowerupBuyRequested;
    public event Action<PowerupType>? PowerupSellRequested;
    public event Action<string>? SandboxSaveRequested;
    public event Action<string>? SandboxLoadRequested;
    public event Action<TroopType>? TroopBuyRequested;
    public event Action<TroopType>? TroopSellRequested;
    public event Action<BlueprintDefinition>? BlueprintSelected;
    public event Action? ReadyPressed;

    private Button? _readyBtn;
    private bool _readyTimerUrgent;

    // Sandbox mode
    private bool _sandboxMode;
    private LineEdit? _sandboxNameInput;
    private VBoxContainer? _sandboxBuildList;
    private List<string> _sandboxBuildNames = new List<string>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopBar();
        BuildLeftPanel();
        BuildRightPanel();
        BuildBottomBar();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.BudgetChanged += OnBudgetChanged;
        }

        // Sync initial material selection so BuildSystem.CurrentMaterial matches the UI
        MaterialSelected?.Invoke(BuildMaterials[_selectedMaterialIndex]);

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
            EventBus.Instance.BudgetChanged -= OnBudgetChanged;
        }
    }

    public override void _Process(double delta)
    {
        // Update timer from GameManager
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm != null)
        {
            _countdown = gm.PhaseCountdownSeconds;
        }

        UpdateTimerDisplay();
        UpdateBudgetDisplay();
        UpdateReadyButtonTimer();
    }

    // ========== TOP BAR ==========
    private void BuildTopBar()
    {
        HBoxContainer topBar = new HBoxContainer();
        topBar.SetAnchorsPreset(LayoutPreset.TopWide);
        topBar.OffsetBottom = 60;
        topBar.CustomMinimumSize = new Vector2(0, 60);
        topBar.AddThemeConstantOverride("separation", 12);
        topBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(topBar);

        MarginContainer topMargin = new MarginContainer();
        topMargin.SetAnchorsPreset(LayoutPreset.TopWide);
        topMargin.AddThemeConstantOverride("margin_left", 16);
        topMargin.AddThemeConstantOverride("margin_right", 16);
        topMargin.AddThemeConstantOverride("margin_top", 12);
        topMargin.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(topMargin);

        HBoxContainer topContent = new HBoxContainer();
        topContent.AddThemeConstantOverride("separation", 16);
        topContent.MouseFilter = MouseFilterEnum.Ignore;
        topMargin.AddChild(topContent);

        // Phase label
        PanelContainer phasePanel = CreateStyledPanel(PanelBg, 0);
        phasePanel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(phasePanel);

        Label phaseLabel = new Label();
        phaseLabel.Text = "  BUILD PHASE  ";
        phaseLabel.AddThemeFontOverride("font", PixelFont);
        phaseLabel.AddThemeFontSizeOverride("font_size", 12);
        phaseLabel.AddThemeColorOverride("font_color", AccentGreen);
        phaseLabel.MouseFilter = MouseFilterEnum.Ignore;
        phasePanel.AddChild(phaseLabel);

        // Budget display
        PanelContainer budgetPanel = CreateStyledPanel(PanelBg, 0);
        budgetPanel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(budgetPanel);

        MarginContainer budgetMargin = new MarginContainer();
        budgetMargin.AddThemeConstantOverride("margin_left", 16);
        budgetMargin.AddThemeConstantOverride("margin_right", 16);
        budgetMargin.AddThemeConstantOverride("margin_top", 6);
        budgetMargin.AddThemeConstantOverride("margin_bottom", 6);
        budgetMargin.MouseFilter = MouseFilterEnum.Ignore;
        budgetPanel.AddChild(budgetMargin);

        _budgetLabel = new Label();
        _budgetLabel.Text = "$0";
        _budgetLabel.AddThemeFontOverride("font", PixelFont);
        _budgetLabel.AddThemeFontSizeOverride("font_size", 18);
        _budgetLabel.AddThemeColorOverride("font_color", AccentGold);
        _budgetLabel.MouseFilter = MouseFilterEnum.Ignore;
        budgetMargin.AddChild(_budgetLabel);

        // Spacer
        Control spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(spacer);

        // Timer
        _timerPanel = CreateStyledPanel(PanelBg, 0);
        _timerPanel.MouseFilter = MouseFilterEnum.Ignore;
        topContent.AddChild(_timerPanel);

        MarginContainer timerMargin = new MarginContainer();
        timerMargin.AddThemeConstantOverride("margin_left", 16);
        timerMargin.AddThemeConstantOverride("margin_right", 16);
        timerMargin.AddThemeConstantOverride("margin_top", 6);
        timerMargin.AddThemeConstantOverride("margin_bottom", 6);
        timerMargin.MouseFilter = MouseFilterEnum.Ignore;
        _timerPanel.AddChild(timerMargin);

        HBoxContainer timerRow = new HBoxContainer();
        timerRow.AddThemeConstantOverride("separation", 8);
        timerRow.MouseFilter = MouseFilterEnum.Ignore;
        timerMargin.AddChild(timerRow);

        Label clockIcon = new Label();
        clockIcon.Text = "\u23f1";
        clockIcon.AddThemeFontSizeOverride("font_size", 20);
        clockIcon.AddThemeColorOverride("font_color", TextSecondary);
        clockIcon.MouseFilter = MouseFilterEnum.Ignore;
        timerRow.AddChild(clockIcon);

        _timerLabel = new Label();
        _timerLabel.Text = "5:00";
        _timerLabel.AddThemeFontOverride("font", PixelFont);
        _timerLabel.AddThemeFontSizeOverride("font_size", 16);
        _timerLabel.AddThemeColorOverride("font_color", TextPrimary);
        _timerLabel.MouseFilter = MouseFilterEnum.Ignore;
        timerRow.AddChild(_timerLabel);
    }

    // ========== LEFT PANEL: Material Palette ==========
    private void BuildLeftPanel()
    {
        PanelContainer leftPanel = CreateBeveledPanel(PanelBg, AccentGreen);
        leftPanel.SetAnchorsPreset(LayoutPreset.LeftWide);
        leftPanel.OffsetLeft = 0;
        leftPanel.OffsetRight = 240;
        leftPanel.OffsetTop = 80;
        leftPanel.OffsetBottom = -96;
        leftPanel.CustomMinimumSize = new Vector2(240, 0);
        leftPanel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(leftPanel);

        MarginContainer leftMargin = new MarginContainer();
        leftMargin.AddThemeConstantOverride("margin_left", 14);
        leftMargin.AddThemeConstantOverride("margin_right", 14);
        leftMargin.AddThemeConstantOverride("margin_top", 14);
        leftMargin.AddThemeConstantOverride("margin_bottom", 14);
        leftMargin.MouseFilter = MouseFilterEnum.Ignore;
        leftPanel.AddChild(leftMargin);

        VBoxContainer matContainer = new VBoxContainer();
        matContainer.AddThemeConstantOverride("separation", 6);
        matContainer.MouseFilter = MouseFilterEnum.Ignore;
        leftMargin.AddChild(matContainer);

        // Header
        Label matHeader = new Label();
        matHeader.Text = "MATERIALS";
        matHeader.AddThemeFontOverride("font", PixelFont);
        matHeader.AddThemeFontSizeOverride("font_size", 10);
        matHeader.AddThemeColorOverride("font_color", TextSecondary);
        matHeader.MouseFilter = MouseFilterEnum.Ignore;
        matContainer.AddChild(matHeader);

        ColorRect headerLine = new ColorRect();
        headerLine.CustomMinimumSize = new Vector2(0, 1);
        headerLine.Color = BorderColor;
        headerLine.MouseFilter = MouseFilterEnum.Ignore;
        matContainer.AddChild(headerLine);

        // Spacer after header
        Control headerSpacer = new Control();
        headerSpacer.CustomMinimumSize = new Vector2(0, 2);
        headerSpacer.MouseFilter = MouseFilterEnum.Ignore;
        matContainer.AddChild(headerSpacer);

        // Material items
        for (int i = 0; i < BuildMaterials.Length; i++)
        {
            VoxelMaterialType mat = BuildMaterials[i];
            VoxelMaterialDefinition def = VoxelMaterials.GetDefinition(mat);
            Color previewColor = VoxelMaterials.GetPreviewColor(mat);

            PanelContainer matBtn = CreateStyledPanel(
                i == _selectedMaterialIndex ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.15f) : new Color(0, 0, 0, 0),
                0);
            matBtn.CustomMinimumSize = new Vector2(0, 42);
            matBtn.MouseFilter = MouseFilterEnum.Stop;
            _materialButtons.Add(matBtn);

            int capturedIndex = i;

            HBoxContainer matRow = new HBoxContainer();
            matRow.AddThemeConstantOverride("separation", 10);
            matRow.MouseFilter = MouseFilterEnum.Ignore;
            matBtn.AddChild(matRow);

            // Material texture swatch — show the actual AI-generated texture image
            MarginContainer swatchMargin = new MarginContainer();
            swatchMargin.AddThemeConstantOverride("margin_left", 8);
            swatchMargin.AddThemeConstantOverride("margin_top", 5);
            swatchMargin.AddThemeConstantOverride("margin_bottom", 5);
            swatchMargin.MouseFilter = MouseFilterEnum.Ignore;
            matRow.AddChild(swatchMargin);

            // Try to load the actual texture; fall back to flat color if missing
            string texName = mat.ToString().ToLowerInvariant();
            // Handle multi-word names (ArmorPlate -> armorplate, ReinforcedSteel -> reinforcedsteel)
            texName = texName.Replace(" ", "");
            string texPath = $"res://assets/textures/voxels/{texName}_32.png";
            Texture2D? matTex = ResourceLoader.Exists(texPath) ? ResourceLoader.Load<Texture2D>(texPath) : null;

            if (matTex != null)
            {
                // Show the actual texture image
                PanelContainer swatchBorder = new PanelContainer();
                swatchBorder.CustomMinimumSize = new Vector2(36, 36);
                StyleBoxFlat borderStyle = new StyleBoxFlat();
                borderStyle.BgColor = new Color(previewColor.R * 0.6f, previewColor.G * 0.6f, previewColor.B * 0.6f, 1f);
                borderStyle.ContentMarginTop = 2;
                borderStyle.ContentMarginBottom = 2;
                borderStyle.ContentMarginLeft = 2;
                borderStyle.ContentMarginRight = 2;
                swatchBorder.AddThemeStyleboxOverride("panel", borderStyle);
                swatchBorder.MouseFilter = MouseFilterEnum.Ignore;

                TextureRect texRect = new TextureRect();
                texRect.Texture = matTex;
                texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                texRect.StretchMode = TextureRect.StretchModeEnum.Scale;
                texRect.CustomMinimumSize = new Vector2(32, 32);
                texRect.MouseFilter = MouseFilterEnum.Ignore;
                swatchBorder.AddChild(texRect);
                swatchMargin.AddChild(swatchBorder);
            }
            else
            {
                // Fallback: flat color swatch
                PanelContainer swatch = new PanelContainer();
                swatch.CustomMinimumSize = new Vector2(32, 32);
                StyleBoxFlat swatchStyle = new StyleBoxFlat();
                swatchStyle.BgColor = previewColor;
                swatchStyle.BorderWidthTop = 2;
                swatchStyle.BorderWidthBottom = 2;
                swatchStyle.BorderWidthLeft = 2;
                swatchStyle.BorderWidthRight = 2;
                swatchStyle.BorderColor = new Color(previewColor.R * 0.6f, previewColor.G * 0.6f, previewColor.B * 0.6f, 1f);
                swatch.AddThemeStyleboxOverride("panel", swatchStyle);
                swatch.MouseFilter = MouseFilterEnum.Ignore;
                swatchMargin.AddChild(swatch);
            }

            // Name and cost
            VBoxContainer matInfo = new VBoxContainer();
            matInfo.AddThemeConstantOverride("separation", 2);
            matInfo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            matInfo.MouseFilter = MouseFilterEnum.Ignore;
            matRow.AddChild(matInfo);

            Label matName = new Label();
            matName.Text = mat.ToString();
            matName.AddThemeFontOverride("font", PixelFont);
            matName.AddThemeFontSizeOverride("font_size", 10);
            matName.AddThemeColorOverride("font_color", TextPrimary);
            matName.MouseFilter = MouseFilterEnum.Ignore;
            matInfo.AddChild(matName);

            Label matCost = new Label();
            matCost.Text = $"${def.Cost}  HP:{def.MaxHitPoints}";
            matCost.AddThemeFontOverride("font", PixelFont);
            matCost.AddThemeFontSizeOverride("font_size", 8);
            matCost.AddThemeColorOverride("font_color", TextSecondary);
            matCost.MouseFilter = MouseFilterEnum.Ignore;
            matInfo.AddChild(matCost);

            // Click handler via button overlay
            Button matClickArea = new Button();
            matClickArea.Flat = true;
            matClickArea.MouseFilter = MouseFilterEnum.Stop;
            matClickArea.Modulate = new Color(1, 1, 1, 0);
            matClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); SelectMaterial(capturedIndex); };
            VoxelMaterialType capturedMat = mat;
            matClickArea.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); ShowMaterialTooltip(capturedMat); };
            matClickArea.MouseExited += HideBuildTooltip;
            matBtn.AddChild(matClickArea);
            matClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            matClickArea.OffsetLeft = 0;
            matClickArea.OffsetRight = 0;
            matClickArea.OffsetTop = 0;
            matClickArea.OffsetBottom = 0;

            matContainer.AddChild(matBtn);
        }
    }

    // ========== RIGHT PANEL: Tool Selector ==========
    private void BuildRightPanel()
    {
        // Outer container anchored to right side — uses a VBoxContainer so we can
        // put the ScrollContainer on top and the Ready button fixed at the bottom.
        PanelContainer rightPanel = CreateBeveledPanel(PanelBg, AccentGreen);
        rightPanel.SetAnchorsPreset(LayoutPreset.RightWide);
        rightPanel.OffsetLeft = -300;
        rightPanel.OffsetRight = 0;
        rightPanel.OffsetTop = 80;
        rightPanel.OffsetBottom = -96;
        rightPanel.CustomMinimumSize = new Vector2(300, 0);
        rightPanel.MouseFilter = MouseFilterEnum.Stop;
        rightPanel.ClipContents = true;
        AddChild(rightPanel);

        // Root layout inside the panel: scroll area + fixed ready button
        VBoxContainer panelRoot = new VBoxContainer();
        panelRoot.AddThemeConstantOverride("separation", 0);
        panelRoot.MouseFilter = MouseFilterEnum.Ignore;
        panelRoot.SizeFlagsVertical = SizeFlags.ExpandFill;
        panelRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        rightPanel.AddChild(panelRoot);

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        scrollContainer.MouseFilter = MouseFilterEnum.Pass;
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        panelRoot.AddChild(scrollContainer);

        MarginContainer rightMargin = new MarginContainer();
        rightMargin.AddThemeConstantOverride("margin_left", 14);
        rightMargin.AddThemeConstantOverride("margin_right", 14);
        rightMargin.AddThemeConstantOverride("margin_top", 12);
        rightMargin.AddThemeConstantOverride("margin_bottom", 12);
        rightMargin.MouseFilter = MouseFilterEnum.Ignore;
        scrollContainer.AddChild(rightMargin);

        VBoxContainer toolContainer = new VBoxContainer();
        toolContainer.AddThemeConstantOverride("separation", 4);
        toolContainer.MouseFilter = MouseFilterEnum.Ignore;
        rightMargin.AddChild(toolContainer);

        // Header
        Label toolHeader = new Label();
        toolHeader.Text = "TOOLS";
        toolHeader.AddThemeFontOverride("font", PixelFont);
        toolHeader.AddThemeFontSizeOverride("font_size", 10);
        toolHeader.AddThemeColorOverride("font_color", TextSecondary);
        toolHeader.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(toolHeader);

        ColorRect toolLine = new ColorRect();
        toolLine.CustomMinimumSize = new Vector2(0, 1);
        toolLine.Color = BorderColor;
        toolLine.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(toolLine);

        for (int i = 0; i < Tools.Length; i++)
        {
            var tool = Tools[i];
            bool isEraser = tool.Mode == BuildToolMode.Eraser;
            bool isDoor = tool.Mode == BuildToolMode.Door;
            Color toolAccent = isEraser ? AccentRed : isDoor ? AccentGold : AccentGreen;

            PanelContainer toolBtn = CreateStyledPanel(
                i == _selectedToolIndex ? new Color(toolAccent.R, toolAccent.G, toolAccent.B, 0.15f) : new Color(0, 0, 0, 0),
                0);
            toolBtn.CustomMinimumSize = new Vector2(0, 40);
            toolBtn.MouseFilter = MouseFilterEnum.Stop;
            _toolButtons.Add(toolBtn);

            int capturedIndex = i;

            HBoxContainer toolRow = new HBoxContainer();
            toolRow.AddThemeConstantOverride("separation", 10);
            toolRow.MouseFilter = MouseFilterEnum.Ignore;
            toolBtn.AddChild(toolRow);

            MarginContainer iconMargin = new MarginContainer();
            iconMargin.AddThemeConstantOverride("margin_left", 12);
            iconMargin.AddThemeConstantOverride("margin_top", 4);
            iconMargin.MouseFilter = MouseFilterEnum.Ignore;
            toolRow.AddChild(iconMargin);

            Label toolIcon = new Label();
            toolIcon.Text = tool.Icon;
            toolIcon.AddThemeFontSizeOverride("font_size", 20);
            toolIcon.AddThemeColorOverride("font_color", toolAccent);
            toolIcon.MouseFilter = MouseFilterEnum.Ignore;
            iconMargin.AddChild(toolIcon);

            Label toolName = new Label();
            toolName.Text = tool.Name;
            toolName.AddThemeFontOverride("font", PixelFont);
            toolName.AddThemeFontSizeOverride("font_size", 12);
            toolName.AddThemeColorOverride("font_color", TextPrimary);
            toolName.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            toolName.MouseFilter = MouseFilterEnum.Ignore;
            toolRow.AddChild(toolName);

            Button toolClickArea = new Button();
            toolClickArea.Flat = true;
            toolClickArea.MouseFilter = MouseFilterEnum.Stop;
            toolClickArea.Modulate = new Color(1, 1, 1, 0);
            toolClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); SelectTool(capturedIndex); };
            toolBtn.AddChild(toolClickArea);
            toolClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            toolClickArea.OffsetLeft = 0;
            toolClickArea.OffsetRight = 0;
            toolClickArea.OffsetTop = 0;
            toolClickArea.OffsetBottom = 0;

            toolContainer.AddChild(toolBtn);
        }

        // Separator before blueprints
        Control bpSepBefore = new Control();
        bpSepBefore.CustomMinimumSize = new Vector2(0, 4);
        bpSepBefore.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(bpSepBefore);

        // Blueprints header
        Label bpHeader = new Label();
        bpHeader.Text = "BLUEPRINTS";
        bpHeader.AddThemeFontOverride("font", PixelFont);
        bpHeader.AddThemeFontSizeOverride("font_size", 10);
        bpHeader.AddThemeColorOverride("font_color", TextSecondary);
        bpHeader.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(bpHeader);

        ColorRect bpLine = new ColorRect();
        bpLine.CustomMinimumSize = new Vector2(0, 1);
        bpLine.Color = BorderColor;
        bpLine.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(bpLine);

        // Blueprint preset buttons
        BlueprintDefinition[] blueprints = BuildBlueprints.All;
        for (int i = 0; i < blueprints.Length; i++)
        {
            BlueprintDefinition bp = blueprints[i];

            PanelContainer bpBtn = CreateStyledPanel(new Color(0, 0, 0, 0), 0);
            bpBtn.CustomMinimumSize = new Vector2(0, 36);
            bpBtn.MouseFilter = MouseFilterEnum.Stop;
            _blueprintButtons.Add(bpBtn);

            int capturedBpIndex = i;

            HBoxContainer bpRow = new HBoxContainer();
            bpRow.AddThemeConstantOverride("separation", 6);
            bpRow.MouseFilter = MouseFilterEnum.Ignore;
            bpBtn.AddChild(bpRow);

            MarginContainer bpIconMargin = new MarginContainer();
            bpIconMargin.AddThemeConstantOverride("margin_left", 8);
            bpIconMargin.AddThemeConstantOverride("margin_top", 4);
            bpIconMargin.MouseFilter = MouseFilterEnum.Ignore;
            bpRow.AddChild(bpIconMargin);

            Label bpIcon = new Label();
            bpIcon.Text = bp.Icon;
            bpIcon.AddThemeFontSizeOverride("font_size", 14);
            bpIcon.AddThemeColorOverride("font_color", AccentGreen);
            bpIcon.MouseFilter = MouseFilterEnum.Ignore;
            bpIconMargin.AddChild(bpIcon);

            VBoxContainer bpInfo = new VBoxContainer();
            bpInfo.AddThemeConstantOverride("separation", 0);
            bpInfo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            bpInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bpInfo.MouseFilter = MouseFilterEnum.Ignore;
            bpRow.AddChild(bpInfo);

            Label bpName = new Label();
            bpName.Text = bp.Name;
            bpName.AddThemeFontOverride("font", PixelFont);
            bpName.AddThemeFontSizeOverride("font_size", 10);
            bpName.AddThemeColorOverride("font_color", TextPrimary);
            bpName.ClipText = true;
            bpName.MouseFilter = MouseFilterEnum.Ignore;
            bpInfo.AddChild(bpName);

            Label bpDesc = new Label();
            bpDesc.Text = bp.Description;
            bpDesc.AddThemeFontOverride("font", PixelFont);
            bpDesc.AddThemeFontSizeOverride("font_size", 10);
            bpDesc.AddThemeColorOverride("font_color", TextSecondary);
            bpDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            bpDesc.MouseFilter = MouseFilterEnum.Ignore;
            bpInfo.AddChild(bpDesc);

            Button bpClickArea = new Button();
            bpClickArea.Flat = true;
            bpClickArea.MouseFilter = MouseFilterEnum.Stop;
            bpClickArea.Modulate = new Color(1, 1, 1, 0);
            bpClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); SelectBlueprint(capturedBpIndex); };
            bpClickArea.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); ShowBlueprintTooltip(capturedBpIndex); };
            bpClickArea.MouseExited += HideBuildTooltip;
            bpBtn.AddChild(bpClickArea);
            bpClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            bpClickArea.OffsetLeft = 0;
            bpClickArea.OffsetRight = 0;
            bpClickArea.OffsetTop = 0;
            bpClickArea.OffsetBottom = 0;

            toolContainer.AddChild(bpBtn);
        }

        // Separator before weapons
        Control weapSep = new Control();
        weapSep.CustomMinimumSize = new Vector2(0, 4);
        weapSep.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(weapSep);

        // Weapons header
        Label weapHeader = new Label();
        weapHeader.Text = "WEAPONS";
        weapHeader.AddThemeFontOverride("font", PixelFont);
        weapHeader.AddThemeFontSizeOverride("font_size", 10);
        weapHeader.AddThemeColorOverride("font_color", TextSecondary);
        weapHeader.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(weapHeader);

        ColorRect weapLine = new ColorRect();
        weapLine.CustomMinimumSize = new Vector2(0, 1);
        weapLine.Color = BorderColor;
        weapLine.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(weapLine);

        // Weapon type buttons with costs
        for (int i = 0; i < WeaponOptions.Length; i++)
        {
            var weaponOpt = WeaponOptions[i];
            bool isSelected = i == _selectedWeaponTypeIndex;

            PanelContainer weapBtn = CreateStyledPanel(
                isSelected ? new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.15f) : new Color(0, 0, 0, 0),
                0);
            weapBtn.CustomMinimumSize = new Vector2(0, 36);
            weapBtn.MouseFilter = MouseFilterEnum.Stop;
            _weaponTypeButtons.Add(weapBtn);

            int capturedIndex = i;

            HBoxContainer weapRow = new HBoxContainer();
            weapRow.AddThemeConstantOverride("separation", 6);
            weapRow.MouseFilter = MouseFilterEnum.Ignore;
            weapBtn.AddChild(weapRow);

            MarginContainer weapIconMargin = new MarginContainer();
            weapIconMargin.AddThemeConstantOverride("margin_left", 8);
            weapIconMargin.AddThemeConstantOverride("margin_top", 4);
            weapIconMargin.MouseFilter = MouseFilterEnum.Ignore;
            weapRow.AddChild(weapIconMargin);

            Label weapIcon = new Label();
            weapIcon.Text = "\u2694";
            weapIcon.AddThemeFontSizeOverride("font_size", 14);
            weapIcon.AddThemeColorOverride("font_color", AccentRed);
            weapIcon.MouseFilter = MouseFilterEnum.Ignore;
            weapIconMargin.AddChild(weapIcon);

            VBoxContainer weapInfo = new VBoxContainer();
            weapInfo.AddThemeConstantOverride("separation", 0);
            weapInfo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            weapInfo.MouseFilter = MouseFilterEnum.Ignore;
            weapRow.AddChild(weapInfo);

            Label weapName = new Label();
            weapName.Text = weaponOpt.Name;
            weapName.AddThemeFontOverride("font", PixelFont);
            weapName.AddThemeFontSizeOverride("font_size", 10);
            weapName.AddThemeColorOverride("font_color", TextPrimary);
            weapName.MouseFilter = MouseFilterEnum.Ignore;
            weapInfo.AddChild(weapName);

            Label weapCost = new Label();
            weapCost.Text = $"${weaponOpt.Cost}";
            weapCost.AddThemeFontOverride("font", PixelFont);
            weapCost.AddThemeFontSizeOverride("font_size", 10);
            weapCost.AddThemeColorOverride("font_color", AccentGold);
            weapCost.MouseFilter = MouseFilterEnum.Ignore;
            weapInfo.AddChild(weapCost);

            Button weapClickArea = new Button();
            weapClickArea.Flat = true;
            weapClickArea.MouseFilter = MouseFilterEnum.Stop;
            weapClickArea.Modulate = new Color(1, 1, 1, 0);
            weapClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); SelectWeaponType(capturedIndex); };
            weapClickArea.GuiInput += (InputEvent @event) =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    AudioDirector.Instance?.PlaySFX("ui_click");
                    WeaponSellRequested?.Invoke(WeaponOptions[capturedIndex].Type);
                    weapClickArea.AcceptEvent();
                }
            };
            int capturedWeapIdx = i;
            weapClickArea.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); ShowWeaponTooltip(capturedWeapIdx); };
            weapClickArea.MouseExited += HideBuildTooltip;
            weapBtn.AddChild(weapClickArea);
            weapClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            weapClickArea.OffsetLeft = 0;
            weapClickArea.OffsetRight = 0;
            weapClickArea.OffsetTop = 0;
            weapClickArea.OffsetBottom = 0;

            toolContainer.AddChild(weapBtn);
        }

        // Separator before powerups
        Control pwrSep = new Control();
        pwrSep.CustomMinimumSize = new Vector2(0, 4);
        pwrSep.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(pwrSep);

        // Powerups header
        Label pwrHeader = new Label();
        pwrHeader.Text = "POWERUPS";
        pwrHeader.AddThemeFontOverride("font", PixelFont);
        pwrHeader.AddThemeFontSizeOverride("font_size", 10);
        pwrHeader.AddThemeColorOverride("font_color", TextSecondary);
        pwrHeader.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(pwrHeader);

        ColorRect pwrLine = new ColorRect();
        pwrLine.CustomMinimumSize = new Vector2(0, 1);
        pwrLine.Color = BorderColor;
        pwrLine.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(pwrLine);

        // Powerup buy buttons
        PowerupType[] powerupTypes = (PowerupType[])Enum.GetValues(typeof(PowerupType));
        for (int i = 0; i < powerupTypes.Length; i++)
        {
            PowerupType pType = powerupTypes[i];
            PowerupDefinition def = PowerupDefinitions.Get(pType);

            PanelContainer pwrBtn = CreateStyledPanel(new Color(0, 0, 0, 0), 0);
            pwrBtn.CustomMinimumSize = new Vector2(0, 36);
            pwrBtn.MouseFilter = MouseFilterEnum.Stop;
            _powerupButtons.Add(pwrBtn);

            HBoxContainer pwrRow = new HBoxContainer();
            pwrRow.AddThemeConstantOverride("separation", 6);
            pwrRow.MouseFilter = MouseFilterEnum.Ignore;
            pwrBtn.AddChild(pwrRow);

            MarginContainer pwrIconMargin = new MarginContainer();
            pwrIconMargin.AddThemeConstantOverride("margin_left", 6);
            pwrIconMargin.AddThemeConstantOverride("margin_top", 4);
            pwrIconMargin.MouseFilter = MouseFilterEnum.Ignore;
            pwrRow.AddChild(pwrIconMargin);

            Label pwrIcon = new Label();
            pwrIcon.Text = def.IconGlyph;
            pwrIcon.AddThemeFontSizeOverride("font_size", 14);
            pwrIcon.AddThemeColorOverride("font_color", def.AccentColor);
            pwrIcon.MouseFilter = MouseFilterEnum.Ignore;
            pwrIconMargin.AddChild(pwrIcon);

            VBoxContainer pwrInfo = new VBoxContainer();
            pwrInfo.AddThemeConstantOverride("separation", 0);
            pwrInfo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pwrInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            pwrInfo.MouseFilter = MouseFilterEnum.Ignore;
            pwrRow.AddChild(pwrInfo);

            Label pwrName = new Label();
            pwrName.Text = def.Name;
            pwrName.AddThemeFontOverride("font", PixelFont);
            pwrName.AddThemeFontSizeOverride("font_size", 10);
            pwrName.AddThemeColorOverride("font_color", TextPrimary);
            pwrName.MouseFilter = MouseFilterEnum.Ignore;
            pwrInfo.AddChild(pwrName);

            Label pwrCost = new Label();
            pwrCost.Text = $"${def.Cost}";
            pwrCost.AddThemeFontOverride("font", PixelFont);
            pwrCost.AddThemeFontSizeOverride("font_size", 10);
            pwrCost.AddThemeColorOverride("font_color", AccentGold);
            pwrCost.MouseFilter = MouseFilterEnum.Ignore;
            pwrInfo.AddChild(pwrCost);

            // Count label (shows "x0", "x1", etc.)
            Label countLabel = new Label();
            countLabel.Text = "x0";
            countLabel.AddThemeFontOverride("font", PixelFont);
            countLabel.AddThemeFontSizeOverride("font_size", 10);
            countLabel.AddThemeColorOverride("font_color", TextSecondary);
            countLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            countLabel.MouseFilter = MouseFilterEnum.Ignore;
            pwrRow.AddChild(countLabel);
            _powerupCountLabels.Add(countLabel);

            PowerupType capturedType = pType;
            Button pwrClickArea = new Button();
            pwrClickArea.Flat = true;
            pwrClickArea.MouseFilter = MouseFilterEnum.Stop;
            pwrClickArea.Modulate = new Color(1, 1, 1, 0);
            pwrClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); OnPowerupBuyClicked(capturedType); };
            pwrClickArea.GuiInput += (InputEvent @event) =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    AudioDirector.Instance?.PlaySFX("ui_click");
                    OnPowerupSellClicked(capturedType);
                    pwrClickArea.AcceptEvent();
                }
            };
            pwrClickArea.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); ShowPowerupTooltip(capturedType); };
            pwrClickArea.MouseExited += HideBuildTooltip;
            pwrBtn.AddChild(pwrClickArea);
            pwrClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            pwrClickArea.OffsetLeft = 0;
            pwrClickArea.OffsetRight = 0;
            pwrClickArea.OffsetTop = 0;
            pwrClickArea.OffsetBottom = 0;

            toolContainer.AddChild(pwrBtn);
        }

        // Separator before army
        Control armySep = new Control();
        armySep.CustomMinimumSize = new Vector2(0, 4);
        armySep.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(armySep);

        // Army header
        Label armyHeader = new Label();
        armyHeader.Text = "ARMY";
        armyHeader.AddThemeFontOverride("font", PixelFont);
        armyHeader.AddThemeFontSizeOverride("font_size", 10);
        armyHeader.AddThemeColorOverride("font_color", TextSecondary);
        armyHeader.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(armyHeader);

        ColorRect armyLine = new ColorRect();
        armyLine.CustomMinimumSize = new Vector2(0, 1);
        armyLine.Color = BorderColor;
        armyLine.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(armyLine);

        // Troop buy buttons
        TroopType[] troopTypes = TroopDefinitions.AllTypes;
        for (int i = 0; i < troopTypes.Length; i++)
        {
            TroopType tType = troopTypes[i];
            TroopStats tStats = TroopDefinitions.Get(tType);

            PanelContainer troopBtn = CreateStyledPanel(new Color(0, 0, 0, 0), 0);
            troopBtn.CustomMinimumSize = new Vector2(0, 36);
            troopBtn.MouseFilter = MouseFilterEnum.Stop;
            _troopButtons.Add(troopBtn);

            HBoxContainer troopRow = new HBoxContainer();
            troopRow.AddThemeConstantOverride("separation", 6);
            troopRow.MouseFilter = MouseFilterEnum.Ignore;
            troopBtn.AddChild(troopRow);

            MarginContainer troopIconMargin = new MarginContainer();
            troopIconMargin.AddThemeConstantOverride("margin_left", 6);
            troopIconMargin.AddThemeConstantOverride("margin_top", 4);
            troopIconMargin.MouseFilter = MouseFilterEnum.Ignore;
            troopRow.AddChild(troopIconMargin);

            Label troopIcon = new Label();
            troopIcon.Text = tType == TroopType.Infantry ? "\u265f" :
                             tType == TroopType.Demolisher ? "\u2620" : "\u21c9";
            troopIcon.AddThemeFontSizeOverride("font_size", 14);
            troopIcon.AddThemeColorOverride("font_color", AccentGreen);
            troopIcon.MouseFilter = MouseFilterEnum.Ignore;
            troopIconMargin.AddChild(troopIcon);

            VBoxContainer troopInfo = new VBoxContainer();
            troopInfo.AddThemeConstantOverride("separation", 0);
            troopInfo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            troopInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            troopInfo.MouseFilter = MouseFilterEnum.Ignore;
            troopRow.AddChild(troopInfo);

            Label troopName = new Label();
            troopName.Text = tStats.Name;
            troopName.AddThemeFontOverride("font", PixelFont);
            troopName.AddThemeFontSizeOverride("font_size", 10);
            troopName.AddThemeColorOverride("font_color", TextPrimary);
            troopName.MouseFilter = MouseFilterEnum.Ignore;
            troopInfo.AddChild(troopName);

            Label troopCost = new Label();
            troopCost.Text = $"${tStats.Cost}";
            troopCost.AddThemeFontOverride("font", PixelFont);
            troopCost.AddThemeFontSizeOverride("font_size", 10);
            troopCost.AddThemeColorOverride("font_color", AccentGold);
            troopCost.MouseFilter = MouseFilterEnum.Ignore;
            troopInfo.AddChild(troopCost);

            // Count label (shows "x0", "x1", etc.)
            Label troopCountLabel = new Label();
            troopCountLabel.Text = "x0";
            troopCountLabel.AddThemeFontOverride("font", PixelFont);
            troopCountLabel.AddThemeFontSizeOverride("font_size", 10);
            troopCountLabel.AddThemeColorOverride("font_color", TextSecondary);
            troopCountLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            troopCountLabel.MouseFilter = MouseFilterEnum.Ignore;
            troopRow.AddChild(troopCountLabel);
            _troopCountLabels.Add(troopCountLabel);

            TroopType capturedTroopType = tType;
            Button troopClickArea = new Button();
            troopClickArea.Flat = true;
            troopClickArea.MouseFilter = MouseFilterEnum.Stop;
            troopClickArea.Modulate = new Color(1, 1, 1, 0);
            troopClickArea.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); OnTroopBuyClicked(capturedTroopType); };
            troopClickArea.GuiInput += (InputEvent @event) =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    AudioDirector.Instance?.PlaySFX("ui_click");
                    OnTroopSellClicked(capturedTroopType);
                    troopClickArea.AcceptEvent();
                }
            };
            troopClickArea.MouseEntered += () => { AudioDirector.Instance?.PlaySFX("ui_hover"); ShowTroopTooltip(capturedTroopType); };
            troopClickArea.MouseExited += HideBuildTooltip;
            troopBtn.AddChild(troopClickArea);
            troopClickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            troopClickArea.OffsetLeft = 0;
            troopClickArea.OffsetRight = 0;
            troopClickArea.OffsetTop = 0;
            troopClickArea.OffsetBottom = 0;

            toolContainer.AddChild(troopBtn);
        }

        // ── End of scrollable content — add bottom padding inside scroll ──
        Control scrollBottomPad = new Control();
        scrollBottomPad.CustomMinimumSize = new Vector2(0, 8);
        scrollBottomPad.MouseFilter = MouseFilterEnum.Ignore;
        toolContainer.AddChild(scrollBottomPad);

        // ── PLACE COMMANDER + READY — FIXED at the bottom, outside the ScrollContainer ──
        // Separator line
        ColorRect readyLine = new ColorRect();
        readyLine.CustomMinimumSize = new Vector2(0, 2);
        readyLine.Color = AccentGold;
        readyLine.MouseFilter = MouseFilterEnum.Ignore;
        panelRoot.AddChild(readyLine);

        // Commander placement button — always visible at the bottom
        MarginContainer cmdMargin = new MarginContainer();
        cmdMargin.AddThemeConstantOverride("margin_left", 14);
        cmdMargin.AddThemeConstantOverride("margin_right", 14);
        cmdMargin.AddThemeConstantOverride("margin_top", 6);
        cmdMargin.AddThemeConstantOverride("margin_bottom", 4);
        cmdMargin.MouseFilter = MouseFilterEnum.Ignore;
        panelRoot.AddChild(cmdMargin);

        Button cmdBtn = new Button();
        cmdBtn.Text = "\u2655  PLACE COMMANDER";
        cmdBtn.CustomMinimumSize = new Vector2(0, 40);
        cmdBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cmdBtn.AddThemeFontOverride("font", PixelFont);
        cmdBtn.AddThemeFontSizeOverride("font_size", 12);
        cmdBtn.AddThemeColorOverride("font_color", AccentGold);
        cmdBtn.AddThemeColorOverride("font_hover_color", BgDark);
        cmdBtn.MouseFilter = MouseFilterEnum.Stop;
        cmdBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); PlaceCommanderRequested?.Invoke(); };

        StyleBoxFlat cmdNormal = new StyleBoxFlat();
        cmdNormal.BgColor = new Color(AccentGold.R, AccentGold.G, AccentGold.B, 0.15f);
        cmdNormal.ContentMarginLeft = 8;
        cmdNormal.ContentMarginRight = 8;
        cmdNormal.ContentMarginTop = 6;
        cmdNormal.ContentMarginBottom = 6;
        cmdBtn.AddThemeStyleboxOverride("normal", cmdNormal);

        StyleBoxFlat cmdHover = new StyleBoxFlat();
        cmdHover.BgColor = AccentGold;
        cmdHover.ContentMarginLeft = 8;
        cmdHover.ContentMarginRight = 8;
        cmdHover.ContentMarginTop = 6;
        cmdHover.ContentMarginBottom = 6;
        cmdBtn.AddThemeStyleboxOverride("hover", cmdHover);

        cmdMargin.AddChild(cmdBtn);

        // Ready separator
        ColorRect readyLine2 = new ColorRect();
        readyLine2.CustomMinimumSize = new Vector2(0, 2);
        readyLine2.Color = AccentGreen;
        readyLine2.MouseFilter = MouseFilterEnum.Ignore;
        panelRoot.AddChild(readyLine2);

        // Margin around the ready button
        MarginContainer readyMargin = new MarginContainer();
        readyMargin.AddThemeConstantOverride("margin_left", 14);
        readyMargin.AddThemeConstantOverride("margin_right", 14);
        readyMargin.AddThemeConstantOverride("margin_top", 6);
        readyMargin.AddThemeConstantOverride("margin_bottom", 8);
        readyMargin.MouseFilter = MouseFilterEnum.Ignore;
        panelRoot.AddChild(readyMargin);

        _readyBtn = new Button();
        _readyBtn.Name = "ReadyButton";
        _readyBtn.CustomMinimumSize = new Vector2(0, 48);
        _readyBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _readyBtn.MouseFilter = MouseFilterEnum.Stop;
        _readyBtn.AddThemeFontOverride("font", PixelFont);
        _readyBtn.AddThemeFontSizeOverride("font_size", 14);
        _readyBtn.AddThemeColorOverride("font_color", TextPrimary);
        _readyBtn.AddThemeColorOverride("font_hover_color", BgDark);

        // Normal style — green bg, square corners
        StyleBoxFlat readyNormal = new StyleBoxFlat();
        readyNormal.BgColor = AccentGreen;
        readyNormal.CornerRadiusTopLeft = 0;
        readyNormal.CornerRadiusTopRight = 0;
        readyNormal.CornerRadiusBottomLeft = 0;
        readyNormal.CornerRadiusBottomRight = 0;
        readyNormal.ContentMarginLeft = 8;
        readyNormal.ContentMarginRight = 8;
        readyNormal.ContentMarginTop = 8;
        readyNormal.ContentMarginBottom = 8;
        _readyBtn.AddThemeStyleboxOverride("normal", readyNormal);

        // Hover style — brighter green
        StyleBoxFlat readyHover = new StyleBoxFlat();
        readyHover.BgColor = new Color(AccentGreen.R * 1.2f, AccentGreen.G * 1.2f, AccentGreen.B * 1.2f, 1f);
        readyHover.CornerRadiusTopLeft = 0;
        readyHover.CornerRadiusTopRight = 0;
        readyHover.CornerRadiusBottomLeft = 0;
        readyHover.CornerRadiusBottomRight = 0;
        readyHover.ContentMarginLeft = 8;
        readyHover.ContentMarginRight = 8;
        readyHover.ContentMarginTop = 8;
        readyHover.ContentMarginBottom = 8;
        _readyBtn.AddThemeStyleboxOverride("hover", readyHover);

        // Pressed style — darker green
        StyleBoxFlat readyPressed = new StyleBoxFlat();
        readyPressed.BgColor = new Color(AccentGreen.R * 0.7f, AccentGreen.G * 0.7f, AccentGreen.B * 0.7f, 1f);
        readyPressed.CornerRadiusTopLeft = 0;
        readyPressed.CornerRadiusTopRight = 0;
        readyPressed.CornerRadiusBottomLeft = 0;
        readyPressed.CornerRadiusBottomRight = 0;
        readyPressed.ContentMarginLeft = 8;
        readyPressed.ContentMarginRight = 8;
        readyPressed.ContentMarginTop = 8;
        readyPressed.ContentMarginBottom = 8;
        _readyBtn.AddThemeStyleboxOverride("pressed", readyPressed);

        _readyBtn.Text = "READY";
        _readyBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_confirm"); ReadyPressed?.Invoke(); };
        readyMargin.AddChild(_readyBtn);
    }

    private void OnPowerupSellClicked(PowerupType type)
    {
        PowerupSellRequested?.Invoke(type);
    }

    private void OnPowerupBuyClicked(PowerupType type)
    {
        PowerupBuyRequested?.Invoke(type);
        // Flash the button to confirm purchase
        int index = Array.IndexOf((PowerupType[])Enum.GetValues(typeof(PowerupType)), type);
        if (index >= 0 && index < _powerupButtons.Count)
        {
            PowerupDefinition def = PowerupDefinitions.Get(type);
            _powerupButtons[index].AddThemeStyleboxOverride("panel",
                CreateFlatStyle(new Color(def.AccentColor.R, def.AccentColor.G, def.AccentColor.B, 0.2f), 0));

            // Reset after brief highlight
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                if (index < _powerupButtons.Count && GodotObject.IsInstanceValid(_powerupButtons[index]))
                {
                    _powerupButtons[index].AddThemeStyleboxOverride("panel",
                        CreateFlatStyle(new Color(0, 0, 0, 0), 0));
                }
            };
        }
    }

    /// <summary>
    /// Updates the powerup count labels based on the current player's inventory.
    /// Called from GameManager or externally after a purchase.
    /// </summary>
    public void UpdatePowerupCounts(PowerupInventory? inventory)
    {
        PowerupType[] powerupTypes = (PowerupType[])Enum.GetValues(typeof(PowerupType));
        for (int i = 0; i < powerupTypes.Length && i < _powerupCountLabels.Count; i++)
        {
            int count = inventory?.CountOf(powerupTypes[i]) ?? 0;
            _powerupCountLabels[i].Text = $"x{count}";
            _powerupCountLabels[i].AddThemeColorOverride("font_color",
                count > 0 ? AccentGreen : TextSecondary);
        }
    }

    private void OnTroopBuyClicked(TroopType type)
    {
        TroopBuyRequested?.Invoke(type);
        // Flash the button to confirm purchase
        TroopType[] troopTypes = TroopDefinitions.AllTypes;
        int index = Array.IndexOf(troopTypes, type);
        if (index >= 0 && index < _troopButtons.Count)
        {
            _troopButtons[index].AddThemeStyleboxOverride("panel",
                CreateFlatStyle(new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.2f), 0));

            // Reset after brief highlight
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                if (index < _troopButtons.Count && GodotObject.IsInstanceValid(_troopButtons[index]))
                {
                    _troopButtons[index].AddThemeStyleboxOverride("panel",
                        CreateFlatStyle(new Color(0, 0, 0, 0), 0));
                }
            };
        }
    }

    private void OnTroopSellClicked(TroopType type)
    {
        TroopSellRequested?.Invoke(type);
    }

    /// <summary>
    /// Updates the count label for a specific troop type.
    /// Called from GameManager or externally after a purchase/sell.
    /// </summary>
    public void UpdateTroopCount(TroopType type, int count)
    {
        TroopType[] troopTypes = TroopDefinitions.AllTypes;
        int index = Array.IndexOf(troopTypes, type);
        if (index >= 0 && index < _troopCountLabels.Count)
        {
            _troopCountLabels[index].Text = $"x{count}";
            _troopCountLabels[index].AddThemeColorOverride("font_color",
                count > 0 ? AccentGreen : TextSecondary);
        }
    }

    private void SelectWeaponType(int index)
    {
        _selectedWeaponTypeIndex = index;
        for (int i = 0; i < _weaponTypeButtons.Count; i++)
        {
            Color bg = i == index ? new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.15f) : new Color(0, 0, 0, 0);
            _weaponTypeButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }
        WeaponTypeSelected?.Invoke(WeaponOptions[index].Type);
        PlaceWeaponRequested?.Invoke();
    }

    // ========== BOTTOM BAR ==========
    private void BuildBottomBar()
    {
        // --- Help instructions bar (above main bottom bar) ---
        PanelContainer helpBar = CreateStyledPanel(new Color(0f, 0f, 0f, 0.5f), 0);
        helpBar.SetAnchorsPreset(LayoutPreset.BottomWide);
        helpBar.OffsetTop = -92;
        helpBar.OffsetBottom = -66;
        helpBar.CustomMinimumSize = new Vector2(0, 26);
        helpBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(helpBar);

        MarginContainer helpMargin = new MarginContainer();
        helpMargin.AddThemeConstantOverride("margin_left", 252);
        helpMargin.AddThemeConstantOverride("margin_right", 312);
        helpMargin.AddThemeConstantOverride("margin_top", 4);
        helpMargin.AddThemeConstantOverride("margin_bottom", 4);
        helpMargin.MouseFilter = MouseFilterEnum.Ignore;
        helpBar.AddChild(helpMargin);

        HBoxContainer helpRow = new HBoxContainer();
        helpRow.AddThemeConstantOverride("separation", 20);
        helpRow.Alignment = BoxContainer.AlignmentMode.Center;
        helpRow.MouseFilter = MouseFilterEnum.Ignore;
        helpMargin.AddChild(helpRow);

        string[] hints = { "LMB=Place", "RMB=Erase", "R=Rotate", "MidDrag=Camera", "Scroll=Zoom", "Ctrl+Z=Undo" };
        foreach (string hint in hints)
        {
            Label hintLabel = new Label();
            hintLabel.Text = hint;
            hintLabel.AddThemeFontOverride("font", PixelFont);
            hintLabel.AddThemeFontSizeOverride("font_size", 10);
            hintLabel.AddThemeColorOverride("font_color", TextSecondary);
            hintLabel.MouseFilter = MouseFilterEnum.Ignore;
            helpRow.AddChild(hintLabel);
        }

        // --- Main bottom bar ---
        PanelContainer bottomBar = CreateBeveledPanel(PanelBg, AccentGreen);
        bottomBar.SetAnchorsPreset(LayoutPreset.BottomWide);
        bottomBar.OffsetTop = -66;
        bottomBar.OffsetBottom = -8;
        bottomBar.CustomMinimumSize = new Vector2(0, 58);
        bottomBar.MouseFilter = MouseFilterEnum.Stop;
        AddChild(bottomBar);

        MarginContainer bottomMargin = new MarginContainer();
        bottomMargin.AddThemeConstantOverride("margin_left", 252);
        bottomMargin.AddThemeConstantOverride("margin_right", 312);
        bottomMargin.AddThemeConstantOverride("margin_top", 10);
        bottomMargin.AddThemeConstantOverride("margin_bottom", 10);
        bottomMargin.MouseFilter = MouseFilterEnum.Ignore;
        bottomBar.AddChild(bottomMargin);

        HBoxContainer bottomContent = new HBoxContainer();
        bottomContent.AddThemeConstantOverride("separation", 16);
        bottomContent.MouseFilter = MouseFilterEnum.Ignore;
        bottomMargin.AddChild(bottomContent);

        // Symmetry toggle
        Label symLabel = new Label();
        symLabel.Text = "SYMMETRY:";
        symLabel.AddThemeFontOverride("font", PixelFont);
        symLabel.AddThemeFontSizeOverride("font_size", 10);
        symLabel.AddThemeColorOverride("font_color", TextSecondary);
        symLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        symLabel.MouseFilter = MouseFilterEnum.Ignore;
        bottomContent.AddChild(symLabel);

        string[] symModes = { "OFF", "X", "Z", "XZ" };
        BuildSymmetryMode[] symValues = { BuildSymmetryMode.None, BuildSymmetryMode.MirrorX, BuildSymmetryMode.MirrorZ, BuildSymmetryMode.MirrorXZ };
        _symmetryButtons.Clear();
        for (int i = 0; i < symModes.Length; i++)
        {
            int idx = i;
            Button symBtn = CreateSmallButton(symModes[i], i == 0 ? AccentGreen : TextSecondary);
            symBtn.Pressed += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_click");
                SymmetryChanged?.Invoke(symValues[idx]);
                SelectSymmetry(idx);
            };
            _symmetryButtons.Add(symBtn);
            bottomContent.AddChild(symBtn);
        }

        // Spacer
        Control spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        bottomContent.AddChild(spacer);

        // Undo / Redo
        Button undoBtn = CreateSmallButton("\u21b6 UNDO", TextSecondary);
        undoBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); UndoRequested?.Invoke(); };
        bottomContent.AddChild(undoBtn);

        Button redoBtn = CreateSmallButton("REDO \u21b7", TextSecondary);
        redoBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); RedoRequested?.Invoke(); };
        bottomContent.AddChild(redoBtn);
    }

    // ========== Helpers ==========
    private void SelectTool(int index)
    {
        _selectedToolIndex = index;
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            bool isEraser = Tools[i].Mode == BuildToolMode.Eraser;
            bool isDoor = Tools[i].Mode == BuildToolMode.Door;
            Color accent = isEraser ? AccentRed : isDoor ? AccentGold : AccentGreen;
            Color bg = i == index ? new Color(accent.R, accent.G, accent.B, 0.15f) : new Color(0, 0, 0, 0);
            _toolButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }

        // Deselect any active blueprint when a regular tool is selected
        _selectedBlueprintIndex = -1;
        for (int i = 0; i < _blueprintButtons.Count; i++)
        {
            _blueprintButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(new Color(0, 0, 0, 0), 0));
        }

        ToolSelected?.Invoke(Tools[index].Mode);
    }

    private void SelectSymmetry(int index)
    {
        for (int i = 0; i < _symmetryButtons.Count; i++)
        {
            _symmetryButtons[i].AddThemeColorOverride("font_color", i == index ? AccentGreen : TextSecondary);
        }
    }

    private void SelectMaterial(int index)
    {
        _selectedMaterialIndex = index;
        for (int i = 0; i < _materialButtons.Count; i++)
        {
            Color bg = i == index ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.15f) : new Color(0, 0, 0, 0);
            _materialButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }
        MaterialSelected?.Invoke(BuildMaterials[index]);
    }

    /// <summary>
    /// Updates the visual selection highlight to the given material type without
    /// firing the MaterialSelected event. Called by GameManager when the material
    /// is changed externally (e.g. via scroll wheel cycling) so the UI stays in sync.
    /// </summary>
    public void SetSelectedMaterialVisual(VoxelMaterialType material)
    {
        for (int i = 0; i < BuildMaterials.Length; i++)
        {
            if (BuildMaterials[i] == material)
            {
                _selectedMaterialIndex = i;
                for (int j = 0; j < _materialButtons.Count; j++)
                {
                    Color bg = j == i ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.15f) : new Color(0, 0, 0, 0);
                    _materialButtons[j].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
                }
                return;
            }
        }
    }

    private void SelectBlueprint(int index)
    {
        _selectedBlueprintIndex = index;

        // Highlight the selected blueprint button, unhighlight others
        for (int i = 0; i < _blueprintButtons.Count; i++)
        {
            Color bg = i == index
                ? new Color(AccentGreen.R, AccentGreen.G, AccentGreen.B, 0.15f)
                : new Color(0, 0, 0, 0);
            _blueprintButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(bg, 0));
        }

        // Unhighlight tool buttons since we're switching to blueprint mode
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            _toolButtons[i].AddThemeStyleboxOverride("panel", CreateFlatStyle(new Color(0, 0, 0, 0), 0));
        }

        BlueprintDefinition[] blueprints = BuildBlueprints.All;
        if (index >= 0 && index < blueprints.Length)
        {
            BlueprintSelected?.Invoke(blueprints[index]);
        }
    }

    private void UpdateTimerDisplay()
    {
        if (_timerLabel == null || _timerPanel == null) return;

        int minutes = (int)(_countdown / 60f);
        int seconds = (int)(_countdown % 60f);
        _timerLabel.Text = $"{minutes}:{seconds:D2}";

        bool urgent = _countdown < 30f && _countdown > 0f;
        if (urgent != _timerUrgent)
        {
            _timerUrgent = urgent;
            _timerLabel.AddThemeColorOverride("font_color", urgent ? AccentRed : TextPrimary);
        }

        // Pulse effect when urgent
        if (urgent)
        {
            float pulse = (Mathf.Sin((float)Time.GetTicksMsec() / 200f) + 1f) / 2f;
            _timerLabel.Modulate = new Color(1, 1, 1, 0.6f + pulse * 0.4f);
        }
        else
        {
            _timerLabel.Modulate = Colors.White;
        }
    }

    private void UpdateBudgetDisplay()
    {
        if (_budgetLabel != null)
        {
            _budgetLabel.Text = $"${_currentBudget:N0}";
        }
    }

    private void UpdateReadyButtonTimer()
    {
        if (_readyBtn == null) return;

        int minutes = (int)(_countdown / 60f);
        int seconds = (int)(_countdown % 60f);
        string timeStr = $"{minutes}:{seconds:D2}";

        _readyBtn.Text = $"READY  ({timeStr})";

        bool urgent = _countdown < 30f && _countdown > 0f;
        if (urgent != _readyTimerUrgent)
        {
            _readyTimerUrgent = urgent;
            if (urgent)
            {
                // Urgent: red-tinted background
                StyleBoxFlat urgentStyle = new StyleBoxFlat();
                urgentStyle.BgColor = new Color(AccentRed.R, AccentRed.G, AccentRed.B, 0.9f);
                urgentStyle.CornerRadiusTopLeft = 0;
                urgentStyle.CornerRadiusTopRight = 0;
                urgentStyle.CornerRadiusBottomLeft = 0;
                urgentStyle.CornerRadiusBottomRight = 0;
                urgentStyle.ContentMarginLeft = 8;
                urgentStyle.ContentMarginRight = 8;
                urgentStyle.ContentMarginTop = 8;
                urgentStyle.ContentMarginBottom = 8;
                _readyBtn.AddThemeStyleboxOverride("normal", urgentStyle);
            }
            else
            {
                // Normal: green background
                StyleBoxFlat normalStyle = new StyleBoxFlat();
                normalStyle.BgColor = AccentGreen;
                normalStyle.CornerRadiusTopLeft = 0;
                normalStyle.CornerRadiusTopRight = 0;
                normalStyle.CornerRadiusBottomLeft = 0;
                normalStyle.CornerRadiusBottomRight = 0;
                normalStyle.ContentMarginLeft = 8;
                normalStyle.ContentMarginRight = 8;
                normalStyle.ContentMarginTop = 8;
                normalStyle.ContentMarginBottom = 8;
                _readyBtn.AddThemeStyleboxOverride("normal", normalStyle);
            }
        }

        // Pulse effect on button when urgent
        if (urgent)
        {
            float pulse = (Mathf.Sin((float)Time.GetTicksMsec() / 200f) + 1f) / 2f;
            _readyBtn.Modulate = new Color(1, 1, 1, 0.7f + pulse * 0.3f);
        }
        else
        {
            _readyBtn.Modulate = Colors.White;
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

    private static Button CreateSmallButton(string text, Color color)
    {
        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 10);
        btn.AddThemeColorOverride("font_color", color);
        btn.AddThemeColorOverride("font_hover_color", new Color("e6edf3"));
        btn.CustomMinimumSize = new Vector2(60, 32);
        btn.MouseFilter = MouseFilterEnum.Stop;
        return btn;
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

    // ========== Tooltip Helpers ==========

    private TooltipSystem? GetTooltipSystem()
    {
        if (_tooltipSystem == null || !GodotObject.IsInstanceValid(_tooltipSystem))
        {
            _tooltipSystem = GetTree().Root.GetNodeOrNull<TooltipSystem>("Main/TooltipSystem");
        }
        return _tooltipSystem;
    }

    private void ShowMaterialTooltip(VoxelMaterialType mat)
    {
        TooltipSystem? ts = GetTooltipSystem();
        if (ts == null) return;

        VoxelMaterialDefinition def = VoxelMaterials.GetDefinition(mat);

        // Build properties list
        List<string> props = new List<string>();
        if (def.IsFlammable) props.Add("Flammable");
        if (def.IsTransparent) props.Add("Transparent");
        if (def.UsesGravity) props.Add("Gravity-affected");
        if (def.ExteriorOnly) props.Add("Exterior only");
        if (def.RicochetChance > 0f) props.Add($"{(int)(def.RicochetChance * 100)}% ricochet chance");

        // Brief material descriptions
        string desc = mat switch
        {
            VoxelMaterialType.Dirt => "Cheap filler -- destroyed easily",
            VoxelMaterialType.Wood => "Light and cheap, but catches fire",
            VoxelMaterialType.Stone => "Solid mid-tier wall material",
            VoxelMaterialType.Brick => "Sturdy masonry, good HP per cost",
            VoxelMaterialType.Concrete => "Heavy-duty structural block",
            VoxelMaterialType.Metal => "Tough plating that deflects shots",
            VoxelMaterialType.ReinforcedSteel => "Premium armor with high ricochet",
            VoxelMaterialType.Glass => "Transparent but fragile",
            VoxelMaterialType.Obsidian => "Ultra-dense -- nearly missile-proof",
            VoxelMaterialType.Sand => "Absorbs blast radius, falls with gravity",
            VoxelMaterialType.Ice => "Translucent and slippery, low HP",
            VoxelMaterialType.ArmorPlate => "Exterior-only shell plating",
            VoxelMaterialType.Leaves => "Decorative camouflage, burns fast",
            VoxelMaterialType.Bark => "Natural cover, flammable but tougher than leaves",
            _ => string.Empty,
        };

        string propsLine = props.Count > 0 ? string.Join(", ", props) : "No special properties";
        string body = $"Cost: ${def.Cost}  |  HP: {def.MaxHitPoints}  |  Weight: {def.Weight:F1}\n{propsLine}\n{desc}";

        ts.ShowTooltip(mat.ToString(), body);
    }

    private void ShowWeaponTooltip(int weaponIndex)
    {
        TooltipSystem? ts = GetTooltipSystem();
        if (ts == null || weaponIndex < 0 || weaponIndex >= WeaponOptions.Length) return;

        var opt = WeaponOptions[weaponIndex];
        string desc;
        int damage;
        string blastInfo;

        switch (opt.Type)
        {
            case WeaponType.Cannon:
                damage = 30;
                blastInfo = "Blast: 4";
                desc = "Standard ballistic cannon with moderate\narc and blast radius";
                break;
            case WeaponType.Mortar:
                damage = 30;
                blastInfo = "Blast: 6";
                desc = "High-arc lobber -- arcs shots over walls\nwith a wide blast";
                break;
            case WeaponType.Drill:
                damage = 50;
                blastInfo = "Penetration: 10 blocks";
                desc = "Bunker buster -- bores through walls and\ndetonates inside fortifications";
                break;
            case WeaponType.Railgun:
                damage = 50;
                blastInfo = "Penetration: 5 blocks";
                desc = "Instant hitscan beam that pierces through\nmultiple blocks and hits commanders";
                break;
            case WeaponType.MissileLauncher:
                damage = 50;
                blastInfo = "Blast: 8";
                desc = "Guided missile with homing and a massive\nexplosion radius";
                break;
            default:
                damage = 0;
                blastInfo = string.Empty;
                desc = string.Empty;
                break;
        }

        string body = $"Cost: ${opt.Cost}  |  Damage: {damage}  |  {blastInfo}\n{desc}\n\nLeft-click to place, right-click to sell";
        ts.ShowTooltip(opt.Name, body);
    }

    private void ShowTroopTooltip(TroopType troopType)
    {
        TooltipSystem? ts = GetTooltipSystem();
        if (ts == null) return;

        TroopStats stats = TroopDefinitions.Get(troopType);

        string desc = troopType switch
        {
            TroopType.Infantry => "Fast foot soldiers that rush the enemy\ncommander through doorways",
            TroopType.Demolisher => "Heavy troops that smash through walls\nand deal double damage",
            _ => string.Empty,
        };

        string wallInfo = stats.CanDamageWalls ? "Can break walls" : "Cannot break walls";
        string body = $"Cost: ${stats.Cost}  |  HP: {stats.MaxHP}  |  Dmg: {stats.AttackDamage}/turn\nSpeed: {stats.MoveStepsPerTick} cells/tick  |  {wallInfo}\n{desc}\n\nLeft-click to buy, right-click to sell";
        ts.ShowTooltip(stats.Name, body);
    }

    private void ShowBlueprintTooltip(int bpIndex)
    {
        TooltipSystem? ts = GetTooltipSystem();
        if (ts == null) return;

        BlueprintDefinition[] bps = BuildBlueprints.All;
        if (bpIndex < 0 || bpIndex >= bps.Length) return;

        BlueprintDefinition bp = bps[bpIndex];

        // Estimate cost based on currently selected material
        VoxelMaterialDefinition matDef = VoxelMaterials.GetDefinition(BuildMaterials[_selectedMaterialIndex]);
        int totalCost = bp.BlockCount * matDef.Cost;

        string desc = bp.Name switch
        {
            "Tower" => "Sturdy defensive tower with open top\nfor weapon placement",
            "Room" => "Enclosed room with a front doorway\nfor troop access",
            "Wall" => "Flat wall segment for quick perimeter\ndefense",
            "Bunker" => "Thick-walled shelter with firing slits\non three sides",
            "Ramp" => "Stepped ramp for reaching elevated\npositions",
            "Sniper Nest" => "Elevated platform with railings for\nlong-range weapon placement",
            _ => bp.Description,
        };

        string body = $"Blocks: {bp.BlockCount}  |  Size: {bp.Size.X}x{bp.Size.Y}x{bp.Size.Z}\nEst. cost: ${totalCost} (with {BuildMaterials[_selectedMaterialIndex]})\n{desc}";
        ts.ShowTooltip(bp.Name, body);
    }

    private void ShowPowerupTooltip(PowerupType pType)
    {
        TooltipSystem? ts = GetTooltipSystem();
        if (ts == null) return;

        PowerupDefinition def = PowerupDefinitions.Get(pType);
        string duration = def.DurationTurns > 0 ? $"Duration: {def.DurationTurns} turns" : "Instant";
        string body = $"Cost: ${def.Cost}  |  {duration}\n{def.Description}\n\nLeft-click to buy, right-click to sell";
        ts.ShowTooltip(def.Name, body);
    }

    private void HideBuildTooltip()
    {
        GetTooltipSystem()?.HideTooltip();
    }

    // ========== Events ==========
    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Building;
    }

    private void OnBudgetChanged(BudgetChangedEvent payload)
    {
        _currentBudget = payload.NewBudget;
    }

    // ========== Sandbox Mode ==========

    /// <summary>
    /// Enables sandbox mode: hides commander/weapon/troop sections,
    /// shows save/load panel with build name input and saved build list.
    /// </summary>
    public void EnableSandboxMode(List<string> savedBuildNames)
    {
        _sandboxMode = true;
        _sandboxBuildNames = savedBuildNames ?? new List<string>();

        // Change ready button text to "EXIT SANDBOX"
        if (_readyBtn != null)
        {
            _readyBtn.Text = "EXIT SANDBOX";
        }

        // Add sandbox panel as a floating overlay
        BuildSandboxPanel();
    }

    private void BuildSandboxPanel()
    {
        // Floating panel on the right side
        PanelContainer sandboxPanel = CreateStyledPanel(PanelBg, 0);
        sandboxPanel.SetAnchorsPreset(LayoutPreset.CenterRight);
        sandboxPanel.OffsetLeft = -240;
        sandboxPanel.OffsetRight = -10;
        sandboxPanel.OffsetTop = -200;
        sandboxPanel.OffsetBottom = 200;
        sandboxPanel.CustomMinimumSize = new Vector2(230, 400);
        sandboxPanel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(sandboxPanel);

        VBoxContainer content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 6);
        sandboxPanel.AddChild(content);

        // Header
        MarginContainer headerMargin = new MarginContainer();
        headerMargin.AddThemeConstantOverride("margin_left", 10);
        headerMargin.AddThemeConstantOverride("margin_top", 10);
        headerMargin.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(headerMargin);

        Label header = new Label();
        header.Text = "SANDBOX";
        header.AddThemeFontOverride("font", PixelFont);
        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", AccentGold);
        header.MouseFilter = MouseFilterEnum.Ignore;
        headerMargin.AddChild(header);

        // Name input
        MarginContainer nameMargin = new MarginContainer();
        nameMargin.AddThemeConstantOverride("margin_left", 10);
        nameMargin.AddThemeConstantOverride("margin_right", 10);
        nameMargin.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(nameMargin);

        _sandboxNameInput = new LineEdit();
        _sandboxNameInput.PlaceholderText = "Build name...";
        _sandboxNameInput.AddThemeFontOverride("font", PixelFont);
        _sandboxNameInput.AddThemeFontSizeOverride("font_size", 10);
        _sandboxNameInput.CustomMinimumSize = new Vector2(0, 32);
        nameMargin.AddChild(_sandboxNameInput);

        // Save button
        MarginContainer btnMargin = new MarginContainer();
        btnMargin.AddThemeConstantOverride("margin_left", 10);
        btnMargin.AddThemeConstantOverride("margin_right", 10);
        btnMargin.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(btnMargin);

        Button saveBtn = new Button();
        saveBtn.Text = "SAVE BUILD";
        saveBtn.AddThemeFontOverride("font", PixelFont);
        saveBtn.AddThemeFontSizeOverride("font_size", 10);
        saveBtn.CustomMinimumSize = new Vector2(0, 32);
        saveBtn.Pressed += () => { AudioDirector.Instance?.PlaySFX("ui_click"); OnSandboxSavePressed(); };
        btnMargin.AddChild(saveBtn);

        // Separator
        ColorRect sep = new ColorRect();
        sep.CustomMinimumSize = new Vector2(0, 1);
        sep.Color = BorderColor;
        sep.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(sep);

        // Saved builds header
        MarginContainer listHeaderMargin = new MarginContainer();
        listHeaderMargin.AddThemeConstantOverride("margin_left", 10);
        listHeaderMargin.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(listHeaderMargin);

        Label listHeader = new Label();
        listHeader.Text = "SAVED BUILDS";
        listHeader.AddThemeFontOverride("font", PixelFont);
        listHeader.AddThemeFontSizeOverride("font_size", 10);
        listHeader.AddThemeColorOverride("font_color", TextSecondary);
        listHeader.MouseFilter = MouseFilterEnum.Ignore;
        listHeaderMargin.AddChild(listHeader);

        // Scrollable build list
        ScrollContainer scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 150);
        scroll.MouseFilter = MouseFilterEnum.Stop;
        content.AddChild(scroll);

        _sandboxBuildList = new VBoxContainer();
        _sandboxBuildList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_sandboxBuildList);

        RefreshSandboxBuildList();
    }

    private void RefreshSandboxBuildList()
    {
        if (_sandboxBuildList == null) return;

        // Clear existing
        foreach (Node child in _sandboxBuildList.GetChildren())
        {
            child.QueueFree();
        }

        if (_sandboxBuildNames.Count == 0)
        {
            MarginContainer emptyMargin = new MarginContainer();
            emptyMargin.AddThemeConstantOverride("margin_left", 10);
            emptyMargin.MouseFilter = MouseFilterEnum.Ignore;
            _sandboxBuildList.AddChild(emptyMargin);

            Label emptyLabel = new Label();
            emptyLabel.Text = "No saved builds";
            emptyLabel.AddThemeFontOverride("font", PixelFont);
            emptyLabel.AddThemeFontSizeOverride("font_size", 8);
            emptyLabel.AddThemeColorOverride("font_color", TextSecondary);
            emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
            emptyMargin.AddChild(emptyLabel);
            return;
        }

        foreach (string buildName in _sandboxBuildNames)
        {
            string capturedName = buildName;
            PanelContainer buildBtn = CreateStyledPanel(new Color(0, 0, 0, 0), 0);
            buildBtn.CustomMinimumSize = new Vector2(0, 28);
            buildBtn.MouseFilter = MouseFilterEnum.Stop;

            MarginContainer buildMargin = new MarginContainer();
            buildMargin.AddThemeConstantOverride("margin_left", 10);
            buildMargin.MouseFilter = MouseFilterEnum.Ignore;
            buildBtn.AddChild(buildMargin);

            Label buildLabel = new Label();
            buildLabel.Text = buildName;
            buildLabel.AddThemeFontOverride("font", PixelFont);
            buildLabel.AddThemeFontSizeOverride("font_size", 9);
            buildLabel.AddThemeColorOverride("font_color", TextPrimary);
            buildLabel.MouseFilter = MouseFilterEnum.Ignore;
            buildMargin.AddChild(buildLabel);

            Button clickArea = new Button();
            clickArea.Flat = true;
            clickArea.MouseFilter = MouseFilterEnum.Stop;
            clickArea.Modulate = new Color(1, 1, 1, 0);
            clickArea.Pressed += () =>
            {
                AudioDirector.Instance?.PlaySFX("ui_click");
                SandboxLoadRequested?.Invoke(capturedName);
                if (_sandboxNameInput != null) _sandboxNameInput.Text = capturedName;
            };
            buildBtn.AddChild(clickArea);
            clickArea.SetAnchorsPreset(LayoutPreset.FullRect);

            _sandboxBuildList.AddChild(buildBtn);
        }
    }

    private void OnSandboxSavePressed()
    {
        string name = _sandboxNameInput?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Build_{_sandboxBuildNames.Count + 1}";
            if (_sandboxNameInput != null) _sandboxNameInput.Text = name;
        }

        SandboxSaveRequested?.Invoke(name);

        // Add to local list if not already there
        if (!_sandboxBuildNames.Contains(name))
        {
            _sandboxBuildNames.Add(name);
        }
        RefreshSandboxBuildList();
    }
}
