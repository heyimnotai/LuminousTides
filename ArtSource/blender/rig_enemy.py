"""Rigs and animates the Meshy enemy, then exports Assets/Art/Models/enemy.fbx.

Skeleton joints were placed by eye from gridded orthographic renders (see ortho_views.py).
Clips: Idle, Run, Attack, Hit, Death.     Run:  blender -b --python rig_enemy.py [-- preview]
"""
import bpy, sys, os, math
from mathutils import Vector, Euler, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt
import lt_rig

PREVIEW = "preview" in sys.argv
HEIGHT = 2.1

# name: (head, tail, parent).  Character faces -Y; .L is +X.  Right side is mirrored automatically.
BONES = {
    "root":        ((0, 0, 0), (0, 0, 0.25), None),
    "hips":        ((0, 0.05, 1.05), (0, 0.04, 1.22), "root"),
    "spine":       ((0, 0.04, 1.22), (0, 0.06, 1.42), "hips"),
    "chest":       ((0, 0.06, 1.42), (0, 0.08, 1.72), "spine"),
    "neck":        ((0, 0.08, 1.72), (0, 0.04, 1.84), "chest"),
    "head":        ((0, 0.04, 1.84), (0, -0.02, 2.06), "neck"),
    "upper_arm.L": ((0.25, 0.10, 1.62), (0.48, 0.10, 1.32), "chest"),
    "forearm.L":   ((0.48, 0.10, 1.32), (0.64, 0.00, 1.08), "upper_arm.L"),
    "hand.L":      ((0.64, 0.00, 1.08), (0.74, -0.10, 0.88), "forearm.L"),
    "thigh.L":     ((0.14, 0.05, 1.05), (0.20, -0.10, 0.72), "hips"),
    "shin.L":      ((0.20, -0.10, 0.72), (0.24, 0.12, 0.38), "thigh.L"),
    "foot.L":      ((0.24, 0.12, 0.38), (0.27, 0.02, 0.09), "shin.L"),
    "toe.L":       ((0.27, 0.02, 0.09), (0.30, -0.22, 0.04), "foot.L"),
}


# Joints for the other creatures, read off ortho_creature.py renders the same way.
BONES_ALPHA = {      # Tide Alpha (boss), 4.6 m: massive shoulders and forearms, head carried low and forward
    "root":        ((0, 0, 0), (0, 0, 0.5), None),
    "hips":        ((0, 0.15, 2.15), (0, 0.15, 2.55), "root"),
    "spine":       ((0, 0.15, 2.55), (0, 0.20, 3.00), "hips"),
    "chest":       ((0, 0.20, 3.00), (0, 0.25, 3.75), "spine"),
    "neck":        ((0, 0.25, 3.75), (0, 0.05, 4.00), "chest"),
    "head":        ((0, 0.05, 4.00), (0, -0.40, 4.45), "neck"),
    "upper_arm.L": ((0.95, 0.20, 3.45), (1.50, 0.15, 2.70), "chest"),
    "forearm.L":   ((1.50, 0.15, 2.70), (1.75, -0.10, 2.00), "upper_arm.L"),
    "hand.L":      ((1.75, -0.10, 2.00), (1.95, -0.35, 1.45), "forearm.L"),
    "thigh.L":     ((0.45, 0.15, 2.15), (0.60, -0.15, 1.40), "hips"),
    "shin.L":      ((0.60, -0.15, 1.40), (0.70, 0.25, 0.75), "thigh.L"),
    "foot.L":      ((0.70, 0.25, 0.75), (0.75, 0.05, 0.20), "shin.L"),
    "toe.L":       ((0.75, 0.05, 0.20), (0.80, -0.55, 0.08), "foot.L"),
}
BONES_SPITTER = {    # Tide Spitter, 2.0 m: thin limbs, a long neck, arms held wide
    "root":        ((0, 0, 0), (0, 0, 0.25), None),
    "hips":        ((0, -0.03, 0.98), (0, -0.03, 1.12), "root"),
    "spine":       ((0, -0.03, 1.12), (0, -0.04, 1.28), "hips"),
    "chest":       ((0, -0.04, 1.28), (0, -0.04, 1.48), "spine"),
    "neck":        ((0, -0.04, 1.48), (0, 0.07, 1.78), "chest"),
    "head":        ((0, 0.07, 1.78), (0, -0.08, 1.97), "neck"),
    "upper_arm.L": ((0.14, 0.00, 1.42), (0.36, 0.02, 1.20), "chest"),
    "forearm.L":   ((0.36, 0.02, 1.20), (0.50, 0.02, 1.03), "upper_arm.L"),
    "hand.L":      ((0.50, 0.02, 1.03), (0.64, 0.02, 0.90), "forearm.L"),
    "thigh.L":     ((0.10, -0.03, 0.98), (0.13, 0.02, 0.62), "hips"),
    "shin.L":      ((0.13, 0.02, 0.62), (0.17, 0.12, 0.32), "thigh.L"),
    "foot.L":      ((0.17, 0.12, 0.32), (0.19, 0.08, 0.07), "shin.L"),
    "toe.L":       ((0.19, 0.08, 0.07), (0.20, -0.18, 0.03), "foot.L"),
}

