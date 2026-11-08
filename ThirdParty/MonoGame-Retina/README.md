# Local MonoGame Retina package

`../Packages/MonoGame.Framework.DesktopGL.3.8.6.1-retina.2.nupkg` is a local
build of [MonoGame v3.8.6](https://github.com/MonoGame/MonoGame/releases/tag/v3.8.6)
with the macOS HiDPI changes from [MonoGame PR #9462](https://github.com/MonoGame/MonoGame/pull/9462).
The patch in this directory adapts that PR's resize hunk to v3.8.6. It is
used only by this game; it is not an upstream contribution. MonoGame's
Microsoft Public License is included as `MonoGame-LICENSE.txt`.

The local NuGet feed is declared in `../../NuGet.Config`. The package keeps
the assembly version at `3.8.6.0` for compatibility with other project
dependencies. Its package version is distinct so it cannot be confused with
an official MonoGame release.

To rebuild the package from source:

1. Check out the official MonoGame `v3.8.6` tag and initialize its
   `ThirdParty/StbImageSharp` (`0ec6394bb7184e0576a3fa2e382a487c7ee82e9b`)
   and `ThirdParty/StbImageWriteSharp`
   (`c14f96af618175c73c1cbedbf613c6a0d3c996ee`) submodules.
2. Apply `monogame-v3.8.6-retina.patch` from the MonoGame repository root
   with `git apply`.
3. Restore `MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj` and pack it
   with configuration `Release`, package version `3.8.6.1-retina.2`, assembly
   version `3.8.6.0`, and file version `3.8.6.2`.
4. Put the resulting `.nupkg` in `ThirdParty/Packages` and restore the game.

`Program.cs` enables the patched SDL path before MonoGame creates its window.
The game derives Gum zoom from the actual backbuffer density and uses
backbuffer coordinates for mouse hit testing and cursor centering. On a 2x
Retina display, a 1280x720 window gets a 2560x1440 backbuffer. This improves
UI clarity but also increases the pixels rendered by the terrain shader.
