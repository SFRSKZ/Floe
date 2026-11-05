"""Makes one checkpoint sheet: each candidate's beat frame, the current best two, at the same scale.

    python montage.py out/checkpoint.png Skyline NeonRain Traffic

Rows are labelled; the strip is 3840 wide, downscaled to 1840 so the whole comparison fits on screen.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
W = 1840
FONT = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 16)


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    out = Path(sys.argv[1])
    names = sys.argv[2:]
    rows: list[tuple[str, Image.Image]] = []

    rows.append(("CyberSignal — current best (reference)", _load(ROOT / "out" / "ref-signal")))
    rows.append(("CyberBlackwall — current best (reference)", _load(ROOT / "out" / "ref-blackwall")))
    for name in names:
        folder = ROOT / "out" / name
        rows.append((f"{name} — new candidate", _load(folder)))

    strip = round(126 * W / 3840)
    label = 24
    gap = 8
    canvas = Image.new("RGB", (W, (strip + label + gap) * len(rows)), (10, 12, 18))
    draw = ImageDraw.Draw(canvas)
    y = 0
    for text, image in rows:
        if image is None:
            continue
        canvas.paste(image, (0, y))
        draw.text((8, y + strip + 3), text, font=FONT, fill=(235, 238, 245))
        y += strip + label + gap

    out.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out)
    print(f"written {out} ({canvas.width}x{canvas.height})")
    return 0


def _load(folder: Path) -> Image.Image | None:
    for name in ("shot-beat.png", "shot-t10.00.png"):
        path = folder / "shots" / name
        if path.exists():
            image = Image.open(path).convert("RGB")
            return image.resize((W, max(1, round(image.height * W / image.width))), Image.LANCZOS)
    return None


if __name__ == "__main__":
    raise SystemExit(main())
