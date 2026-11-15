// FighterDataLibrary.cs — code fallback for FighterData.
//
// Builds the exact fighter.gd CHAR_DATA numbers in code. Used ONLY when a
// FighterData asset reference is missing at runtime (e.g. an .asset import
// failed) — the assets under Assets/Scripts/Data/ are the primary source.
// Keep this table in sync with fighter.gd and the .asset files; the
// verifier script (GAME-NOTES.md) checks all three against each other.
using UnityEngine;

public static class FighterDataLibrary
{
    public static FighterData Create(string characterId)
    {
        var data = ScriptableObject.CreateInstance<FighterData>();
        if (characterId == "azula") FillAzula(data);
        else FillKatara(data);
        return data;
    }

    public static void FillKatara(FighterData d)
    {
        d.characterId = "katara";
        d.displayName = "Katara";
        d.element = "water";
        d.topColor = new Color(0.15f, 0.35f, 0.65f);
        d.accentColor = new Color(0.35f, 0.75f, 0.95f);
        d.accent2Color = new Color(0.85f, 0.95f, 1.0f);
        d.runSpeed = 1.06f;
        d.punchDamage = 8f; d.punch2Damage = 10f; d.kickDamage = 13f; d.finisherDamage = 22f;
        d.punchCooldown = 0.35f; d.kickCooldown = 0.55f;
        d.skills = new SkillDef[4];
        //            name           short     dmg  cost  cd    vfx            cast           hit            delay  range
        d.skills[0] = Skill("Water Blast",  "BLAST",  18f, 25f, 1.2f, "WaterBlast",  "water_whoosh", "water_splash", 0.22f, 0f);
        d.skills[1] = Skill("Water Whip",   "WHIP",   22f, 35f, 2.5f, "WaterWhip",   "water_whoosh", "water_splash", 0.23f, 3.0f);
        d.skills[2] = Skill("Ice Spikes",   "SPIKES", 26f, 30f, 3.0f, "IceSpikes",   "ice_crack",    "ice_crack",    0.35f, 2.2f);
        d.skills[3] = Skill("Tidal Wave",   "WAVE",   16f, 40f, 4.0f, "TidalWave",   "water_whoosh", "water_splash", 0.23f, 2.0f);
        d.ultimateDamage = 60f; d.ultimateRange = 4.5f; d.ultimateKnockback = 14f;
        d.ultimateVfx = "UltimateKatara"; d.ultimateSound = "ultimate_katara";
        d.regenHpPerSecond = 12f; d.regenManaPerSecond = 14f;
    }

    public static void FillAzula(FighterData d)
    {
        d.characterId = "azula";
        d.displayName = "Azula";
        d.element = "fire";
        d.topColor = new Color(0.62f, 0.08f, 0.08f);
        d.accentColor = new Color(0.30f, 0.45f, 1.0f);
        d.accent2Color = new Color(0.65f, 0.80f, 1.0f);
        d.runSpeed = 0.96f;
        d.punchDamage = 8f; d.punch2Damage = 10f; d.kickDamage = 13f; d.finisherDamage = 22f;
        d.punchCooldown = 0.35f; d.kickCooldown = 0.55f;
        d.skills = new SkillDef[4];
        //            name              short    dmg  cost  cd    vfx               cast           hit            delay  range
        d.skills[0] = Skill("Fireball",        "FIRE",  20f, 25f, 1.2f, "Fireball",       "fire_whoosh",  "impact_thud",  0.22f, 0f);
        d.skills[1] = Skill("Flame Sweep",     "SWEEP", 24f, 35f, 2.5f, "FlameSweep",    "fire_whoosh",  "impact_thud",  0.27f, 3.0f);
        d.skills[2] = Skill("Lightning Strike","BOLT",  30f, 30f, 3.0f, "LightningStrike","lightning_zap","lightning_zap",0.25f, 1.8f);
        d.skills[3] = Skill("Fire Spin",       "SPIN",  20f, 30f, 3.5f, "FireSpin",       "fire_whoosh",  "impact_thud",  0.23f, 2.8f);
        d.ultimateDamage = 60f; d.ultimateRange = 4.5f; d.ultimateKnockback = 14f;
        d.ultimateVfx = "UltimateAzula"; d.ultimateSound = "ultimate_azula";
        d.regenHpPerSecond = 12f; d.regenManaPerSecond = 14f;
    }

    static SkillDef Skill(string name, string shortName, float dmg, float cost, float cd,
        string vfx, string castSound, string hitSound, float hitDelay, float range)
    {
        return new SkillDef
        {
            name = name, shortName = shortName, damage = dmg, cost = cost, cooldown = cd,
            vfxPrefab = vfx, castSound = castSound, hitSound = hitSound,
            hitDelay = hitDelay, range = range
        };
    }
}
