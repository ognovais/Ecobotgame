using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Gera as cenas, os assets autorais e o executável Windows do Eco-Bot.</summary>
public static partial class EcoBuild
{
    private const string Generated = "Assets/EcoBot";
    private const string Scenes = Generated + "/Scenes";
    private const int Walkable = 3;
    private const int WorldWidth = 72;
    private const int WorldDepth = 64;
    private static readonly Vector3 Origin = new Vector3(-36f, -3f, -6f);

    private static readonly string[] Names =
    {
        "A Clareira Esquecida", "O Leito do Rio Seco", "O Pátio Industrial Abandonado",
        "As Montanhas de Entulho", "O Santuário do Vale"
    };

    private static readonly string[] SceneNames =
    {
        "01_Clareira", "02_Rio", "03_Patio", "04_Montanhas", "05_Santuario"
    };

    private static readonly string[] Briefings =
    {
        "2077 • Vale Verde. Uma tempestade reativou o Eco-Bot. Explore a clareira, recolha os resíduos e instale o módulo de corrida. Os resíduos próximos ganham um marcador verde.",
        "Atravesse as pontes sobre o antigo rio e siga pelas margens. O módulo de pulo duplo ajuda a cruzar as elevações do leito.",
        "Siga os degraus e as rampas entre contêineres para recolher a sucata. O módulo de impulso gasta carga solar e facilita a travessia.",
        "Suba em zigue-zague pelas encostas. Use a corrida, o pulo duplo e o radar para localizar os eletrônicos nas partes mais altas.",
        "Purifique o santuário. Depois de recolher todos os resíduos, vá ao núcleo sinalizado e pressione E para restaurar o vale."
    };

    private static readonly string[] PaletteNames =
    {
        "Solo", "Cascalho", "Areia", "Argila", "Grama", "Musgo", "Basalto",
        "Aço", "Água", "Folhagem"
    };

    private static readonly Color32[] Colors =
    {
        new Color32(129, 99, 72, 255), new Color32(152, 155, 134, 255),
        new Color32(201, 168, 113, 255), new Color32(153, 91, 69, 255),
        new Color32(104, 141, 75, 255), new Color32(76, 112, 88, 255),
        new Color32(82, 92, 108, 255), new Color32(91, 133, 133, 255),
        new Color32(49, 137, 154, 255), new Color32(132, 172, 87, 255)
    };

    private static TerrainLayer[] layers;
    private static Material[] materials;
    private static Material polluted;
    private static Material gold;
    private static Material signal;
    private static Material smoke;
    private static GameObject effect;
    private static GameObject robot;
    private static AnimatorControllerReference animation;
    private static GameObject[] scenery;

    private sealed class AnimatorControllerReference
    {
        public RuntimeAnimatorController controller;
        public Avatar avatar;
    }

    /// <summary>Regenera os cinco níveis e configura a lista de cenas. Execute pelo menu ou em modo batch.</summary>
    [MenuItem("Eco-Bot/Gerar cinco fases")]
    public static void Generate()
    {
        Folder("Assets", "EcoBot");
        Folder(Generated, "Scenes");
        Folder(Generated, "Terrain");
        Folder(Generated, "Textures");
        Folder(Generated, "Materials");
        Folder(Generated, "Effects");
        Folder(Generated, "Navigation");

        CreatePalette();
        effect = CreateEffect();
        robot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ch35_nonPBR.fbx");
        animation = new AnimatorControllerReference
        {
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/EcoBotAnimator.controller"),
            avatar = AssetDatabase.LoadAllAssetsAtPath("Assets/Ch35_nonPBR.fbx").OfType<Avatar>().FirstOrDefault()
        };
        if (robot == null || animation.controller == null)
            throw new InvalidOperationException("Modelo ou Animator original não encontrado.");

        scenery = FindScenery();
        if (scenery.Length < 70)
            throw new InvalidOperationException("O catálogo de cenário precisa de 70 assets distintos.");

        var buildScenes = new EditorBuildSettingsScene[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            Scene scene = CreateScene(i + 1);
            string path = Scenes + "/" + SceneNames[i] + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException("Não foi possível salvar " + path);
            buildScenes[i] = new EditorBuildSettingsScene(path, true);
        }

        EditorBuildSettings.scenes = buildScenes;
        EditorBuildSettings.RemoveConfigObject("com.unity.input.settings.actions");
        PlayerSettings.productName = "Eco-Bot - Protocolo de Limpeza";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(buildScenes[0].path);
        Debug.Log("ECOBOT_GENERATED: 5 fases, " + scenery.Length + " assets de catálogo por fase.");
    }

    /// <summary>Cria uma cena para o número recebido e devolve a cena pronta para salvar.</summary>
    private static Scene CreateScene(int level)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Terrain terrain = MakeTerrain(level);
        var world = new GameObject("Cenário e rotas");
        var lighting = new GameObject("Iluminação");
        MakeLighting(level, lighting.transform);

