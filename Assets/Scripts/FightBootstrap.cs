// FightBootstrap.cs — builds the fight at runtime (Phase 2; G1/G2 fixed 2026-11-15).
//
// Main.unity holds NO prefab instances (hand-authoring PrefabInstance YAML
// is fragile — see GAME-NOTES.md). Instead it holds prefab REFERENCES on
// this component, and Awake instantiates them:
//
//   Arena.prefab        -> (0, 0, 0)
//   Katara.prefab       -> SpawnA (0, 0, -6), facing +Z (the opponent)
//   Azula.prefab        -> SpawnB (0, 0, 6),  facing -Z
//
// Spawn positions/rotations are read from the arena's SpawnA/SpawnB
// markers when present (hardcoded fallbacks match ARENA-NOTES.md).
// Each fighter gets a CharacterController (capsule r 0.38, h 1.75 —
// fighter.gd) + FighterController; the player also gets
// PlayerInputBridge, the bot gets BotAI. Opponents, the ReelCamera
// targets, and the RoundManager fighters are wired here.
//
// DEFERRED START (G2): Awake spawns the arena + the default pair
// (Katara = player, Azula = bot) as the LIVE LOBBY BACKDROP only —
// fighters Frozen, RoundManager Idle, no rounds run. Nothing in the
// fight advances until BeginMatch is called, so the UI no longer needs
// the old timeScale = 0 freeze to hold the menu screens (pause still
// uses timeScale = 0 — that is unrelated and untouched).
//
// HERO SWAP (G1): BeginMatch(playerHeroId, botHeroId, difficulty)
// starts (or restarts) a match with the chosen heroes. If the spawned
// pair already matches the pick, the fighters are kept and the match
// just starts; otherwise both fighter GameObjects are destroyed and
// re-spawned from the chosen prefabs/data through the single spawn
// path (SpawnFighter), then re-wired and the match starts. The UI must
// refresh its fighter references after BeginMatch — a hero swap
// respawns the fighters, so old FighterController references go stale
// (UIFlowManager does this: it refreshes its player/bot fields and
// re-binds the HUD, which re-subscribes fighter events and re-points
// the touch controls).
//
// VS FACE-OFF (2026-11-15 fix): the VS screen shows BOTH fighters
// facing off in the arena (PoseForVersus), per the approved fight-scene
// image — not text over an empty gradient. BeginMatch (via
// RoundManager.StartMatch -> ResetForRound) moves them back to the
// fight spawns, so no separate "back to spawns" call is needed.
//
// FAIL-LOUD (2026-11-15 fix): v0.2.0 shipped an APK whose scene refs
// resolved to nothing (stale incremental build) — the VS screen was an
// empty gradient and the fight never started, with the failure visible
// only in logcat. If any reference is null at Awake, or a fighter
// fails to spawn, a full-screen error is shown on the device instead
// of a silent empty scene. The CI build also gates on this
// (ProofBuild.VerifySceneWiringOrThrow + EditorSmokeTest).
//
// The arena brings its own sun/fog (ArenaEnvironment.cs), so Main.unity
// has no Directional Light.
using UnityEngine;
using UnityEngine.UI;

public class FightBootstrap : MonoBehaviour
{
    [Header("Prefabs (wired in Main.unity)")]
    public GameObject arenaPrefab;
    public GameObject kataraPrefab;
    public GameObject azulaPrefab;

    [Header("Fighter data (wired in Main.unity)")]
    public FighterData kataraData;
    public FighterData azulaData;

    [Header("Scene wiring (wired in Main.unity)")]
    public ReelCamera reelCamera;
    public RoundManager roundManager;

    [Header("Match setup")]
    public BotDifficulty botDifficulty = BotDifficulty.Medium;

    // VS face-off marks (approved fight-scene image composition): the two
    // fighters stand 5 m apart at the arena centre, facing each other.
    public static readonly Vector3 VersusAPos = new Vector3(0f, 0f, -2.5f);
    public const float VersusAYaw = 0f;      // A faces +Z (toward B)
    public static readonly Vector3 VersusBPos = new Vector3(0f, 0f, 2.5f);
    public const float VersusBYaw = 180f;    // B faces -Z (toward A)

