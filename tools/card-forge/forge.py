"""Draws Cyberpunk cards for apps the set doesn't know, with Gemini's image model (Nano Banana).

    python tools/card-forge/forge.py "BreeZip" path/to/icon.png [--variants 2] [--model gemini-3-pro-image]
    python tools/card-forge/forge.py "BreeZip" "C:/Program Files/BreeZip/BreeZip.exe"
    python tools/card-forge/forge.py --install work/ai-cards/breezip-2.png "BreeZip"

Without API access (the free tier gets no image model), --kit writes the prompt and the three images to paste into
the Gemini app, and --from turns the image it gives back into a card:

    python tools/card-forge/forge.py "BreeZip" BreeZip.exe --kit
    python tools/card-forge/forge.py "BreeZip" --from Downloads/gemini-image.png

The app's own icon goes to the model with two cards of the set as style references. What comes back is cut out of its
white background, toned the way Floe tones a logo, and put in the set's frame at the set's size; each variant is saved
with a preview sheet that shows it at card size and at dock size on a dark and a yellow dock. --install copies the
chosen card into the set and adds its name to names.csv.

Needs GEMINI_API_KEY (Google AI Studio) and `pip install google-genai icoextract pillow`.
"""

import argparse
import io
import os
import re
import sys
import unicodedata
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
CARDS = ROOT / "src" / "Floe.App" / "Assets" / "CardIcons"
OUT = ROOT / "work" / "ai-cards"

# the set's geometry and grey, as in CardIcons.cs
CARD = 192
LOGO = 106
LOGO_AT = 43
GREY = (0xAB, 0xB2, 0xC0)
SHADOW = 0.4

REFERENCES = ["android-studio.png", "angry-ip-scanner.png"]
MODEL = "gemini-3.1-flash-image"

PROMPT = """Image 1 is an app icon. Images 2 and 3 are style references from an icon set.
Redraw the app icon from image 1 as a flat emblem in exactly the style of images 2 and 3.

Style rules:
- Flat filled shapes. No outline-only line art, no hatching, no textures, no gradients, no shadows, no glow, no 3D rendering.
- Exactly three tones: light cool grey #B4B8C4 for the main body, mid grey #7E8494 for side faces and secondary parts,
  dark charcoal #2A2C33 for the strongest details.
- Keep the details that make the icon recognizable (buckles, folds, cut-outs, inner symbols): use about 5 to 8 shapes.
  Separate touching parts with thin white gaps. No line or gap thinner than 1/40 of the image width; drop anything smaller.
- No text, letters or numbers, unless the logo itself is a letter.
- Centered, filling about 70% of a square canvas, on a plain pure white background.
- Do not draw any frame, border, card or HUD lines around it.

Output one square image."""


def slug(name: str) -> str:
    plain = unicodedata.normalize("NFKD", name.replace("ı", "i")).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", plain.lower()).strip("-")


def load_icon(path: Path) -> Image.Image:
    if path.suffix.lower() in (".exe", ".dll"):
        from icoextract import IconExtractor

        icon = Image.open(IconExtractor(str(path)).get_icon())
        # an .ico holds several sizes: take the largest
        sizes = sorted(icon.info.get("sizes", {icon.size}), key=lambda s: s[0] * s[1])
        icon.size = sizes[-1]
    else:
        icon = Image.open(path)
    icon = icon.convert("RGBA")
    # the drawing alone, without the padding around it
    box = icon.getbbox()
    return icon.crop(box) if box else icon


