using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.Managers;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Styles existing Gum buttons without owning their layout or actions.</summary>
internal static class UiButtonStyles
{
    public static void ApplyStandard(Button button,
        GameUiTheme theme, GameUiTextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(textures);
        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = textures.GetFrameTexture(
            0, 1, theme.ButtonFill, theme.ButtonBorder);
        visual.Background.TextureAddress = TextureAddress.EntireTexture;
        visual.Background.Dock(Gum.Wireframe.Dock.Fill);
        visual.TextInstance.Dock(Gum.Wireframe.Dock.Fill);
        visual.BackgroundColor = Color.White;
        visual.ForegroundColor = theme.TextPrimary;
        visual.FocusedIndicatorColor = theme.Accent;
        visual.TextInstance.FontSize = 16f;
        visual.TextInstance.UseFontSmoothing = true;
    }

    public static void ApplyMenu(Button button,
        GameUiTheme theme, GameUiTextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(textures);
        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = textures.GetNoiseTexture(new UiNoiseTextureSpec(
            Width: 192, Height: 48, BaseColor: theme.MenuButtonFill,
            Opacity: 220, FadeWidth: 12f, NoiseStrength: 2f));
        visual.Background.TextureAddress = TextureAddress.EntireTexture;
        visual.Background.Dock(Gum.Wireframe.Dock.Fill);
        visual.TextInstance.Dock(Gum.Wireframe.Dock.Fill);
        visual.BackgroundColor = Color.White;
        visual.ForegroundColor = theme.TextPrimary;
        visual.FocusedIndicator.Visible = false;
        visual.TextInstance.FontSize = 15f;
        visual.TextInstance.UseFontSmoothing = true;

        visual.States.Enabled.Clear();
        visual.States.Enabled.Apply = () => ApplyColors(visual,
            Color.White, theme.TextPrimary);
        visual.States.Highlighted.Clear();
        visual.States.Highlighted.Apply = () => ApplyColors(visual,
            theme.MenuButtonHover, Color.White);
        visual.States.HighlightedFocused.Clear();
        visual.States.HighlightedFocused.Apply = visual.States.Highlighted.Apply;
        visual.States.Pushed.Clear();
        visual.States.Pushed.Apply = () => ApplyColors(visual,
            theme.MenuButtonPressed, Color.White);
        visual.States.Focused.Clear();
        visual.States.Focused.Apply = () => ApplyColors(visual,
            theme.MenuButtonFocused, Color.White);
        visual.States.Disabled.Clear();
        visual.States.Disabled.Apply = () => ApplyColors(visual,
            theme.MenuButtonDisabled, theme.TextSecondary);
        visual.States.DisabledFocused.Clear();
        visual.States.DisabledFocused.Apply = visual.States.Disabled.Apply;
        button.UpdateState();
    }

    private static void ApplyColors(ButtonVisual visual,
        Color background, Color text)
    {
        visual.Background.Color = background;
        visual.TextInstance.Color = text;
    }
}
