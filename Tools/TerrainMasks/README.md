# Terrain material masks

The offline generator creates editable grayscale `Grass.png`, `Rock.png`,
`River.png`, and `Snow.png` masks from the terrain height EXR. Only
the initial river suggestion uses the downloaded diffuse EXR. The game does
not load that diffuse map. Black in a mask means zero coverage; white means
full coverage. Grass and rock cover the underlying ground, while river
and snow are separate layers.

From the repository root, install the offline dependencies and generate all
four masks when setting up a new terrain:

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r Tools/TerrainMasks/requirements.txt
.venv/bin/python Tools/TerrainMasks/generate.py
```

To regenerate only an initial snow suggestion without replacing hand-painted
ground or river masks:

```sh
.venv/bin/python Tools/TerrainMasks/generate.py --snow-only
```

The default snowline is 405 world height units, with broad variation and less
snow on steep faces. To replace only `Snow.png` with a different starting
snowline, use `--snow-only --snowline 430 --force`. This overwrites any manual
edits to `Snow.png`.

Open `Assets/Terrains/Rugged Terrain with Rivers/Masks/TerrainControlPreview.png`
to inspect the distribution. The previous soil coverage was merged into
`Grass.png`; there is no separate soil material or soil mask. Edit the four
grayscale PNGs at the same 4096×4096 size and alignment. Then repack without
changing those edits:

```sh
.venv/bin/python Tools/TerrainMasks/generate.py --pack-only
```

`TerrainControl.png` is an opaque RGB packing of grass and rock; use
`TerrainControlPreview.png` for an intuitive colour preview.
`TerrainControlGpu.png` is derived RGBA data for the shader: R=grass, G=rock,
B=unused, A=snow. Its transparent appearance in image viewers is expected;
do not edit it directly. The MonoGame content entry disables alpha
premultiplication, preserving RGB even where snow coverage is zero. The
river remains in `River.png`, so it can be edited independently. All masks
share the heightmap's world UVs. Press F4 in inspect mode for material and
mask diagnostics.
F10 cycles hex-tiling for all materials, grass only, and off to compare its
appearance and cost with ordinary tiling. F11 shows FPS, frame time and memory at the left side of the
viewport and prints a five-second performance summary to the terminal.

`generate.py` refuses to overwrite editable masks without `--force`.
The default world and height scales match `BootstrapScene`; pass
`--world-size` or `--height-scale` if they change. Snow's material textures
are separate from `Snow.png`: the former describe its surface, while the
latter decides where that surface appears.

The snow material combines the downloaded OpenGL normal and roughness into
one GPU texture. Repack after replacing either source image:

```sh
.venv/bin/python Tools/TerrainMaterials/pack_normal_rough.py \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_NormalGL.png \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_Roughness.png \
  Assets/Textures/Snow006_2K-PNG/Snow006_2K-PNG_NormalRough.png
```

Grass, rock and snow each use one RGBA albedo texture: RGB is the
original colour, and A is material-local height used at boundaries. The
source colour and displacement images remain untouched. Rebuild a derived
8-bit RGBA PNG with `pack_albedo_height.py` after replacing either source.
It uses the same Python dependencies as the mask generator:

```sh
.venv/bin/python Tools/TerrainMaterials/pack_albedo_height.py \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_diff_2k.jpg \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_disp_2k.png \
  Assets/Textures/sparse_grass_2k.blend/textures/sparse_grass_albedo_height_2k.png
```

Use the same command for rock and snow with their matching colour,
displacement and `*albedo_height*` output paths. The content entries disable
alpha premultiplication so the height channel does not change the colour.
No separate height texture is loaded for these three materials.
