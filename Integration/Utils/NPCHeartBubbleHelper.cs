// ============================================================================
// NPCHeartBubbleHelper.cs - NPC爱心/心碎气泡特效通用辅助
// ============================================================================
// 模块说明：
//   从 GoblinNPCAnimation 中提取的通用爱心/心碎气泡逻辑。
//   所有NPC共用同一套序列帧动画资源和回退文字气泡。
// ============================================================================

using UnityEngine;
using Duckov.UI.DialogueBubbles;

namespace BossRush.Utils
{
    /// <summary>
    /// NPC爱心/心碎气泡特效通用辅助
    /// </summary>
    public static class NPCHeartBubbleHelper
    {
        /// <summary>
        /// 显示冒爱心气泡（优先序列帧动画，回退文字气泡）
        /// </summary>
        /// <param name="npcTransform">NPC的Transform</param>
        /// <param name="bubbleHeight">气泡高度（名字标签高度 + 偏移）</param>
        /// <param name="animationHeight">动画高度（名字标签高度 + 动画偏移）</param>
        /// <param name="duration">显示时长</param>
        /// <param name="logPrefix">日志前缀</param>
        public static void ShowLoveHeart(Transform npcTransform, float bubbleHeight, float animationHeight, float duration, string logPrefix)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (TryShowHeartAnimation(npcTransform, "love_heart", animationHeight, duration))
                {
                    ModBehaviour.DevLog(logPrefix + " 显示冒爱心动画");
                    return;
                }

                // 回退：使用文字气泡
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    DialogueBubblesManager.Show("♥", npcTransform, bubbleHeight, false, false, -1f, duration)
                );
                ModBehaviour.DevLog(logPrefix + " 显示爱心文字气泡（回退方案）");
            }, "NPCHeartBubbleHelper.ShowLoveHeart");
        }

        /// <summary>
        /// 显示心碎气泡（优先序列帧动画，回退文字气泡）
        /// </summary>
        public static void ShowBrokenHeart(Transform npcTransform, float bubbleHeight, float animationHeight, float duration, string logPrefix)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (TryShowHeartAnimation(npcTransform, "broken_heart", animationHeight, duration))
                {
                    ModBehaviour.DevLog(logPrefix + " 显示心碎动画");
                    return;
                }

                // 回退：使用文字气泡
                string brokenHeart = L10n.T("心碎", "Heartbroken");
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    DialogueBubblesManager.Show(brokenHeart, npcTransform, bubbleHeight, false, false, -1f, duration)
                );
                ModBehaviour.DevLog(logPrefix + " 显示心碎文字气泡（回退方案）");
            }, "NPCHeartBubbleHelper.ShowBrokenHeart");
        }

        /// <summary>
        /// 尝试从 AssetBundle 加载序列帧动画并播放
        /// </summary>
        /// <param name="npcTransform">NPC的Transform</param>
        /// <param name="bundleName">Assets/ui/ 下的 bundle 文件名</param>
        /// <param name="height">动画显示高度</param>
        /// <param name="duration">显示时长</param>
        /// <returns>是否成功</returns>
        private static bool TryShowHeartAnimation(Transform npcTransform, string bundleName, float height, float duration)
        {
            return NPCExceptionHandler.TryExecute(() =>
            {
                if (!NPCUIAssetCache.TryGetAllSprites(bundleName, out Sprite[] frames) ||
                    frames == null ||
                    frames.Length == 0)
                {
                    return false;
                }

                NPCBubbleAnimator.Create(npcTransform, frames, height, duration, false);
                return true;
            }, "NPCHeartBubbleHelper.TryShowHeartAnimation", false, false);
        }
    }
}
