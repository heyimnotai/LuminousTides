using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Builds and animates the whole HUD at runtime from the sprites in Resources/HUD:
/// minimap, objective tracker, compass, shield/health panel, ability slots, ammo panel,
/// crosshair with hit markers, damage direction arcs and floating damage numbers.
public class HUD : MonoBehaviour
{
    public PlayerController player;
    public Health playerHealth;
    public Arsenal arsenal;
    Weapon weapon => arsenal.Current;
    public Abilities abilities;
    public Camera cam;

    static readonly Color Cyan = new Color(0.30f, 0.95f, 0.92f);
    static readonly Color Cream = new Color(0.98f, 0.93f, 0.82f);
    static readonly Color Orange = new Color(1f, 0.62f, 0.15f);
    static readonly Color Red = new Color(1f, 0.32f, 0.25f);
    static readonly Color Yellow = new Color(1f, 0.88f, 0.25f);
    static readonly Color MinorBar = new Color(0.90f, 0.17f, 0.15f);
    static readonly Color EliteBar = new Color(1f, 0.74f, 0.12f);
    static readonly Color Soft = new Color(1f, 1f, 1f, 0.75f);
    static readonly Color BarBack = new Color(0f, 0f, 0f, 0.45f);

    const float MapRadius = 86f;          // px
    const float MapScale = 2.3f;          // px per metre
    const float CompassHalfWidth = 300f;  // px for 60 degrees

    Font font;
    RectTransform root;
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    // bars
    RectTransform shieldFill, shieldGhost, healthFill, healthGhost;
    Image shieldFillImg, healthFillImg, shieldIcon;
    Text shieldText, healthText;
    float shieldShown = 1f, healthShown = 1f, shieldGhostV = 1f, healthGhostV = 1f, shieldFlash;
    const float BarWidth = 372f;

    // ammo
    Text ammoText, reserveText, weaponText;
    RectTransform reloadFill, crossReload;
    float ammoPunch, reservePunch;
    Weapon shownWeapon;
    Text otherWeaponText, superLabel;
    RectTransform superFill;
    Image superFillImg;
    int lastMag = -1, lastReserve = -1;

    // crosshair
    readonly RectTransform[] cross = new RectTransform[4];
    readonly Image[] hitTicks = new Image[4];
    float hitPunch;
    CanvasGroup crossGroup;

    // enemy health bars
    class EnemyBar { public EnemyAI enemy; public RectTransform rt, fill, ghost; public CanvasGroup group; public float ghostV = 1f, lastFrac = 1f, ghostHold, shown; }
    readonly Dictionary<EnemyAI, EnemyBar> enemyBars = new Dictionary<EnemyAI, EnemyBar>();
    readonly List<EnemyAI> staleBars = new List<EnemyAI>();
    RectTransform enemyBarLayer;

    // mission: uplink progress or boss health under the compass, and phase banners
    CanvasGroup missionGroup;
    RectTransform missionFill, missionGhost;
    Image missionFillImg;
    Text missionLeft, missionRight, bannerText;
    float missionGhostV, missionShown;
    const float MissionBarWidth = 620f;
    const float EnemyBarWidth = 150f, EnemyBarHeight = 7f;

    // minimap + compass + objective
    RectTransform mapRoot, mapImage, mapObjective, marker, compassObjective;
    readonly List<RectTransform> mapDots = new List<RectTransform>();
    readonly List<Image> mapDotImages = new List<Image>();
    readonly List<RectTransform> compassTicks = new List<RectTransform>();
    readonly List<float> compassTickAngles = new List<float>();
    Text objectiveText, distanceText, markerDist;

    // feedback
    Image vignette;
    float vignetteAlpha;
    struct Arc { public RectTransform rt; public Image img; public Vector3 source; public float time; }
    readonly Arc[] arcs = new Arc[4];
    int nextArc;
    struct Number { public Text text; public Vector3 world; public float time; public bool kill, crit; }
    readonly Number[] numbers = new Number[14];
    int nextNumber;

    // contextual control hints, each shown once per session
    Text hintText;
    float hintShownAt = -99f, hintDuration, nextHintCheck;
    static readonly HashSet<string> hintsShown = new HashSet<string>();
    Volatile[] pods;

    // score and kill callouts
    Text scoreText;
    readonly Text[] calloutText = new Text[4];
    readonly float[] calloutTime = { -99f, -99f, -99f, -99f };
    int nextCallout, shownScore;
    bool lastCalloutWasStreak;

    // abilities
    readonly Image[] abilityIcon = new Image[3], abilityCool = new Image[3], abilitySlot = new Image[3], abilityKey = new Image[3];
    readonly Text[] abilityTimer = new Text[3];
    readonly float[] abilityFlash = new float[3];
    readonly bool[] abilityWasReady = { true, true, true };

    // messages
    Text centerText, subText, introTitle, introSub;
    CanvasGroup introGroup;

