// ============================================================================
// ModeHGroupRoster.cs - 鸭王杯群战：Boss 池、战力估算与蓝红两队的抽取（2026-09-29 owner 改版）
// ============================================================================
// 玩法（owner 原话收敛）：
//   - 每场两边一起抽：蓝队（左）按本场的战力目标抽，红队（右）按蓝队战力配平，两边合计战力相差不超过 500；
//   - 战力与人数随场次爬升：第 1 场较低，第 6 场到顶；
//   - 官方 Boss 可以重复出场（可能一边 20 个都是同一只），三只自定义 Boss 每边至多一只；
//   - 玩家选押哪边赢；一季固定 6 场，打完按押中场数与净赚进名人堂排名。
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
        /// <summary>蓝队人数下限。</summary>
        internal const int MinAllyCount = 3;
        /// <summary>蓝队人数上限。</summary>
        internal const int MaxAllyCount = 20;
        /// <summary>两边合计战力允许的最大差值。</summary>
        internal const int PowerTolerance = 500;
        /// <summary>红队人数上限（战力再高也不无限往上堆人）。</summary>
        internal const int MaxEnemyCount = 30;
        /// <summary>每场选人页可以「换一批」的次数。</summary>
        internal const int RerollsPerMatch = 3;
        /// <summary>单场时长。人多血厚，比单挑版多给一分钟；到时按剩余战力判胜负。</summary>
        internal const float MatchDurationSeconds = 240f;
        /// <summary>
        /// 自定义 Boss 的战力（owner 2026-09-29）：起初三只统一按 1000 点算；第五轮改为龙裔 1500、幻影女巫 500，
        /// 龙皇单独 2000（见下）。
        /// </summary>
        internal const int DragonDescendantPower = 1500;
        internal const int PhantomWitchPower = 500;
        /// <summary>焚天龙皇单独按 2000 点算（owner 2026-09-29 第二轮）。</summary>
        internal const int DragonKingPower = 2000;
        /// <summary>
        /// 鸭王杯里焚天龙皇的血量倍率（2026-09-29 第三轮）：本体 800 血，战力按 2000 算意味着对面约有八九只官方 Boss，
        /// 一上场被集火半秒就进二阶段、一阶段技能放不出来。×3 后一阶段能放完两三个技能；其余两只不加。
        /// </summary>
        internal const float DragonKingHealthScale = 3f;

        internal static float CustomHealthScale(string key)
        {
            return string.Equals(key, DragonKingConfig.BossNameKey, StringComparison.Ordinal) ? DragonKingHealthScale : 1f;
        }

        /// <summary>同一只自定义 Boss 每边最多几只（特效与技能很重，不让一边刷一排龙王）；官方 Boss 不限。</summary>
        internal const int MaxSameCustomPerSide = 1;
        /// <summary>官方 Boss 战力下限 / 上限（防止个别预设数值异常把配平搞崩）。</summary>
        internal const int MinOfficialPower = 80;
        internal const int MaxOfficialPower = 2500;

        /// <summary>
        /// 每场蓝队的战力目标 = 官方 Boss 平均战力 × 这一档（大致等于「平均几个人」）。
        /// 第 1 场约 4 人份，第 6 场约 17.5 人份，逐场单调爬升；落在目标 ±8% 内就算中。
        /// </summary>
        internal static readonly float[] MatchPowerScale = { 4f, 6f, 8.5f, 11f, 14f, 17.5f };
        internal const float MatchPowerBand = 0.08f;

        /// <summary>第 matchIndex 场（1 起）的蓝队战力目标；池里没有官方 Boss 时按 400 一人估。</summary>
        internal static int TargetPower(int matchIndex, float averagePower)
        {
            int slot = Mathf.Clamp(matchIndex, 1, MatchPowerScale.Length) - 1;
            float average = averagePower > 0f ? averagePower : 400f;
            return Mathf.RoundToInt(average * MatchPowerScale[slot]);
        }
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

    /// <summary>一场的蓝红两队（纯运行时，不落盘：中断重进回到选人页重新抽）。</summary>
    internal sealed class ModeHGroupRoster
    {
        public string RunId;
        public int MatchIndex;
        /// <summary>本场已经换过几批。</summary>
        public int RerollsUsed;
        /// <summary>蓝队（左，Teams.scav）。</summary>
        public readonly List<ModeHGroupEntry> Allies = new List<ModeHGroupEntry>();
        /// <summary>红队（右，Teams.wolf）。</summary>
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
        /// 三只自定义 Boss 走各自的托管生成器，战力固定（龙皇 2000，其余 1000）。
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
                DragonDescendantConfig.BOSS_NAME_CN, DragonDescendantConfig.BOSS_NAME_EN, ModeHGroupConfig.DragonDescendantPower);
            AddCustom(pool, DragonKingConfig.BossNameKey, DragonKingConfig.BossNameCN, DragonKingConfig.BossNameEN,
                ModeHGroupConfig.DragonKingPower);
            AddCustom(pool, PhantomWitchConfig.BossNameKey, PhantomWitchConfig.BossNameCN, PhantomWitchConfig.BossNameEN,
                ModeHGroupConfig.PhantomWitchPower);
            return pool;
        }

        private static void AddCustom(List<ModeHGroupEntry> pool, string key, string cn, string en, int power)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < pool.Count; i++) if (string.Equals(pool[i].Key, key, StringComparison.Ordinal)) return;
            pool.Add(new ModeHGroupEntry { Key = key, DisplayNameCn = cn, DisplayNameEn = en, Power = power, IsCustom = true });
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

        /// <summary>官方 Boss 的平均战力（场次战力目标的单位）。</summary>
        internal static float AverageOfficialPower(List<ModeHGroupEntry> pool)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; pool != null && i < pool.Count; i++)
            {
                if (pool[i] == null || pool[i].IsCustom) continue;
                sum += pool[i].Power;
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        /// <summary>
        /// 两边一起抽：蓝队按第 matchIndex 场的战力目标（±8%，3~20 人），红队按蓝队战力配平（差 ≤ 500，1~30 人）。
        /// </summary>
        internal static bool TryRollTeams(System.Random rng, List<ModeHGroupEntry> pool, int matchIndex, ModeHGroupRoster roster)
        {
            if (rng == null || pool == null || pool.Count == 0 || roster == null) return false;
            roster.Allies.Clear();
            roster.Enemies.Clear();
            int target = ModeHGroupConfig.TargetPower(matchIndex, AverageOfficialPower(pool));
            int band = Mathf.Max(1, Mathf.RoundToInt(target * ModeHGroupConfig.MatchPowerBand));
            List<ModeHGroupEntry> blue = RollToPower(rng, pool, target, band,
                ModeHGroupConfig.MinAllyCount, ModeHGroupConfig.MaxAllyCount);
            if (blue == null) return false;
            roster.Allies.AddRange(blue);
            List<ModeHGroupEntry> red = RollToPower(rng, pool, roster.AllyPower, ModeHGroupConfig.PowerTolerance,
                1, ModeHGroupConfig.MaxEnemyCount);
            if (red == null)
            {
                roster.Allies.Clear();
                return false;
            }
            roster.Enemies.AddRange(red);
            return true;
        }

        /// <summary>
        /// 抽一队到目标战力：随机往里加人（官方 Boss 可重复），停在目标下方随机一点；最后一个冲过头就换成让差值最小的那一个。
        /// 多试几轮，取第一组「人数合规且差值在容差内」的；都不行取差值最小的一组再贪心修一次。
        /// </summary>
        internal static List<ModeHGroupEntry> RollToPower(System.Random rng, List<ModeHGroupEntry> pool, int target,
            int tolerance, int minCount, int maxCount)
        {
            List<ModeHGroupEntry> best = null;
            int bestDiff = int.MaxValue;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                List<ModeHGroupEntry> team = new List<ModeHGroupEntry>();
                int stopAt = target - tolerance + rng.Next(0, tolerance + 1);
                int sum = 0;
                int guard = 0;
                while ((sum < stopAt || team.Count < minCount) && team.Count < maxCount && guard++ < 400)
                {
                    ModeHGroupEntry pick = pool[rng.Next(pool.Count)];
                    if (!CanAdd(team, pick)) continue;
                    team.Add(pick);
                    sum += pick.Power;
                }
                if (sum - target > tolerance && team.Count > minCount)
                {
                    // 最后一个冲过头：换成让差值最小的那一个（同样守自定义上限）
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
                    if (replacement != null && (team.Count < minCount || replacementDiff < Math.Abs(without - target)))
                    {
                        team.Add(replacement);
                        sum = without + replacement.Power;
                    }
                    else sum = without;
                }
                if (team.Count < minCount) continue;
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
            if (best == null) return null;
            if (bestDiff > tolerance) GreedyFix(pool, best, target, tolerance, minCount, maxCount);
            return best;
        }

        /// <summary>兜底：差太多时逐个加 / 减，直到进容差或没得改（池里最小战力远低于容差，实际总能收敛）。</summary>
        private static void GreedyFix(List<ModeHGroupEntry> pool, List<ModeHGroupEntry> team, int target, int tolerance,
            int minCount, int maxCount)
        {
            for (int step = 0; step < 64; step++)
            {
                int sum = ModeHGroupRoster.SumPower(team);
                int diff = sum - target;
                if (Math.Abs(diff) <= tolerance) return;
                if (diff < 0)
                {
                    if (team.Count >= maxCount) return;
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
                    if (team.Count <= minCount) return;
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
    }
}
