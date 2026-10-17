namespace MonoGame_project.Engine;

/// <summary>Passes application-level requests from a scene to the MonoGame host.</summary>
public sealed class ApplicationControl
{
    public bool ExitRequested { get; private set; }

    public void RequestExit() => ExitRequested = true;
}
