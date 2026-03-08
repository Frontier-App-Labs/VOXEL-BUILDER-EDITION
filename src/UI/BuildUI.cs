using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class BuildUI : Control
{
    private Label? _label;
    private string _phaseText = "Building";
    private string _budgetText = string.Empty;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _label = GetNodeOrNull<Label>("Status") ?? new Label();
        if (_label.GetParent() == null)
        {
            _label.Name = "Status";
            AddChild(_label);
        }

        _label.Position = new Vector2(24f, 24f);
        _label.AddThemeFontSizeOverride("font_size", 22);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.BudgetChanged += OnBudgetChanged;
        }

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
        if (_label != null)
        {
            _label.Text = $"{_phaseText}\n{_budgetText}";
        }
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        _phaseText = $"Phase: {payload.CurrentPhase}";
        Visible = payload.CurrentPhase == GamePhase.Building || payload.CurrentPhase == GamePhase.FogReveal;
    }

    private void OnBudgetChanged(BudgetChangedEvent payload)
    {
        _budgetText = $"{payload.Player}: ${payload.NewBudget}";
    }
}
