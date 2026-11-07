# Sci-fi HUD in Gum

The editable project is `Content/GumProject/GameUi.gumx`. Open it in the Gum
editor. Its layout is based on `sci-fi-hud-ui-style-guide.md` in the workspace root.

| File | Purpose |
| --- | --- |
| `Screens/PauseMenu.gusx` | Dimmed modal menu, labels, exit button and placement. |
| `Screens/InspectHud.gusx` | Reticle, selection card, inspector and scroll-list slot. |
| `Screens/Performance.gusx` | F11 telemetry text list with subtle text shadows. |
| `Components/HudPanel.gucx` | Shared glass background, thin border and corner markers. |
| `Components/HudButton.gucx` | Shared action button and label. |
| `Standards/*.gutx` | Gum's base Container, Text, Sprite, Circle and ColoredRectangle definitions. |
| `Textures/panel_glass.png` | Subtle panel gradient and technical grid. |
| `Fonts/Poppins-Medium.ttf` | Static Poppins Medium typeface, licensed under `Fonts/Poppins-OFL.txt`. |

Edit sizes, anchors, text and colors in the Gum files. `Game/UI/GumHudProject.cs`
finds the named elements and attaches actions; `PauseMenuScreen`,
`InspectHudScreen` and `PerformanceScreen` supply behavior and changing values.
The inspector component list is built at runtime because its rows depend on the
selected entity. Keep names referenced by `GumHudProject.Require` stable or
update the corresponding C# binding when renaming an element in Gum.

The `.gumx`, `.gusx`, `.gucx`, `.gutx`, font and PNG files are copied to the build
output by the project file. Gum reads them directly at runtime, outside the
MonoGame content pipeline.
