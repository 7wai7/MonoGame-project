# Sci-fi HUD in Gum

The editable project is `Content/GumProject/GameUi.gumx`. Open it in the Gum
editor. Its layout is based on `sci-fi-hud-ui-style-guide.md` in the workspace root.

| File | Purpose |
| --- | --- |
| `Screens/PauseMenu.gusx` | Dimmed modal menu with Settings and Exit buttons. |
| `Screens/Settings.gusx` | Full-screen settings backdrop, tabs, rows and actions. |
| `Screens/InspectHud.gusx` | Reticle, selection card, inspector and scroll-list slot. |
| `Screens/Performance.gusx` | F11 telemetry text list with subtle text shadows. |
| `Components/HudPanel.gucx` | Shared glass background, thin border and corner markers. |
| `Components/HudButton.gucx` | Shared action button and label. |
| `Standards/*.gutx` | Gum's base Container, Text, Sprite, Circle and ColoredRectangle definitions. |
| `Textures/panel_glass.png` | Subtle panel gradient and technical grid. |
| `Fonts/Poppins-Medium.ttf` | UI font, licensed under `Fonts/Poppins-OFL.txt`. |

Edit sizes, anchors, text and colors in the Gum files. `Game/UI/GumHudProject.cs`
finds the named elements and attaches actions; `PauseMenuScreen`,
`SettingsScreen`, `InspectHudScreen` and `PerformanceScreen` supply behavior and changing values.
The inspector component list is built at runtime because its rows depend on the
selected entity. Keep names referenced by `GumHudProject.Require` stable or
update the corresponding C# binding when renaming an element in Gum.

`GameSettings.UiScale` supplies the initial UI scale; the player's selected
scale is saved in `settings.json` and used on the next launch. `1f` is the
unscaled size. Gum adjusts its canvas when the window size changes, so UI
positions and pointer interaction use the same scale. On a Retina display,
`MainGame` multiplies this logical scale by the ratio of backbuffer pixels to
window points. `GameSettings.UseHighDpi` controls the native Retina backbuffer
on macOS. `UiText` increases Poppins raster size once when a Gum element is
created, then compensates with `FontScale`. This keeps text sharp without
generating a new font atlas during `Gum.Draw` when an inspector row appears.

`UserSettingsStore` saves fullscreen, VSync, UI scale and inspect mouse
sensitivity in the OS user's local application data directory under
`MonoGame-project/settings.json`. The Graphics and Controls tabs edit a draft;
Apply saves it, Back or Escape discards unsaved changes, and Reset Defaults
loads defaults into the draft. Audio controls are visible but disabled until
an audio system exists. While Settings is open, the engine suspends scene
updates, physics and 3D drawing; the UI is limited to 30 frames per second.

The `.gumx`, `.gusx`, `.gucx`, `.gutx`, font and PNG files are copied to the build
output by the project file. Gum reads them directly at runtime, outside the
MonoGame content pipeline.
