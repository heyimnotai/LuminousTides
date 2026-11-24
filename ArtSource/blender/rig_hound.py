"""Rigs and animates the Varrik Hound (four legs and a tail), then exports Assets/Art/Models/varrik_hound.fbx.

Joints were read off ortho_any.py renders. Clips: Idle, Run, Attack, Hit, Death.
Run:  blender -b --python rig_hound.py [-- preview]
"""
import bpy, sys, os, math
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt
import lt_rig

PREVIEW = "preview" in sys.argv
MODEL = "varrik_hound"
HEIGHT = 1.25
SHIFT_Y = 0.6          # the long tail puts the body well forward of the model's centre: bring the body over the origin

# name: (head, tail, parent). The hound faces -Y; .L is +X and is mirrored to .R.
# Front legs reuse the arm names and hind legs the leg names, so the game finds a "head" bone the same way as on bipeds.
BONES = {
    "root":        ((0, 0, 0), (0, 0, 0.2), None),
    "hips":        ((0, 0.81, 1.10), (0, 0.50, 1.12), "root"),
    "spine":       ((0, 0.50, 1.12), (0, 0.10, 1.13), "hips"),
    "chest":       ((0, 0.10, 1.13), (0, -0.38, 1.13), "spine"),
    "neck":        ((0, -0.38, 1.13), (0, -0.72, 1.02), "chest"),
    "head":        ((0, -0.72, 1.02), (0, -1.00, 0.82), "neck"),
    "tail1":       ((0, 1.05, 1.07), (0, 1.61, 0.99), "hips"),
    "tail2":       ((0, 1.61, 0.99), (0, 2.19, 0.98), "tail1"),
    "upper_arm.L": ((0.20, -0.26, 0.83), (0.22, -0.23, 0.51), "chest"),
    "forearm.L":   ((0.22, -0.23, 0.51), (0.24, -0.33, 0.10), "upper_arm.L"),
    "hand.L":      ((0.24, -0.33, 0.10), (0.24, -0.52, 0.03), "forearm.L"),
    "thigh.L":     ((0.15, 0.87, 0.89), (0.16, 0.75, 0.51), "hips"),
    "shin.L":      ((0.16, 0.75, 0.51), (0.17, 1.11, 0.36), "thigh.L"),
    "foot.L":      ((0.17, 1.11, 0.36), (0.17, 1.14, 0.08), "shin.L"),
    "toe.L":       ((0.17, 1.14, 0.08), (0.17, 0.99, 0.03), "foot.L"),
}
SPINE = 1.0          # sign of the back's flex; flip if the stretched frames come out hunched

# One fore leg through a gallop stride: phase, upper leg, lower leg, paw (degrees). Phase 0 is the paw striking.
FORE = [
    (0.00, -34, -6, -8),         # strikes, reaching ahead
    (0.15, -12, -16, 10),        # takes the weight
    (0.35, 36, -4, 18),          # pulls through to behind the shoulder
    (0.50, 30, -40, 30),         # lifts
    (0.70, -10, -62, 26),        # folds and is carried forward
    (0.88, -40, -24, 0),         # unfolds to reach
]
# One hind leg: phase, thigh, shin, foot, toe.
HIND = [
    (0.00, -40, -6, 8, 0),       # strikes, drawn well forward under the body
    (0.15, -18, -24, 26, -6),    # compresses
    (0.38, 34, 6, 2, 8),         # drives straight out behind
    (0.52, 26, -30, 34, -10),    # lifts
    (0.72, -20, -44, 44, -10),   # gathered up under the belly
    (0.90, -44, -16, 18, 0),     # reaches for the next strike
]
LENGTHS = {"Idle": 60, "Run": 16, "Attack": 24, "Hit": 10, "Death": 30}


