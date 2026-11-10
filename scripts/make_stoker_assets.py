"""Generates the stoker's two shapes and its two glass textures.

Canonical pose: the `side=north` variant. The stoker looks NORTH (-Z) for the track, and the
window is on that NORTH face: the track is where the window looks. The player who placed it
looking north sees its back. The two pins sit on the top plate where the anchor has them.
"""
import json
import math
import os
import random
from PIL import Image

ROOT = r"C:/Develope/VintageStory/Projects/SignalsLink/SignalsLink.YTT/assets/signalslinkytt"
SHAPES = os.path.join(ROOT, "shapes", "block")
TEX = os.path.join(ROOT, "textures", "block")
os.makedirs(SHAPES, exist_ok=True)
os.makedirs(TEX, exist_ok=True)

TEXTURES = {
    "iron": "signalslink:block/castironbrown",
    "plate": "signalslink:block/iron",
    "rivet": "signalslink:block/rustycolored",
    "porcelain": "signalslink:block/pin-porcelain",
    "brass": "signalslink:block/pin-brass",
    "glass": "signalslinkytt:block/stoker-glass-dark",
}


def face(tex, w, h, glow=None):
    f = {"texture": "#" + tex, "uv": [0, 0, min(16, w), min(16, h)]}
    if glow:
        f["glow"] = glow
    return f


def box(name, frm, to, tex, glow=None, glow_faces=()):
    x1, y1, z1 = frm
    x2, y2, z2 = to
    dx, dy, dz = x2 - x1, y2 - y1, z2 - z1
    return {
        "name": name,
        "from": [x1, y1, z1],
        "to": [x2, y2, z2],
        "faces": {
            "north": face(tex, dx, dy, glow if "north" in glow_faces else None),
            "east": face(tex, dz, dy, glow if "east" in glow_faces else None),
            "south": face(tex, dx, dy, glow if "south" in glow_faces else None),
            "west": face(tex, dz, dy, glow if "west" in glow_faces else None),
            "up": face(tex, dx, dz, glow if "up" in glow_faces else None),
            "down": face(tex, dx, dz, glow if "down" in glow_faces else None),
        },
    }


def terminal(index, x):
    # Same build as the anchor's: a porcelain block with a brass stud and a brass washer.
    return [
        box(f"terminal_{index}", (x, 9.5, 7), (x + 2, 11.5, 9), "porcelain"),
        box(f"stud_{index}", (x + 0.5, 11.5, 7.5), (x + 1.5, 12.5, 8.5), "brass"),
        box(f"washer_{index}", (x, 11.7, 7), (x + 2, 12.2, 9), "brass"),
    ]


def rivet_north(name, x, y, z):
    # A flat rusty stud standing proud of a plate toward -Z.
    return box(name, (x - 0.25, y - 0.25, z - 0.2), (x + 0.25, y + 0.25, z), "rivet")


def rivet(name, x, y, z, along="z"):
    # A flat rusty stud, barely proud of its plate. `along` is the axis it sticks out on.
    if along == "z":
        return box(name, (x - 0.25, y - 0.25, z), (x + 0.25, y + 0.25, z + 0.2), "rivet")
    return box(name, (x, y - 0.25, z - 0.25), (x + 0.2, y + 0.25, z + 0.25), "rivet")


