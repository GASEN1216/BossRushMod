using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 原图隔离租约（设计提案 §19.2、§25.1）。
    ///
    /// 冻结契约：
    /// - 必须先于 spectator lease、正式 UI、生产认证与角色生成取得；
    /// - 在同一 scene generation 内完成：枚举并保存原图 spawner 及其原启用状态 -> 逐个冻结
    ///   -> 在 Mode H 角色尚未生成时清理并核对原生敌人 -> 确认擂台/staging/看台/退出边界
    ///   -> 登记 owner token 与 scene generation；
    /// - 获取失败按已完成步骤逆序回滚；原生敌人已被清理时不得在同场景回落普通 BossRush，
    ///   必须退款并从 modeHExitPos 或既有安全传送路径离场；
    /// - 活动期间只检查已登记 spawner 与晚到实例，不在 Update 里全场扫描；
    /// - 正常离场、技术中止、场景切换与 OnDestroy 都调用同一幂等释放入口；
    /// - 不接管 Mode E/F/G/Zombie 的刷怪器，也不改写 WavesArena 波次状态。
    /// </summary>
    internal sealed class ModeHArenaIsolationLease
    {
        #region 状态

        private readonly List<CharacterSpawnerRoot> _frozenSpawners = new List<CharacterSpawnerRoot>();
        private readonly List<bool> _frozenOriginalCreated = new List<bool>();
        private readonly List<bool> _frozenOriginalActive = new List<bool>();
        private readonly HashSet<int> _registeredSpawnerIds = new HashSet<int>();

        private static FieldInfo _createdField;
        private static bool _createdFieldResolved;

        private bool _acquired;
        private bool _released;
        private long _ownerToken;
        private int _sceneGeneration;
        private string _sceneName;
        private ModeHSupportedMap _map;
        private int _clearedNativeEnemies;
        private string _lastError;

        #endregion

        #region 只读

        /// <summary>租约是否有效。</summary>
        public bool IsActive { get { return _acquired && !_released; } }

        /// <summary>本次冻结的原图 spawner 数量。</summary>
        public int FrozenSpawnerCount { get { return _frozenSpawners.Count; } }

        /// <summary>本次清理的原生敌人数量。</summary>
        public int ClearedNativeEnemyCount { get { return _clearedNativeEnemies; } }

        /// <summary>最后一次失败原因。</summary>
        public string LastError { get { return _lastError; } }

        /// <summary>本次隔离绑定的地图。</summary>
        public ModeHSupportedMap Map { get { return _map; } }

        /// <summary>是否已经清理过原生敌人（决定失败后能否回落 Legacy）。</summary>
        public bool HasClearedNativeEnemies { get { return _clearedNativeEnemies > 0; } }

        #endregion

        #region 获取

        /// <summary>
        /// 取得隔离租约。任何一步失败都会逆序回滚并返回 false。
        /// </summary>
        public bool TryAcquire(string sceneName, int sceneGeneration, long ownerToken, out string failureReasonId)
        {
            failureReasonId = null;
            if (_acquired)
            {
                failureReasonId = "isolation_already_acquired";
                return false;
            }

            _sceneName = sceneName;
            _sceneGeneration = sceneGeneration;
            _ownerToken = ownerToken;

            int step = 0;
            try
            {
                // 步骤 1：地图点位审计
                if (!ModeHMapSupportRegistry.TryGetMap(sceneName, out _map) || _map == null)
                {
                    failureReasonId = "isolation_map_unsupported";
                    return false;
                }
                step = 1;

                // 步骤 2：冻结原图 spawner（保存原状态以便回滚/恢复）
                if (!FreezeNativeSpawners(out failureReasonId))
                {
                    RollbackTo(step);
                    return false;
                }
                step = 2;

                // 步骤 3：清理原生敌人（必须在 Mode H 角色生成之前）
                if (!ClearNativeEnemies(out failureReasonId))
                {
                    RollbackTo(step);
                    return false;
                }
                step = 3;

                // 步骤 4：核对场景边界
                if (!VerifyArenaBoundaries(out failureReasonId))
                {
                    RollbackTo(step);
                    return false;
                }

                _acquired = true;
                _released = false;
                _lastError = null;
                ModBehaviour.DevLog("[ModeH] 隔离租约已取得: scene=" + sceneName
                    + " spawners=" + _frozenSpawners.Count + " cleared=" + _clearedNativeEnemies);
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "isolation_exception:" + e.GetType().Name;
                _lastError = failureReasonId;
                RollbackTo(step);
                return false;
            }
        }

        private bool FreezeNativeSpawners(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                CharacterSpawnerRoot[] roots = ObjectCache.GetCharacterSpawnerRoots();
                if (roots == null)
                {
                    return true; // 该图没有原生 spawner，视为已隔离
                }

                EnsureCreatedField();
                for (int i = 0; i < roots.Length; i++)
                {
                    CharacterSpawnerRoot root = roots[i];
                    if (root == null || root.gameObject == null) continue;

                    bool originalCreated = false;
                    if (_createdField != null)
                    {
                        object raw = _createdField.GetValue(root);
                        if (raw is bool) originalCreated = (bool)raw;
                    }
                    bool originalActive = root.gameObject.activeSelf;

                    _frozenSpawners.Add(root);
                    _frozenOriginalCreated.Add(originalCreated);
                    _frozenOriginalActive.Add(originalActive);
                    _registeredSpawnerIds.Add(root.GetInstanceID());

                    // 标记 created=true 阻止刷怪，并停用对象；两者都可逆
                    if (_createdField != null)
                    {
                        _createdField.SetValue(root, true);
                    }
                    root.gameObject.SetActive(false);
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "isolation_freeze_failed:" + e.GetType().Name;
                _lastError = failureReasonId;
                return false;
            }
        }

        private bool ClearNativeEnemies(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (characters == null) return true;

                CharacterMainControl player = null;
                try { player = CharacterMainControl.Main; }
                catch (Exception)
                {
                    // 取不到玩家引用：见下面的 fail-closed 判定
                }

                // 认不出玩家就绝不清场。旧实现在 player == null 时跳过引用比较继续销毁，
                // 会把**玩家自己**一起 Destroy；注释却写着「不会误伤」。
                // 隔离失败可以退款离场重来，销毁玩家角色不可逆。
                if (player == null)
                {
                    failureReasonId = "isolation_player_unresolved";
                    _lastError = failureReasonId;
                    return false;
                }

                int cleared = 0;
                for (int i = 0; i < characters.Length; i++)
                {
                    CharacterMainControl character = characters[i];
                    if (character == null || character.gameObject == null) continue;
                    if (object.ReferenceEquals(character, player)) continue;

                    try
                    {
                        UnityEngine.Object.Destroy(character.gameObject);
                        cleared++;
                    }
                    catch (Exception)
                    {
                        // 单个角色销毁失败不阻断整体隔离，稍后由晚到实例检查兜底
                    }
                }
                _clearedNativeEnemies = cleared;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "isolation_clear_failed:" + e.GetType().Name;
                _lastError = failureReasonId;
                return false;
            }
        }

        private bool VerifyArenaBoundaries(out string failureReasonId)
        {
            failureReasonId = null;
            if (_map == null)
            {
                failureReasonId = "isolation_map_missing";
                return false;
            }
            if (_map.ArenaSpawnPoints == null || _map.ArenaSpawnPoints.Length == 0)
            {
                failureReasonId = "isolation_arena_points_missing";
                return false;
            }
            float stagingToArena = Vector3.Distance(_map.StagingPos, _map.ArenaCenter);
            float stagingToSpectator = Vector3.Distance(_map.StagingPos, _map.SpectatorPos);
            if (stagingToArena < ModeHMapSupportRegistry.MinStagingIsolationDistance
                || stagingToSpectator < ModeHMapSupportRegistry.MinStagingIsolationDistance)
            {
                failureReasonId = "isolation_staging_too_close";
                return false;
            }
            return true;
        }

        #endregion

        #region 活动期检查

        /// <summary>
        /// 活动期轻量检查：只看已登记 spawner 是否被重新启用，以及是否出现晚到原生 spawner。
        /// 不做全场扫描；发现异常返回 false，由调用方停止生成并进入技术恢复。
        /// </summary>
        public bool CheckStillIsolated(int currentSceneGeneration, out string failureReasonId)
        {
            failureReasonId = null;
            if (!IsActive)
            {
                failureReasonId = "isolation_not_active";
                return false;
            }
            if (currentSceneGeneration != _sceneGeneration)
            {
                failureReasonId = "isolation_scene_generation_mismatch";
                return false;
            }

            try
            {
                for (int i = 0; i < _frozenSpawners.Count; i++)
                {
                    CharacterSpawnerRoot root = _frozenSpawners[i];
                    if (root == null || root.gameObject == null) continue;
                    if (root.gameObject.activeSelf)
                    {
                        failureReasonId = "isolation_spawner_reactivated";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "isolation_check_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 晚到 spawner 检查（低频调用）：发现未登记的原生 spawner 时立即冻结并返回 false，
        /// 由调用方停止生成并进入技术恢复。
        /// </summary>
        public bool CheckLateSpawners(out string failureReasonId)
        {
            failureReasonId = null;
            if (!IsActive) return true;
            try
            {
                CharacterSpawnerRoot[] roots = ObjectCache.GetCharacterSpawnerRoots();
                if (roots == null) return true;
                EnsureCreatedField();

                bool foundLate = false;
                for (int i = 0; i < roots.Length; i++)
                {
                    CharacterSpawnerRoot root = roots[i];
                    if (root == null || root.gameObject == null) continue;
                    if (_registeredSpawnerIds.Contains(root.GetInstanceID())) continue;

                    foundLate = true;
                    _frozenSpawners.Add(root);
                    _frozenOriginalCreated.Add(false);
                    _frozenOriginalActive.Add(root.gameObject.activeSelf);
                    _registeredSpawnerIds.Add(root.GetInstanceID());
                    if (_createdField != null) _createdField.SetValue(root, true);
                    root.gameObject.SetActive(false);
                }

                if (foundLate)
                {
                    failureReasonId = "isolation_late_spawner";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "isolation_late_check_exception:" + e.GetType().Name;
                return false;
            }
        }

        #endregion

        #region 释放与回滚

        /// <summary>
        /// 幂等释放。仍在同一 scene generation 时恢复已保存的 spawner 状态；
        /// generation 已变化时只清空 owner 与存活登记，不写旧 Unity 引用。
        /// </summary>
        public void Release(int currentSceneGeneration)
        {
            if (_released) return;
            _released = true;

            bool sameGeneration = currentSceneGeneration == _sceneGeneration;
            if (sameGeneration)
            {
                RestoreSpawners();
            }

            _frozenSpawners.Clear();
            _frozenOriginalCreated.Clear();
            _frozenOriginalActive.Clear();
            _registeredSpawnerIds.Clear();
            _acquired = false;
            _ownerToken = 0;
            _map = null;
        }

        private void RollbackTo(int completedStep)
        {
            // 逆序回滚：先恢复 spawner，再清空登记
            if (completedStep >= 2)
            {
                RestoreSpawners();
            }
            _frozenSpawners.Clear();
            _frozenOriginalCreated.Clear();
            _frozenOriginalActive.Clear();
            _registeredSpawnerIds.Clear();
            _acquired = false;
            _map = null;
        }

        private void RestoreSpawners()
        {
            EnsureCreatedField();
            for (int i = 0; i < _frozenSpawners.Count; i++)
            {
                CharacterSpawnerRoot root = _frozenSpawners[i];
                if (root == null || root.gameObject == null) continue;
                try
                {
                    if (_createdField != null && i < _frozenOriginalCreated.Count)
                    {
                        _createdField.SetValue(root, _frozenOriginalCreated[i]);
                    }
                    if (i < _frozenOriginalActive.Count)
                    {
                        root.gameObject.SetActive(_frozenOriginalActive[i]);
                    }
                }
                catch (Exception)
                {
                    // 单个 spawner 恢复失败不阻断释放流程
                }
            }
        }

        private static void EnsureCreatedField()
        {
            if (_createdFieldResolved) return;
            _createdFieldResolved = true;
            try
            {
                _createdField = typeof(CharacterSpawnerRoot).GetField(
                    "created", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception)
            {
                _createdField = null;
            }
        }

        #endregion
    }
}
