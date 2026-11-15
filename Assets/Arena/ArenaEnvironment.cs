using UnityEngine;

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
}
