"""Rigs and animates the Varrik Strider (a tripod walker), then exports Assets/Art/Models/varrik_strider.fbx.
Also exports the Kite, which needs no skeleton: Assets/Art/Models/varrik_kite.fbx.

Joints were read off ortho_any.py renders of the 7 m model and are scaled to the height used in the game.
Clips: Idle, Run (a three-beat walk), Spit (cannon fire), Slam (stomp), Death.
Run:  blender -b --python rig_strider.py [-- preview]
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt
import lt_rig

PREVIEW = "preview" in sys.argv
MODEL = "varrik_strider"
HEIGHT = 5.8
K = HEIGHT / 7.0

# One leg points straight ahead (-Y) under the cannon; the other two splay back and out. No mirroring: each leg is listed.
RAW = {
    "root":   ((0, 0, 0), (0, 0, 0.5), None),
    "hips":   ((0, 0.45, 4.35), (0, 0.45, 5.4), "root"),            # the pod; named hips so body motion keys apply to it
    "head":   ((0, 0.0, 5.4), (0, -0.5, 6.2), "hips"),              # crest, used by the game for the weak point and the bar
    "cannon": ((0, -0.3, 5.1), (0, -2.5, 5.1), "hips"),
    "f1":     ((0, -0.13, 4.27), (0, -1.03, 3.18), "hips"),
    "f2":     ((0, -1.03, 3.18), (0, -2.0, 0.45), "f1"),
    "f3":     ((0, -2.0, 0.45), (0, -2.15, 0.05), "f2"),
    "l1":     ((0.4, 0.6, 4.3), (1.6, 1.35, 3.5), "hips"),
    "l2":     ((1.6, 1.35, 3.5), (2.75, 1.75, 0.45), "l1"),
    "l3":     ((2.75, 1.75, 0.45), (2.9, 1.85, 0.05), "l2"),
    "r1":     ((-0.4, 0.6, 4.3), (-1.6, 1.35, 3.5), "hips"),
    "r2":     ((-1.6, 1.35, 3.5), (-2.75, 1.75, 0.45), "r1"),
    "r3":     ((-2.75, 1.75, 0.45), (-2.9, 1.85, 0.05), "r2"),
}
BONES = {n: (tuple(c * K for c in h), tuple(c * K for c in t), p) for n, (h, t, p) in RAW.items()}
LENGTHS = {"Idle": 60, "Run": 36, "Spit": 20, "Slam": 36, "Death": 40}


def leg_pose(pose, leg, swing, lift):
    """swing: degrees fore/aft about the hip. lift: 0..1, how far the foot is off the ground."""
    pose[leg + "1"] = (swing, 0, 0)
    if leg == "f":
        pose["f2"] = (-22 * lift, 0, 0)
    else:
        pose[leg + "2"] = (-8 * lift, (-16 if leg == "l" else 16) * lift, 0)


def make_animations(arm):
    A = lt_rig.Animator(arm, K)

    A.begin("Idle", 60, loop=True)
    for f, s in ((0, 0), (15, 1), (30, 0), (45, -1), (60, 0)):
        A.key(f, {"hips": (1.5 * abs(s), 0, 3 * s), "cannon": (0, 0, -2 * s)}, hips_z=-0.05 * abs(s))

    A.begin("Run", 36, loop=True)          # each leg in turn lifts, swings forward and plants while the other two carry the pod
    for f in range(0, 37, 3):
        p = f / 36 * 2 * math.pi
        pose = {"hips": (2 + 2.5 * math.sin(p * 3), 3.5 * math.sin(p), 3 * math.sin(p)), "cannon": (-2.5 * math.sin(p * 3), 0, -3 * math.sin(p)),
                "head": (3 * math.sin(p * 3 - 0.6), 0, 0)}
        for i, leg in enumerate("flr"):
            ph = p + i * 2 * math.pi / 3
            lift = max(0.0, math.sin(ph)) ** 1.5
            leg_pose(pose, leg, -9 * math.cos(ph), lift)
        # The pod sinks sharply as each foot comes down and takes the weight, then rides back up: three thuds a cycle.
        A.key(f, pose, hips_z=0.035 - 0.11 * max(0.0, math.cos(p * 3 + 0.5)) ** 3)

    A.begin("Spit", 20)         # cannon fire: the pod rocks back on its legs as the shots leave (first at frame 9)
    A.key(0, {})
    A.key(6, {"hips": (4, 0, 0)}, hips_z=-0.05)
    A.key(10, {"hips": (-7, 0, 0), "f1": (4, 0, 0)}, hips_y=0.12)
    A.key(14, {"hips": (-3, 0, 0)}, hips_y=0.05)
    A.key(20, {})

    A.begin("Slam", 36)         # rears the front leg high and drives it into the ground at frame 20
    A.key(0, {})
    pose = {"hips": (-9, 0, 0)}; leg_pose(pose, "f", -20, 1.15)
    A.key(14, pose, hips_y=0.25, hips_z=0.1)
    pose = {"hips": (10, 0, 0)}; leg_pose(pose, "f", 8, 0)
    A.key(20, pose, hips_y=-0.2, hips_z=-0.3)
    pose = {"hips": (6, 0, 0)}; leg_pose(pose, "f", 5, 0)
    A.key(26, pose, hips_y=-0.1, hips_z=-0.2)
    A.key(36, {})

    A.begin("Death", 40)        # the legs buckle outwards and the pod comes down nose first
    A.key(0, {})
    A.key(10, {"hips": (8, 6, 0), "f1": (-14, 0, 0), "l1": (0, -10, 0), "r1": (0, 10, 0)}, hips_z=-0.5)
    down = {"hips": (30, 14, 8), "f1": (-52, 0, 0), "f2": (30, 0, 0), "l1": (10, -46, 0), "l2": (0, 24, 0), "r1": (10, 46, 0), "r2": (0, -24, 0),
            "cannon": (14, 0, 0)}
    A.key(26, down, hips_z=-3.3, hips_y=-0.5)
    A.key(40, down, hips_z=-3.3, hips_y=-0.5)

    A.finish()
    for act in bpy.data.actions:
        act.frame_range = (0, LENGTHS[act.name])
        act.use_frame_range = True


def export_kite():
    """The Kite is rigid. Meshy builds it as an upright sheet facing the viewer; tip it over so it flies like a glider
    with its leading edge forward and its lamp eye looking down and ahead."""
    lt.reset()
    kite = lt.import_glb("varrik_kite")
    lt.normalize(kite, 3.4, 0, ground=False)
    tilt = Matrix.Rotation(math.radians(58), 4, 'X')
    for v in kite.data.vertices:
        v.co = tilt @ v.co
    kite.data.update()
    lt.save_textures(kite, "varrik_kite")
    if PREVIEW:
        lt.persp_render(os.path.join(lt.SHOTS, "anim_varrik_kite.png"), (0, 0, 0), 5.0, (0.9, -1.5, -0.7), (560, 400))
    lt.export_fbx("varrik_kite", [kite])


def main():
    export_kite()
    lt.reset()
    bpy.context.scene.render.fps = 30
    mesh = lt.import_glb(MODEL)
    lt.normalize(mesh, HEIGHT, 2)
    lt.save_textures(mesh, MODEL)
    arm = lt_rig.build_armature(BONES)
    lt_rig.skin(mesh, arm)
    make_animations(arm)
    if PREVIEW:
        for clip, frame in (("Idle", 0), ("Run", 6), ("Run", 18), ("Slam", 14), ("Death", 40)):
            lt_rig.set_action(arm, clip)
            bpy.context.scene.frame_set(frame)
            lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_{clip}{frame}.png"), (0, 0, HEIGHT * 0.45), HEIGHT * 2.6, (1.3, -1.2, 0.2), (560, 400))
    lt_rig.set_action(arm, "Idle")
    bpy.context.scene.frame_set(0)
    lt.export_fbx(MODEL, [arm, mesh], rigged=True)


main()
