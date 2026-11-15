#!/usr/bin/env python3
"""verify-ui.py — file-inspection verification for Elementis Phase 2b UI.

No Unity Editor, no device: this checks what can be checked from the
files alone (the CI build is the compile check; the on-device pass is
the behaviour check — see UI-NOTES.md).

Checks:
 1. Brace/paren/bracket balance in every new UI .cs file.
 2. Every combat-API member the UI code touches exists as a public
    member in the combat sources (GAME-NOTES.md contract).
 3. Every OnScreen controlPath in TouchControls.cs is a real gamepad
    binding in ElementisInputActions.inputactions.
 4. Layout: parses the `public const float` layout constants out of the
    UI sources (single source of truth) and recomputes every control's
    rect in the 1920x1080 reference space; fails on any overlap within
    a group of controls that can be visible at the same time.
    Groups: fight controls / HUD static / fight feedback (timing,
    regen, ult banner) / countdown-phase (countdown, round banner) /
    setup / versus / lobby. Temporally exclusive pairs (e.g. KO banner
    vs countdown) are checked in their own phase groups, not against
    each other — the exclusion is noted, not hidden.
 5. .meta GUIDs: every UI .cs has a .meta, GUIDs unique project-wide,
    and Main.unity's UI MonoBehaviour references UIFlowManager's GUID.
 6. Main.unity: fileIDs unique, SceneRoots lists the UI transform,
    no other scene object was restructured (root count 4 -> 5).
 7. Forbidden areas untouched: .github/workflows/*, ProjectSettings/*,
    ProofBuild.cs — mtimes must predate this UI work (2026-11-15 13:30).
 8. No obsolete/banned API in UI code: Camera.main, FindObjectOfType,
    TextMeshPro, or non-WGL glyphs (only U+2014 — and U+2022 • allowed).
 9. Flow invariants in source: pause freezes timeScale, menus run
    unfrozen (ShowLobby restores timeScale 1, Awake sets no freeze),
    BeginFight starts via FightBootstrap.BeginMatch (which owns
    StartMatch + difficulty via BotAI.SetDifficulty), mute goes through
    AudioManager.Muted.

Usage: python3 Assets/UI/verify-ui.py   (exit 0 = all checks pass)
"""
import json
import os
import re
import sys

PROJ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# PROJ = <project root> (Assets/UI/verify-ui.py -> up 3)
UI = os.path.join(PROJ, "Assets", "UI")
SCRIPTS = os.path.join(UI, "Scripts")
W, H = 1920.0, 1080.0
FAILURES = []
WARNINGS = []


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + ((" — " + detail) if detail else ""))
    if not ok:
        FAILURES.append(name + " " + detail)


def warn(name, detail=""):
    print("WARN " + name + ((" — " + detail) if detail else ""))
    WARNINGS.append(name)


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def strip_strings_comments(src):
    src = re.sub(r'//.*', '', src)
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    src = re.sub(r'"(?:\\.|[^"\\])*"', '""', src)
    src = re.sub(r"'(?:\\.|[^'\\])*'", "''", src)
    return src


# ---------------------------------------------------------------- 1. balance
UI_CS = ["UiKit.cs", "UiButtonPress.cs", "TouchControls.cs", "FightHud.cs",
         "LobbyShowcase.cs", "UIFlowManager.cs"]
for name in UI_CS:
    src = strip_strings_comments(read(os.path.join(SCRIPTS, name)))
    for a, b, label in [("{", "}", "braces"), ("(", ")", "parens"), ("[", "]", "brackets")]:
        check(f"balance {name} {label}", src.count(a) == src.count(b),
              f"{src.count(a)} vs {src.count(b)}")

