"""Builds the first-person ability arm: a rigged, animated left arm -> Assets/Art/Models/fp_ability_arm.fbx.

While an ability is used the weapon drops out of view and this arm comes up from the lower left to act it out.
Clips (30 fps), authored in view space with the eye at the origin:
  Strike - a palm strike driven to the middle of the view; it lands at frame 4
  Throw - a baseball throw; the charge leaves the hand at frame 7
  Surge - a cupped hand raised, gathering light, then a fist driven down at frame 12
Bones: hand (root), forearm, fingers1, fingers2, thumb1, thumb2, and grip (where a held light or grenade sits).

Run:  blender -b --python build_fp_arm.py [-- preview]
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix, Euler
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

PREVIEW = "preview" in sys.argv
WRIST = Vector((0.02, 0.0, 0.11))     # wrist position in the normalised Meshy arm (fingers +X, palm +Y, thumb -Z)
PALM = Vector((0.07, 0.028, 0.0))     # centre of the palm, arm-local
FOREARM_STRETCH = 1.9                 # the model's forearm is a short stub: lengthen it so its open end stays off screen

ARM_BONES = {
    "hand":     ((0.00, 0, 0.000), (0.10, 0, 0.000), None),
    "forearm":  ((0.00, 0, 0.000), (-0.28, 0, 0.000), "hand"),
    "fingers1": ((0.10, 0, 0.015), (0.17, 0, 0.015), "hand"),
    "fingers2": ((0.17, 0, 0.015), (0.24, 0, 0.015), "fingers1"),
    "thumb1":   ((0.04, 0, -0.045), (0.09, 0, -0.075), "hand"),
    "thumb2":   ((0.09, 0, -0.075), (0.14, 0, -0.105), "thumb1"),
    "grip":     ((0.078, 0.066, 0.0), (0.078, 0.116, 0.0), "hand"),
}


def make_arm():
    mesh = lt.import_glb("arm")
    lt.normalize(mesh, 0.52, 0, ground=True)
    mesh.location = -WRIST
    lt.apply(mesh)
    mesh.name = "ability_arm"
    arm_data = bpy.data.armatures.new("AbilityArm")
    arm = bpy.data.objects.new("AbilityArm", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (h, t, p) in ARM_BONES.items():
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = h, t
    for name, (h, t, p) in ARM_BONES.items():
        if p:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[p]
    arm_data.edit_bones["grip"].use_deform = False
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    print("arm unweighted verts:", len([v for v in mesh.data.vertices if not v.groups]))
    return arm, mesh


def view(right, up, forward):
    """View space (eye at the origin) to Blender: forward is -Y, up +Z, the player's right -X."""
    return Vector((-right, -forward, up))


def frame(fingers, palm):
    x = fingers.normalized()
    y = (palm - x * palm.dot(x)).normalized()
    return Matrix((x, y, x.cross(y))).transposed()


# A key: frame, then where the palm is and how the hand is held, all in view space (right, up, forward):
#   palm - position of the palm centre (metres)        fingers - direction the fingers point
#   face - direction the palm faces                     elbow   - direction from the wrist back to the elbow
#   curl - finger bend in degrees (0 open, 85 a fist)   thumb   - thumb tuck in degrees
#   ease - how the hand arrives at this key: "io" eased both ends, "in" accelerating into it, "out" settling into it
def K(f, palm, fingers, face, elbow, curl, thumb=20, ease="io"):
    return dict(f=f, palm=view(*palm), fingers=view(*fingers), face=view(*face), elbow=view(*elbow), curl=curl, thumb=thumb, ease=ease)


def W(f, palm, elbow, face, bend, curl, thumb=20, ease="io"):
    """A key given by the forearm instead of the fingers, so the wrist is never limp: the hand runs straight on from
    the forearm, bent at the wrist by `bend` degrees (negative laid back, positive snapped forward toward the palm)."""
    along = -view(*elbow).normalized()
    n = view(*face)
    n = (n - along * n.dot(along)).normalized()
    b = math.radians(bend)
    fingers = along * math.cos(b) + n * math.sin(b)
    palm_normal = n * math.cos(b) - along * math.sin(b)
    return dict(f=f, palm=view(*palm), fingers=fingers, face=palm_normal, elbow=view(*elbow), curl=curl, thumb=thumb, ease=ease)


