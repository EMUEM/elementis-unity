# Elementis — VFX & SFX Library (Unity, URP 17.0.3)

**Project:** `elementis-proof` (Unity 6000.0.85f2, URP). **Location:** `Assets/VFX/` (prefabs, materials, textures) and `Assets/Audio/SFX/` (WAVs + `AudioManager.cs`).
**Design sources:** `fighter.gd` CHAR_DATA (damage/costs/cooldowns), `DESIGN.md` effects bar (layered particles, glow, readable), the approved v1.2 fight-scene image (translucent glowing arcing water whips, blue fire, saturated effects on muted stone).
**Status:** authored as Unity YAML + synthesized WAVs, verified by file inspection only (see Verification at the bottom). No Editor run was done — the license is fragile on this VM and the project is shared. The CI build is the next check.

## Palette (readability: saturated glow on muted stone)

| Element | Colours (particle Start Color) |
|---|---|
| Katara water | cyan `(0.45, 0.85, 1.0)`, deep `(0.15, 0.55, 0.95)`, foam white `(0.90, 0.97, 1.0)` |
| Katara ice | pale `(0.75, 0.92, 1.0)`, deep `(0.55, 0.80, 1.0)` |
| Azula blue fire | core near-white blue `(0.80, 0.92, 1.0)`, mid `(0.30, 0.55, 1.0)`, edge `(0.15, 0.30, 0.90)` |
| Lightning | white-blue `(0.90, 0.95, 1.0)`, blue `(0.60, 0.80, 1.0)` |
| Feedback | PERFECT gold `(1.0, 0.85, 0.45)`, hit sparks gold-white `(1.0, 0.90, 0.60)`, dust warm white `(0.95, 0.94, 0.88)` |
| Regen | mint `(0.50, 0.95, 0.78)`, pale `(0.75, 1.0, 0.88)` — deliberately distinct from water cyan |

Materials are all **URP Particles/Unlit** (`Universal Render Pipeline/Particles/Unlit`, shader GUID `0406db5a14f94604a8c57ccfbc9f3b46` from the cached URP 17.0.3 package), `_BaseColor` white — hue lives in the particle colours so one material serves many effects. Textures are procedural (generated with numpy/PIL, no downloads): `SoftCircle`, `Streak`, `Ring`, `Shard` (128 px max, ASTC 6x6 on Android, no mipmaps, clamp wrap).

## Prefabs (`Assets/VFX/Prefabs/`, one GameObject, layered ParticleSystems)

| Prefab | What it is | Layers | Duration | Max particles | Looping? |
|---|---|---|---|---|---|
| `WaterBlast` | Ranged water projectile (Katara skill 1) | core + trail + foam | 5 s (projectile life) | 130 | **Yes** — combat moves + destroys it |
| `WaterWhip` | Close arc slash, twin-whip feel (Katara skill 2) | streak slash + foam + mist, trails on | 0.5 s | 52 | No — self-destroys |
| `IceSpikes` | Telegraph + ground eruption (Katara skill 3) | 0.35 s ground ring, then spikes + frost + mist | 1.2 s | 92 | No — self-destroys |
| `TidalWave` | Travelling ground wave (Katara skill 4) | wall + foam crest + spray | 5 s (travel life) | 125 | **Yes** — combat moves + destroys it |
| `Fireball` | Blue-flame projectile (Azula skill 1) | core + flame + embers | 5 s (projectile life) | 125 | **Yes** — combat moves + destroys it |
| `FlameSweep` | Close ground arc, blue fire (Azula skill 2) | arc flames + ground fire + embers | 0.6 s | 68 | No — self-destroys |
| `LightningStrike` | 0.25 s ground telegraph, then bolt + flash (Azula skill 3) | telegraph ring, bolt column, flash, sparks, scorch ring | 0.7 s | 109 | No — self-destroys |
| `FireSpin` | Close AoE swirling blue fire (Azula skill 4) | ring flames + swirl embers + core flash | 0.6 s | 64 | No — self-destroys |
| `HitSparks` | Generic impact burst | streak sparks + flash + blue shards | 0.35 s | 35 | No — self-destroys |
| `DustBurst` | White dust for hard landings | dust + debris | 0.7 s | 34 | No — self-destroys |
| `SprintTrail` | Pale speed trail while sprinting | streak trail + puffs, world space | 5 s | 32 | **Yes** — combat enables/disables or destroys |
| `BlockShimmer` | Gold shield flash for PERFECT blocks | expanding shell + sparkles + ring | 0.4 s | 27 | No — self-destroys |
| `RegenAura` | Calm channelling aura for the regen stance | rising mint motes + ground ring motes + soft glow | 5 s | 65 | **Yes** — combat **must Destroy it the instant the channel is cancelled** (any enemy attack, move, or cast). No fade-out by design. |
| `UltimateKatara` | Massive water eruption + wave (Katara ultimate) | telegraph ring → energy column → eruption → wave ring → foam → flash → mist | 2.4 s | 241 | No — self-destroys |
| `UltimateAzula` | Huge blue-fire explosion + pillar (Azula ultimate) | telegraph ring → fire pillar → explosion → shockwave ring → ember storm → flash → heat linger | 2.4 s | 261 | No — self-destroys |

