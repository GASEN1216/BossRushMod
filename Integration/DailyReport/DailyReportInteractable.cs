// ============================================================================
// DailyReportInteractable.cs - 报箱交互组件
// ============================================================================
// 形态照 Integration/WishFountain/WishFountainInteractable.cs：
// 继承官方 InteractableBase，交互完成（OnTimeOut）后打开日报面板。
//
// 注意 base.Awake() 必须包 try/catch：其他 Mod 可能 patch 了 InteractableBase，
// 它们的异常不能把我们的建筑一起拖挂。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>基地报箱的交互组件。</summary>
    public class DailyReportInteractable : InteractableBase
    {
        /// <summary>交互名的本地化 key（由 DailyReportLocalization 注入）。</summary>
        private const string InteractNameKey = DailyReportTuning.LocalizationPrefix + "Interact";

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = InteractNameKey;
                this.InteractName = InteractNameKey;
            }
            catch { }

            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch { }

            try
            {
                this.interactMarkerOffset = new Vector3(0f, 1.2f, 0f);
            }
            catch { }

            try
            {
                base.Awake();
            }
            catch { }

            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
            }
            catch { }
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch { }

            // 再设一遍：官方 Start 可能把 InteractName 覆盖回 prefab 上的值
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = InteractNameKey;
                this.InteractName = InteractNameKey;
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                return owner != null && owner.IsDailyReportConfiguredEnabled();
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch { }

            try
            {
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.OpenDailyReportUI();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "交互触发异常: " + e.Message);
            }
        }
    }
}
