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
//   实现的接口（与羽织一致，唯独不实现 INPCShopConfig）：
//     INPCAffinityConfig            —— 唯一必须
//     INPCGiftConfig                —— 送礼反应
//     INPCDialogueConfig            —— 分级对话
//     INPCRelationshipDialogueConfig—— 婚后台词
//     INPCGiftContainerConfig       —— 礼物容器 UI 文案
//
//   **故意不实现 INPCShopConfig**：不实现时 NPCShopSystem.IsShopUnlocked 直接返回 false，
//   NPCShopInteractable 自动 SetActive(false) —— 正好满足「专属服务留接口但先不显示」。
//   将来要开商店，让本类补实现 INPCShopConfig 即可，蓝图加对应数据。
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
        INPCGiftContainerConfig
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
                return _unlocksByLevel;
            }
        }

        /// <summary>
        /// 折扣表。本版永久 NPC 不开商店/服务，因此为空。
        /// 将来接服务时从蓝图读。
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

        public string[] PositiveBubbles
        {
            get { return _data != null ? _data.positiveBubbles : null; }
        }

        public string[] NegativeBubbles
        {
            get { return _data != null ? _data.negativeBubbles : null; }
        }

        public string[] NormalBubbles
        {
            get { return _data != null ? _data.normalBubbles : null; }
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
