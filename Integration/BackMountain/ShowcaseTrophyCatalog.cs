// ============================================================================
// ShowcaseTrophyCatalog.cs - 哪些 Mod 物品算「战利品」：摆上官方展示建筑才计陈列加成
// ============================================================================
// 官方展示建筑：枪械展示架（槽位要 Gun）、假人（槽位按枪 / 近战 / 头盔 / 护甲 / 面罩 / 耳机）、
// 基地皮肤柜（壁纸 / 摆件），以及「陈列柜」Showcase01（12 个展示位全要 ShowCase 标签，摆物品自己的
// ItemGraphic，没有模型就空着）。陈列柜能建，但前置是已建比特币矿机 + 完成任务 940「玩具收藏家-2」，
// 官方建造菜单对前置未满足的建筑直接隐藏，所以 2026-09-22 实机看不到；当时「废弃建筑」的结论有误，
// 2026-11-19 按字节读 BuildingDataCollection 更正。ShowcaseDisplayScanner 采集场上全部官方 Showcase，
// 本身带 ShowCase 的 Mod 物品（如 5 档以上收集品）摆进陈列柜同样计入。
// Mod 武器与装备由 EquipmentFactory 克隆官方件，自带枪架 / 假人的槽位标签（F3 探针实测 Mod 枪甲已能上架），
// 名录不给战利品补 ShowCase（owner 2026-11-19 拍板维持）；这里只维护「算不算战利品」的判定（纯判据
// ShowcaseDisplayJudges.ShouldCountAsTrophy），供 ShowcaseDisplayScanner 采集官方展示建筑里的
// 槽位内容时筛选。枚举源是 BossRushDynamicItemRegistry，新 Boss 装备 / 新武器自动纳入。
// 取 prefab 只走 BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring：ItemAssetsCollection.GetPrefab
// 被 Harmony 补丁接管，会对每个 TypeID 触发同步按需注册（强制同步加载 bundle），2026-09-22 实机出过一次。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>Mod 战利品名录：陈列加成只认这里判为战利品的 TypeID。</summary>
    internal static class ShowcaseTrophyCatalog
    {
        /// <summary>品质达标但明显不是战利品的功能道具（票据 / 图鉴 / 熔石 / 蛋 / 装置一类），按 TypeID 排除。</summary>
        private static readonly HashSet<int> DenyList = new HashSet<int>
        {
            BossRushItemIds.BossRushTicket, BossRushItemIds.RelicEgg, BossRushItemIds.PortableSafeZoneDevice,
            BossRushItemIds.ZombieTideBeacon, BossRushItemIds.ZombieTideInvitation,
        };

        private static readonly HashSet<int> _trophies = new HashSet<int>();
        private static HashSet<int> _published;

        /// <summary>注册表快照（新内容登记后由 Refresh 刷新）。</summary>
        private static HashSet<int> Published
        {
            get
            {
                if (_published == null) _published = new HashSet<int>(BossRushDynamicItemRegistry.GetPublishedTypeIds());
                return _published;
            }
        }

        /// <summary>该 TypeID 是否算 Mod 战利品（陈列加成与探针共用同一判据）。</summary>
        internal static bool IsShowcaseTrophy(int typeId)
        {
            if (typeId <= 0) return false;
            try
            {
                if (!Published.Contains(typeId)) return false;
                Item prefab = BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId);
                int quality = prefab != null ? prefab.Quality : 0;
                return ShowcaseDisplayJudges.ShouldCountAsTrophy(typeId, quality,
                    BackMountainItems.GetDefinition(typeId) != null, DenyList.Contains(typeId), prefab != null);
            }
            catch (Exception) { return false; }
        }

        /// <summary>幂等：刷新注册表快照并把 prefab 已在位且判为战利品的 TypeID 记入名录；lazy 注册的下次再补，不 force-load bundle。</summary>
        internal static void Refresh()
        {
            int[] ids;
            try { ids = BossRushDynamicItemRegistry.GetPublishedTypeIds(); _published = new HashSet<int>(ids); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 读取物品注册表失败，战利品名录刷新跳过: " + e.Message);
                return;
            }
            int added = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                int typeId = ids[i];
                if (_trophies.Contains(typeId)) continue;
                if (!IsShowcaseTrophy(typeId)) continue;
                _trophies.Add(typeId);
                added++;
            }
            if (added > 0) ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "战利品名录新增 " + added + " 件（共 " + _trophies.Count + " 件）");
        }

        /// <summary>已判为战利品且 prefab 在位的 TypeID 数（探针 metrics 用）。</summary>
        internal static int TrophyCount { get { return _trophies.Count; } }

        internal static void ResetStaticCaches()
        {
            _trophies.Clear();
            _published = null;
        }
    }
}
