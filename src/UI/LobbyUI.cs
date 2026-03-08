using Godot;
using System.Text;
using VoxelSiege.Networking;

namespace VoxelSiege.UI;

public partial class LobbyUI : Control
{
    [Export]
    public NodePath? LobbyManagerPath { get; set; }

    private Label? _label;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Members") ?? new Label();
        if (_label.GetParent() == null)
        {
            _label.Name = "Members";
            AddChild(_label);
        }
    }

    public override void _Process(double delta)
    {
        if (_label == null)
        {
            return;
        }

        LobbyManager? lobby = LobbyManagerPath is null ? GetTree().Root.GetNodeOrNull<LobbyManager>("Main/LobbyManager") : GetNodeOrNull<LobbyManager>(LobbyManagerPath);
        if (lobby == null)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(lobby.LobbyName);
        foreach (LobbyMember member in lobby.Members.Values)
        {
            builder.AppendLine($"{member.DisplayName} [{member.Slot}] {(member.Ready ? "Ready" : "Waiting")}");
        }

        _label.Text = builder.ToString();
    }
}
