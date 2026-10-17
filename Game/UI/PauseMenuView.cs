using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>
/// Visual tree for the pause menu. The scene decides what its button does.
/// </summary>
public sealed class PauseMenuView : IDisposable
{
    private readonly ContainerRuntime _root = new();
    private bool _disposed;

    public PauseMenuView(GameUiTheme theme, Action onExitRequested)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        BuildVisualTree(theme, onExitRequested);
        _root.Visible = false;
        _root.AddToRoot();
    }

    public bool IsVisible
    {
        get => _root.Visible;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _root.Visible = value;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _root.RemoveFromRoot();
        _disposed = true;
    }

    private void BuildVisualTree(GameUiTheme theme, Action onExitRequested)
    {
        _root.Dock(Dock.Fill);
        _root.Children.Add(theme.CreateOverlay());

        var panel = theme.CreatePanel(width: 400f, height: 220f);
        panel.Anchor(Anchor.Center);
        _root.Children.Add(panel);

        var exitButton = theme.CreateButton(
            "EXIT GAME",
            width: 160f,
            height: 22f,
            onExitRequested);
        exitButton.Anchor(Anchor.Center);
        panel.AddChild(exitButton);
    }
}
