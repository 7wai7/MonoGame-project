namespace MonoGame_project.Ecs;

/// <summary>A boxed, read-only copy of one ECS component at the time of inspection.</summary>
public readonly record struct ComponentSnapshot(Type Type, object Value);
