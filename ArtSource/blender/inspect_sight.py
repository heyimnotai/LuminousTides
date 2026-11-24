"""Renders gridded close-ups of the carbine's sight in view-model space (used to align aim-down-sights).

Run:  blender -b --python inspect_sight.py
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

lt.reset()
gun = lt.import_glb("carbine")
lt.normalize(gun, 0.80, 0)
gun.rotation_euler = (0, 0, math.radians(90))
lt.apply(gun)
gun.location = (0, -0.10, -0.125)
lt.apply(gun)

# Sight region statistics: everything above the receiver top.
top = [v.co for v in gun.data.vertices if v.co.z > 0.105]
print("sight verts", len(top))
for axis, name in enumerate("xyz"):
    vals = [c[axis] for c in top]
    print(f"  {name}: {min(vals):.4f} .. {max(vals):.4f}")

lt.ortho_render(os.path.join(lt.SHOTS, "sight_BACK.png"), (0, 0, 0.115), 0.14, 'BACK', 700)
lt.ortho_render(os.path.join(lt.SHOTS, "sight_FRONT.png"), (0, 0, 0.115), 0.14, 'FRONT', 700)
lt.ortho_render(os.path.join(lt.SHOTS, "sight_RIGHT.png"), (0, -0.07, 0.10), 0.24, 'RIGHT', 700)
