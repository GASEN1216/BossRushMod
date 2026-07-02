using System;
using System.Collections.Generic;
using System.IO;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// BossRush 动态物品按需注册表。
    /// 官方存档/商店/UI 可能在延迟 bootstrap 前按 TypeID 查询 prefab 或 metadata；
    /// 所有这类同步兜底都必须走这里，避免各奖励路径重复维护 bundle 清单。
    /// </summary>
    internal static class BossRushDynamicItemRegistry
    {
        private sealed class RegistrationPlan
        {
            public string[] EquipmentBundles = BossRushDynamicItemRegistry.EmptyBundles;
            public string[] ItemBundles = BossRushDynamicItemRegistry.EmptyBundles;
            public Func<bool> SpecialLoader;
            public Func<int, bool> FallbackLoader;
        }

        private static readonly string[] EmptyBundles = new string[0];
        private static readonly Dictionary<int, RegistrationPlan> Plans = BuildPlans();
        private static readonly HashSet<int> InProgress = new HashSet<int>();
        private static readonly HashSet<int> FailureLogged = new HashSet<int>();
        private static int prefabCheckDepth = 0;
        private static bool itemConfiguratorsRegistered = false;

        internal static bool IsPatchBypassed
        {
            get { return prefabCheckDepth > 0; }
        }

        internal static bool IsBossRushDynamicItemType(int typeId)
        {
            return Plans.ContainsKey(typeId);
        }

        internal static bool EnsureRegistered(int typeId)
        {
            RegistrationPlan plan;
            if (!Plans.TryGetValue(typeId, out plan))
            {
                return false;
            }

            if (HasRegisteredPrefabWithoutEnsuring(typeId))
            {
                return true;
            }

            if (InProgress.Contains(typeId))
            {
                return false;
            }

            InProgress.Add(typeId);
            try
            {
                bool attempted = false;

                if (plan.SpecialLoader != null)
                {
                    attempted = TryRunLoader(typeId, "special", plan.SpecialLoader) || attempted;
                }

                if (plan.EquipmentBundles.Length > 0)
                {
                    attempted = true;
                    LoadEquipmentBundles(typeId, plan.EquipmentBundles);
                }

                if (plan.ItemBundles.Length > 0)
                {
                    attempted = true;
                    EnsureItemConfiguratorsRegistered();
                    LoadItemBundles(typeId, plan.ItemBundles);
                }

                if (!HasRegisteredPrefabWithoutEnsuring(typeId) && plan.FallbackLoader != null)
                {
                    attempted = TryRunLoader(typeId, "fallback", plan.FallbackLoader) || attempted;
                }

                bool ready = HasRegisteredPrefabWithoutEnsuring(typeId);
                if (!ready && attempted)
                {
                    LogFailureOnce(typeId, "按需注册后仍未找到 prefab");
                }

                return ready;
            }
            finally
            {
                InProgress.Remove(typeId);
            }
        }

        internal static int EnsureRegistered(IEnumerable<int> typeIds)
        {
            if (typeIds == null)
            {
                return 0;
            }

            int readyCount = 0;
            foreach (int typeId in typeIds)
            {
                if (EnsureRegistered(typeId))
                {
                    readyCount++;
                }
            }

            return readyCount;
        }

        internal static bool HasRegisteredPrefabWithoutEnsuring(int typeId)
        {
            prefabCheckDepth++;
            try
            {
                return ItemAssetsCollection.GetPrefab(typeId) != null;
            }
            catch
            {
                return false;
            }
            finally
            {
                prefabCheckDepth--;
            }
        }

        internal static void ResetStaticCaches()
        {
            InProgress.Clear();
            FailureLogged.Clear();
            prefabCheckDepth = 0;
            itemConfiguratorsRegistered = false;
        }

        private static void EnsureItemConfiguratorsRegistered()
        {
            if (itemConfiguratorsRegistered)
            {
                return;
            }

            ModBehaviour instance = ModBehaviour.Instance;
            if (instance == null)
            {
                return;
            }

            try
            {
                if (instance.EnsureItemContentConfiguratorsRegisteredForDynamicRegistry())
                {
                    itemConfiguratorsRegistered = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] 注册 Item configurator 失败: " + e.Message);
            }
        }

        private static void LoadEquipmentBundles(int typeId, string[] bundleNames)
        {
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string bundleName = bundleNames[i];
                if (string.IsNullOrEmpty(bundleName))
                {
                    continue;
                }

                try
                {
                    EquipmentFactory.LoadBundle(bundleName);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] 加载装备 bundle 失败: TypeID=" + typeId + ", bundle=" + bundleName + ", " + e.Message);
                }
            }
        }

        private static void LoadItemBundles(int typeId, string[] bundleNames)
        {
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string bundleName = bundleNames[i];
                if (string.IsNullOrEmpty(bundleName))
                {
                    continue;
                }

                try
                {
                    ItemFactory.LoadBundle(bundleName);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] 加载物品 bundle 失败: TypeID=" + typeId + ", bundle=" + bundleName + ", " + e.Message);
                }
            }
        }

        private static bool TryRunLoader(int typeId, string label, Func<bool> loader)
        {
            try
            {
                return loader != null && loader();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] " + label + " 注册失败: TypeID=" + typeId + ", " + e.Message);
                return false;
            }
        }

        private static bool TryRunLoader(int typeId, string label, Func<int, bool> loader)
        {
            try
            {
                return loader != null && loader(typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] " + label + " 注册失败: TypeID=" + typeId + ", " + e.Message);
                return false;
            }
        }

        private static void LogFailureOnce(int typeId, string message)
        {
            if (!FailureLogged.Add(typeId))
            {
                return;
            }

            ModBehaviour.DevLog("[BossRushDynamicItemRegistry] [WARNING] " + message + ": TypeID=" + typeId);
        }

        private static Dictionary<int, RegistrationPlan> BuildPlans()
        {
            Dictionary<int, RegistrationPlan> plans = new Dictionary<int, RegistrationPlan>();

            Add(plans, new RegistrationPlan { SpecialLoader = EnsureBossRushTicket }, BossRushItemIds.BossRushTicket);
            Add(plans, new RegistrationPlan { SpecialLoader = EnsureBirthdayCake }, BossRushItemIds.BirthdayCake);
            Add(plans, new RegistrationPlan { SpecialLoader = EnsureAdventureJournal }, BossRushItemIds.AdventureJournal);

            Add(plans, EquipmentOnly("dragon_equipment"),
                DragonDescendantConfig.DRAGON_HELM_TYPE_ID,
                DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID,
                DragonDescendantConfig.DRAGON_BREATH_TYPE_ID);
            Add(plans, EquipmentOnly("flight_totem"), DragonKingConfig.DRAGON_KING_LOOT_TYPE_ID);
            Add(plans, EquipmentOnly("dragonking_equipment"),
                DragonKingConfig.DRAGON_KING_HELM_TYPE_ID,
                DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID,
                DragonKingConfig.REVERSE_SCALE_TYPE_ID,
                DragonKingBossGunConfig.WeaponTypeId);
            Add(plans, EquipmentAndItem(
                new string[] { "fenhuang_halberd_model" },
                new string[] { "fenhuang_halberd_item" }), DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID);
            Add(plans, EquipmentAndItem(
                new string[] { "frostmourne_model" },
                new string[] { "frostmourne_item" }), FrostmourneIds.WeaponTypeId);
            Add(plans, EquipmentOnly("phantom_scythe"), PhantomWitchScytheIds.WeaponTypeId);

            Add(plans, ItemOnly(AwenCourierTokenConfig.BUNDLE_NAME), AwenCourierTokenConfig.TYPE_ID);
            Add(plans, ItemOnly(ColdQuenchFluidConfig.BUNDLE_NAME), ColdQuenchFluidConfig.TYPE_ID);
            Add(plans, ItemOnly(BrickStoneConfig.BUNDLE_NAME), BrickStoneConfig.TYPE_ID);
            Add(plans, ItemOnly(DingdangDrawingConfig.BUNDLE_NAME), DingdangDrawingConfig.TYPE_ID);
            Add(plans, ItemOnly(DiamondConfig.BUNDLE_NAME), DiamondConfig.TYPE_ID);
            Add(plans, ItemOnly(AchievementMedalConfig.BUNDLE_NAME), AchievementMedalConfig.TYPE_ID);
            Add(plans, ItemOnly(WildHornConfig.BUNDLE_NAME), WildHornConfig.TYPE_ID);
            Add(plans, ItemOnly("faction_flags"), FactionFlagConfig.ALL_FLAG_TYPE_IDS);
            Add(plans, ItemOnly("respawn_items"), RespawnItemConfig.ALL_RESPAWN_ITEM_TYPE_IDS);
            Add(plans, ItemOnly(DiamondRingConfig.BUNDLE_NAME), DiamondRingConfig.TYPE_ID);
            Add(plans, ItemOnly(CalmingDropsConfig.BUNDLE_NAME), CalmingDropsConfig.TYPE_ID);
            Add(plans, ItemOnly(PeaceCharmConfig.BUNDLE_NAME), PeaceCharmConfig.TYPE_ID);
            Add(plans, ItemOnly(BloodhuntTransponderConfig.BUNDLE_NAME), BloodhuntTransponderConfig.TYPE_ID);
            Add(plans, ItemOnly(FoldableCoverPackConfig.BUNDLE_NAME), FoldableCoverPackConfig.TYPE_ID);
            Add(plans, ItemOnly(ReinforcedRoadblockPackConfig.BUNDLE_NAME), ReinforcedRoadblockPackConfig.TYPE_ID);
            Add(plans, ItemOnly(BarbedWirePackConfig.BUNDLE_NAME), BarbedWirePackConfig.TYPE_ID);
            Add(plans, ItemOnly(EmergencyRepairSprayConfig.BUNDLE_NAME), EmergencyRepairSprayConfig.TYPE_ID);
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { AwenLootSweepTokenConfig.BUNDLE_NAME },
                FallbackLoader = delegate(int typeId) { return AwenLootSweepTokenConfig.EnsureRuntimeRegistration(); }
            }, AwenLootSweepTokenConfig.TYPE_ID);
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { ZombieTideInvitationConfig.BUNDLE_NAME },
                FallbackLoader = delegate(int typeId) { return ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.ZombieTideInvitation);
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { ZombieTideBeaconConfig.BUNDLE_NAME },
                FallbackLoader = delegate(int typeId) { return ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.ZombieTideBeacon);

            Add(plans, NewWeaponPlan("viperdagger_melee_model", "viperdagger_item"), NewWeaponIds.ViperDaggerTypeId);
            Add(plans, NewWeaponPlan("summonstaff_melee_model", "summonstaff_item"), NewWeaponIds.SummonStaffTypeId);
            Add(plans, NewWeaponPlan("energyshield_totem_model", "energyshield_item"), NewWeaponIds.EnergyShieldTypeId);
            Add(plans, NewWeaponPlan("frostspear_melee_model", "frostspear_item"), NewWeaponIds.FrostSpearTypeId);
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { "thunderring_item" },
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, NewWeaponIds.ThunderRingTypeId);

            Add(plans, new RegistrationPlan
            {
                EquipmentBundles = new string[] { "frost_set" },
                FallbackLoader = EnsureSetBonusPlaceholder
            }, 500053, 500054);
            Add(plans, new RegistrationPlan
            {
                EquipmentBundles = new string[] { "thunder_set" },
                FallbackLoader = EnsureSetBonusPlaceholder
            }, 500055, 500056);

            return plans;
        }

        private static RegistrationPlan EquipmentOnly(string bundleName)
        {
            return new RegistrationPlan { EquipmentBundles = new string[] { bundleName } };
        }

        private static RegistrationPlan ItemOnly(string bundleName)
        {
            return new RegistrationPlan { ItemBundles = new string[] { bundleName } };
        }

        private static RegistrationPlan EquipmentAndItem(string[] equipmentBundles, string[] itemBundles)
        {
            return new RegistrationPlan { EquipmentBundles = equipmentBundles, ItemBundles = itemBundles };
        }

        private static RegistrationPlan NewWeaponPlan(string equipmentBundle, string itemBundle)
        {
            return new RegistrationPlan
            {
                EquipmentBundles = new string[] { equipmentBundle },
                ItemBundles = new string[] { itemBundle },
                FallbackLoader = EnsureNewWeaponPlaceholder
            };
        }

        private static void Add(Dictionary<int, RegistrationPlan> plans, RegistrationPlan plan, params int[] typeIds)
        {
            for (int i = 0; i < typeIds.Length; i++)
            {
                plans[typeIds[i]] = plan;
            }
        }

        private static bool EnsureBossRushTicket()
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.EnsureBossRushTicketItemRegisteredForDynamicRegistry();
        }

        private static bool EnsureBirthdayCake()
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.EnsureBirthdayCakeItemRegisteredForDynamicRegistry();
        }

        private static bool EnsureAdventureJournal()
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.EnsureAdventureJournalItemRegisteredForDynamicRegistry();
        }

        private static bool EnsureNewWeaponPlaceholder(int typeId)
        {
            return NewWeaponPlaceholderRegistry.EnsureRegistered(typeId);
        }

        private static bool EnsureSetBonusPlaceholder(int typeId)
        {
            return SetBonusPlaceholderRegistry.EnsureRegistered(typeId);
        }
    }

    public partial class ModBehaviour
    {
        internal bool EnsureItemContentConfiguratorsRegisteredForDynamicRegistry()
        {
            RegisterItemContentConfigurators();
            return true;
        }

        internal bool EnsureBossRushTicketItemRegisteredForDynamicRegistry()
        {
            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.BossRushTicket))
            {
                if (bossRushTicketTypeId <= 0)
                {
                    bossRushTicketTypeId = BossRushItemIds.BossRushTicket;
                }
                return true;
            }

            AssetBundle bundle = null;
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "bossrush_ticket");

                if (!File.Exists(bundlePath))
                {
                    DevLog("[BossRushDynamicItemRegistry] 未找到 bossrush_ticket AssetBundle: " + bundlePath);
                    return false;
                }

                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    DevLog("[BossRushDynamicItemRegistry] AssetBundle.LoadFromFile 失败: " + bundlePath);
                    return false;
                }

                UnityEngine.Object[] assets = bundle.LoadAllAssets<UnityEngine.Object>();
                if (assets == null || assets.Length == 0)
                {
                    DevLog("[BossRushDynamicItemRegistry] bossrush_ticket AssetBundle 中未找到任何资源");
                    return false;
                }

                int itemCount = 0;
                bool targetRegistered = false;
                foreach (UnityEngine.Object obj in assets)
                {
                    GameObject go = obj as GameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    Item itemPrefab = go.GetComponent<Item>();
                    if (itemPrefab == null)
                    {
                        continue;
                    }

                    AddTagsToItem(itemPrefab, new string[] { "Key", "SpecialKey" });
                    ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                    itemCount++;

                    if (itemPrefab.TypeID == BossRushItemIds.BossRushTicket)
                    {
                        targetRegistered = true;
                        bossRushTicketTypeId = itemPrefab.TypeID;
                    }
                    else if (bossRushTicketTypeId <= 0 && itemPrefab.TypeID > 0)
                    {
                        bossRushTicketTypeId = itemPrefab.TypeID;
                    }
                }

                DevLog("[BossRushDynamicItemRegistry] bossrush_ticket 按需注册完成：Item=" + itemCount + ", BossRushTicketTypeId=" + bossRushTicketTypeId);
                return targetRegistered || BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.BossRushTicket);
            }
            catch (Exception e)
            {
                DevLog("[BossRushDynamicItemRegistry] bossrush_ticket 按需注册失败: " + e.Message);
                return false;
            }
            finally
            {
                AssetBundleUnloadHelper.TryUnload(bundle, "[BossRushDynamicItemRegistry]");
            }
        }

        internal bool EnsureBirthdayCakeItemRegisteredForDynamicRegistry()
        {
            InitializeBirthdayCakeItem();
            return BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.BirthdayCake);
        }

        internal bool EnsureAdventureJournalItemRegisteredForDynamicRegistry()
        {
            InitializeWikiBookItem();
            return BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.AdventureJournal);
        }
    }
}
