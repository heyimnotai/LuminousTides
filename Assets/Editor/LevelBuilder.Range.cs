using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// The combat test range: a training deck out at sea, well away from the world, built for judging how the shooting
/// and the movement feel. South to north on a 96 m square:
///   west strip   - a firing lane with practice targets at 10, 20, 35 and 55 m (one of them moving)
///   east strip   - a movement course: steps to mantle, gaps to jump, a tower, and a long ramp to slide down
///   the middle   - an arena with cover and two raised decks, where the pads' enemies arrive from the north end
///   by the start - two rows of pads: press E at one to call something in, clear the arena, or refill
public static partial class LevelBuilder
{
    public static readonly Vector3 RangeCentre = new Vector3(-320f, 2f, 100f);      // the deck's top surface is at y = 2
    static Transform rangeSpawn, rangeCentreMarker;
    static readonly List<Transform> rangeSpawnPoints = new List<Transform>();
    static readonly List<(string label, Vector3 local, string[] units, string action)> rangePads = new List<(string, Vector3, string[], string)>();

    static GameObject Block(Transform parent, string name, Vector3 local, Vector3 size, Material mat, bool collider = true)
    {
        // `local` is the centre of the block's footprint on the deck; it stands on the deck.
        return Prim(PrimitiveType.Cube, name, parent, RangeCentre + new Vector3(local.x, local.y + size.y * 0.5f, local.z), size, mat, collider);
    }

    static TextMesh Sign(Transform parent, Vector3 world, string text, float size, Color colour)
    {
        var go = new GameObject("Sign");
        go.transform.SetParent(parent, false);
        go.transform.position = world;
        var tm = go.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        tm.text = text; tm.fontSize = 72; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = colour;
        tm.fontStyle = FontStyle.Bold;
        return tm;
    }

