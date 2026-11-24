using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Generates the whole "Luminous Tides" vertical-slice scene from code.
/// Any model present at Assets/Art/Models/<name>.fbx replaces its primitive placeholder,
/// so re-running the build after adding art upgrades the level in place.
public static partial class LevelBuilder
{
    const string Gen = "Assets/Generated";
    const string Models = "Assets/Art/Models/";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const float HalfW = 40f, Length = 140f;
    public static readonly Vector2 SpawnXZ = new Vector2(0, 8);
    public static readonly Vector2 StationXZ = new Vector2(0, 122);

    static readonly Vector4[] Islands = // x, z, radius, height
    {
        new Vector4(0, 8, 11, 1.5f), new Vector4(-8, 30, 10, 1.4f), new Vector4(10, 52, 11, 1.4f),
        new Vector4(-6, 76, 11, 1.4f), new Vector4(6, 98, 10, 1.3f), new Vector4(0, 122, 15, 1.5f),
    };
    static readonly Vector2[] EnemySpots =
    {
        new Vector2(-8, 31), new Vector2(-15, 29), new Vector2(12, 50), new Vector2(17, 59), new Vector2(-8, 74),
        new Vector2(-3, 80), new Vector2(8, 96), new Vector2(2, 101), new Vector2(-6, 112), new Vector2(6, 114),
    };
    static readonly Vector3[] CrateSpots = // x, z, yaw: cover placed where fights happen
    {
        new Vector3(-4, 19, 10), new Vector3(5, 34, 80),                                    // landing
        new Vector3(-2, 47, -20), new Vector3(8, 50, 60), new Vector3(2, 70, 0),           // either end of the canyon
        new Vector3(-9, 79, 35), new Vector3(9, 93, -50),                                   // basin
        new Vector3(0, 115, 0), new Vector3(-7, 119, 60), new Vector3(7, 119, -60),         // a ring of cover around the uplink pad
        new Vector3(-8, 127, 90), new Vector3(8, 127, 90),
    };

    static Material waterfallMat, moteMat, splashMat, poolMat;
    static Mesh waterfallMesh;
    static Material rock, water, seabed, trunk, cap, glow, grass, coral, cream, red, navy, dark, enemyBody, enemyFrill, barBack, barFill, tracerMat, planetMat;
    static System.Random rng;

