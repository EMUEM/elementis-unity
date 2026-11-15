// UIFlowManager.cs — Elementis UI flow (Phase 2b; G1/G2 fixed 2026-11-15).
//
// Flow: Lobby -> Setup -> VS -> Countdown -> Fight -> (round banners) ->
// KO / Match End -> Rematch | Lobby. Pause overlays the fight.
//
// HOW THE FLOW STARTS THE MATCH (FightBootstrap.BeginMatch):
// FightBootstrap.Awake spawns the arena + the default pair (Katara =
// player, Azula = bot) as the live lobby backdrop only — fighters
// Frozen, RoundManager Idle, no rounds run. The match does NOT begin
// until BeginFight() calls FightBootstrap.BeginMatch(playerHero,
// botHero, difficulty) with the player's actual pick: the chosen hero
// spawns as the player (SpawnA), the other as the bot (SpawnB), the
// chosen difficulty is applied via BotAI.SetDifficulty, and the match
// starts with the 3-2-1 countdown. A hero swap respawns the fighters,
// so BeginFight refreshes its player/bot references and re-binds the
// HUD after BeginMatch. Every fight start — first, rematch, restart
// from pause — goes through this one method.
// Menus run UNFROZEN (Time.timeScale = 1): there is no match to hold
// back any more, so the old timeScale = 0 freeze workaround is gone.
// Pause is still Time.timeScale = 0 + overlay (RoundManager has no
// pause API); quit-to-lobby freezes the fighters too.
//
// Screens are built at runtime under ONE Screen Space - Overlay canvas
// (reference 1920x1080, match 0.5) with a SafeArea child container, so
// every control sits inside the device safe area on the 14T and any
// other phone. Menu buttons call UIFlowManager methods directly; fight
// buttons are Input System on-screen controls (TouchControls.cs).
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public class UIFlowManager : MonoBehaviour
{
    public enum FlowState { Lobby, Setup, Versus, Fight, Paused, MatchEnd }

    // --- LAYOUT (reference 1920x1080 landscape, px; see UI-NOTES.md) ---
    public const float LobbyTitleTop = -90f;
    public const float LobbyStartBottom = 130f;   // START centre from bottom
    public const float LobbyStartWidth = 420f;
    public const float LobbyStartHeight = 110f;
    public const float SetupTitleTop = -48f;
    public const float SetupCardWidth = 560f;
    public const float SetupCardHeight = 300f;
    public const float SetupCardTop = -230f;      // card centre from top
    public const float SetupCardGapX = 300f;      // card centre x offset
    public const float SetupDiffTop = -420f;      // difficulty row centre
    public const float SetupButtonsTop = -495f;   // FIGHT/BACK row centre
    public const float VersusPlateX = 440f;
    public const float VersusPlateY = 60f;
    public const float VersusFightBottom = -220f; // FIGHT centre from middle
    public const float PausePanelWidth = 640f;
    public const float PausePanelHeight = 440f;
    public const float MatchEndPanelWidth = 720f;
    public const float MatchEndPanelHeight = 460f;
    public const float VersusAutoSeconds = 3.0f;  // auto-advance delay

    FlowState state = FlowState.Lobby;
    string selectedHeroId = "katara"; // "katara" | "azula"
    BotDifficulty selectedDifficulty = BotDifficulty.Medium;

    FightBootstrap bootstrap;
    RoundManager rounds;
    FighterController player, bot;

    Canvas canvas;
    RectTransform safeContainer;
    GameObject lobbyPanel, setupPanel, versusPanel, pausePanel, matchEndPanel;
    GameObject bannerLayer;
    Text countdownText, roundBannerText, koBannerText;
    Text versusPlayerName, versusPlayerElement, versusBotName, versusBotElement;
    Text versusDiffText, versusAutoText;
    Text matchEndTitle, matchEndScore;
    Button kataraCardButton, azulaCardButton;
    GameObject kataraBorder, azulaBorder;
    Text kataraTag, azulaTag;
    Button[] difficultyButtons = new Button[3];
    Text pauseMuteLabel;
    FightHud hud;
    LobbyShowcase showcase;

    float countdownLeft, roundBannerLeft, koLeft, vsAutoLeft;
    bool vsAutoArmed;

    void Awake()
    {
        EnsureEventSystem();
        BuildCanvas();
        BuildBannerLayer();
        BuildLobby();
        BuildSetup();
        BuildVersus();
        BuildPause();
        BuildMatchEnd();
        hud = gameObject.AddComponent<FightHud>();
        hud.Build(safeContainer, canvas);
        showcase = gameObject.AddComponent<LobbyShowcase>();
    }

    void Start()
    {
        var fightGo = GameObject.Find("Fight");
        bootstrap = fightGo != null ? fightGo.GetComponent<FightBootstrap>() : null;
        if (bootstrap == null)
        {
            Debug.LogError("[UIFlowManager] 'Fight' object / FightBootstrap not found — flow cannot start a match.");
        }
        else
        {
            rounds = bootstrap.roundManager;
            player = bootstrap.PlayerFighter;
            bot = bootstrap.BotFighter;
        }
        if (rounds != null)
        {
            rounds.CountdownTick += OnCountdownTick;
            rounds.RoundStarted += OnRoundStarted;
            rounds.RoundEnded += OnRoundEnded;
            rounds.MatchEnded += OnMatchEnded;
        }
        if (player != null && bot != null && rounds != null)
            hud.Bind(player, bot, rounds, OnPausePressed, MatchSubLabel());
        if (player != null) showcase.SetFighter(player);
        ShowLobby();
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
#if UNITY_INPUT_SYSTEM
        // No actions asset assigned: InputSystemUIInputModule assigns its
        // built-in default UI actions in OnEnable (verified in the 1.14.0
        // package source). Multi-touch works: each finger is a pointer.
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildCanvas()
    {
        var go = new GameObject("ElementisUICanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        var safeGo = new GameObject("SafeArea");
        safeGo.transform.SetParent(go.transform, false);
        safeContainer = UiKit.Stretch(safeGo);
        safeGo.AddComponent<SafeAreaFitter>();
    }

    // --- Banner layer (countdown / round / KO, above the HUD) ---
    void BuildBannerLayer()
    {
        bannerLayer = new GameObject("BannerLayer");
        bannerLayer.transform.SetParent(safeContainer, false);
        UiKit.Stretch(bannerLayer);
        countdownText = UiKit.Label(bannerLayer.transform, "CountdownText", "", 200,
            UiKit.Gold);
        countdownText.fontStyle = FontStyle.Bold;
        UiKit.Rect(countdownText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(900f, 260f), new Vector2(0.5f, 0.5f));
        countdownText.gameObject.SetActive(false);
        roundBannerText = UiKit.Label(bannerLayer.transform, "RoundBannerText", "", 96,
            UiKit.Gold);
        roundBannerText.fontStyle = FontStyle.Bold;
        UiKit.Rect(roundBannerText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 340f), new Vector2(1000f, 130f), new Vector2(0.5f, 0.5f));
        roundBannerText.gameObject.SetActive(false);
        koBannerText = UiKit.Label(bannerLayer.transform, "KoBannerText", "", 110,
            UiKit.EnemyRed);
        koBannerText.fontStyle = FontStyle.Bold;
        UiKit.Rect(koBannerText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1100f, 150f), new Vector2(0.5f, 0.5f));
        koBannerText.gameObject.SetActive(false);
    }

    // --- Lobby: ONLY a Start option + the live Katara showcase behind ---
    void BuildLobby()
    {
        lobbyPanel = new GameObject("LobbyPanel");
        lobbyPanel.transform.SetParent(safeContainer, false);
        UiKit.Stretch(lobbyPanel); // no background: the arena + Katara show through
        var title = UiKit.GoldTitle(lobbyPanel.transform, "Title", "ELEMENTIS", 120);
        UiKit.Rect(title, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, LobbyTitleTop), new Vector2(900f, 160f), new Vector2(0.5f, 0.5f));
        var sub = UiKit.Label(lobbyPanel.transform, "Subtitle",
            "WATER  vs  FIRE   •   BEST OF 3", 28, Color.white);
        UiKit.Rect(sub.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, LobbyTitleTop - 110f), new Vector2(700f, 40f),
            new Vector2(0.5f, 0.5f));
        var start = UiKit.MakeButton(lobbyPanel.transform, "StartButton", "START",
            new Vector2(LobbyStartWidth, LobbyStartHeight), UiKit.Gold, 44,
            new Color(0.15f, 0.08f, 0.02f), false);
        UiKit.Rect(start.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, LobbyStartBottom),
            new Vector2(LobbyStartWidth, LobbyStartHeight), new Vector2(0.5f, 0.5f));
        start.onClick.AddListener(() => { AudioManager.Play("ui_tap"); ShowSetup(); });
        var foot = UiKit.Label(lobbyPanel.transform, "Footer",
            "KATARA  vs  AZULA  •  v1", 18, new Color(1f, 1f, 1f, 0.65f));
        UiKit.Rect(foot.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 36f), new Vector2(500f, 26f), new Vector2(0.5f, 0.5f));
    }

    // --- Setup: fighter cards + bot difficulty + FIGHT / BACK ---
    void BuildSetup()
    {
        setupPanel = UiKit.Panel(safeContainer, "SetupPanel",
            new Color(0.05f, 0.04f, 0.04f, 0.72f));
        setupPanel.GetComponent<Image>().raycastTarget = true; // block taps through
        var title = UiKit.Label(setupPanel.transform, "Title", "CHOOSE YOUR FIGHTER",
            40, UiKit.Gold);
        title.fontStyle = FontStyle.Bold;
        UiKit.Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, SetupTitleTop), new Vector2(800f, 56f), new Vector2(0.5f, 0.5f));

        kataraCardButton = BuildFighterCard(setupPanel.transform, "katara",
            new Vector2(-SetupCardGapX, SetupCardTop), out kataraBorder, out kataraTag);
        azulaCardButton = BuildFighterCard(setupPanel.transform, "azula",
            new Vector2(SetupCardGapX, SetupCardTop), out azulaBorder, out azulaTag);
        kataraCardButton.onClick.AddListener(() => SelectHero("katara"));
        azulaCardButton.onClick.AddListener(() => SelectHero("azula"));

        var diffLabel = UiKit.Label(setupPanel.transform, "DifficultyLabel",
            "BOT DIFFICULTY", 24, Color.white);
        UiKit.Rect(diffLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-380f, SetupDiffTop), new Vector2(200f, 32f), new Vector2(0.5f, 0.5f));
        string[] diffs = { "EASY", "MEDIUM", "HARD" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i; // capture for the listener
            var b = UiKit.MakeButton(setupPanel.transform, "Diff" + diffs[i], diffs[i],
                new Vector2(200f, 64f), UiKit.ButtonGrey, 26, Color.white, false);
            UiKit.Rect(b.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-120f + i * 220f, SetupDiffTop), new Vector2(200f, 64f),
                new Vector2(0.5f, 0.5f));
            b.onClick.AddListener(() => SelectDifficulty((BotDifficulty)idx));
            difficultyButtons[i] = b;
        }
        var fight = UiKit.MakeButton(setupPanel.transform, "SetupFightButton", "FIGHT",
            new Vector2(420f, 84f), UiKit.Gold, 36, new Color(0.15f, 0.08f, 0.02f), false);
        UiKit.Rect(fight.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(120f, SetupButtonsTop), new Vector2(420f, 84f), new Vector2(0.5f, 0.5f));
        fight.onClick.AddListener(() => { AudioManager.Play("ui_tap"); ShowVersus(); });
        var back = UiKit.MakeButton(setupPanel.transform, "SetupBackButton", "BACK",
            new Vector2(200f, 84f), UiKit.ButtonGrey, 28, Color.white, false);
        UiKit.Rect(back.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-260f, SetupButtonsTop), new Vector2(200f, 84f), new Vector2(0.5f, 0.5f));
        back.onClick.AddListener(() => { AudioManager.Play("ui_tap"); ShowLobby(); });
        SelectHero("katara");
        SelectDifficulty(BotDifficulty.Medium);
    }

    // One fighter card: name, element, the 4 real skills (damage / mana /
    // cooldown from FighterDataLibrary — the same numbers as the .assets).
    Button BuildFighterCard(Transform parent, string heroId, Vector2 centreFromTop,
        out GameObject border, out Text tag)
    {
        var data = FighterDataLibrary.Create(heroId);
        Color accent = data != null ? data.accentColor : Color.white;
        string name = data != null ? data.displayName.ToUpper() : heroId.ToUpper();
        string element = data != null ? data.element.ToUpper() : "";

        border = new GameObject(heroId + "Border");
        border.transform.SetParent(parent, false);
        var borderImg = border.AddComponent<Image>();
        borderImg.sprite = UiKit.Square;
        borderImg.color = UiKit.Gold;
        borderImg.raycastTarget = false;
        UiKit.Rect(border, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            centreFromTop, new Vector2(SetupCardWidth + 8f, SetupCardHeight + 8f),
            new Vector2(0.5f, 0.5f));
        border.SetActive(false);

        var btn = UiKit.MakeButton(parent, heroId + "Card", "",
            new Vector2(SetupCardWidth, SetupCardHeight), UiKit.PanelDarkSolid,
            20, Color.white, false);
        UiKit.Rect(btn.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            centreFromTop, new Vector2(SetupCardWidth, SetupCardHeight),
            new Vector2(0.5f, 0.5f));
        // Remove the empty label MakeButton added (the card builds its own).
        var emptyLabel = btn.transform.Find("Label");
        if (emptyLabel != null) UnityEngine.Object.Destroy(emptyLabel.gameObject);

        var nameText = UiKit.Label(btn.transform, "Name", name, 36, accent);
        nameText.fontStyle = FontStyle.Bold;
        UiKit.Rect(nameText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -18f), new Vector2(520f, 46f), new Vector2(0.5f, 0.5f));
        var elText = UiKit.Label(btn.transform, "Element", element, 22,
            new Color(1f, 1f, 1f, 0.8f));
        UiKit.Rect(elText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -58f), new Vector2(520f, 30f), new Vector2(0.5f, 0.5f));
        if (data != null)
        {
            for (int i = 0; i < 4 && i < data.skills.Length; i++)
            {
                var s = data.skills[i];
                var line = UiKit.Label(btn.transform, "Skill" + (i + 1),
                    s.shortName + "  —  " + Mathf.RoundToInt(s.damage) + " dmg  •  " +
                    Mathf.RoundToInt(s.cost) + " mana  •  " + s.cooldown.ToString("0.0") + "s",
                    19, Color.white, TextAnchor.MiddleLeft);
                UiKit.Rect(line.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -104f - i * 30f), new Vector2(500f, 28f),
                    new Vector2(0.5f, 0.5f));
            }
        }
        tag = UiKit.Label(btn.transform, "SelectedTag", "YOU", 22, UiKit.Gold);
        tag.fontStyle = FontStyle.Bold;
        UiKit.Rect(tag.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 16f), new Vector2(200f, 30f), new Vector2(0.5f, 0.5f));
        tag.gameObject.SetActive(false);
        return btn;
    }

    void SelectHero(string heroId)
    {
        selectedHeroId = heroId;
        bool katara = heroId == "katara";
        if (kataraBorder != null) kataraBorder.SetActive(katara);
        if (azulaBorder != null) azulaBorder.SetActive(!katara);
        if (kataraTag != null) kataraTag.gameObject.SetActive(katara);
        if (azulaTag != null) azulaTag.gameObject.SetActive(!katara);
        // Silent while the setup screen is still being built (inactive).
        if (setupPanel != null && setupPanel.activeInHierarchy)
            AudioManager.Play("ui_tap", 0.7f);
    }

    void SelectDifficulty(BotDifficulty d)
    {
        selectedDifficulty = d;
        for (int i = 0; i < 3; i++)
        {
            if (difficultyButtons[i] == null) continue;
            var img = difficultyButtons[i].GetComponent<Image>();
            if (img != null)
                img.color = (BotDifficulty)i == d ? UiKit.Gold : UiKit.ButtonGrey;
            var label = difficultyButtons[i].GetComponentInChildren<Text>();
            if (label != null)
                label.color = (BotDifficulty)i == d
                    ? new Color(0.15f, 0.08f, 0.02f) : Color.white;
        }
        // Silent while the setup screen is still being built (inactive).
        if (setupPanel != null && setupPanel.activeInHierarchy)
            AudioManager.Play("ui_tap", 0.7f);
    }

    // --- VS screen: plates, gold VS, BEST OF 3, FIGHT, auto-advance ---
    void BuildVersus()
    {
        versusPanel = UiKit.Panel(safeContainer, "VersusPanel",
            new Color(0.05f, 0.04f, 0.04f, 0.45f));
        versusPlayerName = UiKit.Label(versusPanel.transform, "PlayerName", "KATARA",
            64, UiKit.KataraBlue);
        versusPlayerName.fontStyle = FontStyle.Bold;
        UiKit.Rect(versusPlayerName.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-VersusPlateX, VersusPlateY + 40f), new Vector2(560f, 80f),
            new Vector2(0.5f, 0.5f));
        versusPlayerElement = UiKit.Label(versusPanel.transform, "PlayerElement",
            "WATER", 28, Color.white);
        UiKit.Rect(versusPlayerElement.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-VersusPlateX, VersusPlateY - 30f), new Vector2(400f, 38f),
            new Vector2(0.5f, 0.5f));
        versusBotName = UiKit.Label(versusPanel.transform, "BotName", "AZULA",
            64, UiKit.AzulaRed);
        versusBotName.fontStyle = FontStyle.Bold;
        UiKit.Rect(versusBotName.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(VersusPlateX, VersusPlateY + 40f), new Vector2(560f, 80f),
            new Vector2(0.5f, 0.5f));
        versusBotElement = UiKit.Label(versusPanel.transform, "BotElement",
            "FIRE", 28, Color.white);
        UiKit.Rect(versusBotElement.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(VersusPlateX, VersusPlateY - 30f), new Vector2(400f, 38f),
            new Vector2(0.5f, 0.5f));
        var vs = UiKit.GoldTitle(versusPanel.transform, "VsTitle", "VS", 140);
        UiKit.Rect(vs, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, VersusPlateY), new Vector2(300f, 180f), new Vector2(0.5f, 0.5f));
        var bestOf = UiKit.Label(versusPanel.transform, "BestOf", "BEST OF 3", 32,
            UiKit.Gold);
        bestOf.fontStyle = FontStyle.Bold;
        UiKit.Rect(bestOf.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(400f, 44f), new Vector2(0.5f, 0.5f));
        versusDiffText = UiKit.Label(versusPanel.transform, "DiffText", "BOT — MEDIUM",
            24, Color.white);
        UiKit.Rect(versusDiffText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -110f), new Vector2(400f, 34f), new Vector2(0.5f, 0.5f));
        var fight = UiKit.MakeButton(versusPanel.transform, "VersusFightButton", "FIGHT",
            new Vector2(420f, 100f), UiKit.Gold, 40, new Color(0.15f, 0.08f, 0.02f), false);
        UiKit.Rect(fight.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, VersusFightBottom), new Vector2(420f, 100f),
            new Vector2(0.5f, 0.5f));
        fight.onClick.AddListener(() => { AudioManager.Play("ui_tap"); BeginFight(); });
        versusAutoText = UiKit.Label(versusPanel.transform, "AutoText",
            "FIGHT STARTS IN 3", 22, new Color(1f, 1f, 1f, 0.75f));
        UiKit.Rect(versusAutoText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, VersusFightBottom - 80f), new Vector2(400f, 30f),
            new Vector2(0.5f, 0.5f));
        var back = UiKit.MakeButton(versusPanel.transform, "VersusBackButton", "BACK",
            new Vector2(180f, 64f), UiKit.ButtonGrey, 26, Color.white, false);
        UiKit.Rect(back.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(150f, -50f), new Vector2(180f, 64f), new Vector2(0.5f, 0.5f));
        back.onClick.AddListener(() => { AudioManager.Play("ui_tap"); ShowSetup(); });
    }

    // --- Pause overlay ---
    void BuildPause()
    {
        pausePanel = UiKit.Panel(safeContainer, "PausePanel",
            new Color(0.03f, 0.03f, 0.03f, 0.78f));
        pausePanel.GetComponent<Image>().raycastTarget = true;
        var box = new GameObject("PauseBox");
        box.transform.SetParent(pausePanel.transform, false);
        var boxImg = box.AddComponent<Image>();
        boxImg.sprite = UiKit.Square;
        boxImg.color = UiKit.PanelDarkSolid;
        boxImg.raycastTarget = false;
        UiKit.Rect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PausePanelWidth, PausePanelHeight),
            new Vector2(0.5f, 0.5f));
        var title = UiKit.Label(box.transform, "Title", "PAUSED", 56, UiKit.Gold);
        title.fontStyle = FontStyle.Bold;
        UiKit.Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(400f, 70f), new Vector2(0.5f, 0.5f));
        string[] labels = { "RESUME", "RESTART MATCH", "SOUND: ON", "QUIT TO LOBBY" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var b = UiKit.MakeButton(box.transform, "Pause" + labels[i], labels[i],
                new Vector2(360f, 72f), UiKit.ButtonGrey, 26, Color.white, false);
            UiKit.Rect(b.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -120f - i * 84f), new Vector2(360f, 72f),
                new Vector2(0.5f, 0.5f));
            b.onClick.AddListener(() => OnPauseMenuButton(idx));
            if (i == 2)
                pauseMuteLabel = b.GetComponentInChildren<Text>();
        }
    }

    void OnPauseMenuButton(int idx)
    {
        AudioManager.Play("ui_tap");
        switch (idx)
        {
            case 0: ResumeFight(); break;
            case 1: BeginFight(); break;
            case 2:
                AudioManager.Muted = !AudioManager.Muted;
                RefreshPauseMuteLabel();
                if (hud != null) hud.RefreshMuteLabel();
                break;
            case 3: QuitToLobby(); break;
        }
    }

    void RefreshPauseMuteLabel()
    {
        if (pauseMuteLabel != null)
            pauseMuteLabel.text = AudioManager.Muted ? "SOUND: OFF" : "SOUND: ON";
    }

    // --- Match-end panel ---
    void BuildMatchEnd()
    {
        matchEndPanel = UiKit.Panel(safeContainer, "MatchEndPanel",
            new Color(0.03f, 0.03f, 0.03f, 0.78f));
        matchEndPanel.GetComponent<Image>().raycastTarget = true;
        var box = new GameObject("MatchEndBox");
        box.transform.SetParent(matchEndPanel.transform, false);
        var boxImg = box.AddComponent<Image>();
        boxImg.sprite = UiKit.Square;
        boxImg.color = UiKit.PanelDarkSolid;
        boxImg.raycastTarget = false;
        UiKit.Rect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(MatchEndPanelWidth, MatchEndPanelHeight),
            new Vector2(0.5f, 0.5f));
        matchEndTitle = UiKit.Label(box.transform, "Title", "VICTORY", 72, UiKit.Gold);
        matchEndTitle.fontStyle = FontStyle.Bold;
        UiKit.Rect(matchEndTitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -30f), new Vector2(640f, 90f), new Vector2(0.5f, 0.5f));
        matchEndScore = UiKit.Label(box.transform, "Score", "", 32, Color.white);
        UiKit.Rect(matchEndScore.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(640f, 44f), new Vector2(0.5f, 0.5f));
        var rematch = UiKit.MakeButton(box.transform, "RematchButton", "REMATCH",
            new Vector2(360f, 84f), UiKit.Gold, 30, new Color(0.15f, 0.08f, 0.02f), false);
        UiKit.Rect(rematch.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -230f), new Vector2(360f, 84f), new Vector2(0.5f, 0.5f));
        rematch.onClick.AddListener(() => { AudioManager.Play("ui_tap"); BeginFight(); });
        var lobby = UiKit.MakeButton(box.transform, "MatchEndLobbyButton", "LOBBY",
            new Vector2(360f, 84f), UiKit.ButtonGrey, 30, Color.white, false);
        UiKit.Rect(lobby.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -330f), new Vector2(360f, 84f), new Vector2(0.5f, 0.5f));
        lobby.onClick.AddListener(() => { AudioManager.Play("ui_tap"); QuitToLobby(); });
    }

    // --- State transitions ---
    void HideAllPanels()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (setupPanel != null) setupPanel.SetActive(false);
        if (versusPanel != null) versusPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (matchEndPanel != null) matchEndPanel.SetActive(false);
        vsAutoArmed = false;
    }

    void FreezeFighters(bool frozen)
    {
        if (player != null) player.Frozen = frozen;
        if (bot != null) bot.Frozen = frozen;
    }

    public void ShowLobby()
    {
        state = FlowState.Lobby;
        Time.timeScale = 1f; // menus run live (no match running to freeze)
        FreezeFighters(true);
        HideAllPanels();
        if (hud != null) hud.Hide();
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        // The showcase follows the current player fighter — after a hero
        // swap the spawned player is a different object (BeginMatch).
        if (showcase != null && bootstrap != null && bootstrap.PlayerFighter != null)
            showcase.SetFighter(bootstrap.PlayerFighter);
        if (showcase != null) showcase.StartShowcase();
    }

    void ShowSetup()
    {
        state = FlowState.Setup;
        HideAllPanels();
        if (hud != null) hud.Hide();
        if (setupPanel != null) setupPanel.SetActive(true);
        if (showcase != null) showcase.StartShowcase();
    }

    void ShowVersus()
    {
        state = FlowState.Versus;
        HideAllPanels();
        if (hud != null) hud.Hide();
        // Plates reflect the pick — and the fight now spawns exactly this
        // pairing (FightBootstrap.BeginMatch).
        var picked = FighterDataLibrary.Create(selectedHeroId);
        string otherId = selectedHeroId == "katara" ? "azula" : "katara";
        var other = FighterDataLibrary.Create(otherId);
        if (versusPlayerName != null)
        {
            versusPlayerName.text = picked != null ? picked.displayName.ToUpper() : selectedHeroId.ToUpper();
            versusPlayerName.color = picked != null ? picked.accentColor : Color.white;
        }
        if (versusPlayerElement != null)
            versusPlayerElement.text = picked != null ? picked.element.ToUpper() + "  •  YOU" : "YOU";
        if (versusBotName != null)
        {
            versusBotName.text = other != null ? other.displayName.ToUpper() : otherId.ToUpper();
            versusBotName.color = other != null ? other.accentColor : Color.white;
        }
        if (versusBotElement != null)
            versusBotElement.text = other != null ? other.element.ToUpper() + "  •  BOT" : "BOT";
        if (versusDiffText != null)
            versusDiffText.text = "BOT — " + selectedDifficulty.ToString().ToUpper();
        if (versusPanel != null) versusPanel.SetActive(true);
        vsAutoLeft = VersusAutoSeconds;
        vsAutoArmed = true;
        if (showcase != null) showcase.StartShowcase();
    }

    // THE fight start (first fight, rematch, restart-from-pause).
    public void BeginFight()
    {
        if (bootstrap == null)
        {
            Debug.LogError("[UIFlowManager] BeginFight: FightBootstrap missing — cannot start.");
            return;
        }
        string botHeroId = selectedHeroId == "katara" ? "azula" : "katara";
        // Spawns the picked hero as the player, the other as the bot,
        // applies the difficulty, and starts the match (3-2-1 countdown).
        // A hero swap respawns the fighters, so refresh the references
        // before binding the HUD.
        if (!bootstrap.BeginMatch(selectedHeroId, botHeroId, selectedDifficulty))
        {
            Debug.LogError("[UIFlowManager] BeginFight: FightBootstrap.BeginMatch failed — cannot start.");
            return;
        }
        player = bootstrap.PlayerFighter;
        bot = bootstrap.BotFighter;
        rounds = bootstrap.roundManager;
        if (player == null || bot == null || rounds == null)
        {
            Debug.LogError("[UIFlowManager] BeginFight: fighters/rounds missing after BeginMatch — cannot start.");
            return;
        }
        state = FlowState.Fight;
        vsAutoArmed = false;
        HideAllPanels();
        if (showcase != null) showcase.StopShowcase();
        if (hud != null)
        {
            hud.Bind(player, bot, rounds, OnPausePressed, MatchSubLabel());
            hud.Show();
        }
        Time.timeScale = 1f;
    }

    string MatchSubLabel()
    {
        var picked = FighterDataLibrary.Create(selectedHeroId);
        string otherId = selectedHeroId == "katara" ? "azula" : "katara";
        var other = FighterDataLibrary.Create(otherId);
        string a = picked != null ? picked.displayName.ToUpper() : selectedHeroId.ToUpper();
        string b = other != null ? other.displayName.ToUpper() : otherId.ToUpper();
        return a + " vs " + b + "  •  " + selectedDifficulty.ToString().ToUpper();
    }

    void OnPausePressed()
    {
        if (state != FlowState.Fight) return;
        state = FlowState.Paused;
        Time.timeScale = 0f;
        RefreshPauseMuteLabel();
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    void ResumeFight()
    {
        if (state != FlowState.Paused) return;
        state = FlowState.Fight;
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    void QuitToLobby()
    {
        FreezeFighters(true);
        ShowLobby();
    }

    // --- Round / countdown events (GAME-NOTES.md contract) ---
    void OnCountdownTick(int n)
    {
        if (state != FlowState.Fight || countdownText == null) return;
        if (n == 0)
        {
            countdownText.text = "FIGHT!";
            countdownText.color = UiKit.Gold;
        }
        else
        {
            countdownText.text = n.ToString();
            countdownText.color = UiKit.Gold;
        }
        countdownText.gameObject.SetActive(true);
        countdownLeft = n == 0 ? 0.7f : 0.8f; // RoundManager ticks every 0.8 s
    }

    void OnRoundStarted(int round)
    {
        if (state != FlowState.Fight || roundBannerText == null) return;
        roundBannerText.text = "ROUND " + round;
        roundBannerText.gameObject.SetActive(true);
        roundBannerLeft = 1.2f;
    }

    void OnRoundEnded(FighterController winner, RoundEndReason reason)
    {
        if (state != FlowState.Fight || koBannerText == null) return;
        switch (reason)
        {
            case RoundEndReason.KO:
                koBannerText.text = "K.O.!";
                koBannerText.color = UiKit.EnemyRed;
                break;
            case RoundEndReason.Timeout:
                koBannerText.text = "TIME UP";
                koBannerText.color = UiKit.Gold;
                break;
            default:
                koBannerText.text = "DRAW";
                koBannerText.color = Color.white;
                break;
        }
        koBannerText.gameObject.SetActive(true);
        koLeft = 2.4f; // RoundManager waits 2.5 s before the next round
    }

    void OnMatchEnded(FighterController winner)
    {
        state = FlowState.MatchEnd;
        vsAutoArmed = false;
        HideAllPanels();
        if (hud != null) hud.Hide();
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (roundBannerText != null) roundBannerText.gameObject.SetActive(false);
        if (koBannerText != null) koBannerText.gameObject.SetActive(false);
        bool playerWon = winner == player;
        if (matchEndTitle != null)
        {
            matchEndTitle.text = playerWon ? "VICTORY" : "DEFEAT";
            matchEndTitle.color = playerWon ? UiKit.Gold : UiKit.EnemyRed;
        }
        if (matchEndScore != null && rounds != null)
        {
            string w = winner != null && winner.Data != null
                ? winner.Data.displayName.ToUpper() : "—";
            matchEndScore.text = w + " WINS  •  " + rounds.WinsA + " — " + rounds.WinsB;
        }
        if (matchEndPanel != null) matchEndPanel.SetActive(true);
    }

    void Update()
    {
        // Banner/auto-advance timers run on unscaled time, so they keep
        // ticking while paused (and behave identically at timeScale 1).
        if (countdownLeft > 0f)
        {
            countdownLeft -= Time.unscaledDeltaTime;
            if (countdownLeft <= 0f && countdownText != null)
                countdownText.gameObject.SetActive(false);
        }
        if (roundBannerLeft > 0f)
        {
            roundBannerLeft -= Time.unscaledDeltaTime;
            if (roundBannerLeft <= 0f && roundBannerText != null)
                roundBannerText.gameObject.SetActive(false);
        }
        if (koLeft > 0f)
        {
            koLeft -= Time.unscaledDeltaTime;
            if (koLeft <= 0f && koBannerText != null)
                koBannerText.gameObject.SetActive(false);
        }
        if (state == FlowState.Versus && vsAutoArmed)
        {
            vsAutoLeft -= Time.unscaledDeltaTime;
            if (versusAutoText != null)
                versusAutoText.text = "FIGHT STARTS IN " +
                    Mathf.Max(1, Mathf.CeilToInt(vsAutoLeft));
            if (vsAutoLeft <= 0f)
            {
                vsAutoArmed = false;
                BeginFight();
            }
        }
    }
}

// SafeAreaFitter — insets a full-stretch container to Screen.safeArea
// (camera cutout + gesture bar) so every control stays tappable.
// Lives in this file: it is part of the flow manager's canvas setup.
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;
    Rect lastSafe;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafe) Apply();
    }

    void Apply()
    {
        if (rt == null) return;
        lastSafe = Screen.safeArea;
        Vector2 min = lastSafe.min;
        Vector2 max = lastSafe.max;
        rt.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
        rt.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
