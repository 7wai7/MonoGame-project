namespace MonoGame_project.Ecs;

/// <summary>
/// A versioned entity handle. The generation prevents stale handles from
/// becoming valid when an id is recycled.
/// </summary>
public readonly record struct Entity(int Id, int Generation)
{
    public static Entity None { get; } = new(-1, 0);
    public bool IsNone => Id < 0;

    public override string ToString() => IsNone ? "Entity.None" : $"Entity({Id}:{Generation})";
}
