// ============================================================================
// GardenSiteJudges.cs - 官方菜地工地「开门」与「建成」的纯判据
// ============================================================================
// 官方基地里的菜地是一个 ConstructionSite（GameObject `GardenConstruct`，_key = "GardenConstruction"）：
// 付费交互 CostTaker 挂在被关着的父物体 `Interactparent` 下面，官方代码从不引用这个父物体，
// 所以玩家没有任何途径把菜地建起来（2026-09-22 UnityPy 读 level5 核实）。Mod 只做一件事：
// 第一章交付后把 `Interactparent` 打开；付款、wasBuilt、存档、亮出 Garden 全走官方代码，
// Mod 不写官方存档键 ConstructionSite_GardenConstruction（只读）。
//
// 本文件零 Unity 引用：判据只吃 bool / long / int，tests/fixtures/BackMountainLifecycle 直接链接穷举。
// 前向兼容：官方将来默认把父物体打开 → interactParentActive 为真 → no-op；官方直接把菜地做成已建成 →
// alreadyBuilt 为真 → no-op 且 IsGardenBuilt 照样为真；改 GameObject 名 → 取数层两级查找免疫；
// 改 _key → 找不到工地 → fail-closed，只丢「Mod 开门」，作物注入 / 出击餐 / 其它设施不受影响。
// ============================================================================

namespace BossRush
{
    /// <summary>菜地工地的纯判据（无 Unity 依赖）。</summary>
    internal static class GardenSiteJudges
    {
        #region 冻结常量（官方序列化值，只读）

        /// <summary>官方 ConstructionSite 的私有序列化 _key。</summary>
        internal const string ConstructionKey = "GardenConstruction";

        /// <summary>官方按 "ConstructionSite_" + _key 写的存档键。Mod 只读，全仓库禁止写。</summary>
        internal const string ConstructionSaveKey = "ConstructionSite_GardenConstruction";

        /// <summary>兜底查找用的 GameObject 名。</summary>
        internal const string SiteObjectName = "GardenConstruct";

        /// <summary>付费交互的父物体名（官方拼写就是小写 p）。</summary>
        internal const string InteractParentName = "Interactparent";

        #endregion

        #region 纯判据

        /// <summary>
        /// 该不该把工地的付费交互打开。任一条不满足即 false（fail-closed），reason 给日志。
        /// cropsInjected 是硬顺序：Built 子树激活时官方 Garden.Start → Crop.RefreshDisplayInstance 对 GetPrefab 无空检查，
        /// 作物必须先进 CropDatabase。
        /// </summary>
        internal static bool ShouldOpenSite(bool moduleEnabled, bool gardenUnlocked, bool inBaseScene,
            bool cropsInjected, bool siteFound, bool interactParentFound, bool interactParentActive,
            bool alreadyBuilt, out string reason)
        {
            if (!moduleEnabled) { reason = "module_disabled"; return false; }
            if (!gardenUnlocked) { reason = "garden_locked"; return false; }
            if (!inBaseScene) { reason = "not_base_scene"; return false; }
            if (!cropsInjected) { reason = "crops_not_injected"; return false; }
            if (alreadyBuilt) { reason = "already_built"; return false; }
            if (!siteFound) { reason = "site_not_found"; return false; }
            if (!interactParentFound) { reason = "interact_parent_not_found"; return false; }
            if (interactParentActive) { reason = "already_open"; return false; }
            reason = null;
            return true;
        }

        /// <summary>「菜地已建成」的唯一判据：场上有活的官方 Garden，或官方存档键已写真。</summary>
        internal static bool IsGardenBuilt(bool liveGardenRegistered, bool saveKeyExists, bool saveKeyValue)
        {
            return liveGardenRegistered || (saveKeyExists && saveKeyValue);
        }

        /// <summary>
        /// F3 只读用例：工地门是否处于「可解释」的状态。红 = 官方结构变了，必须人看一眼。
        /// 未解锁不 SKIP：工地找不找得到与解锁无关，这正是要查的。
        /// </summary>
        internal static bool EvaluateSiteGate(bool siteFound, bool interactParentFound, bool interactParentActive,
            bool costTakerFound, bool built, bool gardenUnlocked, bool cropsInjected, out string metrics, out string reason)
        {
            metrics = "site_found=" + siteFound + ",interact_parent=" + interactParentFound + ",parent_active=" + interactParentActive
                + ",costtaker=" + costTakerFound + ",built=" + built + ",garden_unlocked=" + gardenUnlocked
                + ",crops_injected=" + cropsInjected + ",read_only=true";
            reason = null;
            if (!siteFound && !built) reason = "找不到官方菜地工地（key=GardenConstruction / name=GardenConstruct），官方结构可能已变";
            else if (siteFound && !interactParentFound && !built) reason = "工地下没有付费交互的父物体，Mod 开不了门";
            else if (interactParentActive && !costTakerFound) reason = "父物体已打开却找不到 CostTaker，结构自相矛盾";
            else if (gardenUnlocked && cropsInjected && siteFound && interactParentFound && !interactParentActive && !built)
                reason = "第一章已交付、作物已注入，工地却仍关着：开门逻辑没跑";
            return reason == null;
        }

        /// <summary>造价文案。money 为官方 Cost.money，itemLines 为已拼好的「名字 x数量」（可空）。</summary>
        internal static string BuildCostText(long money, string itemLines, bool chinese)
        {
            bool hasItems = !string.IsNullOrEmpty(itemLines);
            if (money <= 0 && !hasItems) return chinese ? "免费" : "free";
            string text = string.Empty;
            if (money > 0) text = chinese ? money + " 金" : money.ToString();
            if (hasItems) text = text.Length == 0 ? itemLines : text + (chinese ? "，" : ", ") + itemLines;
            return text;
        }

        #endregion
    }
}
