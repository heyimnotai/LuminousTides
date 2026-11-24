using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// The player's abilities:
///   Q  Melee    - a palm strike. The player steps in on the nearest enemy in reach, turns to face it and hits it.
///                 Always available; when its charge is ready (a cooldown) the blow also releases a pulse that
///                 damages and throws back everything around the target.
///   G  Grenade  - a thrown charge that bounces once and bursts on a timer or on contact with an enemy
/// plus the super, which charges from kills and damage instead of a timer:
///   F  Lightning Wave - seen from outside the body: a raised hand gathers lightning, a wave rolls out, and every
///                       enemy it reaches is struck from the sky
public class Abilities : MonoBehaviour
{
    public const int Melee = 0, Grenade = 1, Super = 3;
    public const int SlotCount = 2;
    public static Abilities Instance { get; private set; }

    public PlayerController player;
    public Camera cam;
    public Arsenal arsenal;
    Weapon weapon => arsenal.Current;
    public Material flashMaterial;       // plain additive, used for rings, flashes and trails
    public Material grenadeMaterial;     // glowing shell of the grenade

    [Header("Cooldowns (seconds): melee charge, grenade")]
    public float[] cooldowns = { 12f, 9f };

    [Header("Melee")]
    public float meleeDamage = 95f;
    public float lungeRange = 6f;          // how far away a target can be and still be stepped in on
    public float meleeReach = 1.7f;        // where the step leaves you: arm's length
    public float meleeRecovery = 0.7f;     // seconds before another strike

    [Header("Charged melee: the pulse released through the target")]
    public float pulseRadius = 5.5f;
    public float pulseDamage = 55f;
    public float pulseStun = 1.4f;

    [Header("Grenade")]
    public float throwSpeed = 19f;
    public float fuse = 1.7f;
    public float blastRadius = 5.5f;
    public float blastDamage = 110f;

    [Header("Super: Lightning Wave")]
    public float surgeRadius = 28f;        // how far the wave travels
    public float surgeDamage = 520f;       // each bolt
    public float waveSpeed = 26f;          // metres per second
    public Animator body;                  // the character seen from outside while casting
    public Transform bodyHand;             // the hand that is raised
    /// True while the super is being cast: nothing else can be used.
    public bool Casting => casting;
    bool casting;
    public float chargePerKill = 0.085f;
    public float chargePerEliteKill = 0.2f;
    public float chargePerDamage = 0.00035f;
    public float chargePerSecond = 0.003f;

    /// 0..1; the super can be used at 1.
    public float SuperCharge { get; private set; }
    public bool SuperReady => SuperCharge >= 1f;

    /// Raised when an ability fires, with its index.
    public event System.Action<int> Used;
    /// Raised when a strike lands: where, damage dealt, and whether it killed.
    public event System.Action<Vector3, float, bool> MeleeHit;

    readonly float[] readyAt = new float[SlotCount];
    float nextMeleeTime;
    AudioSource audioSrc, hitSrc, strikeSrc;
    AudioClip smackClip, thudClip, castClip, thunderClip;
    AudioClip dashClip, pulseClip, throwClip, blastClip, gatherClip;
    static Mesh ringMesh;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Color Cyan = new Color(0.45f, 1f, 0.95f);

    /// 0 just after use, 1 when ready.
    public float Ready01(int i) => Mathf.Clamp01(1f - (readyAt[i] - Time.time) / cooldowns[i]);
    public float Remaining(int i) => Mathf.Max(0f, readyAt[i] - Time.time);
    public bool IsReady(int i) => Time.time >= readyAt[i];

    void Awake()
    {
        Instance = this;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.8f;
        dashClip = ProceduralAudio.Whoosh(0.28f);
        pulseClip = ProceduralAudio.Shockwave(0.55f);
        throwClip = ProceduralAudio.Whoosh(0.16f);
        blastClip = ProceduralAudio.Explosion(0.9f);
        gatherClip = ProceduralAudio.Charge(0.42f);
        smackClip = ProceduralAudio.Thwack();
        thudClip = ProceduralAudio.Thump(0.26f);
        hitSrc = gameObject.AddComponent<AudioSource>();
        hitSrc.spatialBlend = 0f; hitSrc.volume = 0.9f;
        strikeSrc = gameObject.AddComponent<AudioSource>();
        strikeSrc.spatialBlend = 0f; strikeSrc.volume = 0.9f;
        castClip = ProceduralAudio.Charge(CastSlam);
        thunderClip = ProceduralAudio.Thunder();
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool control = (gm == null || gm.State == GameManager.GameState.Playing) && Cursor.lockState == CursorLockMode.Locked;
        if (gm == null || gm.State == GameManager.GameState.Playing) AddSuper(chargePerSecond * Time.deltaTime);
        if (!control || InputGate.Blocked || casting) return;
        if (Input.GetKeyDown(KeyCode.F)) TryUse(Super);
        if (Input.GetKeyDown(KeyCode.Q)) TryUse(Melee);
        if (Input.GetKeyDown(KeyCode.G)) TryUse(Grenade);
    }

