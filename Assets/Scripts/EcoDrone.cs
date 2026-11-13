using UnityEngine;
using UnityEngine.AI;

/// <summary>NPC de manutenção que percorre balizas de uma fase por NavMesh.</summary>
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EcoDrone : MonoBehaviour
{
    public Transform[] waypoints;
    public NavMeshData navigation;
    private NavMeshAgent agent;
    private NavMeshDataInstance meshInstance;
    private int index;
    private float wait;

    /// <summary>Adiciona o NavMesh da fase e posiciona o agente em um ponto caminhável.</summary>
    private void Start()
    {
        if (navigation == null || waypoints.Length == 0)
            return;
        meshInstance = NavMesh.AddNavMeshData(navigation);
        agent = GetComponent<NavMeshAgent>();
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
            SetDestination();
        }
    }

    /// <summary>Avança o NPC para a próxima baliza depois de chegar ao destino atual.</summary>
    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh || agent.pathPending || agent.remainingDistance > 0.6f)
            return;
        wait += Time.deltaTime;
        if (wait < 0.8f)
            return;
        wait = 0f;
        index = (index + 1) % waypoints.Length;
        SetDestination();
    }

    /// <summary>Projeta a baliza atual no NavMesh e solicita o caminho ao agente.</summary>
    private void SetDestination()
    {
        if (NavMesh.SamplePosition(waypoints[index].position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    /// <summary>Desregistra o NavMesh da fase quando o NPC e a cena forem descarregados.</summary>
    private void OnDestroy()
    {
        if (meshInstance.valid)
            meshInstance.Remove();
    }
}
