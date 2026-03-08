using Godot;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class CombatUI : Control
{
    private Label? _label;
    private string _turnText = string.Empty;
    private string _commanderText = string.Empty;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _label = GetNodeOrNull<Label>("CombatStatus") ?? new Label();
        if (_label.GetParent() == null)
        {
            _label.Name = "CombatStatus";
            AddChild(_label);
        }

        _label.Position = new Vector2(24f, 24f);
        _label.AddThemeFontSizeOverride("font_size", 22);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged += OnPhaseChanged;
            EventBus.Instance.TurnChanged += OnTurnChanged;
            EventBus.Instance.CommanderDamaged += OnCommanderDamaged;
        }

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PhaseChanged -= OnPhaseChanged;
            EventBus.Instance.TurnChanged -= OnTurnChanged;
            EventBus.Instance.CommanderDamaged -= OnCommanderDamaged;
        }
    }

    public override void _Process(double delta)
    {
        if (_label != null)
        {
            _label.Text = $"{_turnText}\n{_commanderText}\nPress Space to fire";
        }
    }

    private void OnTurnChanged(TurnChangedEvent payload)
    {
        _turnText = $"Turn: {payload.CurrentPlayer}  Round: {payload.RoundNumber}  Timer: {payload.TurnTimeSeconds:0}";
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Combat;
    }

    private void OnCommanderDamaged(CommanderDamagedEvent payload)
    {
        _commanderText = $"Commander hit: {payload.Player} HP {payload.RemainingHealth}";
    }
}