    void Awake()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)transform;
        Build();
    }

    void Start()
    {
        enemyBarLayer = Node("EnemyBars", root, BL, BL, Vector2.zero, Vector2.zero);
        enemyBarLayer.SetSiblingIndex(1);       // above the vignette, below every panel
        playerHealth.Damaged += OnPlayerDamaged;
        if (GameManager.Instance != null) GameManager.Instance.Callout += OnCallout;
        foreach (var w in arsenal.weapons) w.Hit += OnWeaponHit;
        if (abilities != null) abilities.MeleeHit += (point, amount, killed) => OnWeaponHit(point, amount, killed, false);
    }

    void OnPlayerDamaged(float amount, Vector3 source)
    {
        vignetteAlpha = Mathf.Min(0.85f, vignetteAlpha + 0.45f);
        if (playerHealth.Shield <= 0f && shieldShown > 0.01f) shieldFlash = 1f;
        arcs[nextArc].source = source;
        arcs[nextArc].time = Time.time;
        nextArc = (nextArc + 1) % arcs.Length;
    }

    void OnWeaponHit(Vector3 point, float amount, bool killed, bool precision)
    {
        hitPunch = 1f;
        if (killed && precision && GameManager.Instance != null) GameManager.Instance.RegisterPrecisionKill();
        var n = numbers[nextNumber];
        n.world = point + Vector3.up * 0.3f + Random.insideUnitSphere * 0.25f;
        n.time = Time.time;
        n.kill = killed;
        n.crit = precision;
        n.text.text = Mathf.RoundToInt(amount).ToString();
        numbers[nextNumber] = n;
        nextNumber = (nextNumber + 1) % numbers.Length;
    }

    // ------------------------------------------------------------------ construction helpers
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f), BL = Vector2.zero, BR = new Vector2(1, 0), TL = new Vector2(0, 1),
                            TC = new Vector2(0.5f, 1), BC = new Vector2(0.5f, 0), LeftMid = new Vector2(0, 0.5f);

    Sprite Spr(string name)
    {
        if (name == null) return null;
        if (!sprites.TryGetValue(name, out var s)) sprites[name] = s = Resources.Load<Sprite>("HUD/" + name);
        return s;
    }

    RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    Image Img(string name, Transform parent, string sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var rt = Node(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Spr(sprite);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, Color color,
               TextAnchor align, FontStyle style = FontStyle.Normal)
    {
        var rt = Node(name, parent, anchor, pivot, pos, size);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.55f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static string Spaced(string s) => string.Join(" ", s.ToCharArray()).Replace("   ", "    ");

    void BuildBar(Transform parent, string name, string icon, float y, Color color, out RectTransform fill, out RectTransform ghost,
                  out Image fillImg, out Text label, out Image iconImg)
    {
        iconImg = Img(name + "Icon", parent, icon, BL, Center, new Vector2(46, y + 4), new Vector2(34, 34), color);
        label = Label(name + "Label", parent, BL, BL, new Vector2(84, y + 4), new Vector2(300, 26), 21, Color.white, TextAnchor.LowerLeft);
        Img(name + "Back", parent, null, BL, BL, new Vector2(84, y - 12), new Vector2(BarWidth, 9), BarBack);
        ghost = Img(name + "Ghost", parent, null, BL, BL, new Vector2(84, y - 12), new Vector2(BarWidth, 9), new Color(1, 1, 1, 0.7f)).rectTransform;
        fillImg = Img(name + "Fill", parent, null, BL, BL, new Vector2(84, y - 12), new Vector2(BarWidth, 9), color);
        fill = fillImg.rectTransform;
        // segment notches every quarter
        for (int i = 1; i < 4; i++)
            Img(name + "Notch" + i, parent, null, BL, BL, new Vector2(84 + BarWidth * i / 4f - 1, y - 12), new Vector2(2, 9), new Color(0, 0, 0, 0.5f));
    }

    void Build()
    {
        // ---- damage vignette (bottom-most)
        vignette = Img("Vignette", root, "vignette", Center, Center, Vector2.zero, Vector2.zero, new Color(0.9f, 0.08f, 0.05f, 0f));
        vignette.rectTransform.anchorMin = Vector2.zero; vignette.rectTransform.anchorMax = Vector2.one;

        // ---- world objective marker and damage numbers sit under the panels
        marker = Img("Marker", root, "diamond_outline", Center, Center, Vector2.zero, new Vector2(30, 30), Orange).rectTransform;
        markerDist = Label("MarkerDist", marker, Center, Center, new Vector2(0, -30), new Vector2(120, 24), 19, Color.white, TextAnchor.MiddleCenter);
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i].text = Label("Dmg" + i, root, BL, Center, Vector2.zero, new Vector2(100, 30), 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            numbers[i].time = -99f;
            numbers[i].text.enabled = false;
        }

        // ---- minimap, top-left
        var map = Node("Minimap", root, TL, Center, new Vector2(150, -150), new Vector2(200, 200));
        Img("MapBack", map, "disc", Center, Center, Vector2.zero, new Vector2(MapRadius * 2 + 6, MapRadius * 2 + 6), new Color(0.03f, 0.07f, 0.16f, 0.62f));
        var mask = Img("MapMask", map, "disc", Center, Center, Vector2.zero, new Vector2(MapRadius * 2, MapRadius * 2), Color.white);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        mapRoot = Node("MapRoot", mask.transform, Center, Center, Vector2.zero, Vector2.zero);
        var mapSprite = Spr("minimap");
        var mi = Img("MapImage", mapRoot, "minimap", Center, new Vector2(0.5f, 0f), Vector2.zero,
                     mapSprite != null ? new Vector2(mapSprite.rect.width, mapSprite.rect.height) * (MapScale / 4f) : Vector2.zero,
                     new Color(1, 1, 1, mapSprite != null ? 0.95f : 0f));
        mapImage = mi.rectTransform;
        Img("MapRing", map, "ring", Center, Center, Vector2.zero, new Vector2(MapRadius * 2 + 14, MapRadius * 2 + 14), new Color(1, 1, 1, 0.75f));
        mapObjective = Img("MapObjective", map, "diamond", Center, Center, Vector2.zero, new Vector2(13, 13), Orange).rectTransform;
        Img("MapPlayer", map, "arrow", Center, Center, Vector2.zero, new Vector2(20, 20), Cyan);

        // ---- where you are: the area's name, shown large for a few seconds on arrival
        areaGroup = Node("Area", root, Center, Center, new Vector2(0, 150), new Vector2(900, 120)).gameObject.AddComponent<CanvasGroup>();
        areaGroup.alpha = 0f;
        areaName = Label("AreaName", areaGroup.transform, Center, Center, new Vector2(0, 8), new Vector2(900, 60), 46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        areaRegion = Label("AreaRegion", areaGroup.transform, Center, Center, new Vector2(0, -34), new Vector2(900, 30), 20, Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
        prompt = Label("Prompt", root, BC, BC, new Vector2(0, 190), new Vector2(900, 40), 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        prompt.text = "";
        areaRule = Img("AreaRule", areaGroup.transform, null, Center, Center, new Vector2(0, -16), new Vector2(0, 2), new Color(1, 1, 1, 0.7f)).rectTransform;

        // ---- objective, under the minimap
        var obj = Img("ObjectivePanel", root, "panel_objective", TL, TL, new Vector2(48, -262), new Vector2(430, 64), Color.white);
        Img("ObjIcon", obj.transform, "diamond_outline", LeftMid, Center, new Vector2(30, 0), new Vector2(26, 26), Orange);
        objectiveText = Label("Objective", obj.transform, LeftMid, LeftMid, new Vector2(56, 11), new Vector2(360, 26), 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        distanceText = Label("Distance", obj.transform, LeftMid, LeftMid, new Vector2(56, -13), new Vector2(200, 24), 19, Soft, TextAnchor.MiddleLeft);

        // ---- compass, top-centre
        var compass = Node("Compass", root, TC, TC, new Vector2(0, -34), new Vector2(CompassHalfWidth * 2, 60));
        Img("CompassLine", compass, null, BC, BC, new Vector2(0, 10), new Vector2(CompassHalfWidth * 2, 2), new Color(1, 1, 1, 0.65f));
        Img("CompassPointer", compass, "triangle", BC, TC, new Vector2(0, 9), new Vector2(15, 13), Cyan);
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int a = 0; a < 360; a += 15)
        {
            RectTransform rt;
            if (a % 45 == 0)
            {
                bool major = a % 90 == 0;
                var t = Label("Compass" + a, compass, BC, BC, new Vector2(0, 16), new Vector2(60, 34), major ? 28 : 17,
                              new Color(1, 1, 1, major ? 1f : 0.75f), TextAnchor.LowerCenter, FontStyle.Bold);
                t.text = names[a / 45];
                rt = t.rectTransform;
            }
            else rt = Img("Tick" + a, compass, null, BC, BC, new Vector2(0, 11), new Vector2(2, 9), new Color(1, 1, 1, 0.7f)).rectTransform;
            compassTicks.Add(rt);
            compassTickAngles.Add(a);
        }
        compassObjective = Img("CompassObjective", compass, "diamond", BC, Center, new Vector2(0, 11), new Vector2(13, 13), Orange).rectTransform;

        // ---- shield / health, bottom-left
        var bars = Img("BarsPanel", root, "panel_bars", BL, BL, new Vector2(44, 40), new Vector2(500, 124), Color.white);
        BuildBar(bars.transform, "Shield", "icon_shield", 82, Cyan, out shieldFill, out shieldGhost, out shieldFillImg, out shieldText, out shieldIcon);
        BuildBar(bars.transform, "Health", "icon_plus", 32, Cream, out healthFill, out healthGhost, out healthFillImg, out healthText, out _);

        // ---- ability slots, bottom-centre (not active in this milestone)
        string[] keys = { "Q", "G" };
        string[] icons = { "icon_burst", "icon_grenade" };
        for (int i = 0; i < Abilities.SlotCount; i++)
        {
            float x = (i - 0.5f) * 96f;
            var slot = Img("Ability" + i, root, "slot", BC, BC, new Vector2(x, 70), new Vector2(72, 72), new Color(1, 1, 1, 0.9f));
            abilitySlot[i] = slot;
            abilityIcon[i] = Img("AbilityIcon" + i, slot.transform, icons[i], Center, Center, Vector2.zero, new Vector2(42, 42), new Color(1, 1, 1, 0.9f));
            // Dark shutter that drains from the top as the ability recharges, with the seconds left on it.
            abilityCool[i] = Img("AbilityCool" + i, slot.transform, "slot", Center, Center, Vector2.zero, new Vector2(72, 72), new Color(0f, 0.01f, 0.04f, 1f));
            abilityCool[i].type = Image.Type.Filled;
            abilityCool[i].fillMethod = Image.FillMethod.Vertical;
            abilityCool[i].fillOrigin = (int)Image.OriginVertical.Top;
            abilityCool[i].fillAmount = 0f;
            abilityTimer[i] = Label("AbilityTimer" + i, slot.transform, Center, Center, Vector2.zero, new Vector2(72, 40), 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            abilityTimer[i].text = "";
            var key = Img("AbilityKey" + i, root, "slot", BC, BC, new Vector2(x, 36), new Vector2(28, 28), new Color(1, 1, 1, 0.8f));
            abilityKey[i] = key;
            Label("AbilityKeyText" + i, key.transform, Center, Center, Vector2.zero, new Vector2(28, 28), 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).text = keys[i];
            if (i < 2) Img("AbilitySep" + i, root, "icon_plus", BC, Center, new Vector2(x + 48, 106), new Vector2(10, 10), new Color(1, 1, 1, 0.45f));
        }

        // ---- super meter, above the ability slots
        Img("SuperBack", root, null, BC, BC, new Vector2(0, 156), new Vector2(268, 9), BarBack);
        superFillImg = Img("SuperFill", root, null, BC, LeftMid, new Vector2(-132, 160.5f), new Vector2(0, 5), Cyan);
        superFill = superFillImg.rectTransform;
        superLabel = Label("SuperLabel", root, BC, BC, new Vector2(0, 168), new Vector2(420, 22), 16, Soft, TextAnchor.LowerCenter, FontStyle.Bold);

        // ---- ammo, bottom-right
        var ammo = Img("AmmoPanel", root, "panel_ammo", BR, BR, new Vector2(-44, 40), new Vector2(330, 128), Color.white);
        var slash = Img("AmmoSlash", ammo.transform, null, LeftMid, Center, new Vector2(34, 0), new Vector2(4, 84), Cyan);
        slash.rectTransform.localRotation = Quaternion.Euler(0, 0, -24);
        ammoText = Label("Ammo", ammo.transform, BR, BR, new Vector2(-150, 40), new Vector2(150, 80), 76, Color.white, TextAnchor.LowerRight, FontStyle.Bold);
        reserveText = Label("Reserve", ammo.transform, BR, BL, new Vector2(-142, 50), new Vector2(130, 44), 34, Soft, TextAnchor.LowerLeft);
        weaponText = Label("Weapon", ammo.transform, BR, BR, new Vector2(-22, 18), new Vector2(290, 26), 19, Color.white, TextAnchor.LowerRight);
        otherWeaponText = Label("OtherWeapon", ammo.transform, new Vector2(1, 1), BR, new Vector2(-8, 8), new Vector2(360, 22), 16, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight);
        Img("ReloadBack", ammo.transform, null, BL, BL, new Vector2(40, 8), new Vector2(268, 4), BarBack);
        reloadFill = Img("ReloadFill", ammo.transform, null, BL, BL, new Vector2(40, 8), new Vector2(0, 4), Cyan).rectTransform;

        // ---- crosshair
        var crossRoot = Node("Crosshair", root, Center, Center, Vector2.zero, Vector2.zero);
        crossGroup = crossRoot.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < 4; i++)
            cross[i] = Img("Cross" + i, crossRoot, null, Center, Center, Vector2.zero, i < 2 ? new Vector2(11, 2) : new Vector2(2, 11), Color.white).rectTransform;
        Img("CrossDot", crossRoot, "disc", Center, Center, Vector2.zero, new Vector2(4, 4), Color.white);
        for (int i = 0; i < 4; i++)
        {
            hitTicks[i] = Img("Hit" + i, root, null, Center, Center, Vector2.zero, new Vector2(3, 14), new Color(1, 1, 1, 0));
            hitTicks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 45 + 90 * i);
        }
        Img("CrossReloadBack", root, null, Center, Center, new Vector2(0, -44), new Vector2(64, 4), new Color(0, 0, 0, 0));
        crossReload = Img("CrossReload", root, null, Center, LeftMid, new Vector2(-32, -44), new Vector2(0, 4), Cyan).rectTransform;

        // ---- damage direction arcs
        for (int i = 0; i < arcs.Length; i++)
        {
            arcs[i].img = Img("DamageArc" + i, root, "damage_arc", Center, Center, Vector2.zero, new Vector2(620, 620), new Color(1f, 0.2f, 0.12f, 0f));
            arcs[i].rt = arcs[i].img.rectTransform;
            arcs[i].time = -99f;
        }

        // ---- score (top right) and kill callouts (beside the crosshair)
        scoreText = Label("Score", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -44), new Vector2(320, 30), 22, Color.white, TextAnchor.UpperRight, FontStyle.Bold);
        for (int i = 0; i < calloutText.Length; i++)
        {
            calloutText[i] = Label("Callout" + i, root, Center, LeftMid, new Vector2(150, 0), new Vector2(460, 32), 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            calloutText[i].text = "";
        }

        // ---- mission bar under the compass: uplink progress, then the boss's health
        var mission = Node("Mission", root, TC, TC, new Vector2(0, -108), new Vector2(MissionBarWidth, 44));
        missionGroup = mission.gameObject.AddComponent<CanvasGroup>();
        missionGroup.alpha = 0f;
        missionLeft = Label("MissionLeft", mission, BL, BL, new Vector2(0, 16), new Vector2(400, 24), 20, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
        missionRight = Label("MissionRight", mission, BR, BR, new Vector2(0, 16), new Vector2(300, 24), 18, Soft, TextAnchor.LowerRight);
        Img("MissionBack", mission, null, BL, BL, new Vector2(-2, 0), new Vector2(MissionBarWidth + 4, 12), BarBack);
        missionGhost = Img("MissionGhost", mission, null, BL, BL, new Vector2(0, 2), new Vector2(0, 8), Color.white).rectTransform;
        missionFillImg = Img("MissionFill", mission, null, BL, BL, new Vector2(0, 2), new Vector2(0, 8), Cyan);
        missionFill = missionFillImg.rectTransform;
        bannerText = Label("Banner", root, Center, Center, new Vector2(0, 175), new Vector2(1600, 60), 42, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        bannerText.text = "";

        hintText = Label("Hint", root, BC, BC, new Vector2(0, 214), new Vector2(1400, 30), 21, Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
        hintText.text = "";

        // ---- messages
        centerText = Label("Center", root, Center, Center, new Vector2(0, 150), new Vector2(1400, 80), 62, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        subText = Label("Sub", root, Center, Center, new Vector2(0, 92), new Vector2(1400, 40), 26, Cream, TextAnchor.MiddleCenter);
        centerText.text = subText.text = "";

        var intro = Node("Intro", root, Center, Center, new Vector2(0, 250), new Vector2(1200, 140));
        introGroup = intro.gameObject.AddComponent<CanvasGroup>();
        Img("IntroLine", intro, null, Center, Center, new Vector2(0, -2), new Vector2(420, 2), Cyan);
        introTitle = Label("IntroTitle", intro, Center, BC, new Vector2(0, 8), new Vector2(1200, 60), 54, Color.white, TextAnchor.LowerCenter, FontStyle.Bold);
        introSub = Label("IntroSub", intro, Center, TC, new Vector2(0, -12), new Vector2(1200, 34), 22, Cream, TextAnchor.UpperCenter);
        introTitle.text = Spaced("LUMINOUS TIDES");
        introSub.text = Spaced("SURVEY ZONE 7  -  TIDAL FLATS");
    }

    // ------------------------------------------------------------------ per-frame
    void Update()
    {
        float dt = Time.deltaTime;
        var gm = GameManager.Instance;
        UpdateBars(dt);
        UpdateAmmo(dt);
        UpdateCrosshair(dt);
        UpdateNavigation(gm);
        UpdateFeedback(dt);
        UpdateEnemyBars(dt);
        UpdateAbilities(dt);
        UpdateMission(dt);
        UpdateScore(gm, dt);
        UpdateHints(gm);
        UpdateMessages(gm);
    }

    void UpdateBars(float dt)
    {
        float sf = playerHealth.maxShield > 0 ? playerHealth.Shield / playerHealth.maxShield : 0f;
        float hf = playerHealth.Current / playerHealth.maxHealth;
        shieldShown = Mathf.MoveTowards(shieldShown, sf, dt * 2.5f);
        healthShown = Mathf.MoveTowards(healthShown, hf, dt * 2.5f);
        // The pale "ghost" bar lingers, then drains, to show how much was just lost.
        shieldGhostV = sf >= shieldGhostV ? sf : Mathf.MoveTowards(shieldGhostV, sf, dt * 0.45f);
        healthGhostV = hf >= healthGhostV ? hf : Mathf.MoveTowards(healthGhostV, hf, dt * 0.45f);
        shieldFill.sizeDelta = new Vector2(BarWidth * shieldShown, 9);
        healthFill.sizeDelta = new Vector2(BarWidth * healthShown, 9);
        shieldGhost.sizeDelta = new Vector2(BarWidth * shieldGhostV, 9);
        healthGhost.sizeDelta = new Vector2(BarWidth * healthGhostV, 9);

        shieldFlash = Mathf.MoveTowards(shieldFlash, 0f, dt * 1.5f);
        bool broken = sf <= 0f;
        float blink = broken ? 0.5f + 0.5f * Mathf.Sin(Time.time * 9f) : 0f;
        shieldIcon.color = Color.Lerp(Cyan, Red, broken ? 0.4f + 0.6f * blink : shieldFlash);
        shieldFillImg.color = Color.Lerp(Cyan, Color.white, sf < 1f && sf > shieldShown - 0.001f && sf > 0f ? 0.25f + 0.2f * Mathf.Sin(Time.time * 7f) : 0f);
        bool low = hf < 0.35f;
        healthFillImg.color = low ? Color.Lerp(Red, Cream, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : Cream;

        shieldText.text = "SHIELD  " + Mathf.CeilToInt(playerHealth.Shield);
        healthText.text = "HEALTH  " + Mathf.CeilToInt(playerHealth.Current);
    }

    void UpdateAmmo(float dt)
    {
        if (weapon != shownWeapon) { shownWeapon = weapon; lastMag = -1; lastReserve = -1; }     // swapped: no false "shot fired" pop
        if (arsenal.weapons.Length > 1)
        {
            int other = (arsenal.CurrentIndex + 1) % arsenal.weapons.Length;
            var o = arsenal.weapons[other];
            otherWeaponText.text = "[" + (other + 1) + "]   " + o.weaponName + "   " + o.Magazine + " / " + o.Reserve;
        }
        else otherWeaponText.text = "";
        if (weapon.Magazine != lastMag)
        {
            if (lastMag >= 0 && weapon.Magazine < lastMag) ammoPunch = 1f;
            lastMag = weapon.Magazine;
        }
        ammoPunch = Mathf.MoveTowards(ammoPunch, 0f, dt * 9f);
        bool low = weapon.Magazine <= 6;
        ammoText.text = weapon.Magazine.ToString();
        ammoText.color = low ? Color.Lerp(Orange, Red, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : Color.white;
        ammoText.rectTransform.localScale = Vector3.one * (1f + 0.07f * ammoPunch);
        if (lastReserve >= 0 && weapon.Reserve > lastReserve && !weapon.Reloading) reservePunch = 1f;     // picked up ammo
        lastReserve = weapon.Reserve;
        reservePunch = Mathf.MoveTowards(reservePunch, 0f, dt * 1.8f);
        reserveText.text = "/ " + weapon.Reserve;
        reserveText.color = weapon.Reserve == 0 ? Red : Color.Lerp(Soft, Cyan, reservePunch);
        reserveText.rectTransform.localScale = Vector3.one * (1f + 0.25f * reservePunch);
        weaponText.text = Spaced(weapon.Reloading ? "RELOADING" : weapon.weaponName);
        weaponText.color = weapon.Reloading ? Cyan : Color.white;
        float rp = weapon.Reloading ? weapon.ReloadProgress : 0f;
        reloadFill.sizeDelta = new Vector2(268f * rp, 4);
        crossReload.sizeDelta = new Vector2(64f * rp, 4);
    }

    void UpdateCrosshair(float dt)
    {
        // The gap tracks the weapon's real spread; the crosshair hands over to the sight reticle when aiming.
        float gap = 2f + weapon.CurrentSpread * 9f;
        crossGroup.alpha = weapon.hasSightReticle ? Mathf.Clamp01(1f - weapon.Aim * 1.6f) : 1f;
        cross[0].anchoredPosition = new Vector2(-gap - 6, 0);
        cross[1].anchoredPosition = new Vector2(gap + 6, 0);
        cross[2].anchoredPosition = new Vector2(0, gap + 6);
        cross[3].anchoredPosition = new Vector2(0, -gap - 6);

        bool killMark = weapon.LastHitKilled;
        hitPunch = Mathf.MoveTowards(hitPunch, 0f, dt * (killMark ? 2.6f : 5f));       // the kill marker lingers
        var hc = killMark ? Red : weapon.LastHitPrecision ? Yellow : Color.white;
        hc.a = Mathf.Clamp01(hitPunch * (killMark ? 2.5f : 1.5f));
        // It snaps in tight on the hit and springs outward; a kill throws it wider and draws it heavier.
        float outT = 1f - hitPunch;
        float d = killMark ? 12f + 22f * (1f - (1f - outT) * (1f - outT)) : 11f + 10f * outT;
        var tick = killMark ? new Vector2(5f, 22f) : weapon.LastHitPrecision ? new Vector2(4f, 17f) : new Vector2(3f, 14f);
        for (int i = 0; i < 4; i++)
        {
            float a = (45 + 90 * i) * Mathf.Deg2Rad;
            hitTicks[i].color = hc;
            hitTicks[i].rectTransform.sizeDelta = tick;
            hitTicks[i].rectTransform.anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * d;
        }
    }

    Text prompt;
    CanvasGroup areaGroup;
    Text areaName, areaRegion;
    RectTransform areaRule;

    /// The name of the area just walked into: it fades up in the middle of the screen, holds, and fades away.
    void UpdateArea()
    {
        // What E would do, when standing at one of the test range's pads.
        string ask = RangeDirector.Instance != null ? RangeDirector.Instance.Prompt : null;
        if (prompt != null) prompt.text = ask ?? "";
        var world = WorldDirector.Instance;
        if (world == null || areaGroup == null) return;
        if (Time.time - world.RescuedTime < 3f)
        {
            areaName.text = Spaced("THE TIDE CARRIES YOU BACK");
            areaRegion.text = "THE WATER IS TOO DEEP TO CROSS";
            areaRule.sizeDelta = new Vector2(420, 2);
            areaGroup.alpha = Mathf.Clamp01(3f - (Time.time - world.RescuedTime)) ;
            return;
        }
        float age = Time.time - world.EnteredTime;
        if (world.Current == null || age > 5f || age < 0f) { areaGroup.alpha = 0f; return; }
        areaName.text = Spaced(world.Current.name);
        areaRegion.text = (world.EnteredFirstTime ? "AREA DISCOVERED   -   " : "") + world.Current.region;
        areaRule.sizeDelta = new Vector2(Mathf.Lerp(0f, 460f, Mathf.SmoothStep(0f, 1f, age / 0.7f)), 2);
        areaGroup.alpha = Mathf.Clamp01(age / 0.5f) * Mathf.Clamp01((5f - age) / 1.2f);
    }

    void UpdateNavigation(GameManager gm)
    {
        UpdateArea();
        float yaw = player.Yaw;
        Vector3 pp = player.transform.position;

        // Minimap: the map rotates around the player so "up" is always forward.
        mapRoot.localRotation = Quaternion.Euler(0, 0, yaw);
        mapImage.anchoredPosition = new Vector2(-(pp.x - WorldMap.CentreX), -(pp.z - WorldMap.MinZ)) * MapScale;
        var all = EnemyAI.All;
        while (mapDots.Count < all.Count)
        {
            var dot = Img("EnemyDot", mapRoot, "disc", Center, Center, Vector2.zero, new Vector2(9, 9), Orange);
            mapDots.Add(dot.rectTransform);
            mapDotImages.Add(dot);
        }
        for (int i = 0; i < mapDots.Count; i++)
        {
            var e = i < all.Count ? all[i] : null;
            bool show = e != null && !e.IsDead && (e.transform.position - pp).sqrMagnitude < 46f * 46f;
            mapDots[i].gameObject.SetActive(show);
            if (!show) continue;
            mapDots[i].anchoredPosition = new Vector2(e.transform.position.x - pp.x, e.transform.position.z - pp.z) * MapScale;
            mapDots[i].sizeDelta = Vector2.one * (e.kind == EnemyKind.Boss ? 17f : e.elite ? 12f : 9f);
            mapDotImages[i].color = e.kind == EnemyKind.Boss ? Red : e.elite ? EliteBar : Orange;
        }

        // Compass: 2 * CompassHalfWidth px spans 120 degrees; labels fade toward the ends.
        for (int i = 0; i < compassTicks.Count; i++)
        {
            float d = Mathf.DeltaAngle(yaw, compassTickAngles[i]);
            bool on = Mathf.Abs(d) < 60f;
            compassTicks[i].gameObject.SetActive(on);
            if (on) compassTicks[i].anchoredPosition = new Vector2(d / 60f * CompassHalfWidth, compassTicks[i].anchoredPosition.y);
        }

        if (gm == null || gm.objective == null) return;
        Vector3 to = gm.objective.position - pp;
        float dist = new Vector2(to.x, to.z).magnitude;
        objectiveText.text = gm.objectiveText;
        distanceText.text = Mathf.RoundToInt(dist) + " m";

        float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float rel = Mathf.DeltaAngle(yaw, bearing);
        compassObjective.anchoredPosition = new Vector2(Mathf.Clamp(rel, -60f, 60f) / 60f * CompassHalfWidth, 11);

        float r = Mathf.Min(dist * MapScale, MapRadius - 4f);
        mapObjective.anchoredPosition = new Vector2(Mathf.Sin(rel * Mathf.Deg2Rad), Mathf.Cos(rel * Mathf.Deg2Rad)) * r;

        // World-space marker, clamped to the screen edge and hidden when it would sit on the crosshair.
        Vector3 vp = cam.WorldToViewportPoint(gm.objective.position + Vector3.up * 7f);
        if (vp.z < 0f) { vp.x = 1f - vp.x; vp.y = 0.08f; }
        var a = new Vector2(Mathf.Clamp(vp.x, 0.04f, 0.96f), Mathf.Clamp(vp.y, 0.1f, 0.86f));
        marker.anchorMin = marker.anchorMax = a;
        markerDist.text = Mathf.RoundToInt(dist) + " m";
        marker.gameObject.SetActive(dist > 9f);
    }

    void UpdateFeedback(float dt)
    {
        float hf = playerHealth.Current / playerHealth.maxHealth;
        vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, 0f, dt * 0.9f);
        float lowHealth = hf < 0.35f ? 0.4f + 0.15f * Mathf.Sin(Time.time * 5f) : 0f;
        var vc = vignette.color; vc.a = Mathf.Max(vignetteAlpha, lowHealth); vignette.color = vc;

        for (int i = 0; i < arcs.Length; i++)
        {
            float age = Time.time - arcs[i].time;
            float alpha = Mathf.Clamp01(1f - age / 1.3f);
            var c = arcs[i].img.color; c.a = alpha * 0.9f; arcs[i].img.color = c;
            if (alpha <= 0f) continue;
            Vector3 to = arcs[i].source - player.transform.position;
            float rel = Mathf.DeltaAngle(player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            arcs[i].rt.localRotation = Quaternion.Euler(0, 0, -rel);
        }

        var canvasSize = root.rect.size;
        for (int i = 0; i < numbers.Length; i++)
        {
            float age = Time.time - numbers[i].time;
            bool on = age < 0.7f;
            Vector3 vp = on ? cam.WorldToViewportPoint(numbers[i].world + Vector3.up * age * 1.2f) : Vector3.zero;
            on &= vp.z > 0f;
            numbers[i].text.enabled = on;
            if (!on) continue;
            numbers[i].text.rectTransform.anchoredPosition = new Vector2(vp.x * canvasSize.x, vp.y * canvasSize.y);
            var c = numbers[i].crit ? Yellow : numbers[i].kill ? Orange : Color.white;
            c.a = Mathf.Clamp01(1.6f - age / 0.45f);
            numbers[i].text.color = c;
            numbers[i].text.rectTransform.localScale = Vector3.one * (numbers[i].kill || numbers[i].crit ? 1.35f : 1f) * Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(age * 8f));
        }
    }

    /// Enemy health bars: the name left-aligned above a flat bar coloured by rank (red = minor, yellow = elite).
    /// Damage just taken stays white for a moment before draining. Shown while targeted or recently hurt.
    /// Bars are created as enemies appear, so reinforcements get them too. The boss uses the mission bar instead.
    EnemyBar CreateEnemyBar(EnemyAI e)
    {
        var left = new Vector2(-EnemyBarWidth / 2, 0);
        var bar = new Vector2(EnemyBarWidth, EnemyBarHeight);
        var b = new EnemyBar { enemy = e };
        b.rt = Node("EnemyBar", enemyBarLayer, BL, Center, Vector2.zero, new Vector2(EnemyBarWidth, 30));
        b.group = b.rt.gameObject.AddComponent<CanvasGroup>();
        b.group.alpha = 0f;
        Img("Back", b.rt, null, Center, LeftMid, left, bar, new Color(0f, 0f, 0f, 0.55f));
        b.ghost = Img("Ghost", b.rt, null, Center, LeftMid, left, bar, Color.white).rectTransform;
        b.fill = Img("Fill", b.rt, null, Center, LeftMid, left, bar, e.elite ? EliteBar : MinorBar).rectTransform;
        float nameX = -EnemyBarWidth / 2;
        if (e.elite)
        {
            Img("Rank", b.rt, "diamond", Center, LeftMid, new Vector2(nameX, 15), new Vector2(12, 12), EliteBar);
            nameX += 17;
        }
        var name = Label("Name", b.rt, Center, new Vector2(0, 0), new Vector2(nameX, 5), new Vector2(300, 20), 17, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
        name.text = e.displayName;
        return b;
    }

    void UpdateEnemyBars(float dt)
    {
        EnemyAI aimed = null;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 70f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            aimed = hit.collider.GetComponentInParent<EnemyAI>();
        foreach (var e in EnemyAI.All)
            if (e.kind != EnemyKind.Boss && !enemyBars.ContainsKey(e)) enemyBars[e] = CreateEnemyBar(e);

        var size = root.rect.size;
        staleBars.Clear();
        foreach (var pair in enemyBars)
        {
            var e = pair.Key;
            var b = pair.Value;
            if (e == null || e.IsDead)
            {
                Destroy(b.rt.gameObject);
                staleBars.Add(e);
                continue;
            }
            Vector3 vp = cam.WorldToViewportPoint(e.BarAnchor);
            bool want = vp.z > 0.2f && (e == aimed || Time.time - e.LastDamageTime < 3.5f);
            b.shown = Mathf.MoveTowards(b.shown, want ? 1f : 0f, dt * (want ? 12f : 2.5f));
            b.group.alpha = b.shown;
            if (b.shown <= 0f || vp.z <= 0.2f) continue;

            float frac = Mathf.Clamp01(e.Health.Current / e.Health.maxHealth);
            if (frac < b.lastFrac) b.ghostHold = 0.28f;           // hold the white chunk, then drain it
            b.lastFrac = frac;
            b.ghostHold -= dt;
            if (frac >= b.ghostV) b.ghostV = frac;
            else if (b.ghostHold <= 0f) b.ghostV = Mathf.MoveTowards(b.ghostV, frac, dt * 1.6f);
            b.fill.sizeDelta = new Vector2(EnemyBarWidth * frac, EnemyBarHeight);
            b.ghost.sizeDelta = new Vector2(EnemyBarWidth * b.ghostV, EnemyBarHeight);
            b.rt.anchoredPosition = new Vector2(vp.x * size.x, vp.y * size.y);
            b.rt.localScale = Vector3.one * Mathf.Clamp(14f / vp.z, 0.62f, 1.1f);
        }
        foreach (var e in staleBars) enemyBars.Remove(e);
    }

    /// The bar under the compass (uplink charge, then boss health) and the headline shown when a new part begins.
    void UpdateMission(float dt)
    {
        var md = MissionDirector.Instance;
        bool show = false;
        float frac = 0f;
        Color colour = Cyan;
        string left = "", right = "";
        if (md != null)
        {
            if (md.Current == MissionDirector.Phase.Uplink && md.UplinkStarted)
            {
                show = true;
                frac = md.UplinkProgress;
                left = "UPLINK   " + Mathf.FloorToInt(frac * 100f) + "%";
                right = md.PlayerOnPad ? "WAVE " + Mathf.Max(1, md.Wave) + " / " + md.WaveCount : "RETURN TO THE PAD";
                colour = md.PlayerOnPad ? Cyan : Color.Lerp(Orange, Red, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f));
            }
            else if (md.Current == MissionDirector.Phase.Boss && md.Boss != null && !md.Boss.IsDead)
            {
                show = true;
                frac = Mathf.Clamp01(md.Boss.Health.Current / md.Boss.Health.maxHealth);
                left = Spaced(md.bossName);
                colour = new Color(1f, 0.5f, 0.12f);
            }
            float age = Time.time - md.BannerTime;
            bool won = GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Won;
            bannerText.text = age < 4.2f && !won ? Spaced(md.Banner) : "";      // never over the mission-complete headline
            var bc = Color.white; bc.a = Mathf.Clamp01(age / 0.35f) * Mathf.Clamp01((4f - age) / 0.8f);
            bannerText.color = bc;
        }
        missionShown = Mathf.MoveTowards(missionShown, show ? 1f : 0f, dt * 4f);
        missionGroup.alpha = missionShown;
        if (!show) { missionGhostV = 0f; return; }
        missionGhostV = frac >= missionGhostV ? frac : Mathf.MoveTowards(missionGhostV, frac, dt * 0.25f);
        missionFill.sizeDelta = new Vector2(MissionBarWidth * frac, 8);
        missionGhost.sizeDelta = new Vector2(MissionBarWidth * missionGhostV, 8);
        missionFillImg.color = colour;
        missionLeft.text = left;
        missionRight.text = right;
    }

    void UpdateAbilities(float dt)
    {
        if (abilities == null) return;
        float charge = abilities.SuperCharge;
        bool surgeReady = abilities.SuperReady;
        float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
        superFill.sizeDelta = new Vector2(264f * charge, 5);
        superFillImg.color = surgeReady ? Color.Lerp(Cyan, Color.white, beat) : new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f);
        superLabel.text = surgeReady ? Spaced("LIGHTNING WAVE READY") + "     [F]" : "LIGHTNING WAVE   " + Mathf.FloorToInt(charge * 100f) + "%";
        superLabel.color = surgeReady ? Color.Lerp(Cyan, Color.white, beat) : new Color(1, 1, 1, 0.55f);
        for (int i = 0; i < Abilities.SlotCount; i++)
        {
            float ready = abilities.Ready01(i);
            bool isReady = ready >= 1f;
            if (isReady && !abilityWasReady[i]) abilityFlash[i] = 1f;         // pop when it comes back
            abilityWasReady[i] = isReady;
            abilityFlash[i] = Mathf.MoveTowards(abilityFlash[i], 0f, dt * 2.5f);

            abilityCool[i].fillAmount = 1f - ready;
            float left = abilities.Remaining(i);
            abilityTimer[i].text = isReady ? "" : left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0");
            abilityIcon[i].color = isReady ? Color.Lerp(new Color(1, 1, 1, 0.92f), Cyan, abilityFlash[i]) : new Color(1, 1, 1, 0.3f);
            abilitySlot[i].color = Color.Lerp(new Color(1, 1, 1, isReady ? 0.95f : 0.55f), Cyan, abilityFlash[i]);
            abilitySlot[i].rectTransform.localScale = Vector3.one * (1f + 0.14f * abilityFlash[i]);
            abilityKey[i].color = new Color(1, 1, 1, isReady ? 0.85f : 0.4f);
        }
    }

    void OnCallout(string text, int points)
    {
        // A running streak updates its own line instead of adding a new one for every kill.
        int last = (nextCallout - 1 + calloutText.Length) % calloutText.Length;
        bool streakLine = text.StartsWith("RAMPAGE");
        if (streakLine && lastCalloutWasStreak && Time.time - calloutTime[last] < 2.4f)
        {
            calloutText[last].text = Spaced(text) + "     +" + points;
            calloutTime[last] = Time.time;
            return;
        }
        lastCalloutWasStreak = streakLine;
        calloutText[nextCallout].text = Spaced(text) + "     +" + points;
        calloutTime[nextCallout] = Time.time;
        nextCallout = (nextCallout + 1) % calloutText.Length;
    }

    void UpdateScore(GameManager gm, float dt)
    {
        if (gm == null) return;
        // The score counts up toward its real value rather than jumping.
        shownScore = (int)Mathf.MoveTowards(shownScore, gm.Score, Mathf.Max(60f, (gm.Score - shownScore) * 4f) * dt + 1f);
        scoreText.text = "SCORE   " + shownScore.ToString("N0");

        // Newest callout nearest the crosshair; older ones slide up and fade.
        for (int i = 0; i < calloutText.Length; i++)
        {
            float age = Time.time - calloutTime[i];
            bool on = age < 2.4f && gm.State != GameManager.GameState.Won;       // never over the mission-complete screen
            calloutText[i].enabled = on;
            if (!on) continue;
            int rank = (nextCallout - 1 - i + calloutText.Length * 2) % calloutText.Length;      // 0 = newest
            var rt = calloutText[i].rectTransform;
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(170, -70 - rank * 36), 14f * dt);
            rt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(age * 7f));
            var c = Color.Lerp(Orange, Color.white, Mathf.Clamp01(age * 3f));
            c.a = Mathf.Clamp01((2.4f - age) / 0.6f);
            calloutText[i].color = c;
        }
    }

    bool Hint(string id, string text, float seconds = 7f)
    {
        if (!hintsShown.Add(id)) return false;
        hintText.text = text;
        hintShownAt = Time.time;
        hintDuration = seconds;
        return true;
    }

    void UpdateHints(GameManager gm)
    {
        float age = Time.time - hintShownAt;
        var c = Cream; c.a = Mathf.Clamp01(age / 0.4f) * Mathf.Clamp01((hintDuration - age) / 0.8f);
        hintText.color = c;
        if (gm == null || gm.State != GameManager.GameState.Playing || InputGate.Blocked) return;
        if (age < hintDuration + 1.5f || Time.time < nextHintCheck) return;       // one at a time, with a gap
        nextHintCheck = Time.time + 0.5f;

        if (Time.timeSinceLevelLoad > 5.5f && Hint("move", "SHIFT  sprint          SPACE twice  double jump          CTRL while sprinting  slide")) return;
        bool hunted = false;
        foreach (var e in EnemyAI.All) if (e != null && e.IsAlerted) { hunted = true; break; }
        if (hunted && Hint("fight", "RIGHT MOUSE  aim          Q  melee          G  grenade          1 / 2 / 3  swap weapon", 8f)) return;
        if (abilities != null && abilities.SuperReady && Hint("super", "LIGHTNING WAVE IS CHARGED   -   PRESS  F")) return;
        if (weapon.Magazine == 0 && weapon.Reserve == 0 && Hint("ammo", "OUT OF AMMO   -   SWAP WEAPONS WITH 1 / 2, OR WALK OVER THE GLOWING DROPS")) return;
        if (pods == null) pods = FindObjectsByType<Volatile>(FindObjectsSortMode.None);
        foreach (var pod in pods)
            if (pod != null && (pod.transform.position - player.transform.position).sqrMagnitude < 15f * 15f
                && Hint("pods", "SHOOT THE GLOWING ORANGE PODS   -   THEY BLOW UP ANYTHING NEAR THEM")) return;
    }

    void UpdateMessages(GameManager gm)
    {
        float t = Time.timeSinceLevelLoad;
        introGroup.alpha = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((5f - t) / 1.2f);
        if (gm == null) return;
        switch (gm.State)
        {
            case GameManager.GameState.Dead:
                centerText.text = Spaced("SURVEYOR DOWN"); centerText.color = Red; subText.text = "Respawning..."; break;
            case GameManager.GameState.Won:
                centerText.text = Spaced("MISSION COMPLETE"); centerText.color = Cyan;
                subText.text = "RANK  " + gm.Rank + "          SCORE  " + gm.Score.ToString("N0") + "          KILLS  " + gm.Kills +
                               "          TIME  " + Mathf.FloorToInt(gm.MissionTime / 60f) + ":" + Mathf.FloorToInt(gm.MissionTime % 60f).ToString("00") +
                               "          DEATHS  " + gm.Deaths; break;
            default:
                centerText.text = ""; subText.text = ""; break;
        }
    }
}
