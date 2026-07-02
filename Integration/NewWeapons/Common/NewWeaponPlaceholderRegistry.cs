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
// 调用时机要求：
//   必须在 EquipmentFactory.LoadAllEquipment() 之后（让 500010 飞行图腾 / 500013 逆鳞 /
//   500003 龙裔头盔等克隆源已注册到 ItemAssetsCollection），
//   并在 ConfigureNewWeaponsAfterLoad 之前。
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
        private static bool initialized = false;

        /// <summary>
        /// 确保所有 P0 新武器已注册到物品系统（AssetBundle 缺失时使用占位符）
        /// </summary>
        public static void EnsureAllRegistered()
        {
            if (initialized) return;

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

            initialized = true;
        }

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
            try
            {
                // 检查是否已通过 AssetBundle 加载
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); } catch  { /* best-effort fallback intentionally ignored */ }
                if (existing != null) return true;

                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    try { ItemAssetsCollection.AddDynamicEntry(existing); } catch  { /* best-effort fallback intentionally ignored */ }
                    return true;
                }

                // 查找克隆源：图腾用真图腾物品（500010 飞行图腾 / 500013 逆鳞）作源，
                // 这样占位符天然带 Totem 槽位匹配；近战类则继续用通用源
                Item source = !isMelee && tag == "Totem"
                    ? FindTotemFallbackSource()
                    : FindFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog("[NewWeaponPlaceholder] 无法找到克隆源物品，跳过: " + prefabName);
                    return false;
                }

                // 克隆并配置
                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null)
                {
                    ModBehaviour.DevLog("[NewWeaponPlaceholder] 克隆失败: " + prefabName);
                    return false;
                }

                clone.gameObject.name = prefabName;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);

                // 设置 TypeID
                clone.SetTypeID(typeId);

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

                // 基础配置
                clone.DisplayNameRaw = locKey;
                clone.Quality = 5;
                clone.MaxDurability = 999f;
                clone.Durability = 999f;
                clone.MaxStackCount = 1;
                if (clone.StackCount <= 0) clone.StackCount = 1;

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

                // 注册到物品系统（在 TryConfigure 之前完成，TryConfigure 内可能依赖 AddDynamicEntry）
                ItemAssetsCollection.AddDynamicEntry(clone);

                // 直接走对应 WeaponConfig.TryConfigure 写入 Stats / 标签 / Buff，
                // 不再依赖 ConfigureNewWeaponsAfterLoad（它走 ItemFactory.loadedItems 缓存路径）
                bool configured = TryConfigureWeapon(clone, baseName, typeId);
                if (!configured)
                {
                    // TryConfigure 内部已打日志；占位符仍然进了 ItemAssetsCollection，但功能不全
                    ModBehaviour.DevLog("[NewWeaponPlaceholder] [WARNING] " + prefabName + " 配置器未匹配 baseName=" + baseName);
                }

                // 兜底：图腾类型（无 WeaponConfig 中处理标签的近战路径）需要再确认 Totem 标签存在
                if (!isMelee && !string.IsNullOrEmpty(tag))
                {
                    EquipmentHelper.AddTagToItem(clone, tag);
                }

                // 注入用户提供的图标 PNG（用户只供贴图/模型/音效，物品 prefab 由占位创建）。
                // 失败时保留克隆源的图标，不影响功能。
                TryInjectWeaponIcon(clone, typeId);

                ModBehaviour.DevLog("[NewWeaponPlaceholder] 占位符已注册并配置完成: " + prefabName + " (TypeID=" + typeId + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponPlaceholder] 注册失败 " + prefabName + ": " + e.Message);
                return false;
            }
        }

        public static void ResetStaticCaches()
        {
            initialized = false;
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
            int[] fallbackIds = new int[]
            {
                BossRushItemIds.BossRushTicket,
                BossRushItemIds.AdventureJournal,
                500014 // 冷淬液
            };

            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch  { /* best-effort fallback intentionally ignored */ }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(fallbackIds[i]);
                    if (loaded != null) return loaded;
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }

            return null;
        }

        /// <summary>
        /// 查找图腾类装备克隆源
        /// 优先：飞行图腾 (500010) -> 逆鳞 (500013)
        /// 这些是真实图腾物品，自带正确的 Totem 槽位适配
        /// </summary>
        private static Item FindTotemFallbackSource()
        {
            int[] totemIds = new int[]
            {
                500010, // 腾云驾雾 I（飞行图腾）
                500013  // 逆鳞
            };

            for (int i = 0; i < totemIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(totemIds[i]);
                    if (prefab != null) return prefab;
                }
                catch  { /* best-effort fallback intentionally ignored */ }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(totemIds[i]);
                    if (loaded != null) return loaded;
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }

            // 实在找不到再退回到通用源（玩家会得到一个能装备但属性接近船票的物品）
            return FindFallbackSource();
        }

        /// <summary>
        /// 按 TypeID 路由到对应 Bundle/Icon 名，并尝试从 Equipment/Items PNG 或 Equipment Bundle 读图标
        /// </summary>
        private static void TryInjectWeaponIcon(Item clone, int typeId)
        {
            string bundle = null;
            string icon = null;

            if (typeId == NewWeaponIds.ViperDaggerTypeId)
            {
                bundle = NewWeaponIds.ViperDaggerBundleName;
                icon = NewWeaponIds.ViperDaggerIconAssetName;
            }
            else if (typeId == NewWeaponIds.SummonStaffTypeId)
            {
                bundle = NewWeaponIds.SummonStaffBundleName;
                icon = NewWeaponIds.SummonStaffIconAssetName;
            }
            else if (typeId == NewWeaponIds.EnergyShieldTypeId)
            {
                bundle = NewWeaponIds.EnergyShieldBundleName;
                icon = NewWeaponIds.EnergyShieldIconAssetName;
            }
            else if (typeId == NewWeaponIds.FrostSpearTypeId)
            {
                bundle = NewWeaponIds.FrostSpearBundleName;
                icon = NewWeaponIds.FrostSpearIconAssetName;
            }
            else if (typeId == NewWeaponIds.ThunderRingTypeId)
            {
                bundle = NewWeaponIds.ThunderRingBundleName;
                icon = NewWeaponIds.ThunderRingIconAssetName;
            }

            if (!string.IsNullOrEmpty(icon))
            {
                EquipmentHelperIcon.TryInjectIcon(clone, bundle, icon);
            }
        }

        /// <summary>
        /// 注入本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                InjectSingleLocalization(NewWeaponIds.ViperDaggerTypeId, "BossRush_ViperDagger",
                    "毒蛇匕首", "Viper Dagger",
                    "近身叠毒，满5层爆发伤害", "Stacks poison on hit, explodes at 5 stacks",
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.SummonStaffTypeId, "BossRush_SummonStaff",
                    "召唤法杖", "Summoning Staff",
                    "右键召唤3只灵魂战士（15秒），自身伤害偏弱", "Right-click summons 3 soul warriors (15s), lower self damage",
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.EnergyShieldTypeId, "BossRush_EnergyShield",
                    "能量盾", "Energy Shield",
                    "图腾槽位，正面受击回补30%伤害为HP", "Totem slot, frontal hits restore 30% damage as HP",
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.FrostSpearTypeId, "BossRush_FrostSpear",
                    "冰霜长矛", "Frost Spear",
                    "中距离冰属性近战，100%冰冻减速", "Mid-range ice melee, 100% freeze slow",
                    isChinese);

                InjectSingleLocalization(NewWeaponIds.ThunderRingTypeId, "BossRush_ThunderRing",
                    "雷电戒指", "Thunder Ring",
                    "图腾槽位，受击蓄雷满层后攻击释放雷电伤害", "Totem slot, charges on hit, releases thunder damage when full",
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