def shape(lit):
    els = []
    # Foot and body: lower and wider than the anchor's upright cabinet.
    els.append(box("foot", (1, 0, 3), (15, 1, 13), "iron"))
    els.append(box("body", (2, 1, 4), (14, 9, 12), "iron"))
    els.append(box("top", (1.5, 9, 3.5), (14.5, 9.5, 12.5), "iron"))

    # Riveted front plate on the north face, toward the track, with the window a third of the
    # way up. Everything here stands proud toward -Z.
    els.append(box("frontplate", (3, 1.5, 3.5), (13, 8.5, 4), "plate"))
    for i, (rx, ry) in enumerate([(3.6, 2.1), (12.4, 2.1), (3.6, 7.9), (12.4, 7.9), (8, 2.1), (8, 7.9)]):
        els.append(rivet_north(f"platerivet_{i}", rx, ry, 3.5))

    els.append(box("frame", (5.5, 3.25, 3), (10.5, 6.75, 3.5), "plate"))
    for i, (rx, ry) in enumerate([(5.9, 3.65), (10.1, 3.65), (5.9, 6.35), (10.1, 6.35)]):
        els.append(rivet_north(f"framerivet_{i}", rx, ry, 3))

    glow = 200 if lit else None
    els.append(box("glass", (6, 3.75, 2.75), (10, 6.25, 3), "glass", glow, ("north", "up", "down", "east", "west")))

    # Bands round the sides, so the flanks are not a bare slab.
    for i, (y1, y2) in enumerate([(2, 2.75), (6.25, 7)]):
        els.append(box(f"bandw_{i}", (1.5, y1, 4.5), (2, y2, 11.5), "plate"))
        els.append(box(f"bande_{i}", (14, y1, 4.5), (14.5, y2, 11.5), "plate"))
        for j, rz in enumerate([5.2, 10.8]):
            els.append(rivet(f"bandrivetw_{i}_{j}", 1.5, (y1 + y2) / 2, rz, along="xneg"))
            els.append(rivet(f"bandrivete_{i}_{j}", 14.5, (y1 + y2) / 2, rz, along="x"))

    els += terminal(0, 9)
    els += terminal(1, 5)

    textures = dict(TEXTURES)
    if lit:
        textures["glass"] = "signalslinkytt:block/stoker-glass-lit"

    return {"editor": {"allAngles": True}, "textureWidth": 16, "textureHeight": 16,
            "textures": textures, "elements": els}


def fix_xneg(els):
    # rivet(along="xneg") was emitted as if along +x; mirror it to stick out of the west face.
    for e in els:
        if e["name"].startswith("bandrivetw"):
            x1, y1, z1 = e["from"]
            x2, y2, z2 = e["to"]
            e["from"] = [x1 - 0.2, y1, z1]
            e["to"] = [x1, y2, z2]
    return els


for lit, name in [(False, "stoker"), (True, "stoker_lit")]:
    s = shape(lit)
    s["elements"] = fix_xneg(s["elements"])
    with open(os.path.join(SHAPES, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(s, f, indent=1)
    print("wrote", name, len(s["elements"]), "elements")


def glass(path, base, hot, noise, seed):
    rnd = random.Random(seed)
    im = Image.new("RGBA", (32, 32))
    px = im.load()
    for y in range(32):
        for x in range(32):
            # Brightest in the lower middle, where the fire is; darker at the edges.
            dx = (x - 15.5) / 15.5
            dy = (y - 20) / 16
            d = min(1.0, math.sqrt(dx * dx + dy * dy))
            t = (1 - d) ** 1.6
            r = base[0] + (hot[0] - base[0]) * t + rnd.uniform(-noise, noise)
            g = base[1] + (hot[1] - base[1]) * t + rnd.uniform(-noise, noise)
            b = base[2] + (hot[2] - base[2]) * t + rnd.uniform(-noise, noise)
            px[x, y] = (int(max(0, min(255, r))), int(max(0, min(255, g))), int(max(0, min(255, b))), 255)
    # A thin dark rim, so the pane reads as set into the frame.
    for i in range(32):
        for (x, y) in [(i, 0), (i, 31), (0, i), (31, i)]:
            r, g, b, a = px[x, y]
            px[x, y] = (r * 2 // 3, g * 2 // 3, b * 2 // 3, 255)
    im.save(path)
    print("wrote", path)


glass(os.path.join(TEX, "stoker-glass-lit.png"), base=(196, 72, 10), hot=(255, 214, 96), noise=10, seed=7)
glass(os.path.join(TEX, "stoker-glass-dark.png"), base=(58, 26, 8), hot=(112, 54, 16), noise=6, seed=11)
