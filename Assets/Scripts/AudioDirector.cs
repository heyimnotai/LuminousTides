using UnityEngine;

/// Generated soundtrack and ambience. Everything is synthesized when the scene loads, so there are no audio
/// files. Four looping layers play in sync and are mixed by what is happening:
///   pad    - always on: a slow minor chord bed
///   bass   - fades in when enemies are hunting the player
///   drums  - fades in with the bass, a little later
///   lead   - a fast arpeggio for the boss fight
/// plus wind-and-water ambience, short stings for ambushes, waves and the boss, and the player's own
/// warning sounds (shield breaking, shield recharging, a heartbeat at low health).
public class AudioDirector : MonoBehaviour
{
    const int Rate = 44100;
    const float Bpm = 108f;
    const int Bars = 8;
    static readonly float Beat = 60f / Bpm;

    // D minor: Dm, Bb, F, C - two bars each. Frequencies of the root, third and fifth around octave 3.
    static readonly float[][] Chords =
    {
        new[] { 146.83f, 174.61f, 220.00f },    // D  F  A
        new[] { 116.54f, 146.83f, 174.61f },    // Bb D  F
        new[] { 174.61f, 220.00f, 261.63f },    // F  A  C
        new[] { 130.81f, 164.81f, 196.00f },    // C  E  G
    };

    AudioSource pad, bass, drums, lead, ambience, oneShot, heartbeat;
    AudioClip stingAmbush, stingWave, stingBoss, stingComplete, shieldBreak, shieldRecharge;
    float combat, bossMix, lastCombatTime = -99f, lastShield = -1f;
    bool shieldWasRising;

    void Awake()
    {
        int n = Mathf.RoundToInt(Bars * 4 * Beat * Rate);
        pad = Layer("Pad", MakeClip("Pad", BuildPad(n)));
        bass = Layer("Bass", MakeClip("Bass", BuildBass(n)));
        drums = Layer("Drums", MakeClip("Drums", BuildDrums(n)));
        lead = Layer("Lead", MakeClip("Lead", BuildLead(n)));
        ambience = Layer("Ambience", MakeClip("Ambience", BuildAmbience(Rate * 14)));
        heartbeat = Layer("Heartbeat", MakeClip("Heartbeat", BuildHeartbeat()));
        oneShot = gameObject.AddComponent<AudioSource>();
        oneShot.spatialBlend = 0f;

        stingAmbush = MakeClip("StingAmbush", BuildSting(new[] { 73.42f, 77.78f, 110f }, 1.6f, true));
        stingWave = MakeClip("StingWave", BuildSting(new[] { 146.83f, 220f }, 1.1f, false));
        stingBoss = MakeClip("StingBoss", BuildSting(new[] { 36.71f, 55f, 58.27f, 73.42f }, 3.2f, true));
        stingComplete = MakeClip("StingComplete", BuildSting(new[] { 146.83f, 220f, 293.66f, 369.99f }, 3f, false));
        shieldBreak = MakeClip("ShieldBreak", BuildSweep(1400f, 180f, 0.4f, 0.5f));
        shieldRecharge = MakeClip("ShieldRecharge", BuildSweep(260f, 900f, 0.6f, 0.25f));

        // Start every musical layer on the same sample so they stay locked together.
        double start = AudioSettings.dspTime + 0.2;
        foreach (var s in new[] { pad, bass, drums, lead }) s.PlayScheduled(start);
        ambience.Play();
        heartbeat.Play();
    }

    void Start()
    {
        if (MissionDirector.Instance != null) MissionDirector.Instance.Sting += OnSting;
    }

