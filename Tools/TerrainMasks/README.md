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
share the heightmap's world UVs. Press F4 in inspect mode for material and
mask diagnostics.

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
