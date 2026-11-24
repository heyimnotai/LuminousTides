using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// The demo level, "The Breach": one handcrafted route, about five minutes long, built to be shown.
/// The layout is set out below as plain masses (a footprint and a height each), at real jump distances. Each mass is
/// then built as weathered rock with broken, uneven faces and a level top, so what you can stand on is exactly the
/// footprint given here. Enemies stand where the layout names them; plants, banners and landmarks dress each stretch.
///
/// South to north, in metres from the level's origin (x east, z north, y up; the sea is at 0):
///   z   0        the drop-in ledge, 26 m up, with the whole level in view
///   z  16- 63    the sea stacks: six pillars stepping down 23 -> 9 m, each a jump from the last
///   z  72-112    the shelf (6 m): the first fight; a dropship sets reinforcements down here
///   z 114-148    the climb: a canyon of ledges, each a jump and a mantle, up to 20 m
///   z 148-186    the gate court (20 m): a Captain and his guard, and a Strider that walks in through the ring gate
///   then east    the ridge: a gap, three stacks to hop and a tunnel to slide through, the boss arena in view below
///   x 108-154    the relay yard (23 m, upper tier 26.6 m): the big fight, with a second dropship
///   then south   the bridge, out over the water
///   x 131, z 78  the boss arena (23 m): the Corsair Queen, her ship hanging in the sky behind her.
///                It sits across the bay from the drop-in ledge, about 150 m away, in view from the first step.
/// About 400 m on foot.
public static partial class LevelBuilder
{
    public static readonly Vector3 DemoOrigin = new Vector3(620f, 0f, -40f);
    static Transform demoSpawn, demoObjective;
    static Material demoMass;

    static Vector3 DemoPoint(float x, float y, float z) => DemoOrigin + new Vector3(x, y, z);

    static Mesh demoMeshes;
    static int demoBeat, demoSeed;
    static readonly List<(string unit, Vector3 pos, int beat)> demoPlanned = new List<(string, Vector3, int)>();
    static readonly List<(float x, float z, float sx, float sz, float top, bool scenery)> demoTops = new List<(float, float, float, float, float, bool)>();