# The Varrik share one build (gaunt, long-necked, digitigrade), so their joints are given for a 1 m figure and
# scaled to each unit's height. Read off ortho_creature.py renders of the Meshy models.
BONES_VARRIK = {
    "root":        ((0, 0, 0), (0, 0, 0.12), None),
    "hips":        ((0, 0.0, 0.56), (0, 0.0, 0.63), "root"),
    "spine":       ((0, 0.0, 0.63), (0, 0.005, 0.71), "hips"),
    "chest":       ((0, 0.005, 0.71), (0, 0.01, 0.81), "spine"),
    "neck":        ((0, 0.01, 0.81), (0, -0.01, 0.90), "chest"),
    "head":        ((0, -0.01, 0.90), (0, -0.04, 0.985), "neck"),
    "upper_arm.L": ((0.09, 0.0, 0.79), (0.172, -0.01, 0.645), "chest"),
    "forearm.L":   ((0.172, -0.01, 0.645), (0.235, -0.02, 0.545), "upper_arm.L"),
    "hand.L":      ((0.235, -0.02, 0.545), (0.268, -0.04, 0.455), "forearm.L"),
    "thigh.L":     ((0.045, 0.0, 0.56), (0.068, -0.028, 0.355), "hips"),
    "shin.L":      ((0.068, -0.028, 0.355), (0.083, 0.055, 0.185), "thigh.L"),
    "foot.L":      ((0.083, 0.055, 0.185), (0.097, 0.0, 0.05), "shin.L"),
    "toe.L":       ((0.097, 0.0, 0.05), (0.10, -0.055, 0.012), "foot.L"),
}
BONES_VARRIK_CAPTAIN = {      # broad and armoured, standing flat-footed in heavy boots; horns reach the full height
    "root":        ((0, 0, 0), (0, 0, 0.12), None),
    "hips":        ((0, 0.0, 0.53), (0, 0.0, 0.60), "root"),
    "spine":       ((0, 0.0, 0.60), (0, 0.0, 0.68), "hips"),
    "chest":       ((0, 0.0, 0.68), (0, 0.0, 0.787), "spine"),
    "neck":        ((0, 0.0, 0.787), (0, -0.015, 0.83), "chest"),
    "head":        ((0, -0.015, 0.83), (0, -0.04, 0.915), "neck"),
    "upper_arm.L": ((0.137, -0.005, 0.749), (0.23, -0.015, 0.59), "chest"),
    "forearm.L":   ((0.23, -0.015, 0.59), (0.294, -0.02, 0.487), "upper_arm.L"),
    "hand.L":      ((0.294, -0.02, 0.487), (0.32, -0.03, 0.417), "forearm.L"),
    "thigh.L":     ((0.055, 0.0, 0.53), (0.086, -0.02, 0.34), "hips"),
    "shin.L":      ((0.086, -0.02, 0.34), (0.127, 0.0, 0.095), "thigh.L"),
    "foot.L":      ((0.127, 0.0, 0.095), (0.13, -0.05, 0.025), "shin.L"),
    "toe.L":       ((0.13, -0.05, 0.025), (0.13, -0.09, 0.012), "foot.L"),
}


BONES_VARRIK_QUEEN = {        # the Corsair Queen, 3.6 m, in metres: slender, robed to the ground, a train behind her
    "root":        ((0, 0, 0), (0, 0, 0.3), None),
    "hips":        ((0, -0.24, 2.10), (0, -0.24, 2.36), "root"),
    "spine":       ((0, -0.24, 2.36), (0, -0.24, 2.55), "hips"),
    "chest":       ((0, -0.24, 2.55), (0, -0.24, 2.79), "spine"),
    "neck":        ((0, -0.24, 2.79), (0, -0.28, 2.92), "chest"),
    "head":        ((0, -0.28, 2.92), (0, -0.33, 3.22), "neck"),
    "upper_arm.L": ((0.25, -0.24, 2.72), (0.475, -0.24, 2.36), "chest"),
    "forearm.L":   ((0.475, -0.24, 2.36), (0.63, -0.25, 2.03), "upper_arm.L"),
    "hand.L":      ((0.63, -0.25, 2.03), (0.71, -0.25, 1.83), "forearm.L"),
    "thigh.L":     ((0.12, -0.24, 2.10), (0.13, -0.26, 1.15), "hips"),
    "shin.L":      ((0.13, -0.26, 1.15), (0.14, -0.22, 0.20), "thigh.L"),
    "foot.L":      ((0.14, -0.22, 0.20), (0.14, -0.34, 0.05), "shin.L"),
    "toe.L":       ((0.14, -0.34, 0.05), (0.14, -0.45, 0.02), "foot.L"),
}


BONES_PLAYER = {              # the player's own surveyor suit, 1.9 m, in metres. One extra joint per hand so it can make a fist.
    "root":        ((0, 0, 0), (0, 0, 0.2), None),
    "hips":        ((0, 0.0, 1.00), (0, 0.0, 1.10), "root"),
    "spine":       ((0, 0.0, 1.10), (0, 0.0, 1.26), "hips"),
    "chest":       ((0, 0.0, 1.26), (0, 0.0, 1.50), "spine"),
    "neck":        ((0, 0.0, 1.50), (0, -0.01, 1.61), "chest"),
    "head":        ((0, -0.01, 1.61), (0, -0.02, 1.88), "neck"),
    "upper_arm.L": ((0.22, 0.0, 1.52), (0.365, 0.01, 1.265), "chest"),
    "forearm.L":   ((0.365, 0.01, 1.265), (0.46, 0.0, 1.07), "upper_arm.L"),
    "hand.L":      ((0.46, 0.0, 1.07), (0.50, -0.01, 0.985), "forearm.L"),
    "fingers.L":   ((0.50, -0.01, 0.985), (0.54, -0.02, 0.90), "hand.L"),
    "thigh.L":     ((0.097, 0.0, 0.97), (0.133, -0.01, 0.645), "hips"),
    "shin.L":      ((0.133, -0.01, 0.645), (0.175, 0.02, 0.17), "thigh.L"),
    "foot.L":      ((0.175, 0.02, 0.17), (0.175, -0.09, 0.04), "shin.L"),
    "toe.L":       ((0.175, -0.09, 0.04), (0.175, -0.17, 0.02), "foot.L"),
}


def scaled(bones, k):
    return {n: (tuple(c * k for c in h), tuple(c * k for c in t), p) for n, (h, t, p) in bones.items()}


