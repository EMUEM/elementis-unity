# Elementis Characters — CHARACTER-NOTES

Two playable fighters for the Elementis Unity V1 (Katara vs Azula), built
2026-11-15. **The bar:** REAL, detailed, mature humanoid characters —
never primitives/boxes, nothing Roblox/Minecraft (the owner's Godot v1.1/v1.2
were rejected for exactly that look). Visual contract: the approved v1.2
fight-scene render (`media-generation-elementis-v12-fight-scene-*.webp`) —
Katara from behind in blue with fur collar + long braided hair with blue
beads, water whips arcing; Azula facing her in red/black/gold with blue
fire, scary and intimidating. Katara = power + inner rage; Azula = scary,
intimidating. Mature, serious, not cartoonish/childish.

> **IP note:** Katara and Azula are Nickelodeon / Avatar: The Last Airbender
> characters. This is a PRIVATE build only. Do not present the names, looks,
> or this build as safe for public release.

## What was built

`Assets/Characters/` (Unity 6000.0.85f2, URP 17.0.3):

| File | Contents |
|---|---|
| `Models/Katara.fbx` (972,332 B) | Katara: Rocketbox `Female_Adult_08` body + 18 modeled costume pieces |
| `Models/Azula.fbx` (1,126,716 B) | Azula: Rocketbox `Female_Adult_02` body + 18 modeled costume pieces |
| `Animations/UAL1_Standard.fbx` (61 MB) | Quaternius UAL1 — 43 takes, UE-style rig |
| `Animations/UAL2_Standard.fbx` (70 MB) | Quaternius UAL2 — 43 takes, UE-style rig |
| `Animations/Elementis_Authored.fbx` (2.2 MB) | Authored in Blender: Cast_TwoArm, RegenChannel, UltimateCast |
| `Animations/Kick_Roundhouse.fbx` (399 KB) | CMU mocap 135_07, trimmed + re-timed to 30 fps |
| `Textures/f008_*.tga` (7) / `f002_*.tga` (7) | Rocketbox body/head color+normal+specular, opacity (2048²) |
| `Materials/*.mat` (20) | URP/Lit, exact CHAR_DATA colors (see below) |
| `Katara.controller` / `Azula.controller` | 15 states each (see clip table); motion links are placeholders — see §Setup |
| `Katara.prefab` / `Azula.prefab` | Root + Animator (real controller link); model/Avatar/materials attached by setup script |
| `Editor/CharacterSetup.cs` | **Authoritative**: import postprocessor + menu `Elementis > Characters > Setup Characters` / `Verify Characters` |
| `Avatars/` | (empty — filled by the setup script with `KataraAvatar.asset`, `AzulaAvatar.asset`) |

## Sources + licenses (all free, all login-free)

| Source | URL | License | What was used |
|---|---|---|---|
| Microsoft Rocketbox | `https://github.com/microsoft/Microsoft-Rocketbox` | MIT (Copyright (c) 2020 Microsoft) | Bodies: `Female_Adult_08` (Katara), `Female_Adult_02` (Azula). Sparse clone at `~/workspace/tools/rocketbox/` (260 MB). 1 mesh each, ~4,500 verts, Bip01 80-bone rig, height 1.735 m. |
| Quaternius Universal Animation Library 1 + 2, Universal Base Characters | `https://github.com/NafisRayan/Animate-Rigged-Humanoid-No-Blender` (public mirror of the Quaternius CC0 Standard editions; Quaternius direct site routes downloads through itch.io, no direct zip URL) | CC0 1.0 Universal ("Models by @Quaternius") | UAL1/UAL2 animation FBXs (43 takes each). UBC kept as backup only, NOT used as bodies (stylized). |
| CMU Graphics Lab Motion Capture Database, via HuggingFace `gbionics/cmu-fbx` | `https://huggingface.co/datasets/gbionics/cmu-fbx` (file `animations/135_07.fbx`); trim window from `https://github.com/zenith-umbra/.../cmu_map.json` | Free to copy/modify/redistribute; do not resell the animations themselves; credit `mocap.cs.cmu.edu` | Roundhouse kick, `135_07: ["CMU_Kick_Roundhouse", 0.5, 1.9]` s. |
| Poly Pizza / OpenGameArt | — | — | Checked: Poly Pizza API needs an API key (401); no usable CC0 humanoid found. Not used. |
| Mixamo / Sketchfab / Unity Asset Store | — | — | NOT used: all need an account/login. No credentials used or requested anywhere in this work. |

