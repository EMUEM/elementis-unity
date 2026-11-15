// FighterData.cs — Elementis per-hero combat data.
//
// Single source of truth for the numbers in fighter.gd CHAR_DATA
// (damage / mana cost / cooldown / run speed), plus the Phase 2 additions
// (ultimate damage, regen rates). One ScriptableObject asset per hero:
//   Assets/Scripts/Data/KataraData.asset
//   Assets/Scripts/Data/AzulaData.asset
// A new hero is one new asset, not new code.
//
// Fallback: if a data asset reference is missing at runtime,
// FighterDataLibrary.Create(characterId) builds the same numbers in code
// (FighterDataLibrary.cs) so the fight still runs with correct values.
using UnityEngine;

[System.Serializable]
public class SkillDef
{
    [Tooltip("Full skill name, e.g. Water Blast.")]
    public string name;
    [Tooltip("Short HUD label, e.g. BLAST.")]
    public string shortName;
    [Tooltip("Damage on a clean hit (CHAR_DATA).")]
    public float damage;
    [Tooltip("Mana (energy) cost (CHAR_DATA).")]
    public float cost;
    [Tooltip("Cooldown in seconds (CHAR_DATA).")]
    public float cooldown;
    [Tooltip("VFX prefab name in ElementisFx (e.g. WaterBlast).")]
    public string vfxPrefab;
    [Tooltip("Cast SFX name in AudioManager (e.g. water_whoosh).")]
    public string castSound;
    [Tooltip("Hit SFX name in AudioManager (e.g. water_splash).")]
    public string hitSound;
    [Tooltip("Seconds from cast start to the hit point inside the skill (telegraph).")]
    public float hitDelay;
    [Tooltip("Hit range in metres for melee/AoE skills; projectiles use their own travel.")]
    public float range;
}

[CreateAssetMenu(fileName = "FighterData", menuName = "Elementis/Fighter Data")]
public class FighterData : ScriptableObject
{
    [Header("Identity")]
    public string characterId = "katara";   // "katara" | "azula"
    public string displayName = "Katara";
    public string element = "water";        // "water" | "fire"

    [Header("Look (CHAR_DATA colours)")]
    public Color topColor = new Color(0.15f, 0.35f, 0.65f);
    public Color accentColor = new Color(0.35f, 0.75f, 0.95f);
    public Color accent2Color = new Color(0.85f, 0.95f, 1.0f);

    [Header("Movement (CHAR_DATA run_speed)")]
    public float runSpeed = 1.06f;          // Katara 1.06, Azula 0.96

    [Header("Melee (fighter.gd: punch 8 / punch2 10 / kick 13 / finisher 22)")]
    public float punchDamage = 8f;
    public float punch2Damage = 10f;
    public float kickDamage = 13f;
    public float finisherDamage = 22f;
    public float punchCooldown = 0.35f;
    public float kickCooldown = 0.55f;

    [Header("Skills — exactly CHAR_DATA skill1..4, in order")]
    public SkillDef[] skills = new SkillDef[4];

    [Header("Ultimate (Phase 2, owner 2026-11-15 16:08)")]
    [Tooltip("High but balanced: ~17% of 350 max HP. Never a one-shot.")]
    public float ultimateDamage = 60f;
    public float ultimateRange = 4.5f;
    public float ultimateKnockback = 14f;
    public string ultimateVfx = "UltimateKatara";
    public string ultimateSound = "ultimate_katara";

    [Header("Regen channel (Phase 2, owner 2026-11-15 16:08)")]
    [Tooltip("HP per second while channeling. Steady, not instant: ~29 s to refill from 0.")]
    public float regenHpPerSecond = 12f;
    [Tooltip("Mana per second while channeling. ~7 s to refill from 0.")]
    public float regenManaPerSecond = 14f;

    public SkillDef GetSkill(int index)
    {
        if (skills == null || index < 0 || index >= skills.Length) return null;
        return skills[index];
    }
}
