using System;
using UnityEngine;

namespace BossRush
{
    public static partial class AffinityManager
    {
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

            // 如果今天已经检查过，不重复衰减
            if (data.lastDecayCheckDay >= currentDay)
            {
                return 0;
            }

            if (!HasAnyRecordedInteraction(data))
            {
                data.lastDecayCheckDay = currentDay;
                MarkDirty();
                ModBehaviour.DevLog("[Affinity] " + npcId + " 尚未发生有效互动，跳过衰减检查: " + currentDay);
                return 0;
            }

            // 如果是第一次检查（lastDecayCheckDay == -1），初始化为当前日期，不衰减
            if (data.lastDecayCheckDay < 0)
            {
                data.lastDecayCheckDay = currentDay;
                MarkDirty();
                ModBehaviour.DevLog("[Affinity] " + npcId + " 首次衰减检查，初始化日期: " + currentDay);
                return 0;
            }

            // 计算需要检查的天数范围：从上次检查日 到 昨天（当天不算，因为当天有聊天机会）
            // 例如：上次检查日=1，今天=4，需要检查第1、2、3天里尚未结算的部分；
            // 在按天进入场景的常规流程下，会表现为“第二天检查第一天，第三天检查第二天”。
            int startDay = data.lastDecayCheckDay;
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
                bool hadInteractionOnDay = HasInteractionOnDay(data, day);

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
        /// 使用游戏原生 Saves.SavesSystem 保存到缓存
        /// </summary>
        private static void SaveImmediate()
        {
            try
            {
                // 使用专用序列化器
                string json = AffinityJsonSerializer.Serialize(npcDataMap);

                // 保存到游戏存档系统
                Saves.SavesSystem.Save<string>(AffinityConfig.SAVE_KEY, json);

                // 重置脏标记和保存时间
                isDirty = false;
                lastSaveTime = Time.realtimeSinceStartup;

                ModBehaviour.DevLog("[Affinity] 好感度数据已保存，NPC数量: " + npcDataMap.Count);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Affinity] [WARNING] 保存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 加载所有好感度数据
        /// 使用游戏原生 Saves.SavesSystem 从存档加载
        /// </summary>
        public static void Load()
        {
            // 先清空数据，确保存档隔离
            npcDataMap.Clear();

            try
            {
                // 检查是否存在存档数据
                if (!Saves.SavesSystem.KeyExisits(AffinityConfig.SAVE_KEY))
                {
                    ModBehaviour.DevLog("[Affinity] 没有找到存档数据，使用默认值");
                    return;
                }

                // 加载JSON字符串
                string json = Saves.SavesSystem.Load<string>(AffinityConfig.SAVE_KEY);

                if (string.IsNullOrEmpty(json))
                {
                    ModBehaviour.DevLog("[Affinity] 存档数据为空，使用默认值");
                    return;
                }

                // 使用专用序列化器反序列化
                if (AffinityJsonSerializer.Deserialize(json, npcDataMap))
                {
                    ModBehaviour.DevLog("[Affinity] 好感度数据已加载，NPC数量: " + npcDataMap.Count);
                }
                else
                {
                    ModBehaviour.DevLog("[Affinity] [WARNING] JSON解析失败");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Affinity] [WARNING] 加载失败: " + e.Message);
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
