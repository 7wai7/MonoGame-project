using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
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
    private readonly ContainerRuntime _panel;
    private readonly ContainerRuntime _content;
    private readonly TextRuntime _resumeHint;
    private readonly Button _exitButton;
    private int _viewportWidth;
    private int _viewportHeight;
    private bool _disposed;

    public PauseMenuScreen(EngineContext context, GameUiTheme theme)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(theme);

        _root.Dock(Dock.Fill);
        _root.Children.Add(theme.CreateOverlay());

        _panel = theme.CreatePausePanel(width: 360f, height: 136f);
        _panel.Anchor(Anchor.Center);
        _root.Children.Add(_panel);

        _content = new ContainerRuntime
        {
            Width = -48f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 92f,
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            StackSpacing = 8f
        };
        _content.Anchor(Anchor.Center);
        _panel.Children.Add(_content);

        var heading = new TextRuntime
        {
            Text = "Paused",
            Width = 0f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 24f,
            FontSize = 20f,
            UseFontSmoothing = true,
            Color = GameUiTheme.TextPrimary,
            HorizontalAlignment = RenderingLibrary.Graphics.HorizontalAlignment.Center
        };
        _content.Children.Add(heading);

        _exitButton = theme.CreatePauseButton("Exit game", width: 1f, height: 34f);
        _exitButton.Anchor(Anchor.CenterHorizontally);
        _exitButton.WidthUnits = DimensionUnitType.RelativeToParent;
        _exitButton.Width = -48f;
        _exitButton.Click += OnExitClicked;
        _content.AddChild(_exitButton);

        _resumeHint = new TextRuntime
        {
            Text = "Esc to resume",
            Width = 0f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 18f,
            FontSize = 12f,
            UseFontSmoothing = true,
            Color = GameUiTheme.TextSecondary,
            HorizontalAlignment = RenderingLibrary.Graphics.HorizontalAlignment.Center
        };
        _content.Children.Add(_resumeHint);
    }

    public override GraphicalUiElement Visual => _root;

    public override void Refresh()
    {
        var viewport = _context.GraphicsDevice.Viewport;
        if (viewport.Width == _viewportWidth && viewport.Height == _viewportHeight)
        {
            return;
        }

        _viewportWidth = viewport.Width;
        _viewportHeight = viewport.Height;
        _panel.Width = MathF.Min(360f, MathF.Max(1f, viewport.Width - 24f));
        _panel.Height = MathF.Min(136f, MathF.Max(1f, viewport.Height - 24f));
        var compactWidth = _panel.Width < 240f;
        _content.Width = compactWidth ? -24f : -48f;
        _exitButton.Width = compactWidth ? 0f : -48f;
        _resumeHint.Visible = _panel.Height >= 124f;
        _content.Height = _resumeHint.Visible ? 92f : 66f;
    }

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