    public FighterController PlayerFighter { get; private set; }
    public FighterController BotFighter { get; private set; }

    // Spawn transforms, resolved once in Awake from the arena markers
    // (fallbacks match ARENA-NOTES.md) and reused by every BeginMatch.
    Vector3 spawnAPos = new Vector3(0f, 0f, -6f);
    float spawnAYaw = 0f;
    Vector3 spawnBPos = new Vector3(0f, 0f, 6f);
    float spawnBYaw = 180f;

    // The arena instance (kept so the editor smoke test can destroy it).
    GameObject spawnedArena;

    void Awake()
    {
        SpawnInitialFight();
    }

    /// <summary>Arena + default pair + wiring. Shared by Awake (runtime)
    /// and the editor smoke test (ProofBuild) so both exercise the same
    /// path. Returns false if anything failed to spawn.</summary>
    public bool SpawnInitialFight()
    {
        // Arena at the origin.
        GameObject arena = null;
        if (arenaPrefab != null)
        {
            arena = Instantiate(arenaPrefab, Vector3.zero, Quaternion.identity);
            spawnedArena = arena;
        }
        else
        {
            ShowFatalError("arenaPrefab is not wired in Main.unity — the arena cannot load.");
        }

        // Spawns from the arena markers (fallback = ARENA-NOTES.md values).
        if (arena != null)
        {
            Transform a = arena.transform.Find("SpawnA");
            Transform b = arena.transform.Find("SpawnB");
            if (a != null) { spawnAPos = a.position; spawnAYaw = a.eulerAngles.y; }
            if (b != null) { spawnBPos = b.position; spawnBYaw = b.eulerAngles.y; }
        }

        // Default pair: Katara = player, Azula = bot. This is the live
        // lobby backdrop (see header) — BeginMatch re-spawns on a
        // different pick. Fighters are born Frozen (FighterController
        // .Setup) and no rounds run until BeginMatch, so nothing here
        // needs the match to start.
        PlayerFighter = SpawnFighter(kataraPrefab, kataraData, "katara",
            spawnAPos, spawnAYaw, isBot: false);
        BotFighter = SpawnFighter(azulaPrefab, azulaData, "azula",
            spawnBPos, spawnBYaw, isBot: true);

        WireFight();

        if (PlayerFighter == null || BotFighter == null)
        {
            ShowFatalError("A fighter failed to spawn (Katara prefab / Azula prefab missing from the build). " +
                "The VS screen and the fight cannot work — reinstall a fixed build.");
            return false;
        }
        return true;
    }

    /// <summary>VS face-off: move both fighters to the face-off marks at
    /// the arena centre, facing each other in a ready stance (Frozen,
    /// Idle). Called by UIFlowManager.ShowVersus. BeginMatch moves them
    /// back to the fight spawns via RoundManager.ResetForRound.</summary>
    public void PoseForVersus()
    {
        if (PlayerFighter != null)
        {
            PlayerFighter.transform.position = VersusAPos;
            PlayerFighter.transform.rotation = Quaternion.Euler(0f, VersusAYaw, 0f);
        }
        if (BotFighter != null)
        {
            BotFighter.transform.position = VersusBPos;
            BotFighter.transform.rotation = Quaternion.Euler(0f, VersusBYaw, 0f);
        }
    }

    /// <summary>Lobby backdrop reset: revive + reposition both fighters
    /// at their fight spawns (Idle, Frozen) and restore the fight camera
    /// framing. Called by UIFlowManager.ShowLobby. Without this, a
    /// fighter who lost the last match would stay dead (KO pose) on the
    /// lobby backdrop, and the camera would stay in the VS face-off pose
    /// after coming back from the VS screen.</summary>
    public void ResetForLobby()
    {
        if (PlayerFighter != null)
        {
            PlayerFighter.ResetForRound(spawnAPos, spawnAYaw);
            PlayerFighter.Frozen = true;
        }
        if (BotFighter != null)
        {
            BotFighter.ResetForRound(spawnBPos, spawnBYaw);
            BotFighter.Frozen = true;
        }
        if (reelCamera != null)
        {
            reelCamera.SetVsMode(false);
            reelCamera.Snap();
        }
    }

