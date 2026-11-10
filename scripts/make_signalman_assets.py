"""Generates the signalman's shape.

Canonical pose: the `side=north` variant. The plate watches the block NORTH (-Z) of it; the
player who placed it looking north stands south of it, so the strip with the two pins is at the
SOUTH edge, away from the track, and the little cabinet faces the player.
"""
import json
import os

ROOT = r"C:/Develope/VintageStory/Projects/SignalsLink/SignalsLink.YTT/assets/signalslinkytt"
SHAPES = os.path.join(ROOT, "shapes", "block")
os.makedirs(SHAPES, exist_ok=True)

TEXTURES = {
    "iron": "signalslink:block/castironbrown",
    "plate": "signalslink:block/iron",
    "rivet": "signalslink:block/rustycolored",
    "porcelain": "signalslink:block/pin-porcelain",
    "brass": "signalslink:block/pin-brass",
}


def face(tex, w, h):
    return {"texture": "#" + tex, "uv": [0, 0, min(16, w), min(16, h)]}


def box(name, frm, to, tex):
    x1, y1, z1 = frm
    x2, y2, z2 = to
    dx, dy, dz = x2 - x1, y2 - y1, z2 - z1
    return {
        "name": name,
        "from": [x1, y1, z1],
        "to": [x2, y2, z2],
        "faces": {
            "north": face(tex, dx, dy), "east": face(tex, dz, dy), "south": face(tex, dx, dy),
            "west": face(tex, dz, dy), "up": face(tex, dx, dz), "down": face(tex, dx, dz),
        },
    }


def rivet_up(name, x, y, z):
    # A flat rusty stud standing on a horizontal plate.
    return box(name, (x - 0.25, y, z - 0.25), (x + 0.25, y + 0.2, z + 0.25), "rivet")


def rivet_south(name, x, y, z):
    return box(name, (x - 0.25, y - 0.25, z), (x + 0.25, y + 0.25, z + 0.2), "rivet")


def terminal(index, x, z):
    return [
        box(f"terminal_{index}", (x, 1.25, z), (x + 2, 4.25, z + 2), "porcelain"),
        box(f"stud_{index}", (x + 0.5, 4.25, z + 0.5), (x + 1.5, 5.25, z + 1.5), "brass"),
        box(f"washer_{index}", (x, 4.45, z), (x + 2, 4.95, z + 2), "brass"),
    ]


els = []
# The ground plate, with the strip of sheet metal along its south edge.
els.append(box("plate", (1, 0, 2), (15, 0.75, 14), "iron"))
els.append(box("strip", (2, 0.75, 10.5), (14, 1.25, 14), "plate"))
for i, rx in enumerate([3, 8, 13]):
    els.append(rivet_up(f"striprivet_{i}", rx, 1.25, 13.5))

# A low cabinet toward the track, with a riveted lid and a riveted door toward the player.
els.append(box("cabinet", (3, 0.75, 3.5), (13, 3.75, 10), "iron"))
els.append(box("lid", (2.5, 3.75, 3), (13.5, 4.25, 10.5), "plate"))
for i, (rx, rz) in enumerate([(3.2, 3.7), (12.8, 3.7), (3.2, 9.8), (12.8, 9.8)]):
    els.append(rivet_up(f"lidrivet_{i}", rx, 4.25, rz))
els.append(box("door", (4.5, 1.25, 10), (11.5, 3.25, 10.3), "plate"))
for i, (rx, ry) in enumerate([(5, 1.7), (11, 1.7), (5, 2.8), (11, 2.8)]):
    els.append(rivet_south(f"doorrivet_{i}", rx, ry, 10.3))

els += terminal(0, 9, 11.5)
els += terminal(1, 5, 11.5)

shape = {"editor": {"allAngles": True}, "textureWidth": 16, "textureHeight": 16,
         "textures": TEXTURES, "elements": els}

with open(os.path.join(SHAPES, "signalman.json"), "w", encoding="utf-8") as f:
    json.dump(shape, f, indent=1)
print("wrote signalman", len(els), "elements")
