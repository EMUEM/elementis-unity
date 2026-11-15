// LobbyShowcase.cs — Elementis lobby idle showcase (Phase 2b).
//
// Lobby spec (GOAL.md): the home screen shows ONLY a Start option plus
// ONE character standing and doing an elemental motion. The showcase
// character is the live Katara fighter FightBootstrap already spawned
// in the arena (SpawnA), seen from the fight camera — no second model,
// no extra scene object.
//
// The menu states used to run with Time.timeScale = 0 (UIFlowManager
// froze the auto-started match until FIGHT was pressed); since the
// G1/G2 fix the match is deferred and menus run live, but the showcase
// still moves on UNSCALED time — harmless at timeScale 1, and it keeps
// the flourishes playing if the showcase is ever shown while frozen:
//  * the fighter's Animator is switched to UnscaledTime while a menu
//    screen is up, so her Idle clip keeps playing (FighterController
//    keeps her in the Idle state while Frozen);
//  * every FlourishInterval seconds a WaterWhip burst is spawned at her
//    hands (a one-shot VFX prefab — no SkillProjectile, no lifetime
//    ownership needed) with useUnscaledTime on its particle systems,
//    so the water flourish plays while the world is frozen.
// On fight start the Animator is restored to Normal update mode.
using UnityEngine;

public class LobbyShowcase : MonoBehaviour
{
    public const float FlourishInterval = 4.5f;
    public const float FlourishHeight = 1.3f;
    public const float FlourishForward = 0.7f;

    FighterController showcaseFighter;
    bool active;
    float timer;

    public void SetFighter(FighterController fighter)
    {
        showcaseFighter = fighter;
    }

    // Menu shown: idle anim + flourishes on unscaled time.
    public void StartShowcase()
    {
        active = true;
        timer = 1.2f; // first flourish shortly after the lobby appears
        SetUnscaled(true);
    }

    // Fight starting: hand the animator back to scaled time.
    public void StopShowcase()
    {
        active = false;
        SetUnscaled(false);
    }

    void SetUnscaled(bool unscaled)
    {
        if (showcaseFighter == null || showcaseFighter.Animator == null) return;
        showcaseFighter.Animator.updateMode = unscaled
            ? AnimatorUpdateMode.UnscaledTime
            : AnimatorUpdateMode.Normal;
    }

    void Update()
    {
        if (!active || showcaseFighter == null) return;
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = FlourishInterval;
        SpawnFlourish();
    }

    void SpawnFlourish()
    {
        if (ElementisFx.Instance == null) return;
        Transform t = showcaseFighter.transform;
        Vector3 pos = t.position + Vector3.up * FlourishHeight
            + t.forward * FlourishForward;
        // Element-aware: Katara flourishes water, Azula flourishes fire
        // (the showcase fighter is whoever the player picked).
        string vfx = "WaterWhip";
        string sfx = "water_whoosh";
        if (showcaseFighter.Data != null && showcaseFighter.Data.characterId == "azula")
        {
            vfx = "FlameSweep";
            sfx = "fire_whoosh";
        }
        GameObject fx = ElementisFx.Instance.Spawn(vfx, pos,
            Quaternion.identity);
        if (fx == null) return;
        // Run the burst on unscaled time (was required when menus ran frozen).
        var systems = fx.GetComponentsInChildren<ParticleSystem>();
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.useUnscaledTime = true;
        }
        AudioManager.Play(sfx, 0.4f);
    }
}
