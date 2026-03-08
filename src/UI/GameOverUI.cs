using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class GameOverUI : Control
{
    private Label? _label;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _label = GetNodeOrNull<Label>("Winner") ?? new Label();
        if (_label.GetParent() == null)
        {
            _label.Name = "Winner";
            AddChild(_label);
        }

        _label.Position = new Vector2(760f, 420f);
        _label.AddThemeFontSizeOverride("font_size", 32);
        Visible = false;

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
        if (_label != null && Visible)
        {
            _label.Text = "Game Over";
        }
    }
}
