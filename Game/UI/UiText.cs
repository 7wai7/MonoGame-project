using Gum.DataTypes;
using Gum.GueDeriving;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

internal static class UiText
{
    public static TextRuntime Create(float fontSize, Color color, float height) => new()
    {
        Width = 0f,
        WidthUnits = DimensionUnitType.RelativeToParent,
        Height = height,
        FontSize = fontSize,
        UseFontSmoothing = true,
        Color = color,
        IsTruncatingWithEllipsisOnLastLine = true
    };

    public static void Set(TextRuntime target, string value)
    {
        if (target.Text != value)
        {
            target.Text = value;
        }
    }
}