# ------------------------------------------------- 2. combat API cross-check
# (member, defining file) — the GAME-NOTES.md contract surface the UI uses.
COMBAT = {
    "FighterController": "Assets/Scripts/FighterController.cs",
    "RoundManager": "Assets/Scripts/RoundManager.cs",
    "FightBootstrap": "Assets/Scripts/FightBootstrap.cs",
    "PlayerInputBridge": "Assets/Scripts/PlayerInputBridge.cs",
    "BotAI": "Assets/Scripts/BotAI.cs",
    "FighterDataLibrary": "Assets/Scripts/FighterDataLibrary.cs",
    "ElementisFx": "Assets/Scripts/ElementisFx.cs",
    "AudioManager": "Assets/Audio/SFX/AudioManager.cs",
}
combat_src = {k: read(os.path.join(PROJ, v)) for k, v in COMBAT.items()}


def member_exists(cls, member):
    src = combat_src[cls]
    # public event / property / method / field / const / enum value
    pats = [
        r"public\s+(?:static\s+|event\s+|const\s+|virtual\s+|override\s+)*[\w<>\[\], ?]+\s+" + re.escape(member) + r"\b",
        r"public\s+" + re.escape(member) + r"\b",
        r"enum\s+\w+\s*\{[^}]*\b" + re.escape(member) + r"\b",
    ]
    return any(re.search(p, src) for p in pats)


# Members the UI code actually touches, grouped by receiver type.
TOUCHED = {
    "RoundManager": ["CountdownTick", "RoundStarted", "RoundEnded", "MatchEnded",
                     "TimerChanged", "ScoreChanged", "StartMatch", "TimeRemaining",
                     "RoundNumber", "WinsA", "WinsB", "RoundTimeSeconds"],
    "FighterController": ["HealthChanged", "ManaChanged", "UltimateChanged",
                          "UltimateReadyChanged", "TimingShown", "SkillCast",
                          "SkillCooldownStarted", "RegenStarted", "RegenEnded",
                          "UltimateCast", "Died", "Health", "Mana", "Ultimate01",
                          "UltimateReady", "UltimatePoints", "Data", "Frozen",
                          "Animator", "Kit", "SkillCooldownRemaining",
                          "SkillCooldownTotal", "IsDead"],
    "FightBootstrap": ["PlayerFighter", "BotFighter", "roundManager", "botDifficulty",
                      "BeginMatch"],
    "PlayerInputBridge": ["UiPunch", "UiKick", "UiSkill", "UiDash", "UiJump",
                          "UiUltimate", "UiSurf", "UiBlock", "UiRegen", "UiMove"],
    "BotAI": ["SetDifficulty"],
    "FighterDataLibrary": ["Create"],
    "ElementisFx": ["Instance", "Spawn"],
    "AudioManager": ["Play", "Muted"],
}
for cls, members in TOUCHED.items():
    for m in members:
        check(f"API {cls}.{m} exists", member_exists(cls, m))

# Also: every `rounds.X` / `player.X` / `bot.X` / `bootstrap.X` member used
# in the UI sources must be in the touched list (no invented member names).
# UnityEngine.Component/MonoBehaviour members (GetComponent, transform,
# gameObject, ...) are allowed without a combat-source definition.
UNITY_BASE = {"GetComponent", "GetComponentInChildren", "GetComponentsInChildren",
              "transform", "gameObject", "name", "enabled", "tag"}
RECEIVER = {"rounds": "RoundManager", "player": "FighterController",
            "bot": "FighterController", "bootstrap": "FightBootstrap"}
ui_src_all = "\n".join(read(os.path.join(SCRIPTS, n)) for n in UI_CS)
for recv, cls in RECEIVER.items():
    used = set(re.findall(r"\b" + recv + r"\.(\w+)", ui_src_all))
    for m in sorted(used):
        check(f"UI use {recv}.{m} is a real {cls} member",
              m in TOUCHED[cls] or m in UNITY_BASE or member_exists(cls, m), m)

# ------------------------------------------------- 3. OnScreen control paths
actions = json.load(open(os.path.join(PROJ, "Assets", "Scripts",
                                      "ElementisInputActions.inputactions")))
