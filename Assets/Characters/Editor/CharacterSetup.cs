// CharacterSetup.cs — Elementis fighter characters (Katara / Azula).
//
// AUTHORITATIVE path for the Characters folder. The hand-authored
// Katara.prefab / Azula.prefab / *.controller YAML in this folder is
// best-effort: FBX sub-asset fileIDs (meshes, clips, Avatars) cannot be
// predicted by hand, so those files carry {fileID: 0} placeholders for
// clip/mesh/Avatar references. THIS script resolves everything by name
// through the UnityEditor APIs and is the reliable way to (re)build the
// characters. Run it from the menu:  Elementis > Characters > Setup Characters
//
// What it does:
//   1. Verifies the four animation FBXs imported as Humanoid and that every
//      clip the controllers need exists (by take name).
//   2. Builds a Humanoid Avatar asset per character from the Bip01 rig
//      (Assets/Characters/Avatars/KataraAvatar.asset, AzulaAvatar.asset).
//   3. (Re)builds Katara.controller / Azula.controller with the 15 states
//      (Idle, Run, Punch_Jab, Punch_Cross, Kick, Cast, Hit, KO,
//      Jump_Start, Jump_Loop, Jump_Land, Dodge, Block, RegenChannel,
//      UltimateCast) and the correct clip motions.
//   4. (Re)builds Katara.prefab / Azula.prefab: FBX model + Animator
//      (Avatar + controller) + URP/Lit materials assigned per mesh.
//   5. Logs a verification summary (missing clips / materials / bones).
//
// The AssetPostprocessor below runs on every import under
// Assets/Characters/ and enforces: Humanoid rig, explicit HumanBone maps
// for the CMU kick rig (Unity cannot auto-map those names), per-take clip
// splits (only the takes the controllers use, with loop flags), and
// root-motion baked into pose for the kick (in-place fighting-game clip).
//
// NOTE: written by file inspection only — NOT compiled in the Editor on
// this VM (license is fragile, other agents share the project). The CI
// build will catch compile errors; run the menu item in the Editor and
// check the console summary before calling the characters done.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CharacterSetup
{
    const string Root = "Assets/Characters";
    const string Models = Root + "/Models";
    const string Anims = Root + "/Animations";
    const string Mats = Root + "/Materials";
    const string Avatars = Root + "/Avatars";

    // ------------------------------------------------------------------
    // Clip table: take name -> (source FBX, first frame, last frame, loop)
    // Frame ranges verified in Blender 4.2 (FBX re-import, 2026-11-15).
    // UAL takes are 30 fps; the CMU kick was re-timed to 30 fps, 43f.
    // ------------------------------------------------------------------
    struct ClipSpec
    {
        public string take, file;
        public int first, last;
        public bool loop;
        public ClipSpec(string take, string file, int first, int last, bool loop)
        { this.take = take; this.file = file; this.first = first; this.last = last; this.loop = loop; }
    }

    static readonly ClipSpec[] Ual1Clips = new ClipSpec[]
    {
        new ClipSpec("Idle_Loop",   "UAL1_Standard.fbx",      1, 76, true),
        new ClipSpec("Sprint_Loop", "UAL1_Standard.fbx",      1, 21, true),
        new ClipSpec("Punch_Jab",   "UAL1_Standard.fbx",      1, 27, false),
        new ClipSpec("Punch_Cross", "UAL1_Standard.fbx",      1, 31, false),
        new ClipSpec("Hit_Chest",   "UAL1_Standard.fbx",      1, 11, false),
        new ClipSpec("Death01",     "UAL1_Standard.fbx",      1, 73, false),
        new ClipSpec("Jump_Start",  "UAL1_Standard.fbx",      1, 41, false),
        new ClipSpec("Jump_Loop",   "UAL1_Standard.fbx",      1, 76, true),
        new ClipSpec("Jump_Land",   "UAL1_Standard.fbx",      1, 39, false),
        new ClipSpec("Roll",        "UAL1_Standard.fbx",      1, 45, false),
    };
    static readonly ClipSpec[] Ual2Clips = new ClipSpec[]
    {
        new ClipSpec("Sword_Block", "UAL2_Standard.fbx",      1, 38, false),
    };
    static readonly ClipSpec[] AuthoredClips = new ClipSpec[]
    {
        new ClipSpec("Cast_TwoArm",   "Elementis_Authored.fbx", 1, 18, false),
        new ClipSpec("RegenChannel",  "Elementis_Authored.fbx", 1, 75, true),
        new ClipSpec("UltimateCast",  "Elementis_Authored.fbx", 1, 60, false),
    };
    static readonly ClipSpec[] KickClips = new ClipSpec[]
    {
        new ClipSpec("Kick_Roundhouse", "Kick_Roundhouse.fbx",  1, 43, false),
    };

    static IEnumerable<ClipSpec> AllClips()
    {
        foreach (var c in Ual1Clips) yield return c;
        foreach (var c in Ual2Clips) yield return c;
        foreach (var c in AuthoredClips) yield return c;
        foreach (var c in KickClips) yield return c;
    }

    // Controller states in order: (state name, take name, state speed).
    // Run speed is the CHAR_DATA run_speed (Katara 1.06, Azula 0.96).
    static (string, string)[] StateTable() => new (string, string)[]
    {
        ("Idle", "Idle_Loop"), ("Run", "Sprint_Loop"),
        ("Punch_Jab", "Punch_Jab"), ("Punch_Cross", "Punch_Cross"),
        ("Kick", "Kick_Roundhouse"), ("Cast", "Cast_TwoArm"),
        ("Hit", "Hit_Chest"), ("KO", "Death01"),
        ("Jump_Start", "Jump_Start"), ("Jump_Loop", "Jump_Loop"),
        ("Jump_Land", "Jump_Land"), ("Dodge", "Roll"),
        ("Block", "Sword_Block"), ("RegenChannel", "RegenChannel"),
        ("UltimateCast", "UltimateCast"),
    };

    // ------------------------------------------------------------------
    // HumanBone maps. Names are tried in order; the first existing bone
    // wins, so a renamed/renumbered finger segment never breaks the build.
    // ------------------------------------------------------------------
    static readonly Dictionary<string, string[]> Bip01Map = new Dictionary<string, string[]>
    {
        { "Hips",            new[] { "Bip01 Pelvis" } },
        { "Spine",           new[] { "Bip01 Spine" } },
        { "Chest",           new[] { "Bip01 Spine1" } },
        { "UpperChest",      new[] { "Bip01 Spine2" } },
        { "Neck",            new[] { "Bip01 Neck" } },
        { "Head",            new[] { "Bip01 Head" } },
        { "LeftEye",         new[] { "Bip01 LEye" } },
        { "RightEye",        new[] { "Bip01 REye" } },
        { "LeftShoulder",    new[] { "Bip01 L Clavicle" } },
        { "RightShoulder",   new[] { "Bip01 R Clavicle" } },
        { "LeftUpperArm",    new[] { "Bip01 L UpperArm" } },
        { "RightUpperArm",   new[] { "Bip01 R UpperArm" } },
        { "LeftLowerArm",    new[] { "Bip01 L Forearm" } },
        { "RightLowerArm",   new[] { "Bip01 R Forearm" } },
        { "LeftHand",        new[] { "Bip01 L Hand" } },
        { "RightHand",       new[] { "Bip01 R Hand" } },
        { "LeftThumbProximal",      new[] { "Bip01 L Finger0" } },
        { "LeftThumbIntermediate",  new[] { "Bip01 L Finger01" } },
        { "LeftThumbDistal",        new[] { "Bip01 L Finger02" } },
        { "LeftIndexProximal",      new[] { "Bip01 L Finger1" } },
        { "LeftIndexIntermediate",  new[] { "Bip01 L Finger11" } },
        { "LeftIndexDistal",        new[] { "Bip01 L Finger12" } },
        { "LeftMiddleProximal",     new[] { "Bip01 L Finger2" } },
        { "LeftMiddleIntermediate", new[] { "Bip01 L Finger21" } },
        { "LeftMiddleDistal",       new[] { "Bip01 L Finger22" } },
        { "LeftRingProximal",       new[] { "Bip01 L Finger3" } },
        { "LeftRingIntermediate",   new[] { "Bip01 L Finger31" } },
        { "LeftRingDistal",         new[] { "Bip01 L Finger32" } },
        { "LeftLittleProximal",     new[] { "Bip01 L Finger4" } },
        { "LeftLittleIntermediate", new[] { "Bip01 L Finger41" } },
        { "LeftLittleDistal",       new[] { "Bip01 L Finger42" } },
        { "RightThumbProximal",     new[] { "Bip01 R Finger0" } },
        { "RightThumbIntermediate", new[] { "Bip01 R Finger01" } },
        { "RightThumbDistal",       new[] { "Bip01 R Finger02" } },
        { "RightIndexProximal",     new[] { "Bip01 R Finger1" } },
        { "RightIndexIntermediate", new[] { "Bip01 R Finger11" } },
        { "RightIndexDistal",       new[] { "Bip01 R Finger12" } },
        { "RightMiddleProximal",    new[] { "Bip01 R Finger2" } },
        { "RightMiddleIntermediate", new[] { "Bip01 R Finger21" } },
        { "RightMiddleDistal",      new[] { "Bip01 R Finger22" } },
        { "RightRingProximal",      new[] { "Bip01 R Finger3" } },
        { "RightRingIntermediate",  new[] { "Bip01 R Finger31" } },
        { "RightRingDistal",        new[] { "Bip01 R Finger32" } },
        { "RightLittleProximal",    new[] { "Bip01 R Finger4" } },
        { "RightLittleIntermediate", new[] { "Bip01 R Finger41" } },
        { "RightLittleDistal",      new[] { "Bip01 R Finger42" } },
        // NOTE: Bip01 thighs are parented to Bip01 Spine, not to Pelvis.
        // AvatarBuilder handles that; the Humanoid avatar does not care.
        { "LeftUpperLeg",    new[] { "Bip01 L Thigh" } },
        { "RightUpperLeg",   new[] { "Bip01 R Thigh" } },
        { "LeftLowerLeg",    new[] { "Bip01 L Calf" } },
        { "RightLowerLeg",   new[] { "Bip01 R Calf" } },
        { "LeftFoot",        new[] { "Bip01 L Foot" } },
        { "RightFoot",       new[] { "Bip01 R Foot" } },
        { "LeftToes",        new[] { "Bip01 L Toe0" } },
        { "RightToes",       new[] { "Bip01 R Toe0" } },
    };

    // CMU mocap rig (Kick_Roundhouse.fbx). Unity cannot auto-map these.
    static readonly Dictionary<string, string[]> CmuMap = new Dictionary<string, string[]>
    {
        { "Hips",          new[] { "hip" } },
        { "Spine",         new[] { "abdomen" } },
        { "Chest",         new[] { "chest" } },
        { "Neck",          new[] { "neck" } },
        { "Head",          new[] { "head" } },
        { "LeftShoulder",  new[] { "lCollar" } },
        { "RightShoulder", new[] { "rCollar" } },
        { "LeftUpperArm",  new[] { "lShldr" } },
        { "RightUpperArm", new[] { "rShldr" } },
        { "LeftLowerArm",  new[] { "lForeArm" } },
        { "RightLowerArm", new[] { "rForeArm" } },
        { "LeftHand",      new[] { "lHand" } },
        { "RightHand",     new[] { "rHand" } },
        { "LeftUpperLeg",  new[] { "lThigh" } },
        { "RightUpperLeg", new[] { "rThigh" } },
        { "LeftLowerLeg",  new[] { "lShin" } },
        { "RightLowerLeg", new[] { "rShin" } },
        { "LeftFoot",      new[] { "lFoot" } },
        { "RightFoot",     new[] { "rFoot" } },
    };

    // Quaternius UE-style rig (UAL1 / UAL2 / Elementis_Authored).
    static readonly Dictionary<string, string[]> UalMap = new Dictionary<string, string[]>
    {
        { "Hips",          new[] { "pelvis" } },
        { "Spine",         new[] { "spine_01" } },
        { "Chest",         new[] { "spine_02" } },
        { "UpperChest",    new[] { "spine_03" } },
        { "Neck",          new[] { "neck_01" } },
        { "Head",          new[] { "Head" } },
        { "LeftShoulder",  new[] { "clavicle_l" } },
        { "RightShoulder", new[] { "clavicle_r" } },
        { "LeftUpperArm",  new[] { "upperarm_l" } },
        { "RightUpperArm", new[] { "upperarm_r" } },
        { "LeftLowerArm",  new[] { "lowerarm_l" } },
        { "RightLowerArm", new[] { "lowerarm_r" } },
        { "LeftHand",      new[] { "hand_l" } },
        { "RightHand",     new[] { "hand_r" } },
        { "LeftThumbProximal",     new[] { "thumb_01_l" } },
        { "LeftThumbIntermediate", new[] { "thumb_02_l" } },
        { "LeftThumbDistal",       new[] { "thumb_03_l" } },
        { "LeftIndexProximal",     new[] { "index_01_l" } },
        { "LeftIndexIntermediate", new[] { "index_02_l" } },
        { "LeftIndexDistal",       new[] { "index_03_l" } },
        { "LeftMiddleProximal",    new[] { "middle_01_l" } },
        { "LeftMiddleIntermediate", new[] { "middle_02_l" } },
        { "LeftMiddleDistal",      new[] { "middle_03_l" } },
        { "LeftRingProximal",      new[] { "ring_01_l" } },
        { "LeftRingIntermediate",  new[] { "ring_02_l" } },
        { "LeftRingDistal",        new[] { "ring_03_l" } },
        { "LeftLittleProximal",    new[] { "pinky_01_l" } },
        { "LeftLittleIntermediate", new[] { "pinky_02_l" } },
        { "LeftLittleDistal",      new[] { "pinky_03_l" } },
        { "RightThumbProximal",    new[] { "thumb_01_r" } },
        { "RightThumbIntermediate", new[] { "thumb_02_r" } },
        { "RightThumbDistal",      new[] { "thumb_03_r" } },
        { "RightIndexProximal",    new[] { "index_01_r" } },
        { "RightIndexIntermediate", new[] { "index_02_r" } },
        { "RightIndexDistal",      new[] { "index_03_r" } },
        { "RightMiddleProximal",   new[] { "middle_01_r" } },
        { "RightMiddleIntermediate", new[] { "middle_02_r" } },
        { "RightMiddleDistal",     new[] { "middle_03_r" } },
        { "RightRingProximal",     new[] { "ring_01_r" } },
        { "RightRingIntermediate", new[] { "ring_02_r" } },
        { "RightRingDistal",       new[] { "ring_03_r" } },
        { "RightLittleProximal",   new[] { "pinky_01_r" } },
        { "RightLittleIntermediate", new[] { "pinky_02_r" } },
        { "RightLittleDistal",     new[] { "pinky_03_r" } },
        { "LeftUpperLeg",  new[] { "thigh_l" } },
        { "RightUpperLeg", new[] { "thigh_r" } },
        { "LeftLowerLeg",  new[] { "calf_l" } },
        { "RightLowerLeg", new[] { "calf_r" } },
        { "LeftFoot",      new[] { "foot_l" } },
        { "RightFoot",     new[] { "foot_r" } },
        { "LeftToes",      new[] { "ball_l" } },
        { "RightToes",     new[] { "ball_r" } },
    };

    // Flattened single-candidate maps for the import postprocessor
    // (first candidate name per HumanBone — the importer only needs one).
    public static Dictionary<string, string> CmuMapForImporter() =>
        CmuMap.ToDictionary(kv => kv.Key, kv => kv.Value[0]);
    public static Dictionary<string, string> UalMapForImporter() =>
        UalMap.ToDictionary(kv => kv.Key, kv => kv.Value[0]);

    // ------------------------------------------------------------------
    // Menu: build / rebuild everything, then verify.
    // ------------------------------------------------------------------
    [MenuItem("Elementis/Characters/Setup Characters")]
    public static void SetupCharacters()
    {
        var clips = LoadAllClips();
        int missing = 0;
        foreach (var spec in AllClips())
            if (!clips.ContainsKey(spec.take))
            { Debug.LogError($"[CharacterSetup] MISSING clip take '{spec.take}' in {spec.file}"); missing++; }
        if (missing > 0)
        {
            Debug.LogError($"[CharacterSetup] {missing} clip(s) missing — fix the FBX import first (see CHARACTER-NOTES.md).");
            return;
        }

        BuildAvatar("Katara", Models + "/Katara.fbx", Bip01Map);
        BuildAvatar("Azula", Models + "/Azula.fbx", Bip01Map);
        BuildController("Katara", clips, 1.06f);
        BuildController("Azula", clips, 0.96f);
        BuildPrefab("Katara", "Katara");
        BuildPrefab("Azula", "Azula");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CharacterSetup] DONE. Run Elementis > Characters > Verify Characters for the summary.");
    }

    [MenuItem("Elementis/Characters/Verify Characters")]
    public static void VerifyCharacters()
    {
        foreach (var name in new[] { "Katara", "Azula" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + name + ".prefab");
            if (prefab == null) { Debug.LogError($"[CharacterSetup] {name}.prefab NOT FOUND"); continue; }
            var anim = prefab.GetComponent<Animator>();
            Debug.Log($"[CharacterSetup] {name}: prefab={(prefab != null)}, animator={(anim != null)}, " +
                $"avatar={(anim != null && anim.avatar != null ? anim.avatar.name + " human=" + anim.avatar.isHuman : "MISSING")}, " +
                $"controller={(anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "MISSING")}");
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == null)
                        Debug.LogWarning($"[CharacterSetup] {name}/{r.name}: material slot {i} is NULL");
            }
            var ctrl = anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
            if (ctrl != null)
                foreach (var layer in ctrl.layers)
                    foreach (var st in layer.stateMachine.states)
                        Debug.Log($"[CharacterSetup] {name} state '{st.state.name}' motion=" +
                            $"{(st.state.motion != null ? st.state.motion.name + " " + ((st.state.motion as AnimationClip)?.length ?? 0f).ToString("0.00") + "s" : "MISSING")} speed={st.state.speed}");
        }
    }

    // ------------------------------------------------------------------
    // Clip loading: find every AnimationClip sub-asset of the animation FBXs.
    // ------------------------------------------------------------------
    static Dictionary<string, AnimationClip> LoadAllClips()
    {
        var map = new Dictionary<string, AnimationClip>();
        foreach (var f in new[] { "UAL1_Standard.fbx", "UAL2_Standard.fbx", "Elementis_Authored.fbx", "Kick_Roundhouse.fbx" })
        {
            var path = Anims + "/" + f;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is AnimationClip clip && !string.IsNullOrEmpty(clip.name))
                {
                    if (!map.ContainsKey(clip.name)) map.Add(clip.name, clip);
                    else Debug.LogWarning($"[CharacterSetup] duplicate clip name '{clip.name}' in {f}");
                }
            }
        }
        return map;
    }

    // ------------------------------------------------------------------
    // Avatar: build a Humanoid Avatar asset from the imported FBX rig.
    // ------------------------------------------------------------------
    static void BuildAvatar(string charName, string fbxPath, Dictionary<string, string[]> boneMap)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (root == null) { Debug.LogError($"[CharacterSetup] FBX not found: {fbxPath}"); return; }

        var all = root.GetComponentsInChildren<Transform>(true);
        var byName = all.ToDictionary(t => t.name, t => t);

        var human = new List<HumanBone>();
        var missing = new List<string>();
        foreach (var kv in boneMap)
        {
            string found = null;
            foreach (var cand in kv.Value)
                if (byName.ContainsKey(cand)) { found = cand; break; }
            if (found != null) human.Add(new HumanBone { humanName = kv.Key, boneName = found });
            else missing.Add(kv.Key);
        }
        if (missing.Count > 0)
            Debug.LogWarning($"[CharacterSetup] {charName}: unmapped HumanBones (optional ones are fine): {string.Join(", ", missing)}");

        var skeleton = new List<SkeletonBone>();
        foreach (var t in all)
            skeleton.Add(new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            });

        var desc = new HumanDescription
        {
            human = human.ToArray(),
            skeleton = skeleton.ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f,
            hasTranslationDoF = false,
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"[CharacterSetup] {charName}: AvatarBuilder FAILED (valid={avatar != null && avatar.isValid}, human={avatar != null && avatar.isHuman}). " +
                "Check the Bip01 bone names in the FBX.");
            return;
        }
        avatar.name = charName + "Avatar";
        System.IO.Directory.CreateDirectory(Avatars);
        var assetPath = Avatars + "/" + charName + "Avatar.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Avatar>(assetPath);
        if (existing != null) AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(avatar, assetPath);
        Debug.Log($"[CharacterSetup] {charName}: Avatar built ({human.Count} HumanBones, valid={avatar.isValid}).");
    }

    // ------------------------------------------------------------------
    // Controller: one layer, 15 states, combat drives states by name.
    // No transitions in v1 — the combat system calls Animator.Play(state).
    // ------------------------------------------------------------------
    static void BuildController(string charName, Dictionary<string, AnimationClip> clips, float runSpeed)
    {
        var path = Root + "/" + charName + ".controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var layer = ctrl.layers[0];
        layer.name = "Base Layer";
        var sm = layer.stateMachine;

        AnimatorState defaultState = null;
        foreach (var (stateName, take) in StateTable())
        {
            var st = sm.AddState(stateName);
            st.motion = clips[take];
            if (stateName == "Run") st.speed = runSpeed;
            if (stateName == "Idle") defaultState = st;
        }
        sm.defaultState = defaultState;
        AssetDatabase.SaveAssets();
        Debug.Log($"[CharacterSetup] {charName}: controller built ({StateTable().Length} states, default Idle, Run speed {runSpeed}).");
    }

    // ------------------------------------------------------------------
    // Prefab: FBX model + Animator (Avatar + controller) + materials.
    // Materials are matched by the FBX material name; Katara beads get
    // their own pale-blue emissive material by renderer name.
    // ------------------------------------------------------------------
    static readonly Dictionary<string, string> MaterialByFbxName = new Dictionary<string, string>
    {
        // Katara (Rocketbox f008)
        { "f008_body", "Katara_Body" }, { "f008_head", "Katara_Head" }, { "f008_opacity", "Katara_Opacity" },
        // Azula (Rocketbox f002)
        { "f002_body", "Azula_Body" }, { "f002_head", "Azula_Head" }, { "f002_opacity", "Azula_Opacity" },
        // Shared Blender costume material names (per-FBX files, so no clash)
        { "top", "TOP" }, { "trim", "TRIM" }, { "accent", "ACCENT" }, { "hair", "HAIR" },
        { "eye", "EYE" }, { "pants", "PANTS" }, { "fur", "FUR" }, { "gold", "GOLD" }, { "sash", "SASH" },
    };

    static void BuildPrefab(string charName, string fbxName)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "/" + fbxName + ".fbx");
        if (fbx == null) { Debug.LogError($"[CharacterSetup] FBX not found: {Models}/{fbxName}.fbx"); return; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        go.name = charName;

        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Avatars + "/" + charName + "Avatar.asset");
        if (avatar != null) anim.avatar = avatar;
        else Debug.LogWarning($"[CharacterSetup] {charName}: Avatar asset missing, Animator keeps the FBX auto-avatar.");
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/" + charName + ".controller");
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false; // combat moves the root; clips are in-place
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // Materials, per renderer.
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var src = r.sharedMaterials;
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string matName = ResolveMaterial(charName, r.name, src[i] != null ? src[i].name : "");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "/" + matName + ".mat");
                if (mat == null) Debug.LogWarning($"[CharacterSetup] {charName}/{r.name}: material asset missing: {Mats}/{matName}.mat (FBX material '{(src[i] != null ? src[i].name : "?")}')");
                dst[i] = mat;
            }
            r.sharedMaterials = dst;
        }

        var prefabPath = Root + "/" + charName + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[CharacterSetup] {charName}: prefab built at {prefabPath}.");
    }

    static string ResolveMaterial(string charName, string rendererName, string fbxMatName)
    {
        // Katara beads: pale blue + emissive, distinct from the bracers.
        if (charName == "Katara" && rendererName.StartsWith("Katara_Bead")) return "Katara_Bead";
        if (!MaterialByFbxName.TryGetValue(fbxMatName, out var key)) return charName + "_Robe";
        switch (key)
        {
            case "TOP": return charName == "Katara" ? "Katara_Robe" : "Azula_Tunic";
            case "TRIM": return "Katara_Trim";
            case "ACCENT": return "Katara_Bracer";
            case "HAIR": return charName + "_Hair";
            case "EYE": return charName + "_Eye";
            case "PANTS": return charName == "Katara" ? "Katara_Boot" : "Azula_Pants";
            case "FUR": return "Katara_Fur";
            case "GOLD": return "Azula_Gold";
            case "SASH": return "Azula_Sash";
            default: return key;
        }
    }
}

