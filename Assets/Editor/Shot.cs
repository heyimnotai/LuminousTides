using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Renders preview screenshots of the main scene to /Screenshots (editor or batch mode).
public static class Shot
{
    [MenuItem("Luminous Tides/3. Capture Screenshots")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        Directory.CreateDirectory("Screenshots");
        var cam = Camera.main;
        var t = cam.transform;
        Vector3 p0 = t.position; Quaternion r0 = t.rotation;

        Render(cam, "Screenshots/player_view.png"); // warm-up: the first frame renders before textures are ready
        Render(cam, "Screenshots/player_view.png");

        t.SetPositionAndRotation(new Vector3(-9, 3.4f, 25), Quaternion.Euler(8, -5, 0));
        Render(cam, "Screenshots/enemy_view.png");

        t.SetPositionAndRotation(new Vector3(3, 2.4f, 100), Quaternion.Euler(-4, -5, 0));
        Render(cam, "Screenshots/station_view.png");

        // The layout from above (north is up), and one ground-level view of each area.
        t.SetPositionAndRotation(new Vector3(0, 150, 70), Quaternion.Euler(90, 0, 0));
        Render(cam, "Screenshots/level_top.png");
        t.SetPositionAndRotation(new Vector3(WorldMap.CentreX, 330, WorldMap.CentreZ), Quaternion.Euler(90, 0, 0));
        Render(cam, "Screenshots/world_top.png");
        // The test range: from the start, down the firing lane, along the movement course, and from above.
        Vector3 rc = LevelBuilder.RangeCentre;
        foreach (var (name, from, at) in new[] { ("range_start", new Vector3(0, 1.75f, -41), new Vector3(0, 2, 10)), ("range_lane", new Vector3(-32, 1.75f, -37), new Vector3(-32, 1.5f, 10)),
                                                  ("range_course", new Vector3(22, 3.5f, -38), new Vector3(34, 3, 10)), ("range_arena", new Vector3(0, 9, -26), new Vector3(0, 0, 14)),
                                                  ("range_top", new Vector3(0, 95, -1), new Vector3(0, 0, 0)) })
        {
            t.position = rc + from;
            t.LookAt(rc + at);
            Render(cam, "Screenshots/" + name + ".png");
        }
        // The demo level's blockout: plan, side elevation, and the view from where the player stands at each beat.
        Vector3 dz = LevelBuilder.DemoOrigin;
        t.SetPositionAndRotation(dz + new Vector3(0, 470, 186), Quaternion.Euler(90, 90, 0));
        Render(cam, "Screenshots/demo_plan.png");
        foreach (var (name, fx, fz, ax, az, ay) in new[]
        {
            ("demo_1_landing", 3f, 12f, -6f, 30f, 1.5f), ("demo_2_narrows", 3f, 43f, 1f, 62f, 3f), ("demo_3_basin", 0f, 74f, 0f, 112f, 2.5f),
            ("demo_4_terraces", -12f, 133f, 2f, 172f, 6f), ("demo_5_gorge", 0f, 187f, 2f, 222f, 5f), ("demo_6_meadow", -9f, 234f, 4f, 256f, 6f),
            ("demo_7_causeway", 0f, 262f, 0f, 300f, 4f), ("demo_8_crown", 0f, 307f, 0f, 352f, 8f), ("demo_9_back", 0f, 352f, 0f, 250f, 4f),
        })
        {
            t.position = dz + new Vector3(fx, LevelBuilder.H(dz.x + fx, dz.z + fz) - dz.y + 1.75f, fz);
            t.LookAt(dz + new Vector3(ax, ay, az));
            Render(cam, "Screenshots/" + name + ".png");
        }
        // Down the hand cannon's sights at the 10 m target, the way the game shows it when aiming.
        var cannonWeapon = Object.FindObjectsByType<Weapon>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(w => w.weaponName.Contains("HAND CANNON"));
        var carbineWeapon = Object.FindObjectsByType<Weapon>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(w => w.weaponName.Contains("CARBINE"));
        if (cannonWeapon != null && carbineWeapon != null)
        {
            carbineWeapon.viewModel.gameObject.SetActive(false);
            cannonWeapon.viewModel.gameObject.SetActive(true);
            Vector3 rest = cannonWeapon.viewModel.localPosition;
            cannonWeapon.viewModel.localPosition = cannonWeapon.aimPos;
            float fov = cam.fieldOfView;
            cam.fieldOfView = cannonWeapon.aimFov;
            t.position = rc + new Vector3(-37, 1.75f, -34);
            t.LookAt(rc + new Vector3(-37, 1.9f, -24));
            Render(cam, "Screenshots/range_cannon_ads.png");
            cam.fieldOfView = fov;
            cannonWeapon.viewModel.localPosition = rest;
            cannonWeapon.viewModel.gameObject.SetActive(false);
            carbineWeapon.viewModel.gameObject.SetActive(true);
        }
        var views = new (string, Vector3, Vector3)[]
        {
            ("w_tidewalk", new Vector3(36, 0, 87), new Vector3(60, 2, 92)),
            ("w_anchorage", new Vector3(92, 0, 86), new Vector3(136, 4, 60)),
            ("w_ship", new Vector3(128, 0, 78), new Vector3(136, 4, 58)),
            ("w_gully", new Vector3(116, 0, 128), new Vector3(114, 6, 170)),
            ("w_canopy", new Vector3(108, 0, 176), new Vector3(112, 12, 208)),
            ("w_descent", new Vector3(88, 0, 197), new Vector3(60, 9, 211)),
            ("w_highwatch", new Vector3(30, 0, 204), new Vector3(36, 22, 229)),
            ("w_view", new Vector3(50, 0, 198), new Vector3(118, 0, 90)),
            ("w_pass", new Vector3(1, 0, 138), new Vector3(14, 12, 180)),
            ("level_landing", new Vector3(3, 0, 12), new Vector3(-6, 1.5f, 30)),
            ("level_canyon", new Vector3(3, 0, 43), new Vector3(1, 3f, 62)),
            ("level_ridge", new Vector3(-17, 0, 63.5f), new Vector3(2, 0.5f, 88)),
            ("level_basin", new Vector3(0, 0, 74), new Vector3(0, 2.5f, 112)),
            ("level_station", new Vector3(0, 0, 113), new Vector3(0, 5f, 131)),
            ("varrik_party", new Vector3(20.2f, 0, 82.8f), new Vector3(23.2f, 3.0f, 84.3f)),
            ("varrik_scouts", new Vector3(16.5f, 0, 31.5f), new Vector3(20.5f, 3.3f, 31)),
            ("varrik_strider", new Vector3(-21, 0, 74), new Vector3(-31, 4.5f, 80)),
            ("varrik_queen", new Vector3(-23.5f, 0, 111), new Vector3(-28, 3.2f, 105)),
            ("varrik_hound", new Vector3(22.5f, 0, 80.5f), new Vector3(26, 2.2f, 82)),
        };
        foreach (var (name, from, lookAt) in views)
        {
            t.position = new Vector3(from.x, LevelBuilder.H(from.x, from.z) + 1.75f, from.z);
            t.LookAt(lookAt);
            Render(cam, "Screenshots/" + name + ".png");
        }

        // The boss only exists as an inactive template until the mission's last part: wake it for a portrait.
        foreach (var enemy in Resources.FindObjectsOfTypeAll<EnemyAI>())
        {
            if (enemy.kind != EnemyKind.Boss || enemy.gameObject.scene.name == null) continue;
            var parent = enemy.transform.parent;
            parent.gameObject.SetActive(true);
            enemy.gameObject.SetActive(true);
            enemy.transform.position = new Vector3(0, LevelBuilder.H(0, 20), 20);
            enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
            t.position = new Vector3(3.5f, LevelBuilder.H(0, 20) + 2.2f, 9.5f);
            t.LookAt(enemy.transform.position + Vector3.up * 2.6f);
            Render(cam, "Screenshots/boss_portrait.png");
            enemy.transform.position = Vector3.zero;
            enemy.gameObject.SetActive(false);
            break;
        }

        // Both feet of each arch, from a little way back and from close to the ground.
        var arches = GameObject.Find("Landmarks").transform.Cast<Transform>().Where(a => a.name.StartsWith("arch")).ToList();
        for (int i = 0; i < arches.Count; i++)
        {
            var a = arches[i];
            Vector3 back = -a.forward;
            Vector3 stand = a.position + back * 30f;
            t.position = new Vector3(stand.x, Mathf.Max(0f, LevelBuilder.H(stand.x, stand.z)) + 3f, stand.z);
            t.LookAt(a.position + Vector3.up * 6f);
            Render(cam, $"Screenshots/arch{i}_view.png");
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 foot = a.position + a.right * 11.5f * side;
                foot.y = Mathf.Max(0f, LevelBuilder.H(foot.x, foot.z));
                t.position = foot + back * 9f + Vector3.up * 2.2f;
                t.LookAt(foot + Vector3.up * 1.5f);
                Render(cam, $"Screenshots/arch{i}_foot{(side < 0 ? "L" : "R")}.png");
            }
        }

