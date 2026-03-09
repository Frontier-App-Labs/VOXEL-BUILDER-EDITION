using Godot;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.Utility;

public partial class DebugOverlay : CanvasLayer
{
    private Label? _label;

    [Export]
    public NodePath? GameManagerPath { get; set; }

    [Export]
    public NodePath? VoxelWorldPath { get; set; }

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        if (_label == null)
        {
            _label = new Label();
            _label.Name = "Label";
            AddChild(_label);
        }

        _label.Position = new Vector2(12f, 12f);
    }

    public override void _Process(double delta)
    {
        if (_label == null)
        {
            return;
        }

        GameManager? gameManager = GameManagerPath is null ? GetTree().Root.GetNodeOrNull<GameManager>("Main") : GetNodeOrNull<GameManager>(GameManagerPath);
        VoxelWorld? voxelWorld = VoxelWorldPath is null ? GetTree().Root.GetNodeOrNull<VoxelWorld>("Main/GameWorld") : GetNodeOrNull<VoxelWorld>(VoxelWorldPath);
        int chunkCount = voxelWorld?.ChunkCount ?? 0;
        string phaseText = gameManager?.CurrentPhase.ToString() ?? "Unknown";
        _label.Text = $"FPS: {Engine.GetFramesPerSecond()}\nPhase: {phaseText}\nChunks: {chunkCount}";
    }
}
