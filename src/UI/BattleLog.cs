using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

/// <summary>
/// Scrollable battle log panel that displays turn-by-turn combat events.
/// Subscribes to EventBus signals and batches voxel destruction messages.
/// </summary>
public partial class BattleLog : Control
{
    // --- Layout ---
    private const int PanelWidth = 300;
    private const int PanelHeight = 200;
    private const int MaxEntries = 50;
    private const int FontSize = 10;

    // --- Event Colors ---
    private static readonly Color ColorTurnChange = Colors.White;
    private static readonly Color ColorWeaponFired = new Color("FFD700");
    private static readonly Color ColorCommanderDamaged = new Color("FF8C00");
    private static readonly Color ColorCommanderCritical = new Color("FF2222");
    private static readonly Color ColorCommanderKilled = new Color("FF4444");
    private static readonly Color ColorPhaseChange = new Color("00CED1");
    private static readonly Color ColorVoxelDestruction = new Color("AAAAAA");

    // --- Theme ---
    private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color BorderColor = new Color("30363d");
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    // --- Weapon ID to display name mapping ---
    private static readonly Dictionary<string, string> WeaponNames = new Dictionary<string, string>
    {
        { "cannon", "Cannon" },
        { "mortar", "Mortar" },
        { "railgun", "Railgun" },
        { "missile", "Missile Launcher" },
        { "drill", "Drill" },
    };