**Why Rocketbox bodies, not Quaternius characters:** Quaternius characters
are stylized/cute low-poly and fail the bar. Rocketbox is the only
login-free source found with REAL, detailed, mature adult female scans.
Costume overlays cover the original modern clothes almost entirely; hands,
neck and face remain from the scan. Skin tint stays photographic (close to
CHAR_DATA); costume uses flat CHAR_DATA colors.

## Character construction (Blender 4.2.0, `build_character.py`)

Script: `~/workspace/tools/elementis-characters/scripts/build_character.py`
(rebuild any time; `.blend` files saved next to it). Costume pieces are
separate modeled meshes, skinned (`ARMATURE_AUTO`) where they must deform
(robe/tunic/sleeves/pants) and bone-parented where rigid (collar, belt,
beads, headpiece, eyes, boots, pads). Every piece is a distinct mesh so the
fight camera (behind view) reads the silhouette — the fix for "reads as a
plain robe from behind".

**Katara** (19 meshes) — body 4,516 verts (`f008_body` / `f008_head` /
`f008_opacity`) + Robe (lathe, open hem, 16 vertex groups), Sleeve_L/R
(shoulder→elbow), FurCollar (jittered torus, `(0.62,0.66,0.72)`), Belt
(trim), Bracer_L/R (accent), Boot_L/R, HairCap shell, Braid (Bezier tube,
draped OUTSIDE the robe to mid-back), Bead_1/2/3 (accent2, on the braid
surface), Loopy_L/R, Eye_L/R (blue `(0.15,0.35,0.65)`, emissive).

**Azula** (19 meshes) — body 4,662 verts (`f002_body` / `f002_head` /
`f002_opacity`) + Tunic (lathe, open hem), Sleeve_L/R (shoulder→elbow),
Pants_L/R, Sash (dark red `(0.45,0.05,0.05)`), Belt (gold), Pad_L/R (gold),
Boot_L/R, Topknot (crown), KnotTip, Headpiece (gold torus at the forehead),
Crest (gold), Ponytail (Bezier tube, draped OUTSIDE the tunic to the waist),
Eye_L/R (amber `(0.85,0.62,0.15)`, emissive).

Face measurements used for placement (Katara/08 head-material verts):
head bbox x ±0.105, y −0.075 (face front) … +0.185, z 1.313–1.735; eye bones
`(±0.03, −0.029, 1.612)`; nose `(0, −0.068, 1.584)`. Eye spheres sit at the
face surface (bone + forward×0.030, r 0.022) — an earlier placement was
buried ~0.03 inside the head and read as goggles; fixed in v3.

## Animation set (per character, 15 states)

All @30 fps. Humanoid retargeting: UAL/Quaternius rig and CMU rig retarget
onto the Bip01 character Avatars. No Animation Events yet (combat wires
them later). No transitions in v1 — combat drives states by name
(`Animator.Play(state)`).

