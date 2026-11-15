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
// The arena brings its own sun/fog (ArenaEnvironment.cs), so Main.unity
// has no Directional Light.
using UnityEngine;

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

    public FighterController PlayerFighter { get; private set; }
    public FighterController BotFighter { get; private set; }

    // Spawn transforms, resolved once in Awake from the arena markers
    // (fallbacks match ARENA-NOTES.md) and reused by every BeginMatch.
    Vector3 spawnAPos = new Vector3(0f, 0f, -6f);
    float spawnAYaw = 0f;
    Vector3 spawnBPos = new Vector3(0f, 0f, 6f);
    float spawnBYaw = 180f;

    void Awake()
    {
        // Arena at the origin.
        GameObject arena = null;
        if (arenaPrefab != null)
            arena = Instantiate(arenaPrefab, Vector3.zero, Quaternion.identity);
        else
            Debug.LogError("[FightBootstrap] arenaPrefab not wired in Main.unity.");

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
            Destroy(PlayerFighter.gameObject);
        }
        if (BotFighter != null)
        {
            BotFighter.Frozen = true;
            var oldBot = BotFighter.GetComponent<BotAI>();
            if (oldBot != null) oldBot.enabled = false;
            Destroy(BotFighter.gameObject);
        }

        PlayerFighter = SpawnFighter(playerPrefab, playerData, playerId,
            spawnAPos, spawnAYaw, isBot: false);
        BotFighter = SpawnFighter(botPrefab, botData, botId,
            spawnBPos, spawnBYaw, isBot: true);
        WireFight();
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
}