    /// <summary>Fail-loud on-device error: a full-screen message instead
    /// of a silent empty scene. Only used when the fight cannot be built
    /// at all (missing prefab references). Standalone canvas so it works
    /// even before the UI flow builds its own canvas.</summary>
    public static void ShowFatalError(string message)
    {
        Debug.LogError("[FightBootstrap] FATAL: " + message);
        var canvasGo = new GameObject("FightBootstrapFatalError");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.02f, 0.02f, 0.92f);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var textGo = new GameObject("ErrorText");
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<Text>();
        text.text = "ELEMENTIS could not start the fight.\n\n" + message;
        text.fontSize = 40;
        text.color = new Color(1f, 0.45f, 0.4f);
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.1f, 0.2f);
        textRect.anchorMax = new Vector2(0.9f, 0.8f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Start (or restart) a match with the chosen heroes. Hero ids are
    /// "katara" / "azula" (FighterData.characterId). The chosen hero
    /// spawns as the player (SpawnA), the other as the bot (SpawnB); the
    /// chosen difficulty is applied via BotAI.SetDifficulty. Returns
    /// false (and starts nothing) on an unknown hero id, the same hero
    /// on both sides, or a missing RoundManager. The caller must refresh
    /// its fighter references after this call — a hero swap respawns the
    /// fighters, so old FighterController references go stale.
    /// </summary>
    public bool BeginMatch(string playerHeroId, string botHeroId, BotDifficulty difficulty)
    {
        if (roundManager == null)
        {
            Debug.LogError("[FightBootstrap] BeginMatch: roundManager not wired in Main.unity.");
            return false;
        }
        if (!TryResolveHero(playerHeroId, out GameObject playerPrefab, out FighterData playerData)
            || !TryResolveHero(botHeroId, out GameObject botPrefab, out FighterData botData))
        {
            Debug.LogError("[FightBootstrap] BeginMatch: unknown hero id '" + playerHeroId +
                "' / '" + botHeroId + "' (want katara / azula).");
            return false;
        }
        if (playerHeroId == botHeroId)
        {
            Debug.LogError("[FightBootstrap] BeginMatch: player and bot cannot be the same hero ('" +
                playerHeroId + "').");
            return false;
        }

        botDifficulty = difficulty;

        if (FightersMatch(playerHeroId, botHeroId))
        {
            // Same pair already spawned (default pick, or a rematch):
            // keep the fighters, just apply the difficulty.
            ApplyDifficultyToBot();
        }
        else
        {
            RespawnFighters(playerPrefab, playerData, playerHeroId,
                botPrefab, botData, botHeroId);
        }

        // The respawn can fail (null prefab in the build) — never start a
        // match with missing fighters: the caller would show a frozen VS
        // screen with no way forward (the v0.2.0 failure).
        if (PlayerFighter == null || BotFighter == null)
        {
            Debug.LogError("[FightBootstrap] BeginMatch: fighters missing after spawn — cannot start.");
            return false;
        }

        roundManager.StartMatch(); // fresh match + 3-2-1 countdown
        return true;
    }

    bool FightersMatch(string playerHeroId, string botHeroId)
    {
        return PlayerFighter != null && BotFighter != null
            && PlayerFighter.Data != null && BotFighter.Data != null
            && PlayerFighter.Data.characterId == playerHeroId
            && BotFighter.Data.characterId == botHeroId;
    }

    bool TryResolveHero(string heroId, out GameObject prefab, out FighterData data)
    {
        switch (heroId)
        {
            case "katara":
                prefab = kataraPrefab; data = kataraData; return true;
            case "azula":
                prefab = azulaPrefab; data = azulaData; return true;
            default:
                prefab = null; data = null; return false;
        }
    }

    void ApplyDifficultyToBot()
    {
        if (BotFighter == null) return;
        var botAi = BotFighter.GetComponent<BotAI>();
        if (botAi != null) botAi.SetDifficulty(botDifficulty);
    }

    void RespawnFighters(GameObject playerPrefab, FighterData playerData, string playerId,
        GameObject botPrefab, FighterData botData, string botId)
    {
        // Silence the outgoing pair first: Object.Destroy is deferred
        // to end of frame, so freeze them and disable their input/AI
        // components now — no step runs on a half-torn-down fighter.
        if (PlayerFighter != null)
        {
            PlayerFighter.Frozen = true;
            var oldBridge = PlayerFighter.GetComponent<PlayerInputBridge>();
            if (oldBridge != null) oldBridge.enabled = false;
            DestroyFighterObject(PlayerFighter.gameObject);
        }
        if (BotFighter != null)
        {
            BotFighter.Frozen = true;
            var oldBot = BotFighter.GetComponent<BotAI>();
            if (oldBot != null) oldBot.enabled = false;
            DestroyFighterObject(BotFighter.gameObject);
        }

        PlayerFighter = SpawnFighter(playerPrefab, playerData, playerId,
            spawnAPos, spawnAYaw, isBot: false);
        BotFighter = SpawnFighter(botPrefab, botData, botId,
            spawnBPos, spawnBYaw, isBot: true);
        WireFight();
    }

    // Destroy now in the editor (Destroy is deferred to end of frame,
    // which never comes in edit mode); Destroy at runtime as usual.
    static void DestroyFighterObject(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { Object.DestroyImmediate(go); return; }
#endif
        Object.Destroy(go);
    }

    // Opponents, camera targets, RoundManager fighters + spawns.
    // (SpawnFighter already applied botDifficulty to the new BotAI.)
    void WireFight()
    {
        if (PlayerFighter != null && BotFighter != null)
        {
            PlayerFighter.SetOpponent(BotFighter);
            BotFighter.SetOpponent(PlayerFighter);
        }

        // Camera.
        if (reelCamera != null)
        {
            if (PlayerFighter != null) reelCamera.player = PlayerFighter.transform;
            if (BotFighter != null) reelCamera.opponent = BotFighter.transform;
            reelCamera.Snap();
        }

        // Rounds.
        if (roundManager != null)
        {
            roundManager.fighterA = PlayerFighter;
            roundManager.fighterB = BotFighter;
            roundManager.spawnAPos = spawnAPos;
            roundManager.spawnAYaw = spawnAYaw;
            roundManager.spawnBPos = spawnBPos;
            roundManager.spawnBYaw = spawnBYaw;
        }
        else
        {
            Debug.LogError("[FightBootstrap] roundManager not wired in Main.unity.");
        }
    }

    FighterController SpawnFighter(GameObject prefab, FighterData data,
        string characterId, Vector3 pos, float yaw, bool isBot)
    {
        if (prefab == null)
        {
            Debug.LogError("[FightBootstrap] fighter prefab not wired: " + characterId);
            return null;
        }
        GameObject go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
        // (The prefab root is already named Katara / Azula.)

        // CharacterController capsule (fighter.gd: radius 0.38, height 1.75).
        var cc = go.GetComponent<CharacterController>();
        if (cc == null) cc = go.AddComponent<CharacterController>();
        cc.radius = 0.38f;
        cc.height = 1.75f;
        cc.center = new Vector3(0f, 0.875f, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        var fighter = go.GetComponent<FighterController>();
        if (fighter == null) fighter = go.AddComponent<FighterController>();
        fighter.Setup(data, characterId, isBot);

        if (isBot)
        {
            var bot = go.GetComponent<BotAI>();
            if (bot == null) bot = go.AddComponent<BotAI>();
            bot.fighter = fighter;
            bot.SetDifficulty(botDifficulty);
        }
        else
        {
            var bridge = go.GetComponent<PlayerInputBridge>();
            if (bridge == null) bridge = go.AddComponent<PlayerInputBridge>();
        }
        return fighter;
    }

#if UNITY_EDITOR
    /// <summary>CI smoke test (edit mode, called by ProofBuild): spawns
    /// the arena + default pair through the real SpawnInitialFight path,
    /// then starts a match with BOTH hero orders and asserts BeginMatch
    /// returns true, the fighters exist with a valid Animator / human
    /// Avatar / controller, and RoundManager reaches Countdown. This is
    /// the exact v0.2.0 failure (null prefabs -> the fight never starts)
    /// caught before an APK is built. Cleans up after itself (destroys
    /// the spawned arena + fighters immediately).</summary>
    public bool EditorSmokeTest(out string error)
    {
        error = null;
        if (!SpawnInitialFight())
        {
            error = "SpawnInitialFight failed (arena or a fighter prefab did not spawn).";
            CleanupSmokeTest();
            return false;
        }
        if (!AssertSmokeFighter(PlayerFighter, "katara", out error)) { CleanupSmokeTest(); return false; }
        if (!AssertSmokeFighter(BotFighter, "azula", out error)) { CleanupSmokeTest(); return false; }
        // Azula as player — the owner's pick, and the order that failed.
        if (!BeginMatch("azula", "katara", BotDifficulty.Easy))
        {
            error = "BeginMatch(azula-player, katara-bot) returned false.";
            CleanupSmokeTest(); return false;
        }
        if (!AssertSmokeFighter(PlayerFighter, "azula", out error)) { CleanupSmokeTest(); return false; }
        if (!AssertSmokeFighter(BotFighter, "katara", out error)) { CleanupSmokeTest(); return false; }
        if (roundManager == null || roundManager.Phase != RoundPhase.Countdown)
        {
            error = "RoundManager.Phase is not Countdown after BeginMatch(azula-player).";
            CleanupSmokeTest(); return false;
        }
        // Katara as player.
        if (!BeginMatch("katara", "azula", BotDifficulty.Easy))
        {
            error = "BeginMatch(katara-player, azula-bot) returned false.";
            CleanupSmokeTest(); return false;
        }
        if (!AssertSmokeFighter(PlayerFighter, "katara", out error)) { CleanupSmokeTest(); return false; }
        if (!AssertSmokeFighter(BotFighter, "azula", out error)) { CleanupSmokeTest(); return false; }
        if (roundManager == null || roundManager.Phase != RoundPhase.Countdown)
        {
            error = "RoundManager.Phase is not Countdown after BeginMatch(katara-player).";
            CleanupSmokeTest(); return false;
        }
        CleanupSmokeTest();
        return true;
    }

    bool AssertSmokeFighter(FighterController fighter, string wantId, out string error)
    {
        error = null;
        if (fighter == null)
        {
            error = "fighter '" + wantId + "' is null after spawn.";
            return false;
        }
        if (fighter.Data == null || fighter.Data.characterId != wantId)
        {
            error = "fighter is '" + (fighter.Data != null ? fighter.Data.characterId : "null") +
                "', expected '" + wantId + "'.";
            return false;
        }
        if (fighter.Animator == null)
        {
            error = "fighter '" + wantId + "' has no Animator.";
            return false;
        }
        if (fighter.Animator.avatar == null || !fighter.Animator.avatar.isHuman)
        {
            error = "fighter '" + wantId + "' has no valid human Avatar.";
            return false;
        }
        if (fighter.Animator.runtimeAnimatorController == null)
        {
            error = "fighter '" + wantId + "' has no AnimatorController.";
            return false;
        }
        return true;
    }

    void CleanupSmokeTest()
    {
        if (PlayerFighter != null) DestroyFighterObject(PlayerFighter.gameObject);
        if (BotFighter != null) DestroyFighterObject(BotFighter.gameObject);
        PlayerFighter = null;
        BotFighter = null;
        if (spawnedArena != null) { Object.DestroyImmediate(spawnedArena); spawnedArena = null; }
    }
#endif
}
