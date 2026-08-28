// ============================================================================
// PetNestCompanionAgent.cs - 遗种巢随从维护组件（实施计划 步骤 0 / 步骤 6）
// ============================================================================
// 作用：
//   - 驱动幼体随从跟随玩家：写官方公开字段 AICharacterController.leader，
//     由原生 Update（AICharacterController.cs:238）把 patrolPosition 同步成 leader
//     位置，行为树 patrol 分支据此跟随。索敌与作战完全交还原生行为树，
//     本组件不写 searchedEnemy，也不挂第二套战斗逻辑。
//   - >40m 传送兜底：patrol 只在 searchedEnemy == null 时跟随，崽追敌会跑远，
//     因此沿用官方 PetAI.Update 的同款阈值与落点公式自写一份兜底。
//   - 作为「玩家方随从」的身份标记：WavesArenaEnemyMaintenance 的清场豁免与
//     ModBehaviour 的敌对性安全网都据此放行（AGENTS.md 4.5）。
//   - 作为致死钳制链第四消费者的身份表：静态 armed bool + Health 引用集合，
//     未出战时热路径零分配早返。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 遗种巢随从维护组件。挂在幼体角色根上，与角色同寿命。
    ///
    /// 契约：
    /// - 跟随驱动只写 AICharacterController.leader（官方 public 字段），
    ///   不写 searchedEnemy、不写 leaderAI（后者会双向同步目标，
    ///   见 ModeH/ModeHSpawnBridge.cs:44-46 的同款理由）；
    /// - 维护节流到 4Hz，热路径零分配；
    /// - 静态身份表在 OnDestroy 必然退表，避免死引用累积。
    /// </summary>
    internal sealed class PetNestCompanionAgent : MonoBehaviour
    {
        #region 常量

        /// <summary>维护节流间隔（4Hz，与 ModeG HUD 同口径）。</summary>
        internal const float MaintainIntervalSeconds = 0.25f;

        /// <summary>超过该距离直接传送到主人身边（官方 PetAI.Update 同款阈值）。</summary>
        internal const float TeleportDistance = 40f;

        #endregion

        #region 静态身份表（致死钳制 / 清场豁免 / 敌对性安全网共用）

        private static readonly object _identityLock = new object();
        private static readonly HashSet<int> _companionHealthIds = new HashSet<int>();
        private static readonly HashSet<int> _companionCharacterIds = new HashSet<int>();
        private static int _armedCount;

        /// <summary>
        /// 是否有随从在场。零分配 bool 快路径：未带崽时致死钳制链直接早返。
        /// </summary>
        internal static bool IsCompanionArmed
        {
            get
            {
                try { return _armedCount > 0; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>该 Health 是否属于在场的遗种巢随从（O(1) 引用身份比较）。</summary>
        internal static bool IsCompanionHealth(Health health)
        {
            if (health == null) return false;
            try
            {
                if (_armedCount <= 0) return false;
                lock (_identityLock)
                {
                    return _companionHealthIds.Contains(health.GetInstanceID());
                }
            }
            catch (Exception)
            {
                // 身份表故障不得拖崩宿主受伤路径：查询失败按“不是随从”处理
                return false;
            }
        }

        /// <summary>该角色是否是遗种巢随从（清场豁免与敌对性安全网使用）。</summary>
        internal static bool IsCompanionCharacter(CharacterMainControl character)
        {
            if (character == null) return false;
            try
            {
                if (_armedCount > 0)
                {
                    lock (_identityLock)
                    {
                        if (_companionCharacterIds.Contains(character.GetInstanceID())) return true;
                    }
                }
                return character.GetComponent<PetNestCompanionAgent>() != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 致死钳制命中计数（PoC 探针与诊断读取）。步骤 7 接入 PetNestDownedHandler
        /// 之后，退场与战痕落档由该 handler 承接，本计数只保留为可观测量。
        /// </summary>
        internal static int LethalClampHitCount { get { return _lethalClampHitCount; } }

        private static int _lethalClampHitCount;

        /// <summary>
        /// 致死钳制链第四消费者命中回调。只记账 + DevLog，不在 Hurt 内部改场景状态。
        /// </summary>
        internal static void NotifyLethalClamped(Health health)
        {
            try
            {
                _lethalClampHitCount++;
                ModBehaviour.DevLog("[PetNest] 拦截随从致死伤害，钳至 1 血（重伤不死）");
            }
            catch (Exception)
            {
                // 记账失败不得打断宿主受伤流程
            }
        }

        /// <summary>清空静态身份表（Mod 卸载 / 宿主重建 / 静态缓存重置）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_identityLock)
            {
                _companionHealthIds.Clear();
                _companionCharacterIds.Clear();
                _armedCount = 0;
            }
            _lethalClampHitCount = 0;
        }

        #endregion

        #region 实例状态

        private CharacterMainControl _self;
        private CharacterMainControl _master;
        private AICharacterController _ai;
        private Health _health;
        private float _maintainTimer;
        private bool _identityRegistered;
        private bool _bound;

        /// <summary>当前主人（玩家）。</summary>
        internal CharacterMainControl Master { get { return _master; } }

        /// <summary>绑定的幼体角色。</summary>
        internal CharacterMainControl Self { get { return _self; } }

        #endregion

        #region 绑定

        /// <summary>
        /// 绑定自身角色与主人。幂等：重复调用只刷新引用，不重复登记身份表。
        /// </summary>
        internal void Bind(CharacterMainControl self, CharacterMainControl master)
        {
            _self = self;
            _master = master;
            _bound = true;
            _maintainTimer = 0f;

            try
            {
                _ai = self != null ? self.GetComponentInChildren<AICharacterController>() : null;
            }
            catch (Exception)
            {
                _ai = null;
            }

            try
            {
                _health = self != null ? self.Health : null;
            }
            catch (Exception)
            {
                _health = null;
            }

            RegisterIdentity();
            Maintain();
        }

        private void RegisterIdentity()
        {
            if (_identityRegistered) return;
            try
            {
                lock (_identityLock)
                {
                    if (_health != null) _companionHealthIds.Add(_health.GetInstanceID());
                    if (_self != null) _companionCharacterIds.Add(_self.GetInstanceID());
                    _armedCount++;
                }
                _identityRegistered = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 随从身份登记失败: " + e.Message);
            }
        }

        private void UnregisterIdentity()
        {
            if (!_identityRegistered) return;
            _identityRegistered = false;
            try
            {
                lock (_identityLock)
                {
                    if (_health != null) _companionHealthIds.Remove(_health.GetInstanceID());
                    if (_self != null) _companionCharacterIds.Remove(_self.GetInstanceID());
                    if (_armedCount > 0) _armedCount--;
                }
            }
            catch (Exception)
            {
                // 退表失败不阻断销毁
            }
        }

        #endregion

        #region 维护

        private void Update()
        {
            if (!_bound) return;

            _maintainTimer -= Time.deltaTime;
            if (_maintainTimer > 0f) return;
            _maintainTimer = MaintainIntervalSeconds;

            Maintain();
        }

        /// <summary>
        /// 一次维护：刷新主人引用 -> 写 leader -> >40m 传送兜底。
        /// no-throw，热路径零分配。
        /// </summary>
        private void Maintain()
        {
            try
            {
                if (_self == null) return;

                if (_master == null)
                {
                    _master = CharacterMainControl.Main;
                }
                if (_master == null) return;

                if (_ai == null)
                {
                    _ai = _self.GetComponentInChildren<AICharacterController>();
                }

                if (_ai != null && _ai.leader != _master)
                {
                    // 只写 leader：原生 Update 会在 searchedEnemy == null 时把
                    // patrolPosition 同步成 leader 位置，跟随由行为树 patrol 分支完成。
                    _ai.leader = _master;
                }

                float distance = Vector3.Distance(_self.transform.position, _master.transform.position);
                if (distance > TeleportDistance)
                {
                    TeleportToMaster();
                }
            }
            catch (Exception)
            {
                // 维护异常不得拖崩宿主 Update
            }
        }

        private void TeleportToMaster()
        {
            Vector3 target = _master.transform.position + Vector3.forward * 0.4f + Vector3.up * 0.5f;
            try
            {
                _self.SetPosition(target);
            }
            catch (Exception)
            {
                try { _self.transform.position = target; }
                catch (Exception)
                {
                    // 传送兜底本身失败：保持原位，下一次维护再试
                }
            }
        }

        #endregion

        #region 生命周期

        private void OnDestroy()
        {
            UnregisterIdentity();
            _bound = false;
            _self = null;
            _master = null;
            _ai = null;
            _health = null;
        }

        #endregion
    }
}
