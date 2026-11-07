using Gum.GueDeriving;
using Gum.Wireframe;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Reusable one-line Gum row with optional click and hover behavior.</summary>
internal sealed class UiTextRow : IDisposable
{
    private readonly TextRuntime _text;
    private readonly Action _clicked;
    private readonly Color _accent;
    private Color _baseColor;
    private bool _clickable;
    private bool _hovered;

    public UiTextRow(float height, Action clicked)
    {
        _clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
        _accent = HudPalette.Accent;
        _text = UiText.Create(16f, HudPalette.Text, height);
        _text.Font = "Poppins";
        _text.HasEvents = true;
        _text.Click += OnClick;
        _text.RollOn += OnRollOn;
        _text.RollOff += OnRollOff;
    }

    public GraphicalUiElement Visual => _text;

    public void Set(string text, Color color, bool clickable)
    {
        UiText.Set(_text, text);
        _baseColor = color;
        _clickable = clickable;
        _text.Color = _hovered && _clickable ? _accent : color;
    }

    public void Dispose()
    {
        _text.Click -= OnClick;
        _text.RollOn -= OnRollOn;
        _text.RollOff -= OnRollOff;
    }

    private void OnClick(object? sender, EventArgs args)
    {
        if (_clickable)
        {
            _clicked();
        }
    }

    private void OnRollOn(object? sender, EventArgs args)
    {
        _hovered = true;
        if (_clickable)
        {
            _text.Color = _accent;
        }
    }

    private void OnRollOff(object? sender, EventArgs args)
    {
        _hovered = false;
        _text.Color = _baseColor;
    }
}