| State | Clip (take) | Source | Length | Notes |
|---|---|---|---|---|
| Idle | `Idle_Loop` | UAL1 | 76f / 2.53s | loop |
| Run | `Sprint_Loop` | UAL1 | 21f / 0.70s | loop; state speed Katara **1.06**, Azula **0.96** (CHAR_DATA run_speed) |
| Punch_Jab | `Punch_Jab` | UAL1 | 27f / 0.90s | |
| Punch_Cross | `Punch_Cross` | UAL1 | 31f / 1.03s | |
| Kick | `Kick_Roundhouse` | CMU 135_07 | 43f / 1.43s | root motion baked into pose (in-place); hip travels ~1.9 m in the raw take |
| Cast | `Cast_TwoArm` | Authored | 18f / 0.60s | two-arm bending cast, wind-up f6 → release f11 |
| Hit | `Hit_Chest` | UAL1 | 11f / 0.37s | alt: UAL2 `Hit_Knockback` 26f |
| KO | `Death01` | UAL1 | 73f / 2.43s | |
| Jump_Start | `Jump_Start` | UAL1 | 41f / 1.37s | |
| Jump_Loop | `Jump_Loop` | UAL1 | 76f / 2.53s | loop |
| Jump_Land | `Jump_Land` | UAL1 | 39f / 1.30s | |
| Dodge | `Roll` | UAL1 | 45f / 1.50s | alt: UAL2 `Slide_Start/Loop/Exit` |
| Block | `Sword_Block` | UAL2 | 38f / 1.27s | swordless forearm guard; reads as guard |
| **RegenChannel** | `RegenChannel` | Authored | 75f / 2.50s | **NEW (owner, 2026-11-15 16:08).** Hands together at chest, bowed head, slow breathing (spine 3°→8°, root −0.01→−0.03). Loop, first frame == last frame. Clearly distinct from Idle and Cast. Combat plays it while regenerating; any hit cancels it. |
| **UltimateCast** | `UltimateCast` | Authored | 60f / 2.00s | **NEW (owner, 2026-11-15 16:08).** f1 neutral → f14 crouch wind-up (root −0.14, arms back, spine −12°) → f30 apex (arms wide overhead ±58°, head back −16°, chest open, root +0.04) → f42 release (arms thrust forward 88°, spine +18°, root lunge −0.18) → f60 settle. Katara: water ultimate; Azula: blue-fire ultimate. |

**Why authored / CMU clips exist:** no free library had a two-arm bending
cast, a channeling stance, a dramatic ultimate cast, or a kick — UAL has no
kick at all, and its `Spell_Simple_Shoot` / `Spell_Simple_Idle_Loop` are
near-identical subtle one-hand gestures (hand positions differ ~0.04 m).
Authored clips were posed in Blender on the Quaternius rig and
render-verified (cast release, regen hands-at-chest, ultimate apex/release).
Kick verified numerically: left foot peak z 0.39 m / shin 0.73 m at frame
~16 of 43; feet grounded at frames 1 and 43.

**Rig bone maps** (explicit, in `CharacterSetup.cs`): UAL
(pelvis→Hips, spine_01→Spine, spine_02→Chest, spine_03→UpperChest,
neck_01→Neck, Head→Head, clavicle→Shoulder, upperarm/lowerarm/hand,
thigh/calf/foot/ball, fingers 01–03); Bip01 (`Bip01 Pelvis`→Hips,
`Bip01 Spine/Spine1/Spine2`, `Bip01 Neck/Head`, `Bip01 LEye/REye`,
clavicles, UpperArm/Forearm/Hand, Finger0–4 + 01/02 segments,
Thigh/Calf/Foot/Toe0 — note Bip01 thighs parent to Spine, not Pelvis);
CMU kick (hip→Hips, abdomen→Spine, chest→Chest, neck→Neck, head→Head,
r/lCollar→Shoulder, r/lShldr→UpperArm, r/lForeArm→LowerArm, r/lHand→Hand,
r/lThigh→UpperLeg, r/lShin→LowerLeg, r/lFoot→Foot).

## Materials (URP/Lit, exact CHAR_DATA colors)

CHAR_DATA (`fighter.gd`): Katara top `(0.15,0.35,0.65)`, pants
`(0.10,0.16,0.35)`, skin `(0.86,0.66,0.50)`, hair `(0.10,0.07,0.05)`,
trim `(0.90,0.90,0.85)`, accent `(0.35,0.75,0.95)`, accent2
`(0.85,0.95,1.0)`. Azula top `(0.62,0.08,0.08)`, pants `(0.12,0.08,0.10)`,
skin `(0.90,0.72,0.55)`, hair `(0.05,0.04,0.04)`, trim/gold
`(0.85,0.65,0.20)`, accent/blue-fire `(0.30,0.45,1.0)`, accent2
`(0.65,0.80,1.0)`.

