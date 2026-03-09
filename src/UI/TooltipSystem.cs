using Godot;

namespace VoxelSiege.UI;

public partial class TooltipSystem : CanvasLayer
{
    // --- Theme Colors ---
    private static readonly Color BgPanel = new Color(0.086f, 0.106f, 0.133f, 0.95f);
    private static readonly Color TextPrimary = new Color("e6edf3");
    private static readonly Color TextSecondary = new Color("8b949e");
    private static readonly Color AccentGreen = new Color("2ea043");
    private static readonly Color BorderColor = new Color("30363d");

    private PanelContainer? _tooltipPanel;
    private VBoxContainer? _contentContainer;
    private Label? _titleLabel;
    private Label? _bodyLabel;

    private float _showDelay = 0.3f;
    private float _showTimer;
    private bool _pendingShow;
    private string _pendingTitle = string.Empty;
    private string _pendingBody = string.Empty;

    private const float OffsetX = 16f;
    private const float OffsetY = 16f;
    private const float EdgeMargin = 12f;

    public override void _Ready()
    {
        // Build tooltip panel
        _tooltipPanel = new PanelContainer();
        _tooltipPanel.Name = "TooltipPanel";
        _tooltipPanel.Visible = false;
        _tooltipPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tooltipPanel.ZIndex = 100;

        StyleBoxFlat panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = BgPanel;
        panelStyle.CornerRadiusTopLeft = 0;
        panelStyle.CornerRadiusTopRight = 0;
        panelStyle.CornerRadiusBottomLeft = 0;
        panelStyle.CornerRadiusBottomRight = 0;
        panelStyle.BorderWidthTop = 1;
        panelStyle.BorderWidthBottom = 1;
        panelStyle.BorderWidthLeft = 1;
        panelStyle.BorderWidthRight = 1;
        panelStyle.BorderColor = BorderColor;
        panelStyle.ContentMarginLeft = 12;
        panelStyle.ContentMarginRight = 12;
        panelStyle.ContentMarginTop = 8;
        panelStyle.ContentMarginBottom = 8;
        panelStyle.SetShadowSize(4);
        panelStyle.ShadowColor = new Color(0, 0, 0, 0.3f);
        panelStyle.ShadowOffset = new Vector2(2, 2);
        _tooltipPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_tooltipPanel);

        _contentContainer = new VBoxContainer();
        _contentContainer.AddThemeConstantOverride("separation", 4);
        _contentContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tooltipPanel.AddChild(_contentContainer);

        _titleLabel = new Label();
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", TextPrimary);
        _titleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _contentContainer.AddChild(_titleLabel);

        // Accent line under title
        ColorRect accentLine = new ColorRect();
        accentLine.Name = "AccentLine";
        accentLine.CustomMinimumSize = new Vector2(0, 1);
        accentLine.Color = AccentGreen;
        accentLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        _contentContainer.AddChild(accentLine);

        _bodyLabel = new Label();
        _bodyLabel.AddThemeFontSizeOverride("font_size", 12);
        _bodyLabel.AddThemeColorOverride("font_color", TextSecondary);
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.CustomMinimumSize = new Vector2(180, 0);
        _bodyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _contentContainer.AddChild(_bodyLabel);
    }

    public override void _Process(double delta)
    {
        // Handle delayed show
        if (_pendingShow)
        {
            _showTimer -= (float)delta;
            if (_showTimer <= 0f)
            {
                _pendingShow = false;
                ApplyTooltipContent(_pendingTitle, _pendingBody);
            }
        }

        // Follow mouse with edge clamping
        if (_tooltipPanel != null && _tooltipPanel.Visible)
        {
            Vector2 mousePos = GetViewport().GetMousePosition();
            Vector2 tooltipSize = _tooltipPanel.Size;
            Vector2 viewportSize = GetViewport().GetVisibleRect().Size;

            float x = mousePos.X + OffsetX;
            float y = mousePos.Y + OffsetY;

            // Clamp to viewport edges
            if (x + tooltipSize.X > viewportSize.X - EdgeMargin)
            {
                x = mousePos.X - tooltipSize.X - OffsetX;
            }
            if (y + tooltipSize.Y > viewportSize.Y - EdgeMargin)
            {
                y = mousePos.Y - tooltipSize.Y - OffsetY;
            }
            if (x < EdgeMargin) x = EdgeMargin;
            if (y < EdgeMargin) y = EdgeMargin;

            _tooltipPanel.Position = new Vector2(x, y);
        }
    }

    /// <summary>
    /// Shows a simple single-line tooltip.
    /// </summary>
    public void ShowTooltip(string text)
    {
        ShowTooltip(string.Empty, text, 0f);
    }

    /// <summary>
    /// Shows a tooltip with a title and body text.
    /// </summary>
    public void ShowTooltip(string title, string body, float delay = 0f)
    {
        if (delay > 0f)
        {
            _pendingShow = true;
            _showTimer = delay;
            _pendingTitle = title;
            _pendingBody = body;
            return;
        }

        ApplyTooltipContent(title, body);
    }

    /// <summary>
    /// Shows material info tooltip.
    /// </summary>
    public void ShowMaterialTooltip(string materialName, int cost, int hp, string properties)
    {
        ShowTooltip(materialName, $"Cost: ${cost}  |  HP: {hp}\n{properties}");
    }

    /// <summary>
    /// Shows weapon stats tooltip.
    /// </summary>
    public void ShowWeaponTooltip(string weaponName, int damage, float cooldown, string description)
    {
        ShowTooltip(weaponName, $"Damage: {damage}  |  Cooldown: {cooldown:F1}s\n{description}");
    }

    public void HideTooltip()
    {
        _pendingShow = false;
        if (_tooltipPanel != null)
        {
            _tooltipPanel.Visible = false;
        }
    }

    private void ApplyTooltipContent(string title, string body)
    {
        if (_tooltipPanel == null || _titleLabel == null || _bodyLabel == null) return;

        bool hasTitle = !string.IsNullOrEmpty(title);

        _titleLabel.Text = title;
        _titleLabel.Visible = hasTitle;

        // Also toggle the accent line
        Node? accentLine = _contentContainer?.GetNodeOrNull("AccentLine");
        if (accentLine is ColorRect line)
        {
            line.Visible = hasTitle;
        }

        _bodyLabel.Text = body;
        _tooltipPanel.Visible = true;

        // Reset size so it re-calculates
        _tooltipPanel.ResetSize();
    }
}
