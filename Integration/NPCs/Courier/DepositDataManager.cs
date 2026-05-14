// ============================================================================
// DepositDataManager.cs - 寄存数据管理器
// ============================================================================
// 模块说明：
//   管理"阿稳寄存"服务的数据持久化，使用原版 SavesSystem.SaveGlobal/LoadGlobal API
//   数据保存到 Global.json，实现多存档互通
//   
// 存储结构：
//   - BossRush_Deposit_Items: List<ItemTreeData> 物品数据列表
//   - BossRush_Deposit_Times: List<long> 存入时间列表（Ticks）
//   - BossRush_Deposit_Values: List<int> 物品原始价值列表
//   - BossRush_Deposit_*Generation: 三个列表的提交代号
//   - BossRush_Deposit_Backup_*: 上一次完整提交的备份槽
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Saves;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush
{
    /// <summary>
    /// 寄存物品数据（单个物品，运行时使用）
    /// </summary>
    public class DepositedItemData
    {
        /// <summary>物品数据（原版序列化格式）</summary>
        public ItemTreeData itemData;
        
        /// <summary>存入时间（DateTime.UtcNow.Ticks）</summary>
        public long depositTimeTicks;
        
        /// <summary>物品原始价值</summary>
        public int itemValue;
        
        /// <summary>
        /// 获取存入时间
        /// </summary>
        public DateTime GetDepositTime()
        {
            return new DateTime(depositTimeTicks, DateTimeKind.Utc);
        }
        
        /// <summary>
        /// 计算当前寄存费
        /// 公式：物品价值 × (1% + 存放分钟数 × 1%)
        /// </summary>
        public int GetCurrentFee()
        {
            int minutes = (int)(DateTime.UtcNow - GetDepositTime()).TotalMinutes;
            if (minutes < 0) minutes = 0;  // 防止时间回退导致负数
            float feeRate = DepositDataManager.BASE_FEE_RATE + minutes * DepositDataManager.FEE_RATE_PER_MINUTE;
            return Mathf.CeilToInt(itemValue * feeRate);
        }
    }
    
    /// <summary>
    /// 寄存数据管理器（静态类）
    /// 使用分离的列表存储，避免 ES3 序列化嵌套自定义类的问题
    /// </summary>
    public static class DepositDataManager
    {
        // ============================================================================
        // 常量
        // ============================================================================
        
        /// <summary>物品数据存储键</summary>
        private const string KEY_ITEMS = "BossRush_Deposit_Items";
        
        /// <summary>存入时间存储键</summary>
        private const string KEY_TIMES = "BossRush_Deposit_Times";
        
        /// <summary>物品价值存储键</summary>
        private const string KEY_VALUES = "BossRush_Deposit_Values";

        /// <summary>主数据提交代号键</summary>
        private const string KEY_COMMIT_GENERATION = "BossRush_Deposit_CommitGeneration";

        /// <summary>主数据物品列表提交代号键</summary>
        private const string KEY_ITEMS_GENERATION = "BossRush_Deposit_ItemsGeneration";

        /// <summary>主数据时间列表提交代号键</summary>
        private const string KEY_TIMES_GENERATION = "BossRush_Deposit_TimesGeneration";

        /// <summary>主数据价值列表提交代号键</summary>
        private const string KEY_VALUES_GENERATION = "BossRush_Deposit_ValuesGeneration";

        /// <summary>备份物品数据存储键</summary>
        private const string KEY_BACKUP_ITEMS = "BossRush_Deposit_Backup_Items";

        /// <summary>备份存入时间存储键</summary>
        private const string KEY_BACKUP_TIMES = "BossRush_Deposit_Backup_Times";

        /// <summary>备份物品价值存储键</summary>
        private const string KEY_BACKUP_VALUES = "BossRush_Deposit_Backup_Values";

        /// <summary>备份提交代号键</summary>
        private const string KEY_BACKUP_COMMIT_GENERATION = "BossRush_Deposit_Backup_CommitGeneration";

        /// <summary>备份物品列表提交代号键</summary>
        private const string KEY_BACKUP_ITEMS_GENERATION = "BossRush_Deposit_Backup_ItemsGeneration";

        /// <summary>备份时间列表提交代号键</summary>
        private const string KEY_BACKUP_TIMES_GENERATION = "BossRush_Deposit_Backup_TimesGeneration";

        /// <summary>备份价值列表提交代号键</summary>
        private const string KEY_BACKUP_VALUES_GENERATION = "BossRush_Deposit_Backup_ValuesGeneration";
        
        /// <summary>基础费率（1%）</summary>
        public const float BASE_FEE_RATE = 0.01f;
        
        /// <summary>每分钟增长费率（约0.0098%，7天达到100%）</summary>
        public const float FEE_RATE_PER_MINUTE = 0.000098f;
        
        // ============================================================================
        // 私有字段
        // ============================================================================
        
        /// <summary>缓存的物品数据列表</summary>
        private static List<DepositedItemData> cachedItems = new List<DepositedItemData>();
        
        /// <summary>是否已加载</summary>
        private static bool isLoaded = false;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 加载寄存数据（从 Global.json）
        /// </summary>
        public static void Load()
        {
            try
            {
                cachedItems.Clear();

                List<ItemTreeData> items;
                List<long> times;
                List<int> values;
                bool recoveredFromBackup = false;
                bool repairedFromLegacy = false;

                if (!TryLoadCommittedDepositLists(out items, out times, out values))
                {
                    if (TryLoadBackupDepositLists(out items, out times, out values))
                    {
                        recoveredFromBackup = true;
                        ModBehaviour.DevLog("[DepositDataManager] [WARNING] 主寄存数据未完整提交，已从备份槽恢复");
                    }
                    else if (TryLoadLegacyMinimumDepositLists(out items, out times, out values))
                    {
                        repairedFromLegacy = true;
                        ModBehaviour.DevLog("[DepositDataManager] [WARNING] 未找到完整提交快照，使用旧格式最小一致长度恢复");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[DepositDataManager] 数据为空或不完整，初始化空列表");
                        isLoaded = true;
                        return;
                    }
                }

                RebuildCacheFromLists(items, times, values, recoveredFromBackup || repairedFromLegacy);
                isLoaded = true;

                if (recoveredFromBackup || repairedFromLegacy)
                {
                    Save();
                }

                ModBehaviour.DevLog("[DepositDataManager] 数据加载成功，共 " + cachedItems.Count + " 件物品");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DepositDataManager] [ERROR] 加载数据失败: " + e.Message + "\n" + e.StackTrace);
                cachedItems.Clear();
                isLoaded = true;
            }
        }
        
        /// <summary>
        /// 保存寄存数据（到 Global.json）
        /// </summary>
        public static void Save()
        {
            try
            {
                List<ItemTreeData> items = new List<ItemTreeData>();
                List<long> times = new List<long>();
                List<int> values = new List<int>();

                foreach (var data in cachedItems)
                {
                    if (data == null || data.itemData == null) continue;
                    items.Add(data.itemData);
                    times.Add(data.depositTimeTicks);
                    values.Add(data.itemValue);
                }

                SaveBackupFromCurrentCommittedData();

                long generation = CreateNextDepositSaveGeneration(KEY_COMMIT_GENERATION);
                SaveDepositListsWithGeneration(
                    KEY_ITEMS,
                    KEY_TIMES,
                    KEY_VALUES,
                    KEY_ITEMS_GENERATION,
                    KEY_TIMES_GENERATION,
                    KEY_VALUES_GENERATION,
                    KEY_COMMIT_GENERATION,
                    generation,
                    items,
                    times,
                    values);

                ModBehaviour.DevLog("[DepositDataManager] 数据保存成功，共 " + items.Count + " 件物品");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DepositDataManager] [ERROR] 保存数据失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 添加寄存物品
        /// </summary>
        /// <param name="item">要寄存的物品</param>
        public static void AddItem(Item item)
        {
            EnsureLoaded();
            
            if (item == null)
            {
                ModBehaviour.DevLog("[DepositDataManager] [WARNING] 物品为空，跳过添加");
                return;
            }
            
            try
            {
                ReforgeDataPersistence.SyncCurrentReforgeState(item);

                // 创建寄存数据
                DepositedItemData depositedItem = new DepositedItemData();
                depositedItem.itemData = ItemTreeData.FromItem(item);
                depositedItem.depositTimeTicks = DateTime.UtcNow.Ticks;
                // 使用 GetTotalRawValue() 获取考虑耐久度损耗后的实际价值
                // 而不是 item.Value * item.StackCount（满耐久度价值）
                depositedItem.itemValue = item.GetTotalRawValue();
                
                cachedItems.Add(depositedItem);
                
                ModBehaviour.DevLog("[DepositDataManager] 添加物品: " + item.DisplayName + 
                    ", 价值: " + depositedItem.itemValue + 
                    ", 当前费用: " + depositedItem.GetCurrentFee());
                
                // 立即保存
                Save();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DepositDataManager] [ERROR] 添加物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 移除寄存物品（取回时）
        /// </summary>
        /// <param name="index">物品索引</param>
        public static void RemoveItem(int index)
        {
            EnsureLoaded();
            
            if (index < 0 || index >= cachedItems.Count)
            {
                ModBehaviour.DevLog("[DepositDataManager] [WARNING] 索引越界: " + index);
                return;
            }
            
            try
            {
                cachedItems.RemoveAt(index);
                ModBehaviour.DevLog("[DepositDataManager] 移除物品索引: " + index);
                
                // 立即保存
                Save();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DepositDataManager] [ERROR] 移除物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取所有寄存物品
        /// </summary>
        public static List<DepositedItemData> GetAllItems()
        {
            EnsureLoaded();
            return new List<DepositedItemData>(cachedItems);
        }
        
        /// <summary>
        /// 获取寄存物品数量
        /// </summary>
        public static int GetItemCount()
        {
            EnsureLoaded();
            return cachedItems.Count;
        }
        
        /// <summary>
        /// 清空所有寄存物品（调试用）
        /// </summary>
        public static void ClearAll()
        {
            EnsureLoaded();
            cachedItems.Clear();
            Save();
            ModBehaviour.DevLog("[DepositDataManager] 已清空所有寄存物品");
        }
        
        /// <summary>
        /// 强制重新加载数据
        /// </summary>
        public static void ForceReload()
        {
            isLoaded = false;
            Load();
        }
        
        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 确保数据已加载
        /// </summary>
        private static void EnsureLoaded()
        {
            if (!isLoaded)
            {
                Load();
            }
        }

        private static bool TryLoadCommittedDepositLists(
            out List<ItemTreeData> items,
            out List<long> times,
            out List<int> values)
        {
            items = SavesSystem.LoadGlobal<List<ItemTreeData>>(KEY_ITEMS, null);
            times = SavesSystem.LoadGlobal<List<long>>(KEY_TIMES, null);
            values = SavesSystem.LoadGlobal<List<int>>(KEY_VALUES, null);
            return IsCommittedDepositSnapshot(
                items,
                times,
                values,
                KEY_COMMIT_GENERATION,
                KEY_ITEMS_GENERATION,
                KEY_TIMES_GENERATION,
                KEY_VALUES_GENERATION);
        }

        private static bool TryLoadBackupDepositLists(
            out List<ItemTreeData> items,
            out List<long> times,
            out List<int> values)
        {
            items = SavesSystem.LoadGlobal<List<ItemTreeData>>(KEY_BACKUP_ITEMS, null);
            times = SavesSystem.LoadGlobal<List<long>>(KEY_BACKUP_TIMES, null);
            values = SavesSystem.LoadGlobal<List<int>>(KEY_BACKUP_VALUES, null);
            return IsCommittedDepositSnapshot(
                items,
                times,
                values,
                KEY_BACKUP_COMMIT_GENERATION,
                KEY_BACKUP_ITEMS_GENERATION,
                KEY_BACKUP_TIMES_GENERATION,
                KEY_BACKUP_VALUES_GENERATION);
        }

        private static bool TryLoadLegacyMinimumDepositLists(
            out List<ItemTreeData> items,
            out List<long> times,
            out List<int> values)
        {
            items = SavesSystem.LoadGlobal<List<ItemTreeData>>(KEY_ITEMS, null);
            times = SavesSystem.LoadGlobal<List<long>>(KEY_TIMES, null);
            values = SavesSystem.LoadGlobal<List<int>>(KEY_VALUES, null);
            return items != null && times != null && values != null;
        }

        private static bool IsCommittedDepositSnapshot(
            List<ItemTreeData> items,
            List<long> times,
            List<int> values,
            string commitGenerationKey,
            string itemsGenerationKey,
            string timesGenerationKey,
            string valuesGenerationKey)
        {
            if (items == null || times == null || values == null)
            {
                return false;
            }
            if (items.Count != times.Count || times.Count != values.Count)
            {
                return false;
            }

            long commitGeneration = SavesSystem.LoadGlobal<long>(commitGenerationKey, 0L);
            long itemsGeneration = SavesSystem.LoadGlobal<long>(itemsGenerationKey, 0L);
            long timesGeneration = SavesSystem.LoadGlobal<long>(timesGenerationKey, 0L);
            long valuesGeneration = SavesSystem.LoadGlobal<long>(valuesGenerationKey, 0L);

            return itemsGeneration == commitGeneration &&
                   timesGeneration == commitGeneration &&
                   valuesGeneration == commitGeneration;
        }

        private static void RebuildCacheFromLists(
            List<ItemTreeData> items,
            List<long> times,
            List<int> values,
            bool allowMinimumLengthRecovery)
        {
            int count = Mathf.Min(items.Count, Mathf.Min(times.Count, values.Count));
            if (items.Count != times.Count || times.Count != values.Count)
            {
                if (!allowMinimumLengthRecovery)
                {
                    ModBehaviour.DevLog("[DepositDataManager] [WARNING] 数据长度不一致，初始化空列表");
                    return;
                }
                ModBehaviour.DevLog("[DepositDataManager] [WARNING] 数据长度不一致，使用最小长度: " + count);
            }

            for (int i = 0; i < count; i++)
            {
                if (items[i] == null) continue;

                DepositedItemData data = new DepositedItemData();
                data.itemData = items[i];
                data.depositTimeTicks = times[i];
                data.itemValue = values[i];
                cachedItems.Add(data);
            }
        }

        private static void SaveBackupFromCurrentCommittedData()
        {
            try
            {
                List<ItemTreeData> existingItems;
                List<long> existingTimes;
                List<int> existingValues;
                if (!TryLoadCommittedDepositLists(out existingItems, out existingTimes, out existingValues))
                {
                    return;
                }

                long generation = CreateNextDepositSaveGeneration(KEY_BACKUP_COMMIT_GENERATION);
                SaveDepositListsWithGeneration(
                    KEY_BACKUP_ITEMS,
                    KEY_BACKUP_TIMES,
                    KEY_BACKUP_VALUES,
                    KEY_BACKUP_ITEMS_GENERATION,
                    KEY_BACKUP_TIMES_GENERATION,
                    KEY_BACKUP_VALUES_GENERATION,
                    KEY_BACKUP_COMMIT_GENERATION,
                    generation,
                    existingItems,
                    existingTimes,
                    existingValues);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DepositDataManager] [WARNING] 保存备份寄存数据失败: " + e.Message);
            }
        }

        private static void SaveDepositListsWithGeneration(
            string itemsKey,
            string timesKey,
            string valuesKey,
            string itemsGenerationKey,
            string timesGenerationKey,
            string valuesGenerationKey,
            string commitGenerationKey,
            long generation,
            List<ItemTreeData> items,
            List<long> times,
            List<int> values)
        {
            SavesSystem.SaveGlobal<List<ItemTreeData>>(itemsKey, items);
            SavesSystem.SaveGlobal<long>(itemsGenerationKey, generation);
            SavesSystem.SaveGlobal<List<long>>(timesKey, times);
            SavesSystem.SaveGlobal<long>(timesGenerationKey, generation);
            SavesSystem.SaveGlobal<List<int>>(valuesKey, values);
            SavesSystem.SaveGlobal<long>(valuesGenerationKey, generation);
            SavesSystem.SaveGlobal<long>(commitGenerationKey, generation);
        }

        private static long CreateNextDepositSaveGeneration(string commitGenerationKey)
        {
            long currentGeneration = SavesSystem.LoadGlobal<long>(commitGenerationKey, 0L);
            long now = DateTime.UtcNow.Ticks;
            return now > currentGeneration ? now : currentGeneration + 1L;
        }
    }
}
