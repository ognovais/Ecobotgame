using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reduz variantes de materiais para o executável empacotado pelo Eco-Bot.</summary>
public sealed class EcoShaderFilter : IPreprocessShaders
{
    private const string Lit = "Universal Render Pipeline/Lit";
    private const string Simple = "Universal Render Pipeline/Simple Lit";
    private static readonly string[] LitVertex = { "_NORMALMAP", "_PARALLAXMAP", "_RECEIVE_SHADOWS_OFF", "_DETAIL_MULX2", "_DETAIL_SCALED" };
    private static readonly string[] LitFragment = LitVertex.Concat(new[]
    {
        "_SURFACE_TYPE_TRANSPARENT", "_ALPHATEST_ON", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON", "_EMISSION",
        "_METALLICSPECGLOSSMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", "_OCCLUSIONMAP",
        "_SPECULARHIGHLIGHTS_OFF", "_ENVIRONMENTREFLECTIONS_OFF", "_SPECULAR_SETUP"
    }).ToArray();
    private static readonly string[] SimpleFeatures =
    {
        "_NORMALMAP", "_RECEIVE_SHADOWS_OFF", "_SURFACE_TYPE_TRANSPARENT", "_ALPHATEST_ON", "_ALPHAPREMULTIPLY_ON",
        "_ALPHAMODULATE_ON", "_EMISSION", "_SPECGLOSSMAP", "_SPECULAR_COLOR", "_GLOSSINESS_FROM_BASE_ALPHA"
    };

    private static readonly HashSet<string> UnusedSceneFeatures = new HashSet<string>
    {
        "DYNAMICLIGHTMAP_ON", "FOG_EXP", "FOG_EXP2", "EVALUATE_SH_VERTEX", "EVALUATE_SH_MIXED",
        "_LIGHT_COOKIES", "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION",
        "_FORWARD_PLUS", "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3",
        "_LIGHT_LAYERS", "_SCREEN_SPACE_OCCLUSION", "DEBUG_DISPLAY"
    };
    private static Material[] sceneMaterials;
    public int callbackOrder => 100;

    /// <summary>Indexa os materiais das cenas que entrarão no build, inclusive materiais de modelos importados.</summary>
    public static void Prepare(string[] scenes)
    {
        sceneMaterials = AssetDatabase.GetDependencies(scenes)
            .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Material>().ToArray();
    }

    /// <summary>Libera a configuração após o build para que outros builds usem a política padrão.</summary>
    public static void Clear() => sceneMaterials = null;

    /// <summary>Mantém as combinações de recursos locais dos materiais usados e preserva as variantes de iluminação.</summary>
    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (snippet.passName != "ForwardLit" || (shader.name != Lit && shader.name != Simple))
            return;
        if (sceneMaterials == null)
        {
            if (!Environment.GetCommandLineArgs().Contains("EcoBuild.BuildWithCompatibleRendering"))
                return;
            Prepare(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray());
        }

        string[] candidates = shader.name == Lit
            ? (snippet.shaderType == ShaderType.Vertex ? LitVertex : LitFragment)
            : SimpleFeatures;
        var keywords = candidates.Where(name => shader.keywordSpace.FindKeyword(name).isValid)
            .Select(name => new ShaderKeyword(shader, name)).ToArray();
        string[] names = candidates.Where(name => shader.keywordSpace.FindKeyword(name).isValid).ToArray();
        if (shader.name == Simple && snippet.shaderType == ShaderType.Vertex)
        {
            names = new[] { "_NORMALMAP", "_RECEIVE_SHADOWS_OFF" };
            keywords = names.Select(name => new ShaderKeyword(shader, name)).ToArray();
        }
        if (keywords.Length == 0)
            return;

        var masks = new HashSet<int> { 0 };
        foreach (Material material in sceneMaterials)
        {
            if (material.shader != shader)
                continue;
            int mask = 0;
            for (int i = 0; i < names.Length; i++)
                if (material.IsKeywordEnabled(names[i]))
                    mask |= 1 << i;
            masks.Add(mask);
        }

        var remove = new List<int>();
        for (int j = 0; j < data.Count; j++)
        {
            int mask = 0;
            for (int i = 0; i < keywords.Length; i++)
                if (data[j].shaderKeywordSet.IsEnabled(keywords[i]))
                    mask |= 1 << i;
            if (!masks.Contains(mask) || data[j].shaderKeywordSet.GetShaderKeywords()
                .Any(keyword => UnusedSceneFeatures.Contains(keyword.name)))
                remove.Add(j);
        }
        if (remove.Count == data.Count)
            return;
        int originalCount = data.Count;
        for (int j = remove.Count - 1; j >= 0; j--)
            data.RemoveAt(remove[j]);
        Debug.Log($"ECOBOT_SHADER: {shader.name} {snippet.shaderType}, {originalCount} -> {data.Count}");
    }
}
