// FighterController.cs — Elementis fighter state machine (Phase 2 combat).
//
// One state machine per fighter. Port of the combat half of fighter.gd
// (Godot) into Unity: same states, same numbers, same rules —
// hits applied at a progress point inside the action (~0.4 of the clip),
// never at button press; damage in exactly one place (TakeDamage);
// the fighter ALWAYS faces the opponent.
//
// States: Idle, Run, Punch, Kick, Skill1-4, Regen, Ultimate, Block, Hit,
// KO, Dash, WallRun, Dodge, Surf. One active action at a time.
// Movement itself lives in MovementKit (owned here); the kit asks this
// class for state and fires VFX/SFX through the On* callbacks.
//
// Animator: states are driven by NAME (Animator.CrossFade) against the
// 15-state controllers built by CharacterSetup (Idle, Run, Punch_Jab,
// Punch_Cross, Kick, Cast, Hit, KO, Jump_Start, Jump_Loop, Jump_Land,
// Dodge, Block, RegenChannel, UltimateCast). Missing animator/clip =
// skipped silently (the fight logic never depends on the animator).
//
// Events are the HUD contract — the HUD never reads fighter fields
// directly (see GAME-NOTES.md). Cooldowns are polled via
// SkillCooldownRemaining(i); everything else is an event.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum FighterState
{
    Idle, Run, Punch, Kick,
    Skill1, Skill2, Skill3, Skill4,
    Regen, Ultimate, Block, Hit, KO,
    Dash, WallRun, Dodge, Surf
}

public class FighterController : MonoBehaviour
{
    public const float MaxHealth = 350f;
    public const float MaxEnergy = 100f;
    public const float ComboWindow = 0.55f;
    public const float UltimateMax = 100f;

    // --- HUD / system events (the public API; see GAME-NOTES.md) ---
    public event Action<float, float> HealthChanged;   // (health, maxHealth)
    public event Action<float, float> ManaChanged;     // (mana, maxMana)
    public event Action<float> UltimateChanged;        // 0..1
    public event Action<bool> UltimateReadyChanged;    // meter full?
    public event Action<FighterController> Died;
    public event Action TookHit;
    public event Action<string, float, bool> TimingShown; // (text, seconds, perfect)
    public event Action<int> SkillCast;                // skill index 0..3
    public event Action<int, float> SkillCooldownStarted; // (skill index, seconds)
    public event Action RegenStarted;
    public event Action<bool> RegenEnded;              // cancelled by an enemy attack?
    public event Action UltimateCast;

    // --- Identity / setup ---
    public FighterData Data { get; private set; }
    public FighterController Opponent { get; private set; }
    public bool IsBot { get; private set; }
    public bool Frozen { get; set; }

    // --- Read-only combat state (BotAI / RoundManager / HUD polling) ---
    public float Health { get; private set; }
    public float Mana { get; private set; }
    public float UltimatePoints { get; private set; }
    public float Ultimate01 { get { return Mathf.Clamp01(UltimatePoints / UltimateMax); } }
    public bool UltimateReady { get { return UltimatePoints >= UltimateMax - 0.001f; } }
    public bool IsDead { get; private set; }
    public bool IsBlocking { get { return blocking; } }
    public bool IsChanneling { get { return State == FighterState.Regen; } }
    public bool IsCasting
    {
        get
        {
            return State == FighterState.Skill1 || State == FighterState.Skill2
                || State == FighterState.Skill3 || State == FighterState.Skill4
                || State == FighterState.Ultimate;
        }
    }
    public FighterState State { get; private set; }
    public string StateName { get; private set; } // lowercase, for MovementKit
    public float OpponentDistance { get; private set; }
    public int ComboStep { get { return comboStep; } }
    public List<string> CastLog { get; private set; }

    public CharacterController Controller { get; private set; }
    public MovementKit Kit { get; private set; }
    public Animator Animator { get; private set; }

    // --- Tunables ---
    [Tooltip("Optional surf visual prefab (Waterspout). Null = speed trail + sound only; see GAME-NOTES.md.")]
    public GameObject surfFxPrefab;

    // --- Private state ---
    bool blocking;
    float blockStartedTime;
    float stateT;      // counts DOWN from stateDur
    float stateDur;
    bool hitApplied;
    int comboStep;
    float comboT;
    float meleeDmg;
    float meleeKb;
    bool regenHeld;
    GameObject regenAuraInstance;
    GameObject sprintTrailInstance;
    GameObject surfFxInstance;
    Vector3 moveWishWorld;
    string lastAnimClip = "";

    // Hit-flash + backflip caches (REA S2/S3).
    SkinnedMeshRenderer[] bodyRenderers;
    Transform modelRoot;                 // topmost transform under the fighter root (the FBX model)
    Quaternion baseModelRotation = Quaternion.identity;
    MaterialPropertyBlock flashBlock;
    float flashUntilUnscaled;
    bool flipApplied;

    // Cooldowns: 0 punch, 1 kick, 2..5 skill1..4, 6 dash, 7 dodge.
    readonly float[] cds = new float[8];

