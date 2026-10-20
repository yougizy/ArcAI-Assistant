# -*- coding: utf-8 -*-
"""Generate the small ArcAI toolbar/dockable-window icons."""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "ArcAI.AddIn", "Images")
os.makedirs(OUT, exist_ok=True)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\arialbd.ttf",
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\arial.ttf",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        if os.path.isfile(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


for size in (16, 32, 48):
    scale = 4
    canvas = size * scale
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    radius = max(2 * scale, canvas // 6)
    draw.rounded_rectangle(
        [0, 0, canvas - 1, canvas - 1], radius=radius,
        fill=(31, 111, 235, 255), outline=(20, 76, 168, 255), width=max(1, scale),
    )
    # Small white "spark" in the upper-right corner.
    spark = max(2 * scale, canvas // 8)
    cx, cy = canvas - spark * 2, spark * 2
    draw.ellipse([cx - spark // 2, cy - spark // 2,
                  cx + spark // 2, cy + spark // 2],
                 fill=(255, 255, 255, 230))
    font = load_font(int(canvas * 0.46))
    text = "AI"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text(((canvas - tw) / 2 - bbox[0], (canvas - th) / 2 - bbox[1] - scale),
              text, font=font, fill=(255, 255, 255, 255))
    img = img.resize((size, size), Image.LANCZOS)
    path = os.path.join(OUT, "ai%d.png" % size)
    img.save(path, "PNG")
    print(path)
