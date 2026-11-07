using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Owns the pause input context and connects the menu to game actions.</summary>
internal sealed class PauseMenuScreen : UiScreen
{
    private readonly EngineContext _context;
    private readonly GraphicalUiElement _visual;
    private readonly GumHudButton _exitButton;
    private bool _disposed;

    public PauseMenuScreen(EngineContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _visual = GumHudProject.CreateScreen("PauseMenu");
        _exitButton = new GumHudButton(_visual, "ExitButton", "EXIT", RequestExit);
    }

    public override GraphicalUiElement Visual => _visual;

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

        _exitButton.Dispose();
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
