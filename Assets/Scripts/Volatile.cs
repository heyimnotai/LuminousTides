using UnityEngine;

/// A destructible hazard: a cluster of glowing spore pods. One shot sets it off, and its blast hurts
/// enemies far more than the player, so luring a pack next to one pays off. Blasts chain to nearby pods.
[RequireComponent(typeof(Health))]
public class Volatile : MonoBehaviour
{
    public float radius = 5.5f;
    public float enemyDamage = 140f;
    public float playerDamage = 40f;
    static readonly Color Glow = new Color(1f, 0.6f, 0.15f);
    Vector3 baseScale;
    bool gone;

    void Awake()
    {
        baseScale = transform.localScale;
        GetComponent<Health>().Died += () => Invoke(nameof(Explode), 0.1f);     // short fuse, so chains ripple outward
    }

    void Update()
    {
        // A slow, uneasy throb so they read as dangerous.
        transform.localScale = baseScale * (1f + 0.06f * Mathf.Sin(Time.time * 3.1f + transform.position.x));
    }

    void Explode()
    {
        if (gone) return;
        gone = true;
        Blast.At(transform.position + Vector3.up * 0.5f, radius, enemyDamage, playerDamage, Glow, GetComponent<Health>());
        Destroy(gameObject);
    }
}
