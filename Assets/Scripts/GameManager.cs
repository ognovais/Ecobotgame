using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameManager : MonoBehaviour
{
    /// <summary>Recarrega a fase atual e restaura a escala do tempo. Não recebe parâmetros.</summary>
    public void Reiniciar()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Abre a fase do índice informado na lista do build.</summary>
    public void Abrir(int index)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(index);
    }
}
