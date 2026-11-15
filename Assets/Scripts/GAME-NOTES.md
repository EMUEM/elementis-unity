# Elementis — GAME-NOTES (Phase 2: combat core)

**Project:** `elementis-proof` (Unity 6000.0.85f2, URP). **Status:** combat code
written 2026-11-15, verified by file inspection only — NO Editor run, NO
playtest on this VM (license is fragile; the project is shared). The CI
build is the first real compile/import test. Do not claim a playtest happened.

**Numbers source:** `~/workspace/apps/element-arena/scripts/fighter.gd`
CHAR_DATA, verbatim. Where Phase 2 had to choose a number, the choice is
listed below with its reasoning.

## What was built

| File (`Assets/Scripts/`) | What it is |
|---|---|
| `FighterController.cs` | One state machine per fighter (Idle, Run, Punch, Kick, Skill1–4, Regen, Ultimate, Block, Hit, KO, Dash, WallRun, Dodge, Surf). Damage in exactly one place (`TakeDamage`). Hits at ~0.4 of the action, never at button press. Always faces the opponent. |
| `MovementKit.cs` | Locomotion: sprint 1.6× + trail, wall-run, double jump = backflip, super-jump, dive pose, hard landing = dust + crouch, slide dodge with i-frames, Katara waterspout surf. CharacterController-based. |
| `FighterData.cs` + `Data/KataraData.asset` / `Data/AzulaData.asset` | Per-hero ScriptableObject data (CHAR_DATA). New hero = new asset. |
| `FighterDataLibrary.cs` | Same numbers in code — fallback if a `.asset` reference is missing at runtime. |
| `SkillProjectile.cs` | Travelling skill visuals (Water Blast / Fireball / Tidal Wave): moves the VFX root, hits once, destroys itself. |
| `BotAI.cs` | Bot state machine (Approach / Attack / Block / Retreat / Channel / Feint) × Easy / Medium / Hard presets. Uses skills, blocks, combos, regen when safe, ultimate when ready. |
| `ReelCamera.cs` | Tight third-person behind the player, distance/FOV grow with fighter separation, shake on ultimate. On the Main Camera. |
| `RoundManager.cs` | Best-of-3, 60 s timer, 3-2-1 countdown, KO / timeout / draw, round-win tally. |
| `ElementisInput.cs` + `ElementisInputActions.inputactions` | The single input surface (map `Fighting`, 13 actions). All `UnityEngine.InputSystem` API in the combat code lives in this file only. |
| `PlayerInputBridge.cs` | Wires `ElementisInput` events to the player fighter; also exposes `Ui*` direct-call methods for touch buttons. |
| `FightBootstrap.cs` | Instantiates arena + fighters at runtime from prefab references in Main.unity; wires opponents, camera, rounds. Awake spawns the lobby-backdrop pair only (deferred start); `BeginMatch(playerHeroId, botHeroId, difficulty)` swaps heroes + starts the match (G1/G2 fixed 2026-11-15). |
| `ElementisFx.cs` | VFX library (`FxLibrary` object): `Spawn(name, pos, rot)` for the 15 prefabs. |

**Scene (`Assets/Scenes/Main.unity`):** Main Camera repositioned to the
arena's FightCameraProxy pose (0, 3, −13, looking at SpawnB) with
`ReelCamera` + `AudioListener`; the old Directional Light is REMOVED (the
arena brings its own sun/fog via `ArenaEnvironment.cs` — no duplicate
lights). `Fight` object holds `ElementisInput` + `FightBootstrap` +
`RoundManager`; `FxLibrary` holds the 15 VFX prefabs; `Audio` holds
`AudioManagerSetup` with all 20 SFX clips.

**Design decision — no prefab instances in the scene:** Main.unity holds
prefab *references* on `FightBootstrap`, which `Instantiate()`s them in
`Awake` (arena at origin, Katara at SpawnA (0,0,−6), Azula at SpawnB
(0,0,6)). Hand-authoring `PrefabInstance` YAML is fragile; runtime
instantiation is verifiable C# instead. The observable result is identical.

