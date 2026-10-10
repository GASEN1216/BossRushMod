// ============================================================================
// PermanentDuckNpcAffinityConfig.cs - 永久捏脸 NPC 的好感度配置
// ============================================================================
// 模块说明：
//   **一个类服务所有永久捏脸 NPC**，而不是每只 NPC 一个类。
//
//   羽织/叮当是「一 NPC 一类」：NurseAffinityConfig 923 行、
//   GoblinAffinityConfig 1175 行，其中约 90% 是写死的对话字符串。
//   那种形状下，第二只、第三只 NPC 就是再写 900 行。
//
//   这里每个实例绑一条蓝图，所有文本从 PermanentDuckNpcData（即 JSON）来。
//   新增第 N 只永久 NPC 的增量仍然是「往 JSON 加一条」。
//
//   实现的接口：
//     INPCAffinityConfig            —— 唯一必须
//     INPCGiftConfig                —— 送礼反应
//     INPCDialogueConfig            —— 分级对话
//     INPCRelationshipDialogueConfig—— 婚后台词
//     INPCGiftContainerConfig       —— 礼物容器 UI 文案
//     INPCShopConfig                —— 蓝图可选商店
//
//   只有 permanent.shop 含有效商品时启用商店；未配置的旧蓝图保持关闭。
//   商店解锁、现金购买与婚后交易沿用 NPCShopSystem，不新增关系或库存存档。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 数据驱动的永久捏脸 NPC 好感度配置。一条蓝图一个实例。
    /// </summary>
    internal sealed class PermanentDuckNpcAffinityConfig :
        INPCAffinityConfig,
        INPCGiftConfig,
        INPCDialogueConfig,
        INPCRelationshipDialogueConfig,
        INPCGiftContainerConfig,
        INPCShopConfig
    {
        private readonly DuckNpcBlueprint _blueprint;
        private readonly PermanentDuckNpcData _data;

        private Dictionary<string, int> _giftValues;
        private Dictionary<int, string[]> _unlocksByLevel;
        private Dictionary<int, float> _discountsByLevel;
        private Dictionary<int, int> _positiveItems;
        private Dictionary<int, int> _negativeItems;
        private HashSet<string> _positiveTags;

        internal PermanentDuckNpcAffinityConfig(DuckNpcBlueprint blueprint)
        {
            _blueprint = blueprint;
            _data = blueprint != null ? blueprint.permanent : null;
        }

        internal DuckNpcBlueprint Blueprint
        {
            get { return _blueprint; }
        }

        // ====================================================================
        // INPCAffinityConfig
        // ====================================================================

        public string NpcId
        {
            get { return _blueprint != null ? _blueprint.id : string.Empty; }
        }

        public string DisplayName
        {
            get
            {
                if (_data == null)
                {
                    return NpcId;
                }
                return L10n.T(_data.displayNameCn, _data.displayNameEn);
            }
        }

        // MaxPoints / PointsPerLevel 实际上没有任何消费方：
        // AffinityManager.GetMaxPoints 硬返回 UNIFIED_MAX_POINTS，等级走 LevelPointsRequired 静态表。
        // 这里跟随羽织/叮当填统一值，改了也不会生效。
        public int MaxPoints
        {
            get { return AffinityManager.UNIFIED_MAX_POINTS; }
        }

        public int PointsPerLevel
        {
            get { return 250; }
        }

        public int MaxLevel
        {
            get { return AffinityManager.UNIFIED_MAX_LEVEL; }
        }

        /// <summary>与羽织/叮当同值，保证送礼手感一致。</summary>
        public Dictionary<string, int> GiftValues
        {
            get
            {
                if (_giftValues == null)
                {
                    _giftValues = new Dictionary<string, int>
                    {
                        { "Liked", 80 },
                        { "Disliked", -40 },
                        { "Default", 20 }
                    };
                }
                return _giftValues;
            }
        }

        /// <summary>
        /// 等级解锁说明。只用于升级横幅展示，不驱动任何实际解锁。
        /// </summary>
        public Dictionary<int, string[]> UnlocksByLevel
        {
            get
            {
                if (_unlocksByLevel == null)
                {
                    _unlocksByLevel = new Dictionary<int, string[]>();
                }
                if (ShopEnabled)
                {
                    // 升级横幅取用时解析语言，避免蓝图首次载入后把文案锁在旧语言。
                    _unlocksByLevel[ShopUnlockLevel] = new[] { ShopName };
                }
                return _unlocksByLevel;
            }
        }

        /// <summary>
        /// 永久 NPC 商店按物品原价出售，暂不附加好感折扣。
        /// </summary>
        public Dictionary<int, float> DiscountsByLevel
        {
            get
            {
                if (_discountsByLevel == null)
                {
                    _discountsByLevel = new Dictionary<int, float>();
                }
                return _discountsByLevel;
            }
        }

        // ====================================================================
        // INPCShopConfig：未配置商店的蓝图保持关闭，婚前婚后共用同一配置。
        // ====================================================================

        public bool ShopEnabled
        {
            get { return _data != null && _data.shop != null && _data.shop.items.Count > 0; }
        }

        public int ShopUnlockLevel
        {
            get { return ShopEnabled ? _data.shop.unlockLevel : int.MaxValue; }
        }

        public string ShopName
        {
            get
            {
                if (ShopEnabled && !string.IsNullOrEmpty(_data.shop.nameCn))
                    return L10n.T(_data.shop.nameCn, _data.shop.nameEn);
                return DisplayName + L10n.T("的商店", "'s Shop");
            }
        }

        public List<ShopItemEntry> GetShopItems()
        {
            return ShopEnabled ? new List<ShopItemEntry>(_data.shop.items) : new List<ShopItemEntry>();
        }

        public float GetDiscountForLevel(int level) { return 0f; }

        // ====================================================================
        // INPCGiftConfig
        // ====================================================================

        public int DailyChatAffinity
        {
            get { return _data != null ? _data.dailyChatAffinity : 30; }
        }

        public Dictionary<int, int> PositiveItems
        {
            get
            {
                if (_positiveItems == null)
                {
                    _positiveItems = BuildItemMap(_data != null ? _data.positiveItemTypeIds : null, 80);
                }
                return _positiveItems;
            }
        }

        public Dictionary<int, int> NegativeItems
        {
            get
            {
                if (_negativeItems == null)
                {
                    _negativeItems = BuildItemMap(_data != null ? _data.negativeItemTypeIds : null, -40);
                }
                return _negativeItems;
            }
        }

        public HashSet<string> PositiveTags
        {
            get
            {
                if (_positiveTags == null)
                {
                    _positiveTags = new HashSet<string>(StringComparer.Ordinal);
                    if (_data != null && _data.positiveTags != null)
                    {
                        for (int i = 0; i < _data.positiveTags.Length; i++)
                        {
                            string tag = _data.positiveTags[i];
                            if (!string.IsNullOrEmpty(tag))
                            {
                                _positiveTags.Add(tag);
                            }
                        }
                    }
                }
                return _positiveTags;
            }
        }

        private static Dictionary<int, int> BuildItemMap(int[] typeIds, int value)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            if (typeIds == null)
            {
                return map;
            }
            for (int i = 0; i < typeIds.Length; i++)
            {
                int typeId = typeIds[i];
                if (typeId > 0 && !map.ContainsKey(typeId))
                {
                    map.Add(typeId, value);
                }
            }
            return map;
        }

        // 气泡走数据层的按语言视图（CR-2026-09-12-016）：蓝图里每一句可以写成 {cn, en}，
        // 这里取当前语言的那一半；老蓝图只有中文时 L10n.T 自动回落，行为不变。
        public string[] PositiveBubbles
        {
            get { return _data != null ? _data.PositiveBubbles : null; }
        }

        public string[] NegativeBubbles
        {
            get { return _data != null ? _data.NegativeBubbles : null; }
        }

        public string[] NormalBubbles
        {
            get { return _data != null ? _data.NormalBubbles : null; }
        }

        public string[] GetAlreadyGiftedDialogues(GiftReactionType lastReaction)
        {
            if (_data == null)
            {
                return null;
            }

            string category;
            switch (lastReaction)
            {
                case GiftReactionType.Positive: category = "alreadyGiftedPositive"; break;
                case GiftReactionType.Negative: category = "alreadyGiftedNegative"; break;
                default: category = "alreadyGiftedNormal"; break;
            }

            // 这里要的是整组而不是一句，走 GetDialogue 只会拿到一句。
            // 复用同一份数据，取 0 级那档的全部行。
            string line = _data.GetDialogue(category, 0);
            return line == null ? null : new string[] { line };
        }

        public bool ShowLoveHeartOnPositive
        {
            get { return true; }
        }

        public bool ShowBrokenHeartOnNegative
        {
            get { return true; }
        }

        // ====================================================================
        // INPCDialogueConfig
        // ====================================================================

        public string GetDialogue(DialogueCategory category, int level)
        {
            if (_data == null)
            {
                return null;
            }
            return _data.GetDialogue(CategoryKey(category), level);
        }

        public string GetSpecialDialogue(string eventKey, int level)
        {
            if (_data == null || string.IsNullOrEmpty(eventKey))
            {
                return null;
            }
            return _data.GetDialogue(eventKey, level);
        }

        public float DialogueBubbleHeight
        {
            get { return _data != null ? _data.dialogueBubbleHeight : 2.5f; }
        }

        public float DefaultDialogueDuration
        {
            get { return _data != null ? _data.defaultDialogueDuration : 4f; }
        }

        /// <summary>
        /// DialogueCategory 枚举 → JSON 里的 key 名。
        /// 用小驼峰，与蓝图其他字段风格一致。
        /// </summary>
        private static string CategoryKey(DialogueCategory category)
        {
            switch (category)
            {
                case DialogueCategory.Greeting: return "greeting";
                case DialogueCategory.AfterGift: return "afterGift";
                case DialogueCategory.LevelUp: return "levelUp";
                case DialogueCategory.Shopping: return "shopping";
                case DialogueCategory.AlreadyGifted: return "alreadyGifted";
                case DialogueCategory.Idle: return "idle";
                case DialogueCategory.Farewell: return "farewell";
                case DialogueCategory.Special: return "special";
                default: return "idle";
            }
        }

        // ====================================================================
        // INPCRelationshipDialogueConfig
        // ====================================================================

        /// <summary>
        /// 婚后专属台词。只有当该 NPC 是当前配偶时才会被调用。
        /// 返回 null 即回落普通台词。
        /// </summary>
        /// <remarks>
        /// 系统会喂进来的 eventKey（在 JSON 的 marriedDialogues 里按需配）：
        ///   dialogue_greeting_married / dialogue_after_gift_married /
        ///   dialogue_level_up_married / dialogue_shopping_married /
        ///   dialogue_already_gifted_married / dialogue_idle_married /
        ///   dialogue_farewell_married
        ///   gift_positive_married / gift_negative_married / gift_normal_married
        ///   gift_already_positive_married / gift_already_normal_married /
        ///   gift_already_negative_married
        /// 婚恋场景专属台词（不要求已婚，由 NPCDialogueSystem.GetPersonaLine 取；没配就落回通用文案）：
        ///   marriage_chapel_required / marriage_divorce / marriage_bubble_date（正文里用 {date} 占位）/
        ///   marriage_cheat_first / marriage_cheat_repeat / marriage_follow_refused /
        ///   ring_reject / ring_cheater / ring_spouse_repeat
        /// </remarks>
        public string GetRelationshipDialogue(string eventKey, int level)
        {
            if (_data == null)
            {
                return null;
            }
            return _data.GetMarriedDialogue(eventKey);
        }

        // ====================================================================
        // INPCGiftContainerConfig
        // ====================================================================
        // 三个 key 全部留空 → NPCGiftContainerConfigDefaults 会用通用文案
        // （BossRush_GiftContainer_Default*），这些 key 全局已注入，新 NPC 不必再注。

        public string ContainerTitleKey
        {
            get { return string.Empty; }
        }

        public string GiftButtonTextKey
        {
            get { return string.Empty; }
        }

        public string EmptySlotTextKey
        {
            get { return string.Empty; }
        }

        public bool UseContainerUI
        {
            get { return true; }
        }
    }
}
