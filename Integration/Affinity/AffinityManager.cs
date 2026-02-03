// ============================================================================
// AffinityManager.cs - 好感度管理器
// ============================================================================
// 模块说明：
//   核心单例类，管理所有NPC的好感度数据的读写和业务逻辑。
//   支持多NPC扩展，通过 RegisterNPC() 注册新NPC配置。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ReplaceThisWithYourModNameSpace;

namespace BossRush
{
    /// <summary>
    /// 好感度管理器 - 管理所有NPC的好感度
    /// </summary>
    public static class AffinityManager
    {
        // ============================================================================
        // 统一等级配置（所有NPC共用）
        // ============================================================================
        
        /// <summary>
        /// 各等级所需累计点数（递增式，所有NPC统一使用）
        /// 2级50点，3级150点，4级300点...10级2300点
        /// </summary>
        private static readonly int[] LevelPointsRequired = new int[]
        {
            0,      // 1级: 0点（起始）
            50,     // 2级: 50点（快速解锁基础功能）
            150,    // 3级: 150点（本级需100点）
            300,    // 4级: 300点（本级需150点）
            500,    // 5级: 500点（本级需200点）
            750,    // 6级: 750点（本级需250点）
            1050,   // 7级: 1050点（本级需300点）
            1400,   // 8级: 1400点（本级需350点）
            1800,   // 9级: 1800点（本级需400点）
            2300    // 10级: 2300点（本级需500点）
        };
        
        /// <summary>统一最大等级</summary>
        public const int UNIFIED_MAX_LEVEL = 10;
        
        /// <summary>统一最大点数（10级所需点数）</summary>
        public const int UNIFIED_MAX_POINTS = 2300;
        
        // ============================================================================
        // 状态
        // ============================================================================
        
        /// <summary>所有NPC的好感度数据</summary>
        private static Dictionary<string, AffinityData> npcDataMap = new Dictionary<string, AffinityData>();
        
        /// <summary>已注册的NPC配置</summary>
        private static Dictionary<string, INPCAffinityConfig> npcConfigMap = new Dictionary<string, INPCAffinityConfig>();
        
        /// <summary>是否已初始化</summary>
        private static bool isInitialized = false;
        
        /// <summary>数据是否有变更（脏标记）</summary>
        private static bool isDirty = false;
        
        /// <summary>上次保存时间</summary>
        private static float lastSaveTime = 0f;
        
        /// <summary>延迟保存间隔（秒）</summary>
        private const float SAVE_DELAY = 5f;
        
        // ============================================================================
        // 事件
        // ============================================================================
        
        /// <summary>
        /// 好感度变化事件（npcId, 旧点数, 新点数）
        /// </summary>
        public static event Action<string, int, int> OnAffinityChanged;
        
        /// <summary>
        /// 等级提升事件（npcId, 新等级）
        /// </summary>
        public static event Action<string, int> OnLevelUp;
        
        // ============================================================================
        // 初始化
        // ============================================================================
        
        /// <summary>
        /// 初始化好感度管理器
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;
            
            ModBehaviour.DevLog("[Affinity] 初始化好感度管理器...");
            
            // 清空数据
            npcDataMap.Clear();
            npcConfigMap.Clear();
            
            // 重置脏标记
            isDirty = false;
            lastSaveTime = Time.realtimeSinceStartup;
            
            // 加载存档
            Load();
            
            isInitialized = true;
            ModBehaviour.DevLog("[Affinity] 好感度管理器初始化完成");
        }
        
