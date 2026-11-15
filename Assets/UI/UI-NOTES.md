# Elementis — UI-NOTES (Phase 2b: UI + flow)

**Project:** `elementis-proof` (Unity 6000.0.85f2, URP). **Status:** UI + flow
built 2026-11-15, verified by file inspection + `verify-ui.py` (171 checks,
0 failures) — NO Editor run, NO compile, NO playtest on this VM (license
is fragile; the project is shared). The CI build is the first compile/
import test. Do not claim a playtest happened.

**Files:** `Assets/UI/Scripts/` — `UIFlowManager.cs` (flow + menus),
`FightHud.cs` (HUD), `TouchControls.cs` (joystick + fight buttons),
`LobbyShowcase.cs` (lobby idle), `UiKit.cs` (runtime UI construction),
`UiButtonPress.cs` (no-Input-System fallback). Scene wiring:
`Assets/Scenes/Main.unity` gains ONE root GameObject, `UI`, holding
`UIFlowManager` — the combat scene objects are untouched. All UI is
built at runtime in C# (no hand-authored UGUI scene YAML, no prefabs);
the single canvas is Screen Space – Overlay, `CanvasScaler` reference
1920×1080, match 0.5, inside a `SafeArea` container fitted to
`Screen.safeArea` (camera cutout + gesture bar). Font is the built-in
Arial (`Resources.GetBuiltinResource<Font>("Arial.ttf")`) — no
TextMeshPro in the project, no downloaded fonts, free only.

**Reference space:** all sizes/positions below are px in the 1920×1080
landscape reference space (dp at 1080p; the CanvasScaler scales them).
Anchor notation: BL = bottom-left (0,0), BR = bottom-right (1,0),
TC = top-centre (0.5,1), BC = bottom-centre (0.5,0), C = centre (0.5,0.5).
Positions are the control CENTRE as an offset from its anchor point.

## 1. Layout tables (measured; `verify-ui.py` recomputes every rect)

### Fight controls (`TouchControls.cs` — all raycast targets, no overlaps)

| Control | Anchor | Centre (x, y) | Size | Notes |
|---|---|---|---|---|
| Joystick base | BL | (190, 170) | 260×260 | Knob 110; `movementRange` 110 ref px × `CanvasScaler.scaleFactor` |
| PUNCH (basic) | BR | (-520, 95) | 150×150 | Separate cluster; warm red-orange circle + gold ring |
| KICK | BR | (-680, 95) | 104×104 | Basic-attack cluster with PUNCH |
| BLOCK (hold) | BR | (-556, 292) | 96×96 | Hold: down = guard, up = release |
| REGEN (hold) | BR | (-668, 292) | 96×96 | Hold: channel; any hit cancels (fighter rule) |
| Skill 1 | BR | (-218, 68) | 96×96 | Real name + mana cost as label |
| Skill 2 | BR | (-218, 180) | 96×96 | |
| Skill 3 | BR | (-330, 68) | 96×96 | |
| Skill 4 | BR | (-330, 180) | 96×96 | |
| ULTIMATE | BR | (-86, 106) | 132×132 | Biggest skill button; gold ring halo (+18 px) |
| DASH | BR | (-330, 292) | 88×88 | |
| JUMP | BR | (-218, 292) | 88×88 | |
| SURF | BR | (-444, 292) | 88×88 | Katara only; lights only while sprinting + mana ≥ 20 |

Spacing (edge-to-edge, verified): skill cluster internal gap **16 px**;
skill top row → DASH/JUMP/SURF row **20 px**; DASH↔SURF **26 px**,
SURF↔BLOCK **20 px**, JUMP↔DASH **24 px**; BLOCK↔REGEN **16 px**;
PUNCH cluster → skill cluster **67 px**; joystick → own-health bar
**320 px** clear. PUNCH (150) > ULTIMATE (132) > skills (96) — the
basic attack is unmistakably the biggest button, in its own cluster,
a different colour and shape from the skills (Mobile Legends style).

Skill labels come from the player's `FighterData` at bind time
(`shortName` + mana cost): Katara BLAST 25 / WHIP 35 / SPIKES 30 /
WAVE 40. Cooldown = radial dark sweep + seconds text; a skill greys
out while on cooldown **or** unaffordable (mana < cost).

### Fight HUD (`FightHud.cs` — minimal, Infinite Souls style)