# model name -> (height in metres, joints). Pick one on the command line:  -- alpha   (default: enemy)
SPECS = {"enemy": (2.1, BONES), "alpha": (4.6, BONES_ALPHA), "spitter": (2.0, BONES_SPITTER),
         "varrik_runt": (1.35, scaled(BONES_VARRIK, 1.35)), "varrik_raider": (2.2, scaled(BONES_VARRIK, 2.2)),
         "varrik_captain": (2.7, scaled(BONES_VARRIK_CAPTAIN, 2.7)), "varrik_queen": (3.6, BONES_VARRIK_QUEEN), "player": (1.9, BONES_PLAYER)}
# Meshy file each model is built from, where the names differ.
SOURCE = {"varrik_runt": "varrik_thrall", "varrik_raider": "varrik_rifleman", "varrik_captain": "varrik_captain2"}
# A separate weapon model joined onto the right hand: (meshy file, length in metres, grip position along it 0..1).
HELD = {"varrik_raider": ("varrik_rifle", 1.75, 0.34)}
# Units whose energy weapons are built here rather than modelled (Meshy drops hand-held items in A-pose).
ENERGY_ARMS = {"varrik_captain": "sabre", "varrik_queen": "glaive"}
# How far the legs swing in the run. The Queen is robed to the ground and glides rather than strides.
GAIT = {"varrik_queen": 0.22}
ARMED = MODEL_IS_VARRIK = False
MODEL = next((a for a in sys.argv if a in SPECS), "enemy")
HEIGHT, BONES = SPECS[MODEL]
MODEL_IS_VARRIK = MODEL.startswith("varrik")
ANIM_SCALE = HEIGHT / 2.1        # body offsets in the clips were authored for the 2.1 m stalker


def build_armature():
    arm_data = bpy.data.armatures.new("EnemyRig")
    arm = bpy.data.objects.new("EnemyRig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    full = {}
    for name, (h, t, p) in BONES.items():
        full[name] = (h, t, p)
        if name.endswith(".L"):
            rp = p[:-2] + ".R" if p.endswith(".L") else p
            full[name[:-2] + ".R"] = ((-h[0], h[1], h[2]), (-t[0], t[1], t[2]), rp)
    for name, (h, t, p) in full.items():
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = h, t
    for name, (h, t, p) in full.items():
        if p:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[p]
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def skin(mesh, arm):
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    unweighted = [v.index for v in mesh.data.vertices if not v.groups or sum(g.weight for g in v.groups) < 1e-4]
    print("unweighted verts after auto weights:", len(unweighted), "of", len(mesh.data.vertices))
    if unweighted:
        fallback_weights(mesh, arm, unweighted)
    # Root must not deform anything.
    if "root" in mesh.vertex_groups:
        mesh.vertex_groups.remove(mesh.vertex_groups["root"])
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)


