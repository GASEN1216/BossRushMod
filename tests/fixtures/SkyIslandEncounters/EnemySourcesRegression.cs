using System;
using System.Collections.Generic;
using BossRush;

internal static partial class Program
{
    /// <summary>
    /// owner 2026-10-01：天空岛全部用官方 Boss 预设。生产的 SkyIslandEnemySources 只许把官方 Boss（EnemyPreset_ 前缀、isBoss）
    /// 收进小兵池，排除 NPC 伪装 Boss、口口口口、测试预设、载具、中立 / 玩家阵营与非官方前缀；有固定参照的克隆参照那一位。
    /// 再经真实遭遇 owner 刷一整组：小兵换拾荒者掉落，带队岛主不换。
    /// </summary>
    private static void CheckEnemySources()
    {
        Reset();
        SkyIslandMinionKit.Applied.Clear();
        var shot = CharacterRandomPreset.Source;
        var scav = CharacterRandomPreset.ScavReference;
        var killa = Variant("EnemyPreset_Boss_Killa", "Cname_Killa", true);
        var alex = Variant("EnemyPreset_Boss_Alex", "Character_Alex", true);
        var child = Variant("EnemyPreset_BossMelee_SchoolBully_Child", "Cname_SchoolBully_Child", false);
        var meleeScav = Variant("EnemyPreset_Scav_Melee", "Cname_ScavRage", false);
        var jeff = Variant("EnemyPreset_Boss_Island_NPC_Jeff", "Character_Jeff", true);
        var koukou = Variant("EnemyPreset_Boss_Island_Koukou", "Cname_IslandBoss", true);
        var test = Variant("EnemyPreset_Boss_Vida_Test", "Cname_Vida", true);
        var moto = Variant("EnemyPreset_Boss_Moto", "Cname_Moto", true); moto.isVehicle = true;
        var neutral = Variant("EnemyPreset_Boss_Neutral", "Cname_Neutral", true); neutral.team = Teams.middle;
        var dragon = Variant("BossRush_DragonKing", "BossRush_DragonKing", true);
        var otherMod = Variant("OtherMod_Boss", "OtherMod_Boss", true);
        UnityEngine.Resources.Presets = new[] { child, meleeScav, killa, jeff, koukou, test, moto, neutral, dragon, otherMod, scav, shot, alex };
        try
        {
            SkyIslandEnemySources.ResetStaticCaches();
            List<CharacterRandomPreset> pool = SkyIslandEnemySources.Bosses();
            Check(pool.Count == 3 && pool[0] == alex && pool[1] == shot && pool[2] == killa,
                "boss pool keeps only official bosses, sorted, without NPC / Koukou / test / vehicle / neutral / mod bosses");
            Check(SkyIslandEnemySources.ScavReference() == scav, "minion loot reference is the island scavenger");
            Check(SkyIslandEnemySources.ForMinion("C#1") == SkyIslandEnemySources.ForMinion("C#1"), "minion source is stable per slot");
            Check(SkyIslandEnemySources.ForBaseline(SkyIslandCombatBalance.Find("EnemyPreset_Boss_Alex"), "x") == alex,
                "profile bosses clone their own reference boss");
            Check(pool.Contains(SkyIslandEnemySources.ForBaseline(SkyIslandCombatBalance.Find("EnemyPreset_Boss_Hunter"), "x")),
                "missing reference boss falls back to the pool");
            // 真实遭遇 owner 刷一整组：C 的带队是岛主（有固定参照，留 Boss 掉落），其余是小兵（换拾荒者掉落）。
            using (var world = new World("C"))
            {
                world.Tick();
                int minions = 0;
                for (int i = 0; i < GroupSize("C"); i++)
                    if (SkyIslandCombatBalance.For("C", i, Array.Find(SkyIslandContent.CreateFallback().Encounters, e => e.Id == "C").TierFor(i)) == null) minions++;
                Check(CharacterRandomPreset.Clones.Count == GroupSize("C"), "group spawned from boss sources");
                Check(minions > 0 && SkyIslandMinionKit.Applied.Count == minions, "every minion, and only minions, takes scavenger loot");
                foreach (var spawned in CharacterRandomPreset.Clones)
                {
                    bool minion = SkyIslandMinionKit.Applied.Contains(spawned);
                    Check(minion ? !spawned.isBoss && spawned.exp == scav.exp : spawned.isBoss, "boss flag and exp follow the minion / lord split");
                }
            }
            UnityEngine.Resources.Presets = new[] { child, meleeScav, scav };
            SkyIslandEnemySources.ResetStaticCaches();
            bool threw = false;
            try { SkyIslandEnemySources.Bosses(); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "no official boss reports instead of silently spawning scavengers");
        }
        finally
        {
            UnityEngine.Resources.Presets = null;
            SkyIslandEnemySources.ResetStaticCaches();
            SkyIslandMinionKit.Applied.Clear();
            Reset();
        }
    }

    private static CharacterRandomPreset Variant(string name, string nameKey, bool boss)
    {
        var preset = (CharacterRandomPreset)CharacterRandomPreset.Source.Copy();
        CharacterRandomPreset.Clones.Remove(preset);
        preset.name = name; preset.nameKey = nameKey; preset.isBoss = boss;
        return preset;
    }
}
