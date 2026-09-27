// ============================================================================
// F3GameplayValidationMapTour.cs - 地图选择器逐图进场（MAP_TOUR_*）的取数与编排
// ============================================================================
// 模块说明：
//   以前 F3 只进 DEMO 竞技场，另外八张图（含风暴区地下、冷库两个子场景）全靠人工 M_ENTRY_02。
//   这里按 ModBehaviour.GetAllMapConfigs()——也就是地图选择器列出来的同一份清单——逐张走玩家的路：
//   在基地排好入场（与点地图条目同一个待处理索引、同一个 bossRushArenaPlanned、同一个信标索引），
//   加载地图的 sceneID，等 BossRush 接管，核对之后经官方 LoadBaseScene 回基地，再走下一张。
//
//   只读核对：刷新点落不落得了地、接不接得上导航，玩家有没有落在配置的传送点上，
//   回基地后竞技场与模式状态有没有清掉。不刷怪、不开波次、不扣船票（直传来源）、不写存档。
//   判据在 F3GameplayValidationMapTourJudges.cs（纯函数，执行回归 F3MapTourJudges 逐字运行）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>进场加载完之后，等 BossRush 接管（竞技场激活、交互点、刷新点、传送到位）的上限。</summary>
        private const float MapTourSetupTimeoutSeconds = 30f;
        /// <summary>刷新点 / 玩家脚下找地面：往下多深、从上方多高起射。</summary>
        private const float MapTourGroundBelow = 3f, MapTourGroundAbove = 1.5f;
        /// <summary>报告里最多列几个不合格的刷新点。</summary>
        private const int MapTourMaxOffenders = 12;

        private bool _mapTourPassed;

        /// <summary>6/7 之后：地图选择器里的每一张图都从基地出发进一次，全部跑完再记 MAP_TOUR_ALL。</summary>
        private IEnumerator RunMapTour()
        {
            BossRushMapConfig[] maps = ModBehaviour.GetAllMapConfigs() ?? new BossRushMapConfig[0];
            Stopwatch total = Stopwatch.StartNew();
            int ran = 0, passed = 0;
            // 前面阶段停在竞技场里：先按验收口径收干净，第一张图的「回基地」再走官方入口。
            try { _host.ValidationSafeCleanup(); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 逐图进场前清理失败: " + e.Message); }
            for (int i = 0; i < maps.Length; i++)
            {
                string caseId = F3MapTourJudges.CaseId(maps[i] == null ? null : maps[i].sceneName);
                if (ShouldAbort())
                {
                    Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason());
                    continue;
                }
                ran++;
                yield return RunMapTourEntry(maps[i], i, caseId);
                if (_mapTourPassed) passed++;
            }
            string metrics, reason;
            bool ok = F3MapTourJudges.JudgeTour(maps.Length, passed, ran, out metrics, out reason);
            Record("MAP_TOUR_ALL", ok ? "PASS" : "FAIL", total.ElapsedMilliseconds, metrics, reason);
        }

        private IEnumerator RunMapTourEntry(BossRushMapConfig map, int index, string caseId)
        {
            _mapTourPassed = false;
            Stopwatch sw = Stopwatch.StartNew();
            F3MapTourObservation o = new F3MapTourObservation();
            o.ExpectedScene = map == null ? null : map.sceneName;
            o.ConfiguredPoints = map == null || map.spawnPoints == null ? 0 : map.spawnPoints.Length;
            o.HasCustomSpawn = map != null && map.customSpawnPos.HasValue;
            o.PlayerToSpawnMeters = float.NaN;

            // 1. 地图选择器在基地：不在基地就先经官方入口回去。
            if (!IsRuntimeReady(BaseSceneNameForValidation()))
            {
                yield return LoadScene(null, caseId + "_FROM_BASE", returnToBase: true);
                if (!_operationSucceeded)
                {
                    Record(caseId, "FAIL", sw.ElapsedMilliseconds, string.Empty, "base_not_ready_before_entry");
                    yield break;
                }
            }
            if (map == null || string.IsNullOrEmpty(map.sceneID) || string.IsNullOrEmpty(map.sceneName))
            {
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, string.Empty, "map_config_incomplete");
                yield break;
            }

            // 2. 排好入场并加载；子场景（风暴区地下、冷库）由生产 ForceTeleportToSubScene 接着传，LoadScene 等到目标子场景为止。
            bool planned = false;
            try { _host.ValidationPlanMapTourEntry(index, map.beaconIndex); planned = true; }
            catch (Exception e) { o.CollectError = "plan:" + e.GetType().Name; }
            bool entered = false;
            if (planned)
            {
                yield return LoadScene(map.sceneID, caseId + "_ENTER", false, false, map.sceneName);
                entered = _operationSucceeded;
            }

            // 3. 等 BossRush 接管：竞技场激活、路牌 / 交互点、刷新点、自定义传送到位。
            float deadline = Time.realtimeSinceStartup + MapTourSetupTimeoutSeconds;
            while (entered && !ShouldAbort() && Time.realtimeSinceStartup < deadline && !MapTourSetupDone(map))
                yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;
            o = CollectMapTourArrival(map, o);
            if (!entered)
            {
                try { _host.ValidationCancelMapTourEntry(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 撤掉逐图入场失败: " + e.Message); }
            }

            // 4. 走官方入口回基地：离开竞技场的收尾是生产 OnSceneLoaded 那一支做的，这里只看它有没有做干净。
            yield return LoadScene(null, caseId + "_RETURN_BASE", returnToBase: true);
            o.ReturnedToBase = _operationSucceeded;
            if (o.ReturnedToBase)
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                o.BaseLeftover = MapTourBaseLeftover();
            }
            o.ElapsedMs = sw.ElapsedMilliseconds;
            string metrics, reason;
            _mapTourPassed = F3MapTourJudges.JudgeArrival(o, out metrics, out reason);
            Record(caseId, _mapTourPassed ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, reason);
        }

        /// <summary>BossRush 接管这张图的事实都到齐了（等待循环用；判定另由纯判据按全量观测做）。</summary>
        private bool MapTourSetupDone(BossRushMapConfig map)
        {
            try
            {
                if (!IsRuntimeReady(map.sceneName) || !_host.ValidationArenaActive) return false;
                if (GameObject.Find(MapTourEntryPointName) == null) return false;
                Vector3[] points = _host.GetCurrentSceneSpawnPoints();
                if (points == null || points.Length == 0) return false;
                if (!map.customSpawnPos.HasValue) return true;
                CharacterMainControl player = CharacterMainControl.Main;
                return player != null && HorizontalDistance(player.transform.position, map.customSpawnPos.Value)
                    <= F3MapTourJudges.MaxPlayerToSpawnMeters;
            }
            catch { return false; }
        }

        /// <summary>生产 TryCreateArenaDifficultyEntryPoint 建的路牌（零度挑战是同名的隐形交互点）。</summary>
        private const string MapTourEntryPointName = "BossRush_Roadsign";

        private F3MapTourObservation CollectMapTourArrival(BossRushMapConfig map, F3MapTourObservation o)
        {
            try
            {
                o.ActualScene = SceneManager.GetActiveScene().name;
                o.RuntimeReady = IsRuntimeReady(map.sceneName);
                o.ArenaActive = _host.ValidationArenaActive;
                string mode;
                if (_host.ValidationHasActiveMode(out mode)) o.OtherMode = mode;
                o.EntryPointPresent = GameObject.Find(MapTourEntryPointName) != null;

                Vector3[] loaded = _host.GetCurrentSceneSpawnPoints();
                o.LoadedPoints = loaded == null ? 0 : loaded.Length;
                List<string> offenders = new List<string>();
                AstarPath astar = AstarPath.active;
                for (int i = 0; loaded != null && i < loaded.Length; i++)
                {
                    Vector3 point = loaded[i];
                    if (HasGroundNear(point, null)) o.GroundedPoints++;
                    else if (offenders.Count < MapTourMaxOffenders) offenders.Add(i + ":ground");
                    float nav = NavDistance(astar, point);
                    if (nav <= F3MapTourJudges.MaxNavDistanceMeters) o.NavPoints++;
                    else if (offenders.Count < MapTourMaxOffenders)
                        offenders.Add(i + ":nav=" + (float.IsInfinity(nav) ? "none" : nav.ToString("0.0", CultureInfo.InvariantCulture) + "m"));
                }
                o.PointOffenders = offenders.Count == 0 ? null : string.Join(",", offenders.ToArray());

                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    Vector3 position = player.transform.position;
                    if (map.customSpawnPos.HasValue) o.PlayerToSpawnMeters = HorizontalDistance(position, map.customSpawnPos.Value);
                    o.PlayerGrounded = HasGroundNear(position, player.transform);
                }
            }
            catch (Exception e) { o.CollectError = e.GetType().Name; }
            return o;
        }

        /// <summary>回基地之后还挂着的竞技场 / 模式状态；干净返回 null。</summary>
        private string MapTourBaseLeftover()
        {
            try
            {
                string mode;
                if (_host.ValidationHasActiveMode(out mode)) return mode;
                return _host.ValidationArenaActive ? "arena_active" : null;
            }
            catch (Exception e) { return "probe_threw:" + e.GetType().Name; }
        }

        /// <summary>从点上方一段往下扫，找到一处实体碰撞面算有地（跳过 <paramref name="ignore"/> 自己的碰撞体，屋顶挡在上方也不算数）。</summary>
        private static bool HasGroundNear(Vector3 point, Transform ignore)
        {
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * MapTourGroundAbove, Vector3.down,
                MapTourGroundAbove + MapTourGroundBelow, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; hits != null && i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || (ignore != null && collider.transform.IsChildOf(ignore))) continue;
                if (hits[i].point.y <= point.y + 0.5f) return true;
            }
            return false;
        }

        /// <summary>点到最近可走导航面的水平距离；没有 A*、找不到节点或上下差超过 3 m 时是无穷大。</summary>
        private static float NavDistance(AstarPath astar, Vector3 point)
        {
            if (astar == null) return float.PositiveInfinity;
            NNInfo nearest = astar.GetNearest(point, NNConstraint.Walkable);
            if (nearest.node == null) return float.PositiveInfinity;
            Vector3 snapped = nearest.position;
            if (Mathf.Abs(snapped.y - point.y) > 3f) return float.PositiveInfinity;
            return HorizontalDistance(snapped, point);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
