// ============================================================================
// CodexBossCatalog.cs - 鸭皇图鉴 Boss 目录
// ============================================================================
// 职责：把「图鉴里应该有哪些格子、每格叫什么名字」收敛成唯一查询入口。
// 蓝本：PetNest/PetNestLineageCatalog.cs（惰性构建 + Invalidate 重建）。
//
// 口径（与遗种巢血脉目录同源，但**刻意多一步并集**）：
//   1) ModBehaviour.GetFilteredEnemyPresets() 的官方过滤池；
//   2) 三个自定义 Boss 常量（它们不在官方 preset 池里）；
//   3) 五个丧尸模式 Boss 的合成条目；
//   4) **存档里已经出现过、但前三步都不含的历史条目**。
//
// 第 4 步是图鉴与血脉目录的关键差异，理由：血脉目录管的是「现在还能不能产蛋」，
// 池子里没有就该消失；图鉴管的是「玩家打过什么」，是只增不减的收藏册。
// 玩家在 Boss 筛选器里关掉某个 Boss（或官方改了 nameKey）之后，
// 已解锁的条目**必须**继续留在图鉴里，否则收藏进度看起来像被清空了。
// 这类条目标 IsHistoricalOnly = true，显示名用存档里的 n 快照。
//
// fail-open 纪律：任何一步失败都只让目录少几条，不阻塞其余步骤，也不让面板打不开。
// **不经宿主单例**：owner 由运行时模块显式传入，避免给冻结的宿主单例引用分类基线
// 增列（tests/ModBehaviourInstanceClassificationGuard.py）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一个 Boss 的目录条目。只读快照，构建后不再变更。</summary>
    internal sealed class CodexBossInfo
    {
        /// <summary>身份 key（官方 preset nameKey / 自定义 Boss 常量 / zombie_boss_*）。</summary>
        internal string Key;

        /// <summary>显示名（回落链见 ResolveOfficialDisplayName / ResolveCustomDisplayName）。</summary>
        internal string DisplayName;

        /// <summary>是否是本 Mod 的自定义 Boss。</summary>
        internal bool IsCustomBoss;

        /// <summary>是否是丧尸模式 Boss（合成条目）。</summary>
        internal bool IsZombieBoss;

        /// <summary>只在存档里出现、当前池不含（筛选器禁用 / 官方改名）。</summary>
        internal bool IsHistoricalOnly;
    }

    /// <summary>图鉴 Boss 目录。惰性构建，Boss 池变化时由 Invalidate 作废重建。</summary>
    internal static class CodexBossCatalog
    {
        #region 自定义 / 丧尸登记表

        /// <summary>三个自定义 Boss 的 canonical key。它们不在官方 preset 池里，走显式登记。</summary>
        private static readonly string[] CustomBossKeys =
        {
            DragonDescendantConfig.BOSS_NAME_KEY,
            DragonKingConfig.BossNameKey,
            PhantomWitchConfig.BossNameKey,
        };

        /// <summary>
        /// 丧尸模式的五种 Boss。显式列表而不是 Enum.GetValues：
        /// 后者会装箱，且枚举顺序一旦变动会静默改变目录顺序。
        /// </summary>
        private static readonly ZombieModeBossKind[] ZombieBossKinds =
        {
            ZombieModeBossKind.Titan,
            ZombieModeBossKind.Hunter,
            ZombieModeBossKind.Splitter,
            ZombieModeBossKind.Shielder,
            ZombieModeBossKind.Corruptor,
        };

        #endregion

        #region 状态

        private static readonly object _lock = new object();
        private static Dictionary<string, CodexBossInfo> _byKey;
        private static List<CodexBossInfo> _ordered;
        private static bool _built;

        /// <summary>目录是否已构建。</summary>
        internal static bool IsBuilt { get { return _built; } }

        /// <summary>目录条目数（未构建返回 0）。里程碑「全收集」判定用它当分母。</summary>
        internal static int Count
        {
            get { lock (_lock) { return _ordered != null ? _ordered.Count : 0; } }
        }

        #endregion

        #region 构建

        /// <summary>
        /// 幂等构建。owner 为 null 时只登记自定义 + 丧尸 + 历史条目（官方池不可达）。
        /// </summary>
        internal static void EnsureBuilt(ModBehaviour owner)
        {
            if (_built) return;

            // 存档快照必须在**进锁之前**读：CodexPersistence.LoadOrInit 在检测到槽位漂移时
            // 会反向回调 CodexBossCatalog.NotifySlotChanged()。虽然 C# 的 lock 可重入、
            // 同线程不会死锁，但那会让「构建中途被作废」变成一条隐蔽路径。
            // 先读后锁把这条路径彻底去掉。
            CodexData saved = ReadSavedDataSafe();

            lock (_lock)
            {
                if (_built) return;

                Dictionary<string, CodexBossInfo> byKey =
                    new Dictionary<string, CodexBossInfo>(StringComparer.Ordinal);
                List<CodexBossInfo> ordered = new List<CodexBossInfo>(64);

                try
                {
                    AddOfficialEntries(owner, byKey, ordered);
                    AddCustomEntries(byKey, ordered);
                    AddZombieEntries(byKey, ordered);
                    AddHistoricalEntries(saved, byKey, ordered);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] Boss 目录构建失败，按已收集到的条目降级: " + e.Message);
                }

                _byKey = byKey;
                _ordered = ordered;
                _built = true;
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "Boss 目录构建完成，条目数=" + ordered.Count);
            }
        }

        /// <summary>no-throw 读存档快照。读不到返回 null（历史条目这一步就跳过）。</summary>
        private static CodexData ReadSavedDataSafe()
        {
            try
            {
                return CodexPersistence.Current;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 读取图鉴存档快照失败，历史条目本次不并入: " + e.Message);
                return null;
            }
        }

        /// <summary>第 1 步：官方过滤池。</summary>
        private static void AddOfficialEntries(
            ModBehaviour owner,
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            if (owner == null) return;

            List<EnemyPresetInfo> pool = null;
            try { pool = owner.GetFilteredEnemyPresets(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 读取 Boss 过滤池失败: " + e.Message);
                return;
            }
            if (pool == null) return;

            // 官方返回的是内部缓存列表**本体**（BossFilter.GetFilteredEnemyPresets 直接
            // return _filteredPresetsCache），只能读、不能改，也不能长期持有引用：
            // 这里逐项拷贝成自己的 CodexBossInfo。
            for (int i = 0; i < pool.Count; i++)
            {
                EnemyPresetInfo info = pool[i];
                if (info == null || string.IsNullOrEmpty(info.name)) continue;
                if (byKey.ContainsKey(info.name)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = info.name;
                entry.DisplayName = ResolveOfficialDisplayName(info);
                entry.IsCustomBoss = false;
                entry.IsZombieBoss = false;
                entry.IsHistoricalOnly = false;

                byKey[entry.Key] = entry;
                ordered.Add(entry);
            }
        }

        /// <summary>第 2 步：三个自定义 Boss 的 canonical key。</summary>
        private static void AddCustomEntries(
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            for (int i = 0; i < CustomBossKeys.Length; i++)
            {
                string key = CustomBossKeys[i];
                if (string.IsNullOrEmpty(key) || byKey.ContainsKey(key)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = key;
                entry.DisplayName = ResolveCustomDisplayName(key);
                entry.IsCustomBoss = true;
                entry.IsZombieBoss = false;
                entry.IsHistoricalOnly = false;

                byKey[entry.Key] = entry;
                ordered.Add(entry);
            }
        }

        /// <summary>第 3 步：五个丧尸模式 Boss 的合成条目。</summary>
        private static void AddZombieEntries(
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            for (int i = 0; i < ZombieBossKinds.Length; i++)
            {
                ZombieModeBossKind kind = ZombieBossKinds[i];
                string key = BuildZombieBossKey(kind);
                if (string.IsNullOrEmpty(key) || byKey.ContainsKey(key)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = key;
                entry.DisplayName = ResolveZombieDisplayName(kind);
                entry.IsCustomBoss = false;
                entry.IsZombieBoss = true;
                entry.IsHistoricalOnly = false;

                byKey[entry.Key] = entry;
                ordered.Add(entry);
            }
        }

        /// <summary>
        /// 第 4 步：存档里已有、但前三步都不含的历史条目。
        /// 只增不减是图鉴的产品语义：筛选器禁用 Boss 或官方改名之后，
        /// 玩家已解锁的格子必须留在册子里。
        /// </summary>
        private static void AddHistoricalEntries(
            CodexData saved,
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            if (saved == null) return;

            for (int i = 0; i < saved.Entries.Count; i++)
            {
                CodexEntry stored = saved.Entries[i];
                if (stored == null || string.IsNullOrEmpty(stored.Key)) continue;
                if (byKey.ContainsKey(stored.Key)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = stored.Key;
                entry.DisplayName = string.IsNullOrEmpty(stored.DisplayName)
                    ? stored.Key
                    : stored.DisplayName;
                entry.IsCustomBoss = false;
                entry.IsZombieBoss = !string.IsNullOrEmpty(stored.Key)
                    && stored.Key.StartsWith(CodexTuning.ZombieBossKeyPrefix, StringComparison.Ordinal);
                entry.IsHistoricalOnly = true;

                byKey[entry.Key] = entry;
                ordered.Add(entry);
            }
        }

        #endregion

        #region 显示名回落链

        /// <summary>官方 Boss：displayName -&gt; 本地化表 -&gt; 裸 nameKey。</summary>
        private static string ResolveOfficialDisplayName(EnemyPresetInfo info)
        {
            if (info == null) return string.Empty;
            if (!string.IsNullOrEmpty(info.displayName)) return info.displayName;
            if (!string.IsNullOrEmpty(info.name))
            {
                try
                {
                    // 走官方本地化表；查不到时官方会返回带 '*' 的占位串，此时回落裸 key
                    string plain = L10n.T(info.name);
                    if (!string.IsNullOrEmpty(plain) && plain.IndexOf('*') < 0) return plain;
                }
                catch (Exception)
                {
                    // 显示名解析失败：继续走下面的回落
                }
                return info.name;
            }
            return string.Empty;
        }

        /// <summary>
        /// 自定义 Boss 的显示名。
        ///
        /// 不尝试从 runtime preset 读（与 PetNestLineageCatalog.ResolveCustomDisplayName 同源）：
        /// 这三个 Boss 的 preset 是各自生成时才构造的，目录构建期必然查不到，
        /// 结果就是面板上显示 "boss_dragonking" 这类裸 key。
        /// </summary>
        private static string ResolveCustomDisplayName(string key)
        {
            if (string.Equals(key, DragonKingConfig.BossNameKey, StringComparison.Ordinal))
            {
                return L10n.T(DragonKingConfig.BossNameCN, DragonKingConfig.BossNameEN);
            }
            if (string.Equals(key, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
            {
                return L10n.T(PhantomWitchConfig.BossNameCN, PhantomWitchConfig.BossNameEN);
            }
            if (string.Equals(key, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal))
            {
                return L10n.T(DragonDescendantConfig.BOSS_NAME_CN, DragonDescendantConfig.BOSS_NAME_EN);
            }
            return key;
        }

        /// <summary>
        /// 丧尸 Boss 的显示名。五个 BossRush_ZombieMode_Boss_&lt;Kind&gt; 键由
        /// Localization/LocalizationInjector.cs 注入，与 ZombieModeSpawner 同源。
        /// </summary>
        private static string ResolveZombieDisplayName(ZombieModeBossKind kind)
        {
            try
            {
                string name = L10n.T("BossRush_ZombieMode_Boss_" + kind.ToString());
                if (!string.IsNullOrEmpty(name) && name.IndexOf('*') < 0) return name;
            }
            catch (Exception)
            {
                // 回落到枚举名，不阻塞目录构建
            }
            return kind.ToString();
        }

        #endregion

        #region 查询

        /// <summary>按 key 查目录。未构建或查不到返回 false。</summary>
        internal static bool TryGet(string key, out CodexBossInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
            {
                if (_byKey == null) return false;
                return _byKey.TryGetValue(key, out info);
            }
        }

        /// <summary>全部条目（构建顺序）。返回内部列表，调用方不得修改。</summary>
        internal static IList<CodexBossInfo> All
        {
            get
            {
                lock (_lock)
                {
                    if (_ordered == null) return new List<CodexBossInfo>();
                    return _ordered;
                }
            }
        }

        /// <summary>
        /// 目录查不到时的显示名回落：返回 key 本身而不是空串。
        /// 空串会让存档里落一个没有名字的条目，之后永远回落不出来。
        /// </summary>
        internal static string ResolveDisplayName(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            CodexBossInfo info;
            if (TryGet(key, out info) && info != null && !string.IsNullOrEmpty(info.DisplayName))
            {
                return info.DisplayName;
            }
            return key;
        }

        /// <summary>丧尸 Boss 合成 key。存档兼容面，取值域冻结。</summary>
        internal static string BuildZombieBossKey(ZombieModeBossKind kind)
        {
            return CodexTuning.ZombieBossKeyPrefix + kind.ToString();
        }

        #endregion

        #region 清理

        /// <summary>
        /// 作废目录（Boss 池筛选变化 / preset 缓存刷新时）。下次 EnsureBuilt 会重建。
        /// </summary>
        internal static void Invalidate()
        {
            lock (_lock)
            {
                _byKey = null;
                _ordered = null;
                _built = false;
            }
        }

        /// <summary>切档 / 删档：目录含存档历史条目，必须跟着换档重建。</summary>
        internal static void NotifySlotChanged()
        {
            Invalidate();
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Invalidate();
        }

        #endregion
    }
}
