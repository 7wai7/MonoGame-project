using System.Collections.Generic;

namespace MonoGame_project.Engine.Input;

/// <summary>
/// Routes input through ordered contexts and applies the mouse mode requested by
/// the context at the top of the stack.
/// </summary>
public sealed class InputContextStack
{
    private readonly InputService _input;
    private readonly List<InputContext> _contexts = [];

    internal InputContextStack(InputService input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
    }

    public int Count => _contexts.Count;
    public InputContext? Current => _contexts.Count == 0 ? null : _contexts[^1];

    /// <summary>Replaces the complete stack with one root context.</summary>
    public void Reset(InputContext rootContext)
    {
        ArgumentNullException.ThrowIfNull(rootContext);
        _contexts.Clear();
        _contexts.Add(rootContext);
        ApplyCurrentMouseMode();
    }

    /// <summary>Adds a context above all currently active contexts.</summary>
    public void Push(InputContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_contexts.Contains(context))
        {
            throw new InvalidOperationException(
                $"Input context '{context.Name}' is already in the stack.");
        }

        _contexts.Add(context);
        ApplyCurrentMouseMode();
    }

    /// <summary>Removes the expected top context.</summary>
    public void Pop(InputContext expectedContext)
    {
        ArgumentNullException.ThrowIfNull(expectedContext);
        if (_contexts.Count == 0)
        {
            throw new InvalidOperationException("The input context stack is empty.");
        }

        var current = _contexts[^1];
        if (current != expectedContext)
        {
            throw new InvalidOperationException(
                $"Cannot pop input context '{expectedContext.Name}' because " +
                $"'{current.Name}' is currently on top.");
        }

        _contexts.RemoveAt(_contexts.Count - 1);
        ApplyCurrentMouseMode();
    }

    /// <summary>Removes every context and restores a free cursor.</summary>
    public void Clear()
    {
        _contexts.Clear();
        ApplyCurrentMouseMode();
    }

    /// <summary>
    /// Returns whether the context is present, is not blocked by a context above
    /// it, and has completed any required relative-mouse activation click.
    /// </summary>
    public bool CanReceiveInput(InputContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        for (var index = _contexts.Count - 1; index >= 0; index--)
        {
            var candidate = _contexts[index];
            if (candidate == context)
            {
                return candidate.MouseMode != MouseMode.Relative ||
                       _input.IsRelativeMouseCaptured;
            }

            if (candidate.BlocksLowerInput)
            {
                return false;
            }
        }

        return false;
    }

    private void ApplyCurrentMouseMode()
    {
        _input.SetMouseMode(Current?.MouseMode ?? MouseMode.Free);
    }
}
