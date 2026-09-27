using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 鸭王杯候选预制体的官方基础战斗参数快照。
    ///
    /// 数值来自官方 Wiki creatures 页面（2026-09-25 抓取），只作为匹配/赔率的
    /// 公开强度基线；实际生成仍以游戏内 CharacterRandomPreset 为准。倍率全部保存为
    /// 千分比整数，避免 Mono 浮点四舍五入让同一场比赛的赔率漂移。
    /// </summary>
    internal sealed class ModeHOfficialBossAttribute
    {
        public readonly string StableKey;
        public readonly string PresetId;
        public readonly int Health;
        public readonly int DamageMultiplierMilli;
        public readonly int GunDistanceMultiplierMilli;
        public readonly int MoveSpeedFactorMilli;
        public readonly int GunCritRateGainMilli;
        public readonly int AiCombatFactorMilli;
        public readonly int SightDistance;

        public ModeHOfficialBossAttribute(
            string stableKey, string presetId, int health, int damageMultiplierMilli,
            int gunDistanceMultiplierMilli, int moveSpeedFactorMilli,
            int gunCritRateGainMilli, int aiCombatFactorMilli, int sightDistance)
        {
            StableKey = stableKey;
            PresetId = presetId;
            Health = health;
            DamageMultiplierMilli = damageMultiplierMilli;
            GunDistanceMultiplierMilli = gunDistanceMultiplierMilli;
            MoveSpeedFactorMilli = moveSpeedFactorMilli;
            GunCritRateGainMilli = gunCritRateGainMilli;
            AiCombatFactorMilli = aiCombatFactorMilli;
            SightDistance = sightDistance;
        }

        /// <summary>
        /// 把官方属性压成仅用于公开匹配的整数强度分。
        /// 这是赔率基线，不会改写伤害、血量或游戏内属性。
        /// </summary>
        public int ComputeStrengthScore()
        {
            int score = Health / 20;
            score += DamageMultiplierMilli / 100;
            score += GunDistanceMultiplierMilli / 200;
            score += MoveSpeedFactorMilli / 200;
            score += AiCombatFactorMilli / 500;
            score += Math.Min(20, GunCritRateGainMilli / 10);
            score += SightDistance / 10;
            return score;
        }
    }

    /// <summary>官方 Wiki 属性表的运行时只读索引。</summary>
    internal static class ModeHOfficialBossAttributeCatalog
    {
        private static Dictionary<string, ModeHOfficialBossAttribute> _byStableKey =
            Build();

        private static Dictionary<string, ModeHOfficialBossAttribute> Build()
        {
            Dictionary<string, ModeHOfficialBossAttribute> map =
                new Dictionary<string, ModeHOfficialBossAttribute>(StringComparer.Ordinal);

            Add(map, "Cname_Boss_Shot", "EnemyPreset_Boss_Shot", 360, 1500, 1600, 1250, 300, 2000, 18);
            Add(map, "Cname_Speedy_Ice", "EnemyPreset_Boss_Speedy_Ice", 226, 800, 1250, 1200, 0, 2000, 18);
            Add(map, "Cname_Snow_Fleeze", "EnemyPreset_Boss_Snow_Fleeze", 350, 1000, 1350, 1500, 0, 2000, 17);
            Add(map, "Cname_Boss_Sniper", "EnemyPreset_Boss_Deng", 190, 850, 1400, 1200, 100000, 2000, 35);
            Add(map, "Cname_Boss_3Shot", "EnemyPreset_Boss_3Shot", 400, 1000, 1400, 1200, 150, 2000, 40);
            Add(map, "Cname_XING", "EnemyPreset_Boss_XING", 350, 1000, 1800, 1600, 0, 2000, 18);
            Add(map, "Cname_SnowMan", "EnemyPreset_Boss_SnowMan", 320, 1000, 1350, 1500, 0, 5000, 17);
            Add(map, "Cname_Snow_BigIce", "EnemyPreset_Boss_Snow_BigIce", 300, 1000, 1350, 1500, 0, 2000, 17);
            Add(map, "Cname_Grenade", "EnemyPreset_Boss_Grenade", 300, 1000, 1350, 1500, 0, 2000, 17);
            Add(map, "Cname_PMCLeader", "EnemyPreset_Boss_PMCLeader", 550, 950, 1350, 1150, 0, 2000, 17);
            Add(map, "Cname_Hunter", "EnemyPreset_Boss_Hunter", 450, 1150, 1350, 1500, 0, 4000, 17);
            Add(map, "Cname_Prison_Boss", "EnemyPreset_Prison_Boss", 135, 1350, 1300, 1000, 0, 1500, 17);
            return map;
        }

        private static void Add(Dictionary<string, ModeHOfficialBossAttribute> map,
            string stableKey, string presetId, int health, int damageMultiplierMilli,
            int gunDistanceMultiplierMilli, int moveSpeedFactorMilli,
            int gunCritRateGainMilli, int aiCombatFactorMilli, int sightDistance)
        {
            map[stableKey] = new ModeHOfficialBossAttribute(
                stableKey, presetId, health, damageMultiplierMilli,
                gunDistanceMultiplierMilli, moveSpeedFactorMilli,
                gunCritRateGainMilli, aiCombatFactorMilli, sightDistance);
        }

        public static bool TryGet(string stableKey, out ModeHOfficialBossAttribute attribute)
        {
            attribute = null;
            if (string.IsNullOrEmpty(stableKey)) return false;
            return _byStableKey.TryGetValue(stableKey, out attribute) && attribute != null;
        }

        /// <summary>Mod 卸载或宿主重建时重建只读快照，避免旧域的静态字典残留。</summary>
        public static void ResetStaticCaches()
        {
            _byStableKey = Build();
        }

        /// <summary>
        /// 返回敌方候选的平均官方强度。缺少 Wiki 快照的旧 key 会回退到 BossProfiles
        /// 的 threatScore，保证新增候选不会让赔率服务直接失效。
        /// </summary>
        public static int ComputeAverageStrength(IList<string> stableKeys, out int count)
        {
            count = 0;
            if (stableKeys == null) return 0;
            int total = 0;
            for (int i = 0; i < stableKeys.Count; i++)
            {
                string key = stableKeys[i];
                ModeHOfficialBossAttribute attribute;
                if (TryGet(key, out attribute))
                {
                    total += attribute.ComputeStrengthScore();
                    count++;
                    continue;
                }

                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(key);
                if (template == null) continue;
                total += template.ThreatScore;
                count++;
            }
            return count > 0 ? (total + count / 2) / count : 0;
        }
    }
}
