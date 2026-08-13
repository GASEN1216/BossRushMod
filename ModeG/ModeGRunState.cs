using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 运行状态。方案文档 §4/§20（2026-08-10 裁决版）。
    /// Starting 阶段冻结身份字段，Active 阶段只经 CAS/ResolveSlotOnce 推进，
    /// Rewarding/Exiting 阶段只追加不回滚。
    ///
    /// 主槽计数契约（§20 ModeGStateModelGuard）：
    ///   0 &lt;= committed &lt;= resolved &lt;= expected
    /// 只有 <see cref="ResolveSlotOnce"/> 可写 resolved/committed；
    /// battleResultToken 只允许 Victory/Defeat CAS 一次；
    /// contractStreakBreakToken 只允许有效 ManualExit 清 streak（一次）。
    /// </summary>
    public sealed class ModeGRunState
    {
        #region Identity（Starting 冻结）

        /// <summary>运行 ID（processNonce ^ sessionCounter 派生，规格第五节）</summary>
        public readonly ulong runId;
        /// <summary>确定性 runSeed（preview 冻结，规格第五节第 2 步）</summary>
        public readonly ulong runSeed;
        /// <summary>玩家 GUID（真实来源：Steam ID 回退存档槽位，禁止硬编码）</summary>
        public readonly string playerGuid;
        /// <summary>运行开始时间戳（UtcNow.Ticks）</summary>
        public readonly long runTimestampTicks;
        /// <summary>进程内会话计数（每次过门控开确认页递增）</summary>
        public readonly long sessionCounter;

        #endregion

        #region Lifecycle

        public ModeGLifecyclePhase lifecyclePhase;
        public ModeGCombatPhase combatPhase;
        public ModeGBattleResult battleResult;
        public ModeGExitReason exitReason;

        #endregion

        #region Wave Progress

        /// <summary>当前波次 epoch（0-8，固定九波，index 8 = 第 9 波）</summary>
        public int waveEpoch;
        /// <summary>当前幕（0=第一幕 1-3 波，1=第二幕 4-6 波，2=第三幕 7-9 波）</summary>
        public int actIndex;
        /// <summary>是否处于 Last Stand（最后一名 Boss 倒计时处决，12 秒 owner tunable）</summary>
        public bool lastStandActive;
        /// <summary>Last Stand 剩余秒数</summary>
        public float lastStandTimer;
        /// <summary>休整倒计时剩余秒数</summary>
        public float intermissionTimer;
        /// <summary>休整是否激活</summary>
        public bool intermissionActive;

        #endregion

        #region Slot Counters（契约：0 <= committed <= resolved <= expected）

        /// <summary>当前波期望槽位数（波开始冻结，只读）</summary>
        private int _slotExpected;
        /// <summary>已结案并确认提交的槽位数（仅 ResolveSlotOnce 写）</summary>
        private int _slotCommitted;
        /// <summary>已结案槽位数（仅 ResolveSlotOnce 写）</summary>
        private int _slotResolved;
        /// <summary>已结案 ticket 集合（防重复结案）</summary>
        private readonly HashSet<int> _resolvedTickets = new HashSet<int>();

        public int SlotExpected { get { return _slotExpected; } }
        public int SlotCommitted { get { return _slotCommitted; } }
        public int SlotResolved { get { return _slotResolved; } }

        /// <summary>当前波全部槽位已结案</summary>
        public bool AreAllSlotsResolved
        {
            get { lock (_casLock) { return _slotExpected > 0 && _slotResolved >= _slotExpected; } }
        }

        #endregion

        #region Tracked Boss Registry（exact 引用身份，O(1)）

        /// <summary>已登记 Boss 的 exact Health -> Character 映射（OnDead 抑制/追踪用）</summary>
        private readonly Dictionary<Health, CharacterMainControl> _trackedBossByHealth
            = new Dictionary<Health, CharacterMainControl>();

        #endregion

        #region Contract

        /// <summary>本局宿命契约稳定 ID（-1 = 未选择；bit 0-31 只追加不复用）</summary>
        public int fateContractId = -1;

        #endregion

        #region Invalidation Flags（host destroy / 清理路径写入）

        /// <summary>reward nonce 已失效（host destroy 或 Rewarding 死亡路径）</summary>
        public volatile bool rewardNonceInvalidated;
        /// <summary>spawn lease 已失效（host destroy / 有界 timeout 路径）</summary>
        public volatile bool spawnLeasesInvalidated;
        /// <summary>成就 report 已同步消费（PrepareHostDestroy token CAS）</summary>
        public volatile bool pendingAchievementReportsConsumed;

        #endregion

        public ModeGRunState(ulong runId, ulong runSeed, string playerGuid, long runTimestampTicks, long sessionCounter)
        {
            this.runId = runId;
            this.runSeed = runSeed;
            this.playerGuid = playerGuid ?? string.Empty;
            this.runTimestampTicks = runTimestampTicks;
            this.sessionCounter = sessionCounter;
            this.lifecyclePhase = ModeGLifecyclePhase.None;
            this.combatPhase = ModeGCombatPhase.None;
            this.battleResult = ModeGBattleResult.Pending;
            this.exitReason = ModeGExitReason.None;
            this.waveEpoch = 0;
            this.actIndex = 0;
        }

        #region State Queries

        public bool IsActive { get { return lifecyclePhase == ModeGLifecyclePhase.Active; } }
        public bool IsStarting { get { return lifecyclePhase == ModeGLifecyclePhase.Starting; } }
        public bool IsExiting { get { return lifecyclePhase == ModeGLifecyclePhase.Exiting; } }
        public bool IsRewarding { get { return lifecyclePhase == ModeGLifecyclePhase.Rewarding; } }
        public bool IsTerminal { get { return lifecyclePhase == ModeGLifecyclePhase.None && exitReason != ModeGExitReason.None; } }
        public bool IsVictory { get { return battleResult == ModeGBattleResult.Victory; } }
        public bool IsDefeat { get { return battleResult == ModeGBattleResult.Defeat; } }
        public bool IsFinalWave { get { return waveEpoch >= 8; } }
        public bool IsLastAct { get { return actIndex >= 2; } }

        /// <summary>
        /// 战斗相位判定（Active + Fighting/LastStand）。
        /// </summary>
        public bool IsCombatActive
        {
            get { return IsActive && ModeGPhaseGuards.IsCombatPhase(combatPhase); }
        }

        #endregion

        #region CAS Guards

        private readonly object _casLock = new object();

        /// <summary>battleResultToken：Pending -> Victory/Defeat 只允许 CAS 一次。</summary>
        public bool TryLockBattleResult(ModeGBattleResult target)
        {
            if (target != ModeGBattleResult.Victory && target != ModeGBattleResult.Defeat) return false;
            lock (_casLock)
            {
                if (battleResult != ModeGBattleResult.Pending) return false;
                battleResult = target;
                return true;
            }
        }

        /// <summary>
        /// lifecyclePhase 原子推进。单向：None->Starting->Active->Rewarding->Exiting->None
        /// （Starting 可直落 Exiting 处理开局失败；Preview 阶段已按裁决移除）。
        /// </summary>
        public bool TryAdvanceLifecycle(ModeGLifecyclePhase target)
        {
            lock (_casLock)
            {
                if (!CanAdvanceLifecycle(lifecyclePhase, target)) return false;
                lifecyclePhase = target;
                return true;
            }
        }

        private static bool CanAdvanceLifecycle(ModeGLifecyclePhase from, ModeGLifecyclePhase to)
        {
            switch (from)
            {
                case ModeGLifecyclePhase.None: return to == ModeGLifecyclePhase.Starting;
                case ModeGLifecyclePhase.Starting: return to == ModeGLifecyclePhase.Active || to == ModeGLifecyclePhase.Exiting;
                case ModeGLifecyclePhase.Active: return to == ModeGLifecyclePhase.Rewarding || to == ModeGLifecyclePhase.Exiting;
                case ModeGLifecyclePhase.Rewarding: return to == ModeGLifecyclePhase.Exiting;
                case ModeGLifecyclePhase.Exiting: return to == ModeGLifecyclePhase.None;
                default: return false;
            }
        }

        #endregion

        #region Slot Resolution（唯一写 resolved/committed 的入口）

        /// <summary>
        /// 波开始时冻结期望槽位数。只允许从 0 重置（每波一次）。
        /// </summary>
        public bool TrySetWaveSlotPlan(int expected)
        {
            if (expected < 0) return false;
            lock (_casLock)
            {
                if (_slotExpected != 0 || _slotResolved != 0 || _slotCommitted != 0) return false;
                if (_resolvedTickets.Count != 0) return false;
                _slotExpected = expected;
                return true;
            }
        }

        /// <summary>
        /// 结案单个槽位（ticket = 槽位索引）。同一 ticket 只允许结案一次。
        /// 唯一允许写 resolved/committed 的入口；写后校验 0 &lt;= committed &lt;= resolved &lt;= expected。
        /// </summary>
        public bool ResolveSlotOnce(int ticket, ModeGSlotOutcome outcome)
        {
            if (outcome == ModeGSlotOutcome.Pending) return false;
            lock (_casLock)
            {
                if (!_resolvedTickets.Add(ticket)) return false;
                _slotResolved++;
                if (outcome == ModeGSlotOutcome.Committed) _slotCommitted++;
                // 不变式校验：违反即视为状态机故障（guard 会断言同一条件）
                if (_slotCommitted < 0 || _slotCommitted > _slotResolved || _slotResolved > _slotExpected)
                {
                    throw new InvalidOperationException(
                        "[ModeG] slot invariant violated: committed=" + _slotCommitted
                        + " resolved=" + _slotResolved + " expected=" + _slotExpected);
                }
                return true;
            }
        }

        /// <summary>
        /// 下一波前重置槽位计数（仅允许在全部结案后调用）。
        /// </summary>
        public bool TryResetSlotsForNextWave()
        {
            lock (_casLock)
            {
                if (_slotResolved != _slotExpected) return false;
                _slotExpected = 0;
                _slotCommitted = 0;
                _slotResolved = 0;
                _resolvedTickets.Clear();
                return true;
            }
        }

        #endregion

        #region Tracked Boss Registry API（门控契约消费）

        private readonly HashSet<CharacterRandomPreset> _stagingPresets
            = new HashSet<CharacterRandomPreset>();
        private readonly Dictionary<Health, CharacterMainControl> _stagingBossByHealth
            = new Dictionary<Health, CharacterMainControl>();

        public bool RegisterStagingPreset(CharacterRandomPreset preset)
        {
            if (preset == null) return false;
            lock (_casLock) { return _stagingPresets.Add(preset); }
        }

        public void UnregisterStagingPreset(CharacterRandomPreset preset)
        {
            if (preset == null) return;
            lock (_casLock) { _stagingPresets.Remove(preset); }
        }

        public bool RegisterStagingBoss(Health health, CharacterMainControl character)
        {
            if (health == null || character == null) return false;
            lock (_casLock)
            {
                if (_stagingBossByHealth.ContainsKey(health) || _trackedBossByHealth.ContainsKey(health))
                    return false;
                _stagingBossByHealth.Add(health, character);
                return true;
            }
        }

        public void UnregisterStagingBoss(Health health)
        {
            if (health == null) return;
            lock (_casLock) { _stagingBossByHealth.Remove(health); }
        }

        public bool IsStagingBossHealth(Health health)
        {
            if (health == null) return false;
            lock (_casLock)
            {
                if (_stagingBossByHealth.ContainsKey(health)) return true;
                CharacterMainControl character = null;
                try { character = health.TryGetCharacter(); } catch { }
                return character != null && character.characterPreset != null
                    && _stagingPresets.Contains(character.characterPreset);
            }
        }

        /// <summary>
        /// 登记已提交 Boss（exact Health 引用身份）。重复登记同一 Health 返回 false。
        /// </summary>
        public bool RegisterTrackedBoss(Health health, CharacterMainControl character)
        {
            if (health == null || character == null) return false;
            lock (_casLock)
            {
                if (_trackedBossByHealth.ContainsKey(health)) return false;
                _stagingBossByHealth.Remove(health);
                _trackedBossByHealth.Add(health, character);
                return true;
            }
        }

        /// <summary>
        /// 注销 Boss（死亡结案/清理路径）。按引用身份移除，幂等。
        /// </summary>
        public void UnregisterTrackedBoss(Health health)
        {
            if (health == null) return;
            lock (_casLock)
            {
                _trackedBossByHealth.Remove(health);
            }
        }

        /// <summary>
        /// 已登记 Boss 的 exact Health 引用身份查询（OnDead 抑制/伤害窗口用）。O(1)，no-throw。
        /// </summary>
        public bool IsRegisteredBossHealth(Health health)
        {
            if (health == null) return false;
            lock (_casLock)
            {
                return _trackedBossByHealth.ContainsKey(health);
            }
        }

        /// <summary>
        /// 已登记 Boss 的 exact Character 引用身份查询（大兴兴清理 owner 用）。O(1)，no-throw。
        /// </summary>
        public bool IsRegisteredBossCharacter(CharacterMainControl character)
        {
            if (character == null) return false;
            lock (_casLock)
            {
                foreach (KeyValuePair<Health, CharacterMainControl> kv in _trackedBossByHealth)
                {
                    if (ReferenceEquals(kv.Value, character)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 填充当前已登记 Boss 的 Character 快照（EnemyRecovery 用）。调用方提供目标表。
        /// </summary>
        public void CollectTrackedBosses(List<CharacterMainControl> sink)
        {
            if (sink == null) return;
            lock (_casLock)
            {
                foreach (KeyValuePair<Health, CharacterMainControl> kv in _trackedBossByHealth)
                {
                    if (kv.Value != null) sink.Add(kv.Value);
                }
            }
        }

        /// <summary>
        /// 清空追踪表（End/host destroy 路径，幂等）。
        /// </summary>
        public void ClearTrackedBosses()
        {
            lock (_casLock)
            {
                _trackedBossByHealth.Clear();
                _stagingBossByHealth.Clear();
                _stagingPresets.Clear();
            }
        }

        public int TrackedBossCount
        {
            get { lock (_casLock) { return _trackedBossByHealth.Count; } }
        }

        #endregion

        #region Contract Streak Break Token

        private bool _contractStreakBreakConsumed;

        /// <summary>
        /// contractStreakBreakToken：只允许有效 ManualExit 消费一次（CAS）。
        /// 调用方（CleanupController）负责确认 exitReason == ManualExit 语义有效。
        /// </summary>
        public bool TryConsumeContractStreakBreakToken()
        {
            lock (_casLock)
            {
                if (_contractStreakBreakConsumed) return false;
                _contractStreakBreakConsumed = true;
                return true;
            }
        }

        #endregion
    }
}
