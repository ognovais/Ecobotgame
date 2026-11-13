using UnityEngine;

/// <summary>Gera os resíduos da fase e aplica a queda com rotação física.</summary>
public sealed class LixoSpawner : MonoBehaviour
{
    public GameObject[] prefabsLixo;

    /// <summary>Faz chover resíduos extras em volta do jogador, com queda e rotação físicas. Valem pontos bônus, não entram na meta.</summary>
    public void Rain(EcoMission mission, int count, int seed)
    {
        var random = new System.Random(seed);
        Vector3 center = mission.Player.transform.position;
        for (int i = 0; i < count; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = 3f + (float)random.NextDouble() * 7f;
            Vector3 spot = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (!Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, 1 << 3, QueryTriggerInteraction.Ignore))
                continue;
            GameObject prefab = prefabsLixo[random.Next(prefabsLixo.Length)];
            GameObject item = Instantiate(prefab, hit.point + Vector3.up * (5f + (float)random.NextDouble() * 3f),
                Quaternion.Euler(0f, random.Next(360), 0f), transform);
            item.name = "Resíduo extra";
            EcoPickup pickup = item.AddComponent<EcoPickup>();
            pickup.bonus = true;
            pickup.Bind(mission, hit.point + Vector3.up * 0.1f);
            if (item.TryGetComponent(out Rigidbody body))
                body.AddTorque(new Vector3(random.Next(-7, 8), random.Next(-5, 6), random.Next(-7, 8)), ForceMode.Impulse);
        }
    }

    /// <summary>Recebe missão, posições e semente. Cria um resíduo em cada ponto alcançável e devolve a lista para o radar.</summary>
    public EcoPickup[] Spawn(EcoMission mission, Vector3[] points, int seed)
    {
        var random = new System.Random(seed);
        var pickups = new EcoPickup[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 landing = points[i] + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f);
            GameObject prefab = prefabsLixo[random.Next(prefabsLixo.Length)];
            GameObject item = Instantiate(prefab, landing + Vector3.up * (3f + (float)random.NextDouble() * 3f),
                Quaternion.Euler(0f, random.Next(360), 0f), transform);
            item.name = "Resíduo " + (i + 1);
            EcoPickup pickup = item.AddComponent<EcoPickup>();
            pickup.Bind(mission, landing);
            pickups[i] = pickup;
            if (item.TryGetComponent(out Rigidbody body))
                body.AddTorque(new Vector3(random.Next(-7, 8), random.Next(-5, 6), random.Next(-7, 8)), ForceMode.Impulse);
        }
        return pickups;
    }
}
