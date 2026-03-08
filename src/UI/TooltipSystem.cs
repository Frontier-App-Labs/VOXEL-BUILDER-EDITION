using Godot;

namespace VoxelSiege.UI;

public partial class TooltipSystem : CanvasLayer
{
    private Label? _tooltip;

    public override void _Ready()
    {
        _tooltip = GetNodeOrNull<Label>("Tooltip") ?? new Label();
        if (_tooltip.GetParent() == null)
        {
            _tooltip.Name = "Tooltip";
            AddChild(_tooltip);
        }

        _tooltip.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_tooltip != null && _tooltip.Visible)
        {
            _tooltip.Position = GetViewport().GetMousePosition() + new Vector2(16f, 16f);
        }
    }

    public void ShowTooltip(string text)
    {
        if (_tooltip == null)
        {
            return;
        }

        _tooltip.Text = text;
        _tooltip.Visible = true;
    }

    public void HideTooltip()
    {
        if (_tooltip != null)
        {
            _tooltip.Visible = false;
        }
    }
}
