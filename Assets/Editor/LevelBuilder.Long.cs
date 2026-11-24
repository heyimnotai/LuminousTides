using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// The demo level as a long island in the style of the first one: one continuous piece of hand-shaped ground, the same
/// tidal rock, purple grass, giant fungi, coral and daylight, dressed just as densely, but about 370 m long.
/// The first hundred metres ARE the first island (same shelves, ridge, canyon and basin, at the same coordinates);
/// five new stretches carry on north from there in the same manner.
///
/// In the level's own coordinates (x: -40..40 west to east, z: 0..372 south to north):
///   z   0- 45  The Landing: a beach, two rock shelves either side of a tidal channel
///   z  45- 70  The Narrows: a ridge across the island, a canyon through it under the great arch
///   z  70-110  The Sunken Basin: a pool between two rims
///   z 110-136  The Overlook: a low plateau reached by three ramps
///   z 138-180  The Terraces: two broad steps climbing north, each a fight
///   z 180-228  The Gorge: a stream between high walls; walk the floor, or jump the ledges up the walls
///   z 232-262  The High Meadow: a wide plateau and a grove; the big fight, and where the Strider walks
///   z 262-294  The Causeway: the island narrows to a neck of low ground, sea stacks standing off either side
///   z 296-362  The Crown: a broad round rise ringed with spires, where the Corsair Queen holds court
public static partial class LevelBuilder
{
    const float LongLength = 372f;

    // ax, az, bx, bz, width, height at a, height at b. The first thirteen are the first island's own ramps.
    static readonly float[][] LongRamps =
    {
        new[] { -6f, 21f, -11.5f, 25f, 5f, 0.6f, 2.2f }, new[] { -18f, 42f, -18f, 35f, 5f, 0.6f, 2.2f },
        new[] { 9f, 26f, 13.5f, 30f, 5f, 0.5f, 1.9f }, new[] { 19f, 44f, 19f, 38f, 5f, 0.6f, 1.9f },
        new[] { -24f, 46f, -24f, 54.5f, 5.5f, 0.6f, 3.4f }, new[] { -26f, 74f, -26f, 65.5f, 5.5f, 0.6f, 3.4f },
        new[] { 25f, 46f, 25f, 54.5f, 5.5f, 0.6f, 3.2f }, new[] { 27f, 74f, 27f, 65.5f, 5.5f, 0.6f, 3.2f },
        new[] { -11.5f, 86f, -17.5f, 86f, 5f, 0.3f, 1.9f }, new[] { 11.5f, 84f, 17.5f, 84f, 5f, 0.3f, 1.9f },
        new[] { 0f, 103f, 0f, 112.5f, 8f, 0.5f, 2.4f }, new[] { -27f, 122f, -18f, 122f, 6f, 0.5f, 2.4f }, new[] { 27f, 122f, 18f, 122f, 6f, 0.5f, 2.4f },
        // The Terraces
        new[] { -12f, 134f, -12f, 143f, 6f, 2.4f, 4.2f }, new[] { 12f, 158f, 12f, 163f, 6f, 4.2f, 6.2f },
        new[] { -27f, 150f, -21f, 150f, 5f, 0.6f, 4.2f }, new[] { 25f, 170f, 19f, 170f, 5f, 0.6f, 6.2f },
        // down into the Gorge, and up out of it onto the Meadow
        new[] { 0f, 177f, 0f, 186f, 6f, 6.2f, 0.8f },
        new[] { -9f, 225f, -9f, 233.5f, 5.5f, 0.7f, 5.0f }, new[] { 10f, 225f, 10f, 233.5f, 5.5f, 0.7f, 5.0f },
        // off the Meadow onto the Causeway, and up onto the Crown
        new[] { 0f, 259f, 0f, 267f, 7f, 5.0f, 0.7f },
        new[] { 0f, 297f, 0f, 306f, 9f, 0.7f, 4.5f }, new[] { -30f, 326f, -24f, 326f, 6f, 0.6f, 4.5f }, new[] { 30f, 334f, 24f, 334f, 6f, 0.6f, 4.5f },
    };

