using UnityEngine;

public sealed class DestroySelf : MonoBehaviour
{
    [Min(0f)] public float tempoDeVida = 1f;

    /// <summary>Agenda a remoção do efeito depois de tempoDeVida segundos.</summary>
    private void Start()
    {
        Destroy(gameObject, tempoDeVida);
    }
}
