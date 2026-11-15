// ElementisInput.cs — the single input surface for the touch UI (Phase 2).
//
// Wraps the Unity Input System action asset
// (Assets/Scripts/ElementisInputActions.inputactions, map "Fighting") and
// exposes one named action per control:
//
//   Move, Punch, Kick, Skill1, Skill2, Skill3, Skill4,
//   Block, Dash, Jump, Regen, Ultimate, Surf (Katara-only)
//
// The touch UI binds to these actions with the Input System's own
// OnScreenButton / OnScreenStick components (multi-touch safe: the
// joystick finger and the button fingers drive different actions, so
// moving + casting at the same time works — the named Godot defect).
// The UI agent may ALSO call FighterController.Try* directly from a
// button's onClick — both paths call the same methods.
//
// Keyboard/gamepad bindings live in the .inputactions asset. If the asset
// reference is missing (import failed), a programmatic asset with the
// same action names and keyboard bindings is built instead, so the game
// never loses input over an asset problem.
//
// ALL UnityEngine.InputSystem API usage in the combat code lives in THIS
// file only. PlayerInputBridge and the fighters talk to plain C# events
// (MoveChanged, PunchPressed, ...), so no other combat file references the
// Input System package. If the package ever fails to resolve, delete this
// file (and its component in Main.unity) and the touch UI calls
// FighterController.Try* directly — the fight still runs.
//
// REQUIRED PROJECT SETTING (coordinator step, NOT done here —
// ProjectSettings are out of scope for this phase):
//   Player Settings -> Active Input Handler -> Both (or Input System).
// With "Input Manager" selected, Input System actions receive no input.
// The touch UI's direct Try* calls work regardless of that setting.
using System;
using UnityEngine;
#if UNITY_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ElementisInput : MonoBehaviour
{
    public static ElementisInput Instance { get; private set; }

    [Header("Action asset (wired in Main.unity)")]
    [Tooltip("ElementisInputActions.inputactions. Null = programmatic fallback asset is built.")]
    public InputActionAsset actionsAsset;

    // --- Events the player bridge subscribes to ---
    public event Action<Vector2> MoveChanged;
    public event Action PunchPressed;
    public event Action KickPressed;
    public event Action<int> SkillPressed;   // 0..3
    public event Action BlockPressed;
    public event Action BlockReleased;
    public event Action DashPressed;
    public event Action JumpPressed;
    public event Action RegenPressed;
    public event Action RegenReleased;
    public event Action UltimatePressed;
    public event Action SurfPressed; // Katara-only traversal (HUD SURF button / C key)

    public Vector2 MoveValue { get; private set; }
    public bool IsBlockingHeld { get; private set; }
    public bool IsRegenHeld { get; private set; }

    // Public action handles (for the UI agent: OnScreenButton.controlPath
    // targets these by name through the asset; direct access is here too).
    public InputAction MoveAction { get; private set; }
    public InputAction PunchAction { get; private set; }
    public InputAction KickAction { get; private set; }
    public InputAction Skill1Action { get; private set; }
    public InputAction Skill2Action { get; private set; }
    public InputAction Skill3Action { get; private set; }
    public InputAction Skill4Action { get; private set; }
    public InputAction BlockAction { get; private set; }
    public InputAction DashAction { get; private set; }
    public InputAction JumpAction { get; private set; }
    public InputAction RegenAction { get; private set; }
    public InputAction UltimateAction { get; private set; }
    public InputAction SurfAction { get; private set; }

    void Awake()
    {
        Instance = this;
#if UNITY_INPUT_SYSTEM
        if (actionsAsset == null)
        {
            Debug.LogWarning("[ElementisInput] action asset not wired — building the fallback asset.");
            actionsAsset = BuildFallbackAsset();
        }
        var map = actionsAsset.FindActionMap("Fighting", true);
        MoveAction = map.FindAction("Move", true);
        PunchAction = map.FindAction("Punch", true);
        KickAction = map.FindAction("Kick", true);
        Skill1Action = map.FindAction("Skill1", true);
        Skill2Action = map.FindAction("Skill2", true);
        Skill3Action = map.FindAction("Skill3", true);
        Skill4Action = map.FindAction("Skill4", true);
        BlockAction = map.FindAction("Block", true);
        DashAction = map.FindAction("Dash", true);
        JumpAction = map.FindAction("Jump", true);
        RegenAction = map.FindAction("Regen", true);
        UltimateAction = map.FindAction("Ultimate", true);
        SurfAction = map.FindAction("Surf", true);
        Wire();
        map.Enable();
#else
        Debug.LogError("[ElementisInput] Input System package (com.unity.inputsystem) is not " +
            "present — keyboard/gamepad input is unavailable. The touch UI must call " +
            "FighterController.Try* directly (see GAME-NOTES.md).");
#endif
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

#if UNITY_INPUT_SYSTEM
    void Wire()
    {
        MoveAction.performed += ctx => { MoveValue = ctx.ReadValue<Vector2>(); if (MoveChanged != null) MoveChanged(MoveValue); };
        MoveAction.canceled += ctx => { MoveValue = Vector2.zero; if (MoveChanged != null) MoveChanged(MoveValue); };
        PunchAction.performed += ctx => { if (PunchPressed != null) PunchPressed(); };
        KickAction.performed += ctx => { if (KickPressed != null) KickPressed(); };
        Skill1Action.performed += ctx => { if (SkillPressed != null) SkillPressed(0); };
        Skill2Action.performed += ctx => { if (SkillPressed != null) SkillPressed(1); };
        Skill3Action.performed += ctx => { if (SkillPressed != null) SkillPressed(2); };
        Skill4Action.performed += ctx => { if (SkillPressed != null) SkillPressed(3); };
        BlockAction.performed += ctx => { IsBlockingHeld = true; if (BlockPressed != null) BlockPressed(); };
        BlockAction.canceled += ctx => { IsBlockingHeld = false; if (BlockReleased != null) BlockReleased(); };
        DashAction.performed += ctx => { if (DashPressed != null) DashPressed(); };
        JumpAction.performed += ctx => { if (JumpPressed != null) JumpPressed(); };
        RegenAction.performed += ctx => { IsRegenHeld = true; if (RegenPressed != null) RegenPressed(); };
        RegenAction.canceled += ctx => { IsRegenHeld = false; if (RegenReleased != null) RegenReleased(); };
        UltimateAction.performed += ctx => { if (UltimatePressed != null) UltimatePressed(); };
        SurfAction.performed += ctx => { if (SurfPressed != null) SurfPressed(); };
    }

    /// <summary>Programmatic fallback: the same 13 actions, keyboard bindings
    /// (WASD/arrows move as a 2DVector composite, like the .inputactions asset).</summary>
    static InputActionAsset BuildFallbackAsset()
    {
        var asset = ScriptableObject.CreateInstance<InputActionAsset>();
        var map = asset.AddActionMap("Fighting");
        var move = map.AddAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        move.AddBinding("<Gamepad>/leftStick");
        map.AddAction("Punch", InputActionType.Button, "<Keyboard>/j");
        map.AddAction("Kick", InputActionType.Button, "<Keyboard>/k");
        map.AddAction("Skill1", InputActionType.Button, "<Keyboard>/1");
        map.AddAction("Skill2", InputActionType.Button, "<Keyboard>/2");
        map.AddAction("Skill3", InputActionType.Button, "<Keyboard>/3");
        map.AddAction("Skill4", InputActionType.Button, "<Keyboard>/4");
        map.AddAction("Block", InputActionType.Button, "<Keyboard>/l");
        map.AddAction("Dash", InputActionType.Button, "<Keyboard>/leftShift");
        map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
        map.AddAction("Regen", InputActionType.Button, "<Keyboard>/r");
        map.AddAction("Ultimate", InputActionType.Button, "<Keyboard>/u");
        map.AddAction("Surf", InputActionType.Button, "<Keyboard>/c");
        return asset;
    }
#endif
}
