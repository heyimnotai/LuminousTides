"""Normalises the static Meshy props and exports FBX + textures for Unity.

The fungus also gets a water surface cut to the exact inner outline of its cup ("Pool") and
"Spout" markers at the lowest points of the rim, where water would naturally spill over.

Run:  blender -b --python process_static.py [-- name ...]     (no names = all)
"""
import sys, os, math
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lt_common as lt

# name -> (size in metres, axis the size applies to: 0 = X width, 2 = Z height)
STATIC = {
    "fungus": (6.5, 2),
    "station": (14.0, 0),
    "crate": (2.5, 0),
    "arch": (15.0, 2),
    "spire": (11.0, 2),
    "boulder": (2.6, 0),
    "bulbplant": (1.3, 2),
    "varrik_ship": (38.0, 0),
    "stormgate": (26.0, 2),
    "citadel_stack": (24.0, 2),
    "citadel_cliff": (26.0, 0),
    "citadel_colonnade": (18.0, 0),
    "citadel_arch": (13.0, 2),
    "citadel_head": (10.0, 0),
    "citadel_wall": (13.0, 0),
    "citadel_stairs": (9.0, 0),
    "citadel_bridge": (30.0, 0),
    "citadel_tower": (22.0, 2),
    "citadel_terrace": (24.0, 2),
    "citadel_arenawall": (16.0, 0),
    "citadel_tusk": (16.0, 2),
    "citadel_dais": (14.0, 0),
    "citadel_statue": (20.0, 2),
    "citadel_rubble": (5.0, 0),
    "citadel_obelisk": (11.0, 2),
}
DIRS = 72


