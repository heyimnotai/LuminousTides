using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// A hitscan firearm, configured by its fields. The pulse carbine (automatic, single shot, reflex sight) and the
/// breaker shotgun (slow, many pellets, falls off quickly with range) are both instances of this.
/// Handles the magazine and reload, aiming, recoil and spread bloom, precision hits, and the spring-driven view model.
public class Weapon : MonoBehaviour
{
    public enum ShotSound { Pulse, Shotgun, HandCannon }

    public string weaponName = "PULSE CARBINE";
    public PlayerController player;
    public Camera cam;
    public Transform viewModel;      // moved procedurally
    public Transform muzzle;
    public Light muzzleLight;
    public LineRenderer tracerPrefab;
    public ParticleSystem impactFx;
    public ParticleSystem impactGlow;
    public Transform muzzleFlash;      // additive quad shown for a frame or two per shot
    public Camera viewModelCamera;     // fixed-FOV camera that draws the weapon
    public Transform supportHand;      // the off-hand arm of the view model, moved for reloads and pumping
    public bool pumpAction;            // the off hand racks the fore-grip after every shot

    [Header("Ballistics")]
    public int magazineSize = 28;
    public int reserveAmmo = 168;
    public int maxReserve = 224;
    public int pickupRounds = 28;         // rounds gained from one ammo pickup
    public float damage = 14f;            // per pellet
    public float precisionMultiplier = 2f;    // applied on top of the hit box's own multiplier, as a ratio to the usual 2
    public float fireRate = 9.5f;
    public float range = 150f;
    public float reloadTime = 1.6f;
    public int pellets = 1;
    public float pelletSpread = 0f;       // extra cone for each pellet, degrees
    public float falloffStart = 45f;      // damage starts dropping here...
    public float falloffEnd = 90f;        // ...and bottoms out here
    public float minFalloff = 0.6f;       // fraction of damage left at long range
    public ShotSound shotSound = ShotSound.Pulse;

    [Header("Accuracy")]
    public float hipSpread = 0.7f;        // degrees, first shot
    public float aimSpread = 0.05f;
    public float bloomPerShot = 0.32f;    // spread added by each shot
    public float maxBloom = 2.4f;
    public float bloomRecovery = 7f;      // degrees per second
    public float recoilPitch = 0.62f;     // degrees of climb per shot
    public float recoilYaw = 0.34f;       // maximum sideways drift per shot
    public float kickScale = 1f;          // how hard the view model and camera are thrown per shot

    [Header("View")]
    public float hipFov = 72f;
    public float aimFov = 55f;
    public float aimZoomRatio = 55f / 72f;    // aimFov as a fraction of hipFov when the player changes field of view
    public Vector3 hipPos = new Vector3(0.16f, -0.17f, 0.30f);
    public Vector3 aimPos = new Vector3(0f, -0.1338f, 0.10f);
    public float aimTime = 0.2f;          // seconds to raise or lower the sight
    public bool hasSightReticle = true;   // the sight draws its own reticle, so the HUD crosshair hides when aiming

    public int Magazine { get; private set; }
    public int Reserve { get; private set; }
    public bool Reloading { get; private set; }
    /// Raised for every target damaged by a shot: hit point, damage dealt, killed, precision hit.
    public event System.Action<Vector3, float, bool, bool> Hit;
    public float ReloadProgress { get; private set; }
    public float LastHitTime { get; private set; } = -99f;
    public bool LastHitKilled { get; private set; }
    public bool LastHitPrecision { get; private set; }
    /// Current cone half-angle in degrees, including bloom and movement. Drives the crosshair.
    public float CurrentSpread { get; private set; }
    public float Aim => aim;
    /// True while the weapon is dipped out of the way (ability, or being drawn); it cannot fire or aim.
    public bool Lowered => Time.time < loweredUntil;

