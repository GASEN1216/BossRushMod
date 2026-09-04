using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 生产兼容性认证（设计提案 §17.2、§19.3、§25.1）。
    ///
    /// 冻结契约：
    /// - 在正式入口内、取得 arena isolation 与 spectator lease 之后运行，不是独立技术样机；
    /// - 每个 stable key 用同一审计 preset 的**两个独立 runtime clone**，一只 Teams.scav、
    ///   一只 Teams.wolf，按“逐帧创建、双方隔离、双向 Team.IsEnemy、自然索敌、受控伤害 ping、
    ///   逐只规范死亡、整批回收”固定顺序执行，因此不依赖另一个尚未认证的基准 key；
    /// - 认证角色只登记到独立 diagnostic owner，禁止生成 ModeHFighterDownToken、伤病、
    ///   战报、奖励或任何中途 Season 写入；
    /// - 每 key 15 秒、全池 180 秒上限，超时条目标 Rejected 并继续下一条；
    /// - 报告带 game/mod/content 三签名，按四签名缓存到 HallOfFame envelope；
    ///   缓存只跳过耗时的逐 key 诊断，绝不跳过两个 lease 与地图点位审计。
    /// </summary>
    internal sealed class ModeHProductionCertification
    {
        #region 状态

        private readonly Dictionary<string, ModeHPresetCertificationRecordDto> _records =
            new Dictionary<string, ModeHPresetCertificationRecordDto>(StringComparer.Ordinal);

        private readonly ModeHSpawnDiagnostics _diagnostics = new ModeHSpawnDiagnostics();

        private readonly Dictionary<string, bool> _observedDamageByKey =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _observedDeathByKey =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private ModeHProductionCertificationDto _report;
        private bool _running;
        private bool _cancelled;
        private string _lastError;
        private ModeHCommandCertificationProbe _commandProbe;
        private ModeHSpawnHandle _activeScavHandle;
        private ModeHSpawnHandle _activeWolfHandle;

        /// <summary>诊断 owner 注册表：认证角色的事件只进认证记录，不进战斗遥测。</summary>
        private static readonly HashSet<int> _diagnosticHealthIds = new HashSet<int>();

        /// <summary>
        /// 当前正在运行认证的实例。ModeHEventRouter 命中诊断注册表后只把事件转到这里，
        /// 认证结束/取消/异常都会立刻解绑，正式开战期间恒为 null。
        /// </summary>
        private static ModeHProductionCertification _activeDiagnosticSink;

        #endregion

        #region 只读

        /// <summary>当前 runtime 的认证报告（可能为 null）。</summary>
        internal ModeHProductionCertificationDto Report { get { return _report; } }

        /// <summary>认证是否正在运行。</summary>
        internal bool IsRunning { get { return _running; } }

        /// <summary>最后一次失败原因。</summary>
        internal string LastError { get { return _lastError; } }

        /// <summary>当前 runtime 是否已通过认证门槛。</summary>
        internal bool IsCurrentRuntimePassed
        {
            get { return _report != null && _report.overallPassed; }
        }

        /// <summary>正式开战前诊断事件接收端必须已解绑。</summary>
        internal static bool IsDiagnosticSinkBound
        {
            get { return _activeDiagnosticSink != null; }
        }

        /// <summary>绑定诊断事件接收端（只由认证流程自身调用）。</summary>
        internal static void BindDiagnosticSink(ModeHProductionCertification instance)
        {
            _activeDiagnosticSink = instance;
        }

        /// <summary>解绑诊断事件接收端；幂等，认证的每条退出路径都会调用。</summary>
        internal static void UnbindDiagnosticSink()
        {
            _activeDiagnosticSink = null;
        }

        /// <summary>
        /// 由 ModeHEventRouter 转来的诊断受伤事件。只写认证记录，
        /// 绝不生成 ModeHFighterDownToken、伤病、战报或奖励。
        /// </summary>
        internal static void NotifyDiagnosticHurt(string stableKey, float damageValue)
        {
            ModeHProductionCertification sink = _activeDiagnosticSink;
            if (sink == null || string.IsNullOrEmpty(stableKey)) return;
            sink._diagnostics.DamageEventCount++;
            sink._observedDamageByKey[stableKey] = true;
        }

        /// <summary>由 ModeHEventRouter 转来的诊断死亡事件。同样只写认证记录。</summary>
        internal static void NotifyDiagnosticDead(string stableKey)
        {
            ModeHProductionCertification sink = _activeDiagnosticSink;
            if (sink == null || string.IsNullOrEmpty(stableKey)) return;
            sink._diagnostics.DeathEventCount++;
            sink._observedDeathByKey[stableKey] = true;
        }

        /// <summary>该 key 在本次认证中是否观察到双向伤害。</summary>
        internal bool HasObservedDamage(string stableKey)
        {
            bool value;
            return !string.IsNullOrEmpty(stableKey)
                && _observedDamageByKey.TryGetValue(stableKey, out value) && value;
        }

        /// <summary>该 key 在本次认证中是否观察到规范死亡。</summary>
        internal bool HasObservedDeath(string stableKey)
        {
            bool value;
            return !string.IsNullOrEmpty(stableKey)
                && _observedDeathByKey.TryGetValue(stableKey, out value) && value;
        }

        /// <summary>正式开战前 diagnostic registry 必须为空。</summary>
        internal static bool IsDiagnosticRegistryEmpty
        {
            get { return _diagnosticHealthIds.Count == 0; }
        }

        /// <summary>该 Health 是否属于认证诊断批次（查询优先于战斗 participant registry）。</summary>
        internal static bool IsDiagnosticHealth(Health health)
        {
            if (health == null) return false;
            try
            {
                return _diagnosticHealthIds.Contains(health.GetInstanceID());
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 缓存

        /// <summary>
        /// 按 (gameBuildSignature, modBuildSignature, contentCatalogSignature, slotGeneration)
        /// 四元组命中缓存；命中且 overallPassed=true 时可跳过逐 key 诊断。
        /// </summary>
        internal bool TryUseCachedReport(int slotGeneration)
        {
            string game;
            string mod;
            string error;
            if (!ModeHCanonicalDigest.TryGetGameBuildSignature(out game, out error)) return false;
            if (!ModeHCanonicalDigest.TryGetModBuildSignature(out mod, out error)) return false;
            string content = ModeHContentCatalog.ContentCatalogSignature;
            if (string.IsNullOrEmpty(content)) return false;

            ModeHProductionCertificationDto cached =
                ModeHHallOfFamePersistence.TryGetCertificationCache(game, mod, content, slotGeneration);
            if (cached == null) return false;

            _report = cached;
            ApplyReportToRegistries(cached);
            if (!EvaluateThreshold(cached.passedStableKeys))
            {
                ModBehaviour.DevLog("[ModeH] 认证缓存口令恢复未通过: " + _lastError);
                _report = null;
                return false;
            }
            ModBehaviour.DevLog("[ModeH] 生产认证缓存命中，跳过逐 key 诊断");
            return true;
        }

        /// <summary>作废缓存（诊断页“强制重新认证”）。</summary>
        internal static bool InvalidateCache(out string error)
        {
            return ModeHSaveFlushCoordinator.RequestCertificationCacheInvalidate(out error);
        }

        #endregion

        #region 认证主流程

        /// <summary>
        /// 逐 key 运行认证。协程形式，受每 key 15 秒 / 全池 180 秒上限约束。
        /// </summary>
        internal IEnumerator Run(
            IList<string> stableKeys,
            ModeHSupportedMap map,
            ModeHCertificationResult result)
        {
            if (result == null) yield break;
            result.Completed = false;
            result.Passed = false;

            if (stableKeys == null || stableKeys.Count == 0 || map == null)
            {
                result.FailureReasonId = "certification_input_invalid";
                yield break;
            }

            _running = true;
            _cancelled = false;
            _records.Clear();
            _diagnosticHealthIds.Clear();
            _observedDamageByKey.Clear();
            _observedDeathByKey.Clear();
            _lastError = null;
            string gameSignature;
            string modSignature;
            string signatureError;
            if (!ModeHCommandCompatibilityRegistry.EnsureValidated()
                || !ModeHCanonicalDigest.TryGetGameBuildSignature(out gameSignature, out signatureError)
                || !ModeHCanonicalDigest.TryGetModBuildSignature(out modSignature, out signatureError))
            {
                _running = false;
                result.Completed = true;
                result.FailureReasonId = "certification_command_registry_or_signature_unavailable";
                yield break;
            }
            // 先绑定构建，再测量；若在 BuildReport 之后首次绑定，会清掉刚得到的效果证据。
            ModeHCommandCompatibilityRegistry.BindBuildSignature(
                gameSignature, modSignature, ModeHContentCatalog.ContentCatalogSignature);
            BindDiagnosticSink(this);

            float poolDeadline = Time.realtimeSinceStartup + ModeHConfig.CertificationPoolTimeoutSeconds;

            for (int i = 0; i < stableKeys.Count; i++)
            {
                if (_cancelled)
                {
                    result.FailureReasonId = "certification_cancelled";
                    break;
                }
                if (Time.realtimeSinceStartup >= poolDeadline)
                {
                    // 全池超时：剩余条目一律标 Rejected，不静默通过
                    for (int j = i; j < stableKeys.Count; j++)
                    {
                        RecordRejected(stableKeys[j], "certification_pool_timeout", 0);
                    }
                    break;
                }

                string stableKey = stableKeys[i];
                ModeHCertificationKeyResult keyResult = new ModeHCertificationKeyResult();
                yield return CertifyKey(stableKey, map, poolDeadline, keyResult);
                if (!keyResult.Passed)
                {
                    ModBehaviour.DevLog("[ModeH] 认证拒绝 " + stableKey + ": " + keyResult.FailureReasonId);
                }
            }

            _running = false;
            // 认证结束的每条退出路径都必须先解绑诊断接收端：正式开战期间不得有诊断路由
            UnbindDiagnosticSink();
            _report = BuildReport();
            ApplyReportToRegistries(_report);

            result.Completed = true;
            result.Passed = _report != null && _report.overallPassed;
            result.Report = _report;
            if (!result.Passed && string.IsNullOrEmpty(result.FailureReasonId))
            {
                result.FailureReasonId = _lastError ?? "certification_threshold_not_met";
            }
            ModBehaviour.DevLog("[ModeH] 认证汇总 records=" + _report.records.Count
                + ",passed=" + _report.passedStableKeys.Count + ",common="
                + _report.commonVerifiedCommandIds.Count + ",overall=" + result.Passed
                + ",reason=" + (result.FailureReasonId ?? "none"));
            yield break;
        }

        /// <summary>取消当前认证批次（离场、技术中止、场景切换）。</summary>
        internal void Cancel()
        {
            _cancelled = true;
            _running = false;
            ReleaseDiagnosticPair();
            _diagnosticHealthIds.Clear();
            UnbindDiagnosticSink();
        }

        private IEnumerator CertifyKey(
            string stableKey, ModeHSupportedMap map, float poolDeadline, ModeHCertificationKeyResult result)
        {
            float keyStart = Time.realtimeSinceStartup;
            ModeHCommandCompatibilityRegistry.ClearStableKey(stableKey);
            float keyDeadline = Mathf.Min(
                keyStart + ModeHConfig.CertificationPerKeyTimeoutSeconds, poolDeadline);

            CharacterRandomPreset audited = ResolveAuditedPreset(stableKey);
            if (audited == null)
            {
                RecordRejected(stableKey, "certification_preset_unavailable", ElapsedMs(keyStart));
                result.FailureReasonId = "certification_preset_unavailable";
                yield break;
            }

            string auditFailure;
            if (!PassesStaticAudit(audited, out auditFailure))
            {
                RecordRejected(stableKey, auditFailure, ElapsedMs(keyStart));
                result.FailureReasonId = auditFailure;
                yield break;
            }

            ModeHSpawnHandle scavHandle = null;
            ModeHSpawnHandle wolfHandle = null;
            string failure = null;

            // 逐帧创建两个独立 clone：一只 scav、一只 wolf
            Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> scavTask =
                ModeHSpawnBridge.CreateIsolatedAsync(audited, stableKey, Teams.scav, map.StagingPos, _diagnostics);
            while (scavTask.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
            {
                if (Time.realtimeSinceStartup >= keyDeadline) break;
                yield return null;
            }
            try { scavHandle = scavTask.GetAwaiter().GetResult(); }
            catch (Exception e) { failure = "certification_scav_create:" + e.GetType().Name; }
            _activeScavHandle = scavHandle;

            if (failure == null && scavHandle == null) failure = "certification_scav_create_null";
            if (failure == null && _diagnostics.HasWindowSideEffects())
            {
                failure = "certification_scav_window_side_effect";
            }

            if (failure == null)
            {
                yield return null;
                Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> wolfTask =
                    ModeHSpawnBridge.CreateIsolatedAsync(audited, stableKey, Teams.wolf, map.StagingPos, _diagnostics);
                while (wolfTask.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
                {
                    if (Time.realtimeSinceStartup >= keyDeadline) break;
                    yield return null;
                }
                try { wolfHandle = wolfTask.GetAwaiter().GetResult(); }
                catch (Exception e) { failure = "certification_wolf_create:" + e.GetType().Name; }
                _activeWolfHandle = wolfHandle;

                if (failure == null && wolfHandle == null) failure = "certification_wolf_create_null";
                if (failure == null && _diagnostics.HasWindowSideEffects())
                {
                    failure = "certification_wolf_window_side_effect";
                }
            }

            // 登记诊断 owner：这些角色的事件只进认证记录
            if (failure == null)
            {
                RegisterDiagnostic(scavHandle);
                RegisterDiagnostic(wolfHandle);
            }

            // 下一帧核对阵营稳定与双向敌对
            if (failure == null)
            {
                yield return null;
                if (scavHandle.Character == null || wolfHandle.Character == null)
                {
                    failure = "certification_handle_lost";
                }
                else if (scavHandle.Character.Team != Teams.scav || wolfHandle.Character.Team != Teams.wolf)
                {
                    failure = "certification_team_drift";
                }
                else if (!Team.IsEnemy(Teams.scav, Teams.wolf) || !Team.IsEnemy(Teams.wolf, Teams.scav))
                {
                    failure = "certification_team_enemy_rule";
                }
            }

            // 两只诊断角色在真实 AI 更新中采样口令，完成还原后才执行受控死亡。
            if (failure == null)
            {
                _commandProbe = new ModeHCommandCertificationProbe(scavHandle, wolfHandle);
                IEnumerator probe = _commandProbe.Run(stableKey, map, keyDeadline);
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = probe.MoveNext(); }
                        catch (Exception e)
                        {
                            failure = "certification_command_exception:" + e.GetType().Name;
                            ModBehaviour.DevLog("[ModeH] [ERROR] 口令认证失败 " + stableKey + "\n" + e);
                            break;
                        }
                        if (!more) break;
                        yield return probe.Current;
                    }
                    if (failure == null) failure = _cancelled ? "certification_cancelled"
                        : (_commandProbe != null ? _commandProbe.FailureReasonId : "certification_command_probe_lost");
                }
                finally
                {
                    IDisposable disposable = probe as IDisposable;
                    if (disposable != null) disposable.Dispose();
                    if (_commandProbe != null) _commandProbe.Dispose();
                    _commandProbe = null;
                }
            }

            // 受控伤害 ping + 规范死亡（无战利品）
            if (failure == null)
            {
                if (!TryControlledKill(wolfHandle, scavHandle.Character, out failure)
                    || !TryControlledKill(scavHandle, wolfHandle.Character, out failure))
                {
                    // failure 已填
                }
                else
                {
                    yield return null;
                }
            }

            int durationMs = ElapsedMs(keyStart);
            if (failure == null && Time.realtimeSinceStartup >= keyDeadline)
            {
                failure = "certification_key_timeout";
            }

            // 整批回收
            ReleaseDiagnosticPair();

            if (failure != null)
            {
                RecordRejected(stableKey, failure, durationMs);
                result.FailureReasonId = failure;
                yield break;
            }

            RecordPassed(stableKey, durationMs);
            result.Passed = true;
        }

        private bool TryControlledKill(ModeHSpawnHandle handle, CharacterMainControl attacker,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (handle == null || handle.Character == null || handle.Health == null)
            {
                failureReasonId = "certification_kill_handle_invalid";
                return false;
            }
            if (attacker == null || attacker == handle.Character || attacker.IsMainCharacter)
            {
                failureReasonId = "certification_attacker_invalid";
                return false;
            }
            // 官方字段是“非 Raid 地图是否允许死亡”，不是预设是否能死亡。
            // SpawnBridge 已只在独立 clone 上打开它；认证应验证实际生成角色。
            if (!handle.Health.CanDieIfNotRaidMap)
            {
                failureReasonId = "audit_cannot_die";
                return false;
            }
            if (handle.Health.OnHurtEvent == null || handle.Health.OnDeadEvent == null)
            {
                failureReasonId = "certification_health_events_missing";
                return false;
            }
            bool observedHurt = false;
            bool observedDeath = false;
            UnityEngine.Events.UnityAction<DamageInfo> onHurt = delegate(DamageInfo info)
            {
                observedHurt = true;
                NotifyDiagnosticHurt(handle.StableKey, info.damageValue);
            };
            UnityEngine.Events.UnityAction<DamageInfo> onDead = delegate(DamageInfo info)
            {
                observedDeath = true;
                NotifyDiagnosticDead(handle.StableKey);
            };
            try
            {
                // 认证早于战斗 router 绑定，临时监听当前 Health，finally 内立刻退订。
                handle.Health.OnHurtEvent.AddListener(onHurt);
                handle.Health.OnDeadEvent.AddListener(onDead);
                // SetHealth 只改数值，不触发死亡。必须走 Hurt 并实际观察两个事件及 IsDead。
                handle.Health.SetInvincible(false);
                // 使用对侧诊断 clone 作为真实 NPC 来源：死亡订阅者可能直接读取来源角色。
                // 不用主玩家代替，避免把认证记为玩家击杀、触发经验/击杀提示等奖励副作用。
                DamageInfo damage = new DamageInfo(attacker);
                damage.damageValue = Mathf.Max(1f, handle.Health.MaxHealth * 10f);
                damage.ignoreArmor = true;
                damage.toDamageReceiver = handle.Character.mainDamageReceiver;
                damage.damagePoint = handle.Character.transform.position;
                handle.Health.Hurt(damage);
                if (handle.Health.IsDead && observedHurt && observedDeath) return true;
                failureReasonId = "certification_death_not_observed";
                return false;
            }
            catch (Exception e)
            {
                failureReasonId = "certification_kill_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[ModeH] [ERROR] 认证伤害链失败 key=" + handle.StableKey
                    + ",team=" + handle.Team + ",hurt=" + observedHurt + ",death=" + observedDeath
                    + "\n" + e);
                return false;
            }
            finally
            {
                handle.Health.OnHurtEvent.RemoveListener(onHurt);
                handle.Health.OnDeadEvent.RemoveListener(onDead);
            }
        }

        #endregion

        #region 审计与记录

        /// <summary>按 stable key 回查原版审计 preset。</summary>
        internal static CharacterRandomPreset ResolveAuditedPreset(string stableKey)
        {
            if (string.IsNullOrEmpty(stableKey)) return null;
            try
            {
                CharacterRandomPreset[] presets = ObjectCache.GetCharacterPresets();
                if (presets == null) return null;
                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset == null) continue;
                    if (string.Equals(preset.nameKey, stableKey, StringComparison.Ordinal)) return preset;
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        /// <summary>
        /// 静态资格审计（§17.2）：EnemyPresetInfo 字段有限，不能单独作为资格判断，
        /// 必须回查原版 CharacterRandomPreset。
        /// </summary>
        internal static bool PassesStaticAudit(CharacterRandomPreset preset, out string failureReasonId)
        {
            failureReasonId = null;
            if (preset == null)
            {
                failureReasonId = "audit_preset_null";
                return false;
            }
            try
            {
                if (!preset.isBoss)
                {
                    failureReasonId = "audit_not_boss";
                    return false;
                }
                if (preset.team == Teams.player || preset.team == Teams.middle)
                {
                    failureReasonId = "audit_team_forbidden";
                    return false;
                }
                if (preset.isVehicle)
                {
                    failureReasonId = "audit_is_vehicle";
                    return false;
                }
                if (!preset.showName)
                {
                    failureReasonId = "audit_no_show_name";
                    return false;
                }
                // canDieIfNotRaidMap 在 SpawnBridge 的独立 clone 上归一化，
                // 可死亡性由 TryControlledKill 对真实 Health 与死亡事件验证。
                if (preset.specialAttachmentBases != null && preset.specialAttachmentBases.Count > 0)
                {
                    failureReasonId = "audit_special_attachments";
                    return false;
                }
                if (ModeHProfileRegistry.IsExcludedStableKey(preset.nameKey))
                {
                    failureReasonId = "audit_excluded_key";
                    return false;
                }
                if (string.IsNullOrEmpty(preset.nameKey) || preset.nameKey.Contains("(Clone)"))
                {
                    failureReasonId = "audit_invalid_key";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "audit_exception:" + e.GetType().Name;
                return false;
            }
        }

        private void RegisterDiagnostic(ModeHSpawnHandle handle)
        {
            if (handle == null || handle.Health == null) return;
            try { _diagnosticHealthIds.Add(handle.Health.GetInstanceID()); }
            catch (Exception)
            {
                // 登记失败时该角色的事件会落到普通过滤路径，仍不会进入战斗结算
            }
        }

        private void ReleaseDiagnosticPair()
        {
            if (_commandProbe != null) _commandProbe.Dispose();
            _commandProbe = null;
            UnregisterDiagnostic(_activeScavHandle);
            UnregisterDiagnostic(_activeWolfHandle);
            ModeHSpawnBridge.Recycle(_activeWolfHandle);
            ModeHSpawnBridge.Recycle(_activeScavHandle);
            _activeScavHandle = null;
            _activeWolfHandle = null;
        }

        private void UnregisterDiagnostic(ModeHSpawnHandle handle)
        {
            if (handle == null || handle.Health == null) return;
            try { _diagnosticHealthIds.Remove(handle.Health.GetInstanceID()); }
            catch (Exception)
            {
                // 同上
            }
        }

        private static int ElapsedMs(float start)
        {
            return Mathf.Max(0, Mathf.RoundToInt((Time.realtimeSinceStartup - start) * 1000f));
        }

        private void RecordPassed(string stableKey, int durationMs)
        {
            ModeHPresetCertificationRecordDto record = new ModeHPresetCertificationRecordDto();
            record.stableKey = stableKey;
            record.status = (int)ModeHCertificationStatus.Passed;
            record.failureReasonIds = new List<string>();
            record.commandStatuses = BuildCommandStatuses(stableKey);
            record.spawnTimelineDigest = string.Empty;
            record.durationMs = durationMs;
            _records[stableKey] = record;
            // 伤病门（§17.5「可用伤病少于 3 条」）**只进诊断日志，永不阻断入场**：
            // 对玩家来说「这条 key 可用伤病不足 3 条」不是可行动信息，把它做成入场拒绝
            // 只会在某些机型/官方版本下把整个模式关在门外（owner 裁决 2026-09-03）。
            // durationMs 一并打出来，是判断认证预算是否还够用的实机输入。
            ModBehaviour.DevLog("[ModeH] 认证通过 " + stableKey + ",commands="
                + ModeHCommandCompatibilityRegistry.CountUsableCommonCommands(stableKey)
                + ",injuries=" + ModeHInjuryAndScarSystem.GetUsableInjuryIds(stableKey).Count
                + ",injuryGate=" + ModeHInjuryAndScarSystem.MeetsInjuryGate(stableKey)
                + ",ms=" + durationMs);
        }

        private void RecordRejected(string stableKey, string failureReasonId, int durationMs)
        {
            ModeHPresetCertificationRecordDto record = new ModeHPresetCertificationRecordDto();
            record.stableKey = stableKey;
            record.status = (int)ModeHCertificationStatus.Rejected;
            record.failureReasonIds = new List<string>();
            if (!string.IsNullOrEmpty(failureReasonId)) record.failureReasonIds.Add(failureReasonId);
            List<string> extra = _diagnostics.GetFailures(stableKey);
            for (int i = 0; i < extra.Count; i++)
            {
                if (!record.failureReasonIds.Contains(extra[i])) record.failureReasonIds.Add(extra[i]);
            }
            record.commandStatuses = new List<ModeHCommandCertificationStatusDto>();
            record.spawnTimelineDigest = string.Empty;
            record.durationMs = durationMs;
            _records[stableKey] = record;
        }

        /// <summary>
        /// 落盘的逐条目实测证据。**口令、伤病与战痕三族都要进**：
        /// 缓存往返是 BuildReport -> 名人堂四签名缓存 -> TryUseCachedReport ->
        /// RestoreCertificationEffects，任何没进这张表的条目，在缓存命中的那一局
        /// 就等于从没测过——伤病重新变无名、战痕重新无候选，而口令层看起来毫无异常。
        /// 这是本子系统最难查的一类回归，务必与 RestoreCertificationEffects 成对修改。
        /// </summary>
        private List<ModeHCommandCertificationStatusDto> BuildCommandStatuses(string stableKey)
        {
            List<ModeHCommandCertificationStatusDto> statuses =
                new List<ModeHCommandCertificationStatusDto>();
            List<ModeHCommandSpec> commands = ModeHContentCatalog.Commands;
            for (int i = 0; commands != null && i < commands.Count; i++)
            {
                AppendEntryStatus(statuses, stableKey, commands[i].CommandId);
            }

            // 伤病与战痕：dto.commandId 承载条目 ID（见 ModeHStateDtos 的字段注释）。
            // 不新增 DTO 字段是刻意的——ModeHBehaviorStatusDto / ModeHCommandCertificationStatusDto
            // 都进 canonical digest，加字段会让所有已存赛季与名人堂信封 VerifyDigest 失败。
            List<string> behaviorEntryIds = ModeHCommandCompatibilityRegistry.GetBehaviorEntryIds();
            for (int i = 0; i < behaviorEntryIds.Count; i++)
            {
                AppendEntryStatus(statuses, stableKey, behaviorEntryIds[i]);
            }
            return statuses;
        }

        /// <summary>追加一条条目级证据（含其逐分量状态）。口令与伤病 / 战痕同构。</summary>
        private void AppendEntryStatus(
            List<ModeHCommandCertificationStatusDto> statuses, string stableKey, string entryId)
        {
            if (statuses == null || string.IsNullOrEmpty(entryId)) return;
            ModeHCommandCertificationStatusDto dto = new ModeHCommandCertificationStatusDto();
            dto.commandId = entryId;
            dto.status = (int)ModeHCommandCompatibilityRegistry.GetBehaviorEntryStatus(stableKey, entryId);
            dto.effectStatuses = new List<ModeHBehaviorStatusDto>();
            List<string> effectIds = ModeHCommandCompatibilityRegistry.GetBehaviorEffectIds(entryId);
            if (effectIds != null)
            {
                for (int j = 0; j < effectIds.Count; j++)
                {
                    ModeHBehaviorStatusDto effect = new ModeHBehaviorStatusDto();
                    effect.entryId = effectIds[j];
                    effect.entryKind = "effect";
                    effect.status = (int)ModeHCommandCompatibilityRegistry.GetEffectStatus(
                        stableKey, effectIds[j]);
                    dto.effectStatuses.Add(effect);
                }
            }
            statuses.Add(dto);
        }

        private ModeHProductionCertificationDto BuildReport()
        {
            ModeHProductionCertificationDto report = new ModeHProductionCertificationDto();
            report.certificationSchemaVersion = ModeHConfig.CurrentCertificationSchemaVersion;

            string game;
            string mod;
            string error;
            ModeHCanonicalDigest.TryGetGameBuildSignature(out game, out error);
            ModeHCanonicalDigest.TryGetModBuildSignature(out mod, out error);
            report.gameBuildSignature = game;
            report.modBuildSignature = mod;
            report.contentCatalogSignature = ModeHContentCatalog.ContentCatalogSignature;
            report.completedUtc = DateTime.UtcNow.ToString("O");

            List<ModeHPresetCertificationRecordDto> records =
                new List<ModeHPresetCertificationRecordDto>(_records.Values);
            records.Sort(delegate (ModeHPresetCertificationRecordDto a, ModeHPresetCertificationRecordDto b)
            {
                return string.CompareOrdinal(a.stableKey, b.stableKey);
            });
            report.records = records;

            List<string> passed = new List<string>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].status == (int)ModeHCertificationStatus.Passed)
                {
                    passed.Add(records[i].stableKey);
                }
            }
            passed.Sort(StringComparer.Ordinal);
            report.passedStableKeys = passed;

            // 全池均可用的通用口令
            List<string> commonVerified = new List<string>();
            for (int i = 0; i < ModeHStableIds.AllCommonCommands.Length; i++)
            {
                string commandId = ModeHStableIds.AllCommonCommands[i];
                bool allUsable = passed.Count > 0;
                for (int j = 0; j < passed.Count; j++)
                {
                    if (!ModeHCommandCompatibilityRegistry.IsCommandSelectable(passed[j], commandId))
                    {
                        allUsable = false;
                        break;
                    }
                }
                if (allUsable) commonVerified.Add(commandId);
            }
            commonVerified.Sort(StringComparer.Ordinal);
            report.commonVerifiedCommandIds = commonVerified;

            report.overallPassed = EvaluateThreshold(passed);
            return report;
        }

        /// <summary>
        /// 门槛：至少 8 个 key 通过、五种原型可覆盖，且每个通过 key 至少 3 条可用通用口令。
        /// </summary>
        private bool EvaluateThreshold(List<string> passedKeys)
        {
            if (passedKeys == null || passedKeys.Count < ModeHConfig.MinProductionCandidateCount)
            {
                _lastError = "certification_passed_below_min";
                return false;
            }

            HashSet<string> archetypes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < passedKeys.Count; i++)
            {
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(passedKeys[i]);
                if (template != null) archetypes.Add(template.ArchetypeId);

                if (!ModeHCommandCompatibilityRegistry.MeetsCommandGate(passedKeys[i]))
                {
                    _lastError = "certification_command_gate:" + passedKeys[i];
                    return false;
                }
            }
            if (archetypes.Count < ModeHConfig.RequiredArchetypeCoverage)
            {
                _lastError = "certification_archetype_coverage";
                return false;
            }

            _lastError = null;
            return true;
        }

        private void ApplyReportToRegistries(ModeHProductionCertificationDto report)
        {
            if (report == null || !ModeHCommandCompatibilityRegistry.EnsureValidated()) return;
            ModeHCommandCompatibilityRegistry.BindBuildSignature(
                report.gameBuildSignature, report.modBuildSignature, report.contentCatalogSignature);
            ModeHCommandCompatibilityRegistry.RestoreCertificationEffects(report.records);
            ModeHPresetRegistry.MaterializeFromReport(report);
        }

        #endregion
    }

    /// <summary>认证批次结果（协程载体）。</summary>
    internal sealed class ModeHCertificationResult
    {
        /// <summary>是否跑完。</summary>
        public bool Completed;
        /// <summary>是否达到门槛。</summary>
        public bool Passed;
        /// <summary>失败原因。</summary>
        public string FailureReasonId;
        /// <summary>本次报告。</summary>
        public ModeHProductionCertificationDto Report;
    }

    /// <summary>单个 key 的认证结果（协程载体）。</summary>
    internal sealed class ModeHCertificationKeyResult
    {
        /// <summary>是否通过。</summary>
        public bool Passed;
        /// <summary>失败原因。</summary>
        public string FailureReasonId;
    }
}
