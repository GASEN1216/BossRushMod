using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode G Boss 池 run-scoped 快照（Starting 一次构建）。
    /// infoByKey：official key（EnemyPresetInfo.name）与托管 key（managed_*）-> preset；
    /// officialKeys：排除托管三 Boss Legacy preset 后的 Ordinal 升序 key 表。
    /// </summary>
    internal sealed class ModeGBossSnapshot
    {
        internal readonly Dictionary<string, EnemyPresetInfo> infoByKey =
            new Dictionary<string, EnemyPresetInfo>(StringComparer.Ordinal);
        internal readonly List<string> officialKeys = new List<string>();
    }

    /// <summary>
    /// Mode G 运行时模块（规格 §4/§13/§14/§16 重写版）。
    ///
    /// 跨任务契约：
    /// - 继承 BossRushRuntimeModuleBase，可被 Register(new ModeGRuntimeModule())（任务 #7 接线）；
    /// - legacy-shaped API（Initialize/StartRun/Update/State/Dispose）供 ModeGEntry 驱动；
    /// - OnUpdate/Entry.Update 双驱动帧去重守卫；
    /// - Initialize 中赋值 ModBehaviour.ManagedBossSpawnDispatcher（托管 Boss 分流），End/Dispose 复位；
    /// - 九波编排：Spawning 串行逐槽（每槽最多 2 次尝试）-> Fighting -> Last Stand /
    ///   WaveSettling -> Intermission -> 下一波；第 9 波清 -> DeathRouting.HandleVictory。
    /// </summary>
    internal sealed partial class ModeGRuntimeModule : BossRushRuntimeModuleBase, IDisposable
    {
        #region Module Identity

        public override string ModuleName { get { return "ModeG"; } }

        /// <summary>同进程已出现 plan fingerprint（Reroll 判重）</summary>
        private static readonly HashSet<string> SeenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Fields

        private ModBehaviour _host;
        private ModeGRunState _state;
        private ModeGEntryPreview _preview;
        private ModeGBossSnapshot _snapshot;
        private ModeGWavePlan _wavePlan;
        private ModeGCombatTelemetry _telemetry;
        private ModeGSpawnTransaction _spawnTransaction;
        private ModeGAdaptiveCombat _adaptive;

        private readonly List<ManagedBossRuntimeHandle> _managedHandles = new List<ManagedBossRuntimeHandle>(4);
        // 托管辅助单位（女巫随从等）激活前原子提交登记（规格 §20 第 16 条；End 统一清空）
        private readonly HashSet<CharacterMainControl> _committedAuxiliaries = new HashSet<CharacterMainControl>();
        private readonly List<Health> _healthScratch = new List<Health>(3);
        private readonly List<CharacterMainControl> _characterScratch = new List<CharacterMainControl>(3);

        private Func<EnemyPresetInfo, Vector3, object, bool, UniTask<ManagedBossPrepareResult>> _dispatcherRef;
        private bool _initialized;
        private bool _disposed;
        private bool _ended;
        private bool _waveSpawnInFlight;
        private bool _waveSettlementPending;
        private bool _playerDeadSubscribed;
        private bool _firstWaveCombatStarted;
        private bool _startupTicketRefundable;
        private bool _startupRelicRefundable;
        private bool _startupRefundSettled;
        private bool _batchActivationInProgress;
        private int _lastDrivenFrame = -1;

        // 遥测/契约统计
        private int _totalBossKills;
        private int _lastStandCount;
        private int _axisBreakChain;
        private int _maxAxisBreakChain;
        private int _ammoBanCount;
        private int _attributeLockCount;
        private int _distanceEchoCount;
        private int _ammoBanNemesisWaveCount;
        private bool _nemesisR3FinalBlowDirect;
        private bool _nemesisDefeatedThisRun;
        private string _runNemesisKey; // 本局出场宿敌 key（可能与持久化记录不同）
        private readonly int[] _resolvesPerAct = new int[3];

        // 三轴本局尝试计数（recap near-miss 呈现用；尝试=该轴反制生效的波数）
        private int _axisAttemptDistance;
        private int _axisAttemptAmmo;
        private int _axisAttemptAttribute;

        #endregion

        #region Public API（DeathRouting / HUD / SpawnTransaction 消费）

        public ModeGRunState State { get { return _state; } }
        public ModBehaviour Host { get { return _host; } }
        public ModeGEntryPreview Preview { get { return _preview; } }
        public ModeGWavePlan WavePlan { get { return _wavePlan; } }
        public ModeGCombatTelemetry Telemetry { get { return _telemetry; } }
        public ModeGSpawnTransaction SpawnTransaction { get { return _spawnTransaction; } }
        public ModeGAdaptiveCombat Adaptive { get { return _adaptive; } }
        public int TotalBossKills { get { return _totalBossKills; } }
        public bool IsNemesisDefeatedThisRun { get { return _nemesisDefeatedThisRun; } }

        public void ArmStartupRefund(bool refundTicket, bool refundRelic)
        {
            _startupTicketRefundable = refundTicket;
            _startupRelicRefundable = refundRelic;
            _startupRefundSettled = false;
        }

        public void DisarmStartupRefund()
        {
            _startupRefundSettled = true;
            _startupTicketRefundable = false;
            _startupRelicRefundable = false;
        }

        // 三轴本局尝试计数（recap 呈现层只读消费）
        public int AxisAttemptDistance { get { return _axisAttemptDistance; } }
        public int AxisAttemptAmmo { get { return _axisAttemptAmmo; } }
        public int AxisAttemptAttribute { get { return _axisAttemptAttribute; } }

        public float RunElapsedSeconds
        {
            get
            {
                if (_state == null) return 0f;
                return (float)((DateTime.UtcNow.Ticks - _state.runTimestampTicks) / (double)TimeSpan.TicksPerSecond);
            }
        }

        /// <summary>
        /// 已登记 Boss Character 的冻结 preset key 查询（宿敌归因用）。
        /// </summary>
        public bool TryGetRegisteredBossPresetKey(CharacterMainControl character, out string presetKey)
        {
            presetKey = null;
            if (_spawnTransaction == null) return false;
            return _spawnTransaction.TryGetCommittedKey(character, out presetKey);
        }

        /// <summary>
        /// 登记托管 Boss handle（Dispatcher 成功激活后调用；End 统一清理）。
        /// </summary>
        public void RegisterManagedHandle(ManagedBossRuntimeHandle handle)
        {
            if (handle == null) return;
            lock (_managedHandles)
            {
                _managedHandles.Add(handle);
            }
        }

        /// <summary>
        /// 组装契约进度快照（终局评估用）。
        /// </summary>
        public ModeGContractProgress BuildContractProgress()
        {
            ModeGContractProgress p = new ModeGContractProgress();
            if (_adaptive != null)
            {
                p.distanceResolves = _adaptive.ResolveDistance;
                p.ammoResolves = _adaptive.ResolveAmmo;
                p.attributeResolves = _adaptive.ResolveAttribute;
            }
            p.lastStandCount = _lastStandCount;
            p.nemesisR3FinalBlowDirect = _nemesisR3FinalBlowDirect;
            p.maxConsecutiveAxisBreaks = _maxAxisBreakChain;
            p.resolvesPerAct = (int[])_resolvesPerAct.Clone();
            p.distanceEchoCount = _distanceEchoCount;
            p.ammoBanCount = _ammoBanCount;
            p.attributeLockCount = _attributeLockCount;
            p.ammoBanAvailableOnNemesisWaves = _ammoBanNemesisWaveCount;
            return p;
        }

        #endregion

        #region Initialize / StartRun

        /// <summary>
        /// Starting 阶段一次性初始化：Boss 池快照、九波计划、遥测/事务/自适应、双 nonce、
        /// ManagedBossSpawnDispatcher 赋值。
        /// </summary>
        public bool Initialize(ModeGRunState state, ModeGEntryPreview preview)
        {
            if (_initialized || state == null || preview == null) return false;
            try
            {
                _host = ModBehaviour.Instance;
                if (_host == null) return false;
                _state = state;
                _preview = preview;

                _snapshot = _host.CreateModeGBossSnapshot();
                if (_snapshot == null || _snapshot.officialKeys.Count == 0)
                {
                    ModBehaviour.DevLog("[ModeG] boss snapshot 构建失败（fail-closed）");
                    return false;
                }

                // 署名 key：preview 冻结轮换与快照交集（首版 eligibility 全 false 时走开发池）
                List<string> signatures = new List<string>();
                for (int i = 0; i < preview.signatureRotation.Length; i++)
                {
                    string key = preview.signatureRotation[i];
                    if (_snapshot.infoByKey.ContainsKey(key)) signatures.Add(key);
                }

                ModeGNemesisPersistence.NemesisRecordDto nemesis = ModeGNemesisPersistence.LoadOrInit();
                int temperamentId = nemesis != null ? nemesis.temperamentId : (int)ModeGNemesisTemperament.None;
                _wavePlan = ModeGWavePlan.Build(
                    state.runSeed,
                    preview.runFormat,
                    signatures,
                    _snapshot.officialKeys,
                    state.fateContractId,
                    temperamentId,
                    SeenFingerprints);
                if (_wavePlan == null)
                {
                    ModBehaviour.DevLog("[ModeG] wave plan 构建失败（署名/官方池不足，fail-closed）");
                    return false;
                }
                SeenFingerprints.Add(_wavePlan.planFingerprint);

                _telemetry = new ModeGCombatTelemetry(state, HandleBossDead);
                _spawnTransaction = new ModeGSpawnTransaction(state);
                _adaptive = new ModeGAdaptiveCombat(state);

                ModeGRewardTransaction.ResetRelicReturnGate();
                ModeGRewardTransaction.InitializeNonces(state.runSeed);

                // 托管 Boss 分流接线（任务 #7 追加契约：只赋值，不改 EnemySpawnCore.cs）
                _dispatcherRef = _host.DispatchModeGManagedBossSpawnAsync;
                ModBehaviour.ManagedBossSpawnDispatcher = _dispatcherRef;

                _initialized = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] Initialize 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 启动 run（Starting -> Active；订阅遥测；成就 session；首波生成）。
        /// </summary>
        public bool StartRun()
        {
            if (!_initialized || _disposed || _state == null) return false;
            if (_state.lifecyclePhase != ModeGLifecyclePhase.Starting) return false;
            try
            {
                try { ModeGRecapPanel.DismissActive(); } catch { /* 残留 recap 清理 no-throw */ }
                _telemetry.SubscribeDead();
                _telemetry.SubscribeCombat();
                SubscribePlayerDeath();

                _host.PrepareModeGArenaRuntime();

                if (!_state.TryAdvanceLifecycle(ModeGLifecyclePhase.Active)) return false;

                try { _host.BeginModeGAchievementSession(); } catch { /* no-throw */ }

                SpawnWaveAsync(0).Forget();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] StartRun 失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region Wave Orchestration

        private bool CanContinueRun()
        {
            return !_disposed && !_ended && _state != null && !_state.spawnLeasesInvalidated
                && (_state.IsStarting || _state.IsActive);
        }

        private async UniTaskVoid SpawnWaveAsync(int waveIndex)
        {
            if (_waveSpawnInFlight || !CanContinueRun()) return;
            _waveSpawnInFlight = true;
            try
            {
                ModeGWavePlan.WaveSlot wave = _wavePlan.GetWave(waveIndex);
                if (wave == null) { End(ModeGExitReason.TechnicalIntegrityLoss); return; }

                _state.waveEpoch = waveIndex;
                _state.actIndex = wave.actIndex;
                _state.combatPhase = ModeGCombatPhase.Spawning;
                _state.intermissionActive = false;
                _state.lastStandActive = false;
                if (!_state.TrySetWaveSlotPlan(wave.bossCount))
                {
                    End(ModeGExitReason.TechnicalIntegrityLoss);
                    return;
                }

                Vector3[] positions = _host.GetModeGSpawnPositions(waveIndex, wave.bossCount, wave.variant);
                if (positions == null || positions.Length < wave.bossCount)
                {
                    End(ModeGExitReason.SpawnExhausted);
                    return;
                }

                // ---- 本波轴应用（消费上一波遥测）----
                ModeGDistanceVerdict distanceVerdict = ModeGDistanceVerdict.None;
                int armedAmmoTypeId = -1;
                ModeGCounterAxis axis = ModeGAdaptiveCombat.GetAxisForWave(waveIndex);
                int bannedAmmoThreatSharePercent = 0;
                if (axis == ModeGCounterAxis.Distance)
                {
                    distanceVerdict = ModeGAdaptiveCombat.EvaluateDistanceAxis(_telemetry);
                    if (distanceVerdict != ModeGDistanceVerdict.None)
                    {
                        _distanceEchoCount++;
                        _axisAttemptDistance++;
                    }
                }
                else if (axis == ModeGCounterAxis.Ammo)
                {
                    armedAmmoTypeId = ModeGAdaptiveCombat.SelectAmmoBan(_state.runSeed, waveIndex, _telemetry);
                    if (armedAmmoTypeId > 0)
                    {
                        _axisAttemptAmmo++;
                        if (wave.isNemesisWave) _ammoBanNemesisWaveCount++;
                        // 禁令归因占比：BeginWave 会 Clear 波级威胁缓存，必须在此捕获
                        bannedAmmoThreatSharePercent = GetAmmoThreatSharePercent(armedAmmoTypeId);
                    }
                }
                else if (axis == ModeGCounterAxis.Attribute)
                {
                    if (_adaptive.ApplyAttributeLock(CharacterMainControl.Main, _telemetry))
                    {
                        _attributeLockCount++;
                        _axisAttemptAttribute++;
                    }
                }

                // ---- 串行逐槽生成（每槽最多 2 次尝试）----
                List<ManagedBossRuntimeHandle> pendingActivationHandles
                    = new List<ManagedBossRuntimeHandle>(wave.bossCount);
                for (int slotIndex = 0; slotIndex < wave.bossCount; slotIndex++)
                {
                    bool committed = false;
                    for (int attempt = 0; attempt < ModeGSpawnTransaction.MaxAttemptsPerSlot && !committed; attempt++)
                    {
                        if (!CanContinueRun()) return;

                        string key = attempt == 0
                            ? wave.bossPresetKeys[slotIndex]
                            : _wavePlan.reserveKeys[(waveIndex + slotIndex) % _wavePlan.reserveKeys.Length];

                        ModeGSpawnTransaction.SpawnAttemptLease lease =
                            _spawnTransaction.TryAcquireLease(slotIndex, key);
                        if (lease.outcome == ModeGSlotOutcome.Exhausted) break;

                        EnemyPresetInfo info;
                        if (!_snapshot.infoByKey.TryGetValue(key, out info)) continue;

                        ManagedBossPrepareResult prepared = null;
                        if (ModeGEncounterVariation.IsManagedSignatureKey(key))
                        {
                            prepared = await SpawnManagedSlotAsync(
                                info, key, positions[slotIndex], waveIndex);
                        }
                        else
                        {
                            prepared = await _host.SpawnModeGOfficialBossAsync(
                                info, positions[slotIndex], waveIndex + 1,
                                ctx => CommitOfficialSpawn(ctx));
                        }

                        CharacterMainControl boss = prepared != null ? prepared.Character : null;
                        ManagedBossRuntimeHandle managedHandle = prepared != null ? prepared.Handle : null;

                        if (!CanContinueRun())
                        {
                            if (managedHandle != null)
                                managedHandle.CleanupOnce(ManagedBossCleanupReason.OwnerInvalid);
                            else
                                DestroyBoss(boss);
                            return;
                        }
                        if (boss == null || boss.Health == null) continue;

                        if (!_spawnTransaction.TryCommit(slotIndex, key, boss))
                        {
                            if (managedHandle != null)
                                managedHandle.CleanupOnce(ManagedBossCleanupReason.SpawnRejected);
                            else
                                DestroyBoss(boss);
                            continue;
                        }

                        if (managedHandle != null)
                        {
                            RegisterManagedHandle(managedHandle);
                            pendingActivationHandles.Add(managedHandle);
                        }

                        committed = true;
                        ApplyWaveModifiers(boss, wave, distanceVerdict);
                    }

                    if (!committed) _spawnTransaction.MarkExhausted(slotIndex);
                }

                if (!CanContinueRun()) return;

                // ---- 最低开战人数（§13.4：单 1 多 2）----
                int minimumCombatants = ModeGDeathRouting.GetMinimumStartCount(wave.bossCount);
                if (!_spawnTransaction.IsWaveSettled || _spawnTransaction.ActiveBossCount < minimumCombatants)
                {
                    ModBehaviour.DevLog("[ModeG] 波 " + (waveIndex + 1) + " 生成耗尽（低于最低开战人数）");
                    End(ModeGExitReason.SpawnExhausted);
                    return;
                }

                // 全槽结案后同帧批量激活。任一已提交 handle 激活失败都视为技术完整性丢失。
                if (!EnsureCommittedBossTeams())
                {
                    End(ModeGExitReason.TechnicalIntegrityLoss);
                    return;
                }

                _batchActivationInProgress = true;
                try
                {
                    for (int i = 0; i < pendingActivationHandles.Count; i++)
                    {
                        ManagedBossRuntimeHandle handle = pendingActivationHandles[i];
                        if (handle == null || !handle.ActivateOnce())
                        {
                            if (handle != null) handle.CleanupOnce(ManagedBossCleanupReason.TechnicalLoss);
                            End(ModeGExitReason.TechnicalIntegrityLoss);
                            return;
                        }
                    }
                }
                finally
                {
                    _batchActivationInProgress = false;
                }

                // ---- 开战：冻结聚合血量、BeginWave、武装禁令 ----
                float aggregateMaxHealth = 0f;
                _healthScratch.Clear();
                _spawnTransaction.CollectActiveBossHealth(_healthScratch);
                for (int i = 0; i < _healthScratch.Count; i++)
                {
                    if (_healthScratch[i] != null) aggregateMaxHealth += _healthScratch[i].MaxHealth;
                }
                _telemetry.BeginWave(CharacterMainControl.Main, aggregateMaxHealth);
                if (armedAmmoTypeId > 0)
                {
                    _telemetry.ArmAmmoBan(armedAmmoTypeId);
                    _ammoBanCount++;
                    string bannedAmmoName = ModeGRecapPanel.GetAmmoDisplayName(armedAmmoTypeId);
                    string banMessage = L10n.T("BossRush_ModeG_AmmoBan") + bannedAmmoName;
                    string attribution = ModeGRecapPanel.ComposeBanAttributionLine(
                        bannedAmmoName, bannedAmmoThreatSharePercent);
                    if (!string.IsNullOrEmpty(attribution)) banMessage += "\n" + attribution;
                    _host.ShowMessage(banMessage);
                }

                _state.combatPhase = ModeGCombatPhase.Fighting;
                if (waveIndex == 0)
                {
                    _firstWaveCombatStarted = true;
                    _startupRefundSettled = true;
                    _startupTicketRefundable = false;
                    _startupRelicRefundable = false;
                }
                _host.ShowModeGWaveBanner(waveIndex, wave, axis);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] SpawnWaveAsync 异常: " + e.Message);
                End(ModeGExitReason.TechnicalIntegrityLoss);
            }
            finally
            {
                _waveSpawnInFlight = false;
            }
        }

        /// <summary>
        /// 被禁弹种上局威胁占比（%；归因文案用；表为空/异常返回 0）。
        /// 仅可在 BeginWave 之前调用（波级威胁缓存随 BeginWave 清空）。
        /// </summary>
        private int GetAmmoThreatSharePercent(int ammoTypeId)
        {
            try
            {
                if (_telemetry == null || ammoTypeId <= 0) return 0;
                IReadOnlyDictionary<int, double> table = _telemetry.AmmoThreatTable;
                if (table == null || table.Count == 0) return 0;
                double self;
                if (!table.TryGetValue(ammoTypeId, out self) || self <= 0.0) return 0;
                double total = 0.0;
                foreach (KeyValuePair<int, double> kv in table) total += kv.Value;
                if (total <= 0.0) return 0;
                return Math.Max(1, (int)Math.Round(self * 100.0 / total));
            }
            catch { return 0; }
        }

        /// <summary>
        /// managed 槽生成：构造 Mode G 主 Boss 上下文并经 SpawnCore -> Dispatcher 分流。
        /// </summary>
        private async UniTask<ManagedBossPrepareResult> SpawnManagedSlotAsync(
            EnemyPresetInfo info, string key, Vector3 position, int waveIndex)
        {
            try
            {
                ManagedBossSpawnContext ctx = ManagedBossSpawnContext.CreateModeGPrimary(key, CanContinueRun);
                // 托管辅助单位激活前原子提交接线（规格 §20 第 16 条）：
                // 女巫随从等 Auxiliary 激活路径必须提交返回 true 后才放行；
                // 迟到 ticket（run 已结束/父 owner 失效）fail-closed，不写 Legacy 单例。
                ctx.TryCommitAuxiliaryBeforeActivation = TryCommitModeGAuxiliaryBeforeActivation;
                ctx.OnAuxiliaryReleased = HandleModeGAuxiliaryReleased;
                return await _host.SpawnModeGManagedBossAsync(info, position, waveIndex + 1, ctx);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] managed 槽生成异常 key=" + key + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Mode G 托管辅助单位激活前原子提交（规格 §20 第 16 条）：
        /// 返回 true 才允许激活；迟到 ticket（run 已结束/状态失效）与非法角色 fail-closed。
        /// </summary>
        private bool TryCommitModeGAuxiliaryBeforeActivation(CharacterMainControl auxiliary, ManagedBossRole role)
        {
            try
            {
                if (auxiliary == null || auxiliary.Health == null || auxiliary.Health.IsDead) return false;
                if (role != ManagedBossRole.Auxiliary) return false;
                if (!CanContinueRun()) return false;
                return _committedAuxiliaries.Add(auxiliary);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mode G 辅助单位释放通知（仅成功提交者恰好一次；幂等移除）。
        /// </summary>
        private void HandleModeGAuxiliaryReleased(CharacterMainControl auxiliary, ManagedBossRole role)
        {
            try
            {
                if (auxiliary != null) _committedAuxiliaries.Remove(auxiliary);
            }
            catch { /* no-throw */ }
        }

        /// <summary>
        /// official 路径 onCommit（SpawnCore 激活屏障内）：阵营安全网。
        /// </summary>
        private bool CommitOfficialSpawn(EnemySpawnContext ctx)
        {
            try
            {
                if (ctx == null || ctx.character == null) return false;
                ctx.character.SetTeam(Teams.wolf);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool EnsureCommittedBossTeams()
        {
            try
            {
                _characterScratch.Clear();
                _state.CollectTrackedBosses(_characterScratch);
                for (int i = 0; i < _characterScratch.Count; i++)
                {
                    CharacterMainControl boss = _characterScratch[i];
                    if (boss == null || boss.Health == null || boss.Health.IsDead) return false;
                    boss.SetTeam(Teams.wolf);
                }
                return _characterScratch.Count > 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 提交 Boss 阵营安全网失败：" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 提交后波级修饰：阵营安全网、距离适应、宿敌 Rank、幕倍率。
        /// </summary>
        private void ApplyWaveModifiers(CharacterMainControl boss, ModeGWavePlan.WaveSlot wave,
            ModeGDistanceVerdict verdict)
        {
            if (boss == null) return;
            try
            {
                boss.SetTeam(Teams.wolf);

                if (verdict == ModeGDistanceVerdict.Close) _adaptive.ApplyCloseAdaptation(boss);
                else if (verdict == ModeGDistanceVerdict.Far) _adaptive.ApplyFarAdaptation(boss);

                // 幕倍率（伤害/生命，owner tunable）
                float healthBonus = ModeGAdaptiveCombat.GetActHealthBonus(wave.actIndex);
                if (healthBonus > 0f && boss.Health != null)
                {
                    boss.Health.AddHealth(boss.Health.MaxHealth * healthBonus);
                }
                // 幕伤害倍率经 Stat Modifier 应用（PercentageAdd）
                ApplyActDamageBonus(boss, wave.actIndex);

                if (wave.isNemesisWave)
                {
                    ModeGNemesisPersistence.NemesisRecordDto record = ModeGNemesisPersistence.LoadOrInit();
                    int rank = record != null ? Math.Max(1, record.rank) : 1;
                    _adaptive.ApplyNemesisRank(boss, rank);

                    // 记录本局出场宿敌 key（死亡归因/击败判定用）
                    if (record != null && !string.IsNullOrEmpty(record.bossPresetKey))
                    {
                        _runNemesisKey = record.bossPresetKey;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] ApplyWaveModifiers 异常: " + e.Message);
            }
        }

        private void ApplyActDamageBonus(CharacterMainControl boss, int actIndex)
        {
            float bonus = ModeGAdaptiveCombat.GetActDamageBonus(actIndex);
            if (bonus <= 0f) return;
            try
            {
                if (boss.CharacterItem == null) return;
                foreach (string statName in new[] { "GunDamageMultiplier", "MeleeDamageMultiplier" })
                {
                    ItemStatsSystem.Stat stat = boss.CharacterItem.GetStat(statName);
                    if (stat == null) continue;
                    ItemStatsSystem.Stats.Modifier modifier = new ItemStatsSystem.Stats.Modifier(
                        ItemStatsSystem.Stats.ModifierType.PercentageAdd, bonus, this);
                    stat.AddModifier(modifier);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 幕伤害倍率应用失败: " + e.Message);
            }
        }

        #endregion

        #region Boss Death / Wave Settlement

        /// <summary>
        /// 已登记 Boss 死亡路由（telemetry OnDead run owner 回调）。
        /// </summary>
        private void HandleBossDead(Health health, DamageInfo info)
        {
            try
            {
                if (!CanContinueRun() || _state == null || !_state.IsActive) return;
                if (_state.combatPhase == ModeGCombatPhase.Spawning || _batchActivationInProgress)
                {
                    HandleTechnicalBossDeath(health);
                    End(ModeGExitReason.TechnicalIntegrityLoss);
                    return;
                }
                if (!_spawnTransaction.MarkKilled(health)) return;
                _totalBossKills++;

                ManagedBossRuntimeHandle deadManagedHandle = FindManagedHandle(health);
                if (deadManagedHandle != null)
                {
                    try
                    {
                        if (deadManagedHandle.CleanupAfterDeath != null)
                            deadManagedHandle.CleanupAfterDeath(info);
                    }
                    catch (Exception cleanupEx)
                    {
                        ModBehaviour.DevLog("[ModeG] [WARNING] managed death cleanup 异常: " + cleanupEx.Message);
                    }
                    deadManagedHandle.CleanupOnce(ManagedBossCleanupReason.Death);
                }

                // 成就窄上报入队（host destroy token CAS 消费）
                try
                {
                    string bossType;
                    _characterScratch.Clear();
                    bossType = ResolveBossAchievementType(health, out bossType) ? bossType : "Normal";
                    int token = unchecked((int)ModeGDeterministicRandom.Fnv1a64(
                        (bossType ?? "Normal") + "|" + _state.waveEpoch));
                    ModeGCombatTelemetry.EnqueueAchievementReport(
                        token, bossType ?? "Normal", !AchievementTracker.HasTakenDamage);
                }
                catch { /* no-throw */ }

                // 宿敌击败判定（R3 直伤终结记录）
                CheckNemesisDefeat(health, info);

                // Last Stand 触发：恰剩 1 名且开战提交 >=2
                if (!_state.lastStandActive
                    && ModeGDeathRouting.ShouldTriggerLastStand(_state, _spawnTransaction.ActiveBossCount, _state.SlotCommitted))
                {
                    _state.lastStandActive = true;
                    _state.lastStandTimer = ModeGAdaptiveCombat.LastStandDurationSeconds;
                    _state.combatPhase = ModeGCombatPhase.LastStand;
                    _lastStandCount++;
                    _host.ShowMessage(L10n.T("最后处决倒计时开始！", "Last Stand countdown begins!"));
                }

                // 存活归零：下一帧结算
                if (_spawnTransaction.ActiveBossCount == 0) _waveSettlementPending = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] HandleBossDead 异常: " + e.Message);
            }
        }

        private void HandleTechnicalBossDeath(Health health)
        {
            ManagedBossRuntimeHandle handle = FindManagedHandle(health);
            _characterScratch.Clear();
            try { _state.CollectTrackedBosses(_characterScratch); } catch { }
            try { _spawnTransaction.MarkKilled(health); } catch { }

            if (handle != null)
            {
                handle.CleanupOnce(ManagedBossCleanupReason.TechnicalLoss);
                return;
            }

            for (int i = 0; i < _characterScratch.Count; i++)
            {
                CharacterMainControl character = _characterScratch[i];
                if (character != null && character.Health == health)
                {
                    DestroyBoss(character);
                    return;
                }
            }
        }

        private bool ResolveBossAchievementType(Health health, out string bossType)
        {
            bossType = null;
            try
            {
                lock (_managedHandles)
                {
                    for (int i = 0; i < _managedHandles.Count; i++)
                    {
                        ManagedBossRuntimeHandle handle = _managedHandles[i];
                        if (handle != null && handle.Character != null && handle.Character.Health == health)
                        {
                            bossType = handle.AchievementBossType;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private ManagedBossRuntimeHandle FindManagedHandle(Health health)
        {
            if (health == null) return null;
            lock (_managedHandles)
            {
                for (int i = 0; i < _managedHandles.Count; i++)
                {
                    ManagedBossRuntimeHandle handle = _managedHandles[i];
                    if (handle != null && handle.Character != null
                        && ReferenceEquals(handle.Character.Health, health)) return handle;
                }
            }
            return null;
        }

        private void CheckNemesisDefeat(Health health, DamageInfo info)
        {
            try
            {
                ModeGWavePlan.WaveSlot wave = _wavePlan.GetWave(_state.waveEpoch);
                if (wave == null || !wave.isNemesisWave) return;

                string killedKey;
                _characterScratch.Clear();
                _state.CollectTrackedBosses(_characterScratch); // 死亡后可能已注销，忽略
                if (!_spawnTransaction.TryGetCommittedKeyByHealth(health, out killedKey)) return;

                if (!string.IsNullOrEmpty(_runNemesisKey)
                    && string.Equals(killedKey, _runNemesisKey, StringComparison.Ordinal))
                {
                    _nemesisDefeatedThisRun = true;

                    // R3 + 玩家直伤终结：契约进度
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && info.fromCharacter == player && !info.isFromBuffOrEffect)
                    {
                        ModeGNemesisPersistence.NemesisRecordDto record = ModeGNemesisPersistence.Current;
                        if (record != null && record.rank >= ModeGNemesisPersistence.MaxRank)
                        {
                            _nemesisR3FinalBlowDirect = true;
                        }
                    }

                    // 宿敌记账：defeatsByPlayer++；R3 彻底击败写墓碑
                    try
                    {
                        if (!ModeGNemesisPersistence.IsStoreFaulted)
                        {
                            ModeGNemesisPersistence.NemesisRecordDto dto = ModeGNemesisPersistence.LoadOrInit();
                            if (dto != null && string.Equals(dto.bossPresetKey, killedKey, StringComparison.Ordinal))
                            {
                                ModeGNemesisPersistence.NemesisRecordDto copy =
                                    new ModeGNemesisPersistence.NemesisRecordDto
                                    {
                                        schemaVersion = dto.schemaVersion,
                                        bossPresetKey = dto.bossPresetKey,
                                        rank = dto.rank,
                                        temperamentId = dto.temperamentId,
                                        defeatsByPlayer = dto.defeatsByPlayer + 1,
                                        defeatsOfPlayer = dto.defeatsOfPlayer,
                                        lastUpdatedTicks = dto.lastUpdatedTicks,
                                        originRunId = dto.originRunId,
                                        tombstone = dto.rank >= ModeGNemesisPersistence.MaxRank
                                    };
                                ModeGNemesisPersistence.Store(copy);
                            }
                        }
                    }
                    catch { /* no-throw */ }
                }
            }
            catch { /* no-throw */ }
        }

        private void SettleCurrentWave()
        {
            if (_state == null || !_state.IsActive) return;
            _state.combatPhase = ModeGCombatPhase.WaveSettling;

            // 波末轴破解评估
            ModeGCounterAxis axis = ModeGAdaptiveCombat.GetAxisForWave(_state.waveEpoch);
            bool resolved = false;
            if (axis == ModeGCounterAxis.Distance)
            {
                ModeGDistanceVerdict verdict = ModeGAdaptiveCombat.EvaluateDistanceAxis(_telemetry);
                resolved = ModeGAdaptiveCombat.IsDistanceAxisBroken(_telemetry, verdict);
            }
            else if (axis == ModeGCounterAxis.Ammo)
            {
                // 弹药轴破解：样本达标且整波零违规（停火 CalmGate 由采样窗口隐含）
                resolved = _telemetry.TotalAmmoSamples >= ModeGAdaptiveCombat.AmmoAxisMinSamples
                    && _telemetry.ArmedBanViolationCount == 0
                    && _telemetry.ArmedBanAmmoTypeId > 0;
            }
            else if (axis == ModeGCounterAxis.Attribute)
            {
                // 属性封锁破解：被封锁侧仍保持 >=35% 伤害占比并达血量贡献门槛
                float lockedShare = _telemetry.GunDirectDamage >= _telemetry.MeleeDirectDamage
                    ? _telemetry.GunDamageShare
                    : _telemetry.MeleeDamageShare;
                resolved = lockedShare >= ModeGAdaptiveCombat.DistanceBreakDamageShare
                    && _telemetry.CombatStartAggregatePrimaryMaxHealth > 0f
                    && (_telemetry.TotalDirectDamage / _telemetry.CombatStartAggregatePrimaryMaxHealth)
                        >= ModeGAdaptiveCombat.DistanceBreakHealthContribution;
            }

            if (axis != ModeGCounterAxis.None)
            {
                if (resolved && _adaptive.RecordResolve(axis))
                {
                    if (_state.actIndex >= 0 && _state.actIndex < _resolvesPerAct.Length)
                    {
                        _resolvesPerAct[_state.actIndex]++;
                    }
                    _axisBreakChain++;
                    _maxAxisBreakChain = Math.Max(_maxAxisBreakChain, _axisBreakChain);
                    _host.ShowMessage(L10n.T("反制破解！Resolve +1", "Counter broken! Resolve +1"));
                }
                else
                {
                    _axisBreakChain = 0;
                }
            }

            // 第 9 波清：胜利路由
            if (_state.IsFinalWave)
            {
                ModeGDeathRouting.HandleVictory(this);
                return;
            }

            // 休整
            _state.intermissionActive = true;
            _state.intermissionTimer = ModeGWavePlan.GetIntermissionDuration(_state.waveEpoch);
            _state.combatPhase = ModeGCombatPhase.Intermission;
        }

        #endregion

        #region Frame Drive（Entry.Update 与模块系统 OnUpdate 双驱动帧去重）

        /// <summary>
        /// Entry 驱动的每帧更新。
        /// </summary>
        public void Update(float deltaTime)
        {
            DriveCore(deltaTime);
        }

        /// <summary>
        /// 模块系统驱动（双驱动守卫：同帧去重 + run owner 身份校验）。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (ModeGRunContext.CurrentModule != this) return;
            DriveCore(deltaTime);
        }

        private void DriveCore(float deltaTime)
        {
            if (_disposed || _state == null || _ended) return;
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastDrivenFrame) return;
            _lastDrivenFrame = frame;

            try
            {
                if (_state.IsActive)
                {
                    // 玩家死亡兜底轮询（主路由为独立 OnDead 订阅；CAS 幂等）
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null && player.Health.IsDead)
                    {
                        ModeGDeathRouting.HandlePlayerDeath(this, player.Health, default(DamageInfo));
                        return;
                    }

                    // Last Stand 倒计时
                    if (_state.lastStandActive)
                    {
                        _state.lastStandTimer -= deltaTime;
                        if (_state.lastStandTimer <= 0f)
                        {
                            _state.lastStandActive = false;
                            _state.combatPhase = ModeGCombatPhase.Fighting;
                            ApplyRevengeToSurvivor();
                        }
                    }

                    // 波结算（下一帧窄上报）
                    if (_waveSettlementPending && !_waveSpawnInFlight)
                    {
                        _waveSettlementPending = false;
                        SettleCurrentWave();
                    }

                    // 休整倒计时 -> 下一波
                    if (_state.intermissionActive)
                    {
                        _state.intermissionTimer -= deltaTime;
                        if (_state.intermissionTimer <= 0f)
                        {
                            _state.intermissionActive = false;
                            int nextWave = _state.waveEpoch + 1;
                            if (_state.TryResetSlotsForNextWave())
                            {
                                _spawnTransaction.ResetForNextWave();
                                SpawnWaveAsync(nextWave).Forget();
                            }
                            else
                            {
                                End(ModeGExitReason.TechnicalIntegrityLoss);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] DriveCore 异常: " + e.Message);
            }
        }

        private void ApplyRevengeToSurvivor()
        {
            try
            {
                _characterScratch.Clear();
                _state.CollectTrackedBosses(_characterScratch);
                if (_characterScratch.Count > 0 && _characterScratch[0] != null)
                {
                    _adaptive.ApplyRevengeBuff(_characterScratch[0]);
                    _host.ShowMessage(L10n.T("幸存 Boss 获得复仇强化！", "The survivor gains Revenge!"));
                }
            }
            catch { /* no-throw */ }
        }

        #endregion

        #region Module Lifecycle Hooks（BossRushRuntimeModuleBase）

        /// <summary>
        /// 场景加载守卫：run 进行中离开 verified pair 即 End(SceneChanged)。
        /// </summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                if (_state == null || _ended || _disposed) return;
                if (_state.lifecyclePhase == ModeGLifecyclePhase.None) return;
                if (!ModeGMapSupportRegistry.IsVerifiedSceneName(context.SceneName))
                {
                    ModBehaviour.DevLog("[ModeG] 离开 verified scene pair: " + context.SceneName);
                    End(ModeGExitReason.SceneChanged);
                }
            }
            catch { /* no-throw */ }
        }

        /// <summary>
        /// 宿主销毁：委托静态 PrepareHostDestroy（token CAS 幂等）。
        /// </summary>
        public override void OnDestroy()
        {
            try { ModeG.PrepareHostDestroy(); } catch { /* no-throw 契约 */ }
        }

        #endregion

        #region End（九种终局统一幂等出口）

        /// <summary>
        /// 统一幂等 End：Cleanup 状态机 -> 退订 -> Modifier 恢复 -> managed handle 清理 ->
        /// Dispatcher 复位 -> 成就 session 结束。
        /// </summary>
        public void End(ModeGExitReason reason)
        {
            if (_ended || _state == null) return;
            _ended = true;
            try
            {
                RefundStartupPaymentOnTechnicalFailure(reason);
                ModeGCleanupController.Cleanup(_state, reason);

                try
                {
                    if (_telemetry != null)
                    {
                        _telemetry.UnsubscribeCombat();
                        _telemetry.UnsubscribeDead();
                    }
                    UnsubscribePlayerDeath();
                }
                catch { }

                try { if (_adaptive != null) _adaptive.RestoreAllModifiers(); } catch { }

                try
                {
                    lock (_managedHandles)
                    {
                        for (int i = 0; i < _managedHandles.Count; i++)
                        {
                            if (_managedHandles[i] != null)
                            {
                                _managedHandles[i].CleanupOnce(ManagedBossCleanupReason.RunEnded);
                            }
                        }
                        _managedHandles.Clear();
                    }
                }
                catch { }

                // 辅助提交登记随 run 结束清空（迟到 ticket 此后一律 fail-closed）
                try { _committedAuxiliaries.Clear(); } catch { }

                try
                {
                    if (_dispatcherRef != null
                        && ReferenceEquals(ModBehaviour.ManagedBossSpawnDispatcher, _dispatcherRef))
                    {
                        ModBehaviour.ManagedBossSpawnDispatcher = null;
                    }
                }
                catch { }

                try { if (_host != null) _host.EndModeGAchievementSession(); } catch { }
                try { ModeGRewardTransaction.ResetRelicReturnGate(); } catch { }
                try { ModeGNemesisPersistence.ShutdownSubscription(); } catch { }
                try { ModeGProfilePersistence.ShutdownSubscription(); } catch { }

                ModBehaviour.DevLog("[ModeG] run 结束 reason=" + reason
                    + " result=" + _state.battleResult + " wave=" + (_state.waveEpoch + 1));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] End 异常: " + e.Message);
            }
        }

        private void RefundStartupPaymentOnTechnicalFailure(ModeGExitReason reason)
        {
            if (_startupRefundSettled || _firstWaveCombatStarted || _host == null) return;
            if (reason != ModeGExitReason.SpawnExhausted
                && reason != ModeGExitReason.TechnicalIntegrityLoss) return;

            _startupRefundSettled = true;
            if (_startupTicketRefundable)
            {
                _host.TryRefundModeGStartupItem(
                    _host.GetModeGTicketTypeId(), L10n.T("船票", "Boss Rush Ticket"));
            }
            if (_startupRelicRefundable)
            {
                _host.TryRefundModeGStartupItem(
                    FateEchoRelicConfig.TYPE_ID, L10n.T("宿命回响信物", "Fate Echo Relic"));
            }
            _startupTicketRefundable = false;
            _startupRelicRefundable = false;
            _host.ShowMessage(L10n.T(
                "宿命回响首波未能启动，已返还入场道具。",
                "Fate Echo failed before wave one; entry items were refunded."));
        }

        /// <summary>
        /// Dispose（Entry.ShutdownModeG / Initialize 失败路径）。防御式幂等。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_ended && _state != null)
                {
                    End(_state.exitReason != ModeGExitReason.None
                        ? _state.exitReason
                        : ModeGExitReason.ModDestroyed);
                }
            }
            catch { }
            try { UnsubscribePlayerDeath(); } catch { }
            _snapshot = null;
        }

        #endregion

    }
}
