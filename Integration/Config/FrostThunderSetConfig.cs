// ============================================================================
// FrostThunderSetConfig.cs - P1 冰霜/雷霆套装配置
// ============================================================================
// 模块说明：
//   P1 四件套（霜冠、寒冰铠甲、雷神之角、雷霆战甲）的基础属性、
//   图标与模型补绑逻辑。
//   真实资源走 EquipmentFactory 主管线，SetBonusPlaceholderRegistry 只在
//   资源缺失时创建克隆占位物品并委托本配置器补齐展示与基础数值。
// ============================================================================

using System;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    public static class FrostThunderSetConfig
    {
        public const int FROST_HELMET_ID = 500053;
        public const int FROST_ARMOR_ID = 500054;
        public const int THUNDER_HELMET_ID = 500055;
        public const int THUNDER_ARMOR_ID = 500056;

        // ========== 获取路径（2026-09-06 开放） ==========
        // 稳定线：叮当好感商店；运气线：Boss 专属掉落格（LootAndRewardsSetBonusLoot.cs）。
        // 6 级解锁介于冷淬液 4 级与钻戒 7 级之间；库存 1 与钻戒同款（NPCShopSystem 每次开店重置）。
        public const int SET_PIECE_UNLOCK_LEVEL = 6;
        public const int SET_PIECE_MAX_STOCK = 1;
        // 单件售价。StockShop 价格 = Value × 耐久比 × priceFactor，不设 Value 商店会卖 0 元。
        // 品质 6 套装件，取寄存牌 18000 与龙王宝库成就 50000 之间的档位；数值可调。
        public const int SET_PIECE_VALUE = 30000;
        // 天气防护：官方 StormWeather 在 StormProtection > 0.9 时不再施加风暴 buff，每件 +1 即免疫；
        // ColdProtection 同为官方 stat（龙王套装每件也是 +1）。
        private const float WEATHER_PROTECTION_PER_PIECE = 1f;

        private const string FROST_HELMET_BASE = "FrostCrown_Helmet";
        private const string FROST_ARMOR_BASE = "IceArmor_Armor";
        private const string THUNDER_HELMET_BASE = "ThunderHorn_Helmet";
        private const string THUNDER_ARMOR_BASE = "ThunderArmor_Armor";

        private const string FROST_SET_BUNDLE = "frost_set";
        private const string THUNDER_SET_BUNDLE = "thunder_set";
        private const string FROST_HELMET_ICON = "frost_crown_icon";
        private const string FROST_ARMOR_ICON = "ice_armor_icon";
        private const string THUNDER_HELMET_ICON = "thunder_horn_icon";
        private const string THUNDER_ARMOR_ICON = "thunder_armor_icon";

        private const string FROST_HELM_LOC_KEY = "BossRush_FrostCrown";
        private const string FROST_ARMOR_LOC_KEY = "BossRush_IceArmor";
        private const string THUNDER_HELM_LOC_KEY = "BossRush_ThunderHorn";
        private const string THUNDER_ARMOR_LOC_KEY = "BossRush_ThunderArmor";

        private const int DEFAULT_QUALITY = 6;
        private const float DEFAULT_DURABILITY = 999f;
        private const float DEFAULT_ARMOR_VALUE = 5f;

        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName))
            {
                return false;
            }

            if (baseName.Equals(FROST_HELMET_BASE, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureSetItem(item, FROST_HELMET_ID, FROST_HELM_LOC_KEY, "Helmat", "HeadArmor", DEFAULT_QUALITY,
                    FROST_HELMET_BASE, FROST_SET_BUNDLE, FROST_HELMET_ICON, false);
                return true;
            }

            if (baseName.Equals(FROST_ARMOR_BASE, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureSetItem(item, FROST_ARMOR_ID, FROST_ARMOR_LOC_KEY, "Armor", "BodyArmor", DEFAULT_QUALITY,
                    FROST_ARMOR_BASE, FROST_SET_BUNDLE, FROST_ARMOR_ICON, false);
                return true;
            }

            if (baseName.Equals(THUNDER_HELMET_BASE, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureSetItem(item, THUNDER_HELMET_ID, THUNDER_HELM_LOC_KEY, "Helmat", "HeadArmor", DEFAULT_QUALITY,
                    THUNDER_HELMET_BASE, THUNDER_SET_BUNDLE, THUNDER_HELMET_ICON, false);
                return true;
            }

            if (baseName.Equals(THUNDER_ARMOR_BASE, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureSetItem(item, THUNDER_ARMOR_ID, THUNDER_ARMOR_LOC_KEY, "Armor", "BodyArmor", DEFAULT_QUALITY,
                    THUNDER_ARMOR_BASE, THUNDER_SET_BUNDLE, THUNDER_ARMOR_ICON, false);
                return true;
            }

            return false;
        }

        public static bool TryConfigureByTypeId(Item item)
        {
            if (item == null)
            {
                return false;
            }

            switch (item.TypeID)
            {
                case FROST_HELMET_ID:
                    ConfigureSetItem(item, FROST_HELMET_ID, FROST_HELM_LOC_KEY, "Helmat", "HeadArmor", DEFAULT_QUALITY,
                        FROST_HELMET_BASE, FROST_SET_BUNDLE, FROST_HELMET_ICON, true);
                    return true;
                case FROST_ARMOR_ID:
                    ConfigureSetItem(item, FROST_ARMOR_ID, FROST_ARMOR_LOC_KEY, "Armor", "BodyArmor", DEFAULT_QUALITY,
                        FROST_ARMOR_BASE, FROST_SET_BUNDLE, FROST_ARMOR_ICON, true);
                    return true;
                case THUNDER_HELMET_ID:
                    ConfigureSetItem(item, THUNDER_HELMET_ID, THUNDER_HELM_LOC_KEY, "Helmat", "HeadArmor", DEFAULT_QUALITY,
                        THUNDER_HELMET_BASE, THUNDER_SET_BUNDLE, THUNDER_HELMET_ICON, true);
                    return true;
                case THUNDER_ARMOR_ID:
                    ConfigureSetItem(item, THUNDER_ARMOR_ID, THUNDER_ARMOR_LOC_KEY, "Armor", "BodyArmor", DEFAULT_QUALITY,
                        THUNDER_ARMOR_BASE, THUNDER_SET_BUNDLE, THUNDER_ARMOR_ICON, true);
                    return true;
            }

            return false;
        }

        private static void ConfigureSetItem(
            Item item,
            int typeId,
            string locKey,
            string slotTag,
            string armorStatKey,
            int quality,
            string modelBaseName,
            string bundleName,
            string iconAssetName,
            bool bindSupportingResources)
        {
            if (item == null)
            {
                return;
            }

            item.SetTypeID(typeId);
            item.DisplayNameRaw = locKey;
            item.Quality = quality;
            item.MaxDurability = DEFAULT_DURABILITY;
            item.Durability = DEFAULT_DURABILITY;
            item.MaxStackCount = 1;
            if (item.StackCount <= 0)
            {
                item.StackCount = 1;
            }

            EquipmentHelper.AddTagToItem(item, slotTag);
            EnsureBaseArmorModifier(item, armorStatKey, DEFAULT_ARMOR_VALUE);
            // 雷霆件加风暴防护、冰霜件加寒冷防护（与龙王套装同款 stat；幂等，占位符重配也不会叠加）
            bool isThunderPiece = typeId == THUNDER_HELMET_ID || typeId == THUNDER_ARMOR_ID;
            EnsureBaseArmorModifier(item, isThunderPiece ? "StormProtection" : "ColdProtection", WEATHER_PROTECTION_PER_PIECE);
            item.Value = SET_PIECE_VALUE;
            // 可维修标签：官方 Item.Repairable = UseDurability && Tags.Contains("Repairable")，
            // 而 UseDurability 就是 MaxDurability > 0——耐久 999 的这四件必然为 true。
            // 不打这个标签，维修台会直接显示「无法维修」，磨损只能永久带着（龙王套装同款处理）。
            EquipmentHelper.AddRepairableTag(item);
            EquipmentHelperIcon.TryInjectIcon(item, bundleName, iconAssetName);

            if (bindSupportingResources)
            {
                EquipmentFactory.TryBindLoadedEquipmentModel(item, modelBaseName);
            }
        }

        private static void EnsureBaseArmorModifier(Item item, string key, float value)
        {
            if (item == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (PrefabHasModifier(item, key))
            {
                return;
            }

            EquipmentHelper.AddModifierToItem(item, key, ModifierType.Add, value, true);
        }

        private static bool PrefabHasModifier(Item item, string key)
        {
            if (item == null || item.Modifiers == null)
            {
                return false;
            }

            foreach (ModifierDescription mod in item.Modifiers)
            {
                if (mod != null && mod.Key == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
