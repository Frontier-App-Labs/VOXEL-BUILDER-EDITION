using Godot;
using System.Text;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class ScoreboardUI : Control
{
    [Export]
    public NodePath? GameManagerPath { get; set; }

    private Label? _label;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopRight);
        _label = GetNodeOrNull<Label>("Players") ?? new Label();
        if (_label.GetParent() == null)
        {
            _label.Name = "Players";
            AddChild(_label);
        }

        _label.Position = new Vector2(0f, 0f);
        _label.AddThemeFontSizeOverride("font_size", 18);
    }

    public override void _Process(double delta)
    {
        if (_label == null)
        {
            return;
        }

        GameManager? gameManager = GameManagerPath is null ? GetTree().Root.GetNodeOrNull<GameManager>("Main") : GetNodeOrNull<GameManager>(GameManagerPath);
        if (gameManager == null)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        foreach (PlayerData player in gameManager.Players.Values)
        {
            builder.AppendLine($"{player.DisplayName}: HP {player.CommanderHealth}  Budget {player.Budget} {(player.IsAlive ? string.Empty : "ELIMINATED")}");
        }

        _label.Text = builder.ToString();
    }
}
