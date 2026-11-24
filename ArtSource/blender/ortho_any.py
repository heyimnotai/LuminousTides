"""Front, side and top orthographic views of any Meshy model at a given size, for placing joints by eye.

Run:  blender -b --python ortho_any.py -- <name> <size in metres> <axis 0|1|2> [yaw degrees]
Pixel = (coord - centre) * 900 / span + 450, with the span and centre printed below.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

args = sys.argv[sys.argv.index("--") + 1:]
name, size, axis = args[0], float(args[1]), int(args[2])
yaw = float(args[3]) if len(args) > 3 else 0.0
lt.reset()
obj = lt.import_glb(name)
lt.normalize(obj, size, axis, rot_z_deg=yaw)
lo, hi = lt.bounds(obj)
span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z) * 1.1
centre = (0, 0, (lo.z + hi.z) / 2)
print(name, "min", tuple(round(v, 3) for v in lo), "max", tuple(round(v, 3) for v in hi), "span", round(span, 3), "centre z", round(centre[2], 3))
for view in ("FRONT", "RIGHT", "TOP"):
    lt.ortho_render(os.path.join(lt.SHOTS, f"ortho_{name}_{view}.png"), centre, span, view, 900)