| Element | Anchor | Centre (x, y) | Size | Notes |
|---|---|---|---|---|
| Enemy name | TC | (0, −20) | 400×28 | Bot display name, 24 pt |
| Enemy health | TC | (0, −48) | 760×26 | Red fill; `HealthChanged` |
| Round dots | TC | −90/−62, +62/+90 @ −96 | 18×18 | Left pair = you (gold), right = bot (red) |
| ROUND label | TC | (0, −124) | 300×34 | Gold, 30 pt bold |
| Match sublabel | TC | (0, −158) | 600×24 | "KATARA vs AZULA • MEDIUM" |
| Timer | TR | (−90, −48) | 140×48 | `TimerChanged`, `m:ss`, 40 pt bold |
| PAUSE | TL | (83, −48) | 110×48 | Opens pause overlay |
| SND ON/OFF | TL | (207, −48) | 110×48 | Toggles `AudioManager.Muted` (persisted) |
| Own health | BC | (0, 212) | 640×30 | White fill + "350 / 350" dark text |
| Mana | BC | (0, 182) | 640×10 | Thin blue; separate from HP |
| ULTIMATE meter | BC | (0, 156) | 640×12 | Thin gold; `UltimateChanged` 0..1 |
| MANA / ULT labels | BC | (−356, 182 / 156) | 64×16 | Right-aligned, left of the bars |
| Timing text | C | (0, +150) | 800×90 | PERFECT! (gold) / GOOD / COUNTER!, 1.0 s |
| Regen text | C | (0, +80) | 900×40 | "REGENERATING — any hit cancels it" |
| ULTIMATE! banner | C | (0, +260) | 900×110 | On `UltimateCast`, 1.2 s |
| Countdown | C | (0, 0) | 900×260 | Gold 3-2-1 / FIGHT!, 200 pt |
| ROUND banner | C | (0, +340) | 1000×130 | Gold, 96 pt, 1.2 s |
| K.O. banner | C | (0, 0) | 1100×150 | K.O.! / TIME UP / DRAW, 2.4 s |
| Floating enemy bar | screen-projected | above bot head +2.2 m | 220×14 | Red fill + bot name; hidden behind camera |

The bottom bar stack was raised (own 212 / mana 182 / ult 156) so the
bars clear the KICK button's top edge by 3 px and the BLOCK/REGEN row
by 17 px — the verifier checks the stack against every fight control.

### Lobby (`UIFlowManager.cs`)

| Element | Anchor | Centre (x, y) | Size |
|---|---|---|---|
| ELEMENTIS title | TC | (0, −90) | 900×160, gold 120 pt bold + dark offset shadow |
| Subtitle "WATER vs FIRE • BEST OF 3" | TC | (0, −200) | 700×40, 28 pt |
| START | BC | (0, 130) | 420×110, gold, 44 pt |
| Footer "KATARA vs AZULA • v1" | BC | (0, 36) | 500×26, 18 pt |

ONLY a Start option. The showcase character is the live Katara
fighter already spawned in the arena, seen through the transparent
lobby canvas from the fight camera (`LobbyShowcase.cs`: her Animator
runs on unscaled time while the menu is frozen, plus a WaterWhip
flourish at her hands every 4.5 s, also unscaled).

### Setup (`UIFlowManager.cs`)

| Element | Anchor | Centre (x, y) | Size |
|---|---|---|---|
| Title "CHOOSE YOUR FIGHTER" | TC | (0, −48) | 800×56, gold 40 pt |
| Katara card | TC | (−300, −230) | 560×300 |
| Azula card | TC | (+300, −230) | 560×300 |
| "BOT DIFFICULTY" label | TC | (−380, −420) | 200×32 |
| EASY / MEDIUM / HARD | TC | (−120 / +100 / +320, −420) | 200×64 each |
| FIGHT | TC | (+120, −495) | 420×84, gold |
| BACK | TC | (−260, −495) | 200×84 |

Each card shows the hero's name, element, and all 4 real skills
(`shortName — dmg • mana • cooldown`, from `FighterDataLibrary` —
the same numbers as the `.asset` files). Selected card gets a gold
border + "YOU" tag. Backdrop is 72% dark so the arena stays faintly
visible.

### VS screen (`UIFlowManager.cs`)

