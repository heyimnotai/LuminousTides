using System.Collections.Generic;
using UnityEngine;

/// Shared area explosion: damages enemies, the player and destructible props (each by its own amount),
/// shoves what survives, and plays the visuals and sound. Used by bursters and volatile pods.
public static class Blast
{
    static AudioClip clip;
    static readonly HashSet<Health> seen = new HashSet<Health>();

    public static void At(Vector3 point, float radius, float enemyDamage, float playerDamage, Color colour, Health ignore = null)
    {
        var gm = GameManager.Instance;
        seen.Clear();
        var targets = new List<Health>();
        foreach (var c in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = c.GetComponentInParent<Health>();
            if (h != null && h != ignore && !h.IsDead && seen.Add(h)) targets.Add(h);
        }
        foreach (var h in targets)
        {
            if (h == null || h.IsDead) continue;
            float t = Mathf.Clamp01(Vector3.Distance(h.transform.position + Vector3.up * 0.8f, point) / radius);
            float falloff = Mathf.Lerp(1f, 0.35f, t);
            var enemy = h.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                Vector3 away = h.transform.position - point; away.y = 0f;
                enemy.Stagger(0.6f, away.normalized * Mathf.Lerp(8f, 3f, t));
                h.Damage(enemyDamage * falloff, point);
            }
            else if (h.GetComponent<PlayerController>() != null) h.Damage(playerDamage * falloff, point);
            else h.Damage(9999f, point);                 // pods and other props simply go off
        }

        if (clip == null) clip = ProceduralAudio.Explosion(0.9f);
        AudioSource.PlayClipAtPoint(clip, point, 1f);
        if (gm != null && gm.player != null)
            gm.player.AddShake(2.6f * Mathf.Clamp01(1f - Vector3.Distance(point, gm.player.transform.position) / 20f));
        var fx = Abilities.Instance;
        if (fx == null) return;
        fx.Ring(point + Vector3.up * 0.1f, 0.4f, radius, 0.32f, colour * 2.4f);
        fx.Flash(point + Vector3.up * 0.4f, colour, 24f, radius * 2.4f, 0.35f);
        fx.Sparks(point + Vector3.up * 0.2f, 44, colour, 5f, 14f, false);
    }
}

/// A glowing pool left where a lobbed glob lands. It burns the player while they stand in it, forcing
/// them to keep moving, then fades.
public class AcidPool : MonoBehaviour
{
    float radius, damagePerSecond, born, nextTick;
    const float Life = 5.5f;
    Light glow;

    public static void Spawn(Vector3 position, float radius, float damagePerSecond, Material material)
    {
        // Settle onto the ground under the impact point.
        if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            position = hit.point;
        position.y = Mathf.Max(position.y, 0.02f);          // floats on the water surface
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.name = "AcidPool";
        go.transform.position = position + Vector3.up * 0.04f;
        go.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
        var mr = go.GetComponent<MeshRenderer>();
        if (material) mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var pool = go.AddComponent<AcidPool>();
        pool.radius = radius;
        pool.damagePerSecond = damagePerSecond;
        pool.born = Time.time;
        pool.glow = go.AddComponent<Light>();
        pool.glow.type = LightType.Point; pool.glow.color = new Color(1f, 0.55f, 0.15f); pool.glow.range = radius * 2.2f; pool.glow.intensity = 5f;
        if (Abilities.Instance != null) Abilities.Instance.Sparks(position + Vector3.up * 0.2f, 16, new Color(1f, 0.55f, 0.15f), 2f, 6f, false);
    }

    void Update()
    {
        float age = Time.time - born;
        if (age > Life) { Destroy(gameObject); return; }
        float fade = Mathf.Clamp01((Life - age) / 1.2f);
        float grow = Mathf.Clamp01(age / 0.25f);
        transform.localScale = new Vector3(radius * 2f * grow * Mathf.Lerp(0.5f, 1f, fade), 0.03f, radius * 2f * grow * Mathf.Lerp(0.5f, 1f, fade));
        glow.intensity = (4f + Mathf.Sin(age * 9f)) * fade;

        var gm = GameManager.Instance;
        if (gm == null || Time.time < nextTick) return;
        nextTick = Time.time + 0.35f;
        Vector3 p = gm.player.transform.position;
        float flat = new Vector2(p.x - transform.position.x, p.z - transform.position.z).magnitude;
        if (flat <= radius * fade && Mathf.Abs(p.y - transform.position.y) < 1.3f && gm.player.IsGrounded)
            gm.playerHealth.Damage(damagePerSecond * 0.35f, transform.position);
    }
}
