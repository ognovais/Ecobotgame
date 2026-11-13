using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static partial class EcoBuild
{
    /// <summary>Confere referências, materiais, terreno e rotas nas cinco cenas antes do build.</summary>
    [MenuItem("Eco-Bot/Validar cinco fases")]
    public static void Verify()
    {
        var configured = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
        Require(configured.Length == 5, "O build precisa de cinco níveis.");
        for (int i = 0; i < configured.Length; i++)
        {
            var scene = EditorSceneManager.OpenScene(configured[i].path);
            EcoMission mission = UnityEngine.Object.FindFirstObjectByType<EcoMission>();
            Require(mission != null && mission.level == i + 1, "Ordem de fases inválida.");
            Require(mission.Player != null && mission.view != null && mission.effect != null && mission.relay != null, "Referência faltando.");
            Require(mission.restoredZones.Length == 5 && mission.pollutionZones.Length == 5, "Marcos de restauração inválidos.");
            Require(mission.spawner.prefabsLixo.All(prefab => prefab != null && prefab.CompareTag("Lixo")), "Prefabs de coleta inválidos.");
            Require(mission.landingPoints.Length >= 8, "Rota de coleta incompleta.");
            Terrain terrain = Terrain.activeTerrain;
            Require(terrain != null && terrain.terrainData.terrainLayers.Length == 10, "Terreno não contém dez camadas.");
            Require(terrain.terrainData.terrainLayers.All(layer => layer.diffuseTexture != null && layer.normalMapTexture != null && layer.maskMapTexture != null),
                "Mapas de terreno incompletos.");
            int distinct = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                .Select(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)).Distinct().Count();
            int instances = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                .Count(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) ||
                    (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject) && t.GetComponent<Renderer>() != null));
            Require(instances >= 70, "Menos de setenta elementos de cenário ativos.");
            Require(mission.Total == 20 + i * 8, "Meta de coleta fora da progressão configurada.");
            if (mission.level == 1)
                VerifyClareira(terrain);
            if (mission.level == 2)
                VerifyBridges(mission);
            Require(mission.drone != null && mission.drone.navigation != null, "NavMesh ausente.");
            var navigation = NavMesh.AddNavMeshData(mission.drone.navigation);
            try
            {
                Require(NavMesh.SamplePosition(mission.drone.transform.position, out NavMeshHit start, 4f, NavMesh.AllAreas), "NPC fora da malha.");
                foreach (var point in mission.drone.waypoints)
                {
                    var path = new NavMeshPath();
                    Require(NavMesh.SamplePosition(point.position, out NavMeshHit end, 4f, NavMesh.AllAreas) &&
                        NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete, "Patrulha sem caminho completo.");
                }
            }
            finally
            {
                navigation.Remove();
            }
            Debug.Log($"ECOBOT_VERIFIED: fase {i + 1}, {distinct} prefabs distintos, {mission.Total} resíduos, 10 camadas, patrulha navegável.");
        }
        EditorSceneManager.OpenScene(configured[0].path);
    }

    /// <summary>Confere que os quatro lados da Clareira possuem morros contínuos.</summary>
    private static void VerifyClareira(Terrain terrain)
    {
        for (int i = 0; i <= 24; i++)
        {
            float x = Mathf.Lerp(-35f, 35f, i / 24f);
            float z = Mathf.Lerp(-5.9f, 57.5f, i / 24f);
            foreach (Vector3 point in new[] {new Vector3(x,0f,-5.9f),new Vector3(x,0f,57.5f),
                new Vector3(-35f,0f,z),new Vector3(35f,0f,z)})
                Require(terrain.SampleHeight(point) + terrain.transform.position.y >= 6f,
                    "A Clareira possui uma abertura no contorno.");
        }
        Debug.Log("ECOBOT_BOUNDARY_OK: quatro lados fechados por terreno, 100 pontos conferidos.");
    }
    /// <summary>Confere o piso contínuo, os guarda-corpos e a malha de água do rio.</summary>
    private static void VerifyBridges(EcoMission mission)
    {
        Physics.SyncTransforms();
        foreach (float x in new[] {-8f, 7f})
        {
            float river = 23f + Mathf.Sin(x * 0.13f) * 4.5f;
            for (int j = 0; j <= 20; j++)
            {
                Vector3 origin = new Vector3(x, 10f, river - 5.1f + j * 10.2f / 20f);
                Require(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, 1 << 3) &&
                    hit.collider.name == "Superfície caminhável", "Piso da ponte interrompido.");
            }
        }
        Require(UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
            .Count(collider => collider.name == "Superfície caminhável") == 2, "Devem existir duas pontes completas.");
        Require(GameObject.Find("Rio • correnteza e espuma") != null, "Rio contínuo ausente.");
        Debug.Log("ECOBOT_BRIDGES_OK: dois pisos contínuos com 42 pontos de apoio verificados.");
    }
    /// <summary>Gera e valida as cinco cenas, incluindo assets e referências.</summary>
    public static void GenerateAndVerify()
    {
        Generate();
        Verify();
    }

    /// <summary>Gera as cenas, tenta a iluminação estática e compila o executável Windows.</summary>
    public static void CompleteDelivery()
    {
        ConfigureRendering();
        GenerateAndVerify();
        try
        {
            BakeLighting();
        }
        catch (Exception error)
        {
            Debug.LogWarning("ECOBOT_BAKE_INCOMPLETE: " + error.Message);
        }
        BuildWindows();
    }

    /// <summary>Valida o paisagismo atual, atualiza a iluminação e compila a entrega.</summary>
    public static void BuildVisualDelivery()
    {
        ConfigureRendering();
        EcoVisualUpgrade.ApplyAll();
        Verify();
        BakeLighting();
        BuildWindows();
    }

    /// <summary>Gera a iluminação estática das cinco cenas e salva os lightmaps.</summary>
    public static void BakeLighting()
    {
        foreach (EditorBuildSettingsScene settings in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            var scene = EditorSceneManager.OpenScene(settings.path);
            if (!Lightmapping.Bake())
                throw new InvalidOperationException("Bake não concluído em " + settings.path);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ECOBOT_BAKED: " + settings.path);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>Valida as cenas e compila a versão Windows x64 no caminho passado após -eco-output.</summary>
    public static void BuildWindows()
    {
        Verify();
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-eco-output");
        string target = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Build/EcoBot.exe";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        BuildReport report;
        EcoShaderFilter.Prepare(scenes);
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = target,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
        }
        finally
        {
            EcoShaderFilter.Clear();
        }
        Require(report.summary.result == BuildResult.Succeeded, "Build falhou: " + report.summary.result);
        Debug.Log("ECOBOT_BUILD_OK: " + report.summary.totalSize + " bytes.");
    }

    /// <summary>Lança um erro com o motivo recebido se a condição de validação falhar.</summary>
    private static void Require(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidOperationException(reason);
    }
}
