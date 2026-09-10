// ============================================================================
// F3GameplayValidationSkyIsland.cs - 天空岛（晴岚群岛）岛内验收套件
// ============================================================================
// 为什么要单独一套：
//   主套件 `RunSuite` 从基地出发、切竞技场、最后 `RunFinalChecks` 又切回基地，
//   而天空岛是**独立出击关卡**——一旦切图，会话、租约、bundle 和这一趟的所有内容
//   就都没了。所以 `CheckStartGate` 一直显式拒绝在岛内启动（"请先退出天空岛"），
//   结果是 `M_SKY_ISLAND_01..09` 九条用例长期零自动覆盖，只能从头手点两小时。
//
//   这里给天空岛一条**自己的入口**：`TryStartSkyIsland` 要求人已经在岛上，
//   套件只在岛内跑、跑完把人留在岛上，不切任何图。
//
// 纪律（与主套件一致，且更严）：
//   1. **只读**。岛内验收跑在玩家真实的这一趟出击上，任何写剧情、移动玩家、
//      生成敌人、开箱的动作都会污染他正在做的事。需要「做点什么才能验」的
//      一律进 `docs/制作教程/天空岛/天空岛_待人工验证清单.md`，不许在这里偷偷改状态。
//   2. **用例隔离**。单条红项只记 FAIL 继续跑下一条，绝不 `fatalAbort` 整套
//      （2026-09-01 已经踩过：一条红把后面全部拖成 CANCELLED）。同步用例天然
//      被 `RunSyncCase` 的 try/catch 隔离，协程用例走 `RunSkyIslandCase`。
//   3. **不许把结构断言说成实机验收**。本文件里所有 PASS 都只证明「这一帧的
//      对象图、数值和几何满足断言」，证明不了手感、观感、帧率与真实战斗。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Duckov.Scenes;
using Duckov.UI;
using Pathfinding;
using Saves;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>本轮会话跑的是天空岛套件而不是主套件。由 <see cref="TryStartSkyIsland"/> 置位。</summary>
        private bool _skyIslandMode;

        /// <summary>岛内单条协程用例的时间预算。这里没有刷怪与切图，60 秒足够导航探路跑完。</summary>
        private const float SkyIslandCaseTimeoutSeconds = 60f;

        /// <summary>开跑那一刻的天空岛场景实例句柄与模态输入租约数：用例据此判断「这趟出击还在不在」「是不是自己漏了租约」。</summary>
        private int _skyIslandSceneHandle, _skyIslandBaselineLeases;

        /// <summary>
        /// 岛内套件里全部用例的 ID。装配失败时用来批量记 SKIP，报告里不留空白，
        /// 也让 `GameplayCoverage.json` 的 `SKY_ISLAND.automatic` 有一份可对照的清单。
        /// </summary>
        private static readonly string[] SkyIslandCaseIds =
        {
            "SKY_SESSION_READY", "SKY_OFFICIAL_CONTRACT", "SKY_SCENE_IDENTITY", "SKY_NAV_GRAPH",
            "SKY_EXPLOSION_PATCH", "SKY_MARKERS", "SKY_CONTENT_TABLE", "SKY_SCAVENGE_PLACEMENT",
            "SKY_INTERACTION_SEPARATION", "SKY_LOOT_BANDS", "SKY_PANEL_ART",
            "SKY_GATE_STATE", "SKY_GATE_REACHABILITY",
            "SKY_STORY_OBJECTIVE", "SKY_STORY_CODEC", "SKY_STORY_SAVE_STATE", "SKY_SERVICE_PRICING",
            "SKY_BOUNTY_GATING", "SKY_RESIDENTS", "SKY_EXTRACTION_RINGS", "SKY_EXTRACTION_RULE",
            "SKY_EXTRACTION_OFFICIAL_UI", "SKY_STORM_TUNING", "SKY_LOCALIZATION_EN", "SKY_SCENE_BASELINE",
        };

        // ====================================================================
        // 入口与启动门
        // ====================================================================

        /// <summary>
        /// 岛内启动门。与 <see cref="CheckStartGate"/> 的差别是**互斥方向相反**：
        /// 主套件要求「不在岛上」，这一套要求「已经在岛上且已就绪」。
        ///
        /// 仍然要求专用测试档：套件本身只读，但它跑在一趟真实出击上，
        /// 而这趟出击会照常写剧情、掉耐久、结算死亡。让人在自己的正式存档上按这个按钮，
        /// 与 `M_SKY_ISLAND_01/04/06/07` 自己写的「用专用测试档进岛」也不一致。
        /// </summary>
        private bool CheckSkyIslandStartGate(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "仅 Dev 构建可用"; return false; }
            if (_host == null) { reason = "ModBehaviour 未就绪"; return false; }
            SkyIslandSession session = _host.GetComponent<SkyIslandSession>();
            if (session == null) { reason = "请先进入天空岛，再运行岛内验收"; return false; }
            if (!session.IsReady) { reason = "天空岛尚未就绪或正在返航，请稍后重试"; return false; }
            if (_host.GetComponent<ArenaPrototypeSession>() != null) { reason = "请先退出自建试验场"; return false; }
            if (!SkyIslandRaidLease.IsRaidScene(SceneManager.GetActiveScene()))
            { reason = "当前活动场景不是天空岛独立关卡"; return false; }
            if (!IsDedicatedCurrentSlot()) { reason = "当前槽不是专用测试档，请回基地先点标记按钮"; return false; }
            if (SavesSystem.IsSaving) { reason = "存档系统正忙"; return false; }
            if (LevelManager.Instance == null || !LevelManager.AfterInit) { reason = "LevelManager 未初始化"; return false; }
            if (CharacterMainControl.Main == null || CharacterMainControl.Main.CharacterItem == null)
            { reason = "玩家或资源未就绪"; return false; }
            if (SceneLoader.IsSceneLoading) { reason = "正在切图"; return false; }
            // 与主门同口径：开着剧情面板、背包或地图时不开跑。否则收尾那条「套件没漏模态租约」
            // 会把玩家自己开着的面板算成验收的锅。
            if (ZombieModeUIHelper.ModalInputLeaseCount > 0) { reason = "请先关闭剧情面板等模态界面"; return false; }
            if (View.ActiveView != null) { reason = "请先关闭背包、地图等官方界面"; return false; }
            string mode;
            if (_host.ValidationHasActiveMode(out mode)) { reason = "检测到活动玩法: " + mode; return false; }
            return true;
        }

        /// <summary>
        /// 这趟出击是否还在：会话存在、已就绪（未返航、未死亡），而且还是开跑时那个场景实例。
        /// 验收途中玩家站进撤离圈返航、倒下或换槽都会让它不成立——那之后的用例在已经没有岛的环境里跑只会红一片，
        /// 而那不是产品缺陷；旧编排把这些红项连同「套件不应结束这趟出击」一起算在套件头上。
        /// </summary>
        private bool SkyIslandSessionStillValid(out string reason)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "island_session_closed"; return false; }
            if (!session.IsReady) { reason = "island_session_not_ready"; return false; }
            if (session.ValidationScene.handle != _skyIslandSceneHandle) { reason = "island_scene_changed"; return false; }
            reason = null;
            return true;
        }

        /// <summary>岛内同步用例的外壳：这趟出击已经不在了就记 SKIP（附原因），否则照常交给 RunSyncCase。</summary>
        private void RunSkyIslandSync(string id, SyncValidation validation)
        {
            string reason;
            if (!SkyIslandSessionStillValid(out reason))
            {
                Record(id, "SKIP", 0L, string.Empty, reason);
                return;
            }
            RunSyncCase(id, validation);
        }

        internal static bool TryStartSkyIsland(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._running) { reason = "已有验收正在运行"; return false; }
            if (!_instance.CheckSkyIslandStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            _instance._skyIslandMode = true;
            SkyIslandSession started = _instance.SkyIslandSessionOrNull();
            _instance._skyIslandSceneHandle = started == null ? 0 : started.ValidationScene.handle;
            _instance._skyIslandBaselineLeases = ZombieModeUIHelper.ModalInputLeaseCount;
            if (!_instance.BeginSession(out reason)) { _instance._skyIslandMode = false; return false; }
            _instance.WriteRaw("SUITE | SKY_ISLAND | scene=" + SceneManager.GetActiveScene().path);
            _instance._routine = _instance.StartCoroutine(_instance.RunSession());
            return true;
        }

        private SkyIslandSession SkyIslandSessionOrNull()
        {
            return _host == null ? null : _host.GetComponent<SkyIslandSession>();
        }

        // ====================================================================
        // 编排
        // ====================================================================

        private IEnumerator RunSkyIslandSuite()
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null)
            {
                for (int i = 0; i < SkyIslandCaseIds.Length; i++)
                    Record(SkyIslandCaseIds[i], "SKIP", 0L, string.Empty, "sky_island_session_missing");
                yield break;
            }

            SetStage("1/5 场景装配与官方合同");
            yield return SamplePerformance("SKY_PERF_BASELINE_5S", 5f, true);
            RunSkyIslandSync("SKY_SESSION_READY", ValidateSkyIslandSessionReady);
            RunSkyIslandSync("SKY_OFFICIAL_CONTRACT", ValidateSkyIslandOfficialContract);
            RunSkyIslandSync("SKY_SCENE_IDENTITY", ValidateSkyIslandSceneIdentity);
            RunSkyIslandSync("SKY_NAV_GRAPH", ValidateSkyIslandNavigationGraph);
            RunSkyIslandSync("SKY_EXPLOSION_PATCH", ValidateSkyIslandExplosionPatch);

            SetStage("2/5 内容装配与落位");
            RunSkyIslandSync("SKY_MARKERS", ValidateSkyIslandMarkers);
            RunSkyIslandSync("SKY_CONTENT_TABLE", ValidateSkyIslandContentTable);
            RunSkyIslandSync("SKY_SCAVENGE_PLACEMENT", ValidateSkyIslandScavengePlacement);
            RunSkyIslandSync("SKY_INTERACTION_SEPARATION", ValidateSkyIslandInteractionSeparation);
            RunSkyIslandSync("SKY_LOOT_BANDS", ValidateSkyIslandLootBands);
            RunSkyIslandSync("SKY_PANEL_ART", ValidateSkyIslandPanelArt);

            SetStage("3/5 门控与导航");
            RunSkyIslandSync("SKY_GATE_STATE", ValidateSkyIslandGateState);
            yield return RunSkyIslandCase("SKY_GATE_REACHABILITY", RunSkyIslandReachability);
            RunSkyIslandSync("SKY_STORY_OBJECTIVE", ValidateSkyIslandObjective);

            SetStage("4/5 存档、服务与居民");
            RunSkyIslandSync("SKY_STORY_CODEC", ValidateSkyIslandStoryCodec);
            RunSkyIslandSync("SKY_STORY_SAVE_STATE", ValidateSkyIslandSaveState);
            RunSkyIslandSync("SKY_SERVICE_PRICING", ValidateSkyIslandServicePricing);
            RunSkyIslandSync("SKY_BOUNTY_GATING", ValidateSkyIslandBountyGating);
            RunSkyIslandSync("SKY_RESIDENTS", ValidateSkyIslandResidents);

            SetStage("5/5 撤离、表现与本地化");
            RunSkyIslandSync("SKY_EXTRACTION_RINGS", ValidateSkyIslandExtractionRings);
            RunSkyIslandSync("SKY_EXTRACTION_RULE", ValidateSkyIslandExtractionRule);
            RunSkyIslandSync("SKY_EXTRACTION_OFFICIAL_UI", ValidateSkyIslandOfficialCountdown);
            RunSkyIslandSync("SKY_STORM_TUNING", ValidateSkyIslandStormTuning);
            RunSkyIslandSync("SKY_LOCALIZATION_EN", ValidateSkyIslandEnglishText);
            RunSkyIslandSync("SKY_SCENE_BASELINE", ValidateSkyIslandSceneBaseline);
        }

        /// <summary>
        /// 岛内收尾。**刻意不切图**：主套件的 `RunFinalChecks` 会 `LoadScene(returnToBase: true)`，
        /// 在这里那等于把玩家连人带这一趟出击一起送回基地——他还要接着做人工清单。
        /// </summary>
        private IEnumerator RunSkyIslandFinalChecks()
        {
            SetStage("收尾 · 岛内快照");
            yield return SamplePerformance("SKY_PERF_FINAL_5S", 5f, false);
            RunSkyIslandSync("SKY_FINAL_SESSION_INTACT", ValidateSkyIslandSessionIntact);
            // `_skyIslandMode` 的复位在 CompleteSession 里做，那是唯一一条无论取消/异常都会走到的收尾路径。
        }

        /// <summary>
        /// 岛内协程用例的隔离壳。与 <see cref="RunIsolatedCase"/> 同一套写法，
        /// 但**不做清场**：岛上没有「竞技场空闲不变式」，强清反而会动玩家的东西。
        /// </summary>
        private IEnumerator RunSkyIslandCase(string caseId, Func<IEnumerator> factory)
        {
            if (ShouldAbort())
            {
                Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason());
                yield break;
            }
            string gone;
            if (!SkyIslandSessionStillValid(out gone))
            {
                Record(caseId, "SKIP", 0L, string.Empty, gone);
                yield break;
            }
            IEnumerator inner = null;
            bool recorded = false;
            try { inner = factory(); }
            catch (Exception e)
            {
                Record(caseId, "FAIL", 0L, string.Empty, "case_factory_threw:" + e);
                recorded = true;
            }
            if (inner == null)
            {
                if (!recorded) Record(caseId, "FAIL", 0L, string.Empty, "case_factory_returned_null");
                yield break;
            }
            Stopwatch sw = Stopwatch.StartNew();
            ValidationCoroutineStack stack = new ValidationCoroutineStack(inner);
            try
            {
                while (!ShouldAbort())
                {
                    object current;
                    Exception error;
                    bool more = TryStep(stack, out current, out error);
                    if (error != null || sw.Elapsed.TotalSeconds > SkyIslandCaseTimeoutSeconds)
                    {
                        Record(caseId + "_UNHANDLED", "FAIL", sw.ElapsedMilliseconds,
                            "budget_s=" + SkyIslandCaseTimeoutSeconds,
                            error != null ? error.ToString() : "case_timeout");
                        break;
                    }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { stack.Dispose(); }
                catch (Exception e) { Record(caseId + "_DISPOSE", "FAIL", sw.ElapsedMilliseconds, string.Empty, e.ToString()); }
            }
        }

        /// <summary>
        /// 用**当前**导航图从玩家所在位置实际探一遍路。
        ///
        /// 判据只钉两件玩家一定要能做到的事：
        /// 1. **撤离点必须可达**——走不到 `Exit` 就是软锁，这是全套里唯一的 P0 级几何事实；
        /// 2. **所有自动遭遇点必须可达**——离线的门/导航属性测试已经证明五门全关时
        ///    13 组自动遭遇都在出生点的可达分量里，运行时对不上就说明门 AABB 或导航重扫出了问题。
        ///
        /// 地标（地形根下全部 POI_ 节点，含装饰节点 POI_B_Mural，共 13 个）的可达性只记进 metrics 不做判据：
        /// 支路与钟庭本来就该被门挡着，把它们写成硬断言会随剧情进度自己变红。
        /// </summary>
        private IEnumerator RunSkyIslandReachability()
        {
            Stopwatch sw = Stopwatch.StartNew();
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            CharacterMainControl player = CharacterMainControl.Main;
            if (root == null || player == null)
            {
                Record("SKY_GATE_REACHABILITY", "FAIL", sw.ElapsedMilliseconds, string.Empty, "world_root_or_player_missing");
                yield break;
            }

            List<Transform> required = new List<Transform>();
            List<Transform> informational = new List<Transform>();
            List<string> unreachable = new List<string>();
            if (session.ValidationExitMarker != null) required.Add(session.ValidationExitMarker);
            else unreachable.Add("Exit:marker_missing");
            SkyIslandContentData content = SkyIslandContent.CreateFallback();
            for (int i = 0; i < content.Encounters.Length; i++)
            {
                if (content.Encounters[i].Manual) continue;
                Transform marker = root.transform.Find(content.Encounters[i].Marker);
                if (marker != null) required.Add(marker);
                // 必到点连标记都找不到不能静默跳过：少探一个点，PASS 就少证明一件事。按不可达记进同一条用例。
                else unreachable.Add(content.Encounters[i].Marker + ":marker_missing");
            }
            foreach (Transform child in root.transform)
                if (child.name.StartsWith("POI_", StringComparison.Ordinal)) informational.Add(child);

            GameObject probeHost = null;
            List<string> reachableLandmarks = new List<string>();
            List<string> blockedLandmarks = new List<string>();
            int probed = 0;
            try
            {
                probeHost = new GameObject("SkyIslandValidationProbe");
                probeHost.transform.SetParent(root.transform, false);
                Seeker seeker = probeHost.AddComponent<Seeker>();
                GraphMask mask = session.ValidationNavigationMask;
                Vector3 origin = player.transform.position;

                for (int i = 0; i < required.Count; i++)
                {
                    IEnumerator step = ProbePath(seeker, origin, required[i], mask, unreachable, null);
                    while (step.MoveNext()) yield return step.Current;
                    probed++;
                }
                for (int i = 0; i < informational.Count; i++)
                {
                    IEnumerator step = ProbePath(seeker, origin, informational[i], mask, blockedLandmarks, reachableLandmarks);
                    while (step.MoveNext()) yield return step.Current;
                    probed++;
                }
            }
            finally { if (probeHost != null) UnityEngine.Object.Destroy(probeHost); }

            string metrics = "probed=" + probed + ",required=" + required.Count
                + ",landmarks_reachable=" + reachableLandmarks.Count + "/" + informational.Count
                + ",landmarks_blocked=" + string.Join("+", blockedLandmarks.ToArray());
            if (unreachable.Count > 0)
                Record("SKY_GATE_REACHABILITY", "FAIL", sw.ElapsedMilliseconds, metrics,
                    "从玩家当前位置走不到这些必到点（撤离点走不到即为软锁）：" + string.Join(",", unreachable.ToArray()));
            else
                Record("SKY_GATE_REACHABILITY", "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
        }

        /// <summary>单条 A* 探路。结果按可达/不可达分别写进调用方给的两个列表。</summary>
        private IEnumerator ProbePath(Seeker seeker, Vector3 origin, Transform target, GraphMask mask,
            List<string> failures, List<string> successes)
        {
            _probePathDone = false;
            _probePathValid = false;
            // 名字先取出来：等待探路的这几帧里场景可能被卸载，之后再读 target.name 会对已销毁对象再抛一次。
            string targetName = target == null ? "missing_target" : target.name;
            try { seeker.StartPath(origin, target.position, OnValidationProbePath, mask); }
            catch (Exception e)
            {
                failures.Add(targetName + ":" + e.GetType().Name);
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!_probePathDone && Time.realtimeSinceStartup < deadline && !ShouldAbort()) yield return null;
            if (!_probePathDone) failures.Add(targetName + ":timeout");
            else if (!_probePathValid) failures.Add(targetName);
            else if (successes != null) successes.Add(targetName);
        }

        private bool _probePathDone, _probePathValid;

        private void OnValidationProbePath(Pathfinding.Path path)
        {
            _probePathDone = true;
            _probePathValid = path != null && !path.error && path.vectorPath != null && path.vectorPath.Count >= 2;
        }
    }

    /// <summary>
    /// 「这条用例在当前条件下没有判据」的信号。
    /// 用异常而不是返回值，是为了不改共享的 <c>SyncValidation</c> 委托签名——
    /// 主套件的几十条用例都在用它。<see cref="F3GameplayValidationRunner.RunSyncCase"/>
    /// 捕获后记 SKIP，绝不记 PASS：中文语境下扫不出中文残留，假绿比不跑更糟。
    /// </summary>
    internal sealed class SkyIslandSkipCase : Exception
    {
        internal readonly string Metrics;
        internal SkyIslandSkipCase(string reason, string metrics) : base(reason) { Metrics = metrics; }
    }
}
