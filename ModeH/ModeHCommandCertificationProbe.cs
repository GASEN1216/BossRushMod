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
                if (Time.realtimeSinceStartup >= deadline)
                {
                    FailureReasonId = "certification_key_timeout";
                    yield break;
                }
                List<ModeHEffectSpec> measurable = new List<ModeHEffectSpec>();
                Dictionary<string, object> scavBefore = new Dictionary<string, object>(StringComparer.Ordinal);
                Dictionary<string, object> wolfBefore = new Dictionary<string, object>(StringComparer.Ordinal);
                for (int j = 0; j < spec.Effects.Count; j++)
                {
                    ModeHEffectSpec effect = spec.Effects[j];
                    if (effect == null || effect.SelfSettled) continue;
                    // 没有目标/路径/技能释放遥测的点火和 marker 保留 ReportOnly。
                    ModeHCommandCompatibilityRegistry.RecordEffectStatus(stableKey, effect.EffectId,
                        ModeHCommandCompatibilityStatus.ReportOnly);
                    object first = ReadField(scavAi, effect.ControlPointId);
                    object second = ReadField(wolfAi, effect.ControlPointId);
                    if (!effect.Restore || first == null || second == null) continue;
                    measurable.Add(effect);
                    scavBefore[effect.EffectId] = first;
                    wolfBefore[effect.EffectId] = second;
                }
                if (measurable.Count == 0) continue;

                HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
                bool applied = false;
                int samples = 0;
                float elapsed = 0f;
                try
                {
                    applied = _scavAdapter.ApplyEffects(scavAi, spec.CommandId, measurable,
                        ModeHConfig.CommandWindowSeconds, 1f, null, out error)
                        && _wolfAdapter.ApplyEffects(wolfAi, spec.CommandId, measurable,
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
                    if (held.Contains(effect.EffectId))
                    {
                        ModeHCommandCompatibilityRegistry.RecordEffectStatus(stableKey, effect.EffectId,
                            ModeHCommandCompatibilityStatus.VerifiedBehavior);
                    }
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
