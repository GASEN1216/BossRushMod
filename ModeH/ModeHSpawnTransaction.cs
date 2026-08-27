using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 单场生成事务（设计提案 §19.3、§25.1）。
    ///
    /// 冻结契约：
    /// - 单场最多保留 3 个敌军临时实例、1 个上场选手临时实例，外加 1 个既有玩家看台身体；
    /// - 按“每帧最多一个角色、每次生成后等待有效 CharacterMainControl 与 Health”分摊；
    /// - 生成队列、失败原因与已生成 stable key 全部进入内存报告，场景切换立即取消；
    /// - 任何创建失败、阵营设置失败、点位重叠、额外掉落/追踪副作用或数量超过计划，
    ///   都触发整批逆序回收并重试同一计划的下一个预冻结候选，不从共享池临时补人；
    /// - 全部角色隔离完成后才统一提交：应用 kit、注册战斗 owner、设置阵营与擂台位置、
    ///   清除 forceTracePlayerDistance/searchedEnemy/noticed、核对 Team.IsEnemy 与无掉落状态，
    ///   最后在同一帧按确定顺序解除 invincible 并激活。
    /// </summary>
    internal sealed class ModeHSpawnTransaction
    {
        #region 状态

        private readonly List<ModeHSpawnHandle> _enemyHandles = new List<ModeHSpawnHandle>();
        private readonly List<ModeHSpawnHandle> _fighterHandles = new List<ModeHSpawnHandle>();
        private readonly List<string> _spawnedStableKeys = new List<string>();
        private readonly List<string> _failureReasonIds = new List<string>();

        private bool _begun;
        private bool _committed;
        private bool _cancelled;
        private int _sceneGeneration;
        private long _ownerToken;
        private ModeHSupportedMap _map;

        #endregion

        #region 只读

        /// <summary>事务是否处于活动状态。</summary>
        public bool IsActive { get { return _begun && !_committed && !_cancelled; } }

        /// <summary>是否已提交。</summary>
        public bool IsCommitted { get { return _committed; } }

        /// <summary>当前敌军实例数。</summary>
        public int EnemyCount { get { return _enemyHandles.Count; } }

        /// <summary>当前选手实例数。</summary>
        public int FighterCount { get { return _fighterHandles.Count; } }

        /// <summary>本次已生成的 stable key（保序）。</summary>
        public List<string> SpawnedStableKeys { get { return _spawnedStableKeys; } }

        /// <summary>失败原因集合。</summary>
        public List<string> FailureReasonIds { get { return _failureReasonIds; } }

        /// <summary>当前敌军 handle（只读遍历用）。</summary>
        public List<ModeHSpawnHandle> EnemyHandles { get { return _enemyHandles; } }

        /// <summary>当前选手 handle（只读遍历用）。</summary>
        public List<ModeHSpawnHandle> FighterHandles { get { return _fighterHandles; } }

        #endregion

        #region 事务边界

        /// <summary>开始一次生成事务。</summary>
        public bool Begin(ModeHSupportedMap map, int sceneGeneration, long ownerToken, out string failureReasonId)
        {
            failureReasonId = null;
            if (_begun && !_committed && !_cancelled)
            {
                failureReasonId = "spawn_tx_already_active";
                return false;
            }
            if (map == null)
            {
                failureReasonId = "spawn_tx_map_missing";
                return false;
            }

            _enemyHandles.Clear();
            _fighterHandles.Clear();
            _spawnedStableKeys.Clear();
            _failureReasonIds.Clear();
            _map = map;
            _sceneGeneration = sceneGeneration;
            _ownerToken = ownerToken;
            _begun = true;
            _committed = false;
            _cancelled = false;
            return true;
        }

        /// <summary>
        /// 分帧生成一批角色。每帧最多创建一个，创建后立即校验引用；
        /// 任一失败整批逆序回收并返回 false。
        /// </summary>
        public IEnumerator SpawnBatch(
            IList<CharacterRandomPreset> presets,
            IList<string> stableKeys,
            Teams team,
            bool isFighter,
            ModeHSpawnDiagnostics diagnostics,
            ModeHSpawnBatchResult result)
        {
            if (result == null) yield break;
            result.Success = false;
            result.FailureReasonId = null;

            if (!IsActive)
            {
                result.FailureReasonId = "spawn_tx_not_active";
                yield break;
            }
            if (presets == null || stableKeys == null || presets.Count != stableKeys.Count)
            {
                result.FailureReasonId = "spawn_tx_batch_mismatch";
                yield break;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                if (!IsActive)
                {
                    result.FailureReasonId = "spawn_tx_cancelled";
                    yield break;
                }

                int cap = isFighter
                    ? ModeHConfig.MaxConcurrentFighterInstances
                    : ModeHConfig.MaxConcurrentEnemyInstances;
                int current = isFighter ? _fighterHandles.Count : _enemyHandles.Count;
                if (current >= cap)
                {
                    result.FailureReasonId = isFighter ? "spawn_fighter_cap" : "spawn_enemy_cap";
                    RecordFailure(result.FailureReasonId);
                    RollbackAll();
                    yield break;
                }

                Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> task =
                    ModeHSpawnBridge.CreateIsolatedAsync(
                        presets[i], stableKeys[i], team, _map.StagingPos, diagnostics);

                // 每帧最多一个角色：等待本次创建完成后再进入下一次
                while (task.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
                {
                    yield return null;
                }

                ModeHSpawnHandle handle = null;
                bool faulted = false;
                try
                {
                    handle = task.GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    faulted = true;
                    RecordFailure("spawn_await_failed:" + e.GetType().Name);
                }

                if (faulted || handle == null || handle.Character == null || handle.Health == null)
                {
                    result.FailureReasonId = "spawn_create_failed:" + stableKeys[i];
                    RecordFailure(result.FailureReasonId);
                    if (handle != null) ModeHSpawnBridge.Recycle(handle);
                    RollbackAll();
                    yield break;
                }

                if (diagnostics != null && diagnostics.HasWindowSideEffects())
                {
                    result.FailureReasonId = "spawn_window_side_effect:" + stableKeys[i];
                    RecordFailure(result.FailureReasonId);
                    ModeHSpawnBridge.Recycle(handle);
                    RollbackAll();
                    yield break;
                }

                if (isFighter) _fighterHandles.Add(handle);
                else _enemyHandles.Add(handle);
                _spawnedStableKeys.Add(stableKeys[i]);

                // 分帧：下一次创建放到下一帧
                yield return null;
            }

            result.Success = true;
        }

        /// <summary>
        /// 提交：把已隔离角色放到擂台点位、清理强制追踪、核对双向敌对，
        /// 最后在同一帧统一激活。任一步失败整批回收并返回 false。
        /// </summary>
        public bool TryCommit(IList<Vector3> enemyPositions, Vector3 fighterPosition, out string failureReasonId)
        {
            failureReasonId = null;
            if (!IsActive)
            {
                failureReasonId = "spawn_tx_not_active";
                return false;
            }

            try
            {
                for (int i = 0; i < _enemyHandles.Count; i++)
                {
                    Vector3 pos = enemyPositions != null && i < enemyPositions.Count
                        ? enemyPositions[i]
                        : _map.ArenaCenter;
                    if (!ModeHSpawnBridge.TryPrepareForArena(_enemyHandles[i], pos, out failureReasonId))
                    {
                        RecordFailure(failureReasonId);
                        RollbackAll();
                        return false;
                    }
                }

                for (int i = 0; i < _fighterHandles.Count; i++)
                {
                    if (!ModeHSpawnBridge.TryPrepareForArena(_fighterHandles[i], fighterPosition, out failureReasonId))
                    {
                        RecordFailure(failureReasonId);
                        RollbackAll();
                        return false;
                    }
                }

                // 廉价护栏：scav 与 wolf 必然双向敌对（Team.IsEnemy 是纯函数），
                // 真正的核对是下一帧再读一次阵营是否仍然成立（由调用方在下一帧执行）。
                if (!VerifyTeamsAssigned(out failureReasonId))
                {
                    RecordFailure(failureReasonId);
                    RollbackAll();
                    return false;
                }

                // 同一帧按确定顺序激活：先敌军后选手
                for (int i = 0; i < _enemyHandles.Count; i++)
                {
                    if (!ModeHSpawnBridge.TryActivate(_enemyHandles[i], out failureReasonId))
                    {
                        RecordFailure(failureReasonId);
                        RollbackAll();
                        return false;
                    }
                }
                for (int i = 0; i < _fighterHandles.Count; i++)
                {
                    if (!ModeHSpawnBridge.TryActivate(_fighterHandles[i], out failureReasonId))
                    {
                        RecordFailure(failureReasonId);
                        RollbackAll();
                        return false;
                    }
                }

                _committed = true;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "spawn_commit_exception:" + e.GetType().Name;
                RecordFailure(failureReasonId);
                RollbackAll();
                return false;
            }
        }

        /// <summary>
        /// 下一帧阵营稳定核对：重新读取双方 team，确认没有被原版逻辑改回中立或改成同阵营。
        /// ERROR 互换期间受控选手会被原版改写为 Teams.all，调用方需按 §17.6.5 记为预期。
        /// </summary>
        public bool VerifyTeamsStableNextFrame(bool allowControlledAllTeam, out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                for (int i = 0; i < _enemyHandles.Count; i++)
                {
                    ModeHSpawnHandle handle = _enemyHandles[i];
                    if (handle == null || handle.Character == null) continue;
                    if (handle.Character.Team != Teams.wolf)
                    {
                        failureReasonId = "spawn_enemy_team_drift";
                        return false;
                    }
                }
                for (int i = 0; i < _fighterHandles.Count; i++)
                {
                    ModeHSpawnHandle handle = _fighterHandles[i];
                    if (handle == null || handle.Character == null) continue;
                    Teams team = handle.Character.Team;
                    if (team == Teams.scav) continue;
                    if (allowControlledAllTeam && team == Teams.all) continue;
                    failureReasonId = "spawn_fighter_team_drift";
                    return false;
                }

                // 廉价护栏：双向敌对断言
                if (_enemyHandles.Count > 0 && _fighterHandles.Count > 0)
                {
                    if (!Team.IsEnemy(Teams.scav, Teams.wolf) || !Team.IsEnemy(Teams.wolf, Teams.scav))
                    {
                        failureReasonId = "spawn_team_enemy_rule_broken";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "spawn_team_verify_exception:" + e.GetType().Name;
                return false;
            }
        }

        private bool VerifyTeamsAssigned(out string failureReasonId)
        {
            failureReasonId = null;
            for (int i = 0; i < _enemyHandles.Count; i++)
            {
                ModeHSpawnHandle handle = _enemyHandles[i];
                if (handle == null || handle.Character == null)
                {
                    failureReasonId = "spawn_enemy_handle_invalid";
                    return false;
                }
                if (handle.Character.Team != Teams.wolf)
                {
                    failureReasonId = "spawn_enemy_team_not_wolf";
                    return false;
                }
            }
            for (int i = 0; i < _fighterHandles.Count; i++)
            {
                ModeHSpawnHandle handle = _fighterHandles[i];
                if (handle == null || handle.Character == null)
                {
                    failureReasonId = "spawn_fighter_handle_invalid";
                    return false;
                }
                if (handle.Character.Team != Teams.scav)
                {
                    failureReasonId = "spawn_fighter_team_not_scav";
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region 回收

        /// <summary>整批逆序回收（含 runtime preset clone），并标记事务取消。</summary>
        public void RollbackAll()
        {
            for (int i = _fighterHandles.Count - 1; i >= 0; i--)
            {
                ModeHSpawnBridge.Recycle(_fighterHandles[i]);
            }
            for (int i = _enemyHandles.Count - 1; i >= 0; i--)
            {
                ModeHSpawnBridge.Recycle(_enemyHandles[i]);
            }
            _fighterHandles.Clear();
            _enemyHandles.Clear();
            _spawnedStableKeys.Clear();
            _cancelled = true;
            _committed = false;
        }

        /// <summary>回收单个已倒地/已替换的选手实例（不影响整批事务）。</summary>
        public void RecycleFighter(ModeHSpawnHandle handle)
        {
            if (handle == null) return;
            _fighterHandles.Remove(handle);
            ModeHSpawnBridge.Recycle(handle);
        }

        /// <summary>回收单个已死亡的敌军实例。</summary>
        public void RecycleEnemy(ModeHSpawnHandle handle)
        {
            if (handle == null) return;
            _enemyHandles.Remove(handle);
            ModeHSpawnBridge.Recycle(handle);
        }

        /// <summary>场景切换 / 技术中止：立即取消并整批回收。</summary>
        public void Cancel()
        {
            RollbackAll();
            _begun = false;
        }

        private void RecordFailure(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return;
            if (!_failureReasonIds.Contains(reasonId)) _failureReasonIds.Add(reasonId);
        }

        #endregion
    }

    /// <summary>分帧生成协程的结果载体（协程不能有 out 参数）。</summary>
    internal sealed class ModeHSpawnBatchResult
    {
        /// <summary>本批是否成功。</summary>
        public bool Success;
        /// <summary>失败原因。</summary>
        public string FailureReasonId;
    }
}
