using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Coordena objetivos, tempo, habilidades, interface e transições de uma fase.</summary>
[RequireComponent(typeof(GameManager), typeof(AudioSource))]
public sealed class EcoMission : MonoBehaviour
{
    private enum State { Briefing, Active, Paused, Victory, Defeat }

    public PlayerController Player;
    public Camera view;
    public LixoSpawner spawner;
    public Vector3[] landingPoints;
    public EcoPickup[] placedWaste = new EcoPickup[0];
    public GameObject[] restoredZones;
    public GameObject[] pollutionZones;
    public GameObject effect;
    public Transform relay;
    public string title;
    [TextArea] public string briefing;
    public int level;
    public float duration;
    public Vector3 startPoint;
    public EcoDrone drone;
    [Tooltip("Som de coleta opcional. Vazio usa o tom sintetizado.")] public AudioClip pickupClip;
    [Tooltip("Árvore que brota quando a fase é concluída. Opcional.")] public GameObject finalTree;

    private static readonly string[] Skills =
    {
        "Corrida (Shift)", "Pulo duplo (Espaço)", "Impulso (Q)", "Radar (F)", "Planar (segure Espaço)"
    };

    private static readonly string[] Restorations =
    {
        "Sementes germinadas", "Solo descontaminado", "Água recuperada",
        "Habitat reativado", "Bioma restaurado"
    };

    private EcoPickup[] pickups;
    private State state;
    private float remaining;
    private float charge = 100f;
    private float yaw;
    private float pitch = 19f;
    private Color contaminatedSky;
    private float dashCooldown;
    private float scanUntil;
    private float messageUntil;
    private float lastRecovery;
    private int collected;
    private int bonus;
    private int unlocked;
    private int restored;
    private string message;
    private AudioSource speaker;
    private AudioClip pickupSound;
    private AudioClip signalSound;
    private AudioClip restoreSound;
    private AudioClip animalSound;
    private GameManager game;
    private GUIStyle heading;
    private GUIStyle body;
    private GUIStyle small;
    private GUIStyle button;

    public bool Playing => state == State.Active;
    public int Collected => collected;
    public int Total => landingPoints.Length + (placedWaste == null ? 0 : placedWaste.Length);
    public bool Won => state == State.Victory;
    /// <summary>Indica se existe uma fase seguinte ativa na lista do build. Sem ela, a vitória volta ao início.</summary>
    private bool HasNextLevel => level < 5 && level < SceneManager.sceneCountInBuildSettings;
    public Vector3 ViewForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

    /// <summary>Carrega os componentes e os sons locais e abre o briefing da fase.</summary>
    private void Start()
    {
        Time.timeScale = 1f;
        Player.Bind(this);
        game = GetComponent<GameManager>();
        speaker = GetComponent<AudioSource>();
        pickupSound = pickupClip != null ? pickupClip : Tone("Reciclagem", 550f, 940f, 0.18f);
        signalSound = Tone("Módulo", 390f, 710f, 0.27f);
        restoreSound = Tone("Renovação", 230f, 520f, 0.8f);
        animalSound = AnimalCall();
        remaining = duration;
        contaminatedSky = view.backgroundColor;
        unlocked = Mathf.Max(0, level - 1);
        yaw = Player.transform.eulerAngles.y;
        SetState(State.Briefing);
    }

    /// <summary>Começa a missão e instancia o lote completo de resíduos, uma única vez.</summary>
    public void Begin()
    {
        if (state != State.Briefing)
            return;
        EcoPickup[] generated = spawner.Spawn(this, landingPoints, System.Environment.TickCount);
        int fixedCount = placedWaste == null ? 0 : placedWaste.Length;
        pickups = new EcoPickup[generated.Length + fixedCount];
        generated.CopyTo(pickups, 0);
        for (int i = 0; i < fixedCount; i++)
        {
            placedWaste[i].Bind(this, placedWaste[i].recoveryPosition);
            pickups[generated.Length + i] = placedWaste[i];
        }
        SetState(State.Active);
        Announce("Leitor ambiental on-line. Recolha os resíduos brilhantes.");
    }

