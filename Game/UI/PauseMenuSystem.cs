using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Game.UI;

/// <summary>Connects the pause menu to input contexts and application actions.</summary>
public sealed class PauseMenuSystem : GameSystem, IUpdateSystem
{
    private PauseMenuView _view = null!;

    protected override void OnInitialize()
    {
        _view = new PauseMenuView(
            World.GetResource<GameUiTheme>(),
            OnExitRequested);
    }

    public void Update(World world, in FrameTime time)
    {
        if (!Context.Input.WasKeyPressed(Keys.Escape))
        {
            return;
        }

        if (Context.InputContexts.Current == InputContexts.PauseMenu)
        {
            _view.IsVisible = false;
            Context.InputContexts.Pop(InputContexts.PauseMenu);
        }
        else if (Context.InputContexts.Current == InputContexts.InspectViewport ||
                 Context.InputContexts.Current == InputContexts.InspectCursor)
        {
            Context.InputContexts.Push(InputContexts.PauseMenu);
            _view.IsVisible = true;
        }
    }

    protected override void OnShutdown()
    {
        _view.Dispose();
        _view = null!;
    }

    private void OnExitRequested()
    {
        if (Context.InputContexts.CanReceiveInput(InputContexts.PauseMenu))
        {
            Context.Application.RequestExit();
        }
    }
}