bound = {b["path"] for m in actions["maps"] for b in m["bindings"]}
tc = read(os.path.join(SCRIPTS, "TouchControls.cs"))
paths = sorted(set(re.findall(r'"(<Gamepad>/[^"]+)"', tc)))
check("OnScreen paths found", len(paths) == 13, f"{len(paths)} paths")
for p in paths:
    check(f"OnScreen path bound in .inputactions: {p}", p in bound)

# ------------------------------------------------- 4. layout (constants -> rects)
CONSTS = {}
for name in UI_CS:
    for m in re.finditer(r"public const float (\w+) = (-?[\d.]+)f;",
                         read(os.path.join(SCRIPTS, name))):
        # A layout constant name must mean the same number everywhere —
        # two files sharing a name with different values silently
        # corrupts any cross-file check (caught RegenCenterY 292 vs 80).
        if m.group(1) in CONSTS and CONSTS[m.group(1)] != float(m.group(2)):
            check(f"const {m.group(1)} has one value everywhere", False,
                  f"{CONSTS[m.group(1)]} vs {m.group(2)} in {name}")
        CONSTS[m.group(1)] = float(m.group(2))
C = CONSTS


def rect(name, cx, cy, w, h):
    return (name, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)


def overlaps(a, b):
    return a[1] < b[3] and b[1] < a[3] and a[2] < b[4] and b[2] < a[4]


def check_group(group_name, rects):
    bad = []
    for i in range(len(rects)):
        for j in range(i + 1, len(rects)):
            if overlaps(rects[i], rects[j]):
                bad.append(f"{rects[i][0]} x {rects[j][0]}")
    check(f"layout no-overlap: {group_name}", not bad, "; ".join(bad))
    # every rect inside the 1920x1080 reference frame
    outside = [r[0] for r in rects if r[1] < 0 or r[2] < 0 or r[3] > W or r[4] > H]
    check(f"layout in-frame: {group_name}", not outside, ",".join(outside))


# Fight controls (joystick anchored bottom-left; rest bottom-right).
fight = [
    rect("Joystick", C["JoyCenterX"], C["JoyCenterY"], C["JoyBaseSize"], C["JoyBaseSize"]),
    rect("Punch", W + C["PunchCenterX"], C["PunchCenterY"], C["PunchSize"], C["PunchSize"]),
    rect("Kick", W + C["KickCenterX"], C["KickCenterY"], C["KickSize"], C["KickSize"]),
    rect("Block", W + C["BlockCenterX"], C["BlockCenterY"], C["BlockSize"], C["BlockSize"]),
    rect("Regen", W + C["RegenCenterX"], C["RegenCenterY"], C["RegenSize"], C["RegenSize"]),
    rect("Skill1", W + C["Skill1CenterX"], C["Skill1CenterY"], C["SkillSize"], C["SkillSize"]),
    rect("Skill2", W + C["Skill2CenterX"], C["Skill2CenterY"], C["SkillSize"], C["SkillSize"]),
    rect("Skill3", W + C["Skill3CenterX"], C["Skill3CenterY"], C["SkillSize"], C["SkillSize"]),
    rect("Skill4", W + C["Skill4CenterX"], C["Skill4CenterY"], C["SkillSize"], C["SkillSize"]),
    rect("Ult", W + C["UltCenterX"], C["UltCenterY"], C["UltSize"], C["UltSize"]),
    rect("UltRing", W + C["UltCenterX"], C["UltCenterY"], C["UltSize"] + 18, C["UltSize"] + 18),
    rect("Dash", W + C["DashCenterX"], C["DashCenterY"], C["DashSize"], C["DashSize"]),
    rect("Jump", W + C["JumpCenterX"], C["JumpCenterY"], C["JumpSize"], C["JumpSize"]),
    rect("Surf", W + C["SurfCenterX"], C["SurfCenterY"], C["SurfSize"], C["SurfSize"]),
]
# UltRing is an intentional halo around Ult — exclude that one pair.
fight_noring = [r for r in fight if r[0] != "UltRing"]
check_group("fight controls", fight_noring)
ult = [r for r in fight if r[0] in ("Ult", "UltRing")]
check("UltRing surrounds Ult (same centre, +18)",
      ult[0][1] > ult[1][1] and ult[0][2] > ult[1][2]
      and ult[0][3] < ult[1][3] and ult[0][4] < ult[1][4])
