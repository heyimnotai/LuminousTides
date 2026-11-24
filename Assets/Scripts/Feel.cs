using System.Collections;
using UnityEngine;

/// Game feel that does not belong to any one system: hit-stop, speed streaks past the camera, the motes of
/// energy that fly from a kill to the player, and directional jets of sparks. Creates itself on first use.
public class Feel : MonoBehaviour
{
    static Feel instance;
    public static Feel Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("Feel").AddComponent<Feel>();
            return instance;
        }
    }

    const float StopScale = 0.04f;
    float stopUntil = -1f, streakUntil = -1f, streakStrength;
    bool stopping;
    ParticleSystem streaks;
    Transform cam;
    AudioSource audioSrc;
    AudioClip orbClip;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.5f;
        orbClip = ProceduralAudio.Ping(0.09f, 1250f);
    }

    static bool Playing => GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Playing;

    // ------------------------------------------------------------------ hit-stop
    /// Freezes the action for a few hundredths of a second, so a kill lands with weight.
    public void HitStop(float seconds)
    {
        if (!Playing) return;
        if (!stopping && Time.timeScale < 0.9f) return;          // something else (a menu, the super) owns time right now
        stopping = true;
        Time.timeScale = StopScale;
        stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + seconds);
    }

    void Update()
    {
        if (stopping && Time.unscaledTime >= stopUntil)
        {
            stopping = false;
            if (Mathf.Approximately(Time.timeScale, StopScale)) Time.timeScale = 1f;     // leave it alone if a menu froze the game meanwhile
        }
        UpdateStreaks();
    }

    // ------------------------------------------------------------------ speed streaks
    /// Lines of light rushing past the camera for a moment (dash, slide, double jump).
    public void Streaks(float seconds, float strength = 1f)
    {
        streakStrength = Mathf.Max(strength, Time.time < streakUntil ? streakStrength : 0f);
        streakUntil = Mathf.Max(streakUntil, Time.time + seconds);
    }

    void UpdateStreaks()
    {
        if (Time.time >= streakUntil || Time.timeScale <= 0f) return;
        if (streaks == null && !BuildStreaks()) return;
        var ep = new ParticleSystem.EmitParams { startLifetime = 0.22f };
        int count = Mathf.CeilToInt(5f * streakStrength);
        for (int i = 0; i < count; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(1.1f, 3.4f);
            ep.position = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.7f, Random.Range(7f, 13f));
            ep.velocity = new Vector3(0f, 0f, -Random.Range(38f, 60f));
            ep.startSize = Random.Range(0.02f, 0.045f);
            ep.startColor = new Color(0.6f, 1f, 0.97f, Random.Range(0.25f, 0.6f) * streakStrength);
            streaks.Emit(ep, 1);
        }
    }

    bool BuildStreaks()
    {
        var a = Abilities.Instance;
        if (a == null || a.cam == null || a.flashMaterial == null) return false;
        cam = a.cam.transform;
        var go = new GameObject("SpeedStreaks");
        go.transform.SetParent(cam, false);
        streaks = go.AddComponent<ParticleSystem>();
        streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = streaks.main;
        main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f; main.maxParticles = 300;
        var em = streaks.emission; em.enabled = false;
        var sh = streaks.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = a.flashMaterial;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 0f; r.velocityScale = 0.05f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streaks.Play();
        return true;
    }

    // ------------------------------------------------------------------ jets and motes
    /// A cone of sparks thrown in one direction (thrusters, dust kicked up, a blast's debris).
    public void Jet(Vector3 position, Vector3 direction, int count, Color color, float minSpeed, float maxSpeed, float spreadDegrees, float size = 0.12f, float life = 0.45f)
    {
        var a = Abilities.Instance;
        var fx = a != null && a.arsenal != null && a.arsenal.Current != null ? a.arsenal.Current.impactFx : null;
        if (fx == null) return;
        var ep = new ParticleSystem.EmitParams { position = position, startColor = color, startSize = size, startLifetime = life };
        Quaternion look = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.up);
        for (int i = 0; i < count; i++)
        {
            Vector2 c = Random.insideUnitCircle * spreadDegrees;
            ep.velocity = look * Quaternion.Euler(c.x, c.y, 0f) * Vector3.forward * Random.Range(minSpeed, maxSpeed);
            fx.Emit(ep, 1);
        }
    }

    /// Motes of light burst out of something that just died, hang for a beat, then streak to the player.
    public void Orbs(Vector3 from, int count, Color color)
    {
        var a = Abilities.Instance;
        if (a == null || a.flashMaterial == null || a.player == null) return;
        for (int i = 0; i < count; i++) StartCoroutine(Orb(from, color, a, i * 0.03f));
    }

    IEnumerator Orb(Vector3 from, Color color, Abilities a, float delay)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Mote";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * 0.11f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = a.flashMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, color * 2.2f);
        mr.SetPropertyBlock(block);
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = a.flashMaterial;
        trail.time = 0.18f; trail.startWidth = 0.09f; trail.endWidth = 0f;
        trail.startColor = color; trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 vel = (Random.onUnitSphere + Vector3.up * 1.2f).normalized * Random.Range(3.5f, 6.5f);
        float t = -delay;
        const float hang = 0.38f;
        while (go != null && a != null && a.player != null)
        {
            float dt = Time.deltaTime;
            t += dt;
            Vector3 target = a.player.transform.position + Vector3.up * 1.0f;
            Vector3 to = target - go.transform.position;
            if (t > hang)
            {
                float pull = Mathf.Min(1f, (t - hang) * 2.2f);
                vel = Vector3.Lerp(vel, to.normalized * Mathf.Lerp(8f, 34f, pull), 10f * pull * dt + 0.02f);
                if (to.magnitude < 0.8f || t > 3f) break;
            }
            else vel = Vector3.Lerp(vel, Vector3.zero, 4f * dt);
            go.transform.position += vel * dt;
            yield return null;
        }
        if (go != null)
        {
            if (t <= 3f)
            {
                audioSrc.pitch = Random.Range(1.0f, 1.5f);
                audioSrc.PlayOneShot(orbClip, 0.35f);
            }
            Destroy(go);
        }
    }
}
