// ============================================================================
// NPCGiftSystem.cs - 通用NPC礼物系统
// ============================================================================
// 模块说明：
//   通用的NPC礼物赠送系统，支持任意实现 INPCGiftConfig 的NPC。
//   处理每日礼物赠送逻辑、好感度计算、反应显示等。
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 通用NPC礼物系统
    /// </summary>
    public static class NPCGiftSystem
    {
        // 反射缓存
        private static FieldInfo _typeIdField;
        private static bool _typeIdFieldCached = false;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 检查指定NPC今日是否可以赠送礼物
        /// </summary>
        public static bool CanGiftToday(string npcId)
        {
            int currentDay = GetCurrentGameDay();
            int lastGiftDay = AffinityManager.GetLastGiftDay(npcId);
            return currentDay != lastGiftDay;
        }
        
        /// <summary>
        /// 赠送礼物给指定NPC
        /// </summary>
        /// <param name="npcId">NPC标识符</param>
        /// <param name="item">要赠送的物品</param>
        /// <param name="npcTransform">NPC的Transform（用于显示气泡）</param>
        /// <param name="npcController">NPC控制器（可选，用于播放动画）</param>
        /// <returns>是否赠送成功</returns>
        public static bool GiveGift(string npcId, Item item, Transform npcTransform = null, GoblinNPCController npcController = null)
        {
            if (string.IsNullOrEmpty(npcId) || item == null)
            {
                ModBehaviour.DevLog("[NPCGift] 赠送失败：参数无效");
                return false;
            }
            
            // 检查今日是否已赠送
            if (!CanGiftToday(npcId))
            {
                ModBehaviour.DevLog("[NPCGift] 赠送失败：今日已赠送");
                return false;
            }
            
            // 获取NPC配置
            var config = AffinityManager.GetNPCConfig(npcId);
            var giftConfig = config as INPCGiftConfig;
            
            // 计算好感度增益
            int giftValue = CalculateGiftValue(npcId, item);
            
            // 获取反应类型
            GiftReactionType reactionType = GetGiftReactionType(npcId, item);
            
            // 增加好感度
            AffinityManager.AddPoints(npcId, giftValue);
            
            // 更新上次赠送日期和反应类型
            int currentDay = GetCurrentGameDay();
            AffinityManager.SetLastGiftDay(npcId, currentDay);
            AffinityManager.SetLastGiftReaction(npcId, (int)reactionType);
            
            // 显示反应气泡
            if (npcTransform != null && giftConfig != null)
            {
                string bubble = GetReactionBubble(giftConfig, reactionType);
                NPCDialogueSystem.ShowDialogue(npcId, npcTransform, bubble);
                
                // 播放动画（如果有控制器）
                if (npcController != null)
                {
                    if (reactionType == GiftReactionType.Positive && giftConfig.ShowLoveHeartOnPositive)
                    {
                        npcController.ShowLoveHeartBubble();
                    }
                    else if (reactionType == GiftReactionType.Negative && giftConfig.ShowBrokenHeartOnNegative)
                    {
                        npcController.ShowBrokenHeartBubble();
                    }
                }
            }
            
            // 显示好感度进度通知
            ShowAffinityNotification(npcId, giftValue);
            
            ModBehaviour.DevLog("[NPCGift] 赠送成功，NPC: " + npcId + ", 好感度变化: " + giftValue + ", 反应: " + reactionType);
            return true;
        }
        
        /// <summary>
        /// 计算物品的好感度增益
        /// </summary>
        public static int CalculateGiftValue(string npcId, Item item)
        {
            if (item == null) return 0;

            var config = AffinityManager.GetNPCConfig(npcId);
            var giftConfig = config as INPCGiftConfig;

            int typeId = GetItemTypeId(item);

            // 检查负向物品列表
            if (giftConfig?.NegativeItems != null && giftConfig.NegativeItems.TryGetValue(typeId, out int penalty))
            {
                return -penalty;
            }

            // 检查正向物品列表
            if (giftConfig?.PositiveItems != null && giftConfig.PositiveItems.TryGetValue(typeId, out int bonus))
            {
                return bonus;
            }

            // 检查正向标签列表（哥布林特殊：喜欢配方和蓝图）
            if (HasPositiveTag(npcId, item))
            {
                // 使用默认喜欢值
                if (config?.GiftValues != null && config.GiftValues.TryGetValue("Liked", out int likedValue))
                {
                    return likedValue;
                }
                return 80; // 默认喜欢值
            }

            // 使用基础计算
            return GetBaseGiftValue(item, config);
        }

        /// <summary>
        /// 获取物品的反应类型
        /// </summary>
        public static GiftReactionType GetGiftReactionType(string npcId, Item item)
        {
            if (item == null) return GiftReactionType.Normal;

            var config = AffinityManager.GetNPCConfig(npcId);
            var giftConfig = config as INPCGiftConfig;

            if (giftConfig == null) return GiftReactionType.Normal;

            int typeId = GetItemTypeId(item);

            if (giftConfig.NegativeItems != null && giftConfig.NegativeItems.ContainsKey(typeId))
            {
                return GiftReactionType.Negative;
            }

            if (giftConfig.PositiveItems != null && giftConfig.PositiveItems.ContainsKey(typeId))
            {
                return GiftReactionType.Positive;
            }

            // 检查正向标签列表
            if (HasPositiveTag(npcId, item))
            {
                return GiftReactionType.Positive;
            }

            return GiftReactionType.Normal;
        }
        
        /// <summary>
        /// 获取反应对话气泡
        /// </summary>
        public static string GetReactionBubble(INPCGiftConfig config, GiftReactionType reactionType)
        {
            if (config == null) return L10n.T("谢谢你的礼物~", "Thanks for the gift~");
            
            string[] bubbles;
            switch (reactionType)
            {
                case GiftReactionType.Positive:
                    bubbles = config.PositiveBubbles;
                    break;
                case GiftReactionType.Negative:
                    bubbles = config.NegativeBubbles;
                    break;
                default:
                    bubbles = config.NormalBubbles;
                    break;
            }
            
            if (bubbles == null || bubbles.Length == 0)
            {
                return L10n.T("谢谢你的礼物~", "Thanks for the gift~");
            }
            
            return bubbles[UnityEngine.Random.Range(0, bubbles.Length)];
        }
        
        /// <summary>
        /// 获取今日已赠送的对话
        /// </summary>
        public static string GetAlreadyGiftedDialogue(string npcId)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var giftConfig = config as INPCGiftConfig;
            
            if (giftConfig == null)
            {
                return L10n.T("今天已经收到礼物了~", "Already received a gift today~");
            }
            
            int lastReaction = AffinityManager.GetLastGiftReaction(npcId);
            GiftReactionType reactionType = (GiftReactionType)lastReaction;
            
            string[] dialogues = giftConfig.GetAlreadyGiftedDialogues(reactionType);
            if (dialogues == null || dialogues.Length == 0)
            {
                return L10n.T("今天已经收到礼物了~", "Already received a gift today~");
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 重置每日礼物状态（调试用）
        /// </summary>
        public static void ResetDailyGift(string npcId)
        {
            AffinityManager.SetLastGiftDay(npcId, -1);
            ModBehaviour.DevLog("[NPCGift] " + npcId + " 每日礼物状态已重置");
        }
        
        /// <summary>
        /// 获取当前游戏日期
        /// </summary>
        public static int GetCurrentGameDay()
        {
            try
            {
                return (int)GameClock.Day;
            }
            catch
            {
                return DateTime.Now.DayOfYear;
            }
        }
        
        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 获取物品TypeID
        /// </summary>
        private static int GetItemTypeId(Item item)
        {
            if (item == null) return 0;

            try
            {
                if (!_typeIdFieldCached)
                {
                    _typeIdField = typeof(Item).GetField("typeID",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _typeIdFieldCached = true;
                }

                if (_typeIdField != null)
                {
                    return (int)_typeIdField.GetValue(item);
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// 检查物品是否拥有NPC喜欢的标签
        /// </summary>
        private static bool HasPositiveTag(string npcId, Item item)
        {
            if (item == null || item.Tags == null || item.Tags.Count == 0)
                return false;

            // 目前只有哥布林支持标签偏好
            if (npcId == GoblinAffinityConfig.NPC_ID)
            {
                var goblinConfig = new GoblinAffinityConfig();
                var positiveTags = goblinConfig.PositiveTags;

                if (positiveTags != null && positiveTags.Count > 0)
                {
                    foreach (var tag in item.Tags)
                    {
                        if (tag != null && positiveTags.Contains(tag.name))
                        {
                            ModBehaviour.DevLog("[NPCGift] 物品拥有喜欢的标签: " + tag.name);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 获取物品基础好感度增益
        /// </summary>
        private static int GetBaseGiftValue(Item item, INPCAffinityConfig config)
        {
            if (item == null) return AffinityConfig.DEFAULT_GIFT_VALUE;
            
            string itemType = GetItemType(item);
            
            if (config?.GiftValues != null && config.GiftValues.TryGetValue(itemType, out int value))
            {
                return value;
            }
            
            if (config?.GiftValues != null && config.GiftValues.TryGetValue("Default", out int defaultValue))
            {
                return defaultValue;
            }
            
            return AffinityConfig.DEFAULT_GIFT_VALUE;
        }
        
        /// <summary>
        /// 获取物品类型字符串
        /// </summary>
        private static string GetItemType(Item item)
        {
            if (item == null) return "Default";
            
            try
            {
                if (item.GetComponent<ItemSetting_Gun>() != null ||
                    item.GetComponent<ItemSetting_MeleeWeapon>() != null)
                {
                    return "Weapon";
                }
                
                string displayName = item.DisplayName ?? "";
                if (displayName.Contains("甲") || displayName.Contains("Armor") ||
                    displayName.Contains("头盔") || displayName.Contains("Helmet"))
                {
                    return "Armor";
                }
                
                if (item.GetComponent<UsageUtilities>() != null)
                {
                    return "Consumable";
                }
                
                if (displayName.Contains("图腾") || displayName.Contains("Totem"))
                {
                    return "Totem";
                }
                
                if (displayName.Contains("宝石") || displayName.Contains("Gem") ||
                    displayName.Contains("钻石") || displayName.Contains("Diamond"))
                {
                    return "Gem";
                }
            }
            catch { }
            
            return "Default";
        }
        
        /// <summary>
        /// 显示好感度进度通知
        /// </summary>
        private static void ShowAffinityNotification(string npcId, int change)
        {
            try
            {
                var config = AffinityManager.GetNPCConfig(npcId);
                string npcName = config?.DisplayName ?? npcId;
                
                int currentLevel = AffinityManager.GetLevel(npcId);
                
                // 使用递增式等级配置获取正确的进度
                AffinityManager.GetLevelProgressDetails(npcId, out int currentLevelProgress, out int levelRequired);
                
                // 满级时显示特殊文本
                string notificationText;
                if (levelRequired <= 0)
                {
                    notificationText = L10n.T(
                        npcName + "好感度 Lv." + currentLevel + " (满级)",
                        npcName + " Affinity Lv." + currentLevel + " (MAX)"
                    );
                }
                else
                {
                    notificationText = L10n.T(
                        npcName + "好感度 Lv." + currentLevel + " 进度 " + currentLevelProgress + "/" + levelRequired,
                        npcName + " Affinity Lv." + currentLevel + " Progress " + currentLevelProgress + "/" + levelRequired
                    );
                }
                
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.ShowBigBanner(notificationText);
                }
                else
                {
                    Duckov.UI.NotificationText.Push(notificationText);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGift] [WARNING] 显示通知失败: " + e.Message);
            }
        }
    }
}
