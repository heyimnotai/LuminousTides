using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyKind { Stalker, Spitter, Boss, Burster, Duelist, Champion }
public enum Faction { Tideborn, Varrik }

/// One script drives every enemy kind:
///   Stalker - melee: circles in from the side, sidesteps when aimed at, pounces from mid-range, then claws.
///   Spitter - ranged: keeps its distance, fires bursts, lobs globs that leave burning pools, backs off when rushed.
///   Burster - small and fast: sprints at the player and detonates. Shooting it sets it off early, hurting its own pack.
///   Boss    - slow and heavy: ground slam up close, bolt volleys and pools at range, calls for help as it weakens.
///   Duelist - shielded elite: fires at range, then blinks to your flank and closes with a blade.
///   Champion- a faction's heavy: fights like a boss (slam and volleys) but roams the world as an ordinary enemy.
/// Options on top of the kind: `cloaks` (nearly invisible until it strikes or is hit) and a recharging shield
/// (set on its Health), so one script covers each faction's rank ladder.
/// All of them patrol until alerted, are knocked back by hits, can be stunned and shoved by abilities, and are
/// thrown backwards when killed. Reinforcements rise out of the ground with Emerge().
/// Drives an Animator if present (Speed, Attack, Spit, Slam, Hit, Dead); otherwise animates procedurally.
[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class EnemyAI : MonoBehaviour
{
    enum State { Patrol, Chase, Attack, Shoot, Slam, Pounce, Fuse, Dead }

    /// Every living enemy currently in the scene.
    public static readonly List<EnemyAI> All = new List<EnemyAI>();
    public static event System.Action<EnemyAI> AnyDied;

    public EnemyKind kind = EnemyKind.Stalker;
    public Faction faction = Faction.Tideborn;
    public bool cloaks;                // shimmer-cloaked while closing in
    public float hoverHeight;          // above 0 = a flyer: it paths along the ground but its body rides this high
    public bool canBlink;              // heavies that teleport to your flank (duelists always can)
    public GameObject[] summonTemplates;   // called in each time a heavy loses a third of its health
    public int summonCount = 2;
    public Material cloakMaterial;
    public Transform muzzle;           // where bolts leave from; defaults to the head
    public Color effectColor = new Color(1f, 0.55f, 0.15f);   // bolts, slams and death flashes
    public string displayName = "TIDE STALKER";
    public bool elite;                 // tougher rank: shown with a yellow health bar
    public Transform visual;           // model root, animated procedurally when there is no Animator

    [Header("Movement and senses")]
    public float patrolRadius = 9f;
    public float patrolSpeed = 1.8f;
    public float chaseSpeed = 6.2f;
    public float sightRange = 26f;
    public float loseRange = 45f;
    public float eyeHeight = 1.5f;

    [Header("Melee and pounce")]
    public float attackRange = 2.3f;
    public float attackDamage = 18f;
    public float attackWindup = 0.35f;
    public float attackCooldown = 1.1f;
    public bool canPounce = true;

    [Header("Ranged (spitter and boss)")]
    public float preferredRange = 15f;
    public float fireInterval = 2.4f;
    public float boltDamage = 13f;
    public float boltSpeed = 23f;
    public int volleySize = 1;
    public float volleySpread = 0f;       // degrees between bolts; 0 fires them all at the same aim point
    public float volleyGap = 0.16f;       // seconds between bolts
    public float lobInterval = 9f;        // seconds between lobbed globs; 0 = never
    public int lobCount = 1;
    public float poolRadius = 2.8f;
    public Material boltMaterial;
    public Material boltTrailMaterial;

    [Header("Boss slam")]
    public float slamRadius = 6.5f;
    public float slamDamage = 34f;

    [Header("Burster")]
    public float burstRadius = 4.8f;
    public float burstPlayerDamage = 50f;
    public float burstEnemyDamage = 120f;

    /// Too big to flinch, stun or shove.
    public bool Heavy => kind == EnemyKind.Boss || kind == EnemyKind.Champion;
    public bool IsCloaked => cloaked;
    public bool IsDead => state == State.Dead;
    public bool IsAlerted => state != State.Patrol && state != State.Dead;
    public Health Health => health;
    public float LastDamageTime { get; private set; } = -99f;
    /// The middle of its body: where a melee strike or an explosion is aimed.
    public Vector3 Centre => hoverHeight > 0f && visual ? visual.position : transform.position + Vector3.up * eyeHeight * 0.6f;
    /// World point the HUD health bar hovers over.
    public Vector3 BarAnchor => hoverHeight > 0f && visual ? visual.position + Vector3.up * 0.9f
        : (headBone ? headBone.position : transform.position + Vector3.up * 1.9f) + Vector3.up * 0.55f;
    /// Raised by the boss when its health first drops below two thirds, then one third.
    public event System.Action<int> BossThreshold;

    NavMeshAgent agent;
    Health health;
    Animator animator;
    Transform player, headBone;
    PlayerController playerController;
    Health playerHealth;
    AudioSource audioSrc;
    AudioClip screech, squelch, spitClip, beepClip;
    State state = State.Patrol;
    Vector3 home, pushVelocity, pounceFrom, pounceTo, flingVelocity, lastHitFrom;
    float nextPatrolTime, actionTimer, flinch, deathT, staggerUntil, stunUntil, flashUntil;
    float nextShotTime, nextSlamTime, nextRepositionTime, nextPounceTime, nextDodgeTime, nextLobTime;
    float flankSide, emergeT = 1f, emergeDepth, deadTime, nextBlinkTime;
    bool cloaked;
    Material[][] openMaterials;
    // feel: scale punch on hits, the shield breaking, a flinch away from the shot, and bones that track the player
    Vector3 visualBaseScale = Vector3.one, flinchAxis = Vector3.right;
    float hitPunch, lastShield, lastHitAmount, flinchAdd, lookYaw, lookPitch, dissolveT;
    Transform[] lookBones = new Transform[0], flinchBones = new Transform[0];
    float[] lookWeights = new float[0];
    bool dissolveBurst;
    float poise, poiseTime = -9f;
    AudioClip shieldBreakClip;
    bool actionDone, alerted, lobbing, airborne;
    int shotsFired, thresholdsPassed;
    Renderer[] bodyRenderers;
    MaterialPropertyBlock flashBlock;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int AttackId = Animator.StringToHash("Attack");
    static readonly int SpitId = Animator.StringToHash("Spit");
    static readonly int SlamId = Animator.StringToHash("Slam");
    static readonly int HitId = Animator.StringToHash("Hit");
    static readonly int DeadId = Animator.StringToHash("Dead");
    static readonly Color SplashColor = new Color(0.6f, 1f, 0.98f);

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null) animator = null;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.maxDistance = Heavy ? 90f : 40f;
        audioSrc.volume = 0.6f;
        screech = ProceduralAudio.Screech();
        squelch = ProceduralAudio.Squelch();
        spitClip = ProceduralAudio.Whoosh(0.2f);
        beepClip = ProceduralAudio.Ping(0.08f, 1500f);
        home = transform.position;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "head") { headBone = t; break; }
        bodyRenderers = visual ? visual.GetComponentsInChildren<Renderer>() : new Renderer[0];
        flashBlock = new MaterialPropertyBlock();
        // Stagger everyone's timers so a pack does not act in unison.
        nextShotTime = Time.time + Random.Range(0.6f, 1.6f);
        nextPounceTime = Time.time + Random.Range(1.5f, 5f);
        nextLobTime = Time.time + Random.Range(4f, 9f);
        flankSide = Random.Range(-1f, 1f);
        nextBlinkTime = Time.time + Random.Range(3f, 6f);
        openMaterials = new Material[bodyRenderers.Length][];
        for (int i = 0; i < bodyRenderers.Length; i++) openMaterials[i] = bodyRenderers[i].sharedMaterials;
        if (visual) visualBaseScale = visual.localScale;
        lastShield = health.maxShield;
        if (health.maxShield > 0f) shieldBreakClip = ProceduralAudio.ShieldBreak();
        FindAimBones();
    }

    /// The bones that turn toward the player (upper body and head) and the ones that bend when it is hit.
    void FindAimBones()
    {
        if (animator == null) return;
        var byName = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>()) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        var bones = new List<Transform>(); var weights = new List<float>(); var bend = new List<Transform>();
        void Add(string name, float w, bool bends)
        {
            if (!byName.TryGetValue(name, out var t)) return;
            bones.Add(t); weights.Add(w);
            if (bends) bend.Add(t);
        }
        if (byName.ContainsKey("cannon")) Add("cannon", 0.8f, false);          // walkers aim their gun, not their body
        else if (byName.ContainsKey("tail1")) { Add("neck", 0.4f, true); Add("head", 0.5f, false); }    // four-legged: neck and head only
        else { Add("spine", 0.15f, true); Add("chest", 0.2f, true); Add("neck", 0.25f, false); Add("head", 0.35f, false); }
        lookBones = bones.ToArray(); lookWeights = weights.ToArray(); flinchBones = bend.ToArray();
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        agent.enabled = true;
        var gm = GameManager.Instance;
        if (gm != null)
        {
            player = gm.player.transform;
            playerController = gm.player;
            playerHealth = gm.playerHealth;
        }
    }

    // ------------------------------------------------------------------ reactions
    void OnDamaged(float amount, Vector3 source)
    {
        flinch = 1f;
        lastHitFrom = source;
        lastHitAmount = amount;
        hitPunch = 1f;
        Vector3 hitDir = transform.position - source; hitDir.y = 0f;
        if (hitDir.sqrMagnitude > 0.01f) flinchAxis = Vector3.Cross(Vector3.up, hitDir.normalized);
        flinchAdd = Mathf.Min(1.6f, flinchAdd + Mathf.Clamp(amount / 40f, 0.45f, 1f));
        if (animator && !Heavy) animator.SetTrigger(HitId);     // the boss does not flinch
        // Brief flash so every hit reads clearly.
        bool shielded = health.Shield > 0f;
        flashBlock.SetColor(BaseColorId, shielded ? new Color(effectColor.r * 2.2f + 0.3f, effectColor.g * 2.2f + 0.3f, effectColor.b * 2.2f + 0.3f, 1f) : new Color(2.1f, 2.0f, 1.8f, 1f));
        if (shielded && Abilities.Instance != null) Abilities.Instance.Sparks(transform.position + Vector3.up * 1.2f, 5, effectColor, 2f, 5f, false);
        foreach (var r in bodyRenderers) r.SetPropertyBlock(flashBlock);
        flashUntil = Time.time + 0.09f;
        LastDamageTime = Time.time;
        // The shield breaking is a moment of its own: a burst, a crack, and the enemy reels.
        if (lastShield > 0f && health.Shield <= 0f && !health.IsDead)
        {
            if (Abilities.Instance != null)
            {
                Vector3 c = transform.position + Vector3.up * eyeHeight * 0.7f;
                Abilities.Instance.Ring(c, 0.3f, 3.2f, 0.3f, effectColor * 2.4f);
                Abilities.Instance.Flash(c, effectColor, 16f, 10f, 0.25f);
                Abilities.Instance.Sparks(c, 34, effectColor, 4f, 11f, false);
            }
            if (shieldBreakClip) { audioSrc.pitch = Heavy ? 0.75f : 1f; audioSrc.PlayOneShot(shieldBreakClip, 1f); }
            if (!Heavy) stunUntil = Mathf.Max(stunUntil, Time.time + 0.7f);
            Feel.Instance.HitStop(0.05f);
        }
        lastShield = health.Shield;
        // Poise: damage landed in quick succession adds up, and past a point the enemy reels - whatever it was
        // doing is broken off. Heavies need far more of it, and a shield has to come down first.
        if (Time.time - poiseTime > 1.3f) poise = 0f;
        poiseTime = Time.time;
        poise += amount / Mathf.Max(1f, health.maxHealth + health.maxShield);
        if (!shielded && !health.IsDead && poise >= (Heavy ? 0.085f : elite ? 0.26f : 0.34f)) Reel();
        if (!Heavy)
        {
            staggerUntil = Time.time + 0.16f;                                   // every hit briefly checks its charge
            Vector3 away = transform.position - source; away.y = 0f;
            if (!airborne) pushVelocity += away.normalized * Mathf.Min(amount * 0.06f, 3f);     // and knocks it back a little
        }
        audioSrc.pitch = Random.Range(0.85f, 1.25f) * (Heavy ? 0.6f : kind == EnemyKind.Burster ? 1.5f : 1f);
        audioSrc.PlayOneShot(squelch, 0.8f);
        if (state == State.Patrol) Alert();

        if (Heavy)
        {
            float frac = health.Current / health.maxHealth;
            while (thresholdsPassed < 2 && frac <= 1f - (thresholdsPassed + 1) / 3f)
            {
                thresholdsPassed++;
                BossThreshold?.Invoke(thresholdsPassed);
                if (summonTemplates != null && summonTemplates.Length > 0) Summon();
            }
        }
    }

    /// Knocked off balance by concentrated fire: it stumbles back, and an attack or a shot it was winding up is lost.
    void Reel()
    {
        poise = 0f;
        Vector3 away = transform.position - lastHitFrom; away.y = 0f; away.Normalize();
        stunUntil = Mathf.Max(stunUntil, Time.time + (Heavy ? 0.6f : 0.55f));
        flinchAdd = Heavy ? 5f : 1.8f;                 // heavies bend far less per point, so they are given more of it
        hitPunch = 1f;
        if (!Heavy && !airborne) pushVelocity += away * 4.5f;
        if (state == State.Attack || state == State.Shoot || state == State.Fuse) { state = State.Chase; nextShotTime = Time.time + 1.2f; }
        if (animator) animator.SetTrigger(HitId);
        audioSrc.pitch = Heavy ? 0.5f : 1.35f;
        audioSrc.PlayOneShot(screech, 0.7f);
        if (Abilities.Instance != null)
        {
            Vector3 c = hoverHeight > 0f && visual ? visual.position : transform.position + Vector3.up * eyeHeight * 0.7f;
            Abilities.Instance.Sparks(c, Heavy ? 30 : 14, Color.Lerp(effectColor, Color.white, 0.5f), 3f, 9f, false);
            if (Heavy) Abilities.Instance.Ring(transform.position + Vector3.up * 0.2f, 0.5f, 4f, 0.3f, effectColor * 1.6f);
        }
    }

    /// Stops the enemy acting for a while and shoves it (used by the pulse, grenade and blasts).
    public void Stagger(float seconds, Vector3 push)
    {
        if (state == State.Dead) return;
        if (Heavy) { seconds *= 0.12f; push = Vector3.zero; }   // far too heavy to stun or move
        stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
        if (!airborne) pushVelocity = push;
        if (state == State.Attack || state == State.Fuse) state = State.Chase;   // an interrupted lunge or fuse does not go off
        if (state == State.Patrol) Alert();
    }

    /// Makes the enemy hunt the player immediately (used for reinforcements).
    public void ForceAlert() { if (state == State.Patrol) Alert(); }

    /// Reinforcements burst up out of the ground or water instead of appearing from nowhere.
    public void Emerge()
    {
        emergeT = 0f;
        emergeDepth = 2.6f * (visual ? visual.localScale.y : 1f);
        if (visual) visual.localPosition = Vector3.down * emergeDepth;
        stunUntil = Mathf.Max(stunUntil, Time.time + 0.7f);
        if (Abilities.Instance != null)
        {
            float size = Heavy ? 3f : 1f;
            Abilities.Instance.Ring(transform.position + Vector3.up * 0.15f, 0.3f, 2.4f * size, 0.5f, SplashColor * 1.6f);
            Abilities.Instance.Sparks(transform.position + Vector3.up * 0.2f, (int)(18 * size), SplashColor, 3f, 9f * Mathf.Sqrt(size), false);
        }
        if (audioSrc) { audioSrc.pitch = Heavy ? 0.5f : 0.9f; audioSrc.PlayOneShot(spitClip, 1f); }
    }

    void Alert()
    {
        if (state == State.Dead) return;
        state = State.Chase;
        if (!alerted)
        {
            alerted = true;
            audioSrc.pitch = Random.Range(0.85f, 1.2f) * (Heavy ? 0.45f : kind == EnemyKind.Burster ? 1.7f : 1f);
            audioSrc.PlayOneShot(screech);
            // Wake nearby pack members.
            foreach (var c in Physics.OverlapSphere(transform.position, 14f))
            {
                var other = c.GetComponentInParent<EnemyAI>();
                if (other != null && other != this && other.state == State.Patrol) other.Alert();
            }
        }
    }

    void OnDied()
    {
        state = State.Dead;
        SetCloaked(false);
        if (airborne) airborne = false;
        agent.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        audioSrc.pitch = Random.Range(0.5f, 0.65f) * (Heavy ? 0.5f : 1f);
        audioSrc.PlayOneShot(screech, 0.9f);
        if (animator) animator.SetBool(DeadId, true);
        if (GameManager.Instance) GameManager.Instance.RegisterKill(this);
        All.Remove(this);

        if (kind == EnemyKind.Burster)
        {
            Invoke(nameof(Detonate), 0.07f);            // a beat later, so chains visibly ripple through a pack
            AnyDied?.Invoke(this);
            return;
        }
        // Thrown backwards by whatever killed it.
        Vector3 away = transform.position - lastHitFrom; away.y = 0f;
        if (!Heavy && away.sqrMagnitude > 0.01f) flingVelocity = away.normalized * (5.5f + Mathf.Min(lastHitAmount * 0.07f, 7f));   // a big hit throws it further
        if (!Heavy && Abilities.Instance != null)
        {
            // Every kill pops: a burst of sparks and light where it stood, and motes that fly to the player.
            Vector3 c = hoverHeight > 0f && visual ? visual.position : transform.position + Vector3.up * eyeHeight * 0.6f;
            Abilities.Instance.Sparks(c, elite ? 46 : 26, effectColor, 3f, elite ? 12f : 9f, false);
            Abilities.Instance.Flash(c, effectColor, elite ? 16f : 9f, elite ? 12f : 7f, 0.22f);
            Abilities.Instance.Ring(transform.position + Vector3.up * 0.15f, 0.3f, elite ? 3.4f : 2.2f, 0.28f, effectColor * 1.5f);
            Feel.Instance.Jet(c, away.sqrMagnitude > 0.01f ? away.normalized + Vector3.up * 0.4f : Vector3.up, elite ? 22 : 14, Color.Lerp(effectColor, Color.white, 0.35f), 5f, 13f, 22f, 0.14f, 0.5f);
            Feel.Instance.Orbs(c, elite ? 5 : 2, new Color(0.45f, 1f, 0.95f));
        }
        Feel.Instance.HitStop(Heavy ? 0.16f : elite ? 0.075f : 0.045f);
        if (Heavy) Feel.Instance.Orbs(transform.position + Vector3.up * 2f, 12, new Color(0.45f, 1f, 0.95f));
        if (Heavy && Abilities.Instance != null)
        {
            Vector3 p = transform.position + Vector3.up;
            Abilities.Instance.Ring(p, 1f, 14f, 0.9f, effectColor * 2.5f);
            Abilities.Instance.Flash(p + Vector3.up * 2f, effectColor, 40f, 40f, 0.8f);
            Abilities.Instance.Sparks(p + Vector3.up, 80, effectColor, 6f, 18f, false);
        }
        AnyDied?.Invoke(this);
        Destroy(gameObject, Heavy ? 14f : DissolveStart + DissolveTime + 0.1f);
    }

    const float DissolveStart = 1.5f, DissolveTime = 0.75f;

    void Dissolve()
    {
        dissolveT = Mathf.Clamp01((deadTime - DissolveStart) / DissolveTime);
        float glow = 1f + 7f * dissolveT;
        flashBlock.SetColor(BaseColorId, new Color(1f + effectColor.r * glow, 1f + effectColor.g * glow, 1f + effectColor.b * glow, 1f));
        foreach (var r in bodyRenderers) if (r) r.SetPropertyBlock(flashBlock);
        flashUntil = 0f;
        if (!visual) return;
        float k = 1f - dissolveT * dissolveT;
        visual.localScale = new Vector3(visualBaseScale.x * k, visualBaseScale.y * Mathf.Lerp(0.2f, 1f, k), visualBaseScale.z * k);
        if (Abilities.Instance == null) return;
        Vector3 c = hoverHeight > 0f ? visual.position : transform.position + Vector3.up * 0.35f;
        if (Random.value < 30f * Time.deltaTime) Feel.Instance.Jet(c + Random.insideUnitSphere * 0.5f, Vector3.up, 2, effectColor, 1.5f, 4.5f, 30f, 0.09f, 0.6f);
        if (!dissolveBurst && dissolveT > 0.85f)
        {
            dissolveBurst = true;
            Abilities.Instance.Sparks(c, 16, effectColor, 2f, 6f, false);
        }
    }

    void Detonate()
    {
        if (visual) visual.gameObject.SetActive(false);
        Blast.At(transform.position + Vector3.up * 0.6f, burstRadius, burstEnemyDamage, burstPlayerDamage, effectColor, health);
        Destroy(gameObject, 0.6f);
    }

    // ------------------------------------------------------------------ behaviour
    void Update()
    {
        if (state == State.Dead)
        {
            // Slide along the ground as the death animation plays.
            if (flingVelocity.sqrMagnitude > 0.05f)
            {
                transform.position += flingVelocity * Time.deltaTime;
                flingVelocity = Vector3.Lerp(flingVelocity, Vector3.zero, 5f * Time.deltaTime);
            }
            // After lying there a moment the body burns away into light, rather than popping out of existence.
            deadTime += Time.deltaTime;
            if (deadTime > DissolveStart && !Heavy) Dissolve();
            DeathAnim();
            return;
        }
        if (emergeT < 1f)
        {
            emergeT = Mathf.Min(1f, emergeT + Time.deltaTime / 0.6f);
            if (visual) visual.localPosition = Vector3.down * emergeDepth * (1f - Mathf.SmoothStep(0f, 1f, emergeT));
        }
        if (player == null) return;

        bool playerAlive = playerHealth != null && !playerHealth.IsDead;
        float dist = Vector3.Distance(transform.position, player.position);

        // A pounce in flight carries on regardless: the agent is switched off while it is in the air.
        if (state == State.Pounce) { UpdatePounce(playerAlive); return; }

        // Knock-back from hits and abilities, then a short stun during which it cannot act.
        if (pushVelocity.sqrMagnitude > 0.02f && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(pushVelocity * Time.deltaTime);
            pushVelocity = Vector3.Lerp(pushVelocity, Vector3.zero, 6f * Time.deltaTime);
        }
        if (Time.time < stunUntil)
        {
            if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            if (animator) { animator.SetFloat(SpeedId, 0f, 0.1f, Time.deltaTime); animator.speed = 1f; }
            return;
        }
        if (!agent.isOnNavMesh) return;
        if (cloaks) SetCloaked(state != State.Attack && state != State.Pounce && dist > 4.5f && Time.time - LastDamageTime > 1.6f);

        switch (state)
        {
            case State.Patrol:
                agent.speed = patrolSpeed;
                if (playerAlive && dist < sightRange && CanSee()) { Alert(); break; }
                if (Time.time >= nextPatrolTime && (!agent.hasPath || agent.remainingDistance < 0.6f))
                {
                    Vector2 r = Random.insideUnitCircle * patrolRadius;
                    if (NavMesh.SamplePosition(home + new Vector3(r.x, 0, r.y), out NavMeshHit hit, 4f, NavMesh.AllAreas))
                        agent.SetDestination(hit.position);
                    nextPatrolTime = Time.time + Random.Range(2.5f, 6f);
                }
                break;

            case State.Chase:
                if (!playerAlive || (dist > loseRange && !Heavy))
                {
                    state = State.Patrol; alerted = false;
                    agent.SetDestination(home);
                    break;
                }
                agent.speed = Time.time < staggerUntil ? chaseSpeed * 0.25f : chaseSpeed;
                switch (kind)
                {
                    case EnemyKind.Stalker: ChaseAsStalker(dist); break;
                    case EnemyKind.Spitter: ChaseAsSpitter(dist); break;
                    case EnemyKind.Burster: ChaseAsBurster(dist); break;
                    case EnemyKind.Boss: case EnemyKind.Champion: ChaseAsBoss(dist); break;
                    case EnemyKind.Duelist: ChaseAsDuelist(dist); break;
                }
                break;

            case State.Attack:      // melee lunge
                FaceTarget(player.position);
                actionTimer += Time.deltaTime;
                if (!actionDone && actionTimer >= attackWindup)
                {
                    actionDone = true;
                    if (playerAlive && dist <= attackRange + 0.8f)
                        playerHealth.Damage(attackDamage, transform.position);
                }
                if (actionTimer >= attackCooldown) state = State.Chase;
                break;

            case State.Shoot:       // a burst of bolts, a spread volley, or lobbed globs
                FaceTarget(player.position);
                actionTimer += Time.deltaTime;
                const float release = 0.3f;
                int count = lobbing ? lobCount : volleySize;
                float gap = lobbing ? 0.22f : volleyGap;
                while (shotsFired < count && actionTimer >= release + shotsFired * gap)
                {
                    if (lobbing) FireLob(); else FireBolt(shotsFired);
                    shotsFired++;
                }
                if (actionTimer >= release + count * gap + 0.35f)
                {
                    if (lobbing) nextLobTime = Time.time + lobInterval * Random.Range(0.8f, 1.3f);
                    nextShotTime = Time.time + fireInterval * Random.Range(0.8f, 1.25f);
                    state = State.Chase;
                }
                break;

            case State.Fuse:        // burster about to go off: it stops, flashes and beeps
                actionTimer += Time.deltaTime;
                bool lit = Mathf.Sin(actionTimer * 60f) > 0f;
                flashBlock.SetColor(BaseColorId, lit ? new Color(3f, 1.4f, 0.4f, 1f) : Color.white);
                foreach (var r in bodyRenderers) r.SetPropertyBlock(flashBlock);
                if (actionTimer >= 0.55f) health.Damage(9999f, transform.position - transform.forward);
                break;

            case State.Slam:        // boss ground slam with a telegraphed ring
                actionTimer += Time.deltaTime;
                const float impact = 0.67f;
                if (!actionDone && actionTimer >= impact)
                {
                    actionDone = true;
                    Vector3 flat = player.position - transform.position; flat.y = 0f;
                    if (playerAlive && flat.magnitude <= slamRadius && Mathf.Abs(player.position.y - transform.position.y) < 2.2f)
                        playerHealth.Damage(slamDamage, transform.position);
                    if (playerController) playerController.AddShake(Mathf.Lerp(3f, 0.5f, Mathf.Clamp01(flat.magnitude / 25f)));
                    if (Abilities.Instance != null)
                    {
                        Abilities.Instance.Ring(transform.position + Vector3.up * 0.2f, 1f, slamRadius, 0.35f, effectColor * 2.6f);
                        Abilities.Instance.Flash(transform.position + Vector3.up, effectColor, 22f, slamRadius * 2f, 0.3f);
                        Abilities.Instance.Sparks(transform.position + Vector3.up * 0.3f, 40, effectColor, 5f, 13f, true);
                    }
                    audioSrc.pitch = 0.7f;
                    audioSrc.PlayOneShot(ProceduralAudio.Thump(0.35f), 1f);
                }
                if (actionTimer >= 1.25f)
                {
                    nextSlamTime = Time.time + 3.2f;
                    state = State.Chase;
                }
                break;
        }

        if (hoverHeight > 0f) HoverAnim();
        else if (animator) Locomote();
        else ProceduralAnim();
    }

    /// Feeds the run cycle. The stride grows and quickens with ground speed, so a patrol walks, a chase runs,
    /// and feet do not slide. Other clips (attacks, hits) always play at normal speed.
    void Locomote()
    {
        float v = agent.velocity.magnitude;
        float frac = Mathf.Clamp01(v / Mathf.Max(0.1f, chaseSpeed));
        animator.SetFloat(SpeedId, v < 0.25f ? 0f : 5f * Mathf.Sqrt(frac), 0.12f, Time.deltaTime);
        bool moving = (state == State.Chase || state == State.Patrol) && v > 0.25f;
        animator.speed = moving ? Mathf.Lerp(0.6f, 1.12f, frac) : 1f;
    }

    float hoverSeed, fallSpeed;

    /// Flyers bob on the air, bank into their turns and tip forward as they move.
    void HoverAnim()
    {
        if (!visual || emergeT < 1f) return;
        if (hoverSeed == 0f) hoverSeed = Random.Range(1f, 50f);
        flinch = Mathf.Lerp(flinch, 0f, 8f * Time.deltaTime);
        Vector3 local = transform.InverseTransformDirection(agent.velocity);
        float bob = Mathf.Sin(Time.time * 1.7f + hoverSeed) * 0.35f + Mathf.Sin(Time.time * 4.1f + hoverSeed) * 0.06f;
        visual.localPosition = Vector3.Lerp(visual.localPosition, new Vector3(0f, hoverHeight + bob - 0.5f * flinch, 0f), 6f * Time.deltaTime);
        var tilt = Quaternion.Euler(Mathf.Clamp(local.z * 2.2f, -18f, 18f) - 14f * flinch, 0f, Mathf.Clamp(-local.x * 4.5f, -32f, 32f));
        visual.localRotation = Quaternion.Slerp(visual.localRotation, tilt, 5f * Time.deltaTime);
    }

    void ChaseAsStalker(float dist)
    {
        if (dist <= attackRange) { BeginMelee(); return; }
        if (canPounce && dist > 5.5f && dist < 11f && Time.time >= nextPounceTime && CanSee()) { BeginPounce(); return; }

        // Come in on a curve from one side rather than in a straight line, so a pack spreads around the player.
        Vector3 toPlayer = player.position - transform.position; toPlayer.y = 0f;
        Vector3 destination = player.position;
        if (dist > 6f) destination += Vector3.Cross(Vector3.up, toPlayer.normalized) * flankSide * Mathf.Min(dist * 0.5f, 7f);
        agent.SetDestination(destination);
        TryDodge(dist);
    }

    /// Sidesteps when the player is drawing a bead on it.
    void TryDodge(float dist)
    {
        if (Time.time < nextDodgeTime || dist > 28f || dist < 4f || playerController == null) return;
        Transform eye = playerController.cam;
        float angle = Vector3.Angle(eye.forward, transform.position + Vector3.up * 1.1f - eye.position);
        if (angle > 3.5f) { nextDodgeTime = Time.time + 0.25f; return; }
        if (Random.value < 0.6f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, (transform.position - eye.position).normalized) * (Random.value < 0.5f ? -1f : 1f);
            pushVelocity = side * 11f;
        }
        nextDodgeTime = Time.time + Random.Range(2.2f, 4.5f);
    }

    void BeginPounce()
    {
        state = State.Pounce;
        actionTimer = 0f; actionDone = false;
        agent.ResetPath();
        pounceFrom = transform.position;
        // Aim where the player is heading, so standing still or running straight gets caught and a sidestep does not.
        Vector3 target = player.position + (playerController ? new Vector3(playerController.Velocity.x, 0f, playerController.Velocity.z) * 0.3f : Vector3.zero);
        pounceTo = NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : player.position;
        nextPounceTime = Time.time + Random.Range(5f, 8.5f);
        if (animator) animator.SetTrigger(AttackId);
        audioSrc.pitch = 1.5f;
        audioSrc.PlayOneShot(screech, 0.8f);
    }

    void UpdatePounce(bool playerAlive)
    {
        const float windup = 0.42f, flight = 0.5f;
        actionTimer += Time.deltaTime;
        if (actionTimer < windup)
        {
            FaceTarget(pounceTo);
            if (visual && !animator) visual.localPosition = Vector3.down * 0.25f * (actionTimer / windup);    // crouch
            return;
        }
        if (!airborne)
        {
            airborne = true;
            agent.enabled = false;
            pounceFrom = transform.position;
        }
        float t = Mathf.Clamp01((actionTimer - windup) / flight);
        transform.position = Vector3.Lerp(pounceFrom, pounceTo, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 2.3f;
        if (t < 1f) return;

        // Landed.
        airborne = false;
        transform.position = pounceTo;
        agent.enabled = true;
        if (agent.isOnNavMesh) agent.Warp(pounceTo);
        Vector3 flat = player.position - pounceTo; flat.y = 0f;
        if (playerAlive && flat.magnitude < 2.5f && Mathf.Abs(player.position.y - pounceTo.y) < 2f)
            playerHealth.Damage(attackDamage * 1.3f, pounceFrom);
        if (Abilities.Instance != null) Abilities.Instance.Ring(pounceTo + Vector3.up * 0.1f, 0.3f, 2.4f, 0.25f, new Color(1f, 0.5f, 0.3f) * 1.3f);
        staggerUntil = Time.time + 0.35f;       // a moment to recover, which is the opening to shoot it
        state = State.Chase;
    }

    void ChaseAsSpitter(float dist)
    {
        bool sees = CanSee();
        if (dist <= attackRange) { BeginMelee(); return; }
        if (sees && lobInterval > 0f && dist > 7f && dist < 30f && Time.time >= nextLobTime) { BeginShoot(true); return; }
        if (sees && dist < 32f && Time.time >= nextShotTime) { BeginShoot(false); return; }

        if (!sees || dist > preferredRange + 4f)
            agent.SetDestination(player.position);                       // close in until it has a shot
        else if (dist < 7f)
        {
            // Rushed: back away from the player.
            Vector3 away = (transform.position - player.position); away.y = 0f;
            if (NavMesh.SamplePosition(transform.position + away.normalized * 7f, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }
        else if (Time.time >= nextRepositionTime)
        {
            // In a good spot: sidestep now and then so it is not a stationary target.
            Vector3 toPlayer = (player.position - transform.position); toPlayer.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer.normalized) * (Random.value < 0.5f ? -1f : 1f);
            if (NavMesh.SamplePosition(transform.position + side * Random.Range(3f, 6f), out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
            nextRepositionTime = Time.time + Random.Range(2.5f, 4.5f);
        }
        if (agent.velocity.sqrMagnitude < 0.3f) FaceTarget(player.position);
    }

    void ChaseAsBurster(float dist)
    {
        agent.SetDestination(player.position);
        if (dist <= 2.9f)
        {
            state = State.Fuse;
            actionTimer = 0f;
            agent.ResetPath();
            audioSrc.pitch = 1.6f;
            audioSrc.PlayOneShot(beepClip, 1f);
        }
    }

    void ChaseAsBoss(float dist)
    {
        if (canBlink && dist > 8f && dist < 28f && Time.time >= nextBlinkTime && CanSee() && Blink()) return;
        if (dist <= slamRadius - 1.4f && Time.time >= nextSlamTime) { BeginSlam(); return; }
        if (dist > 9f && CanSee())
        {
            if (lobInterval > 0f && Time.time >= nextLobTime) { BeginShoot(true); return; }
            if (Time.time >= nextShotTime) { BeginShoot(false); return; }
        }
        agent.SetDestination(player.position);
    }

    void ChaseAsDuelist(float dist)
    {
        bool sees = CanSee();
        if (dist <= attackRange) { BeginMelee(); return; }
        // Blink: vanish and reappear on the player's flank, close enough to strike.
        if (sees && dist > 6f && dist < 24f && Time.time >= nextBlinkTime && Blink()) return;
        if (sees && dist > 6f && dist < 34f && Time.time >= nextShotTime) { BeginShoot(false); return; }
        agent.SetDestination(player.position);
        TryDodge(dist);
    }

    /// Calls a guard in around itself.
    void Summon()
    {
        for (int i = 0; i < summonCount; i++)
        {
            var template = summonTemplates[i % summonTemplates.Length];
            Vector3 around = Quaternion.Euler(0f, i * 360f / summonCount + Random.Range(-25f, 25f), 0f) * Vector3.forward * Random.Range(4f, 6.5f);
            if (template == null || !NavMesh.SamplePosition(transform.position + around, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            var go = Instantiate(template, hit.position, Quaternion.LookRotation(-around.normalized));
            go.SetActive(true);
            var e = go.GetComponent<EnemyAI>();
            if (e == null) continue;
            e.ForceAlert();
            if (e.hoverHeight <= 0f) e.Emerge();
            if (Abilities.Instance != null) Abilities.Instance.Ring(hit.position + Vector3.up * 0.1f, 0.2f, 2f, 0.4f, effectColor * 1.8f);
        }
        if (Abilities.Instance != null) Abilities.Instance.Flash(transform.position + Vector3.up * 2f, effectColor, 18f, 16f, 0.4f);
        audioSrc.pitch = 0.6f;
        audioSrc.PlayOneShot(screech, 1f);
    }

    bool Blink()
    {
        Vector3 toMe = transform.position - player.position; toMe.y = 0f;
        Vector3 side = Quaternion.AngleAxis(Random.Range(55f, 110f) * (Random.value < 0.5f ? -1f : 1f), Vector3.up) * toMe.normalized;
        if (!NavMesh.SamplePosition(player.position + side * 3.6f, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) return false;
        nextBlinkTime = Time.time + Random.Range(5.5f, 8f);
        if (Abilities.Instance != null)
        {
            Abilities.Instance.Flash(transform.position + Vector3.up, effectColor, 10f, 8f, 0.2f);
            Abilities.Instance.Sparks(transform.position + Vector3.up, 14, effectColor, 3f, 7f, false);
            Abilities.Instance.Ring(hit.position + Vector3.up * 0.1f, 0.2f, 1.8f, 0.25f, effectColor * 1.8f);
        }
        agent.Warp(hit.position);
        FaceNow(player.position);
        staggerUntil = Time.time + 0.45f;          // a beat after arriving before it can strike: the window to react
        stunUntil = Mathf.Max(stunUntil, Time.time + 0.3f);
        audioSrc.pitch = 1.8f;
        audioSrc.PlayOneShot(spitClip, 1f);
        return true;
    }

    void FaceNow(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
    }

    void SetCloaked(bool on)
    {
        if (on == cloaked || cloakMaterial == null) return;
        cloaked = on;
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (!on) { bodyRenderers[i].sharedMaterials = openMaterials[i]; continue; }
            var mats = new Material[openMaterials[i].Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = cloakMaterial;
            bodyRenderers[i].sharedMaterials = mats;
        }
        if (!on && Abilities.Instance != null) Abilities.Instance.Sparks(transform.position + Vector3.up, 8, effectColor, 2f, 5f, false);
    }

    void BeginMelee()
    {
        state = State.Attack;
        actionTimer = 0f; actionDone = false;
        agent.ResetPath();
        if (animator) animator.SetTrigger(AttackId);
    }

    void BeginShoot(bool lob)
    {
        state = State.Shoot;
        lobbing = lob;
        actionTimer = 0f; shotsFired = 0;
        agent.ResetPath();
        if (animator) animator.SetTrigger(SpitId);
    }

    void BeginSlam()
    {
        state = State.Slam;
        actionTimer = 0f; actionDone = false;
        agent.ResetPath();
        if (animator) animator.SetTrigger(SlamId);
        // Warning ring: shows the danger area before the blow lands.
        if (Abilities.Instance != null)
            Abilities.Instance.Ring(transform.position + Vector3.up * 0.15f, slamRadius * 0.96f, slamRadius, 0.66f, new Color(1f, 0.25f, 0.1f) * 1.2f);
    }

    Vector3 MouthPosition() => muzzle
        ? new Vector3(transform.position.x, muzzle.position.y, transform.position.z) + transform.forward * 0.9f + Vector3.up * 0.15f
        : (headBone ? headBone.position : transform.position + Vector3.up * eyeHeight) + transform.forward * 0.5f;

    void FireBolt(int index)
    {
        Vector3 origin = MouthPosition();
        // Lead the target a little, so standing still or running straight gets punished but a dash dodges it.
        Vector3 target = player.position + Vector3.up * 1.1f;
        float flight = Vector3.Distance(origin, target) / boltSpeed;
        if (playerController) target += playerController.Velocity * flight * 0.55f;
        Vector3 dir = (target - origin).normalized;
        if (volleySpread > 0f && volleySize > 1)
            dir = Quaternion.AngleAxis((index - (volleySize - 1) * 0.5f) * volleySpread, Vector3.up) * dir;
        EnemyBolt.Spawn(origin, dir * boltSpeed, boltDamage, Heavy ? 0.42f : 0.28f, boltMaterial, boltTrailMaterial);
        audioSrc.pitch = Random.Range(0.8f, 1f) * (Heavy ? 0.7f : 1.3f);
        audioSrc.PlayOneShot(spitClip, 0.9f);
    }

    void FireLob()
    {
        Vector3 origin = MouthPosition() + Vector3.up * 0.3f;
        // The first glob is aimed at the player; any others are scattered around them to cut off escape routes.
        Vector2 scatter = shotsFired == 0 ? Vector2.zero : Random.insideUnitCircle.normalized * Random.Range(3.5f, 6.5f);
        Vector3 target = player.position + new Vector3(scatter.x, 0f, scatter.y);
        float flightTime = Mathf.Clamp(Vector3.Distance(origin, target) / 13f, 0.9f, 1.8f);
        EnemyBolt.SpawnLob(origin, target, flightTime, boltDamage, poolRadius, boltMaterial, boltTrailMaterial);
        audioSrc.pitch = Heavy ? 0.55f : 0.85f;
        audioSrc.PlayOneShot(spitClip, 1f);
    }

    void LateUpdate()
    {
        if (flashUntil > 0f && Time.time >= flashUntil && state != State.Fuse)
        {
            flashUntil = 0f;
            foreach (var r in bodyRenderers) r.SetPropertyBlock(null);
        }
        if (state == State.Dead) return;
        lastShield = health.Shield;
        // Each hit makes the body jolt outward for an instant.
        if (visual && (hitPunch > 0f || visual.localScale != visualBaseScale))
        {
            hitPunch = Mathf.MoveTowards(hitPunch, 0f, Time.deltaTime * 9f);
            visual.localScale = visualBaseScale * (1f + (Heavy ? 0.025f : 0.085f) * hitPunch * hitPunch);
        }
        PoseBones();
    }

    /// Layered over the animation clips each frame: the upper body and head turn to keep their eyes (and guns)
    /// on the player, the body bends away from each shot that lands, and a stunned enemy sags and sways.
    void PoseBones()
    {
        if (animator == null || lookBones.Length == 0 || bodyRenderers.Length == 0 || !bodyRenderers[0].isVisible) return;
        float dt = Time.deltaTime;
        float targetYaw = 0f, targetPitch = 0f;
        bool stunned = Time.time < stunUntil;
        if (IsAlerted && player != null && !stunned && state != State.Pounce)
        {
            Vector3 from = headBone ? headBone.position : transform.position + Vector3.up * eyeHeight;
            Vector3 to = transform.InverseTransformDirection(player.position + Vector3.up * 1.3f - from);
            targetYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -65f, 65f);
            targetPitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -35f, 35f);
        }
        lookYaw = Mathf.Lerp(lookYaw, targetYaw, 7f * dt);
        lookPitch = Mathf.Lerp(lookPitch, targetPitch, 7f * dt);
        flinchAdd = Mathf.Lerp(flinchAdd, 0f, 8f * dt);
        for (int i = 0; i < lookBones.Length; i++)
        {
            var q = Quaternion.AngleAxis(lookYaw * lookWeights[i], transform.up) * Quaternion.AngleAxis(lookPitch * lookWeights[i], transform.right);
            lookBones[i].rotation = q * lookBones[i].rotation;
        }
        float sag = stunned && !Heavy ? 1f : 0f;
        if (flinchAdd < 0.01f && sag <= 0f) return;
        float bend = (Heavy ? 3f : 11f) * flinchAdd;
        var reel = Quaternion.AngleAxis(bend, flinchAxis)
                 * Quaternion.AngleAxis(sag * 13f, transform.right) * Quaternion.AngleAxis(sag * 7f * Mathf.Sin(Time.time * 9f), transform.forward);
        foreach (var b in flinchBones) b.rotation = reel * b.rotation;
    }

    bool CanSee()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1.2f;
        return !Physics.Linecast(eye, target, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
               || hit.collider.GetComponentInParent<EnemyAI>() != null;
    }

    void FaceTarget(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 10f * Time.deltaTime);
    }

    // Fallback motion for placeholder models that have no animation clips.
    void ProceduralAnim()
    {
        if (!visual || emergeT < 1f) return;
        flinch = Mathf.Lerp(flinch, 0f, 10f * Time.deltaTime);
        float speed = agent.velocity.magnitude;
        float bob = Mathf.Abs(Mathf.Sin(Time.time * (4f + speed * 1.6f))) * 0.08f * Mathf.Clamp01(speed);
        float lunge = 0f;
        if (state == State.Attack)
        {
            float t = actionTimer / attackWindup;
            lunge = t < 1f ? -0.25f * t : Mathf.Lerp(0.7f, 0f, (actionTimer - attackWindup) / 0.3f);
        }
        visual.localPosition = new Vector3(0f, bob, Mathf.Max(lunge, -0.25f) - 0.15f * flinch);
        visual.localRotation = Quaternion.Euler(10f * Mathf.Clamp01(speed / chaseSpeed) + 25f * Mathf.Max(lunge, 0f) - 12f * flinch, 0f, 0f);
    }

    void DeathAnim()
    {
        if (hoverHeight > 0f && visual)
        {
            // Shot down: it tumbles out of the air and breaks up where it lands.
            if (visual.localPosition.y <= 0.35f) return;
            fallSpeed += 20f * Time.deltaTime;
            visual.localPosition += Vector3.down * fallSpeed * Time.deltaTime;
            visual.Rotate(260f * Time.deltaTime, 90f * Time.deltaTime, 340f * Time.deltaTime, Space.Self);
            if (visual.localPosition.y <= 0.35f && Abilities.Instance != null)
            {
                Abilities.Instance.Flash(visual.position, effectColor, 14f, 9f, 0.25f);
                Abilities.Instance.Sparks(visual.position, 26, effectColor, 3f, 9f, true);
            }
            return;
        }
        if (animator || !visual) return;
        deathT = Mathf.MoveTowards(deathT, 1f, Time.deltaTime * 2.2f);
        visual.localRotation = Quaternion.Euler(-88f * Mathf.SmoothStep(0, 1, deathT), 0f, 0f);
        visual.localPosition = new Vector3(0f, 0.25f * deathT, 0f);
    }
}
