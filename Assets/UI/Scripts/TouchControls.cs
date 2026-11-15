// TouchControls.cs — Elementis fight controls (Phase 2b).
//
// THE HARD REQUIREMENT (owner, 2026-11-15 16:02): the player must move
// with the joystick AND press skill/attack buttons AT THE SAME TIME.
// The Godot builds were broken here (moving blocked skill presses).
//
// How concurrency works here (Input System 1.14.0, verified against the
// package source — see UI-NOTES.md):
//  * Every control is an Input System on-screen control feeding ONE
//    virtual Gamepad device (OnScreenControl creates one device per
//    layout; all 13 paths below use the "Gamepad" layout, so they share
//    a single virtual gamepad). Each controlPath below is a control the
//    Fighting map ALREADY binds (ElementisInputActions.inputactions):
//      Move     <Gamepad>/leftStick        Punch    <Gamepad>/buttonWest
//      Kick     <Gamepad>/buttonEast        Skill1   <Gamepad>/dpad/up
//      Skill2   <Gamepad>/dpad/right       Skill3   <Gamepad>/dpad/down
//      Skill4   <Gamepad>/dpad/left        Block    <Gamepad>/leftShoulder
//      Dash     <Gamepad>/buttonNorth      Jump     <Gamepad>/buttonSouth
//      Regen    <Gamepad>/rightShoulder    Ultimate <Gamepad>/rightTrigger
//      Surf     <Gamepad>/rightStickButton
//  * Unity's EventSystem tracks each finger as a separate pointer
//    (pointerId). OnScreenStick handles the joystick finger's
//    drag; each OnScreenButton handles its own finger's down/up.
//    Different fingers drive different controls, so move + cast, move +
//    punch, and joystick + two buttons all work simultaneously.
//    Nothing in this file (or in the EventSystem) serialises touches.
//  * Fallback (no Input System package — the GAME-NOTES.md fallback
//    world): the same buttons get UiButtonPress components calling
//    PlayerInputBridge.Ui*() directly, and the joystick gets a direct
//    drag handler calling UiMove(). Same visuals, same layout.
//    The two paths are never both live on one button (double-fire).
//  * Pre-play gate (coordinator step, ProjectSettings — NOT done here):
//    Player Settings -> Active Input Handler -> Both (or Input System).
//
// Layout: reference 1920x1080 landscape, px. Joystick bottom-left,
// LARGE (260 base). Skill buttons bottom-right, big (96) with 16-20 px
// gaps. Basic attack (PUNCH, 150) is a SEPARATE cluster — bigger,
// circular, warm red-orange, 67 px clear of the skill cluster —
// Mobile Legends style, never confused with a skill. The ULTIMATE
// button (132) is the most prominent skill button and lights up only
// when UltimateReadyChanged fires (the fighter gates TryUltimate;
// the button is visual-only gating).
// All sizes/positions are named constants below; UI-NOTES.md tabulates
// them with the no-overlap proof, and a verifier script checks them.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.OnScreen;
#endif

