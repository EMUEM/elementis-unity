// UiButtonPress.cs — direct-call touch button (no Input System).
//
// Used by TouchControls ONLY when the Input System package is absent
// (the GAME-NOTES.md fallback world: ElementisInput.cs deleted, no
// OnScreenButton available). Pointer-down fires the action immediately
// (fighting-game responsiveness: act on press, not on release); hold
// buttons (Block / Regen) fire press on down and release on up/cancel.
//
// When the Input System IS present, TouchControls uses OnScreenButton /
// OnScreenStick instead and never adds this component — the two paths
// are never both live on one button (that would double-fire).
using UnityEngine;
using UnityEngine.EventSystems;

public class UiButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerExitHandler
{
    public enum PressKind
    {
        Punch, Kick, Skill1, Skill2, Skill3, Skill4,
        Block, Dash, Jump, Regen, Ultimate, Surf
    }

    public PressKind kind;
    public PlayerInputBridge bridge;
    bool held;

    public void OnPointerDown(PointerEventData data)
    {
        if (bridge == null) return;
        held = true;
        switch (kind)
        {
            case PressKind.Punch: bridge.UiPunch(); break;
            case PressKind.Kick: bridge.UiKick(); break;
            case PressKind.Skill1: bridge.UiSkill(0); break;
            case PressKind.Skill2: bridge.UiSkill(1); break;
            case PressKind.Skill3: bridge.UiSkill(2); break;
            case PressKind.Skill4: bridge.UiSkill(3); break;
            case PressKind.Block: bridge.UiBlock(true); break;
            case PressKind.Dash: bridge.UiDash(); break;
            case PressKind.Jump: bridge.UiJump(); break;
            case PressKind.Regen: bridge.UiRegen(true); break;
            case PressKind.Ultimate: bridge.UiUltimate(); break;
            case PressKind.Surf: bridge.UiSurf(); break;
        }
    }

    public void OnPointerUp(PointerEventData data) { Release(); }

    public void OnPointerExit(PointerEventData data) { Release(); }

    void Release()
    {
        if (!held || bridge == null) return;
        held = false;
        switch (kind)
        {
            case PressKind.Block: bridge.UiBlock(false); break;
            case PressKind.Regen: bridge.UiRegen(false); break;
        }
    }

    void OnDisable() { Release(); }
}
