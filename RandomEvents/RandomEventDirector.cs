// ============================================================================
// RandomEventDirector.cs - 随机事件调度器（方案二 步骤 4）
// ============================================================================
// 状态机：
//   Dormant ──签名 0→非0──> Armed（开局冷却 90s）
//   Armed ──计时到 + 抽取成功──> EventActive（并发恒 1）
//   EventActive ──到时/异常──> Cooldown（45~75s）──> Armed
//   任意态 ──签名非0→0 / 签名变化 / 切图 / 关开关 / 宿主销毁──> 全量清理 + Dormant
//
// 硬约束：
//   - **局开始/结束用 run signature 边缘轮询**，不侵入任何模式的启停代码；
//     签名只经 RandomEventModeGate 这一个公开门面组合而成；
//   - Dormant / Armed 每帧只有 float 累加 + bool 比较，**零分配、零日志**（AGENTS 4.12）；
//     真正读模式门面被节流到 RunSignaturePollIntervalSeconds 一次；
//   - **不认 IsBossRushArenaActive 空闲态**（门控里已排除）；
//   - OnSceneChanged 里 generation++，作废一切在途异步续作；
//   - 计时一律用受 timeScale 影响的 deltaTime（与官方 GameClock 同源），由 host 传入，
//     并过单帧保险帽 MaxDeltaPerFrame，挡加载完成后的首帧尖峰；
//   - 权重轮盘为本调度器自有实现，**不复用变异词条的抽样器**
//     （两者生命周期语义相反，共用会破坏 AGENTS 4.11 不变式）；
//   - 每一次 OnTrigger 成功都必然配一次 OnCleanup：五条结束路径全部汇聚到 EndActiveEvent；
//   - 本文件不得引用任何波次状态机符号（tests/RandomEventsWaveIsolationGuard.py 守卫）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>随机事件调度器。每个 ModBehaviour 只有一个实例，由运行时模块持有。</summary>
    internal sealed class RandomEventDirector
    {
        #region 状态

        private readonly ModBehaviour _owner;

        private RandomEventPhase _phase;
        private int _generation;
        private int _runSignature;

        /// <summary>Armed / Cooldown 的倒计时剩余秒数。</summary>
        private float _phaseTimer;

        /// <summary>签名轮询节流累加器。</summary>
        private float _signaturePollTimer;

        private int _eventsFiredThisRun;
        private int _maxEventsThisRun;

        private RandomEventBase _activeEvent;
        private RandomEventContext _activeContext;

        /// <summary>上一次成功触发的事件 id，用于避免连刷同一个。</summary>
        private RandomEventId _lastTriggeredId;

        /// <summary>当前事件的 OnTick 已抛过异常：只记一次日志，之后不再调用，避免每帧刷屏。</summary>
        private bool _activeTickFaulted;

        // 抽取用复用缓冲：轮盘只在触发瞬间跑，缓冲复用是为了不给 GC 添无谓垃圾
        private readonly List<RandomEventBase> _pickBuffer = new List<RandomEventBase>(8);
        private readonly List<float> _pickWeights = new List<float>(8);

        /// <summary>F3 调试用的 id 列表缓存（只读快照，构建一次）。</summary>
        private static List<RandomEventId> _cachedEventIds;

        #endregion

        #region 构造

        internal RandomEventDirector(ModBehaviour owner)
        {
            _owner = owner;
            _phase = RandomEventPhase.Dormant;
            _generation = 1;
            _runSignature = 0;
            _phaseTimer = 0f;
            _signaturePollTimer = RandomEventsTuning.RunSignaturePollIntervalSeconds;
            _eventsFiredThisRun = 0;
            _maxEventsThisRun = 0;
            _lastTriggeredId = RandomEventId.None;
        }

        #endregion

        #region 只读门面（HUD / F3 / guard 用）

        /// <summary>当前相位。</summary>
        internal RandomEventPhase Phase { get { return _phase; } }

        /// <summary>当前活动事件 id；无事件时为 None。</summary>
        internal RandomEventId ActiveEventId
        {
            get { return _activeEvent != null ? _activeEvent.Id : RandomEventId.None; }
        }

        /// <summary>当前活动事件显示名；无事件时为 null。no-throw。</summary>
        internal string ActiveEventDisplayName
        {
            get
            {
                try
                {
                    return _activeEvent != null ? _activeEvent.DisplayName : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>当前活动事件剩余秒数；无事件时为 0。</summary>
        internal float ActiveRemainingSeconds
        {
            get { return _activeContext != null ? _activeContext.RemainingSeconds : 0f; }
        }

        /// <summary>本局已触发的事件数。</summary>
        internal int EventsFiredThisRun { get { return _eventsFiredThisRun; } }

        /// <summary>本局事件上限（由频率档决定）。</summary>
        internal int MaxEventsThisRun { get { return _maxEventsThisRun; } }

        /// <summary>调度器 generation：异步续作的作废判据之一。</summary>
        internal int Generation { get { return _generation; } }

        /// <summary>当前 run signature：异步续作的作废判据之一；0 表示不在允许的局内。</summary>
        internal int CurrentRunSignature { get { return _runSignature; } }

        #endregion

        #region 驱动

        /// <summary>
        /// host tick。Dormant / Armed 时只做 float 累加与 bool 比较，零分配、零日志。
        /// deltaTime 必须是受 timeScale 影响的那一份。
        /// </summary>
        internal void Tick(float deltaTime)
        {
            float dt = deltaTime;
            if (dt < 0f) dt = 0f;
            if (dt > RandomEventsTuning.MaxDeltaPerFrame) dt = RandomEventsTuning.MaxDeltaPerFrame;

            // ── 签名边缘轮询（节流）─────────────────────────────────────────
            _signaturePollTimer += dt;
            if (_signaturePollTimer >= RandomEventsTuning.RunSignaturePollIntervalSeconds)
            {
                _signaturePollTimer = 0f;
                int signature = RandomEventModeGate.ComputeRunSignature(_owner);
                if (signature != _runSignature)
                {
                    HandleRunSignatureChanged(signature);
                }
            }

            if (_runSignature == 0)
            {
                // Dormant：到此为止，本帧成本 = 一次 float 加 + 两次比较
                return;
            }

            switch (_phase)
            {
                case RandomEventPhase.Armed:
                    TickArmed(dt);
                    break;
                case RandomEventPhase.EventActive:
                    TickEventActive(dt);
                    break;
                case RandomEventPhase.Cooldown:
                    TickCooldown(dt);
                    break;
                default:
                    // Dormant 但签名非 0：签名边缘刚被处理过的极短窗口，等下一次轮询即可
                    break;
            }
        }

        /// <summary>
        /// 场景回调：generation++ 作废全部在途异步续作，强制结束在跑事件并回 Dormant。
        /// 下一次轮询会因为签名从 0 变回非 0 而重新走开局冷却。
        /// </summary>
        internal void OnSceneChanged()
        {
            try
            {
                _generation++;
                EndActiveEvent(RandomEventEndReason.SceneChanged);
                _phase = RandomEventPhase.Dormant;
                _runSignature = 0;
                _phaseTimer = 0f;
                _eventsFiredThisRun = 0;
                _maxEventsThisRun = 0;
                _lastTriggeredId = RandomEventId.None;
                // 强制下一帧立刻重新识别签名，而不是再等一个轮询间隔
                _signaturePollTimer = RandomEventsTuning.RunSignaturePollIntervalSeconds;
            }
            catch (Exception e)
            {
                LogFailure("scene_changed", e);
            }
        }

        /// <summary>
        /// 开关被关 / 宿主销毁：结束在跑事件并回 Dormant。幂等，可重复调用。
        /// </summary>
        internal void ShutdownRuntime(RandomEventEndReason reason)
        {
            try
            {
                _generation++;
                EndActiveEvent(reason);
                _phase = RandomEventPhase.Dormant;
                _runSignature = 0;
                _phaseTimer = 0f;
                _eventsFiredThisRun = 0;
                _maxEventsThisRun = 0;
                _lastTriggeredId = RandomEventId.None;
                _signaturePollTimer = RandomEventsTuning.RunSignaturePollIntervalSeconds;
            }
            catch (Exception e)
            {
                LogFailure("shutdown", e);
            }
        }

        #endregion

        #region 相位

        private void TickArmed(float dt)
        {
            if (_eventsFiredThisRun >= _maxEventsThisRun)
            {
                // 本局配额已用完：永久停在 Armed，连计时都不再推进
                return;
            }

            _phaseTimer -= dt;
            if (_phaseTimer > 0f) return;

            _phaseTimer = 0f;
            if (!TryStartRandomEvent())
            {
                EnterCooldown();
            }
        }

        private void TickEventActive(float dt)
        {
            RandomEventContext ctx = _activeContext;
            RandomEventBase evt = _activeEvent;
            if (ctx == null || evt == null)
            {
                // 防御：活动态却没有上下文，直接回冷却，避免卡死在 EventActive
                EndActiveEvent(RandomEventEndReason.TriggerFailed);
                EnterCooldown();
                return;
            }

            ctx.ElapsedSeconds += dt;

            if (!_activeTickFaulted)
            {
                try
                {
                    evt.OnTick(ctx, dt);
                }
                catch (Exception e)
                {
                    // 只记一次：热路径不允许每帧刷日志
                    _activeTickFaulted = true;
                    LogFailure("event_tick", e);
                }
            }

            if (ctx.ElapsedSeconds >= ctx.DurationSeconds)
            {
                EndActiveEvent(RandomEventEndReason.Expired);
                EnterCooldown();
            }
        }

        private void TickCooldown(float dt)
        {
            _phaseTimer -= dt;
            if (_phaseTimer > 0f) return;
            EnterArmed(0f);
        }

        private void EnterArmed(float delaySeconds)
        {
            _phase = RandomEventPhase.Armed;
            _phaseTimer = delaySeconds > 0f ? delaySeconds : 0f;
        }

        private void EnterCooldown()
        {
            _phase = RandomEventPhase.Cooldown;
            _phaseTimer = UnityEngine.Random.Range(
                RandomEventsTuning.CooldownMinSeconds,
                RandomEventsTuning.CooldownMaxSeconds);
        }

        #endregion

        #region 签名边缘

        /// <summary>
        /// 签名边缘处理：
        ///   非0 → 0    局末：全量清理（结束在跑事件、清计数、回 Dormant）
        ///   0 → 非0    开局：重置计数、按频率档取上限、进入开局冷却
        ///   非0 → 非0' 换局/换图：先按局末清理，再按开局重置
        /// </summary>
        private void HandleRunSignatureChanged(int signature)
        {
            try
            {
                _runSignature = signature;
                _generation++;

                // 无论哪种边缘，先把在跑的事件按「局结束」收干净
                EndActiveEvent(RandomEventEndReason.RunEnded);
                _lastTriggeredId = RandomEventId.None;

                if (signature == 0)
                {
                    _phase = RandomEventPhase.Dormant;
                    _phaseTimer = 0f;
                    _eventsFiredThisRun = 0;
                    _maxEventsThisRun = 0;
                    return;
                }

                _eventsFiredThisRun = 0;
                _maxEventsThisRun = ResolveMaxEventsThisRun();
                EnterArmed(RandomEventsTuning.OpeningArmDelaySeconds);
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "识别到局开始，开局冷却 " + RandomEventsTuning.OpeningArmDelaySeconds
                    + "s，本局事件上限 " + _maxEventsThisRun);
            }
            catch (Exception e)
            {
                LogFailure("run_signature_changed", e);
            }
        }

        /// <summary>按频率档解析本局事件上限。no-throw，异常回落默认档。</summary>
        private int ResolveMaxEventsThisRun()
        {
            try
            {
                int[] table = RandomEventsTuning.MaxEventsPerRunByFrequency;
                if (table == null || table.Length <= 1) return RandomEventsTuning.DefaultMaxEventsPerRun;

                int tier = _owner != null
                    ? _owner.GetRandomEventsFrequencyTier()
                    : 2;
                if (tier < 1) tier = 1;
                if (tier >= table.Length) tier = table.Length - 1;
                return table[tier];
            }
            catch (Exception)
            {
                return RandomEventsTuning.DefaultMaxEventsPerRun;
            }
        }

        #endregion

        #region 触发

        /// <summary>
        /// 尝试开一个事件。并发恒 1：非 Armed 一律拒绝。
        /// 返回 false 时调用方负责回冷却；触发失败不计入单局上限。
        /// </summary>
        private bool TryStartRandomEvent()
        {
            if (_phase != RandomEventPhase.Armed) return false;
            if (_activeEvent != null || _activeContext != null) return false;
            if (_eventsFiredThisRun >= _maxEventsThisRun) return false;

            RandomEventContext ctx = null;
            try
            {
                string blockReason;
                if (!RandomEventModeGate.IsEventsAllowed(_owner, out blockReason))
                {
                    return false;
                }
                if (!IsPlayerReady()) return false;

                RandomEventBase evt = PickWeightedEvent();
                if (evt == null) return false;

                ctx = CreateContext(evt);
                if (ctx == null) return false;

                if (!evt.CanTrigger(ctx))
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                    return false;
                }

                if (!evt.OnTrigger(ctx))
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                    return false;
                }

                _activeEvent = evt;
                _activeContext = ctx;
                _activeTickFaulted = false;
                _phase = RandomEventPhase.EventActive;
                _lastTriggeredId = evt.Id;
                _eventsFiredThisRun++;

                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "事件触发: " + evt.Id
                    + "，时长 " + ctx.DurationSeconds + "s（本局 "
                    + _eventsFiredThisRun + "/" + _maxEventsThisRun + "）");
                return true;
            }
            catch (Exception e)
            {
                LogFailure("try_start", e);
                if (ctx != null)
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                }
                _activeEvent = null;
                _activeContext = null;
                return false;
            }
        }

        /// <summary>玩家在场且活着才调度：事件全部围绕玩家展开。no-throw。</summary>
        private bool IsPlayerReady()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return false;
                if (player.Health == null) return false;
                return !player.Health.IsDead;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private RandomEventContext CreateContext(RandomEventBase evt)
        {
            try
            {
                RandomEventContext ctx = new RandomEventContext();
                ctx.Owner = _owner;
                ctx.Generation = _generation;
                ctx.RunSignature = _runSignature;
                ctx.SceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
                ctx.ElapsedSeconds = 0f;
                ctx.DurationSeconds = evt.DurationSeconds;
                ctx.Scope = new RuntimeScope("RandomEvent_" + evt.Id, _owner);
                ctx.AnchorPosition = UnityEngine.Vector3.zero;
                return ctx;
            }
            catch (Exception e)
            {
                LogFailure("create_context", e);
                return null;
            }
        }

        /// <summary>回滚一个尚未上任的上下文：只清 Scope，不调 OnCleanup（事件还没成功生效）。</summary>
        private void DiscardContext(RandomEventContext ctx, RandomEventEndReason reason)
        {
            if (ctx == null || ctx.Scope == null) return;
            try
            {
                ctx.Scope.Clear(reason.ToString());
            }
            catch (Exception e)
            {
                LogFailure("discard_context", e);
            }
        }

        /// <summary>
        /// 权重轮盘抽取。默认排除上一次触发的 id，避免连刷同一个；
        /// 若排除后没有候选则放宽这一条重来一次。no-throw，抽不到返回 null。
        /// </summary>
        private RandomEventBase PickWeightedEvent()
        {
            try
            {
                float total = BuildPickBuffer(true);
                if (_pickBuffer.Count == 0 || total <= 0f)
                {
                    total = BuildPickBuffer(false);
                }
                if (_pickBuffer.Count == 0 || total <= 0f) return null;

                float roll = UnityEngine.Random.Range(0f, total);
                float accumulated = 0f;
                for (int i = 0; i < _pickBuffer.Count; i++)
                {
                    accumulated += _pickWeights[i];
                    if (roll <= accumulated) return _pickBuffer[i];
                }
                return _pickBuffer[_pickBuffer.Count - 1];
            }
            catch (Exception e)
            {
                LogFailure("pick_event", e);
                return null;
            }
        }

        /// <summary>填充抽取缓冲并返回权重总和。excludeLast 为 true 时跳过上一次触发的 id。</summary>
        private float BuildPickBuffer(bool excludeLast)
        {
            _pickBuffer.Clear();
            _pickWeights.Clear();

            RandomEventBase[] all = RandomEventCatalog.GetAll();
            if (all == null) return 0f;

            float total = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                RandomEventBase evt = all[i];
                if (evt == null) continue;
                if (excludeLast && _lastTriggeredId != RandomEventId.None && evt.Id == _lastTriggeredId) continue;

                float weight;
                try
                {
                    weight = evt.Weight;
                }
                catch (Exception)
                {
                    continue;
                }
                if (weight <= 0f) continue;

                _pickBuffer.Add(evt);
                _pickWeights.Add(weight);
                total += weight;
            }
            return total;
        }

        #endregion

        #region 结束

        /// <summary>
        /// 结束当前事件。五条结束路径全部汇聚到这里，保证每次 OnTrigger 都配一次 OnCleanup。
        /// 幂等：没有活动事件时 O(1) 早返。
        /// </summary>
        private void EndActiveEvent(RandomEventEndReason reason)
        {
            RandomEventBase evt = _activeEvent;
            RandomEventContext ctx = _activeContext;
            _activeEvent = null;
            _activeContext = null;
            _activeTickFaulted = false;

            if (evt == null && ctx == null) return;

            if (evt != null && ctx != null)
            {
                try
                {
                    evt.OnCleanup(ctx, reason);
                }
                catch (Exception e)
                {
                    LogFailure("event_cleanup", e);
                }
            }

            // 二次保险：即使事件实现忘了清 Scope 或清到一半抛了，这里也把生成物收干净。
            // RuntimeScope.Clear 内部清空各列表，重复调用是 no-op。
            if (ctx != null && ctx.Scope != null)
            {
                try
                {
                    ctx.Scope.Clear(reason.ToString());
                }
                catch (Exception e)
                {
                    LogFailure("scope_clear", e);
                }
            }

            ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "事件结束: "
                + (evt != null ? evt.Id.ToString() : "unknown") + "，原因 " + reason);
        }

        #endregion

        #region 调试（F3）

        /// <summary>
        /// F3 强制触发指定事件。会先结束在跑事件。
        /// **不计入单局上限**：调试要能把 8 个事件逐个试过去。
        /// </summary>
        internal bool TryForceTrigger(RandomEventId id, out string failReason)
        {
            failReason = null;
            RandomEventContext ctx = null;
            try
            {
                if (_owner == null)
                {
                    failReason = RandomEventModeGate.ReasonQueryFailed;
                    return false;
                }

                string blockReason;
                if (!RandomEventModeGate.IsEventsAllowed(_owner, out blockReason))
                {
                    failReason = blockReason;
                    return false;
                }
                if (!IsPlayerReady())
                {
                    failReason = "player_not_ready";
                    return false;
                }

                // 强制触发可能发生在轮询到达之前，这里先把签名对齐，避免上下文带着过期签名出生
                int signature = RandomEventModeGate.ComputeRunSignature(_owner);
                if (signature == 0)
                {
                    failReason = RandomEventModeGate.ReasonNoRunActive;
                    return false;
                }
                if (signature != _runSignature)
                {
                    HandleRunSignatureChanged(signature);
                }

                EndActiveEvent(RandomEventEndReason.DebugForced);

                RandomEventBase evt = RandomEventCatalog.Find(id);
                if (evt == null)
                {
                    failReason = "event_not_found";
                    EnterCooldown();
                    return false;
                }

                ctx = CreateContext(evt);
                if (ctx == null)
                {
                    failReason = "context_failed";
                    EnterCooldown();
                    return false;
                }

                if (!evt.CanTrigger(ctx))
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                    failReason = "can_trigger_false";
                    EnterCooldown();
                    return false;
                }

                if (!evt.OnTrigger(ctx))
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                    failReason = "trigger_failed";
                    EnterCooldown();
                    return false;
                }

                _activeEvent = evt;
                _activeContext = ctx;
                _activeTickFaulted = false;
                _phase = RandomEventPhase.EventActive;
                _lastTriggeredId = evt.Id;
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[调试] 强制触发事件: " + evt.Id);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("force_trigger", e);
                if (ctx != null)
                {
                    DiscardContext(ctx, RandomEventEndReason.TriggerFailed);
                }
                _activeEvent = null;
                _activeContext = null;
                failReason = "exception";
                return false;
            }
        }

        /// <summary>F3 立即结束当前事件并进入冷却。幂等。</summary>
        internal void ForceEndActive()
        {
            try
            {
                bool hadActive = _phase == RandomEventPhase.EventActive;
                EndActiveEvent(RandomEventEndReason.DebugForced);
                if (hadActive && _runSignature != 0)
                {
                    EnterCooldown();
                }
            }
            catch (Exception e)
            {
                LogFailure("force_end", e);
            }
        }

        /// <summary>F3 菜单用：全部事件 id 的只读快照。no-throw，失败返回空表。</summary>
        internal IList<RandomEventId> GetAllEventIds()
        {
            try
            {
                if (_cachedEventIds != null) return _cachedEventIds;

                List<RandomEventId> ids = new List<RandomEventId>(8);
                RandomEventBase[] all = RandomEventCatalog.GetAll();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        ids.Add(all[i].Id);
                    }
                }
                _cachedEventIds = ids;
                return _cachedEventIds;
            }
            catch (Exception e)
            {
                LogFailure("get_all_ids", e);
                return new List<RandomEventId>();
            }
        }

        #endregion

        #region 静态缓存

        /// <summary>
        /// 静态缓存复位。宿主销毁时调用，避免热重载后残留旧目录实例。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                _cachedEventIds = null;
                RandomEventCatalog.ResetStaticCaches();
            }
            catch (Exception)
            {
                // 复位路径不得抛：宿主销毁阶段任何异常都可能拖崩退出流程
            }
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 调度器 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 连日志都失败时静默：绝不让诊断路径拖崩宿主
            }
        }

        #endregion
    }
}
