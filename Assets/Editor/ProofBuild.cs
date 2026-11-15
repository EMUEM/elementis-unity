using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class ProofBuild
{
    public static void BuildAndroid()
    {
        // Android SDK / NDK / JDK: use the ones bundled with the Hub-installed module.
        // Project is ~/workspace/tools/unity/projects/elementis-proof, editor is
        // ~/workspace/tools/unity/editors/6000.0.85f2 — so ../editors/... from the project dir.
        var projectDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
        var androidPlayer = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            projectDir, "../editors/6000.0.85f2/Editor/Data/PlaybackEngines/AndroidPlayer"));
        if (System.IO.Directory.Exists(androidPlayer + "/SDK"))
            EditorPrefs.SetString("AndroidSdkRoot", androidPlayer + "/SDK");
        if (System.IO.Directory.Exists(androidPlayer + "/NDK"))
            EditorPrefs.SetString("AndroidNdkRoot", androidPlayer + "/NDK");
        if (System.IO.Directory.Exists(androidPlayer + "/OpenJDK"))
            EditorPrefs.SetString("JdkPath", androidPlayer + "/OpenJDK");

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
        // NOTE: no public API sets the global Android texture-compression default in
        // Unity 6.0.85f2 (SetTextureCompressionFormat / SetDefaultTextureCompressionFormat
        // do not exist as public PlayerSettings members). ASTC is enforced per-texture
        // via TextureImporter platform settings in the real project; this proof scene
        // has no textures, so the build output is unaffected.

        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

        // Honor game-ci's -customBuildPath when present (the CI runner passes an
        // absolute path such as /github/workspace/build/Android/Android.apk).
        // Local builds run without that argument and keep the default path.
        var locationPathName = "Builds/Android/elementis-proof.apk";
        var args = System.Environment.GetCommandLineArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-customBuildPath" && !string.IsNullOrEmpty(args[i + 1]))
            {
                locationPathName = args[i + 1];
                break;
            }
        }

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = locationPathName,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("PROOF_BUILD_RESULT:" + report.summary.result + " errors:" + report.summary.totalErrors);
        if (report.summary.result != BuildResult.Succeeded)
            throw new System.Exception("Build failed: " + report.summary.result);
        Debug.Log("PROOF_BUILD_DONE");
    }
}
