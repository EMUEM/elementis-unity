# Elementis Arena — ARENA-NOTES

Enclosed dusk temple arena (radius ~40 m, matches `ARENA_RADIUS 40.0`
in `fighter.gd`), built to the approved v1.2 fight-scene visual
contract: stone walls + pillars, 4 elemental banners, 3-tier stands
with a crowd, rock cover, floor puddles, warm dusk light, fog.
Prefab: `Arena.prefab` (root `Arena`, 894 YAML objects).

## What was built

- Floor: 90x90 m stone-flag plane + worn dirt centre circle (r=12 m).
- Perimeter: 8 octagon walls (apothem 40 m, 9 m high) + temple gate
  on the far wall (two 14 m towers + lintel, silhouette above the wall).
- Pillars: 8, stone shaft + base + gold cap, radius 33 m (angles chosen
  to clear the stands).
- Banners (quad 3x6 m, double-sided): Water far-W, Fire far-E, Earth
  side-W, Air side-E, each with an iron pole and a generated emblem
  texture (swirl / flame / mountain / wind spiral).
- Stands: 3 solid tiers per side (tops 1.1 / 2.2 / 3.3 m), z 2..30 m.
- Crowd: 66 seated figures (capsule body + sphere head, 3 muted
  tunic colours + skin), background only — deliberately simple, not
  hero-shaped, and never blocky-boxy.
- Rocks: 6 large sunk boulders (non-uniform spheres) + 8 rubble pieces,
  all outside the centre fight circle and clear of both spawns.
- Puddles: 5 glossy discs (smoothness 0.95, metallic 0.2).
- Sky: 180 m Unlit gradient dome (indigo top -> orange horizon).
- Braziers: 4 (iron post + bowl + emissive flame); only the 2 far ones
  carry a real PointLight.

## Lighting / atmosphere

- SunLight: 1 directional, low dusk sun from the west-southwest,
  colour (1.0, 0.55, 0.30), intensity 2.5, hard shadows on.
- PointLights: 2 (far braziers), orange (1.0, 0.5, 0.15), intensity 60,
  range 20 m, no shadows. Total real-time lights: 3 (URP asset allows
  4 additional lights per object). The other 2 braziers are emissive-only.
- Fog + ambient: applied at runtime by `ArenaEnvironment.cs` on the
  prefab root — linear fog 55->200 m, colour (0.42, 0.30, 0.36);
  trilight ambient (sky 0.45/0.32/0.38, equator 0.30/0.24/0.28,
  ground 0.16/0.12/0.10); also assigns SunLight as the scene sun.
- Palette is deliberately muted earthy stone so saturated elemental
  VFX pop (LoL readability lesson).

## Materials / textures

All materials are URP/Lit except `M_Sky` (URP/Unlit). All textures are
procedurally generated with PIL (no downloads, no paid assets):

| Material | Texture / colour | Notes |
|---|---|---|
| M_StoneFloor | stone_floor.png 1024 | tiling 9x9, smooth 0.35 |
| M_StoneWall | stone_wall.png 512 | tiling 8x2, smooth 0.45 |
| M_StoneBlock | stone_wall.png 512 | tiling 2x2 (towers/pillars/stands) |
| M_Dirt | dirt.png 512 | tiling 3x3, smooth 0.15 |
| M_Rock | rock.png 256 | smooth 0.2 |
| M_Gold | (0.85,0.65,0.20) | metallic 0.9, smooth 0.55 |
| M_Iron | (0.14,0.13,0.12) | metallic 0.75, smooth 0.45 |
| M_Flame | emissive (2.6,0.9,0.25) | _EMISSION keyword set |
| M_Puddle | (0.10,0.22,0.26) | metallic 0.2, smooth 0.95 |
| M_BannerWater | banner_water.png | swirl, double-sided |
| M_BannerFire | banner_fire.png | flame, double-sided |
| M_BannerEarth | banner_earth.png | mountain, double-sided |
| M_BannerAir | banner_air.png | wind spiral, double-sided |
| M_Sky | sky_gradient.png 64x512 | Unlit, double-sided |
| M_CrowdA/B/C | muted blue-grey / brown / green-grey | smooth 0.7 |
| M_CrowdSkin | (0.72,0.55,0.42) | smooth 0.6 |

Texture import: all PNGs are power-of-two, mipmaps on for tiled stone/
dirt/rock, off for banners/sky. Android platform override on every
texture: `textureFormat 50` (ASTC_6x6) — set per-texture via the
TextureImporter, as Unity 6 has no global Android default for this.

## Fight-camera proxy + frustum check (computed, not eyeballed)