- `Katara_Body/Head` — `_BaseMap` f008 color TGA + `_BumpMap` f008 normal
  TGA, `_BaseColor` white (texture carries the skin). `Katara_Opacity` —
  f008 opacity color TGA (scan hair strands).
- `Katara_Robe` top blue, `Katara_Fur` `(0.62,0.66,0.72)`, `Katara_Trim`
  trim white, `Katara_Bracer` accent, `Katara_Bead` accent2 + emission
  0.35, `Katara_Boot` pants blue, `Katara_Hair` hair, `Katara_Eye` blue +
  emission 0.5.
- `Azula_Body/Head` — f002 color/normal TGAs. `Azula_Opacity` — f002
  opacity TGA. `Azula_Tunic` top red, `Azula_Pants` pants, `Azula_Sash`
  `(0.45,0.05,0.05)`, `Azula_Gold` gold, metallic 0.7, smoothness 0.6
  (belt, pads, headpiece, crest), `Azula_Hair` hair, `Azula_Eye` amber +
  emission 0.5.
- All: smoothness 0.35, metallic 0 unless noted. Textures: Android
  **ASTC 6×6** (`textureFormat: 50`), max 1024, mipmaps on; normal maps
  `textureType: 1`, sRGB off. TGAs are 2048² (opacity f002 is 1024²).
