// ============================================================================
// SkyIslandMapMarkers.cs - 天空岛在官方地图（M 键）上的撤离点与当前目标
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using SodaCraft.Localizations;
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
        private int appliedCleared = -1;
        private int appliedExits = -1;
        private SystemLanguage appliedLanguage;
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
            // 标签是按当前语言注入的覆盖文本（见 Add）：换了语言也整体重建一次，地图上的字才跟着换。
            SystemLanguage language = LocalizationManager.CurrentLanguage;
            // 清掉一伙航标守卫只改清场表、不改旗标：地图圈要跟着从这伙人挪到下一伙 / 灯本身，所以清场数也算变化。
            int cleared = data.clearedEncounters == null ? 0 : data.clearedEncounters.Length;
            if (data.flags == appliedFlags && cleared == appliedCleared && exits == appliedExits && language == appliedLanguage) return;
            // 进岛时已经开着的出口不提示；只有这一趟里新点亮的才提示一次。
            int opened = appliedExits < 0 ? 0 : exits & ~appliedExits;
            appliedFlags = data.flags;
            appliedCleared = cleared;
            appliedExits = exits;
            appliedLanguage = language;
            Clear();
            // 撤离点借官方出口的图标（UE-21）：和原版地图上的出口一个样子，一眼认得出「从这里走」；取不到就退回默认图标。
            Sprite exit = ExitIcon();
            Add(dock, L10n.T("码头撤离点", "Dock extraction"), BossRushUIColors.Accent, 0f, exit);
            Add(bell, L10n.T("归航钟庭撤离点", "Bell Court extraction"), BossRushUIColors.SuccessText, 0f, exit);
            Add(wind, L10n.T("悬根林广场撤离点", "Hanging Root Wood extraction"), BossRushUIColors.SuccessText, 0f, exit);
            Add(star, L10n.T("残星工坊广场撤离点", "Fallen Star Workshop extraction"), BossRushUIColors.SuccessText, 0f, exit);
            foreach (string target in ObjectiveTargets(data))
                Add(root.Find(target), ObjectiveLabel(target), BossRushUIColors.WarningText, ObjectiveRadius, null);
            // 支线 / 可选挑战用紫（UE-21）：旧写法和码头撤离点同为 Accent 青，地图上一眼分不开。
            foreach (string target in SideTargets(data))
                Add(root.Find(target), SideLabel(target), BossRushUIColors.RarityEpic, ObjectiveRadius, null);
            if (notify == null) return;
            if ((opened & 2) != 0)
                notify(L10n.T("风标亮了：悬根林广场多了一个返航点，站进绿环即可撤离",
                    "Wind beacon lit: an extraction ring has opened on the Hanging Root Wood plaza; step into the green ring to extract."), false);
            if ((opened & 4) != 0)
                notify(L10n.T("星灯亮了：残星工坊广场多了一个返航点，站进绿环即可撤离",
                    "Star lamp lit: an extraction ring has opened on the Fallen Star Workshop plaza; step into the green ring to extract."), false);
            if ((opened & 1) != 0)
                notify(L10n.T("两端航标都亮了：归航钟庭的撤离点开了", "Both beacons lit: the Bell Court extraction is open"), false);
        }

        /// <summary>与 <see cref="SkyIslandStoryRules.Objective"/> 同一顺序：先接取/复命，再探索；结局后的未交任务仍保留目标。</summary>
        internal static IEnumerable<string> ObjectiveTargets(SkyIslandStoryData data)
        {
            SkyIslandOfficialQuestDefinition contact = SkyIslandOfficialQuestTable.NextContactQuest(data);
            if (contact != null)
            {
                yield return SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(contact.GiverId);
                yield break;
            }
            if (data.Has(SkyIslandStoryFlag.Ending)) yield break;
            if (!data.BothBeacons)
            {
                // 守卫没清完时圈的是还没清的那几伙人，不是灯：修灯要先清两伙，其中一伙离灯五六十米，
                // 以前只圈灯，玩家清完灯旁那一伙、按装置被拒，地图上找不到另一伙在哪儿（2026-09-29 引导复核）。
                // 每盏灯：修好了不圈；守卫没清完圈没清的那几伙；清完了圈灯本身。
                // 写在本方法里而不是拆 helper：导航执行回归按签名只抽这两个迭代器（tests/fixtures/SkyIslandStory/run.py）。
                SkyIslandStoryFlag[] lamps = { SkyIslandStoryFlag.WindBeacon, SkyIslandStoryFlag.StarLamp };
                string[][] guards = { SkyIslandStoryRules.WindBeaconGuards, SkyIslandStoryRules.StarLampGuards };
                string[] beacons = { "Search_D", "Search_G" };
                for (int lamp = 0; lamp < lamps.Length; lamp++)
                {
                    if (data.Has(lamps[lamp])) continue;
                    bool guarded = false;
                    foreach (string guard in guards[lamp])
                    {
                        if (data.EncounterCleared(guard)) continue;
                        guarded = true;
                        yield return SkyIslandStoryRules.GuardMarker(guard);
                    }
                    if (!guarded) yield return beacons[lamp];
                }
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

        /// <summary>
        /// 这个主线目标点要干什么；地图圈与风标罗盘共用（罗盘以前只报「当前目标」，玩家分不清是去打人还是去修灯）。
        /// 认不得的标记返回 null，调用方退回泛称。
        /// </summary>
        internal static string TargetTask(string target)
        {
            switch (target)
            {
                case "EnemySpawn_D": case "Search_D_02": case "EnemySpawn_G": case "Search_G_02":
                    return L10n.T("清掉这伙守卫", "clear these guards");
                case "Search_D": return L10n.T("修风标", "fix the wind beacon");
                case "Search_G": return L10n.T("修星灯", "fix the star lamp");
                case "Search_H": return L10n.T("归航钟庭", "the Bell Court");
                case "Search_A": return L10n.T("找浮舟", "find Fuzhou");
                case "Search_B": return L10n.T("找苇白", "find Weibai");
                default: return null;
            }
        }

        /// <summary>地图圈上的字：说清圈里要干什么，而不是一律「当前目标」。</summary>
        private static string ObjectiveLabel(string target)
        {
            string task = TargetTask(target);
            return task == null ? L10n.T("当前目标", "Current objective") : L10n.T("目标 · ", "Objective · ") + task;
        }

        private static string SideLabel(string target)
        {
            return target == "POI_E"
                ? L10n.T("可选挑战 · 噬风", "Optional challenge · the Windeater")
                : L10n.T("支线目标", "Side objective");
        }

        private static readonly System.Reflection.FieldInfo ExitIconField = BossRush.Common.Utils.ReflectionCache.GetField(
            typeof(global::ExitCreator), "icon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>
        /// 官方出口在地图上用的图标（<c>ExitCreator</c> 的私有序列化字段 <c>icon</c>，官方 SpawnMapElement 就拿它 Setup）。
        /// 天空岛这张图上没有 ExitCreator、或官方改了字段名时返回 null，地图点退回官方默认图标（纯表现层，不告警）。
        /// </summary>
        private static Sprite ExitIcon()
        {
            try
            {
                global::LevelManager level = global::LevelManager.Instance;
                global::ExitCreator creator = level != null ? level.ExitCreator : null;
                return creator != null && ExitIconField != null ? ExitIconField.GetValue(creator) as Sprite : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Add(Transform anchor, string label, Color color, float areaRadius, Sprite icon)
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
                // 官方 SimplePointOfInterest.DisplayName 把 displayName 当本地化键去查（ToPlainText），查不到就显示「*键*」：
                // 直接传成品文字，地图上天空岛的标签全带星号（2026-09-15 第五轮截图）。按标记注册一条覆盖文本，再把键交给它。
                string key = "BossRush_SkyIslandMap_" + spawned.Count;
                LocalizationHelper.InjectLocalization(key, label);
                poi.Setup(icon, key);
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