**Build (`Assets/Editor/ProofBuild.cs`):** `BuildAndroid()` now calls
`CharacterSetup.SetupCharacters()` FIRST (it is idempotent — delete +
rebuild of avatar/controller/prefab), then `VerifyCharactersOrThrow()`,
which fails the build loudly if either fighter prefab lacks a valid human
Avatar or AnimatorController. All pre-existing lines are preserved
unchanged (`-customBuildPath` handling, `Debug.Log("Build succeeded!")`,
`PROOF_BUILD_DONE`, method name, workflow untouched).

**Packages:** `com.unity.inputsystem: 1.14.0` added to
`Packages/manifest.json` (resolves from the Unity registry on the CI runner).

## Public API for the UI agent

### Hero selection + deferred start (G1/G2 — fixed 2026-11-15)
`FightBootstrap.Awake` spawns the arena + the default pair (Katara =
player, Azula = bot) as the **live lobby backdrop only** — fighters
Frozen, `RoundManager` Idle, no rounds run. The match does NOT begin
until the UI calls:

`FightBootstrap.BeginMatch(playerHeroId, botHeroId, difficulty)` →
`bool` — hero ids `"katara"` / `"azula"` (`FighterData.characterId`).
The chosen hero spawns as the player (SpawnA), the other as the bot
(SpawnB); the difficulty is applied via `BotAI.SetDifficulty`; the
match then starts with the 3-2-1 countdown (`RoundManager.StartMatch`
is called inside `BeginMatch` — the UI must NOT call it separately).
Returns `false` (starts nothing) on an unknown hero id, the same hero
on both sides, or a missing `RoundManager`.
- If the spawned pair already matches the pick (default pick, or a
  rematch), the fighters are kept and the match just starts.
- Otherwise both fighter GameObjects are destroyed and re-spawned
  from the chosen prefabs/data through the single `SpawnFighter` path
  (same numbers/AI/camera wiring as the original spawn). **After a
  swap the old `FighterController` references go stale:** the UI must
  refresh `PlayerFighter` / `BotFighter` and re-bind the HUD (which
  re-subscribes fighter events and re-points the touch controls —
  `FightHud.Bind` / `TouchControls.RebindPlayer` handle this).
- Menus run UNFROZEN (`Time.timeScale = 1`): the old `timeScale = 0`
  freeze workaround is removed. Pause still uses `timeScale = 0`.

### Input actions (map `Fighting`, asset `ElementisInputActions`)
`Move` (Vector2) · `Punch` · `Kick` · `Skill1` · `Skill2` · `Skill3` ·
`Skill4` · `Block` (hold) · `Dash` · `Jump` · `Regen` (hold) · `Ultimate` ·
`Surf` (Katara-only traversal; Godot had a dedicated SURF button + `C` key).
Keyboard: WASD/arrows move, J punch, K kick, 1–4 skills, L block,
LeftShift dash, Space jump, R regen, U ultimate, C surf. Gamepad: left stick,
X/Y punch/kick, D-pad skills, LB block, RB regen, RT ultimate, A jump,
Y dash, right-stick click surf.
**Touch binding:** use the Input System's `OnScreenButton` /
`OnScreenStick` components pointed at these actions — joystick finger and
button fingers drive different actions, so move + cast simultaneously
works (the named Godot defect). **Fallback that always works:** a UI
Button can call `PlayerInputBridge.UiPunch()` / `UiKick()` /
`UiSkill(i)` / `UiDash()` / `UiJump()` / `UiUltimate()` /
`UiBlock(held)` / `UiRegen(held)` / `UiMove(Vector2)` directly — same
methods, no Input System involved. **SURF button:** show it only for
Katara, enabled only while sprinting and mana ≥ 20 (Godot `hud.gd` rule);
it calls `PlayerInputBridge.UiSurf()`.
**⚠ REQUIRED before play (coordinator step, ProjectSettings — not done
here): Player Settings → Active Input Handler → Both (or Input System).**
With "Input Manager" selected, Input System actions receive no input.
The direct `Ui*` calls work regardless of that setting.

