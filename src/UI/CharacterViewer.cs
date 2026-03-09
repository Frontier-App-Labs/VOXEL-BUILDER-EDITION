using Godot;
using VoxelSiege.Art;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

/// <summary>
/// Standalone character/troop and weapon viewer for reviewing models and animations.
/// Two rows: characters on top, weapons below. All rotate together on a turntable.
///
/// Controls:
///   1-4: Select animation (Idle, Walk, Attack, Celebrate)
///   F: Flinch
///   P: Panic
///   Tab: Toggle between characters-only, weapons-only, or both views
///   Left/Right arrows: Rotate
///   Mouse wheel: Zoom
/// </summary>
public partial class CharacterViewer : Node3D
{
    private const float CharSpacing = 2.5f;
    private const float WeaponSpacing = 2.0f;
    private Node3D? _turntable;
    private Node3D? _charRow;
    private Node3D? _weaponRow;
    private float _rotationAngle;
    private float _zoomDistance = 6f;
    private Camera3D? _camera;
    private int _viewMode; // 0=both, 1=chars only, 2=weapons only

    private VoxelAnimator? _commanderAnim;
    private VoxelAnimator? _infantryAnim;
    private VoxelAnimator? _demolisherAnim;
    private VoxelAnimator? _scoutAnim;

    private Label? _infoLabel;

    public override void _Ready()
    {
        SetupLighting();

        // Create camera
        _camera = new Camera3D();
        _camera.Position = new Vector3(0, 1.5f, _zoomDistance);
        AddChild(_camera);
        _camera.LookAt(new Vector3(0, 0.5f, 0));
        _camera.Current = true;

        // Create turntable for all content
        _turntable = new Node3D();
        _turntable.Name = "Turntable";
        AddChild(_turntable);

        // === CHARACTER ROW (back row, z=0) ===
        _charRow = new Node3D();
        _charRow.Name = "Characters";
        _turntable.AddChild(_charRow);

        Color teamGreen = GameConfig.PlayerColors[0];
        Color teamRed = GameConfig.PlayerColors[1];

        _commanderAnim = SpawnCharacter(TroopModelGenerator.GenerateCommander(teamGreen),
            new Vector3(-CharSpacing * 1.5f, 0, 0), "CommanderAnimator");

        _infantryAnim = SpawnCharacter(TroopModelGenerator.GenerateInfantry(teamRed),
            new Vector3(-CharSpacing * 0.5f, 0, 0), "InfantryAnimator");

        _demolisherAnim = SpawnCharacter(TroopModelGenerator.GenerateDemolisher(teamGreen),
            new Vector3(CharSpacing * 0.5f, 0, 0), "DemolisherAnimator");

        _scoutAnim = SpawnCharacter(TroopModelGenerator.GenerateScout(teamRed),
            new Vector3(CharSpacing * 1.5f, 0, 0), "ScoutAnimator");

        // === WEAPON ROW (front row, z=3) ===
        _weaponRow = new Node3D();
        _weaponRow.Name = "Weapons";
        _weaponRow.Position = new Vector3(0, 0, 3f);
        _turntable.AddChild(_weaponRow);

        string[] weaponIds = { "cannon", "mortar", "railgun", "missile", "drill" };
        float weaponStartX = -(weaponIds.Length - 1) * WeaponSpacing * 0.5f;
        for (int i = 0; i < weaponIds.Length; i++)
        {
            WeaponModelResult result = WeaponModelGenerator.Generate(weaponIds[i], teamGreen);
            MeshInstance3D weaponMesh = new MeshInstance3D();
            weaponMesh.Mesh = result.Mesh;
            weaponMesh.MaterialOverride = VoxelModelBuilder.CreateVoxelMaterial(0.15f, 0.6f);
            weaponMesh.Position = new Vector3(weaponStartX + i * WeaponSpacing, 0, 0);
            _weaponRow.AddChild(weaponMesh);
        }

        // Ground plane
        MeshInstance3D ground = new MeshInstance3D();
        PlaneMesh planeMesh = new PlaneMesh();
        planeMesh.Size = new Vector2(25, 25);
        ground.Mesh = planeMesh;
        StandardMaterial3D groundMat = new StandardMaterial3D();
        groundMat.AlbedoColor = new Color(0.3f, 0.45f, 0.25f);
        ground.SetSurfaceOverrideMaterial(0, groundMat);
        AddChild(ground);

        // UI labels
        CanvasLayer ui = new CanvasLayer();
        AddChild(ui);

        _infoLabel = new Label();
        _infoLabel.Text = "VIEWER | 1:Idle 2:Walk 3:Attack 4:Celebrate F:Flinch P:Panic Tab:View | Arrows:Rotate Wheel:Zoom";
        _infoLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _infoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _infoLabel.AddThemeFontSizeOverride("font_size", 11);
        _infoLabel.AddThemeColorOverride("font_color", Colors.White);

        Font? pixelFont = ResourceLoader.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");
        if (pixelFont != null) _infoLabel.AddThemeFontOverride("font", pixelFont);
        ui.AddChild(_infoLabel);

        // Character labels
        AddLabel(ui, "COMMANDER", -CharSpacing * 1.5f, -60);
        AddLabel(ui, "INFANTRY", -CharSpacing * 0.5f, -60);
        AddLabel(ui, "DEMOLISHER", CharSpacing * 0.5f, -60);
        AddLabel(ui, "SCOUT", CharSpacing * 1.5f, -60);

        // Weapon labels (lower row)
        string[] weaponNames = { "CANNON\n(Tier 1)", "MORTAR\n(Tier 2)", "RAILGUN\n(Tier 3)", "MISSILE\n(Tier 3)", "DRILL\n(Tier 1)" };
        for (int i = 0; i < weaponNames.Length; i++)
        {
            AddLabel(ui, weaponNames[i], weaponStartX + i * WeaponSpacing, -20);
        }
    }

