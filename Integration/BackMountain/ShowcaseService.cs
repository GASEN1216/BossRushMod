// ============================================================================
// ShowcaseService.cs - 战利品展示柜：收藏持久化与全局加成
// ============================================================================
// 【为什么自建而不是用官方 Showcase】
//   官方 Duckov.Buildings.Showcase 重度依赖 prefab：它要一个配好 SlotCollection
//   的 Item、一组按 slot 名对应的 displayParents、一个 vcam，还有六个私有序列化字段。
//   运行时拼一个合法的多槽 Item 出来既脆又没有先例。
//   而官方 BuildingEffect 恰恰证明了「跨局全局加成」的正统做法就是
//   `GetStat(name).AddModifier(...)`——那和 mod 现成的 RuntimeStatModifierTracker
//   是同一套东西。于是收藏自己存、加成自己挂，比迁就官方 prefab 简单得多。
//
// 【展示柜是「战利品登记簿」，不是储物柜——物品不会被收走】
//   最初的设计是「存进去才算陈列」，但那会逼玩家在「留着这把传说武器」和
//   「换几个百分点属性」之间二选一。绝大多数人会选前者，于是整个系统没人用。
//   改成登记制之后，玩家带着战利品来登记一次，东西照样归自己，
//   加成来自「你确实打到过它」。收集与炫耀的驱动力保留了，机会成本没了。
//   附带好处：不需要动任何物品消耗/归还的 API，也就没有丢件的风险。
//
// 【存 TypeID 而不是存 Item 实例】
//   登记的是「你拥有过这一型战利品」，没有耐久、词缀之类的实例状态需要保留。
//   存 TypeID 集合让存档结构极简。
//
// 【加成数值均为草案，待 owner 审定】
//   按品质给：每高于 Q4 一级 +0.5% 最大生命（Q5→+0.5%，Q8→+2%）。
//   八格全满额外 +5%。上限约 +21%，不至于让展示柜变成必刷。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>展示柜收藏存档 DTO。禁字段初始化器。</summary>
    [Serializable]
    internal class ShowcaseSaveData
    {
        public int schemaVersion;
        public int[] displayedTypeIds;
    }

    /// <summary>展示柜收藏与加成服务。</summary>
    internal static class ShowcaseService
    {
        #region 常量（草案，待 owner 审定）

        private const int CurrentSchemaVersion = 1;

        /// <summary>每高于 Q4 一级的最大生命加成。</summary>
        private const float BonusPerQualityLevel = 0.005f;

        /// <summary>八格全满的额外加成。</summary>
        private const float FullSetBonus = 0.05f;

        /// <summary>可陈列的最低品质。低于它的普通物品不给加成也不让存。</summary>
        private const int MinDisplayQuality = 5;

        /// <summary>拿不到槽位号时的哨兵值（照 CampaignPersistence 的写法）。</summary>
        private const int SlotUnknown = int.MinValue;

        #endregion

        #region 状态

        private static List<int> _displayed;
        private static bool _loaded;

        /// <summary>
        /// 缓存对应的存档槽位。**必须比对**：换槽回调是运行时模块订阅的，
        /// 若那条链断了（模块 dormant、订阅失败），只靠 _loaded 会把 A 档的收藏
        /// 带进 B 档，并在 B 档登记时整体写脏 B 档存档。
        /// </summary>
        private static int _loadedSlot = SlotUnknown;

        private static readonly List<ZombieModeAttributeModifierRecord> _records =
            new List<ZombieModeAttributeModifierRecord>();
        private static readonly object _modifierSource = new object();

        #endregion

        #region 收藏读写

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

        /// <summary>该物品能否放进展示柜。</summary>
        internal static bool CanDisplay(Item item, out string reason)
        {
            reason = null;
            try
            {
                if (item == null)
                {
                    reason = L10n.T("没有可陈列的物品", "No item to display");
                    return false;
                }

                EnsureLoaded();
                BackMountainItems.Definition backMountainItem = BackMountainItems.GetDefinition(item.TypeID);
                if (backMountainItem != null)
                {
                    reason = L10n.T("菜地种子和出击餐不能登记为战利品",
                        "Garden seeds and raid meals are not trophies");
                    return false;
                }
                if (_displayed.Count >= BackMountainConfig.ShowcaseSlotCount)
                {
                    reason = L10n.T("展示柜已满", "The showcase is full");
                    return false;
                }
                if (item.Quality < MinDisplayQuality)
                {
                    reason = L10n.T("只有高品质战利品值得陈列", "Only high-quality trophies are worth displaying");
                    return false;
                }
                if (_displayed.Contains(item.TypeID))
                {
                    reason = L10n.T("这件战利品已经登记过了", "That trophy is already recorded");
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                reason = L10n.T("无法陈列", "Cannot display");
                return false;
            }
        }

        /// <summary>登记一件战利品。物品仍归玩家，这里只记 TypeID。</summary>
        internal static bool TryDisplay(int typeId)
        {
            try
            {
                EnsureLoaded();
                if (typeId <= 0) return false;
                if (_displayed.Count >= BackMountainConfig.ShowcaseSlotCount) return false;
                if (_displayed.Contains(typeId)) return false;

                _displayed.Add(typeId);
                if (!Store())
                {
                    _displayed.Remove(typeId);
                    return false;
                }
                ReapplyBonuses();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 陈列失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 撤销一条登记。玩家用不到（登记是纯收益、没有代价），
        /// 保留它只为调试与将来可能的「重置收藏」需求。
        /// </summary>
        internal static bool TryRemoveRecord(int typeId)
        {
            try
            {
                EnsureLoaded();
                int index = _displayed.IndexOf(typeId);
                if (index < 0) return false;
                _displayed.RemoveAt(index);

                if (!Store())
                {
                    _displayed.Insert(index, typeId);
                    return false;
                }
                ReapplyBonuses();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 撤销登记失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 加成

        /// <summary>
        /// 按当前收藏重挂全局加成。先摘干净再重挂，保证不会叠加。
        /// 玩家不在场（切场景途中）时只摘不挂，等下次场景就绪再来。
        /// </summary>
        internal static void ReapplyBonuses()
        {
            try
            {
                RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");

                if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase)) return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                float total = CalculateBonus();
                if (total <= 0f) return;

                RuntimeStatModifierTracker.TryAdd(
                    main, ZombieModeStatNames.MaxHealth, total, _modifierSource, _records, "Showcase");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜加成失败: " + e.Message);
            }
        }

        /// <summary>当前收藏提供的最大生命加成总量（0.05 = +5%）。</summary>
        internal static float CalculateBonus()
        {
            EnsureLoaded();

            float total = 0f;
            for (int i = 0; i < _displayed.Count; i++)
            {
                int quality = ReadQuality(_displayed[i]);
                if (quality <= 4) continue;
                total += (quality - 4) * BonusPerQualityLevel;
            }

            if (_displayed.Count >= BackMountainConfig.ShowcaseSlotCount)
            {
                total += FullSetBonus;
            }
            return total;
        }

        private static int ReadQuality(int typeId)
        {
            try
            {
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                if (meta.id <= 0) return 0;
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

            try
            {
                if (!SavesSystem.KeyExisits(BackMountainConfig.ShowcaseSaveKey)) return;

                string raw = SavesSystem.Load<string>(BackMountainConfig.ShowcaseSaveKey);
                if (string.IsNullOrEmpty(raw)) return;

                ShowcaseSaveData data = JsonUtility.FromJson<ShowcaseSaveData>(raw);
                if (data == null || data.displayedTypeIds == null) return;
                if (data.schemaVersion != CurrentSchemaVersion)
                {
                    // 未知版本只读不写：宁可这一局不加成，也不覆盖玩家的收藏
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                        + "[WARNING] 展示柜存档版本不符，只读不覆盖");
                    return;
                }

                for (int i = 0; i < data.displayedTypeIds.Length; i++)
                {
                    int typeId = data.displayedTypeIds[i];
                    if (typeId <= 0) continue;
                    if (_displayed.Contains(typeId)) continue;
                    _displayed.Add(typeId);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜读档失败: " + e.Message);
            }
        }

        private static bool Store()
        {
            try
            {
                if (SavesSystem.IsSaving) return false;

                ShowcaseSaveData data = new ShowcaseSaveData();
                data.schemaVersion = CurrentSchemaVersion;
                data.displayedTypeIds = _displayed.ToArray();

                string json = JsonUtility.ToJson(data);
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
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜落档失败: " + e.Message);
                return false;
            }
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
        }

        internal static void ResetStaticCaches()
        {
            NotifySlotChanged();
            _records.Clear();
        }

        #endregion
    }
}
