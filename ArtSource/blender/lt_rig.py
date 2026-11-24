"""Shared rigging helpers for creatures that are not two-legged (rig_enemy.py keeps its own copies):
build an armature from a joint table, skin a mesh to it, and key rotations given in rest-pose armature axes.
"""
import bpy, math
from mathutils import Vector, Euler, Matrix


def build_armature(BONES):
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
    # Anything that was held only by the root (small loose parts such as claws) would now be left behind.
    loose = [v.index for v in mesh.data.vertices if not v.groups or sum(g.weight for g in v.groups) < 1e-4]
    if loose:
        fallback_weights(mesh, arm, loose)
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


# How many frames each part of the body trails the hips. Nothing in a living thing moves all at once: the hips
# lead, the chest follows, then the arms, and the hands and head arrive last. Every clip gets this for free.
DELAYS = (("hand", 2.4), ("forearm", 1.6), ("upper_arm", 0.8), ("head", 1.6), ("neck", 1.2), ("chest", 0.8), ("spine", 0.4),
          ("toe", 0.9), ("foot", 0.6), ("shin", 0.3), ("tail2", 2.2), ("tail1", 1.1), ("cannon", 1.0))
LEAD = 1.0      # frames by which the whole pattern is advanced, so the hands land close to the frame they were keyed on


def delay_of(bone):
    for prefix, d in DELAYS:
        if bone.startswith(prefix):
            return d - LEAD
    return -LEAD if bone in ("hips", "root") else 0.0


class Animator:
    """Records poses, then bakes a key on every frame.

    Poses are bone rotations given as (rx, ry, rz) degrees about the rest-pose armature axes:
    +rx pitches a limb backwards / leans the spine forward, +rz turns left, +ry rolls to the right side down.
    Between poses the motion follows a smooth curve through them, unless a pose names how it is arrived at:
    ease="in" accelerates into it (a blow landing), "out" settles into it, "io" eases both ends, "lin" is linear.
    Each bone samples the curve a little later than its parent (see DELAYS), which gives overlap and follow-through."""

    def __init__(self, arm, anim_scale=1.0):
        self.arm = arm
        self.anim_scale = anim_scale
        self.rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}
        self.keys = None
        self.limb_scale = {}
        arm.animation_data_create()
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'

    def begin(self, name, length, loop=False, overlap=1.0):
        self.finish()
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        self.arm.animation_data.action = act
        self.keys, self.length, self.loop, self.overlap = [], length, loop, overlap
        return act

    def key(self, frame, pose, hips_z=0.0, hips_y=0.0, hips_x=0.0, ease="smooth"):
        full = {}
        for name, r in pose.items():
            if name.endswith(".LR"):                       # symmetric shorthand
                full[name[:-3] + ".L"] = r
                full[name[:-3] + ".R"] = (r[0], -r[1], -r[2])
            else:
                full[name] = r
        for prefix, k in self.limb_scale.items():           # e.g. a robed figure whose legs must barely move
            for name in full:
                if name.startswith(prefix): full[name] = tuple(v * k for v in full[name])
        full["__hips"] = (hips_x, hips_y, hips_z)
        self.keys.append((float(frame), full, ease))

    def _sample(self, name, t):
        keys, n = self.keys, len(self.keys)
        if self.loop:
            t = t % self.length
        t = max(keys[0][0], min(keys[-1][0], t))
        i = 0
        while i < n - 2 and t > keys[i + 1][0]:
            i += 1
        zero = (0.0, 0.0, 0.0)

        def val(k): return Vector(keys[k][1].get(name, zero))

        def neighbour(k):
            # The key before the first / after the last: wraps round for a loop, otherwise repeats the end pose.
            if 0 <= k < n: return keys[k][0], val(k)
            if not self.loop: return (keys[0][0], val(0)) if k < 0 else (keys[-1][0], val(n - 1))
            return (keys[n - 2][0] - self.length, val(n - 2)) if k < 0 else (keys[1][0] + self.length, val(1))

        (ta, a), (tb, b) = (keys[i][0], val(i)), (keys[i + 1][0], val(i + 1))
        k = (t - ta) / (tb - ta) if tb > ta else 0.0
        ease = keys[i + 1][2]
        if ease == "lin": return a.lerp(b, k)
        if ease == "in": return a.lerp(b, k ** 2.3)
        if ease == "out": return a.lerp(b, 1 - (1 - k) ** 2.3)
        if ease == "io": return a.lerp(b, k * k * (3 - 2 * k))
        # Smooth: a Hermite curve whose slope at each pose comes from the poses either side of it.
        (tp, p), (tn, q) = neighbour(i - 1), neighbour(i + 2)
        ma = (b - p) / (tb - tp) * (tb - ta) if tb > tp else Vector(zero)
        mb = (q - a) / (tn - ta) * (tb - ta) if tn > ta else Vector(zero)
        if keys[i][2] in ("in", "out", "io"): ma = Vector(zero)          # leave a struck pose without sliding through it
        k2, k3 = k * k, k * k * k
        return a * (2 * k3 - 3 * k2 + 1) + ma * (k3 - 2 * k2 + k) + b * (-2 * k3 + 3 * k2) + mb * (k3 - k2)

    def finish(self):
        """Bakes the recorded poses of the current clip onto every frame."""
        if not self.keys:
            return
        self.keys.sort(key=lambda k: k[0])
        last = {}
        for f in range(self.length + 1):
            for pb in self.arm.pose.bones:
                r = self._sample(pb.name, f - delay_of(pb.name) * self.overlap)
                R = Euler((math.radians(r[0]), math.radians(r[1]), math.radians(r[2])), 'XYZ').to_matrix()
                M = self.rest[pb.name]
                q = (M.inverted() @ R @ M).to_quaternion()
                if pb.name in last and last[pb.name].dot(q) < 0:
                    q.negate()
                last[pb.name] = q.copy()
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=f)
                if pb.name == "hips":
                    pb.location = M.inverted() @ (self._sample("__hips", f + LEAD * self.overlap) * self.anim_scale)
                    pb.keyframe_insert("location", frame=f)
        self.keys = None


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


def set_action(arm, name):
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]