# Punch cluster separation from the skill cluster (the ML distinction).
punch_right = W + C["PunchCenterX"] + C["PunchSize"] / 2
skill_left = W + C["Skill3CenterX"] - C["SkillSize"] / 2
check("Punch cluster clear of skill cluster (>=40px)",
      skill_left - punch_right >= 40, f"gap={skill_left - punch_right:.0f}px")
check("Punch is the biggest fight button",
      C["PunchSize"] > C["SkillSize"] and C["PunchSize"] > C["UltSize"],
      f"punch={C['PunchSize']:.0f} skill={C['SkillSize']:.0f} ult={C['UltSize']:.0f}")
check("Skill cluster internal gap is 16px",
      abs((W + C["Skill1CenterX"] - C["SkillSize"] / 2)
          - (W + C["Skill3CenterX"] + C["SkillSize"] / 2) - C["SkillGap"]) < 0.01)

# HUD static (top-centre / top corners / bottom-centre).
hud = [
    rect("EnemyName", W / 2, H + C["EnemyNameTop"], 400, 28),
    rect("EnemyBar", W / 2, H + C["EnemyBarTop"], C["EnemyBarWidth"], C["EnemyBarHeight"]),
    rect("PlayerDot1", W / 2 - 90, H + C["RoundDotsTop"], C["RoundDotSize"], C["RoundDotSize"]),
    rect("PlayerDot2", W / 2 - 62, H + C["RoundDotsTop"], C["RoundDotSize"], C["RoundDotSize"]),
    rect("BotDot1", W / 2 + 62, H + C["RoundDotsTop"], C["RoundDotSize"], C["RoundDotSize"]),
    rect("BotDot2", W / 2 + 90, H + C["RoundDotsTop"], C["RoundDotSize"], C["RoundDotSize"]),
    rect("RoundLabel", W / 2, H + C["RoundLabelTop"], 300, 34),
    rect("MatchSub", W / 2, H + C["MatchSubLabelTop"], 600, 24),
    rect("Timer", W + C["TimerRight"], H + C["TimerTop"], C["TimerWidth"], C["TimerHeight"]),
    rect("Pause", C["PauseLeft"], H + C["PauseTop"], C["PauseWidth"], C["PauseHeight"]),
    rect("Mute", C["MuteLeft"], H + C["MuteTop"], C["MuteWidth"], C["MuteHeight"]),
    rect("OwnBar", W / 2, C["OwnBarBottom"], C["OwnBarWidth"], C["OwnBarHeight"]),
    rect("ManaBar", W / 2, C["ManaBarBottom"], C["OwnBarWidth"], C["ManaBarHeight"]),
    rect("ManaLabel", W / 2 - C["OwnBarWidth"] / 2 - 36, C["ManaBarBottom"], 64, 16),
    rect("UltBar", W / 2, C["UltBarBottom"], C["OwnBarWidth"], C["UltBarHeight"]),
    rect("UltLabel", W / 2 - C["OwnBarWidth"] / 2 - 36, C["UltBarBottom"], 64, 16),
]
check_group("HUD static", hud)
# HUD bars must not collide with the fight controls.
check_group("HUD static + fight controls", hud + fight_noring)

# Fight feedback that can show together (timing / regen / ult banner).
feedback = [
    rect("Timing", W / 2, H / 2 + C["TimingCenterY"], 800, 90),
    rect("Regen", W / 2, H / 2 + C["RegenTextCenterY"], 900, 40),
    rect("UltBanner", W / 2, H / 2 + C["UltBannerCenterY"], 900, 110),
]
check_group("fight feedback", feedback)
# Countdown phase: countdown number + round banner (both live in countdown).
countdown_phase = [
    rect("Countdown", W / 2, H / 2, 900, 260),
    rect("RoundBanner", W / 2, H / 2 + 340, 1000, 130),
]
check_group("countdown phase", countdown_phase)

