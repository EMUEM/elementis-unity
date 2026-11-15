// RoundManager.cs — Elementis rounds (Phase 2).
//
// Best-of-3, 60 s timer, 3-2-1 countdown, KO, round-win tally.
// The fight flow: StartMatch -> per round: ResetForRound -> Countdown
// (3-2-1-FIGHT, fighters frozen) -> Fighting (timer runs) -> RoundEnd
// (KO or timeout) -> next round or MatchEnd.
//
// The UI agent builds the visuals (countdown banner, timer, round pips,
// KO banner) on top of these events — this class owns the state, not the
// presentation. RoundManager never reads fighter fields directly except
// through the fighters' public state (health %, IsDead).
using System;
using System.Collections;
using UnityEngine;

public enum RoundPhase { Idle, Countdown, Fighting, RoundEnd, MatchOver }
public enum RoundEndReason { KO, Timeout, Draw }

public class RoundManager : MonoBehaviour
{
    public const int WinsNeeded = 2;
    public const float RoundTimeSeconds = 60f;
    public const float CountdownTickSeconds = 0.8f;

    [Header("Fighters (assigned by FightBootstrap)")]
    public FighterController fighterA; // player (either hero)
    public FighterController fighterB; // bot (the other hero)

    [Header("Spawns (assigned by FightBootstrap)")]
    public Vector3 spawnAPos = new Vector3(0f, 0f, -6f);
    public float spawnAYaw = 0f;
    public Vector3 spawnBPos = new Vector3(0f, 0f, 6f);
    public float spawnBYaw = 180f;

    // --- Events (the HUD contract) ---
    public event Action<int> RoundStarted;          // round number (1-based)
    public event Action<int> CountdownTick;         // 3, 2, 1, 0 = FIGHT
    public event Action<FighterController, RoundEndReason> RoundEnded; // winner (null = draw)
    public event Action<FighterController> MatchEnded;                 // match winner
    public event Action<float> TimerChanged;        // seconds remaining
    public event Action<int, int> ScoreChanged;     // (winsA, winsB)

    // --- Read-only state (HUD polling) ---
    public RoundPhase Phase { get; private set; }
    public int RoundNumber { get; private set; }
    public int WinsA { get; private set; }
    public int WinsB { get; private set; }
    public float TimeRemaining { get; private set; }

    Coroutine roundRoutine;

    void Awake()
    {
        Phase = RoundPhase.Idle;
        TimeRemaining = RoundTimeSeconds;
    }

    public void StartMatch()
    {
        if (fighterA == null || fighterB == null)
        {
            Debug.LogError("[RoundManager] fighters not assigned — call FightBootstrap first.");
            return;
        }
        WinsA = 0;
        WinsB = 0;
        RoundNumber = 0;
        // (Un)subscribe exactly once even if StartMatch is called again.
        fighterA.Died -= OnFighterDied;
        fighterB.Died -= OnFighterDied;
        fighterA.Died += OnFighterDied;
        fighterB.Died += OnFighterDied;
        if (ScoreChanged != null) ScoreChanged(WinsA, WinsB);
        StartRound();
    }

    void StartRound()
    {
        RoundNumber++;
        TimeRemaining = RoundTimeSeconds;
        Phase = RoundPhase.Countdown;
        fighterA.ResetForRound(spawnAPos, spawnAYaw);
        fighterB.ResetForRound(spawnBPos, spawnBYaw);
        fighterA.Frozen = true;
        fighterB.Frozen = true;
        if (RoundStarted != null) RoundStarted(RoundNumber);
        if (roundRoutine != null) StopCoroutine(roundRoutine);
        roundRoutine = StartCoroutine(CountdownRoutine());
    }

    IEnumerator CountdownRoutine()
    {
        for (int n = 3; n >= 1; n--)
        {
            if (CountdownTick != null) CountdownTick(n);
            AudioManager.Play("countdown_beep", 0.9f);
            yield return new WaitForSeconds(CountdownTickSeconds);
        }
        if (CountdownTick != null) CountdownTick(0); // FIGHT
        AudioManager.Play("countdown_beep", 1f, 1.25f);
        Phase = RoundPhase.Fighting;
        fighterA.Frozen = false;
        fighterB.Frozen = false;
        if (TimerChanged != null) TimerChanged(TimeRemaining);
    }

    void Update()
    {
        if (Phase != RoundPhase.Fighting) return;
        TimeRemaining -= Time.deltaTime;
        if (TimeRemaining <= 0f)
        {
            TimeRemaining = 0f;
            EndRoundByTimeout();
        }
        if (TimerChanged != null) TimerChanged(TimeRemaining);
    }

    void OnFighterDied(FighterController dead)
    {
        if (Phase != RoundPhase.Fighting) return;
        FighterController winner = dead == fighterA ? fighterB : fighterA;
        EndRound(winner, RoundEndReason.KO);
    }

    void EndRoundByTimeout()
    {
        if (Phase != RoundPhase.Fighting) return;
        float pctA = fighterA.Health / FighterController.MaxHealth;
        float pctB = fighterB.Health / FighterController.MaxHealth;
        if (Mathf.Abs(pctA - pctB) < 0.001f)
        {
            // Exact tie: nobody scores, the round is replayed.
            EndRound(null, RoundEndReason.Draw);
            return;
        }
        EndRound(pctA > pctB ? fighterA : fighterB, RoundEndReason.Timeout);
    }

    void EndRound(FighterController winner, RoundEndReason reason)
    {
        Phase = RoundPhase.RoundEnd;
        fighterA.Frozen = true;
        fighterB.Frozen = true;
        if (winner == fighterA) WinsA++;
        else if (winner == fighterB) WinsB++;
        if (ScoreChanged != null) ScoreChanged(WinsA, WinsB);
        if (winner != null) AudioManager.Play("round_win_chime", 0.9f);
        if (RoundEnded != null) RoundEnded(winner, reason);
        if (roundRoutine != null) StopCoroutine(roundRoutine);
        roundRoutine = StartCoroutine(AfterRoundRoutine(winner));
    }

    IEnumerator AfterRoundRoutine(FighterController winner)
    {
        yield return new WaitForSeconds(2.5f);
        bool matchOver = (winner == fighterA && WinsA >= WinsNeeded)
            || (winner == fighterB && WinsB >= WinsNeeded);
        if (matchOver)
        {
            Phase = RoundPhase.MatchOver;
            if (MatchEnded != null) MatchEnded(winner);
        }
        else
        {
            StartRound();
        }
    }
}
