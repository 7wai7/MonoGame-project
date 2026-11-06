using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Owns the pause input context and connects the menu to game actions.</summary>
public sealed class PauseMenuScreen : UiScreen
{
    private readonly EngineContext _context;
    private readonly PauseMenuView _view;
    private bool _disposed;

    public PauseMenuScreen(EngineContext context, GameUiTheme theme)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _view = new PauseMenuView(theme, RequestExit);
    }

    public override GraphicalUiElement Visual => _view.Visual;

    public override void Refresh() => _view.RefreshResponsiveVisibility();

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

        _view.Dispose();
        _disposed = true;
    }

    private void RequestExit()
    {
        if (_context.InputContexts.CanReceiveInput(InputContexts.PauseMenu))
        {
            _context.Application.RequestExit();
        }
    }
}
