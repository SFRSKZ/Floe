"""Side-by-side evidence sheet: candidate looks against the current best two (CyberSignal, CyberBlackwall).

    python compare.py --candidate out/Skyline --name Skyline
    python compare.py --candidate out/Skyline --name Skyline --also out/NeonRain --also-name NeonRain

It reads the shots SceneLab wrote (out/<scene>/shots/shot-<label>.png) and stacks, per evidence time, one
row per scene at the same scale — the same comparison rule 14 asks for: the new look only ships if it is
plainly better than the best that exists today. It also proves rule 15's other half: the frames of a scene
are not the same picture repeated (hash check).
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

LABELS = ["t2.00", "t6.00", "t10.00", "t15.50", "beat"]
ROOT = Path(__file__).resolve().parent.parent
SHOT_WIDTH = 1840
FONT = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 17)
SMALL = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 14)


def load(scene_dir: Path, label: str) -> Image.Image | None:
    path = scene_dir / "shots" / f"shot-{label}.png"
    if not path.exists():
        return None
    image = Image.open(path).convert("RGB")
    scale = SHOT_WIDTH / image.width
    return image.resize((SHOT_WIDTH, max(1, round(image.height * scale))), Image.LANCZOS)


def digest(scene_dir: Path) -> dict[str, str]:
    hashes = {}
    for label in LABELS:
        path = scene_dir / "shots" / f"shot-{label}.png"
        if path.exists():
            hashes[label] = hashlib.sha256(path.read_bytes()).hexdigest()[:12]
    return hashes


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate", required=True, help="SceneLab output folder of the new look")
    parser.add_argument("--name", required=True)
    parser.add_argument("--also", action="append", default=[], help="more scene output folders")
    parser.add_argument("--also-name", action="append", default=[])
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    rows = [("CyberSignal (current best)", ROOT / "out" / "ref-signal"),
            ("CyberBlackwall (current best)", ROOT / "out" / "ref-blackwall"),
            (args.name, Path(args.candidate).resolve())]
    for name, folder in zip(args.also_name, args.also):
        rows.append((name, Path(folder).resolve()))

    shown = [r for r in rows if any(load(r[1], label) is not None for label in LABELS)]
    if not shown:
        print("no shots found")
        return 1

    title_height = 34
    label_height = 26
    gap = 6
    strip_height = round(126 * SHOT_WIDTH / 3840)
    block = title_height + (len(shown) * (strip_height + label_height + gap))
    canvas = Image.new("RGB", (SHOT_WIDTH, block * len(LABELS)), (12, 14, 20))
    draw = ImageDraw.Draw(canvas)

    y = 0
    for label in LABELS:
        draw.rectangle([0, y, SHOT_WIDTH, y + title_height], fill=(28, 32, 42))
        draw.text((10, y + 8), f"t = {label}", font=FONT, fill=(252, 238, 10))
        time_y = y
        y += title_height
        for name, folder in shown:
            image = load(folder, label)
            if image is None:
                y += strip_height + label_height + gap
                continue
            canvas.paste(image, (0, y))
            draw.text((6, y + strip_height + 3), name, font=SMALL, fill=(210, 215, 225))
            y += strip_height + label_height + gap
        y = time_y + block

    out = Path(args.out) if args.out else ROOT / "out" / f"compare-{args.name}.png"
    canvas.save(out)
    print(f"written {out}  ({canvas.width}x{canvas.height})")

    print("\nframe hashes (rule 15: a look's frames must differ from each other):")
    for name, folder in shown:
        hashes = digest(folder)
        unique = len(set(hashes.values()))
        print(f"  {name:32} {unique}/{len(hashes)} unique  {hashes}")
        if unique < len(hashes):
            print("    WARNING: repeated frame — that look does not count as animated")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