| Element | Anchor | Centre (x, y) | Size |
|---|---|---|---|
| Player name / element | C | (−440, +100 / +30) | 560×80 / 400×38 |
| Bot name / element | C | (+440, +100 / +30) | 560×80 / 400×38 |
| VS (gold) | C | (0, +60) | 300×180, 140 pt |
| BEST OF 3 | C | (0, −60) | 400×44, gold 32 pt |
| BOT — <difficulty> | C | (0, −110) | 400×34 |
| FIGHT | C | (0, −220) | 420×100, gold |
| "FIGHT STARTS IN n" | C | (0, −300) | 400×30 |
| BACK | TL | (150, −50) | 180×64 |

Auto-advances after **3.0 s** (unscaled timer; BACK cancels it by
leaving the screen). Plates show the picked hero as YOU and the other
hero as BOT — and the fight now spawns exactly that pairing
(`FightBootstrap.BeginMatch`; G1 fixed 2026-11-15).

### Pause / match-end

Pause (centred 640×440 box): PAUSED + RESUME / RESTART MATCH /
SOUND: ON/OFF / QUIT TO LOBBY (360×72, 84 px pitch). Match-end
(centred 720×460 box): VICTORY (gold) / DEFEAT (red) 72 pt, score
line ("AZULA WINS • 1 — 2"), REMATCH / LOBBY (360×84).

## 2. Multi-touch design (the hard requirement)

**Mechanism (verified against Input System 1.14.0 package source,
`OnScreenControl.cs` / `OnScreen.md`, downloaded for this check):**
`OnScreenControl.controlPath` names a *device control*, not an action.
All 13 UI controls feed ONE virtual Gamepad (one device per layout —
every path below uses the `Gamepad` layout, so they share a single
virtual device), and the `Fighting` map already binds every one of
those gamepad controls (`ElementisInputActions.inputactions`, checked
by `verify-ui.py`):

| UI control | `controlPath` | Action bound to it |
|---|---|---|
| Joystick (`OnScreenStick`) | `<Gamepad>/leftStick` | Move |
| PUNCH | `<Gamepad>/buttonWest` | Punch |
| KICK | `<Gamepad>/buttonEast` | Kick |
| Skill 1–4 | `<Gamepad>/dpad/up/right/down/left` | Skill1–4 |
| BLOCK (hold) | `<Gamepad>/leftShoulder` | Block |
| DASH | `<Gamepad>/buttonNorth` | Dash |
| JUMP | `<Gamepad>/buttonSouth` | Jump |
| REGEN (hold) | `<Gamepad>/rightShoulder` | Regen |
| ULTIMATE | `<Gamepad>/rightTrigger` | Ultimate |
| SURF | `<Gamepad>/rightStickButton` | Surf |

**Why concurrent touches work:** Unity's `EventSystem`
(`InputSystemUIInputModule`, default UI actions auto-assigned in
`OnEnable` — verified in the 1.14.0 source) tracks each finger as a
separate pointer (`pointerId`). `OnScreenStick` handles the joystick
finger's drag; each `OnScreenButton` handles its own finger's
down/up (`SendValueToControl(1/0)`). Different fingers drive
different controls on the same virtual gamepad, so move + cast,
move + punch, and joystick + two buttons all work at the same time.
Nothing in this UI serialises touches — that was the Godot defect,
and this construction removes the mechanism that caused it.

**Fallback (no Input System package — the GAME-NOTES.md fallback
world):** the same buttons get `UiButtonPress` components calling
`PlayerInputBridge.Ui*()` directly, and the joystick gets a direct
drag handler calling `UiMove()` (`#else` branches in
`TouchControls.cs`). Same visuals, same layout. The two paths are
never both live on one button (that would double-fire). A runtime
check (`TouchControls.CheckControlsResolved`) logs an error in logcat
if any `OnScreenButton` fails to resolve its control, so a dead
button is visible instead of silent.

**Pre-play gate (coordinator step, ProjectSettings — NOT done here):**
Player Settings → Active Input Handler → **Both** (or Input System).
With "Input Manager" selected, Input System actions receive no input.
The direct `Ui*` fallback path works regardless of that setting.

## 3. Flow (how it works with the API that exists)

