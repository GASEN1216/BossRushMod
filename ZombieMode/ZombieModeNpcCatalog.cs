// SPEC 19 NPC 服务价目表配置。
// 临时商人 / 临时护士 / 即时工事补给的商品列表、库存、价格曲线和生成站位。
// TypeId = -1 表示运行时按 GrantTag 与品质范围从 ItemFilter 抽取。

namespace BossRush
{
    public static class ZombieModeNpcCatalog
    {
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

        // SPEC 19 §3.2.1 普通波后生成
        public static readonly MerchantStockEntry[] NormalWaveStock =
        {
            // 弹药按标签抽取，避免硬编码具体口径 TypeID。
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 1,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Ammo",    GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 1,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Bullet",  GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 1,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Ammo",    GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 60,  BasePrice = 2,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Bullet",  GrantMinQuality = 1, GrantMaxQuality = 3 },
            // 医疗
            new MerchantStockEntry { TypeId = -1, StockCount = 5,   BasePrice = 30,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Medic",   GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 3,   BasePrice = 60,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Medical", GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2,   BasePrice = 120, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Healing", GrantMinQuality = 2, GrantMaxQuality = 4 },
            // 食物饮料
            new MerchantStockEntry { TypeId = -1, StockCount = 4,   BasePrice = 30,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomFood",    GrantTag = "Food",    GrantMinQuality = 1, GrantMaxQuality = 3 },
            new MerchantStockEntry { TypeId = -1, StockCount = 4,   BasePrice = 25,  DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomDrink",   GrantTag = "Drink",   GrantMinQuality = 1, GrantMaxQuality = 3 },
            // 装备（按当前阶层随机抽取，TypeId = -1 表示运行时 ItemFilter 抽取）
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 300, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMelee",   GrantTag = "MeleeWeapon", GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 500, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomGun",     GrantTag = "Gun",     GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 400, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomArmor",   GrantTag = "Armor",   GrantMinQuality = 1, GrantMaxQuality = 4 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 350, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomHelmet",  GrantTag = "Helmet",  GrantMinQuality = 1, GrantMaxQuality = 4 }
        };

        // SPEC 19 §3.2.2 Boss 节点后生成
        public static readonly MerchantStockEntry[] BossNodeStock =
        {
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 2,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Ammo",    GrantMinQuality = 2, GrantMaxQuality = 5 },
            new MerchantStockEntry { TypeId = -1, StockCount = 120, BasePrice = 2,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Bullet",  GrantMinQuality = 2, GrantMaxQuality = 5 },
            new MerchantStockEntry { TypeId = -1, StockCount = 60,  BasePrice = 4,   DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",    GrantTag = "Ammo",    GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2,   BasePrice = 220, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Medical", GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2,   BasePrice = 100, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Medic",   GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 2,   BasePrice = 180, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMedical", GrantTag = "Healing", GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 500, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomMelee",   GrantTag = "MeleeWeapon", GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 800, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomGun",     GrantTag = "Gun",     GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 650, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomArmor",   GrantTag = "Armor",   GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = -1, StockCount = 1,   BasePrice = 550, DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomHelmet",  GrantTag = "Helmet",  GrantMinQuality = 3, GrantMaxQuality = 6 },
            new MerchantStockEntry { TypeId = FoldableCoverPackConfig.TYPE_ID,       StockCount = 2,   BasePrice = 400, DisplayKey = "BossRush_ZombieMode_Reward_FortificationPack", GrantTag = "", GrantMinQuality = 1, GrantMaxQuality = 1 },
            new MerchantStockEntry { TypeId = ReinforcedRoadblockPackConfig.TYPE_ID, StockCount = 2,   BasePrice = 350, DisplayKey = "BossRush_ZombieMode_Reward_FortificationPack", GrantTag = "", GrantMinQuality = 1, GrantMaxQuality = 1 }
        };

        // SPEC 19 §4.2 临时护士服务
        public static readonly NurseServiceEntry[] NurseServices =
        {
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_HealHalf",  BasePrice = 120, Uses = 5 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_HealFull",  BasePrice = 300, Uses = 2 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_Detox",     BasePrice = 80,  Uses = 4 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_StopBleed", BasePrice = 60,  Uses = 4 },
            new NurseServiceEntry { ServiceKey = "BossRush_ZombieMode_Npc_NurseService_FirstAid",  BasePrice = 500, Uses = 1 }
        };

        // SPEC 19 §5 工事补给包数量（普通波 / Boss 节点）
        public const int RepairPackFoldableCoverNormal = 1;
        public const int RepairPackReinforcedRoadblockNormal = 1;
        public const int RepairPackBarbedWireNormal = 1;
        public const int RepairPackEmergencyRepairSprayNormal = 1;
        public const int RepairPackFoldableCoverBoss = 2;
        public const int RepairPackReinforcedRoadblockBoss = 2;
        public const int RepairPackBarbedWireBoss = 2;
        public const int RepairPackEmergencyRepairSprayBoss = 2;

        // SPEC 19 §2.6 价格污染倍率
        public static float GetPollutionPriceMultiplier(int totalPollution)
        {
            if (totalPollution >= 25) return 1.50f;
            if (totalPollution >= 20) return 1.40f;
            if (totalPollution >= 15) return 1.30f;
            if (totalPollution >= 10) return 1.20f;
            if (totalPollution >= 5)  return 1.10f;
            return 1.00f;
        }

        // SPEC 19 §2.1 多 NPC 角度均分（0° / +120° / -120°）
        public static readonly float[] NpcAngleArrangement = { 0f, 120f, -120f };
    }
}
