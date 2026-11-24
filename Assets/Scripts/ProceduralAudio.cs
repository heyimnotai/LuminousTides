using UnityEngine;

/// Synthesized sound effects so the slice has audio without imported assets.
public static class ProceduralAudio
{
    const int Rate = 44100;

    /// Layered energy-rifle shot: sharp crack, descending zap, low thump and a short noisy tail.
    public static AudioClip PulseShot()
    {
        int n = (int)(Rate * 0.34f);
        var d = new float[n];
        var rng = new System.Random(7);
        float thumpPhase = 0f, zapPhase = 0f, lp = 0f, prevNoise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);

            float thumpFreq = 55f + 150f * Mathf.Exp(-t * 26f);
            thumpPhase += 2f * Mathf.PI * thumpFreq / Rate;
            float thump = Mathf.Sin(thumpPhase) * Mathf.Exp(-t * 17f) * 0.95f;

            float zapFreq = 380f + 2300f * Mathf.Exp(-t * 48f);
            zapPhase += 2f * Mathf.PI * zapFreq / Rate;
            float zap = (Mathf.Sin(zapPhase) + 0.45f * Mathf.Sin(zapPhase * 2.01f)) * Mathf.Exp(-t * 36f) * 0.42f;

            float crack = (noise - prevNoise) * Mathf.Exp(-t * 150f) * 0.55f;     // high-passed click
            prevNoise = noise;

            lp += (noise - lp) * 0.18f;                                           // low-passed tail
            float tail = lp * Mathf.Exp(-t * 13f) * 0.3f;

