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

    Camera cam;
    Vector3 velocity; // SmoothDamp state
    float trauma;
    float fov;

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
        if (player == null || opponent == null) return;
        transform.position = Vector3.SmoothDamp(transform.position,
            DesiredPosition(), ref velocity, positionSmoothTime);
        Quaternion want = Quaternion.LookRotation(LookTarget() - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, want,
            1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
        if (cam != null)
        {
            float targetFov = DesiredFov();
            fov = Mathf.Lerp(fov, targetFov, 1f - Mathf.Exp(-5f * Time.deltaTime));
            cam.fieldOfView = fov;
        }
        ApplyShake();
    }

    Vector3 DesiredPosition()
    {
        Vector3 toPlayer = player.position - opponent.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.back;
        toPlayer.Normalize();
        float sep = Vector3.Distance(player.position, opponent.position);
        float dist = Mathf.Clamp(6f + sep * 0.25f, minDistance, maxDistance);
        return player.position + toPlayer * dist + Vector3.up * height;
    }

    Vector3 LookTarget()
    {
        Vector3 playerChest = player.position + Vector3.up * 1.6f;
        Vector3 oppChest = opponent.position + Vector3.up * 1.6f;
        return Vector3.Lerp(playerChest, oppChest, lookBias);
    }

    float DesiredFov()
    {
        float sep = Vector3.Distance(player.position, opponent.position);
        return baseFov + Mathf.Clamp(sep * 0.3f, 0f, fovGrowth);
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
