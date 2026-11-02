"""Pack albedo RGB and displacement height into one 8-bit RGBA PNG."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("albedo", type=Path)
    parser.add_argument("height", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    with Image.open(args.albedo) as source_albedo, Image.open(args.height) as source_height:
        if source_albedo.size != source_height.size:
            parser.error("albedo and height images must have identical dimensions")

        albedo = source_albedo.convert("RGB")
        if source_height.mode.startswith("I;16"):
            # The material reads 8-bit alpha. Keep the high byte of each
            # 16-bit linear height value instead of clipping it to 255.
            height_values = np.asarray(source_height, dtype=np.uint16)
            height = Image.fromarray((height_values >> 8).astype(np.uint8))
        else:
            height = source_height.convert("L")

        args.output.parent.mkdir(parents=True, exist_ok=True)
        Image.merge("RGBA", (*albedo.split(), height)).save(
            args.output, compress_level=4)
        print(f"Packed {albedo.width}x{albedo.height} albedo RGB + height A: {args.output}")


if __name__ == "__main__":
    main()
