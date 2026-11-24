"""Gridded side and top views of a weapon model, for placing hands.  blender -b --python inspect_gun.py -- <name> <length>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

name, length = sys.argv[sys.argv.index("--") + 1], float(sys.argv[sys.argv.index("--") + 2])
lt.reset()
gun = lt.import_glb(name)
lt.normalize(gun, length, 0)
lo, hi = lt.bounds(gun)
print(name, "min", tuple(round(v, 3) for v in lo), "max", tuple(round(v, 3) for v in hi))
lt.ortho_render(os.path.join(lt.SHOTS, f"gun_{name}_FRONT.png"), (0, 0, (lo.z + hi.z) / 2), length * 1.1, 'FRONT', 900)
lt.ortho_render(os.path.join(lt.SHOTS, f"gun_{name}_TOP.png"), (0, 0, 0), length * 1.1, 'TOP', 900)
