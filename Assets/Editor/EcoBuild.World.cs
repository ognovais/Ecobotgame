using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class EcoBuild
{
    /// <summary>Cria o robô original na posição de início, com colisão e animações.</summary>
    private static PlayerController MakePlayer(Vector3 start)
    {
        var player = new GameObject("Eco-Bot", typeof(Rigidbody), typeof(CapsuleCollider));
        player.tag = "Player";
        player.transform.position = start;
        var capsule = player.GetComponent<CapsuleCollider>();
        capsule.center = Vector3.up * 0.9f;
        capsule.height = 1.8f;
        capsule.radius = 0.36f;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(robot);
        model.transform.SetParent(player.transform, false);
        var animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = animation.controller;
        animator.avatar = animation.avatar;
        animator.applyRootMotion = false;

        var probe = new GameObject("Sensor de chão").transform;
        probe.SetParent(player.transform, false);
        probe.localPosition = Vector3.up * 0.11f;
        var controller = player.AddComponent<PlayerController>();
        controller.groundCheck = probe;
        return controller;
    }

    /// <summary>Devolve os prefabs de decoração distintos, agrupados por origem e usados em todas as fases.</summary>
    private static GameObject[] FindScenery()
    {
        string[] roots = { "Assets/FreeVegetation-LowPolyNature", "Assets/URP_WasteOvergrowth_SA/Prefabs" };
        return AssetDatabase.FindAssets("t:Prefab", roots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && !path.Contains("/Flies."))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(84)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab => prefab != null && prefab.GetComponentInChildren<Renderer>() != null)
            .ToArray();
    }

    /// <summary>Distribui os assets de catálogo em margens e categorias fora dos corredores de coleta.</summary>
    private static void PlaceScenery(Terrain terrain, Transform parent, int level)
    {
        var decor = new GameObject("Catálogo aplicado • " + scenery.Length + " prefabs distintos");
        decor.transform.SetParent(parent);
        var groups = new Dictionary<string, Transform>();
        for (int i = 0; i < scenery.Length; i++)
        {
            GameObject asset = scenery[i];
            string category = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset)));
            if (!groups.TryGetValue(category, out Transform group))
            {
                group = new GameObject(category).transform;
                group.SetParent(decor.transform);
                groups.Add(category, group);
            }

            var prop = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            prop.transform.SetParent(group, false);
            float side = i % 2 == 0 ? -1f : 1f;
            int slot = i / 2;
            float x = side * (18f + (slot % 4) * 3.5f);
            float z = 3.5f + (slot / 4) * 4.55f + Mathf.Sin(slot * 2.7f + level) * 1.4f;
            prop.transform.SetPositionAndRotation(Ground(terrain, x, z), Quaternion.Euler(0f, i * 137.51f, 0f));
            Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            bool vegetation = i < 10;
            float targetSize = vegetation ? 1.4f + i % 4 * 0.85f : 0.7f + i % 5 * 0.2f;
            if (largest > 0.001f)
                prop.transform.localScale *= Mathf.Clamp(targetSize / largest, 0.08f, 6f);
            foreach (var rigid in prop.GetComponentsInChildren<Rigidbody>(true))
                rigid.isKinematic = true;
            foreach (var collider in prop.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            Static(prop);
        }

        for (int i = 0; i < 8; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (9.5f + i % 3 * 2.2f);
            float z = 6f + i * 5.8f;
            Vector3 location = Ground(terrain, x, z);
            if (level == 1 || level == 2)
                MakeTree(location, parent, false);
            else
                Shape(level == 3 ? "Container desativado" : "Formação rochosa", level == 3 ? PrimitiveType.Cube : PrimitiveType.Sphere,
                    location + Vector3.up * (0.95f + i % 3 * 0.35f),
                    new Vector3(2.4f, 2f + i % 3 * 0.7f, 2.6f), materials[level == 3 ? 7 : 6], parent, false);
        }

        for (int i = 0; i < 10; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * (10f + level * 0.7f);
            float z = 5f + i * 4f;
            var sample = Shape("Marco de material • " + PaletteNames[i], PrimitiveType.Cube, Ground(terrain, x, z, 0.33f),
                new Vector3(1.35f, 0.66f, 1.2f), materials[i], parent, false);
            sample.transform.rotation = Quaternion.Euler(0f, (i + level) * 29f, 0f);
        }
    }

    /// <summary>Constrói as travessias e devolve as posições seguras para a chuva de resíduos.</summary>
    private static Vector3[] MakeRoute(Terrain terrain, Transform parent, int level)
    {
        int count = 18 + level * 2;
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float z = 4f + 39f * i / (count - 1);
            float x = level == 1
                ? ((i * 3) % 7 - 3) * 2.15f
                : Mathf.Sin(i * (level == 4 ? 0.62f : 0.95f) + level) * (level == 4 ? 12f : 8.6f);
            points[i] = Ground(terrain, x, z, 0.06f);
        }
        if (level == 1)
        {
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 10.5f;
                Shape("Pedra da clareira", PrimitiveType.Sphere, Ground(terrain, x, 9f + i * 5f),
                    new Vector3(2.4f, 1.3f, 1.85f), materials[6], parent, true);
            }
        }
        else if (level == 2)
        {
            for (int i = 0; i < 17; i++)
            {
                float x = (i - 8) * 3.6f;
                float z = River(x);
                Shape("Lâmina d’água residual", PrimitiveType.Cube, new Vector3(x, -1.04f, z),
                    new Vector3(3.69f, 0.07f, 3.7f), materials[8], parent, false);
            }
            foreach (float x in new[] { -8f, 7f })
            {
                float z = River(x);
                for (int i = -4; i <= 4; i++)
                    Shape("Travessa da ponte", PrimitiveType.Cube, new Vector3(x, -0.1f, z + i * 0.79f),
                        new Vector3(3.2f, 0.22f, 0.8f), materials[7], parent, true);
            }
        }
        else if (level == 3 || level == 4)
        {
            for (int i = 3; i < count - 1; i += 4)
            {
                Vector3 basePoint = points[i];
                float height = level == 3 ? 1.1f + (i % 3) * 0.4f : 1.45f;
                Shape(level == 3 ? "Plataforma de sucata" : "Patamar de basalto", PrimitiveType.Cube,
                    basePoint + Vector3.up * (height * 0.5f - 0.06f), new Vector3(3.9f, height, 3.4f),
                    materials[level == 3 ? 7 : 6], parent, true);
                for (int step = 1; step <= 3; step++)
                {
                    float stepHeight = step * height / 4f;
                    Vector3 p = Ground(terrain, basePoint.x, basePoint.z - 4.5f + step * 0.8f, stepHeight * 0.5f);
                    Shape("Degrau de acesso", PrimitiveType.Cube, p,
                        new Vector3(2.5f, stepHeight, 0.81f), materials[1], parent, true);
                }
                points[i] += Vector3.up * height;
            }
        }
        else
        {
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2f / 10f;
                Shape("Pilar da usina", PrimitiveType.Cylinder,
                    Ground(terrain, Mathf.Cos(angle) * 12.5f, 34f + Mathf.Sin(angle) * 9f, 1.8f),
                    new Vector3(1.18f, 1.8f, 1.18f), materials[7], parent, true);
            }
        }
        return points;
    }

    /// <summary>Cria cinco marcos contaminados e suas versões restauradas, incluindo o núcleo e a árvore finais.</summary>
    private static void MakeRestorations(Terrain terrain, EcoMission mission, Transform parent)
    {
        mission.restoredZones = new GameObject[5];
        mission.pollutionZones = new GameObject[5];
        string[] names = { "Jardim de sementes", "Solo vivo", "Nascente", "Habitat", "Bosque restaurado" };
        for (int i = 0; i < 5; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * (12.6f + i % 3);
            float z = 7f + i * 9.4f;
            if (mission.level == 5 && i == 4)
            {
                x = 0f;
                z = 47f;
            }
            Vector3 position = Ground(terrain, x, z);
            var bad = new GameObject("Contaminação • " + names[i]);
            var good = new GameObject("Renovação • " + names[i]);
            bad.transform.SetParent(parent);
            good.transform.SetParent(parent);
            bad.transform.position = position;
            good.transform.position = position;
            mission.pollutionZones[i] = bad;
            mission.restoredZones[i] = good;

            Shape("Área contaminada", PrimitiveType.Cylinder, position + Vector3.up * 0.055f,
                new Vector3(4.5f, 0.1f, 4.5f), polluted, bad.transform, false);
            for (int j = 0; j < 5; j++)
            {
                float angle = j * 1.256637f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (0.9f + j * 0.18f);
                var debris = Shape("Resíduo inerte", PrimitiveType.Cube, position + offset + Vector3.up * 0.25f,
                    new Vector3(0.75f, 0.43f, 0.44f), polluted, bad.transform, false);
                debris.transform.rotation = Quaternion.Euler(14f, j * 42f, 18f);
                if (i != 2)
                {
                    float size = i == 0 ? 0.42f : 0.75f;
                    Shape("Broto nativo", PrimitiveType.Sphere, position + offset + Vector3.up * size * 0.5f,
                        new Vector3(size * 0.8f, size, size * 0.8f), materials[9], good.transform, false);
                }
            }
            Shape(names[i], PrimitiveType.Cylinder, position + Vector3.up * 0.10f,
                new Vector3(4.3f, 0.13f, 4.3f), materials[i == 2 ? 8 : 4], good.transform, false);
            if (i == 3)
            {
                Shape("Abrigo de polinizadores", PrimitiveType.Cube, position + Vector3.up * 0.6f,
                    new Vector3(1.3f, 0.85f, 0.85f), materials[0], good.transform, false);
                Shape("Sinal de vida", PrimitiveType.Sphere, position + Vector3.up * 1.5f,
                    new Vector3(0.55f, 0.28f, 0.45f), gold, good.transform, false);
            }
            if (i == 4)
                MakeTree(position, good.transform, mission.level == 5);
            good.SetActive(false);
        }

        Vector3 relayPos = Ground(terrain, 0f, mission.level == 5 ? 43.8f : 2.7f);
        mission.relay = Shape(mission.level == 5 ? "Núcleo de purificação" : "Baliza de manutenção", PrimitiveType.Cylinder,
            relayPos + Vector3.up * 0.48f, new Vector3(1.3f, 0.47f, 1.3f), materials[7], parent, false).transform;
        Shape("Emissor", PrimitiveType.Sphere, relayPos + Vector3.up * 1.05f, Vector3.one * 0.54f, signal, mission.relay, false);
    }

    /// <summary>Cria a árvore autoral na posição recebida, com copa dourada no desfecho.</summary>
    private static void MakeTree(Vector3 basePoint, Transform parent, bool final)
    {
        var trunk = Shape(final ? "Árvore Dourada" : "Árvore recuperada", PrimitiveType.Cylinder,
            basePoint + Vector3.up * 1.8f, new Vector3(0.65f, 1.85f, 0.65f), materials[0], parent, false);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 2.399963f;
            Vector3 position = basePoint + new Vector3(Mathf.Cos(angle) * 1.35f, 3.2f + i * 0.26f, Mathf.Sin(angle) * 1.35f);
            Shape("Copa", PrimitiveType.Sphere, position, new Vector3(2.1f, 1.8f, 2.1f),
                final ? gold : materials[i % 2 == 0 ? 4 : 9], trunk.transform.parent, false);
        }
    }

    /// <summary>Cria e devolve uma forma com material, escala mundial e colisão de solo opcionais.</summary>
    private static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale,
        Material material, Transform parent, bool collision)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetPositionAndRotation(position, Quaternion.identity);
        obj.transform.localScale = scale;
        obj.transform.SetParent(parent, true);
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (collision)
            obj.layer = Walkable;
        else
            Object.DestroyImmediate(obj.GetComponent<Collider>());
        if (collision)
            Static(obj);
        return obj;
    }

    /// <summary>Marca a geometria fixa informada para participar da iluminação estática.</summary>
    private static void Static(GameObject obj)
    {
        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
    }

    /// <summary>Devolve a cor de céu que identifica a fase informada.</summary>
    private static Color Sky(int level)
    {
        return level switch
        {
            1 => new Color(0.42f, 0.53f, 0.47f),
            2 => new Color(0.37f, 0.52f, 0.59f),
            3 => new Color(0.48f, 0.40f, 0.36f),
            4 => new Color(0.41f, 0.47f, 0.62f),
            _ => new Color(0.53f, 0.56f, 0.44f)
        };
    }

    /// <summary>Configura iluminação ambiental e fontes direcional, pontual e de área da fase.</summary>
    private static void MakeLighting(int level, Transform parent)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.79f, 0.83f);
        RenderSettings.ambientEquatorColor = Sky(level);
        RenderSettings.ambientGroundColor = new Color(0.29f, 0.30f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Sky(level);
        RenderSettings.fogStartDistance = 22f;
        RenderSettings.fogEndDistance = 90f;

        var sun = new GameObject("Sol direcional • Mixed", typeof(Light));
        sun.transform.SetParent(parent);
        sun.transform.rotation = Quaternion.Euler(43f, 32f + level * 13f, 0f);
        Light sunlight = sun.GetComponent<Light>();
        sunlight.type = LightType.Directional;
        sunlight.intensity = 1.45f;
        sunlight.color = new Color(1f, 0.93f, 0.77f);
        sunlight.lightmapBakeType = LightmapBakeType.Mixed;
        sunlight.shadows = LightShadows.Soft;
        RenderSettings.sun = sunlight;

        var point = new GameObject("Baliza • luz pontual", typeof(Light));
        point.transform.SetParent(parent);
        point.transform.position = new Vector3(0f, 3.2f, 8f);
        Light beacon = point.GetComponent<Light>();
        beacon.type = LightType.Point;
        beacon.intensity = 2.5f;
        beacon.range = 13f;
        beacon.color = new Color(0.43f, 1f, 0.58f);

        var area = new GameObject("Painel solar • luz de área Baked", typeof(Light));
        area.transform.SetParent(parent);
        area.transform.SetPositionAndRotation(new Vector3(0f, 4.5f, 5f), Quaternion.Euler(90f, 0f, 0f));
        Light panel = area.GetComponent<Light>();
        panel.type = LightType.Rectangle;
        panel.areaSize = new Vector2(4f, 2f);
        panel.intensity = 2f;
        panel.range = 10f;
        panel.lightmapBakeType = LightmapBakeType.Baked;

        var settings = new LightingSettings
        {
            name = "Eco-Bot " + level,
            bakedGI = true,
            realtimeGI = false,
            mixedBakeMode = MixedLightingMode.Shadowmask,
            lightmapResolution = 4f,
            lightmapMaxSize = 1024,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            directSampleCount = 8,
            indirectSampleCount = 16,
            environmentSampleCount = 8
        };
        Lightmapping.lightingSettings = Save(settings, Generated + "/Scenes/Lighting_" + level + ".lighting");
    }

    /// <summary>Cria um NPC e salva a malha de navegação correspondente à geometria da fase.</summary>
    private static EcoDrone MakeDrone(Terrain terrain, int level)
    {
        Bounds bounds = new Bounds(Origin + new Vector3(WorldWidth / 2f, 8f, WorldDepth / 2f), new Vector3(WorldWidth, 19f, WorldDepth));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, 1 << Walkable, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources, bounds, Vector3.zero, Quaternion.identity);
        data = Save(data, Generated + "/Navigation/" + SceneNames[level - 1] + ".asset");
        var npc = new GameObject("NPC • drone de manutenção", typeof(NavMeshAgent));
        npc.transform.position = Ground(terrain, -4f, 3.5f);
        var agent = npc.GetComponent<NavMeshAgent>();
        agent.enabled = false;
        agent.radius = 0.32f;
        agent.height = 1.4f;
        agent.speed = 1.85f;
        agent.angularSpeed = 340f;
        agent.stoppingDistance = 0.25f;

        Vector3 center = npc.transform.position + Vector3.up * 0.85f;
        Shape("Carcaça", PrimitiveType.Sphere, center, new Vector3(1.08f, 0.55f, 0.69f), materials[7], npc.transform, false);
        Shape("Visor", PrimitiveType.Cube, center + new Vector3(0f, 0.02f, 0.34f),
            new Vector3(0.67f, 0.19f, 0.07f), signal, npc.transform, false);
        Shape("Antena", PrimitiveType.Cylinder, center + new Vector3(0.2f, 0.4f, 0f),
            new Vector3(0.035f, 0.2f, 0.035f), gold, npc.transform, false);
        EcoDrone drone = npc.AddComponent<EcoDrone>();
        drone.navigation = data;
        drone.waypoints = new Transform[3];
        Vector2[] route = { new Vector2(4f, 7f), new Vector2(3f, 14f), new Vector2(-5f, 6f) };
        for (int i = 0; i < route.Length; i++)
        {
            var point = new GameObject("Patrulha " + (i + 1)).transform;
            point.position = Ground(terrain, route[i].x, route[i].y);
            drone.waypoints[i] = point;
        }
        return drone;
    }

    /// <summary>Cria o prefab autoral de limpeza com faíscas e fumaça e devolve a referência persistente.</summary>
    private static GameObject CreateEffect()
    {
        var root = new GameObject("Efeito de reciclagem");
        root.AddComponent<DestroySelf>().tempoDeVida = 1.5f;
        ConfigureParticles(root.transform, "Faíscas de energia", new Color(0.53f, 1f, 0.27f), 26, 2.8f, 0.14f, 0.55f);
        ConfigureParticles(root.transform, "Fumaça de descontaminação", new Color(0.64f, 0.94f, 0.76f, 0.7f), 9, 0.7f, 0.38f, 1.2f);
        string path = Generated + "/Effects/Reciclagem.prefab";
        GameObject result = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return result;
    }

    /// <summary>Cria uma emissão local com a cor, quantidade, velocidade, tamanho e duração recebidos.</summary>
    private static void ConfigureParticles(Transform parent, string name, Color color, short count, float speed, float size, float life)
    {
        var obj = new GameObject(name, typeof(ParticleSystem));
        obj.transform.SetParent(parent, false);
        var particles = obj.GetComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = false;
        main.duration = 1.3f;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.45f, size);
        main.startColor = color;
        main.gravityModifier = speed > 1f ? 0.32f : -0.13f;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        obj.GetComponent<ParticleSystemRenderer>().sharedMaterial = smoke;
    }
}
