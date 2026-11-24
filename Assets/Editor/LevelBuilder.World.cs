using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// The world beyond the first island. The island (x -40..40, z 0..140) keeps its hand-built layout; this file grows
/// the land outward from it into connected areas you can walk between:
///
///   The island's east shore --- THE TIDEWALK (a wading causeway) --- CORSAIR ANCHORAGE (the Varrik camp and their ship)
///   The Survey Station ------- STORMGATE PASS (a climbing ridge) --- HIGHWATCH (a high plateau over everything)
///   Highwatch --- a ramp down --- THE CANOPY (a forest of giant fungi) --- a gully --- back to the Anchorage
///
/// so the whole thing is a loop with the open sea in the middle and around the outside. Beyond the shallows the sea
/// floor falls away; WorldDirector carries the player back if they walk out into it.
public static partial class LevelBuilder
{
    // ---------------------------------------------------------------- land
    static float Rolling(float x, float z)
    {
        float h = (Mathf.PerlinNoise(x * 0.05f + 31.7f, z * 0.05f + 11.3f) - 0.5f) * 0.8f + (Mathf.PerlinNoise(x * 0.17f + 5f, z * 0.17f + 77f) - 0.5f) * 0.3f;
        float hs = h / 0.3f, fl = Mathf.Floor(hs);
        return Mathf.Lerp(h, (fl + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, hs - fl))) * 0.3f, 0.7f);
    }

    /// Height of a piece of land given how far inside its outline a point is (negative = outside). Inside, the ground
    /// rises from the waterline to `surface` across `coast` metres. Outside there is a shelf of shallows, then the
    /// sea floor falls away.
    static float Shore(float inside, float surface, float coast)
    {
        if (inside >= 0f) return Mathf.Lerp(-0.75f, surface, Smooth(Mathf.Clamp01(inside / coast)));
        return Mathf.Max(-5f, -0.75f + Mathf.Min(0f, inside + 7f) * 0.45f);
    }

    /// A roughly round landmass with a wandering coastline.
    static float Blob(float h, float x, float z, float cx, float cz, float r, float baseH, float rough, float coast)
    {
        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
        float wobble = (Mathf.PerlinNoise(x * 0.045f + cx, z * 0.045f + cz) - 0.5f) * r * 0.3f + (Mathf.PerlinNoise(x * 0.13f + cz, z * 0.13f + cx) - 0.5f) * 4f;
        return Mathf.Max(h, Shore(r + wobble - dist, baseH + Rolling(x, z) * rough, coast));
    }

    /// A strip of land between two points, its height running from ha to hb: causeways, gullies, ridges.
    static float Strip(float h, float x, float z, float ax, float az, float bx, float bz, float width, float ha, float hb, float coast)
    {
        float dist = SegmentDistance(x, z, ax, az, bx, bz, out float t);
        float wobble = (Mathf.PerlinNoise(x * 0.11f + ax, z * 0.11f + bz) - 0.5f) * 3f;
        return Mathf.Max(h, Shore(width * 0.5f + wobble - dist, Mathf.Lerp(ha, hb, t) + Rolling(x, z) * 0.3f, coast));
    }

    /// A round hollow: lagoons and ponds.
    static float Pool(float h, float x, float z, float cx, float cz, float r, float depth)
    {
        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) + (Mathf.PerlinNoise(x * 0.2f + cx, z * 0.2f + cz) - 0.5f) * 3f;
        float k = 1f - Mathf.Clamp01((dist - r) / 3.5f);
        return k <= 0f ? h : Mathf.Lerp(h, Mathf.Min(h, depth), Smooth(k));
    }

    // ax, az, bx, bz, width, height at a, height at b
    static readonly float[][] WorldRamps =
    {
        new[] { 60f, 211f, 86f, 198f, 9f, 8.5f, 2.0f },        // Highwatch down to the Canopy
        new[] { 104f, 103f, 109f, 110f, 5f, 1.0f, 3.2f },      // Anchorage: the north bluff, west and east
        new[] { 132f, 103f, 127f, 110f, 5f, 1.0f, 3.2f },
        new[] { 136f, 82f, 141f, 82f, 5f, 1.0f, 2.4f },        // Anchorage: the east rise
        new[] { 112f, 195f, 112f, 200.5f, 5f, 1.9f, 3.4f },    // Canopy: the mound under the Mother Tree
        new[] { 36f, 215f, 36f, 221.5f, 5f, 8.5f, 10.6f },     // Highwatch: the lookout crag
    };

    static float NewLand(float x, float z)
    {
        float h = -5f;
        // The Tidewalk: a sandbar you wade along, east from the Basin's shore.
        h = Strip(h, x, z, 30f, 86f, 52f, 90f, 13f, 0.25f, 0.3f, 4f);
        h = Strip(h, x, z, 52f, 90f, 76f, 82f, 13f, 0.3f, 0.6f, 4f);
        h = Strip(h, x, z, 76f, 82f, 88f, 83f, 16f, 0.6f, 1.0f, 4f);
        // Corsair Anchorage, the gully north of it, and the Canopy.
        h = Blob(h, x, z, 118f, 82f, 46f, 1.0f, 0.9f, 5f);
        h = Strip(h, x, z, 118f, 118f, 114f, 152f, 18f, 1.2f, 1.8f, 5f);
        h = Blob(h, x, z, 112f, 190f, 44f, 1.8f, 1.7f, 6f);
        // Stormgate Pass: a ridge climbing north from the Station, then Highwatch on top, with sheer sides.
        h = Strip(h, x, z, 0f, 128f, 4f, 160f, 16f, 2.4f, 5.5f, 3f);
        h = Strip(h, x, z, 4f, 160f, 20f, 190f, 16f, 5.5f, 8.5f, 3f);
        h = Strip(h, x, z, 20f, 190f, 30f, 204f, 18f, 8.5f, 8.5f, 3f);      // carried well into the plateau so the two join without a notch
        h = Blob(h, x, z, 38f, 214f, 30f, 8.5f, 0.8f, 3.5f);
        h = Strip(h, x, z, 50f, 213f, 60f, 211f, 14f, 8.5f, 8.5f, 3f);
        h = Strip(h, x, z, 60f, 211f, 86f, 198f, 13f, 8.5f, 2.0f, 3f);
        h = Strip(h, x, z, 86f, 198f, 96f, 194f, 14f, 2.0f, 1.9f, 4f);
        if (h <= -4.99f) return h;

        // Anchorage: the lagoon and the channel the ship came in by, a pad for the ship, a bluff and a rise.
        h = Pool(h, x, z, 112f, 78f, 12f, -0.45f);
        h = Channel(h, x, z, 112f, 74f, 110f, 34f, 9f, -0.5f);
        h = Plateau(h, x, z, 136f, 58f, 21f, 8f, 1.3f, 2.2f);
        h = Plateau(h, x, z, 118f, 113f, 16f, 7f, 3.2f);
        h = Plateau(h, x, z, 148f, 82f, 8f, 11f, 2.4f);
        // Canopy: a pond, and the mound the Mother Tree stands on.
        h = Pool(h, x, z, 98f, 180f, 8f, -0.3f);
        h = Plateau(h, x, z, 112f, 207f, 8f, 7f, 3.4f);
        // Highwatch: the lookout crag.
        h = Plateau(h, x, z, 36f, 228f, 11f, 7f, 10.6f);
        foreach (var r in WorldRamps) h = Ramp(h, x, z, r[0], r[1], r[2], r[3], r[4], r[5], r[6]);
        h += 0.14f * (float)System.Math.Tanh(h / 0.07f);
        return h;
    }

    /// Ground height anywhere in the world.
    public static float H(float x, float z)
    {
        if (x > 400f) return LongH(x - DemoOrigin.x, z - DemoOrigin.z);       // the demo level is its own island, far to the east
        float h = IslandH(x, z);
        if (h <= -0.749f)
        {
            // Open water beside the island: a shelf of shallows following its coastline, then the sea floor falls away.
            float halfWidth = 31f + (Mathf.PerlinNoise(z * 0.045f + 3f, 7.7f) - 0.5f) * 11f + (Mathf.PerlinNoise(x * 0.08f + z * 0.08f, 1.3f) - 0.5f) * 4f;
            float inside = Mathf.Min(halfWidth - Mathf.Abs(x), Mathf.Min(z, Length - z) - Mathf.PerlinNoise(x * 0.09f + 40f, z * 0.09f + 17f) * 3f);
            if (inside < 0f) h = Shore(inside, 0f, 1f);
        }
        return Mathf.Max(h, NewLand(x, z));
    }

    // ---------------------------------------------------------------- areas
    // name, region, biome, centre x, centre z, radius, respawn x, respawn z (respawn 0,0 = the mission's own checkpoints apply)
    // Biomes: 0 tidal rock, 1 salt flats, 2 forest floor, 3 storm slate. Each is its own ground, plants, props and light.
    static readonly (string name, string region, int biome, float x, float z, float r, float rx, float rz)[] AreaTable =
    {
        ("THE LANDING", "TIDAL SHALLOWS", 0, 0, 22, 26, 0, 0),
        ("THE NARROWS", "TIDAL SHALLOWS", 0, 0, 58, 16, 0, 0),
        ("SUNKEN BASIN", "TIDAL SHALLOWS", 0, 0, 86, 17, 0, 0),
        ("SURVEY STATION", "TIDAL SHALLOWS", 0, 0, 122, 17, 0, 0),
        ("THE TIDEWALK", "TIDAL SHALLOWS", 0, 53, 87, 22, 40, 87),
        ("CORSAIR ANCHORAGE", "THE SALT FLATS", 1, 118, 82, 46, 80, 84),
        ("THE GULLY", "FUNGAL CANOPY", 2, 116, 136, 13, 0, 0),
        ("THE CANOPY", "FUNGAL CANOPY", 2, 112, 190, 44, 115, 148),
        ("STORMGATE PASS", "STORM PEAKS", 3, 6, 162, 28, 1, 140),
        ("HIGHWATCH", "STORM PEAKS", 3, 38, 214, 30, 24, 197),
    };

    /// How much of each kind of ground a point is: r salt flats, g forest floor, b storm slate (none = tidal rock).
    static Color BiomeAt(float x, float z)
    {
        if (x > 400f) return new Color(0f, 0f, 0f, 1f);         // the demo island is the tidal rock throughout, like the first
        float Disc(float cx, float cz, float r, float edge) => 1f - Smooth(Mathf.Clamp01((Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - r) / edge));
        float wob = (Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 8f) - 0.5f) * 10f;       // ragged borders, not circles
        float salt = Disc(118f, 82f, 42f + wob, 12f) * Smooth(Mathf.Clamp01((126f + wob * 0.5f - z) / 10f));
        float forest = Mathf.Max(Disc(112f, 190f, 44f + wob, 12f), Smooth(Mathf.Clamp01((z - 120f - wob * 0.5f) / 10f)) * Disc(116f, 138f, 16f, 8f));
        float slate = z > 126f && x < 94f ? Smooth(Mathf.Clamp01((H(x, z) - 2.9f) / 1.6f)) : 0f;
        forest *= 1f - slate;
        salt *= 1f - forest;
        return new Color(salt, forest, slate, 1f);
    }

    static Material terrainMat;
    static readonly Dictionary<string, Material> worldMats = new Dictionary<string, Material>();

    /// A material made once per build and shared (making the same asset twice breaks the first one).
    static Material WorldMat(string name, Color c, float smooth = 0.3f, Color? emission = null, float metallic = 0f)
    {
        if (!worldMats.TryGetValue(name, out var m) || m == null) worldMats[name] = m = Mat(name, c, smooth, emission, metallic);
        return m;
    }
    static readonly Material[] biomeRock = new Material[4];

    /// The rock material for one biome, for boulders, spires and arches that stand in it.
    static Material BiomeRock(int biome)
    {
        if (biome == 0) return rock;
        if (biomeRock[biome] == null)
        {
            var m = new Material(rock);
            m.SetVector("_Biome", new Vector4(biome == 1 ? 1 : 0, biome == 2 ? 1 : 0, biome == 3 ? 1 : 0, 1));
            AssetDatabase.CreateAsset(m, Gen + "/Rock_Biome" + biome + ".mat");
            biomeRock[biome] = m;
        }
        return biomeRock[biome];
    }

    /// Re-skins a rock prop so it is made of the ground it stands on.
    static GameObject Dress(GameObject go, int biome)
    {
        if (go != null && biome != 0) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = BiomeRock(biome);
        return go;
    }

    static bool OnWorldRamp(float x, float z, float margin)
    {
        foreach (var r in WorldRamps)
            if (SegmentDistance(x, z, r[0], r[1], r[2], r[3], out _) < r[4] * 0.5f + margin) return true;
        // The pass and the gully are routes too: keep their middles clear.
        return SegmentDistance(x, z, 0f, 128f, 4f, 160f, out _) < 4f + margin || SegmentDistance(x, z, 4f, 160f, 20f, 190f, out _) < 4f + margin
            || SegmentDistance(x, z, 118f, 118f, 114f, 152f, out _) < 4f + margin || SegmentDistance(x, z, 30f, 86f, 76f, 82f, out _) < 3f + margin;
    }

    // ---------------------------------------------------------------- terrain mesh
    /// The ground as a grid of tiles, so only what is in view is drawn. Tiles that are entirely deep sea are left out.
    static void BuildTerrainTiles(Transform parent)
    {
        const float cell = 0.5f, tileX = 38f, tileZ = 40f;
        int nx = Mathf.RoundToInt(tileX / cell) + 1, nz = Mathf.RoundToInt(tileZ / cell) + 1;
        var root = Group("Terrain", parent);
        terrainMat = new Material(rock);
        terrainMat.SetVector("_Biome", Vector4.zero);        // w = 0: read the biome from each vertex
        AssetDatabase.CreateAsset(terrainMat, Gen + "/TerrainBlend.mat");
        Mesh first = null;
        int tiles = 0;
        var origins = new List<Vector2>();
        for (float zw = WorldMap.MinZ; zw < WorldMap.MaxZ - 0.1f; zw += tileZ)
            for (float xw = WorldMap.MinX; xw < WorldMap.MaxX - 0.1f; xw += tileX) origins.Add(new Vector2(xw, zw));
        for (float zd = DemoOrigin.z - 40f; zd < DemoOrigin.z + LongLength + 40f; zd += tileZ)             // and the demo island's own ground
            for (float xd = DemoOrigin.x - 76f; xd < DemoOrigin.x + 76f; xd += tileX) origins.Add(new Vector2(xd, zd));
        foreach (var origin in origins)
            {
                float x0 = origin.x, z0 = origin.y;
                var verts = new Vector3[nx * nz];
                var normals = new Vector3[nx * nz];
                var uvs = new Vector2[nx * nz];
                var colours = new Color[nx * nz];
                float top = float.MinValue;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float x = x0 + i * cell, z = z0 + j * cell;
                        float y = H(x, z);
                        top = Mathf.Max(top, y);
                        verts[j * nx + i] = new Vector3(x, y, z);
                        uvs[j * nx + i] = new Vector2(x / 8f, z / 8f);
                        colours[j * nx + i] = BiomeAt(x, z);
                    }
                if (top < -4.9f) continue;
                // Normals from the height field itself, so neighbouring tiles agree along their shared edge.
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float x = x0 + i * cell, z = z0 + j * cell;
                        float l = i > 0 ? verts[j * nx + i - 1].y : H(x - cell, z), r = i < nx - 1 ? verts[j * nx + i + 1].y : H(x + cell, z);
                        float d = j > 0 ? verts[(j - 1) * nx + i].y : H(x, z - cell), u = j < nz - 1 ? verts[(j + 1) * nx + i].y : H(x, z + cell);
                        normals[j * nx + i] = new Vector3(l - r, 2f * cell, d - u).normalized;
                    }
                var tris = new int[(nx - 1) * (nz - 1) * 6];
                int t = 0;
                for (int j = 0; j < nz - 1; j++)
                    for (int i = 0; i < nx - 1; i++)
                    {
                        int a = j * nx + i, b = a + 1, c = a + nx, e = c + 1;
                        if ((i + j) % 2 == 0) { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = e; }
                        else { tris[t++] = a; tris[t++] = c; tris[t++] = e; tris[t++] = a; tris[t++] = e; tris[t++] = b; }
                    }
                var m = new Mesh { name = "Terrain_" + tiles, vertices = verts, normals = normals, uv = uvs, colors = colours, triangles = tris };
                m.RecalculateBounds();
                if (first == null) { first = m; AssetDatabase.CreateAsset(m, Gen + "/Terrain.asset"); }
                else AssetDatabase.AddObjectToAsset(m, first);
                var go = new GameObject("Tile" + tiles++, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = m;
                go.GetComponent<MeshCollider>().sharedMesh = m;
                go.GetComponent<MeshRenderer>().sharedMaterial = terrainMat;
            }
        Debug.Log("[LT] Terrain tiles: " + tiles);
    }

    // ---------------------------------------------------------------- dressing the new areas
    static readonly List<Vector3> worldKeepOut = new List<Vector3>();     // x, z, radius: camps and landmarks

    static bool Clear(float x, float z, float margin)
    {
        foreach (var k in worldKeepOut) if (Vector2.Distance(new Vector2(x, z), new Vector2(k.x, k.y)) < k.z + margin) return false;
        return !OnWorldRamp(x, z, margin);
    }

    static void Boulder(Transform parent, float x, float z, float s, int biome = 0)
    {
        bool meshy = HasModel("boulder");
        var m = Model(meshy ? "boulder" : "rock", parent);
        if (m == null) return;
        m.transform.position = GroundMin(x, z, 1.1f * s, -0.1f * s);
        m.transform.rotation = Quaternion.Euler(Rand(-8, 8), Rand(0, 360), Rand(-8, 8));
        m.transform.localScale = Vector3.one * s;
        if (!meshy) foreach (var r in m.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = rock;
        Dress(m, biome);
        AddMeshColliders(m);
    }

    /// A Varrik standard: a dark mast with a blade of teal cloth and a blue lamp. They ring everything the Varrik hold.
    static void Pylon(Transform parent, float x, float z, float yaw, float? y = null)
    {
        var root = Group("VarrikPylon", parent);
        root.transform.position = y.HasValue ? new Vector3(x, y.Value - 0.1f, z) : Ground(x, z, -0.1f);
        root.transform.rotation = Quaternion.Euler(0, yaw, Rand(-4f, 4f));
        var iron = WorldMat("VarrikIron", new Color(0.07f, 0.075f, 0.09f), 0.45f, null, 0.6f);
        var cloth = WorldMat("VarrikCloth", new Color(0.10f, 0.62f, 0.62f), 0.15f, new Color(0.01f, 0.07f, 0.07f));
        var lamp = WorldMat("VarrikLamp", new Color(0.1f, 0.4f, 1f), 0.5f, new Color(0.2f, 0.9f, 3.2f));
        var bone = WorldMat("VarrikBone", new Color(0.86f, 0.82f, 0.72f), 0.3f);
        Prim(PrimitiveType.Cylinder, "Mast", root.transform, new Vector3(0, 3.2f, 0), new Vector3(0.16f, 3.2f, 0.16f), iron);
        Prim(PrimitiveType.Cube, "Foot", root.transform, new Vector3(0, 0.2f, 0), new Vector3(0.7f, 0.4f, 0.7f), bone);
        Prim(PrimitiveType.Cube, "Spar", root.transform, new Vector3(0.75f, 5.9f, 0), new Vector3(1.7f, 0.1f, 0.1f), bone, false);
        var sail = Prim(PrimitiveType.Cube, "Banner", root.transform, new Vector3(0.85f, 4.3f, 0), new Vector3(1.3f, 3.0f, 0.04f), cloth, false);
        sail.transform.localRotation = Quaternion.Euler(0, 0, 6f);
        var orb = Prim(PrimitiveType.Sphere, "Lamp", root.transform, new Vector3(0, 6.6f, 0), Vector3.one * 0.42f, lamp, false);
        orb.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var light = orb.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.3f, 0.7f, 1f); light.intensity = 5f; light.range = 9f;
    }

    /// A cluster of amber storm crystals: the Peaks' own light.
    static void Crystals(Transform parent, float x, float z, float s)
    {
        var root = Group("StormCrystals", parent);
        root.transform.position = Ground(x, z, -0.2f);
        root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
        var amber = WorldMat("StormCrystal", new Color(0.9f, 0.42f, 0.08f), 0.85f, new Color(1.1f, 0.42f, 0.05f));
        int n = rng.Next(3, 6);
        for (int i = 0; i < n; i++)
        {
            float h = Rand(1.2f, 3.4f) * s * (i == 0 ? 1.4f : 1f), w = Rand(0.25f, 0.5f) * s;
            var shard = Prim(PrimitiveType.Cube, "Shard", root.transform, Vector3.zero, new Vector3(w, h, w), amber, i == 0);
            shard.transform.localRotation = Quaternion.Euler(Rand(-22f, 22f), Rand(0, 90), Rand(-22f, 22f)) * Quaternion.Euler(0, 45, 0);
            shard.transform.localPosition = new Vector3(i == 0 ? 0 : Rand(-0.9f, 0.9f) * s, 0, i == 0 ? 0 : Rand(-0.9f, 0.9f) * s) + shard.transform.localRotation * Vector3.up * h * 0.42f;
            shard.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        var light = root.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.2f); light.intensity = 4f * s; light.range = 8f * s;
    }

    static void Crate(Transform parent, float x, float z, float yaw)
    {
        var root = Group("Crate", parent);
        root.transform.position = GroundMin(x, z, 1.1f, -0.03f);
        root.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var m = Model("crate", root.transform);
        if (m != null) AddMeshColliders(m);
    }

    /// Calls `place` for up to `count` random points inside a disc that pass `ok`.
    static void Scatter(int count, float cx, float cz, float r, System.Func<float, float, bool> ok, System.Action<float, float> place)
    {
        int done = 0;
        for (int guard = 0; done < count && guard < count * 12; guard++)
        {
            float a = Rand(0, Mathf.PI * 2), d = Mathf.Sqrt(Rand(0, 1)) * r;
            float x = cx + Mathf.Cos(a) * d, z = cz + Mathf.Sin(a) * d;
            if (!ok(x, z)) continue;
            place(x, z);
            done++;
        }
    }

    static void BuildWorldProps(Transform parent)
    {
        worldKeepOut.Clear();
        foreach (var c in CampTable) worldKeepOut.Add(new Vector3(c.x, c.z, 7f));
        var landmarks = Group("WorldLandmarks", parent);
        var fungi = Group("WorldFungi", parent);
        var rocks = Group("WorldRocks", parent);
        var flora = Group("WorldFlora", parent);
        var crates = Group("WorldCrates", parent);
        bool Dry(float x, float z) => H(x, z) > 0.25f;
        bool Level(float x, float z, float slope) => Slope(x, z) < slope;

        // ---- Corsair Anchorage: the ship on its pad, spires on the rim, the camp's cover.
        if (HasModel("varrik_ship"))
        {
            Landmark(landmarks.transform, "varrik_ship", GroundMin(136f, 58f, 6f, -0.15f), 0f, 1f, true);
            worldKeepOut.Add(new Vector3(136f, 58f, 23f));
        }
        foreach (var s in new[] { new Vector3(86, 112, 1.6f), new Vector3(156, 104, 2.0f), new Vector3(160, 62, 1.5f), new Vector3(92, 52, 1.7f), new Vector3(130, 122, 1.4f) })
        {
            Dress(Landmark(landmarks.transform, "spire", GroundMin(s.x, s.y, 3f * s.z, -0.4f), Rand(0, 360), s.z, true), 1);
            worldKeepOut.Add(new Vector3(s.x, s.y, 4f * s.z));
        }
        var pylons = Group("VarrikPylons", parent);
        foreach (var p in new[] { new Vector3(80, 79, 100), new Vector3(81, 90, 80), new Vector3(96, 78, 60), new Vector3(98, 96, 120), new Vector3(104, 104, 30), new Vector3(124, 104, -30),
                                  new Vector3(121, 68, 0), new Vector3(149, 68, 180), new Vector3(112, 122, 90), new Vector3(125, 122, 90), new Vector3(152, 94, 200), new Vector3(100, 62, 40) })
        {
            float px = p.x, pz = p.y;
            for (int tries = 0; tries < 12 && (H(px, pz) < 0.3f || Slope(px, pz) > 0.5f); tries++) { px = p.x + Rand(-3f, 3f); pz = p.y + Rand(-3f, 3f); }
            Pylon(pylons.transform, px, pz, p.z);
        }
        foreach (var c in new[] { new Vector3(106, 92, 20), new Vector3(118, 92, -30), new Vector3(112, 101, 90), new Vector3(101, 84, 60), new Vector3(124, 86, 0),
                                  new Vector3(88, 80, 80), new Vector3(92, 90, 10), new Vector3(128, 70, 40), new Vector3(140, 72, -20), new Vector3(116, 110, 0) })
            Crate(crates.transform, c.x, c.y, c.z);
        Scatter(34, 118, 82, 44, (x, z) => Level(x, z, 0.7f) && Clear(x, z, 3f), (x, z) => Boulder(rocks.transform, x, z, Rand(0.6f, 2.4f), 1));

        // ---- The Tidewalk: a half-drowned arch, low spires, coral everywhere.
        Landmark(landmarks.transform, "arch", ArchGround(54f, 99f, 15f, 1.2f), 15f, 1.2f, true);
        foreach (var s in new[] { new Vector3(44, 78, 0.8f), new Vector3(63, 96, 1.0f), new Vector3(70, 74, 0.9f) })
            Landmark(landmarks.transform, "spire", GroundMin(s.x, s.y, 3f * s.z, -0.5f), Rand(0, 360), s.z, true);
        Scatter(110, 53, 87, 26, (x, z) => H(x, z) < 0.5f && H(x, z) > -0.65f, (x, z) => Coral(flora.transform, Ground(x, z), Rand(0.7f, 1.8f)));
        Scatter(8, 53, 87, 22, (x, z) => Level(x, z, 0.7f) && Clear(x, z, 3f), (x, z) => Boulder(rocks.transform, x, z, Rand(0.5f, 1.4f)));

        // ---- The Canopy: the Mother Tree on its mound, and a forest of giant fungi under it.
        Fungus(fungi.transform, GroundMin(112f, 208f, 6f, -0.6f), 7.5f, true, 2);
        worldKeepOut.Add(new Vector3(112f, 208f, 9f));
        Scatter(34, 112, 190, 42, (x, z) => Dry(x, z) && Level(x, z, 0.45f) && Clear(x, z, 6f),
                (x, z) => { float s = Rand(1.2f, 3.6f); Fungus(fungi.transform, GroundMin(x, z, 2.4f * s, -0.2f * s), s, true, s > 2.4f ? 2 : 0); worldKeepOut.Add(new Vector3(x, z, 2.6f * s)); });
        Scatter(70, 112, 190, 42, (x, z) => H(x, z) > 0f && Level(x, z, 0.6f), (x, z) =>
        {
            var m = Model("bulbplant", flora.transform);
            if (m == null) return;
            m.transform.position = Ground(x, z, -0.05f);
            m.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
            m.transform.localScale = Vector3.one * Rand(0.8f, 2.2f);
        });
        Scatter(20, 112, 190, 42, (x, z) => Level(x, z, 0.7f) && Clear(x, z, 3f), (x, z) => Boulder(rocks.transform, x, z, Rand(0.6f, 2.0f), 2));
        Scatter(10, 116, 135, 13, (x, z) => Dry(x, z) && Level(x, z, 0.45f) && Clear(x, z, 5f), (x, z) => { float s = Rand(1.0f, 2.0f); Fungus(fungi.transform, GroundMin(x, z, 2.4f * s, -0.2f), s, true, 0); });

        // ---- Stormgate Pass and Highwatch: the monument on the crag, a crown of tall spires, bare rock.
        if (HasModel("stormgate"))
        {
            Landmark(landmarks.transform, "stormgate", GroundMin(36f, 229f, 5f, -0.4f), 185f, 1f, true);
            worldKeepOut.Add(new Vector3(36f, 229f, 12f));
        }
        else Dress(Landmark(landmarks.transform, "arch", ArchGround(36f, 229f, 0f, 2f), 0f, 2f, true), 3);
        foreach (var s in new[] { new Vector3(14, 204, 2.6f), new Vector3(60, 228, 2.2f), new Vector3(20, 232, 1.8f), new Vector3(58, 196, 2.0f), new Vector3(-6, 150, 1.6f), new Vector3(14, 172, 1.8f) })
        {
            Dress(Landmark(landmarks.transform, "spire", GroundMin(s.x, s.y, 3f * s.z, -0.5f), Rand(0, 360), s.z, true), 3);
            worldKeepOut.Add(new Vector3(s.x, s.y, 4f * s.z));
        }
        var crystals = Group("StormCrystals", parent);
        Scatter(16, 38, 214, 28, (x, z) => H(x, z) > 7f && Level(x, z, 0.6f) && Clear(x, z, 3f), (x, z) => { Crystals(crystals.transform, x, z, Rand(0.8f, 1.6f)); worldKeepOut.Add(new Vector3(x, z, 2f)); });
        Scatter(9, 8, 162, 28, (x, z) => H(x, z) > 2.8f && Level(x, z, 0.6f) && Clear(x, z, 3f), (x, z) => { Crystals(crystals.transform, x, z, Rand(0.6f, 1.2f)); worldKeepOut.Add(new Vector3(x, z, 2f)); });
        Scatter(6, 72, 205, 12, (x, z) => H(x, z) > 2.8f && Level(x, z, 0.6f) && Clear(x, z, 3f), (x, z) => Crystals(crystals.transform, x, z, Rand(0.6f, 1.1f)));
        Scatter(26, 38, 214, 28, (x, z) => H(x, z) > 7f && Level(x, z, 0.7f) && Clear(x, z, 3f), (x, z) => Boulder(rocks.transform, x, z, Rand(0.7f, 2.6f), 3));
        Scatter(14, 8, 160, 30, (x, z) => H(x, z) > 2f && Level(x, z, 0.7f) && Clear(x, z, 3f), (x, z) => Boulder(rocks.transform, x, z, Rand(0.6f, 1.8f), 3));
        foreach (var c in new[] { new Vector3(30, 208, 30), new Vector3(44, 214, -40), new Vector3(8, 166, 70), new Vector3(100, 150, 10), new Vector3(120, 168, 50), new Vector3(104, 196, -20) })
            Crate(crates.transform, c.x, c.y, c.z);

        // ---- Grass: purple on the Tidewalk, luminous green in the forest; nothing grows on the salt or the slate.
        var forestGrass = WorldMat("ForestGrass", new Color(0.06f, 0.42f, 0.24f), 0.2f, new Color(0.01f, 0.16f, 0.07f));
        foreach (var area in AreaTable.Where(a => a.biome == 2 || a.name == "THE TIDEWALK"))
            Scatter((int)(area.r * (area.biome == 2 ? 2.6f : 1.4f)), area.x, area.z, area.r, (x, z) => H(x, z) > 0.12f && Level(x, z, 0.8f) && BiomeAt(x, z).b < 0.3f, (x, z) =>
            {
                int n = rng.Next(2, 5);
                for (int k = 0; k < n; k++)
                {
                    float gx = x + Rand(-2.2f, 2.2f), gz = z + Rand(-2.2f, 2.2f);
                    if (H(gx, gz) <= 0.05f || Slope(gx, gz) >= 0.8f) continue;
                    int before = flora.transform.childCount;
                    Tuft(flora.transform, Ground(gx, gz, -0.03f), Rand(0.6f, 1.6f) * (area.biome == 2 ? 1.3f : 1f));
                    if (area.biome == 2)
                        foreach (var r in flora.transform.GetChild(before).GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = forestGrass;
                }
            });
    }

    // ---------------------------------------------------------------- who lives where
    // Camp name, centre x, centre z, then members: unit, x, z. Tideborn units: stalker, spitter, burster, elite.
    static readonly (string name, float x, float z, (string unit, float x, float z)[] members)[] CampTable =
    {
        ("TidewalkPack", 54, 88, new[] { ("stalker", 48f, 88f), ("stalker", 56f, 91f), ("stalker", 61f, 86f), ("burster", 66f, 84f), ("burster", 68f, 87f) }),
        ("VarrikPickets", 86, 84, new[] { ("runt", 83f, 82f), ("runt", 87f, 88f), ("raider", 90f, 83f), ("kite", 86f, 79f) }),
        ("VarrikCamp", 112, 96, new[] { ("captain", 112f, 99f), ("raider", 107f, 95f), ("raider", 118f, 95f), ("hound", 103f, 100f), ("hound", 121f, 101f), ("kite", 112f, 91f) }),
        ("VarrikWatch", 118, 113, new[] { ("raider", 113f, 113f), ("raider", 123f, 113f), ("runt", 118f, 115f) }),
        ("VarrikStrider", 142, 98, new[] { ("strider", 142f, 98f) }),
        ("VarrikCourt", 134, 72, new[] { ("queen", 134f, 73f), ("raider", 128f, 70f), ("raider", 140f, 70f) }),
        ("CanopyPack", 98, 170, new[] { ("stalker", 95f, 168f), ("stalker", 100f, 172f), ("stalker", 102f, 166f) }),
        ("CanopySpitters", 126, 176, new[] { ("spitter", 124f, 174f), ("spitter", 129f, 178f), ("stalker", 126f, 181f) }),
        ("CanopyDen", 112, 198, new[] { ("elite", 110f, 197f), ("elite", 115f, 198f), ("stalker", 106f, 200f), ("stalker", 118f, 201f), ("burster", 112f, 193f), ("burster", 109f, 192f) }),
        ("CanopyEdge", 134, 204, new[] { ("spitter", 132f, 203f), ("spitter", 137f, 206f), ("stalker", 134f, 200f) }),
        ("PassAmbush", 5, 160, new[] { ("spitter", 9f, 163f), ("spitter", 1f, 166f), ("stalker", 5f, 158f), ("stalker", 7f, 154f) }),
        ("HighwatchPack", 38, 212, new[] { ("elite", 38f, 213f), ("stalker", 33f, 209f), ("stalker", 43f, 210f), ("stalker", 40f, 218f), ("spitter", 30f, 216f), ("spitter", 47f, 217f) }),
    };

    static void BuildWorldEnemies(Transform parent, AnimatorController tideborn)
    {
        int total = 0;
        foreach (var camp in CampTable)
        {
            var root = Group(camp.name, parent);
            root.transform.position = Ground(camp.x, camp.z);
            root.AddComponent<EnemyCamp>();
            foreach (var m in camp.members)
            {
                float x = m.x, z = m.z;
                for (int tries = 0; tries < 24 && (H(x, z) < 0.3f || Slope(x, z) > 0.5f); tries++) { x = m.x + Rand(-4f, 4f); z = m.z + Rand(-4f, 4f); }   // onto dry, level ground
                Vector3 pos = Ground(x, z);
                GameObject go;
                switch (m.unit)
                {
                    case "stalker": go = BuildEnemy(root.transform, pos, tideborn, EnemyKind.Stalker, false); break;
                    case "elite": go = BuildEnemy(root.transform, pos, tideborn, EnemyKind.Stalker, true); break;
                    case "spitter": go = BuildEnemy(root.transform, pos, tideborn, EnemyKind.Spitter, false); break;
                    case "burster": go = BuildEnemy(root.transform, pos, tideborn, EnemyKind.Burster, false); break;
                    default:
                        go = BuildVarrik(root.transform, pos, m.unit);
                        var ai = go.GetComponent<EnemyAI>();
                        ai.sightRange = Mathf.Min(ai.sightRange, ai.hoverHeight > 0f ? 26f : 23f);      // each party holds its own ground
                        ai.loseRange = 40f;
                        break;
                }
                go.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
                total++;
            }
        }
        Debug.Log("[LT] World camps: " + CampTable.Length + " with " + total + " enemies");
    }

    static void BuildWorldDirector()
    {
        var root = Group("World Areas");
        var director = root.AddComponent<WorldDirector>();
        director.areas = AreaTable.Select(a =>
        {
            var area = new WorldArea { name = a.name, region = a.region, centre = new Vector2(a.x, a.z), radius = a.r };
            switch (a.biome)
            {
                case 1:     // salt flats: a white glare, the horizon lost in haze
                    area.fogColor = new Color(0.93f, 0.89f, 0.80f); area.fogStart = 30f; area.fogEnd = 380f;
                    area.ambientSky = new Color(1.0f, 0.95f, 0.84f); area.ambientEquator = new Color(0.86f, 0.80f, 0.68f); area.ambientGround = new Color(0.62f, 0.56f, 0.46f);
                    area.sunColor = new Color(1f, 0.93f, 0.78f); area.sunIntensity = 2.7f;
                    break;
                case 2:     // the forest: green gloom, little sun, lit from below by what grows there
                    area.fogColor = new Color(0.10f, 0.32f, 0.27f); area.fogStart = 14f; area.fogEnd = 170f;
                    area.ambientSky = new Color(0.32f, 0.66f, 0.58f); area.ambientEquator = new Color(0.24f, 0.50f, 0.46f); area.ambientGround = new Color(0.34f, 0.18f, 0.40f);
                    area.sunColor = new Color(0.6f, 0.95f, 0.8f); area.sunIntensity = 1.35f;
                    break;
                case 3:     // the peaks: thin cold light under a storm sky
                    area.fogColor = new Color(0.52f, 0.57f, 0.70f); area.fogStart = 35f; area.fogEnd = 340f;
                    area.ambientSky = new Color(0.58f, 0.64f, 0.84f); area.ambientEquator = new Color(0.46f, 0.50f, 0.66f); area.ambientGround = new Color(0.34f, 0.36f, 0.46f);
                    area.sunColor = new Color(0.82f, 0.88f, 1f); area.sunIntensity = 1.8f;
                    break;
            }
            if (a.rx != 0f || a.rz != 0f)
            {
                var marker = Group("Respawn " + a.name, root.transform);
                Vector3 p = Ground(a.rx, a.rz, 0.2f);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) p = hit.position + Vector3.up * 0.2f;
                marker.transform.position = p;
                area.respawn = marker.transform;
            }
            return area;
        }).Append(new WorldArea { name = "COMBAT TEST RANGE", region = "SURVEY TRAINING DECK", centre = new Vector2(RangeCentre.x, RangeCentre.z), radius = 80f, respawn = rangeSpawn })
          .Concat(LongWorldAreas(root.transform)).ToArray();
    }

    /// Logs whether each new area can be walked to from the start: the whole point is that they are joined up.
    static void CheckWorldReachable()
    {
        var path = new NavMeshPath();
        Vector3 from = Ground(SpawnXZ.x, SpawnXZ.y);
        foreach (var a in AreaTable.Where(t => t.rx != 0f || t.rz != 0f))
        {
            Vector3 to = Ground(a.rx, a.rz);
            bool onMesh = NavMesh.SamplePosition(to, out var hit, 5f, NavMesh.AllAreas);
            bool ok = onMesh && NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            float length = 0f;
            for (int i = 1; ok && i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            Debug.Log($"[LT] walk to {a.name} (ground {to.y:0.0} m): {(ok ? "reachable, " + length.ToString("0") + " m from the landing" : "NOT REACHABLE on foot")}");
        }
    }
}
