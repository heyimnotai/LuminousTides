"""Renders gridded orthographic reference views of the models that need rigging, for joint placement.

Run:  blender -b --python ortho_views.py
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

# name, size, axis, rot_z, views [(view, centre, span)]
JOBS = [
    ("enemy", 2.1, 2, 0, [("FRONT", (0, 0, 1.05), 2.4), ("RIGHT", (0, 0, 1.05), 2.4)]),
    ("arm", 0.52, 0, 0, [("TOP", (0, 0, 0), 0.6), ("FRONT", (0, 0, 0.1), 0.6)]),
    ("carbine", 0.80, 0, 0, [("FRONT", (0, 0, 0.14), 0.9), ("TOP", (0, 0, 0), 0.9)]),
]
for name, size, axis, rot, views in JOBS:
    lt.reset()
    obj = lt.import_glb(name)
    lt.normalize(obj, size, axis, rot)
    lo, hi = lt.bounds(obj)
    print(name, "min", tuple(round(v, 3) for v in lo), "max", tuple(round(v, 3) for v in hi))
    for view, centre, span in views:
        lt.ortho_render(os.path.join(lt.SHOTS, f"ortho_{name}_{view}.png"), centre, span, view)
