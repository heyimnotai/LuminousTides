using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Front end built at runtime: main menu, pause menu, settings, and the buttons on the mission-complete screen.
/// Escape pauses and resumes (or closes settings). Everything here runs on unscaled time, since the game is
/// frozen while a menu is open.
public class GameMenu : MonoBehaviour
{
    public static GameMenu Instance { get; private set; }

    static readonly Color Cyan = new Color(0.30f, 0.95f, 0.92f);
    static readonly Color Cream = new Color(0.98f, 0.93f, 0.82f);
    static readonly Color PanelColor = new Color(0.03f, 0.06f, 0.13f, 0.88f);
    static readonly Color ButtonColor = new Color(0.07f, 0.12f, 0.22f, 0.95f);
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f), TL = new Vector2(0, 1), BL = Vector2.zero, BR = new Vector2(1, 0),
                            TR = new Vector2(1, 1), LeftMid = new Vector2(0, 0.5f);

    Font font;
    RectTransform root;
    GameObject dim, mainPanel, pausePanel, settingsPanel, wonPanel;
    Text fpsText;
    Canvas hudCanvas;
    bool settingsOpen;
    float fpsSmoothed = 60f;

    void Awake()
    {
        Instance = this;
        GameSettings.Load();
        font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        root = (RectTransform)transform;
        Build();
    }

    void Start()
    {
        var hud = FindAnyObjectByType<HUD>();
        if (hud != null) hudCanvas = hud.GetComponent<Canvas>();
        GameSettings.Apply();
    }

    // ------------------------------------------------------------------ construction
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

    Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool raycast = false)
    {
        var rt = Node(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
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
        return t;
    }

    static string Spaced(string s) => string.Join(" ", s.ToCharArray()).Replace("   ", "    ");

    Button MakeButton(Transform parent, string text, Vector2 pos, Vector2 size, Action onClick)
    {
        var img = Box("Button " + text, parent, TL, TL, pos, size, ButtonColor, true);
        Box("Accent", img.transform, LeftMid, LeftMid, Vector2.zero, new Vector2(4, size.y), Cyan);
        var button = img.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 2.2f, 2.4f, 1f);
        colors.pressedColor = new Color(0.6f, 1.6f, 1.6f, 1f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1.6f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        button.targetGraphic = img;
        button.onClick.AddListener(() => onClick());
        var label = Label("Label", img.transform, LeftMid, LeftMid, new Vector2(26, 0), new Vector2(size.x - 40, size.y), 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        label.text = Spaced(text);
        return button;
    }

    /// A labelled slider row. `format` turns the value into the text shown on the right.
    void MakeSlider(Transform parent, string label, float y, float min, float max, float value, bool whole, Func<float, string> format, Action<float> onChange)
    {
        Label("Label " + label, parent, TL, TL, new Vector2(40, y), new Vector2(300, 36), 22, Color.white, TextAnchor.MiddleLeft).text = label;
        var valueText = Label("Value " + label, parent, TR, TR, new Vector2(-40, y), new Vector2(120, 36), 22, Cyan, TextAnchor.MiddleRight, FontStyle.Bold);
        valueText.text = format(value);

        var track = Node("Slider " + label, parent, TL, TL, new Vector2(330, y - 8), new Vector2(360, 20));
        var slider = track.gameObject.AddComponent<Slider>();
        var back = Box("Back", track, Center, Center, Vector2.zero, new Vector2(360, 6), new Color(1, 1, 1, 0.18f), true);
        var fillArea = Node("Fill Area", track, Center, Center, Vector2.zero, new Vector2(360, 6));
        var fill = Box("Fill", fillArea, Center, Center, Vector2.zero, Vector2.zero, Cyan);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var handleArea = Node("Handle Area", track, Center, Center, Vector2.zero, new Vector2(348, 20));
        var handle = Box("Handle", handleArea, Center, Center, Vector2.zero, new Vector2(12, 24), Color.white, true);
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = whole;
        slider.value = value;
        slider.onValueChanged.AddListener(v => { valueText.text = format(v); onChange(v); });
    }

    /// A labelled button that steps through a list of options.
    void MakeCycle(Transform parent, string label, float y, string[] options, int index, Action<int> onChange)
    {
        Label("Label " + label, parent, TL, TL, new Vector2(40, y), new Vector2(300, 36), 22, Color.white, TextAnchor.MiddleLeft).text = label;
        int current = index;
        Text caption = null;
        var button = MakeButton(parent, options[current], new Vector2(330, y + 2), new Vector2(360, 40), () =>
        {
            current = (current + 1) % options.Length;
            caption.text = Spaced(options[current]);
            onChange(current);
        });
        caption = button.GetComponentInChildren<Text>();
        caption.fontSize = 20;
    }

    void Build()
    {
        dim = Box("Dim", root, Center, Center, Vector2.zero, Vector2.zero, new Color(0f, 0.02f, 0.06f, 0.6f), true).gameObject;
        var dr = (RectTransform)dim.transform;
        dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;

        // ---- main menu
        mainPanel = Node("Main", root, TL, TL, new Vector2(140, -170), new Vector2(700, 760)).gameObject;
        Label("Title", mainPanel.transform, TL, TL, Vector2.zero, new Vector2(900, 90), 84, Color.white, TextAnchor.UpperLeft, FontStyle.Bold).text = Spaced("LUMINOUS TIDES");
        Box("TitleLine", mainPanel.transform, TL, TL, new Vector2(4, -104), new Vector2(520, 3), Cyan);
        Label("Subtitle", mainPanel.transform, TL, TL, new Vector2(4, -122), new Vector2(900, 30), 22, Cream, TextAnchor.UpperLeft).text = Spaced("SURVEY ZONE 7  -  TIDAL FLATS");
        MakeButton(mainPanel.transform, "DEMO LEVEL", new Vector2(4, -220), new Vector2(420, 58), () => GameManager.Instance.StartDemo());
        MakeButton(mainPanel.transform, "TEST RANGE", new Vector2(4, -290), new Vector2(420, 58), () => GameManager.Instance.StartRange());
        MakeButton(mainPanel.transform, "OPEN WORLD", new Vector2(4, -360), new Vector2(420, 58), () => GameManager.Instance.StartMission());
        MakeButton(mainPanel.transform, "SETTINGS", new Vector2(4, -430), new Vector2(420, 58), () => ShowSettings(true));
        MakeButton(mainPanel.transform, "QUIT", new Vector2(4, -500), new Vector2(420, 58), () => GameManager.Instance.QuitGame());
        var controls = Label("Controls", mainPanel.transform, TL, TL, new Vector2(4, -590), new Vector2(700, 240), 20, new Color(1, 1, 1, 0.8f), TextAnchor.UpperLeft);
        controls.lineSpacing = 1.25f;
        controls.text = "WASD  move        SHIFT  sprint        SPACE  jump, again in the air to double jump\n" +
                        "CTRL  crouch, or slide while sprinting\n" +
                        "LEFT MOUSE  fire        RIGHT MOUSE  aim        R  reload\n" +
                        "Q  melee (steps in and strikes)        G  grenade        F  lightning wave (when charged)\n" +
                        "1 / 2 / 3 or MOUSE WHEEL  swap weapon        ESC  pause\n" +
                        "Hold forward into a ledge while in the air to climb it        E  use a pad on the test range";

        // ---- pause menu
        pausePanel = Box("Pause", root, Center, Center, Vector2.zero, new Vector2(520, 470), PanelColor, true).gameObject;
        Label("PauseTitle", pausePanel.transform, TL, TL, new Vector2(40, -34), new Vector2(440, 50), 40, Color.white, TextAnchor.UpperLeft, FontStyle.Bold).text = Spaced("PAUSED");
        Box("PauseLine", pausePanel.transform, TL, TL, new Vector2(40, -92), new Vector2(180, 3), Cyan);
        MakeButton(pausePanel.transform, "RESUME", new Vector2(40, -124), new Vector2(440, 58), () => GameManager.Instance.Resume());
        MakeButton(pausePanel.transform, "SETTINGS", new Vector2(40, -194), new Vector2(440, 58), () => ShowSettings(true));
        MakeButton(pausePanel.transform, "RESTART MISSION", new Vector2(40, -264), new Vector2(440, 58), () => GameManager.Instance.RestartMission());
        MakeButton(pausePanel.transform, "QUIT TO MENU", new Vector2(40, -334), new Vector2(440, 58), () => GameManager.Instance.QuitToMenu());

        // ---- settings
        settingsPanel = Box("Settings", root, Center, Center, Vector2.zero, new Vector2(820, 620), PanelColor, true).gameObject;
        var sp = settingsPanel.transform;
        Label("SettingsTitle", sp, TL, TL, new Vector2(40, -34), new Vector2(600, 50), 40, Color.white, TextAnchor.UpperLeft, FontStyle.Bold).text = Spaced("SETTINGS");
        Box("SettingsLine", sp, TL, TL, new Vector2(40, -92), new Vector2(220, 3), Cyan);
        MakeSlider(sp, "MOUSE SENSITIVITY", -130, 0.5f, 6f, GameSettings.Sensitivity, false, v => v.ToString("0.0"), v => { GameSettings.Sensitivity = v; GameSettings.Apply(); });
        MakeSlider(sp, "FIELD OF VIEW", -190, 60f, 100f, GameSettings.Fov, true, v => Mathf.RoundToInt(v).ToString(), v => { GameSettings.Fov = v; GameSettings.Apply(); });
        MakeSlider(sp, "VOLUME", -250, 0f, 1f, GameSettings.Volume, false, v => Mathf.RoundToInt(v * 100f) + "%", v => { GameSettings.Volume = v; GameSettings.Apply(); });
        MakeCycle(sp, "GRAPHICS", -310, GameSettings.QualityNames, Mathf.Clamp(GameSettings.Quality, 0, 2), i => { GameSettings.Quality = i; GameSettings.Apply(); });
        MakeCycle(sp, "DISPLAY", -370, new[] { "WINDOWED", "FULLSCREEN" }, GameSettings.Fullscreen ? 1 : 0, i => { GameSettings.Fullscreen = i == 1; GameSettings.Apply(); });
        MakeCycle(sp, "FRAME RATE COUNTER", -430, new[] { "OFF", "ON" }, GameSettings.ShowFps ? 1 : 0, i => { GameSettings.ShowFps = i == 1; });
        MakeSlider(sp, "MUSIC", -490, 0f, 1f, GameSettings.MusicVolume, false, v => Mathf.RoundToInt(v * 100f) + "%", v => { GameSettings.MusicVolume = v; });
        MakeButton(sp, "BACK", new Vector2(40, -550), new Vector2(240, 50), () => ShowSettings(false));

        // ---- mission complete buttons (the headline itself is drawn by the HUD)
        wonPanel = Node("Won", root, Center, Center, new Vector2(0, -90), new Vector2(1160, 70)).gameObject;
        MakeButton(wonPanel.transform, "KEEP EXPLORING", new Vector2(0, 0), new Vector2(360, 62), () => GameManager.Instance.KeepExploring());
        MakeButton(wonPanel.transform, "PLAY AGAIN", new Vector2(400, 0), new Vector2(360, 62), () => GameManager.Instance.RestartMission());
        MakeButton(wonPanel.transform, "QUIT TO MENU", new Vector2(800, 0), new Vector2(360, 62), () => GameManager.Instance.QuitToMenu());

        fpsText = Label("Fps", root, TR, TR, new Vector2(-24, -16), new Vector2(200, 30), 20, Cyan, TextAnchor.UpperRight, FontStyle.Bold);
        Refresh();
    }

    // ------------------------------------------------------------------ behaviour
    public void ShowSettings(bool open)
    {
        settingsOpen = open;
        if (!open) GameSettings.Save();
        Refresh();
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        if (Input.GetKeyDown(KeyCode.Escape) && !InputGate.Blocked)
        {
            if (settingsOpen) ShowSettings(false);
            else if (gm.State == GameManager.GameState.Playing) gm.Pause();
            else if (gm.State == GameManager.GameState.Paused) gm.Resume();
        }
        Refresh();

        fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
        fpsText.text = GameSettings.ShowFps ? Mathf.RoundToInt(fpsSmoothed) + " FPS" : "";
    }

    void Refresh()
    {
        var gm = GameManager.Instance;
        var state = gm != null ? gm.State : GameManager.GameState.Menu;
        bool menu = state == GameManager.GameState.Menu, paused = state == GameManager.GameState.Paused, won = state == GameManager.GameState.Won;
        if (!menu && !paused) settingsOpen = false;
        mainPanel.SetActive(menu && !settingsOpen);
        pausePanel.SetActive(paused && !settingsOpen);
        settingsPanel.SetActive(settingsOpen);
        wonPanel.SetActive(won);
        dim.SetActive(menu || paused);
        if (hudCanvas != null) hudCanvas.enabled = !menu;
    }
}
