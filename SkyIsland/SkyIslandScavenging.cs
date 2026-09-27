using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛物资搜集点 owner。
    ///
    /// 设计口径：
    /// - 复用官方 `InteractableLootbox` 与官方搜刮 UI，玩家不需要学新交互；物品来自官方
    ///   `ItemAssetsCollection`，不新增 TypeID，也不写任何经济数值。
    /// - 落点只由已有作者标记 + 极坐标偏移推出，再由地面/墙体/占位三重检查裁决；
    ///   任一锚点失败只跳过该点（fail-open），不影响整套系统。
    /// - 按 4.12 门控：进入 72 米才真正建箱，一次 Tick 最多建一个；不在进图时预生成整图战利品。
    /// - 内容按出击刷新、不进存档：同一次出击里离开再回来是同一份内容（每点固定 seed），
    ///   重新出击才会重新 roll。
    /// </summary>
    internal sealed class SkyIslandScavenging : IDisposable
    {
        private sealed class Point
        {
            internal SkyIslandLootAnchor Anchor;
            internal Vector3 Position;
            internal bool Placed, Built, Failed, Opened, LabelVisible;
            internal InteractableLootbox Box;
            internal GameObject Label;
        }

        private readonly List<Point> points = new List<Point>();
        private readonly GameObject root;
        private readonly CharacterMainControl player;
        private readonly int groundMask;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> report;
        private readonly Action scavenged;
        private readonly int raidSeed;
        /// <summary>
        /// 牌子建不建（启用对象）的滞回带，避免在阈值上反复开关。只管「这块牌子要不要在场」，
        /// 亮不亮交给 <see cref="SkyIslandProximityLabel"/> 按 <see cref="LabelNear"/> / <see cref="LabelFar"/> 走近才浮现（UE-03）。
        /// 旧写法 45 m 内直接亮：一屏只有约 28×20 m，屏幕里每个箱子头顶都一直挂着一行彩字。
        /// </summary>
        private const float LabelShowRange = 45f;
        private const float LabelHideRange = 55f;
        /// <summary>牌子走近才浮现的距离带（米），与采集点（5 / 10）、纪念物（6 / 11）同一量级。</summary>
        private const float LabelNear = 6f;
        private const float LabelFar = 12f;
        /// <summary>翻过的箱子牌子最亮到多少：压暗成「看过了」，不再顶着「星工遗存」招人。</summary>
        private const float OpenedLabelPeak = 0.45f;
        private bool closed, subscribed, poolWarned;
        private float nextTick;
        private int openedCount;

        /// <summary>
        /// 本局实际能搜到的点数，也是 HUD「物资 已搜/可搜」的分母。
        ///
        /// **必须排除建箱失败的点**：`Failed` 的点永远开不了，把它算进分母会让计数器
        /// 永远够不到分母（玩家搜完全岛还显示 37/39，看上去像漏了两处）。
        /// 与 <see cref="AvailablePoints"/> 同一口径，只是不再扣掉已开过的。
        /// </summary>
        internal int PlacedPoints
        {
            get
            {
                int count = 0;
                for (int i = 0; i < points.Count; i++) if (points[i].Placed && !points[i].Failed) count++;
                return count;
            }
        }
        internal int OpenedPoints { get { return openedCount; } }

        /// <summary>
        /// 已经走近并真正尝试过建箱的点数（含建箱失败的）。只读，给 F3 验收用：
        /// 箱子是玩家进入激活半径才建的，没走到的点从来没建过，`FailedPoints == 0` 在那些点上恒真。
        /// 报告里要写出「建了几个」，一个都没建时那条判据只能记 SKIP。
        /// </summary>
        internal int BuiltPoints
        {
            get
            {
                int count = 0;
                for (int i = 0; i < points.Count; i++) if (points[i].Built) count++;
                return count;
            }
        }

        /// <summary>
        /// 已尝试建箱但失败的点数。只读，给 F3 验收用：
        /// `Failed` 的点是 fail-open 静默跳过的，日志里各自有一行 WARNING，但没有汇总口径，
        /// 玩家看到的分母（<see cref="PlacedPoints"/>）已经把它们扣掉了，缺口反而不可见。
        /// </summary>
        internal int FailedPoints
        {
            get
            {
                int count = 0;
                for (int i = 0; i < points.Count; i++) if (points[i].Failed) count++;
                return count;
            }
        }

        /// <summary>本局还能被搜刮记账的点数：落好位、没建箱失败、也还没开过的。</summary>
        internal int AvailablePoints
        {
            get
            {
                int count = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    Point point = points[i];
                    if (point.Placed && !point.Failed && !point.Opened) count++;
                }
                return count;
            }
        }

        internal SkyIslandScavenging(GameObject sceneRoot, CharacterMainControl mainPlayer, int ground,
            int seed, Func<bool> isValid, Action<string, bool> status, Action onScavenged)
        {
            if (sceneRoot == null) throw new ArgumentNullException("sceneRoot");
            if (mainPlayer == null) throw new ArgumentNullException("mainPlayer");
            root = sceneRoot;
            player = mainPlayer;
            groundMask = ground;
            raidSeed = seed;
            valid = isValid;
            report = status;
            scavenged = onScavenged;
            ResolveAnchors();
            InteractableLootbox.OnStartLoot += OnStartLoot;
            subscribed = true;
        }

        /// <summary>把锚点解析成实际落点。仅在进图装配时跑一次，之后不再做全图查找。</summary>
        private void ResolveAnchors()
        {
            SkyIslandLootAnchor[] anchors = SkyIslandLootTables.Anchors;
            List<Transform> blockers = CollectGameplayMarkers();
            for (int i = 0; i < anchors.Length; i++)
            {
                var point = new Point { Anchor = anchors[i] };
                points.Add(point);
                Transform marker = root.transform.Find(anchors[i].Marker);
                if (marker == null) continue;
                Vector3 position;
                if (TryResolve(marker, anchors[i], blockers, out position))
                {
                    point.Position = position;
                    point.Placed = true;
                }
            }
            Debug.Log("[SkyIslandLoot] ANCHORS placed=" + PlacedPoints + "/" + points.Count + " seed=" + raidSeed);
        }

        /// <summary>已有玩法标记全部作为占位障碍，避免搜刮箱和搜索点/居民/撤离圈抢同一次交互选择。</summary>
        private List<Transform> CollectGameplayMarkers()
        {
            var markers = new List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.parent != root.transform) continue;
                string name = child.name;
                if (name.StartsWith("Search", StringComparison.Ordinal) ||
                    name.StartsWith("POI_", StringComparison.Ordinal) ||
                    name.StartsWith("EnemySpawn", StringComparison.Ordinal) ||
                    name == "PlayerSpawn" || name == "Exit" || name == "BellExtraction" ||
                    // 两处航标撤离圈借用岛心标记，搜刮箱同样不许落进圈里。
                    name == "Region_D" || name == "Region_G") markers.Add(child);
            }
            return markers;
        }

        private bool TryResolve(Transform marker, SkyIslandLootAnchor anchor, List<Transform> blockers, out Vector3 result)
        {
            result = Vector3.zero;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float angle = (anchor.Bearing + attempt * 60f) * Mathf.Deg2Rad;
                Vector3 candidate = marker.position +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * anchor.Distance;
                RaycastHit hit;
                if (!Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out hit, 7f, groundMask,
                    QueryTriggerInteraction.Ignore) || !hit.transform.IsChildOf(root.transform)) continue;
                Vector3 ground = hit.point + Vector3.up * 0.05f;
                if (Physics.CheckCapsule(ground + Vector3.up * 0.4f, ground + Vector3.up * 1.2f, 0.45f,
                    GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) continue;
                if (TooCloseToMarker(ground, blockers)) continue;
                if (TooCloseToPlacedPoint(ground)) continue;
                result = ground;
                return true;
            }
            return false;
        }

        private static bool TooCloseToMarker(Vector3 position, List<Transform> blockers)
        {
            float clearance = SkyIslandLootTables.MarkerClearance;
            for (int i = 0; i < blockers.Count; i++)
                if ((blockers[i].position - position).sqrMagnitude < clearance * clearance) return true;
            return false;
        }

        private bool TooCloseToPlacedPoint(Vector3 position)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i].Placed && (points[i].Position - position).sqrMagnitude < 9f) return true;
            return false;
        }

        internal void Tick()
        {
            // 节流排在委托之前（同 SkyIslandEncounters.Tick，R-9「节流判断提前」）：
            // valid() 是纯查询，先判两个字段能让绝大多数帧不进委托。
            if (closed || Time.time < nextTick || valid == null || !valid()) return;
            nextTick = Time.time + SkyIslandLootTables.TickInterval;
            Vector3 origin = player.transform.position;
            float range = SkyIslandLootTables.ActivationRange;
            Point best = null;
            float bestDistance = range * range;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (!point.Placed || point.Built || point.Failed) continue;
                float distance = (point.Position - origin).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = point; }
            }
            // 一次只建一个：入区瞬间不集中做实例化与物品创建。
            if (best != null) Build(best);
            UpdateLabels(origin);
            bool chinese = L10n.IsChinese;
            if (chinese != labelsChinese) RelabelPoints(chinese);
        }

        /// <summary>已建物资牌子上一次按哪种语言写的。</summary>
        private bool labelsChinese = L10n.IsChinese;

        /// <summary>玩家在岛上切了语言：已建的牌子（含暂时隐藏的）按当前语言重写，下次显形就是对的（语言在取用时解析，AGENTS §4.4）。</summary>
        private void RelabelPoints(bool chinese)
        {
            labelsChinese = chinese;
            for (int i = 0; i < points.Count; i++)
            {
                GameObject label = points[i].Label;
                TextMeshPro text = label != null ? label.GetComponent<TextMeshPro>() : null;
                if (text != null) text.text = TierLabel(points[i].Anchor.Tier);
            }
        }

        /// <summary>
        /// 39 块世界空间 TMP 牌子不能全程常驻（每块都是独立网格与 draw call）。
        /// 按距离带滞回开关，只保留近处的；这是纯表现层，不影响箱子本身可否交互。
        /// </summary>
        private void UpdateLabels(Vector3 origin)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Label == null) continue;
                float distance = (point.Position - origin).sqrMagnitude;
                bool visible = point.LabelVisible
                    ? distance < LabelHideRange * LabelHideRange
                    : distance < LabelShowRange * LabelShowRange;
                if (visible == point.LabelVisible) continue;
                point.LabelVisible = visible;
                point.Label.SetActive(visible);
            }
        }

        private void Build(Point point)
        {
            point.Built = true;
            string error;
            InteractableLootbox box = SkyIslandRewardCrate.Build(root.transform, point.Position,
                point.Anchor.Bearing, "SkyIslandLoot_" + point.Anchor.Id, out error);
            if (box == null)
            {
                point.Failed = true;
                Debug.LogWarning("[SkyIslandLoot] 搜刮点 " + point.Anchor.Id + " 准备失败：" + error);
                return;
            }
            point.Box = box;
            int added = SkyIslandRewardCrate.Fill(box, point.Anchor.Tier, raidSeed,
                point.Anchor.Id, SkyIslandLootTables.RollCount(point.Anchor.Tier,
                    SkyIslandLootTables.CreateStream(raidSeed, "count:" + point.Anchor.Id)));
            if (added == 0 && !poolWarned && report != null)
            {
                poolWarned = true;
                report(L10n.T("群岛物资表暂时为空，本次搜刮点没有产出",
                    "The archipelago loot table is empty right now; this cache produced nothing"), true);
            }
            // 牌子先建后隐；下一次 Tick 的距离门控会按需打开。
            point.Label = AttachLabel(box.transform, point.Anchor.Tier);
            if (point.Label != null) point.Label.SetActive(false);
            Debug.Log("[SkyIslandLoot] POINT_READY id=" + point.Anchor.Id + " tier=" + point.Anchor.Tier +
                " items=" + added);
        }

        private static GameObject AttachLabel(Transform parent, SkyIslandLootTier tier)
        {
            GameObject sign = new GameObject("SkyIslandLootLabel", typeof(TextMeshPro));
            sign.transform.SetParent(parent, false);
            sign.transform.localPosition = Vector3.up * 1.4f;
            sign.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            TextMeshPro text = sign.GetComponent<TextMeshPro>();
            text.font = ZombieModeUIHelper.GetGameFont();
            text.text = TierLabel(tier);
            text.fontSize = 2.2f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = TierColor(tier);
            text.rectTransform.sizeDelta = new Vector2(12f, 3f);
            // 压在砂岩与云海高光上的世界字要有描边托住（UE-14），共享材质按字体一份。
            Material outlined = BossRushUIKit.GetOutlinedFontMaterial(text.font);
            if (outlined != null) text.fontSharedMaterial = outlined;
            // 走近才浮现（UE-03）：和纪念物、采集点、船点招牌同一口径，不再 45 m 外就常亮。
            SkyIslandProximityLabel.Attach(sign, LabelNear, LabelFar);
            return sign;
        }

        private static string TierLabel(SkyIslandLootTier tier)
        {
            return L10n.T(SkyIslandLootTables.TierNameCn(tier), SkyIslandLootTables.TierNameEn(tier));
        }

        /// <summary>
        /// 牌子字色按档次走稀有度色（UE-03）：星工遗存传说金、航务补给稀有蓝、生活物资次级灰。
        /// 旧写法借了警示黄（它不是警告）与按钮底色 Success（给白字垫底的暗绿，当字色读不清）。
        /// </summary>
        internal static Color TierColor(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return BossRushUIColors.RarityLegendary;
            if (tier == SkyIslandLootTier.Voyage) return BossRushUIColors.RarityRare;
            return BossRushUIColors.TextSecondary;
        }

        private void OnStartLoot(InteractableLootbox box)
        {
            if (closed || box == null) return;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Box != box || point.Opened) continue;
                point.Opened = true;
                openedCount++;
                // 翻过的箱子：牌子换次级色、最亮压到一半以下，读作「看过了」（UE-03）。
                if (point.Label != null)
                {
                    TextMeshPro text = point.Label.GetComponent<TextMeshPro>();
                    if (text != null) text.color = new Color(BossRushUIColors.TextSecondary.r, BossRushUIColors.TextSecondary.g,
                        BossRushUIColors.TextSecondary.b, text.color.a);
                    SkyIslandProximityLabel.SetPeak(point.Label, OpenedLabelPeak);
                }
                // 委托进度由会话持有的 owner 记账，本类不持有跨系统静态状态。
                if (scavenged != null) scavenged();
                Debug.Log("[SkyIslandLoot] POINT_OPENED id=" + point.Anchor.Id + " total=" + openedCount);
                return;
            }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            if (subscribed) { InteractableLootbox.OnStartLoot -= OnStartLoot; subscribed = false; }
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Box == null) continue;
                // 官方 LootView 仍可能持有引用；先停用再销毁，和遭遇 owner 的清理口径一致。
                point.Box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(point.Box.gameObject);
                point.Box = null;
                point.Label = null;
            }
            points.Clear();
        }
    }
}
