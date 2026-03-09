using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Art;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.UI;

/// <summary>
/// Full-screen asset browser that displays all game assets (materials, weapons,
/// commander, structures) in a scrollable grid with live 3D previews.
/// </summary>
public partial class AssetViewer : Control
{
    // --- Theme Colors (matching MainMenu) ---
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

    public event Action? BackRequested;

    private readonly List<AssetTile> _tiles = new List<AssetTile>();
    private readonly List<PanelContainer> _tabButtons = new List<PanelContainer>();
    private readonly List<VoxelAnimator> _troopAnimators = new List<VoxelAnimator>();
    private string _activeCategory = "Materials";
    private ScrollContainer? _scrollContainer;
    private GridContainer? _gridContainer;
    private HBoxContainer? _animButtonRow;
    private float _time;

    private static readonly string[] Categories = { "Materials", "Weapons", "Troops", "Commander", "Structures" };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        try
        {
            BuildUI();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[AssetViewer] _Ready failed: {ex}");
            var errorLabel = new Label();
            errorLabel.Text = $"ERROR: {ex.Message}";
            errorLabel.SetAnchorsPreset(LayoutPreset.Center);
            errorLabel.AddThemeColorOverride("font_color", Colors.Red);
            AddChild(errorLabel);
        }
    }

    private void BuildUI()
    {
        GD.Print("[AssetViewer] BuildUI starting...");

        // Full-screen dark backdrop
        var backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = BgDark;
        AddChild(backdrop);

        // Main vertical layout
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // --- Top bar: Back button + Title + Category tabs ---
        var topBar = new PanelContainer();
        var topBarStyle = CreatePanelStyle(BgPanel, 0);
        topBarStyle.BorderWidthBottom = 3;
        topBarStyle.BorderColor = BorderColor;
        topBarStyle.ContentMarginLeft = 16;
        topBarStyle.ContentMarginRight = 16;
        topBarStyle.ContentMarginTop = 10;
        topBarStyle.ContentMarginBottom = 10;
        topBar.AddThemeStyleboxOverride("panel", topBarStyle);
        vbox.AddChild(topBar);

        var topHBox = new HBoxContainer();
        topHBox.AddThemeConstantOverride("separation", 16);
        topBar.AddChild(topHBox);

        // Back button
        AddStyledButton(topHBox, "< BACK", AccentRed, () => BackRequested?.Invoke());

        // Title
        var titleLabel = new Label();
        titleLabel.Text = "ASSET VIEWER";
        titleLabel.AddThemeFontOverride("font", PixelFont);
        titleLabel.AddThemeFontSizeOverride("font_size", 14);
        titleLabel.AddThemeColorOverride("font_color", AccentGold);
        titleLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        topHBox.AddChild(titleLabel);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topHBox.AddChild(spacer);

        // Category tabs
        foreach (string cat in Categories)
        {
            Color accent = cat switch
            {
                "Materials" => AccentGreen,
                "Commander" => AccentGold,
                "Troops" => new Color("3e96ff"),
                _ => TextSecondary,
            };
            AddTabButton(topHBox, cat, accent);
        }

        // --- Scroll area with grid ---
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(_scrollContainer);

        // Margin inside scroll
        var scrollMargin = new MarginContainer();
        scrollMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scrollMargin.AddThemeConstantOverride("margin_left", 32);
        scrollMargin.AddThemeConstantOverride("margin_right", 32);
        scrollMargin.AddThemeConstantOverride("margin_top", 24);
        scrollMargin.AddThemeConstantOverride("margin_bottom", 24);
        _scrollContainer.AddChild(scrollMargin);

        _gridContainer = new GridContainer();
        _gridContainer.Columns = 4;
        _gridContainer.AddThemeConstantOverride("h_separation", 20);
        _gridContainer.AddThemeConstantOverride("v_separation", 20);
        _gridContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scrollMargin.AddChild(_gridContainer);

        // --- Bottom bar ---
        var bottomBar = new PanelContainer();
        var bottomStyle = CreatePanelStyle(new Color(BgPanel.R, BgPanel.G, BgPanel.B, 0.8f), 0);
        bottomStyle.ContentMarginLeft = 20;
        bottomStyle.ContentMarginRight = 20;
        bottomStyle.ContentMarginTop = 6;
        bottomStyle.ContentMarginBottom = 6;
        bottomBar.AddThemeStyleboxOverride("panel", bottomStyle);
        vbox.AddChild(bottomBar);

        var bottomVBox = new VBoxContainer();
        bottomVBox.AddThemeConstantOverride("separation", 4);
        bottomBar.AddChild(bottomVBox);

        var bottomLabel = new Label();
        bottomLabel.Text = "Click and drag to rotate models  |  Scroll to browse";
        bottomLabel.AddThemeFontOverride("font", PixelFont);
        bottomLabel.AddThemeFontSizeOverride("font_size", 7);
        bottomLabel.AddThemeColorOverride("font_color", TextSecondary);
        bottomLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bottomVBox.AddChild(bottomLabel);

        // Animation control buttons (visible only on Troops tab)
        _animButtonRow = new HBoxContainer();
        _animButtonRow.Alignment = BoxContainer.AlignmentMode.Center;
        _animButtonRow.AddThemeConstantOverride("separation", 8);
        _animButtonRow.Visible = false;
        bottomVBox.AddChild(_animButtonRow);

        string[] animLabels = { "IDLE", "WALK", "ATTACK", "CELEBRATE", "FLINCH", "PANIC" };
        VoxelAnimator.AnimState[] animStates = {
            VoxelAnimator.AnimState.Idle, VoxelAnimator.AnimState.Walk,
            VoxelAnimator.AnimState.Attack, VoxelAnimator.AnimState.Celebrate,
            VoxelAnimator.AnimState.Flinch, VoxelAnimator.AnimState.Panic
        };
        for (int i = 0; i < animLabels.Length; i++)
        {
            var state = animStates[i];
            AddStyledButton(_animButtonRow, animLabels[i], new Color("3e96ff"), () => SetAllTroopAnim(state));
        }

        // Populate default category
        GD.Print("[AssetViewer] About to populate Materials...");
        PopulateCategory("Materials");
        HighlightTab(0);
        GD.Print($"[AssetViewer] BuildUI complete. Tiles: {_tiles.Count}, Size: {Size}");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;

        // Update all tile previews (auto-rotate + drag rotate)
        foreach (var tile in _tiles)
        {
            tile.Update(dt);
        }
    }

    public override void _ExitTree()
    {
        ClearTiles();
    }

    // =====================================================================
    // CATEGORY TABS
    // =====================================================================

    private void AddTabButton(HBoxContainer parent, string text, Color accent)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(120, 36);

        var style = CreatePanelStyle(ButtonNormal, 0);
        style.BorderWidthLeft = 3;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 3;
        style.BorderColor = accent;
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);

        var btn = new Button();
        btn.Text = text.ToUpper();
        btn.Flat = true;
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 8);
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", accent);
        btn.Alignment = HorizontalAlignment.Center;
        btn.MouseFilter = MouseFilterEnum.Stop;

        string category = text;
        btn.Pressed += () => OnCategorySelected(category);

        btn.MouseEntered += () =>
        {
            if (_activeCategory != category)
            {
                var hoverStyle = CreatePanelStyle(ButtonHover, 0);
                hoverStyle.BorderWidthLeft = 3;
                hoverStyle.BorderWidthTop = 2;
                hoverStyle.BorderWidthRight = 2;
                hoverStyle.BorderWidthBottom = 3;
                hoverStyle.BorderColor = accent;
                hoverStyle.ContentMarginLeft = 12;
                hoverStyle.ContentMarginRight = 12;
                hoverStyle.ContentMarginTop = 6;
                hoverStyle.ContentMarginBottom = 6;
                panel.AddThemeStyleboxOverride("panel", hoverStyle);
            }
        };
        btn.MouseExited += () =>
        {
            if (_activeCategory != category)
            {
                panel.AddThemeStyleboxOverride("panel", style);
            }
        };

        panel.AddChild(btn);
        parent.AddChild(panel);
        _tabButtons.Add(panel);
    }

    private void HighlightTab(int index)
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            Color accent = i switch
            {
                0 => AccentGreen,
                1 => TextSecondary,
                2 => new Color("3e96ff"),
                3 => AccentGold,
                4 => TextSecondary,
                _ => TextSecondary,
            };

            bool active = i == index;
            var style = CreatePanelStyle(active ? ButtonHover : ButtonNormal, 0);
            style.BorderWidthLeft = active ? 4 : 3;
            style.BorderWidthTop = active ? 3 : 2;
            style.BorderWidthRight = active ? 3 : 2;
            style.BorderWidthBottom = active ? 4 : 3;
            style.BorderColor = active ? AccentGold : accent;
            style.ContentMarginLeft = 12;
            style.ContentMarginRight = 12;
            style.ContentMarginTop = 6;
            style.ContentMarginBottom = 6;
            _tabButtons[i].AddThemeStyleboxOverride("panel", style);
        }
    }

    private void OnCategorySelected(string category)
    {
        if (_activeCategory == category) return;
        _activeCategory = category;

        int tabIndex = Array.IndexOf(Categories, category);
        HighlightTab(tabIndex);
        PopulateCategory(category);
    }

    // =====================================================================
    // TILE POPULATION
    // =====================================================================

    private void ClearTiles()
    {
        foreach (var tile in _tiles)
        {
            tile.Cleanup();
        }
        _tiles.Clear();
        _troopAnimators.Clear();

        if (_animButtonRow != null) _animButtonRow.Visible = false;

        if (_gridContainer != null)
        {
            foreach (Node child in _gridContainer.GetChildren())
            {
                child.QueueFree();
            }
        }
    }

    private void PopulateCategory(string category)
    {
        ClearTiles();
        if (_gridContainer == null) return;

        switch (category)
        {
            case "Materials":
                PopulateMaterials();
                break;
            case "Weapons":
                PopulateWeapons();
                break;
            case "Troops":
                PopulateTroops();
                break;
            case "Commander":
                PopulateCommander();
                break;
            case "Structures":
                PopulateStructures();
                break;
        }
    }

    private void PopulateMaterials()
    {
        foreach (VoxelMaterialType matType in Enum.GetValues<VoxelMaterialType>())
        {
            if (matType == VoxelMaterialType.Air) continue;

            var def = VoxelMaterials.GetDefinition(matType);
            Color color = VoxelMaterials.GetPreviewColor(matType);
            string name = matType.ToString();
            string info = $"HP: {def.MaxHitPoints}  Cost: {def.Cost}";

            // Build a simple colored cube mesh
            var mesh = CreateColoredCubeMesh(color);

            var tile = CreateTile(name, info, mesh, null);
            _tiles.Add(tile);
        }
    }

    private void PopulateWeapons()
    {
        Color teamColor = new Color("57c84d"); // default green team color
        string[] weaponIds = { "cannon", "mortar", "railgun", "missile", "drill" };
        string[] weaponNames = { "Cannon", "Mortar", "Railgun", "Missile Launcher", "Drill" };

        for (int i = 0; i < weaponIds.Length; i++)
        {
            var result = WeaponModelGenerator.Generate(weaponIds[i], teamColor);
            var mat = VoxelModelBuilder.CreateVoxelMaterial(0.1f, 0.7f);
            var tile = CreateTile(weaponNames[i], weaponIds[i].ToUpper(), result.Mesh, mat);
            _tiles.Add(tile);
        }
    }

    private void PopulateTroops()
    {
        Color teamGreen = GameConfig.PlayerColors[0];
        Color teamRed = GameConfig.PlayerColors[1];

        var troops = new (CharacterDefinition def, string name, string info, float walkSpeed)[]
        {
            (TroopModelGenerator.GenerateCommander(teamGreen), "Commander", "8x14x6 @ 0.08m\nTier: Leader", 1.0f),
            (TroopModelGenerator.GenerateInfantry(teamRed), "Infantry", "6x10x4 @ 0.06m\nTier: Basic", 1.0f),
            (TroopModelGenerator.GenerateDemolisher(teamGreen), "Demolisher", "7x11x5 @ 0.07m\nTier: Heavy", 0.8f),
            (TroopModelGenerator.GenerateScout(teamRed), "Scout", "5x9x4 @ 0.05m\nTier: Fast", 1.6f),
        };

        foreach (var (def, name, info, walkSpeed) in troops)
        {
            var tile = CreateAnimatedTroopTile(def, name, info);
            _tiles.Add(tile);
        }

        // Show animation buttons
        if (_animButtonRow != null) _animButtonRow.Visible = true;
    }

    private AssetTile CreateAnimatedTroopTile(CharacterDefinition def, string name, string info)
    {
        if (_gridContainer == null)
            throw new InvalidOperationException("Grid container not initialized");

        // Outer panel
        var tilePanel = new PanelContainer();
        tilePanel.CustomMinimumSize = new Vector2(200, 280);
        tilePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var tileStyle = CreatePanelStyle(BgPanel, 0);
        tileStyle.BorderWidthLeft = 3;
        tileStyle.BorderWidthTop = 2;
        tileStyle.BorderWidthRight = 2;
        tileStyle.BorderWidthBottom = 3;
        tileStyle.BorderColor = new Color("3e96ff");
        tileStyle.ContentMarginLeft = 6;
        tileStyle.ContentMarginRight = 6;
        tileStyle.ContentMarginTop = 6;
        tileStyle.ContentMarginBottom = 6;
        tilePanel.AddThemeStyleboxOverride("panel", tileStyle);

        var tileVBox = new VBoxContainer();
        tileVBox.AddThemeConstantOverride("separation", 4);
        tilePanel.AddChild(tileVBox);

        // SubViewportContainer for 3D preview
        var viewportContainer = new SubViewportContainer();
        viewportContainer.CustomMinimumSize = new Vector2(188, 210);
        viewportContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        viewportContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        viewportContainer.Stretch = true;
        viewportContainer.MouseFilter = MouseFilterEnum.Stop;
        tileVBox.AddChild(viewportContainer);

        // SubViewport
        var viewport = new SubViewport();
        viewport.Size = new Vector2I(188, 210);
        viewport.TransparentBg = true;
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        viewport.OwnWorld3D = true;
        viewportContainer.AddChild(viewport);

        // 3D scene
        var world = new Node3D();
        viewport.AddChild(world);

        // Camera
        var camera = new Camera3D();
        camera.Fov = 35f;
        camera.Current = true;
        world.AddChild(camera);

        // Lighting
        var dirLight = new DirectionalLight3D();
        dirLight.Rotation = new Vector3(Mathf.DegToRad(-45), Mathf.DegToRad(30), 0);
        dirLight.LightEnergy = 1.2f;
        dirLight.LightColor = new Color(1.0f, 0.95f, 0.85f);
        world.AddChild(dirLight);

        var fillLight = new DirectionalLight3D();
        fillLight.Rotation = new Vector3(Mathf.DegToRad(-30), Mathf.DegToRad(-60), 0);
        fillLight.LightEnergy = 0.4f;
        fillLight.LightColor = new Color(0.6f, 0.7f, 1.0f);
        world.AddChild(fillLight);

        // Build the character with skeleton
        var pivot = new Node3D();
        world.AddChild(pivot);

        Node3D character = VoxelCharacterBuilder.Build(def);
        pivot.AddChild(character);

        // Attach animator
        var animator = new VoxelAnimator();
        animator.Name = $"{name}Animator";
        character.AddChild(animator);
        animator.Initialize(character);
        _troopAnimators.Add(animator);

        // Position camera based on character size
        float charHeight = def.VoxelSize * 14f; // approximate
        camera.Position = new Vector3(0, charHeight * 0.5f, charHeight * 3.5f);
        camera.LookAt(new Vector3(0, charHeight * 0.4f, 0), Vector3.Up);

        // Name label
        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.AddThemeFontOverride("font", PixelFont);
        nameLabel.AddThemeFontSizeOverride("font_size", 8);
        nameLabel.AddThemeColorOverride("font_color", new Color("3e96ff"));
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tileVBox.AddChild(nameLabel);

        // Info label
        var infoLabel = new Label();
        infoLabel.Text = info;
        infoLabel.AddThemeFontOverride("font", PixelFont);
        infoLabel.AddThemeFontSizeOverride("font_size", 6);
        infoLabel.AddThemeColorOverride("font_color", TextSecondary);
        infoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tileVBox.AddChild(infoLabel);

        _gridContainer.AddChild(tilePanel);

        var tile = new AssetTile
        {
            Pivot = pivot,
            ViewportContainer = viewportContainer,
            AutoRotateSpeed = 0.5f,
            RotationY = 0f,
            IsDragging = false,
            DragStartX = 0f,
            DragRotationOffset = 0f,
        };

        viewportContainer.GuiInput += (InputEvent ev) => HandleTileDrag(tile, ev);

        tilePanel.MouseEntered += () =>
        {
            var hoverStyle = CreatePanelStyle(BgPanel, 0);
            hoverStyle.BorderWidthLeft = 4;
            hoverStyle.BorderWidthTop = 3;
            hoverStyle.BorderWidthRight = 3;
            hoverStyle.BorderWidthBottom = 4;
            hoverStyle.BorderColor = AccentGold;
            hoverStyle.ContentMarginLeft = 6;
            hoverStyle.ContentMarginRight = 6;
            hoverStyle.ContentMarginTop = 6;
            hoverStyle.ContentMarginBottom = 6;
            tilePanel.AddThemeStyleboxOverride("panel", hoverStyle);
        };
        tilePanel.MouseExited += () =>
        {
            tilePanel.AddThemeStyleboxOverride("panel", tileStyle);
        };

        return tile;
    }

    private void SetAllTroopAnim(VoxelAnimator.AnimState state)
    {
        foreach (var animator in _troopAnimators)
        {
            float speed = animator.Name.ToString().Contains("Scout") ? 1.6f :
                          animator.Name.ToString().Contains("Demolisher") ? 0.8f : 1.0f;
            animator.SetState(state, speed);
        }
    }

    private void PopulateCommander()
    {
        Color[] teamColors = {
            new Color("57c84d"), // green
            new Color("d74f4f"), // red
            new Color("3e96ff"), // blue
            new Color("8a8a8a"), // gray
        };
        string[] teamNames = { "Green Team", "Red Team", "Blue Team", "Gray Team" };

        for (int i = 0; i < teamColors.Length; i++)
        {
            var parts = CommanderModelGenerator.Generate(teamColors[i]);
            var mat = VoxelModelBuilder.CreateVoxelMaterial(0.0f, 0.8f);
            var tile = CreateTile($"Commander ({teamNames[i]})", "8x16x8 voxels", parts.FullMesh, mat);
            _tiles.Add(tile);
        }
    }

    private void PopulateStructures()
    {
        // Generate example build pieces using VoxelModelBuilder
        var builder = new VoxelModelBuilder
        {
            VoxelSize = 0.15f,
            JitterAmount = 0.0f,
        };

        // Wall segment
        {
            Color?[,,] v = new Color?[6, 6, 2];
            Color brick = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Brick);
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 6; y++)
                    for (int z = 0; z < 2; z++)
                        v[x, y, z] = brick;
            builder.OriginOffset = new Vector3(-6 * 0.5f * 0.15f, 0, -2 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Wall Segment", "Brick 6x6x2", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }

        // Tower column
        {
            Color?[,,] v = new Color?[3, 10, 3];
            Color stone = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Stone);
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 10; y++)
                    for (int z = 0; z < 3; z++)
                        v[x, y, z] = stone;
            builder.OriginOffset = new Vector3(-3 * 0.5f * 0.15f, 0, -3 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Tower Column", "Stone 3x10x3", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }

        // Platform slab
        {
            Color?[,,] v = new Color?[8, 2, 8];
            Color concrete = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Concrete);
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 8; z++)
                        v[x, y, z] = concrete;
            builder.OriginOffset = new Vector3(-8 * 0.5f * 0.15f, 0, -8 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Platform Slab", "Concrete 8x2x8", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }

        // Bunker shell (hollow box)
        {
            Color?[,,] v = new Color?[8, 6, 8];
            Color metal = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Metal);
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 6; y++)
                    for (int z = 0; z < 8; z++)
                    {
                        bool isEdge = x == 0 || x == 7 || y == 0 || y == 5 || z == 0 || z == 7;
                        if (isEdge)
                            v[x, y, z] = metal;
                    }
            builder.OriginOffset = new Vector3(-8 * 0.5f * 0.15f, 0, -8 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Bunker Shell", "Metal 8x6x8 hollow", mesh, VoxelModelBuilder.CreateVoxelMaterial(0.3f, 0.5f));
            _tiles.Add(tile);
        }

        // Reinforced Steel block
        {
            Color?[,,] v = new Color?[4, 4, 4];
            Color steel = VoxelMaterials.GetPreviewColor(VoxelMaterialType.ReinforcedSteel);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    for (int z = 0; z < 4; z++)
                        v[x, y, z] = steel;
            builder.OriginOffset = new Vector3(-4 * 0.5f * 0.15f, 0, -4 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Armor Block", "Reinforced Steel 4x4x4", mesh, VoxelModelBuilder.CreateVoxelMaterial(0.4f, 0.4f));
            _tiles.Add(tile);
        }

        // L-shaped corner wall
        {
            Color?[,,] v = new Color?[6, 5, 6];
            Color wood = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Wood);
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 5; y++)
                {
                    // L-shape: fill z=0..1 full width + x=0..1 full depth
                    for (int z = 0; z < 2; z++)
                        v[x, y, z] = wood;
                    if (x < 2)
                        for (int z = 2; z < 6; z++)
                            v[x, y, z] = wood;
                }
            builder.OriginOffset = new Vector3(-6 * 0.5f * 0.15f, 0, -6 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Corner Wall", "Wood L-shape", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }

        // Window wall
        {
            Color?[,,] v = new Color?[6, 6, 2];
            Color brick = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Brick);
            Color glass = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Glass);
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 6; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        bool isWindow = x >= 2 && x <= 3 && y >= 2 && y <= 3 && z == 0;
                        v[x, y, z] = isWindow ? glass : brick;
                    }
            builder.OriginOffset = new Vector3(-6 * 0.5f * 0.15f, 0, -2 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Window Wall", "Brick + Glass", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }

        // Stairs
        {
            Color?[,,] v = new Color?[4, 6, 6];
            Color stone = VoxelMaterials.GetPreviewColor(VoxelMaterialType.Stone);
            for (int step = 0; step < 6; step++)
            {
                int height = step + 1;
                for (int x = 0; x < 4; x++)
                    for (int y = 0; y < height; y++)
                        v[x, y, step] = stone;
            }
            builder.OriginOffset = new Vector3(-4 * 0.5f * 0.15f, 0, -6 * 0.5f * 0.15f);
            var mesh = builder.BuildMesh(v);
            var tile = CreateTile("Stairs", "Stone staircase", mesh, VoxelModelBuilder.CreateVoxelMaterial());
            _tiles.Add(tile);
        }
    }

    // =====================================================================
    // TILE CREATION
    // =====================================================================

    private AssetTile CreateTile(string name, string info, ArrayMesh mesh, StandardMaterial3D? material)
    {
        if (_gridContainer == null)
            throw new InvalidOperationException("Grid container not initialized");

        // Outer panel for the tile
        var tilePanel = new PanelContainer();
        tilePanel.CustomMinimumSize = new Vector2(200, 240);
        tilePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var tileStyle = CreatePanelStyle(BgPanel, 0);
        tileStyle.BorderWidthLeft = 3;
        tileStyle.BorderWidthTop = 2;
        tileStyle.BorderWidthRight = 2;
        tileStyle.BorderWidthBottom = 3;
        tileStyle.BorderColor = BorderColor;
        tileStyle.ContentMarginLeft = 6;
        tileStyle.ContentMarginRight = 6;
        tileStyle.ContentMarginTop = 6;
        tileStyle.ContentMarginBottom = 6;
        tilePanel.AddThemeStyleboxOverride("panel", tileStyle);

        var tileVBox = new VBoxContainer();
        tileVBox.AddThemeConstantOverride("separation", 4);
        tilePanel.AddChild(tileVBox);

        // SubViewportContainer for 3D preview
        var viewportContainer = new SubViewportContainer();
        viewportContainer.CustomMinimumSize = new Vector2(188, 170);
        viewportContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        viewportContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        viewportContainer.Stretch = true;
        viewportContainer.MouseFilter = MouseFilterEnum.Stop;
        tileVBox.AddChild(viewportContainer);

        // SubViewport
        var viewport = new SubViewport();
        viewport.Size = new Vector2I(188, 170);
        viewport.TransparentBg = true;
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        viewport.OwnWorld3D = true;
        viewportContainer.AddChild(viewport);

        // 3D scene inside viewport
        var world = new Node3D();
        viewport.AddChild(world);

        // Camera
        var camera = new Camera3D();
        camera.Position = new Vector3(0, 0.5f, 2.0f);
        camera.LookAt(new Vector3(0, 0.3f, 0), Vector3.Up);
        camera.Fov = 35f;
        camera.Current = true;
        world.AddChild(camera);

        // Lighting
        var dirLight = new DirectionalLight3D();
        dirLight.Position = new Vector3(2, 3, 2);
        dirLight.LookAt(Vector3.Zero, Vector3.Up);
        dirLight.LightEnergy = 1.2f;
        dirLight.ShadowEnabled = false;
        world.AddChild(dirLight);

        // Ambient fill light from below-left
        var fillLight = new DirectionalLight3D();
        fillLight.Position = new Vector3(-2, -1, 1);
        fillLight.LookAt(Vector3.Zero, Vector3.Up);
        fillLight.LightEnergy = 0.4f;
        fillLight.LightColor = new Color(0.6f, 0.7f, 1.0f);
        fillLight.ShadowEnabled = false;
        world.AddChild(fillLight);

        // Model pivot (for rotation)
        var pivot = new Node3D();
        world.AddChild(pivot);

        // MeshInstance3D
        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = mesh;
        if (material != null)
        {
            meshInstance.MaterialOverride = material;
        }
        pivot.AddChild(meshInstance);

        // Center the model: compute AABB and offset
        var aabb = mesh.GetAabb();
        Vector3 center = aabb.Position + aabb.Size * 0.5f;
        meshInstance.Position = -center;

        // Adjust camera distance based on model size
        float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
        float cameraDistance = Mathf.Max(1.2f, maxDim * 2.5f);
        float cameraHeight = center.Y > 0 ? 0.5f : aabb.Size.Y * 0.5f;
        camera.Position = new Vector3(0, cameraHeight, cameraDistance);
        camera.LookAt(new Vector3(0, cameraHeight * 0.4f, 0), Vector3.Up);

        // Name label
        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.AddThemeFontOverride("font", PixelFont);
        nameLabel.AddThemeFontSizeOverride("font_size", 8);
        nameLabel.AddThemeColorOverride("font_color", AccentGreen);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tileVBox.AddChild(nameLabel);

        // Info label
        var infoLabel = new Label();
        infoLabel.Text = info;
        infoLabel.AddThemeFontOverride("font", PixelFont);
        infoLabel.AddThemeFontSizeOverride("font_size", 6);
        infoLabel.AddThemeColorOverride("font_color", TextSecondary);
        infoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tileVBox.AddChild(infoLabel);

        _gridContainer.AddChild(tilePanel);

        // Create tile tracking struct
        var tile = new AssetTile
        {
            Pivot = pivot,
            ViewportContainer = viewportContainer,
            AutoRotateSpeed = 0.8f,
            RotationY = 0f,
            IsDragging = false,
            DragStartX = 0f,
            DragRotationOffset = 0f,
        };

        // Wire up drag-to-rotate via input on the viewport container
        viewportContainer.GuiInput += (InputEvent ev) => HandleTileDrag(tile, ev);

        // Hover effect on tile panel
        tilePanel.MouseEntered += () =>
        {
            var hoverStyle = CreatePanelStyle(BgPanel, 0);
            hoverStyle.BorderWidthLeft = 4;
            hoverStyle.BorderWidthTop = 3;
            hoverStyle.BorderWidthRight = 3;
            hoverStyle.BorderWidthBottom = 4;
            hoverStyle.BorderColor = AccentGreen;
            hoverStyle.ContentMarginLeft = 6;
            hoverStyle.ContentMarginRight = 6;
            hoverStyle.ContentMarginTop = 6;
            hoverStyle.ContentMarginBottom = 6;
            tilePanel.AddThemeStyleboxOverride("panel", hoverStyle);
        };
        tilePanel.MouseExited += () =>
        {
            tilePanel.AddThemeStyleboxOverride("panel", tileStyle);
        };

        return tile;
    }

    // =====================================================================
    // DRAG-TO-ROTATE
    // =====================================================================

    private static void HandleTileDrag(AssetTile tile, InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    tile.IsDragging = true;
                    tile.DragStartX = mb.Position.X;
                    tile.DragRotationOffset = tile.RotationY;
                }
                else
                {
                    tile.IsDragging = false;
                }
            }
        }
        else if (ev is InputEventMouseMotion mm && tile.IsDragging)
        {
            float dx = mm.Position.X - tile.DragStartX;
            tile.RotationY = tile.DragRotationOffset + dx * 0.02f;
        }
    }

    // =====================================================================
    // MESH HELPERS
    // =====================================================================

    /// <summary>
    /// Creates a simple colored cube mesh using VoxelModelBuilder (a single solid voxel).
    /// </summary>
    private static ArrayMesh CreateColoredCubeMesh(Color color)
    {
        // Use VoxelModelBuilder to create a single-voxel cube for consistency
        Color?[,,] voxels = new Color?[3, 3, 3];
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    voxels[x, y, z] = color;

        var builder = new VoxelModelBuilder
        {
            VoxelSize = 0.15f,
            OriginOffset = new Vector3(-3 * 0.5f * 0.15f, -3 * 0.5f * 0.15f, -3 * 0.5f * 0.15f),
        };
        return builder.BuildMesh(voxels);
    }

    // =====================================================================
    // BUTTON HELPERS (matching MainMenu style)
    // =====================================================================

    private void AddStyledButton(HBoxContainer parent, string text, Color accent, Action handler)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(100, 36);

        var style = CreatePanelStyle(ButtonNormal, 0);
        style.BorderWidthLeft = 3;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 3;
        style.BorderColor = accent;
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);

        var btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 8);
        btn.AddThemeColorOverride("font_color", TextPrimary);
        btn.AddThemeColorOverride("font_hover_color", accent);
        btn.Alignment = HorizontalAlignment.Center;
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += handler;

        btn.MouseEntered += () =>
        {
            var hoverStyle = CreatePanelStyle(ButtonHover, 0);
            hoverStyle.BorderWidthLeft = 4;
            hoverStyle.BorderWidthTop = 3;
            hoverStyle.BorderWidthRight = 3;
            hoverStyle.BorderWidthBottom = 4;
            hoverStyle.BorderColor = accent;
            hoverStyle.ContentMarginLeft = 12;
            hoverStyle.ContentMarginRight = 12;
            hoverStyle.ContentMarginTop = 6;
            hoverStyle.ContentMarginBottom = 6;
            panel.AddThemeStyleboxOverride("panel", hoverStyle);
        };
        btn.MouseExited += () =>
        {
            panel.AddThemeStyleboxOverride("panel", style);
        };

        panel.AddChild(btn);
        parent.AddChild(panel);
    }

    private static StyleBoxFlat CreatePanelStyle(Color bgColor, int cornerRadius)
    {
        var style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.CornerRadiusTopLeft = cornerRadius;
        style.CornerRadiusTopRight = cornerRadius;
        style.CornerRadiusBottomLeft = cornerRadius;
        style.CornerRadiusBottomRight = cornerRadius;
        return style;
    }

    // =====================================================================
    // ASSET TILE TRACKING
    // =====================================================================

    private class AssetTile
    {
        public Node3D? Pivot;
        public SubViewportContainer? ViewportContainer;
        public float AutoRotateSpeed;
        public float RotationY;
        public bool IsDragging;
        public float DragStartX;
        public float DragRotationOffset;

        public void Update(float delta)
        {
            if (Pivot == null) return;

            if (!IsDragging)
            {
                RotationY += AutoRotateSpeed * delta;
            }

            Pivot.Rotation = new Vector3(0, RotationY, 0);
        }

        public void Cleanup()
        {
            // SubViewport cleanup is handled by Godot's tree when QueueFree is called
            // on the parent grid container's children
        }
    }
}