    /// <summary>Atualiza tempo, carga, ações de interface e recuperação em caso de queda.</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            game.Reiniciar();
            return;
        }
        if (Input.GetKeyDown(KeyCode.R) && Playing)
            RainWaste();
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (state == State.Briefing)
                Begin();
            else if (state == State.Victory)
                NextLevel();
            else if (state == State.Defeat)
                game.Reiniciar();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == State.Active)
                SetState(State.Paused);
            else if (state == State.Paused)
                SetState(State.Active);
        }
        if (!Playing)
            return;

        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        charge = Mathf.Min(100f, charge + Time.deltaTime * 4f);
        dashCooldown = Mathf.Max(0f, dashCooldown - Time.deltaTime);
        if (remaining <= 0f)
        {
            SetState(State.Defeat);
            speaker.PlayOneShot(signalSound, 0.45f);
            return;
        }

        bool freeCursor = Input.GetKey(KeyCode.LeftAlt);
        Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = freeCursor;
        if (!freeCursor)
        {
            yaw += Input.GetAxis("Mouse X") * 3.4f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.8f, 8f, 64f);
        }
        Vector3 pos = Player.transform.position;
        if (Time.time - lastRecovery > 0.6f &&
            (pos.y < startPoint.y - 9f || Mathf.Abs(pos.x - startPoint.x) > 44f || Mathf.Abs(pos.z - startPoint.z) > 71f))
        {
            lastRecovery = Time.time;
            remaining = Mathf.Max(0f, remaining - 8f);
            Player.Respawn(startPoint);
            Announce("Robô recuperado no ponto seguro. −8 s");
        }

        if (Input.GetKeyDown(KeyCode.E))
            ActivateCore();
    }

    /// <summary>Segue o robô com câmera orbital e encurta o braço quando encontra geometria.</summary>
    private void LateUpdate()
    {
        Vector3 pivot = Player.transform.position + Vector3.up * 1.45f;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 arm = orbit * new Vector3(0f, 0f, -7.2f);
        int ground = 1 << 3;
        if (Physics.SphereCast(pivot, 0.22f, arm.normalized, out RaycastHit hit, arm.magnitude, ground, QueryTriggerInteraction.Ignore))
            arm = arm.normalized * Mathf.Max(0.5f, hit.distance - 0.15f);
        view.transform.SetPositionAndRotation(pivot + arm, orbit);
    }

    /// <summary>Purifica o núcleo quando o jogador completou a coleta e está perto dele. Retorna se foi ativado.</summary>
    public bool ActivateCore()
    {
        if (!Playing || level != 5 || collected != Total ||
            Vector3.Distance(Player.transform.position, relay.position) >= 4f)
            return false;
        Complete();
        return true;
    }

    /// <summary>Feedback de um módulo de Tecnologia coletado: som, partículas e aviso no HUD.</summary>
    public void TechCollected(Vector3 position, string text)
    {
        speaker.PlayOneShot(signalSound, 0.6f);
        if (effect != null)
            Instantiate(effect, position, Quaternion.identity);
        Announce(text);
    }

    /// <summary>Faz brotar a árvore final com crescimento suave e rajadas de partículas ao redor.</summary>
    private System.Collections.IEnumerator GrowTree()
    {
        Vector3 target = finalTree.transform.localScale;
        Vector3 center = finalTree.transform.position;
        finalTree.transform.localScale = Vector3.zero;
        finalTree.SetActive(true);
        if (effect != null)
        {
            Instantiate(effect, center + Vector3.up * 1f, Quaternion.identity);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                Instantiate(effect, center + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * 5f, Quaternion.identity);
            }
        }
        const float duration = 2.5f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            float eased = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
            finalTree.transform.localScale = target * Mathf.Max(0f, eased);
            yield return null;
        }
        finalTree.transform.localScale = target;
    }

    /// <summary>Faz cair um lote extra de resíduos ao redor do robô. Só rende pontos bônus e não altera a meta da fase.</summary>
    private void RainWaste()
    {
        if (spawner != null)
            spawner.Rain(this, 5, System.Environment.TickCount);
    }

    /// <summary>Registra um resíduo de chuva extra: soma pontos bônus com o mesmo feedback, sem mudar a meta.</summary>
    public void CollectBonus(Vector3 position)
    {
        bonus++;
        speaker.PlayOneShot(pickupSound, 0.5f);
        if (effect != null)
            Instantiate(effect, position, Quaternion.identity);
    }

    /// <summary>Registra uma coleta na posição recebida e dispara feedback, renovação e a condição de vitória.</summary>
    public void Collect(Vector3 position)
    {
        collected++;
        charge = Mathf.Min(100f, charge + 8f);
        speaker.PlayOneShot(pickupSound, 0.5f);
        if (effect != null)
            Instantiate(effect, position, Quaternion.identity);

        int target = Mathf.Min(restoredZones.Length, collected * 5 / Total);
        while (restored < target && (level != 5 || restored < 4))
            RestoreZone();

        if (collected >= Mathf.CeilToInt(Total * 0.4f) && unlocked < level)
        {
            unlocked = level;
            speaker.PlayOneShot(signalSound, 0.55f);
            Announce("Módulo instalado: " + Skills[unlocked - 1]);
        }
        if (collected == Total)
        {
            if (level == 5)
                Announce("O núcleo está pronto. Aproxime-se e pressione E.");
            else
                Complete();
        }
    }

    /// <summary>Recebe um índice de módulo e informa se ele já foi instalado nesta fase.</summary>
    public bool HasSkill(int index) => index >= 0 && index < unlocked;

    /// <summary>Consome 25 de carga para o impulso quando disponível e informa se foi autorizado.</summary>
    public bool UseDash()
    {
        if (!HasSkill(2) || charge < 25f || dashCooldown > 0f)
            return false;
        charge -= 25f;
        dashCooldown = 1.2f;
        speaker.PlayOneShot(signalSound, 0.35f);
        return true;
    }

    /// <summary>Ativa a marcação temporária do resíduo mais próximo se o radar e a carga permitirem.</summary>
    public void Scan()
    {
        if (!HasSkill(3) || charge < 15f || scanUntil > Time.time || collected == Total)
            return;
        charge -= 15f;
        scanUntil = Time.time + 8f;
        speaker.PlayOneShot(signalSound, 0.4f);
        Announce("Radar ativo por 8 s.");
    }

    /// <summary>Troca o próximo conjunto contaminado por sua versão restaurada e exibe o marco ambiental.</summary>
    private void RestoreZone()
    {
        if (restored >= restoredZones.Length)
            return;
        pollutionZones[restored].SetActive(false);
        restoredZones[restored].SetActive(true);
        if (effect != null)
            Instantiate(effect, restoredZones[restored].transform.position, Quaternion.identity);
        Announce(RestorationLabel(restored));
        if (restored == 2)
        {
            // Vida oculta: ao liberar a área bloqueada, a natureza responde com o canto de um animal.
            speaker.PlayOneShot(animalSound, 0.8f);
            Announce(RestorationLabel(restored) + " — ouça: algo vivo respondeu ao seu trabalho!");
        }
        restored++;
        Color sky = Color.Lerp(contaminatedSky, new Color(0.51f, 0.70f, 0.65f), restored / 5f);
        view.backgroundColor = sky;
        RenderSettings.fogColor = sky;
    }

    /// <summary>Recebe o índice de restauração e devolve o texto mostrado para esse marco.</summary>
    private string RestorationLabel(int index)
    {
        return index == 4 && level == 5 ? "A Árvore Dourada renasceu" : Restorations[index];
    }

    /// <summary>Finaliza a renovação, toca o sinal e abre o resumo de vitória.</summary>
    private void Complete()
    {
        if (restored < 5)
            RestoreZone();
        speaker.PlayOneShot(restoreSound, 0.6f);
        if (finalTree != null)
            StartCoroutine(GrowTree());
        SetState(State.Victory);
    }

    /// <summary>Abre a próxima fase da lista ou retorna à primeira depois do encerramento.</summary>
    public void NextLevel()
    {
        if (Won)
            game.Abrir(HasNextLevel ? level : 0);
    }

    /// <summary>Aplica o estado solicitado e sincroniza cursor e pausa.</summary>
    private void SetState(State next)
    {
        state = next;
        Time.timeScale = next == State.Paused ? 0f : 1f;
        Cursor.lockState = Playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !Playing;
    }

    /// <summary>Exibe o texto recebido no HUD por cinco segundos de jogo.</summary>
    public void Announce(string text)
    {
        message = text;
        messageUntil = Time.time + 5f;
    }

    /// <summary>Desenha HUD, botões e telas em uma área de referência de 1280 por 720.</summary>
    private void OnGUI()
    {
        if (heading == null)
            InitStyles();

        Matrix4x4 previous = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        float x = (Screen.width - 1280f * scale) * 0.5f;
        float y = (Screen.height - 720f * scale) * 0.5f;
        GUI.matrix = Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

        DrawHud();
        if (state != State.Active)
            DrawOverlay();
        GUI.matrix = previous;
        if (Playing)
            DrawWorldMarkers();
    }

    /// <summary>Define as fontes, o contraste e o tamanho dos estilos de texto e botão.</summary>
    private void InitStyles()
    {
        heading = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
        body = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true };
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        button = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
        heading.normal.textColor = new Color(0.83f, 1f, 0.73f);
        body.normal.textColor = Color.white;
        small.normal.textColor = new Color(0.76f, 0.88f, 0.81f);
    }

    /// <summary>Mostra dados da missão, módulos ativos e os atalhos do jogador.</summary>
    private void DrawHud()
    {
        Fill(new Rect(24f, 22f, 550f, 111f), new Color(0.04f, 0.10f, 0.08f, 0.94f));
        GUI.Label(new Rect(40f, 27f, 522f, 29f), $"0{level} / 05   {title}", body);
        GUI.Label(new Rect(40f, 57f, 522f, 25f), $"Lixo Coletado: {collected}/{Total}{(bonus > 0 ? $" (+{bonus})" : "")}     Toxicidade {Mathf.CeilToInt(100f * (Total - collected) / Total)}%     {Mathf.CeilToInt(remaining)} s", body);
        Fill(new Rect(41f, 91f, 180f, 5f), new Color(0.23f, 0.32f, 0.25f));
        Fill(new Rect(41f, 91f, 180f * charge / 100f, 5f), new Color(0.62f, 0.93f, 0.35f));
        GUI.Label(new Rect(40f, 100f, 510f, 27f), $"Carga solar {Mathf.FloorToInt(charge)}%   •   Ecossistema {restored}/5", small);
        if (GUI.Button(new Rect(1090f, 24f, 166f, 39f), "Reiniciar", button))
            game.Reiniciar();

        Fill(new Rect(24f, 642f, 1232f, 55f), new Color(0.04f, 0.10f, 0.08f, 0.92f));
        GUI.Label(new Rect(40f, 647f, 1180f, 22f),
            "WASD  mover   •   Mouse  câmera   •   Espaço  pular   •   Alt  soltar o mouse   •   Esc  pausa   •   R  chover lixo   •   Backspace  reiniciar", small);
        string modules = unlocked == 0 ? "Módulo da fase: " + Skills[level - 1] : string.Join("  •  ", Skills, 0, unlocked);
        GUI.Label(new Rect(40f, 668f, 1194f, 25f), modules, small);
        if (Playing && Time.time < messageUntil)
        {
            Fill(new Rect(314f, 565f, 652f, 43f), new Color(0.05f, 0.16f, 0.10f, 0.96f));
            GUI.Label(new Rect(332f, 571f, 619f, 32f), message, body);
        }
    }

    /// <summary>Mostra o painel correspondente ao estado atual e os botões válidos.</summary>
    private void DrawOverlay()
    {
        Fill(new Rect(291f, 190f, 698f, 347f), new Color(0.035f, 0.09f, 0.07f, 0.97f));
        Fill(new Rect(291f, 190f, 698f, 4f), new Color(0.61f, 0.91f, 0.37f));
        string label = title;
        string description = briefing;
        string action = "Iniciar [Enter]";
        switch (state)
        {
            case State.Paused:
                label = "Missão em pausa";
                description = "O cronômetro e a simulação estão pausados. Retome quando estiver pronto.";
                action = "Continuar [Esc]";
                break;
            case State.Victory:
                label = level == 5 ? "O Vale Verde voltou à vida" : "Bioma recuperado";
                description = level == 5
                    ? "A Árvore Dourada renasceu. Cinco regiões foram restauradas pelo Eco-Bot."
                    : HasNextLevel
                        ? $"Você reciclou {Total} resíduos e ativou {Skills[level - 1]}. A próxima região está liberada."
                        : $"Você reciclou todos os {Total} resíduos e a natureza voltou a crescer. Missão cumprida!";
                action = !HasNextLevel ? "Jogar novamente [Enter]" : "Próxima região [Enter]";
                break;
            case State.Defeat:
                label = "Tempo esgotado";
                description = $"Você coletou {collected} de {Total} resíduos. Reinicie para tentar outra rota.";
                action = "Tentar novamente [Enter]";
                break;
        }
        GUI.Label(new Rect(323f, 216f, 640f, 25f), "ECO-BOT  /  PROTOCOLO DE LIMPEZA", small);
        GUI.Label(new Rect(323f, 254f, 640f, 61f), label, heading);
        GUI.Label(new Rect(323f, 324f, 622f, 102f), description, body);
        if (!GUI.Button(new Rect(323f, 443f, 335f, 51f), action, button))
            return;
        switch (state)
        {
            case State.Briefing: Begin(); break;
            case State.Paused: SetState(State.Active); break;
            case State.Victory: NextLevel(); break;
            case State.Defeat: game.Reiniciar(); break;
        }
    }

    /// <summary>Projeta marcadores de resíduos próximos, alvo do radar ou núcleo no final.</summary>
    private void DrawWorldMarkers()
    {
        if (collected == Total && level == 5)
        {
            Mark(relay.position + Vector3.up * 1.7f, "Núcleo [E]", new Color(1f, 0.79f, 0.22f));
            return;
        }
        EcoPickup closest = null;
        float bestDistance = float.MaxValue;
        foreach (EcoPickup item in pickups)
        {
            if (item == null)
                continue;
            Vector3 position = item.transform.position;
            float distance = (position - Player.transform.position).sqrMagnitude;
            if (distance < 100f)
                Mark(position + Vector3.up * 0.8f, "•", new Color(0.68f, 1f, 0.29f));
            if (distance < bestDistance)
            {
                closest = item;
                bestDistance = distance;
            }
        }
        if (closest != null && scanUntil > Time.time)
            Mark(closest.transform.position + Vector3.up * 1.1f, $"Radar {Mathf.RoundToInt(Mathf.Sqrt(bestDistance))} m", Color.cyan);
    }

    /// <summary>Projeta a posição recebida na tela e desenha o rótulo na cor informada.</summary>
    private void Mark(Vector3 world, string label, Color color)
    {
        Vector3 screen = view.WorldToScreenPoint(world);
        if (screen.z < 0f)
            return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(screen.x - 12f, Screen.height - screen.y - 20f, 145f, 23f), label);
        GUI.color = previous;
    }

    /// <summary>Pinta o retângulo recebido com a cor informada e restaura a cor da interface.</summary>
    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>Libera os sons sintetizados quando a cena for descarregada.</summary>
    private void OnDestroy()
    {
        if (pickupSound != null && pickupSound != pickupClip) Destroy(pickupSound);
        if (signalSound != null) Destroy(signalSound);
        if (restoreSound != null) Destroy(restoreSound);
        if (animalSound != null) Destroy(animalSound);
    }

    /// <summary>Sintetiza e devolve um som mono que varre as frequências informadas ao longo da duração.</summary>
    private static AudioClip Tone(string name, float from, float to, float seconds)
    {
        const int rate = 22050;
        float[] samples = new float[Mathf.RoundToInt(rate * seconds)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)samples.Length;
            phase += Mathf.Lerp(from, to, t) * 2f * Mathf.PI / rate;
            float envelope = Mathf.Min(1f, t * 28f) * Mathf.Pow(1f - t, 1.8f);
            samples[i] = (Mathf.Sin(phase) + 0.23f * Mathf.Sin(phase * 2f)) * envelope * 0.35f;
        }
        AudioClip clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Sintetiza um piado curto de pássaro (três notas agudas com vibrato) para a surpresa da vida oculta.</summary>
    private static AudioClip AnimalCall()
    {
        const int rate = 22050;
        float[] notes = { 2300f, 2900f, 2500f };
        int per = Mathf.RoundToInt(rate * 0.16f);
        int gap = Mathf.RoundToInt(rate * 0.07f);
        float[] samples = new float[(per + gap) * notes.Length];
        float phase = 0f;
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < per; i++)
            {
                float t = i / (float)per;
                float freq = notes[n] * (1f + 0.06f * Mathf.Sin(t * 40f)) * Mathf.Lerp(0.92f, 1.08f, t);
                phase += freq * 2f * Mathf.PI / rate;
                samples[n * (per + gap) + i] = Mathf.Sin(phase) * Mathf.Sin(t * Mathf.PI) * 0.35f;
            }
        AudioClip clip = AudioClip.Create("Animal", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