LOW = dict(palm=(-0.34, -0.52, 0.30), fingers=(0.3, 0.6, 0.7), face=(0.5, 0.2, -0.3), elbow=(-0.3, -0.8, -0.5))    # out of sight, lower left

CLIPS = {
    # A palm strike: the hand is drawn back beside the shoulder, then driven straight out to the middle of the
    # view, heel of the palm first and fingers spread. It lands at frame 4, presses through, and is pulled back.
    "Strike": (14, [
        K(0, LOW["palm"], LOW["fingers"], LOW["face"], LOW["elbow"], 30),
        K(2, (-0.22, -0.16, 0.28), (0.25, 0.95, 0.1), (0.25, -0.05, 1), (-0.30, -0.60, -0.75), 28, ease="out"),
        K(4, (0.00, -0.05, 0.72), (0.22, 1, 0.10), (0.0, -0.10, 1), (-0.33, -0.50, -0.80), 2, thumb=-12, ease="in"),
        K(5, (0.00, -0.04, 0.75), (0.22, 1, 0.20), (0.0, -0.20, 1), (-0.33, -0.50, -0.80), 8, thumb=-8, ease="out"),
        K(8, (-0.03, -0.07, 0.60), (0.22, 1, 0.10), (0.05, -0.10, 1), (-0.32, -0.58, -0.75), 20, ease="out"),
        K(14, LOW["palm"], LOW["fingers"], LOW["face"], LOW["elbow"], 45, ease="in"),
    ]),
    # A baseball throw. The hand shows the charge in its grip, is drawn back beside the head with the wrist laid
    # back, then the elbow drives forward first and the forearm whips after it; the wrist stays cocked until the
    # last instant and snaps through as the fingers let go at frame 7. The arm follows through down across the body.
    "Throw": (18, [
        W(0, (-0.36, -0.42, 0.26), (-0.30, -0.80, -0.50), (0.8, 0.2, 0.5), 0, 82, thumb=40),
        W(2, (-0.22, -0.10, 0.42), (-0.25, -0.85, -0.45), (0.8, 0.2, 0.5), -5, 82, thumb=40, ease="out"),
        W(5, (-0.34, 0.10, 0.20), (-0.10, -0.80, 0.50), (0.2, 0.9, 0.4), -35, 82, thumb=40, ease="io"),
        W(6, (-0.26, 0.13, 0.34), (-0.20, -0.75, 0.35), (0.0, 0.8, 0.6), -48, 80, thumb=38, ease="in"),
        W(7, (-0.10, 0.08, 0.66), (-0.30, -0.52, -0.80), (0.0, -0.6, 0.8), 6, 30, thumb=0, ease="lin"),
        W(8, (-0.03, -0.04, 0.70), (-0.28, -0.44, -0.85), (0.0, -0.9, 0.3), 42, 4, thumb=-14, ease="lin"),
        W(11, (0.07, -0.22, 0.58), (-0.40, -0.42, -0.81), (0.3, -0.9, -0.2), 34, 14, ease="out"),
        W(18, (0.02, -0.64, 0.40), (-0.40, -0.42, -0.81), (0.3, -0.9, -0.2), 20, 30, ease="in"),
    ]),
    # The hand rises cupped around a growing light, tightens, then is driven down as a fist.
    "Surge": (21, [
        K(0, LOW["palm"], LOW["fingers"], (0, 1, 0), LOW["elbow"], 60),
        K(4, (-0.16, -0.14, 0.42), (0.9, 0.2, 0.4), (0.0, 1, -0.1), (-0.45, -0.75, -0.50), 35, ease="out"),
        K(9, (-0.10, 0.00, 0.50), (0.85, 0.35, 0.4), (0.0, 1, -0.25), (-0.40, -0.75, -0.52), 42, ease="io"),
        K(11, (-0.09, 0.07, 0.50), (0.8, 0.5, 0.35), (0.1, 0.9, -0.4), (-0.38, -0.76, -0.52), 80, thumb=35, ease="io"),
        K(13, (-0.06, -0.56, 0.48), (0.7, -0.3, 0.65), (0.0, -0.6, -0.8), (-0.35, 0.2, -0.9), 88, thumb=40, ease="in"),
        K(21, (-0.10, -0.66, 0.44), (0.7, -0.3, 0.65), (0.0, -0.6, -0.8), (-0.35, 0.2, -0.9), 88, thumb=40, ease="out"),
    ]),
}