`FightBootstrap.Awake` spawns the arena + the default pair (Katara =
player, Azula = bot) as the live lobby backdrop only — fighters
Frozen, `RoundManager` Idle, no rounds run. Menus run UNFROZEN
(`Time.timeScale = 1`); the old `timeScale = 0` freeze workaround is
gone (G2 fixed 2026-11-15). Every fight start goes through
`BeginFight()`: it calls `FightBootstrap.BeginMatch(pickedHero,
otherHero, difficulty)`, which spawns the picked hero as the player,
the other as the bot, applies the difficulty via
`BotAI.SetDifficulty()`, and starts a FRESH match (resets
wins/round/timer, re-runs the 3-2-1 countdown); `BeginFight` then
refreshes its player/bot references and binds the HUD. A hero swap
respawns the fighters, so the HUD re-subscribes fighter events and
the touch controls re-point at the new player bridge
(`FightHud.Bind` / `TouchControls.RebindPlayer`). Countdown/round/KO/
match-end are driven purely by the GAME-NOTES events: `CountdownTick`
(3/2/1/0=FIGHT), `RoundStarted`, `RoundEnded` (KO/Timeout/Draw),
`MatchEnded`, `TimerChanged`, `ScoreChanged`. Pause is `timeScale = 0`
+ overlay (RoundManager has no pause API — Gap G3). Quit-to-lobby
freezes the fighters and returns to the lobby; the next Start re-runs
`BeginMatch`.

## 4. API gaps (exact — no event or method was invented)

- **G1 — FIXED 2026-11-15 (hero-swap API landed).**
  `FightBootstrap.BeginMatch(playerHeroId, botHeroId, difficulty)`
  starts a match with the chosen heroes (ids `"katara"` / `"azula"`);
  a swap respawns the fighters, and the UI refreshes its references
  + re-binds the HUD after the call.
- **G2 — FIXED 2026-11-15 (deferred start landed).**
  `FightBootstrap.Awake` no longer calls `RoundManager.StartMatch()`;
  the match starts only in `BeginMatch`. The `timeScale = 0` freeze
  workaround is removed; menus run at `timeScale = 1`.
- **G3 — No pause/stop API on `RoundManager`** (no `Pause()` /
  `StopMatch()`). Pause = `timeScale = 0`; quit-to-lobby =
  `Frozen = true` on both fighters + back to the live lobby. Works.
- **Not gaps:** difficulty (applied via `BotAI.SetDifficulty`, exists);
  sprint state for the SURF rule (`FighterController.Kit.Sprinting`,
  public); all fighter/round events used exist verbatim in the combat
  sources (checked by `verify-ui.py`, §5).

## 5. Verification (honest status)

**Verified here (`python3 Assets/UI/verify-ui.py`, 171 checks, 0 failures):**
- Brace/paren/bracket balance in all 6 new `.cs` files.
- Every combat-API member the UI touches exists as a public member in
  the combat sources (no invented event/method names).
- All 13 `OnScreen` control paths are real gamepad bindings in
  `ElementisInputActions.inputactions`.
- Layout: every control rect recomputed from the parsed layout
  constants in the 1920×1080 reference space — no overlaps within any
  group of simultaneously visible controls, all rects in-frame,
  PUNCH↔skill-cluster gap 67 px, skill internal gap 16 px. (The check
  caught 3 real layout bugs during this build: the bottom bar stack
  overlapping KICK, the round-label/match-sublabel 3 px overlap, and
  the VS name plates overlapping the VS title — all fixed in the
  constants and re-checked. It also caught a `RegenCenterY` constant
  name collision between `TouchControls` and `FightHud` — renamed.)
- `.meta` GUIDs unique project-wide; `Main.unity` references the
  `UIFlowManager` GUID; scene fileIDs unique (24 blocks); SceneRoots
  4 → 5; combat scene objects untouched.
- Forbidden areas untouched: `.github/workflows/*`, `ProjectSettings/*`,
  `ProofBuild.cs` (mtimes predate this work).
- No `Camera.main` / `FindObjectOfType` / TextMeshPro in UI code;
  only WGL-safe non-ASCII in strings (— •).
- Flow invariants in source: pause freezes `timeScale`, menus run
  unfrozen, `BeginFight` starts via `FightBootstrap.BeginMatch`
  (which owns `StartMatch` + difficulty via `SetDifficulty`), mute
  via `AudioManager.Muted`, countdown driven by `CountdownTick`,
  rematch path present.

**NOT verified (cannot be, without the Editor / CI / device):**
- Nothing compiled: no C# compiler exists on this VM. The CI build is
  the first compile check. If it reports errors, they are in the
  newest files first (`Assets/UI/Scripts/*`).