    AudioSource Layer(string name, AudioClip clip)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false;
        s.ignoreListenerPause = true;
        return s;
    }

    void OnSting(string kind)
    {
        float v = GameSettings.MusicVolume;
        switch (kind)
        {
            case "ambush": oneShot.PlayOneShot(stingAmbush, 0.9f * v); break;
            case "wave": oneShot.PlayOneShot(stingWave, 0.8f * v); break;
            case "boss": oneShot.PlayOneShot(stingBoss, 1f * v); break;
            case "complete": oneShot.PlayOneShot(stingComplete, 0.9f * v); break;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        var gm = GameManager.Instance;
        var md = MissionDirector.Instance;

        // How heated things are: enemies hunting the player, held for a few seconds after the last one drops.
        int hunting = 0;
        foreach (var e in EnemyAI.All) if (e != null && e.IsAlerted) hunting++;
        bool inPlay = gm != null && (gm.State == GameManager.GameState.Playing || gm.State == GameManager.GameState.Dead);
        if (hunting > 0 && inPlay) lastCombatTime = Time.unscaledTime;
        bool fighting = inPlay && Time.unscaledTime - lastCombatTime < 4f;
        bool boss = inPlay && md != null && md.Current == MissionDirector.Phase.Boss;
        combat = Mathf.MoveTowards(combat, fighting ? Mathf.Clamp01(0.55f + hunting * 0.15f) : 0f, dt * (fighting ? 0.9f : 0.3f));
        bossMix = Mathf.MoveTowards(bossMix, boss ? 1f : 0f, dt * 0.5f);

        float music = GameSettings.MusicVolume;
        pad.volume = music * Mathf.Lerp(0.5f, 0.32f, combat);
        bass.volume = music * 0.55f * Mathf.Max(combat, bossMix);
        drums.volume = music * 0.6f * Mathf.Max(Mathf.Clamp01(combat * 1.4f - 0.25f), bossMix);
        lead.volume = music * 0.34f * bossMix;
        ambience.volume = 0.3f * Mathf.Lerp(1f, 0.55f, combat);

        // Player warnings.
        if (gm == null || gm.playerHealth == null) return;
        var h = gm.playerHealth;
        float lowHealth = inPlay && h.Current / h.maxHealth < 0.35f && !h.IsDead ? 1f : 0f;
        heartbeat.volume = Mathf.MoveTowards(heartbeat.volume, lowHealth * 0.7f, dt * 2f);
        if (lastShield >= 0f && inPlay)
        {
            if (lastShield > 0f && h.Shield <= 0f) oneShot.PlayOneShot(shieldBreak, 0.6f);
            bool rising = h.Shield > lastShield + 0.01f;
            if (rising && !shieldWasRising && h.Shield < h.maxShield * 0.9f) oneShot.PlayOneShot(shieldRecharge, 0.45f);
            shieldWasRising = rising;
        }
        lastShield = h.Shield;
    }

    // ------------------------------------------------------------------ synthesis helpers
    static AudioClip MakeClip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// Adds a note into a looping buffer. Its tail wraps round to the start, so the loop has no seam.
    static void AddNote(float[] buf, float startSeconds, float seconds, float freq, float amp, int harmonics, float attack, float decayRate)
    {
        int n = buf.Length, start = (int)(startSeconds * Rate), len = (int)(seconds * Rate);
        float release = Mathf.Min(0.08f, seconds * 0.3f);
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t * decayRate) * Mathf.Clamp01((seconds - t) / release);
            float s = 0f;
            for (int k = 1; k <= harmonics; k++) s += Mathf.Sin(2f * Mathf.PI * freq * k * t) / k;
            buf[(start + i) % n] += s * env * amp;
        }
    }

    static void AddNoise(float[] buf, float startSeconds, float seconds, float amp, float decayRate, float lowpass, bool highpass, System.Random rng)
    {
        int n = buf.Length, start = (int)(startSeconds * Rate), len = (int)(seconds * Rate);
        float lp = 0f, prev = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(rng.NextDouble() * 2 - 1);
            lp += (x - lp) * lowpass;
            float s = highpass ? x - prev : lp;
            prev = x;
            buf[(start + i) % n] += s * Mathf.Exp(-t * decayRate) * amp;
        }
    }

    static void Limit(float[] buf, float ceiling)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)System.Math.Tanh(buf[i] * 1.3f) * ceiling;
    }

    // ------------------------------------------------------------------ layers
    static float[] BuildPad(int n)
    {
        var b = new float[n];
        float chordLen = 8 * Beat;                              // two bars
        for (int c = 0; c < 4; c++)
            foreach (float f in Chords[c])
            {
                // Two slightly detuned voices per note, slow to swell and long to fade, overlapping the next chord.
                AddNote(b, c * chordLen, chordLen * 1.25f, f * 1.003f, 0.075f, 4, 1.4f, 0.12f);
                AddNote(b, c * chordLen, chordLen * 1.25f, f * 0.997f * 2f, 0.045f, 3, 1.8f, 0.14f);
            }
        Limit(b, 0.8f);
        return b;
    }

    static float[] BuildBass(int n)
    {
        var b = new float[n];
        for (int step = 0; step < Bars * 8; step++)             // eighth notes
        {
            int chord = step / 16 % 4;
            float root = Chords[chord][0] * 0.5f;
            bool accent = step % 8 == 0 || step % 8 == 3 || step % 8 == 6;
            float f = step % 8 == 7 ? root * 1.5f : root;      // a lift on the last eighth of each bar
            AddNote(b, step * Beat * 0.5f, Beat * 0.46f, f, accent ? 0.42f : 0.26f, 6, 0.006f, 5.5f);
        }
        Limit(b, 0.85f);
        return b;
    }

    static float[] BuildDrums(int n)
    {
        var b = new float[n];
        var rng = new System.Random(5);
        for (int bar = 0; bar < Bars; bar++)
        {
            float t0 = bar * 4 * Beat;
            foreach (float beat in new[] { 0f, 1.5f, 2f, 3.5f })
            {
                // Kick: a sine that drops in pitch.
                int start = (int)((t0 + beat * Beat) * Rate), len = (int)(0.28f * Rate);
                float phase = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    phase += 2f * Mathf.PI * (44f + 120f * Mathf.Exp(-t * 34f)) / Rate;
                    b[(start + i) % n] += Mathf.Sin(phase) * Mathf.Exp(-t * 11f) * 0.85f;
                }
            }
            foreach (float beat in new[] { 1f, 3f })
            {
                AddNoise(b, t0 + beat * Beat, 0.2f, 0.42f, 17f, 0.45f, false, rng);         // snare body
                AddNote(b, t0 + beat * Beat, 0.12f, 190f, 0.2f, 2, 0.002f, 26f);
            }
            for (int e = 0; e < 8; e++)
                AddNoise(b, t0 + e * Beat * 0.5f, 0.05f, e % 2 == 1 ? 0.2f : 0.11f, 70f, 1f, true, rng);   // hats
            if (bar % 4 == 3) for (int s = 0; s < 4; s++)
                AddNoise(b, t0 + (3f + s * 0.25f) * Beat, 0.1f, 0.25f + s * 0.06f, 28f, 0.5f, false, rng);   // fill
        }
        Limit(b, 0.85f);
        return b;
    }

    static float[] BuildLead(int n)
    {
        var b = new float[n];
        int[] pattern = { 0, 1, 2, 1, 0, 2, 1, 2 };
        for (int step = 0; step < Bars * 16; step++)            // sixteenth notes
        {
            int chord = step / 32 % 4;
            float f = Chords[chord][pattern[step % 8]] * (step % 16 < 8 ? 2f : 4f);
            AddNote(b, step * Beat * 0.25f, Beat * 0.24f, f, step % 4 == 0 ? 0.22f : 0.14f, 5, 0.004f, 12f);
        }
        Limit(b, 0.8f);
        return b;
    }

    static float[] BuildAmbience(int n)
    {
        var b = new float[n];
        var rng = new System.Random(9);
        float wind = 0f, wind2 = 0f, water = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float x = (float)(rng.NextDouble() * 2 - 1);
            wind += (x - wind) * 0.012f;
            wind2 += (wind - wind2) * 0.02f;
            // Whole numbers of cycles, so the loop joins up.
            float gust = 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 2f) * Mathf.Sin(t * 2f * Mathf.PI * 5f);
            water += ((x - prev) - water) * 0.25f; prev = x;
            float lap = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2f * Mathf.PI * 4f)), 6f);
            b[i] = wind2 * 9f * gust + water * 0.05f * lap;
        }
        // Occasional alien chirps.
        foreach (float at in new[] { 2.1f, 5.4f, 6.0f, 9.7f, 12.2f })
        {
            int start = (int)(at * Rate), len = (int)(0.22f * Rate);
            float f0 = 1400f + (float)rng.NextDouble() * 900f, phase = 0f;
            for (int i = 0; i < len && start + i < n; i++)
            {
                float t = i / (float)len;
                phase += 2f * Mathf.PI * (f0 + 500f * Mathf.Sin(t * 22f)) / Rate;
                b[start + i] += Mathf.Sin(phase) * Mathf.Sin(t * Mathf.PI) * 0.035f;
            }
        }
        int fade = Rate / 20;
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; b[i] *= k; b[n - 1 - i] *= k; }
        Limit(b, 0.7f);
        return b;
    }

    static float[] BuildHeartbeat()
    {
        int n = (int)(Rate * 0.86f);
        var b = new float[n];
        foreach (float at in new[] { 0f, 0.24f })
        {
            int start = (int)(at * Rate), len = (int)(0.2f * Rate);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                phase += 2f * Mathf.PI * (48f + 40f * Mathf.Exp(-t * 30f)) / Rate;
                b[start + i] += Mathf.Sin(phase) * Mathf.Exp(-t * 16f) * (at == 0f ? 0.9f : 0.6f);
            }
        }
        return b;
    }

    /// A swell of stacked notes: harsh and rising for threats, clean for the uplink horn and the victory chord.
    static float[] BuildSting(float[] freqs, float seconds, bool harsh)
    {
        int n = (int)(seconds * Rate);
        var b = new float[n];
        var rng = new System.Random(3);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, k = i / (float)n;
            float env = Mathf.Min(1f, t / (harsh ? 0.5f : 0.05f)) * Mathf.Pow(1f - k, harsh ? 0.6f : 1.4f);
            float s = 0f;
            foreach (float f in freqs)
                for (int h = 1; h <= (harsh ? 7 : 4); h++)
                    s += Mathf.Sin(2f * Mathf.PI * f * h * t * (harsh ? 1f + 0.012f * k : 1f)) / h;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.05f;
            b[i] = (s / freqs.Length * 0.6f + (harsh ? lp * 2.5f : 0f)) * env;
        }
        Limit(b, 0.9f);
        return b;
    }

    static float[] BuildSweep(float from, float to, float seconds, float amp)
    {
        int n = (int)(seconds * Rate);
        var b = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)n;
            phase += 2f * Mathf.PI * Mathf.Lerp(from, to, k * k) / Rate;
            b[i] = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f)) * Mathf.Sin(k * Mathf.PI) * amp;
        }
        return b;
    }
}
