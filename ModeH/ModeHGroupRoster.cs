// ============================================================================
// ModeHGroupRoster.cs - 鸭王杯群战：Boss 池、战力估算与左右两队的抽取（2026-09-29 owner 改版）
// ============================================================================
// 玩法（owner 原话收敛）：
//   - 每场先在选人页抽出「本场左边出战的群体」，人数 3~20 随机，可换几批；
//   - 选定后按战力给右边随机配敌人，人数由战力决定，两边合计战力相差不超过 500；
//   - 一季固定 6 场，打完按胜场与净赚进名人堂排名。
// Boss 池 = BossRush 的全部 Boss（宿主 EnemyPresets，不经玩家的 Boss 池筛选）+ 三只自定义 Boss。
// 纯数据与抽取逻辑，不碰场景对象；生成与战斗在 ModeHGroupBattle / ModeHRuntimeModule_GroupFlow。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>群战玩法常量（玩法强耦合常量，按 Config 三层归位放在模块内）。</summary>
    internal static class ModeHGroupConfig
    {
        /// <summary>鸭王杯走群战流程（owner 2026-09-29 定）；单挑版代码保留，不再走到。</summary>
        internal static readonly bool Enabled = true;
        /// <summary>左边（玩家这边）人数下限。</summary>
        internal const int MinAllyCount = 3;
        /// <summary>左边人数上限。</summary>
        internal const int MaxAllyCount = 20;
        /// <summary>两边合计战力允许的最大差值。</summary>
        internal const int PowerTolerance = 500;
        /// <summary>右边人数上限（战力再高也不无限往上堆人）。</summary>
        internal const int MaxEnemyCount = 30;
        /// <summary>每场选人页可以「换一批」的次数。</summary>
        internal const int RerollsPerMatch = 3;
        /// <summary>单场时长。人多血厚，比单挑版多给一分钟；到时按剩余战力判胜负。</summary>
        internal const float MatchDurationSeconds = 240f;
        /// <summary>自定义 Boss 的默认战力（owner 2026-09-29：三只强度过高，一律按 1000 点算）。</summary>
        internal const int CustomBossPower = 1000;
        /// <summary>同一只自定义 Boss 每边最多几只（特效与技能很重，不让一边刷一排龙王）。</summary>
        internal const int MaxSameCustomPerSide = 1;
        /// <summary>官方 Boss 战力下限 / 上限（防止个别预设数值异常把配平搞崩）。</summary>
        internal const int MinOfficialPower = 80;
        internal const int MaxOfficialPower = 2500;
    }

    /// <summary>池里的一种 Boss。</summary>
    internal sealed class ModeHGroupEntry
    {
        /// <summary>preset nameKey（自定义 Boss 为其常量 key）。</summary>
        public string Key;
        /// <summary>显示名（取用时解析，玩家可能切语言）。</summary>
        public string DisplayNameCn;
        public string DisplayNameEn;
        /// <summary>估算战力。</summary>
        public int Power;
        /// <summary>是否本 Mod 的三只自定义 Boss 之一（走托管生成）。</summary>
        public bool IsCustom;

        public string DisplayName
        {
            get
            {
                if (IsCustom) return L10n.T(DisplayNameCn, DisplayNameEn);
                string localized = L10n.T(Key);
                if (!string.IsNullOrEmpty(localized) && localized[0] != '*') return localized;
                return !string.IsNullOrEmpty(DisplayNameCn) ? DisplayNameCn : Key;
            }
        }
    }

    /// <summary>一场的左右两队（纯运行时，不落盘：中断重进回到选人页重新抽）。</summary>
    internal sealed class ModeHGroupRoster
    {
        public string RunId;
        public int MatchIndex;
        /// <summary>本场已经换过几批。</summary>
        public int RerollsUsed;
        /// <summary>玩家已点「就这队」并配好对手。</summary>
        public bool Confirmed;
        public readonly List<ModeHGroupEntry> Allies = new List<ModeHGroupEntry>();
        public readonly List<ModeHGroupEntry> Enemies = new List<ModeHGroupEntry>();

        public int AllyPower { get { return SumPower(Allies); } }
        public int EnemyPower { get { return SumPower(Enemies); } }

        internal static int SumPower(List<ModeHGroupEntry> list)
        {
            int sum = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) sum += list[i].Power;
            return sum;
        }
    }

    /// <summary>群战 Boss 池与两队抽取。</summary>
    internal static class ModeHGroupPool
    {
        /// <summary>
        /// 用宿主已初始化的 Boss 预设表建池。官方 Boss 必须能在官方目录里查到 preset（查不到就生成不了，不进池）。
        /// 三只自定义 Boss 走各自的托管生成器，战力固定 <see cref="ModeHGroupConfig.CustomBossPower"/>。
        /// </summary>
        internal static List<ModeHGroupEntry> Build(ModBehaviour owner)
        {
            List<ModeHGroupEntry> pool = new List<ModeHGroupEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<EnemyPresetInfo> presets = null;
            try { presets = owner != null ? owner.BossFilterEnemyPresets : null; }
            catch (Exception) { presets = null; }
            if (presets != null)
            {
                for (int i = 0; i < presets.Count; i++)
                {
                    EnemyPresetInfo info = presets[i];
                    if (info == null || string.IsNullOrEmpty(info.name) || !seen.Add(info.name)) continue;
                    if (IsCustomKey(info.name)) continue; // 自定义三只下面统一登记
                    CharacterRandomPreset preset = ModeHProductionCertification.ResolveAuditedPreset(info.name);
                    if (preset == null) continue;
                    ModeHGroupEntry entry = new ModeHGroupEntry();
                    entry.Key = info.name;
                    entry.DisplayNameCn = !string.IsNullOrEmpty(info.displayName) ? info.displayName : info.name;
                    entry.DisplayNameEn = entry.DisplayNameCn;
                    entry.Power = ComputeOfficialPower(preset);
                    pool.Add(entry);
                }
            }
            AddCustom(pool, DragonDescendantConfig.BOSS_NAME_KEY,
                DragonDescendantConfig.BOSS_NAME_CN, DragonDescendantConfig.BOSS_NAME_EN);
            AddCustom(pool, DragonKingConfig.BossNameKey, DragonKingConfig.BossNameCN, DragonKingConfig.BossNameEN);
            AddCustom(pool, PhantomWitchConfig.BossNameKey, PhantomWitchConfig.BossNameCN, PhantomWitchConfig.BossNameEN);
            return pool;
        }

        private static void AddCustom(List<ModeHGroupEntry> pool, string key, string cn, string en)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < pool.Count; i++) if (string.Equals(pool[i].Key, key, StringComparison.Ordinal)) return;
            pool.Add(new ModeHGroupEntry
            {
                Key = key, DisplayNameCn = cn, DisplayNameEn = en,
                Power = ModeHGroupConfig.CustomBossPower, IsCustom = true,
            });
        }

        internal static bool IsCustomKey(string key)
        {
            return string.Equals(key, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal)
                || string.Equals(key, DragonKingConfig.BossNameKey, StringComparison.Ordinal)
                || string.Equals(key, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// 官方 Boss 战力：生命与伤害倍率的几何平均 ×10，再按移速小幅修正。
        /// 生命 1500、伤害 x1.2、移速 x1 ≈ 424；生命 400 ≈ 200。双方同一把尺子，只用于配平与赔率，不宣称实测胜率。
        /// </summary>
        internal static int ComputeOfficialPower(CharacterRandomPreset preset)
        {
            if (preset == null) return ModeHGroupConfig.MinOfficialPower;
            try
            {
                float health = Mathf.Max(100f, preset.health);
                float damage = Mathf.Max(0.5f, Mathf.Max(preset.damageMultiplier, preset.meleeDamageMultiplier));
                float speed = Mathf.Clamp(preset.moveSpeedFactor, 0.5f, 2f);
                float power = Mathf.Sqrt(health * damage) * 10f * (0.85f + 0.15f * speed);
                return Mathf.Clamp(Mathf.RoundToInt(power),
                    ModeHGroupConfig.MinOfficialPower, ModeHGroupConfig.MaxOfficialPower);
            }
            catch (Exception)
            {
                return ModeHGroupConfig.MinOfficialPower;
            }
        }

        /// <summary>抽左边：人数 3~20 随机，先不重复地抽，池不够再允许重复。</summary>
        internal static bool TryRollAllies(System.Random rng, List<ModeHGroupEntry> pool, ModeHGroupRoster roster)
        {
            if (rng == null || pool == null || pool.Count == 0 || roster == null) return false;
            roster.Allies.Clear();
            roster.Enemies.Clear();
            roster.Confirmed = false;
            int count = rng.Next(ModeHGroupConfig.MinAllyCount, ModeHGroupConfig.MaxAllyCount + 1);
            List<ModeHGroupEntry> bag = new List<ModeHGroupEntry>(pool);
            Shuffle(rng, bag);
            int guard = 0;
            while (roster.Allies.Count < count && guard++ < count * 20)
            {
                if (bag.Count == 0)
                {
                    bag.AddRange(pool);
                    Shuffle(rng, bag);
                }
                ModeHGroupEntry pick = bag[bag.Count - 1];
                bag.RemoveAt(bag.Count - 1);
                if (!CanAdd(roster.Allies, pick)) continue;
                roster.Allies.Add(pick);
            }
            return roster.Allies.Count >= ModeHGroupConfig.MinAllyCount;
        }

        /// <summary>
        /// 抽右边：随机往里加人，停在目标战力下方随机一点；最后一个冲过头就换成让差值最小的那一个。
        /// 多试几轮，取第一组差值在容差内的；都不行取差值最小的一组再贪心修一次。
        /// </summary>
        internal static bool TryRollEnemies(System.Random rng, List<ModeHGroupEntry> pool, ModeHGroupRoster roster)
        {
            if (rng == null || pool == null || pool.Count == 0 || roster == null) return false;
            int target = roster.AllyPower;
            int tolerance = ModeHGroupConfig.PowerTolerance;
            List<ModeHGroupEntry> best = null;
            int bestDiff = int.MaxValue;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                List<ModeHGroupEntry> team = new List<ModeHGroupEntry>();
                int stopAt = target - tolerance + rng.Next(0, tolerance);
                int sum = 0;
                int guard = 0;
                while ((sum < stopAt || team.Count == 0) && team.Count < ModeHGroupConfig.MaxEnemyCount && guard++ < 400)
                {
                    ModeHGroupEntry pick = pool[rng.Next(pool.Count)];
                    if (!CanAdd(team, pick)) continue;
                    team.Add(pick);
                    sum += pick.Power;
                }
                if (sum - target > tolerance && team.Count > 0)
                {
                    // 最后一个冲过头：换成让两边最接近的那一个（同样守自定义上限）
                    ModeHGroupEntry last = team[team.Count - 1];
                    team.RemoveAt(team.Count - 1);
                    int without = sum - last.Power;
                    ModeHGroupEntry replacement = null;
                    int replacementDiff = int.MaxValue;
                    int offset = rng.Next(pool.Count);
                    for (int i = 0; i < pool.Count; i++)
                    {
                        ModeHGroupEntry candidate = pool[(i + offset) % pool.Count];
                        if (!CanAdd(team, candidate)) continue;
                        int diff = Math.Abs(without + candidate.Power - target);
                        if (diff < replacementDiff) { replacementDiff = diff; replacement = candidate; }
                    }
                    if (replacement != null && (team.Count == 0 || replacementDiff < Math.Abs(without - target)))
                    {
                        team.Add(replacement);
                        sum = without + replacement.Power;
                    }
                    else sum = without;
                }
                if (team.Count == 0) continue;
                int finalDiff = Math.Abs(sum - target);
                if (finalDiff <= tolerance)
                {
                    best = team;
                    bestDiff = finalDiff;
                    break;
                }
                if (finalDiff < bestDiff)
                {
                    best = team;
                    bestDiff = finalDiff;
                }
            }
            if (best == null) return false;
            if (bestDiff > tolerance) GreedyFix(pool, best, target, tolerance);
            roster.Enemies.Clear();
            roster.Enemies.AddRange(best);
            return roster.Enemies.Count > 0;
        }

        /// <summary>兜底：差太多时逐个加 / 换 / 减，直到进容差或没得改（池里最小战力远低于容差，实际总能收敛）。</summary>
        private static void GreedyFix(List<ModeHGroupEntry> pool, List<ModeHGroupEntry> team, int target, int tolerance)
        {
            for (int step = 0; step < 64; step++)
            {
                int sum = ModeHGroupRoster.SumPower(team);
                int diff = sum - target;
                if (Math.Abs(diff) <= tolerance) return;
                if (diff < 0)
                {
                    if (team.Count >= ModeHGroupConfig.MaxEnemyCount) return;
                    ModeHGroupEntry add = null;
                    int addDiff = Math.Abs(diff);
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (!CanAdd(team, pool[i])) continue;
                        int d = Math.Abs(diff + pool[i].Power);
                        if (d < addDiff) { addDiff = d; add = pool[i]; }
                    }
                    if (add == null) return;
                    team.Add(add);
                }
                else
                {
                    if (team.Count <= 1) return;
                    int removeIndex = -1;
                    int removeDiff = Math.Abs(diff);
                    for (int i = 0; i < team.Count; i++)
                    {
                        int d = Math.Abs(diff - team[i].Power);
                        if (d < removeDiff) { removeDiff = d; removeIndex = i; }
                    }
                    if (removeIndex < 0) return;
                    team.RemoveAt(removeIndex);
                }
            }
        }

        private static bool CanAdd(List<ModeHGroupEntry> team, ModeHGroupEntry pick)
        {
            if (pick == null) return false;
            if (!pick.IsCustom) return true;
            int same = 0;
            for (int i = 0; i < team.Count; i++)
                if (team[i] != null && string.Equals(team[i].Key, pick.Key, StringComparison.Ordinal)) same++;
            return same < ModeHGroupConfig.MaxSameCustomPerSide;
        }

        private static void Shuffle(System.Random rng, List<ModeHGroupEntry> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                ModeHGroupEntry temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}
