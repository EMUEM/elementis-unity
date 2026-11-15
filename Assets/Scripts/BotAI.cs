// BotAI.cs — Elementis bot opponent (Phase 2).
//
// Drives a FighterController through the SAME public input surface as the
// player (TryPunch / TryKick / TrySkill / TryDash / TryDodge / TryJump /
// TrySurf / SetBlocking / SetMoveInputWorld) — the bot never touches
// fighter internals. Difficulty is a data preset, not a code fork.
//
// State machine: Approach (close the distance), Attack (in range: melee,
// combos, skills), Block (guard when the opponent casts, or on a read),
// Retreat (back off when low HP, or after a heavy hit), Channel (regen
// when safe: far away, opponent not casting), Feint (Hard only: fake an
// approach, then retreat + block to bait a whiff).
//
// The bot uses skills, blocks, combos, the regen channel when safe, and
// the ultimate the moment it is ready and in range — per the owner's
// 2026-11-15 16:02 / 16:08 requirements.
using UnityEngine;

public enum BotDifficulty { Easy, Medium, Hard }

public class BotAI : MonoBehaviour
{
    enum BotState { Approach, Attack, Block, Retreat, Channel, Feint }

    [Header("Setup (assigned by FightBootstrap)")]
    public FighterController fighter;
    public BotDifficulty difficulty = BotDifficulty.Medium;

    // Difficulty presets: reaction (s), skill use (0..1), combo chance,
    // block chance, retreat HP %, aggression (0..1), feints?
    struct Preset
    {
        public float reaction, skillUse, comboChance, blockChance, retreatHp, aggression;
        public bool feints;
    }

    static Preset ForDifficulty(BotDifficulty d)
    {
        switch (d)
        {
            case BotDifficulty.Easy:
                return new Preset { reaction = 0.9f, skillUse = 0.25f, comboChance = 0f,
                    blockChance = 0.05f, retreatHp = 0.25f, aggression = 0.5f, feints = false };
            case BotDifficulty.Hard:
                return new Preset { reaction = 0.18f, skillUse = 0.8f, comboChance = 0.7f,
                    blockChance = 0.35f, retreatHp = 0.2f, aggression = 1f, feints = true };
            default: // Medium
                return new Preset { reaction = 0.45f, skillUse = 0.5f, comboChance = 0.35f,
                    blockChance = 0.2f, retreatHp = 0.3f, aggression = 0.75f, feints = false };
        }
    }

    Preset preset;
    BotState state = BotState.Approach;
    float thinkT;      // counts down to the next decision
    float stateT;      // time in the current state
    float strafeDir = 1f;
    float strafeT;
    int comboPlanned;  // 0 = none, 1 = punch chain planned, 2 = finisher planned
    bool wantRegen;

    void Awake()
    {
        preset = ForDifficulty(difficulty);
        if (fighter == null) fighter = GetComponent<FighterController>();
    }

    public void SetDifficulty(BotDifficulty d)
    {
        difficulty = d;
        preset = ForDifficulty(d);
    }

    void FixedUpdate()
    {
        if (fighter == null || fighter.Data == null) return;
        if (fighter.Frozen || fighter.IsDead || fighter.Opponent == null) return;
        FighterController opp = fighter.Opponent;

        thinkT -= Time.fixedDeltaTime;
        stateT += Time.fixedDeltaTime;
        strafeT -= Time.fixedDeltaTime;
        if (strafeT <= 0f)
        {
            strafeDir = Random.value < 0.5f ? -1f : 1f;
            strafeT = Random.Range(0.8f, 2f);
        }

        float dist = fighter.OpponentDistance;
        float hpPct = fighter.Health / FighterController.MaxHealth;

        // Ultimate: the moment it is ready and the opponent is in range.
        if (fighter.UltimateReady && dist < 4.5f && !fighter.IsActionBusy())
        {
            fighter.TryUltimate();
            return;
        }

        // Decisions happen on the reaction clock, not every frame.
        if (thinkT <= 0f)
        {
            thinkT = preset.reaction * Random.Range(0.8f, 1.2f);
            Decide(opp, dist, hpPct);
        }

        Act(opp, dist, hpPct);
    }

    void Decide(FighterController opp, float dist, float hpPct)
    {
        // Retreat when low HP (and not already channeling to recover).
        if (hpPct < preset.retreatHp && state != BotState.Channel && state != BotState.Retreat)
        {
            state = BotState.Retreat;
            stateT = 0f;
            return;
        }
        // Regen when safe: far away, opponent not casting, HP worth recovering.
        if (hpPct < 0.6f && dist > 8f && !opp.IsCasting && !fighter.IsChanneling)
        {
            wantRegen = true;
            state = BotState.Channel;
            stateT = 0f;
            return;
        }
        if (state == BotState.Channel)
        {
            // Channel ends when healed enough, the opponent closes in, or
            // the opponent starts casting (the channel is interruptible).
            if (hpPct > 0.85f || dist < 6f || opp.IsCasting)
            {
                wantRegen = false;
                fighter.SetRegenHeld(false);
                state = BotState.Approach;
                stateT = 0f;
            }
            return;
        }
        // Block read: the opponent is casting and we are in range.
        if (opp.IsCasting && dist < 5f && Random.value < preset.blockChance + 0.25f)
        {
            state = BotState.Block;
            stateT = 0f;
            return;
        }
        // Hard feint: fake an approach, then retreat + block to bait a whiff.
        if (preset.feints && state == BotState.Approach && stateT > 1.2f
            && dist < 6f && Random.value < 0.25f)
        {
            state = BotState.Feint;
            stateT = 0f;
            return;
        }
        if (state == BotState.Feint && stateT > 0.7f)
        {
            state = BotState.Retreat;
            stateT = 0f;
            return;
        }
        if (state == BotState.Block && stateT > Random.Range(0.3f, 0.8f))
        {
            state = dist < 3f ? BotState.Attack : BotState.Approach;
            stateT = 0f;
            return;
        }
        if (state == BotState.Retreat && (hpPct > preset.retreatHp + 0.15f || stateT > 2f))
        {
            state = BotState.Approach;
            stateT = 0f;
            return;
        }
        if (dist <= 2.2f && state != BotState.Attack && state != BotState.Block)
        {
            state = BotState.Attack;
            stateT = 0f;
            comboPlanned = 0;
        }
        else if (dist > 3.5f && state == BotState.Attack)
        {
            state = BotState.Approach;
            stateT = 0f;
        }
    }

