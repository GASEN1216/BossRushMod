using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// Mode H 场内交互入口（设计提案 §17.1、§23.1、§25.1）。
    ///
    /// 契约：
    /// - 继承 InteractableBase，可被 AddComponent&lt;ModeHInteractable&gt;() 动态挂载，
    ///   与 Mode G 一样不占用路牌的难度子选项；
    /// - IsInteractable 统一受 ModeHAvailability、地图支持、展示资源预检、
    ///   Mode H 运行门和旧模式冲突门控；
    /// - 交互提示必须包含真实资产风险行（BossRush_ModeH_RealStakeRiskNotice，§22.1）；
    /// - 确认后唯一调用 ModeHEntry.TryEnter；被拒绝时不改变旧模式状态。
    /// </summary>
    public sealed class ModeHInteractable : InteractableBase
    {
        #region 状态

        private static ModeHInteractable _activePresenter;
        private ModBehaviour _entryHost;
        private bool _autoPresenter;
        private string _lastReasonId;

        /// <summary>最近一次交互是否真的尝试了入场（供入口流程判定是否需要退款）。</summary>
        internal static bool LastInteractionAttemptedEntry { get; private set; }

        /// <summary>最近一次拒绝原因。</summary>
        internal static string LastReasonId { get; private set; }

        #endregion

        #region 静态入口

        /// <summary>
        /// 打开 Mode H 入口（自动流程与场内交互共用）。
        /// 只创建短命 presenter，不直接扣除物品，也不创建 Season。
        /// </summary>
        internal static bool TryOpenEntry(ModBehaviour host)
        {
            LastInteractionAttemptedEntry = false;
            LastReasonId = null;
            if (host == null) return false;

            GameObject obj = null;
            try
            {
                string reasonId;
                if (!IsEntryAllowed(host, out reasonId))
                {
                    return HandleEntryRejected(host, reasonId);
                }

                if (_activePresenter != null) return false;

                obj = new GameObject("ModeH_EntryPresenter");
                UnityEngine.Object.DontDestroyOnLoad(obj);
                ModeHInteractable presenter = obj.AddComponent<ModeHInteractable>();
                presenter._entryHost = host;
                presenter._autoPresenter = true;
                _activePresenter = presenter;

                return presenter.OpenEntryFlow();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 打开入口失败: " + e.Message);
                if (obj != null)
                {
                    try
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                    catch (Exception)
                    {
                        // presenter 销毁失败不影响入口拒绝结论
                    }
                }
                _activePresenter = null;
                return false;
            }
        }

        /// <summary>入口可用性统一判定（no-throw）。</summary>
        internal static bool IsEntryAllowed(ModBehaviour host, out string reasonId)
        {
            reasonId = null;
            try
            {
                if (host == null)
                {
                    reasonId = ModeHAvailability.ReasonOwnerMissing;
                    return false;
                }
                if (ModeHAvailability.EvaluateNewSeason(host, out reasonId) != ModeHAvailabilityStatus.Available)
                {
                    return false;
                }

                string conflictId;
                if (host.HasLegacyModeConflictForModeH(out conflictId))
                {
                    reasonId = ModeHAvailability.ReasonOtherModeActive;
                    return false;
                }

                if (ModeHMapSupportRegistry.SupportedMapCount <= 0)
                {
                    reasonId = ModeHAvailability.ReasonMapUnsupported;
                    return false;
                }
                if (!ModeHPresentationAssetCache.TryPreflight())
                {
                    reasonId = ModeHAvailability.ReasonPresentationMissing;
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                reasonId = ModeHAvailability.ReasonContentNotReady;
                return false;
            }
        }

        /// <summary>
        /// 入口的恢复分支：存在待处理的赛季恢复记录时打开恢复壳并返回 true。
        /// 按 §18.1，配置关闭也允许处理已有 H 记录；但**有其它模式在跑时不接管**。
        /// </summary>
        /// <summary>
        /// 入口被拒时的统一处置：先试恢复壳，接不住再退回不可用提示。
        /// 静态入口（TryOpenEntry）与场内交互（OnTimeOut）共用，避免两条路的分流走偏。
        ///
        /// 恢复分支的意义：有待处理的赛季记录或未终结押品时，入口的职责是**打开恢复壳**
        /// 而不是把玩家挡在门外——否则中断的赛季永远没有出口
        /// （UIAndSigns 的注入门本来就为这一支放行，CR-2026-08-29-012）。
        /// </summary>
        private static bool HandleEntryRejected(ModBehaviour host, string reasonId)
        {
            LastReasonId = reasonId;
            ModBehaviour.DevLog("[ModeH] 入口不可用: " + reasonId);
            if (TryOpenRecoveryShellForEntry(host, reasonId)) return true;
            ShowUnavailableNotice(reasonId);
            return false;
        }

        private static bool TryOpenRecoveryShellForEntry(ModBehaviour host, string reasonId)
        {
            try
            {
                if (host == null) return false;

                string recoveryReasonId;
                if (ModeHAvailability.EvaluateRecovery(out recoveryReasonId)
                    != ModeHAvailabilityStatus.Available)
                {
                    return false;
                }

                string conflictId;
                if (host.HasLegacyModeConflictForModeH(out conflictId)) return false;

                ModeHRuntimeModule runtime = host.ModeHRuntime;
                if (runtime == null) return false;

                runtime.OpenRecoveryShell(reasonId);
                ModBehaviour.DevLog("[ModeH] 入口转恢复壳: " + (reasonId ?? "unknown"));
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 恢复壳打开失败: " + e.Message);
                return false;
            }
        }

        /// <summary>入口被拒时给玩家一句解释。原因 key 由 ModeHAvailability 统一解析。</summary>
        private static void ShowUnavailableNotice(string reasonId)
        {
            try
            {
                ModBehaviour mod = ResolveHost(null);
                if (mod == null) return;
                mod.ShowMessage(L10n.T(ModeHAvailability.GetReasonLocalizationKey(reasonId)));
            }
            catch (Exception)
            {
                // 提示失败不改变入口拒绝结论
            }
        }

        /// <summary>
        /// Mode H 内解析宿主的唯一入口。集中一处便于单例引用分类守卫审计，
        /// 也避免各处重复解析单例（`docs/testing/2026-05-14-modbehaviour-instance-classification.md`
        /// 里 ModeH 的基线就是「只在一个解析器里取活动 mod 实例」）。
        /// 运行时模块的换档回调也走这里，不再自己取一次单例。
        /// </summary>
        internal static ModBehaviour ResolveHost(ModBehaviour preferred)
        {
            return preferred != null ? preferred : ModBehaviour.Instance;
        }

        /// <summary>关闭当前 presenter（幂等）。</summary>
        internal static void DismissActive()
        {
            ModeHInteractable presenter = _activePresenter;
            _activePresenter = null;
            if (presenter == null) return;
            try
            {
                if (presenter._autoPresenter && presenter.gameObject != null)
                {
                    UnityEngine.Object.Destroy(presenter.gameObject);
                }
            }
            catch (Exception)
            {
                // 销毁失败不阻断流程
            }
        }

        #endregion

        #region 入场流程

        private bool OpenEntryFlow()
        {
            try
            {
                ModeHSupportedMap map;
                string activeScene = SceneManager.GetActiveScene().name;
                if (!ModeHEntry.ResolveTargetMap(
                        ModeHMapSupportRegistry.IsSupportedScene(activeScene) ? activeScene : null, out map))
                {
                    _lastReasonId = ModeHAvailability.ReasonMapUnsupported;
                    LastReasonId = _lastReasonId;
                    ShowUnavailableNotice(_lastReasonId);
                    DismissActive();
                    return false;
                }

                // 真实资产风险行必须在进入前展示（§22.1，进入模式即知情同意）
                ShowRiskNotice();

                LastInteractionAttemptedEntry = true;
                string reasonId;
                bool started = ModeHEntry.TryEnter(_entryHost, map.SceneName, map.SceneId, out reasonId);
                _lastReasonId = reasonId;
                LastReasonId = reasonId;
                if (!started)
                {
                    ModeHEntry.CancelPendingEntry();
                    ShowUnavailableNotice(reasonId);
                }
                DismissActive();
                return started;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 入场流程异常: " + e.Message);
                ModeHEntry.CancelPendingEntry();
                DismissActive();
                return false;
            }
        }

        private static void ShowRiskNotice()
        {
            try
            {
                ModBehaviour mod = ResolveHost(null);
                if (mod == null) return;
                mod.ShowMessage(L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStakeRiskNotice"));
            }
            catch (Exception)
            {
                // 提示失败不阻断入场判定
            }
        }

        #endregion

        #region InteractableBase

        /// <summary>初始化交互名。</summary>
        protected override void Awake()
        {
            try
            {
                base.Awake();
                overrideInteractName = true;
                _overrideInteractNameKey = ModeHConfig.LocalizationKeyPrefix + "EntryInteract";
            }
            catch (Exception)
            {
                // 交互名注入失败不影响可交互性
            }
        }

        /// <summary>Start 阶段再确认一次交互名。</summary>
        protected override void Start()
        {
            try
            {
                base.Start();
                overrideInteractName = true;
                _overrideInteractNameKey = ModeHConfig.LocalizationKeyPrefix + "EntryInteract";
            }
            catch (Exception)
            {
                // 同上
            }
        }

        /// <summary>
        /// 场内交互可用性：「能开新赛季」**或**「有恢复记录要处置」。
        ///
        /// 只判前者是 CR 级缺陷：recovery-only 闸立起时新赛季判定必然 Unavailable，
        /// 官方 InteractableBase.StartInteract 直接 return false，OnTimeOut 永不执行，
        /// 玩家连点都点不动——中断赛季与未终结押品因此**没有任何出口**。
        /// UIAndSigns 的注入门本来就为恢复分支放行，这里必须与它一致。
        /// </summary>
        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour host = ResolveHost(_entryHost);
                string reasonId;
                if (IsEntryAllowed(host, out reasonId)) return true;

                string recoveryReasonId;
                return ModeHAvailability.EvaluateRecovery(out recoveryReasonId)
                    == ModeHAvailabilityStatus.Available;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 交互完成：走带恢复分流的统一入场路径。
        /// 新赛季不可用但有恢复记录时，入口的职责是**打开恢复壳**而不是把玩家挡在门外。
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                ModBehaviour host = ResolveHost(_entryHost);
                _entryHost = host;
                _activePresenter = this;

                string reasonId;
                if (!IsEntryAllowed(host, out reasonId))
                {
                    HandleEntryRejected(host, reasonId);
                    return;
                }

                OpenEntryFlow();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 交互入场失败: " + e.Message);
            }
        }

        #endregion
    }
}