    private VoxelAnimator SpawnCharacter(CharacterDefinition def, Vector3 position, string animName)
    {
        Node3D character = VoxelCharacterBuilder.Build(def);
        character.Position = position;
        _charRow!.AddChild(character);

        VoxelAnimator anim = new VoxelAnimator();
        anim.Name = animName;
        character.AddChild(anim);
        anim.Initialize(character);
        return anim;
    }

    private void AddLabel(CanvasLayer parent, string text, float worldX, float offsetBottom)
    {
        Label label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 9);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
        label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        label.OffsetTop = offsetBottom - 20;
        label.OffsetBottom = offsetBottom;
        label.OffsetLeft = worldX * 70 - 70;
        label.OffsetRight = worldX * 70 + 70;

        Font? pixelFont = ResourceLoader.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");
        if (pixelFont != null) label.AddThemeFontOverride("font", pixelFont);

        parent.AddChild(label);
    }

    private void SetupLighting()
    {
        // Key light (warm)
        DirectionalLight3D keyLight = new DirectionalLight3D();
        keyLight.Rotation = new Vector3(Mathf.DegToRad(-45), Mathf.DegToRad(30), 0);
        keyLight.LightColor = new Color(1.0f, 0.95f, 0.85f);
        keyLight.LightEnergy = 1.2f;
        keyLight.ShadowEnabled = true;
        AddChild(keyLight);

        // Fill light (cool)
        DirectionalLight3D fillLight = new DirectionalLight3D();
        fillLight.Rotation = new Vector3(Mathf.DegToRad(-30), Mathf.DegToRad(-60), 0);
        fillLight.LightColor = new Color(0.7f, 0.8f, 1.0f);
        fillLight.LightEnergy = 0.5f;
        AddChild(fillLight);

        // Ambient
        WorldEnvironment env = new WorldEnvironment();
        Godot.Environment envRes = new Godot.Environment();
        envRes.BackgroundMode = Godot.Environment.BGMode.Color;
        envRes.BackgroundColor = new Color(0.15f, 0.18f, 0.22f);
        envRes.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        envRes.AmbientLightColor = new Color(0.4f, 0.4f, 0.45f);
        envRes.AmbientLightEnergy = 0.6f;
        env.Environment = envRes;
        AddChild(env);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            // Tab cycles view mode
            if (key.Keycode == Key.Tab)
            {
                _viewMode = (_viewMode + 1) % 3;
                if (_charRow != null) _charRow.Visible = _viewMode != 2;
                if (_weaponRow != null) _weaponRow.Visible = _viewMode != 1;
                return;
            }

            VoxelAnimator.AnimState? newState = key.Keycode switch
            {
                Key.Key1 => VoxelAnimator.AnimState.Idle,
                Key.Key2 => VoxelAnimator.AnimState.Walk,
                Key.Key3 => VoxelAnimator.AnimState.Attack,
                Key.Key4 => VoxelAnimator.AnimState.Celebrate,
                Key.F => VoxelAnimator.AnimState.Flinch,
                Key.P => VoxelAnimator.AnimState.Panic,
                _ => null
            };

            if (newState is VoxelAnimator.AnimState state)
            {
                _commanderAnim?.SetState(state, 1f);
                _infantryAnim?.SetState(state, 1f);
                _demolisherAnim?.SetState(state, 0.8f);
                _scoutAnim?.SetState(state, 1.6f);
            }
        }

        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                _zoomDistance = Mathf.Max(2f, _zoomDistance - 0.5f);
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
                _zoomDistance = Mathf.Min(15f, _zoomDistance + 0.5f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (Input.IsKeyPressed(Key.Left))
            _rotationAngle += dt * 1.5f;
        if (Input.IsKeyPressed(Key.Right))
            _rotationAngle -= dt * 1.5f;

        if (_turntable != null)
            _turntable.Rotation = new Vector3(0, _rotationAngle, 0);

        if (_camera != null)
        {
            _camera.Position = new Vector3(0, 1.5f, _zoomDistance);
            _camera.LookAt(new Vector3(0, 0.4f, 0));
        }
    }
}
