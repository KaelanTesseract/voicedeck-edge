"""Renders the Voicedeck Edge app icon (same motif as widget/resources/icon.svg) as a multi-size .ico.

Usage: python tools/make-icon.py hub/Resources/app.ico
"""
import sys

from PIL import Image, ImageDraw

SIZES = [16, 24, 32, 48, 64, 128, 256]
BLURPLE = (88, 101, 242, 255)


def render(size: int) -> Image.Image:
    scale = 4  # draw large, then downsample for smooth edges
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = s * 2 / 64
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 16 / 64, fill=BLURPLE)
    # the "V": same points as the SVG (17,18) -> (32,47) -> (47,18) on a 64 grid
    pts = [(s * 17 / 64, s * 18 / 64), (s * 32 / 64, s * 47 / 64), (s * 47 / 64, s * 18 / 64)]
    width = round(s * 7 / 64)
    d.line(pts, fill="white", width=width, joint="curve")
    r = width / 2
    for x, y in (pts[0], pts[2]):
        d.ellipse([x - r, y - r, x + r, y + r], fill="white")
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = sys.argv[1]
    images = [render(sz) for sz in SIZES]
    images[-1].save(out, format="ICO", sizes=[(sz, sz) for sz in SIZES], append_images=images[:-1])
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
