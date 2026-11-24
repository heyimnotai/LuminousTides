using UnityEngine;

/// Makes enemies drop ammunition when they die. Tougher enemies drop more reliably; the boss's helpers
/// keep the player supplied through the final fight.
public class LootDrops : MonoBehaviour
{
    public Material pickupMaterial;
    public float stalkerChance = 0.45f;
    public float spitterChance = 0.6f;
    public int roundsPerPickup = 28;

    void OnEnable() { EnemyAI.AnyDied += OnEnemyDied; }
    void OnDisable() { EnemyAI.AnyDied -= OnEnemyDied; }

    void OnEnemyDied(EnemyAI enemy)
    {
        int drops;
        if (enemy.kind == EnemyKind.Boss) drops = 0;                               // the mission ends with it
        else if (enemy.elite) drops = 2;
        else drops = Random.value < (enemy.kind == EnemyKind.Spitter ? spitterChance : stalkerChance) ? 1 : 0;
        for (int i = 0; i < drops; i++)
        {
            Vector2 r = Random.insideUnitCircle * 0.8f;
            AmmoPickup.Spawn(enemy.transform.position + new Vector3(r.x, 0.6f, r.y), roundsPerPickup, pickupMaterial);
        }
    }
}

/// A glowing ammunition brick. It bobs and spins, slides toward the player when they come close,
/// and is collected on contact unless their reserve is already full.
public class AmmoPickup : MonoBehaviour
{
    int rounds;
    float born, baseY;
    const float Life = 60f, MagnetRange = 3.2f, CollectRange = 1.1f;

    public static void Spawn(Vector3 position, int rounds, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = "AmmoPickup";
        go.transform.position = position;
        go.transform.localScale = new Vector3(0.22f, 0.3f, 0.42f);
        var mr = go.GetComponent<MeshRenderer>();
        if (material) mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.4f, 1f, 0.95f); light.intensity = 2.5f; light.range = 3.5f;
        var pickup = go.AddComponent<AmmoPickup>();
        pickup.rounds = rounds;
        pickup.born = Time.time;
        // Settle onto the ground under the drop point.
        pickup.baseY = Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(hit.point.y, 0f) + 0.55f : position.y;
    }

    void Update()
    {
        float age = Time.time - born;
        if (age > Life) { Destroy(gameObject); return; }
        transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);

        var gm = GameManager.Instance;
        if (gm == null) return;
        Vector3 target = gm.player.transform.position + Vector3.up * 0.9f;
        float dist = Vector3.Distance(transform.position, target);
        var arsenal = Arsenal.Instance;
        bool wanted = arsenal != null && arsenal.WantsAmmo() && gm.State == GameManager.GameState.Playing;

        if (wanted && dist < CollectRange)
        {
            arsenal.AddAmmoToAll();         // one brick tops up every weapon by its own amount
            AudioSource.PlayClipAtPoint(ProceduralAudio.Mechanical(0.14f, 480f, 9), target, 0.8f);
            Destroy(gameObject);
            return;
        }
        if (wanted && dist < MagnetRange)
            transform.position = Vector3.MoveTowards(transform.position, target, (14f - dist * 3f) * Time.deltaTime);
        else
        {
            var p = transform.position;
            p.y = Mathf.Lerp(p.y, baseY + Mathf.Sin(age * 2.5f) * 0.1f, 6f * Time.deltaTime);
            transform.position = p;
        }
        if (age > Life - 6f) transform.localScale *= 1f - 0.2f * Time.deltaTime;   // shrink away before vanishing
    }
}