- No playtest: no fight has run with this UI. The multi-touch
  behaviour (move + cast simultaneously) is established by
  construction (per-pointer EventSystem + one virtual gamepad) and by
  the binding check above — but the ON-DEVICE test in §6 is still the
  proof. Do not call multi-touch "verified" until T1–T3 pass on the
  phone.
- The `OnScreenButton` → virtual-gamepad → action link depends on
  the Active Input Handler setting (coordinator step) and on the
  Input System package resolving on the CI runner (1.14.0, per
  `Packages/manifest.json`). If either fails, the buttons are dead
  and the logcat check in §2 will say so; the `Ui*` fallback path is
  the documented recovery.
- Lobby showcase framing (Katara's size/position in the fight-camera
  view) is untested — the camera pose is the Phase 2 fight pose
  (0, 3, −13 looking at SpawnB); Katara at SpawnA should read
  centre-frame, but confirm on device.

## 6. On-device test steps (explicit — run on the 14T, landscape)

Setup: install the build, launch, confirm the lobby shows the gold
ELEMENTIS title over the arena with Katara standing centre-frame
doing her idle + water flourish every ~4.5 s. Keep logcat open.

- **T1 — Move + cast (THE test):** hold the joystick full-tilt with
  the left thumb (Katara runs), tap SKILL 1 with the right thumb.
  PASS if she keeps moving AND the Water Blast fires. Repeat for
  skills 2, 3, 4 (WHIP / SPIKES / WAVE). FAIL if movement stops, the
  skill doesn't fire, or the joystick drops.
- **T2 — Move + punch:** hold the joystick, tap PUNCH repeatedly.
  PASS if punches land while she moves. Then punch → punch → kick
  while moving (combo window 0.55 s) — PASS if the chain plays.
- **T3 — Joystick + two buttons:** hold the joystick, hold BLOCK with
  a second finger, tap SKILL 2 with a third. PASS if she moves, the
  guard stays up, and the skill fires — all three at once.
- **T4 — Hold buttons:** hold REGEN — PASS if "REGENERATING" shows
  and HP/mana climb; release — it stops. Hold BLOCK — PASS if the
  guard stays up until release. Have the bot hit her mid-channel —
  PASS if the channel cancels immediately.
- **T5 — Ultimate gating:** the ULT button and gold meter start dim;
  fight until the meter fills — PASS if the button lights up (gold
  ring pulses) exactly when full, and tapping it fires the ultimate
  (banner "ULTIMATE!", meter empties). Tapping it early must do
  nothing (fighter-gated).
- **T6 — SURF (Katara only):** the SURF button is hidden for Azula
  (not applicable in v1 — see G1) and dim for Katara until she is
  sprinting (full-tilt joystick ~0.4 s) with mana ≥ 20. PASS if it
  lights only then, and tapping it surfs.
- **T7 — Layout:** every button is reachable by thumb without
  stretching, nothing overlaps, no label is cut off, in both
  landscape rotations (cutout left / cutout right). Check the safe
  area: no control sits under the camera hole or the gesture bar.
- **T8 — Flow:** START → pick Azula + HARD → VS screen (plates,
  BEST OF 3, FIGHT) — PASS if it auto-advances after ~3 s; BACK
  returns to setup. Countdown shows 3-2-1-FIGHT! every round;
  ROUND 1/2/3 banners show; KO shows "K.O.!"; match end shows
  VICTORY/DEFEAT + score; REMATCH starts a fresh match (score 0–0);
  PAUSE freezes the fight and RESUME / RESTART / QUIT TO LOBBY all
  work; mute toggle flips SND ON/OFF and survives a restart.
- **T9 — Difficulty:** play one match on each of EASY / MEDIUM /
  HARD — PASS if the bot's reactions, skill use and blocking are
  visibly different (Easy: slow, few skills; Hard: fast, combos).
- **T10 — Hero pick (G1 FIXED 2026-11-15):** pick Azula on the setup
  screen. EXPECTED (current build): the VS screen shows AZULA as
  YOU, and the fight plays as Azula (player) vs Katara (bot) — Azula's
  skills on the touch buttons, SURF button hidden (Katara-only), HUD
  bars tracking Azula. Pick Katara and the fight is Katara vs Azula
  as before. No warning is logged for either pick.
