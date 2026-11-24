using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// Scripted smoke test, active only when the game is launched with "-autotest".
/// Plays through the whole mission (combat, abilities, each enemy kind, uplink waves, boss), logging results
/// and saving screenshots. Exits with code 0 only if the mission completes.
public class AutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (System.Environment.GetCommandLineArgs().Contains("-autotest"))
        {
            Application.runInBackground = true;   // keep running if the window loses focus mid-test
            InputGate.Blocked = true;             // the window takes focus, so ignore whatever the user types or moves
            new GameObject("AutoTest").AddComponent<AutoTest>();
        }
    }

    // A test window ignores the keyboard and mouse on purpose. Say so on screen, so it is never mistaken for the game.
    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(14, Screen.height / 36), alignment = TextAnchor.MiddleCenter, wordWrap = true };
        style.normal.textColor = Color.white;
        GUI.backgroundColor = new Color(0.8f, 0.1f, 0.1f, 1f);
        GUI.Box(new Rect(Screen.width * 0.2f, Screen.height * 0.9f, Screen.width * 0.6f, Screen.height * 0.08f),
                "AUTOMATED TEST - keyboard and mouse are disabled in this window. To play, run Build\\LuminousTides.exe yourself.", style);
    }

    string dir;
    GameManager gm;
    PlayerController player;
    Weapon weapon;

    IEnumerator Start()
    {
        dir = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots");
        Directory.CreateDirectory(dir);
        gm = GameManager.Instance;
        player = gm.player;
        weapon = player.GetComponentInChildren<Weapon>();
        var md = MissionDirector.Instance;
        yield return null;
        Log($"varrik at start: {EnemyAI.All.Count(e => e.faction == Faction.Varrik)}");
        Log($"start enemies={EnemyAI.All.Count} stalkers={Count(EnemyKind.Stalker)} spitters={Count(EnemyKind.Spitter)} " +
            $"onNavMesh={FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None).Count(a => a.isOnNavMesh)} phase={md.Current}");

        // ---- Front end: main menu, settings, pause. The game must be frozen while each is open.
        gm.EnterMenu();
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Shot("menu_main");
        Log($"main menu: state={gm.State} timeScale={Time.timeScale}");
        GameMenu.Instance.ShowSettings(true);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot("menu_settings");
        float fovBefore = weapon.hipFov;
        GameSettings.Fov = 90f; GameSettings.Sensitivity = 3.3f; GameSettings.Apply();
        Log($"settings applied: fov {fovBefore:0}->{weapon.hipFov:0} sensitivity={player.mouseSensitivity:0.0}");
        GameSettings.Fov = 72f; GameSettings.Sensitivity = 2.2f; GameSettings.Apply();
        GameMenu.Instance.ShowSettings(false);
        yield return null;
        // Start the mission the way a player does: find what a click on the START MISSION button would hit
        // and deliver a click to it through the UI system. This proves the button is reachable, not just that the code works.
        var startButton = GameObject.Find("Button OPEN WORLD");
        var corners = new Vector3[4];
        ((RectTransform)startButton.transform).GetWorldCorners(corners);
        var pointer = new PointerEventData(EventSystem.current) { position = (corners[0] + corners[2]) / 2f, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        string topHit = hits.Count > 0 ? hits[0].gameObject.name : "nothing";
        if (hits.Count > 0) ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        yield return null;
        Log($"menu click at {pointer.position}: topHit='{topHit}' stateAfter={gm.State} cursorLock={Cursor.lockState} " +
            $"inputModule={(EventSystem.current.currentInputModule != null ? EventSystem.current.currentInputModule.GetType().Name : "none")}");
        if (gm.State != GameManager.GameState.Playing) gm.StartMission();
        yield return new WaitForSeconds(1.0f);
        gm.Pause();
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot("menu_pause");
        Log($"paused: state={gm.State} timeScale={Time.timeScale}");
        gm.Resume();
        Log($"resumed: state={gm.State} timeScale={Time.timeScale}");

        yield return new WaitForSeconds(0.5f);
        yield return Shot("play_1_spawn");

        // Aim transition.
        foreach (float v in new[] { 0.5f, 1f })
        {
            weapon.debugAimOverride = v;
            yield return new WaitForSeconds(0.3f);
            yield return Shot("aim_" + Mathf.RoundToInt(v * 100));
        }
        weapon.debugAimOverride = -1f;

        // ---- The test range: call enemies in at a pad, climb a wall, and fire the hand cannon at a practice target.
        var range = RangeDirector.Instance;
        var arsenalAll = player.cam.GetComponent<Arsenal>();
        if (range != null && gm.rangeSpawn != null)
        {
            Vector3 deck = gm.rangeSpawn.position;
            player.Teleport(deck, 0f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("range_start_game");
            bool called = range.DebugUse("STALKERS");
            yield return new WaitForSeconds(1.6f);
            int arrived = range.Alive;
            range.DebugUse("CLEAR");
            yield return null;
            // The super against real targets: call a mixed pack in, cast, and count who the bolts kill.
            player.Teleport(deck + new Vector3(0f, 0f, 62f), 0f);          // 16 m short of where they arrive
            range.DebugUse("MIXED PACK");
            yield return new WaitForSeconds(1.0f);
            var abilitiesRange = player.GetComponent<Abilities>();
            abilitiesRange.DebugChargeSuper();
            int packAlive = range.Alive, killsBeforeCast = gm.Kills;
            bool cast = abilitiesRange.DebugUse(Abilities.Super);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("super_range_cast");
            float boltsBy = Time.time + 4f; bool caught = false;
            while (abilitiesRange.Casting && Time.time < boltsBy)
            {
                if (!caught && gm.Kills > killsBeforeCast) { caught = true; yield return Shot("super_range_bolts"); }
                yield return null;
            }
            Log($"range super: cast={cast} packAlive={packAlive} killedByBolts={gm.Kills - killsBeforeCast} aliveAfter={range.Alive} playerHealth={gm.playerHealth.Current:0}");
            abilitiesRange.DebugRefill();
            range.DebugUse("CLEAR");
            yield return null;
            // A double jump from a standstill on the flat deck: nothing to climb, nothing overhead.
            player.Teleport(deck + new Vector3(0f, 0f, 3f), 0f);
            yield return new WaitForSeconds(0.5f);
            float deckY = player.transform.position.y, deckApex = deckY, singleApex = deckY;
            bool groundedAtStart = player.IsGrounded;
            player.DebugJump();
            for (float t = 0f; t < 0.36f; t += Time.deltaTime) { singleApex = Mathf.Max(singleApex, player.transform.position.y); yield return null; }
            bool airborneAtSecond = !player.IsGrounded;
            player.DebugJump();
            for (float t = 0f; t < 1.3f; t += Time.deltaTime) { deckApex = Mathf.Max(deckApex, player.transform.position.y); yield return null; }
            Log($"deck double jump: groundedAtStart={groundedAtStart} airborneAtSecondPress={airborneAtSecond} afterFirst={singleApex - deckY:0.00}m apex={deckApex - deckY:0.00}m");
            // The 1.4 m wall on the movement course: run at it, jump, and expect to end up on top.
            player.Teleport(deck + new Vector3(41f, 0f, 14.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            float before = player.transform.position.y;
            player.debugForward = 1f;
            player.DebugJump();
            float highest = before; bool mantled = false;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime) { highest = Mathf.Max(highest, player.transform.position.y); mantled |= player.IsMantling; yield return null; }
            player.debugForward = 0f;
            float zAfter = player.transform.position.z - (deck.z + 14.5f);
            // The hand cannon, from the firing line at the 10 m target.
            player.Teleport(deck + new Vector3(-37f, 0f, 6f), 0f);
            arsenalAll.Switch(arsenalAll.weapons.Length - 1);
            yield return new WaitForSeconds(0.6f);
            TargetDummy nearest = null;
            foreach (var d in FindObjectsByType<TargetDummy>(FindObjectsSortMode.None))
                if (nearest == null || Vector3.Distance(d.transform.position, player.transform.position) < Vector3.Distance(nearest.transform.position, player.transform.position)) nearest = d;
            float dummyBefore = nearest != null ? nearest.GetComponent<Health>().Current : 0f;
            for (int shot = 0; shot < 3 && nearest != null; shot++)
            {
                FaceAt(nearest.transform.position + Vector3.up * 1.1f);
                arsenalAll.Current.DebugFire();
                yield return new WaitForSeconds(0.5f);
            }
            yield return Shot("range_handcannon");
            arsenalAll.Current.debugAimOverride = 1f;         // and the view down its sights
            yield return new WaitForSeconds(0.6f);
            if (nearest != null) FaceAt(nearest.transform.position + Vector3.up * 1.9f);
            yield return null;
            yield return Shot("range_handcannon_sights");
            arsenalAll.Current.debugAimOverride = -1f;
            Log($"range: padCalled={called} stalkersArrived={arrived} aliveAfterClear={range.Alive} | wall climb: mantled={mantled} rose {highest - before:0.0}m movedForward={zAfter:0.0}m | " +
                $"weapons={arsenalAll.weapons.Length} current='{arsenalAll.Current.weaponName}' targetHealth {dummyBefore:0} -> {(nearest != null ? nearest.GetComponent<Health>().Current : 0f):0}");
            arsenalAll.Switch(0);
            yield return new WaitForSeconds(0.5f);
        }

        // ---- The world: stand in each new area, look at it, and note what is there.
        var world = WorldDirector.Instance;
        var tour = new (string name, float x, float z, float lookX, float lookZ)[]
        {
            ("tidewalk", 40f, 87f, 60f, 92f), ("anchorage", 96f, 86f, 136f, 60f), ("anchorage_court", 128f, 90f, 136f, 58f),
            ("gully", 116f, 132f, 114f, 170f), ("canopy", 114f, 184f, 112f, 208f), ("descent", 84f, 199f, 60f, 211f),
            ("highwatch", 30f, 205f, 36f, 229f), ("highwatch_view", 46f, 200f, 110f, 90f), ("pass", 2f, 142f, 12f, 175f),
        };
        foreach (var stop in tour)
        {
            Vector3 ground = new Vector3(stop.x, 0f, stop.z);
            if (Physics.Raycast(new Vector3(stop.x, 80f, stop.z), Vector3.down, out RaycastHit under, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) ground = under.point;
            if (UnityEngine.AI.NavMesh.SamplePosition(ground, out var navStop, 6f, UnityEngine.AI.NavMesh.AllAreas)) ground = navStop.position;
            player.Teleport(ground + Vector3.up * 0.2f, 0f);
            float tourFrames = 0f, started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 3.4f)
            {
                FaceAt(new Vector3(stop.lookX, ground.y + 4f, stop.lookZ));
                tourFrames++;
                yield return null;
            }
            int near = 0;
            foreach (var e in EnemyAI.All) if (Vector3.Distance(e.transform.position, player.transform.position) < 70f) near++;
            Log($"world {stop.name}: area='{(world != null && world.Current != null ? world.Current.name : "none")}' ground={ground.y:0.0}m enemiesWithin70m={near} " +
                $"awakeEnemies={EnemyAI.All.Count} fps={tourFrames / 3.4f:0} discovered={(world != null ? world.DiscoveredCount : 0)}");
            yield return Shot("world_" + stop.name);
            gm.playerHealth.ResetFull();
        }
        player.Teleport(gm.spawnPoint.position, 0f);
        yield return new WaitForSeconds(1.5f);
        gm.playerHealth.ResetFull();

        // ---- Part 1: melee pack, abilities, gunfight.
        var first = Nearest(EnemyKind.Stalker);
        player.Teleport(ClearSpotNear(first.transform.position, 9f), 0f);
        yield return new WaitForSeconds(2.0f);
        Face();
        yield return Shot("play_2_contact");

        var abilities = player.GetComponent<Abilities>();
        int killsBefore = gm.Kills;
        Face();
        bool threw = abilities.DebugUse(Abilities.Grenade);
        float waitBlast = Time.time + 2.5f;
        yield return new WaitForSeconds(0.2f);
        while (Time.time < waitBlast && GameObject.Find("PulseGrenade") != null) yield return null;
        yield return Shot("ability_grenade");
        yield return new WaitForSeconds(0.5f);
        // Melee: stand 4.5 m from an enemy, looking 30 degrees to one side of it, and strike. The player should be
        // carried to arm's length, turned onto the target, and the blow should land.
        var victim = Nearest();
        float gapBefore = -1f, gapAfter = -1f, healthBefore = 0f, healthAfter = 0f, offAim = -1f;
        bool struck = false;
        if (victim != null)
        {
            Vector3 stand = ClearSpotNear(victim.transform.position, 4.5f);
            Vector3 toVictim = victim.transform.position - stand;
            player.Teleport(stand, Mathf.Atan2(toVictim.x, toVictim.z) * Mathf.Rad2Deg + 30f);
            yield return null;
            gapBefore = Vector3.Distance(player.transform.position, victim.transform.position);
            healthBefore = victim.Health.Current + victim.Health.Shield;
            struck = abilities.DebugUse(Abilities.Melee);
            yield return new WaitForSeconds(0.1f);
            yield return Shot("ability_melee");
            yield return new WaitForSeconds(0.25f);
            if (victim != null)
            {
                gapAfter = Vector3.Distance(player.transform.position, victim.transform.position);
                healthAfter = victim.IsDead ? 0f : victim.Health.Current + victim.Health.Shield;
                offAim = Vector3.Angle(player.cam.forward, victim.Centre - player.cam.position);
            }
        }
        yield return new WaitForSeconds(0.3f);
        Log($"abilities: grenade={threw} melee={struck} gap {gapBefore:0.0}m -> {gapAfter:0.0}m targetHealth {healthBefore:0} -> {healthAfter:0} " +
            $"offAimAfter={offAim:0}deg killsFromAbilities={gm.Kills - killsBefore} chargeSpent={!abilities.IsReady(Abilities.Melee)} reuseBlocked={!abilities.DebugUse(Abilities.Melee)}");

        weapon.debugHoldAim = true;
        float until = Time.time + 0.7f;
        while (Time.time < until) { Face(); yield return null; }
        yield return Shot("play_ads");
        weapon.debugHoldAim = false;
        yield return Fight(5f, "play_3_fight");
        Log($"after fight: kills={gm.Kills} health={gm.playerHealth.Current:0} shield={gm.playerHealth.Shield:0} mag={weapon.Magazine}");

        // ---- Movement: double jump and slide.
        gm.playerHealth.ResetFull();
        float groundY = player.transform.position.y, apex = groundY;
        player.DebugJump();
        until = Time.time + 1.6f;
        bool second = false;
        while (Time.time < until)
        {
            apex = Mathf.Max(apex, player.transform.position.y);
            if (!second && Time.time > until - 1.25f) { second = true; player.DebugJump(); }
            gm.playerHealth.ResetFull();
            yield return null;
        }
        player.DebugSlide();
        yield return new WaitForSeconds(0.15f);
        Log($"movement: doubleJumpApex={apex - groundY:0.00}m sliding={player.IsSliding} slideSpeed={player.PlanarSpeed:0.0}");

        // ---- Ambush: walk into the first trigger and count what erupts.
        int beforeAmbush = EnemyAI.All.Count;
        var ambush = md.ambushes[0];
        player.Teleport(ambush.point.position + Vector3.up * 0.6f, 0f);
        yield return new WaitForSeconds(0.45f);
        yield return Shot("ambush_emerge");
        yield return new WaitForSeconds(0.8f);
        Log($"ambush: sprung={ambush.sprung} enemies {beforeAmbush}->{EnemyAI.All.Count} bursters={Count(EnemyKind.Burster)} banner='{md.Banner}'");
        int killsBeforeAmbushFight = gm.Kills, podsBefore = FindObjectsByType<Volatile>(FindObjectsSortMode.None).Length;
        until = Time.time + 6f;
        bool ambushShot = false;
        while (Time.time < until)
        {
            gm.playerHealth.ResetFull();
            Face();
            weapon.DebugFire();
            if (!ambushShot && Time.time > until - 4.5f) { ambushShot = true; yield return Shot("ambush_fight"); }
            yield return null;
        }
        Log($"ambush fight: kills={gm.Kills - killsBeforeAmbushFight} score={gm.Score} deaths={gm.Deaths} pods {podsBefore}->{FindObjectsByType<Volatile>(FindObjectsSortMode.None).Length}");

        // ---- Second weapon and the super.
        var arsenal = Arsenal.Instance;
        arsenal.Switch(1);
        yield return new WaitForSeconds(0.8f);
        var shotgun = arsenal.Current;
        int killsBeforeShotgun = gm.Kills;
        var close = Nearest();
        if (close != null) player.Teleport(ClearSpotNear(close.transform.position, 6f), 0f);
        until = Time.time + 5f;
        bool shotgunShot = false;
        while (Time.time < until)
        {
            gm.playerHealth.ResetFull();
            Face();
            shotgun.DebugFire();
            if (!shotgunShot && Time.time > until - 4.2f) { shotgunShot = true; yield return Shot("shotgun_fire"); }
            yield return null;
        }
        Log($"shotgun: current='{arsenal.Current.weaponName}' kills={gm.Kills - killsBeforeShotgun} mag={shotgun.Magazine} reserve={shotgun.Reserve}");
        float chargeFromPlay = abilities.SuperCharge;
        abilities.DebugChargeSuper();
        int killsBeforeSurge = gm.Kills;
        bool surged = abilities.DebugUse(Abilities.Super);
        yield return new WaitForSeconds(1.3f);
        yield return Shot("super_cast");            // third person, hand raised, lightning gathering
        yield return new WaitForSeconds(0.85f);
        yield return Shot("super_slam");
        yield return new WaitForSeconds(0.35f);
        yield return Shot("super_wave");            // the wave out and bolts coming down
        float castEnds = Time.time + 3f;
        while (abilities.Casting && Time.time < castEnds) yield return null;
        yield return new WaitForSeconds(0.3f);
        yield return Shot("super_after");           // back behind the eyes
        Log($"super: chargeEarnedSoFar={chargeFromPlay:0.00} used={surged} chargeAfter={abilities.SuperCharge:0.00} killsFromSurge={gm.Kills - killsBeforeSurge} timeScale={Time.timeScale} stillCasting={abilities.Casting} cameraBack={Vector3.Distance(player.cam.position, player.transform.position + Vector3.up * 1.75f) < 0.6f}");
        arsenal.Switch(0);
        yield return new WaitForSeconds(0.8f);
        Log($"switched back: '{arsenal.Current.weaponName}' carbineEnabled={weapon.enabled}");

        // ---- Ranged enemy: stand in the open in front of a spitter and let it shoot.
        var spitter = Nearest(EnemyKind.Spitter);
        if (spitter != null)
        {
            gm.playerHealth.ResetFull();
            player.Teleport(ClearSpotNear(spitter.transform.position, 14f), 0f);
            float taken = 0f, lastTotal = gm.playerHealth.Shield + gm.playerHealth.Current;
            int boltsSeen = 0;
            until = Time.time + 6f;
            bool shot = false;
            while (Time.time < until)
            {
                // Measure the damage taken, but keep the player alive so the rest of the run is not derailed by a respawn.
                float total = gm.playerHealth.Shield + gm.playerHealth.Current;
                if (total < lastTotal) taken += lastTotal - total;
                gm.playerHealth.ResetFull();
                lastTotal = gm.playerHealth.Shield + gm.playerHealth.Current;
                Face();
                int bolts = FindObjectsByType<EnemyBolt>(FindObjectsSortMode.None).Length;
                boltsSeen = Mathf.Max(boltsSeen, bolts);
                if (bolts > 0 && !shot) { shot = true; yield return Shot("spitter_bolt"); }
                yield return null;
            }
            Log($"spitter: boltsInFlight={boltsSeen} damageTaken={taken:0} " +
                $"distance={Vector3.Distance(player.transform.position, spitter.transform.position):0.0}");
        }
        else Log("spitter: none found");

        // ---- Part 2: reach the arch, then hold the uplink through its waves.
        player.Teleport(md.archPoint.position + Vector3.up * 0.6f, 0f);
        yield return new WaitForSeconds(0.6f);
        Log($"at arch: phase={md.Current} objective='{gm.objectiveText}'");
        md.DebugSetUplinkDuration(14f);
        player.Teleport(md.uplinkPoint.position + Vector3.up * 0.6f, 180f);
        yield return new WaitForSeconds(0.5f);
        Log($"on pad: started={md.UplinkStarted} onPad={md.PlayerOnPad}");
        int maxEnemies = 0, frames = 0;
        float fpsStart = Time.realtimeSinceStartup, worstFrame = 0f;
        bool waveShot = false;
        until = Time.time + 40f;
        while (Time.time < until && md.Current == MissionDirector.Phase.Uplink)
        {
            gm.playerHealth.ResetFull();            // the test is about the flow, not survival
            Face();
            weapon.DebugFire();
            maxEnemies = Mathf.Max(maxEnemies, EnemyAI.All.Count);
            frames++;
            if (frames > 30) worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            if (!waveShot && md.Wave >= 2 && md.UplinkProgress > 0.5f) { waveShot = true; yield return Shot("uplink_wave"); }
            yield return null;
        }
        Log($"uplink: progress={md.UplinkProgress:0.00} waves={md.Wave}/{md.WaveCount} mostEnemiesAlive={maxEnemies} phase={md.Current}");
        Log($"performance during waves at {Screen.width}x{Screen.height}: average {frames / (Time.realtimeSinceStartup - fpsStart):0} fps, " +
            $"slowest frame {worstFrame * 1000f:0} ms, vsync={QualitySettings.vSyncCount}");

        // ---- Part 3: the boss.
        yield return new WaitForSeconds(0.3f);
        var boss = md.Boss;
        Log($"boss: spawned={boss != null} name='{(boss != null ? boss.displayName : "")}' health={(boss != null ? boss.Health.maxHealth : 0):0} objective='{gm.objectiveText}'");
        if (boss != null)
        {
            yield return Shot("boss_arrives");
            // Watch it attack for a while: volleys at range, slam up close.
            float bossStart = boss.Health.Current;
            float damageTaken = 0f, last = gm.playerHealth.Shield + gm.playerHealth.Current;
            int shots = 0;
            until = Time.time + 16f;
            while (Time.time < until && !boss.IsDead)
            {
                float now = gm.playerHealth.Shield + gm.playerHealth.Current;
                if (now < last) damageTaken += last - now;
                gm.playerHealth.ResetFull();
                last = gm.playerHealth.Shield + gm.playerHealth.Current;
                FaceAt(boss.transform.position + Vector3.up * 3f);
                weapon.DebugFire();
                float d = Vector3.Distance(player.transform.position, boss.transform.position);
                if (shots == 0 && FindObjectsByType<EnemyBolt>(FindObjectsSortMode.None).Length >= 3) { shots++; yield return Shot("boss_volley"); }
                if (shots == 1 && d < 7f) { shots++; yield return new WaitForSeconds(0.75f); yield return Shot("boss_slam"); }
                yield return null;
            }
            Log($"boss fight: gunDamage={bossStart - boss.Health.Current:0} playerDamageTaken={damageTaken:0} bossHealth={boss.Health.Current:0}");

            // Finish it in chunks so both reinforcement calls fire.
            int before = EnemyAI.All.Count;
            int called = 0;
            boss.BossThreshold += _ => called++;
            while (!boss.IsDead)
            {
                boss.Health.Damage(260f, player.transform.position);
                gm.playerHealth.ResetFull();
                yield return new WaitForSeconds(0.15f);
            }
            Log($"boss dead: reinforcementCalls={called} enemiesBefore={before} enemiesAfter={EnemyAI.All.Count}");
            yield return new WaitForSeconds(0.6f);
        }
        Log("final state=" + gm.State + " phase=" + md.Current + " kills=" + gm.Kills);
        yield return Shot("play_6_won");
        Log("done");
        Application.Quit(gm.State == GameManager.GameState.Won ? 0 : 3);
    }

    IEnumerator Fight(float seconds, string shotName)
    {
        float end = Time.time + seconds;
        bool shotTaken = false;
        while (Time.time < end)
        {
            Face();
            weapon.DebugFire();
            if (!shotTaken && Time.time > end - seconds * 0.7f) { shotTaken = true; yield return Shot(shotName); }
            yield return null;
        }
    }

    static int Count(EnemyKind kind) => EnemyAI.All.Count(e => e.kind == kind);

    EnemyAI Nearest(EnemyKind? kind = null)
    {
        return EnemyAI.All.Where(e => e != null && !e.IsDead && (kind == null || e.kind == kind))
            .OrderBy(e => (e.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
    }

    /// A standing spot at the given distance from a target with a clear line to it.
    Vector3 ClearSpotNear(Vector3 target, float distance)
    {
        foreach (float ang in new[] { 180f, 150f, 210f, 120f, 240f, 90f, 270f, 0f, 45f, 315f })
        {
            Vector3 candidate = target + Quaternion.Euler(0, ang, 0) * Vector3.forward * distance + Vector3.up * 3f;
            if (!Physics.Raycast(candidate, Vector3.down, out RaycastHit ground, 10f)) continue;
            if (ground.point.y < 0.05f) continue;                                    // not in the water
            Vector3 eye = ground.point + Vector3.up * 1.7f;
            if (Physics.Linecast(eye, target + Vector3.up * 1.2f, out RaycastHit block)
                && block.collider.GetComponentInParent<EnemyAI>() == null) continue;
            return ground.point + Vector3.up * 0.3f;
        }
        return target + new Vector3(0, 0.6f, -distance);
    }

    void Face()
    {
        var target = Nearest();
        if (target != null) FaceAt(target.transform.position + Vector3.up * 1.2f);
    }

    void FaceAt(Vector3 point)
    {
        Vector3 aim = point - player.cam.position;
        player.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, 0);
        player.DebugSetPitch(-Mathf.Asin(aim.normalized.y) * Mathf.Rad2Deg);
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(0.1f);   // let the capture finish before the scene changes (works while paused)
    }

    static void Log(string msg) { Debug.Log("[AUTOTEST] " + msg); }
}
