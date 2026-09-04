using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一个正在生效的伤病/战痕窗口。</summary>
    internal sealed class ModeHActiveWindow
    {
        /// <summary>条目稳定 ID（injuryId 或 scarId）。</summary>
        public string EntryId;
        /// <summary>是否为战痕（否则为伤病）。</summary>
        public bool IsScar;
        /// <summary>复用口令适配器的同一套调制/重申/还原设施。</summary>
        public ModeHCommandAdapter Adapter;
        /// <summary>剩余秒数（伤病为整场）。</summary>
        public float RemainingSeconds;
    }

    /// <summary>
    /// Mode H 伤病与战痕系统（设计提案 §17.4、§25.1）。
    ///
    /// 冻结契约：
    /// - 战斗效果**复用 `ModeHCommandAdapters` 的同一套重申/还原设施**，
    ///   禁止另建第二条字段修改路径；
    /// - `armor` 与 `spirit` 完全由 Mode H 自结算，对任何 stable key 恒为 `VerifiedBehavior`；
    ///   `leg`、`hand`、`old_wound` 依赖原版字段，按 stable key 验证；
    /// - 任一生产 key 的可用伤病少于 3 条时内容不可用；
    /// - 伤病与战痕不设 `PartiallyVerified`：任一分量不可用则整条不进抽池，
    ///   不允许“收益生效、代价失效”；
    /// - 只有本场从未实际踏入擂台的选手才算完整休息；
    /// - `Injured=true` 的选手再次实际登场并被击倒时直接 `Retired`，不叠加第三层伤势；
    /// - 胆怯逃跑不增加身体伤病；ERROR 接管期间的击倒仍归属被接管选手；
    /// - 战痕最多三条，满三条时接受新战痕必须明确替换一条；
    /// - 拒绝候选获得 `fameDisplayCount +1`（上限 99），名声不影响战斗/赔率/奖励/市场。
    /// </summary>
    internal sealed class ModeHInjuryAndScarSystem
    {
        #region 状态

        private readonly List<ModeHActiveWindow> _activeWindows = new List<ModeHActiveWindow>();

        /// <summary>本系统自带的空上下文，只在还没收到战斗控制器转发时兜底。</summary>
        private readonly ModeHCommandFireContext _fireContext = new ModeHCommandFireContext();

        /// <summary>
        /// 战斗控制器每帧转发进来的**活**上下文（它的 _fireContext 是 readonly，
        /// 引用恒定，缓存安全）。开窗时的首次点火必须用它：只用本系统的空上下文，
        /// 带 `fire_lowest_health_target` 的战痕在开窗那一下会空转，
        /// 要等下一次重申（≤ CommandReassertIntervalSeconds）才补上。
        /// </summary>
        private ModeHCommandFireContext _sharedFireContext;

        private readonly HashSet<string> _consumedTriggers = new HashSet<string>(StringComparer.Ordinal);

        private AICharacterController _ai;
        private string _activeProfileId;
        private string _activeStableKey;
        private int _matchIndex;
        private float _selfSettledCommandScale = 1f;
        private bool _armorKitDisabled;

        #endregion

        #region 只读

        /// <summary>
        /// Mode H 自结算的口令调制幅度系数（`spirit` x0.85、`bell_dependence` x1.2 等）。
        /// 由口令控制器在 Apply 时读取。
        /// </summary>
        public float SelfSettledCommandScale { get { return _selfSettledCommandScale; } }

        /// <summary>本场 `Armor` 槽虚拟 kit 是否被 `armor` 伤病禁用。</summary>
        public bool IsArmorKitDisabled { get { return _armorKitDisabled; } }

        /// <summary>当前生效窗口数量。</summary>
        public int ActiveWindowCount { get { return _activeWindows.Count; } }

        #endregion

        #region 本场生命周期

        /// <summary>
        /// 绑定本场登场选手。切换先发/接力者时重复调用：会先还原上一位的全部窗口。
        /// </summary>
        public void BindFighter(
            AICharacterController ai, string profileId, string stableKey, int matchIndex)
        {
            RestoreAll();
            _ai = ai;
            _activeProfileId = profileId;
            _activeStableKey = stableKey;
            _matchIndex = matchIndex;
            _selfSettledCommandScale = 1f;
            _armorKitDisabled = false;
        }

        /// <summary>
        /// 施加该选手的既有伤病（整场生效）。`old_wound` 是触发型，不在这里施加。
        /// </summary>
        public bool ApplyStandingInjury(string injuryId, out string failureReasonId)
        {
            failureReasonId = null;
            if (string.IsNullOrEmpty(injuryId)) return true;

            ModeHInjurySpec spec = GetInjury(injuryId);
            if (spec == null)
            {
                failureReasonId = "injury_spec_missing:" + injuryId;
                return false;
            }
            if (!IsEntryUsable(spec.Components))
            {
                failureReasonId = "injury_not_verified:" + injuryId;
                return false;
            }
            if (string.Equals(spec.Scope, "triggered_once", StringComparison.Ordinal)) return true;

            // 带敌军数量门的伤病（当前只有 spirit）由 OnEnemyCountChanged 按条件施加
            // 自结算系数，这里必须告诉 OpenWindow 别再施加一次，否则 x0.85 会叠成 x0.7225。
            bool gated = spec.RequiresEnemyCountAtLeast > 0;
            return OpenWindow(injuryId, false, spec.Components, ModeHConfig.MatchDurationSeconds,
                out failureReasonId, gated);
        }

        /// <summary>施加该选手已持有的全部战痕（按窗口类型分为常驻与触发型）。</summary>
        public bool ApplyStandingScars(IList<string> scarIds, out string failureReasonId)
        {
            failureReasonId = null;
            if (scarIds == null) return true;
            for (int i = 0; i < scarIds.Count; i++)
            {
                ModeHScarSpec spec = GetScar(scarIds[i]);
                if (spec == null) continue;
                if (!IsEntryUsable(spec.Components))
                {
                    // 任一分量不可用则整条不生效（不允许只生效一半的利弊绑定）
                    continue;
                }
                if (spec.WindowSeconds > 0) continue; // 触发型窗口等条件命中再开
                if (!OpenWindow(scarIds[i], true, spec.Components, ModeHConfig.MatchDurationSeconds,
                        out failureReasonId))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>推进全部窗口；窗口结束由 adapter 自身幂等还原。</summary>
        public void Tick(float deltaTime, ModeHCommandFireContext fireContext)
        {
            if (fireContext != null) _sharedFireContext = fireContext;
            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                ModeHActiveWindow window = _activeWindows[i];
                if (window == null || window.Adapter == null)
                {
                    _activeWindows.RemoveAt(i);
                    continue;
                }
                window.Adapter.Tick(deltaTime, fireContext != null ? fireContext : _fireContext);
                window.RemainingSeconds = window.Adapter.WindowRemainingSeconds;
                if (!window.Adapter.IsActive) _activeWindows.RemoveAt(i);
            }
        }

        /// <summary>
        /// 幂等还原：窗口结束、倒地、接力、技术中止、切图与 shutdown 共用同一入口。
        /// </summary>
        public void RestoreAll()
        {
            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                ModeHActiveWindow window = _activeWindows[i];
                if (window != null && window.Adapter != null) window.Adapter.Restore();
            }
            _activeWindows.Clear();
            _consumedTriggers.Clear();
            _selfSettledCommandScale = 1f;
            _armorKitDisabled = false;
            _ai = null;
        }

        private bool OpenWindow(
            string entryId, bool isScar, IList<ModeHEffectSpec> components, float windowSeconds,
            out string failureReasonId, bool gatedByCondition = false)
        {
            failureReasonId = null;
            if (_ai == null)
            {
                failureReasonId = "window_no_active_fighter";
                return false;
            }

            // 自结算分量不写原版字段，只调整 Mode H 自己的系数
            ApplySelfSettledComponents(entryId, components, gatedByCondition);

            List<ModeHEffectSpec> engineComponents = new List<ModeHEffectSpec>();
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null && !components[i].SelfSettled) engineComponents.Add(components[i]);
            }
            if (engineComponents.Count == 0) return true; // 纯自结算条目，无需 adapter

            ModeHCommandAdapter adapter = new ModeHCommandAdapter();
            if (!adapter.ApplyEffects(_ai, entryId, engineComponents, windowSeconds, 1f,
                    _sharedFireContext != null ? _sharedFireContext : _fireContext,
                    out failureReasonId))
            {
                return false;
            }

            ModeHActiveWindow window = new ModeHActiveWindow();
            window.EntryId = entryId;
            window.IsScar = isScar;
            window.Adapter = adapter;
            window.RemainingSeconds = windowSeconds;
            _activeWindows.Add(window);
            return true;
        }

        /// <summary>
        /// Mode H 自结算分量：`armor` 禁用本场 Armor 槽 kit，
        /// `spirit`/`bell_dependence`/`center_keeper`/`blood_rush` 调整口令调制幅度。
        /// </summary>
        private void ApplySelfSettledComponents(
            string entryId, IList<ModeHEffectSpec> components, bool gatedByCondition)
        {
            for (int i = 0; i < components.Count; i++)
            {
                ModeHEffectSpec component = components[i];
                if (component == null || !component.SelfSettled) continue;

                // 自结算分量的条件在开窗时一次性求值。这**只**对整场恒定的条件成立
                // （condition_* 擂台条件族），而 ModeHScarTriggerWiringGuard 正是断言
                // 自结算分量只能带这一类条件——否则这里就需要像调制类分量那样
                // 随重申持续求值，而 _selfSettledCommandScale 是个累乘标量，做不到只撤销其中一项。
                if (!ModeHEffectConditions.IsSatisfied(
                        component.AppliesWhen,
                        _sharedFireContext != null ? _sharedFireContext : _fireContext))
                {
                    continue;
                }

                if (string.Equals(component.Op, "self_settled_kit_slot_disabled", StringComparison.Ordinal))
                {
                    if (string.Equals(component.TargetSlot, "Armor", StringComparison.Ordinal))
                    {
                        _armorKitDisabled = true;
                    }
                    continue;
                }
                // 两种写法都表示"按 multiplierMilli 缩放口令调制幅度"：
                //   op = self_settled_command_scale  —— 显式命名
                //   op = self_settled + controlPointId = command_scale —— 按控制点区分
                // 数据表里两种都在用（bell_dependence 与 spirit 用后者）。
                // 2026-09-03 之前只认前者，于是 bell_dependence 的 +20% 收益从未生效，
                // 而它的 -10% skillSuccessChance 代价照常生效——一条纯负面的"利弊绑定"。
                bool isCommandScale =
                    string.Equals(component.Op, "self_settled_command_scale", StringComparison.Ordinal)
                    || (string.Equals(component.Op, "self_settled", StringComparison.Ordinal)
                        && string.Equals(component.ControlPointId, "command_scale", StringComparison.Ordinal));
                if (isCommandScale)
                {
                    // 带条件门的条目（当前只有 spirit：requiresEnemyCountAtLeast）
                    // 由各自的专用路径按条件施加，这里跳过，否则会叠加两次。
                    if (gatedByCondition) continue;
                    float multiplier = component.MultiplierMilli > 0 ? component.MultiplierMilli / 1000f : 1f;
                    _selfSettledCommandScale *= multiplier;
                }
            }
        }

        #endregion

        #region 触发型窗口

        /// <summary>
        /// `old_wound`：生命首次低于 35% 时施加 `baseReactionTime +0.15`，本场不再撤销。
        /// </summary>
        public void OnHealthFractionChanged(string injuryId, float healthFraction)
        {
            if (!string.Equals(injuryId, "old_wound", StringComparison.Ordinal)) return;
            if (healthFraction > ModeHConfig.OldWoundTriggerHealthFraction) return;
            if (!_consumedTriggers.Add("old_wound|" + _activeProfileId)) return;

            ModeHInjurySpec spec = GetInjury("old_wound");
            if (spec == null || !IsEntryUsable(spec.Components)) return;
            string reason;
            OpenWindow("old_wound", false, spec.Components, ModeHConfig.MatchDurationSeconds, out reason);
        }

        /// <summary>
        /// `spirit`：敌方同时在场 >= 2 时本场所有口令调制幅度 x0.85（Mode H 自结算）。
        /// 每场只判定一次，不随敌军数量反复开关。
        /// </summary>
        public void OnEnemyCountChanged(string injuryId, int liveEnemyCount)
        {
            if (!string.Equals(injuryId, "spirit", StringComparison.Ordinal)) return;
            if (liveEnemyCount < ModeHConfig.SpiritInjuryEnemyThreshold) return;
            if (!_consumedTriggers.Add("spirit|" + _activeProfileId)) return;
            _selfSettledCommandScale *= ModeHConfig.SpiritInjuryCommandScale;
        }

        /// <summary>
        /// 按触发条件开启一条战痕窗口。同一触发本场只消费一次。
        /// </summary>
        public bool TryOpenScarWindow(string scarId, string triggerId, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHScarSpec spec = GetScar(scarId);
            if (spec == null)
            {
                failureReasonId = "scar_spec_missing:" + scarId;
                return false;
            }
            if (!string.Equals(spec.Trigger, triggerId, StringComparison.Ordinal)) return false;
            if (!IsEntryUsable(spec.Components))
            {
                failureReasonId = "scar_not_verified:" + scarId;
                return false;
            }
            if (!_consumedTriggers.Add(scarId + "|" + _activeProfileId + "|" + _matchIndex)) return false;

            float window = spec.WindowSeconds > 0 ? spec.WindowSeconds : ModeHConfig.MatchDurationSeconds;
            return OpenWindow(scarId, true, spec.Components, window, out failureReasonId);
        }

        /// <summary>导出当前战痕窗口状态（战场快照采集用）。</summary>
        public List<ModeHScarWindowStateDto> ExportScarWindows()
        {
            List<ModeHScarWindowStateDto> result = new List<ModeHScarWindowStateDto>();
            for (int i = 0; i < _activeWindows.Count; i++)
            {
                ModeHActiveWindow window = _activeWindows[i];
                if (window == null || !window.IsScar) continue;
                ModeHScarWindowStateDto dto = new ModeHScarWindowStateDto();
                dto.scarId = window.EntryId;
                dto.remainingSeconds = window.RemainingSeconds;
                result.Add(dto);
            }
            result.Sort(CompareScarWindow);
            return result;
        }

        private static int CompareScarWindow(ModeHScarWindowStateDto a, ModeHScarWindowStateDto b)
        {
            return string.CompareOrdinal(a.scarId, b.scarId);
        }

        /// <summary>按快照恢复战痕窗口（重建后续接，不重复触发一次性判定）。</summary>
        public void RestoreScarWindows(IList<ModeHScarWindowStateDto> windows)
        {
            if (windows == null) return;
            for (int i = 0; i < windows.Count; i++)
            {
                ModeHScarWindowStateDto dto = windows[i];
                if (dto == null || dto.remainingSeconds <= 0f) continue;
                ModeHScarSpec spec = GetScar(dto.scarId);
                if (spec == null || !IsEntryUsable(spec.Components)) continue;
                _consumedTriggers.Add(dto.scarId + "|" + _activeProfileId + "|" + _matchIndex);
                string reason;
                OpenWindow(dto.scarId, true, spec.Components, dto.remainingSeconds, out reason);
            }
        }

        #endregion

        #region 伤病判定与退役

        /// <summary>
        /// 倒地结算：第一次被 `ModeHFighterDownToken` 确认时记一条公开伤病并置 Injured；
        /// 已带伤且再次实际登场并被击倒时直接退役。
        /// 胆怯逃跑不走这条路径（不增加身体伤病）。
        /// </summary>
        public ModeHInjuryEventDto ResolveDownEvent(
            ModeHProfileDto profile, long runSeed, int matchIndex, string downToken, int eventSequence)
        {
            if (profile == null || string.IsNullOrEmpty(downToken)) return null;

            ModeHInjuryEventDto evt = new ModeHInjuryEventDto();
            evt.profileId = profile.profileId;
            evt.downToken = downToken;
            evt.eventSequence = eventSequence;

            if (profile.status == (int)ModeHParticipantStatus.Injured)
            {
                // 带伤再次登场被击倒：直接退役，不叠加第三层随机伤势
                profile.status = (int)ModeHParticipantStatus.Retired;
                evt.injuryId = profile.injuryId != null ? profile.injuryId : string.Empty;
                evt.retired = true;
                return evt;
            }

            string injuryId = PickInjury(profile, runSeed, matchIndex);
            profile.injuryId = injuryId;
            profile.status = (int)ModeHParticipantStatus.Injured;
            evt.injuryId = injuryId;
            evt.retired = false;
            return evt;
        }

        /// <summary>
        /// 完整休息结算：带伤选手本场列入名单但**从未踏入擂台**时，赛后解除带伤。
        ///
        /// owner 裁决（2026-08-17 濒退制 / 2026-08-18 判定口径）：首次倒地进入带伤，
        /// 休息一场解除带伤，带伤**实际再登场**后倒地才赛季退役；「实际登场」包含
        /// 自动接力后的短暂登场，因此判据取 `ModeHCombatTelemetry.HasRested`
        /// （本场 entrant 名单里没有它）而不是「是否被列入 starter/relay」。
        ///
        /// 与 <see cref="ResolveDownEvent"/> 天然互斥：有倒地 token 就意味着登场过，
        /// 那时 HasRested 为 false，两条路径不会同时改同一个 profile。
        /// 返回 true 表示确实发生了一次解除（供调用方决定是否展示「完整休息」）。
        /// </summary>
        public bool ResolveRestRecovery(ModeHProfileDto profile)
        {
            if (profile == null) return false;
            if (profile.status != (int)ModeHParticipantStatus.Injured) return false;

            profile.injuryId = string.Empty;
            profile.status = (int)ModeHParticipantStatus.Available;
            return true;
        }

        /// <summary>
        /// 从该 key 可用的伤病中确定性抽一条。可用条目少于 3 条时返回空串，
        /// 由调用方按 `IsModeHContentReady=false` 处理。
        /// </summary>
        public static string PickInjury(ModeHProfileDto profile, long runSeed, int matchIndex)
        {
            List<string> usable = GetUsableInjuryIds(profile != null ? profile.stableKey : null);
            if (usable.Count < ModeHConfig.MinUsableInjuriesPerKey) return string.Empty;
            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, ModeHSeedStream.Domains.Scar, matchIndex * 100);
            return usable[stream.NextInt(usable.Count)];
        }

        /// <summary>该 stable key 当前可用的伤病 ID（ordinal 升序）。</summary>
        public static List<string> GetUsableInjuryIds(string stableKey)
        {
            List<string> usable = new List<string>();
            List<ModeHInjurySpec> injuries = ModeHContentCatalog.Injuries;
            if (injuries == null) return usable;
            for (int i = 0; i < injuries.Count; i++)
            {
                ModeHInjurySpec spec = injuries[i];
                if (spec == null || string.IsNullOrEmpty(spec.InjuryId)) continue;
                if (!IsEntryUsableForKey(stableKey, spec.Components)) continue;
                usable.Add(spec.InjuryId);
            }
            usable.Sort(StringComparer.Ordinal);
            return usable;
        }

        /// <summary>该 key 的可用伤病是否达到 3 条门槛（§17.4）。</summary>
        public static bool MeetsInjuryGate(string stableKey)
        {
            return GetUsableInjuryIds(stableKey).Count >= ModeHConfig.MinUsableInjuriesPerKey;
        }

        #endregion

        #region 战痕候选

        /// <summary>
        /// 战后候选：从与该选手原型兼容且全部分量可用的战痕中按 seed 固定抽一条，
        /// 不能重抽。已持有的战痕不再重复提供。
        /// </summary>
        public static string PickScarOffer(
            ModeHProfileDto profile, long runSeed, int matchIndex, out string failureReasonId)
        {
            failureReasonId = null;
            if (profile == null)
            {
                failureReasonId = "scar_offer_no_profile";
                return string.Empty;
            }

            List<string> candidates = new List<string>();
            List<ModeHScarSpec> scars = ModeHContentCatalog.Scars;
            if (scars == null)
            {
                failureReasonId = "scar_catalog_missing";
                return string.Empty;
            }
            for (int i = 0; i < scars.Count; i++)
            {
                ModeHScarSpec spec = scars[i];
                if (spec == null || string.IsNullOrEmpty(spec.ScarId)) continue;
                if (profile.scarIds != null && profile.scarIds.Contains(spec.ScarId)) continue;
                if (spec.CompatibleArchetypeIds != null && spec.CompatibleArchetypeIds.Count > 0
                    && !spec.CompatibleArchetypeIds.Contains(profile.archetypeId))
                {
                    continue;
                }
                if (!IsEntryUsableForKey(profile.stableKey, spec.Components)) continue;
                candidates.Add(spec.ScarId);
            }
            if (candidates.Count == 0)
            {
                failureReasonId = "scar_offer_no_candidate";
                return string.Empty;
            }
            candidates.Sort(StringComparer.Ordinal);
            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, ModeHSeedStream.Domains.Scar, matchIndex);
            return candidates[stream.NextInt(candidates.Count)];
        }

        /// <summary>
        /// 接受一条战痕。满三条时必须显式指定被替换的 scarId，不能堆叠同 scarId。
        /// </summary>
        public static bool TryAcceptScar(
            ModeHProfileDto profile, string scarId, string replacedScarId, out string failureReasonId)
        {
            failureReasonId = null;
            if (profile == null || string.IsNullOrEmpty(scarId))
            {
                failureReasonId = "scar_accept_invalid";
                return false;
            }
            if (profile.scarIds == null) profile.scarIds = new List<string>();
            if (profile.scarIds.Contains(scarId))
            {
                failureReasonId = "scar_accept_duplicate";
                return false;
            }
            if (profile.scarIds.Count >= ModeHConfig.MaxScarsPerProfile)
            {
                if (string.IsNullOrEmpty(replacedScarId) || !profile.scarIds.Contains(replacedScarId))
                {
                    failureReasonId = "scar_accept_requires_replacement";
                    return false;
                }
                profile.scarIds.Remove(replacedScarId);
            }
            profile.scarIds.Add(scarId);
            profile.scarIds.Sort(StringComparer.Ordinal);
            return true;
        }

        /// <summary>拒绝候选：稳定名声 +1，上限 99；名声不影响战斗/赔率/奖励/市场。</summary>
        public static void DeclineScar(ModeHProfileDto profile)
        {
            if (profile == null) return;
            int next = profile.fameDisplayCount + ModeHConfig.ScarDeclineFameGain;
            profile.fameDisplayCount = next > ModeHConfig.MaxFameDisplayCount
                ? ModeHConfig.MaxFameDisplayCount
                : next;
        }

        #endregion

        #region 可用性判定

        /// <summary>当前登场选手的条目是否可用。</summary>
        private bool IsEntryUsable(IList<ModeHEffectSpec> components)
        {
            return IsEntryUsableForKey(_activeStableKey, components);
        }

        /// <summary>
        /// 伤病与战痕不设 `PartiallyVerified`：任一分量对该 key 不可用，整条就不进抽池。
        /// 自结算分量对任何 key 恒可用。
        /// </summary>
        public static bool IsEntryUsableForKey(string stableKey, IList<ModeHEffectSpec> components)
        {
            if (components == null || components.Count == 0) return false;
            for (int i = 0; i < components.Count; i++)
            {
                ModeHEffectSpec component = components[i];
                if (component == null) return false;
                if (component.SelfSettled) continue;
                if (string.IsNullOrEmpty(stableKey)) return false;
                if (!ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(stableKey, component.EffectId))
                {
                    return false;
                }
            }
            return true;
        }

        private static ModeHInjurySpec GetInjury(string injuryId)
        {
            List<ModeHInjurySpec> injuries = ModeHContentCatalog.Injuries;
            if (injuries == null || string.IsNullOrEmpty(injuryId)) return null;
            for (int i = 0; i < injuries.Count; i++)
            {
                if (injuries[i] != null
                    && string.Equals(injuries[i].InjuryId, injuryId, StringComparison.Ordinal))
                {
                    return injuries[i];
                }
            }
            return null;
        }

        private static ModeHScarSpec GetScar(string scarId)
        {
            List<ModeHScarSpec> scars = ModeHContentCatalog.Scars;
            if (scars == null || string.IsNullOrEmpty(scarId)) return null;
            for (int i = 0; i < scars.Count; i++)
            {
                if (scars[i] != null && string.Equals(scars[i].ScarId, scarId, StringComparison.Ordinal))
                {
                    return scars[i];
                }
            }
            return null;
        }

        #endregion
    }
}