def make_animations(arm):
    # Rotations are about the armature's axes. For this horizontal body: +rx tips a forward-pointing bone (chest,
    # neck, head) nose-down and swings a hanging leg backwards; +rz turns left; +ry rolls right side down.
    A = lt_rig.Animator(arm)

    A.begin("Idle", 60, loop=True)
    for f, s in ((0, 0), (15, 1), (30, 0), (45, -1), (60, 0)):
        A.key(f, {"chest": (1.5 * abs(s), 0, 2 * s), "neck": (4 + 3 * abs(s), 0, 6 * s), "head": (3, 0, 8 * s),
                  "tail1": (0, 0, -14 * s), "tail2": (0, 0, -18 * s), "upper_arm.LR": (0, 0, 0)}, hips_z=-0.012 * abs(s))

    # Run: a gallop. The hind feet strike one after the other and drive the body forward, the fore feet reach, land
    # and pull through, then all four are gathered under the body in the air. The spine stretches long as the hind
    # legs push and bunches as they are drawn up for the next stride; the head stays level while the back works.
    A.begin("Run", 16, loop=True)
    for f in range(0, 17):
        t = f / 16
        stretch = math.cos(2 * math.pi * (t - 0.30))        # +1 fully stretched out, -1 bunched up in the air
        pose = {"hips": (SPINE * 7 * stretch, 0, 0), "spine": (-SPINE * 6 * stretch, 0, 0), "chest": (-SPINE * 8 * stretch, 0, 0),
                "neck": (-4 + SPINE * 7 * stretch, 0, 0), "head": (4 + SPINE * 4 * stretch, 0, 0),
                "tail1": (-10 * stretch, 0, 0), "tail2": (-14 * stretch, 0, 0)}
        for side, lead in ((".L", 0.0), (".R", 0.10)):
            thigh, shin, foot, toe = lt_rig.cycle(HIND, t - lead)
            pose["thigh" + side] = (thigh, 0, 0); pose["shin" + side] = (shin, 0, 0); pose["foot" + side] = (foot, 0, 0); pose["toe" + side] = (toe, 0, 0)
            upper, fore, hand = lt_rig.cycle(FORE, t - 0.36 - lead)
            pose["upper_arm" + side] = (upper, 0, 0); pose["forearm" + side] = (fore, 0, 0); pose["hand" + side] = (hand, 0, 0)
        A.key(f, pose, hips_z=-0.02 + 0.055 * math.cos(2 * math.pi * (t - 0.78)))

    A.begin("Attack", 24)       # coils back, lunges and snaps (the bite lands at frame 11), then worries what it caught
    A.key(0, {"neck": (4, 0, 0)})
    A.key(7, {"hips": (-10, 0, 0), "chest": (-8, 0, 0), "neck": (-16, 0, 0), "head": (-10, 0, 0),
              "thigh.LR": (-20, 0, 0), "shin.LR": (-20, 0, 0), "foot.LR": (24, 0, 0), "upper_arm.LR": (16, 0, 0), "tail1": (-20, 0, 0)},
          hips_z=-0.10, hips_y=0.16, ease="out")
    A.key(11, {"hips": (8, 0, 0), "chest": (6, 0, 0), "neck": (14, 0, 0), "head": (22, 0, 0),
               "upper_arm.LR": (-46, 0, 0), "forearm.LR": (-14, 0, 0), "thigh.LR": (34, 0, 0), "shin.LR": (10, 0, 0), "tail1": (16, 0, 0)},
          hips_z=0.04, hips_y=-0.45, ease="in")
    A.key(13, {"hips": (7, 0, 5), "chest": (6, 0, 8), "neck": (12, 0, 16), "head": (18, 12, 20),
               "upper_arm.LR": (-38, 0, 0), "forearm.LR": (-12, 0, 0), "thigh.LR": (28, 0, 0), "tail1": (10, 0, -18)}, hips_y=-0.42)
    A.key(15, {"hips": (6, 0, -5), "chest": (5, 0, -8), "neck": (10, 0, -16), "head": (14, -12, -20),
               "upper_arm.LR": (-30, 0, 0), "thigh.LR": (22, 0, 0), "tail1": (8, 0, 18)}, hips_y=-0.36)
    A.key(19, {"hips": (3, 0, 0), "neck": (8, 0, 0), "head": (8, 0, 0), "upper_arm.LR": (-14, 0, 0), "thigh.LR": (10, 0, 0)}, hips_y=-0.16)
    A.key(24, {"neck": (4, 0, 0)})

    A.begin("Hit", 10)
    A.key(0, {"neck": (4, 0, 0)})
    A.key(3, {"hips": (0, 8, 6), "chest": (-6, 0, 10), "neck": (-14, 0, -16), "head": (-8, 0, -14), "tail1": (0, 0, 20)}, hips_z=-0.04, hips_y=0.08)
    A.key(10, {"neck": (4, 0, 0)})

    A.begin("Death", 30)        # legs give way and it rolls onto its side
    A.key(0, {"neck": (4, 0, 0)})
    A.key(8, {"root": (0, 18, 0), "neck": (-14, 0, 12), "upper_arm.LR": (-20, 0, 0), "forearm.LR": (-40, 0, 0),
              "thigh.LR": (20, 0, 0), "shin.LR": (-30, 0, 0)}, hips_z=-0.35)
    down = {"root": (0, 86, 0), "neck": (10, 0, 14), "head": (10, 0, 10), "upper_arm.L": (-34, 0, 0), "upper_arm.R": (-10, 0, 0),
            "forearm.LR": (-30, 0, 0), "thigh.L": (28, 0, 0), "thigh.R": (8, 0, 0), "shin.LR": (-24, 0, 0), "tail1": (0, 0, 24)}
    A.key(20, down, hips_z=-0.72)
    A.key(30, down, hips_z=-0.72)

    A.finish()
    for act in bpy.data.actions:
        act.frame_range = (0, LENGTHS[act.name])
        act.use_frame_range = True


def main():
    lt.reset()
    bpy.context.scene.render.fps = 30
    mesh = lt.import_glb(MODEL)
    lt.normalize(mesh, HEIGHT, 2)
    for v in mesh.data.vertices:
        v.co.y += SHIFT_Y
    mesh.data.update()
    lt.save_textures(mesh, MODEL)
    arm = lt_rig.build_armature(BONES)
    lt_rig.skin(mesh, arm)
    make_animations(arm)

    if PREVIEW:
        for clip, frame in [("Run", f) for f in range(0, 16, 2)] + [("Attack", f) for f in (0, 5, 7, 9, 11, 13, 15, 19)]:
            lt_rig.set_action(arm, clip)
            bpy.context.scene.frame_set(frame)
            lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_{clip}{frame}.png"), (0, 0.3, 0.65), 5.6, (1.6, -0.2, 0.12), (420, 300))

    lt_rig.set_action(arm, "Idle")
    bpy.context.scene.frame_set(0)
    lt.export_fbx(MODEL, [arm, mesh], rigged=True)


main()