    /// Used by the scripted smoke test.
    public bool DebugUse(int ability) => TryUse(ability);

    bool TryUse(int ability)
    {
        if (casting) return false;
        if (ability == Super)
        {
            if (!SuperReady) return false;
            SuperCharge = 0f;
            StartCoroutine(LightningWave());
            Used?.Invoke(Super);
            return true;
        }
        if (ability == Melee)
        {
            if (Time.time < nextMeleeTime) return false;
            StartCoroutine(Strike());
            return true;
        }
        if (!IsReady(ability)) return false;
        readyAt[ability] = Time.time + cooldowns[ability];
        switch (ability)
        {
            case Grenade: StartCoroutine(ThrowGrenade()); break;
        }
        Used?.Invoke(ability);
        return true;
    }

    // ------------------------------------------------------------------ super
    void OnEnable() { EnemyAI.AnyDied += OnEnemyDied; }
    void OnDisable() { EnemyAI.AnyDied -= OnEnemyDied; }

    void Start()
    {
        if (arsenal != null)
            foreach (var w in arsenal.weapons) w.Hit += (point, dealt, killed, precision) => AddSuper(dealt * chargePerDamage);
    }

    void OnEnemyDied(EnemyAI enemy) { AddSuper(enemy.elite ? chargePerEliteKill : chargePerKill); }
    void AddSuper(float amount) { SuperCharge = Mathf.Clamp01(SuperCharge + amount); }

    /// Smoke test only.
    public void DebugChargeSuper() { SuperCharge = 1f; }
    /// The test range's refill pad: every ability ready, the super charged.
    public void DebugRefill() { for (int i = 0; i < readyAt.Length; i++) readyAt[i] = 0f; nextMeleeTime = 0f; SuperCharge = 1f; }

    // ------------------------------------------------------------------ super: Lightning Wave
    // The view pulls out behind the character. They raise a hand, palm up, and lightning gathers in it; after a
    // couple of seconds the hand closes and is driven down, and a wave rolls out from the body along the ground.
    // Every enemy the wave reaches is struck by a bolt out of the sky. Then the view returns to the eyes.
    // The timings here match the body's "Cast" clip (ArtSource/blender/rig_enemy.py, model "player").
    const float CastRaise = 0.45f;          // the hand is up by now
    const float CastSlam = 2.2f;            // the fist lands and the wave leaves
    static readonly Color Bolt = new Color(0.72f, 0.82f, 1f);

