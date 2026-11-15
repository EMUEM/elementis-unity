using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class ProofBuild
{
    public static void BuildAndroid()
    {
        // Phase 2: finalize the character prefabs BEFORE building. The
        // hand-authored Katara/Azula prefabs carry {fileID: 0} placeholders
        // for FBX sub-assets (meshes, clips, Avatars) until
        // CharacterSetup.SetupCharacters() runs in the Editor. Setup is
        // idempotent (delete + rebuild: avatar, controller, prefab), so
        // running it on every build is safe. If it cannot complete
        // (missing clips), VerifyCharactersOrThrow fails the build LOUDLY
        // instead of shipping placeholder characters.
        CharacterSetup.SetupCharacters();
        VerifyCharactersOrThrow();

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
        // game-ci CLI v0.1.75 validateBuild only passes if the build output
        // contains the literal string "Build succeeded!" (with "!") or a
        // "# Build results #" section. Unity 6000.0.85f2 prints neither, so
        // emit the exact string here — ONLY on a genuinely successful build.
        Debug.Log("Build succeeded!");
        Debug.Log("PROOF_BUILD_DONE");
    }

    // Phase 2 gate: after CharacterSetup runs, both fighter prefabs must
    // carry a real human Avatar and a real AnimatorController. Anything
    // else means the characters never finalized — fail the build with a
    // clear message instead of shipping a fight with invisible fighters.
    static void VerifyCharactersOrThrow()
    {
        foreach (var name in new[] { "Katara", "Azula" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Characters/" + name + ".prefab");
            if (prefab == null)
                throw new System.Exception("[CharacterSetup] " + name + ".prefab NOT FOUND.");
            var anim = prefab.GetComponent<Animator>();
            if (anim == null)
                throw new System.Exception("[CharacterSetup] " + name + ".prefab has no Animator.");
            if (anim.avatar == null || !anim.avatar.isHuman)
                throw new System.Exception("[CharacterSetup] " + name +
                    " has no valid human Avatar — SetupCharacters did not finalize it.");
            if (anim.runtimeAnimatorController == null)
                throw new System.Exception("[CharacterSetup] " + name +
                    " has no AnimatorController — SetupCharacters did not finalize it.");
            Debug.Log("[CharacterSetup] verified " + name + ": avatar human=" +
                anim.avatar.isHuman + ", controller=" +
                anim.runtimeAnimatorController.name);
        }
    }
}
