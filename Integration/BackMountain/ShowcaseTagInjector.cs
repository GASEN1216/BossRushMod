// ============================================================================
// ShowcaseTagInjector.cs - 给 Mod 战利品补官方展示标签，让它们能摆进官方陈列柜
// ============================================================================
// 官方 Duckov.Buildings.Showcase 的槽位按物品 Tag 准入（Slot.requireTags / excludeTags）。
// 官方陈列柜「陈列柜」要 ShowCase 标签（Tag_ShowCase = 展示品 / Exhibits）；枪械展示架 / 假人
// 走枪械与装备槽位标签，Mod 武器 / 装备由 EquipmentFactory 克隆官方件本来就带。
// 这里只给筛中的 Mod 物品 **prefab** 加官方 Tag：实例从 prefab 克隆自动带上，读档重建的实例也一样。
//
// 枚举源是 BossRushDynamicItemRegistry（每件新内容都必须登记），所以将来新 Boss 装备 / 新武器
// 自动纳入，不用改这个文件。判据 ShouldTagForShowcase 在 ShowcaseDisplayJudges（纯函数）。
// 探针（F3 SHOWCASE_OFFICIAL_PROBE）打印各官方柜槽位的 requireTags；若实机发现槽位要别的标签，
// 只改 OfficialShowcaseTagNames 这一处。
// 取 prefab 只走 BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring：ItemAssetsCollection.GetPrefab
// 被 Harmony 补丁接管，会对每个 TypeID 触发同步按需注册（强制同步加载 bundle），2026-09-22 实机出过一次。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>Mod 战利品的官方展示标签注入。</summary>
    internal static class ShowcaseTagInjector
    {
        /// <summary>官方 Tag 资产名（大写 C）。探针发现槽位要更多标签时在这里追加。</summary>
        internal static readonly string[] OfficialShowcaseTagNames = { "ShowCase" };

        /// <summary>品质达标但明显不是战利品的功能道具（票据 / 图鉴 / 熔石 / 蛋 / 装置一类），按 TypeID 排除。</summary>
        private static readonly HashSet<int> DenyList = new HashSet<int>
        {
            BossRushItemIds.BossRushTicket, BossRushItemIds.RelicEgg, BossRushItemIds.PortableSafeZoneDevice,
            BossRushItemIds.ZombieTideBeacon, BossRushItemIds.ZombieTideInvitation,
        };

        private static readonly HashSet<int> _tagged = new HashSet<int>();
        private static HashSet<int> _published;

        /// <summary>注册表快照（新内容登记后由 EnsureTagged 刷新）。</summary>
        private static HashSet<int> Published
        {
            get
            {
                if (_published == null) _published = new HashSet<int>(BossRushDynamicItemRegistry.GetPublishedTypeIds());
                return _published;
            }
        }

        /// <summary>该 TypeID 是否算 Mod 战利品（陈列加成与标签注入共用同一判据）。</summary>
        internal static bool IsShowcaseTrophy(int typeId)
        {
            if (typeId <= 0) return false;
            try
            {
                if (!Published.Contains(typeId)) return false;
                Item prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                int quality = prefab != null ? prefab.Quality : 0;
                return ShowcaseDisplayJudges.ShouldTagForShowcase(typeId, quality,
                    BackMountainItems.GetDefinition(typeId) != null, DenyList.Contains(typeId), prefab != null);
            }
            catch (Exception) { return false; }
        }

        /// <summary>幂等：只给 prefab 已在位且筛中的物品加标签；lazy 注册的下次再补，不 force-load bundle。</summary>
        internal static void EnsureTagged()
        {
            int[] ids;
            try { ids = BossRushDynamicItemRegistry.GetPublishedTypeIds(); _published = new HashSet<int>(ids); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 读取物品注册表失败，展示标签注入跳过: " + e.Message);
                return;
            }
            int added = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                int typeId = ids[i];
                if (_tagged.Contains(typeId)) continue;
                if (!IsShowcaseTrophy(typeId)) continue;
                Item prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                if (prefab == null) continue;
                for (int t = 0; t < OfficialShowcaseTagNames.Length; t++) EquipmentHelper.AddTagToItem(prefab, OfficialShowcaseTagNames[t]);
                _tagged.Add(typeId);
                added++;
            }
            if (added > 0) ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "已给 " + added + " 件 Mod 战利品补官方展示标签");
        }

        internal static int TaggedCount { get { return _tagged.Count; } }

        internal static void ResetStaticCaches()
        {
            _tagged.Clear();
            _published = null;
        }
    }
}
