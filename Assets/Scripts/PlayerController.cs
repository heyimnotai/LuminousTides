using UnityEngine;

/// First-person movement: walk, sprint, crouch, jump and double jump, slide, and mouse look,
/// with camera feedback (landing dip, slide lean, shake) and footstep, jump and landing sounds.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Transform cam;
    public float walkSpeed = 5.6f;
    public float sprintSpeed = 9.2f;
    public float crouchSpeed = 2.8f;
    public float jumpHeight = 1.5f;
    public float doubleJumpHeight = 1.6f;
    public float gravity = -24f;
    public float mouseSensitivity = 2.2f;
    public float standHeight = 1.9f;
    public float crouchHeight = 1.2f;
    public float slideSpeed = 14f;        // speed at the start of a slide
    public float slideTime = 0.85f;

    public bool IsSprinting { get; private set; }
    public bool IsSliding => slideLeft > 0f;
    public bool IsGrounded => cc.isGrounded;
    public float PlanarSpeed { get; private set; }
    public float Yaw => transform.eulerAngles.y;
    public Vector3 Velocity => cc.velocity;
    /// Set by the weapon: 1 = hip, 0 = fully aimed. Slows look and movement.
    [HideInInspector] public float aimFactor = 1f;
    /// Set by the weapon while firing or aiming: those always cancel a sprint.
    [HideInInspector] public bool suppressSprint;

    CharacterController cc;
    AudioSource audioSrc;
    AudioClip stepClip, landClip, jumpClip, slideClip, boostClip;
    float nextSlideFx, dashCarry = 0.35f;
    Vector3 velocity, dashVelocity, slideDir;
    float pitch, recoilPitch, recoilYaw, verticalVel, camHeight, shake, dashLeft;
    float slideLeft, slideCooldownUntil, lastGroundedTime = -9f, jumpBufferedUntil = -9f, stepDistance;
    float landDip, landDipVel, lean, lastFallSpeed;
    int jumpsUsed;
    bool wasGrounded = true, wasSprinting;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        camHeight = standHeight - 0.15f;
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.55f;
        stepClip = ProceduralAudio.Footstep();
        landClip = ProceduralAudio.Thump(0.16f);
        jumpClip = ProceduralAudio.Whoosh(0.18f);
        slideClip = ProceduralAudio.Whoosh(0.6f);
        boostClip = ProceduralAudio.Shockwave(0.3f);
    }

    public void AddRecoil(float pitchDegrees, float yawDegrees = 0f) { recoilPitch += pitchDegrees; recoilYaw += yawDegrees; }

    /// Overrides movement for a moment: a flat burst in the given direction (used by the dash ability).
    public void Dash(Vector3 direction, float speed, float seconds, float carry = 0.35f)
    {
        dashCarry = carry;
        direction.y = 0f;
        dashVelocity = direction.normalized * speed;
        dashLeft = seconds;
        slideLeft = 0f;
    }

    public void AddShake(float degrees) { shake = Mathf.Min(4f, shake + degrees); }
    /// Drives the camera down as if something heavy just landed (the super's slam).
    public void Slam(float strength) { landDipVel -= strength; }
    /// Swings the view part of the way (0..1) toward a world point. Called every frame while a melee strike homes in.
    public void TurnToward(Vector3 point, float fraction)
    {
        Vector3 to = point - cam.position;
        float flat = new Vector2(to.x, to.z).magnitude;
        if (flat < 0.05f) return;
        float yaw = Mathf.LerpAngle(transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, fraction);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = Mathf.Clamp(Mathf.Lerp(pitch, -Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg, fraction), -85f, 85f);
    }
    /// Rolls the camera for a moment (a dash to the side).
    public void Lean(float degrees) { lean += degrees; }
    /// Raised when the player leaves the ground: false for a jump, true for the second jump in the air.
    public event System.Action<bool> Jumped;
    /// Raised when the player catches a ledge and is pulled up onto it.
    public event System.Action Mantled;
    public bool IsMantling => Time.time < mantleUntil;
    float mantleUntil = -9f, nextMantleTime, landedTime = -9f;
    Vector3 mantleDir;

    /// Looks for a ledge in front of the player that they could be pulled up onto: a wall at chest height with a
    /// level top between knee and well overhead, and room to stand on it. Returns how far up its top is.
    bool FindLedge(Vector3 forward, out float rise)
    {
        rise = 0f;
        Vector3 feet = transform.position;
        const int mask = Physics.DefaultRaycastLayers;
        bool wall = false;
        foreach (float h in new[] { 0.45f, 1.0f, 1.55f })
            if (Physics.Raycast(feet + Vector3.up * h, forward, out RaycastHit w, 0.85f, mask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(w.normal.y) < 0.45f && w.collider.GetComponentInParent<EnemyAI>() == null) { wall = true; break; }
        if (!wall) return false;
        Vector3 above = feet + forward * 0.8f + Vector3.up * 2.9f;
        if (!Physics.Raycast(above, Vector3.down, out RaycastHit top, 2.9f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (top.normal.y < 0.7f || top.collider.GetComponentInParent<EnemyAI>() != null) return false;
        rise = top.point.y - feet.y;
        if (rise < 0.55f || rise > 2.5f) return false;
        // Room to stand there, and nothing overhead on the way up.
        Vector3 stand = top.point + Vector3.up * 0.1f;
        if (Physics.CheckCapsule(stand + Vector3.up * 0.35f, stand + Vector3.up * 1.5f, 0.3f, mask, QueryTriggerInteraction.Ignore)) return false;
        return !Physics.Raycast(feet + Vector3.up * 1.7f, Vector3.up, rise + 0.3f, mask, QueryTriggerInteraction.Ignore);
    }

    /// Raised on touching down, with how hard: 0 a step off a kerb, 1 a long fall.
    public event System.Action<float> Landed;
    static readonly Color Pale = new Color(0.75f, 1f, 0.97f), Thrust = new Color(0.45f, 1f, 0.95f);
    public void DebugSetPitch(float degrees) { pitch = Mathf.Clamp(degrees, -85f, 85f); }

    /// Smoke test hooks: perform a jump (or double jump) and a slide without keyboard input.
    public void DebugJump() { jumpBufferedUntil = Time.time + 0.2f; }
    public void DebugSlide() { BeginSlide(transform.forward); }
    /// Set while something else owns the player: `frozen` stops all input, `externalCamera` leaves the camera alone
    /// (the super's third-person view).
    [System.NonSerialized] public bool frozen, externalCamera;
    /// Smoke test only: holds the forward key down.
    [System.NonSerialized] public float debugForward;

    public void Teleport(Vector3 pos, float yaw)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        cc.enabled = true;
        pitch = 0; verticalVel = 0; velocity = Vector3.zero; slideLeft = 0f; dashLeft = 0f;
    }

    void BeginSlide(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        slideDir = direction.normalized;
        slideLeft = slideTime;
        slideCooldownUntil = Time.time + slideTime + 0.35f;
        Feel.Instance.Streaks(0.4f, 0.6f);
        if (Abilities.Instance != null) Abilities.Instance.Ring(transform.position + Vector3.up * 0.08f, 0.3f, 1.8f, 0.25f, Pale * 0.9f);
        audioSrc.pitch = 0.8f;
        audioSrc.PlayOneShot(slideClip, 0.7f);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        bool control = playing && !InputGate.Blocked && !frozen;
        float dt = Time.deltaTime;

        // ---- look
        if (control && Cursor.lockState == CursorLockMode.Locked)
        {
            float s = mouseSensitivity * Mathf.Lerp(0.6f, 1f, aimFactor);
            transform.Rotate(0f, Input.GetAxisRaw("Mouse X") * s, 0f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * s, -85f, 85f);
        }
        recoilPitch = Mathf.Lerp(recoilPitch, 0f, 6f * dt);
        recoilYaw = Mathf.Lerp(recoilYaw, 0f, 6f * dt);
        shake = Mathf.Lerp(shake, 0f, 14f * dt);

        // ---- input
        Vector3 input = Vector3.zero;
        if (control) input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")), 1f);
        if (debugForward != 0f && playing) input = new Vector3(0f, 0f, debugForward);
        bool crouchHeld = control && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C));
        bool crouchPressed = control && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C));
        if (control && Input.GetButtonDown("Jump")) jumpBufferedUntil = Time.time + 0.12f;
        bool grounded = cc.isGrounded;
        if (grounded) lastGroundedTime = Time.time;

        // ---- slide: crouch while sprinting. It carries more speed than a sprint and bleeds off as it goes.
        if (crouchPressed && grounded && wasSprinting && Time.time >= slideCooldownUntil)
            BeginSlide(new Vector3(cc.velocity.x, 0f, cc.velocity.z));
        bool sliding = slideLeft > 0f;
        if (sliding)
        {
            slideLeft -= dt;
            if (!grounded && Time.time - lastGroundedTime > 0.25f) slideLeft = 0f;          // slid off a ledge
            // Sparks and spray thrown up from under the boots for as long as the slide lasts.
            if (grounded && Time.time >= nextSlideFx)
            {
                nextSlideFx = Time.time + 0.035f;
                Vector3 side = Vector3.Cross(Vector3.up, slideDir) * Random.Range(-0.3f, 0.3f);
                Feel.Instance.Jet(transform.position + Vector3.up * 0.06f + slideDir * 0.5f + side, -slideDir + Vector3.up * 0.55f, 2,
                                  transform.position.y < 0.06f ? Pale : new Color(1f, 0.8f, 0.45f), 2f, 6f, 24f, 0.07f, 0.3f);
            }
        }
        bool crouch = crouchHeld || sliding;

        float targetHeight = crouch ? crouchHeight : standHeight;
        cc.height = Mathf.MoveTowards(cc.height, targetHeight, (sliding ? 9f : 5f) * dt);
        cc.center = new Vector3(0f, cc.height * 0.5f, 0f);

        IsSprinting = control && !crouch && !suppressSprint && aimFactor > 0.9f && Input.GetKey(KeyCode.LeftShift) && input.z > 0.1f;
        wasSprinting = IsSprinting;
        float speed = crouch ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed * Mathf.Lerp(0.6f, 1f, aimFactor);

        // ---- horizontal velocity
        Vector3 wish = transform.TransformDirection(input) * speed;
        if (grounded)
        {
            // A beat of grace on touching down: speed carried in from a slide-jump is not scrubbed off instantly,
            // so chaining a jump or dropping into a slide keeps it.
            bool carrying = Time.time - landedTime < 0.14f && velocity.sqrMagnitude > wish.sqrMagnitude;
            velocity = Vector3.Lerp(velocity, wish, (carrying ? 2.5f : 14f) * dt);
        }
        else
        {
            // In the air you steer, but you keep the speed you left the ground with; it only bleeds away slowly.
            float had = velocity.magnitude, want = wish.magnitude;
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 steered = Vector3.Lerp(velocity, wish, 4.5f * dt);
                float keep = Mathf.Max(want, had - 1.2f * dt);
                velocity = had > want && steered.sqrMagnitude > 0.01f ? steered.normalized * keep : steered;
            }
            else velocity = Vector3.Lerp(velocity, Vector3.zero, 0.7f * dt);
        }
        if (sliding)
        {
            float k = Mathf.Clamp01(slideLeft / slideTime);
            slideDir = Vector3.Slerp(slideDir, (slideDir + transform.right * input.x * 0.6f).normalized, 3f * dt);   // a little steering
            velocity = slideDir * Mathf.Lerp(walkSpeed, slideSpeed, Mathf.Pow(k, 0.7f));
        }
        bool dashing = dashLeft > 0f;
        if (dashing)
        {
            dashLeft -= dt;
            velocity = dashLeft > 0f ? dashVelocity : dashVelocity * dashCarry;   // keep a little momentum coming out of it
        }

        // ---- jumping: a buffered press, a moment of grace after walking off an edge, and one extra jump in the air
        bool canGroundJump = grounded || Time.time - lastGroundedTime < 0.12f;
        if (grounded && verticalVel < 0f) verticalVel = -2f;
        if (Time.time <= jumpBufferedUntil && playing)
        {
            if (canGroundJump && jumpsUsed == 0)
            {
                verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumpsUsed = 1;
                jumpBufferedUntil = -9f;
                lastGroundedTime = -9f;
                if (slideLeft > 0f) velocity = Vector3.ClampMagnitude(velocity * 1.1f, 16.5f);     // a slide-jump throws you a little faster still
                slideLeft = 0f;                       // jumping out of a slide keeps its speed
                audioSrc.pitch = 1.1f;
                audioSrc.PlayOneShot(jumpClip, 0.5f);
                // Kicking off: a ring of dust at the feet and the camera pops up with the body.
                landDipVel += 0.7f;
                if (Abilities.Instance != null)
                {
                    Vector3 feet = transform.position + Vector3.up * 0.08f;
                    Abilities.Instance.Ring(feet, 0.25f, 1.5f, 0.22f, Pale * 0.9f);
                    Abilities.Instance.Sparks(feet, 12, Pale, 2f, 5f, true);
                }
                Jumped?.Invoke(false);
            }
            else if (!canGroundJump && jumpsUsed < 2)
            {
                verticalVel = Mathf.Sqrt(doubleJumpHeight * -2f * gravity);
                velocity += transform.TransformDirection(input) * 2.5f;
                jumpsUsed = 2;
                jumpBufferedUntil = -9f;
                audioSrc.pitch = 1.5f;
                audioSrc.PlayOneShot(jumpClip, 0.9f);
                audioSrc.pitch = 1.5f;
                audioSrc.PlayOneShot(boostClip, 0.55f);
                // The suit's thrusters fire: a jet of light straight down, two rings, and a shove on the camera.
                landDipVel += 1.5f;
                AddShake(0.7f);
                Feel.Instance.Streaks(0.2f, 0.5f);
                if (Abilities.Instance != null)
                {
                    Vector3 feet = transform.position + Vector3.up * 0.1f;
                    Abilities.Instance.Ring(feet, 0.2f, 2.0f, 0.28f, Thrust * 2f);
                    Abilities.Instance.Ring(feet + Vector3.down * 0.5f, 0.15f, 1.2f, 0.36f, Thrust * 1.2f);
                    Abilities.Instance.Flash(feet, Thrust, 10f, 8f, 0.22f);
                    Feel.Instance.Jet(feet, Vector3.down - new Vector3(velocity.x, 0f, velocity.z) * 0.05f, 34, Thrust, 7f, 17f, 16f, 0.13f, 0.4f);
                }
                Jumped?.Invoke(true);
            }
        }
        // ---- mantling: in the air, moving into a ledge you can nearly reach, you are carried up onto it.
        if (!grounded && playing && input.z > 0.2f && verticalVel < 5f && Time.time >= nextMantleTime && dashLeft <= 0f)
        {
            Vector3 fwd = transform.forward;
            if (FindLedge(fwd, out float rise))
            {
                verticalVel = Mathf.Sqrt(2f * -gravity * (rise + 0.35f));
                mantleDir = fwd;
                mantleUntil = Time.time + Mathf.Lerp(0.3f, 0.55f, Mathf.InverseLerp(0.55f, 2.5f, rise));
                nextMantleTime = Time.time + 0.6f;
                landDipVel -= 1.1f;
                audioSrc.pitch = 0.85f;
                audioSrc.PlayOneShot(jumpClip, 0.7f);
                audioSrc.PlayOneShot(stepClip, 0.6f);
                Mantled?.Invoke();
            }
        }
        // While being carried up, keep pressing over the lip so the body arrives on top rather than sliding back.
        if (IsMantling && !grounded)
        {
            float along = Vector3.Dot(velocity, mantleDir);
            if (along < 4.2f) velocity += mantleDir * (4.2f - along);
        }
        if (!grounded) lastFallSpeed = Mathf.Max(0f, -verticalVel);
        verticalVel += gravity * dt;
        if (dashing) verticalVel = Mathf.Min(verticalVel, 0f) * 0.2f;        // the dash travels flat, even in the air

        cc.Move((velocity + Vector3.up * verticalVel) * dt);
        PlanarSpeed = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;

        // ---- landing
        if (cc.isGrounded && !wasGrounded)
        {
            jumpsUsed = 0;
            landedTime = Time.time;
            mantleUntil = -9f;
            // Landing with crouch held and speed in hand drops straight into a slide.
            if (crouchHeld && Time.time >= slideCooldownUntil && new Vector2(velocity.x, velocity.z).magnitude > 6.5f)
                BeginSlide(new Vector3(velocity.x, 0f, velocity.z));
            float impact = Mathf.Clamp01(lastFallSpeed / 16f);
            landDipVel -= 2.3f * impact;
            if (impact > 0.2f)
            {
                audioSrc.pitch = Random.Range(0.9f, 1.1f) * Mathf.Lerp(1.15f, 0.8f, impact);
                audioSrc.PlayOneShot(landClip, Mathf.Lerp(0.35f, 1f, impact));
                AddShake(1.3f * impact);
                // Dust (or spray, in the shallows) thrown out from under the boots, more of it the harder the landing.
                if (Abilities.Instance != null)
                {
                    Vector3 feet = transform.position + Vector3.up * 0.08f;
                    Abilities.Instance.Ring(feet, 0.3f, 1.2f + 2.6f * impact, 0.3f, Pale * (0.6f + impact));
                    Abilities.Instance.Sparks(feet, (int)(8 + 30 * impact), Pale, 2f, 4f + 8f * impact, true);
                    if (impact > 0.7f) Abilities.Instance.Ring(feet, 0.2f, 4.5f, 0.45f, Pale * 0.7f);
                }
                Landed?.Invoke(impact);
            }
            lastFallSpeed = 0f;
        }
        if (cc.isGrounded && jumpsUsed > 0 && verticalVel <= 0f) jumpsUsed = 0;
        wasGrounded = cc.isGrounded;

        // ---- footsteps
        if (cc.isGrounded && !sliding && PlanarSpeed > 2f)
        {
            stepDistance += PlanarSpeed * dt;
            if (stepDistance > (IsSprinting ? 2.7f : 2.1f))
            {
                stepDistance = 0f;
                if (transform.position.y < 0.06f && Abilities.Instance != null)       // wading: each step leaves a ripple
                    Abilities.Instance.Ring(new Vector3(transform.position.x, 0.03f, transform.position.z), 0.15f, 1.0f, 0.5f, new Color(0.7f, 1f, 1f) * 0.7f);
                audioSrc.pitch = Random.Range(0.85f, 1.15f);
                audioSrc.PlayOneShot(stepClip, IsSprinting ? 0.4f : 0.28f);
            }
        }

        // ---- camera: height, landing dip (a damped spring), slide lean, shake
        landDipVel += -landDip * 120f * dt;
        landDipVel *= Mathf.Exp(-12f * dt);
        landDip += landDipVel * dt;
        camHeight = Mathf.Lerp(camHeight, cc.height - 0.15f, (sliding ? 16f : 12f) * dt);
        if (externalCamera) return;
        cam.localPosition = new Vector3(0f, camHeight + landDip, 0f);
        lean = Mathf.Lerp(lean, sliding ? -5f : -input.x * 1.5f * Mathf.Clamp01(PlanarSpeed / walkSpeed), 9f * dt);     // tips a little into a strafe
        float st = Time.time * 47f;
        cam.localRotation = Quaternion.Euler(pitch - recoilPitch + (Mathf.PerlinNoise(st, 0f) - 0.5f) * shake,
                                             recoilYaw + (Mathf.PerlinNoise(0f, st) - 0.5f) * shake,
                                             lean + (Mathf.PerlinNoise(st, st) - 0.5f) * shake * 1.5f);
    }
}
