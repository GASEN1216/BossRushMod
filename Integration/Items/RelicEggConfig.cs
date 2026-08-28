// ============================================================================
// RelicEggConfig.cs - 遗种蛋物品配置（实施计划 步骤 4）
// ============================================================================
// TypeID 500059（台账见 docs/Bossrush使用物品ID表.md）。
//
// 设计要点：
//   - **通用蛋 + KV 记血脉**：全谱系只占一个 TypeID，血脉写在物品自定义变量
//     `PetNest_Lineage` 上，随 ItemTreeData 持久化（Item.Variables 的 KV 会被
//     ItemTreeData.FromItem 逐条拷贝）。这是"全 Boss 皆可出崽"却不占号的关键。
//   - **MaxStack = 1**：堆叠会把两枚不同血脉的蛋合并成一枚，血脉信息随之丢失。
//   - **零新增 bundle**：没有专属模型，走 BossRushDynamicItemRegistry 的
//     FallbackLoader 从既有物品克隆（形态照 PortableSafeZoneDeviceConfig）。
//   - 血脉显示走 `Var_PetNest_Lineage` 本地化键（`Var_` 前缀是官方 KV 展示惯例，
//     先例见 Integration/Config/FlightTotemConfig.cs:143）。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>遗种蛋物品配置。全血脉共用一个 TypeID，血脉写在 KV 上。</summary>
    public static class RelicEggConfig
    {
        #region 常量

        public const int TYPE_ID = BossRushItemIds.RelicEgg;
        public const string PREFAB_NAME = "BossRush_RelicEgg";
        public const string LOC_KEY_DISPLAY = "BossRush_PetNest_RelicEgg";
        public const string DISPLAY_NAME_CN = "遗种蛋";
        public const string DISPLAY_NAME_EN = "Relic Egg";
        public const string DESCRIPTION_CN = "Boss 陨落时留下的血脉遗种。带回基地的遗种巢即可孵化，"
            + "孵出的幼体会认你作主人。每一枚蛋只记一条血脉，堆叠会让血脉丢失，因此它不可堆叠。";
        public const string DESCRIPTION_EN = "A bloodline relic left behind when a boss falls. "
            + "Hatch it at the PetNest in your base and the cub will take you as its master. "
            + "Each egg records exactly one bloodline, so it cannot stack.";

        /// <summary>血脉 KV 键（与 PetNestTuning.EggLineageVariableKey 同一个契约）。</summary>
        public const string VAR_LINEAGE = "PetNest_Lineage";

        public const int VALUE = 3200;
        public const int MAX_STACK = 1;
        public const int QUALITY = 5;

        #endregion

        private static bool runtimeFallbackRegistered;

        #region 显示文本

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        #endregion

        #region 注册

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[RelicEggConfig] Registered item configurator");
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                // 不可堆叠：堆叠合并会让两枚不同血脉的蛋变成一枚，血脉信息丢失
                item.MaxStackCount = MAX_STACK;
                item.StackCount = 1;
                item.Value = VALUE;
                item.Quality = QUALITY;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RelicEggConfig] ConfigureItem failed: " + e.Message);
            }
        }

        #endregion

        #region 血脉 KV

        /// <summary>
        /// 把血脉写进蛋的自定义变量并开启展示。血脉为空时不写（调用方应 fail-closed）。
        /// </summary>
        public static bool TryStampLineage(Item egg, string lineageKey)
        {
            if (egg == null || string.IsNullOrEmpty(lineageKey))
            {
                return false;
            }
            try
            {
                egg.Variables.Set(VAR_LINEAGE, lineageKey, true);
                egg.Variables.SetDisplay(VAR_LINEAGE, true);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RelicEggConfig] 写入蛋血脉失败: " + e.Message);
                return false;
            }
        }

        /// <summary>读取蛋上的血脉。缺失返回 null（孵化侧据此 fail-closed）。</summary>
        public static string ReadLineage(Item egg)
        {
            if (egg == null) return null;
            try
            {
                string value = egg.Variables.GetString(VAR_LINEAGE);
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        /// <summary>
        /// 没有专属 bundle，因此从既有物品克隆一个 prefab 顶上。
        /// 形态照 PortableSafeZoneDeviceConfig.EnsureRuntimeRegistration。
        /// </summary>
        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); }
                catch (Exception)
                {
                    // prefab 查询失败按"尚未注册"处理，继续走克隆路径
                }
                if (existing != null)
                {
                    ConfigureItem(existing);
                    return true;
                }

                existing = ItemFactory.GetLoadedItem(TYPE_ID);
                if (existing != null)
                {
                    ConfigureItem(existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }

                if (runtimeFallbackRegistered)
                {
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; }
                    catch (Exception) { return false; }
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog("[RelicEggConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
                    return false;
                }

                clone.gameObject.name = PREFAB_NAME;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(TYPE_ID);
                ConfigureItem(clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                runtimeFallbackRegistered = true;
                ModBehaviour.DevLog("[RelicEggConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RelicEggConfig] EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
        }

        /// <summary>供 BossRushDynamicItemRegistry 的 FallbackLoader 调用。</summary>
        public static bool EnsureRuntimeFallbackRegistrationShell()
        {
            return EnsureRuntimeRegistration();
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                BossRushItemIds.PortableSafeZoneDevice,
                BossRushItemIds.ZombieTideBeacon,
                BossRushItemIds.ZombieTideInvitation,
                AwenLootSweepTokenConfig.TYPE_ID,
            };

            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch (Exception)
                {
                    // 该候选不可用，继续下一个
                }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(fallbackIds[i]);
                    if (loaded != null) return loaded;
                }
                catch (Exception)
                {
                    // 同上
                }
            }

            return null;
        }

        #endregion

        #region 本地化

        public static void InjectLocalization()
        {
            try
            {
                string displayName = GetDisplayName();
                string description = GetDescription();
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, displayName);
                // KV 展示名：官方按 "Var_" + 键 查本地化
                LocalizationHelper.InjectLocalization("Var_" + VAR_LINEAGE, L10n.T("血脉", "Bloodline"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RelicEggConfig] InjectLocalization failed: " + e.Message);
            }
        }

        #endregion
    }
}