        Vector3 start = Ground(terrain, 0f, 0f, 0.1f);
        PlayerController player = MakePlayer(start);
        var cameraObject = new GameObject("Câmera principal", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 61f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Sky(level);
        camera.farClipPlane = 155f;

        var root = new GameObject("Protocolo de Limpeza");
        EcoMission mission = root.AddComponent<EcoMission>();
        mission.Player = player;
        mission.view = camera;
        mission.level = level;
        mission.title = Names[level - 1];
        mission.briefing = Briefings[level - 1];
        mission.duration = 170f + level * 22f;
        mission.startPoint = start;
        mission.effect = effect;
        mission.spawner = root.AddComponent<LixoSpawner>();
        mission.spawner.prefabsLixo = new[] { "PrefabLixo", "PrefabSoda", "PrefabApple", "PrefabBurgerBox", "PrefabPizzabox" }
            .Select(name => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab")).ToArray();

        mission.landingPoints = MakeRoute(terrain, world.transform, level);
        PlaceScenery(terrain, world.transform, level);
        MakeRestorations(terrain, mission, world.transform);
        mission.drone = MakeDrone(terrain, level);
        EcoVisualUpgrade.Enhance(mission);
        return scene;
    }

    /// <summary>Cria a pasta indicada caso ela ainda não exista no projeto.</summary>
    private static void Folder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>Salva ou atualiza o asset informado, preservando o GUID das referências existentes.</summary>
    private static T Save<T>(T value, string path) where T : Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(value, path);
            return value;
        }
        EditorUtility.CopySerialized(value, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(value);
        return existing;
    }