    public void Setup(FighterData data, string characterIdFallback, bool isBot)
    {
        Data = data != null ? data : FighterDataLibrary.Create(characterIdFallback);
        IsBot = isBot;
        Health = MaxHealth;
        Mana = MaxEnergy;
        UltimatePoints = 0f;
        IsDead = false;
        Frozen = true; // RoundManager unfreezes on FIGHT.
        State = FighterState.Idle;
        StateName = "idle";
        CastLog = new List<string>();
        Controller = GetComponent<CharacterController>();
        Animator = GetComponent<Animator>();
        if (Kit == null) Kit = new MovementKit(this);
        // Hit-flash renderers + the model root for the backflip visual.
        bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        flashBlock = new MaterialPropertyBlock();
        modelRoot = FindModelRoot();
        if (modelRoot != null) baseModelRotation = modelRoot.localRotation;
    }

    // The model root is the topmost transform under the fighter root on
    // the path to the Hips bone (the FBX container, not a bone — the
    // Animator drives bones, not this transform, so rotating it for the
    // backflip does not fight the animation).
    Transform FindModelRoot()
    {
        if (Animator != null)
        {
            Transform hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                Transform t = hips;
                while (t.parent != null && t.parent != transform) t = t.parent;
                if (t != transform) return t;
            }
        }
        return transform.childCount > 0 ? transform.GetChild(0) : null;
    }

    public void SetOpponent(FighterController opponent)
    {
        Opponent = opponent;
    }

    // ------------------------------------------------------------------
    // Input surface — called by PlayerInputBridge, BotAI, or the touch UI.
    // Every Try* returns void and silently ignores illegal calls (same as
    // fighter.gd: no exceptions, no half-started actions).
    // ------------------------------------------------------------------

    public void SetMoveInput(Vector2 camSpace, float cameraYawDeg)
    {
        // camSpace: x = strafe right, y = forward (up on the stick = +y).
        // Convert camera-relative input to a world wish direction.
        float yaw = cameraYawDeg * Mathf.Deg2Rad;
        Vector3 fwd = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
        Vector3 right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        moveWishWorld = (right * camSpace.x + fwd * camSpace.y);
        if (moveWishWorld.sqrMagnitude > 1f) moveWishWorld.Normalize();
    }

    public void SetMoveInputWorld(Vector3 worldDir)
    {
        moveWishWorld = worldDir;
        if (moveWishWorld.sqrMagnitude > 1f) moveWishWorld.Normalize();
    }

    public void SetBlocking(bool b)
    {
        if (IsDead || Frozen)
        {
            blocking = false;
            return;
        }
        bool wasBlocking = blocking;
        blocking = b && (State == FighterState.Idle || State == FighterState.Run
            || State == FighterState.Block);
        if (blocking && !wasBlocking)
        {
            // Timing window starts the moment the guard goes up (reel 5 deflect).
            blockStartedTime = Time.time;
            if (State != FighterState.Block) SetState(FighterState.Block, 999f);
        }
        else if (!blocking && State == FighterState.Block)
        {
            SetState(FighterState.Idle, 0f);
        }
    }

    public void SetRegenHeld(bool held)
    {
        regenHeld = held;
        if (held) TryStartRegen();
        else if (State == FighterState.Regen) CancelRegen(false);
    }

    public bool IsActionBusy()
    {
        switch (State)
        {
            case FighterState.Punch:
            case FighterState.Kick:
            case FighterState.Skill1:
            case FighterState.Skill2:
            case FighterState.Skill3:
            case FighterState.Skill4:
            case FighterState.Ultimate:
            case FighterState.Regen:
            case FighterState.Hit:
            case FighterState.KO:
            case FighterState.Dash:
            case FighterState.Dodge:
            case FighterState.WallRun:
                return true;
            default:
                return false;
        }
    }

    bool IsRooted()
    {
        switch (State)
        {
            case FighterState.Punch:
            case FighterState.Kick:
            case FighterState.Skill1:
            case FighterState.Skill2:
            case FighterState.Skill3:
            case FighterState.Skill4:
            case FighterState.Ultimate:
            case FighterState.Regen:
            case FighterState.Hit:
            case FighterState.KO:
                return true;
            default:
                return false;
        }
    }

    public void TryPunch()
    {
        if (IsDead || Frozen) return;
        // Chaining ignores punch1's cooldown — the chain IS the timing.
        bool chaining = false;
        if (State == FighterState.Punch)
        {
            chaining = stateT <= stateDur * 0.6f && comboStep >= 1;
            if (!chaining) return;
        }
        else if (State == FighterState.Idle || State == FighterState.Run
            || State == FighterState.Block)
        {
            chaining = comboStep == 1 && comboT > 0f;
        }
        else return;
        if (!chaining && cds[0] > 0f) return;

        if (chaining)
        {
            // Punch 2: faster, harder.
            comboStep = 2;
            meleeDmg = Data.punch2Damage;
            SetState(FighterState.Punch, 0.26f);
        }
        else
        {
            comboStep = 1;
            meleeDmg = Data.punchDamage;
            SetState(FighterState.Punch, 0.30f);
        }
        comboT = ComboWindow;
        meleeKb = 3f;
        hitApplied = false;
        cds[0] = Data.punchCooldown;
        AudioManager.Play("swing_whoosh", 0.8f);
    }

    public void TryKick()
    {
        if (IsDead || Frozen || cds[1] > 0f) return;
        if (State == FighterState.Kick) return;
        if (State != FighterState.Idle && State != FighterState.Run
            && State != FighterState.Block && State != FighterState.Punch) return;
        if (State == FighterState.Punch && stateT > stateDur * 0.6f) return; // kick chains only in the late half of the punch (fighter.gd)
        // Finisher: a kick chained off a punch (in the window or straight out
        // of the punch) hits much harder and launches the opponent.
        bool finisher = comboStep >= 1 && comboT > 0f;
        if (finisher)
        {
            meleeDmg = Data.finisherDamage;
            meleeKb = 12f;
            SetState(FighterState.Kick, 0.50f);
        }
        else
        {
            meleeDmg = Data.kickDamage;
            meleeKb = 7f;
            SetState(FighterState.Kick, 0.42f);
        }
        comboStep = 0;
        comboT = 0f;
        hitApplied = false;
        cds[1] = Data.kickCooldown;
        AudioManager.Play("swing_whoosh", 0.8f);
    }

    bool SkillReady(int index)
    {
        var skill = Data.GetSkill(index);
        if (skill == null) return false;
        if (IsDead || Frozen) return false;
        if (cds[2 + index] > 0f || Mana < skill.cost) return false;
        if (State == FighterState.Skill1 || State == FighterState.Skill2
            || State == FighterState.Skill3 || State == FighterState.Skill4
            || State == FighterState.Ultimate || State == FighterState.Regen
            || State == FighterState.Hit || State == FighterState.KO
            || State == FighterState.Dash || State == FighterState.Dodge
            || State == FighterState.WallRun) return false;
        // Skills may CANCEL a punch/kick, but only in the late half of the
        // strike — the fast, fluid skill cancel (fighter.gd).
        if (State == FighterState.Punch || State == FighterState.Kick)
            return Progress() >= 0.45f;
        return State == FighterState.Idle || State == FighterState.Run
            || State == FighterState.Block;
    }

    public bool IsSkillReady(int index)
    {
        return SkillReady(index);
    }

    public void TrySkill(int index)
    {
        if (!SkillReady(index)) return;
        var skill = Data.GetSkill(index);
        SpendMana(skill.cost);
        SetState((FighterState)((int)FighterState.Skill1 + index),
            index == 1 ? 0.60f : 0.50f);
        hitApplied = false;
        comboStep = 0;
        comboT = 0f;
        cds[2 + index] = skill.cooldown;
        CastLog.Add("skill" + (index + 1));
        if (SkillCast != null) SkillCast(index);
        if (SkillCooldownStarted != null) SkillCooldownStarted(index, skill.cooldown);
    }

    public void TryUltimate()
    {
        if (IsDead || Frozen || !UltimateReady) return;
        if (IsActionBusy()) return;
        // Meter is consumed on activation (Tekken-like: the moment is spent).
        SetUltimatePoints(0f);
        SetState(FighterState.Ultimate, 2.0f);
        hitApplied = false;
        comboStep = 0;
        comboT = 0f;
        CastLog.Add("ultimate");
        if (UltimateCast != null) UltimateCast();
    }

    public void TryDash()
    {
        if (IsActionBusy() || cds[6] > 0f || IsDead || Frozen) return;
        AudioManager.Play("dash_whoosh", 0.9f);
        Vector3 toward = Opponent != null
            ? (Opponent.transform.position - transform.position) : Vector3.zero;
        toward.y = 0f;
        Kit.TryDash(moveWishWorld, toward);
        SetState(FighterState.Dash, 0.18f);
        cds[6] = 1.0f;
    }

    public void TryDodge()
    {
        if (IsActionBusy() || cds[7] > 0f || IsDead || Frozen) return;
        AudioManager.Play("dash_whoosh", 0.9f, 0.85f);
        Vector3 toward = Opponent != null
            ? (Opponent.transform.position - transform.position) : Vector3.zero;
        toward.y = 0f;
        Kit.TryDodge(moveWishWorld, toward);
        // Reel 8 slide: dodging out of a sprint is a longer, lower slide.
        SetState(FighterState.Dodge, Kit.DodgeSliding ? 0.5f : 0.35f);
        cds[7] = 1.2f;
    }

    public void TryJump()
    {
        if (IsDead || Frozen) return;
        if (State == FighterState.WallRun)
        {
            Kit.TryJump();
            return;
        }
        if (IsActionBusy()) return;
        Kit.TryJump();
    }

    public void TrySurf()
    {
        if (IsDead || Frozen) return;
        if (State != FighterState.Idle && State != FighterState.Block) return;
        if (Kit.TrySurf()) SetState(FighterState.Surf, 999f);
    }

    // ------------------------------------------------------------------
    // Regen channel — owner rule (2026-11-15 16:08): a channel stance that
    // regenerates HP + mana at a steady rate. ANY enemy attack cancels it
    // immediately (see TakeDamage). Moving, casting, or releasing the
    // button ends it voluntarily (RegenEnded(false)).
    // ------------------------------------------------------------------
    void TryStartRegen()
    {
        if (IsDead || Frozen) return;
        if (State != FighterState.Idle && State != FighterState.Run
            && State != FighterState.Block) return;
        SetState(FighterState.Regen, 999f);
        if (ElementisFx.Instance != null)
            regenAuraInstance = ElementisFx.Instance.Spawn("RegenAura", SelfGround(), Quaternion.identity);
        if (RegenStarted != null) RegenStarted();
    }

    public void CancelRegen(bool cancelledByHit)
    {
        if (State != FighterState.Regen) return;
        DestroyFx(ref regenAuraInstance);
        SetState(FighterState.Idle, 0f);
        if (RegenEnded != null) RegenEnded(cancelledByHit);
    }

    // ------------------------------------------------------------------
    // Damage — the ONE place health/mana/knockback/hit-state/death change.
    // ------------------------------------------------------------------
    public void TakeDamage(float amount, Vector3 fromPos, float knockback,
        FighterController attacker)
    {
        if (IsDead || Frozen) return;
        if (State == FighterState.Dash || State == FighterState.Dodge) return; // i-frames

        // OWNER RULE: ANY enemy attack cancels the regen channel immediately —
        // even a blocked one. The channel stance breaks the moment an attack
        // reaches the channeler.
        if (State == FighterState.Regen) CancelRegen(true);

        float finalAmount = amount;
        Vector3 toAttacker = fromPos - transform.position;
        toAttacker.y = 0f;
        bool isCounter = false;
        if (IsCasting)
        {
            // Counter: hit mid-cast — bonus damage + COUNTER text (reel 5).
            isCounter = true;
            finalAmount *= 1.25f;
        }

        bool blocked = false;
        if (blocking && toAttacker.sqrMagnitude > 0.0001f)
        {
            Vector3 fwd = transform.forward;
            if (Vector3.Angle(fwd, toAttacker.normalized) < 63f) // 1.1 rad
            {
                blocked = true;
                float dt = Time.time - blockStartedTime;
                if (dt <= 0.25f)
                {
                    // PERFECT = 0 damage + energy refund.
                    finalAmount = 0f;
                    AddMana(10f);
                    if (TimingShown != null) TimingShown("PERFECT!", dt, true);
                    AudioManager.Play("perfect_chime", 0.9f);
                    if (ElementisFx.Instance != null)
                        ElementisFx.Instance.Spawn("BlockShimmer", SelfCenter(), Quaternion.identity);
                }
                else if (dt <= 0.5f)
                {
                    finalAmount *= 0.15f;
                    if (TimingShown != null) TimingShown("GOOD", dt, false);
                    AudioManager.Play("block_clink", 0.9f);
                }
                else
                {
                    finalAmount *= 0.3f;
                    AudioManager.Play("block_clink", 0.7f);
                }
            }
        }
        if (!blocked) AudioManager.Play("impact_thud", 0.8f);

        Health -= finalAmount;
        if (Health < 0f) Health = 0f;
        if (HealthChanged != null) HealthChanged(Health, MaxHealth);
        if (TookHit != null) TookHit();

        // Hit punctuation (REA S3): a landed hit freezes the fight for a
        // few dozen ms and flashes the victim white for 80 ms. A perfect
        // block (finalAmount 0) is a successful defence — no freeze.
        if (finalAmount > 0f)
        {
            HitStop.Freeze(HitStop.ForDamage(finalAmount));
            StartFlash();
        }

        // Ultimate meter: dealing damage, taking damage, landing skills.
        if (attacker != null) attacker.AddUltimatePoints(finalAmount * 0.6f);
        AddUltimatePoints(finalAmount * 0.4f);

        if (isCounter && TimingShown != null) TimingShown("COUNTER!", 0f, false);
        if (ElementisFx.Instance != null && finalAmount > 0f)
            ElementisFx.Instance.Spawn("HitSparks", SelfCenter(), Quaternion.identity);

        if (Health <= 0f)
        {
            Die();
            return;
        }
        if (finalAmount <= 0f) return; // perfect block: no stagger, no knockback
        SetState(FighterState.Hit, 0.28f);
        if (toAttacker.sqrMagnitude > 0.0001f)
        {
            Vector3 kbDir = -toAttacker.normalized;
            Kit.AddKnockback(kbDir * knockback);
        }
    }

    void Die()
    {
        IsDead = true;
        blocking = false;
        CancelRegenSilent();
        Kit.EndSurf();
        Kit.EndWallRun();
        Kit.ResetForRound();
        SetState(FighterState.KO, 999f);
        AudioManager.Play("ko_boom", 1f);
        StartCoroutine(DeathBurst());
        if (Died != null) Died(this);
    }

    IEnumerator DeathBurst()
    {
        for (int i = 0; i < 3; i++)
        {
            if (ElementisFx.Instance != null)
                ElementisFx.Instance.Spawn("HitSparks", SelfCenter(), Quaternion.identity);
            yield return new WaitForSeconds(0.1f);
        }
    }

    void CancelRegenSilent()
    {
        DestroyFx(ref regenAuraInstance);
    }

    // ------------------------------------------------------------------
    // Per-frame (rendered): hit-flash timer + backflip visual. Update
    // runs even while timeScale is 0 (hit-stop / pause), and the flash
    // is driven by UNSCALED time so it always clears.
    // ------------------------------------------------------------------
    void Update()
    {
        if (flashUntilUnscaled > 0f && Time.unscaledTime >= flashUntilUnscaled)
        {
            flashUntilUnscaled = 0f;
            ClearFlash();
        }
        UpdateBackflip();
    }

    // White flash for 80 ms on a landed hit (REA S3): albedo + emission
    // pushed to white through a MaterialPropertyBlock (per-renderer, so
    // the shared materials are untouched; clearing the block restores
    // the material's own colours).
    void StartFlash()
    {
        if (bodyRenderers == null || bodyRenderers.Length == 0 || flashBlock == null) return;
        flashUntilUnscaled = Time.unscaledTime + 0.08f;
        flashBlock.SetColor("_BaseColor", Color.white);
        flashBlock.SetColor("_EmissionColor", Color.white);
        foreach (var r in bodyRenderers)
        {
            if (r != null) r.SetPropertyBlock(flashBlock);
        }
    }

    void ClearFlash()
    {
        if (bodyRenderers == null) return;
        foreach (var r in bodyRenderers)
        {
            if (r != null) r.SetPropertyBlock(null);
        }
    }

    // Double-jump backflip visual (REA S2): while the kit reports a flip
    // in progress, rotate the model root a full -360 deg around its local
    // X axis (ease-in-out over the 0.45 s flip), then restore the base
    // rotation. The Animator drives the bones, not this transform.
    void UpdateBackflip()
    {
        if (modelRoot == null) return;
        if (Kit != null && Kit.FlipTimeRemaining > 0f)
        {
            float p = Mathf.Clamp01(1f - Kit.FlipTimeRemaining / 0.45f);
            float eased = p * p * (3f - 2f * p);
            modelRoot.localRotation = baseModelRotation * Quaternion.AngleAxis(-360f * eased, Vector3.right);
            flipApplied = true;
        }
        else if (flipApplied)
        {
            modelRoot.localRotation = baseModelRotation;
            flipApplied = false;
        }
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------
    void FixedUpdate()
    {
        if (Data == null) return;
        if (Frozen || IsDead)
        {
            // Frozen (countdown / round end) or KO: rooted, no timers, no
            // movement — but still settle to the ground if airborne.
            if (Controller != null)
            {
                Controller.Move(new Vector3(0f, -2f * Time.fixedDeltaTime, 0f));
                Kit.PostMove();
            }
            SyncAnimator();
            return;
        }

        float dt = Time.fixedDeltaTime;

        // Cooldowns.
        for (int i = 0; i < cds.Length; i++)
            cds[i] = Mathf.Max(0f, cds[i] - dt);

        // Combo window ticks down only while idle/running.
        if (comboStep > 0 && (State == FighterState.Idle || State == FighterState.Run))
        {
            comboT -= dt;
            if (comboT <= 0f) { comboStep = 0; comboT = 0f; }
        }

        // Passive mana regen (12/s, 6/s while blocking) — NOT while channeling
        // (the channel has its own rates; no double-dip).
        if (State != FighterState.Regen)
            AddMana((blocking ? 6f : 12f) * dt);

        // Regen channel tick.
        if (State == FighterState.Regen)
        {
            Health = Mathf.Min(MaxHealth, Health + Data.regenHpPerSecond * dt);
            if (HealthChanged != null) HealthChanged(Health, MaxHealth);
            AddMana(Data.regenManaPerSecond * dt);
        }

        // Sprint trail emission (sprinting or surfing).
        UpdateSprintTrail();

        // Action timer + hit point (~0.4 of the action).
        if (stateT > 0f)
        {
            stateT -= dt;
            if (!hitApplied) TryApplyHit();
            if (stateT <= 0f && State != FighterState.Idle && State != FighterState.Block
                && State != FighterState.KO && State != FighterState.Regen
                && State != FighterState.Surf && State != FighterState.WallRun)
            {
                SetState(blocking ? FighterState.Block : FighterState.Idle, 0f);
            }
        }

        // Locomotion state from movement.
        if (State == FighterState.Idle || State == FighterState.Run)
        {
            bool moving = Kit.HorizontalSpeed > 0.6f && Kit.IsGrounded;
            if (moving && State == FighterState.Idle) SetState(FighterState.Run, 0f);
            else if (!moving && State == FighterState.Run) SetState(FighterState.Idle, 0f);
        }
        // Guard held through an action: back to the Block state when idle.
        if (blocking && State == FighterState.Idle) SetState(FighterState.Block, 999f);

        // Opponent distance (camera + bot AI + closing bonus).
        if (Opponent != null)
        {
            Vector3 to = Opponent.transform.position - transform.position;
            to.y = 0f;
            OpponentDistance = to.magnitude;
        }

        // Facing — combat rule (v1.2): ALWAYS face the opponent.
        FaceOpponent(dt);

        // Movement.
        bool canMove = !IsRooted() && !IsDead && !Frozen;
        Kit.UpdateMovement(dt, moveWishWorld, canMove, IsRooted(), blocking, StateName);
        if (Controller != null)
        {
            Controller.Move(Kit.Velocity * dt);
            Kit.PostMove();
        }

        SyncAnimator();
    }

    float Progress()
    {
        if (stateDur <= 0.001f) return 1f;
        return Mathf.Clamp01(1f - stateT / stateDur);
    }

    void TryApplyHit()
    {
        float prog = Progress();
        switch (State)
        {
            case FighterState.Punch:
                if (prog >= 0.4f)
                {
                    hitApplied = true;
                    if (MeleeHit(meleeDmg, 1.9f, 100f, meleeKb))
                        AudioManager.Play("punch_hit", 0.9f);
                }
                break;
            case FighterState.Kick:
                if (prog >= 0.42f)
                {
                    hitApplied = true;
                    if (MeleeHit(meleeDmg, 2.1f, 110f, meleeKb))
                        AudioManager.Play("punch_hit", 0.9f);
                }
                break;
            case FighterState.Skill1:
                if (prog >= 0.44f) { hitApplied = true; ApplySkill1(); }
                break;
            case FighterState.Skill2:
                if (prog >= 0.45f) { hitApplied = true; ApplySkill2(); }
                break;
            case FighterState.Skill3:
                if (prog >= 0.45f) { hitApplied = true; ApplySkill3(); }
                break;
            case FighterState.Skill4:
                if (prog >= 0.45f) { hitApplied = true; ApplySkill4(); }
                break;
            case FighterState.Ultimate:
                if (prog >= 0.5f) { hitApplied = true; ApplyUltimate(); }
                break;
        }
    }

    // ------------------------------------------------------------------
    // Skill application (hit point of each cast). VFX/SFX per VFX-NOTES.
    // ------------------------------------------------------------------
    void ApplySkill1()
    {
        var skill = Data.GetSkill(0);
        AudioManager.Play(skill.castSound, 0.9f);
        if (ElementisFx.Instance == null || Opponent == null) return;
        Vector3 hand = HandPosition();
        Vector3 target = Opponent.transform.position + Vector3.up * 1.3f;
        GameObject fx = ElementisFx.Instance.Spawn(skill.vfxPrefab, hand, Quaternion.identity);
        if (fx == null) return;
        var proj = fx.AddComponent<SkillProjectile>();
        Vector3 dir = (target - fx.transform.position).normalized;
        // Water arcs (REA S4); Azula's fireball flies straight and fast.
        float arc = Data.characterId == "katara" ? 1.2f : 0f;
        proj.Launch(this, Opponent, skill.damage, 7f, 22f, 5f, 1.2f,
            skill.hitSound, "HitSparks", dir, arc);
    }

    void ApplySkill2()
    {
        var skill = Data.GetSkill(1);
        AudioManager.Play(skill.castSound, 0.9f);
        // Water/fire comes FROM the hand (REA S4: "water follows arms") —
        // the whip is released from the casting hand, not the body centre.
        if (ElementisFx.Instance != null)
            ElementisFx.Instance.Spawn(skill.vfxPrefab, HandPosition(), FacingRotation());
        if (MeleeHit(skill.damage, skill.range, 150f, 7f, true))
            AudioManager.Play(skill.hitSound, 0.9f);
    }

    void ApplySkill3()
    {
        var skill = Data.GetSkill(2);
        // Telegraph: the VFX prefab spawns at the opponent's ground position
        // LOCKED at cast time; the eruption lands hitDelay later. The sound
        // plays at the eruption, synced with the visible bolt/spikes.
        if (Opponent == null) return;
        Vector3 groundTarget = Opponent.transform.position;
        groundTarget.y = 0f;
        if (ElementisFx.Instance != null)
            ElementisFx.Instance.Spawn(skill.vfxPrefab, groundTarget + Vector3.up * 0.05f,
                Quaternion.identity);
        StartCoroutine(TelegraphedHit(skill, groundTarget));
    }

    IEnumerator TelegraphedHit(SkillDef skill, Vector3 groundTarget)
    {
        yield return new WaitForSeconds(skill.hitDelay);
        AudioManager.Play(skill.castSound, 0.9f);
        if (Opponent == null || Opponent.IsDead) yield break;
        Vector3 d = Opponent.transform.position - groundTarget;
        d.y = 0f;
        if (d.magnitude < skill.range)
        {
            float kb = Data.characterId == "katara" ? 8f : 10f;
            Opponent.TakeDamage(skill.damage, transform.position, kb, this);
            AddUltimatePoints(5f); // skill landed
        }
    }

    void ApplySkill4()
    {
        var skill = Data.GetSkill(3);
        AudioManager.Play(skill.castSound, 0.9f);
        if (Data.characterId == "katara")
        {
            // Tidal Wave: a wide wave travels forward, pushing + damaging.
            if (ElementisFx.Instance == null) return;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 spawn = transform.position + fwd * 1.2f + Vector3.up * 0.9f;
            GameObject fx = ElementisFx.Instance.Spawn(skill.vfxPrefab, spawn,
                Quaternion.LookRotation(fwd));
            if (fx == null || Opponent == null) return;
            var proj = fx.AddComponent<SkillProjectile>();
            // The wave travels as an arcing wall of water (REA S4).
            proj.Launch(this, Opponent, skill.damage, 6f, 10f, 5f, 2.0f,
                skill.hitSound, "HitSparks", fwd, 1.0f);
            AudioManager.Play("wave_rumble", 0.7f);
        }
        else
        {
            // Fire Spin: a ring of blue fire spins out around her — close AoE.
            if (ElementisFx.Instance != null)
                ElementisFx.Instance.Spawn(skill.vfxPrefab, SelfCenter(), Quaternion.identity);
            AudioManager.Play("blue_fire_crackle", 0.8f);
            if (MeleeHit(skill.damage, skill.range, 360f, 9f, true))
                AudioManager.Play(skill.hitSound, 0.9f);
        }
    }

    void ApplyUltimate()
    {
        AudioManager.Play(Data.ultimateSound, 1f);
        if (ElementisFx.Instance != null)
            ElementisFx.Instance.Spawn(Data.ultimateVfx, SelfGround(), Quaternion.identity);
        if (ReelCamera.Instance != null) ReelCamera.Instance.Shake(0.7f);
        if (Opponent == null || Opponent.IsDead) return;
        Vector3 d = Opponent.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude < Data.ultimateRange)
        {
            Opponent.TakeDamage(Data.ultimateDamage, transform.position,
                Data.ultimateKnockback, this);
            AddUltimatePoints(5f);
        }
    }

    // ------------------------------------------------------------------
    // Melee / hit helpers
    // ------------------------------------------------------------------
    bool MeleeHit(float dmg, float reach, float arcDeg, float kb, bool isSkill = false)
    {
        if (Opponent == null || Opponent.IsDead) return false;
        Vector3 to = Opponent.transform.position - transform.position;
        to.y = 0f;
        if (to.magnitude > reach) return false;
        if (to.sqrMagnitude > 0.0001f
            && Vector3.Angle(transform.forward, to.normalized) > arcDeg * 0.5f) return false;
        Opponent.TakeDamage(dmg, transform.position, kb, this);
        if (isSkill) AddUltimatePoints(5f); // skill landed
        return true;
    }

    // ------------------------------------------------------------------
    // Rounds
    // ------------------------------------------------------------------
    public void ResetForRound(Vector3 pos, float yawDeg)
    {
        Health = MaxHealth;
        Mana = MaxEnergy;
        SetUltimatePoints(0f);
        IsDead = false;
        blocking = false;
        regenHeld = false;
        CancelRegenSilent();
        if (Kit != null)
        {
            Kit.EndSurf();
            Kit.EndWallRun();
            Kit.ResetForRound();
        }
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        SetState(FighterState.Idle, 0f);
        for (int i = 0; i < cds.Length; i++) cds[i] = 0f;
        comboStep = 0;
        comboT = 0f;
        moveWishWorld = Vector3.zero;
        DestroyFx(ref sprintTrailInstance);
        DestroyFx(ref surfFxInstance);
        if (HealthChanged != null) HealthChanged(Health, MaxHealth);
        if (ManaChanged != null) ManaChanged(Mana, MaxEnergy);
    }

    // ------------------------------------------------------------------
    // Cooldown / meter queries (HUD polling)
    // ------------------------------------------------------------------
    public float SkillCooldownRemaining(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex > 3) return 0f;
        return cds[2 + skillIndex];
    }

    public float SkillCooldownTotal(int skillIndex)
    {
        var skill = Data != null ? Data.GetSkill(skillIndex) : null;
        return skill != null ? skill.cooldown : 0f;
    }

    public float DashCooldownRemaining { get { return cds[6]; } }
    public float DodgeCooldownRemaining { get { return cds[7]; } }

    // ------------------------------------------------------------------
    // Mana / ultimate helpers
    // ------------------------------------------------------------------
    public void SpendMana(float amount)
    {
        Mana = Mathf.Max(0f, Mana - amount);
        if (ManaChanged != null) ManaChanged(Mana, MaxEnergy);
    }

    public void DrainManaForSurf(float dt)
    {
        SpendMana(8f * dt);
    }

    public void AddMana(float amount)
    {
        float before = Mana;
        Mana = Mathf.Min(MaxEnergy, Mana + amount);
        if (Mathf.Abs(Mana - before) > 0.001f && ManaChanged != null)
            ManaChanged(Mana, MaxEnergy);
    }

    public void AddUltimatePoints(float points)
    {
        bool wasReady = UltimateReady;
        UltimatePoints = Mathf.Clamp(UltimatePoints + points, 0f, UltimateMax);
        if (UltimateChanged != null) UltimateChanged(Ultimate01);
        if (UltimateReady != wasReady)
        {
            if (UltimateReadyChanged != null) UltimateReadyChanged(UltimateReady);
        }
    }

    void SetUltimatePoints(float points)
    {
        bool wasReady = UltimateReady;
        UltimatePoints = Mathf.Clamp(points, 0f, UltimateMax);
        if (UltimateChanged != null) UltimateChanged(Ultimate01);
        if (UltimateReady != wasReady && UltimateReadyChanged != null)
            UltimateReadyChanged(UltimateReady);
    }

    // ------------------------------------------------------------------
    // MovementKit callbacks (VFX/SFX edges)
    // ------------------------------------------------------------------
    public void OnSprintStarted()
    {
        AudioManager.Play("dash_whoosh", 0.5f, 1.1f);
    }

    public void OnJumped(bool superJump)
    {
        AudioManager.Play("jump_whoosh", 0.8f, superJump ? 0.85f : 1f);
    }

    public void OnHardLanding(bool hard)
    {
        if (ElementisFx.Instance != null)
            ElementisFx.Instance.Spawn("DustBurst", SelfGround(), Quaternion.identity);
        if (hard) AudioManager.Play("impact_thud", 0.5f);
    }

    public void OnWallRunStarted()
    {
        if (State == FighterState.Idle || State == FighterState.Run)
            SetState(FighterState.WallRun, 999f);
    }
    public void OnWallRunEnded()
    {
        if (State == FighterState.WallRun) SetState(FighterState.Idle, 0f);
    }

    public void OnSurfStarted()
    {
        AudioManager.Play("water_whoosh", 0.8f);
        AudioManager.Play("wave_rumble", 0.6f);
        if (surfFxPrefab != null)
        {
            surfFxInstance = Instantiate(surfFxPrefab, SelfGround(), Quaternion.identity, transform);
        }
        else if (ElementisFx.Instance != null)
        {
            // No authored spout prefab: build the translucent water
            // column + spiral spray procedurally (ElementisFx).
            surfFxInstance = ElementisFx.Instance.SpawnSurfSpout(SelfGround(), transform);
        }
    }

    public void OnSurfEnded()
    {
        DestroyFx(ref surfFxInstance);
        if (State == FighterState.Surf) SetState(FighterState.Idle, 0f);
    }

    void UpdateSprintTrail()
    {
        bool want = (Kit.Sprinting || Kit.Surfing) && !IsDead;
        if (want && sprintTrailInstance == null && ElementisFx.Instance != null)
        {
            sprintTrailInstance = ElementisFx.Instance.Spawn("SprintTrail",
                SelfGround(), Quaternion.identity, transform);
        }
        else if (!want && sprintTrailInstance != null)
        {
            DestroyFx(ref sprintTrailInstance);
        }
    }

    // ------------------------------------------------------------------
    // Facing, animation, spawn points
    // ------------------------------------------------------------------
    void FaceOpponent(float dt)
    {
        if (Opponent == null || IsDead) return;
        Vector3 to;
        if (State == FighterState.WallRun && Kit != null)
        {
            // Wall-running keeps its travel facing.
            to = Kit.WallTangent;
        }
        else
        {
            to = Opponent.transform.position - transform.position;
        }
        to.y = 0f;
        if (to.sqrMagnitude < 0.0025f) return;
        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float cur = transform.eulerAngles.y;
        float next = Mathf.MoveTowardsAngle(cur, want, 540f * dt);
        transform.rotation = Quaternion.Euler(0f, next, 0f);
    }

    void SetState(FighterState state, float duration)
    {
        State = state;
        StateName = state.ToString().ToLowerInvariant();
        stateDur = duration;
        stateT = duration;
    }

    void SyncAnimator()
    {
        if (Animator == null) return;
        string clip = AnimClipForState();
        if (clip == lastAnimClip) return;
        lastAnimClip = clip;
        int hash = Animator.StringToHash(clip);
        if (!Animator.HasState(0, hash)) return;
        Animator.CrossFade(hash, 0.08f, 0);
    }

    string AnimClipForState()
    {
        // Air / landing override the locomotion clip.
        if ((State == FighterState.Idle || State == FighterState.Run) && Kit != null)
        {
            if (!Kit.IsGrounded) return "Jump_Loop";
            if (Kit.LandCrouchRemaining > 0f) return "Jump_Land";
        }
        switch (State)
        {
            case FighterState.Idle: return "Idle";
            case FighterState.Run: return "Run";
            case FighterState.Punch: return comboStep >= 2 ? "Punch_Cross" : "Punch_Jab";
            case FighterState.Kick: return "Kick";
            case FighterState.Skill1:
            case FighterState.Skill2:
            case FighterState.Skill3:
            case FighterState.Skill4: return "Cast";
            case FighterState.Ultimate: return "UltimateCast";
            case FighterState.Regen: return "RegenChannel";
            case FighterState.Block: return "Block";
            case FighterState.Hit: return "Hit";
            case FighterState.KO: return "KO";
            case FighterState.Dash: return "Run";
            case FighterState.Dodge: return "Dodge";
            case FighterState.WallRun: return "Run";
            case FighterState.Surf: return "Run";
            default: return "Idle";
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Kit != null) Kit.NoteWallHit(hit.normal);
    }

    void DestroyFx(ref GameObject instance)
    {
        if (instance != null)
        {
            Destroy(instance);
            instance = null;
        }
    }

    // --- Spawn points (VFX-NOTES.md convention) ---
    Vector3 HandPosition()
    {
        if (Animator != null)
        {
            Transform hand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null) return hand.position;
        }
        return transform.position + Vector3.up * 1.3f + transform.forward * 0.3f;
    }

    Vector3 SelfCenter()
    {
        return transform.position + Vector3.up * 1.2f;
    }

    Vector3 SelfGround()
    {
        return transform.position + Vector3.up * 0.05f;
    }

    Quaternion FacingRotation()
    {
        return Quaternion.LookRotation(transform.forward);
    }
}
