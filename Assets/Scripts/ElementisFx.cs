// ElementisFx.cs — VFX library for the combat system (Phase 2).
//
// One GameObject in the fight scene ("FxLibrary") holds this component with
// the 15 VFX prefabs from Assets/VFX/Prefabs/ dragged into the fields
// (wired in Main.unity). Combat code never references prefab assets
// directly — it asks ElementisFx.Instance.Spawn(name, position, rotation).
//
// Lifetime rules (VFX-NOTES.md): one-shot prefabs self-destroy (their
// longest ParticleSystem has stopAction: Destroy); looping prefabs
// (WaterBlast, Fireball, TidalWave, SprintTrail, RegenAura) never
// self-destroy — the caller owns their lifetime and must Destroy the
// returned GameObject (projectiles do it on hit/timeout, the fighter
// does it for trail/aura on cancel).
// Quality tiers: scale any effect via
//   GetComponentInChildren<ParticleSystem>().emission.rateOverTimeMultiplier
// (Low 0.5 / Med 0.75 / High 1.0). Never scale the ultimates below 0.75.
using System.Collections.Generic;
using UnityEngine;

public class ElementisFx : MonoBehaviour
{
    public static ElementisFx Instance { get; private set; }

    [Header("Katara — skills")]
    public GameObject waterBlast;
    public GameObject waterWhip;
    public GameObject iceSpikes;
    public GameObject tidalWave;
    [Header("Azula — skills")]
    public GameObject fireball;
    public GameObject flameSweep;
    public GameObject lightningStrike;
    public GameObject fireSpin;
    [Header("Ultimates")]
    public GameObject ultimateKatara;
    public GameObject ultimateAzula;
    [Header("Feedback / movement")]
    public GameObject hitSparks;
    public GameObject dustBurst;
    public GameObject sprintTrail;
    public GameObject blockShimmer;
    public GameObject regenAura;

    Dictionary<string, GameObject> byName;

    void Awake()
    {
        Instance = this;
        byName = new Dictionary<string, GameObject>
        {
            { "WaterBlast", waterBlast },
            { "WaterWhip", waterWhip },
            { "IceSpikes", iceSpikes },
            { "TidalWave", tidalWave },
            { "Fireball", fireball },
            { "FlameSweep", flameSweep },
            { "LightningStrike", lightningStrike },
            { "FireSpin", fireSpin },
            { "UltimateKatara", ultimateKatara },
            { "UltimateAzula", ultimateAzula },
            { "HitSparks", hitSparks },
            { "DustBurst", dustBurst },
            { "SprintTrail", sprintTrail },
            { "BlockShimmer", blockShimmer },
            { "RegenAura", regenAura },
        };
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Spawn a VFX prefab by name. Returns null (and logs once per
    /// name) when the prefab is not wired — the fight keeps running.</summary>
    public GameObject Spawn(string prefabName, Vector3 position, Quaternion rotation,
        Transform parent = null)
    {
        GameObject prefab;
        if (byName == null || !byName.TryGetValue(prefabName, out prefab) || prefab == null)
        {
            Debug.LogWarning("[ElementisFx] prefab not wired: " + prefabName);
            return null;
        }
        return Instantiate(prefab, position, rotation, parent);
    }

    public bool Has(string prefabName)
    {
        GameObject prefab;
        return byName != null && byName.TryGetValue(prefabName, out prefab) && prefab != null;
    }
}