One-shot prefabs self-clean: their longest ParticleSystem has `stopAction: Destroy` (destroys the root GameObject when the last particle dies). Looping prefabs never self-destroy — the combat system owns their lifetime.
Worst case if every prefab were alive at once: 1,460 max particles. In a real fight only 2–4 effects are alive at once (typically < 400 particles), which is mobile-sane for the Xiaomi 14T target.
**Quality tiers:** combat/quality code can scale any effect down without touching the prefabs: `GetComponentInChildren<ParticleSystem>().emission.rateOverTimeMultiplier = 0.5` (Low) / `0.75` (Med) / `1.0` (High). Never scale the ultimates below 0.75 — they are the showcase moment.

## Power-showcase indicator (ultimates)

Both ultimate prefabs carry the same in-world indicator sequence so the ultimate reads as the biggest moment in the fight:
1. **0.00–0.50 s:** wide ground ring (radius 2.5 m) pulsing at the caster's feet — the telegraph.
2. **0.30 s:** rising energy column (water / blue fire) erupts from the ground.
3. **0.50 s:** bright white flash + the main eruption/explosion.
The combat system should ALSO drive a UI power-showcase indicator at cast time (banner / meter flash / screen shake) — the prefab covers the in-world part only. Ultimate damage is the combat system's job; these prefabs are visuals + sound only.

## SFX (`Assets/Audio/SFX/`, all synthesized with numpy → 16-bit WAV, 44.1 kHz mono, no downloads, no copyrighted audio)

| Sound | Duration | What it is / when it plays |
|---|---|---|
| `water_whoosh` | 0.60 s | Katara skill cast whoosh (Blast / Whip / Wave) |
| `water_splash` | 0.70 s | Water impact / projectile hit |
| `ice_crack` | 0.50 s | Ice Spikes eruption |
| `wave_rumble` | 1.20 s | Tidal Wave travelling rumble (replay/loop while the wave travels) |
| `fire_whoosh` | 0.50 s | Azula skill cast whoosh (Fireball / Sweep / Spin) |
| `blue_fire_crackle` | 0.90 s | Blue-fire bed for Flame Sweep / Fire Spin |
| `lightning_zap` | 0.60 s | Lightning Strike bolt |
| `impact_thud` | 0.35 s | Melee / skill hit impact |
| `block_clink` | 0.30 s | Blocked hit (GOOD / late block) |
| `ko_boom` | 1.10 s | KO / round-end boom |
| `countdown_beep` | 0.18 s | Round countdown tick (3-2-1) |
| `round_win_chime` | 1.10 s | Round-win chime |
| `ui_tap` | 0.08 s | UI button tap |
| `swing_whoosh` | 0.35 s | Punch / kick swing |
| `punch_hit` | 0.25 s | Punch / kick landing thud |
| `dash_whoosh` | 0.40 s | Dash / dodge whoosh |
| `jump_whoosh` | 0.30 s | Jump / double-jump whoosh |
| `perfect_chime` | 0.50 s | PERFECT block chime |
| `ultimate_katara` | 2.40 s | Katara ultimate: 0.8 s rising swell, then massive water-eruption impact |
| `ultimate_azula` | 2.40 s | Azula ultimate: 0.8 s rising swell, then huge blue-fire detonation |

