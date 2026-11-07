# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-171 M: the partner map markers' pictures for the launcher's per-run brand pak.

The artist's knights are 4 colours x 8 rotation frames, 1800x2200 RGBA PNG, in assets/map-markers/source/<colour>/<n>.png
(colours red purple blue green = players 1..4; frame n = 0..7). This writes, into the brand pak's source folder
docs/branding/pak-source/Libs/UI/Textures/Icons/Map/ (the folder the game's map reads its icons from; our files all begin kct_,
so none of them shadows a file of the game's):

    kct_<c><n>[b|c|d][M]_icon.dds    the picture; b, c, d: the same art under the next revisions' names; M: the map's 2x size           SIZE x SIZE DXT5, no mips (c = r p b g, n = 0..7; the game's own map icons are 64 x 64, and the compass draws them at that size)
    kct_<c><n>_undiscovered_icon.dds the same picture (the map asks for this variant of a not-yet-discovered point)
    kct_<c><n>_sh_icon.dds           its shadow             SIZE/2 x SIZE/2 DXT5

and docs/branding/map-markers-contact-sheet.png (4 rows x 8 columns, each cell labelled colour / the compass direction this build
assumes the frame faces / the source file name).

The artist's pixels are kept: every frame is cut with the SAME box (the union of the 32 frames' solid pixels, so the pedestal never
moves between frames) and only scaled down (a high-quality filter, in premultiplied alpha so no dark fringes) to the size the map
wants. Source and output sizes are printed.

    python tools/Build-MapMarkers.py [--size 128] [--src assets/map-markers/source]
"""
import argparse, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COLOURS = [("red", "r"), ("purple", "p"), ("blue", "b"), ("green", "g")]
COMPASS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]


def facing_of_frame(n):
    """The assumption recorded in docs/WO-171-findings.md: frame 0 is the front view (facing the viewer = south on the map) and
    the frames turn clockwise as seen from above, one octant each: frame n faces compass octant (n + 4) mod 8."""
    return COMPASS[(n + 4) % 8]


def premultiply(im):
    a = np.asarray(im.convert("RGBA"), dtype=np.float32)
    a[..., :3] *= a[..., 3:4] / 255.0
    return a


def unpremultiply(a):
    out = a.copy()
    alpha = out[..., 3:4]
    with np.errstate(divide="ignore", invalid="ignore"):
        out[..., :3] = np.where(alpha > 0, out[..., :3] * 255.0 / np.maximum(alpha, 1e-6), 0)
    return np.clip(out, 0, 255).astype(np.uint8)


def resize_premult(im, size):
    """Lanczos in premultiplied alpha, channel by channel."""
    a = premultiply(im)
    chans = []
    for c in range(4):
        chans.append(np.asarray(Image.fromarray(a[..., c], mode="F").resize(size, Image.LANCZOS)))
    return Image.fromarray(unpremultiply(np.stack(chans, axis=-1)), "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=64)
    ap.add_argument("--src", default=os.path.join(ROOT, "assets", "map-markers", "source"))
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "branding", "pak-source", "Libs", "UI", "Textures", "Icons", "Map"))
    ap.add_argument("--sheet", default=os.path.join(ROOT, "docs", "branding", "map-markers-contact-sheet.png"))
    a = ap.parse_args()

    frames = {}
    missing = []
    for colour, _ in COLOURS:
        for n in range(8):
            p = os.path.join(a.src, colour, "%d.png" % n)
            if os.path.isfile(p):
                frames[(colour, n)] = Image.open(p).convert("RGBA")
            else:
                missing.append(p)
    if missing:
        print("MISSING %d of 32 frames:" % len(missing))
        for m in missing: print("  ", os.path.relpath(m, ROOT))
    if not frames:
        sys.exit("no frames found under " + a.src)
    sizes = {im.size for im in frames.values()}
    if len(sizes) != 1:
        sys.exit("the frames are not all one size: %r" % sizes)
    W, H = sizes.pop()

    # one box for all frames: the union of the solid pixels (alpha > 48; the faintest shadow tail would stretch it for nothing)
    box = None
    for im in frames.values():
        b = im.getchannel("A").point(lambda v: 255 if v > 48 else 0).getbbox()
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    bw, bh = box[2] - box[0], box[3] - box[1]
    side = max(bw, bh)
    cx, cy = (box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0
    sq = (round(cx - side / 2.0), round(cy - side / 2.0), round(cx - side / 2.0) + side, round(cy - side / 2.0) + side)
    print("source frames %dx%d; solid box %s (%dx%d); square cut %s; output %dx%d (scale %.4f)" % (W, H, box, bw, bh, sq, a.size, a.size, a.size / side))

    os.makedirs(a.out, exist_ok=True)
    cells = {}

    def write_set(icon, base, size):
        """icon (size x size), its undiscovered variant and its shadow (half size), under base and the revisions' names."""
        sil = np.zeros((size, size, 4), dtype=np.uint8)
        al = np.asarray(icon.getchannel("A"), dtype=np.float32) * 0.45
        sil[..., 3] = np.clip(al, 0, 255).astype(np.uint8)
        sh = Image.fromarray(sil, "RGBA").filter(ImageFilter.GaussianBlur(size / 40.0)).resize((size // 2, size // 2), Image.LANCZOS)
        # a revision is the same art under the next name: a name can be loaded into the game's localization once per game run, so a
        # colour taken over by another name uses the next revision's keys (docs/WO-171-findings.md)
        for rev in ("", "b", "c", "d"):
            icon.save(base.replace("@", rev) + "_icon.dds", pixel_format="DXT5")
            icon.save(base.replace("@", rev) + "_undiscovered_icon.dds", pixel_format="DXT5")
            sh.save(base.replace("@", rev) + "_sh_icon.dds", pixel_format="DXT5")

    for (colour, n), im in sorted(frames.items()):
        letter = dict(COLOURS)[colour]
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(im.crop(sq), (0, 0))
        small = resize_premult(canvas, (a.size, a.size))
        large = resize_premult(canvas, (a.size * 2, a.size * 2))
        write_set(small, os.path.join(a.out, "kct_%s%d@" % (letter, n)), a.size)          # the compass draws it 1:1
        write_set(large, os.path.join(a.out, "kct_%s%d@M" % (letter, n)), a.size * 2)     # the map draws its icons scaled down
        cells[(colour, n)] = large
        assert Image.open(os.path.join(a.out, "kct_%s%d_icon.dds" % (letter, n))).size == (a.size, a.size)
        assert Image.open(os.path.join(a.out, "kct_%s%dM_icon.dds" % (letter, n))).size == (a.size * 2, a.size * 2)
    print("wrote %d pictures x 4 revisions x 2 sizes (+ undiscovered and shadow variants) under %s" % (len(cells), os.path.relpath(a.out, ROOT)))

    # the contact sheet
    cw, ch, lab = 200, 200 + 48, 48
    sheet = Image.new("RGB", (8 * cw, 4 * ch), (58, 58, 64))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("arial.ttf", 15)
    except OSError:
        font = ImageFont.load_default()
    for r, (colour, letter) in enumerate(COLOURS):
        for n in range(8):
            x0, y0 = n * cw, r * ch
            im = cells.get((colour, n))
            if im is not None:
                big = im.resize((200, 200), Image.LANCZOS)
                tile = Image.new("RGBA", (200, 200), (58, 58, 64, 255)); tile.alpha_composite(big)
                sheet.paste(tile.convert("RGB"), (x0, y0))
            d.text((x0 + 6, y0 + 202), "%s  frame %d" % (colour, n), fill=(235, 235, 235), font=font)
            d.text((x0 + 6, y0 + 222), "faces %s (assumed)  %d.png" % (facing_of_frame(n), n), fill=(170, 200, 255), font=font)
    sheet.save(a.sheet, optimize=True)
    print("wrote", os.path.relpath(a.sheet, ROOT), sheet.size)


if __name__ == "__main__":
    main()
