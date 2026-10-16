namespace MonoGame_project.Engine.Input;

/// <summary>
/// Describes one input layer. Contexts near the top of the stack receive input
/// first and may block contexts below them.
/// </summary>
public sealed record InputContext
{
    public InputContext(
        string name,
        MouseMode mouseMode,
        bool blocksLowerInput = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        MouseMode = mouseMode;
        BlocksLowerInput = blocksLowerInput;
    }

    public string Name { get; }
    public MouseMode MouseMode { get; }
    public bool BlocksLowerInput { get; }

    public override string ToString() => Name;
}