def ease(k, kind):
    if kind == "in": return k ** 2.4
    if kind == "out": return 1 - (1 - k) ** 2.4
    return k * k * (3 - 2 * k)


def sample(keys, f):
    i = 0
    while i < len(keys) - 2 and f > keys[i + 1]["f"]:
        i += 1
    a, b = keys[i], keys[i + 1]
    k = ease(max(0.0, min(1.0, (f - a["f"]) / (b["f"] - a["f"]))), b["ease"])
    out = {}
    for name in ("palm", "fingers", "face", "elbow"):
        out[name] = a[name].lerp(b[name], k) if name == "palm" else a[name].normalized().slerp(b[name].normalized(), k)
    out["curl"] = a["curl"] + (b["curl"] - a["curl"]) * k
    out["thumb"] = a["thumb"] + (b["thumb"] - a["thumb"]) * k
    return out


def pose(arm, p, rest):
    R = frame(p["fingers"], p["face"])
    world = Matrix.Translation(p["palm"] - R @ PALM) @ R.to_4x4()
    hand = arm.pose.bones["hand"]
    hand.matrix = world @ arm.data.bones["hand"].matrix_local
    bpy.context.view_layer.update()

    def set_rot(name, rot3):
        pb = arm.pose.bones[name]
        M = rest[name]
        pb.rotation_quaternion = (M.inverted() @ rot3 @ M).to_quaternion()

    def rz(deg): return Euler((0, 0, math.radians(deg)), 'XYZ').to_matrix()

    set_rot("fingers1", rz(p["curl"]))
    set_rot("fingers2", rz(p["curl"] * 0.95))
    t = p["thumb"]
    set_rot("thumb1", Euler((0, math.radians(t), math.radians(t * 1.2)), 'XYZ').to_matrix())
    set_rot("thumb2", Euler((0, math.radians(t * 0.5), math.radians(t * 0.6)), 'XYZ').to_matrix())
    local = R.inverted() @ p["elbow"].normalized()
    set_rot("forearm", Vector((-1, 0, 0)).rotation_difference(local).to_matrix())
    arm.pose.bones["forearm"].scale = (1, FOREARM_STRETCH, 1)


def animate(arm):
    rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}
    arm.animation_data_create()
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    for name, (length, keys) in CLIPS.items():
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        arm.animation_data.action = act
        last = {}
        for f in range(length + 1):
            pose(arm, sample(keys, f), rest)
            for pb in arm.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in last and last[pb.name].dot(q) < 0:
                    q.negate()
                    pb.rotation_quaternion = q
                last[pb.name] = q
                pb.keyframe_insert("rotation_quaternion", frame=f)
                pb.keyframe_insert("scale", frame=f)
                if pb.name == "hand":
                    pb.keyframe_insert("location", frame=f)
        act.frame_range = (0, length)
        act.use_frame_range = True


def set_action(arm, name):
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]


def main():
    lt.reset()
    bpy.context.scene.render.fps = 30
    arm, mesh = make_arm()
    mesh.data.materials[0].name = "arm"
    animate(arm)

    if PREVIEW:
        # The weapon camera: 58 degrees vertical, 16:9, at the eye.
        lt.setup_render((640, 360))
        cam = bpy.data.objects.new("fp", bpy.data.cameras.new("fp"))
        bpy.context.scene.collection.objects.link(cam)
        cam.data.sensor_fit = 'VERTICAL'
        cam.data.angle = math.radians(58)
        cam.data.clip_start = 0.01
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        bpy.context.scene.camera = cam
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.042)
        ball = bpy.context.active_object
        shots = {"Throw": (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13)}
        for clip, frames in shots.items():
            set_action(arm, clip)
            for f in frames:
                bpy.context.scene.frame_set(f)
                # Stand-in for the charge held in the hand until it is thrown.
                ball.hide_render = not (clip == "Throw" and f <= 7)
                ball.location = arm.matrix_world @ arm.pose.bones["grip"].head
                bpy.context.scene.render.filepath = os.path.join(lt.SHOTS, f"arm_{clip}_{f:02d}.png")
                bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
        bpy.data.objects.remove(ball)

    set_action(arm, "Strike")
    bpy.context.scene.frame_set(0)
    lt.export_fbx("fp_ability_arm", [arm, mesh], rigged=True)


main()