`AudioManager.cs` (in `Assets/Audio/SFX/`) is the front-end: static `AudioManager.Play(name, volume, pitch)`, `AudioManager.Muted` (persisted in PlayerPrefs `elementis_muted`), `AudioManager.HasClip(name)`. Wire it with the `AudioManagerSetup` component (drag the 20 WAVs into its Clips array in the fight scene). `Play()` is safe before wiring — it logs one warning and no-ops, never throws. 8-source voice pool, 2D (no spatial blend).

## Spawn-point convention

| Name | Meaning |
|---|---|
| `Hand` | Caster's hand/orb position, facing the opponent. Projectiles spawn here and travel forward. |
| `SelfCenter` | Caster position + `(0, 1.0–1.4, 0)` (chest height). Close-range arcs, blocks, hit feedback. |
| `SelfGround` | Caster position + `(0, 0.05, 0)` (feet). Ground effects, dust, ultimates, regen aura. |
| `GroundTarget` | Opponent's ground position **locked at cast time**. Telegraph rings + eruptions spawn here (Ice Spikes, Lightning Strike). |
| `Forward` | Caster position + forward × 1.2 m, y ≈ 0.9. Tidal Wave spawns here and travels along forward. |
| `Target` | Opponent's chest position at hit time (impact feedback). |

Prefab roots are authored facing +Z; rotate the instantiated root to face the opponent (or the travel direction) before/after spawning. Projectile prefabs (`WaterBlast`, `Fireball`) are visuals only — the combat system moves the root and destroys it on hit or timeout. `TidalWave` is the same: combat moves the root forward and destroys it on hit/timeout.

## EVENT CONTRACT — what the combat system calls

Damage / cost / cooldown are from `fighter.gd` CHAR_DATA (unchanged here — this library is visuals + sound only).

