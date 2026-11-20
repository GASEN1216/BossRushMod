// ============================================================================
// SetBonusPlaceholderRegistry.cs - P1套装体系占位符注册
// ============================================================================
// 模块说明：
//   当 Assets/Equipment/ 下缺少霜冠、寒冰铠甲、雷神之角、雷霆战甲的
//   AssetBundle 时，通过克隆已有装备模板创建占位符，确保套装物品能注册到
//   游戏物品系统。SetBonusManager 的效果检测逻辑基于 TypeID 匹配，
//   只要物品注册成功即可生效。
// ============================================================================

using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// P1 套装体系占位符注册器
    /// </summary>
    public static class SetBonusPlaceholderRegistry
    {
        // 套装 TypeID 常量
        private const int FROST_HELMET_ID = 500053;
        private const int FROST_ARMOR_ID = 500054;
        private const int THUNDER_HELMET_ID = 500055;
        private const int THUNDER_ARMOR_ID = 500056;

        /// <summary>
        /// 确保所有套装物品已注册（AssetBundle 缺失时使用占位符）
        /// </summary>
        public static void EnsureAllRegistered()
        {
            // 霜冠 - 头盔
            EnsureRegistered(FROST_HELMET_ID);

            // 寒冰铠甲 - 护甲
            EnsureRegistered(FROST_ARMOR_ID);

            // 雷神之角 - 头盔
            EnsureRegistered(THUNDER_HELMET_ID);

            // 雷霆战甲 - 护甲
            EnsureRegistered(THUNDER_ARMOR_ID);

            // 天空岛头目 / 岛主的专属装备（500086-500089）同样是「缺 bundle 时克隆同槽位官方装备顶上」，在同一个装配期时点补齐。
            SkyIslandBossGearConfig.EnsureAllRegistered();
        }

        // 保留原公开入口；批量注册随当前 prefab 状态运行，克隆由集成运行时清理。
        public static void ResetStaticCaches() { }

        /// <summary>
        /// 按 TypeID 确保单个 P1 套装物品已注册，供 BossRushDynamicItemRegistry 按需调用。
        /// </summary>
        public static bool EnsureRegistered(int typeId)
        {
            if (typeId == FROST_HELMET_ID)
            {
                return EnsureSetItemRegistered(FROST_HELMET_ID, "FrostCrown_Placeholder");
            }

            if (typeId == FROST_ARMOR_ID)
            {
                return EnsureSetItemRegistered(FROST_ARMOR_ID, "IceArmor_Placeholder");
            }

            if (typeId == THUNDER_HELMET_ID)
            {
                return EnsureSetItemRegistered(THUNDER_HELMET_ID, "ThunderHorn_Placeholder");
            }

            if (typeId == THUNDER_ARMOR_ID)
            {
                return EnsureSetItemRegistered(THUNDER_ARMOR_ID, "ThunderArmor_Placeholder");
            }

            return false;
        }

        /// <summary>
        /// 确保单个套装物品已注册
        /// </summary>
        private static bool EnsureSetItemRegistered(int typeId, string prefabName)
        {
            string sourceSlotTag = GetFallbackSourceSlotTag(typeId);
            return RuntimeItemFallback.Ensure(typeId, prefabName, "[SetBonusPlaceholder] ",
                () => FindEquipmentSource(sourceSlotTag), clone =>
                {
                    // 清掉从龙裔/龙王装备克隆继承下来的 Modifier 与显示 Variables，
                    // 否则 Frost Crown / Thunder Horn 占位符会自带"龙头"全部属性（HeadArmor +7、
                    // 元素抗性、ViewAngle -20% 等），与套装设计不符。
                    try
                    {
                        if (clone.Modifiers != null)
                        {
                            clone.Modifiers.Clear();
                        }
                    }
                    catch  { /* best-effort fallback intentionally ignored */ }
                    try { clone.Variables.Clear(); } catch  { /* best-effort fallback intentionally ignored */ }

                    FrostThunderSetConfig.TryConfigureByTypeId(clone);
                }, existing => FrostThunderSetConfig.TryConfigureByTypeId(existing));
        }

        private static string GetFallbackSourceSlotTag(int typeId)
        {
            if (typeId == FROST_HELMET_ID || typeId == THUNDER_HELMET_ID)
            {
                return "Helmat";
            }

            return "Armor";
        }

        /// <summary>
        /// 查找同类型装备的克隆源（优先龙裔套装，其次任何同类型装备）
        /// </summary>
        private static Item FindEquipmentSource(string slotTag)
        {
            int[] dragonIds = slotTag == "Helmat" ? new[] { 500003, 500011 } : new[] { 500004, 500012 };
            Item prefab = RuntimeItemFallback.FindSource(dragonIds, false);
            return prefab != null ? prefab : RuntimeItemFallback.FindSource(new[] { BossRushItemIds.BossRushTicket, 500014 }, false);
        }
    }
}