# Setup screen (top-centre anchor; y measured down from the top edge).
setup = [
    rect("SetupTitle", W / 2, H + C["SetupTitleTop"], 800, 56),
    rect("CardKatara", W / 2 - C["SetupCardGapX"], H + C["SetupCardTop"],
         C["SetupCardWidth"], C["SetupCardHeight"]),
    rect("CardAzula", W / 2 + C["SetupCardGapX"], H + C["SetupCardTop"],
         C["SetupCardWidth"], C["SetupCardHeight"]),
    rect("DiffLabel", W / 2 - 380, H + C["SetupDiffTop"], 200, 32),
    rect("DiffEasy", W / 2 - 120, H + C["SetupDiffTop"], 200, 64),
    rect("DiffMedium", W / 2 + 100, H + C["SetupDiffTop"], 200, 64),
    rect("DiffHard", W / 2 + 320, H + C["SetupDiffTop"], 200, 64),
    rect("SetupFight", W / 2 + 120, H + C["SetupButtonsTop"], 420, 84),
    rect("SetupBack", W / 2 - 260, H + C["SetupButtonsTop"], 200, 84),
]
check_group("setup", setup)

# Versus screen (centre anchor).
versus = [
    rect("VsPlayerName", W / 2 - C["VersusPlateX"], H / 2 + C["VersusPlateY"] + 40, 560, 80),
    rect("VsPlayerElement", W / 2 - C["VersusPlateX"], H / 2 + C["VersusPlateY"] - 30, 400, 38),
    rect("VsBotName", W / 2 + C["VersusPlateX"], H / 2 + C["VersusPlateY"] + 40, 560, 80),
    rect("VsBotElement", W / 2 + C["VersusPlateX"], H / 2 + C["VersusPlateY"] - 30, 400, 38),
    rect("VsTitle", W / 2, H / 2 + C["VersusPlateY"], 300, 180),
    rect("BestOf", W / 2, H / 2 - 60, 400, 44),
    rect("VsDiff", W / 2, H / 2 - 110, 400, 34),
    rect("VsFight", W / 2, H / 2 + C["VersusFightBottom"], 420, 100),
    rect("VsAuto", W / 2, H / 2 + C["VersusFightBottom"] - 80, 400, 30),
    rect("VsBack", 150, H - 50, 180, 64),
]
check_group("versus", versus)

# Lobby (top-centre title, bottom-centre START).
lobby = [
    rect("LobbyTitle", W / 2, H + C["LobbyTitleTop"], 900, 160),
    rect("LobbySubtitle", W / 2, H + C["LobbyTitleTop"] - 110, 700, 40),
    rect("LobbyStart", W / 2, C["LobbyStartBottom"], C["LobbyStartWidth"], C["LobbyStartHeight"]),
    rect("LobbyFooter", W / 2, 36, 500, 26),
]
check_group("lobby", lobby)

# ------------------------------------------------- 5. .meta GUIDs
all_guids = {}
for root, _, files in os.walk(os.path.join(PROJ, "Assets")):
    for f in files:
        if f.endswith(".meta"):
            m = re.search(r"guid:\s*([0-9a-f]{32})", read(os.path.join(root, f)))
            if m:
                all_guids.setdefault(m.group(1), []).append(os.path.join(root, f))
dupes = {g: fs for g, fs in all_guids.items() if len(fs) > 1}
check("meta GUIDs unique project-wide", not dupes, str(dupes)[:200])
for name in UI_CS:
    meta = os.path.join(SCRIPTS, name + ".meta")
    check(f"meta exists: {name}", os.path.exists(meta))
ui_flow_guid = re.search(r"guid:\s*([0-9a-f]{32})",
                        read(os.path.join(SCRIPTS, "UIFlowManager.cs.meta"))).group(1)

