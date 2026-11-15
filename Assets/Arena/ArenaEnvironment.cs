using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies the arena's scene-level atmosphere when the Arena prefab is
/// instantiated: linear dusk fog + warm trilight ambient. The sun itself
/// lives in the prefab (SunLight) and is assigned here as the scene sun.
/// Kept deliberately tiny so it cannot fail the build: every call is a
/// plain RenderSettings assignment, no asset lookups.
/// </summary>
public class ArenaEnvironment : MonoBehaviour
{
    public Light sun;

    [SerializeField] private Color fogColor = new Color(0.42f, 0.30f, 0.36f);
    [SerializeField] private float fogStart = 55f;
    [SerializeField] private float fogEnd = 200f;

    private void Awake()
    {
        Apply();
        AddFloorMarkings();
    }

    public void Apply()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.fogColor = fogColor;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.32f, 0.38f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.24f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.12f, 0.10f);
        if (sun != null)
        {
            RenderSettings.sun = sun;
        }
    }

    // Two-tone floor (REA S5 — Flowbound's readable floor): the arena
    // already has a stone floor + a dirt centre circle; add a thin gold
    // ring at r = 12 m (the range reference) and a centre emblem disc.
    // Built procedurally at runtime (no prefab YAML surgery) and placed
    // 2 cm above the floor so they never z-fight it.
    void AddFloorMarkings()
    {
        Material gold = GoldMaterial();
        if (gold == null) return;

        var ringGo = new GameObject("CentreRing");
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        var ringFilter = ringGo.AddComponent<MeshFilter>();
        ringFilter.sharedMesh = BuildAnnulusMesh(11.8f, 12.2f, 64);
        var ringRend = ringGo.AddComponent<MeshRenderer>();
        ringRend.sharedMaterial = gold;

        // Centre emblem: a gold disc with a darker inner disc.
        var discGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        discGo.name = "CentreEmblem";
        discGo.transform.SetParent(transform, false);
        discGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        discGo.transform.localScale = new Vector3(4f, 0.02f, 4f); // r = 2 m (cylinder r = 0.5 * scale)
        var discCol = discGo.GetComponent<Collider>();
        if (discCol != null) Object.Destroy(discCol);
        var discRend = discGo.GetComponent<Renderer>();
        if (discRend != null) discRend.sharedMaterial = gold;
        var innerGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        innerGo.name = "CentreEmblemInner";
        innerGo.transform.SetParent(transform, false);
        innerGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        innerGo.transform.localScale = new Vector3(2f, 0.02f, 2f); // r = 1 m
        var innerCol = innerGo.GetComponent<Collider>();
        if (innerCol != null) Object.Destroy(innerCol);
        var innerRend = innerGo.GetComponent<Renderer>();
        if (innerRend != null)
        {
            var slate = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            slate.color = new Color(0.16f, 0.18f, 0.22f);
            slate.SetFloat("_Metallic", 0.2f);
            slate.SetFloat("_Smoothness", 0.5f);
            innerRend.sharedMaterial = slate;
        }
    }

    Material GoldMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.color = new Color(0.85f, 0.65f, 0.25f); // gold
        mat.SetFloat("_Metallic", 0.8f);
        mat.SetFloat("_Smoothness", 0.6f);
        return mat;
    }

    // Flat annulus (ring) mesh, y = 0. Triangles are emitted in BOTH
    // windings so the ring is visible from above regardless of the
    // coordinate handedness — a one-sided winding mistake would make it
    // invisible, and this costs nothing (64 segments).
    Mesh BuildAnnulusMesh(float innerR, float outerR, int segments)
    {
        var mesh = new Mesh();
        mesh.name = "CentreRingMesh";
        var verts = new Vector3[(segments + 1) * 2];
        for (int i = 0; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * innerR, 0f, s * innerR);
            verts[i * 2 + 1] = new Vector3(c * outerR, 0f, s * outerR);
        }
        var tris = new int[segments * 12];
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2;
            int t = i * 12;
            tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
            tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            tris[t + 6] = b; tris[t + 7] = b + 2; tris[t + 8] = b + 1;
            tris[t + 9] = b + 1; tris[t + 10] = b + 2; tris[t + 11] = b + 3;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
}
