// FightHud.cs — Elementis fight HUD (Phase 2b).
//
// Minimal, Infinite Souls style (DESIGN.md): own health as a white bar
// bottom-center ("350 / 350"), enemy health as a red bar at top (+ a
// floating enemy health bar above the opponent), a thin mana bar under
// own health, an ULTIMATE meter under that, round-win dots top-center,
// a 60 s timer, pause + mute buttons top-left.
//
// The HUD NEVER reads fighter fields directly except through the
// public polling getters the contract allows (Mana, Ultimate01,
// SkillCooldownRemaining — see GAME-NOTES.md); everything else arrives
// on the FighterController / RoundManager events listed below.
// Touch controls live in TouchControls.cs (built into this same panel).
using System;
using UnityEngine;
using UnityEngine.UI;

public class FightHud : MonoBehaviour
{
    // --- LAYOUT (reference 1920x1080 landscape, px; see UI-NOTES.md) ---
    // Top (anchor top-centre unless noted).
    public const float EnemyBarWidth = 760f;
    public const float EnemyBarHeight = 26f;
    public const float EnemyBarTop = -48f;      // centre y from top edge
    public const float EnemyNameTop = -20f;
    public const float RoundDotsTop = -96f;
    public const float RoundDotSize = 18f;
    public const float RoundDotGap = 10f;
    public const float RoundLabelTop = -124f;
    public const float MatchSubLabelTop = -158f;
    public const float TimerRight = -90f;       // centre x from right edge
    public const float TimerTop = -48f;
    public const float TimerWidth = 140f;
    public const float TimerHeight = 48f;
    public const float PauseLeft = 83f;         // centre x from left edge
    public const float PauseTop = -48f;
    public const float PauseWidth = 110f;
    public const float PauseHeight = 48f;
    public const float MuteLeft = 207f;
    public const float MuteTop = -48f;
    public const float MuteWidth = 110f;
    public const float MuteHeight = 48f;
    // Bottom-centre bars (anchor bottom-centre; y = centre from bottom).
    public const float OwnBarWidth = 640f;
    public const float OwnBarHeight = 30f;
    public const float OwnBarBottom = 212f;
    public const float ManaBarHeight = 10f;
    public const float ManaBarBottom = 182f;
    public const float UltBarHeight = 12f;
    public const float UltBarBottom = 156f;
    // Centre feedback.
    public const float TimingCenterY = 150f;
    public const float RegenTextCenterY = 80f;
    public const float UltBannerCenterY = 260f;
    // Floating enemy bar (screen px, positioned every frame).
    public const float FloatBarWidth = 220f;
    public const float FloatBarHeight = 14f;
    public const float FloatBarHeadOffset = 2.2f; // metres above the bot root

    GameObject root;
    Canvas canvas;
    Camera fightCamera;

    FighterController player, bot;
    RoundManager rounds;
    TouchControls controls;

    Image ownFill, manaFill, ultFill, enemyFill, floatFill;
    Text ownText, enemyNameText, timerText, roundLabel, matchSubLabel;
    Text timingText, regenText, ultBannerText, floatNameText;
    Image[] playerDots = new Image[2];
    Image[] botDots = new Image[2];
    Button pauseButton, muteButton;
    GameObject floatRoot;
    RectTransform floatRootRect;

    float timingLeft, ultBannerLeft;
    bool subscribed; // subscribed to the current player/bot pair
    bool roundsSubscribed; // RoundManager object never changes: once only
    bool controlsBuilt;

