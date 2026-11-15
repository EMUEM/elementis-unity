// ReelCamera.cs — Elementis fight camera (reel camera).
//
// Tight third-person behind the PLAYER fighter, looking toward the
// opponent — the Infinite Souls / Mobile Legends framing from the reels.
// Distance and FOV grow with the fighter separation (Fortnite-style
// distance-aware camera, fighter.gd opp_dist comment): close brawl =
// tight + low FOV, far apart = pulled back + wider FOV.
//
// Mounted on the Main Camera in Main.unity (starting pose = the arena's
// FightCameraProxy: (0, 3, -13) looking at SpawnB). FightBootstrap assigns
// Player / Opponent at fight start. Shake(amount) is the ultimate/KO punch.
//
// FEEL (REA S1, 2026-11-15): the camera moves WITH the body — it rolls
// on wall-runs (25 deg), rolls a full -360 deg with the double-jump
// backflip, rolls 12 deg on a dodge/slide, kicks the FOV +6 deg on
// sprint (+8 on surf), tilts up 5 deg on wall-runs and rising jumps,
// and dips -0.3 m on a hard landing. The roll magnitudes are the
// readable mobile-safe version of Tachyon Flow's near-90 deg swings;
// tune on device. The roll SIGN for wall-runs (roll toward the wall)
// is set by the wall normal — flip the sign if it reads backwards.
//
// VS MODE (2026-11-15 fix): the VS screen shows the face-off — camera
// behind fighter A at (0, 2.0, -7.5), FOV 55, both fighters large in
// frame with the arena behind them (approved fight-scene image).
// UIFlowManager enables it on ShowVersus and disables it on BeginFight.
using UnityEngine;

public class ReelCamera : MonoBehaviour
{
    public static ReelCamera Instance { get; private set; }

    [Header("Targets (assigned by FightBootstrap)")]
    public Transform player;
    public Transform opponent;

    [Header("Framing")]
    [Tooltip("Camera height above the player.")]
    public float height = 3f;
    [Tooltip("Closest follow distance (fighters in a clinch).")]
    public float minDistance = 7f;
    [Tooltip("Farthest follow distance (fighters across the arena).")]
    public float maxDistance = 13f;
    [Tooltip("Base vertical FOV; grows with separation.")]
    public float baseFov = 60f;
    [Tooltip("Extra FOV at max separation.")]
    public float fovGrowth = 10f;
    [Tooltip("How much of the look target leans toward the opponent (0 = player, 1 = opponent).")]
    [Range(0f, 1f)]
    public float lookBias = 0.35f;

    [Header("Feel")]
    public float positionSmoothTime = 0.12f;
    public float rotationSpeed = 10f;

    // VS face-off pose (approved fight-scene image composition).
    public static readonly Vector3 VsPosition = new Vector3(0f, 2.0f, -7.5f);
    public static readonly Vector3 VsLookTarget = new Vector3(0f, 1.4f, 0f);
    public const float VsFov = 55f;

    // Feel magnitudes (REA S1 targets — tune on device).
    const float WallRunRollDeg = 25f;
    const float DodgeRollDeg = 12f;
    const float MaxRollDeg = 30f; // cap, except the backflip
    const float SprintFovKick = 6f;
    const float SurfFovKick = 8f;
    const float TiltUpDeg = 5f;
    const float LandingDipM = 0.3f;
    const float LandingDipFov = 2f;
    const float FlipDuration = 0.45f; // MovementKit flip time

    Camera cam;
    Vector3 velocity; // SmoothDamp state
    float trauma;
    float fov;
    bool vsMode;
    float tiltUp; // 0..1, eased toward wall-run / rising
    FighterController cachedPlayerFighter;
    Transform cachedPlayerTransform;

    /// <summary>Camera yaw in degrees, for camera-relative move input
    /// (PlayerInputBridge). 0 = looking toward +Z... see SetMoveInput.</summary>
    public float Yaw
    {
        get { return transform.eulerAngles.y; }
    }

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        fov = baseFov;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Add screen shake (0..1). Ultimates call Shake(0.7).</summary>
    public void Shake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    /// <summary>VS face-off framing on/off. On: the camera holds the VS
    /// pose (behind fighter A, both fighters + arena in frame). Off: the
    /// normal fight follow resumes (call Snap() after turning it off).</summary>
    public void SetVsMode(bool on)
    {
        vsMode = on;
        if (on) SnapVs();
    }

    /// <summary>Snap to the VS face-off pose immediately.</summary>
    public void SnapVs()
    {
        transform.position = VsPosition;
        transform.LookAt(VsLookTarget);
        if (cam != null) cam.fieldOfView = VsFov;
        fov = VsFov;
    }

    /// <summary>Snap to the ideal framing immediately (round start).</summary>
    public void Snap()
    {
        if (player == null || opponent == null) return;
        transform.position = DesiredPosition();
        transform.LookAt(LookTarget());
        if (cam != null) cam.fieldOfView = DesiredFov();
    }

