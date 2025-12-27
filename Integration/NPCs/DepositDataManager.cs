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
                
                // 分别加载三个列表
                List<ItemTreeData> items = SavesSystem.LoadGlobal<List<ItemTreeData>>(KEY_ITEMS, null);
                List<long> times = SavesSystem.LoadGlobal<List<long>>(KEY_TIMES, null);
                List<int> values = SavesSystem.LoadGlobal<List<int>>(KEY_VALUES, null);
                
                // 检查数据完整性
                if (items == null || times == null || values == null)
                {
                    ModBehaviour.DevLog("[DepositDataManager] 数据为空或不完整，初始化空列表");
                    isLoaded = true;
                    return;
                }
                
                // 检查列表长度一致性
                int count = Mathf.Min(items.Count, Mathf.Min(times.Count, values.Count));
                if (items.Count != times.Count || times.Count != values.Count)
                {
                    ModBehaviour.DevLog("[DepositDataManager] [WARNING] 数据长度不一致，使用最小长度: " + count);
                }
                
                // 重建缓存
                for (int i = 0; i < count; i++)
                {
                    if (items[i] == null) continue;
                    
                    DepositedItemData data = new DepositedItemData();
                    data.itemData = items[i];
                    data.depositTimeTicks = times[i];
                    data.itemValue = values[i];
                    cachedItems.Add(data);
                }
                
                isLoaded = true;
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
                // 构建三个分离的列表
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
                
                // 分别保存三个列表
                SavesSystem.SaveGlobal<List<ItemTreeData>>(KEY_ITEMS, items);
                SavesSystem.SaveGlobal<List<long>>(KEY_TIMES, times);
                SavesSystem.SaveGlobal<List<int>>(KEY_VALUES, values);
                
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
            return cachedItems;
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
    }
}
