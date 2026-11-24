using UnityEngine;

/// A practice target. It takes damage like anything else (its head counts as a precision hit), shows the damage per
/// second it has been taking, folds down when "killed" and stands back up a moment later. Some slide from side to side.
[RequireComponent(typeof(Health))]
public class TargetDummy : MonoBehaviour
{
    public TextMesh readout;
    public float slideDistance;         // metres either side of where it starts; 0 = it stands still
    public float slideSpeed = 2.2f;

    Health health;
    Vector3 home;
    Renderer[] renderers;
    Collider[] colliders;
    MaterialPropertyBlock block;
    float downUntil = -1f, flashUntil, windowStart = -9f, windowDamage, lastHit, shownDps;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        health = GetComponent<Health>();
        home = transform.position;
        renderers = GetComponentsInChildren<MeshRenderer>();
        colliders = GetComponentsInChildren<Collider>();
        block = new MaterialPropertyBlock();
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnDamaged(float amount, Vector3 source)
    {
        // Damage per second over the current burst: the clock starts on the first hit and runs until fire stops.
        if (Time.time - lastHit > 1.5f) { windowStart = Time.time; windowDamage = 0f; }
        lastHit = Time.time;
        windowDamage += amount;
        float span = Mathf.Max(0.35f, Time.time - windowStart);
        shownDps = windowDamage / span;
        if (readout) readout.text = Mathf.RoundToInt(amount) + "\n" + Mathf.RoundToInt(shownDps) + " / s";
        block.SetColor(BaseColorId, new Color(2.2f, 2.1f, 1.9f, 1f));
        foreach (var r in renderers) if (r.GetComponent<TextMesh>() == null) r.SetPropertyBlock(block);
        flashUntil = Time.time + 0.07f;
    }

    void OnDied()
    {
        downUntil = Time.time + 1.2f;
        foreach (var c in colliders) c.enabled = false;
        if (Abilities.Instance != null) Abilities.Instance.Sparks(transform.position + Vector3.up * 1.2f, 24, new Color(1f, 0.6f, 0.2f), 3f, 9f, false);
    }

    void Update()
    {
        if (flashUntil > 0f && Time.time >= flashUntil)
        {
            flashUntil = 0f;
            foreach (var r in renderers) if (r.GetComponent<TextMesh>() == null) r.SetPropertyBlock(null);
        }
        if (slideDistance > 0f)
            transform.position = home + transform.right * Mathf.Sin(Time.time * slideSpeed / Mathf.Max(0.1f, slideDistance)) * slideDistance;
        bool down = Time.time < downUntil;
        float target = down ? 82f : 0f;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.Euler(target, transform.localEulerAngles.y, 0f), 12f * Time.deltaTime);
        if (!down && health.IsDead)
        {
            health.ResetFull();
            foreach (var c in colliders) c.enabled = true;
        }
        if (readout && Time.time - lastHit > 4f && readout.text.Length > 0) readout.text = "";
    }
}