public class TouchControls : MonoBehaviour, IPointerDownHandler, IDragHandler,
    IPointerUpHandler
{
    // --- LAYOUT (reference 1920x1080 landscape, px; see UI-NOTES.md) ---
    // Joystick (anchor bottom-left; values = centre offset from the corner).
    public const float JoyCenterX = 190f;
    public const float JoyCenterY = 170f;
    public const float JoyBaseSize = 260f;
    public const float JoyKnobSize = 110f;
    public const float JoyMoveRange = 110f; // ref px; x CanvasScaler scaleFactor at runtime
    // Basic-attack cluster (anchor bottom-right; x = centre offset from the corner).
    public const float PunchCenterX = -520f;
    public const float PunchCenterY = 95f;
    public const float PunchSize = 150f;
    public const float KickCenterX = -680f;
    public const float KickCenterY = 95f;
    public const float KickSize = 104f;
    // Hold cluster (Block / Regen, above the basic-attack cluster).
    public const float BlockCenterX = -556f;
    public const float BlockCenterY = 292f;
    public const float BlockSize = 96f;
    public const float RegenCenterX = -668f;
    public const float RegenCenterY = 292f;
    public const float RegenSize = 96f;
    // Skill cluster (2x2, anchor bottom-right).
    public const float Skill1CenterX = -218f;
    public const float Skill1CenterY = 68f;
    public const float Skill2CenterX = -218f;
    public const float Skill2CenterY = 180f;
    public const float Skill3CenterX = -330f;
    public const float Skill3CenterY = 68f;
    public const float Skill4CenterX = -330f;
    public const float Skill4CenterY = 180f;
    public const float SkillSize = 96f;
    public const float SkillGap = 16f; // edge-to-edge gap inside the cluster
    // Ultimate (most prominent skill button, far right) + movement row.
    public const float UltCenterX = -86f;
    public const float UltCenterY = 106f;
    public const float UltSize = 132f;
    public const float DashCenterX = -330f;
    public const float DashCenterY = 292f;
    public const float DashSize = 88f;
    public const float JumpCenterX = -218f;
    public const float JumpCenterY = 292f;
    public const float JumpSize = 88f;
    public const float SurfCenterX = -444f;
    public const float SurfCenterY = 292f;
    public const float SurfSize = 88f;

    FighterController player;
    PlayerInputBridge bridge;
    Canvas canvas;
    Transform controlsParent;
    bool controlsChecked;

    RectTransform joyBase;
    RectTransform joyKnob;
#if ENABLE_INPUT_SYSTEM
    OnScreenStick stick;
#endif
    bool stickRangeApplied;

    Button[] skillButtons = new Button[4];
    Image[] skillImages = new Image[4];
    Image[] skillCooldown = new Image[4];
    Text[] skillCooldownText = new Text[4];
    Button punchButton, kickButton, blockButton, regenButton;
    Button dashButton, jumpButton, surfButton, ultButton;
    Image ultRing, ultFill;
    Text ultLabel;

    Color skillColor = UiKit.KataraBlue;

    // Build the controls under the HUD canvas. Called once by UIFlowManager.
    public void Build(Transform parent, FighterController playerFighter, Canvas hudCanvas)
    {
        player = playerFighter;
        canvas = hudCanvas;
        controlsParent = parent;
        bridge = player != null ? player.GetComponent<PlayerInputBridge>() : null;

        // --- Joystick (bottom-left) ---
        var joyGo = new GameObject("Joystick");
        joyGo.transform.SetParent(parent, false);
        var joyImg = joyGo.AddComponent<Image>();
        joyImg.sprite = UiKit.Circle;
        joyImg.color = new Color(1f, 1f, 1f, 0.22f);
        joyImg.raycastTarget = true; // the touch zone
        joyBase = UiKit.Rect(joyGo, Vector2.zero, Vector2.zero,
            new Vector2(JoyCenterX, JoyCenterY),
            new Vector2(JoyBaseSize, JoyBaseSize), new Vector2(0.5f, 0.5f));
        // Ring so the base reads on the bright arena floor.
        var ringGo = new GameObject("JoystickRing");
        ringGo.transform.SetParent(joyGo.transform, false);
        var ring = ringGo.AddComponent<Image>();
        ring.sprite = UiKit.Circle;
        ring.color = new Color(1f, 1f, 1f, 0.35f);
        ring.raycastTarget = false;
        UiKit.Rect(ringGo, Vector2.zero, Vector2.one, Vector2.zero,
            new Vector2(JoyBaseSize + 10f, JoyBaseSize + 10f), new Vector2(0.5f, 0.5f));
        var knobGo = new GameObject("JoystickKnob");
        knobGo.transform.SetParent(joyGo.transform, false);
        var knob = knobGo.AddComponent<Image>();
        knob.sprite = UiKit.Circle;
        knob.color = new Color(1f, 1f, 1f, 0.55f);
        knob.raycastTarget = false;
        joyKnob = UiKit.Rect(knobGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(JoyKnobSize, JoyKnobSize), new Vector2(0.5f, 0.5f));
#if ENABLE_INPUT_SYSTEM
        stick = joyGo.AddComponent<OnScreenStick>();
        stick.controlPath = "<Gamepad>/leftStick";
        stick.movementRange = JoyMoveRange; // refined x scaleFactor in Update
#endif
        // The knob visual follows the drag (this component's pointer
        // handlers run alongside OnScreenStick's — same finger, no conflict).
        joyGo.AddComponent<TouchControlsForwarder>().owner = this;

        // --- Basic-attack cluster (separate, bigger, warmer — ML style) ---
        punchButton = MakeFightButton(parent, "PunchButton", "PUNCH",
            new Vector2(PunchCenterX, PunchCenterY), PunchSize,
            new Color(0.92f, 0.32f, 0.12f), 26,
            "<Gamepad>/buttonWest", UiButtonPress.PressKind.Punch);
        kickButton = MakeFightButton(parent, "KickButton", "KICK",
            new Vector2(KickCenterX, KickCenterY), KickSize,
            new Color(0.85f, 0.50f, 0.15f), 22,
            "<Gamepad>/buttonEast", UiButtonPress.PressKind.Kick);

        // --- Hold cluster ---
        blockButton = MakeFightButton(parent, "BlockButton", "BLOCK",
            new Vector2(BlockCenterX, BlockCenterY), BlockSize,
            new Color(0.35f, 0.42f, 0.55f), 20,
            "<Gamepad>/leftShoulder", UiButtonPress.PressKind.Block);
        regenButton = MakeFightButton(parent, "RegenButton", "REGEN",
            new Vector2(RegenCenterX, RegenCenterY), RegenSize,
            new Color(0.20f, 0.65f, 0.30f), 20,
            "<Gamepad>/rightShoulder", UiButtonPress.PressKind.Regen);

        // --- Skill cluster (2x2) ---
        Vector2[] skillCentres =
        {
            new Vector2(Skill1CenterX, Skill1CenterY),
            new Vector2(Skill2CenterX, Skill2CenterY),
            new Vector2(Skill3CenterX, Skill3CenterY),
            new Vector2(Skill4CenterX, Skill4CenterY),
        };
        string[] skillPaths =
        {
            "<Gamepad>/dpad/up", "<Gamepad>/dpad/right",
            "<Gamepad>/dpad/down", "<Gamepad>/dpad/left",
        };
        UiButtonPress.PressKind[] skillKinds =
        {
            UiButtonPress.PressKind.Skill1, UiButtonPress.PressKind.Skill2,
            UiButtonPress.PressKind.Skill3, UiButtonPress.PressKind.Skill4,
        };
        for (int i = 0; i < 4; i++)
        {
            skillButtons[i] = MakeFightButton(parent, "Skill" + (i + 1) + "Button",
                "SKILL " + (i + 1), skillCentres[i], SkillSize,
                UiKit.KataraBlue, 20, skillPaths[i], skillKinds[i]);
            skillImages[i] = skillButtons[i].GetComponent<Image>();
            // Cooldown sweep (radial, dark) + seconds text, both non-raycast.
            var cdGo = new GameObject("CooldownSweep");
            cdGo.transform.SetParent(skillButtons[i].transform, false);
            var cd = cdGo.AddComponent<Image>();
            cd.sprite = UiKit.Circle;
            cd.color = new Color(0f, 0f, 0f, 0.55f);
            cd.type = Image.Type.Filled;
            cd.fillMethod = Image.FillMethod.Radial360;
            cd.fillOrigin = (int)Image.Origin360.Top;
            cd.fillClockwise = true;
            cd.fillAmount = 0f;
            cd.raycastTarget = false;
            UiKit.Stretch(cdGo);
            skillCooldown[i] = cd;
            skillCooldownText[i] = UiKit.Label(skillButtons[i].transform,
                "CooldownText", "", 28, Color.white);
            UiKit.Stretch(skillCooldownText[i].gameObject);
        }

        // --- Ultimate (most prominent skill button, far right) ---
        var ultRingGo = new GameObject("UltRing");
        ultRingGo.transform.SetParent(parent, false);
        ultRing = ultRingGo.AddComponent<Image>();
        ultRing.sprite = UiKit.Circle;
        ultRing.color = new Color(0.91f, 0.71f, 0.30f, 0.35f);
        ultRing.raycastTarget = false;
        UiKit.Rect(ultRingGo, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(UltCenterX, UltCenterY),
            new Vector2(UltSize + 18f, UltSize + 18f), new Vector2(0.5f, 0.5f));
        ultButton = MakeFightButton(parent, "UltButton", "ULT",
            new Vector2(UltCenterX, UltCenterY), UltSize,
            new Color(0.55f, 0.38f, 0.10f), 24,
            "<Gamepad>/rightTrigger", UiButtonPress.PressKind.Ultimate);
        var ultFillGo = new GameObject("UltFill");
        ultFillGo.transform.SetParent(ultButton.transform, false);
        ultFill = ultFillGo.AddComponent<Image>();
        ultFill.sprite = UiKit.Circle;
        ultFill.color = new Color(0.91f, 0.71f, 0.30f, 0.85f);
        ultFill.type = Image.Type.Filled;
        ultFill.fillMethod = Image.FillMethod.Radial360;
        ultFill.fillOrigin = (int)Image.Origin360.Bottom;
        ultFill.fillClockwise = true;
        ultFill.fillAmount = 0f;
        ultFill.raycastTarget = false;
        UiKit.Stretch(ultFillGo);
        ultFillGo.transform.SetAsFirstSibling(); // fill sits behind the label
        ultLabel = ultButton.GetComponentInChildren<Text>();

        // --- Movement row (Dash / Jump / Surf) ---
        dashButton = MakeFightButton(parent, "DashButton", "DASH",
            new Vector2(DashCenterX, DashCenterY), DashSize,
            new Color(0.45f, 0.35f, 0.65f), 20,
            "<Gamepad>/buttonNorth", UiButtonPress.PressKind.Dash);
        jumpButton = MakeFightButton(parent, "JumpButton", "JUMP",
            new Vector2(JumpCenterX, JumpCenterY), JumpSize,
            new Color(0.20f, 0.55f, 0.55f), 20,
            "<Gamepad>/buttonSouth", UiButtonPress.PressKind.Jump);
        surfButton = MakeFightButton(parent, "SurfButton", "SURF",
            new Vector2(SurfCenterX, SurfCenterY), SurfSize,
            new Color(0.20f, 0.60f, 0.95f), 20,
            "<Gamepad>/rightStickButton", UiButtonPress.PressKind.Surf);

        RefreshSkillLabels();
    }

    // Re-point the controls at a new player fighter (hero swap respawns
    // the fighters — see FightBootstrap.BeginMatch). The joystick reads
    // the bridge field every frame, but the no-Input-System buttons
    // captured it at Build time, so their UiButtonPress.bridge must be
    // re-pointed too (the OnScreenButton path needs nothing: it drives
    // Input System actions, not the bridge).
    public void RebindPlayer(FighterController playerFighter)
    {
        player = playerFighter;
        bridge = player != null ? player.GetComponent<PlayerInputBridge>() : null;
        if (controlsParent != null)
        {
            var presses = controlsParent.GetComponentsInChildren<UiButtonPress>(true);
            foreach (var press in presses) press.bridge = bridge;
        }
        RefreshSkillLabels();
    }

    // One fight button: circular, bottom-right anchored, labelled.
    // controlPath is the Input System path (ignored in the no-package build).
    Button MakeFightButton(Transform parent, string name, string label,
        Vector2 centreFromBottomRight, float size, Color bg, int fontSize,
        string controlPath, UiButtonPress.PressKind kind)
    {
        var btn = UiKit.MakeButton(parent, name, label, new Vector2(size, size),
            bg, fontSize, Color.white, circular: true);
        UiKit.Rect(btn.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f),
            centreFromBottomRight, new Vector2(size, size), new Vector2(0.5f, 0.5f));
#if ENABLE_INPUT_SYSTEM
        var osc = btn.gameObject.AddComponent<OnScreenButton>();
        osc.controlPath = controlPath;
#else
        var press = btn.gameObject.AddComponent<UiButtonPress>();
        press.kind = kind;
        press.bridge = bridge;
#endif
        return btn;
    }

    // Real skill names + mana costs from the player's FighterData
    // (GAME-NOTES.md table; Katara: BLAST 25 / WHIP 35 / SPIKES 30 / WAVE 40).
    public void RefreshSkillLabels()
    {
        if (player == null || player.Data == null) return;
        skillColor = player.Data.accentColor;
        for (int i = 0; i < 4; i++)
        {
            var skill = player.Data.GetSkill(i);
            if (skill == null) continue;
            var labelT = skillButtons[i].transform.Find("Label");
            var label = labelT != null ? labelT.GetComponent<Text>() : null;
            if (label != null)
                label.text = skill.shortName + "\n" + Mathf.RoundToInt(skill.cost);
            if (skillImages[i] != null) skillImages[i].color = skillColor;
        }
    }

    // Joystick knob visual (this component lives on the UI root; the
    // forwarder on the joystick base relays its pointer events here).
    // Only the visual knob moves here — the VALUE comes from OnScreenStick
    // (or, in the no-package build, from UiMove below).
    public void OnPointerDown(PointerEventData data) { DragKnob(data); }
    public void OnDrag(PointerEventData data) { DragKnob(data); }
    public void OnPointerUp(PointerEventData data)
    {
        if (joyKnob != null) joyKnob.anchoredPosition = Vector2.zero;
#if !ENABLE_INPUT_SYSTEM
        if (bridge != null) bridge.UiMove(Vector2.zero);
#endif
    }

    void DragKnob(PointerEventData data)
    {
        if (joyBase == null || joyKnob == null) return;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                joyBase, data.position, null, out local)) return;
        float range = JoyMoveRange * (canvas != null ? canvas.scaleFactor : 1f);
        Vector2 clamped = Vector2.ClampMagnitude(local, range);
        joyKnob.anchoredPosition = clamped;
