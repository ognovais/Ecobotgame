using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Refina os cenários existentes e integra os resíduos decorativos à missão.</summary>
[InitializeOnLoad]
public static partial class EcoVisualUpgrade
{
    private const string Folder = "Assets/EcoBot/Visual";
    private const string Nature = "Assets/FreeVegetation-LowPolyNature/FreeVegetation/Prefabs/";
    private const string Textures = "Assets/Game Buffs/Free Stylized Textures/Textures/";
    private static Material stone;
    private static Material wood;
    private static Material metal;
    private static Material accent;
    private static Material water;
    private static Mesh rockMesh;

    static EcoVisualUpgrade()
    {
        if (File.Exists("eco-visual-update.pending"))
            EditorApplication.delayCall += ApplyPending;
    }

    /// <summary>Executa a atualização solicitada por arquivo de controle após a recompilação.</summary>
    private static void ApplyPending()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += ApplyPending;
            return;
        }
        File.Delete("eco-visual-update.pending");
        try
        {
            ApplyAll();
            File.WriteAllText("eco-visual-update.result", "OK");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("eco-visual-update.result", error.ToString());
        }
    }

    /// <summary>Atualiza as cinco cenas do build e reabre a Clareira para inspeção.</summary>
    [MenuItem("Eco-Bot/Melhorar paisagismo e coleta")]
    public static void ApplyAll()
    {
        Prepare();
        foreach (var configured in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            var scene = EditorSceneManager.OpenScene(configured.path);
            EcoMission mission = Object.FindFirstObjectByType<EcoMission>();
            Enhance(mission);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"ECOBOT_VISUAL_OK: fase {mission.level}, {mission.placedWaste.Length} resíduos de cenário coletáveis.");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(EditorBuildSettings.scenes.First(scene => scene.enabled).path);
    }

    /// <summary>Aplica os novos materiais e a composição na missão recebida.</summary>
    public static void Enhance(EcoMission mission)
    {
        Prepare();
        Terrain terrain = Terrain.activeTerrain;
        RefineTerrain(terrain, mission);
        PaintTerrain(terrain, mission.level);
        RefineMaterials(terrain);
        var existing = GameObject.Find("Paisagismo");
        if (existing != null)
            Object.DestroyImmediate(existing);
        var landscape = new GameObject("Paisagismo").transform;
        RemovePlaceholders(mission);
        RefineGeometry(terrain, mission, landscape);
        ConvertWaste(terrain, mission);

        var random = new System.Random(310 + mission.level);
        for (int i = 0; i < 38; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (17f + (float)random.NextDouble() * 11f);
            float z = -1f + i / 2 * 2.9f + (float)random.NextDouble();
            if (mission.level == 3 && i % 3 != 0)
            {
                Rock(At(terrain, x, z), new Vector3(2.5f, 1.6f, 2f), landscape, i * 59f);
                continue;
            }
            Plant(i % 3 == 0 ? "Tree_1_2" : "Tree_1_1", At(terrain, x, z),
                4.3f + (float)random.NextDouble() * 2.4f, landscape, random.Next(360));
        }
        for (int i = 0; i < 115; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (13.6f + (float)random.NextDouble() * 13f);
            float z = (float)random.NextDouble() * 49f;
            string name = i % 9 == 0 ? "Bush_1_2" : i % 5 == 0 ? "Plant_1_1" : "Grass_1_2";
            if (mission.level == 3 && i % 3 != 0)
                name = "Grass_2_1";
            Plant(name, At(terrain, x, z), name.StartsWith("Bush") ? 1.2f : 0.42f + (float)random.NextDouble() * 0.35f,
                landscape, random.Next(360));
        }
        for (int i = 0; i < 24; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (15f + (float)random.NextDouble() * 8f);
            float z = 2f + i / 2 * 4.1f;
            Rock(At(terrain, x, z), new Vector3(1.6f + (float)random.NextDouble(), 0.7f + (float)random.NextDouble(), 1.4f), landscape, i * 41f);
        }
        for (int i = 0; i < 6; i++)
            Plant("FallenTree_1_1", At(terrain, i % 2 == 0 ? -15.5f : 15.5f, 5f + i * 7f), 2.6f, landscape, i * 37f);

        AddWayfinding(terrain, mission, landscape);
        if (mission.level == 2)
            RiverBanks(terrain, landscape);
        if (mission.level == 3)
            Industry(terrain, landscape);
        if (mission.level == 4)
            MountainRocks(terrain, landscape);
        if (mission.level == 5)
            Sanctuary(terrain, landscape);

        for (int i = 0; i < mission.restoredZones.Length; i++)
        {
            var zone = mission.restoredZones[i].transform;
            foreach (var child in zone.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Broto nativo").ToArray())
                Object.DestroyImmediate(child.gameObject);
            foreach (Transform plant in zone.Cast<Transform>().Where(child => child.name == "Plant_1_1" || child.name == "Bush_1_1").ToArray())
                Object.DestroyImmediate(plant.gameObject);
            for (int j = 0; j < 5; j++)
            {
                float angle = j * 1.2566f;
                Plant(j % 2 == 0 ? "Plant_1_1" : "Bush_1_1",
                    zone.position + new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * 1.5f,
                    j % 2 == 0 ? 0.72f : 0.95f, zone, j * 61f);
            }
        }
        mission.duration = 180f + mission.level * 30f;
        if (!mission.briefing.Contains("Todo resíduo visível"))
            mission.briefing += "\nTodo resíduo visível faz parte da coleta.";
        mission.relay.position = At(terrain, mission.level == 5 ? 0f : -3.6f, mission.level == 5 ? 43.8f : 2.7f, 0.48f);
        Lighting(mission);
        RepairProps(terrain, mission);
        RebuildNavigation(terrain, mission);
        EditorUtility.SetDirty(mission);
    }

    /// <summary>Prepara os materiais sem padronagem repetitiva e a geometria das pedras.</summary>
    private static void Prepare()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/EcoBot", "Visual");
        stone = Mat("Pedra cinza", new Color(0.43f, 0.48f, 0.44f), 0.12f);
        wood = Mat("Madeira", new Color(0.34f, 0.23f, 0.13f), 0.15f);
        metal = Mat("Metal escuro", new Color(0.20f, 0.28f, 0.29f), 0.32f);
        accent = Mat("Amarelo sinalização", new Color(0.95f, 0.67f, 0.13f), 0.2f);
        water = Mat("Água do vale", new Color(0.18f, 0.56f, 0.63f), 0.74f);
        rockMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Pedra.asset");
        if (rockMesh == null)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] source = {
                new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
                new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)
            };
            int[] faces = {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
                3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
            var vertices = new Vector3[faces.Length];
            var indices = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++)
            {
                vertices[i] = source[faces[i]].normalized * 0.5f;
                indices[i] = i;
            }
            rockMesh = new Mesh { name = "Pedra de faces planas", vertices = vertices, triangles = indices };
            rockMesh.RecalculateNormals();
            rockMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(rockMesh, Folder + "/Pedra.asset");
        }
    }

    /// <summary>Salva ou recupera o material de cor sólida informado.</summary>
    private static Material Mat(string name, Color color, float smoothness)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Restaura a grama original e pinta caminhos, margens e encostas segundo o bioma.</summary>
    private static void PaintTerrain(Terrain terrain, int level)
    {
        string[] sources = {"Forest_Ground_38","Rocky_Dirt_2","Rippled_Sand_2","Muddy_Cracked_Sand_6",
            "Grass_37","Arctic_Forest_Ground_12","Cliff_Rock_Surface_21","Cracked_Concrete_26","Underwater_Rock_Surface_3","Grass_37"};
        var terrainLayers = terrain.terrainData.terrainLayers;
        for (int i = 0; i < terrainLayers.Length; i++)
        {
            var layer = terrainLayers[i];
            string prefix = Textures + sources[i] + "/" + sources[i];
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png") ?? terrainLayers[4].normalMapTexture;
            layer.normalScale = 0.18f;
            layer.tileSize = Vector2.one * (i == 4 || i == 9 ? 2.4f : 5f);
            layer.smoothness = 0.08f;
            EditorUtility.SetDirty(layer);
        }
        terrain.terrainData.terrainLayers = terrainLayers;
        int res = terrain.terrainData.alphamapResolution;
        var paint = new float[res, res, terrainLayers.Length];
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float wx = terrain.transform.position.x + (x + 0.5f) / res * terrain.terrainData.size.x;
                float wz = terrain.transform.position.z + (z + 0.5f) / res * terrain.terrainData.size.z;
                float path = Mathf.Exp(-Mathf.Pow((wx - Mathf.Sin(wz * 0.14f + level) * 6f) / 1.3f, 2f));
                float river = 1f - Mathf.Clamp01(Mathf.Abs(wz - (23f + Mathf.Sin(wx * 0.13f) * 4.5f)) / 5f);
                float slope = terrain.terrainData.GetSteepness((x + 0.5f) / res, (z + 0.5f) / res);
                float rock = Mathf.Clamp01((slope - 23f) / 28f);
                float total = 0f;
                for (int i = 0; i < terrainLayers.Length; i++)
                {
                    float weight = 0.0015f;
                    if (i == 4) weight += level == 3 ? 0.14f : level == 4 ? 0.7f : 1.9f;
                    if (i == 0) weight += level == 1 ? path * 0.1f : path * 0.25f;
                    if (i == 1) weight += level == 3 ? 0.4f : path * 0.12f;
                    if (i == 6) weight += rock * (level == 4 ? 1.6f : 0.9f);
                    if (i == 7 && level == 3) weight += 0.9f;
                    if (i == 2 && level == 2) weight += river * 3.2f;
                    if (i == 9) weight += Mathf.PerlinNoise(wx * 0.12f + 10f, wz * 0.12f) * 0.13f;
                    paint[z, x, i] = weight;
                    total += weight;
                }
                for (int i = 0; i < terrainLayers.Length; i++)
                    paint[z, x, i] /= total;
            }
        }
        terrain.terrainData.SetAlphamaps(0, 0, paint);
        terrain.Flush();
        EditorUtility.SetDirty(terrain.terrainData);
    }

    /// <summary>Elimina peças abstratas que confundiam o lixo coletável e as árvores naturais.</summary>
    private static void RemovePlaceholders(EcoMission mission)
    {
        string[] roots = {"Marco de material", "Resíduo inerte", "Copa", "Árvore recuperada",
            "Container desativado", "Formação rochosa"};
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.name.Contains("Dourada") ||
                (mission.level == 5 && t.IsChildOf(mission.restoredZones[4].transform)))
                continue;
            if (roots.Any(prefix => t.name.StartsWith(prefix, StringComparison.Ordinal)))
                Object.DestroyImmediate(t.gameObject);
        }
    }

    /// <summary>Transforma todo prefab de lixo do catálogo em um item contabilizado e alcançável.</summary>
    private static void ConvertWaste(Terrain terrain, EcoMission mission)
    {
        var waste = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) &&
                PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) == t.gameObject)
            .Where(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).Contains("/URP_WasteOvergrowth_SA/Prefabs/"))
            .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
        int targetCount = 12 + (mission.level - 1) * 6;
        var selected = new HashSet<Transform>();
        int keep = Mathf.Min(targetCount, waste.Length);
        for (int i = 0; i < keep; i++)
            selected.Add(waste[Mathf.RoundToInt(i * (waste.Length - 1f) / Mathf.Max(1, keep - 1))]);
        foreach (Transform item in waste)
            if (!selected.Contains(item))
                Object.DestroyImmediate(item.gameObject);
        waste = waste.Where(t => t != null).ToArray();
        int fallingCount = 8 + (mission.level - 1) * 2;
        Vector3[] oldPoints = mission.landingPoints;
        if (oldPoints.Length > fallingCount)
        {
            mission.landingPoints = new Vector3[fallingCount];
            for (int i = 0; i < fallingCount; i++)
                mission.landingPoints[i] = oldPoints[Mathf.RoundToInt(i * (oldPoints.Length - 1f) / (fallingCount - 1f))];
        }
        var items = new List<EcoPickup>();
        Physics.SyncTransforms();
        for (int i = 0; i < waste.Length; i++)
        {
            GameObject obj = waste[i].gameObject;
            float radius = Mathf.Sqrt((i + 0.65f) / waste.Length);
            float angle = i * 2.399963f + mission.level * 0.24f;
            float x = Mathf.Sin(angle) * radius * 12f;
            float z = 25f + Mathf.Cos(angle) * radius * 19f;
            if (mission.level == 2 && Mathf.Abs(z - (23f + Mathf.Sin(x * 0.13f) * 4.5f)) < 4.7f)
                z += z < 23f ? -4.8f : 4.8f;
            Vector3 location = At(terrain, x, z, 0.06f);
            if (Physics.Raycast(new Vector3(x, 25f, z), Vector3.down, out RaycastHit hit, 40f, 1 << 3))
                location.y = Mathf.Max(location.y, hit.point.y + 0.06f);
            obj.transform.position = location;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            obj.transform.position += Vector3.up * (location.y - bounds.min.y);
            obj.tag = "Lixo";
            foreach (var collider in obj.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            var pickup = obj.GetComponent<EcoPickup>() ?? obj.AddComponent<EcoPickup>();
            pickup.recoveryPosition = obj.transform.position;
            GameObjectUtility.SetStaticEditorFlags(obj, 0);
            items.Add(pickup);
        }
        mission.placedWaste = items.ToArray();
    }

    /// <summary>Instancia uma planta original e ajusta sua altura conservando o modelo e os materiais.</summary>
    private static GameObject Plant(string name, Vector3 location, float height, Transform parent, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + name + ".prefab");
        var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        obj.transform.SetParent(parent, true);
        obj.transform.SetPositionAndRotation(location, Quaternion.Euler(0f, yaw, 0f));
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        float reference = name.StartsWith("Fallen") ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : bounds.size.y;
        obj.transform.localScale *= height / Mathf.Max(0.01f, reference);
        foreach (var collider in obj.GetComponentsInChildren<Collider>())
            collider.enabled = false;
        foreach (var renderer in renderers)
            renderer.receiveShadows = true;
        return obj;
    }

    /// <summary>Devolve o ponto sobre o terreno com a folga vertical informada.</summary>
    private static Vector3 At(Terrain terrain, float x, float z, float offset = 0f)
    {
        Vector3 p = new Vector3(x, 0f, z);
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y + offset;
        return p;
    }

    /// <summary>Cria uma pedra de faces planas na posição e no tamanho informados.</summary>
    private static GameObject Rock(Vector3 position, Vector3 scale, Transform parent, float yaw)
    {
        var obj = new GameObject("Pedra natural", typeof(MeshFilter), typeof(MeshRenderer));
        obj.GetComponent<MeshFilter>().sharedMesh = rockMesh;
        obj.GetComponent<MeshRenderer>().sharedMaterial = stone;
        obj.transform.SetParent(parent, true);
        obj.transform.SetPositionAndRotation(position + Vector3.up * scale.y * 0.28f, Quaternion.Euler(0f, yaw, 7f));
        obj.transform.localScale = scale;
        return obj;
    }

    /// <summary>Cria uma peça arquitetônica com material e escala definidos.</summary>
    private static GameObject Box(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent, true);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    /// <summary>Adiciona balizas, bancos e uma estação solar ao início do percurso.</summary>
    private static void AddWayfinding(Terrain terrain, EcoMission mission, Transform parent)
    {
        for (int i = 0; i < 10; i++)
        {
            float x = i % 2 == 0 ? -6.4f : 6.4f;
            Vector3 p = At(terrain, x, 4f + i * 4.2f);
            Box("Baliza de madeira", p + Vector3.up * 0.37f, new Vector3(0.13f, 0.74f, 0.13f), wood, parent);
            Box("Faixa de orientação", p + Vector3.up * 0.64f, new Vector3(0.18f, 0.10f, 0.18f), accent, parent);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 p = At(terrain, side * 5.5f, 1.5f);
            Box("Banco de madeira", p + Vector3.up * 0.55f, new Vector3(2.2f, 0.15f, 0.6f), wood, parent);
            Box("Pé do banco", p + new Vector3(-0.7f, 0.25f, 0f), new Vector3(0.15f, 0.5f, 0.5f), metal, parent);
            Box("Pé do banco", p + new Vector3(0.7f, 0.25f, 0f), new Vector3(0.15f, 0.5f, 0.5f), metal, parent);
        }
        Solar(At(terrain, -8f, -0.5f), parent);
    }

    /// <summary>Constrói um painel solar inclinado com moldura e células.</summary>
    private static void Solar(Vector3 p, Transform parent)
    {
        Box("Base do painel solar", p + Vector3.up * 0.8f, new Vector3(0.16f, 1.6f, 0.16f), metal, parent);
        var panel = new GameObject("Painel solar").transform;
        panel.SetParent(parent);
        panel.position = p + Vector3.up * 1.5f;
        panel.rotation = Quaternion.Euler(23f, -16f, 0f);
        Box("Moldura", panel.position, new Vector3(2.8f, 0.12f, 1.65f), metal, panel).transform.localRotation = Quaternion.identity;
        Material cell = Mat("Células solares", new Color(0.10f, 0.25f, 0.42f), 0.65f);
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 3; z++)
            {
                var tile = Box("Célula", Vector3.zero, new Vector3(0.62f, 0.035f, 0.45f), cell, panel);
                tile.transform.localPosition = new Vector3((x - 1.5f) * 0.67f, 0.09f, (z - 1f) * 0.5f);
                tile.transform.localRotation = Quaternion.identity;
            }
    }

    /// <summary>Enquadra o rio com rochas, vegetação de margem e uma superfície azul limpa.</summary>
    private static void RiverBanks(Terrain terrain, Transform parent)
    {
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name.StartsWith("Lâmina")))
            renderer.sharedMaterial = water;
        for (int i = 0; i < 22; i++)
        {
            float x = -29f + i * 2.7f;
            float river = 23f + Mathf.Sin(x * 0.13f) * 4.5f;
            float z = river + (i % 2 == 0 ? -3.6f : 3.6f);
            Rock(At(terrain, x, z), new Vector3(1.6f, 0.9f, 1.3f), parent, i * 23f);
            Plant("Grass_2_1", At(terrain, x + 0.8f, z), 0.8f, parent, i * 57f);
        }
    }

    /// <summary>Detalha o pátio com contêineres, corrugações e sinalização de segurança.</summary>
    private static void Industry(Terrain terrain, Transform parent)
    {
        Material blue = Mat("Contêiner azul", new Color(0.17f, 0.37f, 0.46f), 0.22f);
        Material rust = Mat("Contêiner terracota", new Color(0.55f, 0.25f, 0.16f), 0.18f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = At(terrain, i % 2 == 0 ? -15.4f : 15.4f, 8f + i * 6.6f, 1.3f);
            Box("Contêiner", p, new Vector3(3.5f, 2.6f, 6f), i % 2 == 0 ? blue : rust, parent);
            for (int j = 0; j < 9; j++)
                Box("Nervura de aço", p + new Vector3(i % 2 == 0 ? 1.78f : -1.78f, 0f, (j - 4) * 0.6f),
                    new Vector3(0.08f, 2.4f, 0.075f), metal, parent);
            Box("Faixa de segurança", p + new Vector3(0f, -1.12f, -3.04f), new Vector3(3.2f, 0.22f, 0.05f), accent, parent);
        }
    }

    /// <summary>Reforça a silhueta das montanhas com grupos de afloramentos.</summary>
    private static void MountainRocks(Terrain terrain, Transform parent)
    {
        for (int i = 0; i < 18; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * (22f + i % 4 * 2f);
            Rock(At(terrain, x, 10f + i / 2 * 5f), new Vector3(6f, 5f + i % 3, 5f), parent, i * 71f);
        }
    }

    /// <summary>Enquadra o núcleo do santuário com pedras de caminho e estações solares.</summary>
    private static void Sanctuary(Terrain terrain, Transform parent)
    {
        for (int i = 0; i < 20; i++)
        {
            float angle = i * Mathf.PI * 2f / 20f;
            Vector3 p = At(terrain, Mathf.Cos(angle) * 7.5f, 41f + Mathf.Sin(angle) * 6f, 0.04f);
            Rock(p, new Vector3(1.6f, 0.22f, 1.1f), parent, -angle * Mathf.Rad2Deg);
        }
        Solar(At(terrain, -12f, 36f), parent);
        Solar(At(terrain, 12f, 36f), parent);
    }

    /// <summary>Ajusta céu, névoa e luz para a leitura dos cenários verdes.</summary>
    private static void Lighting(EcoMission mission)
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Ceu.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, Folder + "/Ceu.mat");
        }
        sky.SetColor("_SkyTint", new Color(0.48f, 0.66f, 0.85f));
        sky.SetColor("_GroundColor", new Color(0.37f, 0.45f, 0.48f));
        sky.SetFloat("_AtmosphereThickness", 0.85f);
        sky.SetFloat("_Exposure", 1.15f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.77f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.56f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.23f, 0.29f, 0.18f);
        RenderSettings.fogColor = new Color(0.65f, 0.77f, 0.73f);
        RenderSettings.fogStartDistance = 42f;
        RenderSettings.fogEndDistance = 110f;
        RenderSettings.sun.intensity = 1.25f;
        RenderSettings.sun.color = new Color(1f, 0.97f, 0.86f);
        mission.view.clearFlags = CameraClearFlags.Skybox;
        mission.view.backgroundColor = RenderSettings.fogColor;
        EditorUtility.SetDirty(sky);
    }
}