    static void BuildRange(Transform world)
    {
        rangeSpawnPoints.Clear();
        rangePads.Clear();
        var root = Group("TestRange", world);
        var deckMat = Mat("RangeDeck", new Color(0.13f, 0.16f, 0.24f), 0.35f);
        var lineMat = Mat("RangeLine", new Color(0.85f, 0.83f, 0.76f), 0.3f);
        var padMat = Mat("RangePad", new Color(0.1f, 0.5f, 0.55f), 0.5f, new Color(0.1f, 1.4f, 1.3f));
        var utilMat = Mat("RangePadUtility", new Color(0.6f, 0.4f, 0.1f), 0.5f, new Color(1.6f, 0.8f, 0.1f));
        var targetMat = Mat("RangeTarget", new Color(0.86f, 0.84f, 0.78f), 0.3f);
        var headMat = Mat("RangeTargetHead", new Color(0.85f, 0.2f, 0.12f), 0.3f);

        // ---- the deck and its wall
        Prim(PrimitiveType.Cube, "Deck", root.transform, RangeCentre + Vector3.down * 3f, new Vector3(96f, 6f, 96f), deckMat);
        foreach (var (pos, size) in new[] { (new Vector3(0, 0, 48.5f), new Vector3(98, 3, 1)), (new Vector3(0, 0, -48.5f), new Vector3(98, 3, 1)),
                                            (new Vector3(48.5f, 0, 0), new Vector3(1, 3, 98)), (new Vector3(-48.5f, 0, 0), new Vector3(1, 3, 98)) })
        {
            Block(root.transform, "Wall", pos, size, cream);
            Block(root.transform, "WallStripe", pos + Vector3.up * 3f, new Vector3(size.x, 0.25f, size.z), red, false);
        }
        // Painted lines dividing the three strips.
        foreach (float x in new[] { -20f, 20f })
            Block(root.transform, "Line", new Vector3(x, 0, 0), new Vector3(0.25f, 0.02f, 92f), lineMat, false);

        // ---- start
        rangeSpawn = Group("RangeSpawn", root.transform).transform;
        rangeSpawn.position = RangeCentre + new Vector3(0, 0.2f, -40f);
        rangeCentreMarker = Group("RangeCentre", root.transform).transform;
        rangeCentreMarker.position = RangeCentre + new Vector3(0, 0, 8f);
        Sign(root.transform, RangeCentre + new Vector3(0, 6.5f, -47.6f), "COMBAT TEST RANGE", 0.16f, Color.white).transform.rotation = Quaternion.Euler(0, 180, 0);

        // ---- firing lane (west): a firing line, then targets at measured distances
        var lane = Group("FiringLane", root.transform);
        Block(lane.transform, "FiringLine", new Vector3(-32f, 0, -34f), new Vector3(18f, 0.03f, 0.4f), red, false);
        Block(lane.transform, "Bench", new Vector3(-32f, 0, -35.2f), new Vector3(8f, 1.0f, 0.8f), cream);
        Sign(lane.transform, RangeCentre + new Vector3(-32f, 3.4f, -30f), "FIRING LANE", 0.1f, new Color(0.6f, 1f, 0.95f));
        foreach (var (dist, x, slide) in new[] { (10f, -37f, 0f), (20f, -28f, 0f), (20f, -37f, 3.5f), (35f, -32f, 0f), (55f, -27f, 0f), (55f, -37f, 5f) })
        {
            var t = Group("Target", lane.transform);
            t.transform.position = RangeCentre + new Vector3(x, 0, -34f + dist);
            Prim(PrimitiveType.Cube, "Post", t.transform, new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.5f, 0.5f), dark, false);
            Prim(PrimitiveType.Capsule, "Body", t.transform, new Vector3(0, 1.05f, 0), new Vector3(0.75f, 0.62f, 0.45f), targetMat);
            var head = Prim(PrimitiveType.Sphere, "Head", t.transform, new Vector3(0, 1.9f, 0), Vector3.one * 0.42f, headMat);
            head.AddComponent<Hitbox>().multiplier = 2f;
            t.AddComponent<Health>().maxHealth = 300f;
            var dummy = t.AddComponent<TargetDummy>();
            dummy.slideDistance = slide;
            dummy.readout = Sign(t.transform, t.transform.position + Vector3.up * 3.0f, "", 0.07f, new Color(1f, 0.85f, 0.4f));
            Block(lane.transform, "Mark", new Vector3(-42.5f, 0, -34f + dist), new Vector3(1.6f, 0.02f, 0.3f), lineMat, false);
            Sign(lane.transform, RangeCentre + new Vector3(-44.5f, 1.4f, -34f + dist), dist + " m", 0.07f, Color.white);
        }

        // ---- movement course (east): each block is a climb, a gap or a drop
        var course = Group("MovementCourse", root.transform);
        Sign(course.transform, RangeCentre + new Vector3(32f, 3.4f, -36f), "MOVEMENT COURSE", 0.1f, new Color(0.6f, 1f, 0.95f));
        Block(course.transform, "Step1", new Vector3(32f, 0, -30f), new Vector3(6f, 1.0f, 4f), cream);          // step up
        Block(course.transform, "Step2", new Vector3(32f, 0, -25f), new Vector3(6f, 2.1f, 4f), cream);          // mantle
        Block(course.transform, "Step3", new Vector3(32f, 0, -20f), new Vector3(6f, 3.3f, 4f), cream);          // mantle again
        Block(course.transform, "Gap1", new Vector3(32f, 0, -11f), new Vector3(6f, 3.3f, 5f), navy);            // 4.5 m jump
        Block(course.transform, "Gap2", new Vector3(32f, 0, 0f), new Vector3(6f, 3.3f, 5f), navy);              // 6 m jump
        Block(course.transform, "Gap3", new Vector3(32f, 0, 13f), new Vector3(6f, 3.3f, 5f), navy);             // 8 m: needs the second jump
        Block(course.transform, "Tower", new Vector3(32f, 0, 21f), new Vector3(6f, 6.2f, 5f), cream);           // jump, jump again, catch the edge
        var ramp = Block(course.transform, "SlideRamp", new Vector3(32f, 0, 33.2f), new Vector3(6f, 0.5f, 20.6f), red);
        ramp.transform.position = RangeCentre + new Vector3(32f, 3.0f, 33.3f);
        ramp.transform.rotation = Quaternion.Euler(17.6f, 0, 0);                                               // from the tower's top down to the deck
        Block(course.transform, "Wall1", new Vector3(24f, 0, -8f), new Vector3(0.6f, 2.2f, 8f), cream);         // a low wall to vault beside the course
        Block(course.transform, "Wall2", new Vector3(41f, 0, -22f), new Vector3(5f, 1.4f, 0.6f), cream);
        Block(course.transform, "Wall3", new Vector3(41f, 0, -16f), new Vector3(5f, 2.4f, 0.6f), cream);
        Block(course.transform, "Tunnel", new Vector3(41f, 1.45f, 2f), new Vector3(5f, 1.6f, 9f), navy);        // crouch or slide under it
        Block(course.transform, "TunnelL", new Vector3(43.6f, 0, 2f), new Vector3(0.4f, 1.45f, 9f), navy);
        Block(course.transform, "TunnelR", new Vector3(38.4f, 0, 2f), new Vector3(0.4f, 1.45f, 9f), navy);

