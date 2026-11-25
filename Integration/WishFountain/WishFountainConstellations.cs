// ============================================================================
// WishFountainConstellations.cs - 许愿台「星宿祈向」的十二宿与奖励类别接线
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 3 项）：
//   许愿面板输入框上方排一道星宿穹顶，十二宿一一对应许愿奖励的十二个类别
//   （WishFountainService.WishRewardCategories）。玩家点亮一宿，等同于心愿里写中了该类别的关键词：
//   同一个 matchedCategoryIds、同一条偏置上限，品质概率不变（WishRewardOddsGuard）。
//   祈向只在本地用，不随心愿上传，飞书表结构不变。
//
//   星图坐标在 1×1 的方框里（x 向右、y 向上），第一个点是主星（画得大一点），
//   Edges 是成对的点下标，画成连星细线。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed class WishConstellation
    {
        internal string Id;          // 对应许愿奖励类别 id
        internal string NameZh;      // 星宿名
        internal string NameEn;
        internal string LabelZh;     // 类别名（芯片下方的小字）
        internal string LabelEn;
        internal Vector2[] Stars;
        internal int[] Edges;

        internal string Name { get { return L10n.T(NameZh, NameEn); } }
        internal string Label { get { return L10n.T(LabelZh, LabelEn); } }
    }

    internal static class WishConstellations
    {
        internal static readonly WishConstellation[] All =
        {
            Make("weapon", "天枪", "Spear", "武器", "Arms",
                new[] { P(0.62f, 0.66f), P(0.08f, 0.12f), P(0.38f, 0.42f), P(0.92f, 0.92f), P(0.48f, 0.80f), P(0.78f, 0.50f) },
                new[] { 1, 2, 2, 0, 0, 3, 4, 0, 0, 5 }),
            Make("gun", "弧矢", "Bow", "枪械", "Guns",
                new[] { P(0.95f, 0.50f), P(0.26f, 0.08f), P(0.10f, 0.50f), P(0.26f, 0.92f), P(0.58f, 0.50f) },
                new[] { 1, 2, 2, 3, 2, 4, 4, 0 }),
            Make("melee", "斧钺", "Axe", "近战", "Melee",
                new[] { P(0.86f, 0.62f), P(0.18f, 0.06f), P(0.44f, 0.48f), P(0.66f, 0.94f), P(0.58f, 0.70f) },
                new[] { 1, 2, 2, 3, 3, 0, 0, 4, 4, 2 }),
            Make("armor", "天铠", "Mail", "护甲", "Armor",
                new[] { P(0.50f, 0.96f), P(0.88f, 0.84f), P(0.80f, 0.36f), P(0.50f, 0.06f), P(0.20f, 0.36f), P(0.12f, 0.84f) },
                new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 0 }),
            Make("helmet", "华盖", "Canopy", "头盔", "Helms",
                new[] { P(0.50f, 0.92f), P(0.04f, 0.40f), P(0.25f, 0.76f), P(0.75f, 0.76f), P(0.96f, 0.40f), P(0.50f, 0.10f) },
                new[] { 1, 2, 2, 0, 0, 3, 3, 4, 0, 5 }),
            Make("totem", "神宫", "Shrine", "图腾", "Totems",
                new[] { P(0.50f, 0.60f), P(0.50f, 0.96f), P(0.18f, 0.44f), P(0.82f, 0.44f), P(0.50f, 0.08f) },
                new[] { 1, 0, 0, 2, 0, 3, 0, 4 }),
            Make("gift", "织女", "Weaver", "礼物", "Gifts",
                new[] { P(0.50f, 0.58f), P(0.28f, 0.88f), P(0.72f, 0.88f), P(0.34f, 0.14f), P(0.72f, 0.24f) },
                new[] { 1, 2, 2, 0, 0, 1, 0, 3, 0, 4 }),
            Make("healing", "天医", "Healer", "疗伤", "Heals",
                new[] { P(0.50f, 0.56f), P(0.50f, 0.96f), P(0.50f, 0.06f), P(0.10f, 0.56f), P(0.90f, 0.56f) },
                new[] { 1, 0, 0, 2, 3, 0, 0, 4 }),
            Make("flag", "天旗", "Banner", "营旗", "Flags",
                new[] { P(0.14f, 0.96f), P(0.14f, 0.04f), P(0.86f, 0.82f), P(0.56f, 0.64f), P(0.14f, 0.50f) },
                new[] { 1, 0, 0, 2, 2, 3, 3, 4 }),
            Make("summon", "天狼", "Wolf", "召唤", "Summon",
                new[] { P(0.50f, 0.50f), P(0.14f, 0.82f), P(0.86f, 0.76f), P(0.72f, 0.10f), P(0.20f, 0.18f) },
                new[] { 0, 1, 0, 2, 0, 3, 0, 4 }),
            Make("fortification", "垒壁", "Rampart", "工事", "Forts",
                new[] { P(0.30f, 0.72f), P(0.04f, 0.30f), P(0.30f, 0.30f), P(0.70f, 0.72f), P(0.70f, 0.30f), P(0.96f, 0.30f) },
                new[] { 1, 2, 2, 0, 0, 3, 3, 4, 4, 5 }),
            Make("travel", "天船", "Ship", "通行", "Travel",
                new[] { P(0.50f, 0.92f), P(0.04f, 0.56f), P(0.26f, 0.18f), P(0.50f, 0.18f), P(0.74f, 0.18f), P(0.96f, 0.56f) },
                new[] { 1, 2, 2, 3, 3, 4, 4, 5, 3, 0 }),
        };

        internal static WishConstellation Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase)) return All[i];
            }
            return null;
        }

        private static WishConstellation Make(string id, string nameZh, string nameEn, string labelZh, string labelEn, Vector2[] stars, int[] edges)
        {
            return new WishConstellation
            {
                Id = id,
                NameZh = nameZh,
                NameEn = nameEn,
                LabelZh = labelZh,
                LabelEn = labelEn,
                Stars = stars,
                Edges = edges
            };
        }

        private static Vector2 P(float x, float y)
        {
            return new Vector2(x, y);
        }
    }

    public static partial class WishFountainService
    {
        /// <summary>
        /// 玩家在星宿穹顶上点亮的一宿：按心意词命中同一类别处理（同一个 matchedCategoryIds，同一条偏置上限）。
        /// id 不在奖励类别表里时什么都不做（旧档 / 数据改名不会抽出怪结果）。
        /// </summary>
        private static void AddChosenWishCategory(WishRewardMatchResult match, string chosenCategoryId)
        {
            if (match == null || string.IsNullOrEmpty(chosenCategoryId)) return;
            for (int i = 0; i < WishRewardCategories.Length; i++)
            {
                WishRewardCategoryDefinition category = WishRewardCategories[i];
                if (category != null && string.Equals(category.categoryId, chosenCategoryId, StringComparison.OrdinalIgnoreCase))
                {
                    match.matchedCategoryIds.Add(category.categoryId);
                    return;
                }
            }
        }
    }
}
