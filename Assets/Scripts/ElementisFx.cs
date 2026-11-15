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

    /// <summary>Surf spout (REA S4): a translucent water column + spiral
    /// spray + foam ring that follows the fighter while surfing (Katara,
    /// reel 4 — "ride a translucent water wave, crouched"). Built
    /// procedurally — there is no authored spout prefab — and it reuses
    /// the water glow material from the WaterBlast prefab so it matches
    /// the existing water look. Loops until the caller destroys it
    /// (FighterController.OnSurfEnded / ResetForRound).</summary>
    public GameObject SpawnSurfSpout(Vector3 position, Transform parent)
    {
        var go = new GameObject("SurfSpout");
        go.transform.position = position;
        if (parent != null) go.transform.SetParent(parent); // keeps world position, follows the fighter
        Material glow = WaterGlowMaterial();

        // Rising column: a straight translucent water jet.
        var column = AddSpoutParticles(go, "Column",
            new Vector3(0f, 0.9f, 0f), glow,
            new Color(0.45f, 0.85f, 1.0f, 0.55f),
            60f, 0.8f, 4f, 0.45f, 0.35f, 0f);
        // Spiral spray: a wider mist cone around the column.
        var spray = AddSpoutParticles(go, "Spray",
            new Vector3(0f, 0.5f, 0f), glow,
            new Color(0.85f, 0.95f, 1.0f, 0.4f),
            40f, 0.6f, 2.5f, 0.35f, 0.55f, 25f);
        // Foam ring at the base: a flat white disc.
        var foam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        foam.name = "FoamRing";
        foam.transform.SetParent(go.transform, false);
        foam.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        foam.transform.localScale = new Vector3(1.2f, 0.03f, 1.2f);
        var col = foam.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        var foamRend = foam.GetComponent<Renderer>();
        if (foamRend != null)
        {
            // Unlit transparent (Sprites/Default handles alpha natively).
            var foamMat = new Material(Shader.Find("Sprites/Default"));
            foamMat.color = new Color(0.9f, 0.97f, 1.0f, 0.55f);
            foamRend.sharedMaterial = foamMat;
        }
        return go;
    }

    // One looping particle jet for the spout. All values are the water
    // look from the existing VFX (translucent + additive glow).
    ParticleSystem AddSpoutParticles(GameObject parent, string name, Vector3 localPos,
        Material material, Color color, float rate, float lifetime, float speed,
        float size, float radius, float coneAngleDeg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = radius;
        shape.angle = coneAngleDeg;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        if (rend != null && material != null) rend.sharedMaterial = material;
        ps.Play();
        return ps;
    }

    // The water glow material from the WaterBlast prefab (the same
    // additive glow the other water VFX use). Falls back to a plain
    // unlit transparent material if the prefab is not wired.
    Material WaterGlowMaterial()
    {
        if (waterBlast != null)
        {
            var rend = waterBlast.GetComponentInChildren<ParticleSystemRenderer>();
            if (rend != null && rend.sharedMaterial != null) return rend.sharedMaterial;
        }
        var fallback = new Material(Shader.Find("Sprites/Default"));
        fallback.color = new Color(0.45f, 0.85f, 1.0f, 0.55f);
        return fallback;
    }
}
