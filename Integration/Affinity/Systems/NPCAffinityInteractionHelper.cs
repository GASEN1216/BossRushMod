// ============================================================================
// NPCAffinityInteractionHelper.cs - NPC好感度交互通用助手
// ============================================================================
// 模块说明：
//   统一管理所有NPC每日聊天好感度判定与聊天反馈显示逻辑，
//   避免各NPC各自维护一套实现导致行为不一致。
// ============================================================================

using System;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// NPC好感度交互通用助手
    /// </summary>
    public static class NPCAffinityInteractionHelper
    {
        /// <summary>
        /// 在NPC生成时检查并应用每日好感度衰减（统一入口）
        /// </summary>
        public static int ApplyDailyDecayOnSpawn(string npcId, string logPrefix)
        {
            try
            {
                int decayAmount = AffinityManager.CheckAndApplyDailyDecay(npcId);
                if (decayAmount > 0)
                {
                    ModBehaviour.DevLog(logPrefix + " 好感度衰减已应用: -" + decayAmount);
                }
                return decayAmount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 应用每日好感度衰减失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 判断今天是否还能通过聊天获得好感度
        /// </summary>
        public static bool CanGainDailyChatAffinity(string npcId)
        {
            try
            {
                int currentDay = NPCGiftSystem.GetCurrentGameDay();
                int lastChatDay = AffinityManager.GetLastChatDay(npcId);
                return currentDay != lastChatDay;
            }
            catch
            {
                // 出错时默认允许聊天，避免误伤交互流程
                return true;
            }
        }

        /// <summary>
        /// 尝试发放每日聊天好感度
        /// </summary>
        public static bool TryGrantDailyChatAffinity(string npcId, int dailyAffinityValue, out int gainedPoints)
        {
            gainedPoints = 0;
            if (!CanGainDailyChatAffinity(npcId)) return false;

            try
            {
                int oldPoints = AffinityManager.GetPoints(npcId);
                AffinityManager.AddPoints(npcId, dailyAffinityValue);
                int newPoints = AffinityManager.GetPoints(npcId);

                // 返回实际增量，避免满级或接近满级时显示错误的(+x)
                gainedPoints = Math.Max(0, newPoints - oldPoints);
                AffinityManager.SetLastChatDay(npcId, NPCGiftSystem.GetCurrentGameDay());
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCAffinityHelper] 发放每日聊天好感度失败: " + e.Message);
                gainedPoints = 0;
                return false;
            }
        }

        /// <summary>
        /// 处理聊天相关的统一好感度逻辑和反馈展示
        /// </summary>
        /// <param name="npcId">NPC唯一标识</param>
        /// <param name="dailyAffinityValue">每日聊天好感度增量</param>
        /// <param name="loveHeartLevelThreshold">触发爱心反馈的等级阈值</param>
        /// <param name="showLoveHeartAction">显示爱心反馈动作（可选）</param>
        /// <param name="logPrefix">日志前缀，如 [GoblinNPC]</param>
        /// <returns>本次实际增加的好感度</returns>
        public static int ProcessChatAffinityAndFeedback(
            string npcId,
            int dailyAffinityValue,
            int loveHeartLevelThreshold,
            Action showLoveHeartAction,
            string logPrefix)
        {
            int gainedPoints = 0;

            try
            {
                if (TryGrantDailyChatAffinity(npcId, dailyAffinityValue, out gainedPoints))
                {
                    ModBehaviour.DevLog(logPrefix + " 今日首次对话，好感度增加: " + gainedPoints);
                }

                int currentLevel = AffinityManager.GetLevel(npcId);
                if (currentLevel >= loveHeartLevelThreshold && showLoveHeartAction != null)
                {
                    try
                    {
                        showLoveHeartAction();
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog(logPrefix + " [WARNING] 爱心反馈动作执行失败: " + e.Message);
                    }

                    ModBehaviour.DevLog(logPrefix + " 好感度等级 " + currentLevel + " >= " + loveHeartLevelThreshold + "，显示冒爱心特效");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 处理聊天好感度逻辑失败: " + e.Message);
            }

            ShowChatProgressBanner(npcId, gainedPoints);
            return gainedPoints;
        }

        /// <summary>
        /// 显示聊天后的好感度进度反馈（统一为横幅）
        /// </summary>
        public static void ShowChatProgressBanner(string npcId, int gainedPoints)
        {
            try
            {
                var config = AffinityManager.GetNPCConfig(npcId);
                string npcName = config != null ? config.DisplayName : npcId;

                int currentLevel = AffinityManager.GetLevel(npcId);
                int maxLevel = AffinityManager.UNIFIED_MAX_LEVEL;

                string notificationText;
                if (currentLevel >= maxLevel)
                {
                    notificationText = L10n.T(
                        npcName + "好感度 Lv." + currentLevel + " (MAX)",
                        npcName + " Affinity Lv." + currentLevel + " (MAX)");
                }
                else
                {
                    int currentLevelProgress;
                    int pointsNeededForNextLevel;
                    AffinityManager.GetLevelProgressDetails(npcId, out currentLevelProgress, out pointsNeededForNextLevel);
                    notificationText = L10n.T(
                        npcName + "好感度 Lv." + currentLevel + " 进度 " + currentLevelProgress + "/" + pointsNeededForNextLevel,
                        npcName + " Affinity Lv." + currentLevel + " Progress " + currentLevelProgress + "/" + pointsNeededForNextLevel);
                }

                if (gainedPoints > 0)
                {
                    notificationText += " (+" + gainedPoints + ")";
                }

                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.ShowBigBanner(notificationText);
                }
                else
                {
                    NotificationText.Push(notificationText);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCAffinityHelper] 显示聊天反馈失败: " + e.Message);
            }
        }
    }
}
