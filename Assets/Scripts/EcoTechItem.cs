using UnityEngine;

/// <summary>Módulo de Tecnologia coletável: ao ser tocado pelo robô, multiplica a força de pulo.</summary>
public sealed class EcoTechItem : MonoBehaviour
{
    public EcoMission mission;
    public float jumpMultiplier = 2f;
    [TextArea] public string message = "Servomotores melhorados: força do pulo dobrada!";
    public float spinSpeed = 90f;
    public float bobHeight = 0.18f;

    private Vector3 origin;
    private bool taken;

    /// <summary>Guarda a posição inicial usada na flutuação.</summary>
    private void Start()
    {
        origin = transform.position;
    }

    /// <summary>Gira e flutua o módulo para chamar a atenção do jogador.</summary>
    private void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = origin + Vector3.up * (Mathf.Sin(Time.time * 2.2f) * bobHeight);
    }

    /// <summary>Aplica o aumento de pulo uma única vez quando o robô toca o módulo.</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (taken || mission == null || !mission.Playing)
            return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player != mission.Player)
            return;
        taken = true;
        player.jumpForce *= jumpMultiplier;
        mission.TechCollected(transform.position, message);
        Destroy(gameObject);
    }
}
