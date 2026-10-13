// ============================================================================
// PetNestCompanionAgent.cs - 遗种巢随从维护组件（实施计划 步骤 0 / 步骤 6）
// ============================================================================
// 作用：
//   - 驱动幼体随从跟随玩家：写官方公开字段 AICharacterController.leader，
//     由原生 Update（AICharacterController.cs:238）把 patrolPosition 同步成 leader
//     位置，行为树 patrol 分支据此跟随。索敌与作战完全交还原生行为树，
//     仅在主角换队时清理已不敌对的旧目标，不挂第二套战斗逻辑。
//   - >40m 传送兜底：patrol 只在 searchedEnemy == null 时跟随，崽追敌会跑远，
//     因此沿用官方 PetAI.Update 的同款阈值与落点公式自写一份兜底。
//   - 作为「玩家方随从」的身份标记：WavesArenaEnemyMaintenance 的清场豁免与
//     ModBehaviour 的敌对性安全网都据此放行（AGENTS.md 4.5）。
//   - 作为致死钳制链第四消费者的身份表：静态 armed bool + Health 引用集合，
//     未出战时热路径零分配早返。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 遗种巢随从维护组件。挂在幼体角色根上，与角色同寿命。
    ///
    /// 契约：
    /// - 跟随驱动只写 AICharacterController.leader（官方 public 字段），
    ///   换队事件只清无效 searchedEnemy，不写 leaderAI（后者会双向同步目标，
    ///   见 ModeH/ModeHSpawnBridge.cs:44-46 的同款理由）；
    /// - 维护节流到 4Hz，热路径零分配；
    /// - 静态身份表在 OnDestroy 必然退表，避免死引用累积。
    /// </summary>
    internal sealed class PetNestCompanionAgent : MonoBehaviour
    {
        #region 常量

        /// <summary>维护节流间隔（4Hz，与 ModeG HUD 同口径）。</summary>
        internal const float MaintainIntervalSeconds = 0.25f;

        /// <summary>
        /// 超过该距离直接传送到主人身边（官方 PetAI.Update 同款阈值）。
        /// 这是**默认档**；性格会在 <see cref="ApplyPersonality"/> 里覆盖成自己的阈值
        /// （忠诚贴得更紧、懒散放得更开），表在 PetNest/PetNestPersonality.cs。
        /// </summary>
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
        private bool _allowCombat;
        private CharacterMainControl _teamOwner;

        // —— 行动方式（PetNestCompanionCare.cs）——
        // 设定之前保持加行动方式前的老行为（主动），由生成器在 Bind 之后按崽的记录写入。
        private PetNestBehaviour _behaviour = PetNestBehaviour.Active;
        private float _engagedUntil;
        private bool _aiSuspended;
        private bool _ownsInvincible;
        private float _nextCleanseAt;
        private DuckNpcMovement _movement;
        private bool _movementUnavailable;
        private bool _aiNotSuspendable;

        /// <summary>
        /// 本只崽的跟随传送阈值（平方值，省掉每次维护的一次开方）。
        /// 默认是 <see cref="TeleportDistance"/>，由 <see cref="ApplyPersonality"/> 按性格覆盖。
        /// </summary>
        private float _teleportDistanceSqr = TeleportDistance * TeleportDistance;

        /// <summary>当前主人（玩家）。</summary>
        internal CharacterMainControl Master { get { return _master; } }

        /// <summary>当前行动方式。</summary>
        internal PetNestBehaviour Behaviour { get { return _behaviour; } }

        /// <summary>绑定的幼体角色。</summary>
        internal CharacterMainControl Self { get { return _self; } }

        #endregion

        #region 绑定

        /// <summary>
        /// 按性格设置跟随距离。必须在 <see cref="Bind"/> 之前调（Bind 会立刻做一次维护）。
        /// profile 为 null 或数值非法时保持默认档。
        /// </summary>
        internal void ApplyPersonality(PetNestPersonalityProfile profile)
        {
            float distance = profile != null && profile.FollowTeleportDistance > 0f
                ? profile.FollowTeleportDistance
                : TeleportDistance;
            _teleportDistanceSqr = distance * distance;
        }

        /// <summary>
        /// 绑定自身角色与主人。幂等：重复调用只刷新引用，不重复登记身份表。
        /// </summary>
        internal void Bind(CharacterMainControl self, CharacterMainControl master, bool allowCombat = true)
        {
            _self = self;
            BindMaster(master);
            _allowCombat = allowCombat;
            _bound = true;
            _maintainTimer = 0f;

            try
            {
                _ai = allowCombat && self != null ? self.GetComponentInChildren<AICharacterController>() : null;
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

        private void BindMaster(CharacterMainControl master)
        {
            if (_teamOwner != master)
            {
                if (_teamOwner != null) _teamOwner.OnTeamChanged -= HandleMasterTeamChanged;
                _teamOwner = master;
                if (_teamOwner != null) _teamOwner.OnTeamChanged += HandleMasterTeamChanged;
            }
            _master = master;
            if (_master != null) HandleMasterTeamChanged(_master.Team);
        }

        private void HandleMasterTeamChanged(Teams team)
        {
            try
            {
                if (_self == null) return;
                if (_self.Team != team) _self.SetTeam(team);
                // Mode E 入场、雇佣或退场可能在宠物生成之后改主角阵营，立即清掉已不敌对的旧目标。
                if (_ai != null && _ai.searchedEnemy != null && !Team.IsEnemy(team, _ai.searchedEnemy.Team))
                {
                    _ai.searchedEnemy = null;
                    _ai.noticed = false;
                }
            }
            catch (Exception) { /* 角色正在回收，不打断阵营事件。 */ }
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

        #region 行动方式

        /// <summary>切换行动方式并立刻生效（只在局内可战斗时有意义；基地本来就不打）。</summary>
        internal void SetBehaviour(PetNestBehaviour behaviour)
        {
            _behaviour = behaviour;
            _engagedUntil = 0f;
            _nextCleanseAt = 0f;
            ApplyBehaviourState();
        }

        /// <summary>
        /// 主人或崽卷入战斗（PetNestCareService.NotifyHurt 调）。跟随档在这一刻放开战斗、锁定这个敌人，
        /// <see cref="PetNestTuning.BehaviourEngageWindowSeconds"/> 秒内没有新的交战就收手回到主人身边。
        /// </summary>
        internal void NotifyEngaged(CharacterMainControl enemy)
        {
            if (!_allowCombat || _behaviour != PetNestBehaviour.Follow || enemy == null) return;
            _engagedUntil = Time.time + PetNestTuning.BehaviourEngageWindowSeconds;
            ResumeAi();
            try
            {
                if (_ai != null && enemy.mainDamageReceiver != null)
                {
                    _ai.searchedEnemy = enemy.mainDamageReceiver;
                    _ai.noticed = true;
                }
            }
            catch (Exception) { /* 目标已回收，交给行为树自己索敌 */ }
        }

        /// <summary>4Hz 维护里按行动方式收放战斗 AI、跟随、无敌与回避清理。未变化时只有几次 bool 比较。</summary>
        private void ApplyBehaviourState()
        {
            if (!_allowCombat || _ai == null || _self == null) return;
            bool wantAi = _behaviour == PetNestBehaviour.Active
                || (_behaviour == PetNestBehaviour.Follow && Time.time < _engagedUntil);
            // 跟随档若这张图没法挂寻路跟随，就退回老行为，免得崽原地站桩（40 米传送兜底仍在）
            if (!wantAi && _behaviour == PetNestBehaviour.Follow && !EnsureMovement()) wantAi = true;
            if (wantAi) ResumeAi();
            else SuspendAi();

            bool avoid = _behaviour == PetNestBehaviour.Avoid;
            SetAvoidInvincible(avoid);
            if (avoid && Time.time >= _nextCleanseAt)
            {
                _nextCleanseAt = Time.time + PetNestTuning.AvoidCleanseIntervalSeconds;
                CleanseNegativeBuffs();
            }
        }

        private bool EnsureMovement()
        {
            if (_movement != null) return true;
            if (_movementUnavailable || _self == null) return false;
            try
            {
                _movement = _self.GetComponent<DuckNpcMovement>();
                if (_movement == null) _movement = _self.gameObject.AddComponent<DuckNpcMovement>();
                if (!_movement.Bind(_self, _self.transform.position, 2f))
                {
                    Destroy(_movement);
                    _movement = null;
                    _movementUnavailable = true;
                    return false;
                }
                _movement.enabled = false;
                return true;
            }
            catch (Exception)
            {
                _movement = null;
                _movementUnavailable = true;
                return false;
            }
        }

        /// <summary>停用整棵战斗 AI 子树（基地同款做法：只清目标会被行为树重新索敌），改由寻路跟随主人。</summary>
        private void SuspendAi()
        {
            if (_aiSuspended || _aiNotSuspendable || _ai == null) return;
            try
            {
                if (_ai.gameObject == _self.gameObject)
                {
                    // AI 挂在角色根上时停用它等于停用整只崽，只能放弃收手（回避档的无敌仍然生效）
                    _aiNotSuspendable = true;
                    return;
                }
                _ai.searchedEnemy = null;
                _ai.noticed = false;
                _self.Trigger(false, false, false);
                _self.SetMoveInput(Vector2.zero);
                _ai.gameObject.SetActive(false);
                _aiSuspended = true;
                if (EnsureMovement() && _master != null)
                {
                    _movement.enabled = true;
                    _movement.EnablePlayerFollow(_master.transform);
                }
            }
            catch (Exception) { /* 收手失败：下一次维护再试 */ }
        }

        private void ResumeAi()
        {
            if (!_aiSuspended || _ai == null) return;
            try
            {
                if (_movement != null)
                {
                    _movement.DisablePlayerFollow();
                    _movement.enabled = false;
                }
                // 停用前的目标可能早已失效，重启前清掉，让行为树从当前局面重新判断
                _ai.searchedEnemy = null;
                _ai.noticed = false;
                _ai.gameObject.SetActive(true);
                if (_master != null) _ai.leader = _master;
                _aiSuspended = false;
            }
            catch (Exception) { /* 恢复失败：下一次维护再试 */ }
        }

        private void SetAvoidInvincible(bool on)
        {
            if (_health == null || on == _ownsInvincible) return;
            try
            {
                // 只撤自己加的无敌：重伤退场那条路径有自己的无敌 owner（PetNestDownedHandler）
                if (on && _health.Invincible) return;
                _health.SetInvincible(on);
                _ownsInvincible = on;
            }
            catch (Exception) { /* 无敌切换失败不打断维护 */ }
        }

        private void CleanseNegativeBuffs()
        {
            try
            {
                Buff.BuffExclusiveTags[] tags = PetNestCareService.AvoidCleansedTags;
                for (int i = 0; i < tags.Length; i++) _self.RemoveBuffsByTag(tags[i], false);
            }
            catch (Exception) { /* 清理失败等下一轮 */ }
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
                    BindMaster(CharacterMainControl.Main);
                }
                if (_master == null) return;

                if (_allowCombat && _ai == null)
                {
                    _ai = _self.GetComponentInChildren<AICharacterController>();
                }

                if (_ai != null && _ai.leader != _master)
                {
                    // 只写 leader：原生 Update 会在 searchedEnemy == null 时把
                    // patrolPosition 同步成 leader 位置，跟随由行为树 patrol 分支完成。
                    _ai.leader = _master;
                }

                ApplyBehaviourState();

                // 平方比较：避免每次维护一次开方（与 NPCFollowMovementBaseSqr 的仓库惯例一致）
                Vector3 offset = _self.transform.position - _master.transform.position;
                if (offset.sqrMagnitude > _teleportDistanceSqr)
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
            if (_teamOwner != null) _teamOwner.OnTeamChanged -= HandleMasterTeamChanged;
            _teamOwner = null;
            UnregisterIdentity();
            if (_ownsInvincible && _health != null)
            {
                try { _health.SetInvincible(false); } catch (Exception) { /* 角色正在回收 */ }
            }
            _ownsInvincible = false;
            _bound = false;
            _self = null;
            _master = null;
            _ai = null;
            _health = null;
        }

        #endregion
    }
}