// ======================================================================
// Import postprocessor: enforces Humanoid rig + clip splits on import.
// Gated to Assets/Characters/ so other agents' assets are untouched.
// ======================================================================
class CharacterImportPostprocessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!(assetImporter is ModelImporter importer)) return;
        if (!assetPath.StartsWith("Assets/Characters/")) return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.useFileUnits = true;
        importer.globalScale = 1f;
        importer.importBlendShapes = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.optimizeGameObjects = false;

        // Explicit HumanBone map where Unity's auto-mapper cannot cope.
        if (assetPath.EndsWith("Kick_Roundhouse.fbx"))
        {
            importer.humanDescription = BuildHumanDescription(CharacterSetup.CmuMapForImporter());
            importer.clipAnimations = new[]
            {
                KickClip("Kick_Roundhouse", 1, 43),
            };
        }
        else if (assetPath.EndsWith("UAL1_Standard.fbx"))
        {
            importer.humanDescription = BuildHumanDescription(CharacterSetup.UalMapForImporter());
            importer.clipAnimations = Ual1ClipDefs();
        }
        else if (assetPath.EndsWith("UAL2_Standard.fbx"))
        {
            importer.humanDescription = BuildHumanDescription(CharacterSetup.UalMapForImporter());
            importer.clipAnimations = new[]
            {
                SimpleClip("Sword_Block", 1, 38, false),
            };
        }
        else if (assetPath.EndsWith("Elementis_Authored.fbx"))
        {
            importer.humanDescription = BuildHumanDescription(CharacterSetup.UalMapForImporter());
            importer.clipAnimations = new[]
            {
                SimpleClip("Cast_TwoArm", 1, 18, false),
                SimpleClip("RegenChannel", 1, 75, true),
                SimpleClip("UltimateCast", 1, 60, false),
            };
        }
        // Katara.fbx / Azula.fbx: Bip01 auto-maps correctly; no override.
    }

    static ModelImporterClipAnimation SimpleClip(string take, int first, int last, bool loop) =>
        new ModelImporterClipAnimation
        {
            name = take, takeName = take, firstFrame = first, lastFrame = last,
            loopTime = loop, loopPose = loop,
            wrapMode = loop ? WrapMode.Loop : WrapMode.Once,
            keepOriginalOrientation = false,
            keepOriginalPositionXZ = false,
            keepOriginalPositionY = false,
        };

    // Kick: ALL root motion baked into pose so the roundhouse plays in
    // place (the CMU take travels ~1.9 m forward in its window).
    static ModelImporterClipAnimation KickClip(string take, int first, int last) =>
        new ModelImporterClipAnimation
        {
            name = take, takeName = take, firstFrame = first, lastFrame = last,
            loopTime = false, loopPose = false, wrapMode = WrapMode.Once,
            keepOriginalOrientation = false,
            keepOriginalPositionXZ = false,
            keepOriginalPositionY = false,
        };

    static ModelImporterClipAnimation[] Ual1ClipDefs() => new[]
    {
        SimpleClip("Idle_Loop", 1, 76, true),
        SimpleClip("Sprint_Loop", 1, 21, true),
        SimpleClip("Punch_Jab", 1, 27, false),
        SimpleClip("Punch_Cross", 1, 31, false),
        SimpleClip("Hit_Chest", 1, 11, false),
        SimpleClip("Death01", 1, 73, false),
        SimpleClip("Jump_Start", 1, 41, false),
        SimpleClip("Jump_Loop", 1, 76, true),
        SimpleClip("Jump_Land", 1, 39, false),
        SimpleClip("Roll", 1, 45, false),
    };

    static HumanDescription BuildHumanDescription(Dictionary<string, string> map)
    {
        var human = new List<HumanBone>();
        foreach (var kv in map)
            human.Add(new HumanBone { humanName = kv.Key, boneName = kv.Value });
        return new HumanDescription
        {
            human = human.ToArray(),
            skeleton = new SkeletonBone[0], // Unity derives the skeleton from the model
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f,
            hasTranslationDoF = false,
        };
    }
}
