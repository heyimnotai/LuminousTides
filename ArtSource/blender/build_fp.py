"""Builds the first-person view model: Meshy carbine held by two posed Meshy arms -> Assets/Art/Models/fp_carbine.fbx.

Each arm gets a small rig (hand root, forearm, two shared finger joints, two thumb joints), is posed to grip
the weapon, and the pose is then baked into a static mesh (weapon motion is procedural in Unity).

View-model space (Blender): forward -Y, up +Z, player's right -X. Origin sits 0.135 m below the sight line.
Run:  blender -b --python build_fp.py [-- preview]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils.bvhtree import BVHTree
from mathutils import Vector, Matrix, Euler, Quaternion
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

PREVIEW = "preview" in sys.argv
WRIST = Vector((0.02, 0.0, 0.11))     # wrist position in the normalised Meshy arm (fingers +X, palm +Y, thumb -Z)

# Arm-local bones relative to the wrist. Left hand as generated; the right hand mirrors Z.
ARM_BONES = {
    "hand":     ((0.00, 0, 0.000), (0.10, 0, 0.000), None),
    "forearm":  ((0.00, 0, 0.000), (-0.28, 0, 0.000), "hand"),
    "fingers1": ((0.10, 0, 0.015), (0.17, 0, 0.015), "hand"),
    "fingers2": ((0.17, 0, 0.015), (0.24, 0, 0.015), "fingers1"),
    "thumb1":   ((0.04, 0, -0.045), (0.09, 0, -0.075), "hand"),
    "thumb2":   ((0.09, 0, -0.075), (0.14, 0, -0.105), "thumb1"),
}


def make_arm(side):
    """Returns (armature, mesh) for a rigged arm with its origin at the wrist. side: 'L' or 'R'."""
    mesh = lt.import_glb("arm")
    lt.normalize(mesh, 0.52, 0, ground=True)
    mesh.location = -WRIST
    lt.apply(mesh)
    flip = -1.0 if side == 'R' else 1.0
    if side == 'R':
        for v in mesh.data.vertices:
            v.co.z = -v.co.z
        mesh.data.flip_normals()
    mesh.name = "arm_" + side

    arm_data = bpy.data.armatures.new("ArmRig_" + side)
    arm = bpy.data.objects.new("ArmRig_" + side, arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (h, t, p) in ARM_BONES.items():
        b = arm_data.edit_bones.new(name)
        b.head = (h[0], h[1], h[2] * flip)
        b.tail = (t[0], t[1], t[2] * flip)
    for name, (h, t, p) in ARM_BONES.items():
        if p:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[p]
    bpy.ops.object.mode_set(mode='OBJECT')

    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    bad = [v.index for v in mesh.data.vertices if not v.groups]
    print(side, "arm unweighted verts:", len(bad))
    return arm, mesh


def frame(fingers, palm):
    """Rotation matrix whose X axis is the finger direction and Y axis the palm normal."""
    x = Vector(fingers).normalized()
    y = Vector(palm)
    y = (y - x * y.dot(x)).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed()


def pose_arm(arm, side, fingers, palm, palm_point, forearm_dir, curl1, curl2, thumb, palm_local=(0.07, 0.028, 0.0)):
    """Places the palm at `palm_point` (view-model space) and poses fingers and forearm.
    curl1/curl2: finger joint bends in degrees. thumb: (rx, ry, rz) for both thumb joints."""
    flip = -1.0 if side == 'R' else 1.0
    R = frame(fingers, palm)
    arm.matrix_world = Matrix.Translation(Vector(palm_point) - R @ Vector((palm_local[0], palm_local[1], palm_local[2] * flip))) @ R.to_4x4()
    rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}

    def set_rot(name, rot3):
        pb = arm.pose.bones[name]
        pb.rotation_mode = 'QUATERNION'
        M = rest[name]
        pb.rotation_quaternion = (M.inverted() @ rot3 @ M).to_quaternion()

    def euler(rx, ry, rz):
        return Euler((math.radians(rx), math.radians(ry * flip), math.radians(rz)), 'XYZ').to_matrix()

    set_rot("fingers1", euler(0, 0, curl1))
    set_rot("fingers2", euler(0, 0, curl2))
    set_rot("thumb1", euler(*thumb))
    set_rot("thumb2", euler(thumb[0] * 0.5, thumb[1] * 0.5, thumb[2] * 0.5))
    local_target = R.inverted() @ Vector(forearm_dir).normalized()
    set_rot("forearm", Vector((-1, 0, 0)).rotation_difference(local_target).to_matrix())
    bpy.context.view_layer.update()


def bake(arm, mesh):
    """Applies the armature deformation so the arm becomes a plain static mesh."""
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    for mod in list(mesh.modifiers):
        if mod.type == 'ARMATURE':
            bpy.ops.object.modifier_apply(modifier=mod.name)
    mw = mesh.matrix_world.copy()
    mesh.parent = None
    mesh.matrix_world = mw
    lt.apply(mesh)
    bpy.data.objects.remove(arm)


# ---------------------------------------------------------------------------------------------------------
# Sight. Built to the rules observed in Destiny's aim-down-sights views:
#   * one thin open frame around a lens - no hood or tunnel
#   * the frame sits close to the eye, so the window is large
#   * the reticle is drawn at screen centre by the lens shader, not painted on the model
# The frame is its own Meshy model (ArtSource/refs/sight.png), generated in the carbine's paint scheme.
SIGHT_WIDTH = 0.074      # overall width of the frame, metres
LENS_Y = -0.045          # where the frame stands along the rail (view-model space, -Y is forward)
EYE_RELIEF = 0.135       # eye-to-lens distance when aiming; must match LevelBuilder


def rebuild_sight(gun):
    """Removes the carbine's original solid sight block and mounts the open-frame sight in its place.
    Returns a dict of objects: sight, plate (closes the rail), lens (cut to the opening), aim (centre of the opening)."""
    me = gun.data
    bm = bmesh.new()
    bm.from_mesh(me)
    doomed = [f for f in bm.faces if f.calc_center_median().z > 0.1195 and -0.125 < f.calc_center_median().y < -0.005]
    print("sight: removing", len(doomed), "faces of the original sight block")
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.to_mesh(me)
    bm.free()

    # Flush cap closing the top of the rail where the block sat.
    pbm = bmesh.new()
    lo, hi = Vector((-0.0165, -0.123, 0.1182)), Vector((0.0165, -0.007, 0.1197))
    bmesh.ops.create_cube(pbm, size=1.0, matrix=Matrix.Translation((lo + hi) / 2) @ Matrix.Diagonal((*(hi - lo), 1.0)))
    pm = bpy.data.meshes.new("SightPlate")
    pbm.to_mesh(pm)
    pbm.free()
    pm.materials.append(bpy.data.materials.new("sight_plate"))
    plate = bpy.data.objects.new("SightPlate", pm)
    bpy.context.scene.collection.objects.link(plate)

    # The frame model: detailed face toward the shooter, clamp gripping the rail.
    sight = lt.import_glb("sight")
    lt.normalize(sight, SIGHT_WIDTH, 0, rot_z_deg=180)
    sight.location = (0, LENS_Y, 0.1125)
    lt.apply(sight)
    lt.save_textures(sight, "sight")
    lo, hi = lt.bounds(sight)
    print("sight bounds", tuple(round(v, 4) for v in lo), tuple(round(v, 4) for v in hi))

    # Find the opening by casting rays through the frame along the line of sight.
    sbm = bmesh.new()
    sbm.from_mesh(sight.data)
    bvh = BVHTree.FromBMesh(sbm)

    def blocked(x, z):
        return bvh.ray_cast(Vector((x, hi.y + 0.05, z)), Vector((0, -1, 0)), 0.4)[0] is not None

    def longest_open(samples, test):
        best, run = [], []
        for v in samples:
            if test(v):
                run = []
            else:
                run.append(v)
                if len(run) > len(best):
                    best = list(run)
        return best

    steps = 240
    zs = longest_open([lo.z + (hi.z - lo.z) * i / steps for i in range(1, steps)], lambda z: blocked(0.0, z))
    if not zs:
        raise RuntimeError("The sight model has no opening to look through")
    cz = (zs[0] + zs[-1]) / 2
    xs = longest_open([lo.x + (hi.x - lo.x) * i / steps for i in range(1, steps)], lambda x: blocked(x, cz))
    cx = (xs[0] + xs[-1]) / 2
    y_mid = (lo.y + hi.y) / 2
    print(f"sight opening: {xs[-1] - xs[0]:.4f} wide, {zs[-1] - zs[0]:.4f} tall, centre x {cx:.4f} z {cz:.4f}")

    # Lens: a fan that follows the inner edge of the frame at mid-thickness.
    centre = Vector((cx, y_mid, cz))
    N = 72
    rim = []
    for i in range(N):
        ang = i / N * 2 * math.pi
        d = Vector((math.cos(ang), 0, math.sin(ang)))
        hit = bvh.ray_cast(centre, d, 0.2)
        r = hit[3] if hit[0] else 0.02
        rim.append(centre + d * max(r - 0.0012, 0.004))
    span = max(max(abs(p.x - cx) for p in rim), max(abs(p.z - cz) for p in rim)) * 2
    lm = bpy.data.meshes.new("Lens")
    lm.from_pydata([tuple(centre)] + [tuple(p) for p in rim], [], [(0, 1 + (i + 1) % N, 1 + i) for i in range(N)])
    uvl = lm.uv_layers.new(name="UVMap")
    for loop in lm.loops:
        co = lm.vertices[loop.vertex_index].co
        uvl.data[loop.index].uv = ((co.x - cx) / span + 0.5, (co.z - cz) / span + 0.5)
    lm.materials.append(bpy.data.materials.new("lens"))
    lens = bpy.data.objects.new("Lens", lm)
    bpy.context.scene.collection.objects.link(lens)
    sbm.free()

    aim = bpy.data.objects.new("AimPoint", None)
    aim.location = centre
    bpy.context.scene.collection.objects.link(aim)
    return {"sight": sight, "plate": plate, "lens": lens, "aim": aim}


def main():
    lt.reset()
    gun = lt.import_glb("carbine")
    lt.normalize(gun, 0.80, 0)                      # muzzle ends up at -X, grip under +X
    gun.rotation_euler = (0, 0, math.radians(90))   # muzzle -> -Y (forward)
    lt.apply(gun)
    gun.location = (0, -0.10, -0.125)                # origin near the grip
    lt.apply(gun)
    lt.save_textures(gun, "carbine")
    parts = rebuild_sight(gun)
    aim = parts["aim"]
    lo, hi = lt.bounds(gun)
    print("gun bounds", tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi))

    # Right hand on the pistol grip.
    r_arm, r_mesh = make_arm('R')
    pose_arm(r_arm, 'R', fingers=(0.0, -0.95, -0.30), palm=(1, 0, 0), palm_point=(-0.022, 0.08, -0.05),
             forearm_dir=(-0.22, 0.72, -0.62), curl1=80, curl2=85, thumb=(0, -35, 55))
    # Left hand cradling the handguard from below.
    l_arm, l_mesh = make_arm('L')
    pose_arm(l_arm, 'L', fingers=(-0.92, -0.30, 0.25), palm=(0, 0, 1), palm_point=(0.0, -0.20, 0.012),
             forearm_dir=(0.42, 0.72, -0.55), curl1=75, curl2=60, thumb=(0, 25, 30))

    if PREVIEW:
        # The weapon camera has a fixed 58 degree field of view. Hip: the view model sits 0.16 right, 0.17 below
        # and 0.30 ahead of it. Aimed: the centre of the lens sits on the axis, EYE_RELIEF ahead.
        parts["lens"].hide_render = True            # its real material is a Unity shader
        lt.setup_render((960, 540))
        cam = bpy.data.objects.new("fp", bpy.data.cameras.new("fp"))
        bpy.context.scene.collection.objects.link(cam)
        cam.data.sensor_fit = 'VERTICAL'
        cam.data.angle = math.radians(58)
        cam.data.clip_start = 0.01
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        bpy.context.scene.camera = cam
        hip = Vector((0.16, 0.30, 0.17))
        ads = Vector((aim.location.x, aim.location.y + EYE_RELIEF, aim.location.z))
        for name, t in (("fp_hip", 0.0), ("fp_mid", 0.5), ("fp_ads", 1.0)):
            cam.location = hip.lerp(ads, t)
            bpy.context.scene.render.filepath = os.path.join(lt.SHOTS, name + ".png")
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
        lt.persp_render(os.path.join(lt.SHOTS, "fp_left.png"), (0, 0.05, 0), 1.1, (1, -0.25, 0.25), (800, 600))
        lt.persp_render(os.path.join(lt.SHOTS, "fp_right.png"), (0, 0.05, 0), 1.1, (-1, -0.25, 0.25), (800, 600))
        lt.persp_render(os.path.join(lt.SHOTS, "fp_sight.png"), (0, -0.045, 0.14), 0.3, (0.5, 1.0, 0.25), (800, 600))
        parts["lens"].hide_render = False

    bake(r_arm, r_mesh)
    bake(l_arm, l_mesh)
    r_mesh.data.materials[0].name = "arm"
    l_mesh.data.materials.clear()
    l_mesh.data.materials.append(r_mesh.data.materials[0])
    lt.save_textures(r_mesh, "arm")

    muzzle = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muzzle)
    muzzle.location = (0, lo.y - 0.01, 0.195 - 0.125)
    gun.name = "carbine"
    lt.export_fbx("fp_carbine", [gun, parts["sight"], parts["plate"], parts["lens"], aim, r_mesh, l_mesh, muzzle])


def main_shotgun():
    """Breaker shotgun view model -> Assets/Art/Models/fp_shotgun.fbx. Hand positions were read off
    inspect_gun.py renders: pistol grip around x 0.16, pump fore-grip from x -0.27 to -0.07 (before rotation)."""
    lt.reset()
    gun = lt.import_glb("shotgun")
    lt.normalize(gun, 0.72, 0)                      # muzzle at -X, grip under +X
    gun.rotation_euler = (0, 0, math.radians(90))   # muzzle -> -Y (forward)
    lt.apply(gun)
    gun.location = (0, -0.12, -0.10)                 # origin just ahead of the grip, at trigger height
    lt.apply(gun)
    lt.save_textures(gun, "shotgun")
    lo, hi = lt.bounds(gun)
    print("shotgun bounds", tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi))

    r_arm, r_mesh = make_arm('R')
    pose_arm(r_arm, 'R', fingers=(0.0, -0.95, -0.30), palm=(1, 0, 0), palm_point=(-0.024, 0.055, -0.04),
             forearm_dir=(-0.22, 0.72, -0.62), curl1=80, curl2=85, thumb=(0, -35, 55))
    # Left hand under the pump.
    l_arm, l_mesh = make_arm('L')
    pose_arm(l_arm, 'L', fingers=(-0.92, -0.30, 0.25), palm=(0, 0, 1), palm_point=(0.0, -0.25, -0.006),
             forearm_dir=(0.42, 0.72, -0.55), curl1=70, curl2=55, thumb=(0, 25, 30))

    if PREVIEW:
        lt.setup_render((960, 540))
        cam = bpy.data.objects.new("fp", bpy.data.cameras.new("fp"))
        bpy.context.scene.collection.objects.link(cam)
        cam.data.sensor_fit = 'VERTICAL'
        cam.data.angle = math.radians(58)
        cam.data.clip_start = 0.01
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        bpy.context.scene.camera = cam
        for name, pos in (("fps_hip", (0.16, 0.30, 0.17)), ("fps_ads", (0.0, 0.16, 0.145))):
            cam.location = pos
            bpy.context.scene.render.filepath = os.path.join(lt.SHOTS, name + ".png")
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
        lt.persp_render(os.path.join(lt.SHOTS, "fps_right.png"), (0, 0.05, 0), 1.1, (-1, -0.25, 0.25), (800, 600))
        lt.persp_render(os.path.join(lt.SHOTS, "fps_left.png"), (0, 0.05, 0), 1.1, (1, -0.25, 0.25), (800, 600))

    bake(r_arm, r_mesh)
    bake(l_arm, l_mesh)
    r_mesh.data.materials[0].name = "arm"            # same arm material and textures as the carbine's hands
    l_mesh.data.materials.clear()
    l_mesh.data.materials.append(r_mesh.data.materials[0])

    muzzle = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muzzle)
    muzzle.location = (0, lo.y - 0.01, 0.175 - 0.10)
    gun.name = "shotgun"
    lt.export_fbx("fp_shotgun", [gun, r_mesh, l_mesh, muzzle])


HC_EYE_RELIEF = 0.27       # eye-to-rear-sight distance when aiming the hand cannon; must match LevelBuilder


def main_handcannon():
    """Heavy sidearm view model -> Assets/Art/Models/fp_handcannon.fbx. Positions were read off ortho_any.py renders:
    muzzle at -X, the grip under +X about 0.18 from the middle, barrel axis 0.11 and sight line 0.146 above the base."""
    lt.reset()
    gun = lt.import_glb("handcannon")
    lt.normalize(gun, 0.42, 0)                      # muzzle at -X, grip under +X
    gun.rotation_euler = (0, 0, math.radians(90))   # muzzle -> -Y (forward)
    lt.apply(gun)
    gun.location = (0, -0.13, -0.075)                # origin at the trigger, just above the grip
    lt.apply(gun)
    lt.save_textures(gun, "handcannon")
    lo, hi = lt.bounds(gun)
    print("handcannon bounds", tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi))

    # ---- Iron sights. As generated, the rear sight is a solid block standing taller than the front post, so the post
    # cannot be seen over it. Cut a notch down the middle of the rear sight, bring the front post up level with the
    # rear sight's shoulders, and mark where the eye has to look from (AimPoint: the middle of the notch).
    vs = gun.data.vertices
    front = [v for v in vs if v.co.y < lo.y + 0.07]
    front_top = max(v.co.z for v in front)
    rear = [v for v in vs if -0.12 < v.co.y < 0.0]
    rear_top = max(v.co.z for v in rear)
    crown = [v for v in rear if v.co.z > rear_top - 0.004]
    rear_y = sum(v.co.y for v in crown) / len(crown)
    rear_x = sum(v.co.x for v in crown) / len(crown)
    post = [v for v in front if v.co.z > front_top - 0.004]
    front_x = sum(v.co.x for v in post) / len(post)
    print(f"sights before: front tip z {front_top:.4f} x {front_x:.4f} | rear top z {rear_top:.4f} at y {rear_y:.3f} x {rear_x:.4f}")
    # Sights in the style of a Destiny hand cannon: a low, wide rear notch with a glowing dot on each shoulder, and a
    # tall front blade topped with a bright bead. The three lights line up in a row when the gun is on target, and
    # the sights stand well clear of the top of the gun so you look down along it rather than at the back of a block.
    # The generated rear sight is shaved flat and the generated front post is left buried inside the new blade.
    strap = rear_top - 0.010                       # the top of the gun under the rear sight
    for v in rear:
        if v.co.z > strap and abs(v.co.y - rear_y) < 0.03:
            v.co.z = strap
    front_y = sum(v.co.y for v in post) / len(post)
    floor = strap + 0.004                          # bottom of the notch
    top = floor + 0.0035                           # top of the low rear shoulders
    bead_z = top - 0.001                           # the bead stands a little proud of them, as the eye's aim point
    for v in front:                                # sink the generated post so it cannot poke out of the new blade
        if v.co.z > front_top - 0.012:
            v.co.z = front_top - 0.012
    gun.data.update()

    def box(name, lo3, hi3, material, colour):
        lo3, hi3 = Vector(lo3), Vector(hi3)
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((lo3 + hi3) / 2) @ Matrix.Diagonal((*(hi3 - lo3), 1.0)))
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        mat = bpy.data.materials.get(material) or bpy.data.materials.new(material)
        mat.diffuse_color = colour
        me.materials.append(mat)
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj

    DARK, GLOW = (0.02, 0.02, 0.025, 1), (0.1, 1.0, 0.95, 1)
    x, half_gap, shoulder = front_x, 0.0065, 0.0032
    sight_parts = [
        box("RearSightBase", (x - half_gap - shoulder, rear_y - 0.003, strap - 0.002), (x + half_gap + shoulder, rear_y + 0.003, floor), "sight_dark", DARK),
        box("RearSightL", (x + half_gap, rear_y - 0.003, floor), (x + half_gap + shoulder, rear_y + 0.003, top), "sight_dark", DARK),
        box("RearSightR", (x - half_gap - shoulder, rear_y - 0.003, floor), (x - half_gap, rear_y + 0.003, top), "sight_dark", DARK),
        # The dots face the eye (+Y is toward the shooter).
        box("RearDotL", (x + half_gap + 0.0007, rear_y + 0.003, top - 0.0026), (x + half_gap + 0.0026, rear_y + 0.0036, top - 0.0006), "sight_glow", GLOW),
        box("RearDotR", (x - half_gap - 0.0026, rear_y + 0.003, top - 0.0026), (x - half_gap - 0.0007, rear_y + 0.0036, top - 0.0006), "sight_glow", GLOW),
        box("FrontBlade", (x - 0.0028, front_y - 0.006, front_top - 0.016), (x + 0.0028, front_y + 0.006, bead_z - 0.0026), "sight_dark", DARK),
        box("FrontBead", (x - 0.0028, front_y - 0.006, bead_z - 0.0026), (x + 0.0028, front_y + 0.0065, bead_z + 0.0026), "sight_glow", GLOW),
    ]
    print(f"sights after: strap {strap:.4f}, notch floor {floor:.4f}, top {top:.4f}, bead centre {bead_z:.4f} at y {front_y:.3f}")
    aim = bpy.data.objects.new("AimPoint", None)
    aim.location = (front_x, rear_y, bead_z)
    bpy.context.scene.collection.objects.link(aim)

    # Right hand round the grip, left hand cupped under it.
    r_arm, r_mesh = make_arm('R')
    pose_arm(r_arm, 'R', fingers=(0.0, -0.95, -0.30), palm=(1, 0, 0), palm_point=(-0.022, 0.055, -0.045),
             forearm_dir=(-0.22, 0.72, -0.62), curl1=80, curl2=85, thumb=(0, -35, 55))
    l_arm, l_mesh = make_arm('L')
    pose_arm(l_arm, 'L', fingers=(-0.92, -0.30, 0.25), palm=(0, 0, 1), palm_point=(-0.005, 0.045, -0.105),
             forearm_dir=(0.50, 0.66, -0.56), curl1=78, curl2=70, thumb=(0, 25, 30))

    if PREVIEW:
        lt.setup_render((960, 540))
        cam = bpy.data.objects.new("fp", bpy.data.cameras.new("fp"))
        bpy.context.scene.collection.objects.link(cam)
        cam.data.sensor_fit = 'VERTICAL'
        cam.data.angle = math.radians(58)
        cam.data.clip_start = 0.01
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        bpy.context.scene.camera = cam
        for name, pos in (("fph_hip", (0.15, 0.34, 0.15)), ("fph_ads", (aim.location.x, aim.location.y + HC_EYE_RELIEF, aim.location.z))):
            cam.location = pos
            bpy.context.scene.render.filepath = os.path.join(lt.SHOTS, name + ".png")
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
        lt.persp_render(os.path.join(lt.SHOTS, "fph_right.png"), (0, -0.05, 0), 0.9, (-1, -0.25, 0.25), (800, 600))

    bake(r_arm, r_mesh)
    bake(l_arm, l_mesh)
    r_mesh.data.materials[0].name = "arm"
    l_mesh.data.materials.clear()
    l_mesh.data.materials.append(r_mesh.data.materials[0])
    muzzle = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muzzle)
    muzzle.location = (0, lo.y - 0.01, 0.036)
    gun.name = "handcannon"
    lt.export_fbx("fp_handcannon", [gun, aim, r_mesh, l_mesh, muzzle] + sight_parts)


if "handcannon-only" in sys.argv:
    main_handcannon()
else:
    if "shotgun-only" not in sys.argv:
        main()
    main_shotgun()
    main_handcannon()
