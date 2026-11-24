using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Runs the mission in three parts:
///   1. Advance - fight through to the rock arch. Ambushes burst out of the ground along the way.
///   2. Uplink  - reach the survey station and hold its pad while the uplink charges; announced waves arrive.
///   3. Boss    - the Tide Alpha rises and must be killed. It calls reinforcements as it weakens.
/// Each part moves the respawn point forward, so dying does not send the player back to the start.
public class MissionDirector : MonoBehaviour
{
    public enum Phase { Advance, Uplink, Boss, Complete }
    public static MissionDirector Instance { get; private set; }

    /// A group of enemies that erupts around a point the first time the player comes close.
    [System.Serializable]
    public class Ambush
    {
        public Transform point;
        public float triggerRadius = 15f;
        public int stalkers, spitters, bursters, elites;
        [System.NonSerialized] public bool sprung;
    }

    public Transform archPoint, uplinkPoint, bossSpawn;
    public Transform[] waveSpawns;
    public Ambush[] ambushes = new Ambush[0];
    public GameObject stalkerTemplate, spitterTemplate, eliteTemplate, bursterTemplate, bossTemplate;
    public float uplinkDuration = 50f;
    public float uplinkRadius = 9f;
    public string bossName = "TIDE ALPHA";

    public Phase Current { get; private set; } = Phase.Advance;
    /// True once the player has first stepped onto the uplink pad.
    public bool UplinkStarted { get; private set; }
    public bool PlayerOnPad { get; private set; }
    public float UplinkProgress { get; private set; }
    public int Wave { get; private set; }
    public int WaveCount => waves.Length;
    public EnemyAI Boss { get; private set; }
    public int AmbushesSprung { get; private set; }
    /// Short headline shown when something happens, and when it was set.
    public string Banner { get; private set; } = "";
    public float BannerTime { get; private set; } = -99f;
    /// Raised with "ambush", "wave", "boss" or "complete" so the audio can punctuate the moment.
    public event System.Action<string> Sting;

    struct Group { public int stalkers, spitters, elites, bursters; public Group(int st, int sp, int el, int bu) { stalkers = st; spitters = sp; elites = el; bursters = bu; } }
    readonly Group[] waves = { new Group(2, 1, 0, 1), new Group(2, 1, 0, 2), new Group(2, 1, 1, 2) };
    readonly float[] waveAt = { 0.03f, 0.36f, 0.68f };
    GameManager gm;

    void Awake() { Instance = this; }

    void Start()
    {
        gm = GameManager.Instance;
        gm.objective = archPoint;
        gm.objectiveText = "PUSH THROUGH TO THE ARCH";
        EnemyAI.AnyDied += OnEnemyDied;
    }

    void OnDestroy() { EnemyAI.AnyDied -= OnEnemyDied; }

    void Update()
    {
        if (gm == null || gm.State == GameManager.GameState.Won || gm.State == GameManager.GameState.Menu) return;
        Vector3 p = gm.player.transform.position;

        if (gm.State == GameManager.GameState.Playing)
            foreach (var a in ambushes)
                if (!a.sprung && a.point != null && Flat(p, a.point.position) < a.triggerRadius) SpringAmbush(a);

        switch (Current)
        {
            case Phase.Advance:
                if (Flat(p, archPoint.position) < 10f) EnterUplink();
                break;

            case Phase.Uplink:
                PlayerOnPad = Flat(p, uplinkPoint.position) <= uplinkRadius && gm.State == GameManager.GameState.Playing;
                if (!UplinkStarted)
                {
                    if (!PlayerOnPad) break;
                    UplinkStarted = true;
                    SetBanner("HOLD THE UPLINK");
                    MoveCheckpoint(uplinkPoint.position);
                }
                gm.objectiveText = PlayerOnPad ? "HOLD THE UPLINK" : "RETURN TO THE UPLINK";
                if (PlayerOnPad) UplinkProgress = Mathf.Min(1f, UplinkProgress + Time.deltaTime / uplinkDuration);
                while (Wave < waves.Length && UplinkProgress >= waveAt[Wave])
                {
                    Wave++;
                    SetBanner("WAVE " + Wave + " OF " + waves.Length + "  -  INCOMING");
                    Sting?.Invoke("wave");
                    SpawnAtWavePoints(waves[Wave - 1]);
                }
                if (UplinkProgress >= 1f) EnterBoss();
                break;
        }
    }

