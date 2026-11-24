"""Prints structure of GLB files and renders a quick preview.  blender -b --python inspect_glb.py -- <glb> [<glb> ...]"""
import bpy, sys, os, math
from mathutils import Vector

for path in sys.argv[sys.argv.index("--") + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    print("=== ", os.path.basename(path))
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in bpy.context.scene.objects:
        if o.type != 'MESH':
            print("  obj", o.name, o.type); continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        print("  mesh", o.name, "verts", len(o.data.vertices), "tris", sum(len(p.vertices) - 2 for p in o.data.polygons),
              "mats", [m.name for m in o.data.materials], "parent", o.parent.name if o.parent else None)
    print("  bounds min", tuple(round(v, 3) for v in lo), "max", tuple(round(v, 3) for v in hi), "size", tuple(round(v, 3) for v in hi - lo))
    for img in bpy.data.images:
        print("  image", img.name, tuple(img.size))
    for m in bpy.data.materials:
        links = [(l.from_node.type, l.to_socket.name) for l in m.node_tree.links] if m.use_nodes else []
        print("  material", m.name, links)

    # preview render
    size = max(hi - lo); centre = (hi + lo) / 2
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = centre + Vector((1.3, -1.6, 0.9)) * size
    cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    bpy.context.scene.collection.objects.link(sun)
    w = bpy.data.worlds.new("w"); bpy.context.scene.world = w
    w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.6, 0.6, 1)
    s = bpy.context.scene
    s.render.engine = 'BLENDER_EEVEE'
    s.render.resolution_x = 640; s.render.resolution_y = 480
    s.render.filepath = os.path.splitext(path)[0] + "_preview.png"
    bpy.ops.render.render(write_still=True)
