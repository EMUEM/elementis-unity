// PlayerInputBridge.cs — wires ElementisInput events to the player fighter.
//
// Added at runtime by FightBootstrap to the player fighter (Katara).
// Subscribes to ElementisInput's plain C# events and forwards them to the
// fighter's public Try* surface. Move input is camera-relative: the
// joystick vector is converted with the ReelCamera yaw, so "up" always
// means "away from the camera" no matter where the fighters stand.
//
// If ElementisInput is missing (Input System package removed), this
// component does nothing — the touch UI then calls FighterController.Try*
// directly from button events (see GAME-NOTES.md).
using UnityEngine;

public class PlayerInputBridge : MonoBehaviour
{
    FighterController fighter;
    ElementisInput input;
    bool subscribed;

    void Awake()
    {
        fighter = GetComponent<FighterController>();
    }

    void OnEnable()
    {
        TrySubscribe();
    }

    void Update()
    {
        // Lazy subscribe: ElementisInput.Awake may run after this bridge is
        // created (FightBootstrap instantiates fighters during its own Awake).
        if (!subscribed) TrySubscribe();
    }

    void TrySubscribe()
    {
        input = ElementisInput.Instance;
        if (input == null) return;
        input.MoveChanged += OnMove;
        input.PunchPressed += OnPunch;
        input.KickPressed += OnKick;
        input.SkillPressed += OnSkill;
        input.BlockPressed += OnBlockPressed;
        input.BlockReleased += OnBlockReleased;
        input.DashPressed += OnDash;
        input.JumpPressed += OnJump;
        input.RegenPressed += OnRegenPressed;
        input.RegenReleased += OnRegenReleased;
        input.UltimatePressed += OnUltimate;
        input.SurfPressed += OnSurf;
        subscribed = true;
    }

    void OnDisable()
    {
        if (input == null) return;
        input.MoveChanged -= OnMove;
        input.PunchPressed -= OnPunch;
        input.KickPressed -= OnKick;
        input.SkillPressed -= OnSkill;
        input.BlockPressed -= OnBlockPressed;
        input.BlockReleased -= OnBlockReleased;
        input.DashPressed -= OnDash;
        input.JumpPressed -= OnJump;
        input.RegenPressed -= OnRegenPressed;
        input.RegenReleased -= OnRegenReleased;
        input.UltimatePressed -= OnUltimate;
        input.SurfPressed -= OnSurf;
    }

    void OnMove(Vector2 move)
    {
        if (fighter == null) return;
        float yaw = ReelCamera.Instance != null ? ReelCamera.Instance.Yaw : 0f;
        fighter.SetMoveInput(move, yaw);
    }

    void OnPunch() { if (fighter != null) fighter.TryPunch(); }
    void OnKick() { if (fighter != null) fighter.TryKick(); }
    void OnSkill(int index) { if (fighter != null) fighter.TrySkill(index); }
    void OnBlockPressed() { if (fighter != null) fighter.SetBlocking(true); }
    void OnBlockReleased() { if (fighter != null) fighter.SetBlocking(false); }
    void OnDash() { if (fighter != null) fighter.TryDash(); }
    void OnJump() { if (fighter != null) fighter.TryJump(); }
    void OnRegenPressed() { if (fighter != null) fighter.SetRegenHeld(true); }
    void OnRegenReleased() { if (fighter != null) fighter.SetRegenHeld(false); }
    void OnUltimate() { if (fighter != null) fighter.TryUltimate(); }
    void OnSurf() { if (fighter != null) fighter.TrySurf(); }

    // Direct-call surface for the touch UI (UI Button onClick / onPointer
    // events can call these without going through the Input System at all —
    // the multi-touch-safe fallback path).
    public void UiPunch() { OnPunch(); }
    public void UiKick() { OnKick(); }
    public void UiSkill(int index) { OnSkill(index); }
    public void UiDash() { OnDash(); }
    public void UiJump() { OnJump(); }
    public void UiUltimate() { OnUltimate(); }
    public void UiSurf() { OnSurf(); } // Katara-only; UI shows the SURF button only for Katara
    public void UiBlock(bool held)
    {
        if (fighter == null) return;
        fighter.SetBlocking(held);
    }
    public void UiRegen(bool held)
    {
        if (fighter == null) return;
        fighter.SetRegenHeld(held);
    }
    public void UiMove(Vector2 move) { OnMove(move); }
}
