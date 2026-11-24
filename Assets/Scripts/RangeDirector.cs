using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Runs the combat test range: a row of pads the player walks up to and presses E at. Most pads call in enemies
/// to the arena; one clears them all away and one refills ammunition and abilities.
public class RangeDirector : MonoBehaviour
{
    public static RangeDirector Instance { get; private set; }

    [System.Serializable]
    public class Pad
    {
        public string label = "STALKERS";
        public Transform point;
        public string[] units = new string[0];      // names from `unitNames`; empty with an action set = a utility pad
        public string action = "";                  // "clear" or "refill"
    }

    public Pad[] pads = new Pad[0];
    public string[] unitNames = new string[0];      // parallel to `templates`
    public GameObject[] templates = new GameObject[0];
    public Transform[] spawnPoints = new Transform[0];
    public float reach = 2.6f;

    /// What pressing E would do right now, or null when the player is not at a pad. The HUD shows it.
    public string Prompt { get; private set; }

    readonly List<GameObject> spawned = new List<GameObject>();
    int nextPoint;
    AudioSource audioSrc;
    AudioClip confirmClip;

    void Awake()
    {
        Instance = this;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.6f;
        confirmClip = ProceduralAudio.Ping(0.12f, 880f);
    }

    void Update()
    {
        Prompt = null;
        var gm = GameManager.Instance;
        if (gm == null || gm.State != GameManager.GameState.Playing) return;
        Vector3 p = gm.player.transform.position;
        Pad near = null;
        float best = reach;
        foreach (var pad in pads)
        {
            if (pad.point == null) continue;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pad.point.position.x, pad.point.position.z));
            if (d < best && Mathf.Abs(p.y - pad.point.position.y) < 2.5f) { best = d; near = pad; }
        }
        if (near == null) return;
        Prompt = "E     " + (near.action == "clear" ? "CLEAR THE ARENA" : near.action == "refill" ? "REFILL AMMO AND ABILITIES" : "CALL IN  " + near.label);
        if (!InputGate.Blocked && Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.E)) Use(near);
    }

    /// Smoke test hook.
    public bool DebugUse(string label)
    {
        foreach (var pad in pads) if (pad.label == label) { Use(pad); return true; }
        return false;
    }

    public int Alive
    {
        get
        {
            spawned.RemoveAll(g => g == null);
            int n = 0;
            foreach (var g in spawned) { var ai = g.GetComponent<EnemyAI>(); if (ai != null && !ai.IsDead) n++; }
            return n;
        }
    }

    void Use(Pad pad)
    {
        audioSrc.pitch = 1f;
        audioSrc.PlayOneShot(confirmClip);
        if (Abilities.Instance != null && pad.point != null)
            Abilities.Instance.Ring(pad.point.position + Vector3.up * 0.1f, 0.3f, 2.2f, 0.35f, new Color(0.45f, 1f, 0.95f) * 1.8f);
        if (pad.action == "clear")
        {
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear();
            return;
        }
        if (pad.action == "refill")
        {
            if (Arsenal.Instance != null) foreach (var w in Arsenal.Instance.weapons) for (int i = 0; i < 12; i++) w.AddAmmo(w.pickupRounds);
            if (Abilities.Instance != null) Abilities.Instance.DebugRefill();
            var health = GameManager.Instance.playerHealth;
            if (health != null) health.ResetFull();
            return;
        }
        foreach (string unit in pad.units) Spawn(unit);
    }

    void Spawn(string unit)
    {
        int index = System.Array.IndexOf(unitNames, unit);
        if (index < 0 || templates[index] == null || spawnPoints.Length == 0) return;
        Vector3 position = spawnPoints[nextPoint++ % spawnPoints.Length].position;
        Vector2 r = Random.insideUnitCircle * 2.5f;
        position += new Vector3(r.x, 0f, r.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) position = hit.position;
        Vector3 look = GameManager.Instance.player.transform.position - position; look.y = 0f;
        var go = Instantiate(templates[index], position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        go.name = unit;
        go.SetActive(true);
        spawned.Add(go);
        var ai = go.GetComponent<EnemyAI>();
        if (ai == null) return;
        if (ai.kind == EnemyKind.Boss) { ai.kind = EnemyKind.Champion; ai.elite = true; ai.displayName = "TIDE ALPHA"; }   // so it gets a health bar here
        ai.sightRange = 60f; ai.loseRange = 200f;
        if (ai.hoverHeight <= 0f) ai.Emerge();
        ai.Invoke(nameof(EnemyAI.ForceAlert), 0.75f);
    }
}
