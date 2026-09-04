using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>在双诊断角色上复用生产 adapter，采样跨帧字段保持及还原，不把点火占位判据当实测。</summary>
    internal sealed class ModeHCommandCertificationProbe : IDisposable
    {
        private readonly ModeHSpawnHandle _scav;
        private readonly ModeHSpawnHandle _wolf;
        private readonly ModeHCommandAdapter _scavAdapter = new ModeHCommandAdapter();
        private readonly ModeHCommandAdapter _wolfAdapter = new ModeHCommandAdapter();
        private bool _disposed;

        internal string FailureReasonId { get; private set; }

        internal ModeHCommandCertificationProbe(ModeHSpawnHandle scav, ModeHSpawnHandle wolf)
        {
            _scav = scav;
            _wolf = wolf;
        }

        internal IEnumerator Run(string stableKey, ModeHSupportedMap map, float deadline)
        {
            string error;
            if (map == null || map.ArenaSpawnPoints == null || map.ArenaSpawnPoints.Length < 2)
            {
                FailureReasonId = "certification_command_map_invalid";
                yield break;
            }
            if (!ModeHSpawnBridge.TryPrepareForArena(_scav, map.ArenaSpawnPoints[0], out error)
                || !ModeHSpawnBridge.TryPrepareForArena(_wolf, map.ArenaSpawnPoints[1], out error)
                || !ModeHSpawnBridge.TryActivate(_scav, out error)
                || !ModeHSpawnBridge.TryActivate(_wolf, out error))
            {
                FailureReasonId = "certification_command_prepare:" + error;
                yield break;
            }
            // AI 必须真实更新；先保护双方，命令采样完成后才由原认证执行受控伤害。
            _scav.Health.SetInvincible(true);
            _wolf.Health.SetInvincible(true);
            AICharacterController scavAi = _scav.Character.GetComponentInChildren<AICharacterController>(true);
            AICharacterController wolfAi = _wolf.Character.GetComponentInChildren<AICharacterController>(true);
            yield return null;
            if (!IsLive(scavAi, wolfAi))
            {
                FailureReasonId = "certification_command_ai_inactive";
                yield break;
            }

            List<ModeHCommandSpec> commands = ModeHContentCatalog.Commands;
            if (commands == null)
            {
                FailureReasonId = "certification_commands_missing";
                yield break;
            }
            for (int i = 0; i < commands.Count && !_disposed; i++)
            {
                ModeHCommandSpec spec = commands[i];
                if (spec == null || spec.Effects == null) continue;
                IEnumerator group = ProbeGroup(stableKey, spec.CommandId, spec.Effects,
                    scavAi, wolfAi, deadline);
                while (group.MoveNext()) yield return group.Current;
                if (FailureReasonId != null) yield break;
            }

            // 伤病与战痕走同一条实测路径（§17.4 line 1100 / 1118）。
            // 不测它们的话 IsEntryUsableForKey 对任何 key 恒 false：
            // 伤病永远无名（PickInjury 拿不满 3 条返回空串）、战痕永远开不出
            // （PickScarOffer 恒 scar_offer_no_candidate）——「百战留痕」这个模式名
            // 所指的养成内容一条都不落地。
            List<ModeHInjurySpec> injuries = ModeHContentCatalog.Injuries;
            if (injuries != null)
            {
                for (int i = 0; i < injuries.Count && !_disposed; i++)
                {
                    ModeHInjurySpec injury = injuries[i];
                    if (injury == null || injury.Components == null) continue;
                    IEnumerator group = ProbeGroup(stableKey, injury.InjuryId, injury.Components,
                        scavAi, wolfAi, deadline);
                    while (group.MoveNext()) yield return group.Current;
                    if (FailureReasonId != null) yield break;
                }
            }

            List<ModeHScarSpec> scars = ModeHContentCatalog.Scars;
            if (scars != null)
            {
                for (int i = 0; i < scars.Count && !_disposed; i++)
                {
                    ModeHScarSpec scar = scars[i];
                    if (scar == null || scar.Components == null) continue;
                    IEnumerator group = ProbeGroup(stableKey, scar.ScarId, scar.Components,
                        scavAi, wolfAi, deadline);
                    while (group.MoveNext()) yield return group.Current;
                    if (FailureReasonId != null) yield break;
                }
            }
        }

        /// <summary>
        /// 一组分量（一条口令 / 一条伤病 / 一条战痕）的跨帧实测。
        ///
        /// 口令与伤病战痕共用这一条路径，因为 ModeHCommandAdapter.ApplyEffects 本来就是通用入口
        /// （ownerEntryId 只是归属标签，不做任何查找；ModeHInjuryAndScarSystem.OpenWindow
        /// 生产路径传的就是 injuryId / scarId）。
        ///
        /// 失败时写 FailureReasonId 并结束，由调用方据此中止本 key。
        /// </summary>
        private IEnumerator ProbeGroup(string stableKey, string ownerEntryId,
            List<ModeHEffectSpec> effects, AICharacterController scavAi, AICharacterController wolfAi,
            float deadline)
        {
            string error = null;
            if (string.IsNullOrEmpty(ownerEntryId) || effects == null) yield break;
            if (Time.realtimeSinceStartup >= deadline)
            {
                FailureReasonId = "certification_key_timeout";
                yield break;
            }
            List<ModeHEffectSpec> measurable = new List<ModeHEffectSpec>();
            List<ModeHEffectSpec> actionable = new List<ModeHEffectSpec>();
            Dictionary<string, object> scavBefore = new Dictionary<string, object>(StringComparer.Ordinal);
            Dictionary<string, object> wolfBefore = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int j = 0; j < effects.Count; j++)
            {
                ModeHEffectSpec effect = effects[j];
                if (effect == null || effect.SelfSettled) continue;
                // 没有目标/路径/技能释放遥测的点火和 marker 保留 ReportOnly。
                ModeHCommandCompatibilityRegistry.RecordEffectStatus(stableKey, effect.EffectId,
                    ModeHCommandCompatibilityStatus.ReportOnly);
                // 动作型控制点走独立路径：它们是一次性动作，原版每帧自行改写，
                // 「字段仍等于我写进去的值」这条判据对它们没有意义，
                // 塞进 ReadField 会把「口令没生效」标成 VerifiedBehavior
                // （tests/ModeHCommandCompatibilityGuard.py 明令禁止，那条禁令是对的）。
                // 它们的验证标准与运行时 Validate() 一致：动作的可观测后置条件。
                if (ModeHCommandCompatibilityRegistry.IsActionControlPoint(effect.ControlPointId))
                {
                    actionable.Add(effect);
                    continue;
                }

                object first = ReadField(scavAi, effect.ControlPointId);
                object second = ReadField(wolfAi, effect.ControlPointId);
                if (!effect.Restore || first == null || second == null) continue;
                measurable.Add(effect);
                scavBefore[effect.EffectId] = first;
                wolfBefore[effect.EffectId] = second;
            }

            // 动作型分量单独跑一轮：需要真实点火上下文（ApplyFire 对 null 上下文直接早返），
            // 两个探针角色互为对手，正好构成 NearestEnemy / LowestHealthEnemy。
            if (actionable.Count > 0)
            {
                yield return ProbeActionGroup(stableKey, ownerEntryId, actionable, scavAi, wolfAi, deadline);
            }

            if (measurable.Count == 0) yield break;

            HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
            bool applied = false;
            int samples = 0;
            float elapsed = 0f;
            try
            {
                applied = _scavAdapter.ApplyEffects(scavAi, ownerEntryId, measurable,
                    ModeHConfig.CommandWindowSeconds, 1f, null, out error)
                    && _wolfAdapter.ApplyEffects(wolfAi, ownerEntryId, measurable,
                        ModeHConfig.CommandWindowSeconds, 1f, null, out error);
                if (applied)
                {
                    held.UnionWith(_scavAdapter.Validate());
                    held.IntersectWith(_wolfAdapter.Validate());
                    float sampleWindow = ModeHConfig.CommandReassertIntervalSeconds * 3f;
                    while (elapsed < sampleWindow || samples < 3)
                    {
                        yield return null;
                        if (_disposed || !IsLive(scavAi, wolfAi)
                            || Time.realtimeSinceStartup >= deadline)
                        {
                            FailureReasonId = _disposed ? "certification_cancelled"
                                : "certification_command_sampling_interrupted";
                            yield break;
                        }
                        // 先采样再重申，不能每次刚写完就自证保持成功。
                        held.IntersectWith(_scavAdapter.Validate());
                        held.IntersectWith(_wolfAdapter.Validate());
                        float delta = Mathf.Max(0f, Time.deltaTime);
                        _scavAdapter.Tick(delta, null);
                        _wolfAdapter.Tick(delta, null);
                        elapsed += delta;
                        samples++;
                    }
                }
            }
            finally
            {
                _scavAdapter.Restore();
                _wolfAdapter.Restore();
            }
            if (!applied)
            {
                FailureReasonId = "certification_command_apply:" + error;
                yield break;
            }
            // 同一条目里两条分量写同一个控制点时（战痕 relay_expert 的
            // skillSuccessChance 与 starter_penalty 都写 skillSuccessChance），
            // 后写的会盖掉先写的，Validate 只可能确认最后那一条仍然保持。
            // 保持性是 (key, controlPointId) 的属性而不是 effectId 的属性：
            // 字段没被原版回写，就说明 Mode H 对该字段的写入在这条 key 上有效，
            // 同控制点的先写分量同样成立。不做这一步投影，relay_expert 会因为
            // 「自己盖自己」而对任何 key 永久不可用——口令里不存在重复控制点，
            // 所以既有探针从没遇到过这一类。
            HashSet<string> heldControlPoints = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < measurable.Count; j++)
            {
                ModeHEffectSpec effect = measurable[j];
                if (held.Contains(effect.EffectId)) heldControlPoints.Add(effect.ControlPointId);
            }

            for (int j = 0; j < measurable.Count; j++)
            {
                ModeHEffectSpec effect = measurable[j];
                bool restored = object.Equals(scavBefore[effect.EffectId], ReadField(scavAi, effect.ControlPointId))
                    && object.Equals(wolfBefore[effect.EffectId], ReadField(wolfAi, effect.ControlPointId));
                if (!restored)
                {
                    FailureReasonId = "certification_command_restore:" + effect.EffectId;
                    yield break;
                }
                if (heldControlPoints.Contains(effect.ControlPointId))
                {
                    ModeHCommandCompatibilityRegistry.RecordEffectStatus(stableKey, effect.EffectId,
                        ModeHCommandCompatibilityStatus.VerifiedBehavior);
                }
            }
        }

        private bool IsLive(AICharacterController scavAi, AICharacterController wolfAi)
        {
            return !_disposed && scavAi != null && wolfAi != null
                && scavAi.isActiveAndEnabled && wolfAi.isActiveAndEnabled
                && _scav.Health != null && !_scav.Health.IsDead
                && _wolf.Health != null && !_wolf.Health.IsDead;
        }

        /// <summary>
        /// 动作型分量的认证：施加一次，然后按**动作的后置条件**判定是否落地
        /// （adapter.Validate 对 searchedEnemy / setNoticedToTarget 检查的正是
        /// `searchedEnemy != null` 与 `noticed`，与运行时同一套标准）。
        ///
        /// 通过者记 ActionApplied 而不是 VerifiedBehavior：如实表达「动作确实生效，
        /// 但没有也不可能做字段保持验证」。这是 finish 这类纯动作口令唯一诚实的出路。
        /// </summary>
        private IEnumerator ProbeActionGroup(
            string stableKey, string ownerEntryId, List<ModeHEffectSpec> actionable,
            AICharacterController scavAi, AICharacterController wolfAi, float deadline)
        {
            string error = null;
            bool applied = false;
            HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);

            ModeHCommandFireContext scavContext = BuildProbeFireContext(_wolf);
            ModeHCommandFireContext wolfContext = BuildProbeFireContext(_scav);
            if (scavContext == null || wolfContext == null)
            {
                // 拿不到对手受击体：保持 ReportOnly，不臆断动作有效
                yield break;
            }

            try
            {
                applied = _scavAdapter.ApplyEffects(scavAi, ownerEntryId, actionable,
                    ModeHConfig.CommandWindowSeconds, 1f, scavContext, out error)
                    && _wolfAdapter.ApplyEffects(wolfAi, ownerEntryId, actionable,
                        ModeHConfig.CommandWindowSeconds, 1f, wolfContext, out error);
                if (applied)
                {
                    // 动作是一次性的：施加当帧立刻判后置条件，不做保持率采样
                    held.UnionWith(_scavAdapter.Validate());
                    held.IntersectWith(_wolfAdapter.Validate());
                }
            }
            finally
            {
                _scavAdapter.Restore();
                _wolfAdapter.Restore();
            }

            if (!applied)
            {
                FailureReasonId = "certification_command_action_apply:" + error;
                yield break;
            }

            for (int i = 0; i < actionable.Count; i++)
            {
                ModeHEffectSpec effect = actionable[i];
                if (effect == null || !held.Contains(effect.EffectId)) continue;
                ModeHCommandCompatibilityRegistry.RecordEffectStatus(stableKey, effect.EffectId,
                    ModeHCommandCompatibilityStatus.ActionApplied);
            }
            yield break;
        }

        /// <summary>用另一个探针角色当对手，拼一个够 ApplyFire 用的点火上下文。</summary>
        private static ModeHCommandFireContext BuildProbeFireContext(ModeHSpawnHandle opponent)
        {
            try
            {
                if (opponent == null || opponent.Character == null) return null;
                DamageReceiver receiver = opponent.Character.GetComponentInChildren<DamageReceiver>(true);
                if (receiver == null) return null;

                ModeHCommandFireContext context = new ModeHCommandFireContext();
                context.ArenaCenter = opponent.Character.transform.position;
                context.NearestEnemy = receiver;
                context.LowestHealthEnemy = receiver;
                context.EnemyCount = 1;
                return context;
            }
            catch (Exception)
            {
                // 拼不出上下文就让调用方保持 ReportOnly，不臆断
                return null;
            }
        }

        private static object ReadField(AICharacterController ai, string controlPointId)
        {
            if (ai == null) return null;
            switch (controlPointId)
            {
                case "skillSuccessChance": return ai.skillSuccessChance;
                case "itemSkillChance": return ai.itemSkillChance;
                case "itemSkillCoolTime": return ai.itemSkillCoolTime;
                case "sightDistance": return ai.sightDistance;
                case "sightAngle": return ai.sightAngle;
                case "combatTurnSpeed": return ai.combatTurnSpeed;
                case "patrolTurnSpeed": return ai.patrolTurnSpeed;
                case "baseReactionTime": return ai.baseReactionTime;
                case "shootCanMove": return ai.shootCanMove;
                case "skillCoolTimeRange": return ai.skillCoolTimeRange;
                default: return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scavAdapter.Restore();
            _wolfAdapter.Restore();
            if (_scav != null && _scav.Character != null) _scav.Character.gameObject.SetActive(false);
            if (_wolf != null && _wolf.Character != null) _wolf.Character.gameObject.SetActive(false);
        }
    }
}
