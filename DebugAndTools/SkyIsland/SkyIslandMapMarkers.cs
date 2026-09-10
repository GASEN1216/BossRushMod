// ============================================================================
// SkyIslandMapMarkers.cs - 天空岛在官方地图（M 键）上的撤离点与当前目标
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛在官方地图上的指引点。
    ///
    /// 场景包里的 MiniMapSettings 只画地形，撤离点与当前目标此前在官方地图上完全没有标记
    /// （CR-2026-09-09-012：钟庭撤离难找）。这里复用官方 <c>SimplePointOfInterest</c>（ModeF 撤离标记同一类型）：
    /// - 撤离点：码头恒显；钟庭与两处航标广场与撤离圈同一事实源（会话的 *ExitIfUnlocked），解锁才出现；
    /// - 当前目标：按剧情进度圈出下一处要去的地方，用官方的区域圈画法。
    ///
    /// 纯表现层，不参与任何判定。标记挂在世界根下、随岛一起卸载；<see cref="Apply"/> 只在剧情旗标或解锁状态
    /// 真的变化时重建，平时每帧只比较两个整数。组件建好、属性改完再 <c>Setup</c>：官方 Setup 自带「先注销再注册」，
    /// 不会像先 Setup 再激活那样留下一份重复登记。
    /// </summary>
    internal sealed class SkyIslandMapMarkers : IDisposable
    {
        /// <summary>当前目标的区域圈半径（米）：够圈住航标台 / 钟庭留言板一带，又不至于盖住整座岛。</summary>
        private const float ObjectiveRadius = 12f;

        private readonly Transform root;
        private readonly Action<string, bool> notify;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private int appliedFlags = int.MinValue;
        private int appliedExits = -1;
        private bool disposed;

        internal SkyIslandMapMarkers(Transform root, Action<string, bool> notify)
        {
            this.root = root;
            this.notify = notify;
        }

        internal void Apply(SkyIslandStoryData data, Transform dock, Transform bell, Transform wind, Transform star)
        {
            if (disposed || root == null) return;
            int exits = (bell != null ? 1 : 0) | (wind != null ? 2 : 0) | (star != null ? 4 : 0);
            if (data.flags == appliedFlags && exits == appliedExits) return;
            // 进岛时已经开着的出口不提示；只有这一趟里新点亮的才提示一次。
            int opened = appliedExits < 0 ? 0 : exits & ~appliedExits;
            appliedFlags = data.flags;
            appliedExits = exits;
            Clear();
            Add(dock, L10n.T("码头撤离点", "Dock extraction"), BossRushUIColors.Accent, 0f);
            Add(bell, L10n.T("归航钟庭撤离点", "Bell Court extraction"), BossRushUIColors.SuccessText, 0f);
            Add(wind, L10n.T("悬根林广场撤离点", "Hanging Root Wood extraction"), BossRushUIColors.SuccessText, 0f);
            Add(star, L10n.T("残星工坊广场撤离点", "Fallen Star Workshop extraction"), BossRushUIColors.SuccessText, 0f);
            foreach (string target in ObjectiveTargets(data))
                Add(root.Find(target), L10n.T("当前目标", "Current objective"), BossRushUIColors.WarningText, ObjectiveRadius);
            foreach (string target in SideTargets(data))
                Add(root.Find(target), SideLabel(target), BossRushUIColors.Accent, ObjectiveRadius);
            if (notify == null) return;
            if ((opened & 2) != 0)
                notify(L10n.T("风标点亮：悬根林广场开出返航风道，站进绿环即可撤离",
                    "Wind beacon lit: an extraction ring opened on the Hanging Root Wood plaza — step into the green ring to extract"), false);
            if ((opened & 4) != 0)
                notify(L10n.T("星灯点亮：残星工坊广场开出返航风道，站进绿环即可撤离",
                    "Star lamp lit: an extraction ring opened on the Fallen Star Workshop plaza — step into the green ring to extract"), false);
            if ((opened & 1) != 0)
                notify(L10n.T("双航标已亮：归航钟庭的撤离点开放", "Both beacons lit: the Bell Court extraction is open"), false);
        }

        /// <summary>与 <see cref="SkyIslandStoryRules.Objective"/> 同一顺序：先两端航标，再归航钟庭；结局后不再圈目标。</summary>
        internal static IEnumerable<string> ObjectiveTargets(SkyIslandStoryData data)
        {
            if (data.Has(SkyIslandStoryFlag.Ending)) yield break;
            if (!data.BothBeacons)
            {
                if (!data.Has(SkyIslandStoryFlag.WindBeacon)) yield return "Search_D";
                if (!data.Has(SkyIslandStoryFlag.StarLamp)) yield return "Search_G";
                yield break;
            }
            yield return "Search_H";
        }

        /// <summary>
        /// 可选目标（可玩性评估 R-14）：主线目标卡只管航标与钟庭，结局后更是什么都不圈，支线物证与噬风从来不上地图。
        /// - 两端航标都亮、噬风还没打：圈鸣风栈道上的风眼（`POI_E`，噬风遭遇锚点）；结局之后仍没打同样圈。
        /// - 结局之后：圈还没拿到的支线物证（S1–S4 的物证点）；种植记录拿到了但还没交，就圈风铃集留言板。
        /// 与主线目标一样只在剧情旗标变化时重建（见 <see cref="Apply"/>）；风标罗盘在主线目标都没有时指向这里。
        /// </summary>
        internal static IEnumerable<string> SideTargets(SkyIslandStoryData data)
        {
            if (data.BothBeacons && !data.StormResolved) yield return "POI_E";
            if (!data.Has(SkyIslandStoryFlag.Ending)) yield break;
            if (!data.Has(SkyIslandStoryFlag.PlantingRecord)) yield return "Search_S1";
            else if (!data.Has(SkyIslandStoryFlag.PlantingDelivered)) yield return "Search_B";
            if (!data.Has(SkyIslandStoryFlag.OldLetter)) yield return "Search_S2";
            if (!data.Has(SkyIslandStoryFlag.RouteChart)) yield return "Search_S3";
            if (!data.Has(SkyIslandStoryFlag.Telescope)) yield return "Search_S4";
        }

        private static string SideLabel(string target)
        {
            return target == "POI_E"
                ? L10n.T("可选挑战 · 噬风", "Optional challenge · the Windeater")
                : L10n.T("支线目标", "Side objective");
        }

        private void Add(Transform anchor, string label, Color color, float areaRadius)
        {
            if (anchor == null) return;
            try
            {
                var go = new GameObject("SkyIslandMapPoint_" + anchor.name);
                go.transform.SetParent(root, false);
                go.transform.position = anchor.position;
                SimplePointOfInterest poi = go.AddComponent<SimplePointOfInterest>();
                poi.Color = color;
                poi.ScaleFactor = areaRadius > 0f ? 1f : 1.4f;
                poi.IsArea = areaRadius > 0f;
                poi.AreaRadius = areaRadius;
                poi.Setup(null, label);
                spawned.Add(go);
            }
            catch (Exception e)
            {
                // 纯表现层：标不上地图也绝不能拦住旅程本身。
                Debug.LogWarning("[SkyIsland] 地图标记创建失败 " + anchor.name + "：" + e.Message);
            }
        }

        private void Clear()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) UnityEngine.Object.Destroy(spawned[i]);
            spawned.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }
    }
}
