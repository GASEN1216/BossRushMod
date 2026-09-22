// ============================================================================
// CodexBossCatalog.cs - 鸭皇图鉴 Boss 目录
// ============================================================================
// 职责：把「图鉴里应该有哪些格子、每格叫什么名字」收敛成唯一查询入口。
// 蓝本：PetNest/PetNestLineageCatalog.cs（惰性构建 + Invalidate 重建）。
//
// 口径（与遗种巢血脉目录同源，但**刻意多一步并集**）：
//   1) ModBehaviour.GetFilteredEnemyPresets() 的过滤池（自定义键留给下一步）；
//   1b) **官方 Boss 名单**（CodexOfficialBossRegistry / Assets/Data/CodexOfficialBosses.json）
//       里过滤池没给出的那些——这一步 2026-09-20 第三轮补上，见下方长注释；
//   2) 三个自定义 Boss 常量（也在公共池注册，在这里统一分类）；
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
    /// <summary>Boss 身份快照；显示名按取用时的游戏语言解析。</summary>
    internal sealed class CodexBossInfo
    {
        /// <summary>身份 key（官方 preset nameKey / 自定义 Boss 常量 / zombie_boss_*）。</summary>
        internal string Key;

        private string _fallbackName;

        /// <summary>名字按当前语言取用；快照只在现有本地化无法解析时回落。</summary>
        internal string DisplayName
        {
            get { return CodexBossCatalog.ResolveCurrentDisplayName(Key, _fallbackName); }
            set { _fallbackName = value; }
        }

        /// <summary>是否是本 Mod 的自定义 Boss。</summary>
        internal bool IsCustomBoss;

        /// <summary>是否是丧尸模式 Boss（合成条目）。</summary>
        internal bool IsZombieBoss;

        /// <summary>只在存档里出现、当前池不含（筛选器禁用 / 官方改名）。</summary>
        internal bool IsHistoricalOnly;

        /// <summary>
        /// 官方条目是否由这一局的过滤池给出（第 1 步）。false = 只在官方名单里、池子没给（筛选器关掉或
        /// 还没扫到），由第 1b 步补成锁定卡。目录总数不随筛选器变，变的是这个标记。
        /// </summary>
        internal bool IsInCurrentPool;
    }

    /// <summary>图鉴 Boss 目录。惰性构建，Boss 池变化时由 Invalidate 作废重建。</summary>
    internal static class CodexBossCatalog
    {
        #region 自定义 / 丧尸登记表

        /// <summary>三个自定义 Boss 的 canonical key。统一登记，避免在公共池里被误分为官方条目。</summary>
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
        private static bool _officialPoolAvailable;
        private static int _buildCount;

        /// <summary>目录是否已构建。</summary>
        internal static bool IsBuilt { get { return _built; } }
        internal static int BuildCount { get { return _buildCount; } }

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
                    _officialPoolAvailable = AddOfficialEntries(owner, byKey, ordered);
                    AddOfficialRosterEntries(byKey, ordered);
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
                _buildCount++;
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
        private static bool AddOfficialEntries(
            ModBehaviour owner,
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            if (owner == null) return false;

            List<EnemyPresetInfo> pool = null;
            try { pool = owner.GetFilteredEnemyPresets(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 读取 Boss 过滤池失败: " + e.Message);
                return false;
            }
            if (pool == null) return false;

            // 官方返回的是内部缓存列表**本体**（BossFilter.GetFilteredEnemyPresets 直接
            // return _filteredPresetsCache），只能读、不能改，也不能长期持有引用：
            // 这里逐项拷贝成自己的 CodexBossInfo。
            for (int i = 0; i < pool.Count; i++)
            {
                EnemyPresetInfo info = pool[i];
                if (info == null || string.IsNullOrEmpty(info.name)) continue;
                // 公共池也登记了三个自定义 Boss，交给下一步统一分类，避免被先占为官方条目。
                if (IsCustomBossKey(info.name)) continue;
                if (byKey.ContainsKey(info.name)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = info.name;
                entry.DisplayName = ResolveOfficialDisplayName(info);
                entry.IsCustomBoss = false;
                entry.IsZombieBoss = false;
                entry.IsHistoricalOnly = false;
                entry.IsInCurrentPool = true;

                byKey[entry.Key] = entry;
                ordered.Add(entry);
            }
            return true;
        }

        /// <summary>
        /// 第 1b 步：官方 Boss 名单里、过滤池没给出的条目，一律补成**未解锁的锁定卡**。
        ///
        /// 为什么必须有这一步（owner 2026-09-20 问题 3 的后半段）：
        /// 过滤池 = BossFilter.GetFilteredEnemyPresets()，它有两道会让官方 Boss 消失的闸——
        ///   a) 玩家在 Boss 筛选器里关掉了这个 Boss（`IsBossEnabled` 过滤掉）；
        ///   b) 官方 preset 还没被 InitializeEnemyPresets 扫到（没进过竞技场就是这个状态），
        ///      或者它的 baseHealth / team 不满足那张池子的口径。
        /// 于是玩家看到的图鉴会缺格：既没有锁定卡，也没有统计入口和立绘，
        /// 「还差哪几只没打」这件事在册子里根本查不到。
        ///
        /// 图鉴是**收藏册**，它的目录应该是「官方总共有哪些 Boss」，
        /// 而不是「这一局的刷怪池现在允许刷哪些」。名单本身就是 owner 指定的那份
        /// （escapefromduckov.net 的 isBoss 标记，落在 Assets/Data/CodexOfficialBosses.json），
        /// 与 FormatCategory 的分类判据同源——一份名单同时管分类和目录，不会两头打架。
        ///
        /// 显示名走 ResolveCurrentDisplayName 的官方本地化 key（`Cname_*` / `Character_*`），
        /// 解析不出来就退回 key 本身，不编造名字。立绘走 CodexPortraitCache 的同一条
        /// bossKey 通道（40 个名单条目在 tools/codex_official_presets.json 里都有图）。
        ///
        /// fail-open：名单读不出来时 OfficialBossKeys 返回空表，这一步等于不存在，
        /// 目录退回旧口径，面板照开。
        /// </summary>
        private static void AddOfficialRosterEntries(
            Dictionary<string, CodexBossInfo> byKey,
            List<CodexBossInfo> ordered)
        {
            List<string> roster;
            try { roster = CodexOfficialBossRegistry.OfficialBossKeys(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix
                    + "[WARNING] 官方 Boss 名单补目录失败，本次只用过滤池: " + e.Message);
                return;
            }
            if (roster == null) return;

            for (int i = 0; i < roster.Count; i++)
            {
                string key = roster[i];
                if (string.IsNullOrEmpty(key)) continue;
                // 自定义 Boss 不该出现在官方名单里；真出现了也让第 2 步统一分类
                if (IsCustomBossKey(key)) continue;
                if (byKey.ContainsKey(key)) continue;

                CodexBossInfo entry = new CodexBossInfo();
                entry.Key = key;
                entry.DisplayName = null;      // 取用时按当前语言解析官方 key
                entry.IsCustomBoss = false;
                entry.IsZombieBoss = false;
                // 不是历史条目：它是**当前**官方阵容的一员，只是这一局的池子里没有
                entry.IsHistoricalOnly = false;
                entry.IsInCurrentPool = false;

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
                if (stored == null || stored.Kills <= 0 || string.IsNullOrEmpty(stored.Key)) continue;
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

        /// <summary>新解锁的目录外 Boss 立即入册；保留已构建的官方池，避免每次击杀全量重建。</summary>
        internal static void SynchronizeHistoricalEntries(CodexData data)
        {
            EnsureBuilt(ModBehaviour.Instance);
            lock (_lock)
            {
                if (!_built || _byKey == null || _ordered == null) return;
                int before = _ordered.Count;
                AddHistoricalEntries(data, _byKey, _ordered);
                if (_ordered.Count != before) _buildCount++;
            }
        }

        /// <summary>全录按每个实际目录 key 判定，额外历史条目不能抵掉尚未解锁的 Boss。</summary>
        internal static bool IsFullyUnlocked(CodexData data)
        {
            if (data == null) return false;
            lock (_lock)
            {
                if (!_built || !_officialPoolAvailable || _ordered == null || _ordered.Count == 0) return false;
                for (int i = 0; i < _ordered.Count; i++)
                {
                    CodexEntry entry = data.Find(_ordered[i].Key);
                    if (entry == null || entry.Kills <= 0) return false;
                }
                return true;
            }
        }

        #endregion

        #region 显示名回落链

        internal static bool IsCustomBossKey(string key)
        {
            for (int i = 0; i < CustomBossKeys.Length; i++)
                if (string.Equals(key, CustomBossKeys[i], StringComparison.Ordinal)) return true;
            return false;
        }

        internal static string ResolveCurrentDisplayName(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;
            if (IsCustomBossKey(key)) return ResolveCustomDisplayName(key);
            for (int i = 0; i < ZombieBossKinds.Length; i++)
                if (key == BuildZombieBossKey(ZombieBossKinds[i])) return ResolveZombieDisplayName(ZombieBossKinds[i]);
            string localized = LocalizationHelper.GetLocalizedText(key);
            if (!string.IsNullOrEmpty(localized) && localized != key && localized.IndexOf('*') < 0)
                return localized;
            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }

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

            // 目录外 Boss（例如战役终章「冠军之影」）的 key 就是官方 preset 的 nameKey，
            // 它本身是一条**已注入的本地化键**。直接把 key 摆上卡片，玩家看到的是
            // 一串带星号的原始键。这里再过一次本地化：查得到就用译文，查不到才回落 key。
            string localized = LocalizationHelper.GetLocalizedText(key);
            if (!string.IsNullOrEmpty(localized) && localized != key
                && !(localized.Length >= 2 && localized[0] == '*'
                    && localized[localized.Length - 1] == '*'))
            {
                return localized;
            }
            return key;
        }

        /// <summary>丧尸 Boss 合成 key。存档兼容面，取值域冻结。</summary>
        internal static string BuildZombieBossKey(ZombieModeBossKind kind)
        {
            // OnGlobalHurt 每次命中都会查询，不能 Enum.ToString + 拼接分配。
            switch (kind)
            {
                case ZombieModeBossKind.Titan: return CodexTuning.ZombieBossKeyTitan;
                case ZombieModeBossKind.Hunter: return CodexTuning.ZombieBossKeyHunter;
                case ZombieModeBossKind.Splitter: return CodexTuning.ZombieBossKeySplitter;
                case ZombieModeBossKind.Shielder: return CodexTuning.ZombieBossKeyShielder;
                case ZombieModeBossKind.Corruptor: return CodexTuning.ZombieBossKeyCorruptor;
                default: return null;
            }
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
                _officialPoolAvailable = false;
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
            _buildCount = 0;
        }

        #endregion
    }
}