    /// A lump of rock: a prism with a level top exactly `sizeX` by `sizeZ` (or round), whose sides bulge, step and
    /// crumble on the way down. Local origin is the middle of the top; it reaches `height` below that.
    static Mesh RockMesh(float sizeX, float sizeZ, float height, bool round, float rough)
    {
        float perimeter = round ? Mathf.PI * sizeX : 2f * (sizeX + sizeZ);
        int around = Mathf.Clamp(Mathf.RoundToInt(perimeter / 1.3f), 10, 96), rings = Mathf.Clamp(Mathf.RoundToInt(height / 1.6f), 3, 28);
        float seed = (demoSeed++ * 7.31f) % 97f;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings, y = -v * height;
            for (int a = 0; a < around; a++)
            {
                float u = a / (float)around, ang = u * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                // The outline: a circle, or a rectangle with slightly rounded corners.
                float reach = round ? sizeX * 0.5f : 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(dir.x) / (sizeX * 0.5f), 8f) + Mathf.Pow(Mathf.Abs(dir.y) / (sizeZ * 0.5f), 8f), 0.125f);
                // Below the lip the face wanders in and out, in bands like strata, and flares toward the base.
                float n = Mathf.PerlinNoise(u * around * 0.22f + seed, v * rings * 0.55f + seed * 0.7f) - 0.5f;
                float strata = Mathf.PerlinNoise(seed + 3.1f, Mathf.Floor(v * rings * 0.5f) * 0.83f + seed) - 0.45f;
                float lip = Mathf.Clamp01(v * rings / 1.2f);                        // nothing moves at the very top
                float off = (n * 1.5f + strata * 0.9f) * rough * lip + v * v * rough * 0.9f;
                verts.Add(new Vector3(dir.x * (reach + off), y + (r > 0 && r < rings ? n * 0.4f * rough : 0f), dir.y * (reach + off)));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int a = 0; a < around; a++)
            {
                int a1 = (a + 1) % around, p = r * around + a, q = r * around + a1, p2 = p + around, q2 = q + around;
                tris.Add(p); tris.Add(q); tris.Add(p2); tris.Add(q); tris.Add(q2); tris.Add(p2);
            }
        int centre = verts.Count;
        verts.Add(Vector3.zero);
        int cap = verts.Count;
        for (int a = 0; a < around; a++) verts.Add(verts[a]);          // the top has its own vertices, so its edge stays crisp
        for (int a = 0; a < around; a++) { tris.Add(centre); tris.Add(cap + (a + 1) % around); tris.Add(cap + a); }
        var m = new Mesh { name = "DemoRock" + demoSeed, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        if (demoMeshes == null) { demoMeshes = m; UnityEditor.AssetDatabase.CreateAsset(m, Gen + "/DemoRocks.asset"); }
        else UnityEditor.AssetDatabase.AddObjectToAsset(m, demoMeshes);
        return m;
    }

    static GameObject Rock(Transform parent, string name, Vector3 topCentre, float sizeX, float sizeZ, float height, bool round, float rough, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent, false);
        go.transform.position = topCentre;
        var mesh = RockMesh(sizeX, sizeZ, height, round, rough);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshCollider>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// A raised piece of the landscape with a level top: a footprint and a height. The terrain is shaped from these
    /// (see DemoH), so they are part of one continuous landmass, not separate blocks. `scenery` ones are never
    /// walked on and get trees.
    static GameObject Mass(Transform parent, string name, float x, float z, float sizeX, float sizeZ, float top, float bottom = -6f, bool scenery = false)
    {
        demoTops.Add((x, z, sizeX, sizeZ, top, scenery));
        demoShapes.Add((x, z, sizeX * 0.5f, sizeZ * 0.5f, top, false, scenery));
        return null;
    }

    /// A sea stack: a round tower of rock standing in the shallows.
    static GameObject Pillar(Transform parent, string name, float x, float z, float radius, float top, float bottom = -6f, bool scenery = false)
    {
        demoTops.Add((x, z, radius * 1.4f, radius * 1.4f, top, scenery));
        if (!scenery && parent != null && HasModel("citadel_stack"))
        {
            // The model is 9.6 m square and 24 m tall with a paved top. Scale it so the top is the size and height the
            // layout asks for, with its foot sunk a little into the flats.
            var stack = Model("citadel_stack", parent);
            float wide = (radius * 2f + 1.0f) / 9.6f;
            stack.name = name;
            stack.transform.position = DemoPoint(x, -1.5f, z);
            stack.transform.rotation = Quaternion.Euler(0, 90f * rng.Next(0, 4) + Rand(-8f, 8f), 0);
            stack.transform.localScale = new Vector3(wide, (top + 1.5f) / 24f, wide);
            AddMeshColliders(stack);
            return stack;
        }
        demoShapes.Add((x, z, radius, radius, top, true, scenery));
        return null;
    }

    static readonly List<(float x, float z, float hx, float hz, float top, bool round, bool scenery)> demoShapes = new List<(float, float, float, float, float, bool, bool)>();

    /// Ground height of the demo level, in its own coordinates. One landmass: low ground and tidal flats under
    /// everything, the layout's masses rising out of it as cliffs with level tops, scree banked against the foot of
    /// the tall ones, and beaches running down into the sea.
    static float DemoH(float x, float z)
    {
        if (demoShapes.Count == 0) return -5f;
        float h = -5f;
        // The land the route stands on. South: tidal flats, barely above the water, with the sea stacks rising out of them.
        h = Strip(h, x, z, 0f, -14f, 0f, 72f, 70f, 0.45f, 0.5f, 10f);
        // North: higher, rougher ground under the shelf, the climb and the gate court, and along the north shore.
        h = Strip(h, x, z, 0f, 72f, 0f, 192f, 74f, 1.6f, 3.2f, 9f);
        h = Strip(h, x, z, 0f, 174f, 140f, 172f, 54f, 3.0f, 3.4f, 9f);
        // East: the headland the relay yard stands on, and a spit of rock under the boss arena.
        h = Strip(h, x, z, 131f, 192f, 131f, 138f, 62f, 3.2f, 2.6f, 9f);
        h = Strip(h, x, z, 131f, 100f, 131f, 50f, 70f, 1.2f, 1.4f, 10f);
        if (h > -4.9f) h += (Mathf.PerlinNoise(x * 0.09f + 7f, z * 0.09f + 3f) - 0.5f) * 1.2f * Mathf.Clamp01((h + 0.2f) / 1.5f);     // dunes and hummocks, dying out at the waterline

        foreach (var m in demoShapes)
        {
            float dx = Mathf.Abs(x - m.x), dz = Mathf.Abs(z - m.z);
            if (dx > m.hx + 22f || dz > m.hz + 22f) continue;
            float wob = (Mathf.PerlinNoise(x * 0.31f + m.x, z * 0.31f + m.z) - 0.5f) * (m.scenery ? 2.4f : 0.9f);
            // Distance outside the footprint (negative inside). Walkable tops keep their true size; scenery wanders more.
            float d;
            if (m.round) d = Mathf.Sqrt((x - m.x) * (x - m.x) + (z - m.z) * (z - m.z)) - m.hx;
            else
            {
                float ox = Mathf.Max(dx - (m.hx - 1.2f), 0f), oz = Mathf.Max(dz - (m.hz - 1.2f), 0f);
                d = Mathf.Sqrt(ox * ox + oz * oz) - 1.2f + Mathf.Min(0f, Mathf.Max(dx - m.hx, dz - m.hz) + 1.2f) * 0f;
            }
            d += wob * Mathf.Clamp01((d + 0.6f) / 1.2f);                   // the edge itself stays put; the cliff below it is ragged
            float rise = m.top - Mathf.Max(h, 0f);
            // Scree: tall masses have a bank of fallen rock against their foot.
            if (rise > 7f && d > 0f && d < 16f)
            {
                float bank = Mathf.Pow(1f - d / 16f, 2.2f) * Mathf.Min(rise * 0.3f, 7f) * (0.75f + 0.5f * Mathf.PerlinNoise(x * 0.2f + 11f, z * 0.2f + 5f));
                h = Mathf.Max(h, Mathf.Max(h, -0.5f) + bank);
            }
            float cliff = m.scenery ? 2.6f : 1.5f;
            float k = 1f - Mathf.Clamp01(d / cliff);
            if (k <= 0f) continue;
            float surface = m.top + (m.scenery ? (Mathf.PerlinNoise(x * 0.12f + 5f, z * 0.12f + 9f) - 0.5f) * 2.2f : 0f);
            h = Mathf.Max(h, Mathf.Lerp(h, surface, Smooth(k)));
        }
        h += 0.14f * (float)System.Math.Tanh(h / 0.07f);
        return Mathf.Max(h, -5f);
    }

    /// What the demo level's ground is made of at a point: r sand, g moss, b slate (none = the blue tidal rock).
    static Color DemoBiome(float x, float z)
    {
        float h = DemoH(x, z);
        float slope = Mathf.Max(Mathf.Abs(DemoH(x + 0.8f, z) - h), Mathf.Abs(DemoH(x, z + 0.8f) - h)) / 0.8f;
        float flat = 1f - Smooth(Mathf.Clamp01((slope - 0.25f) / 0.5f));
        float patch = Mathf.PerlinNoise(x * 0.045f + 20f, z * 0.045f + 40f);
        float sand = Smooth(Mathf.Clamp01((1.7f - h) / 1.1f)) * flat;                                           // beaches and the tidal flats
        float slate = Smooth(Mathf.Clamp01((h - 27f) / 6f));                                                    // the highest crags, with snow
        float moss = Smooth(Mathf.Clamp01((h - 3.5f) / 2f)) * flat * Smooth(Mathf.Clamp01((patch - 0.38f) / 0.25f)) * (1f - slate);   // green on level tops, in drifts
        // The Sunken Citadel is pale sandstone throughout: the stone biome everywhere the moss is not.
        return new Color(1f - moss, moss, 0f, 1f);
    }

    /// Something to fight from behind: a slab of paler stone.
    static void Cover(Transform parent, float x, float z, float floor, float sizeX, float sizeZ, float height = 1.3f, float yaw = 0f)
    {
        float length = Mathf.Max(sizeX, sizeZ);
        if (length >= 3.5f && HasModel("citadel_wall"))
        {
            var wall = Model("citadel_wall", parent);          // 13 m long, 4.6 m tall and deep
            wall.name = "CoverWall";
            wall.transform.position = DemoPoint(x, floor - 0.15f, z);
            wall.transform.rotation = Quaternion.Euler(0, yaw + (sizeZ > sizeX ? 90f : 0f) + (rng.Next(0, 2) * 180f), 0);
            wall.transform.localScale = new Vector3(length / 13f * 1.1f, (height + 0.25f) / 4.6f, 0.36f);
            AddMeshColliders(wall);
            return;
        }
        var go = Rock(parent, "Cover", DemoPoint(x, floor + height, z), sizeX, sizeZ, height + 0.4f, false, 0.12f, BiomeRock(1));
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
    }

    /// Notes an enemy to be placed here once the level is built and its nav mesh baked.
    static void Marker(Transform parent, string label, float x, float z, float floor, float height = 2f, bool flying = false)
    {
        string unit = label.Split(' ')[0].ToLowerInvariant().Replace("corsair", "queen");
        demoPlanned.Add((unit, DemoPoint(x, floor, z), demoBeat));
    }

    static void BuildDemo(Transform world)
    {
        var root = Group("DemoLevel", world);
        demoMeshes = null; demoSeed = 1; demoBeat = 1;
        demoPlanned.Clear(); demoTops.Clear(); demoShapes.Clear();
        for (int b = 0; b < biomeRock.Length; b++) biomeRock[b] = null;      // this runs before the terrain is built, so it owns the reset
        worldMats.Clear();
        demoMass = rock;
        var t = root.transform;

        // ---- 1. The drop-in: a ledge on a sea cliff, the cliff rising behind you, everything else below and ahead.
        Mass(t, "StartLedge", 0, 0, 18, 16, 26);
        Mass(t, "CliffBehind", 0, -15, 70, 14, 44, -6f, true);
        Mass(t, "CliffWingW", -30, -4, 14, 22, 36, -6f, true);
        Mass(t, "CliffWingE", 26, -12, 14, 10, 18);        // kept low: the view east across the bay to the boss arena stays open
        demoSpawn = Group("DemoSpawn", t).transform;
        demoSpawn.position = DemoPoint(0, 26.2f, -2f);

        // ---- 2. The sea stacks: each one lower than the last. The gaps are 5 to 7 m; falling means the sea.
        Pillar(t, "Stack1", 6, 17, 3.4f, 23);
        Pillar(t, "Stack2", -4, 26, 3.2f, 20);
        Pillar(t, "Stack3", 6, 35, 3.2f, 17);
        Pillar(t, "Stack4", -5, 44, 3.6f, 14);
        Pillar(t, "Stack5", 4, 54, 3.2f, 11.5f);
        Pillar(t, "Stack6", -3, 64, 3.8f, 9);
        Pillar(t, "StackFarW", -22, 40, 5f, 30, -6f, true);          // tall stacks either side, to frame the descent
        Pillar(t, "StackFarE", 24, 30, 4f, 19, -6f, true);
        Pillar(t, "StackFarW2", -30, 66, 6f, 34, -6f, true);
        Marker(t, "RAIDER", -5, 44, 14, 2.2f); Marker(t, "RUNT", -3, 64, 9, 1.4f);
        Marker(t, "KITE", 12, 40, 16, 1f, true); Marker(t, "KITE", -12, 52, 13, 1f, true);

        demoBeat = 2;
        // ---- 3. The shelf: the first fight. High ground to the north-east, a flank to the west, cover in the middle.
        Mass(t, "Shelf", 0, 92, 50, 40, 6);
        Mass(t, "ShelfDeck", 17, 104, 12, 12, 9);                                   // the Raider's perch
        var ramp = Prim(PrimitiveType.Cube, "ShelfRamp", t, DemoPoint(17, 7.4f, 94.4f), new Vector3(5f, 0.4f, 8f), BiomeRock(3));
        ramp.transform.rotation = Quaternion.Euler(-22f, 0, 0);
        Mass(t, "ShelfStepW", -19, 88, 8, 10, 7.6f);                                // a step to fight from, or to flank along
        Cover(t, -6, 84, 6, 4.5f, 1.2f); Cover(t, 7, 86, 6, 1.2f, 4.5f); Cover(t, -2, 96, 6, 5f, 1.2f, 1.3f, 20f);
        Cover(t, -13, 100, 6, 1.2f, 4f); Cover(t, 6, 102, 6, 4f, 1.2f); Cover(t, -4, 108, 6, 1.4f, 1.4f, 2.6f);
        Marker(t, "RUNT", -4, 90, 6, 1.4f); Marker(t, "RUNT", 3, 93, 6, 1.4f); Marker(t, "RUNT", 9, 98, 6, 1.4f);
        Marker(t, "RAIDER", 17, 105, 9, 2.2f); Marker(t, "RAIDER", -8, 104, 6, 2.2f);
        Marker(t, "HOUND", -19, 90, 7.6f, 1.2f);

        demoBeat = 3;
        // ---- 4. The climb: a canyon cut into the cliff, ledges up alternate walls. Each is a jump and a mantle.
        Mass(t, "CanyonWallW", -12, 131, 12, 36, 36, -6f, true);
        Mass(t, "CanyonWallE", 12, 131, 12, 36, 34, -6f, true);
        Mass(t, "CanyonFloor", 0, 130, 12, 36, 6);
        Mass(t, "Ledge1", -3, 119, 6, 6, 8.2f);
        Mass(t, "Ledge2", 3, 125, 6, 6, 10.6f);
        Mass(t, "Ledge3", -3, 131, 6, 6, 13.0f);
        Mass(t, "Ledge4", 3, 137, 6, 6, 15.4f);
        Mass(t, "Ledge5", -3, 143, 6, 6, 17.8f);
        Marker(t, "KITE", 0, 128, 14, 1f, true); Marker(t, "KITE", 0, 138, 20, 1f, true);

        demoBeat = 4;
        // ---- 5. The gate court: a Captain and his guard hold it; a Strider comes in through the gate to the north.
        Mass(t, "Court", 0, 167, 46, 38, 20);
        Mass(t, "CourtFlankW", -29, 150, 12, 40, 28, -6f, true);       // the cliff carries on either side of the canyon mouth
        Mass(t, "CourtFlankE", 29, 146, 12, 30, 30, -6f, true);
        Mass(t, "CourtTerrace", -15, 176, 12, 12, 22.4f);
        Cover(t, -5, 158, 20, 5f, 1.2f); Cover(t, 8, 160, 20, 1.2f, 5f); Cover(t, 0, 168, 20, 1.6f, 1.6f, 3f);
        Cover(t, 13, 174, 20, 5f, 1.2f, 1.3f, -25f); Cover(t, -9, 170, 20, 1.2f, 4f);
        Marker(t, "CAPTAIN", 0, 173, 20, 2.7f); Marker(t, "RAIDER", -15, 177, 22.4f, 2.2f); Marker(t, "RAIDER", 12, 162, 20, 2.2f);
        Marker(t, "HOUND", -8, 162, 20, 1.2f); Marker(t, "HOUND", 6, 164, 20, 1.2f);
        Marker(t, "STRIDER", 0, 180, 20, 5.8f);
        var gate = Landmark(t, "stormgate", DemoPoint(0, 19.6f, 188), 180f, 1.1f, true);
        if (gate == null) { Mass(t, "GatePostW", -9, 188, 5, 5, 44); Mass(t, "GatePostE", 9, 188, 5, 5, 44); }

        demoBeat = 5;
        // ---- 6. The ridge: the route turns east along the cliff top. Broken ground: a gap, three stacks to hop,
        // and a low tunnel to slide through. Kites work it over the whole way, and the boss arena is in view below.
        Mass(t, "Ridge1", 32, 167, 18, 7, 20);
        Mass(t, "Ridge2", 53, 165, 12, 6, 18.5f);
        Pillar(t, "RidgeStack1", 66, 168, 2.8f, 19.5f);
        Pillar(t, "RidgeStack2", 74, 163, 2.8f, 21);
        Pillar(t, "RidgeStack3", 82, 168, 3.0f, 22.5f);
        Mass(t, "RidgeTunnelFloor", 98, 166, 18, 8, 23);
        Prim(PrimitiveType.Cube, "TunnelRoof", t, DemoPoint(98, 25.9f, 166), new Vector3(13f, 2.9f, 8f), demoMass);     // 1.45 m of headroom
        Prim(PrimitiveType.Cube, "TunnelWallN", t, DemoPoint(98, 23.73f, 169.7f), new Vector3(13f, 1.45f, 0.6f), demoMass);
        Prim(PrimitiveType.Cube, "TunnelWallS", t, DemoPoint(98, 23.73f, 162.3f), new Vector3(13f, 1.45f, 0.6f), demoMass);
        Marker(t, "KITE", 50, 172, 22, 1f, true); Marker(t, "KITE", 72, 158, 23, 1f, true); Marker(t, "KITE", 86, 174, 25, 1f, true);
        Marker(t, "RUNT", 53, 165, 18.5f, 1.4f); Marker(t, "RUNT", 104, 166, 23, 1.4f);

        demoBeat = 6;
        // ---- 7. The relay yard: the big fight before the boss. Two levels, raiders holding the upper one, a second
        // dropship. Steps either side lead up to the tier.
        Mass(t, "Yard", 131, 164, 46, 44, 23);
        Mass(t, "YardTier", 131, 180, 46, 12, 26.6f);
        Cover(t, 122, 152, 23, 5f, 1.2f); Cover(t, 134, 156, 23, 1.2f, 5f); Cover(t, 141, 163, 23, 5f, 1.2f, 1.3f, 30f);
        Cover(t, 118, 164, 23, 1.6f, 1.6f, 3f); Cover(t, 128, 168, 23, 5f, 1.2f); Cover(t, 124, 180, 26.6f, 5f, 1.2f); Cover(t, 139, 180, 26.6f, 5f, 1.2f);
        Marker(t, "RAIDER", 122, 181, 26.6f, 2.2f); Marker(t, "RAIDER", 131, 182, 26.6f, 2.2f); Marker(t, "RAIDER", 141, 181, 26.6f, 2.2f);
        Marker(t, "RUNT", 124, 156, 23, 1.4f); Marker(t, "RUNT", 130, 160, 23, 1.4f); Marker(t, "RUNT", 138, 158, 23, 1.4f); Marker(t, "RUNT", 144, 166, 23, 1.4f);
        Marker(t, "HOUND", 116, 160, 23, 1.2f); Marker(t, "HOUND", 147, 158, 23, 1.2f);
        Marker(t, "KITE", 126, 170, 27, 1f, true); Marker(t, "KITE", 140, 150, 27, 1f, true);

        demoBeat = 7;
        // ---- 8. The bridge runs south, out over the water, to the boss arena: across the bay from where you started.
        Mass(t, "Bridge", 131, 124, 6, 36, 23);           // the walkway; the bridge model stands round it
        Pillar(t, "BossArena", 131, 78, 28f, 23);
        foreach (var p in new[] { new Vector2(112, 96), new Vector2(150, 96), new Vector2(108, 64), new Vector2(154, 64) })
        {
            var stone = Landmark(t, HasModel("citadel_stack") ? "citadel_stack" : "spire", DemoPoint(p.x, 22.2f, p.y), Rand(0, 360), 1f, true);         // four standing stones ring the arena
            if (HasModel("citadel_stack")) stone.transform.localScale = new Vector3(0.32f, 0.5f, 0.32f);
        }
        Mass(t, "Dais", 131, 58, 8, 7, 24.5f);
        Marker(t, "QUEEN", 131, 58, 24.5f, 3.6f); Marker(t, "RAIDER", 123, 68, 23, 2.2f); Marker(t, "RAIDER", 139, 68, 23, 2.2f);
        Landmark(t, "varrik_ship", DemoPoint(150, 50, 22), -20f, 1.7f, false);
        demoObjective = Group("DemoObjective", t).transform;
        demoObjective.position = DemoPoint(131, 23, 78);

        // Stand the player on the ground as it was actually built: the cliff behind the ledge spreads forward as it falls.
        demoSpawn.position = DemoPoint(0f, DemoH(0f, 3f) + 0.4f, 3f);
        DressDemo(t);
        MatchCitadelStone(t);

        // ---- The sky: three ships on slow loops, so something is always moving up there.
        if (HasModel("varrik_ship"))
            for (int i = 0; i < 3; i++)
            {
                var skiff = Landmark(t, "varrik_ship", DemoPoint(0, 60, 150), 0, 0.32f, false);
                skiff.name = "SkyTraffic" + i;
                var fly = skiff.AddComponent<SkyTraffic>();
                fly.centre = DemoPoint(i == 1 ? 20 : 80, 62 + i * 16, 100 + i * 30);
                fly.radiusX = 170 + i * 40; fly.radiusZ = 110 + i * 30; fly.speed = 16 + i * 5; fly.phase = i * 0.37f; fly.clockwise = i == 1;
                fly.modelYaw = -90f;
            }
    }

    /// Plants, banners and landmarks over the built layout.
    static void DressDemo(Transform t)
    {
        var flora = Group("DemoFlora", t);
        var citadelGrass = WorldMat("CitadelGrass", new Color(0.46f, 0.62f, 0.28f), 0.2f, new Color(0.02f, 0.06f, 0.02f));
        var props = Group("DemoProps", t);
        // Grass and bulb plants on everything; giant fungi crown the masses nobody walks on.
        foreach (var top in demoTops)
        {
            float area = top.sx * top.sz;
            int tufts = Mathf.Clamp((int)(area / 9f), 2, 60);
            for (int k = 0; k < tufts; k++)
            {
                float x = top.x + Rand(-0.44f, 0.44f) * top.sx, z = top.z + Rand(-0.44f, 0.44f) * top.sz;
                int before = flora.transform.childCount;
                Tuft(flora.transform, DemoPoint(x, top.top - 0.03f, z), Rand(0.6f, 1.5f));
                foreach (var r in flora.transform.GetChild(before).GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = citadelGrass;     // sun-bleached green, not the tidal purple
            }
            for (int k = 0; k < Mathf.Clamp((int)(area / 60f), 0, 8); k++)
            {
                var m = Model("bulbplant", flora.transform);
                if (m == null) break;
                m.transform.position = DemoPoint(top.x + Rand(-0.4f, 0.4f) * top.sx, top.top - 0.05f, top.z + Rand(-0.4f, 0.4f) * top.sz);
                m.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
                m.transform.localScale = Vector3.one * Rand(0.8f, 1.8f);
            }
            if (!top.scenery) continue;
            int trees = Mathf.Clamp((int)(area / 110f), 1, 4);
            for (int k = 0; k < trees; k++)
            {
                float fs = Rand(1.1f, 2.4f) * (area > 400f ? 1.5f : 1f);
                Fungus(flora.transform, DemoPoint(top.x + Rand(-0.3f, 0.3f) * top.sx, top.top - 0.3f * fs, top.z + Rand(-0.3f, 0.3f) * top.sz), fs, false, fs > 2.2f ? 2 : 0);
            }
        }
        // Coral in the shallows round the sea stacks, and fallen boulders along the foot of the cliffs.
        for (int k = 0; k < 120; k++)
        {
            float cx = Rand(-34f, 34f), cz = Rand(-8f, 72f);
            float ch = DemoH(cx, cz);
            if (ch > -0.6f && ch < 0.7f) Coral(flora.transform, DemoPoint(cx, ch, cz), Rand(0.8f, 2.0f));
        }
        var rocks = Group("DemoBoulders", t);
        for (int k = 0; k < 90; k++)
        {
            float bx = Rand(-40f, 165f), bz = Rand(-20f, 195f);
            float bh = DemoH(bx, bz);
            float steep = Mathf.Abs(DemoH(bx + 1.5f, bz) - bh) + Mathf.Abs(DemoH(bx, bz + 1.5f) - bh);
            if (bh < 0.2f || bh > 9f || steep < 0.5f || steep > 3f) continue;          // on the scree banks, not on the level ground or the cliff faces
            var m = Model(HasModel("boulder") ? "boulder" : "rock", rocks.transform);
            if (m == null) break;
            float bs = Rand(0.7f, 2.6f);
            m.transform.position = DemoPoint(bx, bh - 0.25f * bs, bz);
            m.transform.rotation = Quaternion.Euler(Rand(-12, 12), Rand(0, 360), Rand(-12, 12));
            m.transform.localScale = Vector3.one * bs;
            AddMeshColliders(m);
        }
        // Landmarks: an arch over the mouth of the climb, and the Queen's fleet banners wherever the Varrik hold ground.
        if (HasModel("citadel_arch")) Landmark(t, "citadel_arch", DemoPoint(0, 5.6f, 113.5f), 0f, 1.25f, true);      // the way into the climb
        else Landmark(t, "arch", DemoPoint(0, 5.2f, 113), 0f, 1.0f, false);
        // Masonry built up on the cliff behind the start, a colonnade along the back of the ledge, and a second arch framing the first jump.
        if (HasModel("citadel_cliff"))
            foreach (float cx in new[] { -27f, 9f })
                Landmark(t, "citadel_cliff", DemoPoint(cx, 25.2f, -11.5f), 0f, 1.4f, true);
        if (HasModel("citadel_colonnade"))
        {
            Landmark(t, "citadel_colonnade", DemoPoint(0f, 25.8f, -5f), 0f, 0.95f, true);
            Landmark(t, "citadel_colonnade", DemoPoint(-18f, 5.8f, 108f), 90f, 0.8f, true);
            Landmark(t, "citadel_colonnade", DemoPoint(131f, 26.4f, 184f), 0f, 1.1f, true);
        }
        // A colossal statue's head lies in the shallows beside the stacks, and another on the shore under the arena.
        if (HasModel("citadel_head"))
        {
            Landmark(t, "citadel_head", DemoPoint(19f, -1.2f, 44f), 200f, 1.8f, true);
            Landmark(t, "citadel_head", DemoPoint(104f, -0.8f, 96f), 70f, 1.3f, true);
        }
        foreach (var p in new[] { new Vector3(-21, 6, 76), new Vector3(21, 6, 76), new Vector3(-20, 20, 152), new Vector3(20, 20, 152), new Vector3(-20, 20, 183), new Vector3(20, 20, 183),
                                  new Vector3(110, 23, 146), new Vector3(152, 23, 184), new Vector3(110, 26.6f, 184), new Vector3(127.5f, 23, 140), new Vector3(134.5f, 23, 140),
                                  new Vector3(127.5f, 23, 108), new Vector3(134.5f, 23, 108), new Vector3(124, 24.5f, 53), new Vector3(138, 24.5f, 53) })
            Pylon(props.transform, DemoOrigin.x + p.x, DemoOrigin.z + p.z, Rand(0, 360), DemoOrigin.y + p.y);
        // ---- The rest of the kit: the cliffs are faced with masonry, and the citadel's buildings stand along the route.
        // Kit(name, x, top-of-ground y, z, yaw, scale): yaw 0 faces north (+z). Terraces and cliff sections have a flat
        // back, so they stand against a cliff facing outward; `drop` sinks a piece so its top meets a given height.
        GameObject Kit(string name, float x, float y, float z, float yaw, Vector3 scale, bool collide = true)
        {
            if (!HasModel(name)) return null;
            var go = Landmark(t, name, DemoPoint(x, y, z), yaw, 1f, false);
            go.transform.localScale = scale;
            if (collide) AddMeshColliders(go);
            return go;
        }
        Vector3 U(float s) => Vector3.one * s;
        // The starting ledge: faced on three sides, obelisks at its front corners.
        Kit("citadel_terrace", 0, 26f - 20.4f, 8.7f, 0, U(0.85f));
        Kit("citadel_terrace", 9.7f, 26f - 18f, 0, 90, U(0.75f));
        Kit("citadel_terrace", -9.7f, 26f - 18f, 0, -90, U(0.75f));
        Kit("citadel_obelisk", -7.5f, 25.8f, -4f, 0, U(0.5f)); Kit("citadel_obelisk", 7.5f, 25.8f, -4f, 0, U(0.5f));       // behind you, so the view ahead is clear
        // The shelf: an arcaded wall along its seaward face, stairs up to the perch, a tower behind.
        foreach (float sx in new[] { -17f, 0f, 17f }) Kit("citadel_arenawall", sx, -0.2f, 71.6f, 180, U(1.02f));
        Kit("citadel_tower", -22f, 5.8f, 108f, 30, U(0.8f));
        // The climb: the canyon mouth is masonry either side of the arch.
        Kit("citadel_terrace", -12.4f, 5.8f, 112.6f, 180, new Vector3(0.52f, 1.25f, 0.6f));
        Kit("citadel_terrace", 12.4f, 5.8f, 112.6f, 180, new Vector3(0.52f, 1.17f, 0.6f));
        // The gate court: its seaward (east) face, a tower, obelisks by the gate, a colonnade along the west side.
        Kit("citadel_terrace", 23.6f, 20f - 19.2f, 158f, 90, U(0.8f));
        Kit("citadel_terrace", 23.6f, 20f - 19.2f, 177f, 90, U(0.8f));
        Kit("citadel_tower", -19f, 19.8f, 183f, 0, U(0.9f));
        Kit("citadel_obelisk", -9f, 19.8f, 180f, 0, U(0.8f)); Kit("citadel_obelisk", 9f, 19.8f, 180f, 0, U(0.8f));
        Kit("citadel_colonnade", -21f, 19.8f, 164f, 90, U(0.85f));
        // The ridge: a colonnade on the first stretch, a tower past the tunnel.
        Kit("citadel_colonnade", 33f, 19.8f, 169.6f, 0, U(0.75f));
        Kit("citadel_tower", 104f, 22.8f, 173f, 0, U(0.7f));
        // The relay yard: faced to the south and west (the sides the bay sees), stairs up to the tier, a tower, statues.
        Kit("citadel_terrace", 117f, 23f - 21.6f, 141.3f, 180, U(0.9f));
        Kit("citadel_terrace", 145f, 23f - 21.6f, 141.3f, 180, U(0.9f));
        Kit("citadel_terrace", 107.3f, 23f - 21.6f, 153f, -90, U(0.9f));
        Kit("citadel_terrace", 107.3f, 23f - 21.6f, 174.5f, -90, U(0.9f));
        Kit("citadel_stairs", 116f, 22.9f, 170.6f, 180, U(0.66f));
        Kit("citadel_stairs", 146f, 22.9f, 170.6f, 180, U(0.66f));
        Kit("citadel_tower", 150f, 22.8f, 149f, 0, U(1.0f));
        Kit("citadel_statue", 131f, 26.4f, 188f, 180, U(0.7f));
        // The bridge: one long span, its parapets either side of the walkway.
        Kit("citadel_bridge", 131f, 23.85f - 9.04f * 1.2f, 124f, 90, new Vector3(1.2f, 1.2f, 1.0f));
        Kit("citadel_statue", 121.5f, 22.8f, 104.5f, 0, U(0.75f)); Kit("citadel_statue", 140.5f, 22.8f, 104.5f, 0, U(0.75f));
        // The boss arena: a carved floor, the Queen's dais, tusks leaning in round the rim and a low arcaded wall behind them.
        Kit("citadel_dais", 131f, 22.72f, 78f, 0, new Vector3(3.2f, 0.2f, 3.2f));
        Kit("citadel_dais", 131f, 22.6f, 58f, 0, U(1.0f));
        for (int k = 0; k < 10; k++)
        {
            float ang = (k + 0.5f) / 10f * 360f;                          // 0 = north, where the bridge comes in
            if (ang < 30f || ang > 330f) continue;
            float rad = ang * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
            Kit("citadel_tusk", 131f + dir.x * 25.5f, 22.6f, 78f + dir.z * 25.5f, ang + 180f, U(0.8f));
            Kit("citadel_arenawall", 131f + dir.x * 29.2f, 23f - 3.6f, 78f + dir.z * 29.2f, ang + 180f, new Vector3(1.12f, 1.0f, 1.0f));
        }
        // A colossus stands in the bay between the start and the arena: the thing your eye goes to first.
        Kit("citadel_statue", 64f, -3f, 62f, -70, U(2.3f));
        Kit("citadel_obelisk", 40f, -1.5f, 100f, 20, U(1.6f), false); Kit("citadel_obelisk", 88f, -1.5f, 120f, 50, U(1.3f), false);
        // Rubble: fallen blocks wherever there is room to fight, to break up the level ground.
        foreach (var top in demoTops.Where(d => !d.scenery && d.sx * d.sz > 300f).ToList())
            for (int k = 0; k < Mathf.Clamp((int)(top.sx * top.sz / 260f), 2, 7); k++)
            {
                var rub = Kit("citadel_rubble", top.x + Rand(-0.42f, 0.42f) * top.sx, top.top - 0.2f, top.z + Rand(-0.42f, 0.42f) * top.sz, Rand(0, 360), U(Rand(0.7f, 1.3f)));
                if (rub == null) break;
            }

        // Far out: spires, arches and giant fungi standing in the sea all round, so every horizon has something on it.
        for (int k = 0; k < 22; k++)
        {
            float ang = k / 22f * Mathf.PI * 2f + Rand(-0.1f, 0.1f), r = Rand(210f, 420f);
            Vector3 p = DemoPoint(65f + Mathf.Cos(ang) * r, -2.4f, 95f + Mathf.Sin(ang) * r);
            float far = Mathf.InverseLerp(210f, 420f, r);
            bool kit = HasModel("citadel_stack");
            if (k % 3 == 0) Fungus(flora.transform, p + Vector3.up * 1.8f, Mathf.Lerp(3.5f, 7f, far), false, 2);
            else if (k % 3 == 1) Landmark(t, kit ? "citadel_stack" : "spire", p + Vector3.down * 6f, Rand(0, 360), Mathf.Lerp(2f, 4.5f, far), false);
            else Landmark(t, kit ? "citadel_arch" : "arch", p + Vector3.down * 3f, Rand(0, 360), Mathf.Lerp(4f, 9f, far), false);
        }
    }

    /// The kit's models come in darker and redder than the ground they stand on. Lift them to the same sunlit stone.
    static void MatchCitadelStone(Transform t)
    {
        var done = new HashSet<Material>();
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && m.name.StartsWith("citadel_") && done.Add(m) && m.HasProperty("_BaseColor"))
                {
                    m.SetColor("_BaseColor", new Color(0.78f, 0.84f, 1.04f, 1f));       // cooled toward the light of this island, so the old stone sits in it
                    UnityEditor.EditorUtility.SetDirty(m);
                }
    }

    /// The level's enemies, grouped by stretch so each group sleeps until the player is near.
    static void BuildDemoEnemies(Transform parent)
    {
        var worldQueen = varrikQueen;
        foreach (var beat in demoPlanned.GroupBy(e => e.beat))
        {
            var first = beat.First().pos;
            var camp = Group("DemoCamp" + beat.Key, parent);
            camp.transform.position = first;
            var settings = camp.AddComponent<EnemyCamp>();
            settings.respawnDelay = 99999f;                 // nothing comes back in a level you play through once
            foreach (var e in beat)
            {
                var go = BuildVarrik(camp.transform, e.pos, e.unit);
                go.transform.rotation = Quaternion.Euler(0, 180f + Rand(-40f, 40f), 0);
                var ai = go.GetComponent<EnemyAI>();
                ai.sightRange = Mathf.Min(ai.sightRange, ai.hoverHeight > 0f ? 30f : 26f);
                ai.loseRange = 46f;
                if (e.unit == "queen" && worldQueen != null) ai.summonTemplates = worldQueen.summonTemplates;
            }
        }
        varrikQueen = worldQueen;
        Debug.Log("[LT] Demo level: " + demoPlanned.Count + " enemies in " + demoPlanned.Select(e => e.beat).Distinct().Count() + " groups, " + demoTops.Count + " rock masses");
    }

    /// The named stretches of the level. Walking into one announces it and makes it the place you come back to.
    static IEnumerable<WorldArea> DemoWorldAreas(Transform root)
    {
        var defs = new (string name, float x, float z, float r, Vector3 respawn)[]
        {
            ("THE BREACH", 65, 95, 260, new Vector3(0, 26.2f, 3)),
            ("THE SEA STACKS", 0, 40, 30, new Vector3(0, 26.2f, 3)),
            ("THE SHELF", 0, 92, 27, new Vector3(0, 6.2f, 76)),
            ("THE CLIMB", 0, 131, 16, new Vector3(0, 6.2f, 114)),
            ("THE GATE COURT", 0, 167, 22, new Vector3(0, 20.2f, 152)),
            ("THE RIDGE", 65, 166, 36, new Vector3(30, 20.2f, 167)),
            ("THE RELAY YARD", 131, 164, 28, new Vector3(112, 23.2f, 166)),
            ("THE QUEEN'S REACH", 131, 90, 40, new Vector3(131, 23.2f, 132)),
        };
        foreach (var d in defs)
        {
            var marker = Group("Respawn " + d.name, root).transform;
            marker.position = DemoPoint(d.respawn.x, DemoH(d.respawn.x, d.respawn.z) + 0.4f, d.respawn.z);       // on the built ground, wherever that ended up
            // Late golden hour over the whole level: warm low light, a gold and rose sky, haze with depth in it.
            yield return new WorldArea
            {
                name = d.name, region = "THE SUNKEN CITADEL", centre = new Vector2(DemoOrigin.x + d.x, DemoOrigin.z + d.z), radius = d.r, respawn = marker,
                fogColor = new Color(0.98f, 0.76f, 0.60f), fogStart = 35f, fogEnd = 520f,
                ambientSky = new Color(1.0f, 0.80f, 0.70f), ambientEquator = new Color(0.92f, 0.70f, 0.58f), ambientGround = new Color(0.42f, 0.50f, 0.50f),
                sunColor = new Color(1f, 0.80f, 0.56f), sunIntensity = 2.15f,
                skyZenith = new Color(0.22f, 0.26f, 0.56f), skyMid = new Color(0.80f, 0.66f, 0.70f), skyHorizon = new Color(1.0f, 0.80f, 0.58f),
                cloudLit = new Color(1f, 0.86f, 0.66f), cloudShade = new Color(0.82f, 0.60f, 0.62f),
            };
        }
    }
}
