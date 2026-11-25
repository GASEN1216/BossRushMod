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
            public string SplitModelBaseName;
            public bool SplitModelIsMelee;
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

        internal static bool IsRegistrationInProgress(int typeId)
        {
            return InProgress.Contains(typeId);
        }

        internal static int PublishedTypeCount
        {
            get { return Plans.Count; }
        }

        internal static int[] GetPublishedTypeIds()
        {
            int[] ids = new int[Plans.Count];
            Plans.Keys.CopyTo(ids, 0);
            Array.Sort(ids);
            return ids;
        }

        internal static bool EnsureRegistered(int typeId)
        {
            RegistrationPlan plan;
            if (!Plans.TryGetValue(typeId, out plan))
            {
                return false;
            }

            Item prefab = GetRegisteredPrefabWithoutEnsuring(typeId);
            if (prefab != null && (plan.SplitModelBaseName == null || HasSplitModel(plan, prefab)))
            {
                return true;
            }

            if (InProgress.Contains(typeId))
            {
                return prefab != null;
            }

            InProgress.Add(typeId);
            try
            {
                // 物品与装备配置器由同一个入口登记，首次读档可能先请求仅有装备包的 TypeID。
                if ((plan.EquipmentBundles.Length > 0 || plan.ItemBundles.Length > 0) && !EnsureItemConfiguratorsRegistered())
                {
                    LogFailureOnce(typeId, "按需注册前未能登记内容配置器");
                    return false;
                }

                using (ResourceBundleLoader.BeginSynchronousRestore())
                {
                    if (plan.SpecialLoader != null)
                    {
                        TryRunLoader(typeId, "special", plan.SpecialLoader);
                    }

                    if (plan.EquipmentBundles.Length > 0)
                    {
                        LoadEquipmentBundles(typeId, plan.EquipmentBundles);
                    }

                    if (prefab == null && plan.ItemBundles.Length > 0)
                    {
                        LoadItemBundles(typeId, plan.ItemBundles);
                    }

                    if (!HasRegisteredPrefabWithoutEnsuring(typeId) && plan.FallbackLoader != null)
                    {
                        TryRunLoader(typeId, "fallback", plan.FallbackLoader);
                    }

                    prefab = GetRegisteredPrefabWithoutEnsuring(typeId);
                    if (prefab != null && plan.SplitModelBaseName != null && !HasSplitModel(plan, prefab))
                    {
                        ItemAgent model;
                        if (EquipmentFactory.TryGetLoadedModel(plan.SplitModelBaseName, out model) && model != null)
                        {
                            // 只补配官方登记的源 prefab，使下一次实例化带正确模型。玩家物品树由官方还原。
                            ItemFactory.ConfigureItemPrefab(prefab);
                        }
                        if (!HasSplitModel(plan, prefab)) LogFailureOnce(typeId, "指定装备模型未绑定: " + plan.SplitModelBaseName);
                    }
                }

                bool ready = prefab != null;
                if (!ready)
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

        private static bool HasSplitModel(RegistrationPlan plan, Item prefab)
        {
            ItemAgent model;
            if (!EquipmentFactory.TryGetLoadedModel(plan.SplitModelBaseName, out model) || model == null) return false;
            if (plan.SplitModelIsMelee) return EquipmentFactory.HasBoundMeleeModel(prefab, model);
            return EquipmentFactory.HasBoundEquipmentModel(prefab, model);
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

        internal static int EnsureAllRegistered()
        {
            return EnsureRegistered(Plans.Keys);
        }

        internal static bool HasRegisteredPrefabWithoutEnsuring(int typeId)
        {
            return GetRegisteredPrefabWithoutEnsuring(typeId) != null;
        }

        internal static Item GetRegisteredPrefabWithoutEnsuring(int typeId)
        {
            // 对 Mod TypeID 直接核对官方动态表，与原版实际实例化读取的是同一条记录。
            if (IsBossRushDynamicItemType(typeId))
            {
                ItemAssetsCollection.DynamicEntry entry;
                if (!ItemAssetsCollection.TryGetDynamicEntry(typeId, out entry)) return null;
                Item prefab = entry.prefab;
                return prefab != null && prefab.TypeID == typeId ? prefab : null;
            }

            prefabCheckDepth++;
            try
            {
                return ItemAssetsCollection.GetPrefab(typeId);
            }
            catch
            {
                return null;
            }
            finally
            {
                prefabCheckDepth--;
            }
        }

        /// <summary>
        /// Mod 物品 prefab 进官方动态表的唯一出口：先套声明的图标，再 AddDynamicEntry。
        /// 官方第一次读元数据时会缓存 prefab.Icon，所以图标必须在注册前就位（ModItemIcons 文件头）。
        /// </summary>
        internal static bool Publish(Item prefab)
        {
            if (prefab == null) return false;
            try { ModItemIcons.ApplyBeforePublish(prefab); }
            catch (Exception e)
            {
                // 图标资源出了问题，仍让官方读档拿到带槽位、标签与手持组件的真物品。
                ModBehaviour.CriticalLog("dynamic-item-icon-" + prefab.TypeID,
                    "[BossRushDynamicItemRegistry] 物品图标加载失败: " + prefab.TypeID + ", " + e.Message);
            }
            return ItemAssetsCollection.AddDynamicEntry(prefab);
        }

        /// <summary>
        /// Mod 重新启用时调用（生产图标包准备好之后、加载物品包之前）。
        /// 上一轮注册的 prefab 还留在官方动态表里，但它们借的图标随生产包卸载一起销毁了；
        /// 这里补上图标并重新注册，官方缓存的元数据也随之刷新。首次启动时表里没有 Mod 物品，什么都不做。
        /// </summary>
        internal static int RefreshPublishedIcons()
        {
            int refreshed = 0;
            foreach (int typeId in Plans.Keys)
            {
                Item prefab = GetRegisteredPrefabWithoutEnsuring(typeId);
                if (prefab == null || prefab.Icon != null) continue;
                try
                {
                    if (Publish(prefab)) refreshed++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BossRushDynamicItemRegistry] 重新启用时补图标失败 " + typeId + ": " + e.Message);
                }
            }
            if (refreshed > 0) ModBehaviour.DevLog("[BossRushDynamicItemRegistry] 重新启用：补回 " + refreshed + " 件物品的图标");
            return refreshed;
        }

        internal static void ResetStaticCaches()
        {
            InProgress.Clear();
            FailureLogged.Clear();
            prefabCheckDepth = 0;
            itemConfiguratorsRegistered = false;
        }

        private static bool EnsureItemConfiguratorsRegistered()
        {
            if (itemConfiguratorsRegistered)
            {
                return true;
            }

            ModBehaviour instance = ModBehaviour.Instance;
            if (instance == null)
            {
                return false;
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
                ModBehaviour.CriticalLog("dynamic-item-configurators", "[BossRushDynamicItemRegistry] [ERROR] 登记物品与装备配置器失败: " + e.Message);
            }
            return itemConfiguratorsRegistered;
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
                    ModBehaviour.CriticalLog(
                        "equipment-bundle-" + bundleName,
                        "[BossRushDynamicItemRegistry] [ERROR] 加载装备 bundle 失败: TypeID=" + typeId + ", bundle=" + bundleName + ", " + e.Message);
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
                    ModBehaviour.CriticalLog(
                        "item-bundle-" + bundleName,
                        "[BossRushDynamicItemRegistry] [ERROR] 加载物品 bundle 失败: TypeID=" + typeId + ", bundle=" + bundleName + ", " + e.Message);
                }
            }
        }

        private static void TryRunLoader(int typeId, string label, Func<bool> loader)
        {
            try
            {
                loader();
            }
            catch (Exception e)
            {
                LogFailureOnce(typeId, label + " 注册异常: " + e.Message);
            }
        }

        private static void TryRunLoader(int typeId, string label, Func<int, bool> loader)
        {
            try
            {
                loader(typeId);
            }
            catch (Exception e)
            {
                LogFailureOnce(typeId, label + " 注册异常: " + e.Message);
            }
        }

        private static void LogFailureOnce(int typeId, string message)
        {
            if (!FailureLogged.Add(typeId))
            {
                return;
            }

            ModBehaviour.CriticalLog("dynamic-item-prefab-" + typeId, "[BossRushDynamicItemRegistry] [WARNING] " + message + ": TypeID=" + typeId);
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
            Add(plans, SplitWeaponPlan("fenhuang_halberd_model", "fenhuang_halberd_item",
                FenHuangHalberdIds.ModelBaseName, true), DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID);
            Add(plans, SplitWeaponPlan("frostmourne_model", "frostmourne_item",
                FrostmourneIds.ModelBaseName, true), FrostmourneIds.WeaponTypeId);
            Add(plans, EquipmentOnly("phantom_scythe"), PhantomWitchScytheIds.WeaponTypeId);

            Add(plans, ItemOnly(AwenCourierTokenConfig.BUNDLE_NAME), AwenCourierTokenConfig.TYPE_ID);
            Add(plans, ItemOnly(ColdQuenchFluidConfig.BUNDLE_NAME), ColdQuenchFluidConfig.TYPE_ID);
            Add(plans, ItemOnly(BrickStoneConfig.BUNDLE_NAME), BrickStoneConfig.TYPE_ID);
            Add(plans, ItemOnly(DingdangDrawingConfig.BUNDLE_NAME), DingdangDrawingConfig.TYPE_ID);
            Add(plans, ItemOnly(DiamondConfig.BUNDLE_NAME), DiamondConfig.TYPE_ID);
            Add(plans, ItemOnly(AchievementMedalConfig.BUNDLE_NAME), AchievementMedalConfig.TYPE_ID);
            Add(plans, ItemOnly(WildHornConfig.BUNDLE_NAME), WildHornConfig.TYPE_ID);
            Add(plans, ItemOnly("faction_flags"), FactionFlagConfig.ALL_FLAG_TYPE_IDS);
            Add(plans, ItemOnly("respawn_items"),
                RespawnItemConfig.TAUNT_SMOKE_TYPE_ID,
                RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID);
            Add(plans, ItemOnly("bosscall_whistle"), RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID);
            Add(plans, ItemOnly("bloodhunt_beacon"), RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID);
            Add(plans, ItemOnly(DiamondRingConfig.BUNDLE_NAME), DiamondRingConfig.TYPE_ID);
            Add(plans, ItemOnly(CalmingDropsConfig.BUNDLE_NAME), CalmingDropsConfig.TYPE_ID);
            Add(plans, ItemOnly(PeaceCharmConfig.BUNDLE_NAME), PeaceCharmConfig.TYPE_ID);
            Add(plans, ItemOnly(BloodhuntTransponderConfig.BUNDLE_NAME), BloodhuntTransponderConfig.TYPE_ID);
            Add(plans, ItemOnly(FateEchoRelicConfig.BUNDLE_NAME), FateEchoRelicConfig.TYPE_ID);
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
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { PortableSafeZoneDeviceConfig.BUNDLE_NAME },
                FallbackLoader = delegate(int typeId) { return PortableSafeZoneDeviceConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.PortableSafeZoneDevice);
            // 遗种蛋：零新增 bundle，只有运行时克隆兜底（血脉写在 KV 上，全谱系共用一个 TypeID）
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return RelicEggConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.RelicEgg);

            // 后山种子与出击餐：同样零新增 bundle，走克隆兜底。
            // 必须登记在这里——玩家背包/菜地里存着这些 TypeID，
            // 漏登记会让重启后它们退化成官方 FallbackItem（AGENTS 契约第 6 节）。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return BackMountainItems.EnsureRuntimeRegistration(typeId); }
            },
                BossRushItemIds.DragonSeed,
                BossRushItemIds.EmberSeed,
                BossRushItemIds.PhantomSpore,
                BossRushItemIds.DragonFruit,
                BossRushItemIds.EmberChili,
                BossRushItemIds.PhantomMushroom);

            // 词缀熔石与鸭皇图鉴：同为零新增 bundle 的克隆兜底物品。
            // 两者的 EnsureRuntimeFallbackRegistrationShell 早已写好，但一直没登记进本表，
            // 于是重启后玩家背包/仓库里的它们会退化成官方 FallbackItem（AGENTS 契约第 6 节）。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return AffixForgeStoneConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.AffixForgeStone);
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return CodexBookConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.CodexBook);

            // 天空岛物品（纪念品、风标罗盘、岛上特产）：同为零新增 bundle 的克隆兜底。
            // 玩家仓库与背包里存着这些 TypeID，漏登记会让重启后它们退化成官方 FallbackItem（AGENTS 契约第 6 节）。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return SkyIslandItems.EnsureRuntimeRegistration(typeId); }
            },
                BossRushItemIds.SkyIslandHomecomingBadge,
                BossRushItemIds.SkyIslandWindeaterCore,
                BossRushItemIds.SkyIslandWindVaneCompass,
                BossRushItemIds.SkyIslandHomecomingBento,
                BossRushItemIds.SkyIslandStarmossSalve,
                // 内容批次三：采集材料、碎片凑整与局内耗材（同一套克隆兜底）。
                BossRushItemIds.SkyIslandCloudmossFiber,
                BossRushItemIds.SkyIslandGreenearSheaf,
                BossRushItemIds.SkyIslandDriftwood,
                BossRushItemIds.SkyIslandBrassScrap,
                BossRushItemIds.SkyIslandWindcrystalShard,
                BossRushItemIds.SkyIslandStardust,
                BossRushItemIds.SkyIslandQinglanWindcrystal,
                BossRushItemIds.SkyIslandWindLantern,
                BossRushItemIds.SkyIslandWindwardIncense,
                BossRushItemIds.SkyIslandQinglanCharm,
                // 内容批次四：云蚋的对策（云苔纱笠、风晶灭蚊灯、药烟蒲扇），同一套克隆兜底。
                BossRushItemIds.SkyIslandCloudmossVeil,
                BossRushItemIds.SkyIslandGnatZapper,
                BossRushItemIds.SkyIslandSmokeFan);

            // 失落的航向仪：Jeff 序章的交付物，同为零新增 bundle 的克隆兜底。
            // 漏登记时，玩家仓库里没交付的那一具重启后会退化成官方 FallbackItem，任务就交不掉了。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return SkyIslandNavInstrumentConfig.EnsureRuntimeFallbackRegistrationShell(); }
            }, BossRushItemIds.SkyIslandNavInstrument);

            // 潮闸镇物品（阀门手轮、潮汐怀表、防水袋、鸭蹼脚掌、闸伯的信）：同为零新增 bundle 的克隆兜底。
            // 玩家仓库与背包里存着这些 TypeID，漏登记会让重启后它们退化成官方 FallbackItem（AGENTS 契约第 6 节）。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return TidegateItems.EnsureRuntimeRegistration(typeId); }
            },
                BossRushItemIds.TidegateHandwheel,
                BossRushItemIds.TidegateTideWatch,
                BossRushItemIds.TidegateDryBag,
                BossRushItemIds.TidegateFlippers,
                BossRushItemIds.TidegateLetter,
                BossRushItemIds.TidegateSoundingLead);

            // 末班编组站物品（押运钢印、末班票根）：零新增 bundle 的克隆兜底，漏登记会让重启后它们退化成官方 FallbackItem。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return LastShiftItems.EnsureRuntimeRegistration(typeId); }
            },
                BossRushItemIds.LastShiftStamp,
                BossRushItemIds.LastShiftTicketStub);

            // 收集品 100 件与收藏册：零新增 bundle 的克隆兜底。玩家仓库、背包与收藏册存档里都有这些 TypeID，
            // 漏登记会让重启后它们退化成官方 FallbackItem（AGENTS 契约第 6 节）。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return CollectibleItems.EnsureRuntimeRegistration(typeId); }
            }, CollectibleTypeIds());
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return CollectionBookItem.EnsureRuntimeRegistration(); }
            }, BossRushItemIds.CollectionBook);

            // 回火液：克隆冷淬液 prefab，零新增 bundle；漏登记时仓库里的回火液重启后会退化成官方 FallbackItem。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return TemperingSolventConfig.EnsureRuntimeRegistration(); }
            }, BossRushItemIds.TemperingSolvent);

            // 星阙：纯代码光棍，没有任何 bundle，只走克隆兜底。
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = delegate(int typeId) { return AstralStaffWeaponConfig.EnsureRuntimeRegistration(); }
            }, BossRushItemIds.AstralStaff);

            Add(plans, SplitWeaponPlan("viperdagger_melee_model", "viperdagger_item",
                NewWeaponIds.ViperDaggerModelBaseName, true, EnsureNewWeaponPlaceholder), NewWeaponIds.ViperDaggerTypeId);
            Add(plans, SplitWeaponPlan("summonstaff_melee_model", "summonstaff_item",
                NewWeaponIds.SummonStaffModelBaseName, true, EnsureNewWeaponPlaceholder), NewWeaponIds.SummonStaffTypeId);
            Add(plans, SplitWeaponPlan("energyshield_totem_model", "energyshield_item",
                NewWeaponIds.EnergyShieldModelBaseName, false, EnsureNewWeaponPlaceholder), NewWeaponIds.EnergyShieldTypeId);
            Add(plans, SplitWeaponPlan("frostspear_melee_model", "frostspear_item",
                NewWeaponIds.FrostSpearModelBaseName, true, EnsureNewWeaponPlaceholder), NewWeaponIds.FrostSpearTypeId);
            Add(plans, new RegistrationPlan
            {
                ItemBundles = new string[] { "thunderring_item" },
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, NewWeaponIds.ThunderRingTypeId);

            Add(plans, new RegistrationPlan
            {
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.EmptyMagazineMine);
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.ReloadMetronome);

            Add(plans, new RegistrationPlan
            {
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.RollCallLedger);

            Add(plans, new RegistrationPlan
            {
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.PassTheBomb);
            Add(plans, new RegistrationPlan
            {
                EquipmentBundles = new string[] { "residual_sun_shield_model" },
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.ResidualSunShield);
            Add(plans, new RegistrationPlan
            {
                FallbackLoader = EnsureNewWeaponPlaceholder
            }, BossRushItemIds.SilenceBell);

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
            Add(plans, new RegistrationPlan
            {
                EquipmentBundles = new string[] { "skyisland_boss_gear" },
                FallbackLoader = SkyIslandBossGearConfig.EnsureRegistered
            }, SkyIslandBossRules.AllGearTypeIds);

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

        private static RegistrationPlan SplitWeaponPlan(string equipmentBundle, string itemBundle, string modelBaseName,
            bool isMeleeModel, Func<int, bool> fallback = null)
        {
            return new RegistrationPlan
            {
                EquipmentBundles = new string[] { equipmentBundle },
                ItemBundles = new string[] { itemBundle },
                SplitModelBaseName = modelBaseName,
                SplitModelIsMelee = isMeleeModel,
                FallbackLoader = fallback
            };
        }

        /// <summary>收集品 TypeID 连续占 CollectibleFirst..CollectibleLast（定义表按号排列）。</summary>
        private static int[] CollectibleTypeIds()
        {
            int[] ids = new int[BossRushItemIds.CollectibleLast - BossRushItemIds.CollectibleFirst + 1];
            for (int i = 0; i < ids.Length; i++) ids[i] = BossRushItemIds.CollectibleFirst + i;
            return ids;
        }

        private static void Add(Dictionary<int, RegistrationPlan> plans, RegistrationPlan plan, params int[] typeIds)
        {
            for (int i = 0; i < typeIds.Length; i++)
            {
                int typeId = typeIds[i];
                if (plans.ContainsKey(typeId))
                {
                    ModBehaviour.CriticalLog("dynamic-item-plan-duplicate-" + typeId,
                        "[BossRushDynamicItemRegistry] [ERROR] TypeID=" + typeId + " 重复登记注册计划，保留首次登记");
                    continue;
                }
                plans.Add(typeId, plan);
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

    /// <summary>包内物品配置完成和官方接收是两步。失败重试时复用已配置的 prefab。</summary>
    internal sealed class FactoryBundleRegistration
    {
        private readonly Dictionary<int, Item> configuredItems = new Dictionary<int, Item>();
        private readonly HashSet<int> publishedTypes = new HashSet<int>();

        internal bool Loading { get; set; }

        internal bool IsConfigured(Item prefab)
        {
            Item configured;
            return configuredItems.TryGetValue(prefab.TypeID, out configured) && configured == prefab;
        }

        internal bool IsPublished(Item prefab)
        {
            return IsConfigured(prefab) && publishedTypes.Contains(prefab.TypeID)
                && BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(prefab.TypeID);
        }

        internal bool TryPublish(Item prefab, string bundleName)
        {
            int typeId = prefab.TypeID;
            configuredItems[typeId] = prefab;
            publishedTypes.Remove(typeId);
            if (!BossRushDynamicItemRegistry.Publish(prefab)
                || BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId) != prefab)
            {
                ModBehaviour.CriticalLog("bundle-item-publish-" + typeId,
                    "[BossRushDynamicItemRegistry] 官方拒绝注册: bundle=" + bundleName + ", TypeID=" + typeId);
                return false;
            }
            publishedTypes.Add(typeId);
            return true;
        }

        internal bool AllPrefabsLive()
        {
            foreach (int typeId in configuredItems.Keys)
            {
                if (!publishedTypes.Contains(typeId) || !BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId))
                    return false;
            }
            return true;
        }
    }

    internal sealed partial class IntegrationRuntimeModule
    {
        internal static bool DynamicItemsInitialized { get; set; }
        internal static int BossRushTicketTypeId { get; set; } = -1;
        private bool bossRushTicketRegistering;

        internal bool EnsureItemContentConfiguratorsRegisteredForDynamicRegistry()
        {
            RegisterItemContentConfigurators();
            return true;
        }

        internal bool EnsureBossRushTicketItemRegisteredForDynamicRegistry()
        {
            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.BossRushTicket))
            {
                BossRushTicketTypeId = BossRushItemIds.BossRushTicket;
                return true;
            }
            if (bossRushTicketRegistering) return false;
            bossRushTicketRegistering = true;

            AssetBundle bundle = null;
            try
            {
                string modDir = ModBehaviour.GetModPath();
                string bundlePath = Path.Combine(modDir, "Assets", "bossrush_ticket");

                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] 未找到 bossrush_ticket AssetBundle: " + bundlePath);
                    return false;
                }

                bundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] AssetBundle.LoadFromFile 失败: " + bundlePath);
                    return false;
                }

                UnityEngine.Object[] assets = ResourceBundleLoader.LoadAllAssets<UnityEngine.Object>(bundle);
                if (assets == null || assets.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRushDynamicItemRegistry] bossrush_ticket AssetBundle 中未找到任何资源");
                    return false;
                }

                int itemCount = 0;
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

                    if (BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(itemPrefab.TypeID) != itemPrefab)
                    {
                        AddTagsToItem(itemPrefab, new string[] { "Key", "SpecialKey" });
                        if (!BossRushDynamicItemRegistry.Publish(itemPrefab)
                            || BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(itemPrefab.TypeID) != itemPrefab) continue;
                        itemCount++;
                    }

                    if (itemPrefab.TypeID == BossRushItemIds.BossRushTicket)
                    {
                        BossRushTicketTypeId = itemPrefab.TypeID;
                    }
                    else if (BossRushTicketTypeId <= 0 && itemPrefab.TypeID > 0)
                    {
                        BossRushTicketTypeId = itemPrefab.TypeID;
                    }
                }

                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] bossrush_ticket 按需注册完成：Item=" + itemCount + ", BossRushTicketTypeId=" + BossRushTicketTypeId);
                return BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(BossRushItemIds.BossRushTicket);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRushDynamicItemRegistry] bossrush_ticket 按需注册失败: " + e.Message);
                return false;
            }
            finally
            {
                AssetBundleUnloadHelper.TryUnload(bundle, "[BossRushDynamicItemRegistry]");
                bossRushTicketRegistering = false;
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
