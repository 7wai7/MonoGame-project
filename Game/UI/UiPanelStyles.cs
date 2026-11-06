using Gum.GueDeriving;
using Gum.Managers;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Applies panel appearance to backgrounds owned by UI views.</summary>
internal static class UiPanelStyles
{
    public static void ApplyFrame(NineSliceRuntime background,
        GameUiTheme theme, GameUiTextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(textures);
        background.Texture = textures.GetFrameTexture(
            0, 1, theme.PanelFill, theme.PanelBorder);
        background.TextureAddress = TextureAddress.EntireTexture;
        background.Color = Color.White;
    }

    public static void ApplyMenuBackdrop(SpriteRuntime background,
        GameUiTheme theme, GameUiTextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(textures);
        background.Texture = textures.GetNoiseTexture(new UiNoiseTextureSpec(
            Width: 360, Height: 136, BaseColor: theme.MenuPanelFill,
            Opacity: 230, FadeWidth: 52f, NoiseStrength: 13f));
        background.Color = Color.White;
    }
}
