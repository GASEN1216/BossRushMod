// ============================================================================
// F3GameplayValidationModeHErrorSwap.cs - MODE_H_ERROR_SWAP 用例
// ============================================================================
// 为什么这条用例存在：
//   §17.6.5 的 ERROR 完整互换原本要求「逐角色 smoke 白名单」（§26.5 人工矩阵）。
//   owner 2026-09-03 裁决：运行时按 Mode H 自结算恒可用（互换自带 2 秒 deadline
//   与完整回滚，本来就 fail-safe），**实测挪到 F3 里做**。这条用例就是那份实测。
//
// 为什么不放进生产认证：
//   生产认证跑在进场加载阶段，逐 key 15 秒预算内已经排了 22 组字段实测。
//   在那里再插一次「接管玩家控制权」既拖长开局，也把一次真实的控制权转移
//   放进了玩家还在等 loading 的时点。F3 是显式触发的验收入口，是它该待的地方。
//
// 隔离纪律（照 F3GameplayValidationModeHKits.cs 的既有形态）：
//   - 只在 H 缓存验收拿到的 Drafting 租约内跑，复用真实认证池与隔离生成桥；
//   - 自建一次性 ModeHCombatControl + ModeHCombatTelemetry，不碰真实赛季状态；
//   - 收尾一律走 RestoreAll()（内部经 RestoreErrorSwap），**绝不**从测试直接调
//     ModeHRuntimeGates.SetStandInActive —— §17.6.5 冻结了「ModeHCombatControl
//     是解冻门的唯一 setter」，测试自己写门等于把被测的不变式绕过去。
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>暴力搜种子的上限。8% 命中率下几十次就能找到，4096 只是防死循环。</summary>
        private const int ErrorSwapSeedSearchLimit = 4096;

        /// <summary>
        /// ERROR 完整互换端到端验收：触发 -> 控制权切换 -> 看台表演 -> 完整还原。
        ///
        /// 三态：前置不满足记 SKIP（不伪造 PASS），互换开始后任一断言失败记 FAIL。
        /// </summary>
        private IEnumerator RunModeHErrorSwap(ModeHSupportedMap map)
        {
            Stopwatch sw = Stopwatch.StartNew();

            CharacterMainControl playerBody = CharacterMainControl.Main;
            if (playerBody == null)
            {
                Record("MODE_H_ERROR_SWAP", "SKIP", sw.ElapsedMilliseconds, string.Empty,
                    "player_body_unavailable");
                yield break;
            }
            if (LevelManager.Instance == null)
            {
                Record("MODE_H_ERROR_SWAP", "SKIP", sw.ElapsedMilliseconds, string.Empty,
                    "level_manager_unavailable");
                yield break;
            }

            ModeHProfileTemplate template = FindErrorSwapProbeProfile();
            if (template == null || map == null)
            {
                Record("MODE_H_ERROR_SWAP", "SKIP", sw.ElapsedMilliseconds, string.Empty,
                    "no_certified_probe_profile");
                yield break;
            }

            // 确定性触发：找一个让 ERROR 判定为真的 runSeed。
            // 不给产品代码开测试后门，直接用它自己的种子流复算。
            long runSeed;
            if (!TryFindErrorTriggerSeed(out runSeed))
            {
                Record("MODE_H_ERROR_SWAP", "SKIP", sw.ElapsedMilliseconds, string.Empty,
                    "error_seed_not_found");
                yield break;
            }

            KitProbeRequest request = new KitProbeRequest();
            ModeHCombatControl control = null;
            ModeHCombatTelemetry telemetry = null;
            string reason = null;
            string metrics = string.Empty;
            bool passed = false;

            // 玩家身体的赛前状态：还原断言拿它对照。
            // 注意此时观战租约已经把玩家改成中立无敌 + 看台坐标，
            // 这里存的就是「看台态」，与 ModeHCombatControl.CaptureplayerState 同一层。
            Teams beforeTeam = playerBody.Team;
            Vector3 beforePosition = playerBody.transform.position;
            bool beforeInvincible = playerBody.Health != null && playerBody.Health.Invincible;

            try
            {
                CreateKitProbe(request, template, map).Forget();
                float createDeadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
                while (!request.Done && !ShouldAbort() && Time.realtimeSinceStartup < createDeadline)
                {
                    yield return null;
                }
                if (!request.Done || request.Handle == null)
                {
                    reason = request.Done ? (request.Error ?? "create_null") : "create_timeout_or_cancel";
                }
                else
                {
                    string prepareError;
                    if (!ModeHSpawnBridge.TryPrepareForArena(request.Handle, map.ArenaSpawnPoints[0], out prepareError)
                        || !ModeHSpawnBridge.TryActivate(request.Handle, out prepareError))
                    {
                        reason = "probe_prepare_failed:" + prepareError;
                    }
                    else
                    {
                        if (request.Handle.Health != null) request.Handle.Health.SetInvincible(true);

                        telemetry = new ModeHCombatTelemetry();
                        control = new ModeHCombatControl();
                        // 本用例只驱动 ERROR 互换，不涉分量条件：擂台条件留空、
                        // 批次数给 0（即"没有后续批次"），两者都不参与互换判定。
                        control.BeginMatch(null, map, telemetry, 1, runSeed, string.Empty, null, 0);

                        ModeHParticipantRef fighter = new ModeHParticipantRef();
                        fighter.ProfileId = template.ProfileTemplateId;
                        fighter.StableKey = template.StableKey;
                        fighter.IsEnemy = false;
                        fighter.Character = request.Handle.Character;
                        control.SetRoster(fighter, null);

                        // 必须先登记一名敌军：ModeHCombatControl.Tick 的优先级 1 是
                        // 「敌军清空即判胜利」，没有敌军的话第一帧就 RestoreAll 把互换拆掉。
                        // OnEnemyEntered 只要求 IsEnemy=true，不需要真角色。
                        ModeHParticipantRef dummyEnemy = new ModeHParticipantRef();
                        dummyEnemy.IsEnemy = true;
                        dummyEnemy.PlanSlotIndex = 0;
                        telemetry.OnEnemyEntered(dummyEnemy);

                        ModeHProfileDto profile = BuildErrorSwapProbeProfile(template);
                        string enterReason;
                        if (!control.OnFighterEntered(fighter, profile, out enterReason))
                        {
                            reason = "probe_enter_failed:" + enterReason;
                        }
                        else
                        {
                            control.BindPlayerBody(playerBody);

                            // 第一帧：ERROR 判定应当命中（种子是照它复算出来的）
                            control.Tick(Mathf.Max(0f, Time.deltaTime), null);
                            if (!control.ErrorTriggered)
                            {
                                reason = "error_not_triggered";
                            }
                            else
                            {
                                IEnumerator inner = DriveErrorSwapAssertions(
                                    control, profile, playerBody, request.Handle,
                                    beforeTeam, beforePosition, beforeInvincible);
                                while (inner.MoveNext()) yield return inner.Current;
                                passed = _errorSwapProbePassed;
                                reason = _errorSwapProbeReason;
                                metrics = _errorSwapProbeMetrics;
                            }
                        }
                    }
                }
            }
            finally
            {
                request.Abandoned = true;
                // RestoreAll 内部经 RestoreErrorSwap：关表演、清解冻门、还原控制目标、
                // 还原玩家身体，并把 SetErrorSwapControlledParticipant 置空。
                if (control != null)
                {
                    try { control.RestoreAll(); }
                    catch (Exception e) { ModBehaviour.DevLog("[F3] ERROR 互换收尾异常: " + e.Message); }
                }
                ModeHSpawnBridge.Recycle(request.Handle);
                request.Handle = null;
            }

            Record("MODE_H_ERROR_SWAP", passed ? "PASS" : (reason == null ? "PASS" : "FAIL"),
                sw.ElapsedMilliseconds, metrics, passed ? null : reason);
        }

        private bool _errorSwapProbePassed;
        private string _errorSwapProbeReason;
        private string _errorSwapProbeMetrics;

        /// <summary>
        /// 互换本体的断言链。拆成独立协程是因为 catch 子句体内不能 yield return（CS1631），
        /// 而这里每一步都要跨帧等待；结论经三个字段回传，不在 catch 里 yield。
        /// </summary>
        private IEnumerator DriveErrorSwapAssertions(
            ModeHCombatControl control, ModeHProfileDto profile, CharacterMainControl playerBody,
            ModeHSpawnHandle handle, Teams beforeTeam, Vector3 beforePosition, bool beforeInvincible)
        {
            _errorSwapProbePassed = false;
            _errorSwapProbeReason = null;
            _errorSwapProbeMetrics = string.Empty;

            string beginReason;
            if (!control.TryBeginErrorSwap(profile, out beginReason))
            {
                _errorSwapProbeReason = "begin_failed:" + (beginReason ?? "unknown");
                yield break;
            }

            // 等待控制权切换完成（产品代码自己的 2 秒 deadline + 1 秒余量）
            float deadline = Time.realtimeSinceStartup
                + ModeHConfig.ErrorControlSwitchDeadlineSeconds + 1f;
            while (!control.IsErrorSwapActive && !ShouldAbort()
                && Time.realtimeSinceStartup < deadline)
            {
                control.Tick(Mathf.Max(0f, Time.deltaTime), null);
                yield return null;
            }
            if (!control.IsErrorSwapActive)
            {
                _errorSwapProbeReason = "swap_not_active_before_deadline";
                yield break;
            }

            // ---- 生效期断言 ----
            if (!ModeHRuntimeGates.IsModeHStandInActive)
            {
                _errorSwapProbeReason = "standin_gate_inactive";
                yield break;
            }
            if (!ModeHRuntimeGates.IsModeHStandInBody(playerBody.gameObject.GetInstanceID()))
            {
                _errorSwapProbeReason = "standin_body_mismatch";
                yield break;
            }
            if (!ReferenceEquals(LevelManager.Instance.ControllingCharacter, handle.Character))
            {
                _errorSwapProbeReason = "controlling_character_mismatch";
                yield break;
            }
            if (playerBody.Team != Teams.middle)
            {
                _errorSwapProbeReason = "player_body_not_neutral";
                yield break;
            }
            if (playerBody.Health == null || !playerBody.Health.Invincible)
            {
                _errorSwapProbeReason = "player_body_not_invincible";
                yield break;
            }
            string expectedPattern = ModeHStandInPerformer.ResolvePatternId(profile.temperamentId);
            if (!string.Equals(control.StandInPatternId, expectedPattern, StringComparison.Ordinal))
            {
                _errorSwapProbeReason = "standin_pattern_mismatch";
                yield break;
            }

            // 看台位移只**报告**不作判据：CanMove() 还要求 CharacterWalkSpeed > 0f，
            // navmesh 也可能合法地让它停在原地。作为判据会在正常场景下误报红。
            Vector3 sampleOrigin = playerBody.transform.position;
            float maxDisplacement = 0f;
            float sampleDeadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < sampleDeadline && !ShouldAbort()
                && control.IsErrorSwapActive)
            {
                control.Tick(Mathf.Max(0f, Time.deltaTime), null);
                float displacement = Vector3.Distance(sampleOrigin, playerBody.transform.position);
                if (displacement > maxDisplacement) maxDisplacement = displacement;
                yield return null;
            }

            // ---- 还原断言 ----
            control.RestoreErrorSwap();
            yield return null;

            if (ModeHRuntimeGates.IsModeHStandInActive)
            {
                _errorSwapProbeReason = "standin_gate_not_cleared";
                yield break;
            }
            if (!ReferenceEquals(LevelManager.Instance.ControllingCharacter, playerBody))
            {
                _errorSwapProbeReason = "controlling_character_not_restored";
                yield break;
            }
            if (playerBody.Team != beforeTeam)
            {
                _errorSwapProbeReason = "player_team_not_restored";
                yield break;
            }
            if (playerBody.Health == null || playerBody.Health.Invincible != beforeInvincible)
            {
                _errorSwapProbeReason = "player_invincible_not_restored";
                yield break;
            }
            if (Vector3.Distance(playerBody.transform.position, beforePosition) > 1f)
            {
                _errorSwapProbeReason = "player_position_not_restored";
                yield break;
            }

            _errorSwapProbeMetrics = "pattern=" + expectedPattern
                + ",standin_move=" + maxDisplacement.ToString("0.00")
                + ",attempted=" + control.ErrorSwapAttempted;
            _errorSwapProbePassed = true;
        }

        /// <summary>找一个让 ERROR 判定命中的 runSeed。用产品自己的种子流复算，不开后门。</summary>
        private static bool TryFindErrorTriggerSeed(out long runSeed)
        {
            for (int i = 1; i <= ErrorSwapSeedSearchLimit; i++)
            {
                ModeHSeedStream stream = ModeHSeedStream.Create(i, ModeHSeedStream.Domains.Error, 1);
                if (stream.NextChance(ModeHConfig.ErrorTriggerChance))
                {
                    runSeed = i;
                    return true;
                }
            }
            runSeed = 0L;
            return false;
        }

        /// <summary>任取一个已认证的生产候选做探针；取不到就 SKIP，不伪造。</summary>
        private static ModeHProfileTemplate FindErrorSwapProbeProfile()
        {
            foreach (string key in ModeHPresetRegistry.ProductionKeys)
            {
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(key);
                if (template == null) continue;
                if (ModeHPresetRegistry.GetAuditedPreset(key) == null) continue;
                return template;
            }
            return null;
        }

        /// <summary>
        /// 一次性探针档案。anomalyId 固定成 error：本用例测的就是这条异常，
        /// 不依赖抽签结果，也不写任何真实赛季状态。
        /// </summary>
        private static ModeHProfileDto BuildErrorSwapProbeProfile(ModeHProfileTemplate template)
        {
            ModeHProfileDto dto = new ModeHProfileDto();
            dto.profileId = template.ProfileTemplateId;
            dto.stableKey = template.StableKey;
            dto.displayNameKey = template.DisplayNameKey;
            dto.archetypeId = template.ArchetypeId;
            dto.temperamentId = template.TemperamentId;
            dto.quirkId = string.Empty;
            dto.anomalyId = ModeHStableIds.AnomalyError;
            dto.signatureCommandId = template.SignatureCommandId;
            dto.rumorKey = template.RumorKey;
            dto.standInPatternId = template.StandInPatternId;
            dto.status = (int)ModeHParticipantStatus.Available;
            dto.injuryId = string.Empty;
            dto.scarIds = new System.Collections.Generic.List<string>();
            dto.behaviorStatuses = new System.Collections.Generic.List<ModeHBehaviorStatusDto>();
            return dto;
        }
    }
}
