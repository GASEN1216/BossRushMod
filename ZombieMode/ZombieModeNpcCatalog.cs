// 补给终端 / 医疗终端 / 即时工事补给的商品列表、库存、价格曲线和生成站位。
// TypeId = -1 表示运行时按 GrantTag 与品质范围从 ItemFilter 抽取。

namespace BossRush
{
    public static class ZombieModeNpcCatalog
    {
        public const int MaxMerchantStockButtons = 16;

        public sealed class MerchantStockEntry
        {
            public int TypeId;       // 物品 TypeID
            public int StockCount;   // 库存
            public int BasePrice;    // 基础单价（净化点数）
            public string DisplayKey;
            public string GrantTag;
            public int GrantMinQuality = 1;
            public int GrantMaxQuality = 4;
        }

        public sealed class NurseServiceEntry
        {
            public string ServiceKey; // BossRush_ZombieMode_Npc_NurseService_*
            public int BasePrice;     // 净化点数
            public int Uses;          // 次数上限
        }

        public static readonly MerchantStockEntry[] NormalWaveStock =
        {
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 500, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Gun",       GrantTag = "Gun",       GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 300, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Melee",     GrantTag = "MeleeWeapon", GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 260, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Accessory",  GrantTag = "Accessory", GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 100, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bullet",     GrantTag = "Bullet",    GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 350, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Helmat",     GrantTag = "Helmet",    GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 400, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Armor",      GrantTag = "Armor",     GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 260, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Backpack",   GrantTag = "Backpack",  GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 500, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Totem",      GrantTag = "Totem",     GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 180, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Mask",       GrantTag = "Mask",      GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 3, BasePrice = 80,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Medical",    GrantTag = "Medical",   GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 4, BasePrice = 30,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Food",       GrantTag = "Food",      GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 3, BasePrice = 45,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bait",       GrantTag = "Bait",      GrantMinQuality = 1, GrantMaxQuality = 3 }
        };

        public static readonly MerchantStockEntry[] BossNodeStock =
        {
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 800, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Gun",       GrantTag = "Gun",       GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 500, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Melee",     GrantTag = "MeleeWeapon", GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 420, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Accessory",  GrantTag = "Accessory", GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 100, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bullet",     GrantTag = "Bullet",    GrantMinQuality = 2, GrantMaxQuality = 5 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 550, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Helmat",     GrantTag = "Helmet",    GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 650, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Armor",      GrantTag = "Armor",     GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 380, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Backpack",   GrantTag = "Backpack",  GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 900, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Totem",      GrantTag = "Totem",     GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1, BasePrice = 280, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Mask",       GrantTag = "Mask",      GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2, BasePrice = 180, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Medical",    GrantTag = "Medical",   GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 3, BasePrice = 50,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Food",       GrantTag = "Food",      GrantMinQuality = 2, GrantMaxQuality = 5 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2, BasePrice = 70,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_Bait",       GrantTag = "Bait",      GrantMinQuality = 2, GrantMaxQuality = 5 }
        };

        public static readonly NurseServiceEntry[] NurseServices =
        {
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_HealHalf",  BasePrice = 120, Uses = 5 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_HealFull",  BasePrice = 300, Uses = 2 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_Detox",     BasePrice = 80,  Uses = 4 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_StopBleed", BasePrice = 60,  Uses = 4 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_FirstAid",  BasePrice = 500, Uses = 1 }
        };

        // 工事补给包数量（普通波 / Boss 节点）
        public const int RepairPackFoldableCoverNormal = 1;
        public const int RepairPackReinforcedRoadblockNormal = 1;
        public const int RepairPackBarbedWireNormal = 1;
        public const int RepairPackEmergencyRepairSprayNormal = 1;
        public const int RepairPackFoldableCoverBoss = 2;
        public const int RepairPackReinforcedRoadblockBoss = 2;
        public const int RepairPackBarbedWireBoss = 2;
        public const int RepairPackEmergencyRepairSprayBoss = 2;

        public static float GetPollutionPriceMultiplier(int totalPollution)
        {
            if (totalPollution >= 25) return 1.50f;
            if (totalPollution >= 20) return 1.40f;
            if (totalPollution >= 15) return 1.30f;
            if (totalPollution >= 10) return 1.20f;
            if (totalPollution >= 5)  return 1.10f;
            return 1.00f;
        }

        // 多 NPC 角度均分
        public static readonly float[] NpcAngleArrangement = { 0f, 120f, -120f };
    }
}