### Fighter events (subscribe; never read fighter fields from the HUD)
On `FighterController` (player = `FightBootstrap.PlayerFighter`,
bot = `BotFighter`):
- `HealthChanged(health, maxHealth)` — max 350.
- `ManaChanged(mana, maxMana)` — max 100. HP and mana are SEPARATE bars.
- `UltimateChanged(0..1)` — meter fraction; `UltimateReadyChanged(bool)`
  fires when the meter fills/empties. Gate the ultimate button on it.
- `TimingShown(text, seconds, perfect)` — `"PERFECT!"` / `"GOOD"` /
  `"COUNTER!"`. Show the text + time; PERFECT = gold + 0 damage.
- `SkillCast(index)` / `SkillCooldownStarted(index, seconds)` — start the
  radial cooldown. Poll `SkillCooldownRemaining(i)` /
  `SkillCooldownTotal(i)` for the fill.
- `RegenStarted` / `RegenEnded(cancelledByHit)` — channel state.
- `UltimateCast` — the power-showcase moment: show the banner / meter
  flash (the in-world ring + column + flash come from the
  UltimateKatara/UltimateAzula prefabs).
- `Died(fighter)` / `TookHit`.

### Round events (on `RoundManager`)
`RoundStarted(round)` · `CountdownTick(3/2/1/0=FIGHT)` ·
`RoundEnded(winner, reason)` (`reason` = KO / Timeout / Draw; winner null
on a draw) · `MatchEnded(winner)` · `TimerChanged(seconds)` ·
`ScoreChanged(winsA, winsB)`. Poll `Phase`, `RoundNumber`, `TimeRemaining`.

### Skill data (from `FighterData`, also in the `.asset` files)
| Hero | Skill 1 | Skill 2 | Skill 3 | Skill 4 |
|---|---|---|---|---|
| Katara | Water Blast 18 dmg / 25 mana / 1.2 s | Water Whip 22 / 35 / 2.5 s | Ice Spikes 26 / 30 / 3.0 s | Tidal Wave 16 / 40 / 4.0 s |
| Azula | Fireball 20 / 25 / 1.2 s | Flame Sweep 24 / 35 / 2.5 s | Lightning Strike 30 / 30 / 3.0 s | Fire Spin 20 / 30 / 3.5 s |
Melee: punch 8, punch 2 (chain) 10, kick 13, kick finisher (after punch) 22.
Combo: punch → punch → kick, 0.55 s window; skills cancel punch/kick only
in the late half (progress ≥ 0.45). Basic attack (Punch/Kick) and skills are
separate button clusters by design (Mobile Legends style) — keep them
visually distinct in the touch layout.

## Numbers chosen in Phase 2 (owner's 16:08 additions)
- **Regen channel:** +12 HP/s, +14 mana/s while channeling. Steady, not
  instant: ~29 s to refill HP from 0, ~7 s for mana. Hold `Regen` (or the
  regen button) while standing still. **Cancel rule, exactly as ordered:**
  ANY enemy attack cancels the channel immediately — including a blocked
  hit. Moving, casting, or releasing the button also ends it.
- **Ultimate meter (Tekken-like, 0–100):** +0.6 per damage dealt, +0.4 per
  damage taken, +5 per skill landed. Roughly a full meter per ~100 damage
  dealt + ~60 taken + a few landed skills — about one ultimate per active
  round, two at most. When full, `UltimateReady` gates the ultimate:
  **60 damage** (~17% of 350 HP — high but balanced, never a one-shot),
  4.5 m AoE, knockback 14, 2.0 s UltimateCast animation + VFX + SFX +
  camera shake. Meter is consumed on activation.
- **Passive mana:** +12/s (6/s while blocking), as in fighter.gd. No
  passive HP regen outside the channel.
- **Perfect block:** guard raised ≤ 0.25 s before the hit = 0 damage +
  10 mana refund + gold BlockShimmer + chime. ≤ 0.5 s = GOOD, 15% damage.
  Later guard = 30% damage. Counter (hit mid-cast): ×1.25 damage +
  "COUNTER!". Dash/dodge grant i-frames (no damage at all).