    IEnumerator LightningWave()
    {
        casting = true;
        var health = player.GetComponent<Health>();
        if (health != null) { health.ResetFull(); health.immune = true; }
        float waveTime = surgeRadius / waveSpeed;
        float total = CastSlam + waveTime + 0.55f;
        weapon.Lower(total + 0.5f);
        bool thirdPerson = body != null;
        Transform camT = cam.transform;
        Vector3 eyeLocalPos = camT.localPosition;
        var vmCam = weapon.viewModelCamera;
        if (thirdPerson)
        {
            player.frozen = true;
            player.externalCamera = true;
            if (vmCam) vmCam.enabled = false;
            body.gameObject.SetActive(true);
            body.Play("Cast", 0, 0f);
            body.Update(0f);
        }
        else PlayHand(Hand.Surge, 0.72f);
        audioSrc.pitch = 0.8f;
        audioSrc.PlayOneShot(castClip, 1f);

        var struck = new HashSet<EnemyAI>();
        GameObject wave = null;
        MeshRenderer waveRenderer = null;
        var block = new MaterialPropertyBlock();
        Light handLight = null;
        bool slammed = false;
        Vector3 ground = player.transform.position;
        float start = Time.time;
        while (Time.time - start < total)
        {
            float t = Time.time - start;

            // ---- the camera: out to behind the shoulder, hold, and back to the eyes at the end
            if (thirdPerson)
            {
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.45f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((total - t) / 0.4f));
                Vector3 eye = player.transform.TransformPoint(eyeLocalPos);
                Vector3 pivot = player.transform.position + Vector3.up * 1.55f;
                float pull = t > CastSlam ? Mathf.Min(1f, (t - CastSlam) / 0.5f) : 0f;         // draws back a little as the wave goes out
                Vector3 want = pivot - player.transform.forward * (4.4f + 1.6f * pull) + Vector3.up * (0.9f + 0.9f * pull) + player.transform.right * 1.0f;
                if (Physics.Linecast(pivot, want, out RaycastHit wall, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && wall.collider.GetComponentInParent<EnemyAI>() == null) want = wall.point + (pivot - want).normalized * 0.35f;
                Vector3 pos = Vector3.Lerp(eye, want, blend);
                Vector3 lookAt = Vector3.Lerp(eye + player.transform.forward * 10f, pivot + Vector3.up * 0.45f, blend);
                camT.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            }

            // ---- gathering: arcs crawl over the raised hand and reach for the ground around the caster
            Vector3 hand = bodyHand != null && thirdPerson ? bodyHand.position : player.transform.position + Vector3.up * 2.3f;
            if (t > CastRaise * 0.6f && t < CastSlam)
            {
                float charge = Mathf.InverseLerp(CastRaise * 0.6f, CastSlam, t);
                if (handLight == null)
                {
                    handLight = new GameObject("CastLight").AddComponent<Light>();
                    handLight.type = LightType.Point; handLight.color = Bolt;
                }
                handLight.transform.position = hand;
                handLight.range = 5f + 6f * charge;
                handLight.intensity = (2.5f + 8f * charge) * Random.Range(0.6f, 1.2f);       // kept low: it sits beside the character's head
                int arcs = Time.timeScale > 0f ? (Random.value < 0.5f + charge ? 1 : 0) + (charge > 0.6f ? 1 : 0) : 0;
                for (int a = 0; a < arcs; a++)
                {
                    Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.6f - 0.25f;
                    Arc(hand, hand + dir.normalized * Random.Range(0.4f, 0.9f + 1.6f * charge), 0.03f + 0.035f * charge, 0.07f, 6);
                }
                if (Random.value < 0.25f * charge)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.5f, 4f);
                    Arc(hand, player.transform.position + new Vector3(r.x, 0.05f, r.y), 0.045f, 0.09f, 10);
                }
                player.AddShake(3f * charge * Time.deltaTime);
            }

