using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class GameOverUI : Control
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
    private static readonly Color OverlayColor = new Color(0, 0, 0, 0.75f);

    // --- Pixel Font (lazy to avoid loading before Godot's resource system is ready) ---
    private static Font? _pixelFont;
    private static Font PixelFont => _pixelFont ??= GD.Load<Font>("res://assets/fonts/PressStart2P-Regular.ttf");

    private Label? _resultLabel;
    private Label? _resultSubLabel;
    private VBoxContainer? _scoreboardContainer;
    private VBoxContainer? _statsContainer;
    private Control? _mainContent;
    private bool _animating;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        // Dark overlay backdrop
        ColorRect backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = OverlayColor;
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(backdrop);

        // Main content
        _mainContent = new VBoxContainer();
        _mainContent.SetAnchorsPreset(LayoutPreset.FullRect);
        _mainContent.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mainContent);

        // Top spacer
        Control topSpacer = new Control();
        topSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        topSpacer.SizeFlagsStretchRatio = 0.6f;
        topSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _mainContent.AddChild(topSpacer);

        // Center content
        VBoxContainer centerBox = new VBoxContainer();
        centerBox.AddThemeConstantOverride("separation", 0);
        centerBox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        centerBox.MouseFilter = MouseFilterEnum.Ignore;
        _mainContent.AddChild(centerBox);

        // Result title
        _resultLabel = new Label();
        _resultLabel.Text = "VICTORY";
        _resultLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _resultLabel.AddThemeFontOverride("font", PixelFont);
        _resultLabel.AddThemeFontSizeOverride("font_size", 32);
        _resultLabel.AddThemeColorOverride("font_color", AccentGold);
        _resultLabel.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(_resultLabel);

        // Accent bar
        ColorRect accentBar = new ColorRect();
        accentBar.CustomMinimumSize = new Vector2(400, 4);
        accentBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        accentBar.Color = AccentGold;
        accentBar.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(accentBar);

        // Subtitle
        Control subSpacer = new Control();
        subSpacer.CustomMinimumSize = new Vector2(0, 8);
        subSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(subSpacer);

        _resultSubLabel = new Label();
        _resultSubLabel.Text = "Your forces prevailed!";
        _resultSubLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _resultSubLabel.AddThemeFontOverride("font", PixelFont);
        _resultSubLabel.AddThemeFontSizeOverride("font_size", 12);
        _resultSubLabel.AddThemeColorOverride("font_color", TextSecondary);
        _resultSubLabel.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(_resultSubLabel);

        // Spacer
        Control midSpacer = new Control();
        midSpacer.CustomMinimumSize = new Vector2(0, 32);
        midSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(midSpacer);

        // Scoreboard + Stats in horizontal layout
        HBoxContainer panels = new HBoxContainer();
        panels.AddThemeConstantOverride("separation", 20);
        panels.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        panels.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(panels);

        // Scoreboard panel
        PanelContainer scorePanel = CreateStyledPanel(PanelBg, 0);
        scorePanel.CustomMinimumSize = new Vector2(340, 200);
        scorePanel.MouseFilter = MouseFilterEnum.Ignore;
        panels.AddChild(scorePanel);

        MarginContainer scoreMargin = new MarginContainer();
        scoreMargin.AddThemeConstantOverride("margin_left", 20);
        scoreMargin.AddThemeConstantOverride("margin_right", 20);
        scoreMargin.AddThemeConstantOverride("margin_top", 16);
        scoreMargin.AddThemeConstantOverride("margin_bottom", 16);
        scoreMargin.MouseFilter = MouseFilterEnum.Ignore;
        scorePanel.AddChild(scoreMargin);

        _scoreboardContainer = new VBoxContainer();
        _scoreboardContainer.AddThemeConstantOverride("separation", 6);
        _scoreboardContainer.MouseFilter = MouseFilterEnum.Ignore;
        scoreMargin.AddChild(_scoreboardContainer);

        Label scoreHeader = new Label();
        scoreHeader.Text = "SCOREBOARD";
        scoreHeader.AddThemeFontOverride("font", PixelFont);
        scoreHeader.AddThemeFontSizeOverride("font_size", 12);
        scoreHeader.AddThemeColorOverride("font_color", TextSecondary);
        scoreHeader.MouseFilter = MouseFilterEnum.Ignore;
        _scoreboardContainer.AddChild(scoreHeader);

        ColorRect scoreLine = new ColorRect();
        scoreLine.CustomMinimumSize = new Vector2(0, 1);
        scoreLine.Color = BorderColor;
        scoreLine.MouseFilter = MouseFilterEnum.Ignore;
        _scoreboardContainer.AddChild(scoreLine);

        // Stats panel
        PanelContainer statsPanel = CreateStyledPanel(PanelBg, 0);
        statsPanel.CustomMinimumSize = new Vector2(280, 200);
        statsPanel.MouseFilter = MouseFilterEnum.Ignore;
        panels.AddChild(statsPanel);

        MarginContainer statsMargin = new MarginContainer();
        statsMargin.AddThemeConstantOverride("margin_left", 20);
        statsMargin.AddThemeConstantOverride("margin_right", 20);
        statsMargin.AddThemeConstantOverride("margin_top", 16);
        statsMargin.AddThemeConstantOverride("margin_bottom", 16);
        statsMargin.MouseFilter = MouseFilterEnum.Ignore;
        statsPanel.AddChild(statsMargin);

        _statsContainer = new VBoxContainer();
        _statsContainer.AddThemeConstantOverride("separation", 6);
        _statsContainer.MouseFilter = MouseFilterEnum.Ignore;
        statsMargin.AddChild(_statsContainer);

        Label statsHeader = new Label();
        statsHeader.Text = "YOUR STATS";
        statsHeader.AddThemeFontOverride("font", PixelFont);
        statsHeader.AddThemeFontSizeOverride("font_size", 12);
        statsHeader.AddThemeColorOverride("font_color", TextSecondary);
        statsHeader.MouseFilter = MouseFilterEnum.Ignore;
        _statsContainer.AddChild(statsHeader);

        ColorRect statsLine = new ColorRect();
        statsLine.CustomMinimumSize = new Vector2(0, 1);
        statsLine.Color = BorderColor;
        statsLine.MouseFilter = MouseFilterEnum.Ignore;
        _statsContainer.AddChild(statsLine);

        // Spacer before buttons
        Control btnSpacer = new Control();
        btnSpacer.CustomMinimumSize = new Vector2(0, 28);
        btnSpacer.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(btnSpacer);

        // Action buttons
        HBoxContainer buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 16);
        buttonRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        buttonRow.MouseFilter = MouseFilterEnum.Ignore;
        centerBox.AddChild(buttonRow);

        buttonRow.AddChild(CreateActionButton("REMATCH", AccentGreen, OnRematchPressed));
        buttonRow.AddChild(CreateActionButton("MAIN MENU", TextSecondary, OnReturnMenuPressed));
        buttonRow.AddChild(CreateActionButton("QUIT", AccentRed, OnQuitPressed));

        // Bottom spacer
        Control bottomSpacer = new Control();
        bottomSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
        bottomSpacer.MouseFilter = MouseFilterEnum.Ignore;
        _mainContent.AddChild(bottomSpacer);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
        }
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
        Visible = payload.CurrentPhase == GamePhase.GameOver;
        if (Visible)
        {
            PopulateResults();
            AnimateEntry();
        }
        else
        {
            _animating = false;
        }
    }

    private void PopulateResults()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm == null) return;

        // Determine winner
        PlayerData? winner = null;
        PlayerData? localPlayer = null;
        var rankedPlayers = new List<(PlayerData Player, int Rank)>();
        int rank = 1;

        foreach (var (slot, player) in gm.Players)
        {
            rankedPlayers.Add((player, player.IsAlive ? 0 : rank++));
            if (player.IsAlive)
            {
                winner = player;
            }
            if (slot == PlayerSlot.Player1)
            {
                localPlayer = player;
            }
        }

        // Sort: alive first, then by damage dealt
        rankedPlayers.Sort((a, b) =>
        {
            if (a.Player.IsAlive != b.Player.IsAlive)
                return a.Player.IsAlive ? -1 : 1;
            return b.Player.Stats.DamageDealt.CompareTo(a.Player.Stats.DamageDealt);
        });

        bool isVictory = localPlayer != null && localPlayer.IsAlive;

        if (_resultLabel != null)
        {
            _resultLabel.Text = isVictory ? "VICTORY" : "DEFEAT";
            _resultLabel.AddThemeColorOverride("font_color", isVictory ? AccentGold : AccentRed);
        }

        if (_resultSubLabel != null)
        {
            _resultSubLabel.Text = isVictory ? "Your forces prevailed!" : "Your commander has fallen.";
        }

        // Populate scoreboard
        if (_scoreboardContainer != null)
        {
            // Remove old player entries (keep header + line)
            while (_scoreboardContainer.GetChildCount() > 2)
            {
                Node child = _scoreboardContainer.GetChild(_scoreboardContainer.GetChildCount() - 1);
                _scoreboardContainer.RemoveChild(child);
                child.QueueFree();
            }

            int displayRank = 1;
            foreach (var (player, _) in rankedPlayers)
            {
                HBoxContainer row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 12);
                row.MouseFilter = MouseFilterEnum.Ignore;

                Label rankLabel = new Label();
                rankLabel.Text = $"#{displayRank}";
                rankLabel.CustomMinimumSize = new Vector2(30, 0);
                rankLabel.AddThemeFontOverride("font", PixelFont);
                rankLabel.AddThemeFontSizeOverride("font_size", 12);
                rankLabel.AddThemeColorOverride("font_color", displayRank == 1 ? AccentGold : TextSecondary);
                rankLabel.MouseFilter = MouseFilterEnum.Ignore;
                row.AddChild(rankLabel);

                ColorRect colorDot = new ColorRect();
                colorDot.CustomMinimumSize = new Vector2(10, 10);
                colorDot.Size = new Vector2(10, 10);
                colorDot.Color = player.PlayerColor;
                colorDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                colorDot.MouseFilter = MouseFilterEnum.Ignore;
                row.AddChild(colorDot);

                Label nameLabel = new Label();
                nameLabel.Text = player.DisplayName;
                nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                nameLabel.AddThemeFontOverride("font", PixelFont);
                nameLabel.AddThemeFontSizeOverride("font_size", 12);
                nameLabel.AddThemeColorOverride("font_color", TextPrimary);
                nameLabel.MouseFilter = MouseFilterEnum.Ignore;
                row.AddChild(nameLabel);

                Label statusLabel = new Label();
                statusLabel.Text = player.IsAlive ? "WINNER" : "ELIMINATED";
                statusLabel.AddThemeFontOverride("font", PixelFont);
                statusLabel.AddThemeFontSizeOverride("font_size", 10);
                statusLabel.AddThemeColorOverride("font_color", player.IsAlive ? AccentGreen : AccentRed);
                statusLabel.MouseFilter = MouseFilterEnum.Ignore;
                row.AddChild(statusLabel);

                _scoreboardContainer.AddChild(row);
                displayRank++;
            }
        }

        // Populate stats
        if (_statsContainer != null && localPlayer != null)
        {
            while (_statsContainer.GetChildCount() > 2)
            {
                Node child = _statsContainer.GetChild(_statsContainer.GetChildCount() - 1);
                _statsContainer.RemoveChild(child);
                child.QueueFree();
            }

            float accuracy = localPlayer.Stats.ShotsFired > 0
                ? (localPlayer.Stats.ShotsHit / (float)localPlayer.Stats.ShotsFired) * 100f
                : 0f;

            AddStatRow("Damage Dealt", $"{localPlayer.Stats.DamageDealt}");
            AddStatRow("Voxels Destroyed", $"{localPlayer.Stats.VoxelsDestroyed}");
            AddStatRow("Shots Fired", $"{localPlayer.Stats.ShotsFired}");
            AddStatRow("Accuracy", $"{accuracy:F1}%");
            AddStatRow("Commander Kills", $"{localPlayer.Stats.CommanderKills}");
        }
    }

    private void AddStatRow(string label, string value)
    {
        if (_statsContainer == null) return;

        HBoxContainer row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;

        Label nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AddThemeFontOverride("font", PixelFont);
        nameLabel.AddThemeFontSizeOverride("font_size", 10);
        nameLabel.AddThemeColorOverride("font_color", TextSecondary);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLabel);

        Label valueLabel = new Label();
        valueLabel.Text = value;
        valueLabel.AddThemeFontOverride("font", PixelFont);
        valueLabel.AddThemeFontSizeOverride("font_size", 12);
        valueLabel.AddThemeColorOverride("font_color", AccentGold);
        valueLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(valueLabel);

        _statsContainer.AddChild(row);
    }

    private void AnimateEntry()
    {
        if (_mainContent == null) return;

        // Guard against double animation — kill existing tweens first
        if (_animating) return;
        _animating = true;

        _mainContent.Modulate = new Color(1, 1, 1, 0);
        _mainContent.Scale = new Vector2(0.9f, 0.9f);

        Tween fadeTween = _mainContent.CreateTween();
        fadeTween.TweenProperty(_mainContent, "modulate:a", 1.0f, 0.5f)
                 .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        fadeTween.Finished += () => _animating = false;

        Tween scaleTween = _mainContent.CreateTween();
        scaleTween.TweenProperty(_mainContent, "scale", Vector2.One, 0.4f)
                  .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private PanelContainer CreateActionButton(string text, Color accentColor, System.Action handler)
    {
        PanelContainer btnPanel = new PanelContainer();
        btnPanel.CustomMinimumSize = new Vector2(160, 44);

        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.12f);
        style.CornerRadiusTopLeft = 0;
        style.CornerRadiusTopRight = 0;
        style.CornerRadiusBottomLeft = 0;
        style.CornerRadiusBottomRight = 0;
        style.BorderWidthLeft = 4;
        style.BorderWidthBottom = 4;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.3f);
        btnPanel.AddThemeStyleboxOverride("panel", style);

        Button btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.AddThemeFontOverride("font", PixelFont);
        btn.AddThemeFontSizeOverride("font_size", 12);
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

        // Hover
        btn.MouseEntered += () =>
        {
            StyleBoxFlat hover = new StyleBoxFlat();
            hover.BgColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.25f);
            hover.CornerRadiusTopLeft = 0;
            hover.CornerRadiusTopRight = 0;
            hover.CornerRadiusBottomLeft = 0;
            hover.CornerRadiusBottomRight = 0;
            hover.BorderWidthLeft = 4;
            hover.BorderWidthBottom = 4;
            hover.BorderWidthTop = 2;
            hover.BorderWidthRight = 2;
            hover.BorderColor = accentColor;
            btnPanel.AddThemeStyleboxOverride("panel", hover);
        };
        btn.MouseExited += () =>
        {
            btnPanel.AddThemeStyleboxOverride("panel", style);
        };

        return btnPanel;
    }

    private void OnRematchPressed()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gm?.StartPrototypeMatch();
        Visible = false;
    }

    private void OnReturnMenuPressed()
    {
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gm?.CleanupMatchState();
        gm?.SetPhase(GamePhase.Menu);
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
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
