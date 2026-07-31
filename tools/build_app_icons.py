"""Build Windows icon assets from the generated MyBudget master artwork."""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

from PIL import Image


def remove_connected_light_background(image: Image.Image) -> Image.Image:
    """Make the light area connected to the canvas edges transparent.

    The generated artwork has a dark rounded tile on a white canvas. Restricting
    removal to edge-connected pixels preserves the small white highlights inside
    the wallet mark.
    """

    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    queue: deque[tuple[int, int]] = deque()
    visited = bytearray(width * height)

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if visited[index]:
            return

        red, green, blue, _ = pixels[x, y]
        # The tile is almost black; this threshold includes the antialiased white
        # canvas edge without crossing into the charcoal artwork.
        if max(red, green, blue) < 52:
            return

        visited[index] = 1
        queue.append((x, y))

    for x in range(width):
        enqueue(x, 0)
        enqueue(x, height - 1)
    for y in range(height):
        enqueue(0, y)
        enqueue(width - 1, y)

    while queue:
        x, y = queue.popleft()
        pixels[x, y] = (*pixels[x, y][:3], 0)
        if x > 0:
            enqueue(x - 1, y)
        if x + 1 < width:
            enqueue(x + 1, y)
        if y > 0:
            enqueue(x, y - 1)
        if y + 1 < height:
            enqueue(x, y + 1)

    return rgba


def contained_square(image: Image.Image, size: int, padding: float = 0.0) -> Image.Image:
    available = max(1, round(size * (1.0 - 2.0 * padding)))
    resized = image.copy()
    resized.thumbnail((available, available), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(resized, ((size - resized.width) // 2, (size - resized.height) // 2))
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("assets", type=Path)
    args = parser.parse_args()

    args.assets.mkdir(parents=True, exist_ok=True)
    source = Image.open(args.source)
    cleaned = remove_connected_light_background(source)
    master = contained_square(cleaned, 1024)
    master_path = args.assets / "MyBudgetIconMaster-v2.png"
    master.save(master_path, optimize=True)

    icon_source = contained_square(master, 256, padding=0.035)
    icon_source.save(
        args.assets / "MyBudget.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (96, 96), (128, 128), (256, 256)],
    )

    outputs = {
        "Square44x44Logo.scale-200.png": (88, 0.08),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 0.08),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 0.08),
        "Square150x150Logo.scale-200.png": (300, 0.06),
        "StoreLogo.png": (50, 0.06),
    }
    for filename, (size, padding) in outputs.items():
        contained_square(master, size, padding=padding).save(args.assets / filename, optimize=True)

    print(f"Created {master_path}")
    print(f"Created {args.assets / 'MyBudget.ico'}")


if __name__ == "__main__":
    main()
