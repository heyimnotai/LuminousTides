using UnityEngine;

/// The extent of the world. The level builder and the minimap both read these.
public static class WorldMap
{
    public const float MinX = -48f, MaxX = 180f, MinZ = -10f, MaxZ = 270f;
    public const float CentreX = (MinX + MaxX) * 0.5f, CentreZ = (MinZ + MaxZ) * 0.5f;
    public const float Width = MaxX - MinX, Depth = MaxZ - MinZ;
    /// Feet below this are in deep water: the tide carries the player back to shore.
    public const float DeepWater = -1.7f;
}

/// One named place in the world. Areas are discs; where two overlap, the smaller one wins.
[System.Serializable]
public class WorldArea
{
    public string name = "AREA";
    public string region = "TIDAL SHALLOWS";
    public Vector2 centre;
    public float radius = 30f;
    public Transform respawn;           // where the player comes back after dying in or near this area
    // The light and air of the place. The world blends toward these as the player walks in.
    public Color fogColor = new Color(0.62f, 0.86f, 1f);
    public float fogStart = 70f, fogEnd = 1100f;
    public Color ambientSky = new Color(0.62f, 0.76f, 1.05f), ambientEquator = new Color(0.58f, 0.68f, 0.86f), ambientGround = new Color(0.30f, 0.55f, 0.58f);
    public Color sunColor = new Color(1f, 0.95f, 0.85f);
    public float sunIntensity = 2.3f;
    // The sky over the place: its colours from straight up down to the horizon, and the clouds.
    public Color skyZenith = new Color(0.05f, 0.08f, 0.40f), skyMid = new Color(0.18f, 0.42f, 0.98f), skyHorizon = new Color(0.62f, 0.86f, 1f);
    public Color cloudLit = new Color(1f, 0.93f, 0.88f), cloudShade = new Color(0.66f, 0.56f, 0.96f);
    [System.NonSerialized] public bool discovered;
}