        // ---- arena (middle): cover, two raised decks with ramps, enemies come in from the north end
        var arena = Group("Arena", root.transform);
        foreach (var c in new[] { new Vector3(-8, -8, 20), new Vector3(7, -4, -30), new Vector3(0, 4, 90), new Vector3(-12, 10, 0), new Vector3(12, 12, 60), new Vector3(-4, 18, 40),
                                  new Vector3(6, 22, -10), new Vector3(-14, -18, 80), new Vector3(14, -16, 10) })
        {
            var crate = Group("Crate", arena.transform);
            crate.transform.position = RangeCentre + new Vector3(c.x, -0.03f, c.y);
            crate.transform.rotation = Quaternion.Euler(0, c.z, 0);
            var m = Model("crate", crate.transform);
            if (m != null) AddMeshColliders(m);
            else Prim(PrimitiveType.Cube, "Body", crate.transform, new Vector3(0, 0.6f, 0), new Vector3(2.3f, 1.2f, 1.15f), cream);
        }
        Block(arena.transform, "DeckWest", new Vector3(-13f, 0, 0f), new Vector3(7f, 2.0f, 8f), navy);
        var rampW = Block(arena.transform, "RampWest", new Vector3(-13f, 0, -8f), new Vector3(4f, 0.4f, 8.4f), cream);
        rampW.transform.position = RangeCentre + new Vector3(-13f, 0.95f, -8f);
        rampW.transform.rotation = Quaternion.Euler(-14f, 0, 0);
        Block(arena.transform, "DeckEast", new Vector3(13f, 0, 16f), new Vector3(7f, 2.0f, 8f), navy);
        var rampE = Block(arena.transform, "RampEast", new Vector3(13f, 0, 24f), new Vector3(4f, 0.4f, 8.4f), cream);
        rampE.transform.position = RangeCentre + new Vector3(13f, 0.95f, 24f);
        rampE.transform.rotation = Quaternion.Euler(14f, 0, 0);
        Block(arena.transform, "Pillar1", new Vector3(-3f, 0, -14f), new Vector3(1.6f, 4f, 1.6f), cream);
        Block(arena.transform, "Pillar2", new Vector3(4f, 0, 12f), new Vector3(1.6f, 4f, 1.6f), cream);
        for (int i = 0; i < 5; i++)
        {
            var sp = Group("RangeSpawnPoint" + i, arena.transform).transform;
            sp.position = RangeCentre + new Vector3(-14f + i * 7f, 0, 38f);
            rangeSpawnPoints.Add(sp);
            Prim(PrimitiveType.Cylinder, "SpawnMark", sp, new Vector3(0, 0.02f, 0), new Vector3(3f, 0.02f, 3f), red, false);
        }

