// ============================================================================
// WishFountainInteractable.cs - 布满了灰尘的星愿许愿台交互组件
// ============================================================================
// 模块说明：
//   继承 InteractableBase，为布满了灰尘的星愿许愿台建筑提供"许愿"交互选项。
//   玩家靠近并触发交互后，打开运行时创建的许愿 View。
//   当心愿正在发送时，交互会被暂时禁用，避免重复打开和重复提交。
//
// 遵循现有交互组件的防御性编程模式（每个操作 try-catch）。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 布满了灰尘的星愿许愿台交互组件
    /// </summary>
    public class WishFountainInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_StarWish_Interact";
                this.InteractName = "BossRush_StarWish_Interact";
            }
            catch { }

            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch { }

            try
            {
                this.interactMarkerOffset = new Vector3(0f, 1.5f, 0f);
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

            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_StarWish_Interact";
                this.InteractName = "BossRush_StarWish_Interact";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            // 发送中时不允许再次交互
            if (WishFountainService.IsSending)
            {
                return false;
            }

            return true;
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
                // 打开许愿 UI
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.OpenWishFountainUI();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 交互触发异常: " + e.Message);
            }
        }
    }
}
