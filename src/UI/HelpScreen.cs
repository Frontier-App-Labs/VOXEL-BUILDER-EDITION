using Godot;
using System;

namespace VoxelSiege.UI;

/// <summary>
/// Full-screen scrollable help overlay accessible from the main menu and pause menu.
/// Covers game overview, controls, materials, weapons, troops, powerups, and strategy tips.
/// ProcessMode = Always so it works while the game tree is paused (pause menu context).
/// </summary>
public partial class HelpScreen : Control
{
    // --- Theme Colors (matching MainMenu / PauseMenu) ---
    private static readonly Color BgDark = new Color("0d1117");
    private static readonly Color BgPanel = new Color("161b22");
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentGold = new Color("d4a029");
    private static readonly Color AccentRed = new Color("d73a49");
    private static readonly Color AccentCyan = new Color("3e96ff");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color BorderColor = new Color("30363d");
    private static readonly Color OverlayColor = new Color(0, 0, 0, 0.85f);
    private static readonly Color TableHeaderBg = new Color(0.08f, 0.12f, 0.18f, 0.9f);
    private static readonly Color TableRowBg = new Color(0.06f, 0.08f, 0.11f, 0.7f);
    private static readonly Color TableRowAltBg = new Color(0.07f, 0.10f, 0.14f, 0.7f);

    // --- Pixel Font (lazy) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    /// <summary>Emitted when the help screen is closed.</summary>
    [Signal]
    public delegate void HelpClosedEventHandler();

    private Control? _contentContainer;
    private MarginContainer? _scrollMargin;

