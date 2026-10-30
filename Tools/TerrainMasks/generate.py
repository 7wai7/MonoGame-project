"""Generate editable terrain material masks from the project's height EXR.

The diffuse EXR is used only to suggest river coverage. It never chooses
grass, soil, or rock, so its large painted pixels cannot define those areas.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import OpenEXR
from PIL import Image, ImageFilter


REPOSITORY = Path(__file__).resolve().parents[2]
TERRAIN = REPOSITORY / "Assets" / "Terrains" / "Rugged Terrain with Rivers"
MASK_NAMES = ("Grass", "Soil", "Rock", "River", "Snow")


def smoothstep(start: float, end: float, values: np.ndarray) -> np.ndarray:
    fraction = np.clip((values - start) / (end - start), 0.0, 1.0)
    return fraction * fraction * (3.0 - 2.0 * fraction)


def read_height(path: Path, scale: float) -> np.ndarray:
    channels = OpenEXR.File(str(path)).channels()
    if "Y" in channels:
        pixels = channels["Y"].pixels
    elif "R" in channels:
        pixels = channels["R"].pixels
    elif "RGB" in channels:
        pixels = channels["RGB"].pixels[:, :, 0]
    else:
        raise ValueError(f"{path} needs a Y or R height channel")
    height = pixels.astype(np.float32) * scale
    if height.ndim != 2 or not np.isfinite(height).all():
        raise ValueError(f"{path} is not a finite 2D heightmap")
    return height


def offset_neighbours(height: np.ndarray, radius: int) -> tuple[np.ndarray, ...]:
    padded = np.pad(height, radius, mode="edge")
    h, w = height.shape
    return (
        padded[radius : radius + h, 2 * radius : 2 * radius + w],
        padded[radius : radius + h, :w],
        padded[2 * radius : 2 * radius + h, radius : radius + w],
        padded[:h, radius : radius + w],
    )


def terrain_features(height: np.ndarray, cell_metres: float) -> tuple[np.ndarray, np.ndarray]:
    # Sample over several metres, not adjacent 1.22 m source pixels. Broad
    # material areas should follow the landform instead of individual texels.
    slope_radius = max(1, round(6.0 / cell_metres))
    east, west, south, north = offset_neighbours(height, slope_radius)
    east_west = (east - west) / (2.0 * slope_radius * cell_metres)
    north_south = (south - north) / (2.0 * slope_radius * cell_metres)
    slope_degrees = np.degrees(np.arctan(np.hypot(east_west, north_south)))
    del east, west, south, north, east_west, north_south

    curvature_radius = max(slope_radius + 1, round(14.0 / cell_metres))
    east, west, south, north = offset_neighbours(height, curvature_radius)
    # Positive values identify hollows where soil can accumulate; negative
    # values identify exposed crests. Units are approximate world metres.
    hollowness = (east + west + south + north) * 0.25 - height
    return slope_degrees, hollowness


def broad_noise(shape: tuple[int, int], extent_metres: float, spacing_metres: float,
                seed: int) -> np.ndarray:
    height, width = shape
    columns = max(4, round(extent_metres / spacing_metres)) + 1
    rows = max(4, round(extent_metres * height / width / spacing_metres)) + 1
    random = np.random.default_rng(seed)
    grid = random.random((rows, columns), dtype=np.float32)
    image = Image.fromarray(grid, mode="F")
    return np.clip(np.asarray(
        image.resize((width, height), Image.Resampling.BICUBIC),
        dtype=np.float32), 0.0, 1.0)


def srgb(values: np.ndarray) -> np.ndarray:
    values = np.clip(values, 0.0, 1.0)
    return np.where(values <= 0.0031308,
                    values * 12.92,
                    1.055 * np.power(values, 1.0 / 2.4) - 0.055)


def suggest_river(path: Path, expected_shape: tuple[int, int]) -> np.ndarray:
    channels = OpenEXR.File(str(path)).channels()
    if "RGB" in channels:
        pixels = channels["RGB"].pixels
        red, green, blue = (pixels[:, :, channel] for channel in range(3))
    elif all(channel in channels for channel in ("R", "G", "B")):
        red, green, blue = (channels[channel].pixels for channel in ("R", "G", "B"))
    else:
        raise ValueError(f"{path} needs R, G, and B colour channels")
    if red.shape != expected_shape:
        raise ValueError("Diffuse and height EXRs must have identical dimensions")

    # The existing terrain shader reads the diffuse as sRGB. Extract only its
    # distinctly blue regions; rock/soil/grass do not use this colour map.
    blue_minus_red = srgb(blue) - srgb(red)
    blue_minus_green = srgb(blue) - srgb(green)
    river = smoothstep(0.025, 0.10, blue_minus_red)
    river *= smoothstep(0.004, 0.035, blue_minus_green)
    return river


def make_masks(height: np.ndarray, diffuse_path: Path,
               extent_metres: float, snowline_metres: float) -> dict[str, np.ndarray]:
    cell_metres = extent_metres / (height.shape[1] - 1)
    slope, hollowness = terrain_features(height, cell_metres)
    river = suggest_river(diffuse_path, height.shape)

    broad = broad_noise(height.shape, extent_metres, 105.0, seed=71)
    medium = broad_noise(height.shape, extent_metres, 34.0, seed=127)
    variation = 0.7 * broad + 0.3 * medium
    del broad, medium

    exposed_crest = smoothstep(0.25, 1.5, -hollowness)
    rock = smoothstep(14.0, 28.0,
                      slope + 5.0 * exposed_crest + 3.0 * (variation - 0.5))
    del exposed_crest

    concavity = smoothstep(0.15, 1.4, hollowness)
    river_image = Image.fromarray(np.rint(river * 255.0).astype(np.uint8), mode="L")
    river_bank = np.asarray(
        river_image.filter(ImageFilter.GaussianBlur(radius=9.0)),
        dtype=np.float32) / 255.0
    # Soil gathers in broad hollows and near water. Coherent noise permits
    # occasional soil fields; the original diffuse colour never places them.
    soil_score = 0.65 * variation + 0.45 * concavity + 0.35 * river_bank
    soil_fraction = smoothstep(0.34, 0.64, soil_score)
    soil = (1.0 - rock) * soil_fraction
    grass = (1.0 - rock) * (1.0 - soil_fraction)
    snow = suggest_snow(height, slope, extent_metres, snowline_metres)
    return {"Grass": grass, "Soil": soil, "Rock": rock,
            "River": river, "Snow": snow}


def suggest_snow(height: np.ndarray, slope: np.ndarray,
                 extent_metres: float, snowline_metres: float) -> np.ndarray:
    # Altitude is in BootstrapScene world units. A broad, stable variation
    # avoids a straight contour line; steep exposed faces retain their rock.
    variation = broad_noise(height.shape, extent_metres, 140.0, seed=309)
    snowline = snowline_metres + 70.0 * (variation - 0.5)
    altitude = smoothstep(-15.0, 25.0, height - snowline)
    slope_shelter = 1.0 - smoothstep(28.0, 48.0, slope)
    return altitude * slope_shelter


def save_masks(masks: dict[str, np.ndarray], directory: Path) -> None:
    for name in MASK_NAMES:
        image = Image.fromarray(
            np.rint(np.clip(masks[name], 0.0, 1.0) * 255.0).astype(np.uint8),
            mode="L")
        image.save(directory / f"{name}.png", compress_level=4)
        print(f"{name}: average coverage {float(masks[name].mean()) * 100.0:.1f}%")


def pack_masks(directory: Path) -> None:
    sources = [Image.open(directory / f"{name}.png").convert("L")
               for name in MASK_NAMES]
    if len({image.size for image in sources}) != 1:
        raise ValueError("All manually edited masks must have identical dimensions")
    # Keep the ground weights opaque. A river mask in alpha would make most
    # pixels transparent in image editors and can discard their RGB on import.
    Image.merge("RGB", tuple(sources[:3])).save(
        directory / "TerrainControl.png", compress_level=4)
    # This derived file is GPU data, not an image for direct editing. Disable
    # alpha premultiplication in MGCB so snow=0 cannot erase ground RGB.
    Image.merge("RGBA", (*sources[:3], sources[4])).save(
        directory / "TerrainControlGpu.png", compress_level=4)

    reduced = [image.resize((1024, 1024), Image.Resampling.BILINEAR)
               for image in sources]
    grass, soil, rock, river, snow = [
        np.asarray(image, dtype=np.float32) / 255.0 for image in reduced]
    total = np.maximum(grass + soil + rock, 0.001)
    preview = (
        grass[:, :, None] * np.array([59, 105, 56], dtype=np.float32) +
        soil[:, :, None] * np.array([145, 111, 70], dtype=np.float32) +
        rock[:, :, None] * np.array([127, 128, 128], dtype=np.float32)
    ) / total[:, :, None]
    preview = preview * (1.0 - snow[:, :, None]) + snow[:, :, None] * np.array(
        [230, 234, 239], dtype=np.float32)
    preview = preview * (1.0 - river[:, :, None]) + river[:, :, None] * np.array(
        [57, 103, 155], dtype=np.float32)
    Image.fromarray(np.rint(np.clip(preview, 0, 255)).astype(np.uint8),
                    mode="RGB").save(directory / "TerrainControlPreview.png")
    print(f"Packed TerrainControl.png, TerrainControlGpu.png, "
          f"and TerrainControlPreview.png in {directory}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--height", type=Path,
                        default=TERRAIN / "Rugged Terrain Height Map EXR.exr")
    parser.add_argument("--diffuse", type=Path,
                        default=TERRAIN / "Rugged Terrain Diffuse EXR.exr")
    parser.add_argument("--output", type=Path, default=TERRAIN / "Masks")
    parser.add_argument("--world-size", type=float, default=5000.0,
                        help="terrain width in world metres, matching BootstrapScene")
    parser.add_argument("--height-scale", type=float, default=2600.0,
                        help="height multiplier, matching BootstrapScene")
    parser.add_argument("--snowline", type=float, default=405.0,
                        help="mean snowline in terrain world height units")
    parser.add_argument("--pack-only", action="store_true",
                        help="repack edited grayscale masks without regenerating them")
    parser.add_argument("--snow-only", action="store_true",
                        help="generate only Snow.png without replacing other masks")
    parser.add_argument("--force", action="store_true",
                        help="replace existing grayscale masks; discards edits")
    args = parser.parse_args()
    if args.pack_only and args.snow_only:
        parser.error("--pack-only and --snow-only cannot be combined")
    if not np.isfinite(args.world_size) or args.world_size <= 0:
        parser.error("--world-size must be positive and finite")
    if not np.isfinite(args.height_scale) or args.height_scale <= 0:
        parser.error("--height-scale must be positive and finite")
    if not np.isfinite(args.snowline):
        parser.error("--snowline must be finite")
    args.output.mkdir(parents=True, exist_ok=True)

    if not args.pack_only:
        names = ("Snow",) if args.snow_only else MASK_NAMES
        existing = [args.output / f"{name}.png" for name in names
                    if (args.output / f"{name}.png").exists()]
        if existing and not args.force:
            parser.error("grayscale masks already exist; use --pack-only after editing "
                         "or --force to replace them")
        height = read_height(args.height, args.height_scale)
        print(f"Generating {height.shape[1]}x{height.shape[0]} masks "
              f"({args.world_size / (height.shape[1] - 1):.3f} m/texel)")
        if args.snow_only:
            cell_metres = args.world_size / (height.shape[1] - 1)
            slope, _ = terrain_features(height, cell_metres)
            snow = suggest_snow(height, slope, args.world_size, args.snowline)
            snow_image = Image.fromarray(
                np.rint(np.clip(snow, 0.0, 1.0) * 255.0).astype(np.uint8),
                mode="L")
            snow_image.save(args.output / "Snow.png", compress_level=4)
            print(f"Snow: average coverage {float(snow.mean()) * 100.0:.1f}%")
        else:
            save_masks(make_masks(
                height, args.diffuse, args.world_size, args.snowline), args.output)

    pack_masks(args.output)


if __name__ == "__main__":
    main()