        // Shoreline close-ups: walk outward from the spawn until the ground meets the water, then look along and down at it.
        int shot = 0;
        foreach (float ang in new[] { 200f, 250f, 320f })
        {
            Vector3 dir = Quaternion.Euler(0, ang, 0) * Vector3.forward;
            Vector3 origin = new Vector3(LevelBuilder.SpawnXZ.x, 0, LevelBuilder.SpawnXZ.y);
            for (float d = 2f; d < 30f; d += 0.25f)
            {
                Vector3 p = origin + dir * d;
                if (LevelBuilder.H(p.x, p.z) > 0f) continue;
                Vector3 stand = origin + dir * (d - 3.5f);
                t.position = new Vector3(stand.x, LevelBuilder.H(stand.x, stand.z) + 1.5f, stand.z) + Vector3.Cross(Vector3.up, dir) * 1.5f;
                t.LookAt(new Vector3(p.x, 0f, p.z) + dir * 1.5f);
                Render(cam, $"Screenshots/shore{shot++}_view.png");
                break;
            }
        }

        // Looking down into the largest in-level fungus cup, and at it from the side.
        var fungus = GameObject.Find("Fungi").transform.Cast<Transform>().OrderByDescending(f => f.localScale.x).First(f => f.position.y > -0.4f);
        float fs = fungus.localScale.x;
        t.position = fungus.position + new Vector3(7f, 10.5f, -7f) * fs;
        t.LookAt(fungus.position + Vector3.up * 4.5f * fs);
        Render(cam, "Screenshots/fungus_view.png");
        // Each waterfall, seen from the side so any contact with the cap would show.
        int fallShot = 0;
        foreach (var spout in fungus.GetComponentsInChildren<Transform>().Where(x => x.name.StartsWith("Spout")).Take(2))
        {
            Vector3 outward = spout.position - fungus.position; outward.y = 0; outward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            t.position = spout.position + side * 9f * fs + outward * 2f * fs + Vector3.up * 3.5f * fs;
            t.LookAt(spout.position + Vector3.up * 3.2f * fs);
            Render(cam, $"Screenshots/fungus_fall{fallShot++}.png");
        }

        t.SetPositionAndRotation(p0, r0);
        Debug.Log("[LT] Screenshots written.");
    }

    static void Render(Camera cam, string path)
    {
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = prev;
        Object.DestroyImmediate(tex);
        rt.Release();
    }
}
