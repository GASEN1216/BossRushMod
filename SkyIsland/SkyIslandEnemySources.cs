using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛全部刷怪路径（自动遭遇、普通巡守、零号区序章守卫、Dev 测试敌人）共用的官方底模来源。
    ///
    /// owner 2026-10-01：「天空岛全部都使用 Boss 预设，符合我们 BossRush 的名字。」
    /// - 头目 / 岛主 / 具名对手 / 噬风 / 序章守卫：直接克隆各自参照的那个官方 Boss（<see cref="ForBaseline"/>，
    ///   残星匠首就是 EnemyPreset_Boss_Alex），外形、AI、技能与参照数值对得上。
    /// - 小兵、精英与巡守：从官方 Boss 池（<see cref="Bosses"/>）按点位散列固定抽一个，数值是那个 Boss 的原版乘统一倍率；
    ///   掉落与经验另由 <see cref="SkyIslandMinionKit"/> 换成拾荒者口径（owner 同日拍板）。
    ///
    /// Boss 池排除（owner 同日拍板）：Jeff、Fo 这类 NPC 伪装 Boss（名字带 _NPC_）、2000 血的口口口口岛主（噬风专用）、
    /// 测试预设（_Test）、载具、僵尸、中立 / 玩家阵营，以及不是官方 EnemyPreset_ 前缀的预设（本 Mod 的龙王、幽灵女巫等大型 Boss
    /// 与别的 Mod 加的角色）。近战 Boss（Tagilla、Killa、校霸）保留：它们就是冲上来砍的。
    ///
    /// 同日上午的旧口径是「阵营 scav、不是 Boss 的全部 preset」，混进了近战 Boss 小弟、足球员、机器蜘蛛与狂暴拾荒者；
    /// 序章取名字排最前的正好是近战的 EnemyPreset_BossMelee_SchoolBully_Child，守卫手里的枪一枪不开。
    /// 小弟（_Child）的 isBoss 是 false，不会进现在的池子。
    /// </summary>
    internal static class SkyIslandEnemySources
    {
        /// <summary>小兵换掉落与经验时参照的官方拾荒者（显示名 key 是 Cname_Scav），优先岛屿拾荒者。</summary>
        internal const string ScavNameKey = "Cname_Scav";
        private static readonly string[] PreferredScav = { "EnemyPreset_Scav_Island", "EnemyPreset_Scav" };

        private static List<CharacterRandomPreset> bossCache;
        private static bool logged;

        /// <summary>小兵可用的官方 Boss 预设，按名字排序（同一份资源在不同机器上顺序一致）。从不返回空表；一个都没有时抛异常。</summary>
        internal static List<CharacterRandomPreset> Bosses()
        {
            if (bossCache != null && bossCache.Count > 0) return bossCache;
            var result = new List<CharacterRandomPreset>();
            var seen = new HashSet<int>();
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && seen.Add(preset.GetInstanceID()) && IsPoolBoss(preset)) result.Add(preset);
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (result.Count == 0) throw new InvalidOperationException("天空岛没有可用的官方 Boss 预设");
            bossCache = result;
            if (!logged)
            {
                logged = true;
                var names = new List<string>(result.Count);
                for (int i = 0; i < result.Count; i++) names.Add(result[i].name);
                Debug.Log("[SkyIslandEnemy] BOSS_POOL count=" + result.Count + " " + string.Join(",", names.ToArray()));
            }
            return result;
        }

        /// <summary>小兵 / 巡守的底模：按点位 key 稳定散列到 Boss 池里，同一个点每趟都是同一位 Boss。</summary>
        internal static CharacterRandomPreset ForMinion(string key)
        {
            List<CharacterRandomPreset> pool = Bosses();
            return pool[SkyIslandLootTables.StableHash(key ?? string.Empty) % pool.Count];
        }

        /// <summary>
        /// 有固定参照的角色克隆参照的那个官方 Boss；资源里找不到时退回按 key 抽的池内 Boss，照常刷出
        /// （数值仍按参照，见 SkyIslandCombatPreset）。
        /// </summary>
        internal static CharacterRandomPreset ForBaseline(SkyIslandCombatBaseline baseline, string key)
        {
            CharacterRandomPreset named = baseline != null ? Named(baseline.PresetId) : null;
            return named ?? ForMinion(key);
        }

        /// <summary>按精确 preset 名找已加载的官方预设；找不到返回 null。</summary>
        internal static CharacterRandomPreset Named(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return null;
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && string.Equals(preset.name, presetId, StringComparison.Ordinal)) return preset;
            return null;
        }

        /// <summary>小兵掉落与经验的参照：官方持枪拾荒者，优先岛屿拾荒者；一个都没有时返回 null（小兵照常刷，保留 Boss 原掉落）。</summary>
        internal static CharacterRandomPreset ScavReference()
        {
            for (int i = 0; i < PreferredScav.Length; i++)
            {
                CharacterRandomPreset named = Named(PreferredScav[i]);
                if (named != null) return named;
            }
            CharacterRandomPreset best = null;
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && !preset.isBoss && preset.team == Teams.scav
                    && string.Equals(preset.nameKey, ScavNameKey, StringComparison.Ordinal)
                    && preset.name != null && preset.name.StartsWith("EnemyPreset_", StringComparison.Ordinal)
                    && (best == null || string.CompareOrdinal(preset.name, best.name) < 0)) best = preset;
            return best;
        }

        /// <summary>Boss 池的准入口径（owner 2026-10-01 拍板的排除项写在类注释里）。</summary>
        internal static bool IsPoolBoss(CharacterRandomPreset preset)
        {
            if (preset == null || !preset.isBoss || preset.isZombie || preset.isVehicle) return false;
            if (preset.team == Teams.player || preset.team == Teams.middle) return false;
            string name = preset.name ?? string.Empty;
            if (!name.StartsWith("EnemyPreset_", StringComparison.Ordinal)) return false;
            return name.IndexOf("_NPC_", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Koukou", StringComparison.OrdinalIgnoreCase) < 0
                && !name.EndsWith("_Test", StringComparison.OrdinalIgnoreCase);
        }

        internal static void ResetStaticCaches() { bossCache = null; logged = false; }
    }
}