    /// <summary>Maximum width for the content column (paragraphs, tables, etc.).</summary>
    private const float MaxContentWidth = 900f;
    /// <summary>Minimum side margin inside the scroll area.</summary>
    private const int MinSideMargin = 72;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildUI();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Brute-force centering (matching MainMenu / PauseMenu approach)
        if (_contentContainer != null)
        {
            Vector2 viewSize = GetViewportRect().Size;
            float contentW = viewSize.X * 0.85f;
            float contentH = viewSize.Y * 0.9f;
            _contentContainer.Position = new Vector2((viewSize.X - contentW) * 0.5f, (viewSize.Y - contentH) * 0.5f);
            _contentContainer.Size = new Vector2(contentW, contentH);

            // Dynamically widen side margins so the text column never exceeds MaxContentWidth.
            // This keeps content centered and readable on ultrawide / large monitors.
            if (_scrollMargin != null)
            {
                float availableW = contentW;
                int sideMargin = MinSideMargin;
                float innerW = availableW - sideMargin * 2;
                if (innerW > MaxContentWidth)
                {
                    sideMargin = (int)((availableW - MaxContentWidth) * 0.5f);
                }
                _scrollMargin.AddThemeConstantOverride("margin_left", sideMargin);
                _scrollMargin.AddThemeConstantOverride("margin_right", sideMargin);
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open()
    {
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        EmitSignal(SignalName.HelpClosed);
    }

    // =====================================================================
    //  UI CONSTRUCTION
    // =====================================================================

    private void BuildUI()
    {
        // Dark overlay backdrop
        ColorRect backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = OverlayColor;
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(backdrop);

        // Main panel container (positioned in _Process)
        PanelContainer mainPanel = new PanelContainer();
        mainPanel.MouseFilter = MouseFilterEnum.Stop;
        StyleBoxFlat panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = BgPanel;
        panelStyle.BorderWidthLeft = 4;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderWidthBottom = 4;
        panelStyle.BorderColor = AccentGreen;
        panelStyle.CornerRadiusTopLeft = 0;
        panelStyle.CornerRadiusTopRight = 0;
        panelStyle.CornerRadiusBottomLeft = 0;
        panelStyle.CornerRadiusBottomRight = 0;
        mainPanel.AddThemeStyleboxOverride("panel", panelStyle);
        _contentContainer = mainPanel;
        AddChild(mainPanel);

        // Vertical layout: title bar + scroll content
        VBoxContainer outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 0);
        outerVBox.MouseFilter = MouseFilterEnum.Ignore;
        mainPanel.AddChild(outerVBox);

        // --- Title bar ---
        BuildTitleBar(outerVBox);

        // --- Scrollable content ---
        ScrollContainer scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        scroll.ProcessMode = ProcessModeEnum.Always;
        outerVBox.AddChild(scroll);

        // Scroll margin container -- margins are set dynamically in _Process
        // to center content with a max width of ~900px
        _scrollMargin = new MarginContainer();
        _scrollMargin.AddThemeConstantOverride("margin_left", 72);
        _scrollMargin.AddThemeConstantOverride("margin_right", 72);
        _scrollMargin.AddThemeConstantOverride("margin_top", 20);
        _scrollMargin.AddThemeConstantOverride("margin_bottom", 28);
        _scrollMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollMargin.MouseFilter = MouseFilterEnum.Ignore;
        scroll.AddChild(_scrollMargin);

        VBoxContainer content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.MouseFilter = MouseFilterEnum.Ignore;
        _scrollMargin.AddChild(content);

        // --- Build all help sections ---
        BuildGameOverview(content);
        AddSectionSpacer(content);
        BuildGameFlow(content);
        AddSectionSpacer(content);
        BuildControls(content);
        AddSectionSpacer(content);
        BuildBuilding(content);
        AddSectionSpacer(content);
        BuildMaterials(content);
        AddSectionSpacer(content);
        BuildWeapons(content);
        AddSectionSpacer(content);
        BuildTroops(content);
        AddSectionSpacer(content);
        BuildPowerups(content);
        AddSectionSpacer(content);
        BuildCommander(content);
        AddSectionSpacer(content);
        BuildTipsAndStrategy(content);

        // Bottom padding
        Control bottomPad = new Control();
        bottomPad.CustomMinimumSize = new Vector2(0, 24);
        bottomPad.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(bottomPad);
    }

    private void BuildTitleBar(VBoxContainer parent)
    {
        // Title bar background
        PanelContainer titleBar = new PanelContainer();
        titleBar.CustomMinimumSize = new Vector2(0, 52);
        StyleBoxFlat titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = BgDark;
        titleStyle.BorderWidthBottom = 2;
        titleStyle.BorderColor = AccentGreen;
        titleBar.AddThemeStyleboxOverride("panel", titleStyle);
        titleBar.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(titleBar);

        MarginContainer titleMargin = new MarginContainer();
        titleMargin.AddThemeConstantOverride("margin_left", 24);
        titleMargin.AddThemeConstantOverride("margin_right", 16);
        titleMargin.AddThemeConstantOverride("margin_top", 8);
        titleMargin.AddThemeConstantOverride("margin_bottom", 8);
        titleMargin.MouseFilter = MouseFilterEnum.Ignore;
        titleBar.AddChild(titleMargin);

        HBoxContainer titleRow = new HBoxContainer();
        titleRow.MouseFilter = MouseFilterEnum.Ignore;
        titleMargin.AddChild(titleRow);

        Label titleLabel = new Label();
        titleLabel.Text = "HELP & GUIDE";
        titleLabel.AddThemeFontOverride("font", PixelFont);
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", AccentGold);
        titleLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        titleRow.AddChild(titleLabel);

        // Spacer
        Control spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        titleRow.AddChild(spacer);

        // Close button
        Button closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(40, 36);
        closeBtn.AddThemeFontOverride("font", PixelFont);
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        closeBtn.AddThemeColorOverride("font_color", TextPrimary);
        closeBtn.AddThemeColorOverride("font_hover_color", AccentRed);
        closeBtn.AddThemeColorOverride("font_pressed_color", AccentRed);
        closeBtn.Flat = true;
        closeBtn.MouseFilter = MouseFilterEnum.Stop;
        closeBtn.ProcessMode = ProcessModeEnum.Always;
        closeBtn.Pressed += Close;
        titleRow.AddChild(closeBtn);
    }

    // =====================================================================
    //  SECTIONS
    // =====================================================================

    private void BuildGameOverview(VBoxContainer parent)
    {
        AddSectionHeader(parent, "GAME OVERVIEW");
        AddParagraph(parent,
            "VoxelSiege is a turn-based voxel artillery game for 2-4 players. " +
            "Build a fortress from voxel blocks, place weapons on your structure, " +
            "recruit troops, and then battle it out in explosive combat rounds!");
        AddParagraph(parent,
            "Your goal: destroy the enemy commander hidden inside their base. " +
            "Each player has a commander -- a small voxel character placed inside " +
            "the fortress during the build phase. If your commander dies, you lose.");
        AddParagraph(parent,
            "Play against AI bots (1-3 opponents) or online with friends. " +
            "There is also a Sandbox mode for practicing builds without combat pressure.");
    }

    private void BuildGameFlow(VBoxContainer parent)
    {
        AddSectionHeader(parent, "GAME FLOW");

        AddSubHeader(parent, "1. BUILD PHASE");
        AddParagraph(parent,
            "You have 5 minutes and a budget of $15,000 to construct your fortress. " +
            "Place blocks of various materials, mount weapons on your structure, " +
            "and place troops inside your base. Your commander is automatically placed inside your " +
            "build zone -- surround them with strong materials to protect them!");
        AddBullet(parent, "Click blocks to place them within your build zone");
        AddBullet(parent, "Use the material palette to pick block types");
        AddBullet(parent, "Place weapons on the surface of your structure");
        AddBullet(parent, "Use symmetry tools for efficient building");
        AddBullet(parent, "Load blueprints for pre-designed structures");

        AddSubHeader(parent, "2. COMBAT PHASE");
        AddParagraph(parent,
            "Players take turns firing weapons at enemy fortresses. " +
            "On your turn you can move troops (free action) and then fire one weapon. " +
            "Watch as projectiles arc through the sky, smash into voxel walls, and " +
            "create spectacular destruction!");
        AddBullet(parent, "Each turn has a 60-second time limit");
        AddBullet(parent, "You may switch weapons up to 4 times per turn");
        AddBullet(parent, "Use powerups for tactical advantages");
        AddBullet(parent, "Troops move and fight automatically between turns");

        AddSubHeader(parent, "3. VICTORY");
        AddParagraph(parent,
            "The game ends when only one commander remains alive. " +
            "Destroy enemy blocks to expose their commander, then land " +
            "a direct hit for massive bonus damage!");
    }

    private void BuildControls(VBoxContainer parent)
    {
        AddSectionHeader(parent, "CONTROLS");

        // Build phase controls
        AddSubHeader(parent, "BUILD PHASE");
        string[][] buildControls = new string[][]
        {
            new[] { "WASD", "Move camera" },
            new[] { "Mouse", "Look around" },
            new[] { "Left Click", "Place block" },
            new[] { "Right Click", "Remove block" },
            new[] { "Scroll Wheel", "Zoom in/out" },
            new[] { "R", "Rotate block" },
        };
        AddKeyValueTable(parent, buildControls);

        AddSubHeader(parent, "COMBAT PHASE");
        string[][] combatControls = new string[][]
        {
            new[] { "WASD", "Move camera" },
            new[] { "Mouse", "Look around / aim" },
            new[] { "Left Click", "Fire weapon at target" },
            new[] { "Tab", "Cycle to next weapon" },
            new[] { "V", "Toggle spectator view" },
            new[] { "Scroll Wheel", "Zoom in/out" },
        };
        AddKeyValueTable(parent, combatControls);

        AddSubHeader(parent, "GENERAL");
        string[][] generalControls = new string[][]
        {
            new[] { "Escape", "Pause menu" },
        };
        AddKeyValueTable(parent, generalControls);
    }

    private void BuildBuilding(VBoxContainer parent)
    {
        AddSectionHeader(parent, "BUILDING");
        AddParagraph(parent,
            "During the build phase, you construct your fortress within your " +
            "designated build zone. The zone is a 32x24x32 area (or 24x24x24 in " +
            "4-player mode). You cannot build outside your zone.");

        AddSubHeader(parent, "BUILDING TOOLS");
        AddBullet(parent, "Single Block: place one 2x2x2 block at a time");
        AddBullet(parent, "Half Block: place one microvoxel at a time (fine detail)");
        AddBullet(parent, "Box Tool: drag to fill a rectangular area");
        AddBullet(parent, "Hollow Box: create walls without filling the interior");
        AddBullet(parent, "Symmetry: mirror your builds (X-axis, Z-axis, or both)");
        AddBullet(parent, "Undo/Redo: fix mistakes (up to 100 actions)");

        AddSubHeader(parent, "BLUEPRINTS");
        AddParagraph(parent,
            "Save your fortress designs as blueprints and load them in future " +
            "matches. Up to 20 blueprint slots are available. In Sandbox mode, " +
            "you can practice builds freely without combat pressure.");

        AddSubHeader(parent, "TIPS");
        AddBullet(parent, "Your commander needs at least 6 blocks around them");
        AddBullet(parent, "Weapons must be at least 2 blocks away from your commander");
        AddBullet(parent, "Weapons can be placed up to 60 build units from zone center");
        AddBullet(parent, "Place doors (max 4) for troop movement paths");
        AddBullet(parent, "Right-click a weapon button in the build UI to sell the last placed weapon of that type");
    }

    private void BuildMaterials(VBoxContainer parent)
    {
        AddSectionHeader(parent, "MATERIALS");
        AddParagraph(parent,
            "Each material has different cost, durability (HP), and special properties. " +
            "Choose wisely based on your budget and strategy.");

        // Table header
        string[][] materialData = new string[][]
        {
            new[] { "MATERIAL", "COST", "HP", "PROPERTIES" },
            new[] { "Dirt", "$10", "1", "Cheapest filler. Very fragile." },
            new[] { "Sand", "$10", "2", "Gravity-affected. Absorbs blast radius (good shock absorber)." },
            new[] { "Wood", "$15", "3", "Cheap and light. Flammable -- fire spreads through it!" },
            new[] { "Stone", "$20", "6", "Solid all-rounder. Good value for the cost." },
            new[] { "Brick", "$25", "9", "Strong and affordable. Great for walls." },
            new[] { "Concrete", "$30", "13", "Very durable. Excellent for structural cores." },
            new[] { "Metal", "$35", "18", "High HP with 12% ricochet chance. Premium armor." },
            new[] { "Armor Plate", "$55", "23", "Exterior-only. 8% ricochet chance. Top-tier shell." },
            new[] { "Reinforced Steel", "$65", "22", "10% ricochet chance. Extremely tough." },
            new[] { "Obsidian", "$80", "25", "Strongest material! 5% ricochet. Max 20 per player." },
            new[] { "Glass", "$12", "1", "Transparent. Very fragile, but lets you see through." },
            new[] { "Ice", "$12", "3", "Transparent. Cheap filler with decent HP." },
            new[] { "Bark", "$15", "5", "Decorative. Flammable." },
            new[] { "Leaves", "$10", "1", "Decorative. Flammable. Very weak." },
        };
        AddDataTable(parent, materialData);

        AddSubHeader(parent, "SPECIAL PROPERTIES");
        AddBullet(parent, "Ricochet: projectiles may bounce off, dealing no damage");
        AddBullet(parent, "Flammable: fire spreads to nearby flammable blocks every 0.6s");
        AddBullet(parent, "Transparent: does not block line-of-sight (Glass, Ice)");
        AddBullet(parent, "Gravity: Sand falls if unsupported");
        AddBullet(parent, "Exterior-Only: Armor Plate can only be placed on outer surfaces");
        AddBullet(parent, "Foundation: indestructible bedrock (120 HP, free, not player-placed)");
    }

    private void BuildWeapons(VBoxContainer parent)
    {
        AddSectionHeader(parent, "WEAPONS");
        AddParagraph(parent,
            "Weapons are your primary offensive tools. Place them on your fortress " +
            "during the build phase and fire them during combat. Each weapon has " +
            "unique characteristics -- mix and match for maximum effectiveness.");

        // Weapons table
        string[][] weaponData = new string[][]
        {
            new[] { "WEAPON", "COST", "DMG", "BLAST", "SPECIAL" },
            new[] { "Cannon", "$500", "30", "4", "Ballistic arc. Bread-and-butter weapon. Fast, reliable." },
            new[] { "Mortar", "$600", "30", "6", "High arc -- lobs shells OVER walls. Larger blast radius." },
            new[] { "Drill", "$550", "70", "4", "Bunker buster! Bores through 5 blocks, then detonates inside." },
            new[] { "Railgun", "$800", "50", "-", "Hitscan beam. Pierces 5 blocks. 3-shot kill on commanders." },
            new[] { "Missile", "$850", "50", "8", "Guided homing missile. Huge blast radius. Slow but devastating." },
        };
        AddDataTable(parent, weaponData);

        AddSubHeader(parent, "WEAPON DETAILS");

        AddSubHeader(parent, "Cannon", 11, AccentGreen);
        AddParagraph(parent,
            "The standard weapon. Fires cannonballs in a ballistic arc. " +
            "Cheap, no cooldown, and deals solid damage with a moderate blast radius of 4. " +
            "Great for chipping away at walls. Speed: 28.");

        AddSubHeader(parent, "Mortar", 11, AccentGreen);
        AddParagraph(parent,
            "Launches shells in a steep parabola, allowing you to lob shots over tall " +
            "walls and hit the interior of enemy bases. Larger blast radius (6) makes it " +
            "excellent for area damage. Speed: 30.");

        AddSubHeader(parent, "Drill (Bunker Buster)", 11, AccentGreen);
        AddParagraph(parent,
            "The anti-fortress specialist. Fires a drill bit that bores a 3x3 tunnel " +
            "through up to 5 solid blocks, then detonates with a blast radius of 4 inside " +
            "the enemy structure. Higher damage (70) cracks tough materials. Foundation blocks stop it. " +
            "No gravity -- flies straight. Speed: 14.");

        AddSubHeader(parent, "Railgun", 11, AccentCyan);
        AddParagraph(parent,
            "Fires an instant hitscan beam that penetrates up to 5 blocks. The first block " +
            "hit is always destroyed. Deeper blocks take reduced damage. Deals 6 damage to commanders " +
            "(3-shot kill). Foundation blocks are railgun-proof. No blast radius. " +
            "Range: 96 microvoxels. Base damage: 50 (voxels), 25 (weapons), 6 (commanders).");

        AddSubHeader(parent, "Missile Launcher", 11, AccentRed);
        AddParagraph(parent,
            "Fires a guided missile that gently homes toward the target. Reduced gravity " +
            "keeps it airborne longer. The largest blast radius in the game (8) creates " +
            "massive craters. Slower projectile speed (20) but devastating impact.");

        AddSubHeader(parent, "WEAPON DURABILITY");
        AddParagraph(parent,
            "Weapons have 200 HP and can be damaged or destroyed by enemy fire. " +
            "If the blocks beneath a weapon are destroyed, it loses structural " +
            "support and is instantly destroyed. Protect your weapons!");
    }

    private void BuildTroops(VBoxContainer parent)
    {
        AddSectionHeader(parent, "TROOPS");
        AddParagraph(parent,
            "Recruit and PLACE troops during the build phase. Click a troop type, then " +
            "click inside your base to position them. Troops deploy at their placed positions " +
            "when combat starts and move automatically, pathfinding through doors and " +
            "gaps to reach enemy commanders. Max 10 troops per player. Right-click to sell.");

        string[][] troopData = new string[][]
        {
            new[] { "TROOP", "COST", "HP", "MOVE", "ATK", "SPECIAL" },
            new[] { "Infantry", "$50", "3", "5 steps", "1", "Cheap scouts. Attack commanders when adjacent." },
            new[] { "Demolisher", "$100", "5", "4 steps", "2", "Can damage walls! Tougher and hits harder." },
        };
        AddDataTable(parent, troopData);

        AddSubHeader(parent, "TROOP MECHANICS");
        AddBullet(parent, "Troops move automatically along pathfinding routes");
        AddBullet(parent, "Infantry attacks commanders only (1 damage per turn)");
        AddBullet(parent, "Demolishers can damage both walls AND commanders (2 damage per turn)");
        AddBullet(parent, "Troops have a lifespan of 6 ticks before they expire");
        AddBullet(parent, "Troops have a max total damage cap (Infantry: 30, Demolisher: 50)");
        AddBullet(parent, "Place doors in your fortress to create troop pathways");
        AddBullet(parent, "Max 4 doors per player");
        AddBullet(parent, "Attack range: 2 microvoxels (melee only)");
    }

    private void BuildPowerups(VBoxContainer parent)
    {
        AddSectionHeader(parent, "POWERUPS");
        AddParagraph(parent,
            "Powerups provide tactical advantages during combat. Purchase them " +
            "with your budget and activate them on your turn for powerful effects.");

        string[][] powerupData = new string[][]
        {
            new[] { "POWERUP", "COST", "DUR.", "EFFECT" },
            new[] { "Smoke Screen", "$300", "1 rotation", "Makes your fortress invisible. Enemies fire blind. Debris from hits still visible." },
            new[] { "Medkit", "$400", "Instant", "Heals your commander to full HP." },
            new[] { "Shield Gen.", "$600", "1 rotation", "50% damage reduction to your entire fortress and commander." },
            new[] { "EMP Blast", "$700", "2 turns", "1/3 chance per enemy weapon disabled (minimum 1). Disabled for 2 turns." },
            new[] { "Airstrike", "$800", "Instant", "3 bombardment shells on an 8x8 area of enemy fortress." },
        };
        AddDataTable(parent, powerupData);

        AddParagraph(parent,
            "Each powerup type can be used a maximum of 5 times per match.");
    }

    private void BuildCommander(VBoxContainer parent)
    {
        AddSectionHeader(parent, "YOUR COMMANDER");
        AddParagraph(parent,
            "Your commander is your life -- if they die, you lose the game! " +
            "The commander is a small voxel character automatically placed inside " +
            "your build zone. Protect them at all costs.");

        string[][] commanderStats = new string[][]
        {
            new[] { "STAT", "VALUE" },
            new[] { "Hit Points", "15 HP" },
            new[] { "Direct Hit Bonus", "2.5x damage when hit by a projectile directly" },
            new[] { "Exposed Penalty", "1.5x damage when walls around commander are breached" },
            new[] { "Fall Damage", "10 HP per meter fallen (above 2m threshold)" },
            new[] { "Void Kill", "Instant death if falling below Y = -10" },
        };
        AddDataTable(parent, commanderStats);

        AddSubHeader(parent, "PROTECTION STRATEGY");
        AddBullet(parent, "Surround with at least 6 blocks on all sides (required)");
        AddBullet(parent, "Use the toughest materials you can afford around the commander");
        AddBullet(parent, "Place them deep inside the structure, not near edges");
        AddBullet(parent, "Multiple layers of mixed materials are harder to breach");
        AddBullet(parent, "If exposed (adjacent air blocks), they take 1.5x damage from all sources");
        AddBullet(parent, "Explosions landing nearby cause 5 seconds of panic animation");
    }

    private void BuildTipsAndStrategy(VBoxContainer parent)
    {
        AddSectionHeader(parent, "TIPS & STRATEGY");

        AddSubHeader(parent, "BUILDING");
        AddBullet(parent, "Layer materials: cheap Dirt/Sand inside, Concrete/Metal shell outside");
        AddBullet(parent, "Obsidian is the strongest (25 HP) but limited to 20 blocks -- use them around your commander");
        AddBullet(parent, "Sand absorbs blast radius -- use it as a shock absorber layer");
        AddBullet(parent, "Avoid Wood/Bark/Leaves near your commander -- they catch fire!");
        AddBullet(parent, "Use symmetry mode for efficient, balanced fortress construction");
        AddBullet(parent, "Leave internal corridors for troop movement via doors");

        AddSubHeader(parent, "WEAPONS");
        AddBullet(parent, "Place weapons HIGH on your structure for better firing angles");
        AddBullet(parent, "Mix weapon types: Cannons for precision, Mortars for area damage");
        AddBullet(parent, "Drills crack tough materials -- shorter bore but higher damage per block");
        AddBullet(parent, "Railguns can snipe commanders through walls -- aim carefully!");
        AddBullet(parent, "Missiles have the biggest blast (8 radius) -- use them to open up enemy bases");
        AddBullet(parent, "Protect weapon foundations -- if the floor is destroyed, the weapon falls!");

        AddSubHeader(parent, "COMBAT");
        AddBullet(parent, "Target weak spots: look for thin walls, exposed areas, or damaged sections");
        AddBullet(parent, "Smoke Screen makes your fortress invisible -- enemies fire blind");
        AddBullet(parent, "Shield Generator halves all damage to your entire fortress for a full rotation");
        AddBullet(parent, "EMP Blast has 1/3 chance per enemy weapon (min 1 disabled) for 2 turns");
        AddBullet(parent, "Medkit heals your commander to full HP -- save it for critical moments");
        AddBullet(parent, "Airstrike is expensive but deals massive area damage");

        AddSubHeader(parent, "ECONOMY");
        AddBullet(parent, "Starting budget: $15,000 (bots get $8k/$18k/$35k for Easy/Medium/Hard)");
        AddBullet(parent, "You earn currency by destroying enemy-built voxels during combat");
        AddBullet(parent, "Earned currency carries over to future matches via your wallet");
        AddBullet(parent, "Stronger materials earn more when destroyed (Obsidian: $18, Steel: $20)");
        AddBullet(parent, "Watch your budget! Expensive materials run out fast");

        AddSubHeader(parent, "GENERAL");
        AddBullet(parent, "4-player matches use smaller 24x24x24 build zones in 4 quadrants");
        AddBullet(parent, "Press V to toggle spectator view during combat -- see the whole battlefield");
        AddBullet(parent, "Fire spreads to flammable blocks every 0.6 seconds in a 3-block radius");
        AddBullet(parent, "Destroyed structures physically collapse with Teardown-style destruction");
    }

    // =====================================================================
    //  UI HELPER METHODS
    // =====================================================================

    private static void AddSectionHeader(VBoxContainer parent, string text)
    {
        // Accent bar above header
        HBoxContainer barWrapper = new HBoxContainer();
        barWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        barWrapper.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(barWrapper);

        ColorRect bar = new ColorRect();
        bar.CustomMinimumSize = new Vector2(0, 3);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bar.Color = AccentGreen;
        bar.MouseFilter = MouseFilterEnum.Ignore;
        barWrapper.AddChild(bar);

        // Header label
        Label header = new Label();
        header.Text = text;
        header.AddThemeFontOverride("font", PixelFont);
        header.AddThemeFontSizeOverride("font_size", 16);
        header.AddThemeColorOverride("font_color", AccentGold);
        header.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(header);

        // Small spacer after header
        Control spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 4);
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(spacer);
    }

