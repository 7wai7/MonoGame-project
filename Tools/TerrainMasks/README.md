# Terrain material masks

The offline generator creates editable grayscale `Grass.png`, `Soil.png`,
`Rock.png`, `River.png`, and `Snow.png` masks from the terrain height EXR. Only
the initial river suggestion uses the downloaded diffuse EXR. The game does
not load that diffuse map. Black in a mask means zero coverage; white means
full coverage. Grass, soil, and rock cover the underlying ground, while river
and snow are separate layers.

From the repository root, install the offline dependencies and generate all
five masks when setting up a new terrain:

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r Tools/TerrainMasks/requirements.txt
.venv/bin/python Tools/TerrainMasks/generate.py
```

For this terrain, the original four masks already existed. Generate only the
initial snow suggestion without replacing hand-painted ground or river masks:

```sh
.venv/bin/python Tools/TerrainMasks/generate.py --snow-only
```

The default snowline is 405 world height units, with broad variation and less
snow on steep faces. To replace only `Snow.png` with a different starting
snowline, use `--snow-only --snowline 430 --force`. This overwrites any manual
edits to `Snow.png`.

Open `Assets/Terrains/Rugged Terrain with Rivers/Masks/TerrainControlPreview.png`
to inspect the distribution. Edit the five grayscale PNGs at the same
4096×4096 size and alignment. Then repack without changing those edits:

```sh
.venv/bin/python Tools/TerrainMasks/generate.py --pack-only
```

`TerrainControl.png` is an opaque visual RGB version of grass, soil, and rock.
`TerrainControlGpu.png` is derived RGBA data for the shader: R=grass, G=soil,
B=rock, A=snow. Its transparent appearance in image viewers is expected;
do not edit it directly. The MonoGame content entry disables alpha
premultiplication, preserving RGB even where snow coverage is zero. The
river remains in `River.png`, so it can be edited independently. All masks
share the heightmap's world UVs. The legacy textured renderer has F4 mask
diagnostics when it is enabled; the active honeycomb renderer has no F4 mode.

The current `TerrainHoneycomb.fx` material projects a regular hexagonal grid
onto the world X/Z plane. Each cell samples the control and river masks at its
centre and chooses one fixed base colour for grass, soil, rock, snow, or river.
The strongest ground mask wins; snow and river cover it where their masks reach
50% coverage. Only the Voronoi regions vary each base colour's shade. The shader
does not load the source diffuse or detail textures.
`HexRadius`, `HexOutlineWidth`, and `VoronoiPatchSize` in
`Game/Terrain/TerrainHoneycombRenderSystem.cs` control the visual scale in world
units. `VoronoiShadeStrength` controls how much Voronoi changes the brightness
of a base colour: 0 disables it, while 0.12 gives a range of about ±6%.
The current `HexRadius` is 0.5 world units, so each hexagon measures 1 unit
between opposite vertices. This is a shader effect on the terrain mesh; it
does not create hexagon geometry or change the heightfield collider.

`generate.py` refuses to overwrite editable masks without `--force`.
The default world and height scales match `BootstrapScene`; pass
`--world-size` or `--height-scale` if they change. Snow's material textures
are separate from `Snow.png`: the former describe its surface, while the
latter decides where that surface appears.

The previous textured renderer is preserved as
`Game/Terrain/TerrainMaterialRenderSystem.cs`, with its shader in
`Content/Effects/TerrainTexturedLegacy.fx` and content entries in
`Content/LegacyTerrainTextured.mgcb.disabled`. The old material combined the
downloaded OpenGL normal and roughness into one GPU texture. These assets are
retained for a possible future textured material. Repack after replacing either
source image:

```sh
.venv/bin/python Tools/TerrainMaterials/pack_normal_rough.py \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_NormalGL.png \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_Roughness.png \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_NormalRough.png
```

For a future material that reads albedo and material height from one RGBA
texture, `pack_albedo_height.py` puts the source colour in RGB and displacement
in alpha. A 16-bit height image is reduced to 8 bits for the alpha channel.
It uses the same Python dependencies as the mask generator:

```sh
.venv/bin/python Tools/TerrainMaterials/pack_albedo_height.py \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_diff_2k.jpg \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_disp_2k.png \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_albedo_height_2k.png
```

The current honeycomb material does not use this packed file.
