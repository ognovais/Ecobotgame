using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class EcoBuild
{
    /// <summary>Cria os assets de renderização na versão do Editor e compila as cenas já salvas.</summary>
    public static void BuildWithCompatibleRendering()
    {
        ConfigureRendering();
        foreach (var settings in EditorBuildSettings.scenes)
        {
            var scene = EditorSceneManager.OpenScene(settings.path);
            EcoDrone drone = Object.FindFirstObjectByType<EcoDrone>();
            var agent = drone.GetComponent<NavMeshAgent>();
            agent.enabled = false;
            EditorUtility.SetDirty(agent);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        BuildWindows();
    }

    /// <summary>Registra o Renderer, pipeline e configuração global nativos de URP 17.0.</summary>
    private static void ConfigureRendering()
    {
        Folder(Generated, "Rendering");
        const string dir = Generated + "/Rendering";
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(dir + "/EcoRenderer.asset");
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "Eco-Bot Renderer";
            AssetDatabase.CreateAsset(renderer, dir + "/EcoRenderer.asset");
            ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(renderer);
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(dir + "/EcoPipeline.asset");
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.name = "Eco-Bot URP 17.0";
            pipeline.shadowDistance = 53f;
            pipeline.maxAdditionalLightsCount = 4;
            AssetDatabase.CreateAsset(pipeline, dir + "/EcoPipeline.asset");
        }

        var rendering = new SerializedObject(pipeline);
        rendering.FindProperty("m_ShEvalMode").intValue = (int)ShEvalMode.PerPixel;
        rendering.FindProperty("m_SupportsLightCookies").boolValue = false;
        rendering.ApplyModifiedPropertiesWithoutUndo();

        const string settingsPath = dir + "/EcoGlobalSettings.asset";
        var global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(settingsPath);
        if (global == null)
            global = RenderPipelineGlobalSettingsUtils.Create(typeof(UniversalRenderPipeline).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true), settingsPath);
        EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(global);
        GraphicsSettings.defaultRenderPipeline = pipeline;
        int original = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(original, false);
        AssetDatabase.SaveAssets();
        Debug.Log("ECOBOT_RENDERING_OK: assets de URP 17.0 criados no Unity 6000.0.");
    }
}
