using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>快照采集时点（§17.4 冻结的四类离散事件）。</summary>
    internal enum ModeHSnapshotTrigger
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>每批敌军入场完成后</summary>
        BatchEntered = 1,
        /// <summary>每 BattleSnapshotIntervalSeconds 秒</summary>
        Interval = 2,
        /// <summary>拍铃提交后</summary>
        BellCommitted = 3,
        /// <summary>倒地或接力提交后</summary>
        DownOrRelay = 4
    }

    /// <summary>快照重建结果。</summary>
    internal sealed class ModeHSnapshotRebuildPlan
    {
        /// <summary>是否可用（false 表示必须回落到同场重开）。</summary>
        public bool Usable;
        /// <summary>不可用原因（六类 fail-closed 之一）。</summary>
        public string FailureReasonId;
        /// <summary>按 planSlotIndex 升序的待重建敌军。</summary>
        public List<ModeHBattleEnemyStateDto> Enemies = new List<ModeHBattleEnemyStateDto>();
        /// <summary>待重建的我方选手。</summary>
        public ModeHBattleEntrantStateDto Entrant;
    }

    /// <summary>
    /// Mode H 战场快照（设计提案 §17.4、§25.1）。
    ///
    /// 冻结契约：
    /// - 采集时点固定为四类离散事件，**不按帧写盘**；
    /// - 每次采集只在内存构造并递增 `snapshotSequence`，
    ///   随本场下一次 Season 写入一并落盘，不额外调用 `SaveFile`；
    /// - 位置只存 `float x/y/z` 与 `rotationY`，禁止 Unity 引用、`InstanceID` 与委托；
    /// - `snapshotDigest` 参与 §20.2 的同一 canonical digest（排除自身字段）；
    /// - 重建走**同一个** `ModeHSpawnTransaction`，按 `healthFraction * MaxHealth`
    ///   调 `SetHealth`，续接计时、批次、口令窗口、战痕窗口与一次性判定；
    /// - 六类 fail-closed 条件任一命中都丢弃快照、回落到“技术中止 + 同场重开”，
    ///   **绝不判负**，也绝不声称继续了原战斗。
    /// </summary>
    internal sealed class ModeHBattleSnapshot
    {
        #region 状态

        private ModeHBattleSnapshotDto _current;
        private int _snapshotSequence;
        private float _intervalAccumulator;

        #endregion

        #region 只读

        /// <summary>当前内存快照（可能为 null）。</summary>
        public ModeHBattleSnapshotDto Current { get { return _current; } }

        /// <summary>已采集的快照序号。</summary>
        public int SnapshotSequence { get { return _snapshotSequence; } }

        #endregion

        #region 采集

        /// <summary>开始一场新的比赛：清空快照与计时器。</summary>
        public void BeginMatch()
        {
            _current = null;
            _snapshotSequence = 0;
            _intervalAccumulator = 0f;
        }

        /// <summary>
        /// 间隔采集的计时推进。返回 true 表示到达 10 秒采集时点，
        /// 由调用方在同一帧发起一次 `Capture(ModeHSnapshotTrigger.Interval, ...)`。
        /// </summary>
        public bool TickInterval(float deltaTime)
        {
            _intervalAccumulator += deltaTime;
            if (_intervalAccumulator < ModeHConfig.BattleSnapshotIntervalSeconds) return false;
            _intervalAccumulator = 0f;
            return true;
        }

        /// <summary>
        /// 采集一次快照。只构造内存 DTO 并递增序号，不触碰存档 API。
        /// 摘要计算失败时保留上一份快照并返回 false（fail-closed）。
        /// </summary>
        public bool Capture(
            ModeHSnapshotTrigger trigger,
            ModeHBattleSnapshotContext context,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (context == null)
            {
                failureReasonId = "snapshot_context_missing";
                return false;
            }
            if (trigger == ModeHSnapshotTrigger.Unknown)
            {
                failureReasonId = "snapshot_trigger_unknown";
                return false;
            }

            ModeHBattleSnapshotDto dto = new ModeHBattleSnapshotDto();
            dto.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            dto.matchIndex = context.MatchIndex;
            dto.technicalRetrySequence = context.TechnicalRetrySequence;
            dto.snapshotSequence = _snapshotSequence + 1;
            dto.elapsedSeconds = context.ElapsedSeconds;
            dto.entryBatchIndex = context.EntryBatchIndex;
            dto.pendingBatchStableKeys = CopySorted(context.PendingBatchStableKeys, false);
            dto.activeEnemies = CaptureEnemies(context.Enemies);
            dto.entrant = CaptureEntrant(context.Entrant, context.EntrantIsRelay);
            dto.bellConsumed = context.BellConsumed;
            dto.activeCommandId = context.ActiveCommandId != null ? context.ActiveCommandId : string.Empty;
            dto.commandWindowRemainingSeconds = context.CommandWindowRemainingSeconds;
            dto.scarWindowStates = context.ScarWindowStates != null
                ? context.ScarWindowStates
                : new List<ModeHScarWindowStateDto>();
            dto.cowardCheckDone = CopySorted(context.CowardCheckDone, true);
            dto.errorCheckDone = context.ErrorCheckDone;
            dto.errorSwapActive = context.ErrorSwapActive;
            dto.errorSwapProfileId = context.ErrorSwapProfileId != null
                ? context.ErrorSwapProfileId
                : string.Empty;
            dto.standInPatternId = context.StandInPatternId != null
                ? context.StandInPatternId
                : string.Empty;
            dto.appliedEventTokenIds = CopySorted(context.AppliedEventTokenIds, true);

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(dto, "snapshotDigest", out digest, out digestError))
            {
                failureReasonId = "snapshot_digest_failed:" + digestError;
                return false;
            }
            dto.snapshotDigest = digest;

            _current = dto;
            _snapshotSequence = dto.snapshotSequence;
            return true;
        }

        /// <summary>把当前快照挂到 Season 根字段（随下一次 Season 写入一并落盘）。</summary>
        public void AttachTo(ModeHSeasonDto season)
        {
            if (season == null) return;
            season.currentBattleSnapshot = _current;
        }

        /// <summary>比赛结束或回滚时清空根字段。</summary>
        public void ClearFrom(ModeHSeasonDto season)
        {
            _current = null;
            if (season != null) season.currentBattleSnapshot = null;
        }

        private static List<ModeHBattleEnemyStateDto> CaptureEnemies(IList<ModeHSnapshotEnemyInput> enemies)
        {
            List<ModeHBattleEnemyStateDto> result = new List<ModeHBattleEnemyStateDto>();
            if (enemies == null) return result;
            for (int i = 0; i < enemies.Count; i++)
            {
                ModeHSnapshotEnemyInput input = enemies[i];
                if (input == null || input.Character == null) continue;
                ModeHBattleEnemyStateDto dto = new ModeHBattleEnemyStateDto();
                dto.planSlotIndex = input.PlanSlotIndex;
                dto.stableKey = input.StableKey;
                dto.healthFraction = ModeHCombatTelemetry.ReadHealthFraction(input.Character);
                WritePosition(input.Character, out dto.positionX, out dto.positionY, out dto.positionZ,
                    out dto.rotationY);
                result.Add(dto);
            }
            result.Sort(CompareEnemy);
            return result;
        }

        private static int CompareEnemy(ModeHBattleEnemyStateDto a, ModeHBattleEnemyStateDto b)
        {
            if (a.planSlotIndex != b.planSlotIndex) return a.planSlotIndex.CompareTo(b.planSlotIndex);
            return string.CompareOrdinal(a.stableKey, b.stableKey);
        }

        private static ModeHBattleEntrantStateDto CaptureEntrant(
            ModeHSnapshotEnemyInput entrant, bool isRelay)
        {
            if (entrant == null || entrant.Character == null) return null;
            ModeHBattleEntrantStateDto dto = new ModeHBattleEntrantStateDto();
            dto.profileId = entrant.ProfileId;
            dto.healthFraction = ModeHCombatTelemetry.ReadHealthFraction(entrant.Character);
            WritePosition(entrant.Character, out dto.positionX, out dto.positionY, out dto.positionZ,
                out dto.rotationY);
            dto.isRelay = isRelay;
            return dto;
        }

        /// <summary>只写 float 分量：DTO 里禁止出现 Unity 引用与 InstanceID。</summary>
        private static void WritePosition(
            CharacterMainControl character, out float x, out float y, out float z, out float rotationY)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            rotationY = 0f;
            if (character == null) return;
            try
            {
                Vector3 position = character.transform.position;
                x = position.x;
                y = position.y;
                z = position.z;
                rotationY = character.transform.eulerAngles.y;
            }
            catch (Exception)
            {
                // 角色已被回收：保持零位，重建时会因位置不可行走 fail-closed
            }
        }

        private static List<string> CopySorted(IList<string> source, bool sort)
        {
            List<string> result = new List<string>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrEmpty(source[i])) result.Add(source[i]);
            }
            if (sort) result.Sort(StringComparer.Ordinal);
            return result;
        }

        #endregion

        #region 重建校验

        /// <summary>
        /// 六类 fail-closed 校验（§17.4）：
        /// 1) 快照缺失；2) `snapshotDigest` 不符；3) 任一 stableKey 已不再 Passed；
        /// 4) 场次/重试序号不匹配；5) 位置不可行走；6) 缺少可重建的登场选手。
        /// 任一命中都返回 Usable=false，由调用方回落到“技术中止 + 同场重开”。
        /// </summary>
        public static ModeHSnapshotRebuildPlan Validate(
            ModeHBattleSnapshotDto snapshot,
            int expectedMatchIndex,
            int expectedTechnicalRetrySequence,
            ModeHSupportedMap map)
        {
            ModeHSnapshotRebuildPlan plan = new ModeHSnapshotRebuildPlan();

            if (snapshot == null)
            {
                plan.FailureReasonId = "snapshot_missing";
                return plan;
            }
            if (snapshot.schemaVersion != ModeHConfig.CurrentSchemaVersion)
            {
                plan.FailureReasonId = "snapshot_schema_mismatch";
                return plan;
            }

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(
                    snapshot, "snapshotDigest", out digest, out digestError))
            {
                plan.FailureReasonId = "snapshot_digest_failed:" + digestError;
                return plan;
            }
            if (!string.Equals(digest, snapshot.snapshotDigest, StringComparison.Ordinal))
            {
                plan.FailureReasonId = "snapshot_digest_mismatch";
                return plan;
            }

            if (snapshot.matchIndex != expectedMatchIndex
                || snapshot.technicalRetrySequence != expectedTechnicalRetrySequence)
            {
                plan.FailureReasonId = "snapshot_match_context_mismatch";
                return plan;
            }

            if (snapshot.entrant == null || string.IsNullOrEmpty(snapshot.entrant.profileId))
            {
                plan.FailureReasonId = "snapshot_entrant_missing";
                return plan;
            }

            if (snapshot.activeEnemies != null)
            {
                for (int i = 0; i < snapshot.activeEnemies.Count; i++)
                {
                    ModeHBattleEnemyStateDto enemy = snapshot.activeEnemies[i];
                    if (enemy == null || string.IsNullOrEmpty(enemy.stableKey))
                    {
                        plan.FailureReasonId = "snapshot_enemy_invalid";
                        return plan;
                    }
                    if (!ModeHPresetRegistry.IsProductionKey(enemy.stableKey))
                    {
                        plan.FailureReasonId = "snapshot_key_not_passed:" + enemy.stableKey;
                        return plan;
                    }
                    if (!IsPositionUsable(map, enemy.positionX, enemy.positionY, enemy.positionZ))
                    {
                        plan.FailureReasonId = "snapshot_position_unusable:" + enemy.stableKey;
                        return plan;
                    }
                    plan.Enemies.Add(enemy);
                }
            }

            if (snapshot.pendingBatchStableKeys != null)
            {
                for (int i = 0; i < snapshot.pendingBatchStableKeys.Count; i++)
                {
                    string key = snapshot.pendingBatchStableKeys[i];
                    if (!ModeHPresetRegistry.IsProductionKey(key))
                    {
                        plan.FailureReasonId = "snapshot_pending_key_not_passed:" + key;
                        return plan;
                    }
                }
            }

            if (!IsPositionUsable(map, snapshot.entrant.positionX, snapshot.entrant.positionY,
                    snapshot.entrant.positionZ))
            {
                plan.FailureReasonId = "snapshot_entrant_position_unusable";
                return plan;
            }

            plan.Enemies.Sort(CompareEnemy);
            plan.Entrant = snapshot.entrant;
            plan.Usable = true;
            return plan;
        }

        /// <summary>
        /// 位置可用性：必须落在擂台边界内。零位（采集时角色已被回收）一律拒绝。
        /// </summary>
        private static bool IsPositionUsable(ModeHSupportedMap map, float x, float y, float z)
        {
            if (map == null) return false;
            if (x == 0f && y == 0f && z == 0f) return false;
            return map.IsInsideArena(new Vector3(x, y, z));
        }

        /// <summary>
        /// 生命重建：`SetHealth(healthFraction * MaxHealth)`，随后读回核对。
        /// 读回不符即 fail-closed（§17.4 的第五类条件）。
        /// </summary>
        public static bool TryRestoreHealth(
            CharacterMainControl character, float healthFraction, out string failureReasonId)
        {
            failureReasonId = null;
            if (character == null)
            {
                failureReasonId = "snapshot_restore_character_missing";
                return false;
            }
            if (healthFraction <= 0f || healthFraction > 1f)
            {
                failureReasonId = "snapshot_restore_fraction_invalid";
                return false;
            }
            try
            {
                Health health = character.Health;
                if (health == null)
                {
                    failureReasonId = "snapshot_restore_health_missing";
                    return false;
                }
                float target = healthFraction * health.MaxHealth;
                health.SetHealth(target);
                float readBack = ModeHCombatTelemetry.ReadHealthFraction(character);
                if (Mathf.Abs(readBack - healthFraction) > 0.02f)
                {
                    failureReasonId = "snapshot_restore_readback_mismatch";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "snapshot_restore_exception:" + e.GetType().Name;
                return false;
            }
        }

        #endregion
    }

    /// <summary>一次采集的敌军/选手输入（只在内存传递，不进 DTO）。</summary>
    internal sealed class ModeHSnapshotEnemyInput
    {
        /// <summary>计划槽位序号。</summary>
        public int PlanSlotIndex;
        /// <summary>官方预设稳定 key。</summary>
        public string StableKey;
        /// <summary>我方选手的 profileId（敌军为空）。</summary>
        public string ProfileId;
        /// <summary>角色引用（只用于读位置与生命，不进 DTO）。</summary>
        public CharacterMainControl Character;
    }

    /// <summary>一次采集所需的全部上下文（调用方每次刷新，零分配复用）。</summary>
    internal sealed class ModeHBattleSnapshotContext
    {
        /// <summary>本场编号。</summary>
        public int MatchIndex;
        /// <summary>本场技术重试序号。</summary>
        public int TechnicalRetrySequence;
        /// <summary>已用秒数。</summary>
        public float ElapsedSeconds;
        /// <summary>当前入场批次序号。</summary>
        public int EntryBatchIndex;
        /// <summary>尚未入场的批次成员（按计划顺序，不排序）。</summary>
        public List<string> PendingBatchStableKeys;
        /// <summary>场上敌军。</summary>
        public List<ModeHSnapshotEnemyInput> Enemies;
        /// <summary>场上我方选手。</summary>
        public ModeHSnapshotEnemyInput Entrant;
        /// <summary>当前登场者是否为接力者。</summary>
        public bool EntrantIsRelay;
        /// <summary>拍铃是否已消耗。</summary>
        public bool BellConsumed;
        /// <summary>当前生效口令 ID。</summary>
        public string ActiveCommandId;
        /// <summary>口令窗口剩余秒数。</summary>
        public float CommandWindowRemainingSeconds;
        /// <summary>战痕窗口状态。</summary>
        public List<ModeHScarWindowStateDto> ScarWindowStates;
        /// <summary>已消耗的胆怯判定。</summary>
        public List<string> CowardCheckDone;
        /// <summary>ERROR 判定是否已消耗。</summary>
        public bool ErrorCheckDone;
        /// <summary>ERROR 互换是否生效中。</summary>
        public bool ErrorSwapActive;
        /// <summary>被互换的 profileId。</summary>
        public string ErrorSwapProfileId;
        /// <summary>看台表演模式。</summary>
        public string StandInPatternId;
        /// <summary>已提交的事件 token（防重放）。</summary>
        public List<string> AppliedEventTokenIds;
    }
}
