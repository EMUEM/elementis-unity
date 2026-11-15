// HitStop.cs — hit punctuation (REA S3, Tachyon/QTE feel).
//
// Freezing Time.timeScale for a few dozen milliseconds on a landed hit
// is the single cheapest feel win: the impact reads. Durations:
// normal hit 40 ms, finisher 70 ms, ultimate impact 120 ms.
//
// Rules:
//  - The freeze is driven by UNSCALED time (WaitForSecondsRealtime
//    semantics via a per-frame Tick), so it always restores even while
//    timeScale is 0 — it can never wedge the game.
//  - It never fights the pause: if timeScale is already 0 when Freeze
//    is called (paused), it does nothing.
//  - Re-entrant: a new Freeze while one is running extends the window
//    to the later end time.
//  - Tick() must be called every frame from a MonoBehaviour that runs
//    even at timeScale 0 (ReelCamera.LateUpdate does this).
using UnityEngine;

public static class HitStop
{
    static float untilUnscaled;
    static bool active;

    /// <summary>Freeze the fight for `seconds` (real time). No-op while paused.</summary>
    public static void Freeze(float seconds)
    {
        if (seconds <= 0f) return;
        if (Time.timeScale <= 0f) return; // paused: the pause owns timeScale
        float end = Time.unscaledTime + seconds;
        if (end > untilUnscaled) untilUnscaled = end;
        if (!active)
        {
            active = true;
            Time.timeScale = 0f;
        }
    }

    /// <summary>Call every frame (even at timeScale 0). Restores timeScale = 1 when the window ends.</summary>
    public static void Tick()
    {
        if (!active) return;
        if (Time.unscaledTime >= untilUnscaled)
        {
            active = false;
            Time.timeScale = 1f;
        }
    }

    /// <summary>Hit-stop length for a landed hit, by damage: ultimate-scale hits freeze longest.</summary>
    public static float ForDamage(float damage)
    {
        if (damage >= 55f) return 0.12f; // ultimate impact
        if (damage >= 20f) return 0.07f; // finisher / heavy skill
        return 0.04f;                     // normal hit
    }
}