def on_white(image: Image.Image, size: int = 512) -> Image.Image:
    image = image.copy()
    image.thumbnail((size, size), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    canvas.alpha_composite(image, ((size - image.width) // 2, (size - image.height) // 2))
    return canvas.convert("RGB")


def reference(file: str) -> Image.Image:
    # the logo alone, without the card frame the model must not copy
    card = Image.open(CARDS / file).convert("RGBA").crop((LOGO_AT, LOGO_AT, LOGO_AT + LOGO, LOGO_AT + LOGO))
    return on_white(card.resize((LOGO * 4, LOGO * 4), Image.LANCZOS))


def ask(client, model: str, icon: Image.Image) -> Image.Image:
    from google.genai import types

    reply = client.models.generate_content(
        model=model,
        contents=[PROMPT, on_white(icon), *(reference(f) for f in REFERENCES)],
        config=types.GenerateContentConfig(response_modalities=["IMAGE"]),
    )
    for part in reply.candidates[0].content.parts:
        if part.inline_data is not None:
            return Image.open(io.BytesIO(part.inline_data.data)).convert("RGB")
    raise RuntimeError("the model sent no image: " + (reply.text or "no text either"))


def cut_out(drawing: Image.Image) -> Image.Image:
    """White becomes transparent, with a soft edge where the shapes were antialiased against it."""
    rgba = drawing.convert("RGBA")
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            r, g, b, _ = pixels[x, y]
            alpha = max(0, min(255, int((242 - max(r, g, b)) * 255 / 30)))
            pixels[x, y] = (r, g, b, alpha)
    box = rgba.getbbox()
    return rgba.crop(box) if box else rgba


def value(r: int, g: int, b: int) -> float:
    return 0.6 * (0.299 * r + 0.587 * g + 0.114 * b) + 0.4 * max(r, g, b)


def toned(logo: Image.Image) -> Image.Image:
    """Fits the logo in the set's square and greys it the way CardIcons.Greyed does."""
    logo = logo.copy()
    logo.thumbnail((LOGO, LOGO), Image.LANCZOS)
    square = Image.new("RGBA", (LOGO, LOGO), (0, 0, 0, 0))
    square.alpha_composite(logo, ((LOGO - logo.width) // 2, (LOGO - logo.height) // 2))

    pixels = square.load()
    seen = [value(*pixels[x, y][:3]) for y in range(LOGO) for x in range(LOGO) if pixels[x, y][3] >= 20]
    if not seen:
        return square
    high = max(seen)
    low = min(min(seen), high * SHADOW)
    for y in range(LOGO):
        for x in range(LOGO):
            r, g, b, a = pixels[x, y]
            tone = SHADOW + (1 - SHADOW) * min(1, max(0, (value(r, g, b) - low) / max(1, high - low)))
            pixels[x, y] = (int(GREY[0] * tone), int(GREY[1] * tone), int(GREY[2] * tone), a)
    return square


def framed(logo: Image.Image) -> Image.Image:
    card = Image.open(CARDS / "card-frame.png").convert("RGBA").resize((CARD, CARD), Image.LANCZOS)
    card.alpha_composite(toned(logo), (LOGO_AT, LOGO_AT))
    return card


def on_body(card: Image.Image, body, ink) -> Image.Image:
    """The card as Floe redraws it on a dock its grey fades into (CardIcons.OnBody)."""
    out = card.copy()
    pixels = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = pixels[x, y]
            tone = min(1, max(r, g, b) / GREY[2])
            pixels[x, y] = tuple(int(body[i] + (ink[i] - body[i]) * tone) for i in range(3)) + (a,)
    return out


def preview(cards: list[Image.Image], path: Path) -> None:
    dark, yellow, ink = (16, 18, 24), (252, 238, 10), (10, 10, 16)
    dock = 54
    width = 16 + len(cards) * (CARD + 16)
    sheet = Image.new("RGB", (width, CARD + dock * 2 + 48), (40, 42, 48))
    draw = ImageDraw.Draw(sheet)
    for i, card in enumerate(cards):
        x = 16 + i * (CARD + 16)
        sheet.paste(card, (x, 8), card)
        draw.text((x, CARD + 10), f"variant {i + 1}", fill=(220, 220, 220))
        small = card.resize((dock, dock), Image.LANCZOS)
        tile = Image.new("RGB", (dock + 8, dock + 8), dark)
        tile.paste(small, (4, 4), small)
        sheet.paste(tile, (x, CARD + 28))
        on_yellow = on_body(card, yellow, ink).resize((dock, dock), Image.LANCZOS)
        tile = Image.new("RGB", (dock + 8, dock + 8), yellow)
        tile.paste(on_yellow, (4, 4), on_yellow)
        sheet.paste(tile, (x + dock + 16, CARD + 28))
    sheet.save(path)


def install(card: Path, name: str) -> None:
    target = CARDS / f"{slug(name)}.png"
    Image.open(card).save(target)
    names = CARDS / "names.csv"
    lines = names.read_text(encoding="utf-8").splitlines()
    if not any(line.split(";")[0].casefold() == name.casefold() for line in lines):
        with names.open("a", encoding="utf-8", newline="\n") as f:
            f.write(f"{name};{slug(name)}\n")
    print(f"installed {target.name}; rebuild Floe to ship it")


def finish(drawing: Image.Image, base: str, i: int) -> Image.Image:
    drawing.save(OUT / f"{base}-{i + 1}-raw.png")
    card = framed(cut_out(drawing))
    card.save(OUT / f"{base}-{i + 1}.png")
    print(f"variant {i + 1}: {OUT / f'{base}-{i + 1}.png'}")
    return card


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("name", help="the app's name as the dock shows it")
    parser.add_argument("icon", nargs="?", help="the app's icon: png, ico, exe or dll")
    parser.add_argument("--variants", type=int, default=2)
    parser.add_argument("--model", default=MODEL)
    parser.add_argument("--install", metavar="CARD", help="copy this generated card into the set under NAME")
    parser.add_argument("--kit", action="store_true", help="write the prompt and images for the Gemini app instead")
    parser.add_argument("--from", dest="drawings", metavar="IMAGE", nargs="+", help="make cards from images the app drew")
    args = parser.parse_args()

    if args.install:
        install(Path(args.install), args.name)
        return
    OUT.mkdir(parents=True, exist_ok=True)
    base = slug(args.name)
    if args.drawings:
        cards = [finish(Image.open(d).convert("RGB"), base, i) for i, d in enumerate(args.drawings)]
        preview(cards, OUT / f"{base}-preview.png")
        print(f"preview: {OUT / f'{base}-preview.png'}")
        return
    if not args.icon:
        parser.error("give the app's icon")
    if args.kit:
        kit = OUT / f"{base}-kit"
        kit.mkdir(exist_ok=True)
        (kit / "prompt.txt").write_text(PROMPT, encoding="utf-8")
        on_white(load_icon(Path(args.icon))).save(kit / "1-icon.png")
        for i, f in enumerate(REFERENCES):
            reference(f).save(kit / f"{i + 2}-style.png")
        print(f"kit: {kit}  (paste prompt.txt and the images 1..3 in order, then run --from on what comes back)")
        return
    if not os.environ.get("GEMINI_API_KEY"):
        sys.exit("GEMINI_API_KEY is not set")

    from google import genai

    client = genai.Client()
    icon = load_icon(Path(args.icon))
    cards = [finish(ask(client, args.model, icon), base, i) for i in range(args.variants)]
    preview(cards, OUT / f"{base}-preview.png")
    print(f"preview: {OUT / f'{base}-preview.png'}")


if __name__ == "__main__":
    main()