    public void Lower(float seconds) { loweredUntil = Mathf.Max(loweredUntil, Time.time + seconds); }
    public void AddFovKick(float degrees) { fovKick += degrees; }
    /// Throws the view model: back toward the eye, muzzle up, and rolled. It springs back on its own.
    public void Punch(float back, float pitch, float roll) { kickZVel += back; kickPitchVel -= pitch; kickRollVel += roll; }

    /// Adds rounds to the reserve (ammo pickups), up to maxReserve. Returns false if it was already full.
    public bool AddAmmo(int rounds)
    {
        if (Reserve >= maxReserve) return false;
        Reserve = Mathf.Min(maxReserve, Reserve + rounds);
        return true;
    }

    float nextFire, lastShotTime = -99f, aim, bloom, reloadT, bobT, fovKick, sprintBlend, loweredUntil, lowerBlend;
    int burstCount;
    float lastHitSoundTime = -9f, airDrop, airTilt, strafeRoll, turnYaw, turnPitch;
    bool initialised;
    // view-model springs: value + velocity
    float kickZ, kickZVel, kickPitch, kickPitchVel, kickRoll, kickRollVel;
    Vector3 sway, supportRest;
    AudioSource audioSrc;
    AudioClip shotClip, magOutClip, magInClip, chargeClip, emptyClip, hitClip, critClip, killClip, popClip, thwackClip;
    AudioSource hitSrc;
    int hitStreak;

    struct PendingHit { public float damage; public Vector3 point, normal; public bool precision; }
    readonly Dictionary<Health, PendingHit> pending = new Dictionary<Health, PendingHit>();

    void Awake() { Initialise(); }

    void Initialise()
    {
        if (initialised) return;
        initialised = true;
        Magazine = magazineSize;
        Reserve = reserveAmmo;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.8f;
        shotClip = shotSound == ShotSound.Shotgun ? ProceduralAudio.ShotgunBlast() : shotSound == ShotSound.HandCannon ? ProceduralAudio.HeavyShot() : ProceduralAudio.PulseShot();
        magOutClip = ProceduralAudio.Mechanical(0.16f, 240f, 5);
        magInClip = ProceduralAudio.Mechanical(0.14f, 420f, 9);
        chargeClip = ProceduralAudio.Charge(0.22f);
        emptyClip = ProceduralAudio.Click(0.06f, 900f);
        hitClip = ProceduralAudio.Click(0.035f, 1900f);
        critClip = ProceduralAudio.Ping(0.16f, 2600f);
        killClip = ProceduralAudio.Thump(0.2f);
        popClip = ProceduralAudio.KillPop();
        thwackClip = ProceduralAudio.Thwack();
        hitSrc = gameObject.AddComponent<AudioSource>();      // its own source, so hit pitch never bends the gunshot
        hitSrc.spatialBlend = 0f; hitSrc.volume = 0.8f;
        if (player)
        {
            player.Jumped += OnJumped;
            player.Mantled += () => { if (isActiveAndEnabled) { Lower(0.32f); kickRollVel += 80f; } };
            player.Landed += OnLanded;
        }
        if (muzzleLight) muzzleLight.enabled = false;
        if (muzzleFlash) muzzleFlash.gameObject.SetActive(false);
        if (supportHand) supportRest = supportHand.localPosition;
    }

    void OnEnable()
    {
        // Being drawn: start dipped below the screen and rise into place.
        lowerBlend = 1f;
        loweredUntil = Time.time + 0.12f;
        if (viewModel) viewModel.localPosition = hipPos + new Vector3(0f, -0.25f, 0f);
    }