#if !ENABLE_INPUT_SYSTEM
        if (bridge != null) bridge.UiMove(clamped / range);
#endif
    }

    void Update()
    {
        CheckControlsResolved();
#if ENABLE_INPUT_SYSTEM
        // OnScreenStick.movementRange is in screen px; the reference-space
        // 110 px must be scaled by the CanvasScaler factor (1.0 at 1080p).
        if (!stickRangeApplied && stick != null && canvas != null)
        {
            stick.movementRange = JoyMoveRange * canvas.scaleFactor;
            stickRangeApplied = true;
        }
#endif
        if (player == null) return;
        PollSkillButtons();
        PollUltimate();
        PollSurf();
    }

    // Diagnosability: an OnScreen control that failed to resolve its
    // path logs its own error at setup; double-check once (after Build,
    // which runs from UIFlowManager.Start) so a dead button is visible
    // in logcat instead of silent.
    void CheckControlsResolved()
    {
#if ENABLE_INPUT_SYSTEM
        if (controlsChecked || controlsParent == null) return;
        controlsChecked = true;
        var buttons = controlsParent.GetComponentsInChildren<OnScreenButton>(true);
        foreach (var b in buttons)
            if (b.control == null)
                Debug.LogError("[TouchControls] OnScreenButton '" + b.name +
                    "' did not resolve controlPath '" + b.controlPath + "'.");
        if (stick != null && stick.control == null)
            Debug.LogError("[TouchControls] OnScreenStick did not resolve '<Gamepad>/leftStick'.");
#endif
    }

    void PollSkillButtons()
    {
        for (int i = 0; i < 4; i++)
        {
            if (skillButtons[i] == null) continue;
            float remaining = player.SkillCooldownRemaining(i);
            float total = player.SkillCooldownTotal(i);
            float frac = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
            if (skillCooldown[i] != null) skillCooldown[i].fillAmount = frac;
            if (skillCooldownText[i] != null)
                skillCooldownText[i].text =
                    remaining > 0.05f ? Mathf.CeilToInt(remaining).ToString() : "";
            // Greyed while on cooldown or unaffordable (mana < cost).
            var skill = player.Data != null ? player.Data.GetSkill(i) : null;
            bool affordable = skill == null || player.Mana >= skill.cost;
            bool ready = remaining <= 0f && affordable;
            if (skillImages[i] != null)
                skillImages[i].color = ready ? skillColor : UiKit.DisabledGrey;
        }
    }

    void PollUltimate()
    {
        if (ultButton == null) return;
        float fill = player.Ultimate01;
        bool ready = player.UltimateReady;
        if (ultFill != null) ultFill.fillAmount = fill;
        var img = ultButton.GetComponent<Image>();
        if (img != null)
            img.color = ready ? UiKit.Gold : new Color(0.55f, 0.38f, 0.10f);
        if (ultRing != null)
        {
            // Lit ring when ready (gentle pulse); dim otherwise.
            float pulse = ready ? 0.75f + 0.25f * Mathf.Sin(Time.time * 6f) : 0.30f;
            ultRing.color = new Color(0.91f, 0.71f, 0.30f, pulse);
        }
        if (ultLabel != null) ultLabel.text = ready ? "ULT!" : "ULT";
    }

    // SURF button: Katara only (GAME-NOTES.md), enabled only while
    // sprinting and mana >= 20 (the Godot hud.gd rule). The fighter
    // still gates TrySurf; this is the visual state.
    void PollSurf()
    {
        if (surfButton == null) return;
        bool isKatara = player.Data != null && player.Data.characterId == "katara";
        surfButton.gameObject.SetActive(isKatara);
        if (!isKatara) return;
        bool enabled = player.Kit != null && player.Kit.Sprinting && player.Mana >= 20f;
        var img = surfButton.GetComponent<Image>();
        if (img != null)
            img.color = enabled ? new Color(0.20f, 0.60f, 0.95f) : UiKit.DisabledGrey;
    }

}

// Tiny relay: lives on the joystick base so its pointer events reach the
// TouchControls component (the base also hosts OnScreenStick — both
// handlers receive the same finger's events). Top-level, not nested:
// Unity AddComponent requires a public top-level MonoBehaviour type.
public class TouchControlsForwarder : MonoBehaviour, IPointerDownHandler,
    IDragHandler, IPointerUpHandler
{
    public TouchControls owner;
    public void OnPointerDown(PointerEventData d) { if (owner != null) owner.OnPointerDown(d); }
    public void OnDrag(PointerEventData d) { if (owner != null) owner.OnDrag(d); }
    public void OnPointerUp(PointerEventData d) { if (owner != null) owner.OnPointerUp(d); }
}