    void Act(FighterController opp, float dist, float hpPct)
    {
        Vector3 toOpp = opp.transform.position - transform.position;
        toOpp.y = 0f;
        Vector3 dirToOpp = toOpp.sqrMagnitude > 0.001f ? toOpp.normalized : transform.forward;
        Vector3 strafe = Vector3.Cross(Vector3.up, dirToOpp) * strafeDir;

        switch (state)
        {
            case BotState.Approach:
                fighter.SetMoveInputWorld(dirToOpp * preset.aggression + strafe * 0.3f);
                fighter.SetBlocking(false);
                // Far: waterspout surf in on Katara (reel 4), else ranged skill / dash.
                if (fighter.Data.characterId == "katara" && fighter.Kit.Sprinting
                    && fighter.Mana > 40f && dist > 12f && Random.value < 0.5f)
                    fighter.TrySurf();
                // Closing skill: a ranged skill while approaching (skill 1 or 3).
                if (dist > 4f && dist < 14f && Random.value < preset.skillUse * 0.02f)
                    fighter.TrySkill(Random.value < 0.5f ? 0 : 2);
                // Sprint + jump gap-close, occasionally.
                if (dist > 12f && Random.value < 0.01f) fighter.TryJump();
                break;

            case BotState.Attack:
                fighter.SetMoveInputWorld(strafe * 0.4f);
                fighter.SetBlocking(false);
                Attack(opp, dist);
                break;

            case BotState.Block:
                fighter.SetMoveInputWorld(Vector3.zero);
                fighter.SetBlocking(true);
                // Blocked a cast? Punish with a skill as the guard drops.
                if (!opp.IsCasting && stateT > 0.4f && Random.value < preset.skillUse * 0.05f)
                {
                    fighter.SetBlocking(false);
                    fighter.TrySkill(1);
                }
                break;

            case BotState.Retreat:
                fighter.SetMoveInputWorld(-dirToOpp + strafe * 0.5f);
                fighter.SetBlocking(Random.value < preset.blockChance);
                // Kite with a ranged skill while retreating.
                if (Random.value < preset.skillUse * 0.01f) fighter.TrySkill(0);
                break;

            case BotState.Channel:
                fighter.SetMoveInputWorld(Vector3.zero);
                fighter.SetBlocking(false);
                if (wantRegen) fighter.SetRegenHeld(true);
                // The channel roots the fighter; any enemy hit cancels it
                // (FighterController.TakeDamage) and the bot re-decides.
                break;

            case BotState.Feint:
                fighter.SetMoveInputWorld(dirToOpp * 0.8f);
                fighter.SetBlocking(false);
                break;
        }

        // Dodge a telegraphed skill when it is already flying at us (Hard
        // reads the cast; Easy/Medium mostly eat it).
        if (opp.IsCasting && dist < 6f && difficulty == BotDifficulty.Hard
            && Random.value < 0.06f)
            fighter.TryDodge();
    }

    void Attack(FighterController opp, float dist)
    {
        // Skills first when they are ready and in range (best damage first).
        if (Random.value < preset.skillUse * 0.05f)
        {
            int pick = PickSkill(dist);
            if (pick >= 0)
            {
                fighter.TrySkill(pick);
                return;
            }
        }
        // Melee: punch chains, kick finisher, occasional dash-in.
        if (fighter.State == FighterState.Punch && fighter.ComboStep == 1)
        {
            if (Random.value < preset.comboChance) fighter.TryPunch(); // punch 2
            else if (Random.value < preset.comboChance) fighter.TryKick(); // finisher
            return;
        }
        if (fighter.State == FighterState.Punch && fighter.ComboStep == 2)
        {
            if (Random.value < preset.comboChance * 0.8f) fighter.TryKick(); // finisher
            return;
        }
        if (!fighter.IsActionBusy())
        {
            if (dist > 2.4f && Random.value < 0.08f * preset.aggression)
            {
                fighter.TryDash();
                return;
            }
            fighter.TryPunch();
        }
    }

    /// <summary>Best ready skill for the current range, or -1.</summary>
    int PickSkill(float dist)
    {
        // Prefer the hardest-hitting ready skill that fits the range.
        int[] order = { 2, 1, 3, 0 }; // skill3, skill2, skill4, skill1
        foreach (int i in order)
        {
            if (!fighter.IsSkillReady(i)) continue;
            var skill = fighter.Data.GetSkill(i);
            if (skill == null) continue;
            bool melee = i == 1 || i == 3; // whip / sweep / spin are close
            if (melee && dist > skill.range) continue;
            return i;
        }
        return -1;
    }
}
