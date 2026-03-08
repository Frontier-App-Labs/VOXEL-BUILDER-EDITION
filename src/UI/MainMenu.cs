using Godot;
using System;
using VoxelSiege.Core;

namespace VoxelSiege.UI;

public partial class MainMenu : Control
{
    public event Action? PlayOnlineRequested;
    public event Action? PlayBotsRequested;
    public event Action? SandboxRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    private VBoxContainer? _menuStack;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        ColorRect backdrop = new ColorRect();
        backdrop.Name = "Backdrop";
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(0.06f, 0.08f, 0.12f, 0.82f);
        AddChild(backdrop);

        PanelContainer panel = new PanelContainer();
        panel.Name = "Panel";
        panel.Size = new Vector2(460f, 360f);
        panel.Position = new Vector2(70f, 70f);
        AddChild(panel);

        _menuStack = new VBoxContainer();
        _menuStack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _menuStack.SizeFlagsVertical = SizeFlags.ExpandFill;
        _menuStack.AddThemeConstantOverride("separation", 12);
        panel.AddChild(_menuStack);

        Label title = new Label();
        title.Text = "VOXEL SIEGE";
        title.AddThemeFontSizeOverride("font_size", 34);
        _menuStack.AddChild(title);

        Label subtitle = new Label();
        subtitle.Text = "Build fortified strongholds, conceal your commander, and tear apart enemy defenses.";
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _menuStack.AddChild(subtitle);

        _menuStack.AddChild(CreateButton("Start Prototype Match", OnStartPrototypePressed));
        _menuStack.AddChild(CreateButton("Play Vs Bots", () => PlayBotsRequested?.Invoke()));
        _menuStack.AddChild(CreateButton("Sandbox", () => SandboxRequested?.Invoke()));
        _menuStack.AddChild(CreateButton("Settings", () => SettingsRequested?.Invoke()));
        _menuStack.AddChild(CreateButton("Quit", OnQuitPressed));

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

    public void RequestPlayOnline() => PlayOnlineRequested?.Invoke();
    public void RequestPlayBots() => PlayBotsRequested?.Invoke();
    public void RequestSandbox() => SandboxRequested?.Invoke();
    public void RequestSettings() => SettingsRequested?.Invoke();
    public void RequestQuit() => QuitRequested?.Invoke();

    private Button CreateButton(string text, Action handler)
    {
        Button button = new Button();
        button.Text = text;
        button.CustomMinimumSize = new Vector2(0f, 42f);
        button.Pressed += handler;
        return button;
    }

    private void OnStartPrototypePressed()
    {
        GameManager? gameManager = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        gameManager?.StartPrototypeMatch();
        Visible = false;
    }

    private void OnQuitPressed()
    {
        QuitRequested?.Invoke();
        GetTree().Quit();
    }

    private void OnPhaseChanged(PhaseChangedEvent payload)
    {
        Visible = payload.CurrentPhase == GamePhase.Menu;
    }
}