    private static void AddSubHeader(VBoxContainer parent, string text, int fontSize = 12, Color? color = null)
    {
        Control spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 6);
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(spacer);

        Label label = new Label();
        label.Text = text;
        label.AddThemeFontOverride("font", PixelFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? AccentGreen);
        label.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(label);
    }

    private static void AddParagraph(VBoxContainer parent, string text)
    {
        Label label = new Label();
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontOverride("font", PixelFont);
        label.AddThemeFontSizeOverride("font_size", 9);
        label.AddThemeColorOverride("font_color", TextPrimary);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(label);
    }

    private static void AddBullet(VBoxContainer parent, string text)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(row);

        Label bullet = new Label();
        bullet.Text = ">";
        bullet.AddThemeFontOverride("font", PixelFont);
        bullet.AddThemeFontSizeOverride("font_size", 9);
        bullet.AddThemeColorOverride("font_color", AccentGreen);
        bullet.MouseFilter = MouseFilterEnum.Ignore;
        bullet.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(bullet);

        Label label = new Label();
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontOverride("font", PixelFont);
        label.AddThemeFontSizeOverride("font_size", 9);
        label.AddThemeColorOverride("font_color", TextPrimary);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
    }

    private static void AddSectionSpacer(VBoxContainer parent)
    {
        Control spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 16);
        spacer.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(spacer);
    }

    /// <summary>
    /// Adds a simple two-column key-value table (used for controls).
    /// </summary>
    private static void AddKeyValueTable(VBoxContainer parent, string[][] rows)
    {
        VBoxContainer table = new VBoxContainer();
        table.AddThemeConstantOverride("separation", 2);
        table.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        table.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(table);

        for (int i = 0; i < rows.Length; i++)
        {
            PanelContainer rowPanel = new PanelContainer();
            StyleBoxFlat rowStyle = new StyleBoxFlat();
            rowStyle.BgColor = i % 2 == 0 ? TableRowBg : TableRowAltBg;
            rowStyle.ContentMarginLeft = 12;
            rowStyle.ContentMarginRight = 12;
            rowStyle.ContentMarginTop = 4;
            rowStyle.ContentMarginBottom = 4;
            rowPanel.AddThemeStyleboxOverride("panel", rowStyle);
            rowPanel.MouseFilter = MouseFilterEnum.Ignore;
            table.AddChild(rowPanel);

            HBoxContainer rowBox = new HBoxContainer();
            rowBox.MouseFilter = MouseFilterEnum.Ignore;
            rowPanel.AddChild(rowBox);

            Label keyLabel = new Label();
            keyLabel.Text = rows[i][0];
            keyLabel.CustomMinimumSize = new Vector2(160, 0);
            keyLabel.AddThemeFontOverride("font", PixelFont);
            keyLabel.AddThemeFontSizeOverride("font_size", 9);
            keyLabel.AddThemeColorOverride("font_color", AccentGold);
            keyLabel.MouseFilter = MouseFilterEnum.Ignore;
            rowBox.AddChild(keyLabel);

            Label valLabel = new Label();
            valLabel.Text = rows[i][1];
            valLabel.AddThemeFontOverride("font", PixelFont);
            valLabel.AddThemeFontSizeOverride("font_size", 9);
            valLabel.AddThemeColorOverride("font_color", TextPrimary);
            valLabel.MouseFilter = MouseFilterEnum.Ignore;
            valLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rowBox.AddChild(valLabel);
        }
    }

    /// <summary>
    /// Adds a multi-column data table with a header row. First row is treated as the header.
    /// </summary>
    private static void AddDataTable(VBoxContainer parent, string[][] rows)
    {
        if (rows.Length == 0) return;

        VBoxContainer table = new VBoxContainer();
        table.AddThemeConstantOverride("separation", 1);
        table.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        table.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(table);

        int colCount = rows[0].Length;

        for (int i = 0; i < rows.Length; i++)
        {
            bool isHeader = i == 0;

            PanelContainer rowPanel = new PanelContainer();
            StyleBoxFlat rowStyle = new StyleBoxFlat();
            if (isHeader)
            {
                rowStyle.BgColor = TableHeaderBg;
            }
            else
            {
                rowStyle.BgColor = i % 2 == 0 ? TableRowBg : TableRowAltBg;
            }
            rowStyle.ContentMarginLeft = 8;
            rowStyle.ContentMarginRight = 8;
            rowStyle.ContentMarginTop = isHeader ? 6 : 3;
            rowStyle.ContentMarginBottom = isHeader ? 6 : 3;
            rowPanel.AddThemeStyleboxOverride("panel", rowStyle);
            rowPanel.MouseFilter = MouseFilterEnum.Ignore;
            table.AddChild(rowPanel);

            HBoxContainer rowBox = new HBoxContainer();
            rowBox.AddThemeConstantOverride("separation", 4);
            rowBox.MouseFilter = MouseFilterEnum.Ignore;
            rowBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rowPanel.AddChild(rowBox);

            for (int c = 0; c < colCount && c < rows[i].Length; c++)
            {
                Label cellLabel = new Label();
                cellLabel.Text = rows[i][c];
                cellLabel.AddThemeFontOverride("font", PixelFont);
                cellLabel.AddThemeFontSizeOverride("font_size", isHeader ? 9 : 8);
                cellLabel.MouseFilter = MouseFilterEnum.Ignore;

                if (isHeader)
                {
                    cellLabel.AddThemeColorOverride("font_color", AccentGold);
                }
                else if (c == 0)
                {
                    cellLabel.AddThemeColorOverride("font_color", AccentGreen);
                }
                else
                {
                    cellLabel.AddThemeColorOverride("font_color", TextPrimary);
                }

                // Last column is flexible width; others have fixed min width
                bool isLastCol = c == colCount - 1;
                if (isLastCol)
                {
                    cellLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    cellLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                }
                else
                {
                    // Assign widths based on column position
                    float minW = c == 0 ? 140 : 70;
                    cellLabel.CustomMinimumSize = new Vector2(minW, 0);
                }

                rowBox.AddChild(cellLabel);
            }
        }
    }
}
