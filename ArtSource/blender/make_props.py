"""Builds the simple hand-made props (rock, grass tuft, coral) and exports them as FBX.

Run:  blender -b --python make_props.py -- <output dir>
"""
import bpy, bmesh, math, random, sys, os
from mathutils import Vector, noise

OUT = sys.argv[sys.argv.index("--") + 1]
random.seed(11)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, emission=None, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if emission:
        b.inputs["Emission Color"].default_value = (*emission, 1)
        b.inputs["Emission Strength"].default_value = 1.0
    return m


def tube(bm, points, sides=5):
    """points: list of (Vector centre, radius). Builds a capped tube along them."""
    rings = []
    for i, (c, r) in enumerate(points):
        a = points[max(i - 1, 0)][0]
        b = points[min(i + 1, len(points) - 1)][0]
        d = (b - a).normalized()
        rot = d.to_track_quat('Z', 'Y').to_matrix()
        ring = [bm.verts.new(c + rot @ Vector((math.cos(k * 2 * math.pi / sides) * r, math.sin(k * 2 * math.pi / sides) * r, 0)))
                for k in range(sides)]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def finish(name, bm, mat, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(mat)
    for p in mesh.polygons:
        p.use_smooth = smooth
    return obj


def export(name):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, object_types={'MESH'}, mesh_smooth_type='FACE')
    print("exported", name)


def make_rock():
    reset()
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    for v in bm.verts:
        p = v.co.copy()
        n = noise.noise(p * 0.9 + Vector((3.1, 7.7, 1.3))) * 0.45 + noise.noise(p * 2.6) * 0.14
        v.co = p * (1.0 + n)
        v.co.z = max(v.co.z * 0.75, -0.25)          # squat, flat-bottomed boulder
        # chisel a few flat facets for a stylised look
        for plane in (Vector((0.6, 0.3, 0.75)), Vector((-0.7, 0.5, 0.5)), Vector((0.1, -0.8, 0.6))):
            pn = plane.normalized()
            d = v.co.dot(pn) - 0.78
            if d > 0:
                v.co -= pn * d
    obj = finish("rock", bm, material("Rock", (0.17, 0.19, 0.40), rough=0.65))
    obj.location.z = 0.25
    export("rock")


def make_grass():
    """A tuft of broad, arching ribbon blades. UV v runs base (0) to tip (1) for the gradient texture
    (Assets/Art/Textures/grass_base.png, written by make_textures.py and matched by the material name)."""
    reset()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    normals = {}
    blades = 17
    for i in range(blades):
        inner = i < 6
        ang = i / blades * 2 * math.pi * 2.4 + random.uniform(-0.3, 0.3)
        height = random.uniform(1.0, 1.7) if inner else random.uniform(0.55, 1.25)
        lean = random.uniform(0.15, 0.45) if inner else random.uniform(0.55, 1.15)
        width = random.uniform(0.07, 0.11)
        out = Vector((math.cos(ang), math.sin(ang), 0))
        side = Vector((-out.y, out.x, 0))
        base = out * random.uniform(0.02, 0.14)
        segs = 8
        rows = []
        for s in range(segs + 1):
            t = s / segs
            centre = base + out * (lean * height * t ** 1.8) + Vector((0, 0, height * (t - 0.32 * t * t)))
            w = width * (math.sin(min(t * 1.25 + 0.12, 1.0) * math.pi) ** 0.6) * (1.0 - t ** 3) + 0.004
            fold = out * (-0.35 * w)            # shallow V cross-section so the blade catches light
            row = [bm.verts.new(centre - side * w + fold), bm.verts.new(centre), bm.verts.new(centre + side * w + fold)]
            # Soft foliage shading: normals lean up and outward regardless of the facet direction.
            for v in row:
                normals[v] = (Vector((0, 0, 1)) * (0.9 - 0.5 * t) + out * (0.35 + 0.6 * t)).normalized()
            rows.append((row, t))
        for (r0, t0), (r1, t1) in zip(rows, rows[1:]):
            for k in range(2):
                f = bm.faces.new((r0[k], r0[k + 1], r1[k + 1], r1[k]))
                us = (k / 2, (k + 1) / 2, (k + 1) / 2, k / 2)
                vs = (t0, t0, t1, t1)
                for loop, u, v in zip(f.loops, us, vs):
                    loop[uv].uv = (u, v)
    mesh = bpy.data.meshes.new("grass")
    bm.verts.index_update()
    ordered = [normals[v] for v in bm.verts]
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    mesh.normals_split_custom_set_from_vertices(ordered)
    obj = bpy.data.objects.new("grass", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(material("grass", (0.62, 0.18, 0.85), rough=0.4))
    export("grass")


def make_coral():
    reset()
    bm = bmesh.new()

    def branch(start, direction, length, radius, depth):
        pts = []
        pos = start.copy()
        d = direction.normalized()
        segs = 3
        for s in range(segs + 1):
            pts.append((pos.copy(), radius * (1 - 0.25 * s / segs)))
            d = (d + Vector((random.uniform(-0.25, 0.25), random.uniform(-0.25, 0.25), 0.12))).normalized()
            pos += d * (length / segs)
        if depth == 0:
            pts.append((pos + d * radius * 0.8, radius * 0.35))  # rounded tip
        tube(bm, pts, sides=5)
        if depth > 0:
            for _ in range(random.choice((2, 2, 3))):
                nd = (d + Vector((random.uniform(-0.9, 0.9), random.uniform(-0.9, 0.9), random.uniform(0.1, 0.5)))).normalized()
                branch(pts[-1][0], nd, length * 0.7, radius * 0.72, depth - 1)

    for i in range(4):
        ang = i / 4 * 2 * math.pi + random.uniform(-0.4, 0.4)
        d = Vector((math.cos(ang) * 0.45, math.sin(ang) * 0.45, 1))
        branch(Vector((math.cos(ang) * 0.08, math.sin(ang) * 0.08, -0.03)), d, random.uniform(0.25, 0.4), 0.05, 2)
    finish("coral", bm, material("Coral", (1.0, 0.42, 0.10), emission=(0.45, 0.13, 0.0), rough=0.6))
    export("coral")


make_rock()
make_grass()
make_coral()
