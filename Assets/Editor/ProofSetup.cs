using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProofSetup
{
    public static void Setup()
    {
        // URP render pipeline asset (Create() also creates the default renderer data)
        System.IO.Directory.CreateDirectory("Assets/Settings");
        var rp = UniversalRenderPipelineAsset.Create();
        AssetDatabase.CreateAsset(rp, "Assets/Settings/URPAsset.asset");
        var rendererData = rp.rendererDataList[0];
        if (rendererData != null)
            AssetDatabase.AddObjectToAsset(rendererData, rp);
        AssetDatabase.SaveAssets();
        GraphicsSettings.renderPipelineAsset = rp;

        // Minimal scene: camera + directional light
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGO = new GameObject("Main Camera");
        camGO.AddComponent<Camera>();
        camGO.transform.position = new Vector3(0, 1, -5);
        camGO.transform.LookAt(Vector3.zero);
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");

        // Build settings
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };

        // Player settings (proof package id; real game uses com.elementis.prototype)
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.elementis.proof");
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.Android.bundleVersionCode = 1;

        AssetDatabase.SaveAssets();
        Debug.Log("PROOF_SETUP_DONE");
    }
}