    /// <summary>Cria dez famílias de textura e material e os materiais de sinalização e poluição.</summary>
    private static void CreatePalette()
    {
        layers = new TerrainLayer[PaletteNames.Length];
        materials = new Material[PaletteNames.Length];
        for (int i = 0; i < PaletteNames.Length; i++)
        {
            string stem = Generated + "/Textures/" + i.ToString("00") + "_" + PaletteNames[i];
            const int resolution = 64;
            Color[] diffuse = new Color[resolution * resolution];
            Color[] height = new Color[resolution * resolution];
            Color[] normal = new Color[resolution * resolution];
            Color[] mask = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int pixel = y * resolution + x;
                    float h = Grain(x, y, i);
                    float dx = Grain(x + 1, y, i) - Grain(x - 1, y, i);
                    float dy = Grain(x, y + 1, i) - Grain(x, y - 1, i);
                    Vector3 n = new Vector3(-dx, -dy, 0.8f).normalized;
                    Color color = Colors[i];
                    color *= Mathf.Lerp(0.79f, 1.14f, h);
                    color.a = 1f;
                    diffuse[pixel] = color;
                    height[pixel] = new Color(h, h, h, 1f);
                    normal[pixel] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    mask[pixel] = new Color(i == 7 ? 0.24f : 0f, 1f, h, i == 8 ? 0.65f : 0.24f);
                }
            }
            Texture2D d = WriteTexture(stem + "_Diffuse.png", diffuse, resolution, false, false);
            WriteTexture(stem + "_Height.png", height, resolution, true, false);
            Texture2D nrm = WriteTexture(stem + "_Normal.png", normal, resolution, true, true);
            Texture2D m = WriteTexture(stem + "_Mask.png", mask, resolution, true, false);
            var layer = new TerrainLayer
            {
                name = PaletteNames[i],
                diffuseTexture = d,
                normalMapTexture = nrm,
                maskMapTexture = m,
                tileSize = Vector2.one * (i == 8 ? 8f : 3.2f),
                smoothness = i == 8 ? 0.46f : 0.16f
            };
            layers[i] = Save(layer, Generated + "/Terrain/" + i.ToString("00") + "_" + PaletteNames[i] + ".terrainlayer");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = PaletteNames[i] };
            mat.SetTexture("_BaseMap", d);
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 0.65f);
            mat.SetFloat("_Smoothness", i == 8 ? 0.6f : 0.19f);
            mat.EnableKeyword("_NORMALMAP");
            materials[i] = Save(mat, Generated + "/Materials/" + PaletteNames[i] + ".mat");
        }
        polluted = Solid("Poluição", new Color(0.22f, 0.14f, 0.25f), 0.12f);
        gold = Solid("Ouro solar", new Color(0.96f, 0.63f, 0.16f), 0.22f);
        signal = Solid("Sinal de leitura", new Color(0.37f, 0.98f, 0.55f), 0.48f);
        smoke = Save(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Partículas verdes" },
            Generated + "/Materials/Particulas.mat");
    }

    /// <summary>Devolve o relevo periódico do ponto (x,y) para a família de material indicada.</summary>
    private static float Grain(int x, int y, int material)
    {
        float u = x * (2f * Mathf.PI / 64f);
        float v = y * (2f * Mathf.PI / 64f);
        float a = Mathf.Sin(u * (material % 4 + 2) + Mathf.Sin(v * 3f)) * Mathf.Cos(v * (material % 3 + 2));
        float b = Mathf.Cos(u * 13f - v * 17f) * Mathf.Sin(u * 9f + v * 11f);
        return Mathf.Clamp01(0.52f + a * 0.28f + b * 0.15f);
    }

    /// <summary>Grava os pixels na textura informada, configura a importação e devolve a textura importada.</summary>
    private static Texture2D WriteTexture(string path, Color[] pixels, int size, bool linear, bool isNormal)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
        texture.SetPixels(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !linear;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Cria um material com a cor e a emissão indicadas e devolve o asset salvo.</summary>
    private static Material Solid(string name, Color color, float emission)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color * emission);
        material.EnableKeyword("_EMISSION");
        return Save(material, Generated + "/Materials/" + name + ".mat");
    }

    /// <summary>Cria o terreno esculpido e pintado da fase informada e devolve sua instância.</summary>
    private static Terrain MakeTerrain(int level)
    {
        const int heightRes = 129;
        const int splatRes = 128;
        var data = new TerrainData
        {
            name = SceneNames[level - 1],
            heightmapResolution = heightRes,
            alphamapResolution = splatRes,
            size = new Vector3(WorldWidth, 16f, WorldDepth),
            terrainLayers = layers
        };
        float[,] heights = new float[heightRes, heightRes];
        for (int z = 0; z < heightRes; z++)
        {
            for (int x = 0; x < heightRes; x++)
            {
                float worldX = Origin.x + x / 128f * WorldWidth;
                float worldZ = Origin.z + z / 128f * WorldDepth;
                heights[z, x] = Mathf.Clamp01((Elevation(worldX, worldZ, level) - Origin.y) / data.size.y);
            }
        }
        data.SetHeights(0, 0, heights);

        var weights = new float[splatRes, splatRes, layers.Length];
        for (int z = 0; z < splatRes; z++)
        {
            for (int x = 0; x < splatRes; x++)
            {
                float wx = Origin.x + (x + 0.5f) / splatRes * WorldWidth;
                float wz = Origin.z + (z + 0.5f) / splatRes * WorldDepth;
                float path = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(wx - Mathf.Sin(wz * 0.14f + level) * (level == 4 ? 13f : 7f)) / 5f);
                float wet = level == 2 ? 1f - Mathf.Clamp01(Mathf.Abs(wz - River(wx)) / 4f) : 0f;
                float rocky = Mathf.Clamp01((Mathf.Abs(wx) - 17f) / 10f);
                float total = 0f;
                for (int i = 0; i < layers.Length; i++)
                {
                    float n = Mathf.PerlinNoise(x * 0.059f + i * 7.1f, z * 0.059f + level * 2.7f);
                    float w = 0.015f + Mathf.Pow(Mathf.Max(0f, n - 0.27f), 3f);
                    if (i == 1) w += path * 0.42f;
                    if (i == 4) w += (1f - path) * (1f - rocky) * 0.18f;
                    if (i == 6) w += rocky * 0.38f;
                    if (i == 8) w += wet * 0.57f;
                    if (i == 7 && level == 3) w += 0.11f;
                    weights[z, x, i] = w;
                    total += w;
                }
                for (int i = 0; i < layers.Length; i++)
                    weights[z, x, i] /= total;
            }
        }
        data.SetAlphamaps(0, 0, weights);
        data = Save(data, Generated + "/Terrain/" + SceneNames[level - 1] + ".asset");
        GameObject obj = Terrain.CreateTerrainGameObject(data);
        obj.name = "Terreno esculpido • 10 camadas";
        obj.layer = Walkable;
        obj.transform.position = Origin;
        Static(obj);
        Terrain terrain = obj.GetComponent<Terrain>();
        terrain.heightmapPixelError = 4;
        terrain.basemapDistance = 52;
        return terrain;
    }

    /// <summary>Devolve a coordenada Z do rio na coordenada X informada.</summary>
    private static float River(float x) => 23f + Mathf.Sin(x * 0.13f) * 4.5f;

    /// <summary>Devolve a elevação esculpida na coordenada e no nível recebidos.</summary>
    private static float Elevation(float x, float z, int level)
    {
        float banks = Mathf.SmoothStep(0f, 6f, Mathf.Clamp01((Mathf.Abs(x) - 20f) / 15f));
        float horizon = Mathf.SmoothStep(0f, 4f, Mathf.Clamp01((z - 48f) / 10f));
        float relief = Mathf.PerlinNoise(x * 0.079f + 35f, z * 0.075f + level * 9f) - 0.5f;
        float height = banks + horizon + relief * (0.6f + banks * 0.6f);
        if (level == 2)
            height -= Mathf.SmoothStep(0f, 1.95f, 1f - Mathf.Clamp01(Mathf.Abs(z - River(x)) / 7f));
        if (level == 4)
            height += Mathf.SmoothStep(0f, 6f, Mathf.Clamp01(z / 51f)) + Mathf.Sin(x * 0.28f + z * 0.2f) * 0.38f;
        if (level == 5)
            height += Mathf.SmoothStep(0f, 2.7f, Mathf.Clamp01((z - 28f) / 18f));
        return height;
    }

    /// <summary>Devolve a posição sobre o terreno nas coordenadas recebidas com a folga indicada.</summary>
    private static Vector3 Ground(Terrain terrain, float x, float z, float offset = 0f)
    {
        var pos = new Vector3(x, 0f, z);
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + offset;
        return pos;
    }
}