        // ---- the pads, in two rows in front of the start
        var pads = Group("Pads", root.transform);
        var defs = new (string label, string[] units, string action)[]
        {
            ("STALKERS", new[] { "stalker", "stalker", "stalker" }, ""), ("SPITTERS", new[] { "spitter", "spitter" }, ""), ("BURSTERS", new[] { "burster", "burster", "burster" }, ""),
            ("ELITE", new[] { "elite" }, ""), ("TIDE ALPHA", new[] { "alpha" }, ""), ("MIXED PACK", new[] { "stalker", "stalker", "spitter", "burster", "elite" }, ""),
            ("VARRIK SQUAD", new[] { "runt", "runt", "raider" }, ""), ("HOUNDS", new[] { "hound", "hound" }, ""), ("KITES", new[] { "kite", "kite" }, ""),
            ("CAPTAIN", new[] { "captain" }, ""), ("STRIDER", new[] { "strider" }, ""), ("CORSAIR QUEEN", new[] { "queen" }, ""),
            ("CLEAR", new string[0], "clear"), ("REFILL", new string[0], "refill"),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            bool utility = defs[i].action != "";
            Vector3 local = utility ? new Vector3(i == defs.Length - 2 ? -4f : 4f, 0, -43.5f) : new Vector3(-13.75f + (i % 6) * 5.5f, 0, i < 6 ? -30f : -35f);
            var pad = Prim(PrimitiveType.Cylinder, "Pad " + defs[i].label, pads.transform, RangeCentre + local + Vector3.up * 0.03f, new Vector3(2.2f, 0.03f, 2.2f), utility ? utilMat : padMat, false);
            pad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var sign = Sign(pads.transform, RangeCentre + local + Vector3.up * (utility ? 1.5f : i < 6 ? 2.3f : 1.5f), defs[i].label, 0.055f, utility ? new Color(1f, 0.8f, 0.4f) : new Color(0.7f, 1f, 0.97f));
            if (utility) sign.transform.rotation = Quaternion.Euler(0, 180, 0);        // these sit behind the start, so they face north
            rangePads.Add((defs[i].label, local, defs[i].units, defs[i].action));
        }
    }

    /// The range's director, with a dormant copy of every kind of enemy to call in.
    static void BuildRangeDirector(Transform templates, AnimatorController tideborn, GameObject alphaTemplate)
    {
        var store = Group("RangeTemplates", templates);
        var names = new List<string>();
        var objects = new List<GameObject>();
        void Add(string name, GameObject go) { go.name = "Range " + name; go.SetActive(false); names.Add(name); objects.Add(go); }
        Add("stalker", BuildEnemy(store.transform, Vector3.zero, tideborn, EnemyKind.Stalker, false));
        Add("elite", BuildEnemy(store.transform, Vector3.zero, tideborn, EnemyKind.Stalker, true));
        Add("spitter", BuildEnemy(store.transform, Vector3.zero, tideborn, EnemyKind.Spitter, false));
        Add("burster", BuildEnemy(store.transform, Vector3.zero, tideborn, EnemyKind.Burster, false));
        Add("alpha", BuildEnemy(store.transform, Vector3.zero, tideborn, EnemyKind.Boss, false));
        var worldQueen = varrikQueen;
        foreach (string unit in VarrikUnits)
        {
            var go = BuildVarrik(store.transform, Vector3.zero, unit);
            if (unit == "queen" && worldQueen != null) go.GetComponent<EnemyAI>().summonTemplates = worldQueen.summonTemplates;
            Add(unit, go);
        }
        varrikQueen = worldQueen;

        var root = Group("Range Director");
        var director = root.AddComponent<RangeDirector>();
        director.unitNames = names.ToArray();
        director.templates = objects.ToArray();
        director.spawnPoints = rangeSpawnPoints.ToArray();
        director.pads = rangePads.Select(p =>
        {
            var point = Group("PadPoint " + p.label, root.transform).transform;
            point.position = RangeCentre + p.local;
            return new RangeDirector.Pad { label = p.label, point = point, units = p.units, action = p.action };
        }).ToArray();
    }
}