    // Build the HUD under the flow manager's canvas. Camera is found by
    // name ("Main Camera") — the scene object is NOT tagged MainCamera,
    // so Camera.main would return null.
    public void Build(Transform parent, Canvas hudCanvas)
    {
        canvas = hudCanvas;
        var camGo = GameObject.Find("Main Camera");
        fightCamera = camGo != null ? camGo.GetComponent<Camera>() : null;

        root = new GameObject("FightHud");
        root.transform.SetParent(parent, false);
        UiKit.Stretch(root);

        // --- Enemy health (top-centre, red) + name above it ---
        enemyNameText = UiKit.Label(root.transform, "EnemyName", "AZULA", 24,
            Color.white);
        UiKit.Rect(enemyNameText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, EnemyNameTop), new Vector2(400f, 28f), new Vector2(0.5f, 0.5f));
        var enemyBar = UiKit.Bar(root.transform, "EnemyHealth",
            new Vector2(EnemyBarWidth, EnemyBarHeight),
            new Color(0f, 0f, 0f, 0.55f), UiKit.EnemyRed);
        UiKit.Rect(enemyBar.bg.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, EnemyBarTop), new Vector2(EnemyBarWidth, EnemyBarHeight),
            new Vector2(0.5f, 0.5f));
        enemyFill = enemyBar.fill;

        // --- Round-win dots (top-centre; left pair = you, right pair = bot) ---
        for (int i = 0; i < 2; i++)
        {
            playerDots[i] = Dot(root.transform, "PlayerDot" + (i + 1),
                new Vector2(-90f + i * (RoundDotSize + RoundDotGap), RoundDotsTop));
            botDots[i] = Dot(root.transform, "BotDot" + (i + 1),
                new Vector2(62f + i * (RoundDotSize + RoundDotGap), RoundDotsTop));
        }
        roundLabel = UiKit.Label(root.transform, "RoundLabel", "ROUND 1", 30,
            UiKit.Gold);
        roundLabel.fontStyle = FontStyle.Bold;
        UiKit.Rect(roundLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, RoundLabelTop), new Vector2(300f, 34f), new Vector2(0.5f, 0.5f));
        matchSubLabel = UiKit.Label(root.transform, "MatchSubLabel", "", 18,
            new Color(1f, 1f, 1f, 0.75f));
        UiKit.Rect(matchSubLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, MatchSubLabelTop), new Vector2(600f, 24f), new Vector2(0.5f, 0.5f));

        // --- Timer (top-right) + pause / mute (top-left) ---
        timerText = UiKit.Label(root.transform, "Timer", "1:00", 40, Color.white);
        timerText.fontStyle = FontStyle.Bold;
        UiKit.Rect(timerText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(TimerRight, TimerTop), new Vector2(TimerWidth, TimerHeight),
            new Vector2(0.5f, 0.5f));
        pauseButton = UiKit.MakeButton(root.transform, "PauseButton", "PAUSE",
            new Vector2(PauseWidth, PauseHeight), UiKit.ButtonGrey, 20, Color.white, false);
        UiKit.Rect(pauseButton.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(PauseLeft, PauseTop), new Vector2(PauseWidth, PauseHeight),
            new Vector2(0.5f, 0.5f));
        muteButton = UiKit.MakeButton(root.transform, "MuteButton", "SND ON",
            new Vector2(MuteWidth, MuteHeight), UiKit.ButtonGrey, 20, Color.white, false);
        UiKit.Rect(muteButton.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(MuteLeft, MuteTop), new Vector2(MuteWidth, MuteHeight),
            new Vector2(0.5f, 0.5f));

        // --- Own health (bottom-centre, white) + "350 / 350" text ---
        var ownBar = UiKit.Bar(root.transform, "OwnHealth",
            new Vector2(OwnBarWidth, OwnBarHeight),
            new Color(0f, 0f, 0f, 0.55f), UiKit.HealthWhite);
        UiKit.Rect(ownBar.bg.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, OwnBarBottom), new Vector2(OwnBarWidth, OwnBarHeight),
            new Vector2(0.5f, 0.5f));
        ownFill = ownBar.fill;
        ownText = UiKit.Label(ownBar.bg.transform, "OwnHealthText", "350 / 350", 24,
            new Color(0.08f, 0.08f, 0.08f));
        ownText.fontStyle = FontStyle.Bold;
        UiKit.Stretch(ownText.gameObject);
        // --- Mana (thin, blue) + ULTIMATE meter (thin, gold) below it ---
        var manaBar = UiKit.Bar(root.transform, "Mana",
            new Vector2(OwnBarWidth, ManaBarHeight),
            new Color(0f, 0f, 0f, 0.55f), UiKit.ManaBlue);
        UiKit.Rect(manaBar.bg.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, ManaBarBottom), new Vector2(OwnBarWidth, ManaBarHeight),
            new Vector2(0.5f, 0.5f));
        manaFill = manaBar.fill;
        var manaLabel = UiKit.Label(root.transform, "ManaLabel", "MANA", 14,
            new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleRight);
        UiKit.Rect(manaLabel.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-OwnBarWidth / 2f - 36f, ManaBarBottom), new Vector2(64f, 16f),
            new Vector2(0.5f, 0.5f));
        var ultBar = UiKit.Bar(root.transform, "UltimateMeter",
            new Vector2(OwnBarWidth, UltBarHeight),
            new Color(0f, 0f, 0f, 0.55f), UiKit.Gold);
        UiKit.Rect(ultBar.bg.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, UltBarBottom), new Vector2(OwnBarWidth, UltBarHeight),
            new Vector2(0.5f, 0.5f));
        ultFill = ultBar.fill;
        var ultLabel = UiKit.Label(root.transform, "UltLabel", "ULT", 14,
            new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleRight);
        UiKit.Rect(ultLabel.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-OwnBarWidth / 2f - 36f, UltBarBottom), new Vector2(64f, 16f),
            new Vector2(0.5f, 0.5f));

        // --- Centre feedback: timing / regen / ultimate banner ---
        timingText = UiKit.Label(root.transform, "TimingText", "", 64, UiKit.Gold);
        timingText.fontStyle = FontStyle.Bold;
        UiKit.Rect(timingText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, TimingCenterY), new Vector2(800f, 90f), new Vector2(0.5f, 0.5f));
        timingText.gameObject.SetActive(false);
        regenText = UiKit.Label(root.transform, "RegenText",
            "REGENERATING — any hit cancels it", 28, new Color(0.35f, 0.95f, 0.45f));
        regenText.fontStyle = FontStyle.Bold;
        UiKit.Rect(regenText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, RegenTextCenterY), new Vector2(900f, 40f), new Vector2(0.5f, 0.5f));
        regenText.gameObject.SetActive(false);
        ultBannerText = UiKit.Label(root.transform, "UltBannerText", "ULTIMATE!", 80,
            UiKit.Gold);
        ultBannerText.fontStyle = FontStyle.Bold;
        UiKit.Rect(ultBannerText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, UltBannerCenterY), new Vector2(900f, 110f), new Vector2(0.5f, 0.5f));
        ultBannerText.gameObject.SetActive(false);

        // --- Floating enemy health bar (positioned over the bot every frame) ---
        floatRoot = new GameObject("FloatingEnemyBar");
        floatRoot.transform.SetParent(root.transform, false);
        floatRootRect = UiKit.Rect(floatRoot, Vector2.zero, Vector2.zero, Vector2.zero,
            Vector2.zero, new Vector2(0.5f, 0.5f));
        floatNameText = UiKit.Label(floatRoot.transform, "FloatName", "AZULA", 20,
            Color.white);
        floatNameText.fontStyle = FontStyle.Bold;
        UiKit.Rect(floatNameText.gameObject, Vector2.zero, Vector2.zero,
            new Vector2(0f, FloatBarHeight / 2f + 16f),
            new Vector2(FloatBarWidth, 24f), new Vector2(0.5f, 0.5f));
        var floatBar = UiKit.Bar(floatRoot.transform, "FloatHealth",
            new Vector2(FloatBarWidth, FloatBarHeight),
            new Color(0f, 0f, 0f, 0.6f), UiKit.EnemyRed);
        UiKit.Rect(floatBar.bg.gameObject, Vector2.zero, Vector2.zero,
            Vector2.zero, new Vector2(FloatBarWidth, FloatBarHeight),
            new Vector2(0.5f, 0.5f));
        floatFill = floatBar.fill;

        // Touch controls live in the same panel, above the bars.
        controls = gameObject.AddComponent<TouchControls>();

        root.SetActive(false);
    }

    Image Dot(Transform parent, string name, Vector2 centreFromTopCentre)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = UiKit.Circle;
        img.color = new Color(1f, 1f, 1f, 0.25f);
        img.raycastTarget = false;
        UiKit.Rect(go, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            centreFromTopCentre, new Vector2(RoundDotSize, RoundDotSize),
            new Vector2(0.5f, 0.5f));
        return img;
    }

    // Wire the HUD to a fight. Called by UIFlowManager once the fighters
    // exist (FightBootstrap.Awake has run by then). Safe to call again on
    // rematch — and on a hero swap, when the fighters are DIFFERENT
    // objects (FightBootstrap.BeginMatch respawns them): the old pair's
    // events are unsubscribed first, then the new pair is subscribed.
    public void Bind(FighterController playerFighter, FighterController botFighter,
        RoundManager roundManager, Action onPausePressed, string matchSub)
    {
        if (subscribed) UnsubscribeFighters(); // previous pair, if any
        player = playerFighter;
        bot = botFighter;
        rounds = roundManager;
        if (player == null || bot == null)
        {
            Debug.LogError("[FightHud] Bind called without fighters.");
            return;
        }
        if (matchSubLabel != null) matchSubLabel.text = matchSub;
        if (enemyNameText != null)
            enemyNameText.text = bot.Data != null ? bot.Data.displayName.ToUpper() : "AZULA";
        if (floatNameText != null)
            floatNameText.text = bot.Data != null ? bot.Data.displayName.ToUpper() : "AZULA";

        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveAllListeners();
            pauseButton.onClick.AddListener(() => { if (onPausePressed != null) onPausePressed(); });
        }
        if (muteButton != null)
        {
            muteButton.onClick.RemoveAllListeners();
            muteButton.onClick.AddListener(ToggleMute);
        }
        RefreshMuteLabel();
        RefreshFromFighters();

        // Fighter events: (re)subscribed to the current pair every Bind.
        player.HealthChanged += OnPlayerHealth;
        player.ManaChanged += OnPlayerMana;
        player.UltimateChanged += OnPlayerUltimate;
        player.TimingShown += OnTimingShown;
        player.RegenStarted += OnPlayerRegenStarted;
        player.RegenEnded += OnPlayerRegenEnded;
        player.UltimateCast += OnUltimateCast;
        bot.HealthChanged += OnBotHealth;
        subscribed = true;

        // Round events: the RoundManager object never changes, so these
        // are subscribed exactly once.
        if (!roundsSubscribed && rounds != null)
        {
            roundsSubscribed = true;
            rounds.TimerChanged += OnTimer;
            rounds.ScoreChanged += OnScore;
            rounds.RoundStarted += OnRoundStarted;
        }

        if (controls != null && !controlsBuilt)
        {
            controlsBuilt = true;
            controls.Build(root.transform, player, canvas);
            controls.RefreshSkillLabels();
        }
        else if (controls != null)
        {
            controls.RebindPlayer(player);
        }
    }

    // Unsubscribe from the previous fighter pair. Uses ReferenceEquals:
    // after a hero swap the fields may point at DESTROYED fighters,
    // which Unity's == reports as null even though the C# objects (and
    // their delegate lists) still exist.
    void UnsubscribeFighters()
    {
        var oldPlayer = player;
        var oldBot = bot;
        subscribed = false;
        if (!ReferenceEquals(oldPlayer, null))
        {
            oldPlayer.HealthChanged -= OnPlayerHealth;
            oldPlayer.ManaChanged -= OnPlayerMana;
            oldPlayer.UltimateChanged -= OnPlayerUltimate;
            oldPlayer.TimingShown -= OnTimingShown;
            oldPlayer.RegenStarted -= OnPlayerRegenStarted;
            oldPlayer.RegenEnded -= OnPlayerRegenEnded;
            oldPlayer.UltimateCast -= OnUltimateCast;
        }
        if (!ReferenceEquals(oldBot, null))
            oldBot.HealthChanged -= OnBotHealth;
    }

    void OnPlayerRegenStarted() { SetRegenVisible(true); }
    void OnPlayerRegenEnded(bool cancelled) { SetRegenVisible(false); }

    public void Show() { if (root != null) root.SetActive(true); }
    public void Hide() { if (root != null) root.SetActive(false); }
    public bool IsShown { get { return root != null && root.activeSelf; } }

    void RefreshFromFighters()
    {
        if (player == null || bot == null) return;
        OnPlayerHealth(player.Health, FighterController.MaxHealth);
        OnPlayerMana(player.Mana, FighterController.MaxEnergy);
        OnPlayerUltimate(player.Ultimate01);
        OnBotHealth(bot.Health, FighterController.MaxHealth);
        OnTimer(rounds != null ? rounds.TimeRemaining : RoundManager.RoundTimeSeconds);
        OnScore(rounds != null ? rounds.WinsA : 0, rounds != null ? rounds.WinsB : 0);
    }

    void OnPlayerHealth(float health, float max)
    {
        if (ownFill != null) ownFill.fillAmount = Mathf.Clamp01(health / max);
        if (ownText != null)
            ownText.text = Mathf.CeilToInt(health) + " / " + Mathf.RoundToInt(max);
    }

    void OnPlayerMana(float mana, float max)
    {
        if (manaFill != null) manaFill.fillAmount = Mathf.Clamp01(mana / max);
    }

    void OnPlayerUltimate(float frac)
    {
        if (ultFill != null) ultFill.fillAmount = Mathf.Clamp01(frac);
    }

    void OnBotHealth(float health, float max)
    {
        float frac = Mathf.Clamp01(health / max);
        if (enemyFill != null) enemyFill.fillAmount = frac;
        if (floatFill != null) floatFill.fillAmount = frac;
    }

    void OnTimer(float seconds)
    {
        if (timerText == null) return;
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        timerText.text = (s / 60) + ":" + (s % 60).ToString("D2");
    }

    void OnScore(int winsA, int winsB)
    {
        for (int i = 0; i < 2; i++)
        {
            if (playerDots[i] != null)
                playerDots[i].color = i < winsA
                    ? UiKit.Gold : new Color(1f, 1f, 1f, 0.25f);
            if (botDots[i] != null)
                botDots[i].color = i < winsB
                    ? UiKit.EnemyRed : new Color(1f, 1f, 1f, 0.25f);
        }
    }

    void OnRoundStarted(int round)
    {
        if (roundLabel != null) roundLabel.text = "ROUND " + round;
        RefreshFromFighters();
    }

    void OnTimingShown(string text, float seconds, bool perfect)
    {
        if (timingText == null) return;
        timingText.text = text;
        timingText.color = perfect ? UiKit.Gold : Color.white;
        timingText.gameObject.SetActive(true);
        timingLeft = 1.0f;
    }

    void SetRegenVisible(bool visible)
    {
        if (regenText != null) regenText.gameObject.SetActive(visible);
    }

    void OnUltimateCast()
    {
        if (ultBannerText == null) return;
        ultBannerText.gameObject.SetActive(true);
        ultBannerLeft = 1.2f;
    }

    void ToggleMute()
    {
        AudioManager.Muted = !AudioManager.Muted;
        RefreshMuteLabel();
    }

    public void RefreshMuteLabel()
    {
        var label = muteButton != null
            ? muteButton.GetComponentInChildren<Text>() : null;
        if (label != null) label.text = AudioManager.Muted ? "SND OFF" : "SND ON";
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        // Feedback banners hide on unscaled time (they must also vanish
        // while paused, where scaled time is frozen).
        if (timingLeft > 0f)
        {
            timingLeft -= Time.unscaledDeltaTime;
            if (timingLeft <= 0f && timingText != null)
                timingText.gameObject.SetActive(false);
        }
        if (ultBannerLeft > 0f)
        {
            ultBannerLeft -= Time.unscaledDeltaTime;
            if (ultBannerLeft <= 0f && ultBannerText != null)
                ultBannerText.gameObject.SetActive(false);
        }
        UpdateFloatingBar();
    }

    void UpdateFloatingBar()
    {
        if (floatRoot == null || bot == null || fightCamera == null) return;
        Vector3 world = bot.transform.position + Vector3.up * FloatBarHeadOffset;
        Vector3 screen = fightCamera.WorldToScreenPoint(world);
        bool behind = screen.z < 0f;
        floatRoot.SetActive(!behind);
        if (behind) return;
        // Overlay canvas: anchoredPosition in reference px, origin bottom-left.
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        if (floatRootRect != null)
            floatRootRect.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale);
    }
}
