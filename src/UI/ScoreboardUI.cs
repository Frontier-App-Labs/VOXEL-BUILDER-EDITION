using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class ScoreboardUI : Control
{
    // --- Theme Colors ---
    private static readonly Color BgPanel = new Color(0.086f, 0.106f, 0.133f, 0.88f);
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color AccentRed = new Color("d73a49");
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color BorderColor = new Color("30363d");

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    [Export]
    public NodePath? GameManagerPath { get; set; }

    private VBoxContainer? _playerList;
    private PanelContainer? _panel;
    private bool _needsRebuild = true;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = CreateStyledPanel(BgPanel, 0);
        // Use TopRight anchor point so the panel auto-sizes to its content height
        // (not stretching full height). Anchors at top-right corner.
        _panel.AnchorLeft = 1;
        _panel.AnchorTop = 0;
        _panel.AnchorRight = 1;
        _panel.AnchorBottom = 0;
        _panel.OffsetLeft = -272;
        _panel.OffsetRight = -12;
        _panel.OffsetTop = 60;
        _panel.CustomMinimumSize = new Vector2(260, 0);
        _panel.ClipContents = true;
        _panel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_panel);

        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        _panel.AddChild(margin);

        _playerList = new VBoxContainer();
        _playerList.AddThemeConstantOverride("separation", 6);
        _playerList.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(_playerList);

        // Header
        Label header = new Label();
        header.Text = "SCOREBOARD";
        header.AddThemeFontOverride("font", PixelFont);
        header.AddThemeFontSizeOverride("font_size", 10);
        header.AddThemeColorOverride("font_color", TextSecondary);
        header.MouseFilter = MouseFilterEnum.Ignore;
        _playerList.AddChild(header);

        ColorRect headerLine = new ColorRect();
        headerLine.CustomMinimumSize = new Vector2(0, 1);
        headerLine.Color = BorderColor;
        headerLine.MouseFilter = MouseFilterEnum.Ignore;
        _playerList.AddChild(headerLine);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
        }

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
        }
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Combat;
    }

    public override void _Process(double delta)
    {
        if (_playerList == null) return;

        GameManager? gm = GameManagerPath is null
            ? GetTree().Root.GetNodeOrNull<GameManager>("Main")
            : GetNodeOrNull<GameManager>(GameManagerPath);

        if (gm == null) return;

        // Rebuild entries every frame is expensive; only rebuild when player count changes
        int expectedEntries = gm.Players.Count;
        int currentEntries = _playerList.GetChildCount() - 2; // minus header + line

        if (currentEntries != expectedEntries)
        {
            // Clear old entries
            while (_playerList.GetChildCount() > 2)
            {
                Node child = _playerList.GetChild(_playerList.GetChildCount() - 1);
                _playerList.RemoveChild(child);
                child.QueueFree();
            }
            _needsRebuild = true;
        }

        if (_needsRebuild)
        {
            foreach (PlayerData player in gm.Players.Values)
            {
                _playerList.AddChild(CreatePlayerEntry(player));
            }
            _needsRebuild = false;
        }

        // Update existing entries
        int idx = 2; // start after header + line
        foreach (PlayerData player in gm.Players.Values)
        {
            if (idx >= _playerList.GetChildCount()) break;

            Control entry = _playerList.GetChild(idx) as Control ?? new Control();
            UpdatePlayerEntry(entry, player);
            idx++;
        }
    }

    private Control CreatePlayerEntry(PlayerData player)
    {
        VBoxContainer entry = new VBoxContainer();
        entry.Name = $"Player_{player.Slot}";
        entry.AddThemeConstantOverride("separation", 2);
        entry.MouseFilter = MouseFilterEnum.Ignore;

        // Name row
        HBoxContainer nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.MouseFilter = MouseFilterEnum.Ignore;
        entry.AddChild(nameRow);

        // Color indicator
        ColorRect colorDot = new ColorRect();
        colorDot.Name = "ColorDot";
        colorDot.CustomMinimumSize = new Vector2(8, 8);
        colorDot.Color = player.PlayerColor;
        colorDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        colorDot.MouseFilter = MouseFilterEnum.Ignore;
        nameRow.AddChild(colorDot);

        Label nameLabel = new Label();
        nameLabel.Name = "Name";
        nameLabel.Text = player.DisplayName;
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.ClipText = true;
        nameLabel.AddThemeFontOverride("font", PixelFont);
        nameLabel.AddThemeFontSizeOverride("font_size", 10);
        nameLabel.AddThemeColorOverride("font_color", TextPrimary);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        nameRow.AddChild(nameLabel);

        Label statusLabel = new Label();
        statusLabel.Name = "Status";
        statusLabel.Text = player.IsAlive ? "ALIVE" : "DEAD";
        statusLabel.AddThemeFontOverride("font", PixelFont);
        statusLabel.AddThemeFontSizeOverride("font_size", 10);
        statusLabel.AddThemeColorOverride("font_color", player.IsAlive ? AccentGreen : AccentRed);
        statusLabel.MouseFilter = MouseFilterEnum.Ignore;
        nameRow.AddChild(statusLabel);

        // HP bar — use Panel (not PanelContainer) so child anchors work for fill sizing
        Panel barBg = new Panel();
        barBg.Name = "BarBg";
        StyleBoxFlat barBgStyle = new StyleBoxFlat();
        barBgStyle.BgColor = new Color(0.15f, 0.15f, 0.15f);
        barBg.AddThemeStyleboxOverride("panel", barBgStyle);
        barBg.CustomMinimumSize = new Vector2(0, 6);
        barBg.ClipContents = true;
        barBg.MouseFilter = MouseFilterEnum.Ignore;
        entry.AddChild(barBg);

        ColorRect barFill = new ColorRect();
        barFill.Name = "BarFill";
        barFill.AnchorLeft = 0;
        barFill.AnchorRight = 0;
        barFill.AnchorTop = 0;
        barFill.AnchorBottom = 1;
        barFill.OffsetLeft = 0;
        barFill.OffsetRight = 0;
        barFill.OffsetTop = 0;
        barFill.OffsetBottom = 0;
        barFill.Color = player.PlayerColor;
        barFill.MouseFilter = MouseFilterEnum.Ignore;
        barBg.AddChild(barFill);

        // Info row
        HBoxContainer infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 12);
        infoRow.MouseFilter = MouseFilterEnum.Ignore;
        entry.AddChild(infoRow);

        Label hpLabel = new Label();
        hpLabel.Name = "HP";
        hpLabel.Text = $"HP: {player.CommanderHealth}";
        hpLabel.AddThemeFontOverride("font", PixelFont);
        hpLabel.AddThemeFontSizeOverride("font_size", 10);
        hpLabel.AddThemeColorOverride("font_color", TextSecondary);
        hpLabel.MouseFilter = MouseFilterEnum.Ignore;
        infoRow.AddChild(hpLabel);

        Label weaponLabel = new Label();
        weaponLabel.Name = "Weapons";
        weaponLabel.Text = $"Wpns: {player.WeaponIds.Count}";
        weaponLabel.AddThemeFontOverride("font", PixelFont);
        weaponLabel.AddThemeFontSizeOverride("font_size", 10);
        weaponLabel.AddThemeColorOverride("font_color", TextSecondary);
        weaponLabel.MouseFilter = MouseFilterEnum.Ignore;
        infoRow.AddChild(weaponLabel);

        return entry;
    }

    private void UpdatePlayerEntry(Control entry, PlayerData player)
    {
        // Update name (may change during build phase when the player enters a name)
        Label? nameLabel = FindDescendant<Label>(entry, "Name");
        if (nameLabel != null)
        {
            nameLabel.Text = player.DisplayName;
        }

        // Update status
        Label? status = FindDescendant<Label>(entry, "Status");
        if (status != null)
        {
            status.Text = player.IsAlive ? "ALIVE" : "DEAD";
            status.AddThemeColorOverride("font_color", player.IsAlive ? AccentGreen : AccentRed);
        }

        // Update HP label
        Label? hpLabel = FindDescendant<Label>(entry, "HP");
        if (hpLabel != null)
        {
            hpLabel.Text = $"HP: {player.CommanderHealth}";
        }

        // Update bar fill — use AnchorRight to size proportionally (avoids non-equal anchor warning)
        // Clamp to 0-1 range so the bar never extends beyond its container
        ColorRect? barFill = FindDescendant<ColorRect>(entry, "BarFill");
        if (barFill != null)
        {
            float percent = Mathf.Clamp(player.CommanderHealth / (float)GameConfig.CommanderHP, 0f, 1f);
            barFill.AnchorRight = percent;
            // Reset offset after anchor change — Godot 4 preserves the absolute
            // position by adjusting offsets when anchors change, which prevents
            // the bar from visually shrinking.
            barFill.OffsetRight = 0;
        }

        // Update weapon count
        Label? weaponLabel = FindDescendant<Label>(entry, "Weapons");
        if (weaponLabel != null)
        {
            weaponLabel.Text = $"Wpns: {player.WeaponIds.Count}";
        }
    }

    private static T? FindDescendant<T>(Node parent, string name) where T : Node
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            Node child = parent.GetChild(i);
            if (child is T typed && child.Name == name)
            {
                return typed;
            }

            T? found = FindDescendant<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static PanelContainer CreateStyledPanel(Color bgColor, int cornerRadius)
    {
        PanelContainer panel = new PanelContainer();
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.CornerRadiusTopLeft = cornerRadius;
        style.CornerRadiusTopRight = cornerRadius;
        style.CornerRadiusBottomLeft = cornerRadius;
        style.CornerRadiusBottomRight = cornerRadius;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }
}