    void OnDisable()
    {
        // Put away: cancel anything in progress so it comes back in a clean state.
        StopAllCoroutines();
        Reloading = false;
        reloadT = 0f; ReloadProgress = 0f; aim = 0f; bloom = 0f;
        if (player) player.aimFactor = 1f;
        if (muzzleLight) muzzleLight.enabled = false;
        if (muzzleFlash) muzzleFlash.gameObject.SetActive(false);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool control = (gm == null || gm.State == GameManager.GameState.Playing) && Cursor.lockState == CursorLockMode.Locked && !InputGate.Blocked;
        bool fireHeld = control && Input.GetMouseButton(0);
        bool aimHeld = control && Input.GetMouseButton(1);

        // Shooting or aiming always wins over sprinting, so the trigger never feels dead.
        player.suppressSprint = fireHeld || aimHeld || Time.time - lastShotTime < 0.25f;

        bool wantAim = (aimHeld || debugHoldAim) && !Reloading && !Lowered;
        aim = Mathf.MoveTowards(aim, wantAim ? 1f : 0f, Time.deltaTime / aimTime);
        if (debugAimOverride >= 0f) aim = debugAimOverride;
        player.aimFactor = 1f - aim;
        fovKick = Mathf.Lerp(fovKick, 0f, 16f * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(hipFov, aimFov, Mathf.SmoothStep(0, 1, aim)) + (player.IsSprinting ? 4f : 0f) + fovKick;

        // Bloom settles shortly after the trigger is released.
        if (Time.time - lastShotTime > 0.11f) bloom = Mathf.MoveTowards(bloom, 0f, bloomRecovery * Time.deltaTime);
        if (Time.time - lastShotTime > 0.3f) burstCount = 0;
        float move = Mathf.Clamp01(player.PlanarSpeed / 6f) * 0.9f + (player.IsGrounded ? 0f : 1.2f);
        CurrentSpread = Mathf.Lerp(hipSpread + move, aimSpread + move * 0.15f, aim) + bloom * Mathf.Lerp(1f, 0.3f, aim);

        if (control && !Reloading && !Lowered)
        {
            if (Input.GetKeyDown(KeyCode.R) && Magazine < magazineSize && Reserve > 0) StartCoroutine(Reload());
            else if (fireHeld && Time.time >= nextFire)
            {
                if (Magazine > 0) Fire();
                else if (Reserve > 0) StartCoroutine(Reload());
                else if (Input.GetMouseButtonDown(0)) audioSrc.PlayOneShot(emptyClip);
            }
        }
        AnimateViewModel();
    }

    /// Used by the scripted smoke test: fires (and reloads) without mouse input.
    public void DebugFire()
    {
        if (!isActiveAndEnabled || Reloading || Lowered || Time.time < nextFire) return;
        if (Magazine > 0) Fire();
        else if (Reserve > 0) StartCoroutine(Reload());
    }

    /// Used by the scripted smoke test to hold aim-down-sights.
    [System.NonSerialized] public bool debugHoldAim;
    /// Smoke test only: freezes the aim blend at this fraction (negative = off).
    [System.NonSerialized] public float debugAimOverride = -1f;

    void Fire()
    {
        nextFire = Time.time + 1f / fireRate;
        lastShotTime = Time.time;
        Magazine--;
        burstCount++;

        // The shot leaves before recoil is applied, so the first round always goes where the sight points.
        float spread = CurrentSpread;
        Vector3 muzzleWorld = MuzzleWorldPosition();
        pending.Clear();
        int glows = 0;
        bool multi = pellets > 1;
        for (int p = 0; p < pellets; p++)
        {
            Vector2 r = Random.insideUnitCircle * spread + Random.insideUnitCircle * pelletSpread;
            Vector3 dir = cam.transform.rotation * Quaternion.Euler(r.y, r.x, 0f) * Vector3.forward;
            Vector3 end = cam.transform.position + dir * range;

            // Player is on the Ignore Raycast layer, so the default mask skips them.
            if (Physics.Raycast(cam.transform.position, dir, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var h = hit.collider.GetComponentInParent<Health>();
                bool flesh = h != null && !h.IsDead;
                if (flesh)
                {
                    var box = hit.collider.GetComponent<Hitbox>();
                    bool precision = box != null && box.multiplier > 1f;
                    float falloff = Mathf.Lerp(1f, minFalloff, Mathf.InverseLerp(falloffStart, falloffEnd, hit.distance));
                    float dealt = damage * (precision ? box.multiplier * precisionMultiplier / 2f : box != null ? box.multiplier : 1f) * falloff;
                    pending.TryGetValue(h, out PendingHit ph);
                    ph.damage += dealt;
                    ph.precision |= precision;
                    ph.normal = hit.normal;
                    ph.point = ph.point == Vector3.zero ? hit.point : Vector3.Lerp(ph.point, hit.point, 0.5f);
                    pending[h] = ph;
                }
                if (impactFx)
                {
                    // Cyan plasma sparks on scenery, hot orange spray on creatures.
                    impactFx.transform.SetPositionAndRotation(hit.point + hit.normal * 0.03f, Quaternion.LookRotation(hit.normal));
                    var sparks = new ParticleSystem.EmitParams();
                    sparks.startColor = flesh ? new Color(1f, 0.55f, 0.15f) : new Color(0.5f, 1f, 0.95f);
                    sparks.startSize = Mathf.Clamp(hit.distance * 0.02f, 0.05f, 0.16f);   // small up close, readable at range
                    impactFx.Emit(sparks, multi ? 4 : flesh ? 14 : 8);
                    if (flesh)
                    {
                        // A spray thrown back out of the wound toward the shooter, in the creature's own colour.
                        var enemy = h.GetComponent<EnemyAI>();
                        Color blood = enemy != null ? enemy.effectColor : new Color(1f, 0.55f, 0.15f);
                        bool crit = hit.collider.GetComponent<Hitbox>() is Hitbox hb && hb.multiplier > 1f;
                        Feel.Instance.Jet(hit.point, hit.normal - dir * 0.6f + Vector3.up * 0.3f, multi ? 2 : crit ? 12 : 6,
                                          crit ? Color.Lerp(blood, new Color(1f, 0.95f, 0.5f), 0.6f) : blood, 2.5f, crit ? 10f : 7f, crit ? 34f : 24f,
                                          Mathf.Clamp(hit.distance * 0.025f, 0.07f, 0.2f) * (crit ? 1.3f : 1f), 0.4f);
                    }
                }
                if (impactGlow && glows++ < 3)
                {
                    var glow = new ParticleSystem.EmitParams();
                    glow.position = hit.point + hit.normal * 0.06f;
                    glow.startColor = flesh ? new Color(1f, 0.6f, 0.2f, 0.55f) : new Color(0.5f, 1f, 0.95f, 0.7f);
                    // Scale with distance so a hit reads the same at range and does not flood the screen up close.
                    glow.startSize = Mathf.Clamp(hit.distance * 0.07f, 0.16f, 0.6f);
                    impactGlow.Emit(glow, 1);
                }
            }
            if (tracerPrefab) StartCoroutine(Tracer(muzzleWorld, end, multi ? 0.5f : 1f));
        }

        // Apply the damage once per target, so a shotgun blast shows one number and one hit marker.
        bool anyKill = false, anyPrecision = false;
        foreach (var pair in pending)
        {
            var h = pair.Key;
            if (h == null || h.IsDead) continue;
            h.Damage(pair.Value.damage, player.transform.position);
            bool killed = h.IsDead;
            anyKill |= killed;
            anyPrecision |= pair.Value.precision;
            LastHitTime = Time.time;
            LastHitKilled = killed;
            LastHitPrecision = pair.Value.precision;
            Hit?.Invoke(pair.Value.point, pair.Value.damage, killed, pair.Value.precision);
            if (killed && impactFx)
            {
                var burst = new ParticleSystem.EmitParams { position = pair.Value.point, startColor = new Color(1f, 0.55f, 0.15f), startSize = 0.12f };
                for (int i = 0; i < 14; i++) { burst.velocity = Random.onUnitSphere * Random.Range(2f, 7f); impactFx.Emit(burst, 1); }
                if (pair.Value.precision && Abilities.Instance != null)
                {
                    // A precision kill bursts where the shot landed.
                    Abilities.Instance.Flash(pair.Value.point, new Color(1f, 0.9f, 0.5f), 14f, 7f, 0.18f);
                    Abilities.Instance.Sparks(pair.Value.point, 22, new Color(1f, 0.9f, 0.5f), 4f, 11f, false);
                }
            }
        }
        if (pending.Count > 0)
        {
            // Layered: a smack for the round landing, a tick (or a ping for a precision hit) on top, and a pop for a kill.
            // Landing shot after shot raises the pitch a little each time, so a sustained burst on target sings.
            hitStreak = Time.time - lastHitSoundTime < 0.35f ? Mathf.Min(hitStreak + 1, 12) : 0;
            lastHitSoundTime = Time.time;
            hitSrc.pitch = 1f + hitStreak * 0.022f;
            hitSrc.PlayOneShot(thwackClip, 0.55f);
            hitSrc.PlayOneShot(anyPrecision ? critClip : hitClip, anyPrecision ? 0.75f : 0.5f);
            if (anyKill)
            {
                hitSrc.PlayOneShot(popClip, 1f);
                hitSrc.PlayOneShot(killClip, 0.8f);
                fovKick += 1.4f;
                player.AddShake(0.5f);
            }
        }

        // Recoil: steady climb with a sideways drift that wanders, both lighter when aiming.
        float aimScale = Mathf.Lerp(1f, 0.55f, aim);
        float climb = recoilPitch * (burstCount <= 2 ? 1.35f : 1f) * aimScale;
        float drift = Mathf.Sin(burstCount * 0.9f + 0.6f) * recoilYaw * aimScale + Random.Range(-0.12f, 0.12f) * aimScale;
        player.AddRecoil(climb, drift);
        player.AddShake(Mathf.Lerp(0.5f, 0.22f, aim) * kickScale);
        bloom = Mathf.Min(maxBloom, bloom + bloomPerShot);
        fovKick += Mathf.Lerp(0.9f, 0.45f, aim) * kickScale;

        // Kick the view model's springs.
        kickZVel -= Mathf.Lerp(2.6f, 0.55f, aim) * kickScale;      // barely any push toward the eye when aiming
        kickPitchVel -= Mathf.Lerp(190f, 28f, aim) * kickScale;
        kickRollVel += Random.Range(-90f, 90f) * Mathf.Lerp(1f, 0.15f, aim);

        audioSrc.pitch = shotSound == ShotSound.HandCannon ? Random.Range(0.97f, 1.02f) : Random.Range(0.96f, 1.05f);
        audioSrc.PlayOneShot(shotClip, shotSound == ShotSound.HandCannon ? 1f : 0.85f);
        StartCoroutine(Flash());
    }

    void OnJumped(bool second)
    {
        if (!isActiveAndEnabled) return;
        kickPitchVel += second ? 150f : 90f;         // the muzzle dips as the body goes up
        fovKick += second ? 4f : 1.6f;
    }

    void OnLanded(float impact)
    {
        if (!isActiveAndEnabled) return;
        kickPitchVel += 260f * impact;               // and slams down on landing, then springs back
        kickZVel -= 1.2f * impact;
        kickRollVel += Random.Range(-70f, 70f) * impact;
        fovKick -= 2.5f * impact;
    }

    /// The weapon is drawn with its own field of view, so the muzzle's true position does not line up with
    /// where it appears. This returns the world point that sits at the muzzle's on-screen position.
    Vector3 MuzzleWorldPosition()
    {
        if (!viewModelCamera) return muzzle.position;
        return cam.ViewportToWorldPoint(viewModelCamera.WorldToViewportPoint(muzzle.position));
    }

    IEnumerator Flash()
    {
        if (muzzleFlash)
        {
            muzzleFlash.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            muzzleFlash.localScale = Vector3.one * Random.Range(0.75f, 1.25f) * Mathf.Lerp(1f, 0.3f, aim) * Mathf.Sqrt(kickScale);
            muzzleFlash.gameObject.SetActive(true);
        }
        if (muzzleLight) { muzzleLight.intensity = Mathf.Lerp(5f, 2f, aim) * kickScale; muzzleLight.enabled = true; }
        yield return new WaitForSeconds(0.04f);
        if (muzzleLight) muzzleLight.enabled = false;
        if (muzzleFlash) muzzleFlash.gameObject.SetActive(false);
    }

    IEnumerator Tracer(Vector3 a, Vector3 b, float widthScale)
    {
        var lr = Instantiate(tracerPrefab);
        lr.gameObject.SetActive(true);
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        float t = 0f, life = 0.08f;
        float w = lr.widthMultiplier * widthScale;
        while (t < life)
        {
            t += Time.deltaTime;
            lr.widthMultiplier = w * (1f - t / life);
            lr.SetPosition(0, Vector3.Lerp(a, b, t / life * 0.6f));
            yield return null;
        }
        Destroy(lr.gameObject);
    }

    IEnumerator Reload()
    {
        Reloading = true;
        bloom = 0f;
        audioSrc.pitch = 1f;
        audioSrc.PlayOneShot(magOutClip);
        float t = 0f;
        bool magIn = false;
        while (t < reloadTime)
        {
            t += Time.deltaTime;
            ReloadProgress = Mathf.Clamp01(t / reloadTime);
            reloadT = Mathf.Sin(ReloadProgress * Mathf.PI);
            if (!magIn && ReloadProgress > 0.62f)
            {
                magIn = true;
                audioSrc.PlayOneShot(magInClip);
                kickZVel -= 0.8f; kickPitchVel += 120f;     // the magazine slapping home
            }
            yield return null;
        }
        reloadT = 0f;
        int take = Mathf.Min(magazineSize - Magazine, Reserve);
        Magazine += take;
        Reserve -= take;
        audioSrc.PlayOneShot(chargeClip, 0.7f);
        Reloading = false;
    }

    static void Spring(ref float x, ref float v, float stiffness, float damping, float dt)
    {
        v += -x * stiffness * dt;
        v *= Mathf.Exp(-damping * dt);
        x += v * dt;
    }

    void AnimateViewModel()
    {
        if (!viewModel) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        Spring(ref kickZ, ref kickZVel, 260f, 17f, dt);
        Spring(ref kickPitch, ref kickPitchVel, 240f, 15f, dt);
        Spring(ref kickRoll, ref kickRollVel, 200f, 14f, dt);

        Vector3 swayTarget = Vector3.zero;
        if (Cursor.lockState == CursorLockMode.Locked && !InputGate.Blocked)
            swayTarget = new Vector3(-Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y"), 0f) * 0.004f * Mathf.Lerp(1f, 0.2f, aim);
        sway = Vector3.Lerp(sway, Vector3.ClampMagnitude(swayTarget, 0.03f), 8f * Time.deltaTime);

        float moving = player.IsGrounded ? Mathf.Clamp01(player.PlanarSpeed / 5f) : 0f;
        bobT += Time.deltaTime * (player.IsSprinting ? 13f : 9f) * moving;
        float bobAmp = Mathf.Lerp(0.012f, 0.0012f, aim) * moving * (player.IsSprinting ? 2f : 1f);
        Vector3 bob = new Vector3(Mathf.Cos(bobT * 0.5f), Mathf.Abs(Mathf.Sin(bobT * 0.5f)) * -1f, 0f) * bobAmp;

        Vector3 basePos = Vector3.Lerp(hipPos, aimPos, Mathf.SmoothStep(0, 1, aim));
        lowerBlend = Mathf.MoveTowards(lowerBlend, Lowered ? 1f : 0f, Time.deltaTime * 9f);
        float lower = Mathf.SmoothStep(0f, 1f, lowerBlend);
        // In the air the weapon trails the body: it hangs low on the way up and floats on the way down.
        float vy = player.IsGrounded ? 0f : player.Velocity.y;
        float hip = Mathf.Lerp(1f, 0.2f, aim);
        airDrop = Mathf.Lerp(airDrop, Mathf.Clamp(-vy * 0.0045f, -0.04f, 0.045f) * hip, 9f * Time.deltaTime);
        airTilt = Mathf.Lerp(airTilt, Mathf.Clamp(vy * 0.9f, -7f, 9f) * hip, 9f * Time.deltaTime);
        float sideways = Vector3.Dot(player.Velocity, player.transform.right);
        strafeRoll = Mathf.Lerp(strafeRoll, Mathf.Clamp(-sideways * 0.7f, -5f, 5f) * hip, 8f * Time.deltaTime);
        // Turning: the weapon has weight, so it trails the view a few degrees and rolls into the turn, then catches up.
        float mx = 0f, my = 0f;
        if (Cursor.lockState == CursorLockMode.Locked && !InputGate.Blocked) { mx = Input.GetAxisRaw("Mouse X"); my = Input.GetAxisRaw("Mouse Y"); }
        float turnScale = Mathf.Lerp(1f, 0.25f, aim);
        turnYaw = Mathf.Lerp(turnYaw, Mathf.Clamp(-mx * 1.1f, -5f, 5f) * turnScale, 7f * Time.deltaTime);
        turnPitch = Mathf.Lerp(turnPitch, Mathf.Clamp(my * 1.1f, -4f, 4f) * turnScale, 7f * Time.deltaTime);
        // Reloading: the weapon rolls onto its side to show the magazine well, then is righted with a snap.
        float reloadRoll = Reloading ? Mathf.Sin(Mathf.Clamp01(ReloadProgress / 0.85f) * Mathf.PI) * 16f - (ReloadProgress > 0.85f ? Mathf.Sin((ReloadProgress - 0.85f) / 0.15f * Mathf.PI) * 9f : 0f) : 0f;
        Vector3 pos = basePos + sway + bob + new Vector3(0f, -0.18f * reloadT - 0.2f * lower + airDrop, kickZ);
        sprintBlend = Mathf.MoveTowards(sprintBlend, player.IsSprinting ? 1f : 0f, Time.deltaTime * 6f);
        float sp = Mathf.SmoothStep(0f, 1f, sprintBlend);
        Quaternion rot = Quaternion.Euler(kickPitch + 35f * reloadT + 30f * lower + airTilt + turnPitch, -25f * sp + turnYaw,
                                          kickRoll + 20f * reloadT + 10f * sp - 12f * lower + strafeRoll + turnYaw * 0.9f + reloadRoll);

        // Snap quickly so the sight stays where the springs put it; the springs supply the motion.
        viewModel.localPosition = Vector3.Lerp(viewModel.localPosition, pos, 1f - Mathf.Exp(-45f * Time.deltaTime));
        viewModel.localRotation = Quaternion.Slerp(viewModel.localRotation, rot, 1f - Mathf.Exp(-30f * Time.deltaTime));

        // The off hand: drops away to fetch a fresh magazine and comes back to seat it; on a pump gun it
        // racks the fore-grip back and forward after each shot. Offsets are in view-model space.
        if (supportHand)
        {
            Vector3 offset = Vector3.zero;
            if (Reloading)
            {
                float p = ReloadProgress;
                float away = p < 0.28f ? Mathf.SmoothStep(0f, 1f, p / 0.28f) : p < 0.54f ? 1f : Mathf.SmoothStep(1f, 0f, (p - 0.54f) / 0.1f);
                offset += new Vector3(-0.06f, -0.3f, -0.18f) * away;
            }
            if (pumpAction)
            {
                float t = Time.time - lastShotTime;
                float rack = t < 0.12f ? 0f : t < 0.26f ? Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.14f)
                           : t < 0.44f ? Mathf.SmoothStep(1f, 0f, (t - 0.26f) / 0.18f) : 0f;
                offset += new Vector3(0f, 0f, -0.075f) * rack;
            }
            Vector3 local = supportHand.parent.InverseTransformVector(viewModel.TransformVector(offset));
            supportHand.localPosition = Vector3.Lerp(supportHand.localPosition, supportRest + local, 1f - Mathf.Exp(-30f * Time.deltaTime));
        }
    }
}
