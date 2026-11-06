using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using RenderingLibrary.Graphics;

namespace MonoGame_project.Game.UI;

/// <summary>Pause-menu visuals and their local button action; no input-context logic.</summary>
internal sealed class PauseMenuView : IDisposable
{
    private const float MaxPanelWidth = 360f;
    private const float MaxPanelHeight = 136f;
    private const float EdgeInset = 12f;
    private const float MaxContentWidth = 312f;
    private const float MaxButtonWidth = 264f;

    private readonly ContainerRuntime _root = new();
    private readonly ContainerRuntime _panel;
    private readonly TextRuntime _resumeHint;
    private readonly Button _exitButton;
    private readonly Action _exit;

    public PauseMenuView(GameUiTheme theme, Action exit)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        _root.Dock(Dock.Fill);
        _root.Children.Add(theme.CreateOverlay());

        _panel = theme.CreatePausePanel();
        _panel.Anchor(Anchor.Center);
        _panel.Width = -2f * EdgeInset;
        _panel.WidthUnits = DimensionUnitType.RelativeToParent;
        _panel.MinWidth = 1f;
        _panel.MaxWidth = MaxPanelWidth;
        _panel.Height = -2f * EdgeInset;
        _panel.HeightUnits = DimensionUnitType.RelativeToParent;
        _panel.MinHeight = 1f;
        _panel.MaxHeight = MaxPanelHeight;
        _root.Children.Add(_panel);

        var content = new ContainerRuntime
        {
            Width = -24f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            MinWidth = 1f,
            MaxWidth = MaxContentWidth,
            Height = 0f,
            HeightUnits = DimensionUnitType.RelativeToChildren,
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            StackSpacing = 8f
        };
        content.Anchor(Anchor.Center);
        _panel.Children.Add(content);

        var heading = UiText.Create(20f, GameUiTheme.TextPrimary, 24f);
        heading.Text = "Paused";
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(heading);

        _exitButton = theme.CreatePauseButton("Exit game");
        _exitButton.Anchor(Anchor.CenterHorizontally);
        _exitButton.Width = 0f;
        _exitButton.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _exitButton.Visual.MaxWidth = MaxButtonWidth;
        _exitButton.Height = 34f;
        _exitButton.Click += OnExitClicked;
        content.AddChild(_exitButton);

        _resumeHint = UiText.Create(12f, GameUiTheme.TextSecondary, 18f);
        _resumeHint.Text = "Esc to resume";
        _resumeHint.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(_resumeHint);
    }

    public GraphicalUiElement Visual => _root;

    public void RefreshResponsiveVisibility() =>
        _resumeHint.Visible = _panel.AbsoluteHeight >= 124f;

    public void Dispose() => _exitButton.Click -= OnExitClicked;

    private void OnExitClicked(object? sender, EventArgs args) => _exit();
}
