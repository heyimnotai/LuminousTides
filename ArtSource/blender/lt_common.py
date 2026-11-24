"""Shared Blender helpers for the Luminous Tides asset pipeline (Blender 5.x, headless)."""
import bpy, os, math
from mathutils import Vector

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MESHY = os.path.join(ROOT, "ArtSource", "meshy")
MODELS = os.path.join(ROOT, "Assets", "Art", "Models")
TEXTURES = os.path.join(ROOT, "Assets", "Art", "Textures")
SHOTS = os.path.join(ROOT, "Screenshots")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(name):
    """Imports ArtSource/meshy/<name>.glb and returns one joined mesh object with transforms applied."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(MESHY, name + ".glb"), merge_vertices=True)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o.type != 'MESH' and o.name in bpy.data.objects:
            bpy.data.objects.remove(o)
    obj.name = name
    obj.data.name = name
    obj.rotation_mode = 'XYZ'      # the glTF importer leaves objects in quaternion mode
    return obj


def bounds(obj):
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def apply(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def normalize(obj, size, axis=2, rot_z_deg=0.0, ground=True):
    """Rotates about Z, scales so the extent along `axis` equals `size`, centres on XY and rests on z=0."""
    obj.rotation_euler = (0, 0, math.radians(rot_z_deg))
    apply(obj)
    lo, hi = bounds(obj)
    s = size / (hi - lo)[axis]
    obj.scale = (s, s, s)
    apply(obj)
    lo, hi = bounds(obj)
    obj.location = (-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z if ground else -(lo.z + hi.z) / 2)
    apply(obj)
    return obj


def save_textures(obj, name, max_size=2048):
    """Renames the object's material to <name> and writes <name>_base/_normal/_emit.png for Unity."""
    os.makedirs(TEXTURES, exist_ok=True)
    mat = obj.data.materials[0]
    mat.name = name
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')

    def source_image(socket_name):
        sock = bsdf.inputs.get(socket_name)
        if sock is None or not sock.is_linked:
            return None
        node = sock.links[0].from_node
        if node.type == 'NORMAL_MAP':
            node = node.inputs['Color'].links[0].from_node
        return node.image if node.type == 'TEX_IMAGE' else None

    for socket_name, suffix in (("Base Color", "base"), ("Normal", "normal"), ("Emission Color", "emit")):
        img = source_image(socket_name)
        if img is None:
            continue
        if max(img.size) > max_size:
            img.scale(max_size, max_size)
        img.filepath_raw = os.path.join(TEXTURES, f"{name}_{suffix}.png")
        img.file_format = 'PNG'
        img.save()
    return mat


def export_fbx(name, objects, rigged=False):
    os.makedirs(MODELS, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODELS, name + ".fbx"), use_selection=True,
        apply_scale_options='FBX_SCALE_ALL' if rigged else 'FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=not rigged,
        object_types={'MESH', 'ARMATURE', 'EMPTY'}, mesh_smooth_type='FACE', add_leaf_bones=False,
        bake_anim=rigged, bake_anim_use_all_actions=rigged, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode='AUTO')
    print("exported", name)


def setup_render(res=(900, 900)):
    s = bpy.context.scene
    s.render.engine = 'BLENDER_EEVEE'
    s.render.resolution_x, s.render.resolution_y = res
    if s.world is None:
        s.world = bpy.data.worlds.new("w")
    s.world.use_nodes = True
    s.world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.75, 0.75, 1)
    s.world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
    if "lt_sun" not in bpy.data.objects:
        sun = bpy.data.objects.new("lt_sun", bpy.data.lights.new("lt_sun", 'SUN'))
        sun.data.energy = 2.5
        sun.rotation_euler = (math.radians(55), 0, math.radians(-25))
        s.collection.objects.link(sun)


def ortho_render(path, centre, span, view='FRONT', res=900):
    """Orthographic render. `span` metres map to `res` pixels, so pixel = (coord - centre) * res / span + res / 2."""
    setup_render((res, res))
    cam_data = bpy.data.cameras.new("ortho")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = span
    cam = bpy.data.objects.new("ortho", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    c = Vector(centre)
    offsets = {'FRONT': (Vector((0, -50, 0)), (math.radians(90), 0, 0)),
               'BACK': (Vector((0, 50, 0)), (math.radians(90), 0, math.radians(180))),
               'RIGHT': (Vector((50, 0, 0)), (math.radians(90), 0, math.radians(90))),
               'LEFT': (Vector((-50, 0, 0)), (math.radians(90), 0, math.radians(-90))),
               'TOP': (Vector((0, 0, 50)), (0, 0, 0))}
    off, rot = offsets[view]
    cam.location = c + off
    cam.rotation_euler = rot
    bpy.context.scene.camera = cam
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def persp_render(path, target, distance, direction=(1.2, -1.6, 0.7), res=(900, 700)):
    setup_render(res)
    cam = bpy.data.objects.new("persp", bpy.data.cameras.new("persp"))
    bpy.context.scene.collection.objects.link(cam)
    t = Vector(target)
    cam.location = t + Vector(direction).normalized() * distance
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
