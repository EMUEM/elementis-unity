// Elementis — player-camera screenshot (Editor-only utility).
//
// Renders a REAL frame from the actual Unity scene, from the exact
// player-camera pose used in a fight (FightCameraProxy pose), and saves
// it as a PNG under ~/workspace/tools/unity/ci/.
//
// Run headless with graphics (Xvfb + Mesa llvmpipe), NOT with -nographics:
//   xvfb-run -a -s "-screen 0 1920x1080x24" env LD_PRELOAD=<noop-chown.so> \
//     <Editor>/Unity -batchmode -projectPath <project> \
//     -executeMethod ScreenshotCapture.CapturePlayerCamera -quit \
//     -logFile <log>
//
// The scene file is NOT saved: arena + fighters are instantiated only in
// the in-memory scene. CharacterSetup.SetupCharacters() DOES rebuild the
// character avatars/controllers/prefabs on disk (same step CI runs).
// Fighters are posed with AnimationClip.SampleAnimation so the shot shows
// the real Idle pose, not the FBX bind pose (the Animator does not run
// in Edit mode).

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ScreenshotCapture
{
    const string OutPath = "/home/hatch/workspace/tools/unity/ci/elementis-v1-player-camera.png";
    const int Width = 1920;
    const int Height = 1080;

    static readonly string[] AnimFiles =
    {
        "UAL1_Standard.fbx", "UAL2_Standard.fbx",
        "Elementis_Authored.fbx", "Kick_Roundhouse.fbx"
    };

    [MenuItem("Elementis/Render/Player Camera Screenshot")]
    public static void CapturePlayerCamera()
    {
        Debug.Log("[Screenshot] START");
        Debug.Log($"[Screenshot] graphics device: {SystemInfo.graphicsDeviceName} " +
                  $"type={SystemInfo.graphicsDeviceType} renderer={SystemInfo.graphicsDeviceVersion}");

        // 1. Open the fight scene (in memory only; never saved).
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        // 2. Build character avatars / controllers / prefabs (same as CI), then verify.
        CharacterSetup.SetupCharacters();
        CharacterSetup.VerifyCharacters();

        // 3. Arena at the origin + its dusk atmosphere (Awake does not run in Edit mode).
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arena/Arena.prefab");
        if (arenaPrefab == null)
        {
            Debug.LogError("[Screenshot] Arena.prefab NOT FOUND");
            return;
        }
        var arena = (GameObject)PrefabUtility.InstantiatePrefab(arenaPrefab);
        arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var env = arena.GetComponentInChildren<ArenaEnvironment>();
        if (env != null) env.Apply();
        else Debug.LogWarning("[Screenshot] ArenaEnvironment not found on arena prefab");

        // 4. Fighters on their spawns, facing each other (FightBootstrap spawns).
        var katara = SpawnFighter("Katara", new Vector3(0f, 0f, -6f), 0f);
        var azula = SpawnFighter("Azula", new Vector3(0f, 0f, 6f), 180f);
        if (katara == null || azula == null) return;

        // 5. Pose both fighters mid-Idle (SampleAnimation works in Edit mode;
        //    the Animator itself never runs outside Play mode).
        PoseIdle(katara);
        PoseIdle(azula);

        // 6. Player camera: exact FightCameraProxy pose, UI hidden for a clean frame.
        var ui = GameObject.Find("UI");
        if (ui != null) ui.SetActive(false);
        var camObj = GameObject.Find("Main Camera");
        if (camObj == null)
        {
            Debug.LogError("[Screenshot] Main Camera not found in scene");
            return;
        }
        var cam = camObj.GetComponent<Camera>();
        cam.transform.SetPositionAndRotation(
            new Vector3(0f, 3f, -13f), Quaternion.Euler(-4.21f, 180f, 0f));
        cam.fieldOfView = 60f;
        cam.aspect = (float)Width / Height;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 200f;

        // 7. Render to a texture and save a PNG.
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = prevTarget;
        var png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllBytes(OutPath, png);
        Debug.Log($"[Screenshot] SAVED {OutPath} {png.Length} bytes ({Width}x{Height})");
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Debug.Log("[Screenshot] DONE");
    }

    static GameObject SpawnFighter(string name, Vector3 pos, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/{name}.prefab");
        if (prefab == null)
        {
            Debug.LogError($"[Screenshot] {name}.prefab NOT FOUND");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.name = name;
        return go;
    }

    static void PoseIdle(GameObject fighter)
    {
        AnimationClip idle = null;
        foreach (var f in AnimFiles)
        {
            var path = "Assets/Characters/Animations/" + f;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is AnimationClip clip && clip.name == "Idle_Loop")
                {
                    idle = clip;
                    break;
                }
            }
            if (idle != null) break;
        }
        if (idle == null)
        {
            Debug.LogWarning($"[Screenshot] Idle_Loop clip not found — {fighter.name} stays in bind pose");
            return;
        }
        idle.SampleAnimation(fighter, 0.8f);
        Debug.Log($"[Screenshot] {fighter.name} posed from '{idle.name}' at 0.8s");
    }
}