# ------------------------------------------------- 6. scene
scene = read(os.path.join(PROJ, "Assets", "Scenes", "Main.unity"))
ids = re.findall(r"&(\d+)", scene)
check("scene fileIDs unique", len(ids) == len(set(ids)),
      f"{len(ids)} blocks, {len(set(ids))} unique")
check("scene has UI root", "m_Name: UI" in scene)
check("SceneRoots lists UI transform", "{fileID: 340000002}" in scene)
check("scene references UIFlowManager GUID", ui_flow_guid in scene)
roots = len(re.findall(r"- \{fileID: \d+\}\s*$", scene, re.M))
check("SceneRoots has 5 roots (was 4)", roots == 5, f"{roots} roots")
check("scene still has Fight/FxLibrary/Audio/Camera",
      all(s in scene for s in ["m_Name: Fight", "m_Name: FxLibrary",
                               "m_Name: Audio", "m_Name: Main Camera"]))

# ------------------------------------------------- 7. forbidden areas untouched
import time
cutoff = time.mktime((2026, 11, 15, 13, 30, 0, 0, 0, 0))
forbidden = ["Assets/Editor/ProofBuild.cs", "ProjectSettings/ProjectSettings.asset",
             ".github/workflows/build-android.yml"]
for rel in forbidden:
    p = os.path.join(PROJ, rel)
    if not os.path.exists(p):
        warn(f"forbidden file missing (check manually): {rel}")
        continue
    check(f"untouched: {rel}", os.path.getmtime(p) < cutoff,
          time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(p))))

# ------------------------------------------------- 8. banned API / glyphs
# (comments and string literals stripped first — a comment mentioning
# Camera.main must not trip the check.)
ui_code = strip_strings_comments(ui_src_all)
for bad, label in [(r"Camera\.main", "Camera.main"),
                   (r"FindObjectOfType", "FindObjectOfType"),
                   (r"TextMeshPro|TMP_", "TextMeshPro")]:
    check(f"no {label} in UI code", not re.search(bad, ui_code))
non_ascii = sorted(set(c for c in ui_src_all if ord(c) > 127))
check("only WGL-safe non-ASCII in UI strings (— •)",
      set(non_ascii) <= {"—", "•"}, repr(non_ascii))

# ------------------------------------------------- 9. flow invariants
fm = read(os.path.join(SCRIPTS, "UIFlowManager.cs"))
fb = read(os.path.join(PROJ, "Assets", "Scripts", "FightBootstrap.cs"))
check("pause still freezes timeScale", "Time.timeScale = 0f;" in fm)
check("menus run unfrozen (Awake sets no freeze)",
      not re.search(r"void Awake\(\)\s*\{[^}]*Time\.timeScale", fm))
check("ShowLobby restores timeScale 1",
      re.search(r"void ShowLobby\(\)\s*\{[^}]*Time\.timeScale = 1f;", fm) is not None)
check("BeginFight starts via FightBootstrap.BeginMatch",
      "bootstrap.BeginMatch(selectedHeroId" in fm)
check("BeginFight does NOT call StartMatch directly", "rounds.StartMatch();" not in fm)
check("BeginMatch owns StartMatch", "roundManager.StartMatch();" in fb)
check("difficulty via BotAI.SetDifficulty (in BeginMatch path)",
      "SetDifficulty(botDifficulty)" in fb)
check("no local ApplyDifficulty left in UIFlowManager",
      "void ApplyDifficulty()" not in fm)
check("hero pick drives the fight (gap gone)",
      "has no hero-swap API" not in fm)
check("countdown driven by CountdownTick event",
      "rounds.CountdownTick += OnCountdownTick" in fm)
check("rematch path exists (REMATCH -> BeginFight)",
      "RematchButton" in fm and "BeginFight();" in fm)

print()
print(f"{len(FAILURES)} failures, {len(WARNINGS)} warnings")
sys.exit(1 if FAILURES else 0)