            d[i] = (float)System.Math.Tanh((thump + zap + crack + tail) * 1.5f) * 0.9f;
        }
        return Make("PulseShot", d);
    }

    /// Heavy scattergun blast: a hard crack, a deep punch and a wide noisy roar.
    public static AudioClip ShotgunBlast()
    {
        int n = (int)(Rate * 0.5f);
        var d = new float[n];
        var rng = new System.Random(17);
        float phase = 0f, lp = 0f, lp2 = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            phase += 2f * Mathf.PI * (48f + 170f * Mathf.Exp(-t * 20f)) / Rate;
            float crack = (noise - prev) * Mathf.Exp(-t * 110f) * 0.9f; prev = noise;
            lp += (noise - lp) * 0.22f;
            lp2 += (lp - lp2) * 0.07f;
            float roar = lp * Mathf.Exp(-t * 13f) * 1.3f + lp2 * Mathf.Exp(-t * 6f) * 1.6f;
            float punch = Mathf.Sin(phase) * Mathf.Exp(-t * 11f) * 1.1f;
            d[i] = (float)System.Math.Tanh((crack + roar + punch) * 1.7f) * 0.95f;
        }
        return Make("ShotgunBlast", d);
    }

    /// Soft boot-on-rock footstep.
    public static AudioClip Footstep()
    {
        int n = (int)(Rate * 0.11f);
        var d = new float[n];
        var rng = new System.Random(27);
        float lp = 0f, phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.16f;
            phase += 2f * Mathf.PI * (70f + 60f * Mathf.Exp(-t * 60f)) / Rate;
            d[i] = (lp * 1.4f * Mathf.Exp(-t * 55f) + Mathf.Sin(phase) * 0.5f * Mathf.Exp(-t * 40f)) * 0.8f;
        }
        return Make("Footstep", d);
    }

    public static AudioClip Click(float seconds, float freq)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            d[i] = (Mathf.Sin(2f * Mathf.PI * freq * i / Rate) * 0.5f + noise * 0.3f) * Mathf.Exp(-t * 10f) * 0.6f;
        }
        return Make("Click", d);
    }

    /// Bright bell-like ping for precision hits.
    public static AudioClip Ping(float seconds, float freq)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float s = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * freq * 2.02f * t);
            d[i] = s * Mathf.Exp(-t * 24f) * 0.4f;
        }
        return Make("Ping", d);
    }

    /// A thunderclap: a tearing crack, then a rumble that rolls away.
    public static AudioClip Thunder()
    {
        int n = (int)(Rate * 1.5f);
        var d = new float[n];
        var rng = new System.Random(37);
        float hp = 0f, prev = 0f, lp = 0f, lp2 = 0f, sub = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            hp = 0.8f * (hp + noise - prev); prev = noise;
            lp += (noise - lp) * 0.05f;
            lp2 += (noise - lp2) * 0.012f;
            sub += 2f * Mathf.PI * (38f + 60f * Mathf.Exp(-t * 9f)) / Rate;
            float tear = hp * (Mathf.Exp(-t * 55f) + 0.5f * Mathf.Exp(-Mathf.Abs(t - 0.07f) * 70f) + 0.35f * Mathf.Exp(-Mathf.Abs(t - 0.15f) * 60f));
            float boom = lp * Mathf.Exp(-t * 5.5f) * 3.2f + Mathf.Sin(sub) * Mathf.Exp(-t * 4.5f) * 0.9f;
            float roll = lp2 * Mathf.Exp(-t * 2.2f) * 7f * (0.7f + 0.3f * Mathf.Sin(t * 23f));
            float fade = Mathf.Clamp01((n - i) / (Rate * 0.15f));
            d[i] = (float)System.Math.Tanh((tear * 0.9f + boom + roll) * 1.3f) * 0.95f * fade;
        }
        return Make("Thunder", d);
    }

    /// One heavy round: a deep thump you feel, a chesty punch above it, a short hard crack on top, and the report
    /// rolling back off the landscape. No ringing: metal ring is what makes a gun sound like a tin can.
    public static AudioClip HeavyShot()
    {
        int n = (int)(Rate * 0.95f);
        var dry = new float[n];
        var rng = new System.Random(29);
        float sub = 0f, punch = 0f, lp1 = 0f, lp2 = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            sub += 2f * Mathf.PI * (42f + 95f * Mathf.Exp(-t * 26f)) / Rate;           // falls from 137 Hz to 42 Hz
            punch += 2f * Mathf.PI * (105f + 260f * Mathf.Exp(-t * 48f)) / Rate;       // falls from 365 Hz to 105 Hz
            lp1 += (noise - lp1) * 0.07f;                                             // dark, boomy noise: the body of the blast
            lp2 += (noise - lp2) * 0.30f;
            hp = 0.86f * (hp + noise - prev); prev = noise;
            float thump = Mathf.Sin(sub) * Mathf.Exp(-t * 7.5f) * 1.25f;
            float chest = Mathf.Sin(punch) * Mathf.Exp(-t * 19f) * 0.85f;
            float body = lp1 * Mathf.Exp(-t * 9f) * 2.4f + lp2 * Mathf.Exp(-t * 28f) * 0.8f;
            float crack = hp * Mathf.Exp(-t * 140f) * 0.75f;
            dry[i] = thump + chest + body + crack;
        }
        // The report coming back: two quieter, duller copies, like echoes off rock.
        var d = new float[n];
        int e1 = (int)(Rate * 0.11f), e2 = (int)(Rate * 0.23f);
        float tail1 = 0f, tail2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float wet = 0f;
            if (i >= e1) { tail1 += (dry[i - e1] - tail1) * 0.12f; wet += tail1 * 0.42f; }
            if (i >= e2) { tail2 += (dry[i - e2] - tail2) * 0.06f; wet += tail2 * 0.32f; }
            float fade = Mathf.Clamp01((n - i) / (Rate * 0.08f));
            d[i] = (float)System.Math.Tanh((dry[i] + wet) * 1.45f) * 0.95f * fade;
        }
        return Make("HeavyShot", d);
    }

    /// A wet crack falling into a short bass drop: the pop of a kill.
    public static AudioClip KillPop()
    {
        int n = (int)(Rate * 0.26f);
        var d = new float[n];
        var rng = new System.Random(11);
        float phase = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (70f + 900f * Mathf.Exp(-t * 38f)) / Rate;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.55f;
            float crack = lp * Mathf.Exp(-t * 55f) * 0.9f;
            float body = Mathf.Sin(phase) * Mathf.Exp(-t * 13f);
            float sparkle = Mathf.Sin(2f * Mathf.PI * 2350f * t) * Mathf.Exp(-t * 30f) * 0.18f;
            d[i] = (float)System.Math.Tanh((body + crack + sparkle) * 1.5f) * 0.85f;
        }
        return Make("KillPop", d);
    }

    /// A dull meaty smack: a round landing on a body.
    public static AudioClip Thwack()
    {
        int n = (int)(Rate * 0.07f);
        var d = new float[n];
        var rng = new System.Random(17);
        float phase = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (180f + 500f * Mathf.Exp(-t * 80f)) / Rate;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.4f;
            d[i] = (float)System.Math.Tanh((Mathf.Sin(phase) * 0.8f + lp * 0.9f) * Mathf.Exp(-t * 55f) * 1.6f) * 0.7f;
        }
        return Make("Thwack", d);
    }

    /// Glass giving way: a shield breaking.
    public static AudioClip ShieldBreak()
    {
        int n = (int)(Rate * 0.45f);
        var d = new float[n];
        var rng = new System.Random(23);
        float hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            hp = 0.85f * (hp + noise - prev); prev = noise;          // high-passed: all fizz, no thud
            float shards = 0f;
            for (int k = 0; k < 5; k++) shards += Mathf.Sin(2f * Mathf.PI * (1700f + k * 730f) * t * (1f - 0.25f * t)) * Mathf.Exp(-t * (9f + k * 4f));
            float drop = Mathf.Sin(2f * Mathf.PI * (420f - 600f * t) * t) * Mathf.Exp(-t * 9f) * 0.5f;
            d[i] = (float)System.Math.Tanh(hp * Mathf.Exp(-t * 14f) * 0.8f + shards * 0.16f + drop) * 0.8f;
        }
        return Make("ShieldBreak", d);
    }

    /// Heavy low hit for a kill confirmation.
    public static AudioClip Thump(float seconds)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(5);
        float phase = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (60f + 240f * Mathf.Exp(-t * 30f)) / Rate;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.3f;
            float ring = Mathf.Sin(2f * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 40f) * 0.25f;
            d[i] = (float)System.Math.Tanh((Mathf.Sin(phase) * Mathf.Exp(-t * 16f) + lp * Mathf.Exp(-t * 30f) * 0.6f + ring) * 1.4f) * 0.9f;
        }
        return Make("Thump", d);
    }

    /// Metallic clack made of a few rapid ticks, used for magazine handling.
    public static AudioClip Mechanical(float seconds, float freq, int seed)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(seed);
        float[] hits = { 0f, 0.035f, 0.085f };
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float hp = noise - prev; prev = noise;
            float s = 0f;
            for (int h = 0; h < hits.Length; h++)
            {
                float lt = t - hits[h];
                if (lt < 0f) continue;
                float env = Mathf.Exp(-lt * 70f) * (h == 2 ? 1f : 0.55f);
                s += (hp * 0.5f + Mathf.Sin(2f * Mathf.PI * freq * (1f + h * 0.6f) * lt) * 0.5f + Mathf.Sin(2f * Mathf.PI * freq * 5.3f * lt) * 0.2f) * env;
            }
            d[i] = Mathf.Clamp(s * 0.6f, -1f, 1f);
        }
        return Make("Mechanical", d);
    }

    /// Rising electric whine as the weapon re-energises after a reload.
    public static AudioClip Charge(float seconds)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            phase += 2f * Mathf.PI * Mathf.Lerp(300f, 1500f, t * t) / Rate;
            d[i] = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 3f)) * Mathf.Sin(t * Mathf.PI) * 0.28f;
        }
        return Make("Charge", d);
    }

    public static AudioClip Screech()
    {
        int n = (int)(Rate * 0.35f);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float freq = 500f + 350f * Mathf.Sin(t * 40f) - 250f * t;
            phase += 2f * Mathf.PI * freq / Rate;
            d[i] = Mathf.Sign(Mathf.Sin(phase)) * 0.25f * Mathf.Sin(t * Mathf.PI);
        }
        return Make("Screech", d);
    }

    /// Wet, short grunt when a creature is hit.
    public static AudioClip Squelch()
    {
        int n = (int)(Rate * 0.11f);
        var d = new float[n];
        var rng = new System.Random(11);
        float lp = 0f, phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.12f;
            phase += 2f * Mathf.PI * Mathf.Lerp(260f, 110f, t) / Rate;
            d[i] = (lp * 1.6f + Mathf.Sin(phase) * 0.35f) * Mathf.Sin(t * Mathf.PI) * 0.7f;
        }
        return Make("Squelch", d);
    }

    /// Rush of air: band-swept noise that swells and fades.
    public static AudioClip Whoosh(float seconds)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(21);
        float lp = 0f, bp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float cutoff = Mathf.Lerp(0.04f, 0.38f, Mathf.Sin(t * Mathf.PI));
            lp += (noise - lp) * cutoff;
            bp += (lp - bp) * 0.02f;
            d[i] = (lp - bp) * Mathf.Sin(t * Mathf.PI) * 1.6f;
        }
        return Make("Whoosh", d);
    }

    /// Deep expanding boom with an electric edge, for the pulse.
    public static AudioClip Shockwave(float seconds)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(31);
        float phase = 0f, zap = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (42f + 130f * Mathf.Exp(-t * 9f)) / Rate;
            zap += 2f * Mathf.PI * (300f + 1900f * Mathf.Exp(-t * 14f)) / Rate;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.2f;
            float s = Mathf.Sin(phase) * Mathf.Exp(-t * 5.5f) + Mathf.Sin(zap) * Mathf.Exp(-t * 16f) * 0.35f + lp * Mathf.Exp(-t * 8f) * 0.5f;
            d[i] = (float)System.Math.Tanh(s * 1.6f) * 0.9f;
        }
        return Make("Shockwave", d);
    }

    /// Grenade burst: hard crack, heavy low body and a long rumbling tail.
    public static AudioClip Explosion(float seconds)
    {
        int n = (int)(Rate * seconds);
        var d = new float[n];
        var rng = new System.Random(41);
        float phase = 0f, lp = 0f, lp2 = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            phase += 2f * Mathf.PI * (38f + 110f * Mathf.Exp(-t * 12f)) / Rate;
            float crack = (noise - prev) * Mathf.Exp(-t * 90f) * 0.8f; prev = noise;
            lp += (noise - lp) * 0.09f;
            lp2 += (lp - lp2) * 0.05f;
            float body = Mathf.Sin(phase) * Mathf.Exp(-t * 6f) * 1.1f;
            float rumble = (lp * Mathf.Exp(-t * 7f) * 1.4f + lp2 * Mathf.Exp(-t * 3.2f) * 2.2f);
            d[i] = (float)System.Math.Tanh((crack + body + rumble) * 1.5f) * 0.95f;
        }
        return Make("Explosion", d);
    }

    static AudioClip Make(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