    // ---------------------------------------------------------------- terrain
    // ---- Level layout (x: -40..40 west to east, z: 0..140 south to north) ----
    //   z   0- 45  Landing: a beach, two raised rock shelves either side of a tidal channel.
    //   z  45- 70  The Narrows: a ridge across the island. The route runs through a canyon under the great arch;
    //              ramps and a stepping ledge lead up onto the ridge for anyone who wants the high ground.
    //   z  70-100  The Basin: a sunken pool ringed by two rims. The second ambush erupts here, and the boss rises from it.
    //   z 100-137  The Station: a plateau reached by three ramps (south, west, east), which the waves have to climb.
    // Shelves and rims are low enough to reach with a double jump; the ridge needs a ramp or the stepping ledge.

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz, out float t)
    {
        float dx = bx - ax, dz = bz - az;
        t = Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz));
        float px = ax + dx * t - x, pz = az + dz * t - z;
        return Mathf.Sqrt(px * px + pz * pz);
    }

    /// Raises the ground to `top` inside a rounded rectangle, with a steep cliff around it and a wobbly outline.
    static float Plateau(float h, float x, float z, float cx, float cz, float hx, float hz, float top, float cliff = 1.3f)
    {
        float ox = Mathf.Max(Mathf.Abs(x - cx) - (hx - 1.5f), 0f), oz = Mathf.Max(Mathf.Abs(z - cz) - (hz - 1.5f), 0f);
        float d = Mathf.Sqrt(ox * ox + oz * oz) - 1.5f + (Mathf.PerlinNoise(x * 0.33f + cx, z * 0.33f + cz) - 0.5f) * 1.7f;
        float k = 1f - Mathf.Clamp01(d / cliff);
        if (k <= 0f) return h;
        float surface = top + (Mathf.PerlinNoise(x * 0.11f + 5f, z * 0.11f + 9f) - 0.5f) * 0.3f;
        return Mathf.Max(h, Mathf.Lerp(h, surface, Smooth(k)));
    }

    /// A straight walkable slope from height ha at (a) to hb at (b). Fills under it and cuts through cliffs above it.
    static float Ramp(float h, float x, float z, float ax, float az, float bx, float bz, float width, float ha, float hb)
    {
        float dist = SegmentDistance(x, z, ax, az, bx, bz, out float t);
        float k = 1f - Mathf.Clamp01((dist - width * 0.5f) / 1.6f);
        return k <= 0f ? h : Mathf.Lerp(h, Mathf.Lerp(ha, hb, t), Smooth(k));
    }

    /// Lowers the ground to `depth` along a line: tidal channels.
    static float Channel(float h, float x, float z, float ax, float az, float bx, float bz, float width, float depth)
    {
        float dist = SegmentDistance(x, z, ax, az, bx, bz, out _);
        float k = 1f - Mathf.Clamp01((dist - width * 0.5f) / 2.6f);
        return k <= 0f ? h : Mathf.Lerp(h, Mathf.Min(h, depth), Smooth(k));
    }

    // ax, az, bx, bz, width, height at a, height at b
    static readonly float[][] Ramps =
    {
        new[] { -6f, 21f, -11.5f, 25f, 5f, 0.6f, 2.2f },   // left shelf, from the beach
        new[] { -18f, 42f, -18f, 35f, 5f, 0.6f, 2.2f },   // left shelf, north side
        new[] { 9f, 26f, 13.5f, 30f, 5f, 0.5f, 1.9f },   // right shelf
        new[] { 19f, 44f, 19f, 38f, 5f, 0.6f, 1.9f },
        new[] { -24f, 46f, -24f, 54.5f, 5.5f, 0.6f, 3.4f },   // west ridge, south and north
        new[] { -26f, 74f, -26f, 65.5f, 5.5f, 0.6f, 3.4f },
        new[] { 25f, 46f, 25f, 54.5f, 5.5f, 0.6f, 3.2f },   // east ridge, south and north
        new[] { 27f, 74f, 27f, 65.5f, 5.5f, 0.6f, 3.2f },
        new[] { -11.5f, 86f, -17.5f, 86f, 5f, 0.3f, 1.9f },   // basin rims
        new[] { 11.5f, 84f, 17.5f, 84f, 5f, 0.3f, 1.9f },
        new[] { 0f, 103f, 0f, 112.5f, 8f, 0.5f, 2.4f },   // station: south, west, east
        new[] { -27f, 122f, -18f, 122f, 6f, 0.5f, 2.4f },
        new[] { 27f, 122f, 18f, 122f, 6f, 0.5f, 2.4f },
    };

    /// True on or beside a ramp: nothing that blocks movement may stand there.
    static bool OnRamp(float x, float z, float margin)
    {
        foreach (var r in Ramps)
            if (SegmentDistance(x, z, r[0], r[1], r[2], r[3], out _) < r[4] * 0.5f + margin) return true;
        return OnWorldRamp(x, z, margin);
    }

    /// The first island on its own. H() in LevelBuilder.World.cs joins it to the rest of the world.
    static float IslandH(float x, float z)
    {
        // Rolling ground, lightly terraced into rock slabs.
        float h = 0.6f + (Mathf.PerlinNoise(x * 0.05f + 31.7f, z * 0.05f + 11.3f) - 0.5f) * 0.8f
                       + (Mathf.PerlinNoise(x * 0.17f + 5f, z * 0.17f + 77f) - 0.5f) * 0.3f;
        float hs = h / 0.3f, fl = Mathf.Floor(hs);
        h = Mathf.Lerp(h, (fl + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, hs - fl))) * 0.3f, 0.7f);

        // The island's wandering coastline, and the sunken margin at the edge of the level.
        float halfWidth = 31f + (Mathf.PerlinNoise(z * 0.045f + 3f, 7.7f) - 0.5f) * 11f + (Mathf.PerlinNoise(x * 0.08f + z * 0.08f, 1.3f) - 0.5f) * 4f;
        h = Mathf.Lerp(-0.75f, h, Smooth(Mathf.Clamp01((halfWidth - Mathf.Abs(x)) / 4.5f)));
        float edge = Mathf.Min(z, Length - z);
        float wander = Mathf.PerlinNoise(x * 0.09f + 40f, z * 0.09f + 17f) * 3f;
        h = Mathf.Lerp(-0.75f, h, Smooth(Mathf.Clamp01((edge - wander) / 4f)));

        // Landing: two shelves and the channel between them.
        h = Channel(h, x, z, 14f, 9f, 5f, 24f, 5f, -0.4f);
        h = Channel(h, x, z, 5f, 24f, 9f, 40f, 5f, -0.4f);
        h = Plateau(h, x, z, -18f, 27f, 9f, 9f, 2.2f);
        h = Plateau(h, x, z, 19f, 31f, 8f, 8f, 1.9f);
        // The Narrows: stepping ledge, then the ridge either side of the canyon.
        h = Plateau(h, x, z, -13f, 46f, 3.5f, 3f, 1.7f);
        h = Plateau(h, x, z, -23f, 60f, 13f, 7f, 3.4f);
        h = Plateau(h, x, z, 24.5f, 60f, 12.5f, 7f, 3.2f);
        // The Basin: a pool with a rim to the west and the east, and shallows running out to sea.
        h = Channel(h, x, z, 0f, 85f, 0.1f, 87f, 15f, -0.35f);
        h = Channel(h, x, z, 31f, 72f, 35f, 100f, 6f, -0.45f);
        h = Plateau(h, x, z, -23f, 86f, 7f, 8f, 1.9f);
        h = Plateau(h, x, z, 23f, 84f, 7f, 8f, 1.9f);
        // The Station plateau.
        h = Plateau(h, x, z, 0f, 123f, 19f, 12f, 2.4f);

        // Ramps (these cut through the cliffs they meet).
        foreach (var r in Ramps) h = Ramp(h, x, z, r[0], r[1], r[2], r[3], r[4], r[5], r[6]);

        // A level pad to start on.
        h = Mathf.Lerp(h, 0.9f, Mathf.Clamp01(Mathf.InverseLerp(8f, 4f, Vector2.Distance(new Vector2(x, z), SpawnXZ))));

        // Steepen the band around the waterline so land never lies almost level with the water surface.
        h += 0.14f * (float)System.Math.Tanh(h / 0.07f);
        return Mathf.Max(h, -0.75f);
    }

    /// How steep the ground is here: height change per metre. Used to keep props off cliff faces.
    static float Slope(float x, float z)
    {
        float c = H(x, z);
        return Mathf.Max(Mathf.Abs(H(x + 0.8f, z) - c), Mathf.Abs(H(x - 0.8f, z) - c), Mathf.Abs(H(x, z + 0.8f) - c), Mathf.Abs(H(x, z - 0.8f) - c)) / 0.8f;
    }

    static Vector3 Ground(float x, float z, float lift = 0f) => new Vector3(x, H(x, z) + lift, z);

    /// Lowest ground under a footprint of the given radius, so wide props are bedded in rather than left hanging over a slope.
    static Vector3 GroundMin(float x, float z, float radius, float lift = 0f)
    {
        float h = H(x, z);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            h = Mathf.Min(h, H(x + Mathf.Cos(a) * radius, z + Mathf.Sin(a) * radius), H(x + Mathf.Cos(a) * radius * 0.5f, z + Mathf.Sin(a) * radius * 0.5f));
        }
        return new Vector3(x, h + lift, z);
    }

    /// Height for an arch: the lower of its two feet, which stand about 11.5 m either side of the centre.
    static Vector3 ArchGround(float x, float z, float yaw, float scale)
    {
        var along = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        float h = float.MaxValue;
        foreach (float d in new[] { -13f, -11f, -9f, 9f, 11f, 13f })
            foreach (float side in new[] { -3f, 0f, 3f })
            {
                var p = along * d * scale + Vector3.Cross(Vector3.up, along) * side * scale;
                h = Mathf.Min(h, H(x + p.x, z + p.z));
            }
        return new Vector3(x, h - 0.9f * scale, z);
    }
    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// Stylised top-down map for the HUD minimap: 4 px per metre, land, shoreline and shallows from the height field.
    static void BuildMinimapTexture()
    {
        const int ppm = 4;
        int w = (int)WorldMap.Width * ppm, h = (int)WorldMap.Depth * ppm;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = (i + 0.5f) / ppm + WorldMap.MinX, z = (j + 0.5f) / ppm + WorldMap.MinZ;
                float y = H(x, z);
                Color c;
                if (y < -1.2f) c = new Color(0.03f, 0.16f, 0.30f, 0.38f);                      // deep water
                else if (y < -0.05f) c = Color.Lerp(new Color(0.10f, 0.55f, 0.62f, 0.55f), new Color(0.05f, 0.30f, 0.42f, 0.45f), Mathf.InverseLerp(-0.05f, -0.7f, y));
                else if (y < 0.08f) c = new Color(0.80f, 1f, 0.98f, 0.95f);                    // shoreline
                else
                {
                    float steps = Mathf.Floor(y / 0.35f);
                    c = Color.Lerp(new Color(0.20f, 0.24f, 0.46f, 0.92f), new Color(0.40f, 0.44f, 0.72f, 0.92f), Mathf.Clamp01(steps / 6f));
                    var biome = BiomeAt(x, z);          // each kind of ground has its own colour on the map
                    c = Color.Lerp(c, new Color(0.80f, 0.74f, 0.62f, 0.92f), biome.r);
                    c = Color.Lerp(c, new Color(0.12f, 0.46f, 0.36f, 0.92f), biome.g);
                    c = Color.Lerp(c, new Color(0.50f, 0.54f, 0.64f, 0.92f), biome.b);
                    if (y > 4f) c = Color.Lerp(c, new Color(0.62f, 0.66f, 0.90f, 0.95f), Mathf.Clamp01((y - 4f) / 6f));       // the heights read paler
                }
                px[j * w + i] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        Directory.CreateDirectory("Assets/Resources/HUD");
        File.WriteAllBytes("Assets/Resources/HUD/minimap.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset("Assets/Resources/HUD/minimap.png");
    }

    // ---------------------------------------------------------------- helpers
    static Material Mat(string name, Color c, float smooth = 0.3f, Color? emission = null, float metallic = 0f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        AssetDatabase.CreateAsset(m, Gen + "/" + name + ".mat");
        return m;
    }

    static Material UnlitMat(string name, Color c, bool particle = false)
    {
        var m = new Material(Shader.Find(particle ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        AssetDatabase.CreateAsset(m, Gen + "/" + name + ".mat");
        return m;
    }

    static Material ShaderMat(string name, string shader)
    {
        var sh = Shader.Find(shader);
        if (sh == null) throw new System.Exception("Shader not found (compile error?): " + shader);
        var m = new Material(sh);
        AssetDatabase.CreateAsset(m, Gen + "/" + name + ".mat");
        return m;
    }

    static Texture2D Tex(string file, bool srgb = true)
    {
        string path = Textures + file;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null && ti.textureType == TextureImporterType.Default && ti.sRGBTexture != srgb)
        {
            ti.sRGBTexture = srgb;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// Soft-dot particle material, alpha blended or additive.
    static Material ParticleMat(string name, Color c, bool additive)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetTexture("_BaseMap", Tex("particle_soft.png"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent + 20;
        AssetDatabase.CreateAsset(m, Gen + "/" + name + ".mat");
        return m;
    }

    /// Unit waterfall sheet: 1 wide, 1 tall, hanging from y = 0 down to y = -1, arcing outward along +Z.
    static Mesh BuildWaterfallMesh()
    {
        const int rows = 10;
        var verts = new Vector3[(rows + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var tris = new int[rows * 6];
        for (int r = 0; r <= rows; r++)
        {
            float v = r / (float)rows;                       // 0 = top
            float arc = 0.35f * (1f - Mathf.Pow(1f - Mathf.Min(1f, v * 4f), 2f));
            verts[r * 2] = new Vector3(-0.5f, -v, arc);
            verts[r * 2 + 1] = new Vector3(0.5f, -v, arc);
            uvs[r * 2] = new Vector2(0f, 1f - v);
            uvs[r * 2 + 1] = new Vector2(1f, 1f - v);
        }
        for (int r = 0; r < rows; r++)
        {
            int a = r * 2, t = r * 6;
            tris[t] = a; tris[t + 1] = a + 1; tris[t + 2] = a + 2;
            tris[t + 3] = a + 1; tris[t + 4] = a + 3; tris[t + 5] = a + 2;
        }
        var m = new Mesh { name = "Waterfall", vertices = verts, uv = uvs, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, Gen + "/Waterfall.asset");
        return m;
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static GameObject Group(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        return go;
    }

    static bool HasModel(string name) => File.Exists(Models + name + ".fbx");

    /// Instantiates Assets/Art/Models/<name>.fbx, or returns null when it is not there yet.
    static GameObject Model(string name, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx");
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, false);
        return go;
    }

    static void AddMeshColliders(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    // ---------------------------------------------------------------- build
    [MenuItem("Luminous Tides/2. Build Level")]
    public static void Build()
    {
        rng = new System.Random(42);
        enemyVariantSpitter = enemyVariantBoss = enemyVariantBurster = enemyBoltMat = null;
        if (AssetDatabase.IsValidFolder(Gen)) AssetDatabase.DeleteAsset(Gen);
        AssetDatabase.CreateFolder("Assets", "Generated");
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory(Models);
        ConfigureModelImports();
        BuildMinimapTexture();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateMaterials();
        BuildEnvironmentLighting();

        var world = Group("World");
        BuildLong(world.transform);            // first: it resets the shared materials the terrain pass then uses
        BuildTerrain(world.transform);
        BuildProps(world.transform);
        BuildWorldProps(world.transform);
        BuildRange(world.transform);
        var objectiveAnchor = BuildStation(world.transform);

        // Bake before any dynamic colliders (player, enemies, triggers) exist.
        var surface = Group("NavMesh").AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, Gen + "/NavMesh.asset");
        BuildVolatilePods(world.transform);       // after the bake, so their colliders leave no holes when they blow

        // Enemies walk; they cannot jump. Confirm every raised area can be reached on foot from the start.
        var navPath = new NavMeshPath();
        Vector3 navFrom = Ground(SpawnXZ.x, SpawnXZ.y);
        var navTargets = new (string, Vector2)[]
        {
            ("uplink pad", StationXZ), ("canyon", new Vector2(1, 62)), ("left shelf", new Vector2(-18, 27)), ("right shelf", new Vector2(19, 31)),
            ("west ridge", new Vector2(-23, 60)), ("east ridge", new Vector2(24.5f, 60)), ("west rim", new Vector2(-23, 86)),
            ("east rim", new Vector2(23, 84)), ("basin", new Vector2(0, 86)), ("stepping ledge", new Vector2(-13, 46)),
        };
        foreach (var (label, p) in navTargets)
        {
            Vector3 to = Ground(p.x, p.y);
            bool onMesh = NavMesh.SamplePosition(to, out var navHit, 4f, NavMesh.AllAreas);
            bool ok = onMesh && Mathf.Abs(navHit.position.y - to.y) < 1f
                      && NavMesh.CalculatePath(navFrom, navHit.position, NavMesh.AllAreas, navPath) && navPath.status == NavMeshPathStatus.PathComplete;
            Debug.Log($"[LT] nav to {label} (height {to.y:0.0}, nearest walkable {(onMesh ? Vector3.Distance(to, navHit.position).ToString("0.0") + " m away at height " + navHit.position.y.ToString("0.0") : "none")}): {(ok ? "reachable" : "NOT REACHABLE on foot")}");
        }


        CheckWorldReachable();

        var player = BuildPlayer(out Camera cam, out Weapon weapon);
        var enemies = Group("Enemies");
        var controller = BuildEnemyAnimator("enemy");
        alphaController = BuildEnemyAnimator("alpha");
        spitterController = BuildEnemyAnimator("spitter");
        unitControllers.Clear();
        foreach (string unit in CreatureModels.Skip(3)) unitControllers[unit] = BuildEnemyAnimator(unit);
        // Only a few enemies stand in the open (a first pack, an elite mid-route, guards at the station).
        // The rest arrive as ambushes and waves, so the level is not simply pre-populated.
        foreach (int i in new[] { 0, 1, 4, 8, 9 })
        {
            bool spitter = i == 1 || i == 3 || i == 8;
            BuildEnemy(enemies.transform, Ground(EnemySpots[i].x, EnemySpots[i].y), controller,
                       spitter ? EnemyKind.Spitter : EnemyKind.Stalker, i == 4 || i == 9);
        }

        // The Varrik: a raiding party working the east side of the island. Scouts on the right shelf above the
        // landing, and a captain with his guard on the east rim of the basin.
        varrikQueen = null;
        var raiders = Group("Varrik", enemies.transform);
        foreach (var v in VarrikSpots)
        {
            float x = v.x, z = v.y;
            for (int tries = 0; tries < 20 && (H(x, z) < 0.3f || Slope(x, z) > 0.5f); tries++) { x = v.x + Rand(-4f, 4f); z = v.y + Rand(-4f, 4f); }   // onto dry, level ground
            var placed = BuildVarrik(raiders.transform, Ground(x, z), VarrikUnits[(int)v.z]).GetComponent<EnemyAI>();
            // Parties hold their ground until you come to them, so they do not pile into the main mission's fights.
            placed.sightRange = Mathf.Min(placed.sightRange, placed.hoverHeight > 0f ? 22f : 19f);
            placed.loseRange = 34f;
        }

        // Everything that lives beyond the island, in camps that sleep while the player is far away.
        BuildWorldEnemies(enemies.transform, controller);

        // Inactive templates the mission director copies for reinforcements and the boss.
        var templates = Group("EnemyTemplates");
        GameObject Template(string name, EnemyKind kind, bool elite)
        {
            var t = BuildEnemy(templates.transform, Vector3.zero, controller, kind, elite);
            t.name = name;
            t.SetActive(false);
            return t;
        }
        var stalkerT = Template("StalkerTemplate", EnemyKind.Stalker, false);
        var spitterT = Template("SpitterTemplate", EnemyKind.Spitter, false);
        var eliteT = Template("EliteTemplate", EnemyKind.Stalker, true);
        var bossT = Template("BossTemplate", EnemyKind.Boss, false);
        var bursterT = Template("BursterTemplate", EnemyKind.Burster, false);
        // The guard the Corsair Queen calls in as she weakens.
        if (varrikQueen != null)
        {
            var guard = new[] { BuildVarrik(templates.transform, Vector3.zero, "runt"), BuildVarrik(templates.transform, Vector3.zero, "kite") };
            foreach (var g in guard) g.SetActive(false);
            varrikQueen.summonTemplates = guard;
        }

        BuildRangeDirector(templates.transform, controller, bossT);
        BuildDemoEnemies(enemies.transform);

        var spawn = Group("SpawnPoint");
        spawn.transform.position = Ground(SpawnXZ.x, SpawnXZ.y, 0.1f);

        var gmGo = Group("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.player = player.GetComponent<PlayerController>();
        gm.playerHealth = player.GetComponent<Health>();
        gm.spawnPoint = spawn.transform;
        var worldStart = Group("WorldStart");
        worldStart.transform.position = spawn.transform.position;
        gm.worldSpawn = worldStart.transform;
        gm.rangeSpawn = rangeSpawn;
        gm.rangeObjective = rangeCentreMarker;
        gm.demoSpawn = demoSpawn;
        gm.demoObjective = demoObjective;
        gmGo.AddComponent<LootDrops>().pickupMaterial = glow;       // enemies drop ammunition

        // Mission: arch checkpoint, uplink pad, reinforcement points on dry land around the pad, boss entrance.
        var mission = Group("Mission");
        Transform Marker(string name, Vector3 p) { var g = Group(name, mission.transform); g.transform.position = p; return g.transform; }
        var director = mission.AddComponent<MissionDirector>();
        director.archPoint = Marker("ArchPoint", Ground(ArchSpots[0].x, ArchSpots[0].y));
        director.uplinkPoint = objectiveAnchor;
        director.bossSpawn = Marker("BossSpawn", Ground(StationXZ.x, StationXZ.y - 32f));
        var spawns = new System.Collections.Generic.List<Transform>();
        for (int a = 0; a < 720 && spawns.Count < 6; a += 10)
        {
            float ring = a < 360 ? 27f : 31f;
            float x = StationXZ.x + Mathf.Sin(a * Mathf.Deg2Rad) * ring, z = StationXZ.y + Mathf.Cos(a * Mathf.Deg2Rad) * ring;
            if (Mathf.Abs(x) > 34f || z > 136f || z < 60f || H(x, z) < 0.25f || H(x, z) > 1.4f || Slope(x, z) > 0.4f) continue;   // low ground off the plateau
            if (OnRamp(x, z, 1f) || spawns.Any(sp => Vector3.Distance(sp.position, Ground(x, z)) < 9f)) continue;
            spawns.Add(Marker("WaveSpawn" + spawns.Count, Ground(x, z)));
        }
        director.waveSpawns = spawns.ToArray();
        Debug.Log("[LT] Reinforcement points: " + spawns.Count);
        director.stalkerTemplate = stalkerT; director.spitterTemplate = spitterT; director.eliteTemplate = eliteT; director.bossTemplate = bossT;
        director.bursterTemplate = bursterT;
        // Two ambushes on the way in: a rush before the arch, and a mixed group between the arch and the station.
        director.ambushes = new[]
        {
            new MissionDirector.Ambush { point = Marker("Ambush0", Ground(5f, 45f)), triggerRadius = 13f, stalkers = 2, bursters = 2 },
            new MissionDirector.Ambush { point = Marker("Ambush1", Ground(0f, 86f)), triggerRadius = 14f, stalkers = 2, spitters = 1, bursters = 2 },
        };
        gm.objective = director.archPoint;

        BuildWorldDirector();

        var hud = Group("HUD").AddComponent<HUD>();
        hud.player = gm.player; hud.playerHealth = gm.playerHealth; hud.arsenal = cam.GetComponent<Arsenal>(); hud.cam = cam;
        hud.abilities = player.GetComponent<Abilities>();

        Group("Menu").AddComponent<GameMenu>();
        Group("Audio").AddComponent<AudioDirector>();

        BuildPostProcessing();
        ApplyRimLights();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[LT] Level built: " + ScenePath);
    }

    /// Moves every enemy's materials onto the character shader, which adds a rim light in the faction's colour so
    /// their silhouettes stand off the ground behind them.
    static void ApplyRimLights()
    {
        var shader = Shader.Find("LT/Character");
        if (shader == null) { Debug.LogWarning("[LT] LT/Character shader not found: no rim lights"); return; }
        var done = new System.Collections.Generic.HashSet<Material>();
        foreach (var ai in Resources.FindObjectsOfTypeAll<EnemyAI>())
        {
            if (ai.gameObject.scene.name == null) continue;
            Color c = ai.effectColor;
            Color rim = c / Mathf.Max(0.01f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            foreach (var r in ai.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader.name != "Universal Render Pipeline/Lit" || !done.Add(m)) continue;
                    Color emission = m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                    m.shader = shader;
                    m.SetColor("_EmissionColor", emission);
                    m.SetColor("_RimColor", new Color(rim.r, rim.g, rim.b, 1f));
                    EditorUtility.SetDirty(m);
                }
        }
        Debug.Log("[LT] Rim lights on " + done.Count + " enemy materials");
    }

    static void CreateMaterials()
    {
        var noise = Tex("noise.png", false);
        rock = ShaderMat("Rock", "LT/Terrain");
        rock.SetTexture("_BaseMap", Tex("rock_albedo.png"));
        rock.SetTexture("_BumpMap", Tex("rock_normal.png"));
        rock.SetTexture("_Noise", noise);

        waterfallMat = ShaderMat("Waterfall", "LT/Waterfall");
        waterfallMat.SetTexture("_Noise", noise);
        waterfallMesh = BuildWaterfallMesh();
        poolMat = Mat("FungusPool", new Color(0.15f, 0.75f, 0.78f), 0.92f, new Color(0.10f, 0.95f, 0.90f));
        moteMat = ParticleMat("Mote", new Color(0.6f, 2.6f, 2.4f, 0.9f), true);
        splashMat = ParticleMat("Splash", new Color(0.9f, 1f, 1f, 0.5f), false);
        water = ShaderMat("Water", "LT/Water");
        water.SetTexture("_NormalMap", Tex("water_normal.png"));
        water.SetTexture("_Caustics", Tex("caustics.png", false));
        water.SetTexture("_Noise", noise);
        seabed = Mat("Seabed", new Color(0.50f, 0.78f, 0.74f), 0.1f);
        trunk = Mat("FungusTrunk", new Color(0.36f, 0.20f, 0.62f), 0.3f);
        cap = Mat("FungusCap", new Color(1.0f, 0.45f, 0.08f), 0.4f, new Color(0.25f, 0.08f, 0f));
        glow = Mat("CyanGlow", new Color(0.2f, 0.95f, 0.9f), 0.5f, new Color(0.3f, 2.4f, 2.2f));
        grass = Mat("PurpleGrass", new Color(0.62f, 0.18f, 0.85f), 0.45f, new Color(0.10f, 0.02f, 0.16f));
        coral = Mat("Coral", new Color(1.0f, 0.50f, 0.12f), 0.25f, new Color(0.2f, 0.07f, 0f));
        cream = Mat("ArmorCream", new Color(0.93f, 0.86f, 0.72f), 0.35f);
        red = Mat("ArmorRed", new Color(0.85f, 0.33f, 0.24f), 0.4f);
        navy = Mat("ArmorNavy", new Color(0.12f, 0.14f, 0.30f), 0.3f);
        dark = Mat("GunDark", new Color(0.07f, 0.07f, 0.09f), 0.5f, null, 0.4f);
        enemyBody = Mat("EnemyBody", new Color(0.16f, 0.17f, 0.42f), 0.45f);
        enemyFrill = Mat("EnemyFrill", new Color(1.0f, 0.5f, 0.1f), 0.3f, new Color(0.35f, 0.12f, 0f));
        barBack = UnlitMat("BarBack", new Color(0f, 0f, 0f, 1f));
        barFill = UnlitMat("BarFill", new Color(1f, 0.6f, 0.12f));
        tracerMat = ParticleMat("Tracer", new Color(0.5f, 3f, 2.8f, 1f), true);
        planetMat = Mat("Planet", new Color(0.72f, 0.58f, 0.92f), 0.05f, new Color(0.30f, 0.24f, 0.45f));
    }

    static void BuildEnvironmentLighting()
    {
        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.85f);
        sun.intensity = 2.3f;
        sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

        var sky = ShaderMat("Sky", "LT/Sky");
        sky.SetTexture("_Noise", Tex("noise.png", false));
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.76f, 1.05f);
        RenderSettings.ambientEquatorColor = new Color(0.58f, 0.68f, 0.86f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.55f, 0.58f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.86f, 1f);   // matches the sky horizon
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 1100f;

        // Realtime probe so water, wet rock and armour reflect the actual sky.
        var probeGo = new GameObject("ReflectionProbe");
        probeGo.transform.position = new Vector3(0, 6f, 70f);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(3000, 600, 3000);
        probe.resolution = 256;
        probe.hdr = true;
        probe.cullingMask = 0;                // sky only: cheap and avoids capturing nearby props
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
    }

    static void BuildTerrain(Transform parent)
    {
        BuildTerrainTiles(parent);

        var centre = new Vector3(WorldMap.CentreX, 0, WorldMap.CentreZ);
        var w = Prim(PrimitiveType.Plane, "Water", parent, centre, new Vector3(500, 1, 500), water, false);
        w.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Prim(PrimitiveType.Plane, "Seabed", parent, centre + Vector3.down * 5.05f, new Vector3(500, 1, 500), rock, false);

        // Invisible walls round the whole world, out in the deep water.
        var walls = Group("Bounds", parent);
        void Wall(Vector3 pos, Vector3 size)
        {
            var g = Group("Wall", walls.transform);
            g.transform.localPosition = pos;
            g.AddComponent<BoxCollider>().size = size;
        }
        Wall(new Vector3(WorldMap.MinX - 1, 10, WorldMap.CentreZ), new Vector3(2, 60, WorldMap.Depth));
        Wall(new Vector3(WorldMap.MaxX + 1, 10, WorldMap.CentreZ), new Vector3(2, 60, WorldMap.Depth));
        Wall(new Vector3(WorldMap.CentreX, 10, WorldMap.MinZ - 1), new Vector3(WorldMap.Width, 60, 2));
        Wall(new Vector3(WorldMap.CentreX, 10, WorldMap.MaxZ + 1), new Vector3(WorldMap.Width, 60, 2));
    }

    static readonly string[] VarrikUnits = { "runt", "raider", "captain", "hound", "kite", "strider", "queen" };
    static readonly Vector3[] VarrikSpots = { };      // x, z, index into VarrikUnits. They now hold Corsair Anchorage (see CampTable).

    static bool NearPath(float x, float z, float margin)
    {
        foreach (var v in VarrikSpots) if (Vector2.Distance(new Vector2(x, z), new Vector2(v.x, v.y)) < margin) return true;
        foreach (var p in EnemySpots) if (Vector2.Distance(new Vector2(x, z), p) < margin) return true;
        foreach (var c in CrateSpots) if (Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y)) < margin) return true;
        return Vector2.Distance(new Vector2(x, z), SpawnXZ) < margin + 3 || Vector2.Distance(new Vector2(x, z), StationXZ) < margin + 12;
    }

    static readonly Vector3[] ArchSpots = { new Vector3(1, 62, 0), new Vector3(-25, 104, 70) };   // x, z, yaw
    static readonly Vector3[] SpireSpots = // x, z, scale
    {
        new Vector3(-33, 18, 1.0f), new Vector3(31, 30, 1.3f), new Vector3(-35, 52, 1.2f), new Vector3(34, 74, 1.0f),
        new Vector3(-34, 88, 1.4f), new Vector3(33, 112, 1.1f), new Vector3(-20, 133, 1.2f), new Vector3(23, 134, 0.9f),
    };

    /// Shootable hazards: clusters of glowing spore pods beside the route and around the station, placed where
    /// ambushes and waves come through so a well-timed shot takes out several enemies.
    static void BuildVolatilePods(Transform parent)
    {
        var podMat = Mat("PodGlow", new Color(1f, 0.5f, 0.1f), 0.55f, new Color(3.2f, 1.2f, 0.15f));
        var stemMat = Mat("PodStem", new Color(0.30f, 0.12f, 0.40f), 0.3f);
        var pods = Group("VolatilePods", parent);
        var spots = new[]
        {
            new Vector2(-13, 26), new Vector2(11, 40), new Vector2(-2, 50), new Vector2(-12, 58), new Vector2(10, 68),
            new Vector2(-8, 82), new Vector2(8, 90), new Vector2(-3, 98), new Vector2(14, 108), new Vector2(-14, 114),
            new Vector2(-9, 128), new Vector2(12, 126), new Vector2(0, 106),
        };
        foreach (var s in spots)
        {
            float x = s.x, z = s.y;
            for (int tries = 0; tries < 12 && H(x, z) < 0.2f; tries++) { x += Rand(-2.5f, 2.5f); z += Rand(-2.5f, 2.5f); }   // onto dry land
            if (H(x, z) < 0.2f || Slope(x, z) > 0.6f) continue;
            var root = Group("VolatilePod", pods.transform);
            root.transform.position = Ground(x, z);
            root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
            Prim(PrimitiveType.Sphere, "Stem", root.transform, new Vector3(0, 0.2f, 0), new Vector3(1.1f, 0.5f, 1.1f), stemMat, false);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + Rand(-25, 25), r = i == 0 ? 0f : Rand(0.32f, 0.5f), size = i == 0 ? Rand(0.7f, 0.85f) : Rand(0.4f, 0.55f);
                var p = Quaternion.Euler(0, a, 0) * Vector3.forward * r + Vector3.up * (i == 0 ? 0.62f : Rand(0.35f, 0.5f));
                Prim(PrimitiveType.Sphere, "Pod", root.transform, p, Vector3.one * size, podMat, false)
                    .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var hit = root.AddComponent<SphereCollider>();
            hit.center = new Vector3(0, 0.55f, 0); hit.radius = 0.7f;
            root.AddComponent<Health>().maxHealth = 10;
            root.AddComponent<Volatile>();
        }
    }

    static bool NearLandmark(float x, float z, float margin)
    {
        foreach (var a in ArchSpots) if (Vector2.Distance(new Vector2(x, z), new Vector2(a.x, a.y)) < margin + 9f) return true;
        foreach (var sp in SpireSpots) if (Vector2.Distance(new Vector2(x, z), new Vector2(sp.x, sp.y)) < margin + 2.5f) return true;
        return false;
    }

    /// True if there is anything but deep sea within `margin` of a point.
    static bool NearLand(float x, float z, float margin)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            if (H(x + Mathf.Cos(a) * margin, z + Mathf.Sin(a) * margin) > -4.5f) return true;
        }
        if (Vector2.Distance(new Vector2(x, z), new Vector2(RangeCentre.x, RangeCentre.z)) < 85f + margin) return true;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(DemoOrigin.x, DemoOrigin.z + 186f)) < 330f + margin) return true;      // and the demo level's own sky and sea      // keep the test range clear too
        return H(x, z) > -4.5f;
    }

    static GameObject Landmark(Transform parent, string name, Vector3 pos, float yaw, float scale, bool collide)
    {
        var m = Model(name, parent);
        if (m == null) return null;
        m.transform.position = pos;
        m.transform.rotation = Quaternion.Euler(0, yaw, 0);
        m.transform.localScale = Vector3.one * scale;
        if (collide) AddMeshColliders(m);
        return m;
    }

    static void BuildProps(Transform parent)
    {
        // Landmarks: rock arches (one spans the route) and spires framing the play space.
        var landmarks = Group("Landmarks", parent);
        foreach (var a in ArchSpots) Landmark(landmarks.transform, "arch", ArchGround(a.x, a.y, a.z, 1f), a.z, 1f, true);
        foreach (var sp in SpireSpots) Landmark(landmarks.transform, "spire", GroundMin(sp.x, sp.y, 3f * sp.z, -0.35f), Rand(0, 360), sp.z, true);

        // Distant silhouettes out at sea, fading into the haze.
        var distant = Group("Distant", parent);
        for (int i = 0; i < 34; i++)
        {
            float ang = Rand(0, Mathf.PI * 2), r = Rand(120, 560);
            var p = new Vector3(WorldMap.CentreX + Mathf.Cos(ang) * (r + 110f), -2.6f, WorldMap.CentreZ + Mathf.Sin(ang) * (r + 130f));
            if (NearLand(p.x, p.z, 40f)) continue;
            float far = Mathf.InverseLerp(120, 560, r);
            if (i % 3 == 0) Landmark(distant.transform, "arch", p, Rand(0, 360), Mathf.Lerp(1.6f, 5f, far) * Rand(0.8f, 1.2f), false);
            else Landmark(distant.transform, "spire", p, Rand(0, 360), Mathf.Lerp(1.8f, 6f, far) * Rand(0.7f, 1.3f), false);
        }

        // Giant fungus trees in the play space, with pools and waterfalls on the larger ones.
        var fungi = Group("Fungi", parent);
        int placed = 0, guard = 0;
        while (placed < 18 && guard++ < 800)
        {
            float x = Rand(-36, 36), z = Rand(6, 134);
            if (NearPath(x, z, 7f) || NearLandmark(x, z, 5f) || OnRamp(x, z, 5f) || H(x, z) < -0.2f || Slope(x, z) > 0.45f) continue;
            float s = Rand(0.8f, 1.6f);
            Fungus(fungi.transform, GroundMin(x, z, 2.4f * s, -0.2f), s, true, s > 1.15f ? 2 : 0);
            placed++;
        }
        for (int i = 0; i < 30; i++)
        {
            float ang = Rand(0, Mathf.PI * 2), r = Rand(75, 420);
            var p = new Vector3(WorldMap.CentreX + Mathf.Cos(ang) * (r + 60f), -0.5f, WorldMap.CentreZ + Mathf.Sin(ang) * (r + 80f));
            if (NearLand(p.x, p.z, 22f)) continue;
            Fungus(fungi.transform, p, Rand(2f, 6.5f), false, 2);
        }

        // Boulders.
        var rocks = Group("Rocks", parent);
        for (int i = 0; i < 46; i++)
        {
            float x = Rand(-38, 38), z = Rand(3, 137);
            if (NearPath(x, z, 3.5f) || NearLandmark(x, z, 0f) || OnRamp(x, z, 3f) || Slope(x, z) > 0.7f) continue;
            bool meshy = HasModel("boulder") && i % 3 != 0;
            var m = Model(meshy ? "boulder" : "rock", rocks.transform);
            float s = Rand(0.6f, 2.2f);
            if (m != null)
            {
                m.transform.position = GroundMin(x, z, 1.1f * s, -0.1f * s);
                m.transform.rotation = Quaternion.Euler(Rand(-8, 8), Rand(0, 360), Rand(-8, 8));
                m.transform.localScale = Vector3.one * s;
                if (!meshy) foreach (var r in m.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = rock;
                AddMeshColliders(m);
            }
            else
            {
                var go = Prim(PrimitiveType.Sphere, "Rock", rocks.transform, Ground(x, z, 0.1f), new Vector3(s * 1.6f, s, s * 1.3f), rock);
                go.transform.rotation = Quaternion.Euler(Rand(-15, 15), Rand(0, 360), Rand(-15, 15));
            }
        }

        // Purple grass in clumps, glowing bulb plants, and coral along the shoreline (no collision).
        var flora = Group("Flora", parent);
        for (int c = 0; c < 85; c++)
        {
            float cx = Rand(-38, 38), cz = Rand(3, 137);
            if (H(cx, cz) < 0.1f || Vector2.Distance(new Vector2(cx, cz), SpawnXZ) < 2.5f) continue;
            int n = rng.Next(3, 8);
            for (int k = 0; k < n; k++)
            {
                float x = cx + Rand(-2.4f, 2.4f), z = cz + Rand(-2.4f, 2.4f);
                if (Mathf.Abs(x) > 38.5f || z < 2f || z > 138f || H(x, z) < 0.02f || Slope(x, z) > 0.8f) continue;
                Tuft(flora.transform, Ground(x, z, -0.03f), Rand(0.6f, 1.5f));
            }
        }
        for (int i = 0; i < 70; i++)
        {
            float x = Rand(-38, 38), z = Rand(3, 137);
            float h = H(x, z);
            if (h < 0.0f || Slope(x, z) > 0.6f || NearPath(x, z, 1.5f)) continue;
            var m = Model("bulbplant", flora.transform);
            if (m == null) break;
            m.transform.position = Ground(x, z, -0.05f);
            m.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
            m.transform.localScale = Vector3.one * Rand(0.7f, 1.5f);
        }
        for (int i = 0; i < 170; i++)
        {
            float x = Rand(-38, 38), z = Rand(3, 137);
            float h = H(x, z);
            if (h > 0.45f || h < -0.6f) continue; // coral hugs the shoreline
            Coral(flora.transform, Ground(x, z), Rand(0.7f, 1.6f));
        }

        // Cover crates.
        var crates = Group("Crates", parent);
        foreach (var c in CrateSpots)
        {
            var root = Group("Crate", crates.transform);
            root.transform.position = GroundMin(c.x, c.y, 1.1f, -0.03f);
            root.transform.rotation = Quaternion.Euler(0, c.z, 0);
            var m = Model("crate", root.transform);
            if (m != null) AddMeshColliders(m);
            else
            {
                Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0, 0.15f, 0), new Vector3(2.5f, 0.3f, 1.3f), navy);
                Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0, 0.75f, 0), new Vector3(2.3f, 0.9f, 1.15f), cream);
                Prim(PrimitiveType.Cube, "Cap", root.transform, new Vector3(0.75f, 0.76f, 0), new Vector3(0.85f, 0.94f, 1.19f), red, false);
                Prim(PrimitiveType.Cube, "Light", root.transform, new Vector3(0.75f, 0.45f, -0.6f), new Vector3(0.3f, 0.08f, 0.04f), glow, false);
            }
        }

        BuildAmbience(parent);
    }

    /// Drifting bioluminescent motes across the whole play space.
    static void BuildAmbience(Transform parent)
    {
        var go = Group("Motes", parent);
        go.transform.position = new Vector3(0, 3.2f, 20f);
        go.AddComponent<FollowPlayer>();
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.prewarm = true; main.maxParticles = 1200;
        main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 1f, 0.95f), new Color(1f, 0.75f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 85f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(90f, 8f, 90f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.45f; noise.frequency = 0.22f; noise.scrollSpeed = 0.15f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = moteMat; r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void Fungus(Transform parent, Vector3 pos, float scale, bool collide, int waterfalls)
    {
        var root = Group("Fungus", parent);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(Rand(-4, 4), Rand(0, 360), Rand(-4, 4));
        root.transform.localScale = Vector3.one * scale;
        bool hasModel = Model("fungus", root.transform) != null;
        if (!hasModel)
        {
            Prim(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0, 3f, 0), new Vector3(1.3f, 3f, 1.3f), trunk, false);
            Prim(PrimitiveType.Sphere, "Cap", root.transform, new Vector3(0, 6.4f, 0), new Vector3(8.5f, 1.6f, 8.5f), cap, false);
        }
        // The model carries a water surface cut to the inner outline of the cup ("Pool") and markers at the low
        // points of the rim ("Spout*"), so the pool sits inside the cup and water spills where it would overflow.
        var poolRenderer = root.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "Pool");
        if (poolRenderer != null)
        {
            poolRenderer.sharedMaterial = water;
            poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        // The model also carries ready-made waterfall ribbons ("Fall*") that run from the pool over the top of the
        // rim and drop clear of the cap, plus "Spout*" markers where each one lands.
        var falls = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Fall")).OrderBy(r => r.name).ToList();
        for (int i = 0; i < falls.Count; i++)
        {
            falls[i].sharedMaterial = waterfallMat;
            falls[i].shadowCastingMode = ShadowCastingMode.Off;
            falls[i].gameObject.SetActive(i < waterfalls);
        }
        var spouts = root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Spout")).OrderBy(t => t.name).ToList();
        for (int i = 0; i < Mathf.Min(waterfalls, spouts.Count); i++)
        {
            Vector3 lp = root.transform.InverseTransformPoint(spouts[i].position);
            var splash = Group("Splash", root.transform);
            splash.transform.localPosition = new Vector3(lp.x, 0.3f, lp.z);
            splash.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = splash.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.startLifetime = 1.1f; main.startSpeed = 1.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); main.gravityModifier = 0.15f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 12f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 38f; shape.radius = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = splash.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = splashMat; pr.shadowCastingMode = ShadowCastingMode.Off;
        }
        if (collide)
        {
            var c = root.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0, 3f, 0); c.height = 6.5f; c.radius = 1.4f;
        }
    }

    static void Tuft(Transform parent, Vector3 pos, float scale)
    {
        var root = Group("Grass", parent);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
        root.transform.localScale = Vector3.one * scale;
        if (Model("grass", root.transform) != null) return;
        for (int i = 0; i < 6; i++)
        {
            var blade = Prim(PrimitiveType.Sphere, "Blade", root.transform, Vector3.zero, new Vector3(0.07f, 1.5f, 0.16f), grass, false);
            blade.transform.localRotation = Quaternion.Euler(Rand(12, 42), i * 60f + Rand(-15, 15), 0);
            blade.transform.localPosition = blade.transform.localRotation * new Vector3(0, 0.65f, 0);
            blade.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    static void Coral(Transform parent, Vector3 pos, float scale)
    {
        var root = Group("Coral", parent);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
        root.transform.localScale = Vector3.one * scale;
        if (Model("coral", root.transform) != null) return;
        for (int i = 0; i < 5; i++)
        {
            var b = Prim(PrimitiveType.Capsule, "Branch", root.transform, Vector3.zero, new Vector3(0.14f, Rand(0.25f, 0.5f), 0.14f), coral, false);
            b.transform.localRotation = Quaternion.Euler(Rand(-35, 35), 0, Rand(-35, 35));
            b.transform.localPosition = new Vector3(Rand(-0.3f, 0.3f), 0.3f, Rand(-0.3f, 0.3f));
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    static Transform BuildStation(Transform parent)
    {
        var root = Group("SurveyStation", parent);
        root.transform.position = Ground(StationXZ.x, StationXZ.y + 9f);
        var m = Model("station", root.transform);
        if (m != null) AddMeshColliders(m);
        else
        {
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0, 6.5f, 0), new Vector3(13, 4, 8), cream);
            Prim(PrimitiveType.Cube, "Stripe", root.transform, new Vector3(0, 5.0f, 0), new Vector3(13.2f, 0.8f, 8.2f), red, false);
            Prim(PrimitiveType.Cube, "Windows", root.transform, new Vector3(0, 7.2f, -4.02f), new Vector3(10, 1.1f, 0.1f), dark, false);
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1 : 1, sz = i < 2 ? -1 : 1;
                var leg = Prim(PrimitiveType.Cube, "Leg", root.transform, new Vector3(sx * 5.4f, 2.3f, sz * 3.2f), new Vector3(1.2f, 5f, 1.2f), cream);
                leg.transform.localRotation = Quaternion.Euler(sz * -10f, 0, sx * 10f);
            }
            Prim(PrimitiveType.Cylinder, "Mast", root.transform, new Vector3(4, 11f, 1), new Vector3(0.15f, 2.6f, 0.15f), red, false);
            Prim(PrimitiveType.Sphere, "Dish", root.transform, new Vector3(-3, 9.3f, 0), new Vector3(2.4f, 0.6f, 2.4f), cream, false);
        }

        // Objective anchor on the ground in front of the station, with a light beacon.
        var anchor = Group("ObjectiveAnchor", parent);
        anchor.transform.position = Ground(StationXZ.x, StationXZ.y);
        var beam = Prim(PrimitiveType.Cylinder, "Beacon", anchor.transform, new Vector3(0, 20, 0), new Vector3(0.22f, 20, 0.22f), glow, false);
        beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Prim(PrimitiveType.Cylinder, "Pad", anchor.transform, new Vector3(0, 0.03f, 0), new Vector3(5f, 0.03f, 5f), poolMat, false);
        return anchor.transform;
    }

    // ---------------------------------------------------------------- player
    static GameObject BuildPlayer(out Camera cam, out Weapon weapon)
    {
        var player = new GameObject("Player");
        player.transform.position = Ground(SpawnXZ.x, SpawnXZ.y, 0.1f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.9f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.95f, 0); cc.stepOffset = 0.4f; cc.slopeLimit = 50f;
        var health = player.AddComponent<Health>();
        health.maxHealth = 100; health.maxShield = 100;
        var pc = player.AddComponent<PlayerController>();

        var camGo = new GameObject("Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.75f, 0);
        cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.04f; cam.farClipPlane = 4000f; cam.fieldOfView = 72f;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        pc.cam = camGo.transform;

        var vm = Group("ViewModel", camGo.transform);
        vm.transform.localPosition = new Vector3(0.16f, -0.17f, 0.30f);
        Transform muzzle = null;
        var model = Model("fp_carbine", vm.transform);
        if (model != null)
        {
            muzzle = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Muzzle");
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            // Plate closing the rail under the sight.
            var plate = model.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "SightPlate");
            if (plate != null) plate.sharedMaterial = Mat("SightDark", new Color(0.035f, 0.037f, 0.045f), 0.4f, null, 0.3f);
            // Sight lens: clear glass with the reticle drawn at the centre of the screen (see LT/SightLens).
            var lens = model.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Lens");
            if (lens != null)
            {
                var lensMat = ShaderMat("SightLens", "LT/SightLens");
                lensMat.SetTexture("_Reticle", Tex("reticle.png", false));
                lens.sharedMaterial = lensMat;
            }
        }
        else
        {
            Prim(PrimitiveType.Cube, "Receiver", vm.transform, new Vector3(0, 0, 0), new Vector3(0.07f, 0.11f, 0.42f), cream, false);
            Prim(PrimitiveType.Cube, "Barrel", vm.transform, new Vector3(0, 0.01f, 0.32f), new Vector3(0.045f, 0.05f, 0.26f), dark, false);
            Prim(PrimitiveType.Cube, "Stock", vm.transform, new Vector3(0, -0.02f, -0.28f), new Vector3(0.06f, 0.12f, 0.2f), red, false);
            Prim(PrimitiveType.Cube, "Grip", vm.transform, new Vector3(0, -0.11f, -0.08f), new Vector3(0.05f, 0.13f, 0.06f), dark, false);
            Prim(PrimitiveType.Cube, "Mag", vm.transform, new Vector3(0, -0.12f, 0.06f), new Vector3(0.05f, 0.15f, 0.08f), dark, false);
            Prim(PrimitiveType.Cube, "Sight", vm.transform, new Vector3(0, 0.085f, 0.02f), new Vector3(0.04f, 0.05f, 0.08f), red, false);
            Prim(PrimitiveType.Cube, "Strip", vm.transform, new Vector3(0.036f, 0, 0.05f), new Vector3(0.004f, 0.02f, 0.1f), glow, false);
        }
        if (muzzle == null)
        {
            muzzle = Group("Muzzle", vm.transform).transform;
            muzzle.localPosition = new Vector3(0, 0.01f, 0.48f);
        }
        var light = Group("MuzzleLight", muzzle).AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.4f, 1f, 0.95f); light.intensity = 5f; light.range = 6f; light.enabled = false;

        // Muzzle flash: an additive star quad facing the camera, toggled per shot.
        var flashMat = ParticleMat("MuzzleFlash", new Color(0.55f, 1.7f, 1.6f, 0.85f), true);
        flashMat.SetTexture("_BaseMap", Tex("muzzle_flash.png"));
        flashMat.SetFloat("_Cull", 0f);
        var flash = Prim(PrimitiveType.Quad, "MuzzleFlash", muzzle, new Vector3(0, 0, 0.04f), Vector3.one, flashMat, false);
        flash.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var flashPivot = Group("MuzzleFlashPivot", muzzle);
        flash.transform.SetParent(flashPivot.transform, false);
        flash.transform.localScale = Vector3.one * 0.2f;

        var tracerGo = Group("TracerTemplate");
        var lr = tracerGo.AddComponent<LineRenderer>();
        lr.sharedMaterial = tracerMat; lr.positionCount = 2; lr.widthMultiplier = 0.06f; lr.useWorldSpace = true;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        tracerGo.SetActive(false);

        var fxGo = Group("ImpactFx");
        var ps = fxGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.startLifetime = 0.3f; main.startSpeed = 5f; main.startSize = 0.16f;
        main.startColor = new Color(0.5f, 1f, 0.95f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 1.4f;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 40f; shape.radius = 0.02f;
        // Neutral additive material so each emit's own colour (cyan plasma, orange spray) shows true.
        var sparkMat = ParticleMat("Spark", new Color(1.4f, 1.4f, 1.4f, 1f), true);
        fxGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;

        weapon = camGo.AddComponent<Weapon>();
        weapon.player = pc; weapon.cam = cam; weapon.viewModel = vm.transform; weapon.muzzle = muzzle;
        // One soft flash particle per impact.
        var glowGo = Group("ImpactGlow");
        var gps = glowGo.AddComponent<ParticleSystem>();
        var gmain = gps.main;
        gmain.playOnAwake = false; gmain.loop = false; gmain.startLifetime = 0.09f; gmain.startSpeed = 0f; gmain.startSize = 0.42f;
        gmain.simulationSpace = ParticleSystemSimulationSpace.World;
        var gem = gps.emission; gem.enabled = false;
        var gshape = gps.shape; gshape.enabled = false;
        var gsize = gps.sizeOverLifetime; gsize.enabled = true;
        gsize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.3f));
        var gcol = gps.colorOverLifetime; gcol.enabled = true;
        var gg = new Gradient();
        gg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        gcol.color = gg;
        glowGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;

        weapon.muzzleLight = light; weapon.tracerPrefab = lr; weapon.impactFx = ps;
        weapon.impactGlow = gps; weapon.muzzleFlash = flashPivot.transform;
        weapon.supportHand = vm.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "arm_L");


        // Second weapon: the breaker shotgun, with its own view model, muzzle and flash. It shares the tracer
        // and impact effects. Close range only: ten pellets that lose most of their damage past about 20 m.
        Weapon shotgun = null;
        GameObject vm2 = null;
        if (HasModel("fp_shotgun"))
        {
            vm2 = Group("ViewModelShotgun", camGo.transform);
            vm2.transform.localPosition = new Vector3(0.16f, -0.17f, 0.30f);
            var shotgunModel = Model("fp_shotgun", vm2.transform);
            foreach (var r in shotgunModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var muzzle2 = shotgunModel.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Muzzle");
            if (muzzle2 == null) { muzzle2 = Group("Muzzle", vm2.transform).transform; muzzle2.localPosition = new Vector3(0, 0.075f, 0.5f); }
            var light2 = Group("MuzzleLight", muzzle2).AddComponent<Light>();
            light2.type = LightType.Point; light2.color = new Color(1f, 0.75f, 0.4f); light2.intensity = 5f; light2.range = 8f; light2.enabled = false;
            var flashMat2 = ParticleMat("MuzzleFlashShotgun", new Color(2.2f, 1.3f, 0.5f, 0.9f), true);
            flashMat2.SetTexture("_BaseMap", Tex("muzzle_flash.png"));
            flashMat2.SetFloat("_Cull", 0f);
            var flash2 = Prim(PrimitiveType.Quad, "MuzzleFlash", muzzle2, Vector3.zero, Vector3.one, flashMat2, false);
            flash2.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var flashPivot2 = Group("MuzzleFlashPivot", muzzle2);
            flashPivot2.transform.localPosition = new Vector3(0, 0, 0.04f);
            flash2.transform.SetParent(flashPivot2.transform, false);
            flash2.transform.localScale = Vector3.one * 0.22f;

            shotgun = camGo.AddComponent<Weapon>();
            shotgun.weaponName = "BREAKER SHOTGUN";
            shotgun.player = pc; shotgun.cam = cam; shotgun.viewModel = vm2.transform; shotgun.muzzle = muzzle2;
            shotgun.muzzleLight = light2; shotgun.muzzleFlash = flashPivot2.transform;
            shotgun.tracerPrefab = lr; shotgun.impactFx = ps; shotgun.impactGlow = gps;
            shotgun.magazineSize = 6; shotgun.reserveAmmo = 24; shotgun.maxReserve = 36; shotgun.pickupRounds = 4;
            shotgun.damage = 13f; shotgun.pellets = 10; shotgun.pelletSpread = 3.2f; shotgun.fireRate = 1.35f; shotgun.range = 60f;
            shotgun.reloadTime = 2.0f; shotgun.falloffStart = 9f; shotgun.falloffEnd = 24f; shotgun.minFalloff = 0.25f;
            shotgun.shotSound = Weapon.ShotSound.Shotgun;
            shotgun.hipSpread = 0.6f; shotgun.aimSpread = 0.3f; shotgun.bloomPerShot = 0.6f; shotgun.maxBloom = 1.2f;
            shotgun.recoilPitch = 3.2f; shotgun.recoilYaw = 0.6f; shotgun.kickScale = 2.4f;
            shotgun.aimFov = 62f; shotgun.aimZoomRatio = 62f / 72f; shotgun.aimPos = new Vector3(0f, -0.145f, 0.16f);
            shotgun.hasSightReticle = false;
            shotgun.supportHand = vm2.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "arm_L");
            shotgun.pumpAction = true;
            shotgun.enabled = false;            // the carbine is drawn first; Arsenal swaps them
            vm2.SetActive(false);
        }
        // Third weapon: the Lodestar hand cannon. Slow, heavy, precise: big damage per round and a bigger reward for
        // head shots, with recoil that has to be ridden. It shares the tracer and impact effects.
        Weapon cannon = null;
        GameObject vm3 = null;
        if (HasModel("fp_handcannon"))
        {
            vm3 = Group("ViewModelHandCannon", camGo.transform);
            vm3.transform.localPosition = new Vector3(0.15f, -0.15f, 0.34f);
            var cannonModel = Model("fp_handcannon", vm3.transform);
            foreach (var r in cannonModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var muzzle3 = cannonModel.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Muzzle");
            if (muzzle3 == null) { muzzle3 = Group("Muzzle", vm3.transform).transform; muzzle3.localPosition = new Vector3(0, 0.036f, 0.35f); }
            var light3 = Group("MuzzleLight", muzzle3).AddComponent<Light>();
            light3.type = LightType.Point; light3.color = new Color(0.5f, 0.95f, 1f); light3.intensity = 6f; light3.range = 8f; light3.enabled = false;
            var flashMat3 = ParticleMat("MuzzleFlashHandCannon", new Color(0.7f, 1.9f, 1.9f, 0.9f), true);
            flashMat3.SetTexture("_BaseMap", Tex("muzzle_flash.png"));
            flashMat3.SetFloat("_Cull", 0f);
            var flash3 = Prim(PrimitiveType.Quad, "MuzzleFlash", muzzle3, Vector3.zero, Vector3.one, flashMat3, false);
            flash3.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var flashPivot3 = Group("MuzzleFlashPivot", muzzle3);
            flashPivot3.transform.localPosition = new Vector3(0, 0, 0.04f);
            flash3.transform.SetParent(flashPivot3.transform, false);
            flash3.transform.localScale = Vector3.one * 0.24f;

            cannon = camGo.AddComponent<Weapon>();
            cannon.weaponName = "LODESTAR HAND CANNON";
            cannon.player = pc; cannon.cam = cam; cannon.viewModel = vm3.transform; cannon.muzzle = muzzle3;
            cannon.muzzleLight = light3; cannon.muzzleFlash = flashPivot3.transform;
            cannon.tracerPrefab = lr; cannon.impactFx = ps; cannon.impactGlow = gps;
            cannon.magazineSize = 10; cannon.reserveAmmo = 60; cannon.maxReserve = 90; cannon.pickupRounds = 10;
            cannon.damage = 46f; cannon.precisionMultiplier = 2.6f; cannon.fireRate = 2.4f; cannon.range = 130f;
            cannon.reloadTime = 1.9f; cannon.falloffStart = 30f; cannon.falloffEnd = 58f; cannon.minFalloff = 0.5f;
            cannon.shotSound = Weapon.ShotSound.HandCannon;
            cannon.hipSpread = 0.45f; cannon.aimSpread = 0.02f; cannon.bloomPerShot = 1.1f; cannon.maxBloom = 2.6f; cannon.bloomRecovery = 5.5f;
            cannon.recoilPitch = 2.7f; cannon.recoilYaw = 0.5f; cannon.kickScale = 2.3f;
            cannon.hipPos = new Vector3(0.15f, -0.15f, 0.34f);
            cannon.aimFov = 58f; cannon.aimZoomRatio = 58f / 72f; cannon.aimPos = new Vector3(0f, -0.073f, 0.22f);
            cannon.aimTime = 0.16f;
            cannon.hasSightReticle = true;          // the glowing front bead is the reticle: the HUD crosshair steps aside when aiming
            cannon.supportHand = vm3.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "arm_L");
            // Iron sights: dark rear posts and a glowing front bead. Aiming puts the middle of the rear notch (the model's
            // AimPoint) on the camera axis, HC_EYE_RELIEF ahead of the eye (ArtSource/blender/build_fp.py).
            var sightDark = Mat("HandCannonSight", new Color(0.03f, 0.032f, 0.04f), 0.35f, null, 0.4f);
            foreach (var r in cannonModel.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("RearSight") || r.name == "FrontBlade") r.sharedMaterial = sightDark;
                else if (r.name == "FrontBead" || r.name.StartsWith("RearDot")) r.sharedMaterial = glow;
            }
            var cannonAim = vm3.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "AimPoint");
            if (cannonAim != null)
            {
                Vector3 ap = vm3.transform.InverseTransformPoint(cannonAim.position);
                cannon.aimPos = new Vector3(-ap.x, -ap.y, 0.27f - ap.z);
                Debug.Log("[LT] Hand cannon aim position from sights: " + cannon.aimPos.ToString("F4"));
            }
            cannon.enabled = false;
            vm3.SetActive(false);
        }
        var arsenal = camGo.AddComponent<Arsenal>();
        arsenal.weapons = new[] { weapon, shotgun, cannon }.Where(w => w != null).ToArray();

        // Abilities: the melee strike, the grenade and the Tidal Surge super.
        var abilities = player.AddComponent<Abilities>();
        abilities.player = pc; abilities.cam = cam; abilities.arsenal = arsenal;
        var abilityFlash = ParticleMat("AbilityFlash", new Color(1f, 1f, 1f, 1f), true);
        abilityFlash.SetTexture("_BaseMap", null);          // plain additive; shapes come from the meshes
        abilityFlash.SetFloat("_Cull", 0f);
        abilities.flashMaterial = abilityFlash;
        abilities.grenadeMaterial = glow;

        SetLayer(player, 2); // Ignore Raycast, so the player's own shots pass through

        // The weapon is drawn by its own overlay camera with a fixed field of view. The world camera can then
        // zoom for aiming without stretching the weapon, and the weapon never clips into nearby geometry.
        // The character's own body. It is only ever seen during the super, when the view pulls out behind them.
        var bodyModel = Model("player", player.transform);
        if (bodyModel != null)
        {
            var bodyAnimator = bodyModel.GetComponent<Animator>();
            if (bodyAnimator == null) bodyAnimator = bodyModel.AddComponent<Animator>();
            var bodyClips = AssetDatabase.LoadAllAssetsAtPath(Models + "player.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            var bodyCtrl = AnimatorController.CreateAnimatorControllerAtPath(Gen + "/PlayerBody.controller");
            foreach (string state in new[] { "Cast", "Idle" })
            {
                var clip = bodyClips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(state.ToLowerInvariant()));
                if (clip == null) { Debug.LogWarning("[LT] player body has no " + state + " clip"); continue; }
                var st = bodyCtrl.layers[0].stateMachine.AddState(state);
                st.motion = clip;
                if (state == "Cast") bodyCtrl.layers[0].stateMachine.defaultState = st;
            }
            EditorUtility.SetDirty(bodyCtrl);
            bodyAnimator.runtimeAnimatorController = bodyCtrl;
            bodyAnimator.applyRootMotion = false;
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            abilities.body = bodyAnimator;
            abilities.bodyHand = bodyModel.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "hand.L");
            Debug.Log("[LT] Player body clips: " + string.Join(", ", bodyClips.Select(c => c.name + " " + c.length.ToString("0.00") + "s")) + " hand=" + (abilities.bodyHand != null));
            bodyModel.SetActive(false);
        }

        // The ability arm: hidden until an ability is used, then it plays that ability's clip in front of the camera.
        var armModel = Model("fp_ability_arm", camGo.transform);
        if (armModel != null)
        {
            foreach (var r in armModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var r in armModel.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            var armAnimator = armModel.GetComponent<Animator>();
            if (armAnimator == null) armAnimator = armModel.AddComponent<Animator>();
            armAnimator.runtimeAnimatorController = BuildArmAnimator();
            armAnimator.applyRootMotion = false;
            armAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SetLayer(armModel, ProjectSetup.ViewModelLayer);
            armModel.SetActive(false);
            abilities.arm = armAnimator;
        }
        SetLayer(vm, ProjectSetup.ViewModelLayer);
        if (vm2 != null) SetLayer(vm2, ProjectSetup.ViewModelLayer);
        if (vm3 != null) SetLayer(vm3, ProjectSetup.ViewModelLayer);
        cam.cullingMask &= ~(1 << ProjectSetup.ViewModelLayer);
        var vmCamGo = new GameObject("ViewModelCamera");
        vmCamGo.transform.SetParent(camGo.transform, false);
        var vmCam = vmCamGo.AddComponent<Camera>();
        vmCam.cullingMask = 1 << ProjectSetup.ViewModelLayer;
        vmCam.fieldOfView = 58f; vmCam.nearClipPlane = 0.01f; vmCam.farClipPlane = 12f;
        var vmData = vmCamGo.AddComponent<UniversalAdditionalCameraData>();
        vmData.renderType = CameraRenderType.Overlay;
        vmData.renderPostProcessing = true;
        camData.cameraStack.Add(vmCam);
        weapon.viewModelCamera = vmCam;
        if (shotgun != null) shotgun.viewModelCamera = vmCam;
        if (cannon != null) cannon.viewModelCamera = vmCam;

        // Aim-down-sights puts the centre of the sight opening (the model's AimPoint marker) on the camera axis,
        // at the eye relief the sight was sized for (EYE_RELIEF in ArtSource/blender/build_fp.py).
        const float eyeRelief = 0.135f;
        var aimPoint = vm.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "AimPoint");
        if (aimPoint != null)
        {
            Vector3 p = vm.transform.InverseTransformPoint(aimPoint.position);
            weapon.aimPos = new Vector3(-p.x, -p.y, eyeRelief - p.z);
            Debug.Log("[LT] Aim position from sight: " + weapon.aimPos.ToString("F4"));
        }
        return player;
    }

    // ---------------------------------------------------------------- enemies
    /// Marks looping clips and builds an Animator controller from enemy.fbx when it exists.
    const string Textures = "Assets/Art/Textures/";
    const string ArtMaterials = "Assets/Art/Materials/";

    /// For every material inside a model whose textures were exported next to it
    /// (Textures/<material>_base.png, _normal.png, _emit.png), builds a URP material and remaps the model to it.
    static void RemapTexturedMaterials()
    {
        Directory.CreateDirectory(ArtMaterials);
        Directory.CreateDirectory(Textures);
        AssetDatabase.Refresh();
        foreach (var file in Directory.GetFiles(Textures, "*_normal.png"))
        {
            var ti = AssetImporter.GetAtPath(file.Replace('\\', '/')) as TextureImporter;
            if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        }
        foreach (var file in Directory.GetFiles(Models, "*.fbx"))
        {
            string path = file.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;
            bool changed = false;
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + src.name + "_base.png");
                if (baseTex == null) continue;
                string matPath = ArtMaterials + src.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetColor("_BaseColor", Color.white);
                bool hardSurface = src.name == "carbine" || src.name == "shotgun" || src.name == "sight" || src.name == "arm" || src.name == "station" || src.name == "crate";
                mat.SetFloat("_Smoothness", hardSurface ? 0.5f : 0.28f);
                mat.SetFloat("_Metallic", 0f);
                if (src.name == "grass")
                {
                    mat.SetFloat("_Cull", 0f);            // ribbon blades are visible from both sides
                    mat.doubleSidedGI = true;
                    mat.SetFloat("_Smoothness", 0.55f);
                }
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + src.name + "_normal.png");
                if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                var emit = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + src.name + "_emit.png");
                if (emit != null)
                {
                    mat.SetTexture("_EmissionMap", emit);
                    mat.SetColor("_EmissionColor", Color.white * 3.5f);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    /// The rigged creature models: the stalker (also used, scaled and recoloured, for elites and bursters),
    /// the boss and the spitter. Each carries the same set of clips.
    static readonly string[] CreatureModels = { "enemy", "alpha", "spitter", "varrik_runt", "varrik_raider", "varrik_captain", "varrik_hound", "varrik_strider", "varrik_queen" };
    static readonly System.Collections.Generic.Dictionary<string, AnimatorController> unitControllers = new System.Collections.Generic.Dictionary<string, AnimatorController>();
    static AnimatorController alphaController, spitterController;

    static void ConfigureModelImports()
    {
        RemapTexturedMaterials();
        foreach (string model in CreatureModels.Append("player"))
        {
            var importer = AssetImporter.GetAtPath(Models + model + ".fbx") as ModelImporter;
            if (importer == null) continue;
            importer.animationType = ModelImporterAnimationType.Generic;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name.ToLowerInvariant();
                c.loopTime = n.Contains("idle") || n.Contains("run") || n.Contains("walk");
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    /// One state per ability clip. Each plays once and holds its last frame (the arm is out of sight by then).
    static AnimatorController BuildArmAnimator()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(Models + "fp_ability_arm.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Gen + "/AbilityArm.controller");
        var sm = ctrl.layers[0].stateMachine;
        foreach (string name in new[] { "Strike", "Throw", "Surge" })
        {
            var clip = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(name.ToLowerInvariant()));
            if (clip == null) { Debug.LogWarning("[LT] ability arm has no " + name + " clip"); continue; }
            var st = sm.AddState(name);
            st.motion = clip;
            if (name == "Strike") sm.defaultState = st;
        }
        Debug.Log("[LT] Ability arm clips: " + string.Join(", ", clips.Select(c => c.name + " " + c.length.ToString("0.00") + "s")));
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    static AnimatorController BuildEnemyAnimator(string model)
    {
        string path = Models + model + ".fbx";
        if (!HasModel(model)) return null;
        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        AnimationClip Find(string key) => clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(key));
        var idle = Find("idle"); var run = Find("run"); var attack = Find("attack"); var hit = Find("hit"); var death = Find("death");
        if (idle == null || run == null) { Debug.LogWarning("[LT] enemy.fbx has no Idle/Run clips; using procedural motion."); return null; }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Gen + "/Creature_" + model + ".controller");
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Spit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Slam", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        var sm = ctrl.layers[0].stateMachine;

        var tree = new BlendTree { name = "Locomotion", blendParameter = "Speed", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        tree.AddChild(idle, 0f);
        tree.AddChild(run, 5f);
        var loco = sm.AddState("Locomotion");
        loco.motion = tree;
        sm.defaultState = loco;

        void OneShot(string name, AnimationClip clip, string trigger)
        {
            if (clip == null) return;
            var st = sm.AddState(name);
            st.motion = clip;
            var to = sm.AddAnyStateTransition(st);
            to.AddCondition(AnimatorConditionMode.If, 0, trigger);
            to.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            to.duration = 0.08f; to.canTransitionToSelf = false;
            var back = st.AddTransition(loco);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.12f;
        }
        OneShot("Attack", attack, "Attack");
        OneShot("Hit", hit, "Hit");
        OneShot("Spit", Find("spit"), "Spit");
        OneShot("Slam", Find("slam"), "Slam");
        if (death != null)
        {
            var st = sm.AddState("Death");
            st.motion = death;
            var to = sm.AddAnyStateTransition(st);
            to.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            to.duration = 0.1f; to.canTransitionToSelf = false;
        }
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    static Material enemyVariantSpitter, enemyVariantBoss, enemyVariantBurster, enemyBoltMat;

    /// Recoloured, glowing-frill material for the spitter or boss (textures from make_textures.py).
    static Material EnemyVariant(string name)
    {
        var baseTex = Tex("enemy_" + name + "_base.png");
        if (baseTex == null) return null;
        var m = Mat("Enemy_" + name, Color.white, 0.3f);
        m.SetTexture("_BaseMap", baseTex);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "enemy_normal.png");
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
        var emit = Tex("enemy_" + name + "_emit.png");
        if (emit != null)
        {
            m.SetTexture("_EmissionMap", emit);
            m.SetColor("_EmissionColor", Color.white * 3f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject BuildEnemy(Transform parent, Vector3 pos, AnimatorController controller, EnemyKind kind, bool elite)
    {
        var root = new GameObject("Enemy");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f; agent.height = 2f; agent.acceleration = 30f; agent.angularSpeed = 480f; agent.stoppingDistance = 1.6f;
        agent.enabled = false; // EnemyAI enables it in Start, once the baked NavMesh has loaded
        var health = root.AddComponent<Health>();
        health.maxHealth = kind == EnemyKind.Boss ? 2600 : kind == EnemyKind.Burster ? 24 : kind == EnemyKind.Spitter ? 55 : elite ? 180 : 70; health.maxShield = 0;
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.875f, 0); col.height = 1.75f; col.radius = 0.5f;   // body only; the head has its own hitbox
        var ai = root.AddComponent<EnemyAI>();
        ai.kind = kind;
        float scale = 1f;
        switch (kind)
        {
            case EnemyKind.Spitter:
                ai.displayName = "TIDE SPITTER"; ai.chaseSpeed = 4.6f; ai.attackDamage = 12f; ai.sightRange = 34f; ai.boltDamage = 8f;
                ai.volleySize = 3; ai.volleyGap = 0.17f; ai.fireInterval = 2.8f; ai.lobInterval = 9f; ai.canPounce = false;
                scale = 0.92f;
                break;
            case EnemyKind.Boss:
                ai.displayName = "TIDE ALPHA"; ai.elite = true; ai.chaseSpeed = 3.7f; ai.patrolSpeed = 2f;
                ai.sightRange = 90f; ai.loseRange = 999f; ai.volleySize = 5; ai.fireInterval = 4.8f; ai.boltDamage = 11f; ai.boltSpeed = 20f;
                ai.volleySpread = 5.5f; ai.volleyGap = 0.11f; ai.lobInterval = 11f; ai.lobCount = 3; ai.poolRadius = 3.4f; ai.canPounce = false;
                scale = 2.3f;
                agent.radius = 1.3f; agent.height = 4.4f; agent.stoppingDistance = 3.6f; agent.angularSpeed = 240f; agent.acceleration = 14f;
                col.center = new Vector3(0, 0.875f * scale, 0); col.height = 1.75f * scale; col.radius = 0.42f * scale;
                break;
            case EnemyKind.Burster:
                ai.displayName = "TIDE BURSTER"; ai.chaseSpeed = 8.6f; ai.patrolSpeed = 2.6f; ai.sightRange = 32f; ai.canPounce = false;
                scale = 0.62f;
                agent.radius = 0.32f; agent.height = 1.3f; agent.stoppingDistance = 0.6f; agent.acceleration = 40f;
                col.center = new Vector3(0, 0.875f * scale, 0); col.height = 1.75f * scale; col.radius = 0.5f * scale;
                break;
            default:
                if (elite) { ai.elite = true; ai.displayName = "STALKER ALPHA"; ai.attackDamage = 27f; scale = 1.18f; }
                break;
        }
        if (enemyBoltMat == null) enemyBoltMat = Mat("EnemyBolt", new Color(1f, 0.55f, 0.15f), 0.6f, new Color(4f, 1.6f, 0.3f));
        ai.boltMaterial = enemyBoltMat;
        ai.boltTrailMaterial = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/AbilityFlash.mat");

        var visual = Group("Visual", root.transform);
        // The boss and the spitter have their own models, built at their real size. Elites and bursters are the
        // stalker model scaled and recoloured, and anything missing its own model falls back to that too.
        string modelName = kind == EnemyKind.Boss && alphaController != null ? "alpha"
                         : kind == EnemyKind.Spitter && spitterController != null ? "spitter" : "enemy";
        bool unique = modelName != "enemy";
        float hitboxScale = scale;
        if (unique)
        {
            controller = modelName == "alpha" ? alphaController : spitterController;
            scale = 1f;
        }
        visual.transform.localScale = Vector3.one * scale;
        var model = Model(modelName, visual.transform);
        if (model != null)
        {
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            Material variant = null;
            if (kind == EnemyKind.Spitter && !unique) variant = enemyVariantSpitter != null ? enemyVariantSpitter : enemyVariantSpitter = EnemyVariant("spitter");
            if (kind == EnemyKind.Boss && !unique) variant = enemyVariantBoss != null ? enemyVariantBoss : enemyVariantBoss = EnemyVariant("boss");
            if (kind == EnemyKind.Burster) variant = enemyVariantBurster != null ? enemyVariantBurster : enemyVariantBurster = EnemyVariant("burster");
            if (variant != null) foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.sharedMaterial = variant;
        }
        else
        {
            Prim(PrimitiveType.Capsule, "Torso", visual.transform, new Vector3(0, 1.05f, 0), new Vector3(0.75f, 0.65f, 0.6f), enemyBody, false)
                .transform.localRotation = Quaternion.Euler(18, 0, 0);
            Prim(PrimitiveType.Sphere, "Head", visual.transform, new Vector3(0, 1.75f, 0.28f), new Vector3(0.5f, 0.42f, 0.62f), enemyBody, false);
            Prim(PrimitiveType.Sphere, "Frill", visual.transform, new Vector3(0, 1.62f, 0.12f), new Vector3(0.95f, 0.75f, 0.12f), enemyFrill, false);
            Prim(PrimitiveType.Sphere, "EyeL", visual.transform, new Vector3(-0.14f, 1.8f, 0.52f), Vector3.one * 0.08f, glow, false);
            Prim(PrimitiveType.Sphere, "EyeR", visual.transform, new Vector3(0.14f, 1.8f, 0.52f), Vector3.one * 0.08f, glow, false);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1 : 1;
                Prim(PrimitiveType.Capsule, "Leg", visual.transform, new Vector3(s * 0.22f, 0.4f, 0), new Vector3(0.22f, 0.42f, 0.22f), enemyBody, false);
                Prim(PrimitiveType.Capsule, "Arm", visual.transform, new Vector3(s * 0.48f, 1.05f, 0.25f), new Vector3(0.16f, 0.42f, 0.16f), enemyBody, false)
                    .transform.localRotation = Quaternion.Euler(-50, 0, s * -12);
            }
        }
        ai.visual = visual.transform;

        // Precision hitbox that follows the head bone (falls back to a fixed spot on placeholder models).
        Transform headBone = model != null ? model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "head") : null;
        var headGo = Group("HeadHitbox", headBone != null ? headBone : visual.transform);
        headGo.transform.position = headBone != null
            ? headBone.position + (Vector3.up * 0.12f + root.transform.forward * 0.08f) * hitboxScale
            : root.transform.TransformPoint(new Vector3(0, 1.92f, 0.12f));
        var headCol = headGo.AddComponent<SphereCollider>();
        headCol.radius = 0.23f * hitboxScale / Mathf.Max(0.0001f, headGo.transform.lossyScale.x);
        headGo.AddComponent<Hitbox>().multiplier = 2f;
        return root;
    }

    static Material varrikBoltMat, varrikEnergyMat;
    static EnemyAI varrikQueen;

    /// One Varrik unit. Each has its own rigged model (falling back to a scaled stalker until it exists):
    ///   runt    - small, fast and weak; comes in numbers and claws at you
    ///   raider  - rifleman; holds range and fires quick three-round bursts
    ///   captain - shielded elite; fires from range, then blinks to your flank and closes with a sabre
    ///   hound   - four-legged hunter; the fastest thing in the faction, pounces from range
    ///   kite    - flying scout drone; circles overhead firing pairs of bolts, so you have to look up
    ///   strider - tripod walker; cannon bursts at range and a stomp up close. Shoot the pod, not the legs
    ///   queen   - the Corsair Queen, the faction's boss: shielded, blinks, sweeps a glaive, and calls in her guard
    static GameObject BuildVarrik(Transform parent, Vector3 pos, string unit)
    {
        var root = new GameObject("Varrik_" + unit);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f; agent.height = 2f; agent.acceleration = 30f; agent.angularSpeed = 480f; agent.stoppingDistance = 1.6f;
        agent.enabled = false;
        var health = root.AddComponent<Health>();
        var col = root.AddComponent<CapsuleCollider>();
        var ai = root.AddComponent<EnemyAI>();
        ai.faction = Faction.Varrik;
        ai.effectColor = new Color(0.25f, 0.6f, 1f);
        ai.canPounce = false;
        ai.lobInterval = 0f;
        float height;
        string muzzleBone = null;
        switch (unit)
        {
            case "runt":
                height = 1.35f;
                ai.kind = EnemyKind.Stalker; ai.displayName = "VARRIK RUNT";
                health.maxHealth = 34; ai.chaseSpeed = 7.6f; ai.patrolSpeed = 2.4f; ai.attackDamage = 10f; ai.attackCooldown = 0.85f;
                ai.attackRange = 1.9f; ai.sightRange = 30f;
                agent.radius = 0.35f; agent.height = 1.3f; agent.stoppingDistance = 1.1f; agent.acceleration = 40f;
                break;
            case "raider":
                height = 2.2f;
                ai.kind = EnemyKind.Spitter; ai.displayName = "VARRIK RAIDER";
                health.maxHealth = 85; ai.chaseSpeed = 4.9f; ai.attackDamage = 14f; ai.sightRange = 42f; ai.preferredRange = 20f;
                ai.volleySize = 3; ai.volleyGap = 0.11f; ai.fireInterval = 2.1f; ai.boltDamage = 9f; ai.boltSpeed = 36f;
                muzzleBone = "hand.R";
                break;
            case "hound":
                height = 1.25f;
                ai.kind = EnemyKind.Stalker; ai.displayName = "VARRIK HOUND"; ai.canPounce = true;
                health.maxHealth = 60; ai.chaseSpeed = 9f; ai.patrolSpeed = 2.6f; ai.attackDamage = 16f; ai.attackCooldown = 1f;
                ai.attackRange = 2.4f; ai.sightRange = 36f;
                agent.radius = 0.55f; agent.height = 1.2f; agent.stoppingDistance = 1.5f; agent.acceleration = 44f; agent.angularSpeed = 600f;
                break;
            case "strider":
                height = 5.8f;
                ai.kind = EnemyKind.Champion; ai.displayName = "VARRIK STRIDER"; ai.elite = true;
                health.maxHealth = 1400; ai.chaseSpeed = 2.7f; ai.patrolSpeed = 1.4f; ai.patrolRadius = 5f; ai.sightRange = 27f;
                ai.slamRadius = 5.6f; ai.slamDamage = 30f;
                ai.volleySize = 6; ai.volleyGap = 0.1f; ai.volleySpread = 2.5f; ai.fireInterval = 4.4f; ai.boltDamage = 10f; ai.boltSpeed = 34f;
                agent.radius = 1.5f; agent.height = 5f; agent.stoppingDistance = 3.6f; agent.angularSpeed = 150f; agent.acceleration = 8f;
                muzzleBone = "cannon";
                break;
            case "queen":
                height = 3.6f;
                ai.kind = EnemyKind.Champion; ai.displayName = "CORSAIR QUEEN"; ai.elite = true; ai.canBlink = true;
                health.maxHealth = 2200; health.maxShield = 300; health.shieldRegenDelay = 6f; health.shieldRegenRate = 50f;
                ai.chaseSpeed = 4.6f; ai.patrolSpeed = 1.2f; ai.patrolRadius = 4f; ai.sightRange = 23f;
                ai.slamRadius = 5.2f; ai.slamDamage = 36f;
                ai.volleySize = 5; ai.volleyGap = 0.09f; ai.volleySpread = 7f; ai.fireInterval = 3.6f; ai.boltDamage = 12f; ai.boltSpeed = 28f;
                agent.radius = 0.7f; agent.height = 3.4f; agent.stoppingDistance = 2.6f; agent.angularSpeed = 300f; agent.acceleration = 20f;
                muzzleBone = "hand.L";
                varrikQueen = ai;
                break;
            case "kite":
                height = 1f;
                ai.kind = EnemyKind.Spitter; ai.displayName = "VARRIK KITE"; ai.hoverHeight = 4.4f;
                health.maxHealth = 42; ai.chaseSpeed = 6.8f; ai.patrolSpeed = 3f; ai.attackRange = 0f; ai.sightRange = 46f; ai.preferredRange = 13f;
                ai.volleySize = 2; ai.volleyGap = 0.14f; ai.fireInterval = 1.9f; ai.boltDamage = 7f; ai.boltSpeed = 32f;
                agent.radius = 0.4f; agent.height = 1f; agent.stoppingDistance = 0.5f; agent.acceleration = 26f;
                break;
            default:
                height = 2.7f;
                ai.kind = EnemyKind.Duelist; ai.displayName = "VARRIK CAPTAIN"; ai.elite = true;
                health.maxHealth = 260; health.maxShield = 120; health.shieldRegenDelay = 5f; health.shieldRegenRate = 40f;
                ai.chaseSpeed = 5.4f; ai.attackDamage = 30f; ai.attackRange = 2.9f; ai.attackCooldown = 1.3f; ai.sightRange = 42f;
                ai.volleySize = 2; ai.volleyGap = 0.2f; ai.fireInterval = 3.2f; ai.boltDamage = 13f; ai.boltSpeed = 30f;
                agent.radius = 0.65f; agent.height = 2.6f; agent.stoppingDistance = 2f;
                muzzleBone = "hand.L";
                break;
        }
        ai.eyeHeight = height * 0.85f;
        col.center = new Vector3(0, height * 0.42f, 0); col.height = height * 0.84f; col.radius = Mathf.Min(0.5f, height * 0.2f);
        if (unit == "hound")
        {
            // A long low body: a capsule lying along its spine.
            col.direction = 2; col.center = new Vector3(0, 0.75f, 0.1f); col.height = 2f; col.radius = 0.42f;
        }
        if (unit == "strider")
        {
            // Only the pod can be hurt: it rides high on the legs.
            col.direction = 2; col.center = new Vector3(0, 4.1f, 0.1f); col.height = 3f; col.radius = 1.15f;
            ai.eyeHeight = 4.2f;
        }
        if (unit == "kite")
        {
            Object.DestroyImmediate(col);         // its hit volume rides with the body, up in the air
            ai.eyeHeight = ai.hoverHeight;
        }
        if (varrikBoltMat == null) varrikBoltMat = Mat("VarrikBolt", new Color(0.3f, 0.65f, 1f), 0.6f, new Color(0.6f, 1.8f, 4.5f));
        ai.boltMaterial = varrikBoltMat;
        ai.boltTrailMaterial = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/AbilityFlash.mat");

        var visual = Group("Visual", root.transform);
        string modelName = "varrik_" + unit;
        unitControllers.TryGetValue(modelName, out var controller);
        var model = controller != null || unit == "kite" ? Model(modelName, visual.transform) : null;
        if (unit == "kite")
        {
            visual.transform.localPosition = Vector3.up * ai.hoverHeight;
            var body = Group("Body", visual.transform);
            var hit = body.AddComponent<SphereCollider>();
            hit.radius = 0.6f;
            var wings = body.AddComponent<BoxCollider>();
            wings.size = new Vector3(2.6f, 0.2f, 0.7f);
            var eye = Group("Muzzle", visual.transform);
            eye.transform.localPosition = new Vector3(0, -0.2f, 0.5f);
            ai.muzzle = eye.transform;
            ai.visual = visual.transform;
            if (model == null) Prim(PrimitiveType.Cube, "Wing", visual.transform, Vector3.zero, new Vector3(2.8f, 0.12f, 0.8f), enemyBody, false);
            var weak = Group("HeadHitbox", visual.transform);       // the lamp eye is its weak point
            weak.transform.localPosition = new Vector3(0, 0, 0.35f);
            weak.AddComponent<SphereCollider>().radius = 0.24f;
            weak.AddComponent<Hitbox>().multiplier = 2f;
            return root;
        }
        if (model == null)
        {
            // Not modelled yet: a stalker scaled to this unit's height stands in.
            visual.transform.localScale = Vector3.one * (height / 2.1f);
            model = Model("enemy", visual.transform);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Gen + "/Creature_enemy.controller");
        }
        if (model != null)
        {
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            if (muzzleBone != null) ai.muzzle = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == muzzleBone);
            // Energy weapons built in Blender carry a placeholder material: give them the faction's glow.
            if (varrikEnergyMat == null) varrikEnergyMat = Mat("VarrikEnergy", new Color(0.1f, 0.35f, 0.9f), 0.8f, new Color(0.08f, 0.5f, 1.9f));
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("energy")) mats[i] = varrikEnergyMat;
                // Their charcoal leather reads as a black cut-out under this world's bright sky: lift it.
                foreach (var m in mats)
                    if (m != null && m != varrikEnergyMat && m.HasProperty("_BaseColor") && m.GetColor("_BaseColor").r <= 1.01f)
                    { float lift = unit == "hound" || unit == "strider" ? 1.2f : 1.9f; m.SetColor("_BaseColor", new Color(lift, lift, lift * 1.03f, 1f)); EditorUtility.SetDirty(m); }
                smr.sharedMaterials = mats;
            }
        }
        ai.visual = visual.transform;

        Transform headBone = model != null ? model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "head") : null;
        var headGo = Group("HeadHitbox", headBone != null ? headBone : visual.transform);
        float hs = height / 2.1f;
        headGo.transform.position = headBone != null
            ? headBone.position + (Vector3.up * 0.1f + root.transform.forward * 0.1f) * hs
            : root.transform.TransformPoint(new Vector3(0, height * 0.92f, 0.1f));
        var headCol = headGo.AddComponent<SphereCollider>();
        headCol.radius = 0.22f * hs / Mathf.Max(0.0001f, headGo.transform.lossyScale.x);
        headGo.AddComponent<Hitbox>().multiplier = 2f;
        return root;
    }

    // ---------------------------------------------------------------- post
    static void BuildPostProcessing()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, Gen + "/PostProfile.asset");
        T Add<T>() where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
        var bloom = Add<Bloom>();
        bloom.threshold.Override(0.95f); bloom.intensity.Override(1.15f); bloom.scatter.Override(0.7f);
        Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
        Add<WhiteBalance>().temperature.Override(6f);
        var ca = Add<ColorAdjustments>();
        ca.saturation.Override(14f); ca.contrast.Override(10f); ca.postExposure.Override(0.4f);
        var vig = Add<Vignette>();
        vig.intensity.Override(0.22f); vig.smoothness.Override(0.4f);
        EditorUtility.SetDirty(profile);

        var vol = Group("PostProcessing").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;
    }
}