    // --- State ---
    private VBoxContainer? _logContainer;
    private ScrollContainer? _scrollContainer;
    private PanelContainer? _mainPanel;
    private Button? _toggleButton;
    private ColorRect? _headerLine;
    private bool _minimized;
    private readonly List<Label> _entries = new List<Label>();
    private int _pendingVoxelDestroyCount;
    private double _voxelBatchTimer;
    private const double VoxelBatchDelay = 0.15;
    private int _currentRound;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildPanel();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.TurnChanged += OnTurnChanged;
            EventBus.Instance.WeaponFired += OnWeaponFired;
            EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
            EventBus.Instance.CommanderKilled += OnCommanderKilled;
            EventBus.Instance.VoxelChanged += OnVoxelChanged;
        }

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
            EventBus.Instance.TurnChanged -= OnTurnChanged;
            EventBus.Instance.WeaponFired -= OnWeaponFired;
            EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
            EventBus.Instance.CommanderKilled -= OnCommanderKilled;
            EventBus.Instance.VoxelChanged -= OnVoxelChanged;
        }
    }

    public override void _Process(double delta)
    {
        if (_pendingVoxelDestroyCount > 0)
        {
            _voxelBatchTimer += delta;
            if (_voxelBatchTimer >= VoxelBatchDelay)
            {
                FlushVoxelBatch();
            }
        }
    }

    // ========== Panel Construction ==========

    private void BuildPanel()
    {
        _mainPanel = CreateBeveledPanel(PanelBg, BorderColor);
        _mainPanel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _mainPanel.OffsetLeft = 12;
        _mainPanel.OffsetRight = 12 + PanelWidth;
        _mainPanel.OffsetTop = -60 - PanelHeight;
        _mainPanel.OffsetBottom = -60;
        _mainPanel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        _mainPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mainPanel);

        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        _mainPanel.AddChild(margin);

        VBoxContainer outerColumn = new VBoxContainer();
        outerColumn.AddThemeConstantOverride("separation", 4);
        outerColumn.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(outerColumn);

        // Header row with title and minimize toggle
        HBoxContainer headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        headerRow.MouseFilter = MouseFilterEnum.Ignore;
        outerColumn.AddChild(headerRow);

        Label header = new Label();
        header.Text = "BATTLE LOG";
        header.AddThemeFontOverride("font", PixelFont);
        header.AddThemeFontSizeOverride("font_size", 10);
        header.AddThemeColorOverride("font_color", new Color("8b949e"));
        header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.MouseFilter = MouseFilterEnum.Ignore;
        headerRow.AddChild(header);

        _toggleButton = new Button();
        _toggleButton.Text = "\u25bc";
        _toggleButton.Flat = true;
        _toggleButton.CustomMinimumSize = new Vector2(24, 24);
        _toggleButton.AddThemeFontSizeOverride("font_size", 12);
        _toggleButton.AddThemeColorOverride("font_color", new Color("8b949e"));
        _toggleButton.AddThemeColorOverride("font_hover_color", new Color("e6edf3"));
        _toggleButton.MouseFilter = MouseFilterEnum.Stop;
        _toggleButton.Pressed += ToggleMinimize;
        headerRow.AddChild(_toggleButton);

        _headerLine = new ColorRect();
        _headerLine.CustomMinimumSize = new Vector2(0, 1);
        _headerLine.Color = BorderColor;
        _headerLine.MouseFilter = MouseFilterEnum.Ignore;
        outerColumn.AddChild(_headerLine);

        // Scroll container for log entries
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scrollContainer.MouseFilter = MouseFilterEnum.Pass;
        outerColumn.AddChild(_scrollContainer);

        _logContainer = new VBoxContainer();
        _logContainer.AddThemeConstantOverride("separation", 2);
        _logContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logContainer.MouseFilter = MouseFilterEnum.Ignore;
        _scrollContainer.AddChild(_logContainer);
    }

    private void ToggleMinimize()
    {
        _minimized = !_minimized;

        if (_toggleButton != null)
        {
            _toggleButton.Text = _minimized ? "\u25b2" : "\u25bc";
        }

        if (_scrollContainer != null)
        {
            _scrollContainer.Visible = !_minimized;
        }

        if (_headerLine != null)
        {
            _headerLine.Visible = !_minimized;
        }

        if (_mainPanel != null)
        {
            if (_minimized)
            {
                // Collapse to just the header bar height
                _mainPanel.OffsetTop = -60 - 36;
                _mainPanel.CustomMinimumSize = new Vector2(PanelWidth, 36);
            }
            else
            {
                // Restore full size
                _mainPanel.OffsetTop = -60 - PanelHeight;
                _mainPanel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
            }
        }
    }

    // ========== Public API ==========

    private void AddEntry(string text, Color color)
    {
        if (_logContainer == null || _scrollContainer == null)
        {
            return;
        }

        Label entry = new Label();
        entry.Text = text;
        entry.AddThemeFontOverride("font", PixelFont);
        entry.AddThemeFontSizeOverride("font_size", FontSize);
        entry.AddThemeColorOverride("font_color", color);
        entry.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        entry.MouseFilter = MouseFilterEnum.Ignore;
        _logContainer.AddChild(entry);
        _entries.Add(entry);

        // Trim old entries
        while (_entries.Count > MaxEntries)
        {
            Label oldest = _entries[0];
            _entries.RemoveAt(0);
            oldest.QueueFree();
        }

        // Auto-scroll to bottom on next frame so layout has settled
        CallDeferred(MethodName.ScrollToBottom);
    }

    private void ScrollToBottom()
    {
        if (_scrollContainer != null)
        {
            _scrollContainer.ScrollVertical = (int)_scrollContainer.GetVScrollBar().MaxValue;
        }
    }

    // ========== Event Handlers ==========

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Combat;

        string phaseName = payload.CurrentPhase switch
        {
            GamePhase.Building => "Building phase started",
            GamePhase.Combat => "Combat phase started",
            GamePhase.FogReveal => "Fog reveal phase started",
            GamePhase.GameOver => "Game over",
            _ => string.Empty,
        };

        if (!string.IsNullOrEmpty(phaseName))
        {
            AddEntry(phaseName, ColorPhaseChange);
        }
    }

    private void OnTurnChanged(TurnChangedEvent payload)
    {
        _currentRound = payload.RoundNumber;
        string playerName = GetPlayerDisplayName(payload.CurrentPlayer);
        AddEntry($"Round {payload.RoundNumber} - {playerName}'s turn", ColorTurnChange);
    }

    private void OnWeaponFired(WeaponFiredEvent payload)
    {
        string playerName = GetPlayerDisplayName(payload.Owner);
        string weaponName = GetWeaponDisplayName(payload.WeaponId);
        AddEntry($"{playerName} fired {weaponName}", ColorWeaponFired);
    }

    private void OnCommanderDamaged(CommanderDamagedEvent payload)
    {
        string playerName = GetPlayerDisplayName(payload.Player);
        int maxHp = GameConfig.CommanderHP;
        if (payload.IsCriticalHit)
        {
            AddEntry($"CRITICAL HIT! {playerName}'s Commander -{payload.Damage} HP! (HP: {payload.RemainingHealth}/{maxHp})", ColorCommanderCritical);
        }
        else
        {
            AddEntry($"{playerName}'s Commander hit! (HP: {payload.RemainingHealth}/{maxHp})", ColorCommanderDamaged);
        }
    }

    private void OnCommanderKilled(CommanderKilledEvent payload)
    {
        string victimName = GetPlayerDisplayName(payload.Victim);
        if (payload.Killer.HasValue)
        {
            string killerName = GetPlayerDisplayName(payload.Killer.Value);
            AddEntry($"{killerName} eliminated {victimName}!", ColorCommanderKilled);
        }
        else
        {
            AddEntry($"{victimName} was eliminated!", ColorCommanderKilled);
        }
    }

    private void OnVoxelChanged(VoxelChangeEvent payload)
    {
        // Only count voxels being destroyed (becoming air) during combat
        bool wasAir = (payload.BeforeData & 0xFF) == 0;
        bool isAir = (payload.AfterData & 0xFF) == 0;
        if (!wasAir && isAir)
        {
            _pendingVoxelDestroyCount++;
            _voxelBatchTimer = 0.0;
        }
    }

    private void FlushVoxelBatch()
    {
        if (_pendingVoxelDestroyCount > 0)
        {
            AddEntry($"{_pendingVoxelDestroyCount} voxels destroyed", ColorVoxelDestruction);
            _pendingVoxelDestroyCount = 0;
            _voxelBatchTimer = 0.0;
        }
    }

    // ========== Helpers ==========

    private static string GetPlayerDisplayName(PlayerSlot slot)
    {
        GameManager? gm = EventBus.Instance?.GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm != null)
        {
            PlayerData? player = gm.GetPlayer(slot);
            if (player != null && !string.IsNullOrEmpty(player.DisplayName))
            {
                return player.DisplayName;
            }
        }

        return slot.ToString();
    }

    private static string GetWeaponDisplayName(string weaponId)
    {
        if (WeaponNames.TryGetValue(weaponId, out string? name))
        {
            return name;
        }

        // Fallback: capitalize the weapon ID
        if (string.IsNullOrEmpty(weaponId))
        {
            return "Unknown";
        }

        return char.ToUpper(weaponId[0]) + weaponId[1..];
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
}
