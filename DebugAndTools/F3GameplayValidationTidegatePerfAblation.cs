#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationTidegatePerfAblation.cs - 潮闸镇 Dev 演练：同一站位的性能开关对比（2026-11-07）
// ============================================================================
// 2026-11-07 实机（runId 20261106_154008_887）：码头 / 老镇 P95 58.9 / 61.5 ms，显卡约 7 ms、等待显示 0；
// 潮闸镇各 owner 合计 < 0.2 ms，官方 AI Update 加行为树 < 1 ms。渲染线程、物理、脚本更新、GC 这几项计数在正式游戏进程里
// 全是 unavailable（非 Development Player 不给这些 ProfilerRecorder），按同帧分项归因做不出来。
// 所以在同一站位逐项关掉一类东西、各测 5 秒，看帧时间降多少；首尾各测一次全开，判断这一分钟里有没有漂移。
// 只测不改：每一项测完立刻恢复；RestoreTidegateDrillState 与 CompleteSession 兜底会再撤一次。不削减任何玩法。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>当前生效的开关的撤销动作（逆序执行）。还原与兜底都会调 <see cref="UndoTidegatePerfAblation"/>。</summary>
        private readonly List<Action> _tidegateAblationUndo = new List<Action>();

        /// <summary>依次测的开关。首尾两个全开用来看漂移；其余每项只关一类。</summary>
        private static readonly string[] TidegateAblationModes =
        {
            "all_on", "far_enemies_off", "enemy_ai_off", "enemy_render_off", "all_enemies_off",
            "building_blocks_cleared", "shadows_off", "decor_off", "buildings_off", "all_on_again",
        };

        /// <summary>和作者工程 TidegateBuilder 的视觉分组一致（Visual/VIS_*）；地形、水面、箱柜和特效不在其中。</summary>
        private static readonly string[] TidegateDecorGroups = { "VIS_props", "VIS_scatter", "VIS_setpieces", "VIS_vehicles", "VIS_street" };

        private const float TidegateAblationSeconds = 5f;
        private const float TidegateAblationNearRadius = 35f;

        private int UndoTidegatePerfAblation()
        {
            int count = _tidegateAblationUndo.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                try { _tidegateAblationUndo[i](); }
                catch (Exception e) { WriteRaw("TIDE_PERF_ABLATION_UNDO | threw | " + e.Message); }
            }
            _tidegateAblationUndo.Clear();
            return count;
        }

        private IEnumerator RunTidegatePerfAblation(string poiName)
        {
            const string id = "TIDE_PERF_ABLATION";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            Transform poi = session.DevFindMarker(poiName);
            if (poi == null) { Record(id, "FAIL", 0L, string.Empty, "poi_missing:" + poiName); yield break; }
            yield return TidegateDrillStand(session, poi.position, errors, poiName);
            if (errors.Count != 0) { RecordTidegateDrill(id, sw, errors, notes); yield break; }
            yield return WaitSeconds(1.5f);
            var p95 = new Dictionary<string, double>(StringComparer.Ordinal);
            bool incomplete = false;
            try
            {
                foreach (string mode in TidegateAblationModes)
                {
                    if (ShouldAbort()) { incomplete = true; break; }
                    if (!TidegateDrillLive(out session, out gone)) { errors.Add(gone); break; }
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (!AtTidegatePerfStation(player.transform.position, poi.position)) { errors.Add(mode + "_outside_station"); break; }
                    int affected = 0;
                    // 消融临时关闭对象期间冻结距离 owner，防止它在采样中把测试对象重新激活。
                    if (session.DevEnemies != null)
                    {
                        TidegateEnemies activityOwner = session.DevEnemies;
                        activityOwner.DevActivityFrozen = true;
                        _tidegateAblationUndo.Add(delegate { activityOwner.DevActivityFrozen = false; });
                    }
                    try { affected = ApplyTidegateAblation(session, player.transform.position, mode); }
                    catch (Exception e) { errors.Add(mode + "_apply_threw:" + e.GetType().Name); }
                    yield return WaitSeconds(1f);
                    var frames = new List<float>(1024);
                    using (var main = new Counter(ProfilerCategory.Internal, "Main Thread", "ns"))
                    using (var batches = new Counter(ProfilerCategory.Render, "Batches Count", "count/frame"))
                    {
                        double started = Time.realtimeSinceStartupAsDouble;
                        int scene = SceneManager.GetActiveScene().handle;
                        while (!ShouldAbort() && SceneManager.GetActiveScene().handle == scene)
                        {
                            yield return null;
                            float ms = Time.unscaledDeltaTime * 1000f;
                            if (ms > 0f && !float.IsInfinity(ms)) frames.Add(ms);
                            main.Capture();
                            batches.Capture();
                            if (Time.realtimeSinceStartupAsDouble - started >= TidegateAblationSeconds) break;
                        }
                        double elapsed = Time.realtimeSinceStartupAsDouble - started;
                        int alive, near;
                        string enemyText = session.DevEnemies != null && session.DevEnemies.DevCountLivingNear(player.transform.position, TidegateAblationNearRadius, out alive, out near)
                            ? alive + "/" + near : "unavailable";
                        UndoTidegatePerfAblation();
                        if (elapsed < TidegateAblationSeconds * 0.9 || frames.Count < 30) incomplete = true;
                        double value = TidegateAblationP95(frames);
                        p95[mode] = value;
                        ResourceMetric mainMetric = main.Finish(), batchMetric = batches.Finish();
                        notes.Add(mode + ":p95_ms=" + value.ToString("F2") + ",samples=" + frames.Count + ",affected=" + affected
                            + ",main_p95_ms=" + (mainMetric.status == "available" ? (mainMetric.p95 * 0.000001).ToString("F2") : "unavailable")
                            + ",batches_p95=" + (batchMetric.status == "available" ? batchMetric.p95.ToString("F0") : "unavailable")
                            + ",enemies_alive/near35=" + enemyText);
                    }
                    yield return WaitSeconds(0.5f);
                }
            }
            finally { UndoTidegatePerfAblation(); }
            double reference;
            if (p95.TryGetValue("all_on", out reference))
            {
                string verdict = TidegateAblationVerdict(p95, reference);
                notes.Add(verdict);
                double again;
                if (p95.TryGetValue("all_on_again", out again) && Math.Abs(again - reference) > Math.Max(3.0, reference * 0.15))
                    notes.Add("drift_ms=" + (again - reference).ToString("F2"));
            }
            if (errors.Count == 0 && (incomplete || p95.Count < TidegateAblationModes.Length))
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "开关对比没有测满，未测的项目须重跑");
                yield break;
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }

        private static double TidegateAblationP95(List<float> frames)
        {
            if (frames.Count == 0) return double.NaN;
            frames.Sort();
            return frames[Mathf.Clamp(Mathf.CeilToInt(frames.Count * 0.95f) - 1, 0, frames.Count - 1)];
        }

        /// <summary>按「比全开降了多少」排出前三项。负数表示关掉以后反而更慢（多半是漂移或别的东西在抢帧）。</summary>
        private static string TidegateAblationVerdict(Dictionary<string, double> p95, double reference)
        {
            var drops = new List<KeyValuePair<string, double>>();
            foreach (KeyValuePair<string, double> entry in p95)
            {
                if (entry.Key == "all_on" || entry.Key == "all_on_again" || double.IsNaN(entry.Value)) continue;
                drops.Add(new KeyValuePair<string, double>(entry.Key, reference - entry.Value));
            }
            drops.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < drops.Count && i < 3; i++) parts.Add(drops[i].Key + "=" + drops[i].Value.ToString("F2"));
            return "reference_p95_ms=" + reference.ToString("F2") + ",largest_drops_ms=" + (parts.Count == 0 ? "none" : string.Join(";", parts.ToArray()));
        }

        /// <summary>只关一类东西，并登记撤销；返回关掉了多少个对象（0 表示这一项在这张图上没东西可关）。</summary>
        private int ApplyTidegateAblation(TidegateSession session, Vector3 player, string mode)
        {
            int affected = 0;
            switch (mode)
            {
                case "all_on":
                case "all_on_again":
                    return 0;
                case "far_enemies_off":
                case "all_enemies_off":
                    foreach (CharacterMainControl enemy in TidegateAblationEnemies(session))
                    {
                        if (mode == "far_enemies_off" && TidegateDrillFlat(enemy.transform.position, player) <= TidegateAblationNearRadius) continue;
                        GameObject go = enemy.gameObject;
                        if (!go.activeSelf) continue;
                        go.SetActive(false);
                        _tidegateAblationUndo.Add(delegate { if (go != null) go.SetActive(true); });
                        affected++;
                    }
                    return affected;
                case "enemy_ai_off":
                    foreach (CharacterMainControl enemy in TidegateAblationEnemies(session))
                    {
                        foreach (AICharacterController ai in enemy.GetComponentsInChildren<AICharacterController>(true))
                            affected += TidegateAblationDisable(ai);
                        foreach (NodeCanvas.Framework.GraphOwner owner in enemy.GetComponentsInChildren<NodeCanvas.Framework.GraphOwner>(true))
                            affected += TidegateAblationDisable(owner);
                    }
                    return affected;
                case "enemy_render_off":
                    foreach (CharacterMainControl enemy in TidegateAblationEnemies(session))
                        foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>(true))
                        {
                            if (!renderer.enabled) continue;
                            Renderer target = renderer;
                            target.enabled = false;
                            _tidegateAblationUndo.Add(delegate { if (target != null) target.enabled = true; });
                            affected++;
                        }
                    return affected;
                case "building_blocks_cleared":
                {
                    Transform buildings = session.DevWorld.Root.transform.Find("Visual/VIS_buildings");
                    if (buildings == null) return 0;
                    foreach (Renderer renderer in buildings.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!renderer.HasPropertyBlock()) continue;
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        Renderer target = renderer;
                        target.SetPropertyBlock(null);
                        _tidegateAblationUndo.Add(delegate { if (target != null) target.SetPropertyBlock(block); });
                        affected++;
                    }
                    return affected;
                }
                case "shadows_off":
                    for (int s = 0; s < SceneManager.sceneCount; s++)
                    {
                        Scene scene = SceneManager.GetSceneAt(s);
                        if (!scene.isLoaded) continue;
                        foreach (GameObject root in scene.GetRootGameObjects())
                            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                            {
                                if (light.shadows == LightShadows.None) continue;
                                Light target = light;
                                LightShadows before = target.shadows;
                                target.shadows = LightShadows.None;
                                _tidegateAblationUndo.Add(delegate { if (target != null) target.shadows = before; });
                                affected++;
                            }
                    }
                    return affected;
                case "decor_off":
                case "buildings_off":
                {
                    Transform visual = session.DevWorld.Root.transform.Find("Visual");
                    if (visual == null) return 0;
                    string[] groups = mode == "decor_off" ? TidegateDecorGroups : new[] { "VIS_buildings" };
                    foreach (string name in groups)
                    {
                        Transform group = visual.Find(name);
                        if (group == null || !group.gameObject.activeSelf) continue;
                        GameObject go = group.gameObject;
                        go.SetActive(false);
                        _tidegateAblationUndo.Add(delegate { if (go != null) go.SetActive(true); });
                        affected++;
                    }
                    return affected;
                }
            }
            throw new ArgumentException("unknown ablation mode " + mode);
        }

        private int TidegateAblationDisable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return 0;
            Behaviour target = behaviour;
            target.enabled = false;
            _tidegateAblationUndo.Add(delegate { if (target != null) target.enabled = true; });
            return 1;
        }

        private static List<CharacterMainControl> TidegateAblationEnemies(TidegateSession session)
        {
            var list = new List<CharacterMainControl>();
            if (session.DevEnemies == null) return list;
            foreach (TidegateEnemies.DevEnemyInfo info in session.DevEnemies.DevEnemies())
                if (info.Alive && info.Character != null) list.Add(info.Character);
            return list;
        }
    }
}
#endif
