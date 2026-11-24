using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// A group of enemies that belongs to a place. While the player is far away the group sleeps (switched off, costing
/// nothing); once it has been wiped out and the player has gone, it comes back after a while, so the world stays
/// alive behind you the way a patrol zone does.
public class EnemyCamp : MonoBehaviour
{
    public float sleepRange = 100f;         // members are switched off beyond this
    public float wakeRange = 85f;           // and back on inside this
    public float respawnDelay = 100f;       // seconds after the last member dies
    public float respawnClearance = 55f;    // the player must be at least this far away for the camp to refill

    readonly List<GameObject> members = new List<GameObject>();
    readonly List<GameObject> templates = new List<GameObject>();
    bool awake = true;
    float emptySince = -1f, nextCheck;

    void Start()
    {
        // Keep a dormant copy of every member, taken before anything has happened to it, to rebuild the camp from.
        var store = new GameObject("Templates").transform;
        store.SetParent(transform, false);
        store.gameObject.SetActive(false);       // copies made under it never wake
        var originals = new List<GameObject>();
        foreach (Transform child in transform)
            if (child != store && child.GetComponent<EnemyAI>() != null) originals.Add(child.gameObject);
        foreach (var o in originals)
        {
            var copy = Instantiate(o, o.transform.position, o.transform.rotation, store);
            copy.name = o.name;
            templates.Add(copy);
            members.Add(o);
        }
        nextCheck = Random.Range(0f, 0.5f);
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        var gm = GameManager.Instance;
        if (gm == null) return;
        Vector3 p = gm.player.transform.position;
        float dist = new Vector2(p.x - transform.position.x, p.z - transform.position.z).magnitude;

        members.RemoveAll(m => m == null);
        int alive = 0;
        foreach (var m in members)
        {
            var ai = m.GetComponent<EnemyAI>();
            if (ai != null && !ai.IsDead) alive++;
        }

        if (alive == 0)
        {
            if (emptySince < 0f) emptySince = Time.time;
            if (Time.time - emptySince >= respawnDelay && dist >= respawnClearance) Refill(dist < wakeRange);
            return;
        }
        emptySince = -1f;
        if (awake && dist > sleepRange) SetAwake(false);
        else if (!awake && dist < wakeRange) SetAwake(true);
    }

    void SetAwake(bool on)
    {
        awake = on;
        foreach (var m in members)
        {
            if (m == null) continue;
            var ai = m.GetComponent<EnemyAI>();
            if (ai != null && ai.IsDead) continue;      // leave a body to finish burning away
            m.SetActive(on);
        }
    }

    void Refill(bool visible)
    {
        members.Clear();
        foreach (var t in templates)
        {
            Vector3 pos = t.transform.position;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            var go = Instantiate(t, pos, t.transform.rotation, transform);
            go.name = t.name;
            go.SetActive(visible);
            members.Add(go);
        }
        awake = visible;
        emptySince = -1f;
    }
}
