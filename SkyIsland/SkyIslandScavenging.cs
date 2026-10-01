using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛物资搜集点 owner。
    ///
    /// 设计口径：
    /// - 复用官方 `InteractableLootbox` 与官方搜刮 UI，玩家不需要学新交互（交互名「搜集」，去掉尸体箱预制体的「搬起」）；
    ///   官方物品来自 `ItemAssetsCollection`，另保底一格已登记的岛上特产，不新增 TypeID。
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
            internal bool Placed, Built, Failed, Opened;
            internal InteractableLootbox Box;
        }

        private readonly List<Point> points = new List<Point>();
        private readonly GameObject root;
        private readonly CharacterMainControl player;
        private readonly int groundMask;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> report;
        private readonly Action scavenged;
        private readonly int raidSeed;
        private readonly SkyIslandLootCrateLook crateLook;
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
            crateLook = new SkyIslandLootCrateLook(sceneRoot.transform);
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
        }

        private void Build(Point point)
        {
            point.Built = true;
            string error;
            // searchCache：交互名「搜集」、去掉官方尸体箱自带的「搬起」（只有地上的搜刮点这样，赚来的箱子保持官方原样）。
            InteractableLootbox box = SkyIslandRewardCrate.Build(root.transform, point.Position,
                point.Anchor.Bearing, "SkyIslandLoot_" + point.Anchor.Id, out error, true);
            if (box == null)
            {
                point.Failed = true;
                Debug.LogWarning("[SkyIslandLoot] 搜刮点 " + point.Anchor.Id + " 准备失败：" + error);
                return;
            }
            point.Box = box;
            // 3–7 格：保底一格岛上特产，其余按档次类别牌抽武器 / 子弹 / 装备 / 物资（owner 2026-10-01）；地上的箱子不开保底品质带。
            string contents;
            int added = SkyIslandRewardCrate.FillScavenge(box, point.Anchor, raidSeed, out contents);
            if (added == 0 && !poolWarned && report != null)
            {
                poolWarned = true;
                report(L10n.T("岛上的物资表现在是空的，这个搜刮点没出东西",
                    "The archipelago loot table is empty right now; this cache produced nothing"), true);
            }
            // 不挂浮空字（owner 2026-09-30）：档次由箱子本身与官方搜刮界面交代，头顶不再顶一行「星工遗存」。
            // 箱子外观按档次换成岛上的木箱（桶箱堆 / 长货箱 / 长风晶的小货箱）；资源不全时保留官方包的样子。
            crateLook.Apply(box, point.Anchor.Tier, point.Anchor.Bearing);
            Debug.Log("[SkyIslandLoot] POINT_READY id=" + point.Anchor.Id + " tier=" + point.Anchor.Tier +
                " items=" + added + " " + contents);
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
            }
            points.Clear();
        }
    }
}
