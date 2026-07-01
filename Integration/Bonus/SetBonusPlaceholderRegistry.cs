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

        private static bool initialized = false;

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
                "FrostCrown_Placeholder");

            // 寒冰铠甲 - 护甲
            EnsureSetItemRegistered(
                FROST_ARMOR_ID,
                "IceArmor_Placeholder");

            // 雷神之角 - 头盔
            EnsureSetItemRegistered(
                THUNDER_HELMET_ID,
                "ThunderHorn_Placeholder");

            // 雷霆战甲 - 护甲
            EnsureSetItemRegistered(
                THUNDER_ARMOR_ID,
                "ThunderArmor_Placeholder");
        }

        /// <summary>
        /// 确保单个套装物品已注册
        /// </summary>
        private static void EnsureSetItemRegistered(int typeId, string prefabName)
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); } catch  { /* best-effort fallback intentionally ignored */ }
                if (existing != null)
                {
                    FrostThunderSetConfig.TryConfigureByTypeId(existing);
                    return;
                }

                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    FrostThunderSetConfig.TryConfigureByTypeId(existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); } catch  { /* best-effort fallback intentionally ignored */ }
                    return;
                }

                // 查找同类型的克隆源（优先找已有的龙裔装备）
                string sourceSlotTag = GetFallbackSourceSlotTag(typeId);
                Item source = FindEquipmentSource(sourceSlotTag);
                if (source == null)
                {
                    ModBehaviour.DevLog("[SetBonusPlaceholder] 无法找到 " + sourceSlotTag + " 类型克隆源，跳过: " + prefabName);
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

                FrostThunderSetConfig.TryConfigureByTypeId(clone);

                // 注册到物品系统
                ItemAssetsCollection.AddDynamicEntry(clone);

                ModBehaviour.DevLog("[SetBonusPlaceholder] 占位符已注册: " + prefabName + " (TypeID=" + typeId + ")");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SetBonusPlaceholder] 注册失败 " + prefabName + ": " + e.Message);
            }
        }

        private static string GetFallbackSourceSlotTag(int typeId)
        {
            if (typeId == FROST_HELMET_ID || typeId == THUNDER_HELMET_ID)
            {
                return "Helmat";
            }

            return "Armor";
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
    }
}
