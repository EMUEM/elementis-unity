using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

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

        // v0.2.0 ROOT-CAUSE FIX (2026-11-15): the shipped APK contained
        // NO arena / fighters / VFX / meshes at all — only the default
        // skybox + the 20 SFX clips. The scene's prefab references are
        // GUIDs, and the build was an INCREMENTAL player build
        // ("Incremental Player Build", "player data was not rebuilt",
        // Bee "2 items updated") that reused stale player data from the
        // restored Library cache; on top of that the hand-authored
        // character YAML files failed to load at editor startup (leading
        // '#' comments before %YAML 1.1), so the in-memory scene had null
        // prefab references. The result on device: an empty grey/brown VS
        // screen and a fight that never started. Three gates below make
        // that impossible to ship again:
        //   1. VerifySceneWiringOrThrow — reload Main.unity from disk
        //      (the build uses the OPEN scene, so a stale in-memory scene
        //      would silently ship null refs) and fail LOUDLY on any null
        //      FightBootstrap reference that has no fallback (arena,
        //      prefabs, camera, rounds). The two data refs heal instead:
        //      CI run 43734265750 proved they deserialize to NULL even
        //      though the .asset files exist, import, and match by GUID —
        //      so the gate loads them directly from disk, assigns them,
        //      and re-saves the scene (the fight also falls back to
        //      FighterDataLibrary.Create, so null data is never fatal).
        //   2. SmokeTestFightStartOrThrow — run the real spawn + BeginMatch
        //      path for BOTH hero orders in the editor and fail LOUDLY if
        //      either cannot start a match (the exact v0.2.0 failure).
        //   3. ForceCleanPlayerBuild — delete the incremental build caches
        //      so the player data is rebuilt from the current assets.
        VerifySceneWiringOrThrow();
        SmokeTestFightStartOrThrow();
        ForceCleanPlayerBuild();

        // Android SDK / NDK / JDK: use the ones bundled with the Hub-installed module.
        // Project is ~/workspace/tools/unity/projects/elementis-proof, editor is
        // ~/workspace/tools/unity/editors/6000.0.85f2 — so ../editors/... from the project dir.
        var projectDir = Directory.GetParent(Application.dataPath).FullName;
        var androidPlayer = Path.GetFullPath(Path.Combine(
            projectDir, "../editors/6000.0.85f2/Editor/Data/PlaybackEngines/AndroidPlayer"));
        if (Directory.Exists(androidPlayer + "/SDK"))
            EditorPrefs.SetString("AndroidSdkRoot", androidPlayer + "/SDK");
        if (Directory.Exists(androidPlayer + "/NDK"))
            EditorPrefs.SetString("AndroidNdkRoot", androidPlayer + "/NDK");
        if (Directory.Exists(androidPlayer + "/OpenJDK"))
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

        // Never let a stale APK from a previous build be mistaken for a
        // fresh one (the upload step errors when no file is found, but a
        // stale file would upload silently).
        if (File.Exists(locationPathName))
        {
            File.Delete(locationPathName);
            Debug.Log("[ProofBuild] deleted stale output APK: " + locationPathName);
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

    // Gate 1: reload Main.unity from disk and verify every FightBootstrap
    // reference resolves. BuildPlayer builds the OPEN scene, so if the
    // editor started with broken prefab files the in-memory scene keeps
    // null refs even after CharacterSetup fixed the files on disk — the
    // build would then ship an empty world with zero errors. Fail LOUDLY.
    //
    // Data assets are special (CI run 43734265750): the scene's
    // kataraData/azulaData refs deserialize to NULL even though the
    // .asset files exist, import cleanly, and their GUIDs match the
    // scene — a deserialization quirk of the hand-authored
    // ScriptableObject YAML. The fight does NOT depend on them
    // (FighterController.Setup falls back to FighterDataLibrary.Create,
    // the same approved numbers), so a null data ref is healed, not
    // fatal: load the asset directly from disk, assign it to the
    // bootstrap, and re-save the scene so the build uses the real
    // data. Only a disk asset that cannot be loaded at all is fatal.
    static void VerifySceneWiringOrThrow()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        var fightGo = GameObject.Find("Fight");
        if (fightGo == null)
            throw new System.Exception("[ProofBuild] Main.unity has no 'Fight' object.");
        var bootstrap = fightGo.GetComponent<FightBootstrap>();
        if (bootstrap == null)
            throw new System.Exception("[ProofBuild] 'Fight' has no FightBootstrap component.");
        if (bootstrap.arenaPrefab == null)
            throw new System.Exception("[ProofBuild] FightBootstrap.arenaPrefab is NULL in Main.unity — the arena would never instantiate (empty VS screen).");
        if (bootstrap.kataraPrefab == null)
            throw new System.Exception("[ProofBuild] FightBootstrap.kataraPrefab is NULL in Main.unity — Katara would never spawn.");
        if (bootstrap.azulaPrefab == null)
            throw new System.Exception("[ProofBuild] FightBootstrap.azulaPrefab is NULL in Main.unity — Azula would never spawn.");
        if (bootstrap.reelCamera == null)
            throw new System.Exception("[ProofBuild] FightBootstrap.reelCamera is NULL in Main.unity — no fight camera.");
        if (bootstrap.roundManager == null)
            throw new System.Exception("[ProofBuild] FightBootstrap.roundManager is NULL in Main.unity — BeginMatch would return false and the fight would never start.");
        // Diagnostic: what did the prefab refs actually resolve to?
        // (CI run 43735378088: the gate passed all five refs as non-null,
        // yet Instantiate(arenaPrefab) threw InvalidCastException — the
        // scene referenced fileID 100100000 but the prefab files' roots
        // were 2000000000000000001 / 100000. The roots are now
        // 100100000 in all three files; this log proves the resolution.)
        Debug.Log("[ProofBuild] arenaPrefab -> " + AssetDatabase.GetAssetPath(bootstrap.arenaPrefab) +
            " | kataraPrefab -> " + AssetDatabase.GetAssetPath(bootstrap.kataraPrefab) +
            " | azulaPrefab -> " + AssetDatabase.GetAssetPath(bootstrap.azulaPrefab));
        // Data refs: diagnose + heal (see the comment above).
        Debug.Log("[ProofBuild] scene kataraData ref: " + (bootstrap.kataraData == null ? "NULL" : "ok") +
            " | azulaData ref: " + (bootstrap.azulaData == null ? "NULL" : "ok"));
        bool healed = false;
        if (bootstrap.kataraData == null)
        {
            bootstrap.kataraData = AssetDatabase.LoadAssetAtPath<FighterData>("Assets/Scripts/Data/KataraData.asset");
            Debug.Log("[ProofBuild] direct load KataraData.asset: " + (bootstrap.kataraData == null ? "NULL (asset itself unloadable)" : "OK"));
            if (bootstrap.kataraData == null)
                throw new System.Exception("[ProofBuild] KataraData.asset cannot be loaded from disk — the data asset itself is broken; regenerate it from FighterDataLibrary.");
            healed = true;
        }
        if (bootstrap.azulaData == null)
        {
            bootstrap.azulaData = AssetDatabase.LoadAssetAtPath<FighterData>("Assets/Scripts/Data/AzulaData.asset");
            Debug.Log("[ProofBuild] direct load AzulaData.asset: " + (bootstrap.azulaData == null ? "NULL (asset itself unloadable)" : "OK"));
            if (bootstrap.azulaData == null)
                throw new System.Exception("[ProofBuild] AzulaData.asset cannot be loaded from disk — the data asset itself is broken; regenerate it from FighterDataLibrary.");
            healed = true;
        }
        if (healed)
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.LogWarning("[ProofBuild] healed null data ref(s) from the disk .asset files and re-saved Main.unity for this build.");
        }
        Debug.Log("[ProofBuild] Main.unity wiring verified: arena + both prefabs + both data + camera + rounds.");
    }

    // Gate 2: run the real spawn + BeginMatch path for BOTH hero orders
    // in the editor. The azula-player order is the one the owner took in
    // v0.2.0 and the one that failed. The scene is re-opened afterwards
    // so the build starts from a clean disk state.
    static void SmokeTestFightStartOrThrow()
    {
        var fightGo = GameObject.Find("Fight");
        var bootstrap = fightGo != null ? fightGo.GetComponent<FightBootstrap>() : null;
        if (bootstrap == null)
            throw new System.Exception("[ProofBuild] smoke test: FightBootstrap not found.");
        if (!bootstrap.EditorSmokeTest(out string error))
            throw new System.Exception("[ProofBuild] SMOKE TEST FAILED: " + error +
                " — the fight would never start on device. Fix it before shipping.");
        Debug.Log("[ProofBuild] smoke test passed: BeginMatch works for katara-player AND azula-player, Phase reaches Countdown.");
        // Reset the scene to its on-disk state (the smoke test spawned and
        // destroyed objects in the open scene).
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
    }

    // Gate 3: force a clean player build. The v0.2.0 CI build was an
    // incremental player build that reused stale player data from the
    // restored Library cache ("player data was not rebuilt"). Library/Bee
    // and Library/BuildCache are purely build caches — Unity rebuilds
    // them. Deleting them costs build time and buys correctness.
    static void ForceCleanPlayerBuild()
    {
        var library = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library");
        foreach (var dir in new[] { "Bee", "BuildCache" })
        {
            var p = Path.Combine(library, dir);
            if (!Directory.Exists(p)) continue;
            try
            {
                Directory.Delete(p, true);
                Debug.Log("[ProofBuild] deleted stale incremental build cache: " + p);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ProofBuild] could not delete " + p + ": " + e.Message +
                    " — the build may reuse stale player data.");
            }
        }
    }

    // Phase 2 gate: after CharacterSetup runs, both fighter prefabs must
    // carry a real human Avatar and a real AnimatorController, and every
    // renderer must have a real material. Anything else means the
    // characters never finalized — fail the build with a clear message
    // instead of shipping a fight with invisible fighters.
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
            // A NULL material slot renders nothing (Azula's pants/sleeves/
            // tunic were NULL in v0.2.0 because ResolveMaterial fell back
            // to the non-existent Azula_Robe.mat) — fail LOUDLY.
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int matCount = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        throw new System.Exception("[CharacterSetup] " + name + "/" + r.name +
                            ": material slot " + i + " is NULL — that body part would be invisible. Fix CharacterSetup.ResolveMaterial.");
                    matCount++;
                }
            }
            Debug.Log("[CharacterSetup] verified " + name + ": avatar human=" +
                anim.avatar.isHuman + ", controller=" +
                anim.runtimeAnimatorController.name + ", renderers=" + renderers.Length +
                ", materials=" + matCount + " (none null).");
        }
    }
}
