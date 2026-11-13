using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static partial class EcoVisualUpgrade
{
    /// <summary>Forma um anel contínuo de morros ao redor da área jogável da Clareira.</summary>
    private static void RefineTerrain(Terrain terrain, EcoMission mission)
    {
        if (mission.level != 1)
            return;
        TerrainData data = terrain.terrainData;
        int size = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, size, size);
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                float wx = terrain.transform.position.x + x / (size - 1f) * data.size.x;
                float wz = terrain.transform.position.z + z / (size - 1f) * data.size.z;
                float side = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 11f, 36f - Mathf.Abs(wx)));
                float back = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 10f, 58f - wz));
                float front = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 5f, wz + 6f));
                float rim = Mathf.Max(side, Mathf.Max(back, front));
                float height = rim * (8.2f + Mathf.Sin(wx * 0.23f + wz * 0.14f) * 1.1f);
                heights[z, x] = Mathf.Max(heights[z, x], (height - terrain.transform.position.y) / data.size.y);
            }
        }
        data.SetHeights(0, 0, heights);
        mission.startPoint = At(terrain, 0f, 0f, 0.12f);
        mission.Player.transform.position = mission.startPoint;
        EditorUtility.SetDirty(data);
    }

    /// <summary>Usa as texturas naturais do terreno nos materiais antigos e elimina o efeito quadriculado.</summary>
    private static void RefineMaterials(Terrain terrain)
    {
        string[] names = {"Solo","Cascalho","Areia","Argila","Grama","Musgo","Basalto","Aço","Água","Folhagem"};
        for (int i = 0; i < names.Length; i++)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoBot/Materials/" + names[i] + ".mat");
            if (material == null)
                continue;
            material.SetTexture("_BaseMap", terrain.terrainData.terrainLayers[i].diffuseTexture);
            material.SetTexture("_BumpMap", terrain.terrainData.terrainLayers[i].normalMapTexture);
            material.SetFloat("_BumpScale", 0.2f);
            material.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(material);
        }
        string plank = Textures + "Planks_Wood_16/Planks_Wood_16";
        wood.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(plank + "_Albedo.png"));
        wood.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(plank + "_Normal.png"));
        wood.SetColor("_BaseColor", new Color(0.92f, 0.80f, 0.64f));
        wood.SetFloat("_BumpScale", 0.35f);
        wood.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(wood);
        var sky = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Ceu.mat");
        if (sky != null)
        {
            sky.SetColor("_GroundColor", new Color(0.37f, 0.45f, 0.48f));
            EditorUtility.SetDirty(sky);
        }
    }

    /// <summary>Cria o fechamento natural da Clareira e substitui o rio e suas travessias.</summary>
    private static void RefineGeometry(Terrain terrain, EcoMission mission, Transform parent)
    {
        if (mission.level == 1)
        {
            for (int i = 0; i < 28; i++)
            {
                float x = -30f + i * 60f / 27f;
                Plant(i % 2 == 0 ? "Tree_1_1" : "Tree_1_2", At(terrain, x, -3.4f),
                    4.2f + i % 3 * 0.5f, parent, i * 137f);
                Plant(i % 2 == 0 ? "Tree_1_2" : "Tree_1_1", At(terrain, x, 53.5f),
                    4.8f + i % 4 * 0.5f, parent, i * 71f);
            }
            for (int i = 0; i < 20; i++)
            {
                float z = i * 2.75f;
                Plant("Tree_1_1", At(terrain, -29f, z), 4.5f + i % 3, parent, i * 97f);
                Plant("Tree_1_2", At(terrain, 29f, z), 4.8f + i % 2, parent, i * 61f);
                Rock(At(terrain, i % 2 == 0 ? -31f : 31f, z),
                    new Vector3(5f, 3.3f, 4f), parent, i * 37f);
            }
        }
        if (mission.level != 2)
            return;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("Travessa da ponte") || t.name.StartsWith("Lâmina d’água")).ToArray())
            Object.DestroyImmediate(t.gameObject);

        water.shader = Shader.Find("EcoBot/AguaCorrente");
        water.SetColor("_ShallowColor", new Color(0.13f, 0.59f, 0.56f, 0.72f));
        water.SetColor("_DeepColor", new Color(0.035f, 0.26f, 0.36f, 0.92f));
        water.SetColor("_FoamColor", new Color(0.84f, 0.96f, 0.91f, 0.8f));
        water.SetFloat("_FlowSpeed", 0.45f);
        water.renderQueue = 3000;
        EditorUtility.SetDirty(water);
        BuildRiver(parent);
        BuildBridge(terrain, -8f, parent, 1);
        BuildBridge(terrain, 7f, parent, 2);
    }

    /// <summary>Gera uma faixa contínua de água com meandros, bordas suaves e largura variável.</summary>
    private static void BuildRiver(Transform parent)
    {
        const int sections = 97;
        float[] lateral = {-1f,-0.88f,0f,0.88f,1f};
        var vertices = new Vector3[sections * lateral.Length];
        var colors = new Color[vertices.Length];
        var triangles = new List<int>();
        for (int i = 0; i < sections; i++)
        {
            float x = -35.5f + i * 71f / (sections - 1);
            float center = 23f + Mathf.Sin(x * 0.13f) * 4.5f;
            float halfWidth = 2.65f + Mathf.Sin(x * 0.24f) * 0.18f;
            for (int j = 0; j < lateral.Length; j++)
            {
                int k = i * lateral.Length + j;
                vertices[k] = new Vector3(x, -1.02f, center + lateral[j] * halfWidth);
                colors[k] = new Color(1f, 1f, 1f, 1f - Mathf.Abs(lateral[j]));
                if (i == sections - 1 || j == lateral.Length - 1)
                    continue;
                triangles.AddRange(new[] {k,k+1,k+lateral.Length,k+1,k+lateral.Length+1,k+lateral.Length});
            }
        }
        var mesh = new Mesh { name = "Rio contínuo", vertices = vertices, colors = colors, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh = StoreMesh(mesh, Folder + "/Rio.asset");
        var river = new GameObject("Rio • correnteza e espuma", typeof(MeshFilter), typeof(MeshRenderer));
        river.transform.SetParent(parent, false);
        river.GetComponent<MeshFilter>().sharedMesh = mesh;
        river.GetComponent<MeshRenderer>().sharedMaterial = water;
        river.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Constrói uma ponte de madeira com piso arqueado, vigas, pilares e guarda-corpos.</summary>
    private static void BuildBridge(Terrain terrain, float x, Transform parent, int number)
    {
        float center = 23f + Mathf.Sin(x * 0.13f) * 4.5f;
        var bridge = new GameObject("Ponte de madeira " + number).transform;
        bridge.SetParent(parent, false);
        float bankA = At(terrain, x, center - 5.2f).y + 0.16f;
        float bankB = At(terrain, x, center + 5.2f).y + 0.16f;
        const int steps = 26;
        var floorVertices = new Vector3[(steps + 1) * 2];
        var floorTriangles = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float z = center - 5.2f + 10.4f * t;
            float y = DeckHeight(bankA, bankB, t);
            floorVertices[i * 2] = new Vector3(x - 1.48f, y + 0.085f, z);
            floorVertices[i * 2 + 1] = new Vector3(x + 1.48f, y + 0.085f, z);
            if (i == steps)
                continue;
            float nextT = (i + 1f) / steps;
            float nextY = DeckHeight(bankA, bankB, nextT);
            Vector3 midpoint = new Vector3(x, (y + nextY) * 0.5f, z + 0.2f);
            var plank = Box("Tábua " + (i + 1), midpoint, new Vector3(3.08f, 0.16f, 0.382f), wood, bridge);
            plank.transform.rotation = Quaternion.Euler(-Mathf.Atan2(nextY - y, 0.4f) * Mathf.Rad2Deg, 0f, 0f);
            floorTriangles.AddRange(new[] {i*2,i*2+2,i*2+1,i*2+1,i*2+2,i*2+3});
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 a = new Vector3(x + side * 1.13f, y - 0.22f, z);
                Vector3 b = new Vector3(x + side * 1.13f, nextY - 0.22f, z + 0.4f);
                Beam("Viga inferior", a, b, 0.22f, 0.27f, wood, bridge, false);
            }
        }
        var floorMesh = new Mesh { name = "Piso caminhável", vertices = floorVertices, triangles = floorTriangles.ToArray() };
        floorMesh.RecalculateNormals();
        floorMesh.RecalculateBounds();
        floorMesh = StoreMesh(floorMesh, Folder + "/PisoPonte" + number + ".asset");
        var surface = new GameObject("Superfície caminhável", typeof(MeshCollider));
        surface.transform.SetParent(bridge, false);
        surface.layer = 3;
        surface.GetComponent<MeshCollider>().sharedMesh = floorMesh;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lastTop = Vector3.zero;
            Vector3 lastLow = Vector3.zero;
            for (int i = 0; i <= 6; i++)
            {
                float t = i / 6f;
                Vector3 p = new Vector3(x + side * 1.47f, DeckHeight(bankA, bankB, t), center - 5.2f + t * 10.4f);
                Beam("Poste do guarda-corpo", p - Vector3.up * 0.14f, p + Vector3.up * 1.08f,
                    0.16f, 0.16f, wood, bridge, true);
                if (i > 0)
                {
                    Beam("Corrimão", lastTop, p + Vector3.up * 1.02f, 0.13f, 0.15f, wood, bridge, true);
                    Beam("Travessa do guarda-corpo", lastLow, p + Vector3.up * 0.48f, 0.10f, 0.10f, wood, bridge, false);
                }
                lastTop = p + Vector3.up * 1.02f;
                lastLow = p + Vector3.up * 0.48f;
                if (i == 1 || i == 5)
                {
                    float foot = At(terrain, p.x, p.z).y - 0.1f;
                    Beam("Pilar de sustentação", new Vector3(p.x, foot, p.z), p,
                        0.28f, 0.28f, wood, bridge, false);
                }
            }
            foreach (float support in new[] {1f / 6f, 5f / 6f})
            {
                float z = center - 5.2f + support * 10.4f;
                float foot = At(terrain, x + side * 1.13f, z).y + 0.15f;
                Vector3 bottom = new Vector3(x + side * 1.13f, foot, z);
                Vector3 top = new Vector3(x + side * 1.13f, DeckHeight(bankA, bankB, 0.5f) - 0.19f, center);
                Beam("Escora diagonal", bottom, top, 0.12f, 0.13f, wood, bridge, false);
            }        }
    }

    /// <summary>Devolve a altura do arco suave no ponto normalizado do tabuleiro.</summary>
    private static float DeckHeight(float a, float b, float t)
    {
        return Mathf.Lerp(a, b, t) + Mathf.Sin(t * Mathf.PI) * 0.5f;
    }

    /// <summary>Cria uma viga entre os pontos recebidos, com colisão opcional.</summary>
    private static GameObject Beam(string name, Vector3 a, Vector3 b, float width, float depth,
        Material material, Transform parent, bool collision)
    {
        Vector3 delta = b - a;
        GameObject obj = Box(name, (a + b) * 0.5f, new Vector3(width, delta.magnitude, depth), material, parent);
        obj.transform.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        if (collision)
        {
            obj.layer = 3;
            obj.AddComponent<BoxCollider>();
        }
        return obj;
    }

    /// <summary>Salva uma malha mantendo sua referência entre atualizações.</summary>
    private static Mesh StoreMesh(Mesh value, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
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

    /// <summary>Substitui discos artificiais por manchas ajustadas ao solo e harmoniza a arquitetura.</summary>
    private static void RepairProps(Terrain terrain, EcoMission mission)
    {
        Material pollution = Mat("Contaminação no solo", new Color(0.16f, 0.20f, 0.17f), 0.52f);
        for (int index = 0; index < mission.pollutionZones.Length; index++)
        {
            Transform root = mission.pollutionZones[index].transform;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                Object.DestroyImmediate(renderer.gameObject);
            const int segments = 24;
            var vertices = new Vector3[segments + 1];
            var triangles = new List<int>();
            Vector3 center = root.position;
            vertices[0] = At(terrain, center.x, center.z, 0.045f) - center;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float radius = 1.8f + Mathf.Sin(i * 2.7f + index) * 0.27f;
                vertices[i + 1] = At(terrain, center.x + Mathf.Cos(angle) * radius,
                    center.z + Mathf.Sin(angle) * radius, 0.045f) - center;
                triangles.AddRange(new[] {0,(i+1)%segments+1,i+1});
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh = StoreMesh(mesh, Folder + "/Mancha_" + mission.level + "_" + index + ".asset");
            var patch = new GameObject("Solo contaminado", typeof(MeshFilter), typeof(MeshRenderer));
            patch.transform.SetParent(root, false);
            patch.GetComponent<MeshFilter>().sharedMesh = mesh;
            patch.GetComponent<MeshRenderer>().sharedMaterial = pollution;
        }
        string[] disks = {"Jardim de sementes","Solo vivo","Nascente","Habitat","Bosque restaurado"};
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (disks.Contains(renderer.name))
                Object.DestroyImmediate(renderer.gameObject);
            else if (renderer.name == "Pilar da usina" || renderer.name == "Patamar de basalto")
                renderer.sharedMaterial = stone;
            else if (renderer.name == "Baliza de manutenção" || renderer.name == "Núcleo de purificação")
                renderer.sharedMaterial = metal;
            else if (renderer.name == "Pedra da clareira" && renderer.TryGetComponent(out MeshFilter filter))
                filter.sharedMesh = rockMesh;
        }
    }

    /// <summary>Reconstrói a navegação para incluir o relevo fechado e o piso real das pontes.</summary>
    private static void RebuildNavigation(Terrain terrain, EcoMission mission)
    {
        Bounds bounds = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f,
            terrain.terrainData.size + Vector3.up * 8f);
        var sources = new List<NavMeshBuildSource>();
        Physics.SyncTransforms();
        NavMeshBuilder.CollectSources(bounds, 1 << 3, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources, bounds, Vector3.zero, Quaternion.identity);
        NavMeshData existing = mission.drone.navigation;
        if (existing != null)
        {
            EditorUtility.CopySerialized(data, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(data);
        }
        else
            mission.drone.navigation = data;
    }
}
