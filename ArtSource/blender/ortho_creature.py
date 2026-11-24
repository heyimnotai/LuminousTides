"""Gridded front and side views of a creature model scaled to a given height, for placing skeleton joints by eye.

Run:  blender -b --python ortho_creature.py -- <name> <height in metres>
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

name, height = sys.argv[sys.argv.index("--") + 1], float(sys.argv[sys.argv.index("--") + 2])
lt.reset()
obj = lt.import_glb(name)
lt.normalize(obj, height, 2)
lo, hi = lt.bounds(obj)
print(name, "min", tuple(round(v, 3) for v in lo), "max", tuple(round(v, 3) for v in hi))
span = height * 1.15
for view in ("FRONT", "RIGHT"):
    lt.ortho_render(os.path.join(lt.SHOTS, f"creature_{name}_{view}.png"), (0, 0, height / 2), span, view, 900)