    void LateUpdate()
    {
        // Hit-stop is driven by unscaled time and must tick even while
        // timeScale is 0 (and even before the fighters exist).
        HitStop.Tick();

        if (vsMode)
        {
            transform.position = Vector3.SmoothDamp(transform.position,
                VsPosition, ref velocity, positionSmoothTime);
            Quaternion wantVs = Quaternion.LookRotation(VsLookTarget - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantVs,
                1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
            if (cam != null)
            {
                fov = Mathf.Lerp(fov, VsFov, 1f - Mathf.Exp(-5f * Time.deltaTime));
                cam.fieldOfView = fov;
            }
            ApplyShake();
            return;
        }

        if (player == null || opponent == null) return;
        transform.position = Vector3.SmoothDamp(transform.position,
            DesiredPosition(), ref velocity, positionSmoothTime);
        Quaternion want = Quaternion.LookRotation(LookTarget() - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, want,
            1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
        // Roll with the body (REA S1): wall-run / backflip / dodge.
        float roll = CurrentRollDeg();
        if (Mathf.Abs(roll) > 0.001f)
            transform.rotation = transform.rotation * Quaternion.AngleAxis(roll, Vector3.forward);
        if (cam != null)
        {
            float targetFov = DesiredFov();
            fov = Mathf.Lerp(fov, targetFov, 1f - Mathf.Exp(-5f * Time.deltaTime));
            cam.fieldOfView = fov - LandingDipFov * LandingDipFactor();
        }
        ApplyShake();
    }

    // Roll (deg) around the camera forward axis, from the player's kit.
    // Backflip: a full -360 deg over the 0.45 s flip (ease-in-out).
    // Wall-run: 25 deg toward the wall. Dodge/slide: 12 deg toward the
    // dodge direction. Capped at 30 deg except the flip.
    float CurrentRollDeg()
    {
        var kit = PlayerKit();
        if (kit == null) return 0f;
        if (kit.FlipTimeRemaining > 0f)
        {
            float p = Mathf.Clamp01(1f - kit.FlipTimeRemaining / FlipDuration);
            float eased = p * p * (3f - 2f * p); // smoothstep
            return -360f * eased;
        }
        if (kit.WallRunning)
        {
            float side = Vector3.Dot(kit.WallNormal, transform.right);
            return WallRunRollDeg * Mathf.Sign(side == 0f ? 1f : side);
        }
        if (kit.DodgeSliding)
        {
            float side = Vector3.Dot(kit.DodgeDirection, transform.right);
            return DodgeRollDeg * Mathf.Sign(side == 0f ? 1f : side);
        }
        return 0f;
    }

    // 0..1 while the fighter is in the hard-landing crouch (0.4 s).
    float LandingDipFactor()
    {
        var kit = PlayerKit();
        if (kit == null) return 0f;
        return Mathf.Clamp01(kit.LandCrouchRemaining / 0.4f);
    }

    MovementKit PlayerKit()
    {
        if (player == null) return null;
        if (cachedPlayerTransform != player)
        {
            cachedPlayerTransform = player;
            cachedPlayerFighter = player.GetComponent<FighterController>();
        }
        return cachedPlayerFighter != null ? cachedPlayerFighter.Kit : null;
    }

    Vector3 DesiredPosition()
    {
        Vector3 toPlayer = player.position - opponent.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.back;
        toPlayer.Normalize();
        float sep = Vector3.Distance(player.position, opponent.position);
        float dist = Mathf.Clamp(6f + sep * 0.25f, minDistance, maxDistance);
        Vector3 pos = player.position + toPlayer * dist + Vector3.up * height;
        // Landing dip: the camera drops with the crouch (REA S1).
        pos.y -= LandingDipM * LandingDipFactor();
        return pos;
    }

    Vector3 LookTarget()
    {
        Vector3 playerChest = player.position + Vector3.up * 1.6f;
        Vector3 oppChest = opponent.position + Vector3.up * 1.6f;
        Vector3 target = Vector3.Lerp(playerChest, oppChest, lookBias);
        // Tilt-up (REA S1): raise the look target ~5 deg while
        // wall-running or rising through a jump.
        var kit = PlayerKit();
        bool wantTilt = false;
        if (kit != null)
            wantTilt = kit.WallRunning || (!kit.IsGrounded && kit.Velocity.y > 2f);
        tiltUp = Mathf.MoveTowards(tiltUp, wantTilt ? 1f : 0f, Time.deltaTime * 5f);
        if (tiltUp > 0.001f)
        {
            float dist = Vector3.Distance(transform.position, target);
            target.y += Mathf.Tan(TiltUpDeg * Mathf.Deg2Rad) * dist * tiltUp;
        }
        return target;
    }

    float DesiredFov()
    {
        float sep = Vector3.Distance(player.position, opponent.position);
        float f = baseFov + Mathf.Clamp(sep * 0.3f, 0f, fovGrowth);
        // Sprint FOV kick (REA S1): +6 deg sprinting, +8 deg surfing.
        var kit = PlayerKit();
        if (kit != null)
        {
            if (kit.Surfing) f += SurfFovKick;
            else if (kit.Sprinting) f += SprintFovKick;
        }
        return f;
    }

    void ApplyShake()
    {
        if (trauma <= 0.001f) return;
        trauma = Mathf.Max(0f, trauma - Time.deltaTime * 1.4f);
        float s = trauma * trauma * 0.35f;
        float t = Time.time * 47f;
        transform.position += new Vector3(
            (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * s,
            (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * s,
            0f);
    }
}
