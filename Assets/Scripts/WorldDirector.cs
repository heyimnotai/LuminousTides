using UnityEngine;

/// Knows which named area the player is in, announces it on arrival (and the first time, as a discovery), moves the
/// respawn point to the area just entered, and pulls the player back to shore if they walk out into deep water.
public class WorldDirector : MonoBehaviour
{
    public static WorldDirector Instance { get; private set; }
    public WorldArea[] areas = new WorldArea[0];

    public WorldArea Current { get; private set; }
    public float EnteredTime { get; private set; } = -99f;
    public bool EnteredFirstTime { get; private set; }
    public int DiscoveredCount { get; private set; }
    /// Set for a moment after the tide has carried the player back.
    public float RescuedTime { get; private set; } = -99f;

    GameManager gm;
    Vector3 lastSafe;
    float nextCheck, candidateSince;
    WorldArea candidate;

    static readonly int ZenithId = Shader.PropertyToID("_Zenith"), MidId = Shader.PropertyToID("_Mid"), HorizonId = Shader.PropertyToID("_Horizon"),
                        CloudId = Shader.PropertyToID("_CloudColor"), CloudShadeId = Shader.PropertyToID("_CloudShade");

    void Awake() { Instance = this; }

    /// Eases the fog, the ambient light and the sun toward the look of the area the player is in.
    void BlendAtmosphere()
    {
        if (Current == null) return;
        float k = 1f - Mathf.Exp(-0.9f * Time.deltaTime);
        RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, Current.fogColor, k);
        RenderSettings.fogStartDistance = Mathf.Lerp(RenderSettings.fogStartDistance, Current.fogStart, k);
        RenderSettings.fogEndDistance = Mathf.Lerp(RenderSettings.fogEndDistance, Current.fogEnd, k);
        RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, Current.ambientSky, k);
        RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor, Current.ambientEquator, k);
        RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor, Current.ambientGround, k);
        var sky = RenderSettings.skybox;
        if (sky != null && sky.HasProperty(ZenithId))
        {
            sky.SetColor(ZenithId, Color.Lerp(sky.GetColor(ZenithId), Current.skyZenith, k));
            sky.SetColor(MidId, Color.Lerp(sky.GetColor(MidId), Current.skyMid, k));
            sky.SetColor(HorizonId, Color.Lerp(sky.GetColor(HorizonId), Current.skyHorizon, k));
            sky.SetColor(CloudId, Color.Lerp(sky.GetColor(CloudId), Current.cloudLit, k));
            sky.SetColor(CloudShadeId, Color.Lerp(sky.GetColor(CloudShadeId), Current.cloudShade, k));
        }
        var sun = RenderSettings.sun;
        if (sun == null) return;
        sun.color = Color.Lerp(sun.color, Current.sunColor, k);
        sun.intensity = Mathf.Lerp(sun.intensity, Current.sunIntensity, k);
    }

    void Start()
    {
        gm = GameManager.Instance;
        if (gm != null) lastSafe = gm.player.transform.position;
    }

    void Update()
    {
        if (gm == null || gm.State != GameManager.GameState.Playing) return;
        BlendAtmosphere();
        Vector3 p = gm.player.transform.position;

        // Deep water: back to the last dry ground the player stood on.
        if (p.y < WorldMap.DeepWater)
        {
            gm.player.Teleport(lastSafe + Vector3.up * 0.2f, gm.player.Yaw + 180f);
            RescuedTime = Time.time;
            if (Abilities.Instance != null)
            {
                Abilities.Instance.Ring(lastSafe + Vector3.up * 0.15f, 0.4f, 3.5f, 0.5f, new Color(0.6f, 1f, 0.98f) * 1.6f);
                Abilities.Instance.Sparks(lastSafe + Vector3.up * 0.3f, 30, new Color(0.6f, 1f, 0.98f), 3f, 9f, false);
            }
            return;
        }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        if (gm.player.IsGrounded && p.y > 0.15f) lastSafe = p;

        // The area the player is standing in. A new one has to hold for a moment before it is announced, so walking
        // along a border does not flicker between two names.
        WorldArea best = null;
        foreach (var a in areas)
        {
            if (new Vector2(p.x - a.centre.x, p.z - a.centre.y).magnitude > a.radius) continue;
            if (best == null || a.radius < best.radius) best = a;
        }
        if (best == null || best == Current) { candidate = null; return; }
        if (best != candidate) { candidate = best; candidateSince = Time.time; return; }
        if (Time.time - candidateSince < 0.6f) return;

        Current = best;
        candidate = null;
        EnteredTime = Time.time;
        EnteredFirstTime = !best.discovered;
        if (!best.discovered) { best.discovered = true; DiscoveredCount++; }
        // Dying sends you back to the area you last walked into, unless the mission is holding a checkpoint of its own.
        var md = MissionDirector.Instance;
        bool missionHolds = md != null && (md.Current == MissionDirector.Phase.Boss || (md.Current == MissionDirector.Phase.Uplink && md.UplinkStarted));
        if (best.respawn != null && !missionHolds && gm.spawnPoint != null) gm.spawnPoint.position = best.respawn.position;
    }
}
