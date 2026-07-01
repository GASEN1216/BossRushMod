// ============================================================================
// SetBonusPlaceholderRegistry.cs - P1套装体系占位符注册
// ============================================================================
// 模块说明：
//   当 Assets/Equipment/ 下缺少霜冠、寒冰铠甲、雷神之角、雷霆战甲的
//   AssetBundle 时，通过克隆已有装备模板创建占位符，确保套装物品能注册到
//   游戏物品系统。SetBonusManager 的效果检测逻辑基于 TypeID 匹配，
//   只要物品注册成功即可生效。
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

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

        // Bundle / 图标资源名称（用户提供贴图时按这套命名，具体候选路径由 EquipmentHelperIcon 处理）
        private const string FROST_SET_BUNDLE = "frost_set";
        private const string THUNDER_SET_BUNDLE = "thunder_set";
        private const string FROST_HELMET_ICON = "frost_crown_icon";
        private const string FROST_ARMOR_ICON = "ice_armor_icon";
        private const string THUNDER_HELMET_ICON = "thunder_horn_icon";
        private const string THUNDER_ARMOR_ICON = "thunder_armor_icon";

        // 3D 模型基础名：AssetBundle 内 GameObject 名去掉 _Model 后缀
        private const string FROST_HELMET_MODEL = "FrostCrown_Helmet";
        private const string FROST_ARMOR_MODEL = "IceArmor_Armor";
        private const string THUNDER_HELMET_MODEL = "ThunderHorn_Helmet";
        private const string THUNDER_ARMOR_MODEL = "ThunderArmor_Armor";

        private static bool initialized = false;

        /// <summary>路由 TypeID 到 Bundle 名称（用户图标按此查找子目录）</summary>
        private static string GetBundleNameForSetItem(int typeId)
        {
            if (typeId == FROST_HELMET_ID || typeId == FROST_ARMOR_ID) return FROST_SET_BUNDLE;
            if (typeId == THUNDER_HELMET_ID || typeId == THUNDER_ARMOR_ID) return THUNDER_SET_BUNDLE;
            return null;
        }

        /// <summary>路由 TypeID 到图标资源名（PNG 文件名 / Bundle Sprite 名）</summary>
        private static string GetIconNameForSetItem(int typeId)
        {
            if (typeId == FROST_HELMET_ID) return FROST_HELMET_ICON;
            if (typeId == FROST_ARMOR_ID) return FROST_ARMOR_ICON;
            if (typeId == THUNDER_HELMET_ID) return THUNDER_HELMET_ICON;
            if (typeId == THUNDER_ARMOR_ID) return THUNDER_ARMOR_ICON;
            return null;
        }

        /// <summary>路由 TypeID 到 AssetBundle 内模型基础名</summary>
        private static string GetModelBaseNameForSetItem(int typeId)
        {
            if (typeId == FROST_HELMET_ID) return FROST_HELMET_MODEL;
            if (typeId == FROST_ARMOR_ID) return FROST_ARMOR_MODEL;
            if (typeId == THUNDER_HELMET_ID) return THUNDER_HELMET_MODEL;
            if (typeId == THUNDER_ARMOR_ID) return THUNDER_ARMOR_MODEL;
            return null;
        }

        /// <summary>
        /// 确保所有套装物品已注册（AssetBundle 缺失时使用占位符）
        /// </summary>
        public static void EnsureAllRegistered()
        {
            if (initialized) return;
            initialized = true;

            // 霜冠 - 头盔
            EnsureSetItemRegistered(
                FROST_HELMET_ID,
                "FrostCrown_Placeholder",
                "BossRush_FrostCrown",
                "Helmat",  // 游戏原版拼写
                6);

            // 寒冰铠甲 - 护甲
            EnsureSetItemRegistered(
                FROST_ARMOR_ID,
                "IceArmor_Placeholder",
                "BossRush_IceArmor",
                "Armor",
                6);

            // 雷神之角 - 头盔
            EnsureSetItemRegistered(
                THUNDER_HELMET_ID,
                "ThunderHorn_Placeholder",
                "BossRush_ThunderHorn",
                "Helmat",
                6);

            // 雷霆战甲 - 护甲
            EnsureSetItemRegistered(
                THUNDER_ARMOR_ID,
                "ThunderArmor_Placeholder",
                "BossRush_ThunderArmor",
                "Armor",
                6);
        }

        public static void RegisterConfigurators()
        {
            ItemFactory.RegisterConfigurator(FROST_HELMET_ID,
                item => ConfigureLoadedSetItem(item, "BossRush_FrostCrown", "Helmat", 6, FROST_HELMET_ID, "FrostCrown_Helmet_Item"));
            ItemFactory.RegisterConfigurator(FROST_ARMOR_ID,
                item => ConfigureLoadedSetItem(item, "BossRush_IceArmor", "Armor", 6, FROST_ARMOR_ID, "IceArmor_Armor_Item"));
            ItemFactory.RegisterConfigurator(THUNDER_HELMET_ID,
                item => ConfigureLoadedSetItem(item, "BossRush_ThunderHorn", "Helmat", 6, THUNDER_HELMET_ID, "ThunderHorn_Helmet_Item"));
            ItemFactory.RegisterConfigurator(THUNDER_ARMOR_ID,
                item => ConfigureLoadedSetItem(item, "BossRush_ThunderArmor", "Armor", 6, THUNDER_ARMOR_ID, "ThunderArmor_Armor_Item"));
        }

        /// <summary>
        /// 确保单个套装物品已注册
        /// </summary>
        private static void EnsureSetItemRegistered(int typeId, string prefabName, string locKey, string slotTag, int quality)
        {
            try
            {
                // 检查是否已通过 AssetBundle 加载
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); } catch  { /* best-effort fallback intentionally ignored */ }
                if (existing != null) return;

                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    try { ItemAssetsCollection.AddDynamicEntry(existing); } catch  { /* best-effort fallback intentionally ignored */ }
                    return;
                }

                // 查找同类型的克隆源（优先找已有的龙裔装备）
                Item source = FindEquipmentSource(slotTag);
                if (source == null)
                {
                    ModBehaviour.DevLog("[SetBonusPlaceholder] 无法找到 " + slotTag + " 类型克隆源，跳过: " + prefabName);
                    return;
                }

                // 克隆并配置
                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null)
                {
                    ModBehaviour.DevLog("[SetBonusPlaceholder] 克隆失败: " + prefabName);
                    return;
                }

                clone.gameObject.name = prefabName;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);

                // 设置 TypeID
                clone.SetTypeID(typeId);

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

                ApplySetItemBasics(clone, locKey, slotTag, quality);

                // 如果用户只提供模型 AssetBundle 而不提供 Item Prefab，把已扫描到的模型绑定到占位 Item。
                TryBindSetItemModel(clone, typeId, prefabName);

                // 注入用户提供的图标 PNG（用户只供贴图/模型/音效）
                EquipmentHelperIcon.TryInjectIcon(clone, GetBundleNameForSetItem(typeId), GetIconNameForSetItem(typeId));

                // 注册到物品系统
                ItemAssetsCollection.AddDynamicEntry(clone);

                ModBehaviour.DevLog("[SetBonusPlaceholder] 占位符已注册: " + prefabName + " (TypeID=" + typeId + ")");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusPlaceholder] 注册失败 " + prefabName + ": " + e.Message);
            }
        }

        private static void ConfigureLoadedSetItem(
            Item item,
            string locKey,
            string slotTag,
            int quality,
            int typeId,
            string prefabName)
        {
            ApplySetItemBasics(item, locKey, slotTag, quality);
            TryBindSetItemModel(item, typeId, prefabName);
            EquipmentHelperIcon.TryInjectIcon(item, GetBundleNameForSetItem(typeId), GetIconNameForSetItem(typeId));
        }

        private static void ApplySetItemBasics(Item item, string locKey, string slotTag, int quality)
        {
            if (item == null)
            {
                return;
            }

            item.DisplayNameRaw = locKey;
            item.Quality = quality;
            item.MaxDurability = 999f;
            item.Durability = 999f;
            item.MaxStackCount = 1;
            if (item.StackCount <= 0) item.StackCount = 1;

            EquipmentHelper.AddTagToItem(item, slotTag);

            try
            {
                if (slotTag == "Helmat")
                {
                    EnsureBaseArmorModifier(item, "HeadArmor", 5f);
                }
                else if (slotTag == "Armor")
                {
                    EnsureBaseArmorModifier(item, "BodyArmor", 5f);
                }
            }
            catch (Exception modEx)
            {
                ModBehaviour.DevLog("[SetBonusPlaceholder] [WARNING] 基础护甲 modifier 失败: " + modEx.Message);
            }
        }

        private static void EnsureBaseArmorModifier(Item item, string key, float value)
        {
            if (item == null)
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

        private static void TryBindSetItemModel(Item clone, int typeId, string prefabName)
        {
            string modelBaseName = GetModelBaseNameForSetItem(typeId);
            if (string.IsNullOrEmpty(modelBaseName))
            {
                return;
            }

            try
            {
                if (EquipmentFactory.TryBindLoadedEquipmentModel(clone, modelBaseName))
                {
                    ModBehaviour.DevLog("[SetBonusPlaceholder] 已绑定用户模型: " + prefabName + " -> " + modelBaseName);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusPlaceholder] [WARNING] 绑定模型失败 " + prefabName + ": " + e.Message);
            }
        }

        /// <summary>
        /// 查找同类型装备的克隆源（优先龙裔套装，其次任何同类型装备）
        /// </summary>
        private static Item FindEquipmentSource(string slotTag)
        {
            // 优先尝试龙裔套装（已有 AssetBundle）
            int[] dragonIds;
            if (slotTag == "Helmat")
            {
                dragonIds = new int[] { 500003, 500011 }; // 赤龙首、龙王之冕
            }
            else
            {
                dragonIds = new int[] { 500004, 500012 }; // 焰鳞甲、龙王鳞铠
            }

            for (int i = 0; i < dragonIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(dragonIds[i]);
                    if (prefab != null) return prefab;
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }

            // 兜底：任何已注册的物品
            int[] genericIds = new int[] { BossRushItemIds.BossRushTicket, 500014 };
            for (int i = 0; i < genericIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(genericIds[i]);
                    if (prefab != null) return prefab;
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }

            return null;
        }

        /// <summary>
        /// 注入本地化文本（已有 EquipmentLocalization 处理，此处作兜底）
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                InjectSingleLocalization(FROST_HELMET_ID, "BossRush_FrostCrown",
                    "霜冠", "Frost Crown",
                    "冰霜套头盔。2件套效果：受击时有概率冻结周围敌人",
                    "Frost set helmet. 2-piece bonus: chance to freeze nearby enemies when hit",
                    isChinese);

                InjectSingleLocalization(FROST_ARMOR_ID, "BossRush_IceArmor",
                    "寒冰铠甲", "Ice Armor",
                    "冰霜套护甲。2件套效果：受击时有概率冻结周围敌人",
                    "Frost set armor. 2-piece bonus: chance to freeze nearby enemies when hit",
                    isChinese);

                InjectSingleLocalization(THUNDER_HELMET_ID, "BossRush_ThunderHorn",
                    "雷神之角", "Thunder Horn",
                    "雷霆套头盔。2件套效果：受击时触发电击AOE",
                    "Thunder set helmet. 2-piece bonus: triggers thunder AOE when hit",
                    isChinese);

                InjectSingleLocalization(THUNDER_ARMOR_ID, "BossRush_ThunderArmor",
                    "雷霆战甲", "Thunder Armor",
                    "雷霆套护甲。2件套效果：受击时触发电击AOE",
                    "Thunder set armor. 2-piece bonus: triggers thunder AOE when hit",
                    isChinese);

                ModBehaviour.DevLog("[SetBonusPlaceholder] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusPlaceholder] 本地化注入失败: " + e.Message);
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
