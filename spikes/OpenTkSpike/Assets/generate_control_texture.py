"""Генерирует контрольную текстуру control.png (256x256, RGBA) для проверки ориентации UV.

Рисунок (как его видит человек в просмотрщике изображений):
  левый верхний угол — синий, правый верхний — желтый,
  левый нижний — красный, правый нижний — зеленый,
  в центре — черная буква «F» (несимметрична по обеим осям: видно и переворот, и зеркало).

Текстура создана командой проекта, сторонних прав нет. Только стандартная библиотека Python.
Запуск: python3 generate_control_texture.py
"""

import pathlib
import struct
import zlib

SIZE = 256
CORNER = 64
BACKGROUND = (235, 235, 235, 255)
BLACK = (0, 0, 0, 255)
BLUE = (0, 90, 255, 255)
YELLOW = (255, 210, 0, 255)
RED = (230, 30, 30, 255)
GREEN = (20, 180, 60, 255)


def fill(pixels, x0, y0, x1, y1, color):
    """Закрашивает прямоугольник [x0, x1) x [y0, y1); y отсчитывается сверху."""
    for y in range(y0, y1):
        for x in range(x0, x1):
            pixels[y][x] = color


def main():
    pixels = [[BACKGROUND] * SIZE for _ in range(SIZE)]

    fill(pixels, 0, 0, CORNER, CORNER, BLUE)
    fill(pixels, SIZE - CORNER, 0, SIZE, CORNER, YELLOW)
    fill(pixels, 0, SIZE - CORNER, CORNER, SIZE, RED)
    fill(pixels, SIZE - CORNER, SIZE - CORNER, SIZE, SIZE, GREEN)

    # Буква «F»: вертикальная стойка слева, длинная перекладина сверху, короткая посередине.
    fill(pixels, 88, 72, 112, 184, BLACK)
    fill(pixels, 88, 72, 168, 96, BLACK)
    fill(pixels, 88, 120, 148, 140, BLACK)

    raw = b"".join(b"\x00" + b"".join(struct.pack("4B", *p) for p in row) for row in pixels)

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )

    output = pathlib.Path(__file__).with_name("control.png")
    output.write_bytes(png)
    print(f"Written {output}")


if __name__ == "__main__":
    main()
