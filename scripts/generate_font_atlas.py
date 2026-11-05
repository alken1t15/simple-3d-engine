"""Генерация встроенного ASCII-atlas; Pillow нужен только для пересоздания ассета.

Запуск: python3 scripts/generate_font_atlas.py /path/to/DejaVuSansMono.ttf
Обычная .NET-сборка использует готовые ассеты и не запускает скрипт.
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, __version__ as pillow_version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("font", type=Path)
    args = parser.parse_args()
    size, padding, columns = 48, 3, 16
    font = ImageFont.truetype(str(args.font), size)
    ascent, descent = font.getmetrics()
    advance = font.getlength("M")
    width = int(advance + 0.999) + padding * 2
    height = ascent + descent + padding * 2
    first, last = 32, 126
    rows = (last - first + columns) // columns
    atlas = Image.new("RGBA", (columns * width, rows * height), (255, 255, 255, 0))
    draw = ImageDraw.Draw(atlas)
    for code in range(first, last + 1):
        index = code - first
        draw.text((index % columns * width + padding, index // columns * height + padding),
                  chr(code), font=font, fill=(255, 255, 255, 255), anchor="la")
    target = Path(__file__).resolve().parents[1] / "src/Engine3D.OpenGL/Assets"
    target.mkdir(parents=True, exist_ok=True)
    atlas.save(target / "ascii-atlas.png")
    metrics = dict(cellWidth=width, cellHeight=height, columns=columns, first=first, last=last,
                   advance=advance, font="DejaVu Sans Mono", fontPixelSize=size,
                   fontSha256=hashlib.sha256(args.font.read_bytes()).hexdigest(), pillow=pillow_version)
    (target / "ascii-atlas.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {atlas.width}x{atlas.height}, cell {width}x{height}, advance {advance}")


if __name__ == "__main__":
    main()
