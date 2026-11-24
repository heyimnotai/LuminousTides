using UnityEngine;

/// A glowing projectile spat by ranged enemies. A straight bolt damages the player on contact; a lobbed glob
/// arcs through the air and leaves a burning pool where it lands. Both pass through other enemies and are
/// slow enough to sidestep or dash out of.
public class EnemyBolt : MonoBehaviour
{
    Vector3 velocity;
    float damage, radius, born, gravity, poolRadius;
    Material poolMaterial;
    const float Life = 5f;
    static readonly Color Glow = new Color(1f, 0.55f, 0.15f);

    public static EnemyBolt Spawn(Vector3 position, Vector3 velocity, float damage, float size, Material material, Material trailMaterial)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "EnemyBolt";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * size;
        var mr = go.GetComponent<MeshRenderer>();
        if (material) mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (trailMaterial)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = 0.22f; trail.startWidth = size * 0.8f; trail.endWidth = 0f;
            trail.startColor = Glow; trail.endColor = new Color(Glow.r, Glow.g, Glow.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var light = go.AddComponent<Light>();
        light.type = LightType.Point; light.color = Glow; light.intensity = 3f; light.range = 4.5f;

        var bolt = go.AddComponent<EnemyBolt>();
        bolt.velocity = velocity;
        bolt.damage = damage;
        bolt.radius = size * 0.5f;
        bolt.born = Time.time;
        return bolt;
    }

    /// A glob thrown in an arc to land on `target` after `flightTime` seconds, leaving a pool.
    public static EnemyBolt SpawnLob(Vector3 position, Vector3 target, float flightTime, float damage, float poolRadius, Material material, Material trailMaterial)
    {
        const float g = 16f;
        Vector3 v = (target - position) / flightTime + Vector3.up * (0.5f * g * flightTime);
        var bolt = Spawn(position, v, damage, 0.5f, material, trailMaterial);
        bolt.gameObject.name = "EnemyGlob";
        bolt.gravity = g;
        bolt.poolRadius = poolRadius;
        bolt.poolMaterial = material;
        return bolt;
    }

    void Update()
    {
        if (Time.time - born > Life) { Destroy(gameObject); return; }
        velocity.y -= gravity * Time.deltaTime;
        Vector3 step = velocity * Time.deltaTime;
        // All layers: the player sits on the Ignore Raycast layer, which the default mask would skip.
        if (Physics.SphereCast(transform.position, radius, step.normalized, out RaycastHit hit, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<EnemyAI>() == null)
            {
                var player = hit.collider.GetComponentInParent<PlayerController>();
                if (player != null)
                {
                    var health = player.GetComponent<Health>();
                    if (health != null) health.Damage(damage, transform.position - velocity.normalized * 3f);
                }
                if (poolRadius > 0f) AcidPool.Spawn(hit.point, poolRadius, 16f, poolMaterial);
                else if (Abilities.Instance != null) Abilities.Instance.Sparks(hit.point, 10, Glow, 2f, 6f, false);
                Destroy(gameObject);
                return;
            }
        }
        transform.position += step;
        // A glob that falls into the sea still leaves its pool on the surface.
        if (poolRadius > 0f && transform.position.y < 0f && velocity.y < 0f)
        {
            AcidPool.Spawn(transform.position, poolRadius, 16f, poolMaterial);
            Destroy(gameObject);
        }
    }
}
