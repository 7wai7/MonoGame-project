"""Pack a tangent-space normal (RGB) and roughness (L) into one RGBA PNG."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("normal", type=Path)
    parser.add_argument("roughness", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    with Image.open(args.normal) as source_normal, Image.open(args.roughness) as source_roughness:
        normal = source_normal.convert("RGB")
        roughness = source_roughness.convert("L")
        if normal.size != roughness.size:
            parser.error("normal and roughness images must have identical dimensions")

        args.output.parent.mkdir(parents=True, exist_ok=True)
        Image.merge("RGBA", (*normal.split(), roughness)).save(
            args.output, compress_level=4)
        print(f"Packed {normal.width}x{normal.height} normal + roughness: {args.output}")


if __name__ == "__main__":
    main()
