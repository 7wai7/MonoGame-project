using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Owns the pause dialog, its input context and its exit action.</summary>
public sealed class PauseMenuScreen : UiScreen
{
    private readonly EngineContext _context;
    private readonly ContainerRuntime _root = new();
    private readonly Button _exitButton;
    private bool _disposed;

    public PauseMenuScreen(EngineContext context, GameUiTheme theme)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(theme);

        _root.Dock(Dock.Fill);
        _root.Children.Add(theme.CreateOverlay());

        var panel = theme.CreatePanel(width: 400f, height: 220f);
        panel.Anchor(Anchor.Center);
        _root.Children.Add(panel);

        _exitButton = theme.CreateButton("EXIT GAME", width: 160f, height: 22f);
        _exitButton.Anchor(Anchor.Center);
        _exitButton.Click += OnExitClicked;
        panel.AddChild(_exitButton);
    }

    public override GraphicalUiElement Visual => _root;

    public override void HandleInput()
    {
        if (!_context.Input.WasKeyPressed(Keys.Escape))
        {
            return;
        }

        if (_context.InputContexts.Current == InputContexts.PauseMenu)
        {
            _context.Ui.Scene.SetModalVisible(this, false);
            _context.InputContexts.Pop(InputContexts.PauseMenu);
        }
        else if (_context.InputContexts.Current == InputContexts.InspectViewport ||
                 _context.InputContexts.Current == InputContexts.InspectCursor)
        {
            _context.InputContexts.Push(InputContexts.PauseMenu);
            _context.Ui.Scene.SetModalVisible(this, true);
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _exitButton.Click -= OnExitClicked;
        _disposed = true;
    }

    private void OnExitClicked(object? sender, EventArgs args)
    {
        if (_context.InputContexts.CanReceiveInput(InputContexts.PauseMenu))
        {
            _context.Application.RequestExit();
        }
    }
}