        /// <summary>
        /// 更新延迟保存（应在 ModBehaviour.Update 中调用）
        /// </summary>
        public static void UpdateDeferredSave()
        {
            if (!isDirty) return;
            
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - lastSaveTime >= SAVE_DELAY)
            {
                SaveImmediate();
            }
        }
        
        /// <summary>
        /// 强制立即保存（场景切换或退出时调用）
        /// </summary>
        public static void FlushSave()
        {
            if (isDirty)
            {
                SaveImmediate();
            }
        }
        
        /// <summary>
        /// 注册NPC配置
        /// </summary>
        public static void RegisterNPC(INPCAffinityConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.NpcId))
            {
                ModBehaviour.DevLog("[Affinity] [WARNING] 注册NPC失败：配置为空或NpcId为空");
                return;
            }
            
            string npcId = config.NpcId;
            
            // 注册配置
            npcConfigMap[npcId] = config;
            
            // 确保数据存在
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            ModBehaviour.DevLog("[Affinity] 已注册NPC: " + config.DisplayName + " (" + npcId + ")");
        }
        
        // ============================================================================
        // 好感度操作
        // ============================================================================
        
        /// <summary>
        /// 获取指定NPC的好感度点数
        /// </summary>
        public static int GetPoints(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return 0;
            
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.points;
            }
            return 0;
        }
        
        /// <summary>
        /// 获取指定NPC的好感度等级（使用统一递增式等级配置）
        /// </summary>
        public static int GetLevel(string npcId)
        {
            int points = GetPoints(npcId);
            return GetLevelFromPoints(points);
        }
        
        /// <summary>
        /// 根据点数计算等级（统一递增式计算）
        /// </summary>
        public static int GetLevelFromPoints(int points)
        {
            // 从高到低遍历，找到第一个满足条件的等级
            for (int level = UNIFIED_MAX_LEVEL; level >= 1; level--)
            {
                if (points >= GetPointsRequiredForLevel(level))
                {
                    return level;
                }
            }
            return 1;  // 默认1级
        }
        
        /// <summary>
        /// 获取达到指定等级所需的累计好感度点数（统一配置）
        /// </summary>
        public static int GetPointsRequiredForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level > UNIFIED_MAX_LEVEL) return LevelPointsRequired[UNIFIED_MAX_LEVEL - 1];
            return LevelPointsRequired[level - 1];
        }
        
        /// <summary>
        /// 获取指定NPC的当前等级进度（0.0 ~ 1.0）
        /// 使用统一递增式等级配置
        /// </summary>
        public static float GetLevelProgress(string npcId)
        {
            int points = GetPoints(npcId);
            int level = GetLevel(npcId);
            
            // 满级时进度为1.0
            if (level >= UNIFIED_MAX_LEVEL) return 1.0f;
            
            // 计算当前等级的起始点数和下一级所需点数
            int currentLevelStart = GetPointsRequiredForLevel(level);
            int nextLevelRequired = GetPointsRequiredForLevel(level + 1);
            int pointsNeededForNextLevel = nextLevelRequired - currentLevelStart;
            
            // 计算当前等级内的进度
            int currentLevelProgress = points - currentLevelStart;
            return (float)currentLevelProgress / pointsNeededForNextLevel;
        }
        
        /// <summary>
        /// 获取当前等级内的进度点数和本级所需点数
        /// </summary>
        /// <param name="npcId">NPC ID</param>
        /// <param name="currentProgress">输出：当前等级内已获得的点数</param>
        /// <param name="levelRequired">输出：本级升级所需的点数</param>
        public static void GetLevelProgressDetails(string npcId, out int currentProgress, out int levelRequired)
        {
            int points = GetPoints(npcId);
            int level = GetLevel(npcId);
            
            if (level >= UNIFIED_MAX_LEVEL)
            {
                // 满级
                currentProgress = 0;
                levelRequired = 0;
            }
            else
            {
                int currentLevelStart = GetPointsRequiredForLevel(level);
                int nextLevelRequired = GetPointsRequiredForLevel(level + 1);
                currentProgress = points - currentLevelStart;
                levelRequired = nextLevelRequired - currentLevelStart;
            }
        }
        
        /// <summary>
        /// 增加指定NPC的好感度点数
        /// </summary>
        public static void AddPoints(string npcId, int amount)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            // 确保数据存在
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            AffinityData data = npcDataMap[npcId];
            int oldPoints = data.points;
            int oldLevel = GetLevel(npcId);
            
            // 计算新点数（约束范围）
            int maxPoints = GetMaxPoints(npcId);
            int newPoints = Math.Max(0, Math.Min(maxPoints, oldPoints + amount));
            data.points = newPoints;
            
            // 触发事件
            if (newPoints != oldPoints)
            {
                OnAffinityChanged?.Invoke(npcId, oldPoints, newPoints);
                
                // 检查等级提升
                int newLevel = GetLevel(npcId);
                if (newLevel > oldLevel)
                {
                    OnLevelUp?.Invoke(npcId, newLevel);
                    ModBehaviour.DevLog("[Affinity] " + npcId + " 等级提升: " + oldLevel + " -> " + newLevel);
                }
                
                // 标记需要保存（延迟保存）
                MarkDirty();
            }
        }
        
        /// <summary>
        /// 设置指定NPC的好感度点数
        /// </summary>
        public static void SetPoints(string npcId, int points)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            // 确保数据存在
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            AffinityData data = npcDataMap[npcId];
            int oldPoints = data.points;
            int oldLevel = GetLevel(npcId);
            
            // 约束范围
            int maxPoints = GetMaxPoints(npcId);
            int newPoints = Math.Max(0, Math.Min(maxPoints, points));
            data.points = newPoints;
            
            // 触发事件
            if (newPoints != oldPoints)
            {
                OnAffinityChanged?.Invoke(npcId, oldPoints, newPoints);
                
                // 检查等级提升
                int newLevel = GetLevel(npcId);
                if (newLevel > oldLevel)
                {
                    OnLevelUp?.Invoke(npcId, newLevel);
                }
                
                // 标记需要保存（延迟保存）
                MarkDirty();
            }
        }
        
        // ============================================================================
        // 解锁和折扣
        // ============================================================================
        
        /// <summary>
        /// 检查指定NPC是否达到解锁等级
        /// </summary>
        public static bool IsUnlocked(string npcId, int requiredLevel)
        {
            return GetLevel(npcId) >= requiredLevel;
        }
        
        /// <summary>
        /// 获取指定NPC的当前折扣率
        /// </summary>
        public static float GetDiscount(string npcId)
        {
            if (!npcConfigMap.TryGetValue(npcId, out INPCAffinityConfig config))
            {
                return 0f;
            }
            
            int level = GetLevel(npcId);
            float discount = 0f;
            
            // 查找当前等级对应的最高折扣
            if (config.DiscountsByLevel != null)
            {
                foreach (var kvp in config.DiscountsByLevel)
                {
                    if (level >= kvp.Key && kvp.Value > discount)
                    {
                        discount = kvp.Value;
                    }
                }
            }
            
            return discount;
        }

        // ============================================================================
        // 礼物系统辅助
        // ============================================================================
        
        /// <summary>
        /// 获取指定NPC上次赠送礼物的日期
        /// </summary>
        public static int GetLastGiftDay(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.lastGiftDay;
            }
            return -1;
        }
        
        /// <summary>
        /// 设置指定NPC上次赠送礼物的日期
        /// </summary>
        public static void SetLastGiftDay(string npcId, int day)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            npcDataMap[npcId].lastGiftDay = day;
            MarkDirty();
        }
        
        /// <summary>
        /// 获取指定NPC上次赠送礼物的反应类型
        /// </summary>
        /// <returns>0=普通, 1=喜欢, -1=不喜欢</returns>
        public static int GetLastGiftReaction(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.lastGiftReaction;
            }
            return 0;
        }
        
        /// <summary>
        /// 设置指定NPC上次赠送礼物的反应类型
        /// </summary>
        /// <param name="reaction">0=普通, 1=喜欢, -1=不喜欢</param>
        public static void SetLastGiftReaction(string npcId, int reaction)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            npcDataMap[npcId].lastGiftReaction = reaction;
            MarkDirty();
        }
        
        /// <summary>
        /// 获取指定NPC上次对话的日期
        /// </summary>
        public static int GetLastChatDay(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.lastChatDay;
            }
            return -1;
        }
        
        /// <summary>
        /// 设置指定NPC上次对话的日期
        /// </summary>
        public static void SetLastChatDay(string npcId, int day)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            npcDataMap[npcId].lastChatDay = day;
            MarkDirty();
        }
        
        /// <summary>
        /// 标记已遇见指定NPC
        /// </summary>
        public static void MarkMet(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            if (!npcDataMap[npcId].hasMet)
            {
                npcDataMap[npcId].hasMet = true;
                MarkDirty();
            }
        }
        
        /// <summary>
        /// 检查是否已遇见指定NPC
        /// </summary>
        public static bool HasMet(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.hasMet;
            }
            return false;
        }
        
        // ============================================================================
        // 故事对话触发状态（持久化）
        // ============================================================================
        
        /// <summary>
        /// 检查指定NPC的5级故事是否已触发
        /// </summary>
        public static bool HasTriggeredStory5(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.hasTriggeredStory5;
            }
            return false;
        }
        
        /// <summary>
        /// 标记指定NPC的5级故事已触发
        /// </summary>
        public static void MarkStory5Triggered(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            if (!npcDataMap[npcId].hasTriggeredStory5)
            {
                npcDataMap[npcId].hasTriggeredStory5 = true;
                MarkDirty();
                ModBehaviour.DevLog("[Affinity] " + npcId + " 5级故事已标记为触发");
            }
        }
        
        /// <summary>
        /// 检查指定NPC的10级故事是否已触发
        /// </summary>
        public static bool HasTriggeredStory10(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.hasTriggeredStory10;
            }
            return false;
        }
        
        /// <summary>
        /// 标记指定NPC的10级故事已触发
        /// </summary>
        public static void MarkStory10Triggered(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            if (!npcDataMap[npcId].hasTriggeredStory10)
            {
                npcDataMap[npcId].hasTriggeredStory10 = true;
                MarkDirty();
                ModBehaviour.DevLog("[Affinity] " + npcId + " 10级故事已标记为触发");
            }
        }
        
        // ============================================================================
        // 每日衰减系统
        // ============================================================================
        
        /// <summary>
        /// 检查并应用每日好感度衰减（累积衰减版本）
        /// 如果玩家多天没有与NPC互动（聊天或送礼），则累积扣除好感度
        /// 当天有聊天机会，所以当天不计入衰减
        /// 应在游戏加载或新的一天开始时调用
        /// </summary>
        /// <param name="npcId">NPC标识符</param>
        /// <returns>衰减的点数（0表示没有衰减）</returns>
        public static int CheckAndApplyDailyDecay(string npcId)
        {
            // 检查是否启用衰减
            if (!AffinityConfig.ENABLE_DAILY_DECAY) return 0;
            if (string.IsNullOrEmpty(npcId)) return 0;
            
            // 确保数据存在
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            AffinityData data = npcDataMap[npcId];
            int currentDay = GetCurrentGameDay();
            
            // 如果是第一次检查（lastDecayCheckDay == -1），初始化为当前日期，不衰减
            if (data.lastDecayCheckDay < 0)
            {
                data.lastDecayCheckDay = currentDay;
                MarkDirty();
                ModBehaviour.DevLog("[Affinity] " + npcId + " 首次衰减检查，初始化日期: " + currentDay);
                return 0;
            }
            
            // 如果今天已经检查过，不重复衰减
            if (data.lastDecayCheckDay >= currentDay)
            {
                return 0;
            }
            
            // 计算需要检查的天数范围：从上次检查日+1 到 昨天（当天不算，因为当天有聊天机会）
            // 例如：上次检查日=1，今天=4，需要检查第2、3天是否有互动
            int startDay = data.lastDecayCheckDay + 1;
            int endDay = currentDay - 1;  // 当天不算衰减
            
            // 防止异常情况：限制最大衰减天数为30天
            if (endDay - startDay + 1 > 30)
            {
                ModBehaviour.DevLog("[Affinity] " + npcId + " 衰减天数异常(" + (endDay - startDay + 1) + ")，限制为30天");
                startDay = endDay - 29;
            }
            
            // 如果没有需要检查的天数（例如今天=昨天+1，即连续两天都来了）
            if (startDay > endDay)
            {
                // 更新检查日期
                data.lastDecayCheckDay = currentDay;
                MarkDirty();
                return 0;
            }
            
            // 计算累积衰减
            // 逻辑：检查从 startDay 到 endDay 每一天是否有互动
            // 如果某天没有互动，则累积一次衰减
            int totalDecay = 0;
            int daysWithoutInteraction = 0;
            
            for (int day = startDay; day <= endDay; day++)
            {
                // 检查这一天是否有互动
                bool hadInteractionOnDay = (data.lastChatDay == day) || (data.lastGiftDay == day);
                
                if (!hadInteractionOnDay)
                {
                    daysWithoutInteraction++;
                }
            }
            
            // 计算总衰减量
            if (daysWithoutInteraction > 0 && data.points > 0)
            {
                totalDecay = daysWithoutInteraction * AffinityConfig.DAILY_DECAY_AMOUNT;
            }
            
            // 应用衰减
            if (totalDecay > 0)
            {
                int oldPoints = data.points;
                data.points = Math.Max(0, data.points - totalDecay);
                
                ModBehaviour.DevLog("[Affinity] " + npcId + " 好感度累积衰减: " + oldPoints + " -> " + data.points + 
                    " (衰减" + totalDecay + "点, " + daysWithoutInteraction + "天未互动)");
                
                // 触发事件
                OnAffinityChanged?.Invoke(npcId, oldPoints, data.points);
            }
            else
            {
                ModBehaviour.DevLog("[Affinity] " + npcId + " 无需衰减 (检查范围: 第" + startDay + "天~第" + endDay + "天)");
            }
            
            // 更新检查日期
            data.lastDecayCheckDay = currentDay;
            MarkDirty();
            
            return totalDecay;
        }
        
        /// <summary>
        /// 检查所有已注册NPC的每日衰减
        /// </summary>
        /// <returns>总衰减点数</returns>
        public static int CheckAndApplyAllDailyDecay()
        {
            int totalDecay = 0;
            foreach (var npcId in npcConfigMap.Keys)
            {
                totalDecay += CheckAndApplyDailyDecay(npcId);
            }
            return totalDecay;
        }
        
        /// <summary>
        /// 获取指定NPC上次衰减检查的日期
        /// </summary>
        public static int GetLastDecayCheckDay(string npcId)
        {
            if (npcDataMap.TryGetValue(npcId, out AffinityData data))
            {
                return data.lastDecayCheckDay;
            }
            return -1;
        }
        
        /// <summary>
        /// 设置指定NPC上次衰减检查的日期（调试用）
        /// </summary>
        public static void SetLastDecayCheckDay(string npcId, int day)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            if (!npcDataMap.ContainsKey(npcId))
            {
                npcDataMap[npcId] = new AffinityData(npcId);
            }
            
            npcDataMap[npcId].lastDecayCheckDay = day;
            MarkDirty();
        }
        
        /// <summary>
        /// 获取当前游戏日期
        /// </summary>
        private static int GetCurrentGameDay()
        {
            try
            {
                return (int)GameClock.Day;
            }
            catch
            {
                // 如果无法获取游戏日期，使用系统日期
                return DateTime.Now.DayOfYear;
            }
        }
        
        // ============================================================================
        // 配置获取
        // ============================================================================
        
        /// <summary>
        /// 获取指定NPC的配置
        /// </summary>
        public static INPCAffinityConfig GetNPCConfig(string npcId)
        {
            if (npcConfigMap.TryGetValue(npcId, out INPCAffinityConfig config))
            {
                return config;
            }
            return null;
        }
        
        /// <summary>
        /// 获取指定NPC的最大点数（使用统一配置）
        /// </summary>
        private static int GetMaxPoints(string npcId)
        {
            // 使用统一最大点数
            return UNIFIED_MAX_POINTS;
        }
        
        /// <summary>
        /// 获取指定NPC的每级点数（已废弃，保留兼容性）
        /// </summary>
        [System.Obsolete("使用递增式等级配置，此方法仅保留兼容性")]
        private static int GetPointsPerLevel(string npcId)
        {
            if (npcConfigMap.TryGetValue(npcId, out INPCAffinityConfig config))
            {
                return config.PointsPerLevel;
            }
            return AffinityConfig.DEFAULT_POINTS_PER_LEVEL;
        }
        
        /// <summary>
        /// 获取指定NPC的最大等级（使用统一配置）
        /// </summary>
        private static int GetMaxLevel(string npcId)
        {
            // 使用统一最大等级
            return UNIFIED_MAX_LEVEL;
        }
        
        // ============================================================================
        // 存档
        // ============================================================================
        
        /// <summary>
        /// 标记数据已变更，需要保存
        /// </summary>
        private static void MarkDirty()
        {
            isDirty = true;
        }
        
        /// <summary>
        /// 保存所有好感度数据（延迟保存入口）
        /// </summary>
        public static void Save()
        {
            // 标记脏数据，等待延迟保存
            MarkDirty();
        }
        
        /// <summary>
        /// 立即保存所有好感度数据
        /// </summary>
        private static void SaveImmediate()
        {
            try
            {
                // 构建存档数据
                AllAffinityData allData = new AllAffinityData();
                foreach (var kvp in npcDataMap)
                {
                    allData.npcDataList.Add(kvp.Value.Clone());
                }
                
                // 序列化为JSON
                string json = JsonUtility.ToJson(allData);
                
                // 使用 ModConfigAPI 保存
                ModConfigAPI.SafeSave(AffinityConfig.MOD_NAME, AffinityConfig.SAVE_KEY, json);
                
                // 重置脏标记和保存时间
                isDirty = false;
                lastSaveTime = Time.realtimeSinceStartup;
                
                ModBehaviour.DevLog("[Affinity] 好感度数据已保存");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Affinity] [WARNING] 保存好感度数据失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 加载所有好感度数据
        /// </summary>
        public static void Load()
        {
            try
            {
                // 使用 ModConfigAPI 加载
                string json = ModConfigAPI.SafeLoad(AffinityConfig.MOD_NAME, AffinityConfig.SAVE_KEY, "");
                
                if (string.IsNullOrEmpty(json))
                {
                    ModBehaviour.DevLog("[Affinity] 没有找到存档数据，使用默认值");
                    return;
                }
                
                // 反序列化
                AllAffinityData allData = JsonUtility.FromJson<AllAffinityData>(json);
                
                if (allData != null && allData.npcDataList != null)
                {
                    npcDataMap.Clear();
                    foreach (var data in allData.npcDataList)
                    {
                        if (!string.IsNullOrEmpty(data.npcId))
                        {
                            npcDataMap[data.npcId] = data;
                        }
                    }
                    ModBehaviour.DevLog("[Affinity] 好感度数据已加载，NPC数量: " + npcDataMap.Count);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Affinity] [WARNING] 加载好感度数据失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 重置所有好感度数据（调试用）
        /// </summary>
        public static void ResetAll()
        {
            npcDataMap.Clear();
            SaveImmediate();  // 重置时立即保存
            ModBehaviour.DevLog("[Affinity] 所有好感度数据已重置");
        }
        
        /// <summary>
        /// 清理资源（场景切换时调用）
        /// </summary>
        public static void OnSceneUnload()
        {
            // 强制保存未保存的数据
            FlushSave();
        }
    }
}
