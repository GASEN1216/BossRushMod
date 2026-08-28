using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>本场一名参赛者（我方选手或敌军）的路由记录。</summary>
    internal sealed class ModeHParticipantRef
    {
        /// <summary>我方选手的 profileId；敌军为空。</summary>
        public string ProfileId;
        /// <summary>官方预设稳定 key。</summary>
        public string StableKey;
        /// <summary>敌军的计划槽位序号；我方为 -1。</summary>
        public int PlanSlotIndex = -1;
        /// <summary>是否为敌军。</summary>
        public bool IsEnemy;
        /// <summary>是否为本场接力者。</summary>
        public bool IsRelay;
        /// <summary>角色引用（只用于身份比对，不做逻辑判断）。</summary>
        public CharacterMainControl Character;
    }

    /// <summary>Mode H 遥测接收端（由 ModeHCombatTelemetry 实现）。</summary>
    internal interface IModeHTelemetrySink
    {
        /// <summary>一次受伤。attacker 可为空。</summary>
        void OnParticipantHurt(ModeHParticipantRef target, ModeHParticipantRef attacker, float damageValue);

        /// <summary>一次死亡。killer 可为空。</summary>
        void OnParticipantDead(ModeHParticipantRef target, ModeHParticipantRef killer);
    }

    /// <summary>
    /// Mode H 事件路由（设计提案 §19.5、§25.1）。
    ///
    /// 冻结契约：
    /// - 官方 `Health.OnHurt` / `Health.OnDead` 是 **static event**，因此只在唯一
    ///   Mode H owner 上以命名 handler 订阅一次，禁止按每个角色重复静态订阅；
    /// - 由 `Health -> participant` 的 O(1) 注册表路由到本场目标，
    ///   旧场、看台身体与未登记对象的事件直接丢弃；
    /// - `ProductionCertifying` 的 diagnostic 注册表**查询优先**，命中只交认证，
    ///   绝不进入战斗遥测、结果 CAS、伤病/战痕或奖励路径；
    /// - 死亡帧原版顺序为 `OnDeadEvent -> OnDead -> SetActive(false) -> OnHurtEvent -> OnHurt`，
    ///   因此不得假设 OnHurt 先于 OnDead，也不得依赖 activeInHierarchy；
    /// - 同次伤害的多来源用 event token 去重；
    /// - 取消、切图、shutdown、Mod 销毁与订阅失败回滚都对称退订；
    /// - 不调用 `MutatorManager.RollAndApply`，不写共享 `MutatorContext`，
    ///   不复用全局 callback list。
    ///
    /// ERROR 归属修正（§17.6.5）：互换期间受控选手的伤害会被原版改写为
    /// `fromCharacter = CharacterMainControl.Main`；此时按互换状态映射回受控选手，
    /// 而不是当作看台身体事件丢弃，否则 relay_finisher / high_threat_core 归属会整场丢失。
    /// </summary>
    internal static class ModeHEventRouter
    {
        #region 状态

        private static readonly Dictionary<int, ModeHParticipantRef> _participants =
            new Dictionary<int, ModeHParticipantRef>();
        private static readonly Dictionary<int, string> _diagnostics = new Dictionary<int, string>();
        private static readonly HashSet<string> _consumedEventTokens =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool _subscribed;
        private static long _ownerToken;
        private static ModeHRunState _runState;
        private static int _sceneGeneration;
        private static int _matchIndex;
        private static IModeHTelemetrySink _sink;
        private static Action<Health, DamageInfo> _onHurtHandler;
        private static Action<Health, DamageInfo> _onDeadHandler;

        // ERROR 互换归属映射：互换期间被玩家接管的选手
        private static ModeHParticipantRef _errorSwapControlledParticipant;

        private static float _lastWarnTime;

        #endregion

        #region 只读

        /// <summary>是否已订阅官方 static 事件。</summary>
        public static bool IsSubscribed { get { return _subscribed; } }

        /// <summary>当前登记的战斗参赛者数量。</summary>
        public static int ParticipantCount { get { return _participants.Count; } }

        /// <summary>当前登记的认证诊断对象数量（正式开战前必须为 0）。</summary>
        public static int DiagnosticCount { get { return _diagnostics.Count; } }

        #endregion

        #region 订阅生命周期

        /// <summary>
        /// 幂等订阅：同一 owner 重复调用无副作用；换 owner 前必须先 Unbind。
        /// 订阅失败时回滚到未订阅状态（不留半订阅）。
        /// </summary>
        public static bool Bind(long ownerToken, IModeHTelemetrySink sink, out string failureReasonId)
        {
            failureReasonId = null;
            if (ownerToken == 0)
            {
                failureReasonId = "router_owner_invalid";
                return false;
            }
            if (_subscribed)
            {
                if (_ownerToken == ownerToken)
                {
                    _sink = sink;
                    return true;
                }
                failureReasonId = "router_owner_conflict";
                return false;
            }

            _ownerToken = ownerToken;
            _sink = sink;
            _onHurtHandler = HandleHealthHurt;
            _onDeadHandler = HandleHealthDead;
            try
            {
                Health.OnHurt += _onHurtHandler;
                Health.OnDead += _onDeadHandler;
                _subscribed = true;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "router_subscribe_failed:" + e.GetType().Name;
                // 回滚：把可能已挂上的一半退掉，保持“要么全订阅要么全未订阅”
                TryUnsubscribe();
                _ownerToken = 0;
                _sink = null;
                _onHurtHandler = null;
                _onDeadHandler = null;
                return false;
            }
        }

        /// <summary>
        /// 幂等退订：取消、切图、shutdown、Mod 销毁共用同一入口，重复调用安全。
        /// </summary>
        public static void Unbind()
        {
            TryUnsubscribe();
            _subscribed = false;
            _ownerToken = 0;
            _sink = null;
            _onHurtHandler = null;
            _onDeadHandler = null;
            _participants.Clear();
            _diagnostics.Clear();
            _consumedEventTokens.Clear();
            _errorSwapControlledParticipant = null;
            _runState = null;
            _sceneGeneration = 0;
            _matchIndex = 0;
        }

        private static void TryUnsubscribe()
        {
            if (_onHurtHandler != null)
            {
                try { Health.OnHurt -= _onHurtHandler; }
                catch (Exception)
                {
                    // 未订阅或宿主已销毁：退订目标已达成
                }
            }
            if (_onDeadHandler != null)
            {
                try { Health.OnDead -= _onDeadHandler; }
                catch (Exception)
                {
                    // 同上
                }
            }
        }

        /// <summary>
        /// 设置本场上下文。所有事件在进入遥测前都要过
        /// `runId + sceneGeneration + matchIndex` 三比对，旧场事件一律丢弃。
        /// </summary>
        public static void SetContext(ModeHRunState runState, int sceneGeneration, int matchIndex)
        {
            _runState = runState;
            _sceneGeneration = sceneGeneration;
            _matchIndex = matchIndex;
        }

        /// <summary>换场清理：只清参赛者与 token，不动订阅本身。</summary>
        public static void ClearMatchRegistry()
        {
            _participants.Clear();
            _consumedEventTokens.Clear();
            _errorSwapControlledParticipant = null;
        }

        /// <summary>静态缓存复位（宿主 OnDestroy 调用）。</summary>
        public static void ResetStaticCaches()
        {
            Unbind();
            _lastWarnTime = 0f;
        }

        #endregion

        #region 注册表

        /// <summary>登记一名战斗参赛者。</summary>
        public static void RegisterParticipant(Health health, ModeHParticipantRef reference)
        {
            if (health == null || reference == null) return;
            _participants[health.GetInstanceID()] = reference;
        }

        /// <summary>解除一名战斗参赛者。</summary>
        public static void UnregisterParticipant(Health health)
        {
            if (health == null) return;
            _participants.Remove(health.GetInstanceID());
        }

        /// <summary>
        /// 登记一个认证诊断对象。诊断注册表查询优先于战斗注册表，
        /// 命中后事件只写认证记录。
        /// </summary>
        public static void RegisterDiagnostic(Health health, string stableKey)
        {
            if (health == null || string.IsNullOrEmpty(stableKey)) return;
            _diagnostics[health.GetInstanceID()] = stableKey;
        }

        /// <summary>解除一个认证诊断对象（必须先于角色回收）。</summary>
        public static void UnregisterDiagnostic(Health health)
        {
            if (health == null) return;
            _diagnostics.Remove(health.GetInstanceID());
        }

        /// <summary>认证批次结束后清空诊断注册表；正式开战前必须为空。</summary>
        public static void ClearDiagnostics()
        {
            _diagnostics.Clear();
        }

        /// <summary>ERROR 互换期间登记被接管的选手，供归属映射使用。</summary>
        public static void SetErrorSwapControlledParticipant(ModeHParticipantRef participant)
        {
            _errorSwapControlledParticipant = participant;
        }

        #endregion

        #region 事件处理

        /// <summary>命名 handler：受伤。</summary>
        private static void HandleHealthHurt(Health health, DamageInfo info)
        {
            try
            {
                if (!IsContextValid() || health == null) return;

                int id = health.GetInstanceID();
                string diagnosticKey;
                if (_diagnostics.TryGetValue(id, out diagnosticKey))
                {
                    // 诊断优先：只写认证记录，绝不进入战斗遥测
                    ModeHProductionCertification.NotifyDiagnosticHurt(diagnosticKey, info.damageValue);
                    return;
                }

                ModeHParticipantRef target;
                if (!_participants.TryGetValue(id, out target)) return; // 看台身体与旧场对象没有登记

                if (!TryConsumeToken("hurt", id, info)) return;
                if (_sink != null) _sink.OnParticipantHurt(target, ResolveAttacker(info), info.damageValue);
            }
            catch (Exception e)
            {
                WarnThrottled("router_hurt_exception:" + e.GetType().Name);
            }
        }

        /// <summary>
        /// 命名 handler：死亡。注意死亡帧上 GameObject 已被原版 SetActive(false)，
        /// 因此这里绝不读 activeInHierarchy。
        /// </summary>
        private static void HandleHealthDead(Health health, DamageInfo info)
        {
            try
            {
                if (!IsContextValid() || health == null) return;

                int id = health.GetInstanceID();
                string diagnosticKey;
                if (_diagnostics.TryGetValue(id, out diagnosticKey))
                {
                    ModeHProductionCertification.NotifyDiagnosticDead(diagnosticKey);
                    return;
                }

                ModeHParticipantRef target;
                if (!_participants.TryGetValue(id, out target)) return;

                if (!TryConsumeToken("dead", id, info)) return;
                if (_sink != null) _sink.OnParticipantDead(target, ResolveAttacker(info));
            }
            catch (Exception e)
            {
                WarnThrottled("router_dead_exception:" + e.GetType().Name);
            }
        }

        /// <summary>
        /// 归属解析：正常按 `fromCharacter` 找登记项；
        /// ERROR 互换期间原版会把 `fromCharacter` 改写为 Main，此时映射回受控选手。
        /// </summary>
        private static ModeHParticipantRef ResolveAttacker(DamageInfo info)
        {
            CharacterMainControl from = info.fromCharacter;
            if (from == null) return null;

            if (_errorSwapControlledParticipant != null && IsMainCharacter(from))
            {
                return _errorSwapControlledParticipant;
            }

            foreach (KeyValuePair<int, ModeHParticipantRef> pair in _participants)
            {
                ModeHParticipantRef reference = pair.Value;
                if (reference != null && ReferenceEquals(reference.Character, from)) return reference;
            }
            return null;
        }

        private static bool IsMainCharacter(CharacterMainControl character)
        {
            try { return ReferenceEquals(character, CharacterMainControl.Main); }
            catch (Exception)
            {
                // 关卡尚未初始化时读 Main 会抛：按“不是玩家身体”处理
                return false;
            }
        }

        /// <summary>
        /// event token 去重：同一帧同一目标的同类事件只提交一次。
        /// token 与 §17.4 的 appliedEventTokenIds 是两层——这里只挡同帧多来源重复。
        /// </summary>
        private static bool TryConsumeToken(string kind, int targetId, DamageInfo info)
        {
            string token = kind + "|" + _matchIndex + "|" + targetId + "|" + Time.frameCount;
            if (_consumedEventTokens.Contains(token)) return false;
            _consumedEventTokens.Add(token);
            if (_consumedEventTokens.Count > 512) _consumedEventTokens.Clear();
            return true;
        }

        /// <summary>
        /// 事件门控：owner token + runId + sceneGeneration + matchIndex 四比对。
        /// 任一不符即丢弃，绝不让旧场或旧 run 的事件进入本场遥测。
        /// </summary>
        private static bool IsContextValid()
        {
            if (!_subscribed || _ownerToken == 0) return false;
            if (!ModeHRuntimeGates.IsModeHRunOwnerActive) return false;
            if (_runState == null) return false;
            if (!_runState.IsCallbackValid(_ownerToken, _runState.RunId, _sceneGeneration)) return false;
            return _runState.MatchIndex == _matchIndex;
        }

        private static void WarnThrottled(string reason)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWarnTime < ModeHConfig.DiagnosticLogIntervalSeconds) return;
            _lastWarnTime = now;
            ModBehaviour.CriticalLog("[ModeH] 事件路由异常: " + reason);
        }

        #endregion
    }
}
