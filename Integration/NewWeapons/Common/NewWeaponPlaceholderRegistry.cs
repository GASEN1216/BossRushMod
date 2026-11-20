// ============================================================================
// NewWeaponPlaceholderRegistry.cs - P0新武器占位符注册
// ============================================================================
// 模块说明：
//   当 Assets/Equipment/ 下缺少对应 AssetBundle 时，
//   通过克隆已有物品模板创建占位符，确保五把新武器能注册到游戏物品系统。
//   生成占位符后立即调用对应 WeaponConfig.TryConfigure 写入 Stats / 标签 / Buff，
//   避免依赖 ItemFactory.loadedItems —— 该缓存只对真正从 Bundle 加载的物品生效。
//   这样：
//   - AssetBundle 不存在：占位符 + 配置全部齐全（功能可用，仅外观/模型缺失）
//   - AssetBundle 只提供模型：仍创建占位 Item，并绑定已加载模型
//   - AssetBundle 提供完整 Item Prefab：占位符跳过；ConfigureNewWeaponsAfterLoad 仍然处理
// 装配期在装备 bundle 加载后、ConfigureNewWeaponsAfterLoad 之前批量补齐占位件。
// 按需注册先走目标 TypeID 的注册计划，再按原顺序加载飞行图腾、逆鳞等克隆源。
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// P0 新武器占位符注册器
    /// </summary>
    public static class NewWeaponPlaceholderRegistry
    {
        /// <summary>
        /// 确保所有 P0 新武器已注册到物品系统（AssetBundle 缺失时使用占位符）
        /// </summary>
        public static void EnsureAllRegistered()
        {
            // 毒蛇匕首 - 近战武器
            EnsureRegistered(NewWeaponIds.ViperDaggerTypeId);

            // 召唤法杖 - 近战武器
            EnsureRegistered(NewWeaponIds.SummonStaffTypeId);

            // 能量盾 - 图腾槽位
            EnsureRegistered(NewWeaponIds.EnergyShieldTypeId);

            // 冰霜长矛 - 近战武器
            EnsureRegistered(NewWeaponIds.FrostSpearTypeId);

            // 雷电戒指 - 图腾槽位
            EnsureRegistered(NewWeaponIds.ThunderRingTypeId);

            EnsureRegistered(BossRushItemIds.EmptyMagazineMine);
            EnsureRegistered(BossRushItemIds.RollCallLedger);
            EnsureRegistered(BossRushItemIds.ReloadMetronome);
            EnsureRegistered(BossRushItemIds.PassTheBomb);
            EnsureRegistered(BossRushItemIds.ResidualSunShield);
        }

        // 保留原公开入口；批量注册随当前 prefab 状态运行，克隆由集成运行时清理。
        public static void ResetStaticCaches() { }

        /// <summary>
        /// 按 TypeID 确保单个 P0 新武器已注册，供 BossRushDynamicItemRegistry 按需调用。
        /// </summary>
        public static bool EnsureRegistered(int typeId)
        {
            if (typeId == NewWeaponIds.ViperDaggerTypeId)
            {
                return EnsureWeaponRegistered(
                    NewWeaponIds.ViperDaggerTypeId,
                    "ViperDagger_Placeholder",
                    "BossRush_ViperDagger",
                    NewWeaponIds.ViperDaggerBaseName,
                    isMelee: true);
            }

            if (typeId == NewWeaponIds.SummonStaffTypeId)
            {
                return EnsureWeaponRegistered(
                    NewWeaponIds.SummonStaffTypeId,
                    "SummonStaff_Placeholder",
                    "BossRush_SummonStaff",
                    NewWeaponIds.SummonStaffBaseName,
                    isMelee: true);
            }

            if (typeId == NewWeaponIds.EnergyShieldTypeId)
            {
                return EnsureWeaponRegistered(
                    NewWeaponIds.EnergyShieldTypeId,
                    "EnergyShield_Placeholder",
                    "BossRush_EnergyShield",
                    NewWeaponIds.EnergyShieldBaseName,
                    isMelee: false,
                    tag: "Totem");
            }

            if (typeId == NewWeaponIds.FrostSpearTypeId)
            {
                return EnsureWeaponRegistered(
                    NewWeaponIds.FrostSpearTypeId,
                    "FrostSpear_Placeholder",
                    "BossRush_FrostSpear",
                    NewWeaponIds.FrostSpearBaseName,
                    isMelee: true);
            }

            if (typeId == NewWeaponIds.ThunderRingTypeId)
            {
                return EnsureWeaponRegistered(
                    NewWeaponIds.ThunderRingTypeId,
                    "ThunderRing_Placeholder",
                    "BossRush_ThunderRing",
                    NewWeaponIds.ThunderRingBaseName,
                    isMelee: false,
                    tag: "Totem");
            }

            if (typeId == BossRushItemIds.ReloadMetronome)
            {
                return EnsureWeaponRegistered(
                    BossRushItemIds.ReloadMetronome, "ReloadMetronome_Placeholder",
                    "BossRush_ReloadMetronome", ReloadMetronomeConfig.BaseName,
                    isMelee: false, tag: "Totem");
            }

            if (typeId == BossRushItemIds.EmptyMagazineMine)
            {
                return EnsureWeaponRegistered(
                    BossRushItemIds.EmptyMagazineMine,
                    "EmptyMagazineMine_Placeholder",
                    "BossRush_EmptyMagazineMine",
                    EmptyMagazineMineConfig.BaseName,
                    isMelee: false,
                    tag: "Totem");
            }

            if (typeId == BossRushItemIds.RollCallLedger)
            {
                return EnsureWeaponRegistered(
                    BossRushItemIds.RollCallLedger,
                    "RollCallLedger_Placeholder",
                    "BossRush_RollCallLedger",
                    RollCallLedgerConfig.BaseName,
                    isMelee: false,
                    tag: "Totem");
            }

            if (typeId == BossRushItemIds.ResidualSunShield)
            {
                return EnsureWeaponRegistered(BossRushItemIds.ResidualSunShield, "ResidualSunShield_Placeholder",
                    "BossRush_ResidualSunShield", ResidualSunShieldConfig.BaseName, isMelee: false, tag: "Totem");
            }

            if (typeId == BossRushItemIds.PassTheBomb)
            {
                return EnsureWeaponRegistered(
                    BossRushItemIds.PassTheBomb,
                    "PassTheBomb_Placeholder",
                    "BossRush_PassTheBomb",
                    PassTheBombConfig.BaseName,
                    isMelee: false,
                    tag: "Totem");
            }

            return false;
        }

        /// <summary>
        /// 确保单个武器已注册，未注册时创建占位符并直接调用对应 WeaponConfig.TryConfigure
        /// </summary>
        private static bool EnsureWeaponRegistered(
            int typeId,
            string prefabName,
            string locKey,
            string baseName,
            bool isMelee,
            string tag = null)
        {
            Func<Item> findSource = !isMelee && tag == "Totem"
                ? (Func<Item>)FindTotemFallbackSource
                : FindFallbackSource;
            return RuntimeItemFallback.Ensure(typeId, prefabName, "[NewWeaponPlaceholder] ", findSource, clone =>
            {
                // 清空从克隆源继承下来的自定义变量（如飞行图腾的 Flight_MaxUpwardSpeed 等），
                // 避免在新武器物品详情里显示与本武器无关的属性。
                try { clone.Variables.Clear(); } catch  { /* best-effort fallback intentionally ignored */ }
                // 同样清掉继承的 Modifier（防止占位克隆源未来加入新 Modifier 时污染）
                try
                {
                    if (clone.Modifiers != null)
                    {
                        clone.Modifiers.Clear();
                    }
                }
                catch  { /* best-effort fallback intentionally ignored */ }

                // 基础配置。品质 / 耐久 / 售价 / 可维修标签统一由 NewWeaponItemAttributes 写入，
                // 与真 bundle 路径（XxxWeaponConfig.TryConfigure）共用同一张表，避免两条路径给出不同物品。
                clone.DisplayNameRaw = locKey;
                NewWeaponItemAttributes.Apply(clone, typeId);

                // 近战武器：清掉可能从源物品（如船票）继承的非武器组件
                if (isMelee)
                {
                    var gunSetting = clone.GetComponent<ItemSetting_Gun>();
                    if (gunSetting != null)
                    {
                        UnityEngine.Object.DestroyImmediate(gunSetting, true);
                    }
                    EquipmentFactory.RegisterMeleeWeapon(typeId);
                }

            }, afterPublish: clone =>
            {
                // TryConfigure 会按 TypeID 缓存模型绑定，发布失败时不建立这些缓存。
                // 直接走对应 WeaponConfig.TryConfigure 写入 Stats / 标签 / Buff，
                // 不再依赖 ConfigureNewWeaponsAfterLoad（它走 ItemFactory.loadedItems 缓存路径）
                bool configured = TryConfigureWeapon(clone, baseName, typeId);
                if (!configured)
                {
                    // 配置器未匹配时仍允许已有基础属性的占位件注册。
                    ModBehaviour.DevLog("[NewWeaponPlaceholder] [WARNING] " + prefabName + " 配置器未匹配 baseName=" + baseName);
                }

                // 兜底：图腾类型（无 WeaponConfig 中处理标签的近战路径）需要再确认 Totem 标签存在
                if (!isMelee && !string.IsNullOrEmpty(tag))
                {
                    EquipmentHelper.AddTagToItem(clone, tag);
                }
            });
        }

        /// <summary>
        /// 按 baseName 调度到对应的 WeaponConfig.TryConfigure
        /// </summary>
        private static bool TryConfigureWeapon(Item clone, string baseName, int typeId)
        {
            try
            {
                if (typeId == NewWeaponIds.ViperDaggerTypeId)
                    return ViperDaggerWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == NewWeaponIds.SummonStaffTypeId)
                    return SummonStaffWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == NewWeaponIds.EnergyShieldTypeId)
                    return EnergyShieldWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == NewWeaponIds.FrostSpearTypeId)
                    return FrostSpearWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == NewWeaponIds.ThunderRingTypeId)
                    return ThunderRingWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == BossRushItemIds.EmptyMagazineMine)
                    return EmptyMagazineMineWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == BossRushItemIds.RollCallLedger)
                    return RollCallLedgerWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == BossRushItemIds.ReloadMetronome)
                    return ReloadMetronomeWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == BossRushItemIds.ResidualSunShield)
                    return ResidualSunShieldWeaponConfig.TryConfigure(clone, baseName);
                if (typeId == BossRushItemIds.PassTheBomb)
                    return PassTheBombWeaponConfig.TryConfigure(clone, baseName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponPlaceholder] TryConfigureWeapon 异常 (TypeID=" + typeId + "): " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// 查找可用的克隆源物品
        /// </summary>
        private static Item FindFallbackSource()
        {
            return RuntimeItemFallback.FindSource(new[] { BossRushItemIds.BossRushTicket, BossRushItemIds.AdventureJournal, 500014 });
        }

        /// <summary>
        /// 查找图腾类装备克隆源
        /// 优先：飞行图腾 (500010) -> 逆鳞 (500013)
        /// 这些是真实图腾物品，自带正确的 Totem 槽位适配
        /// </summary>
        private static Item FindTotemFallbackSource()
        {
            return RuntimeItemFallback.FindSource(new[] { 500010, 500013 }) ?? FindFallbackSource();
        }

        /// <summary>
        /// 注入本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                // 文案一律取自各武器自己的 Config 常量，与 XxxWeaponConfig.InjectLocalization 同源。
                // 此前这里写的是另一套一句话简介，谁后跑谁覆盖——玩家看到的描述取决于调用顺序，
                // 而且新增的「来源」行只会出现在其中一套里。
                SummonStaffConfig summonStaffConfig = new SummonStaffConfig();

                InjectSingleLocalization(NewWeaponIds.ViperDaggerTypeId, "BossRush_ViperDagger",
                    ViperDaggerConfig.DisplayNameCN, ViperDaggerConfig.DisplayNameEN,
                    ViperDaggerConfig.DescriptionCN, ViperDaggerConfig.DescriptionEN,
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.SummonStaffTypeId, "BossRush_SummonStaff",
                    summonStaffConfig.DisplayNameCN, summonStaffConfig.DisplayNameEN,
                    summonStaffConfig.DescriptionCN, summonStaffConfig.DescriptionEN,
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.EnergyShieldTypeId, "BossRush_EnergyShield",
                    EnergyShieldConfig.DisplayNameCN, EnergyShieldConfig.DisplayNameEN,
                    EnergyShieldConfig.DescriptionCN, EnergyShieldConfig.DescriptionEN,
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.FrostSpearTypeId, "BossRush_FrostSpear",
                    FrostSpearConfig.DisplayNameCN, FrostSpearConfig.DisplayNameEN,
                    FrostSpearConfig.DescriptionCN, FrostSpearConfig.DescriptionEN,
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.ThunderRingTypeId, "BossRush_ThunderRing",
                    ThunderRingConfig.DisplayNameCN, ThunderRingConfig.DisplayNameEN,
                    ThunderRingConfig.DescriptionCN, ThunderRingConfig.DescriptionEN,
                    isChinese);

                ModBehaviour.DevLog("[NewWeaponPlaceholder] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponPlaceholder] 本地化注入失败: " + e.Message);
            }
        }

        private static void InjectSingleLocalization(int typeId, string locKey,
            string nameCn, string nameEn, string descCn, string descEn, bool isChinese)
        {
            string displayName = isChinese ? nameCn : nameEn;
            string description = isChinese ? descCn : descEn;

            LocalizationHelper.InjectLocalization(locKey, displayName);
            LocalizationHelper.InjectLocalization(locKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + typeId, displayName);
            LocalizationHelper.InjectLocalization("Item_" + typeId + "_Desc", description);
            LocalizationHelper.InjectLocalization(nameCn, displayName);
            LocalizationHelper.InjectLocalization(nameCn + "_Desc", description);
        }
    }
}
