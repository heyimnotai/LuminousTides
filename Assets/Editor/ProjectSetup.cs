using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// One-time project configuration: URP pipeline asset, colour space, quality.
public static class ProjectSetup
{
    const string Dir = "Assets/Settings";
    /// Layer for the first-person weapon, which is drawn by its own fixed-FOV camera.
    public const int ViewModelLayer = 6;

    static void EnsureLayer(int index, string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layer = tagManager.FindProperty("layers").GetArrayElementAtIndex(index);
        if (layer.stringValue == name) return;
        layer.stringValue = name;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Luminous Tides/1. Setup Project")]
    public static void Run()
    {
        Directory.CreateDirectory(Dir);
        PlayerSettings.colorSpace = ColorSpace.Linear;
        EnsureLayer(ViewModelLayer, "ViewModel");
        PlayerSettings.productName = "Luminous Tides";
        PlayerSettings.companyName = "LuminousTides";

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Dir + "/URP_Asset.asset");
        if (urp == null)
        {
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, Dir + "/URP_Renderer.asset");
            ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            EditorUtility.SetDirty(data);

            urp = UniversalRenderPipelineAsset.Create(data);
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 4;
            urp.supportsCameraDepthTexture = true;
            urp.supportsCameraOpaqueTexture = true;
            AssetDatabase.CreateAsset(urp, Dir + "/URP_Asset.asset");
        }

        EnsureAmbientOcclusion(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Dir + "/URP_Renderer.asset"));
        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(current, false);

        AssetDatabase.SaveAssets();
        Debug.Log("[LT] Project setup complete. Pipeline: " + GraphicsSettings.defaultRenderPipeline);
    }

    /// Adds URP's screen-space ambient occlusion feature to the renderer (it is not public API, hence reflection).
    static void EnsureAmbientOcclusion(UniversalRendererData data)
    {
        const string typeName = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";
        if (data == null) return;
        data.rendererFeatures.RemoveAll(f => f == null);
        if (data.rendererFeatures.Exists(f => f.GetType().FullName == typeName)) return;
        try
        {
            var type = typeof(UniversalRendererData).Assembly.GetType(typeName);
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);

            var so = new SerializedObject(feature);
            void Set(string path, float v) { var p = so.FindProperty(path); if (p != null) p.floatValue = v; }
            Set("m_Settings.Intensity", 1.6f);
            Set("m_Settings.Radius", 0.6f);
            Set("m_Settings.DirectLightingStrength", 0.45f);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Rebuild the renderer's internal feature map so the new sub-asset is picked up.
            var validate = typeof(ScriptableRendererData).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            validate?.Invoke(data, null);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[LT] SSAO renderer feature added.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[LT] Could not add SSAO: " + e.Message);
        }
    }

    /// Builds a Windows player to Build/LuminousTides.exe.
    [MenuItem("Luminous Tides/4. Build Windows Player")]
    public static void BuildPlayer()
    {
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        // Direct3D 11: the D3D12 player intermittently crashed during shutdown on this project.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Build/LuminousTides.exe",
            target = BuildTarget.StandaloneWindows64,
        });
        Debug.Log("[LT] Player build: " + report.summary.result + " errors=" + report.summary.totalErrors);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }

    /// Batch entry point: configure, then build the level.
    public static void SetupAndBuild()
    {
        Run();
        LevelBuilder.Build();
    }
}