    void SpringAmbush(Ambush a)
    {
        a.sprung = true;
        AmbushesSprung++;
        SetBanner("AMBUSH");
        Sting?.Invoke("ambush");
        // They come up in a ring around the trigger point, so the player is caught in the middle.
        var group = new Group(a.stalkers, a.spitters, a.elites, a.bursters);
        int total = group.stalkers + group.spitters + group.elites + group.bursters, n = 0;
        float start = Random.Range(0f, 360f);
        Vector3 Next(float radius)
        {
            float ang = (start + n++ * (360f / Mathf.Max(1, total))) * Mathf.Deg2Rad;
            return a.point.position + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * radius;
        }
        for (int i = 0; i < group.spitters; i++) Spawn(spitterTemplate, Next(13f));
        for (int i = 0; i < group.stalkers; i++) Spawn(stalkerTemplate, Next(9f));
        for (int i = 0; i < group.elites; i++) Spawn(eliteTemplate, Next(10f));
        for (int i = 0; i < group.bursters; i++) Spawn(bursterTemplate, Next(11f));
    }

    void EnterUplink()
    {
        Current = Phase.Uplink;
        gm.objective = uplinkPoint;
        gm.objectiveText = "REACH THE SURVEY STATION";
        SetBanner("REACH THE SURVEY STATION");
        MoveCheckpoint(archPoint.position);
    }

    void EnterBoss()
    {
        Current = Phase.Boss;
        var go = Spawn(bossTemplate, bossSpawn.position);
        Boss = go != null ? go.GetComponent<EnemyAI>() : null;
        if (Boss != null)
        {
            Boss.displayName = bossName;
            Boss.BossThreshold += OnBossThreshold;
            gm.objective = Boss.transform;
        }
        gm.objectiveText = "KILL THE " + bossName;
        SetBanner("UPLINK COMPLETE  -  " + bossName + " RISES");
        Sting?.Invoke("boss");
        gm.player.AddShake(3.5f);
    }

    void OnBossThreshold(int stage)
    {
        SetBanner(bossName + " CALLS THE PACK");
        Sting?.Invoke("wave");
        SpawnAtWavePoints(stage == 1 ? new Group(2, 1, 0, 1) : new Group(1, 1, 1, 2));
    }

    void OnEnemyDied(EnemyAI enemy)
    {
        if (Current == Phase.Boss && enemy == Boss)
        {
            Current = Phase.Complete;
            gm.objectiveText = "MISSION COMPLETE";
            Sting?.Invoke("complete");
            gm.ObjectiveReached();
        }
    }

    void SpawnAtWavePoints(Group g)
    {
        // Prefer the spawn points farthest from the player so reinforcements run in rather than pop in.
        var points = new List<Transform>(waveSpawns);
        Vector3 p = gm.player.transform.position;
        points.Sort((a, b) => Flat(b.position, p).CompareTo(Flat(a.position, p)));
        int n = 0;
        for (int i = 0; i < g.stalkers; i++) Spawn(stalkerTemplate, Scatter(points[n++ % 3].position));
        for (int i = 0; i < g.spitters; i++) Spawn(spitterTemplate, Scatter(points[n++ % 3].position));
        for (int i = 0; i < g.elites; i++) Spawn(eliteTemplate, Scatter(points[n++ % 3].position));
        for (int i = 0; i < g.bursters; i++) Spawn(bursterTemplate, Scatter(points[n++ % 3].position));
    }

    static Vector3 Scatter(Vector3 p)
    {
        Vector2 r = Random.insideUnitCircle * 3f;
        return p + new Vector3(r.x, 0f, r.y);
    }

    GameObject Spawn(GameObject template, Vector3 position)
    {
        if (template == null) return null;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 8f, NavMesh.AllAreas)) position = hit.position;
        Vector3 look = gm.player.transform.position - position; look.y = 0f;
        var go = Instantiate(template, position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        go.name = template.name.Replace("Template", "");
        go.SetActive(true);
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.Emerge();                                         // rise out of the ground or water
            ai.Invoke(nameof(EnemyAI.ForceAlert), 0.75f);        // then come for the player
        }
        return go;
    }

    void MoveCheckpoint(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) position = hit.position;
        gm.spawnPoint.position = position + Vector3.up * 0.2f;
    }

    void SetBanner(string text) { Banner = text; BannerTime = Time.time; }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // ---- smoke-test hooks
    public void DebugSetUplinkDuration(float seconds) { uplinkDuration = seconds; }
}