- **Movement:** walk 5.6 × run speed (Katara 1.06, Azula 0.96), sprint
  1.6× after 0.4 s full-tilt, surf 2.2× (Katara only: 20 mana to start +
  8/s, max 2.5 s), dash 15 m/s for 0.18 s (1.0 s cd), dodge 12 m/s
  (slide 14 m/s out of a sprint, 1.2 s cd), jump 7.5, super-jump 11
  (sprint + jump), double jump 10.5 (backflip). Wall-run max 1.6 s.
  Arena radius 40 m.

## What is verified vs not (honest status)
**Verified here (file inspection + scripts, no Editor):**
- All 13 `.cs` files: brace/paren balance, every cross-file method call
  resolves to a real method, no Unity API outside the known-good set.
- CHAR_DATA numbers: `.asset` files == `FighterDataLibrary.cs` ==
  `fighter.gd` (checked by script, all 4 skills × 2 heroes + melee +
  speeds).
- Every VFX prefab name and SFX name referenced in code exists on disk.
- `Main.unity` parses: unique fileIDs, every `{fileID, guid}` reference
  resolves to a real file + matching `.meta` GUID, SceneRoots list the 4
  root transforms, Directional Light removed, no duplicate lights.
- `ProofBuild.cs`: all pre-existing lines byte-identical (checked by
  script); `CharacterSetup.SetupCharacters()` is idempotent by inspection
  (delete + rebuild in `BuildAvatar` / `BuildController` / `BuildPrefab`;
  missing clips → early return + error log, which the new gate turns into
  a build failure).
- `.inputactions`: valid JSON, 13 actions, 35 bindings, unique IDs.
**NOT verified (cannot be, without the Editor / CI / device):**
- Nothing compiled: no C# compiler exists on this VM. The CI build is the
  first compile check. If it reports errors, they are in the newest files
  first (`FighterController.cs`, `MovementKit.cs`, `ElementisInput.cs`).
- No playtest: no fight has run. Combat feel, bot difficulty, camera
  framing, VFX/SFX sync, and the multi-touch (move + cast) test all need
  the on-device pass. The bot's reaction timers and the ultimate-meter
  tuning are first guesses — tune from the device, not from this doc.
- `com.unity.inputsystem 1.14.0` has not resolved yet (no package cache on
  this VM). The CI runner resolves it from the Unity registry; if that
  fails, the fallback is in `ElementisInput.cs`'s header comment (delete
  that one file + its scene component; touch UI uses `Ui*` direct calls).
- The `.inputactions` asset's `InputActionImporter` `.meta` and the
  `.asset` files' `NativeFormatImporter` `.meta` are written in the
  canonical shapes but have never been imported. If Unity regenerates
  either `.meta` on import, that is expected and harmless; if the
  `.inputactions` reference in the scene is null at runtime,
  `ElementisInput` builds the programmatic fallback asset (same 12
  actions, keyboard bindings).

## Known gaps / follow-ups (not blockers for the CI build)
1. **Active Input Handler → Both** (coordinator, ProjectSettings) — see above.
2. **Surf visual:** there is no Waterspout VFX prefab. Surf currently
   shows the sprint trail + water sounds. `FighterController.surfFxPrefab`
   is the hook: assign a spout prefab when the VFX agent makes one.
3. **Backflip visual:** double jump sets `flipT` (0.45 s) and plays
   `Jump_Loop`, but no visual flip rotation is applied — the fighter
   models have no single "visual root" to rotate (meshes are direct
   children of the prefab root). Needs a backflip clip or a verified
   visual-root approach in the animation pass.
4. **No hit-stop:** fighter.gd briefly drops `Engine.time_scale` on hits;
   the Unity port deliberately does NOT touch global time scale (a global
   freeze is risky mid-round). Feel comes from knockback + Hit state +
   sparks instead. Revisit only with a device test.
5. **Bot "bait" reads are probabilistic**, not true reads of the player's
   telegraphs. Hard's dodge-on-cast is a timed guess.
6. **Draw rounds replay** as the next round number (no score change).
7. **Skill 3 sound** plays at the eruption (0.35 s / 0.25 s telegraph),
   not at cast start — a deliberate sync change from fighter.gd, where it
   played at the hit point of the cast animation.