def fallback_weights(mesh, arm, indices):
    """Distance-to-bone weighting for vertices bone heat could not solve."""
    segs = [(b.name, b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones if b.name != "root"]
    for name, _, _ in segs:
        if name not in mesh.vertex_groups:
            mesh.vertex_groups.new(name=name)
    for i in indices:
        p = mesh.data.vertices[i].co
        dists = []
        for name, a, b in segs:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            dists.append(((p - (a + ab * t)).length, name))
        dists.sort()
        best = dists[:2]
        ws = [1.0 / (d + 1e-4) ** 4 for d, _ in best]
        for (d, name), w in zip(best, ws):
            mesh.vertex_groups[name].add([i], w / sum(ws), 'REPLACE')


def attach_held(mesh, arm):
    """Joins the unit's weapon model onto the body, rigid to the right hand. In the rest pose it lies along the
    forearm, pointing down and out; raising the arm forward then levels it with its top side up."""
    source, length, grip = HELD[MODEL]
    gun = lt.import_glb(source)
    lo, hi = lt.bounds(gun)
    size = hi - lo
    axis = max(range(3), key=lambda i: size[i])
    lt.normalize(gun, length, axis, ground=False)
    gun_mat = lt.save_textures(gun, source)
    lo, hi = lt.bounds(gun)
    bones = arm.data.bones
    hand_mid = (bones["hand.R"].head_local + bones["hand.R"].tail_local) / 2
    along = (bones["hand.R"].tail_local - bones["forearm.R"].head_local).normalized()      # barrel direction
    top = Vector((0, -1, 0))
    top = (top - along * top.dot(along)).normalized()                                       # sights face forward at rest
    side = along.cross(top)
    # Meshy builds the side-view rifle with its barrel along +X and its top along +Z.
    src = Matrix(((1, 0, 0), (0, 1, 0), (0, 0, 1)))
    if axis != 0:
        src = Matrix(((0, 1, 0), (1, 0, 0), (0, 0, 1))) if axis == 1 else Matrix(((0, 0, 1), (0, 1, 0), (1, 0, 0)))
    back = -along                                                         # the model's +X is its stock end
    dst = Matrix((back, top.cross(back), top)).transposed()               # columns: where +X, +Y, +Z go
    rot = dst @ src.inverted()
    grip_point = (lo + hi) / 2; grip_point[axis] = hi[axis] - (hi[axis] - lo[axis]) * grip
    for v in gun.data.vertices:
        v.co = rot @ (v.co - grip_point) + hand_mid
    gun.data.update()
    group = gun.vertex_groups.new(name="hand.R")
    group.add(list(range(len(gun.data.vertices))), 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    gun.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.join()


def energy_material():
    mat = bpy.data.materials.new("varrik_energy")       # Unity swaps this for a glowing material by name
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Base Color"].default_value = (0.2, 0.55, 1.0, 1)
    bsdf.inputs["Emission Color"].default_value = (0.3, 0.7, 1.0, 1)
    bsdf.inputs["Emission Strength"].default_value = 4.0
    return mat


def join_rigid(mesh, obj, bone):
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.join()


def attach_energy_arms(mesh, arm):
    """A curved energy sabre in the right hand and a round energy buckler on the left forearm."""
    import bmesh
    bones = arm.data.bones
    glow = energy_material()
    k = HEIGHT / 2.7

    if ENERGY_ARMS[MODEL] == "glaive":
        hand = bones["hand.R"]
        along = (hand.tail_local - bones["forearm.R"].head_local).normalized()
        edge = Vector((0, -1, 0)); edge = (edge - along * edge.dot(along)).normalized()
        flat = along.cross(edge)
        grip = (hand.head_local + hand.tail_local) / 2
        bm = bmesh.new()

        def ribbon(points, widths, thick, across):
            rings = [[bm.verts.new(c + across[i] * w), bm.verts.new(c + flat * thick), bm.verts.new(c - across[i] * w), bm.verts.new(c - flat * thick)]
                     for i, (c, w) in enumerate(zip(points, widths))]
            for a, b in zip(rings, rings[1:]):
                for j in range(4):
                    bm.faces.new((a[j], a[(j + 1) % 4], b[(j + 1) % 4], b[j]))
            bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])

        # Haft: from behind the hand out to the blade.
        pts = [grip + along * d for d in (-0.9, -0.3, 0.6, 1.5, 1.9)]
        ribbon(pts, [0.03] * 5, 0.03, [edge] * 5)
        # Crescent: an arc centred beyond the end of the haft, opening forwards.
        tip = grip + along * 1.9
        centre = tip + edge * 0.55
        pts, widths, across = [], [], []
        n = 16
        for i in range(n + 1):
            t = i / n
            a = math.radians(-205 + 230 * t)                     # sweeps round from below the haft to past its end
            radial = along * math.sin(a) * -1 + edge * math.cos(a)
            pts.append(centre + radial * 0.62)
            widths.append(0.02 + 0.15 * math.sin(math.pi * t) ** 0.7)
            across.append(radial)
        ribbon(pts, widths, 0.014, across)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new("Glaive"); bm.to_mesh(me); bm.free()
        glaive = bpy.data.objects.new("Glaive", me)
        bpy.context.scene.collection.objects.link(glaive)
        me.materials.append(glow)
        join_rigid(mesh, glaive, "hand.R")
        return

    # Sabre: a flat blade that curves and tapers, running on from the forearm.
    hand = bones["hand.R"]
    along = (hand.tail_local - bones["forearm.R"].head_local).normalized()
    edge = Vector((0, -1, 0)); edge = (edge - along * edge.dot(along)).normalized()      # cutting edge faces forward
    flat = along.cross(edge)
    start = (hand.head_local + hand.tail_local) / 2
    bm = bmesh.new()
    rings = []
    n = 12
    for i in range(n + 1):
        t = i / n
        centre = start + along * (0.12 + 1.25 * t) * k + edge * (0.22 * t * t) * k      # curve towards the edge
        width = (0.085 * (1 - t ** 3) + 0.004) * k
        thick = 0.012 * k
        rings.append([bm.verts.new(centre + edge * width + flat * 0), bm.verts.new(centre + flat * thick),
                      bm.verts.new(centre - edge * width * 0.6), bm.verts.new(centre - flat * thick)])
    for a, b in zip(rings, rings[1:]):
        for j in range(4):
            bm.faces.new((a[j], a[(j + 1) % 4], b[(j + 1) % 4], b[j]))
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    me = bpy.data.meshes.new("Sabre"); bm.to_mesh(me); bm.free()
    sabre = bpy.data.objects.new("Sabre", me)
    bpy.context.scene.collection.objects.link(sabre)
    me.materials.append(glow)
    join_rigid(mesh, sabre, "hand.R")

    # Buckler: a shallow disc strapped to the outside of the left forearm.
    fore = bones["forearm.L"]
    f_along = (fore.tail_local - fore.head_local).normalized()
    out = Vector((1, 0, 0)); out = (out - f_along * out.dot(f_along)).normalized()
    side = f_along.cross(out)
    centre = (fore.head_local + fore.tail_local) / 2 + out * 0.16 * k
    bm = bmesh.new()
    radius, segs = 0.33 * k, 24
    front_c = bm.verts.new(centre + out * 0.07 * k)
    back_c = bm.verts.new(centre)
    rim = [bm.verts.new(centre + (f_along * math.cos(a) + side * math.sin(a)) * radius)
           for a in (2 * math.pi * i / segs for i in range(segs))]
    for i in range(segs):
        bm.faces.new((front_c, rim[i], rim[(i + 1) % segs]))
        bm.faces.new((back_c, rim[(i + 1) % segs], rim[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Buckler"); bm.to_mesh(me); bm.free()
    buckler = bpy.data.objects.new("Buckler", me)
    bpy.context.scene.collection.objects.link(buckler)
    me.materials.append(glow)
    join_rigid(mesh, buckler, "forearm.L")


# ------------------------------------------------------------------ animation
def Animator(arm):
    return lt_rig.Animator(arm, ANIM_SCALE)


def cycle(table, t):
    """A smooth closed curve through `table` [(phase 0..1, values...)], sampled at phase t."""
    n = len(table)
    t = t % 1.0
    i = max(k for k in range(n) if table[k][0] <= t)

    def at(k):
        return table[k % n][0] + (k // n), Vector(table[k % n][1:])
    (tp, p), (ta, a), (tb, b), (tn, q) = at(i - 1), at(i), at(i + 1), at(i + 2)
    k = (t - ta) / (tb - ta)
    ma, mb = (b - p) / (tb - tp) * (tb - ta), (q - a) / (tn - ta) * (tb - ta)
    k2, k3 = k * k, k * k * k
    return a * (2 * k3 - 3 * k2 + 1) + ma * (k3 - 2 * k2 + k) + b * (-2 * k3 + 3 * k2) + mb * (k3 - k2)


# One leg through a running stride: phase, thigh, shin, foot, toe (degrees). Phase 0 is the foot striking the ground.
STRIDE = [
    (0.00, -36, 14, -10, 8),      # contact: the leg reaches ahead
    (0.10, -24, 36, -22, 0),      # the knee gives as it takes the weight
    (0.28, 2, 26, -12, 0),        # the body passes over the foot
    (0.45, 28, 8, 14, -22),       # push-off: the leg drives straight out behind
    (0.58, 18, 70, 6, 15),        # the heel flicks up
    (0.72, -30, 92, -30, 5),      # the knee is carried forward, folded
    (0.88, -50, 40, -22, 10),     # and the leg unfolds to reach again
]


CAST_DROP = -0.66
ARM_IN = 28          # degrees that bring an A-pose arm in against the body (left arm; the right takes the opposite sign)
CAST = {
    "rest": {"forearm.LR": (-10, 0, 0), "upper_arm.L": (0, ARM_IN, 0), "upper_arm.R": (0, -ARM_IN, 0)},
    # Left hand lifted overhead, palm turned up to the sky, head back to watch it; the right arm hangs ready.
    "raised": {"hips": (-3, 0, 6), "spine": (-6, 0, 4), "chest": (-10, 0, 6), "neck": (-12, 0, -8), "head": (-18, 0, -10),
               "upper_arm.L": (-135, 0, 0), "forearm.L": (-45, 0, 0), "hand.L": (118, 0, 0), "fingers.L": (18, 0, 0),
               "upper_arm.R": (10, -ARM_IN, 0), "forearm.R": (-25, 0, 0)},
    "raised2": {"hips": (-3, 0, 6), "spine": (-7, 0, 4), "chest": (-12, 0, 7), "neck": (-13, 0, -8), "head": (-20, 0, -10),
                "upper_arm.L": (-142, 0, 0), "forearm.L": (-40, 0, 0), "hand.L": (122, 0, 0), "fingers.L": (24, 0, 0),
                "upper_arm.R": (12, -ARM_IN, 0), "forearm.R": (-28, 0, 0)},
    # The hand closes and reaches a little higher: the last moment before it comes down.
    "fist": {"hips": (-5, 0, 6), "spine": (-9, 0, 4), "chest": (-15, 0, 8), "neck": (-12, 0, -8), "head": (-16, 0, -10),
             "upper_arm.L": (-152, 0, 0), "forearm.L": (-58, 0, 0), "hand.L": (100, 0, 0), "fingers.L": (98, 0, 0),
             "upper_arm.R": (20, -ARM_IN, 0), "forearm.R": (-35, 0, 0)},
    # Driven down onto one knee: left foot planted ahead, right knee on the ground, the body bent over the fist.
    "slam": {"hips": (12, 0, 0), "spine": (20, 0, 0), "chest": (24, 0, -6), "neck": (-18, 0, 0), "head": (-16, 0, 0),
             "upper_arm.L": (-50, ARM_IN, 0), "forearm.L": (-6, 0, 0), "hand.L": (10, 0, 0), "fingers.L": (95, 0, 0),
             "upper_arm.R": (30, -ARM_IN, 0), "forearm.R": (-40, 0, 0),
             "thigh.L": (-112, 0, 0), "shin.L": (100, 0, 0), "foot.L": (0, 0, 0),
             "thigh.R": (-12, 0, 0), "shin.R": (90, 0, 0), "foot.R": (38, 0, 0), "toe.R": (-50, 0, 0)},
    "slam2": {"hips": (15, 0, 0), "spine": (23, 0, 0), "chest": (26, 0, -6), "neck": (-14, 0, 0), "head": (-10, 0, 0),
              "upper_arm.L": (-46, ARM_IN, 0), "forearm.L": (-12, 0, 0), "hand.L": (10, 0, 0), "fingers.L": (95, 0, 0),
              "upper_arm.R": (36, -ARM_IN, 0), "forearm.R": (-44, 0, 0),
              "thigh.L": (-114, 0, 0), "shin.L": (102, 0, 0), "foot.L": (0, 0, 0),
              "thigh.R": (-12, 0, 0), "shin.R": (92, 0, 0), "foot.R": (38, 0, 0), "toe.R": (-50, 0, 0)},
}


def make_animations(arm):
    A = Animator(arm)
    relaxed = {"upper_arm.LR": (0, 0, 0)}

    # ---- Idle: 60 frames, breathing and a slow predatory sway
    A.begin("Idle", 60, loop=True)
    for f, s in ((0, 0), (15, 1), (30, 0), (45, -1), (60, 0)):
        A.key(f, {
            "spine": (2 + 1.5 * abs(s), 0, 2 * s), "chest": (3 * abs(s), 0, 2 * s),
            "neck": (-2 * abs(s), 0, -3 * s), "head": (2, 0, -5 * s),
            "upper_arm.L": (-4 - 3 * abs(s), 3 * abs(s), 0), "upper_arm.R": (-4 - 3 * abs(s), -3 * abs(s), 0),
            "forearm.LR": (-10 - 4 * abs(s), 0, 0), "hand.LR": (-8, 0, 0),
        }, hips_z=-0.012 * abs(s))

    # ---- Run: 20 frames. Each leg goes through contact, give, pass, push-off, flick and reach; the hips drop as
    # each foot takes the weight and rise in the flight between; the pelvis and the chest twist against each other,
    # the arms swing against the legs with the elbow folding on the forward swing, and the head stays on its target.
    A.begin("Run", 20, loop=True)
    g = GAIT.get(MODEL, 1.0)
    for f in range(0, 21):
        t = f / 20
        c, s2 = math.cos(2 * math.pi * t), math.sin(2 * math.pi * t)
        bounce = math.cos(4 * math.pi * (t - 0.38))            # +1 at the top of each flight, -1 as each foot loads
        pose = {
            "hips": (10 - 2 * bounce, 4 * s2, -8 * c), "spine": (9 + 2 * bounce, -2 * s2, 4 * c), "chest": (9 + 3 * bounce, -3 * s2, 10 * c),
            "neck": (-10 - 2 * bounce, 0, -5 * c), "head": (-12 - 3 * bounce, 1.5 * s2, -6 * c),
            "upper_arm.L": (8 + 44 * c, 12, 0), "upper_arm.R": (8 - 44 * c, -12, 0),
            "forearm.L": (-56 + 24 * c, 0, 0), "forearm.R": (-56 - 24 * c, 0, 0),
            "hand.L": (-10 + 10 * c, 0, 0), "hand.R": (-10 - 10 * c, 0, 0),
        }
        for side, ph in ((".L", t), (".R", t + 0.5)):
            thigh, shin, foot, toe = cycle(STRIDE, ph)
            pose["thigh" + side] = (thigh * g, 0, 0); pose["shin" + side] = (shin * g, 0, 0)
            pose["foot" + side] = (foot * g, 0, 0); pose["toe" + side] = (toe * g, 0, 0)
        if MODEL in GAIT:           # upright and composed: no forward lean, arms held
            pose.update({"hips": (2, 0, -2 * c), "spine": (2, 0, 2 * c), "chest": (2, 0, 2 * c), "neck": (0, 0, 0), "head": (0, 0, 0),
                         "upper_arm.L": (6 * c, 0, 0), "upper_arm.R": (-6 * c, 0, 0), "forearm.L": (-12, 0, 0), "forearm.R": (-12, 0, 0),
                         "hand.L": (0, 0, 0), "hand.R": (0, 0, 0)})
            A.key(f, pose, hips_z=0.01 * bounce)
        else:
            A.key(f, pose, hips_z=-0.05 + 0.045 * bounce, hips_x=0.02 * s2)

    # ---- Attack: 24 frames; the blow lands at frame 11 (0.35 s at 30 fps, matching EnemyAI.attackWindup).
    # A wind-up that coils the whole body to the right with the right arm cocked high, a lunging step onto the left
    # foot as the torso whips round and the arm rakes down across, then the swing carries through before it recovers.
    ready = {"spine": (4, 0, 0), "forearm.LR": (-10, 0, 0)}
    A.begin("Attack", 24)
    A.limb_scale = {k: g for k in ("thigh", "shin", "foot", "toe")} if g < 1 else {}
    A.key(0, ready)
    A.key(7, {"hips": (-6, 0, -14), "spine": (-8, 0, -10), "chest": (-12, 0, -16), "neck": (4, 0, 14), "head": (8, 0, 14),
              "upper_arm.R": (-150, 25, 0), "forearm.R": (-62, 0, 0), "hand.R": (20, 0, 0),
              "upper_arm.L": (-58, -14, 0), "forearm.L": (-52, 0, 0), "hand.L": (10, 0, 0),
              "thigh.L": (-14, 0, 0), "shin.L": (16, 0, 0), "thigh.R": (6, 0, 0), "shin.R": (16, 0, 0)}, hips_z=-0.05, hips_y=0.07, ease="out")
    A.key(11, {"hips": (20, 0, 12), "spine": (16, 0, 10), "chest": (20, 0, 18), "neck": (-18, 0, -14), "head": (-16, 0, -14),
               "upper_arm.R": (-58, -24, 0), "forearm.R": (-14, 0, 0), "hand.R": (-30, 0, 0),
               "upper_arm.L": (22, -18, 0), "forearm.L": (-42, 0, 0),
               "thigh.L": (-42, 0, 0), "shin.L": (32, 0, 0), "foot.L": (-10, 0, 0), "thigh.R": (16, 0, 0), "shin.R": (10, 0, 0)},
          hips_z=-0.11, hips_y=-0.22, ease="in")
    A.key(14, {"hips": (22, 0, 18), "spine": (16, 0, 14), "chest": (22, 0, 26), "neck": (-16, 0, -20), "head": (-12, 0, -18),
               "upper_arm.R": (-18, -34, 0), "forearm.R": (-26, 0, 0), "hand.R": (-25, 0, 0),
               "upper_arm.L": (30, -22, 0), "forearm.L": (-36, 0, 0),
               "thigh.L": (-40, 0, 0), "shin.L": (36, 0, 0), "foot.L": (-10, 0, 0), "thigh.R": (18, 0, 0), "shin.R": (14, 0, 0)},
          hips_z=-0.12, hips_y=-0.25, ease="out")
    A.key(19, {"hips": (8, 0, 6), "spine": (8, 0, 4), "chest": (8, 0, 8), "neck": (-6, 0, -6), "head": (-4, 0, -6),
               "upper_arm.R": (-8, -12, 0), "forearm.R": (-22, 0, 0), "upper_arm.L": (10, -8, 0), "forearm.L": (-24, 0, 0),
               "thigh.L": (-16, 0, 0), "shin.L": (16, 0, 0), "thigh.R": (6, 0, 0)}, hips_z=-0.04, hips_y=-0.09)
    A.key(24, ready)
    A.finish()
    A.limb_scale = {}

    # ---- Hit: 10 frames, flinch backwards
    A.begin("Hit", 10)
    A.key(0, {"forearm.LR": (-10, 0, 0)})
    A.key(3, {"hips": (-6, 0, 6), "spine": (-12, 0, 8), "chest": (-14, 0, 10), "head": (-18, 0, -12),
              "upper_arm.L": (-25, -30, 0), "upper_arm.R": (-10, 20, 0), "forearm.LR": (-35, 0, 0)}, hips_z=-0.03)
    A.key(10, {"forearm.LR": (-10, 0, 0)})

    # ---- Spit: 20 frames; the bolt leaves at frame 9 (0.3 s, matching EnemyAI's release time)
    A.begin("Spit", 20)
    if MODEL_IS_VARRIK:
        # Weapon arm levelled at the target, off hand brought across to steady it, a short kick as it fires at frame 9.
        aim = {"hips": (0, 0, 14), "spine": (2, 0, 6), "chest": (4, 0, 6), "neck": (0, 0, -14), "head": (4, 0, -12),
               "upper_arm.R": (-88, 0, 38), "forearm.R": (-2, 0, 6), "hand.R": (0, 0, 0),
               "upper_arm.L": (-70, 0, -62), "forearm.L": (-25, 0, -30), "thigh.L": (-10, 0, 0), "shin.L": (8, 0, 0)}
        kick = dict(aim); kick["upper_arm.R"] = (-96, 0, 38); kick["chest"] = (0, 0, 6)
        if MODEL in ENERGY_ARMS:
            # No gun: the buckler arm is thrust forward to loose a bolt, the sabre held back and low.
            aim = {"hips": (0, 0, -12), "spine": (4, 0, -6), "chest": (6, 0, -8), "neck": (0, 0, 12), "head": (2, 0, 10),
                   "upper_arm.L": (-84, 0, -30), "forearm.L": (-6, 0, 0), "upper_arm.R": (28, -10, 0), "forearm.R": (-20, 0, 0),
                   "thigh.R": (-10, 0, 0), "shin.R": (8, 0, 0)}
            kick = dict(aim); kick["upper_arm.L"] = (-70, 0, -30); kick["forearm.L"] = (-30, 0, 0)
        A.key(0, {"forearm.LR": (-10, 0, 0)})
        A.key(5, aim); A.key(8, aim); A.key(10, kick); A.key(13, aim); A.key(16, aim)
        A.key(20, {"forearm.LR": (-10, 0, 0)})
    A.key(0, {"forearm.LR": (-10, 0, 0)}) if not MODEL_IS_VARRIK else None
    if not MODEL_IS_VARRIK:
        A.key(5, {"hips": (-5, 0, 0), "spine": (-12, 0, 0), "chest": (-16, 0, 0), "neck": (-10, 0, 0), "head": (-16, 0, 0),
                  "upper_arm.LR": (22, -18, 0), "forearm.LR": (-45, 0, 0)}, hips_z=-0.03)
        A.key(9, {"hips": (10, 0, 0), "spine": (16, 0, 0), "chest": (20, 0, 0), "neck": (12, 0, 0), "head": (16, 0, 0),
                  "upper_arm.LR": (38, 22, 0), "forearm.LR": (-15, 0, 0), "thigh.LR": (-12, 0, 0), "shin.LR": (16, 0, 0)}, hips_z=-0.06)
        A.key(13, {"hips": (8, 0, 0), "spine": (12, 0, 0), "chest": (14, 0, 0), "neck": (8, 0, 0), "head": (10, 0, 0),
                   "upper_arm.LR": (30, 18, 0), "forearm.LR": (-20, 0, 0)}, hips_z=-0.04)
        A.key(20, {"forearm.LR": (-10, 0, 0)})

    # ---- Slam: 36 frames; both fists hit the ground at frame 20 (0.67 s, matching EnemyAI's impact time)
    A.begin("Slam", 36)
    A.key(0, {"forearm.LR": (-10, 0, 0)})
    A.key(14, {"hips": (-8, 0, 0), "spine": (-12, 0, 0), "chest": (-16, 0, 0), "head": (10, 0, 0),
               "upper_arm.LR": (-165, -15, 0), "forearm.LR": (-30, 0, 0), "hand.LR": (15, 0, 0)}, hips_z=0.02)
    A.key(20, {"hips": (30, 0, 0), "spine": (24, 0, 0), "chest": (24, 0, 0), "neck": (-20, 0, 0), "head": (-15, 0, 0),
               "upper_arm.LR": (-55, 15, 0), "forearm.LR": (-10, 0, 0), "hand.LR": (-30, 0, 0),
               "thigh.LR": (-38, 0, 0), "shin.LR": (48, 0, 0), "foot.LR": (-12, 0, 0)}, hips_z=-0.26)
    A.key(26, {"hips": (26, 0, 0), "spine": (20, 0, 0), "chest": (20, 0, 0), "neck": (-16, 0, 0), "head": (-12, 0, 0),
               "upper_arm.LR": (-50, 18, 0), "forearm.LR": (-14, 0, 0), "hand.LR": (-25, 0, 0),
               "thigh.LR": (-32, 0, 0), "shin.LR": (40, 0, 0), "foot.LR": (-10, 0, 0)}, hips_z=-0.22)
    A.key(36, {"forearm.LR": (-10, 0, 0)})

    # ---- Death: 30 frames, staggers and falls on its back
    A.begin("Death", 30)
    A.key(0, {"forearm.LR": (-10, 0, 0)})
    A.key(8, {"root": (-18, 0, 0), "spine": (-14, 0, 0), "chest": (-18, 0, 0), "head": (-25, 0, 10),
              "upper_arm.LR": (-70, -40, 0), "forearm.LR": (-50, 0, 0), "thigh.LR": (-20, 0, 0), "shin.LR": (35, 0, 0)}, hips_z=-0.1)
    A.key(20, {"root": (-84, 0, 0), "spine": (-6, 0, 0), "chest": (-8, 0, 0), "head": (-12, 0, 20),
               "upper_arm.L": (-30, -55, 0), "upper_arm.R": (-50, 50, 0), "forearm.LR": (-30, 0, 0),
               "thigh.L": (-45, 0, 0), "thigh.R": (-15, 0, 0), "shin.L": (60, 0, 0), "shin.R": (25, 0, 0)}, hips_z=-0.05, hips_y=-0.24)
    A.key(24, {"root": (-90, 0, 0), "spine": (-2, 0, 0), "head": (-8, 0, 25),
               "upper_arm.L": (-20, -60, 0), "upper_arm.R": (-40, 55, 0), "forearm.LR": (-20, 0, 0),
               "thigh.L": (-35, 0, 0), "thigh.R": (-10, 0, 0), "shin.L": (45, 0, 0), "shin.R": (18, 0, 0)}, hips_y=-0.27)
    A.key(30, {"root": (-90, 0, 0), "spine": (-2, 0, 0), "head": (-8, 0, 25),
               "upper_arm.L": (-20, -60, 0), "upper_arm.R": (-40, 55, 0), "forearm.LR": (-20, 0, 0),
               "thigh.L": (-35, 0, 0), "thigh.R": (-10, 0, 0), "shin.L": (45, 0, 0), "shin.R": (18, 0, 0)}, hips_y=-0.27)

    if MODEL == "player":
        # ---- Cast: the Lightning Wave, seen from behind. Frames at 30 fps, matched by Abilities.LightningWave:
        # the hand is up by frame 14, held until 57, closes into a fist by 61, and lands at frame 66 (2.2 s).
        A.begin("Cast", 108)
        A.limb_scale = {}
        A.key(0, CAST["rest"])
        A.key(14, CAST["raised"], ease="out")
        A.key(34, CAST["raised2"])
        A.key(57, CAST["raised"])
        A.key(61, CAST["fist"], ease="io")
        A.key(66, CAST["slam"], hips_z=CAST_DROP, hips_y=-0.12, ease="in")
        A.key(72, CAST["slam2"], hips_z=CAST_DROP - 0.03, hips_y=-0.14, ease="out")
        A.key(92, CAST["slam"], hips_z=CAST_DROP, hips_y=-0.12)
        A.key(108, CAST["rest"])
    A.finish()
    for act in bpy.data.actions:
        if act.name == "Cast":
            act.frame_range = (0, 108)
            act.use_frame_range = True
            continue
        act.frame_range = (0, {"Idle": 60, "Run": 20, "Attack": 24, "Hit": 10, "Spit": 20, "Slam": 36, "Death": 30}[act.name])
        act.use_frame_range = True


def set_action(arm, name):
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]


def main():
    lt.reset()
    bpy.context.scene.render.fps = 30
    mesh = lt.import_glb(SOURCE.get(MODEL, MODEL))
    lt.normalize(mesh, HEIGHT, 2)
    lt.save_textures(mesh, MODEL)
    arm = build_armature()
    skin(mesh, arm)
    if MODEL in HELD:
        attach_held(mesh, arm)
    if MODEL in ENERGY_ARMS:
        attach_energy_arms(mesh, arm)
    make_animations(arm)

    if PREVIEW:
        shots = [("Idle", 15), ("Run", 5), ("Attack", 11), ("Spit", 9), ("Slam", 20), ("Death", 30)]
        for clip, frame in shots:
            set_action(arm, clip)
            bpy.context.scene.frame_set(frame)
            lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_{clip}.png"), (0, 0, 0.48 * HEIGHT), 2.5 * HEIGHT,
                            (1.0, -1.25, 0.25), (480, 480))

    if MODEL == "player" and PREVIEW:
        set_action(arm, "Cast")
        for f in (0, 40, 61, 70):
            bpy.context.scene.frame_set(f)
            lt.persp_render(os.path.join(lt.SHOTS, f"cast_{f:03d}.png"), (0, 0, 1.1), 4.2, (-0.9, 1.6, 0.45), (600, 600))
            lt.persp_render(os.path.join(lt.SHOTS, f"castside_{f:03d}.png"), (0, 0, 1.1), 4.2, (-1.7, -0.5, 0.2), (600, 600))
        bpy.context.scene.frame_set(40)
        hand = arm.matrix_world @ arm.pose.bones["hand.L"].head
        lt.persp_render(os.path.join(lt.SHOTS, "cast_hand.png"), tuple(hand), 1.3, (-1.0, 1.2, 0.3), (600, 600))

    if "sheet" in sys.argv:
        for clip, frames in (("Run", (0, 2, 5, 7, 10, 12, 15, 17)), ("Attack", (0, 4, 7, 9, 11, 14, 19, 22))):
            set_action(arm, clip)
            for f in frames:
                bpy.context.scene.frame_set(f)
                direction = (1.6, -0.15, 0.1) if clip == "Run" else (1.1, -1.2, 0.2)
                lt.persp_render(os.path.join(lt.SHOTS, f"sheet_{MODEL}_{clip}_{f:02d}.png"), (0, 0, 0.5 * HEIGHT), 2.3 * HEIGHT, direction, (360, 360))

    if PREVIEW and MODEL_IS_VARRIK:
        set_action(arm, "Spit")
        bpy.context.scene.frame_set(8)
        lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_SpitSide.png"), (0, 0, 0.55 * HEIGHT), 1.9 * HEIGHT, (1.6, -0.35, 0.15), (480, 480))
        lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_SpitFront.png"), (0, 0, 0.55 * HEIGHT), 1.9 * HEIGHT, (0.3, -1.6, 0.15), (480, 480))
        set_action(arm, "Idle")
        bpy.context.scene.frame_set(15)
        lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_IdleClose.png"), (0, 0, 0.55 * HEIGHT), 1.9 * HEIGHT, (0.9, -1.4, 0.2), (480, 480))
        set_action(arm, "Attack")
        bpy.context.scene.frame_set(11)
        lt.persp_render(os.path.join(lt.SHOTS, f"anim_{MODEL}_AttackSide.png"), (0, 0, 0.5 * HEIGHT), 1.9 * HEIGHT, (1.6, -0.35, 0.15), (480, 480))

    set_action(arm, "Idle")
    bpy.context.scene.frame_set(0)
    lt.export_fbx(MODEL, [arm, mesh], rigged=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(lt.ROOT, "ArtSource", "blender", MODEL + "_rigged.blend"))


main()