- Specular TGAs are copied for reference but NOT referenced by any
  material (URP Lit metallic workflow doesn't use them).
- FBX texture paths are mangled absolute paths from the author's machine
  (`/home/hatch/workspace/tools/rocketbox/temp/...`) — the importer will
  NOT find them by filename search; the materials above reference the
  copied TGAs by GUID, which is the correct assignment path.

## Verification done (2026-11-15, file inspection + Blender — no Editor run)

- **FBX re-import (Blender 4.2):** Katara 19 meshes, body 4,516 verts /
  80 bone groups, robe 16 groups, sleeves 3 groups; Azula 19 meshes,
  body 4,662 verts. Animation FBXs: UAL1 43 takes, UAL2 43 takes,
  Authored 3 actions (Cast_TwoArm 18f, RegenChannel 75f, UltimateCast
  60f), Kick 1 action 43f — frame ranges in the clip table above.
- **Renders (Cycles CPU, textures relinked):** Katara front/back,
  Azula front/back — v4 final. Verified: braid + 3 blue beads visible
  down Katara's back, fur collar, belt, bracers, loopies; Azula topknot
  on the crown, gold headpiece at the forehead, ponytail down the back,
  shoulder pads, sash, belt, amber eyes, serious mature faces on both.
- **Fix history (v1→v4):** v2 — sleeves shoulder→elbow (bracers were
  hidden), open robe/tunic hems (flat bottom cap showed as a triangle
  between the legs), beads moved onto the braid surface, eyes smaller +
  more proud of the face, headpiece raised to the forehead. v3 — topknot
  moved from the forehead (Head-bone tail is at the face, not the crown)
  to the crown; eyes shrunk again (goggle look); loopies shrunk/raised.
  v4 — braid + ponytail swung OUT to drape outside the robe/tunic
  (measured: braid max-Y 0.20 was inside the robe back 0.28; now
  outside, beads visible). The "Azula black face" in early renders was
  a lighting/resolution artifact — the face close-up shows the real
  face; no opacity-material defect.
- **Unity assembly verified by inspection:** 52 unique deterministic
  GUIDs (md5 of asset path); all 24 YAML files parse
  (2 controllers × 17 docs, 2 prefabs × 3 docs, 20 materials); zero
  dangling GUID references; every `_BaseMap`/`_BumpMap` resolves to a
  copied TGA; prefab → controller GUID links resolve; controller state
  lists (15 each), Run speeds (1.06 / 0.96) and default Idle verified;
  `CharacterSetup.cs` braces/parens balanced, all referenced methods
  defined; FBX copies byte-identical to the verified builds
  (Katara sha256 `66a47224…`, Azula `54dcc6e1…`). No files touched
  outside `Assets/Characters/`; nothing pushed (no git repo in the
  project dir — the repo is managed by the coordinator).

## What could NOT be verified (needs the Editor / CI / device)

1. **Humanoid Avatar mapping** — the `.meta` files request Humanoid
   (`animationType: 2`); the postprocessor enforces it + explicit
   CMU/UAL maps, and the setup script rebuilds Avatars via
   `AvatarBuilder`. Whether Unity accepts the maps (esp. Bip01 finger
   segment names `Finger01/11/21/31/41`) is Editor-only. The setup
   script logs unmapped bones as warnings and fails loudly if the
   Avatar is invalid.
2. **Prefab / controller import** — the hand-authored `.prefab` /
   `.controller` YAML carries `{fileID: 0}` placeholders for FBX
   sub-assets (meshes, clips, Avatars) whose fileIDs cannot be
   predicted by hand. They parse, but they are NOT complete until
   `Elementis > Characters > Setup Characters` runs in the Editor and
   re-saves them with real references. **Run that menu item first.**
3. **On-device look** — Blender renders are the only visual proof so
   far. The characters have not been seen in the Unity scene or on a
   phone. The CI build will catch compile/import errors; the game
   builder should screenshot the fighters in the arena before calling
   the look done.
4. **`CharacterSetup.cs` was not compiled** — written by file
   inspection only (Editor runs are forbidden on this VM). The CI build
   will catch compile errors; the header comment says so.
5. **Facial bones unused** — Rocketbox `_facial.fbx` variants and the
   face rig (jaw, eyelids, brows) exist but are not wired in v1; the
   face is the static scan + emissive eye spheres. Lip-sync / face
   animation is out of scope for V1.
6. **Minor look nits (accepted):** at close range the eye spheres still
   read slightly "goggly" and the loopies are plain spheres; at fight
   distance they read as blue/amber eyes and Water Tribe loopies. The
   opacity material renders as opaque dark hair (no alpha cutout) —
   acceptable, since the modeled hair covers it; alpha cutout is a
   possible later improvement.

## Setup steps for the coordinator (Editor)

1. Open the project, let the import finish. If any FBX imported as
   Generic, the postprocessor will re-import it as Humanoid on the
   next import (or delete its `.meta` and re-import).
2. Run **`Elementis > Characters > Setup Characters`** — builds the
   Avatars, controllers (15 states, correct clips), and prefabs
   (model + Animator + materials). Check the console for the DONE line
   and no MISSING-clip errors.
3. Run **`Elementis > Characters > Verify Characters`** — prints each
   prefab's Animator/Avatar/controller, any NULL material slots, and
   every state with its clip name + length.
4. Place `Katara.prefab` / `Azula.prefab` in the arena scene, screenshot
   from the fight camera (behind view), and compare against the v1.2
   fight-scene contract before calling the look done.

## Files on disk (workbench, for rebuilds)

- `~/workspace/tools/elementis-characters/` — `scripts/build_character.py`
  (v4), `scripts/author_clips.py`, `scripts/trim_kick.py`,
  `scripts/render_tex.py`, `scripts/diag_head.py`, `scripts/diag_braid.py`,
  `scripts/assemble_unity.py` (this assembly), `out/Katara.fbx`,
  `out/Azula.fbx`, `out/Elementis_Authored.fbx`, `out/Kick_Roundhouse.fbx`,
  `katara.blend`, `azula.blend`, `r4-katara-back.png`,
  `r4-azula-back.png`, `r3-*-front/back.png` (final front views).
- `~/workspace/tools/rocketbox/` — Rocketbox sparse clone (MIT).
- `~/workspace/tools/quaternius-mirror/` — Quaternius CC0 mirror (441 MB).
- Blender 4.2.0 at `~/workspace/tools/blender/` (headless; Cycles CPU
  renders work, Workbench does not — `libEGL.so.1` missing).
