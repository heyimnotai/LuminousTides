using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Player-adjustable settings, saved between sessions with PlayerPrefs.
public static class GameSettings
{
    public static float Sensitivity = 2.2f;     // mouse look speed
    public static float Fov = 72f;              // hip field of view, degrees
    public static float Volume = 0.8f;          // master volume, 0..1
    public static float MusicVolume = 0.6f;     // soundtrack level within the master volume, 0..1
    public static int Quality = 2;              // 0 performance, 1 balanced, 2 quality
    public static bool Fullscreen = true;
    public static bool ShowFps;

    public static readonly string[] QualityNames = { "PERFORMANCE", "BALANCED", "QUALITY" };
    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Sensitivity = PlayerPrefs.GetFloat("lt.sensitivity", Sensitivity);
        Fov = PlayerPrefs.GetFloat("lt.fov", Fov);
        Volume = PlayerPrefs.GetFloat("lt.volume", Volume);
        MusicVolume = PlayerPrefs.GetFloat("lt.music", MusicVolume);
        Quality = PlayerPrefs.GetInt("lt.quality", Quality);
        Fullscreen = PlayerPrefs.GetInt("lt.fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        ShowFps = PlayerPrefs.GetInt("lt.showfps", 0) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("lt.sensitivity", Sensitivity);
        PlayerPrefs.SetFloat("lt.fov", Fov);
        PlayerPrefs.SetFloat("lt.volume", Volume);
        PlayerPrefs.SetFloat("lt.music", MusicVolume);
        PlayerPrefs.SetInt("lt.quality", Quality);
        PlayerPrefs.SetInt("lt.fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("lt.showfps", ShowFps ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// Pushes the current values into the running game.
    public static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(Volume);
        var gm = GameManager.Instance;
        if (gm != null && gm.player != null)
        {
            gm.player.mouseSensitivity = Sensitivity;
            foreach (var weapon in gm.player.GetComponentsInChildren<Weapon>(true))
            {
                weapon.hipFov = Fov;
                weapon.aimFov = Fov * weapon.aimZoomRatio;      // keep each weapon's zoom ratio when aiming
            }
        }
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            // One pipeline asset, scaled three ways: resolution, shadow reach and anti-aliasing.
            urp.renderScale = Quality == 0 ? 0.7f : Quality == 1 ? 0.85f : 1f;
            urp.shadowDistance = Quality == 0 ? 35f : Quality == 1 ? 60f : 90f;
            urp.msaaSampleCount = Quality == 0 ? 1 : Quality == 1 ? 2 : 4;
        }
        if (!InputGate.Blocked && Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
    }
}
