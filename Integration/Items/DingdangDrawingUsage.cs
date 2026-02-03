// ============================================================================
// DingdangDrawingUsage.cs - 叮当涂鸦使用行为
// ============================================================================
// 模块说明：
//   叮当涂鸦的使用行为，打开全屏图片查看器欣赏画作。
//   不消耗物品，可以无限次欣赏。
// ============================================================================

using System;
using UnityEngine;
using Duckov.ItemUsage;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 叮当涂鸦使用行为
    /// </summary>
    public class DingdangDrawingUsage : UsageBehavior
    {
        /// <summary>
        /// 显示设置（物品描述中显示的使用说明）
        /// </summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = DingdangDrawingConfig.UsageDescription
                };
            }
        }

        /// <summary>
        /// 检查物品是否可以使用
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // 叮当涂鸦始终可以使用
            return true;
        }

        /// <summary>
        /// 使用物品时调用
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 打开图片查看器
                ImageViewerUI.Instance.ShowImage(
                    DingdangDrawingConfig.BUNDLE_NAME,
                    DingdangDrawingConfig.IMAGE_NAME,
                    DingdangDrawingConfig.DisplayName
                );

                ModBehaviour.DevLog("[DingdangDrawing] 打开画作欣赏");

                // 注意：不调用 Consume()，物品不会被消耗
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DingdangDrawing] 打开画作失败: " + e.Message);
            }
        }
    }
}