| Event (fighter.gd hook) | Prefab | Sound | Spawn point | Notes |
|---|---|---|---|---|
| Katara skill 1 — Water Blast (18 dmg, 25 en, 1.2 s cd) | `WaterBlast` | `water_whoosh` (cast), `water_splash` (hit) | `Hand` → travels to `Target` | Looping; destroy on hit/timeout. Hit also spawns `HitSparks` at `Target`. |
| Katara skill 2 — Water Whip (22 dmg, 35 en, 2.5 s cd) | `WaterWhip` | `water_whoosh` | `SelfCenter`, facing opponent | One-shot, 0.5 s. |
| Katara skill 3 — Ice Spikes (26 dmg, 30 en, 3.0 s cd) | `IceSpikes` | `ice_crack` | `GroundTarget` | 0.35 s telegraph ring is inside the prefab; hit applies at 0.35 s. |
| Katara skill 4 — Tidal Wave (16 dmg, 40 en, 4.0 s cd) | `TidalWave` | `water_whoosh` (cast), `wave_rumble` (while travelling) | `Forward`, travels forward | Looping; destroy on hit/timeout. |
| Azula skill 1 — Fireball (20 dmg, 25 en, 1.2 s cd) | `Fireball` | `fire_whoosh` (cast), `impact_thud` (hit) | `Hand` → travels to `Target` | Looping; destroy on hit/timeout. Hit also spawns `HitSparks` at `Target`. |
| Azula skill 2 — Flame Sweep (24 dmg, 35 en, 2.5 s cd) | `FlameSweep` | `fire_whoosh` + `blue_fire_crackle` | `SelfCenter`, facing opponent | One-shot, 0.6 s. |
| Azula skill 3 — Lightning Strike (30 dmg, 30 en, 3.0 s cd) | `LightningStrike` | `lightning_zap` | `GroundTarget` | 0.25 s telegraph is inside the prefab; bolt lands at 0.25 s. |
| Azula skill 4 — Fire Spin (20 dmg, 30 en, 3.5 s cd) | `FireSpin` | `fire_whoosh` + `blue_fire_crackle` | `SelfCenter` | One-shot, 0.6 s, close AoE. |
| **Katara ULTIMATE** (meter full) | `UltimateKatara` | `ultimate_katara` | `SelfGround` | One-shot, 2.4 s. In-world indicator: ring 0–0.5 s → column at 0.3 s → flash at 0.5 s. Combat adds UI banner + shake. |
| **Azula ULTIMATE** (meter full) | `UltimateAzula` | `ultimate_azula` | `SelfGround` | One-shot, 2.4 s. Same indicator sequence as Katara's. |
| Regen channel start | `RegenAura` | — (silent by design; quiet channel) | `SelfGround` | Looping. **Destroy instantly on cancel** — any enemy attack, movement, or cast ends it. |
| Punch / kick swing | — | `swing_whoosh` | — | At strike start. |
| Punch / kick lands | `HitSparks` | `punch_hit` | `Target` | Also on skill hits (with the skill's own hit sound). |
| Fighter takes a hit | `HitSparks` | `impact_thud` | `SelfCenter` | |
| Block — GOOD (guard ≤ 0.5 s) | — | `block_clink` | — | No prefab; small sound only. |
| Block — PERFECT (guard ≤ 0.25 s) | `BlockShimmer` | `perfect_chime` | `SelfCenter` | Gold flash; no damage, no stagger. |
| Dash / dodge | — | `dash_whoosh` | — | |
| Sprinting | `SprintTrail` | — | `SelfGround` | Looping; emit only while sprinting. |
| Jump / double jump | — | `jump_whoosh` | — | |
| Hard landing (fall speed > 8) | `DustBurst` | `impact_thud` (quiet) | `SelfGround` | Soft landing: `DustBurst` only, no sound. |
| KO / death | `HitSparks` (×3, staggered 0.1 s) | `ko_boom` | `SelfCenter` | |
| Round countdown (3-2-1) | — | `countdown_beep` | — | One beep per tick. |
| Round win | — | `round_win_chime` | — | |
| UI button | — | `ui_tap` | — | |

Godot SFX-name mapping (for the migrating combat code — `fighter.gd` calls `splash`/`fire`/`ice`/`bolt`/`hit`/`block`/`perfect`/`swing`/`punch`/`kick`/`dash`/`jump`/`land`): `splash` → `water_splash`, `fire` → `fire_whoosh`, `ice` → `ice_crack`, `bolt` → `lightning_zap`, `hit` → `impact_thud`, `block` → `block_clink`, `perfect` → `perfect_chime`, `swing` → `swing_whoosh`, `punch`/`kick` → `punch_hit`, `dash` → `dash_whoosh`, `jump` → `jump_whoosh`, `land` → `impact_thud`, `sprint_whoosh` → `dash_whoosh`, `surf` → `water_whoosh` + `wave_rumble`.

## Verification (honest status)

**Checked here (file inspection, no Editor):**
- All 15 `.prefab` files parse as YAML; each has 1 GameObject + 1 Transform + N ParticleSystem (class 198) + N ParticleSystemRenderer (class 199) components; every renderer references a material GUID that exists in `Assets/VFX/Materials/`.
- All 5 `.mat` files reference the real URP Particles/Unlit shader GUID from the cached URP 17.0.3 package, with matching `_BaseMap` texture GUIDs.
- All 20 WAVs: 1 channel, 16-bit, 44.1 kHz, durations as listed, non-silent (RMS > 0.01), peak < 0.99 (no clipping).
- All `.meta` GUIDs unique; every file under `Assets/VFX/` and `Assets/Audio/SFX/` has a `.meta`.
- `AudioManager.cs` compiles by inspection (no Unity API outside `UnityEngine`); it is a plain static class + one MonoBehaviour, no Editor code.
- Nothing outside `Assets/VFX/` and `Assets/Audio/SFX/` was touched; nothing was pushed.

**NOT verified here (cannot be, without the Editor):**
- No in-Editor playback: particle look, timing, colours against the real arena, and the telegraph→bolt sequencing have not been seen running. The CI build will catch import/compile errors; the look still needs an on-device check against the v1.2 fight-scene image.
- No runtime test of `AudioManager` wiring (clip names resolve at runtime from `AudioManagerSetup.Clips`).
- Particle counts are budgets from the authored values, not measured frame times on a phone.
