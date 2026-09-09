// ============================================================================
// LootExcludeTagPolicy.cs - 随机奖池「不该流通物品」的唯一排除口径
// ============================================================================
//
// 背景：Boss 奖励箱 / 通关奖励 / 空投 / 天空岛搜刮点各自建过一次随机奖池，
// 排除标签却各写各的。2026-09-09 审核发现天空岛少排了三个标签
// （DestroyOnLootBox / DontDropOnDeadInSlot / LockInDemoTag），
// 于是把口径收敛到这里，由所有奖池共用。
//
// 这里只负责「按 Tag 排除」。按 TypeID 排除是 LootBlacklistRegistry 的职责，
// 两者是互补的两道闸，不要在任何一处只用其中一道。

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;

namespace BossRush
{
    internal static class LootExcludeTagPolicy
    {
        // 官方 TagsData 在当前版本没有 Quest 字段（AllTags 里也没有同名 Tag），
        // 反射永远失败；第一次失败后用 sentinel 把后续调用降为 O(1)。
        private static Tag cachedQuestTag;
        private static bool questTagSearched;

        /// <summary>
        /// 通用随机奖池的排除标签。
        ///
        /// - <c>DestroyOnLootBox</c>：官方 <c>InteractableLootbox.CreateFromItem</c> 见到它就 DestroyTree，
        ///   等于明说这类物品不该出现在箱子里。
        /// - <c>DontDropOnDeadInSlot</c>：设计上不参与掉落。
        /// - <c>LockInDemoTag</c>：演示版锁定内容。
        /// - <c>Quest</c>：本版本不存在，保留反射以便官方将来补上时自动生效。
        ///
        /// <paramref name="includeCharacterTag"/> 控制是否连角色物品一起排除；
        /// <paramref name="includeSpecialTag"/> 控制是否排除 <c>Special</c>。
        /// 注意 <c>Special</c> **不是**可靠护栏（官方有奖池把它列进 requireTags），
        /// 它只能收窄结果，不能替代上面四个。
        /// </summary>
        internal static List<Tag> BuildExcludeTags(GameplayDataSettings.TagsData tagsData,
            bool includeCharacterTag = false, bool includeSpecialTag = false)
        {
            var excludeTags = new List<Tag>();
            if (tagsData == null) return excludeTags;

            if (includeCharacterTag) AddUnique(excludeTags, tagsData.Character);
            if (includeSpecialTag) AddUnique(excludeTags, tagsData.Special);

            AddUnique(excludeTags, tagsData.DestroyOnLootBox);
            AddUnique(excludeTags, tagsData.DontDropOnDeadInSlot);
            AddUnique(excludeTags, tagsData.LockInDemoTag);
            AddUnique(excludeTags, TryFindQuestTag(tagsData));

            return excludeTags;
        }

        internal static void AddUnique(List<Tag> excludeTags, Tag tag)
        {
            if (excludeTags == null || tag == null || excludeTags.Contains(tag)) return;
            excludeTags.Add(tag);
        }

        internal static Tag TryFindQuestTag(GameplayDataSettings.TagsData tagsData)
        {
            if (tagsData == null) return null;
            if (cachedQuestTag != null) return cachedQuestTag;
            if (questTagSearched) return null;

            questTagSearched = true;

            const BindingFlags publicInstanceIgnoreCase =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            try
            {
                FieldInfo questField = tagsData.GetType().GetField("Quest", publicInstanceIgnoreCase);
                if (questField != null && typeof(Tag).IsAssignableFrom(questField.FieldType))
                {
                    cachedQuestTag = questField.GetValue(tagsData) as Tag;
                    if (cachedQuestTag != null) return cachedQuestTag;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootExcludeTagPolicy] 通过字段读取 Quest 标签失败: " + e.Message);
            }

            try
            {
                PropertyInfo questProperty = tagsData.GetType().GetProperty("Quest", publicInstanceIgnoreCase);
                if (questProperty != null && typeof(Tag).IsAssignableFrom(questProperty.PropertyType))
                {
                    cachedQuestTag = questProperty.GetValue(tagsData, null) as Tag;
                    if (cachedQuestTag != null) return cachedQuestTag;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootExcludeTagPolicy] 通过属性读取 Quest 标签失败: " + e.Message);
            }

            try
            {
                if (tagsData.AllTags != null)
                {
                    foreach (Tag tag in tagsData.AllTags)
                    {
                        if (tag == null || string.IsNullOrEmpty(tag.name)) continue;
                        if (string.Equals(tag.name, "Quest", StringComparison.OrdinalIgnoreCase))
                        {
                            cachedQuestTag = tag;
                            return cachedQuestTag;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootExcludeTagPolicy] 遍历 AllTags 查找 Quest 标签失败: " + e.Message);
            }

            // 三段反射全部失败：让维护者从日志看出「Quest tag 阻断永久 disable」
            // 是当前鸭科夫版本的事实而非 mod bug。questTagSearched 已置位，只会打一次。
            ModBehaviour.DevLog("[LootExcludeTagPolicy] Quest tag lookup failed; " +
                "exclusion by Quest tag is permanently disabled in this build");
            return null;
        }

        internal static void ResetStaticCaches()
        {
            cachedQuestTag = null;
            questTagSearched = false;
        }
    }
}