    static bool OnLongRamp(float x, float z, float margin)
    {
        foreach (var r in LongRamps)
            if (SegmentDistance(x, z, r[0], r[1], r[2], r[3], out _) < r[4] * 0.5f + margin) return true;
        return false;
    }

    /// How far the land reaches either side of the centre line at a given point along the island, before the
    /// coastline's own wandering: broad for most of its length, drawn in to a neck at the Causeway.
    static float LongHalfWidth(float z)
    {
        float neck = Mathf.Exp(-Mathf.Pow((z - 278f) / 15f, 2f));          // 1 at the middle of the Causeway
        float crown = Mathf.Exp(-Mathf.Pow((z - 330f) / 26f, 2f));
        return Mathf.Lerp(31f, 13f, neck) + 5f * crown;
    }

    /// Ground height of the long island, in its own coordinates. Built exactly as the first island is: rolling,
    /// lightly terraced ground inside a wandering coastline, then shelves, rims, channels and ramps laid over it.
    static float LongH(float x, float z)
    {
        float h = 0.6f + (Mathf.PerlinNoise(x * 0.05f + 31.7f, z * 0.05f + 11.3f) - 0.5f) * 0.8f
                       + (Mathf.PerlinNoise(x * 0.17f + 5f, z * 0.17f + 77f) - 0.5f) * 0.3f;
        float hs = h / 0.3f, fl = Mathf.Floor(hs);
        h = Mathf.Lerp(h, (fl + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, hs - fl))) * 0.3f, 0.7f);

        float reach = LongHalfWidth(z);
        float halfWidth = reach + (Mathf.PerlinNoise(z * 0.045f + 3f, 7.7f) - 0.5f) * 11f * (reach / 31f) + (Mathf.PerlinNoise(x * 0.08f + z * 0.08f, 1.3f) - 0.5f) * 4f;
        float inside = halfWidth - Mathf.Abs(x);
        h = Mathf.Lerp(-0.75f, h, Smooth(Mathf.Clamp01(inside / 4.5f)));
        float edge = Mathf.Min(z, LongLength - z) - Mathf.PerlinNoise(x * 0.09f + 40f, z * 0.09f + 17f) * 3f;
        h = Mathf.Lerp(-0.75f, h, Smooth(Mathf.Clamp01(edge / 4f)));
        inside = Mathf.Min(inside, edge);

        // ---- the first island, as it was
        h = Channel(h, x, z, 14f, 9f, 5f, 24f, 5f, -0.4f);
        h = Channel(h, x, z, 5f, 24f, 9f, 40f, 5f, -0.4f);
        h = Plateau(h, x, z, -18f, 27f, 9f, 9f, 2.2f);
        h = Plateau(h, x, z, 19f, 31f, 8f, 8f, 1.9f);
        h = Plateau(h, x, z, -13f, 46f, 3.5f, 3f, 1.7f);
        h = Plateau(h, x, z, -23f, 60f, 13f, 7f, 3.4f);
        h = Plateau(h, x, z, 24.5f, 60f, 12.5f, 7f, 3.2f);
        h = Channel(h, x, z, 0f, 85f, 0.1f, 87f, 15f, -0.35f);
        h = Channel(h, x, z, 31f, 72f, 35f, 100f, 6f, -0.45f);
        h = Plateau(h, x, z, -23f, 86f, 7f, 8f, 1.9f);
        h = Plateau(h, x, z, 23f, 84f, 7f, 8f, 1.9f);
        h = Plateau(h, x, z, 0f, 123f, 19f, 12f, 2.4f);

        // ---- the Terraces: two broad steps, the second set back to the east, a rill running down beside them
        h = Plateau(h, x, z, -3f, 152f, 23f, 10f, 4.2f);
        h = Plateau(h, x, z, 3f, 170f, 21f, 8.5f, 6.2f);
        h = Plateau(h, x, z, -22f, 166f, 5f, 5f, 7.6f);                      // a knuckle of rock above the upper step: high ground
        h = Channel(h, x, z, 30f, 140f, 33f, 176f, 5f, -0.4f);
        // ---- the Gorge: two long walls and a stream between them, with ledges stepping up either wall
        h = Plateau(h, x, z, -21f, 204f, 10f, 22f, 8.5f);
        h = Plateau(h, x, z, 21f, 204f, 10f, 22f, 8.0f);
        h = Channel(h, x, z, 0f, 184f, 3f, 204f, 6f, -0.35f);
        h = Channel(h, x, z, 3f, 204f, -2f, 226f, 6f, -0.35f);
        h = Plateau(h, x, z, -8.5f, 192f, 3.2f, 3.2f, 3.0f);
        h = Plateau(h, x, z, 9f, 199f, 3.2f, 3.2f, 4.6f);
        h = Plateau(h, x, z, -8.5f, 207f, 3.2f, 3.2f, 6.2f);
        h = Plateau(h, x, z, 9f, 215f, 3.2f, 3.2f, 6.6f);
        // ---- the High Meadow, with a raised lip to the east and a hollow in the middle
        h = Plateau(h, x, z, 0f, 247f, 25f, 14f, 5.0f);
        h = Plateau(h, x, z, 19f, 252f, 6f, 7f, 6.6f);
        h = Plateau(h, x, z, -20f, 240f, 5f, 5f, 6.4f);
        // ---- the Causeway: low ground and a tidal crossing, with stacks standing off in the sea either side
        h = Channel(h, x, z, -16f, 279f, 16f, 281f, 5f, -0.4f);
        h = Plateau(h, x, z, -24f, 272f, 3.6f, 3.6f, 4.4f);
        h = Plateau(h, x, z, 25f, 286f, 4.2f, 4.2f, 6.0f);
        h = Plateau(h, x, z, -27f, 290f, 3f, 3f, 3.0f);
        // ---- the Crown: a broad rise, a higher dais at its north end
        h = Plateau(h, x, z, 0f, 331f, 24f, 25f, 4.5f);
        h = Plateau(h, x, z, 0f, 351f, 8f, 4.5f, 6.0f);
        h = Plateau(h, x, z, -17f, 318f, 4f, 4f, 6.2f);
        h = Plateau(h, x, z, 17f, 344f, 4f, 4f, 6.2f);

        foreach (var r in LongRamps) h = Ramp(h, x, z, r[0], r[1], r[2], r[3], r[4], r[5], r[6]);
        h = Mathf.Lerp(h, 0.9f, Mathf.Clamp01(Mathf.InverseLerp(8f, 4f, Vector2.Distance(new Vector2(x, z), SpawnXZ))));
        h += 0.14f * (float)System.Math.Tanh(h / 0.07f);
        h = Mathf.Max(h, -0.75f);
        // Open water: a shelf of shallows following the coast, then the sea floor falls away.
        if (h <= -0.749f && inside < 0f) h = Shore(inside, 0f, 1f);
        return h;
    }

    static float LongSlope(float x, float z)
    {
        float c = LongH(x, z);
        return Mathf.Max(Mathf.Abs(LongH(x + 0.8f, z) - c), Mathf.Abs(LongH(x - 0.8f, z) - c), Mathf.Abs(LongH(x, z + 0.8f) - c), Mathf.Abs(LongH(x, z - 0.8f) - c)) / 0.8f;
    }

    static Vector3 LongGround(float x, float z, float lift = 0f) => DemoOrigin + new Vector3(x, LongH(x, z) + lift, z);

    // x, z, yaw: cover where the fights are
    static readonly Vector3[] LongCrates =
    {
        new Vector3(-4, 19, 10), new Vector3(5, 34, 80), new Vector3(-2, 47, -20), new Vector3(8, 50, 60), new Vector3(2, 70, 0), new Vector3(-9, 79, 35), new Vector3(9, 93, -50),
        new Vector3(0, 115, 0), new Vector3(-7, 119, 60), new Vector3(7, 119, -60), new Vector3(-8, 127, 90), new Vector3(8, 127, 90),
        new Vector3(-10, 150, 15), new Vector3(4, 154, -40), new Vector3(14, 148, 80), new Vector3(-4, 168, 0), new Vector3(10, 172, 50), new Vector3(-12, 173, -30),
        new Vector3(-3, 196, 20), new Vector3(4, 211, -60), new Vector3(-2, 221, 40),
        new Vector3(-12, 242, 30), new Vector3(2, 246, -20), new Vector3(12, 244, 70), new Vector3(-6, 254, 0), new Vector3(8, 255, 90), new Vector3(-16, 251, -45),
        new Vector3(-3, 272, 10), new Vector3(4, 288, -30),
        new Vector3(-10, 318, 20), new Vector3(9, 322, -50), new Vector3(-14, 334, 80), new Vector3(13, 336, 10), new Vector3(0, 326, 0), new Vector3(-6, 344, -20), new Vector3(7, 346, 60),
    };
    static readonly Vector3[] LongArches = { new Vector3(1, 62, 0), new Vector3(-25, 104, 70), new Vector3(1, 183, 0), new Vector3(0, 296, 0) };   // x, z, yaw
    static readonly Vector3[] LongSpires = // x, z, scale
    {
        new Vector3(-33, 18, 1.0f), new Vector3(31, 30, 1.3f), new Vector3(-35, 52, 1.2f), new Vector3(34, 74, 1.0f), new Vector3(-34, 88, 1.4f), new Vector3(33, 112, 1.1f),
        new Vector3(-34, 138, 1.2f), new Vector3(35, 156, 1.0f), new Vector3(-35, 180, 1.3f), new Vector3(34, 232, 1.1f), new Vector3(-35, 228, 1.4f), new Vector3(35, 262, 1.0f),
        new Vector3(-18, 262, 0.9f), new Vector3(15, 296, 1.0f), new Vector3(-24, 306, 1.2f), new Vector3(26, 314, 1.3f), new Vector3(-28, 340, 1.4f), new Vector3(27, 352, 1.2f),
        new Vector3(-14, 362, 1.5f), new Vector3(14, 363, 1.6f),
    };
    // Who stands where: unit, x, z, and which stretch they belong to.
    static readonly (string unit, float x, float z, int beat)[] LongEnemies =
    {
        ("runt", 17, 29, 1), ("runt", 21, 33, 1), ("raider", 20, 30, 1), ("runt", -16, 27, 1), ("kite", 4, 34, 1),
        ("raider", -23, 60, 2), ("raider", 24, 60, 2), ("hound", -2, 58, 2), ("hound", 5, 66, 2), ("kite", 0, 54, 2),
        ("captain", 0, 96, 3), ("raider", -23, 86, 3), ("raider", 23, 84, 3), ("runt", -6, 92, 3), ("runt", 7, 94, 3),
        ("raider", -8, 124, 4), ("raider", 8, 126, 4), ("runt", 0, 118, 4), ("runt", -5, 150, 4), ("runt", 6, 152, 4), ("raider", -22, 166, 4), ("raider", 6, 171, 4), ("hound", -8, 168, 4),
        ("kite", 0, 150, 4), ("kite", 4, 170, 4),
        ("raider", -8.5f, 207, 5), ("raider", 9, 215, 5), ("hound", 1, 198, 5), ("hound", -1, 214, 5), ("kite", 2, 194, 5), ("kite", -2, 208, 5), ("kite", 3, 220, 5),
        ("strider", 0, 250, 6), ("raider", 19, 252, 6), ("raider", -20, 240, 6), ("runt", -8, 242, 6), ("runt", 6, 240, 6), ("runt", -3, 254, 6), ("runt", 12, 248, 6), ("hound", -14, 250, 6),
        ("runt", -3, 274, 7), ("runt", 4, 286, 7), ("kite", -10, 280, 7), ("kite", 10, 278, 7), ("raider", 25, 286, 7),
        ("queen", 0, 351, 8), ("captain", 0, 336, 8), ("raider", -17, 318, 8), ("raider", 17, 344, 8), ("hound", -8, 326, 8), ("hound", 8, 328, 8),
    };

    static bool LongKeepOut(float x, float z, float margin)
    {
        foreach (var c in LongCrates) if (Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y)) < margin) return true;
        foreach (var e in LongEnemies) if (Vector2.Distance(new Vector2(x, z), new Vector2(e.x, e.z)) < margin) return true;
        foreach (var a in LongArches) if (Vector2.Distance(new Vector2(x, z), new Vector2(a.x, a.y)) < margin + 9f) return true;
        foreach (var sp in LongSpires) if (Vector2.Distance(new Vector2(x, z), new Vector2(sp.x, sp.y)) < margin + 2.5f) return true;
        return Vector2.Distance(new Vector2(x, z), SpawnXZ) < margin + 3f || OnLongRamp(x, z, margin * 0.6f);
    }

    /// Everything that stands on the long island, by the first island's own recipe at the same density.
    static void BuildLong(Transform world)
    {
        var root = Group("DemoLevel", world);
        var t = root.transform;
        demoPlanned.Clear(); demoTops.Clear(); demoShapes.Clear();
        for (int b = 0; b < biomeRock.Length; b++) biomeRock[b] = null;      // this runs before the terrain is built, so it owns the reset
        worldMats.Clear();
        const float scale = LongLength / 140f;                               // how much more of everything than the first island had

        demoSpawn = Group("DemoSpawn", t).transform;
        demoSpawn.position = LongGround(SpawnXZ.x, SpawnXZ.y, 0.3f);
        demoObjective = Group("DemoObjective", t).transform;
        demoObjective.position = LongGround(0, 340);

        var landmarks = Group("Landmarks", t);
        foreach (var a in LongArches)
        {
            // An arch stands on the lower of its two feet, about 11.5 m either side of its centre.
            var along = Quaternion.Euler(0, a.z, 0) * Vector3.right;
            float low = float.MaxValue;
            foreach (float d in new[] { -13f, -11f, -9f, 9f, 11f, 13f }) low = Mathf.Min(low, LongH(a.x + along.x * d, a.y + along.z * d));
            Landmark(landmarks.transform, "arch", DemoOrigin + new Vector3(a.x, low - 0.9f, a.y), a.z, 1f, true);
        }
        foreach (var sp in LongSpires)
        {
            float low = LongH(sp.x, sp.y);
            for (int i = 0; i < 8; i++) low = Mathf.Min(low, LongH(sp.x + Mathf.Cos(i * 0.785f) * 3f * sp.z, sp.y + Mathf.Sin(i * 0.785f) * 3f * sp.z));
            Landmark(landmarks.transform, "spire", DemoOrigin + new Vector3(sp.x, low - 0.35f, sp.y), Rand(0, 360), sp.z, true);
        }

        // Giant fungus trees, with pools and waterfalls on the larger ones; a grove of them on the High Meadow.
        var fungi = Group("Fungi", t);
        int placed = 0, guard = 0;
        while (placed < (int)(18 * scale) && guard++ < 3000)
        {
            float x = Rand(-36, 36), z = Rand(6, LongLength - 6);
            if (LongKeepOut(x, z, 7f) || LongH(x, z) < -0.2f || LongSlope(x, z) > 0.45f) continue;
            // Each stretch has its own amount of forest: thick in the gorge and on the meadow, thin on the overlook and
            // the terraces, none on the causeway, a few round the edge of the Crown.
            float forest = z < 45f ? 1f : z < 70f ? 0.6f : z < 110f ? 0.8f : z < 138f ? 0.3f : z < 180f ? 0.4f : z < 230f ? 1.2f : z < 262f ? 1.8f : z < 296f ? 0f : 0.3f;
            if (Rand(0f, 1.8f) > forest) continue;
            float s = Rand(0.8f, 1.6f) * (z > 236f && z < 258f ? 1.25f : 1f);
            float low = LongH(x, z);
            for (int i = 0; i < 8; i++) low = Mathf.Min(low, LongH(x + Mathf.Cos(i * 0.785f) * 2.4f * s, z + Mathf.Sin(i * 0.785f) * 2.4f * s));
            Fungus(fungi.transform, DemoOrigin + new Vector3(x, low - 0.2f, z), s, true, s > 1.15f ? 2 : 0);
            // No two alike: some tall and narrow, some squat and wide, each leaning its own way, in one of three colours.
            var made = fungi.transform.GetChild(fungi.transform.childCount - 1);
            float build = Rand(0.78f, 1.3f);
            made.localScale = Vector3.Scale(made.localScale, new Vector3(1f / Mathf.Sqrt(build), build, 1f / Mathf.Sqrt(build)));
            made.rotation = Quaternion.Euler(Rand(-7f, 7f), Rand(0, 360), Rand(-7f, 7f));
            int hue = placed % 3;
            if (hue != 0)
                foreach (var r in made.GetComponentsInChildren<MeshRenderer>())
                {
                    var src = r.sharedMaterial;
                    if (src == null || !src.name.StartsWith("fungus") || !src.HasProperty("_BaseColor")) continue;
                    string key = src.name + "_hue" + hue;
                    if (!worldMats.TryGetValue(key, out var tinted) || tinted == null)
                    {
                        tinted = new Material(src);
                        tinted.SetColor("_BaseColor", hue == 1 ? new Color(0.50f, 1.15f, 1.25f) : new Color(1.25f, 0.62f, 1.15f));     // teal, and rose
                        UnityEditor.AssetDatabase.CreateAsset(tinted, Gen + "/" + key + ".mat");
                        worldMats[key] = tinted;
                    }
                    r.sharedMaterial = tinted;
                }
            placed++;
        }
        // Far out in the sea all round: giant fungi, arches and spires fading into the haze.
        for (int i = 0; i < 60; i++)
        {
            float ang = Rand(0, Mathf.PI * 2), r = Rand(0f, 1f);
            var p = DemoOrigin + new Vector3(Mathf.Cos(ang) * Mathf.Lerp(80f, 520f, r), -0.5f, LongLength * 0.5f + Mathf.Sin(ang) * Mathf.Lerp(230f, 620f, r));
            if (i % 3 == 0) Fungus(fungi.transform, p, Mathf.Lerp(2f, 6.5f, r) * Rand(0.8f, 1.2f), false, 2);
            else if (i % 3 == 1) Landmark(landmarks.transform, "spire", p + Vector3.down * 2.1f, Rand(0, 360), Mathf.Lerp(1.8f, 6f, r) * Rand(0.7f, 1.3f), false);
            else Landmark(landmarks.transform, "arch", p + Vector3.down * 2.1f, Rand(0, 360), Mathf.Lerp(1.6f, 5f, r) * Rand(0.8f, 1.2f), false);
        }

        // Boulders.
        var rocks = Group("Rocks", t);
        for (int i = 0; i < (int)(46 * scale); i++)
        {
            float x = Rand(-38, 38), z = Rand(3, LongLength - 3);
            if (LongKeepOut(x, z, 3.5f) || LongSlope(x, z) > 0.7f || LongH(x, z) < -0.6f) continue;
            bool meshy = HasModel("boulder") && i % 3 != 0;
            var m = Model(meshy ? "boulder" : "rock", rocks.transform);
            if (m == null) break;
            float s = Rand(0.6f, 2.2f);
            m.transform.position = LongGround(x, z, -0.1f * s);
            m.transform.rotation = Quaternion.Euler(Rand(-8, 8), Rand(0, 360), Rand(-8, 8));
            m.transform.localScale = Vector3.one * s;
            if (!meshy) foreach (var r in m.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = rock;
            AddMeshColliders(m);
        }

        // Purple grass in clumps, glowing bulb plants, and coral along the shoreline.
        var flora = Group("Flora", t);
        for (int c = 0; c < (int)(85 * scale); c++)
        {
            float cx = Rand(-38, 38), cz = Rand(3, LongLength - 3);
            if (LongH(cx, cz) < 0.1f || Vector2.Distance(new Vector2(cx, cz), SpawnXZ) < 2.5f) continue;
            int n = rng.Next(3, 8);
            for (int k = 0; k < n; k++)
            {
                float x = cx + Rand(-2.4f, 2.4f), z = cz + Rand(-2.4f, 2.4f);
                if (LongH(x, z) < 0.02f || LongSlope(x, z) > 0.8f) continue;
                Tuft(flora.transform, LongGround(x, z, -0.03f), Rand(0.6f, 1.5f));
            }
        }
        for (int i = 0; i < (int)(70 * scale); i++)
        {
            float x = Rand(-38, 38), z = Rand(3, LongLength - 3);
            if (LongH(x, z) < 0f || LongSlope(x, z) > 0.6f || LongKeepOut(x, z, 1.5f)) continue;
            var m = Model("bulbplant", flora.transform);
            if (m == null) break;
            m.transform.position = LongGround(x, z, -0.05f);
            m.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
            m.transform.localScale = Vector3.one * Rand(0.7f, 1.5f);
        }
        for (int i = 0; i < (int)(170 * scale); i++)
        {
            float x = Rand(-40, 40), z = Rand(1, LongLength - 1);
            float h = LongH(x, z);
            if (h > 0.45f || h < -0.6f) continue;                 // coral hugs the shoreline
            Coral(flora.transform, LongGround(x, z), Rand(0.7f, 1.6f));
        }

        // Cover crates.
        var crates = Group("Crates", t);
        foreach (var c in LongCrates)
        {
            var crate = Group("Crate", crates.transform);
            float low = LongH(c.x, c.y);
            for (int i = 0; i < 8; i++) low = Mathf.Min(low, LongH(c.x + Mathf.Cos(i * 0.785f) * 1.1f, c.y + Mathf.Sin(i * 0.785f) * 1.1f));
            crate.transform.position = DemoOrigin + new Vector3(c.x, low - 0.03f, c.y);
            crate.transform.rotation = Quaternion.Euler(0, c.z, 0);
            var m = Model("crate", crate.transform);
            if (m != null) AddMeshColliders(m);
            else Prim(PrimitiveType.Cube, "Body", crate.transform, new Vector3(0, 0.6f, 0), new Vector3(2.3f, 1.2f, 1.15f), cream);
        }

        // ---- Things that are only in one place, so each stretch has something of its own.
        // Ruins of whoever was here first: old pale stone, half sunk, a different piece in each stretch.
        var ruins = Group("Ruins", t);
        void Ruin(string name, float x, float z, float yaw, float size, float sink = 0.3f, bool collide = true)
        {
            if (!HasModel(name)) return;
            Landmark(ruins.transform, name, LongGround(x, z, -sink), yaw, size, collide);
        }
        Ruin("citadel_head", 14f, 87f, 210f, 0.9f, 1.1f);                   // a fallen face in the basin's pool
        Ruin("citadel_colonnade", -9f, 130f, 0f, 0.6f);                     // the overlook's back row
        Ruin("citadel_obelisk", 12f, 116f, 0f, 0.5f);
        Ruin("citadel_stairs", 22f, 152f, 90f, 0.5f, 0.5f);                 // steps that lead nowhere now
        Ruin("citadel_tower", -22f, 166f, 20f, 0.5f, 0.2f);                 // a watchtower on the knuckle above the terraces
        Ruin("citadel_arch", 1f, 226f, 0f, 0.75f, 0.4f);                    // the gorge's far door
        Ruin("citadel_rubble", -3f, 204f, 40f, 1.2f); Ruin("citadel_rubble", 5f, 214f, 120f, 0.9f);
        Ruin("citadel_statue", 19f, 252f, 250f, 0.42f, 0.2f);               // a guardian on the meadow's lip
        Ruin("citadel_wall", -8f, 247f, 30f, 0.6f); Ruin("citadel_wall", 9f, 240f, -50f, 0.6f);
        Ruin("citadel_obelisk", -8f, 270f, 0f, 0.6f); Ruin("citadel_obelisk", 8f, 288f, 0f, 0.6f);      // markers along the causeway
        Ruin("citadel_dais", 0f, 330f, 0f, 1.5f, 1.6f);                     // the old floor of the Crown
        for (int k = 0; k < 8; k++)
        {
            float ang = (k + 0.5f) / 8f * Mathf.PI * 2f;
            if (Mathf.Abs(Mathf.Sin(ang)) < 0.25f && Mathf.Cos(ang) < 0f) continue;      // leave the way in open
            Ruin("citadel_tusk", Mathf.Sin(ang) * 20f, 331f + Mathf.Cos(ang) * 21f, ang * Mathf.Rad2Deg + 180f, 0.55f, 0.3f);
        }
        // Crystals: amber light on the causeway's stacks and round the Crown, nowhere else.
        var crystals = Group("Crystals", t);
        foreach (var c in new[] { new Vector3(-24, 272, 1.0f), new Vector3(25, 286, 1.3f), new Vector3(-27, 290, 0.8f), new Vector3(-12, 276, 0.7f), new Vector3(13, 284, 0.7f),
                                  new Vector3(-17, 318, 1.2f), new Vector3(17, 344, 1.2f), new Vector3(-21, 340, 0.9f), new Vector3(21, 322, 0.9f), new Vector3(-6, 356, 1.1f), new Vector3(6, 356, 1.1f) })
            Crystals(crystals.transform, DemoOrigin.x + c.x, DemoOrigin.z + c.y, c.z);
        MatchCitadelStone(t);

        // The Queen's ship hangs beyond the Crown, and three more fly slow loops over the island.
        Landmark(t, "varrik_ship", DemoOrigin + new Vector3(8, 44, LongLength + 14), 80f, 1.7f, false);
        if (HasModel("varrik_ship"))
            for (int i = 0; i < 3; i++)
            {
                var skiff = Landmark(t, "varrik_ship", DemoOrigin + new Vector3(0, 60, 150), 0, 0.32f, false);
                skiff.name = "SkyTraffic" + i;
                var fly = skiff.AddComponent<SkyTraffic>();
                fly.centre = DemoOrigin + new Vector3(i == 1 ? -30 : 30, 55 + i * 15, 110 + i * 80);
                fly.radiusX = 150 + i * 40; fly.radiusZ = 130 + i * 40; fly.speed = 16 + i * 5; fly.phase = i * 0.37f; fly.clockwise = i == 1;
                fly.modelYaw = -90f;
            }

        foreach (var e in LongEnemies) demoPlanned.Add((e.unit, LongGround(e.x, e.z), e.beat));
    }

    /// The named stretches of the long island. Walking into one announces it and makes it the place you come back to.
    static IEnumerable<WorldArea> LongWorldAreas(Transform root)
    {
        var defs = new (string name, float z, float r, float rz)[]
        {
            ("THE LANDING", 22, 26, 8), ("THE NARROWS", 58, 15, 46), ("THE SUNKEN BASIN", 88, 18, 74), ("THE OVERLOOK", 123, 16, 108),
            ("THE TERRACES", 160, 22, 141), ("THE GORGE", 205, 24, 184), ("THE HIGH MEADOW", 247, 19, 234), ("THE CAUSEWAY", 279, 14, 268), ("THE CROWN", 332, 31, 303),
        };
        foreach (var d in defs)
        {
            var marker = Group("Respawn " + d.name, root).transform;
            float rx = d.name == "THE HIGH MEADOW" ? -9f : 0f;
            marker.position = LongGround(rx, d.rz, 0.4f);
            yield return new WorldArea { name = d.name, region = "LUMINOUS TIDES", centre = new Vector2(DemoOrigin.x, DemoOrigin.z + d.z), radius = d.r, respawn = marker };
        }
    }
}