- `FightCameraProxy` at position (0, 3.0, -13.0) — 7 m behind SpawnA
  (0, 0, -6), 3 m up — looking at (0, 1.6, 6) (SpawnB).
- Rotation quaternion (x 0.0000, y 0.9993, z 0.0368, w -0.0000),
  euler hint (x -4.21, y 180.00, z 0.00). FOV 60 vertical,
  aspect 16:9, near 0.3, far 200 — same as the Main scene camera.
- Check method: for each target point, camera-space x/y/depth against
  half-height = depth*tan(30deg) and half-width = half-height*16/9;
  banners tested at all 4 corners. Results:

- [PASS] Banner Water (far wall W): worst corner margin +38.6%
- [PASS] Banner Fire (far wall E): worst corner margin +38.6%
- [PASS] Banner Earth (side wall W): worst corner margin +6.3%
- [PASS] Banner Air (side wall E): worst corner margin +6.3%
- [PASS] Gate lintel top (0,15,39.2): depth 51.2m, margin +26.2%
- [PASS] Gate tower W top (-10,14,39.2): depth 51.3m, margin +28.1%
- [PASS] Gate tower E top (10,14,39.2): depth 51.3m, margin +28.1%
- [PASS] Far wall top centre (0,9,40): depth 52.4m, margin +37.9%
- [PASS] Far pillar top (0,9.6,33): depth 45.4m, margin +34.9%
- [PASS] Far pillar top (16.5,9.6,28.6): depth 41.0m, margin +33.3%
- [PASS] Far pillar top (-16.5,9.6,28.6): depth 41.0m, margin +33.3%
- [PASS] Opponent spawn B (0,1.8,6): depth 19.0m, margin +55.2%
- [PASS] Rock (14,2,24): depth 36.9m, margin +50.4%
- [PASS] Rock (-24,2.6,18): depth 30.9m, margin +24.4%
- [PARTIAL] Stand row x=-30.5 (top y=1.1): enters frame at z>=17.0m; crowd in frame 10/22
- [PARTIAL] Stand row x=-33.5 (top y=2.2): enters frame at z>=20.0m; crowd in frame 8/22
- [PARTIAL] Stand row x=-36.5 (top y=3.3): enters frame at z>=23.0m; crowd in frame 6/22
- [PARTIAL] Stand row x=+30.5 (top y=1.1): enters frame at z>=17.0m; crowd in frame 10/22
- [PARTIAL] Stand row x=+33.5 (top y=2.2): enters frame at z>=20.0m; crowd in frame 8/22
- [PARTIAL] Stand row x=+36.5 (top y=3.3): enters frame at z>=23.0m; crowd in frame 6/22
- [PASS] Crowd total: 24/66 figures inside frustum

Reading of the stand/crowd rows: the near ends of the side stands sit
beside/behind the fight camera, so they are out of frame by design
(as in the reference image, where the stands flank the camera). The
far portion of every row — the part the fight camera actually sees —
is inside the frustum; the per-row z-thresholds above are where each
row enters the frame. All 4 banners, the gate, the far wall and both
fighter spawns are fully in frame.

## Integration

- Instantiate `Arena.prefab` at the origin in the fight scene;
  `SpawnA`/`SpawnB` mark the fighter spawns (A faces +Z, B faces -Z).
- The prefab does NOT touch Main.unity, ProjectSettings, ProofBuild.cs
  or the workflow — the coordinator wires it into the scene.
- Crowd uses shared meshes/materials (SRP-batcher friendly); no
  per-figure scripts. Static batching / lightmap baking is a
  coordinator step, not set in the prefab.

## What was NOT verified (honest limits)

- NO Unity Editor run (per task: license is fragile, project shared).
  The prefab YAML, materials and .meta files were verified by file
  inspection + reference-integrity checks only; the CI build is the
  first real compile/import test.
- NO visual check: no offscreen render exists on this VM. The look
  (banner emblems, dusk grade, fog density) is unverified until a
  build runs on a device or in the Editor.
- Unverified assumptions to confirm in the Editor, first import:
  (a) Android textureFormat 50 really lands as ASTC_6x6;
  (b) sky-gradient orientation on the sphere (flip vertically if the
  orange band is at the top instead of the horizon);
  (c) light intensities assume URP physical light units (sun 2.5,
  braziers 60 cd) — tune on device if the scene is too dark/bright;
  (d) the `sun:` reference on ArenaEnvironment resolves to SunLight.

Prefab guid: `342e2a6663b84923838b15ba2135e034` — script guid: `f66519ea45c343ce8d5e66e7a83876f4`.
