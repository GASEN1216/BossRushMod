// ============================================================================
// ShowcaseService.cs - 陈列加成：官方陈列柜里实际摆放的 Mod 战利品 → 全局最大生命加成
// ============================================================================
// 【2026-09-22 改造：自建「战利品登记簿」退役，改接官方陈列柜】
//   官方基地已有陈列柜 / 枪械展示架 / 假人（Duckov.Buildings.Showcase）：物品真被搬进去、
//   官方自己持久化、用物品自己的模型陈列——这正是自建柜给不了的（柜内三件展品是 GLB 烘死的）。
//   Mod 只做两件事：给 Mod 战利品补官方展示标签（ShowcaseTagInjector），以及把「官方柜里
//   现在摆着的 Mod 战利品」采集成快照（ShowcaseDisplayScanner）→ 本文件缓存、按品质挂加成。
//
// 【玩法取舍与回退】旧版是「登记不收走」（怕玩家在留装备与几个百分点之间二选一）。
//   官方柜是基地存储、不带出击、死了不掉，摆进去损失的是「使用」不是「拥有」，换来真实陈列；
//   数值口径逐字不变（每高于 Q4 一级 +0.5%，8 件全满 +5%，上限 +21%）。
//   回退：ShowcaseDisplayJudges 改回读缓存、sourceVersion 回 1 即旧语义，存档形状没变。
//
// 【存档 SCHEMA+】BossRush_BackMountain_Showcase_v1 新增可选 sourceVersion（缺失 = 1 老登记簿，2 = 官方柜陈列）。
//   schemaVersion **保持 1**：EnsureLoaded 对 version != 1 直接返回且 _writeBarrier 仍为 true，
//   升 2 会让老档被永久写保护。老登记簿只在基地且至少找到一个官方柜时被实摆覆盖（ShouldOverwriteLegacyLedger）。
//
// 【局外只读缓存】出击开局 ReapplyBonuses 只读 _displayed；基地外没有 Showcase 实例，不扫描。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>陈列加成存档 DTO。禁字段初始化器。</summary>
    [Serializable]
    internal class ShowcaseSaveData
    {
        public int schemaVersion;
        public int sourceVersion;
        public int[] displayedTypeIds;
    }

    /// <summary>陈列缓存与加成服务。</summary>
    internal static class ShowcaseService
    {
        #region 常量

        /// <summary>存档 schema 版本。**不得升 2**：老档会被 EnsureLoaded 永久写保护（见文件头）。</summary>
        private const int CurrentSchemaVersion = 1;

        private const int CurrentSourceVersion = ShowcaseDisplayJudges.OfficialDisplaySourceVersion;

        /// <summary>拿不到槽位号时的哨兵值（照 CampaignPersistence 的写法）。</summary>
        private const int SlotUnknown = int.MinValue;

        #endregion

        #region 状态

        private static List<int> _displayed;
        private static int _sourceVersion = ShowcaseDisplayJudges.LegacyLedgerSourceVersion;
        private static bool _loaded;
        private static bool _writeBarrier;

        /// <summary>
        /// 缓存对应的存档槽位。**必须比对**：换槽回调是运行时模块订阅的，
        /// 若那条链断了（模块 dormant、订阅失败），只靠 _loaded 会把 A 档的陈列
        /// 带进 B 档，并在 B 档写入时整体写脏 B 档存档。
        /// </summary>
        private static int _loadedSlot = SlotUnknown;

        private static readonly List<ZombieModeAttributeModifierRecord> _records =
            new List<ZombieModeAttributeModifierRecord>();
        private static readonly object _modifierSource = new object();

        #endregion

        #region 陈列读写

        internal static bool IsReadable { get { EnsureLoaded(); return !_writeBarrier; } }

        /// <summary>当前陈列的物品 TypeID 快照。永不返回 null。</summary>
        internal static IList<int> GetDisplayed()
        {
            EnsureLoaded();
            return new List<int>(_displayed);
        }

        /// <summary>已陈列数量。</summary>
        internal static int DisplayedCount
        {
            get
            {
                EnsureLoaded();
                return _displayed.Count;
            }
        }

        /// <summary>征程 trophy_displayed 目标的事实：官方柜里至少摆着一件 Mod 战利品（局外读缓存）。</summary>
        internal static bool HasDisplayedTrophy()
        {
            return DisplayedCount > 0;
        }

        /// <summary>1 = 老登记簿，2 = 官方柜实摆。</summary>
        internal static int SourceVersion { get { EnsureLoaded(); return _sourceVersion; } }

        /// <summary>
        /// 把「官方陈列柜里现在摆着的 Mod 战利品」整体覆盖为当前陈列。
        /// 与缓存一致时不落盘、不重挂加成（避免每次 plug 都写存档）；老登记簿只在基地找到官方柜时才被覆盖。
        /// 写失败恢复原列表并回滚官方缓存（Store 内）。
        /// </summary>
        internal static bool ApplyDisplaySnapshot(int[] typeIdsInOfficialSlots, bool anyShowcaseFound)
        {
            try
            {
                EnsureLoaded();
                if (_writeBarrier) return false;
                if (!ShowcaseDisplayJudges.ShouldOverwriteLegacyLedger(_sourceVersion, IsBaseScene(), anyShowcaseFound)) return false;
                int[] normalized = ShowcaseDisplayJudges.NormalizeDisplaySnapshot(
                    typeIdsInOfficialSlots, ReadQuality, BackMountainConfig.ShowcaseSlotCount);
                if (_sourceVersion == CurrentSourceVersion && ShowcaseDisplayJudges.SameSnapshot(_displayed, normalized)) return true;

                List<int> previous = _displayed;
                int previousSource = _sourceVersion;
                _displayed = new List<int>(normalized);
                _sourceVersion = CurrentSourceVersion;
                if (!Store())
                {
                    _displayed = previous;
                    _sourceVersion = previousSource;
                    return false;
                }
                ReapplyBonuses();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 陈列快照写入失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 加成

        /// <summary>
        /// 按当前陈列重挂全局加成。先摘干净再重挂，保证不会叠加。
        /// 玩家不在场（切场景途中）时只摘不挂，等下次场景就绪再来。
        /// </summary>
        internal static void ReapplyBonuses()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;

                // 摘旧加成之前先记住玩家是不是满血，不能用降低后的上限判断。
                // 官方进局治疗发生在本方法之前，
                // 治的是**加成前**的上限。不补这一下，玩家每次进局都差着展示柜那一截血，
                // 加成在开局等于零。只在原本满血时补，避免陈列一变动就免费回血。
                bool wasFull = false;
                float beforeMax = 0f;
                try
                {
                    if (main != null && main.Health != null)
                    {
                        beforeMax = main.Health.MaxHealth;
                        wasFull = main.Health.CurrentHealth >= beforeMax - 0.01f;
                    }
                }
                catch (Exception)
                {
                    wasFull = false;
                }

                RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");

                if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase)) return;
                if (main == null) return;

                float total = CalculateBonus();
                if (total <= 0f) return;

                RuntimeStatModifierTracker.TryAdd(
                    main, ZombieModeStatNames.MaxHealth, total, _modifierSource, _records, "Showcase");

                if (wasFull)
                {
                    try
                    {
                        if (main.Health != null && main.Health.MaxHealth > beforeMax)
                        {
                            main.Health.SetHealth(main.Health.MaxHealth);
                        }
                    }
                    catch (Exception healEx)
                    {
                        ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                            + "[WARNING] 展示柜加成后补满血失败: " + healEx.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜加成失败: " + e.Message);
            }
        }

        /// <summary>当前陈列提供的最大生命加成总量（0.05 = +5%）。公式在 ShowcaseDisplayJudges。</summary>
        internal static float CalculateBonus()
        {
            EnsureLoaded();
            return ShowcaseDisplayJudges.CalculateBonusFrom(_displayed, ReadQuality, BackMountainConfig.ShowcaseSlotCount);
        }

        private static int ReadQuality(int typeId)
        {
            try
            {
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                if (meta.id != typeId) return 0;
                return meta.quality;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>局结束/切场景时摘掉加成。角色会重建，记录必须作废。</summary>
        internal static void ClearBonuses()
        {
            try
            {
                RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 摘除展示柜加成失败: " + e.Message);
            }
        }

        #endregion

        #region 持久化

        private static void EnsureLoaded()
        {
            int slot = ReadCurrentSlotSafe();
            if (_loaded && _loadedSlot == slot) return;

            if (_loaded)
            {
                // 槽位在没收到换槽回调的情况下变了：上一个槽的加成还挂在角色身上，
                // 先摘干净，再从新槽重读
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                    + "[WARNING] 检测到存档槽位已变更但未收到切档回调，展示柜缓存已自失效");
                ClearBonuses();
            }

            _loaded = true;
            _loadedSlot = slot;
            _displayed = new List<int>();
            _sourceVersion = ShowcaseDisplayJudges.LegacyLedgerSourceVersion;
            _writeBarrier = true;

            try
            {
                if (slot == SlotUnknown || slot < 0) return;
                if (!SavesSystem.KeyExisits(BackMountainConfig.ShowcaseSaveKey))
                {
                    _writeBarrier = false;
                    return;
                }

                string raw = SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey);
                if (string.IsNullOrEmpty(raw)) return;

                BossRushJsonValue root;
                string error;
                int version;
                List<BossRushJsonValue> ids;
                if (!BossRushJsonParser.TryParse(raw, out root, out error) || root == null
                    || !root.TryGetInt("schemaVersion", out version) || version != CurrentSchemaVersion
                    || !root.TryGetArray("displayedTypeIds", out ids)
                    || ids.Count > BackMountainConfig.ShowcaseSlotCount) return;

                // sourceVersion 可选：缺失或 <1（旧 DTO 序列化出的 0）= 老登记簿；存在但不是整数 = 坏档，写保护
                int sourceVersion;
                if (root.TryGetInt("sourceVersion", out sourceVersion))
                {
                    if (sourceVersion < ShowcaseDisplayJudges.LegacyLedgerSourceVersion) sourceVersion = ShowcaseDisplayJudges.LegacyLedgerSourceVersion;
                }
                else if (HasNonIntegerSourceVersion(root)) return;
                else sourceVersion = ShowcaseDisplayJudges.LegacyLedgerSourceVersion;

                foreach (BossRushJsonValue id in ids)
                {
                    if (id == null || id.Kind != BossRushJsonKind.Integer
                        || id.IntegerValue <= 0 || id.IntegerValue > int.MaxValue
                        || _displayed.Contains((int)id.IntegerValue))
                    {
                        _displayed.Clear();
                        return;
                    }
                    _displayed.Add((int)id.IntegerValue);
                }
                _sourceVersion = sourceVersion;
                _writeBarrier = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜读档失败: " + e.Message);
            }
        }

        /// <summary>解析器没有「键存在但类型错」的探测：其它类型任一读得到即视为类型错。</summary>
        private static bool HasNonIntegerSourceVersion(BossRushJsonValue root)
        {
            string s; bool b; float f; List<BossRushJsonValue> a; BossRushJsonValue o;
            return root.TryGetString("sourceVersion", out s) || root.TryGetBool("sourceVersion", out b)
                || root.TryGetFloat("sourceVersion", out f) || root.TryGetArray("sourceVersion", out a)
                || root.TryGetObject("sourceVersion", out o);
        }

        private static bool Store()
        {
            string previousJson = null;
            bool writeAttempted = false;
            try
            {
                if (_writeBarrier || _loadedSlot != ReadCurrentSlotSafe()) return false;
                if (SavesSystem.IsSaving) return false;

                previousJson = SavesSystem.KeyExisits(BackMountainConfig.ShowcaseSaveKey)
                    ? SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey)
                    : Encode(new ShowcaseSaveData { schemaVersion = CurrentSchemaVersion, sourceVersion = _sourceVersion, displayedTypeIds = new int[0] });

                string json = Encode(new ShowcaseSaveData { schemaVersion = CurrentSchemaVersion, sourceVersion = _sourceVersion, displayedTypeIds = _displayed.ToArray() });
                writeAttempted = true;
                SavesSystem.Save<string>(BackMountainConfig.ShowcaseSaveKey, json);
                string readback = SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey);
                if (!string.Equals(readback, json, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("showcase save readback mismatch");
                }
                return true;
            }
            catch (Exception e)
            {
                if (writeAttempted)
                {
                    // Save 可已改内存缓存而回读失败。调用方会恢复列表，这里同步还原
                    // 官方缓存，避免下一次官方存档把已向玩家报告失败的新陈列写到磁盘。
                    try { SavesSystem.Save<string>(BackMountainConfig.ShowcaseSaveKey, previousJson); }
                    catch (Exception rollbackError)
                    {
                        ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                            + "[ERROR] 展示柜存档缓存回滚失败: " + rollbackError.Message);
                    }
                }
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜落档失败: " + e.Message);
                return false;
            }
        }

        private static string Encode(ShowcaseSaveData data)
        {
            var writer = new BossRushJsonWriter();
            writer.BeginObject().Int("schemaVersion", data.schemaVersion).Int("sourceVersion", data.sourceVersion).BeginArray("displayedTypeIds");
            for (int i = 0; i < data.displayedTypeIds.Length; i++) writer.ItemInt(data.displayedTypeIds[i]);
            return writer.EndArray().EndObject().ToString();
        }

        /// <summary>当前存档槽位；拿不到时返回哨兵值。no-throw。</summary>
        private static int ReadCurrentSlotSafe()
        {
            try
            {
                return SavesSystem.CurrentSlot;
            }
            catch (Exception)
            {
                return SlotUnknown;
            }
        }

        private static bool IsBaseScene()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 换槽/删档：丢弃缓存，下次访问从新槽重读。
        /// 由 BackMountainRuntimeModule 订阅 SavesSystem.OnSetFile / OnSaveDeleted 调用。
        /// </summary>
        internal static void NotifySlotChanged()
        {
            ClearBonuses();
            _loaded = false;
            _loadedSlot = SlotUnknown;
            _displayed = null;
            _sourceVersion = ShowcaseDisplayJudges.LegacyLedgerSourceVersion;
        }

        internal static void ResetStaticCaches()
        {
            NotifySlotChanged();
            _records.Clear();
        }

        #endregion
    }
}
