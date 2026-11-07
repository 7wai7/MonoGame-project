using Gum;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Small typed bridge between the editable Gum project and live game data.</summary>
internal static class GumHudProject
{
    public static GraphicalUiElement CreateScreen(string name)
    {
        var save = ObjectFinder.Self.GumProjectSave?.Screens
            .FirstOrDefault(screen => screen.Name == name)
            ?? throw new InvalidOperationException($"Gum screen '{name}' was not loaded.");
        return save.ToGraphicalUiElement();
    }

    public static GraphicalUiElement Require(GraphicalUiElement root, string name) =>
        root.GetGraphicalUiElementByName(name)
        ?? throw new InvalidOperationException($"Gum element '{name}' is missing from the layout.");

    public static void SetText(GraphicalUiElement element, string value)
    {
        if (element is TextRuntime text)
        {
            if (text.Text != value) text.Text = value;
        }
        else
        {
            element.SetProperty("Text", value);
        }
    }
}

internal static class HudPalette
{
    public static readonly Color Text = new(237, 243, 248);
    public static readonly Color Muted = new(210, 223, 231);
    public static readonly Color Accent = new(169, 210, 255);
}

/// <summary>Attaches actions to a button whose visuals and dimensions live in HudButton.gucx.</summary>
internal sealed class GumHudButton : IDisposable
{
    private readonly InteractiveGue _root;
    private readonly GraphicalUiElement _surface;
    private readonly GraphicalUiElement _label;
    private readonly Action _clicked;

    public GumHudButton(GraphicalUiElement parent, string name, string label, Action clicked)
    {
        _root = GumHudProject.Require(parent, name) as InteractiveGue
            ?? throw new InvalidOperationException($"Gum button '{name}' is not interactive.");
        _surface = GumHudProject.Require(_root, "Surface");
        _label = GumHudProject.Require(_root, "Label");
        _clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
        GumHudProject.SetText(_label, label);
        _root.HasEvents = true;
        _root.Click += OnClick;
        _root.RollOn += OnRollOn;
        _root.RollOff += OnRollOff;
    }

    public bool Visible
    {
        get => _root.Visible;
        set => _root.Visible = value;
    }

    public void Dispose()
    {
        _root.Click -= OnClick;
        _root.RollOn -= OnRollOn;
        _root.RollOff -= OnRollOff;
    }

    private void OnClick(object? sender, EventArgs e) => _clicked();

    private void OnRollOn(object? sender, EventArgs e)
    {
        SetSurface(35, 65, 79);
        SetLabel(169, 210, 255);
    }

    private void OnRollOff(object? sender, EventArgs e)
    {
        SetSurface(27, 48, 61);
        SetLabel(237, 243, 248);
    }

    private void SetSurface(int red, int green, int blue)
    {
        _surface.SetProperty("Red", red);
        _surface.SetProperty("Green", green);
        _surface.SetProperty("Blue", blue);
    }

    private void SetLabel(int red, int green, int blue)
    {
        _label.SetProperty("Red", red);
        _label.SetProperty("Green", green);
        _label.SetProperty("Blue", blue);
    }
}
