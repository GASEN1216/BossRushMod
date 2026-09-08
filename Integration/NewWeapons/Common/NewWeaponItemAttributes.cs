// ============================================================================
// NewWeaponItemAttributes.cs - 五把新武器的公共物品属性唯一写入点
// ============================================================================
// 模块说明：
//   品质 / 售价 / 耐久 / 可维修标签这四项此前分散在两条路径上：
//   真 bundle 路径（XxxWeaponConfig.TryConfigure）根本没写，
//   占位符路径（NewWeaponPlaceholderRegistry）硬编码 Quality=5 / MaxDurability=999，
//   于是「有 bundle 时无品质无售价、没 bundle 时才有」。本类把两条路径收敛到同一张表。
//
//   Value 必须设：StockShop 价格 = Value x 耐久比 x priceFactor，
//   不设 Value 叮当商店会把武器标价 0 元（与 FrostThunderSetConfig 同款陷阱）。
//
//   Repairable 标签必须打：官方 Item.Repairable = UseDurability && Tags.Contains("Repairable")，
//   而 UseDurability 就是 MaxDurability > 0。有耐久的近战不打标签，维修台会直接显示「无法维修」。
//
//   图腾（能量盾 / 雷电戒指）不设耐久也不打可维修标签：既有图腾（飞行图腾、逆鳞）都不设，
//   而且定价不需要它——官方 Item.GetTotalRawValue() 在 UseDurability 为 false 时原样返回 Value，
//   只有 MaxDurability > 0 时才按耐久比缩放。给图腾挂一条永远不会磨损的耐久条没有意义。
//
//   幂等：可被 bundle 路径与占位符路径重复调用，重复调用结果一致。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 五把新武器（500048-500052）的公共物品属性写入点
    /// </summary>
    internal static class NewWeaponItemAttributes
    {
        /// <summary>近战武器的耐久上限。沿用占位符路径既有的 999。图腾不使用本值。</summary>
        public const float MeleeMaxDurability = 999f;

        /// <summary>共享品质。五把武器的 XxxConfig.ItemQuality 都是 5，此处与之保持一致。</summary>
        public const int SharedQuality = 5;

        // 售价档位：品质 6 的冰霜/雷霆套装件是 30000（FrostThunderSetConfig.SET_PIECE_VALUE），
        // 品质 5 的这五件取其下方档位；主手近战比被动图腾贵一档。
        public const int MeleeWeaponValue = 20000;
        public const int TotemValue = 16000;

        /// <summary>
        /// 按 TypeID 写入品质 / 售价 / 耐久 / 可维修标签。item 为 null 或 TypeID 不属于本批时不做任何事。
        /// </summary>
        public static void Apply(Item item, int typeId)
        {
            if (item == null) return;

            int value;
            bool isMelee;
            if (!TryGetProfileForTypeId(typeId, out value, out isMelee)) return;

            try
            {
                item.Quality = SharedQuality;

                if (isMelee)
                {
                    // 顺序要紧：官方 Durability setter 是 Mathf.Min(MaxDurability, value)，
                    // 先写 Durability 会被旧上限钳掉。
                    item.MaxDurability = MeleeMaxDurability;
                    item.Durability = MeleeMaxDurability;
                    EquipmentHelper.AddRepairableTag(item);
                }

                item.MaxStackCount = 1;
                if (item.StackCount <= 0)
                {
                    item.StackCount = 1;
                }
                item.Value = value;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponAttributes] 写入物品属性失败 (TypeID=" + typeId + "): " + e.Message);
            }
        }

        /// <summary>
        /// 本批武器的售价与形态查表。返回 false 表示该 TypeID 不属于五把新武器。
        /// isMelee 决定是否写耐久与可维修标签——图腾两件不写。
        /// </summary>
        private static bool TryGetProfileForTypeId(int typeId, out int value, out bool isMelee)
        {
            if (typeId == NewWeaponIds.ViperDaggerTypeId ||
                typeId == NewWeaponIds.SummonStaffTypeId ||
                typeId == NewWeaponIds.FrostSpearTypeId)
            {
                value = MeleeWeaponValue;
                isMelee = true;
                return true;
            }

            if (typeId == NewWeaponIds.EnergyShieldTypeId ||
                typeId == NewWeaponIds.ThunderRingTypeId)
            {
                value = TotemValue;
                isMelee = false;
                return true;
            }

            value = 0;
            isMelee = false;
            return false;
        }
    }

    /// <summary>
    /// 五把新武器在叮当商店的上架参数。
    /// 5 级介于冷淬液 4 级与套装件 6 级之间——它们是品质 5，比品质 6 的套装件早一档；
    /// 库存 1 与套装件/钻戒同款（NPCShopSystem 每次开店重置）。
    /// </summary>
    internal static class NewWeaponShopConfig
    {
        public const int UnlockLevel = 5;
        public const int MaxStock = 1;
    }
}
