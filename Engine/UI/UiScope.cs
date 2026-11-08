using Gum.Wireframe;

namespace MonoGame_project.Engine.UI;

/// <summary>Owns all UI mounted for one scene or the application lifetime.</summary>
public sealed class UiScope : IDisposable
{
    private readonly GraphicalUiElement _root;
    private readonly GraphicalUiElement _modalRoot;
    private readonly List<MountedView> _views = [];
    private readonly List<IDisposable> _resources = [];
    private bool _disposed;

    internal bool SuspendsScene => _views.Exists(mount =>
        mount.Parent is not null && mount.View.SuspendsScene);

    internal UiScope(GraphicalUiElement root,
        GraphicalUiElement modalRoot)
    {
        _root = root;
        _modalRoot = modalRoot;
    }

    /// <summary>Shows a normal screen above previously mounted screens.</summary>
    public void Mount(UiScreen view)
    {
        ValidateNewView(view);
        _root.Children.Add(view.Visual);
        _views.Add(new MountedView(view, isModal: false, parent: _root));
    }

    /// <summary>Registers a dialog, initially closed, for Gum's modal root.</summary>
    public void MountModal(UiScreen view)
    {
        ValidateNewView(view);
        _views.Add(new MountedView(view, isModal: true, parent: null));
    }

    private void ValidateNewView(UiScreen view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(view);
        if (_views.Exists(entry => ReferenceEquals(entry.View, view)))
        {
            throw new InvalidOperationException("The same UI view is already mounted in this scope.");
        }

        if (view.Visual.Parent is not null)
        {
            throw new InvalidOperationException("The UI view already has a parent.");
        }
    }

    /// <summary>Detaches and disposes a screen before the whole scene ends.</summary>
    public bool Unmount(UiScreen view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(view);
        var index = _views.FindIndex(entry => ReferenceEquals(entry.View, view));
        if (index < 0)
        {
            return false;
        }

        var mount = _views[index];
        if (mount.Parent is not null)
        {
            view.Visual.RemoveFromManagers();
            mount.Parent.Children.Remove(view.Visual);
        }
        view.Dispose();
        _views.RemoveAt(index);
        return true;
    }

    /// <summary>Attaches open dialogs to Gum's modal input root.</summary>
    public void SetModalVisible(UiScreen view, bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mount = _views.Find(entry => ReferenceEquals(entry.View, view));
        if (mount is null || !mount.IsModal)
        {
            throw new InvalidOperationException("The view is not mounted as a modal dialog.");
        }

        if (visible && mount.Parent is null)
        {
            _modalRoot.Children.Add(view.Visual);
            mount.Parent = _modalRoot;
        }
        else if (!visible && mount.Parent is not null)
        {
            view.Visual.RemoveFromManagers();
            mount.Parent.Children.Remove(view.Visual);
            mount.Parent = null;
        }
    }

    public void Own(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
    }

    internal void HandleInput()
    {
        foreach (var view in _views)
        {
            view.View.HandleInput();
        }
    }

    internal void Refresh()
    {
        foreach (var view in _views)
        {
            view.View.Refresh();
        }
    }

    internal void AfterSceneDraw()
    {
        foreach (var view in _views)
        {
            view.View.AfterSceneDraw();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        while (_views.Count > 0)
        {
            Unmount(_views[^1].View);
        }

        _views.Clear();
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            _resources[index].Dispose();
        }

        _resources.Clear();
        _disposed = true;
    }

    private sealed class MountedView(UiScreen view, bool isModal, GraphicalUiElement? parent)
    {
        public UiScreen View { get; } = view;
        public bool IsModal { get; } = isModal;
        public GraphicalUiElement? Parent { get; set; } = parent;
    }
}
