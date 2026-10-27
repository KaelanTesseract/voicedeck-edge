"""Builds the README/website hero image: a drawn touch-bar display (no brand marks) showing the
XL widget screenshot. Usage: python tools/make-hero.py docs/images/en/xl-overview.png docs/images/en/hero.png
"""
import sys

from PIL import Image, ImageDraw, ImageFilter

SCREEN_W, SCREEN_H = 2560, 720          # XENEON EDGE panel
BEZEL = 34
CANVAS_W, CANVAS_H = 3000, 1180
OUT_WIDTH = 2000


def main() -> None:
    shot = Image.open(sys.argv[1]).convert("RGBA")
    out = sys.argv[2]

    canvas = Image.new("RGBA", (CANVAS_W, CANVAS_H))
    draw = ImageDraw.Draw(canvas)
    for y in range(CANVAS_H):  # dark backdrop with a faint blurple tint
        t = y / CANVAS_H
        draw.line([(0, y), (CANVAS_W, y)], fill=(int(24 - 8 * t), int(25 - 8 * t), int(38 - 12 * t), 255))

    glow = Image.new("RGBA", (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([500, 150, 2500, 1050], fill=(88, 101, 242, 70))
    canvas.alpha_composite(glow.filter(ImageFilter.GaussianBlur(220)))

    dev_w, dev_h = SCREEN_W + 2 * BEZEL, SCREEN_H + 2 * BEZEL
    x0 = (CANVAS_W - dev_w) // 2
    y0 = (CANVAS_H - dev_h) // 2 - 40

    shadow = Image.new("RGBA", (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([x0 + 30, y0 + 60, x0 + dev_w - 30, y0 + dev_h + 70], 40, fill=(0, 0, 0, 170))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(45)))

    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle([x0, y0, x0 + dev_w, y0 + dev_h], 30, fill=(14, 14, 17, 255), outline=(70, 70, 82, 255), width=3)
    sx, sy = x0 + BEZEL, y0 + BEZEL
    d.rectangle([sx, sy, sx + SCREEN_W, sy + SCREEN_H], fill=(0, 0, 0, 255))
    # iCUE leaves a small margin around a full-width (XL) widget
    canvas.alpha_composite(shot, (sx + (SCREEN_W - shot.width) // 2, sy + (SCREEN_H - shot.height) // 2))

    # subtle glass reflection over the panel
    sheen = Image.new("RGBA", (SCREEN_W, SCREEN_H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sheen)
    for i in range(SCREEN_H):
        a = max(0, int(18 * (1 - i / (SCREEN_H * 0.6))))
        sd.line([(0, i), (SCREEN_W, i)], fill=(255, 255, 255, a))
    canvas.alpha_composite(sheen, (sx, sy))

    h = round(CANVAS_H * OUT_WIDTH / CANVAS_W)
    canvas.convert("RGB").resize((OUT_WIDTH, h), Image.LANCZOS).save(out, optimize=True)
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
