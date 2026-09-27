// ============================================================================
// ModeDInteractables.cs - Mode D 交互组件
// ============================================================================
// 模块说明：
//   定义 Mode D 模式专用的交互组件，包括：
//   - 冲下一波：开始下一波敌人
//   - 清空所有箱子：清理场地上所有 BossRush 生成的掉落箱
//   - 清空空箱子：仅清理已被搜刮过的空箱子
//   
// 主要功能：
//   - 提供 Mode D 专用的路牌交互选项
//   - 管理路牌状态切换（难度选择 -> Mode D 选项）
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode D "冲下一波" 交互选项
    /// <para>玩家通过此选项手动触发下一波敌人</para>
    /// </summary>
    public class ModeDNextWaveInteractable : InteractableBase
    {
        /// <summary>本地去抖标志（防止帧内重复触发）</summary>
        private bool _clickedThisFrame = false;

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ModeD_NextWave";
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ModeD_NextWave";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        /// <summary>P0-1 修复：使用本地去抖代替访问私有字段，避免编译错误</summary>
        protected override void OnTimeOut()
        {
            try
            {
                // 本地去抖：防止帧内多次触发
                if (_clickedThisFrame) return;
                _clickedThisFrame = true;

                var mod = ModBehaviour.Instance;
                if (mod == null || !mod.IsModeDActive)
                {
                    ModBehaviour.DevLog("[ModeD] [WARNING] ModeDNextWaveInteractable: Mode D 未激活");
                    _clickedThisFrame = false;
                    return;
                }

                // 调用开波，检查返回值
                bool success = mod.ModeDStartNextWave();

                // 只在成功开波时隐藏按钮（失败时按钮保持可见，玩家可以重试）
                if (success)
                {
                    gameObject.SetActive(false);
                }

                _clickedThisFrame = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] ModeDNextWaveInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }


}