            // ---- the slam: the wave leaves the body
            if (!slammed && t >= CastSlam)
            {
                slammed = true;
                ground = player.transform.position;
                if (handLight != null) Destroy(handLight.gameObject);
                player.AddShake(4f);
                weapon.AddFovKick(10f);
                audioSrc.pitch = 0.85f;
                audioSrc.PlayOneShot(thunderClip, 1f);
                audioSrc.PlayOneShot(pulseClip, 0.8f);
                Flash(ground + Vector3.up * 1.2f, Bolt, 34f, 30f, 0.5f);
                Sparks(ground + Vector3.up * 0.4f, 90, Bolt, 6f, 20f, true);
                Ball(ground + Vector3.up * 0.8f, 0.3f, 2.4f, 0.2f, Bolt * 1.2f);
                for (int a = 0; a < 10; a++)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(2.5f, 6f);
                    Arc(ground + Vector3.up * 1.0f, ground + new Vector3(r.x, 0.05f, r.y), 0.12f, 0.16f, 8);
                }
                if (ringMesh == null) ringMesh = BuildRing();
                wave = new GameObject("LightningWave", typeof(MeshFilter), typeof(MeshRenderer));
                wave.GetComponent<MeshFilter>().sharedMesh = ringMesh;
                waveRenderer = wave.GetComponent<MeshRenderer>();
                waveRenderer.sharedMaterial = flashMaterial;
                waveRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // ---- the wave front runs outward at a steady speed; whatever it reaches is struck from the sky
            if (slammed && wave != null)
            {
                float front = Mathf.Min(surgeRadius, (t - CastSlam) * waveSpeed);
                float fade = Mathf.Clamp01((surgeRadius - front) / (surgeRadius * 0.25f));
                wave.transform.position = ground + Vector3.up * 0.25f;
                wave.transform.localScale = new Vector3(front, 1f, front);
                block.SetColor(BaseColorId, Bolt * (2.6f * fade + 0.2f) * Random.Range(0.75f, 1.15f));
                waveRenderer.SetPropertyBlock(block);
                if (Time.timeScale > 0f && front < surgeRadius)
                    for (int a = 0; a < 5; a++)         // short arcs dancing along the front
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f), span = Random.Range(0.08f, 0.22f);
                        Vector3 p0 = ground + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * front + Vector3.up * Random.Range(0.1f, 0.9f);
                        Vector3 p1 = ground + new Vector3(Mathf.Cos(ang + span), 0f, Mathf.Sin(ang + span)) * front + Vector3.up * Random.Range(0.1f, 1.4f);
                        Arc(p0, p1, 0.09f, 0.08f, 6);
                    }
                foreach (var enemy in EnemyAI.All.ToArray())
                {
                    if (enemy == null || enemy.IsDead || struck.Contains(enemy)) continue;
                    Vector3 to = enemy.transform.position - ground; to.y = 0f;
                    if (to.magnitude > front || Mathf.Abs(enemy.transform.position.y - ground.y) > 9f) continue;
                    struck.Add(enemy);
                    Strike(enemy, ground);
                }
                if (front >= surgeRadius && fade <= 0f) { Destroy(wave); wave = null; }
            }
            yield return null;
        }

        if (wave != null) Destroy(wave);
        if (handLight != null) Destroy(handLight.gameObject);
        if (thirdPerson)
        {
            body.gameObject.SetActive(false);
            if (vmCam) vmCam.enabled = true;
            player.externalCamera = false;
            player.frozen = false;
        }
        if (health != null) health.immune = false;
        casting = false;
    }

    /// A bolt out of the sky onto one enemy.
    void Strike(EnemyAI enemy, Vector3 from)
    {
        Vector3 target = enemy.Centre;
        Vector2 drift = Random.insideUnitCircle * 7f;
        Vector3 sky = target + new Vector3(drift.x, 46f, drift.y);
        Arc(sky, target, 0.75f, 0.26f, 16);
        Arc(sky + Random.insideUnitSphere * 2f, target + Random.insideUnitSphere * 0.4f, 0.3f, 0.2f, 14);
        Vector3 mid = Vector3.Lerp(sky, target, Random.Range(0.45f, 0.7f));
        Arc(mid, mid + new Vector3(Random.Range(-7f, 7f), -Random.Range(5f, 11f), Random.Range(-7f, 7f)), 0.2f, 0.16f, 8);       // a fork that dies in the air
        Vector3 foot = enemy.transform.position;
        Flash(target + Vector3.up, Bolt, 46f, 26f, 0.4f);
        Ring(foot + Vector3.up * 0.15f, 0.4f, 4.5f, 0.35f, Bolt * 2.6f);
        Sparks(target, 46, Bolt, 5f, 16f, false);
        Feel.Instance.Jet(foot + Vector3.up * 0.2f, Vector3.up, 16, Color.Lerp(Bolt, Color.white, 0.5f), 6f, 16f, 22f, 0.14f, 0.5f);
        float near = Mathf.Clamp01(1f - Vector3.Distance(target, player.transform.position) / 40f);
        strikeSrc.pitch = Random.Range(0.85f, 1.25f);
        strikeSrc.PlayOneShot(thunderClip, 0.35f + 0.65f * near);
        player.AddShake(1.6f * near);
        Vector3 away = enemy.transform.position - from; away.y = 0f;
        enemy.Health.Damage(surgeDamage, from);
        enemy.Stagger(1.6f, away.normalized * 7f);
    }

    readonly List<LineRenderer> arcPool = new List<LineRenderer>();

    /// A jagged line of light between two points that flickers and is gone: one stroke of lightning.
    void Arc(Vector3 a, Vector3 b, float width, float life, int segments)
    {
        LineRenderer lr = null;
        foreach (var free in arcPool) if (free != null && !free.gameObject.activeSelf) { lr = free; break; }
        if (lr == null)
        {
            if (arcPool.Count >= 90) return;
            lr = new GameObject("Arc").AddComponent<LineRenderer>();
            lr.sharedMaterial = flashMaterial;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.numCapVertices = 2;
            arcPool.Add(lr);
        }
        lr.gameObject.SetActive(true);
        lr.positionCount = segments + 1;
        Vector3 along = b - a;
        float reach = along.magnitude;
        Vector3 side = Vector3.Cross(along.normalized, Random.onUnitSphere).normalized, up = Vector3.Cross(along.normalized, side);
        for (int k = 0; k <= segments; k++)
        {
            float f = k / (float)segments;
            float wander = Mathf.Sin(f * Mathf.PI) * reach * 0.09f;          // pinned at both ends, wildest in the middle
            lr.SetPosition(k, a + along * f + (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * wander);
        }
        StartCoroutine(FadeArc(lr, width, life));
    }

    IEnumerator FadeArc(LineRenderer lr, float width, float life)
    {
        float t = 0f;
        while (t < life && lr != null)
        {
            t += Time.deltaTime;
            float k = 1f - t / life;
            lr.widthMultiplier = width * k * Random.Range(0.6f, 1.1f);
            Color c = Color.Lerp(Bolt, Color.white, 0.5f) * (1.5f + 2f * k);
            lr.startColor = lr.endColor = new Color(c.r, c.g, c.b, 1f);
            yield return null;
        }
        if (lr != null) lr.gameObject.SetActive(false);
    }

    /// Jets of spray thrown straight up in a circle.
    void Pillars(Vector3 centre, float radius, int count, float height)
    {
        float start = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float a = (start + i * 360f / count) * Mathf.Deg2Rad;
            Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * 0.1f;
            Feel.Instance.Jet(p, Vector3.up, 5, Color.Lerp(Cyan, Color.white, 0.3f), height * 0.5f, height, 7f, 0.15f, 0.6f);
        }
    }

    // ------------------------------------------------------------------ melee
    /// The enemy a strike would go for: the nearest one in reach that the player is roughly facing and can see.
    EnemyAI FindMeleeTarget()
    {
        EnemyAI best = null;
        float bestScore = float.MaxValue;
        Vector3 eye = cam.transform.position;
        foreach (var e in EnemyAI.All)
        {
            if (e == null || e.IsDead) continue;
            Vector3 to = e.Centre - eye;
            float dist = to.magnitude;
            if (dist > lungeRange || Mathf.Abs(e.Centre.y - eye.y) > 2.6f) continue;
            float angle = Vector3.Angle(cam.transform.forward, to);
            if (angle > (dist < 2.5f ? 85f : 50f)) continue;                    // close enough to touch counts even at the edge of view
            if (Physics.Linecast(eye, e.Centre, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<EnemyAI>() == null) continue;      // something solid in the way
            float score = dist + angle * 0.07f;                                 // nearest, leaning toward whatever is under the crosshair
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    IEnumerator Strike()
    {
        nextMeleeTime = Time.time + meleeRecovery;
        bool charged = IsReady(Melee);
        var target = FindMeleeTarget();
        weapon.Lower(0.46f);
        PlayHand(Hand.Strike, 0.47f);
        audioSrc.pitch = 1.45f;
        audioSrc.PlayOneShot(throwClip, 0.8f);
        weapon.AddFovKick(4f);

        // Step in: the player is carried to arm's length of the target and turned to face it, arriving as the palm lands.
        const float land = 0.13f;
        if (target != null)
        {
            Vector3 to = target.transform.position - player.transform.position; to.y = 0f;
            float travel = Mathf.Max(0f, to.magnitude - meleeReach);
            if (travel > 0.05f)
            {
                player.Dash(to, travel / land, land, 0f);       // stops dead at arm's length rather than running on into it
                Feel.Instance.Streaks(land + 0.04f, 0.45f * Mathf.Clamp01(travel / 3f));
            }
        }
        else player.Dash(player.transform.forward, 5f, 0.1f);                 // a swing at nothing still carries you a step
        for (float t = 0f; t < land; t += Time.deltaTime)
        {
            if (target != null && !target.IsDead) player.TurnToward(target.Centre, 1f - Mathf.Exp(-26f * Time.deltaTime));
            yield return null;
        }
        weapon.Punch(-1.2f, -50f, 0f);

        if (target == null || target.IsDead || Vector3.Distance(cam.transform.position, target.Centre) > meleeReach + 1.6f) yield break;   // a miss

        // Contact.
        Vector3 point = Vector3.Lerp(cam.transform.position, target.Centre, 0.8f);
        Vector3 away = target.transform.position - player.transform.position; away.y = 0f; away.Normalize();
        Feel.Instance.HitStop(charged ? 0.11f : 0.08f);
        player.AddShake(charged ? 3f : 2.2f);
        player.AddRecoil(1.4f);
        weapon.AddFovKick(charged ? 8f : 5f);
        hitSrc.pitch = Random.Range(0.75f, 0.85f);
        hitSrc.PlayOneShot(smackClip, 1f);
        hitSrc.PlayOneShot(thudClip, 1f);
        Flash(point, Cyan, charged ? 18f : 9f, 7f, 0.2f);
        Sparks(point, charged ? 40 : 22, Color.Lerp(Cyan, Color.white, 0.4f), 4f, 12f, false);
        Feel.Instance.Jet(point, away + Vector3.up * 0.25f, 18, target.effectColor, 5f, 14f, 26f, 0.15f, 0.45f);

        target.Health.Damage(meleeDamage, player.transform.position);
        target.Stagger(0.9f, away * 10f);
        AddSuper(meleeDamage * chargePerDamage);
        MeleeHit?.Invoke(point, meleeDamage, target.IsDead);
        Used?.Invoke(Melee);

        // A charged strike releases the pulse through whatever it hit: everything around the target is thrown back.
        if (!charged) yield break;
        readyAt[Melee] = Time.time + cooldowns[Melee];
        Vector3 ground = target.transform.position;
        audioSrc.pitch = 1f;
        audioSrc.PlayOneShot(pulseClip);
        foreach (var enemy in EnemiesWithin(ground + Vector3.up * 0.9f, pulseRadius))
        {
            if (enemy == target) continue;
            Vector3 push = enemy.transform.position - player.transform.position; push.y = 0f;
            float k = Mathf.Clamp01(Vector3.Distance(enemy.transform.position, ground) / pulseRadius);
            Feel.Instance.Jet(enemy.transform.position + Vector3.up, Vector3.up + push.normalized, 9, Cyan, 3f, 9f, 30f);
            enemy.Health.Damage(Mathf.Lerp(pulseDamage, pulseDamage * 0.45f, k), ground);
            enemy.Stagger(pulseStun, push.normalized * Mathf.Lerp(11f, 5f, k));
        }
        Ring(ground + Vector3.up * 0.25f, 0.6f, pulseRadius, 0.38f, Cyan * 2.2f);
        Ring(ground + Vector3.up * 0.1f, pulseRadius * 0.4f, pulseRadius * 1.1f, 0.6f, Cyan * 0.7f);
        Pillars(ground, pulseRadius * 0.55f, 10, 6f);
        Flash(ground + Vector3.up, Cyan, 16f, pulseRadius * 1.6f, 0.3f);
    }

    // ------------------------------------------------------------------ grenade
    IEnumerator ThrowGrenade()
    {
        // The weapon dips out of the way while the off hand swings the charge up and lets it go.
        weapon.Lower(0.62f);
        PlayHand(Hand.Throw, 0.6f);
        audioSrc.pitch = 1.2f;
        audioSrc.PlayOneShot(throwClip, 0.8f);
        yield return new WaitForSeconds(ThrowRelease);
        weapon.Punch(-0.6f, 40f, 60f);

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "PulseGrenade";
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = cam.transform.position + cam.transform.forward * 0.55f - cam.transform.right * 0.22f - cam.transform.up * 0.16f;
        // It leaves from where the hand is on screen. The arm is drawn with its own field of view, so convert through the screen.
        if (armGrip != null && weapon.viewModelCamera != null)
            go.transform.position = cam.ViewportToWorldPoint(weapon.viewModelCamera.WorldToViewportPoint(armGrip.position));
        go.GetComponent<MeshRenderer>().sharedMaterial = grenadeMaterial;
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = flashMaterial;
        trail.time = 0.3f; trail.startWidth = 0.15f; trail.endWidth = 0f;
        trail.startColor = Cyan; trail.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point; light.color = Cyan; light.intensity = 4f; light.range = 5f;

        Vector3 vel = cam.transform.forward * throwSpeed + Vector3.up * 3.2f;
        float born = Time.time;
        int bounces = 0;
        const float radius = 0.1f;
        while (Time.time - born < fuse)
        {
            float dt = Time.deltaTime;
            vel.y -= 19f * dt;
            Vector3 step = vel * dt;
            if (Physics.SphereCast(go.transform.position, radius, step.normalized, out RaycastHit hit, step.magnitude + 0.02f,
                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                go.transform.position = hit.point + hit.normal * radius;
                if (hit.collider.GetComponentInParent<EnemyAI>() != null || bounces >= 1) break;   // burst on a creature or the second surface
                bounces++;
                vel = Vector3.Reflect(vel, hit.normal) * 0.38f;
                Feel.Instance.Jet(hit.point, hit.normal, 8, Cyan, 2f, 5f, 40f, 0.08f, 0.3f);       // sparks where it skips off the ground
            }
            else go.transform.position += step;
            light.intensity = 4f + 3f * Mathf.Sin((Time.time - born) * 30f);        // quickening pulse as the fuse burns
            yield return null;
        }
        Explode(go.transform.position);
        Destroy(go);
    }

    void Explode(Vector3 p)
    {
        foreach (var enemy in EnemiesWithin(p, blastRadius))
        {
            Vector3 away = enemy.transform.position - p; away.y = 0f;
            float t = Mathf.Clamp01(Vector3.Distance(enemy.transform.position + Vector3.up, p) / blastRadius);
            enemy.Health.Damage(Mathf.Lerp(blastDamage, blastDamage * 0.3f, t), p);            // thrown away from the blast, not from the player
            enemy.Stagger(0.8f, away.normalized * Mathf.Lerp(8f, 3f, t));
        }
        AudioSource.PlayClipAtPoint(blastClip, p, 1f);
        float near = Mathf.Clamp01(1f - Vector3.Distance(p, player.transform.position) / 18f);
        player.AddShake(3f * near);
        weapon.AddFovKick(3f * near);

        Ring(p + Vector3.up * 0.15f, 0.4f, blastRadius, 0.32f, Cyan * 2.4f);
        Ring(p + Vector3.up * 0.1f, blastRadius * 0.5f, blastRadius * 1.25f, 0.6f, Cyan * 0.8f);
        Ring(p + Vector3.up * 0.05f, blastRadius * 0.55f, blastRadius * 0.6f, 1.6f, Cyan * 0.45f);     // a scorch that lingers
        Ball(p, 0.4f, blastRadius * 0.55f, 0.22f, new Color(0.8f, 1f, 1f) * 1.0f);
        Flash(p + Vector3.up * 0.5f, Cyan, 22f, blastRadius * 2.4f, 0.4f);
        Sparks(p + Vector3.up * 0.2f, 60, Cyan, 5f, 14f, false);
        Feel.Instance.Jet(p + Vector3.up * 0.1f, Vector3.up, 34, Color.Lerp(Cyan, Color.white, 0.4f), 6f, 17f, 32f, 0.16f, 0.75f);   // debris thrown high
    }

    // ------------------------------------------------------------------ the ability arm
    // While an ability is used the weapon drops out of view and a separate, fully animated left arm comes up to
    // act it out (clips Strike, Throw and Surge, built by ArtSource/blender/build_fp_arm.py).
    public enum Hand { None, Strike, Throw, Surge }
    public Animator arm;

    Hand handAction;
    float handStart, handLength;
    Transform armGrip, orb, held;
    const float ThrowRelease = 0.233f;     // frame 7 of the Throw clip
    Light orbLight;
    MaterialPropertyBlock orbBlock;

    void PlayHand(Hand action, float seconds)
    {
        handAction = action; handStart = Time.time; handLength = seconds;
        if (arm == null) return;
        arm.gameObject.SetActive(true);
        arm.Play(action.ToString(), 0, 0f);
        arm.Update(0f);
        if (armGrip == null)
            foreach (var t in arm.GetComponentsInChildren<Transform>()) if (t.name == "grip") armGrip = t;
    }

    void LateUpdate()
    {
        if (handAction == Hand.None) return;
        float t = Time.time - handStart;
        if (t >= handLength)
        {
            handAction = Hand.None;
            if (arm) arm.gameObject.SetActive(false);
            if (orb) orb.gameObject.SetActive(false);
            if (held) held.gameObject.SetActive(false);
            return;
        }
        if (armGrip == null) return;
        // The grenade sits in the closed hand until the moment it is thrown.
        bool holding = handAction == Hand.Throw && t < ThrowRelease;
        if (holding && held == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "HeldGrenade";
            g.layer = arm.gameObject.layer;
            g.transform.SetParent(cam.transform, false);
            g.transform.localScale = Vector3.one * 0.084f;
            var gr = g.GetComponent<MeshRenderer>();
            gr.sharedMaterial = grenadeMaterial;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            held = g.transform;
        }
        if (held != null)
        {
            held.gameObject.SetActive(holding);
            if (holding) held.position = armGrip.position;
        }

        // The light held in the hand: it swells before a charged strike or the surge, and is the charge itself before a throw.
        float glow = handAction == Hand.Strike ? (IsReady(Melee) && t < 0.13f ? t / 0.13f : 0f)          // only a charged strike glows
                   : handAction == Hand.Throw ? 0f
                   : (t < 0.4f ? 0.3f + 1.9f * t / 0.4f : 0f);
        if (orb == null) BuildOrb(arm.gameObject.layer);
        orb.gameObject.SetActive(glow > 0.01f);
        if (glow <= 0.01f) return;
        orb.position = armGrip.position;
        float size = (handAction == Hand.Throw ? 0.085f : 0.035f + 0.022f * glow) * (1f + 0.12f * Mathf.Sin(Time.time * 40f));
        orb.localScale = Vector3.one * size;
        orbBlock.SetColor(BaseColorId, Color.Lerp(Cyan, Color.white, 0.3f) * (0.7f + 0.35f * glow));     // kept dim: it sits right in front of the eye
        orb.GetComponent<MeshRenderer>().SetPropertyBlock(orbBlock);
        orbLight.intensity = 1f + 1.2f * glow;
    }

    void BuildOrb(int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "PalmLight";
        go.layer = layer;
        go.transform.SetParent(cam.transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = flashMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orb = go.transform;
        orbBlock = new MaterialPropertyBlock();
        orbLight = go.AddComponent<Light>();
        orbLight.type = LightType.Point; orbLight.color = Cyan; orbLight.range = 1.2f;
    }

    // ------------------------------------------------------------------ shared helpers
    static IEnumerable<EnemyAI> EnemiesWithin(Vector3 point, float radius)
    {
        var seen = new HashSet<EnemyAI>();
        foreach (var c in Physics.OverlapSphere(point, radius))
        {
            var e = c.GetComponentInParent<EnemyAI>();
            if (e != null && !e.IsDead && seen.Add(e)) yield return e;
        }
    }

    /// Flat shockwave ring that expands along the ground and fades.
    public void Ring(Vector3 position, float from, float to, float time, Color color)
    {
        if (ringMesh == null) ringMesh = BuildRing();
        var go = new GameObject("Shockwave", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.position = position;
        go.GetComponent<MeshFilter>().sharedMesh = ringMesh;
        StartCoroutine(Expand(go, from, to, time, color, false));
    }

    /// Expanding ball of light at the heart of an explosion.
    void Ball(Vector3 position, float from, float to, float time, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        StartCoroutine(Expand(go, from, to, time, color, true));
    }

    IEnumerator Expand(GameObject go, float from, float to, float time, Color color, bool uniform)
    {
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = flashMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / time);
            float r = Mathf.Lerp(from, to, 1f - (1f - k) * (1f - k));      // fast out, easing to a stop
            go.transform.localScale = uniform ? Vector3.one * r * 2f : new Vector3(r, 1f, r);
            block.SetColor(BaseColorId, color * (1f - k) * (1f - k));
            mr.SetPropertyBlock(block);
            yield return null;
        }
        Destroy(go);
    }

    public void Flash(Vector3 position, Color color, float intensity, float range, float time)
    {
        var go = new GameObject("AbilityFlash");
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = color; l.range = range; l.intensity = intensity;
        StartCoroutine(FadeLight(l, intensity, time));
    }

    IEnumerator FadeLight(Light l, float intensity, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            l.intensity = intensity * (1f - t / time);
            yield return null;
        }
        Destroy(l.gameObject);
    }

    /// Burst of sparks using the weapon's impact particle system.
    public void Sparks(Vector3 position, int count, Color color, float minSpeed, float maxSpeed, bool flat)
    {
        if (weapon == null || weapon.impactFx == null) return;
        var ep = new ParticleSystem.EmitParams { position = position, startColor = color, startSize = 0.13f, startLifetime = 0.55f };
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (flat) dir.y = Mathf.Abs(dir.y) * 0.25f; else dir.y = Mathf.Abs(dir.y);
            ep.velocity = dir.normalized * Random.Range(minSpeed, maxSpeed);
            weapon.impactFx.Emit(ep, 1);
        }
    }

    /// Unit annulus in the XZ plane: outer radius 1, fading to transparent on its inner edge.
    static Mesh BuildRing()
    {
        const int seg = 64;
        const float inner = 0.72f;
        var verts = new Vector3[seg * 2];
        var cols = new Color[seg * 2];
        var uvs = new Vector2[seg * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = d * inner; cols[i * 2] = new Color(1, 1, 1, 0f);
            verts[i * 2 + 1] = d; cols[i * 2 + 1] = Color.white;
            uvs[i * 2] = uvs[i * 2 + 1] = new Vector2(0.5f, 0.5f);
            int n = (i + 1) % seg, t = i * 6;
            tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = n * 2 + 1;
            tris[t + 3] = i * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = n * 2;
        }
        var m = new Mesh { name = "ShockwaveRing", vertices = verts, colors = cols, uv = uvs, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
