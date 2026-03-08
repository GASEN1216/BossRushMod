// ============================================================================
// NPCAffinityInteractionHelper.cs - NPC濂芥劅搴︿氦浜掗€氱敤鍔╂墜
// ============================================================================
// 妯″潡璇存槑锛?//   缁熶竴绠＄悊鎵€鏈塏PC姣忔棩鑱婂ぉ濂芥劅搴﹀垽瀹氫笌鑱婂ぉ鍙嶉鏄剧ず閫昏緫锛?//   閬垮厤鍚凬PC鍚勮嚜缁存姢涓€濂楀疄鐜板鑷磋涓轰笉涓€鑷淬€?// ============================================================================

using System;
using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// NPC濂芥劅搴︿氦浜掗€氱敤鍔╂墜
    /// </summary>
    public static class NPCAffinityInteractionHelper
    {
        /// <summary>
        /// 鍦∟PC鐢熸垚鏃舵鏌ュ苟搴旂敤姣忔棩濂芥劅搴﹁“鍑忥紙缁熶竴鍏ュ彛锛?        /// </summary>
        public static int ApplyDailyDecayOnSpawn(string npcId, string logPrefix)
        {
            try
            {
                int decayAmount = AffinityManager.CheckAndApplyDailyDecay(npcId);
                if (decayAmount > 0)
                {
                    ModBehaviour.DevLog(logPrefix + " 濂芥劅搴﹁“鍑忓凡搴旂敤: -" + decayAmount);
                }
                return decayAmount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 搴旂敤姣忔棩濂芥劅搴﹁“鍑忓け璐? " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 鍒ゆ柇浠婂ぉ鏄惁杩樿兘閫氳繃鑱婂ぉ鑾峰緱濂芥劅搴?        /// </summary>
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
                // 鍑洪敊鏃堕粯璁ゅ厑璁歌亰澶╋紝閬垮厤璇激浜や簰娴佺▼
                return true;
            }
        }

        /// <summary>
        /// 灏濊瘯鍙戞斁姣忔棩鑱婂ぉ濂芥劅搴?        /// </summary>
        public static bool TryGrantDailyChatAffinity(string npcId, int dailyAffinityValue, out int gainedPoints)
        {
            gainedPoints = 0;
            if (!CanGainDailyChatAffinity(npcId)) return false;

            try
            {
                int oldPoints = AffinityManager.GetPoints(npcId);
                AffinityManager.AddPoints(npcId, dailyAffinityValue);
                int newPoints = AffinityManager.GetPoints(npcId);

                // 杩斿洖瀹為檯澧為噺锛岄伩鍏嶆弧绾ф垨鎺ヨ繎婊＄骇鏃舵樉绀洪敊璇殑(+x)
                gainedPoints = Math.Max(0, newPoints - oldPoints);
                AffinityManager.SetLastChatDay(npcId, NPCGiftSystem.GetCurrentGameDay());
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCAffinityHelper] 鍙戞斁姣忔棩鑱婂ぉ濂芥劅搴﹀け璐? " + e.Message);
                gainedPoints = 0;
                return false;
            }
        }

        /// <summary>
        /// 澶勭悊鑱婂ぉ鐩稿叧鐨勭粺涓€濂芥劅搴﹂€昏緫鍜屽弽棣堝睍绀?        /// </summary>
        /// <param name="npcId">NPC鍞竴鏍囪瘑</param>
        /// <param name="dailyAffinityValue">姣忔棩鑱婂ぉ濂芥劅搴﹀閲?/param>
        /// <param name="loveHeartLevelThreshold">瑙﹀彂鐖卞績鍙嶉鐨勭瓑绾ч槇鍊?/param>
        /// <param name="showLoveHeartAction">鏄剧ず鐖卞績鍙嶉鍔ㄤ綔锛堝彲閫夛級</param>
        /// <param name="logPrefix">鏃ュ織鍓嶇紑锛屽 [GoblinNPC]</param>
        /// <returns>鏈瀹為檯澧炲姞鐨勫ソ鎰熷害</returns>
        public static int ProcessChatAffinityAndFeedback(
            string npcId,
            int dailyAffinityValue,
            int loveHeartLevelThreshold,
            Action showLoveHeartAction,
            string logPrefix)
        {
            bool ignoredDailyChatGranted;
            return ProcessChatAffinityAndFeedback(
                npcId,
                dailyAffinityValue,
                loveHeartLevelThreshold,
                showLoveHeartAction,
                logPrefix,
                out ignoredDailyChatGranted);
        }

        public static int ProcessChatAffinityAndFeedback(
            string npcId,
            int dailyAffinityValue,
            int loveHeartLevelThreshold,
            Action showLoveHeartAction,
            string logPrefix,
            out bool dailyChatGranted)
        {
            int gainedPoints = 0;
            int levelBeforeChat = AffinityManager.GetLevel(npcId);
            dailyChatGranted = false;

            try
            {
                if (TryGrantDailyChatAffinity(npcId, dailyAffinityValue, out gainedPoints))
                {
                    dailyChatGranted = true;
                    ModBehaviour.DevLog(logPrefix + " 浠婃棩棣栨瀵硅瘽锛屽ソ鎰熷害澧炲姞: " + gainedPoints);
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
                        ModBehaviour.DevLog(logPrefix + " [WARNING] 鐖卞績鍙嶉鍔ㄤ綔鎵ц澶辫触: " + e.Message);
                    }

                    ModBehaviour.DevLog(logPrefix + " 濂芥劅搴︾瓑绾?" + currentLevel + " >= " + loveHeartLevelThreshold + "锛屾樉绀哄啋鐖卞績鐗规晥");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 澶勭悊鑱婂ぉ濂芥劅搴﹂€昏緫澶辫触: " + e.Message);
            }

            int levelAfterChat = AffinityManager.GetLevel(npcId);
            if (levelAfterChat <= levelBeforeChat)
            {
                ShowChatProgressBanner(npcId, gainedPoints);
            }
            return gainedPoints;
        }

        /// <summary>
        /// 鑻ュ瓨鍦ㄢ€滆姳蹇冨悗寰呰按璐ｂ€濈姸鎬侊紝鍒欏湪鏈涓庨厤鍋跺璇濇椂瑙﹀彂璋磋矗閫昏緫
        /// </summary>
        /// <returns>鏄惁宸叉嫤鎴苟澶勭悊鏈瀵硅瘽锛坱rue 琛ㄧず澶栧眰搴旂洿鎺?return锛?/returns>
        public static bool TryHandleSpouseCheatingRebuke(
            string npcId,
            Transform dialogueTarget,
            Action showBrokenHeartAction,
            string logPrefix)
        {
            try
            {
                int cheatingCount;
                if (!AffinityManager.TryConsumePendingCheatingRebuke(npcId, out cheatingCount))
                {
                    return false;
                }

                int penalty = Math.Max(0, cheatingCount - 1) * AffinityManager.SPOUSE_CHEATING_STACK_PENALTY;
                if (penalty > 0)
                {
                    AffinityManager.AddPoints(npcId, -penalty);
                }

                // 这次“谴责对话”视作当天已聊天，避免同日再次获得日常聊天奖励。
                AffinityManager.SetLastChatDay(npcId, NPCGiftSystem.GetCurrentGameDay());

                string dialogue;
                if (penalty <= 0)
                {
                    dialogue = L10n.T(
                        "你不该这样花心……这次我先原谅你，但别再有下次。",
                        "You shouldn't be so fickle... I'll forgive you this once, but don't do it again.");
                }
                else
                {
                    dialogue = L10n.T(
                        "你又让我失望了……这次我真的很难过。",
                        "You let me down again... this time it really hurts.");
                }

                NPCDialogueSystem.ShowDialogue(npcId, dialogueTarget, dialogue, 4.5f);

                if (showBrokenHeartAction != null)
                {
                    try { showBrokenHeartAction(); }
                    catch (System.Exception e) { ModBehaviour.DevLog("[AffinityHelper] [WARNING] 蹇冪鍙嶉鎵ц澶辫触: " + e.Message); }
                }

                if (penalty > 0)
                {
                    var config = AffinityManager.GetNPCConfig(npcId);
                    string npcName = config != null ? config.DisplayName : npcId;
                    string penaltyText = L10n.T(
                        npcName + "濂芥劅搴?-" + penalty + "锛堣姳蹇冩儵缃氾級",
                        npcName + " Affinity -" + penalty + " (Cheating Penalty)");

                    if (ModBehaviour.Instance != null)
                    {
                        ModBehaviour.Instance.ShowBigBanner(penaltyText);
                    }
                    else
                    {
                        NotificationText.Push(penaltyText);
                    }
                }

                ModBehaviour.DevLog(logPrefix + " 瑙﹀彂閰嶅伓璋磋矗瀵硅瘽锛宑heatingCount=" + cheatingCount + ", penalty=" + penalty);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 澶勭悊閰嶅伓璋磋矗瀵硅瘽澶辫触: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 鏄剧ず鑱婂ぉ鍚庣殑濂芥劅搴﹁繘搴﹀弽棣堬紙缁熶竴涓烘í骞咃級
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
                        npcName + "濂芥劅搴?Lv." + currentLevel + " (MAX)",
                        npcName + " Affinity Lv." + currentLevel + " (MAX)");
                }
                else
                {
                    int currentLevelProgress;
                    int pointsNeededForNextLevel;
                    AffinityManager.GetLevelProgressDetails(npcId, out currentLevelProgress, out pointsNeededForNextLevel);
                    notificationText = L10n.T(
                        npcName + "濂芥劅搴?Lv." + currentLevel + " 杩涘害 " + currentLevelProgress + "/" + pointsNeededForNextLevel,
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
                ModBehaviour.DevLog("[NPCAffinityHelper] 鏄剧ず鑱婂ぉ鍙嶉澶辫触: " + e.Message);
            }
        }
    }
}
