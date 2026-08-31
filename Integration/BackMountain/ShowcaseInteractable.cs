// ============================================================================
// ShowcaseInteractable.cs - 战利品展示柜交互组件
// ============================================================================
// 形态照 Integration/DailyReport/DailyReportInteractable.cs：继承官方 InteractableBase，
// 交互完成（OnTimeOut）后打开展示柜面板。
//
// base.Awake() / base.Start() 单独包 try/catch：其他 Mod 可能 patch 了
// InteractableBase，它们的异常不能把我们的建筑一起拖挂。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>基地战利品展示柜的交互组件。</summary>
    public class ShowcaseInteractable : InteractableBase
    {
        /// <summary>交互名的本地化键（由 BackMountainLocalization 注入）。</summary>
        private const string InteractNameKey = "BossRush_BackMountain_Showcase_Interact";

        protected override void Awake()
        {
            ApplyInteractName("awake");

            try
            {
                this.interactCollider = GetComponent<Collider>();
                this.interactMarkerOffset = new Vector3(0f, 1.3f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜交互体绑定失败: " + e.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] base.Awake 异常: " + e.Message);
            }

            try
            {
                if (this.interactCollider != null) this.interactCollider.enabled = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 展示柜交互体启用失败: " + e.Message);
            }
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] base.Start 异常: " + e.Message);
            }

            ApplyInteractName("start");
        }

        private void ApplyInteractName(string stage)
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = InteractNameKey;
                this.InteractName = InteractNameKey;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                    + "[WARNING] 展示柜交互名设置失败(" + stage + "): " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null || !owner.IsBackMountainConfiguredEnabled()) return false;
                return BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase);
            }
            catch (Exception)
            {
                // 每次靠近都会跑，失败时静默禁用，不打日志免得刷屏
                return false;
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] base.OnTimeOut 异常: " + e.Message);
            }

            try
            {
                ModBehaviour.Instance?.OpenShowcaseUI();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜交互触发异常: " + e.Message);
            }
        }
    }
}
