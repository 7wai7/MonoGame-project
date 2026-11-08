using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Wireframe;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

internal static class UiText
{
    public static float RasterScale { get; set; } = 1f;

    public static void PrepareFontResolution(GraphicalUiElement element)
    {
        if (element is TextRuntime text && text.Font == "Poppins")
        {
            text.FontSize *= RasterScale;
            text.FontScale /= RasterScale;
        }

        foreach (var child in element.Children)
        {
            PrepareFontResolution(child);
        }
    }

    public static TextRuntime Create(float fontSize, Color color, float height) => new()
    {
        Width = 0f,
        WidthUnits = DimensionUnitType.RelativeToParent,
        Height = height,
        HeightUnits = DimensionUnitType.Absolute,
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
