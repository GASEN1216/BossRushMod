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
                    LastReasonId = reasonId;
                    ModBehaviour.DevLog("[ModeH] 入口不可用: " + reasonId);
                    return false;
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
        /// Mode H 内解析宿主的唯一入口。集中一处便于单例引用分类守卫审计，
        /// 也避免各处重复解析单例。
        /// </summary>
        private static ModBehaviour ResolveHost(ModBehaviour preferred)
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

        /// <summary>场内交互可用性：与静态入口共用同一判定。</summary>
        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour host = ResolveHost(_entryHost);
                string reasonId;
                return IsEntryAllowed(host, out reasonId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>交互完成：走同一入场流程。</summary>
        protected override void OnTimeOut()
        {
            try
            {
                _entryHost = ResolveHost(_entryHost);
                _activePresenter = this;
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
