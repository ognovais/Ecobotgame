using UnityEngine;

/// <summary>Coleta os resíduos gerados e os modelos de lixo posicionados no cenário.</summary>
public sealed class EcoPickup : MonoBehaviour
{
    public Vector3 recoveryPosition;
    public bool bonus;
    private EcoMission mission;
    private Vector3 landing;
    private Rigidbody body;
    private bool collected;

    /// <summary>Associa a missão e cria um sensor centrado na geometria visível do resíduo.</summary>
    public void Bind(EcoMission owner, Vector3 safeLanding)
    {
        mission = owner;
        landing = safeLanding;
        recoveryPosition = safeLanding;
        body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
        }
        var sensor = gameObject.AddComponent<SphereCollider>();
        float scale = Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(transform.lossyScale.x),
            Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))));
        sensor.center = transform.InverseTransformPoint(bounds.center);
        sensor.radius = Mathf.Clamp(bounds.extents.magnitude + 0.3f, 0.95f, 1.45f) / scale;
        sensor.isTrigger = true;
    }

    /// <summary>Processa a entrada de um collider do jogador no sensor.</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (mission != null && other.GetComponentInParent<PlayerController>() == mission.Player)
            Collect();
    }

    /// <summary>Confirma o contato persistente sem permitir contagens duplicadas.</summary>
    private void OnTriggerStay(Collider other)
    {
        OnTriggerEnter(other);
    }

    /// <summary>Registra uma coleta única e devolve se o item foi aceito.</summary>
    public bool Collect()
    {
        if (collected || mission == null || !mission.Playing)
            return false;
        collected = true;
        if (bonus)
            mission.CollectBonus(transform.position);
        else
            mission.Collect(transform.position);
        Destroy(gameObject);
        return true;
    }

    /// <summary>Recupera resíduos dinâmicos que rolaram para fora da sua área alcançável.</summary>
    private void FixedUpdate()
    {
        if (mission == null || body == null || body.isKinematic)
            return;
        Vector3 displacement = transform.position - landing;
        displacement.y = 0f;
        if (transform.position.y >= landing.y - 2f && displacement.sqrMagnitude < 25f)
            return;
        body.position = landing + Vector3.up;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }
}
