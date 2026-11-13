using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Permite repetir a verificação de cinco fases e a captura local por linha de comando.</summary>
internal sealed class EcoAcceptance : MonoBehaviour
{
    private string frameFolder;
    private int frame;
    private bool capture;
    private GUIStyle caption;

    /// <summary>Instala o verificador apenas quando os argumentos de teste ou captura estão presentes.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        string[] args = Environment.GetCommandLineArgs();
        int captureIndex = Array.IndexOf(args, "-eco-showcase");
        if (!args.Contains("-eco-smoke") && captureIndex < 0)
            return;

        var root = new GameObject("Verificação de aceitação");
        DontDestroyOnLoad(root);
        var test = root.AddComponent<EcoAcceptance>();
        if (captureIndex >= 0 && captureIndex + 1 < args.Length)
        {
            test.frameFolder = args[captureIndex + 1];
            Directory.CreateDirectory(test.frameFolder);
            test.capture = true;
        }
        test.StartCoroutine(test.Run());
    }

    /// <summary>Exercita o cronômetro, o reinício, a coleta via física, o NPC e as transições em sequência.</summary>
    private IEnumerator Run()
    {
        if (capture)
        {
            Time.captureFramerate = 12;
            StartCoroutine(CaptureFrames());
        }

        if (!capture)
        {
            var initial = Object.FindFirstObjectByType<EcoMission>();
            initial.duration = 0.15f;
            yield return null;
            initial.Begin();
            yield return new WaitForSeconds(0.5f);
            if (!Check(!initial.Playing && !initial.Won, "O cronômetro deveria causar derrota."))
                yield break;
            initial.GetComponent<GameManager>().Reiniciar();
            yield return null;
            yield return null;
        }

        for (int phase = 1; phase <= 5; phase++)
        {
            yield return null;
            EcoMission mission = Object.FindFirstObjectByType<EcoMission>();
            if (!Check(mission != null && mission.level == phase, "Fase incorreta após transição."))
                yield break;
            if (capture)
                yield return new WaitForSeconds(3f);

            mission.Begin();
            yield return new WaitForSeconds(capture ? 2.3f : 1.45f);
            string[] snapshotArgs = Environment.GetCommandLineArgs();
            int snapshotIndex = Array.IndexOf(snapshotArgs, "-eco-snapshots");
            if (snapshotIndex >= 0 && snapshotIndex + 1 < snapshotArgs.Length)
            {
                string folder = snapshotArgs[snapshotIndex + 1];
                Directory.CreateDirectory(folder);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "fase-" + phase + ".png"));
            }
            if (!Check(mission.drone.GetComponent<NavMeshAgent>().isOnNavMesh, "O NPC não entrou na malha."))
                yield break;

            var items = Object.FindObjectsByType<EcoPickup>(FindObjectsSortMode.None)
                .OrderBy(item => item.transform.position.z).ToArray();
            if (!Check(items.Length == mission.Total, "O spawner não gerou a quantidade esperada."))
                yield break;

            foreach (EcoPickup item in items)
            {
                if (item == null)
                    continue;
                Vector3 target = item.transform.position;
                int oldCount = mission.Collected;
                if (capture)
                {
                    Vector3 start = mission.Player.transform.position;
                    Vector3 direction = target - start;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.05f)
                        mission.Player.transform.rotation = Quaternion.LookRotation(direction);
                    float elapsed = 0f;
                    while (elapsed < 0.34f && item != null && mission.Playing)
                    {
                        elapsed += Time.deltaTime;
                        Vector3 p = Vector3.Lerp(start, target, Mathf.Clamp01(elapsed / 0.34f));
                        Terrain terrain = Terrain.activeTerrain;
                        p.y = Mathf.Max(p.y, terrain.SampleHeight(p) + terrain.transform.position.y + 0.06f);
                        mission.Player.Respawn(p);
                        yield return null;
                    }
                }
                for (int step = 0; step < 8 && item != null && mission.Playing; step++)
                {
                    mission.Player.Respawn(item.transform.position - Vector3.up * 0.35f);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }
                yield return null;
                if (!Check(item == null && mission.Collected > oldCount, "Sensor não coletou o resíduo na fase " + phase))
                    yield break;
            }

            if (phase == 5)
            {
                if (capture)
                    yield return new WaitForSeconds(2.5f);
                mission.Player.Respawn(mission.relay.position - Vector3.forward * 2.1f);
                yield return new WaitForFixedUpdate();
                if (!Check(mission.ActivateCore(), "Núcleo final não foi ativado."))
                    yield break;
            }

            if (!Check(mission.Won && mission.Collected == mission.Total && mission.HasSkill(phase - 1) &&
                mission.restoredZones.All(zone => zone.activeSelf), "Vitória, módulo ou restauração incompleta."))
                yield break;
            Debug.Log($"ECOBOT_PLAY_OK: fase {phase}, {mission.Collected} coletas por trigger, NPC na malha, bioma restaurado.");
            if (capture)
                yield return new WaitForSeconds(phase == 5 ? 6f : 3f);
            mission.NextLevel();
        }

        yield return null;
        if (!Check(SceneManager.GetActiveScene().buildIndex == 0, "Retorno à primeira fase falhou."))
            yield break;
        Debug.Log("ECOBOT_SMOKE_OK: derrota, reinício, cinco vitórias, cinco módulos, núcleo e retorno.");
        Application.Quit(0);
    }

    /// <summary>Grava um quadro do jogo no diretório de captura após o fim da renderização.</summary>
    private IEnumerator CaptureFrames()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(frameFolder, $"eco_{frame++:00000}.png"));
        }
    }

    /// <summary>Identifica na tela que os quadros foram registrados por uma demonstração automatizada.</summary>
    private void OnGUI()
    {
        if (!capture)
            return;
        caption ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.Max(12, Screen.height / 37),
            fontStyle = FontStyle.Bold
        };
        var rect = new Rect(Screen.width * 0.59f, Screen.height * 0.15f, Screen.width * 0.38f, Screen.height * 0.045f);
        Color previous = GUI.color;
        GUI.color = new Color(0.04f, 0.10f, 0.08f, 0.94f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, "DEMONSTRAÇÃO AUTOMATIZADA  ", caption);
        GUI.color = previous;
    }

    /// <summary>Devolve a condição recebida e encerra o teste com código de falha se for falsa.</summary>
    private static bool Check(bool condition, string failure)
    {
        if (!condition)
        {
            Debug.LogError("ECOBOT_SMOKE_FAIL: " + failure);
            Application.Quit(2);
        }
        return condition;
    }
}