def add_pool(obj):
    """Returns [pool mesh object, spout empties...] fitted to the fungus cup."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bvh = BVHTree.FromBMesh(bm)
    lo, hi = lt.bounds(obj)
    top = hi.z
    upper = [v.co for v in bm.verts if v.co.z > top * 0.75]
    cx = sum(c.x for c in upper) / len(upper)
    cy = sum(c.y for c in upper) / len(upper)

    # Floor of the cup: first surface below a point high above the centre.
    hit = bvh.ray_cast(Vector((cx, cy, top + 1.0)), Vector((0, 0, -1)))
    floor = hit[0].z if hit[0] else top * 0.6
    dirs = [Vector((math.cos(i / DIRS * 2 * math.pi), math.sin(i / DIRS * 2 * math.pi), 0)) for i in range(DIRS)]

    def radii(z):
        out = []
        for d in dirs:
            h = bvh.ray_cast(Vector((cx, cy, z)), d, 7.0)
            out.append(h[3] if h[0] else None)
        return out

    # Highest level at which the cup still holds water all the way round = the lowest point of the rim.
    spill = None
    z = top
    while z > floor + 0.1:
        if all(r is not None for r in radii(z)):
            spill = z
            break
        z -= 0.04
    if spill is None:
        spill = floor + (top - floor) * 0.5
    level = spill - 0.14
    rs = [r if r is not None else 2.5 for r in radii(level)]
    median = sorted(rs)[len(rs) // 2]
    rs = [r if r > 0.5 * median else median for r in rs]        # ignore rays that clip a bump near the centre
    print(f"fungus pool: floor {floor:.2f}, spill {spill:.2f}, level {level:.2f}, radius {min(rs):.2f}..{max(rs):.2f}")

    # Water surface: a fan that stops just short of the inner wall. The wall is thin near the low side of the
    # rim, so reaching into it would let the water poke out through the outside of the cap.
    verts = [(cx, cy, level)] + [(cx + d.x * (r - 0.03), cy + d.y * (r - 0.03), level) for d, r in zip(dirs, rs)]
    faces = [(0, 1 + i, 1 + (i + 1) % DIRS) for i in range(DIRS)]
    pm = bpy.data.meshes.new("Pool")
    pm.from_pydata(verts, [], faces)
    pm.materials.append(bpy.data.materials.new("pool"))
    pool = bpy.data.objects.new("Pool", pm)
    bpy.context.scene.collection.objects.link(pool)

    # Waterfalls. Each one is a ribbon built from the fungus's real shape: it starts in the pool, hugs the top of
    # the rim, and drops from beyond the widest point of the cap in that direction, so it can never pass
    # through the mushroom. They pour where the rim is lowest, which is where the pool would overflow.
    escape_height = [None] * DIRS
    for i, d in enumerate(dirs):
        zz = spill
        while zz < top + 0.05:
            if not bvh.ray_cast(Vector((cx, cy, zz)), d, 7.0)[0]:
                escape_height[i] = zz
                break
            zz += 0.03
    order = sorted(range(DIRS), key=lambda i: escape_height[i] if escape_height[i] is not None else 99)
    chosen = []
    for i in order:
        if all(min((i - j) % DIRS, (j - i) % DIRS) >= DIRS // 6 for j in chosen):
            chosen.append(i)
        if len(chosen) == 3:
            break

    def top_surface(r, ang):
        """Height of the first surface under a point high above (cx, cy) + r in direction ang, or None."""
        p = Vector((cx + math.cos(ang) * r, cy + math.sin(ang) * r, top + 1.0))
        hit = bvh.ray_cast(p, Vector((0, 0, -1)))
        return hit[0].z if hit[0] else None

    def outer_extent(ang):
        """Largest radius the fungus reaches in this direction at any height above the roots."""
        d = Vector((math.cos(ang), math.sin(ang), 0))
        best, z = 0.0, 1.5
        while z < top + 0.05:
            hit = bvh.ray_cast(Vector((cx, cy, z)) + d * 12.0, -d, 12.0)
            if hit[0]:
                best = max(best, (Vector((hit[0].x, hit[0].y, 0)) - Vector((cx, cy, 0))).length)
            z += 0.05
        return best

    extras = [pool]
    wf_mat = bpy.data.materials.new("waterfall")
    COLS, WIDTH, LIFT, BOTTOM = 7, 1.15, 0.07, -0.7
    for n, i in enumerate(chosen):
        centre_ang = i / DIRS * 2 * math.pi
        rim_r = outer_extent(centre_ang)
        half = WIDTH / 2 / max(rim_r, 1.0)                      # angular half-width of the ribbon
        columns = []
        for c in range(COLS):
            ang = centre_ang + (c / (COLS - 1) * 2 - 1) * half
            # Walk outward along the top surface from just inside the pool edge to the edge of the cap.
            path, r, started, last_z = [], 0.3, False, level
            edge_r = None
            while r < 9.0:
                z = top_surface(r, ang)
                if z is None or z < level - 0.6:                # past the edge of the cap
                    if started:
                        edge_r = r
                        break
                elif z >= level - 0.02:                         # on the wall or rim, at or above the water
                    if not started:
                        path.append((max(r - 0.3, 0.05), level + LIFT))
                        started = True
                    path.append((r, z + LIFT))
                    last_z = z + LIFT
                r += 0.04
            if edge_r is None:
                edge_r = r
            fall_r = max(edge_r, outer_extent(ang)) + 0.09      # clear of everything below the rim too
            path.append((fall_r - 0.03, last_z - 0.01))
            path.append((fall_r, last_z - 0.12))
            for k in range(1, 7):
                path.append((fall_r, last_z - 0.12 + (BOTTOM - last_z + 0.12) * k / 6))
            columns.append((ang, path))

        # Resample every column to the same number of rows by distance along its path.
        ROWS = 26
        verts, uvs = [], []
        for ci, (ang, path) in enumerate(columns):
            seg = [0.0]
            for (r0, z0), (r1, z1) in zip(path, path[1:]):
                seg.append(seg[-1] + math.hypot(r1 - r0, z1 - z0))
            total = seg[-1]
            for row in range(ROWS):
                t = row / (ROWS - 1) * total
                k = max(j for j in range(len(seg)) if seg[j] <= t + 1e-9)
                k = min(k, len(path) - 2)
                f = 0.0 if seg[k + 1] == seg[k] else (t - seg[k]) / (seg[k + 1] - seg[k])
                r = path[k][0] + (path[k + 1][0] - path[k][0]) * f
                z = path[k][1] + (path[k + 1][1] - path[k][1]) * f
                verts.append((cx + math.cos(ang) * r, cy + math.sin(ang) * r, z))
                uvs.append((ci / (COLS - 1), 1.0 - row / (ROWS - 1)))
        faces = []
        for ci in range(COLS - 1):
            for row in range(ROWS - 1):
                a0 = ci * ROWS + row
                faces.append((a0, a0 + ROWS, a0 + ROWS + 1, a0 + 1))
        fm = bpy.data.meshes.new(f"Fall{n}")
        fm.from_pydata(verts, [], faces)
        uvl = fm.uv_layers.new(name="UVMap")
        for loop in fm.loops:
            uvl.data[loop.index].uv = uvs[loop.vertex_index]
        fm.materials.append(wf_mat)
        fall = bpy.data.objects.new(f"Fall{n}", fm)
        bpy.context.scene.collection.objects.link(fall)
        extras.append(fall)

        # Marker where the water lands, for the splash.
        mid_ang, mid_path = columns[COLS // 2]
        land_r = mid_path[-1][0]
        e = bpy.data.objects.new(f"Spout{n}", None)
        e.location = (cx + math.cos(mid_ang) * land_r, cy + math.sin(mid_ang) * land_r, 0.0)
        bpy.context.scene.collection.objects.link(e)
        extras.append(e)
        print(f"  fall {n}: angle {math.degrees(centre_ang):.0f}, rim radius {rim_r:.2f}, drops at radius {land_r:.2f}")
    bm.free()
    return extras


names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(STATIC)
for name in names:
    size, axis = STATIC[name]
    lt.reset()
    obj = lt.import_glb(name)
    lt.normalize(obj, size, axis)
    lt.save_textures(obj, name)
    lo, hi = lt.bounds(obj)
    print(name, "size", tuple(round(v, 2) for v in hi - lo))
    extras = add_pool(obj) if name == "fungus" else []
    lt.export_fbx(name, [obj] + extras)
    span = max(hi - lo) * 1.1
    lt.ortho_render(os.path.join(lt.SHOTS, f"static_{name}_FRONT.png"), (0, 0, (hi.z + lo.z) / 2), span, 'FRONT', 420)
    lt.ortho_render(os.path.join(lt.SHOTS, f"static_{name}_TOP.png"), (0, 0, 0), span, 'TOP', 420)
    if name == "fungus":
        for k, direction in enumerate([(1, -1, 0.9), (-1, -1, 0.25), (1, 1, 0.25), (-1, 1, 0.5)]):
            lt.persp_render(os.path.join(lt.SHOTS, f"static_fungus_PERSP{k}.png"), (0, 0, 3.2), 17, direction, (640, 480))
