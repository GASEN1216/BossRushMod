// ============================================================================
// GardenConstructionSite.cs - 替官方打开基地里被藏起来的菜地工地（取数层）
// ============================================================================
// 判据在 GardenSiteJudges（纯函数）；本文件只负责找工地、读造价、激活父物体、排队飘字、
// 以及 garden_built 的取数。只在基地场景、只在第一章交付后、只在作物注入之后做，
// 每场景至多一次（找不到工地就本场景放弃，不轮询）。开门是单向的：关总开关不回滚（与棘轮同理，
// 玩家站在工地前眼前东西消失、官方可能已写 wasBuilt）。不写任何官方存档键。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Crops;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方菜地工地的开门与建成取数。</summary>
    internal static class GardenConstructionSite
    {
        private static readonly FieldInfo KeyField = typeof(ConstructionSite).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);

        private static int _generation = -1;
        private static bool _settledThisScene;
        private static string _lastFailure;

        /// <summary>开门成功后排队的一条飘字；由运行时模块在对话结束后取走。</summary>
        internal static string PendingNotice;

        internal static void NotifySceneChanged(int generation)
        {
            if (generation == _generation) return;
            _generation = generation;
            _settledThisScene = false;
        }

        /// <summary>
        /// 事件驱动（场景就绪 / 关卡就绪 / 实时解锁 / 语言切换），不在 OnUpdate 轮询。
        /// 本场景已开过门或已放弃后 O(1) 早返。
        /// </summary>
        internal static void EnsureSiteOpen(int generation, bool moduleEnabled, bool gardenUnlocked, bool inBaseScene, bool cropsInjected)
        {
            NotifySceneChanged(generation);
            if (_settledThisScene) return;
            if (!moduleEnabled || !gardenUnlocked || !inBaseScene || !cropsInjected) return;
            try
            {
                ConstructionSite site;
                GameObject interactParent = null;
                bool siteFound = TryFindSite(out site);
                bool parentFound = siteFound && TryFindInteractParent(site, out interactParent);
                bool parentActive = parentFound && interactParent.activeSelf;
                bool built = IsGardenBuilt();
                string reason;
                if (!GardenSiteJudges.ShouldOpenSite(moduleEnabled, gardenUnlocked, inBaseScene, cropsInjected,
                        siteFound, parentFound, parentActive, built, out reason))
                {
                    // 已开 / 已建成：本场景无事可做。找不到工地或父物体：结构问题，本场景放弃并只记一次日志。
                    _settledThisScene = true;
                    if (!siteFound || !parentFound) LogOnce("[WARNING] 未找到菜地工地或付费交互父物体（" + reason + "），Mod 不开门，其它设施不受影响");
                    return;
                }
                interactParent.SetActive(true);
                _settledThisScene = true;
                PendingNotice = BuildOpenedNotice(site);
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "菜地工地已开放（付费交互父物体已激活）");
            }
            catch (Exception e)
            {
                _settledThisScene = true;
                LogOnce("[WARNING] 开放菜地工地异常: " + e.Message);
            }
        }

        /// <summary>garden_built 的取数：场上有活的官方 Garden（OnDestroy 不清字典，必须做 Unity null 检查），或官方键已写真。</summary>
        internal static bool IsGardenBuilt()
        {
            bool live = false;
            try
            {
                Dictionary<string, Garden> gardens = Garden.gardens;
                if (gardens != null)
                {
                    foreach (KeyValuePair<string, Garden> pair in gardens)
                    {
                        if (pair.Value != null) { live = true; break; }
                    }
                }
            }
            catch (Exception) { live = false; }
            bool exists = false, value = false;
            try
            {
                exists = SavesSystem.KeyExisits(GardenSiteJudges.ConstructionSaveKey);
                if (exists) value = SavesSystem.Load<bool>(GardenSiteJudges.ConstructionSaveKey);
            }
            catch (Exception) { exists = false; }
            return GardenSiteJudges.IsGardenBuilt(live, exists, value);
        }

        /// <summary>F3 只读探针：不激活、不付款、不写任何键。</summary>
        internal static bool ProbeSiteGate(bool gardenUnlocked, bool cropsInjected, out string metrics, out string reason)
        {
            ConstructionSite site;
            GameObject interactParent = null;
            bool siteFound = TryFindSite(out site);
            bool parentFound = siteFound && TryFindInteractParent(site, out interactParent);
            bool parentActive = parentFound && interactParent.activeSelf;
            bool costTakerFound = siteFound && site.GetComponentInChildren<CostTaker>(true) != null;
            bool built = IsGardenBuilt();
            return GardenSiteJudges.EvaluateSiteGate(siteFound, parentFound, parentActive, costTakerFound, built,
                gardenUnlocked, cropsInjected, out metrics, out reason);
        }

        internal static void ResetStaticCaches()
        {
            _generation = -1;
            _settledThisScene = false;
            _lastFailure = null;
            PendingNotice = null;
        }

        #region 查找

        /// <summary>主路按官方序列化 _key 匹配（存档键契约，比 GameObject 名稳）；兜底按名字。</summary>
        private static bool TryFindSite(out ConstructionSite site)
        {
            site = null;
            try
            {
                ConstructionSite[] all = UnityEngine.Object.FindObjectsOfType<ConstructionSite>(true);
                if (KeyField != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        string key = KeyField.GetValue(all[i]) as string;
                        if (string.Equals(key, GardenSiteJudges.ConstructionKey, StringComparison.Ordinal)) { site = all[i]; return true; }
                    }
                }
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.name == GardenSiteJudges.SiteObjectName) { site = all[i]; return true; }
                }
            }
            catch (Exception e)
            {
                LogOnce("[WARNING] 查找菜地工地异常: " + e.Message);
            }
            return false;
        }

        /// <summary>主路按名字；兜底找 CostTaker 向上一层（对改名免疫）。</summary>
        private static bool TryFindInteractParent(ConstructionSite site, out GameObject parent)
        {
            parent = null;
            if (site == null) return false;
            Transform named = site.transform.Find(GardenSiteJudges.InteractParentName);
            if (named != null) { parent = named.gameObject; return true; }
            CostTaker[] takers = site.GetComponentsInChildren<CostTaker>(true);
            for (int i = 0; i < takers.Length; i++)
            {
                if (takers[i] == null) continue;
                Transform cursor = takers[i].transform.parent;
                while (cursor != null && cursor.parent != null && cursor.parent != site.transform) cursor = cursor.parent;
                if (cursor != null && cursor != site.transform) { parent = cursor.gameObject; return true; }
            }
            return false;
        }

        #endregion

        #region 文案

        private static string BuildOpenedNotice(ConstructionSite site)
        {
            string costText = DescribeCost(site);
            return L10n.T("基地的菜地工地已开放（造价：" + costText + "）。走过去交互即可动工。",
                "The garden site at base is now open (cost: " + costText + "). Walk up and interact to start construction.");
        }

        /// <summary>造价从官方 CostTaker.Cost 运行时读（ConstructionSite.Awake 已 SetCost，读到的是工地真正的价钱）。</summary>
        private static string DescribeCost(ConstructionSite site)
        {
            long money = 0;
            string lines = null;
            try
            {
                CostTaker taker = site != null ? site.GetComponentInChildren<CostTaker>(true) : null;
                if (taker != null)
                {
                    Duckov.Economy.Cost cost = taker.Cost;
                    money = cost.money;
                    if (cost.items != null)
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < cost.items.Length; i++)
                        {
                            Duckov.Economy.Cost.ItemEntry entry = cost.items[i];
                            if (entry.amount <= 0) continue;
                            parts.Add(ItemName(entry.id) + " x" + entry.amount);
                        }
                        if (parts.Count > 0) lines = string.Join(L10n.T("、", ", "), parts.ToArray());
                    }
                }
            }
            catch (Exception) { }
            return GardenSiteJudges.BuildCostText(money, lines, L10n.IsChinese);
        }

        private static string ItemName(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null && !string.IsNullOrEmpty(prefab.DisplayName)) return prefab.DisplayName;
            }
            catch (Exception) { }
            return "#" + typeId;
        }

        private static void LogOnce(string message)
        {
            if (string.Equals(_lastFailure, message, StringComparison.Ordinal)) return;
            _lastFailure = message;
            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + message);
        }

        #endregion
    }
}
