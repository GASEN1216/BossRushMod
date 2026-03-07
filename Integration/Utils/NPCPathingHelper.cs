// ============================================================================
// NPCPathingHelper.cs - NPC寻路状态统一辅助
// ============================================================================
// 模块说明：
//   统一处理各NPC移动组件的路径回调和停止移动逻辑，避免：
//   - 对话/剧情期间异步路径回调覆盖当前状态
//   - 各NPC重复实现同一套状态清理代码
// ============================================================================

using System;
using Pathfinding;

namespace BossRush.Utils
{
    /// <summary>
    /// NPC 寻路状态统一辅助
    /// </summary>
    public static class NPCPathingHelper
    {
        /// <summary>
        /// 统一停止移动并清理路径状态
        /// </summary>
        public static void StopMovement(
            ref Path currentPath,
            ref int currentWaypoint,
            ref bool moving,
            ref bool waitingForPathResult,
            Action<float> updateMoveAnimation,
            string logPrefix = null)
        {
            currentPath = null;
            currentWaypoint = 0;
            moving = false;
            waitingForPathResult = false;

            NPCExceptionHandler.TryExecute(
                () => updateMoveAnimation?.Invoke(0f),
                "NPCPathingHelper.StopMovement.UpdateMoveAnimation",
                false);

            if (!string.IsNullOrEmpty(logPrefix))
            {
                ModBehaviour.DevLog(logPrefix + " 停止移动并清理路径状态");
            }
        }

        /// <summary>
        /// 统一处理 A* 路径计算完成回调
        /// </summary>
        public static void HandlePathComplete(
            Path resultPath,
            int callbackRequestId,
            int activeRequestId,
            bool shouldDiscard,
            string discardReason,
            ref Path currentPath,
            ref int currentWaypoint,
            ref bool moving,
            ref bool waitingForPathResult,
            Action<float> updateMoveAnimation,
            string logPrefix)
        {
            if (callbackRequestId != activeRequestId)
            {
                if (!string.IsNullOrEmpty(logPrefix))
                {
                    ModBehaviour.DevLog(logPrefix + " Ignore stale path callback: callback=" + callbackRequestId + ", active=" + activeRequestId);
                }
                return;
            }

            waitingForPathResult = false;

            if (shouldDiscard)
            {
                StopMovement(
                    ref currentPath,
                    ref currentWaypoint,
                    ref moving,
                    ref waitingForPathResult,
                    updateMoveAnimation);

                if (!string.IsNullOrEmpty(logPrefix))
                {
                    string reason = string.IsNullOrEmpty(discardReason) ? "状态不允许接收路径" : discardReason;
                    ModBehaviour.DevLog(logPrefix + " 丢弃路径回调: " + reason);
                }
                return;
            }

            bool hasUsablePath = resultPath != null &&
                                 !resultPath.error &&
                                 resultPath.vectorPath != null &&
                                 resultPath.vectorPath.Count > 0;

            if (hasUsablePath)
            {
                currentPath = resultPath;
                currentWaypoint = 0;
                moving = true;
                if (!string.IsNullOrEmpty(logPrefix))
                {
                    ModBehaviour.DevLog(logPrefix + " 路径计算成功，路径点数: " + resultPath.vectorPath.Count);
                }
            }
            else
            {
                string error = resultPath != null ? resultPath.errorLog : "null path";
                StopMovement(
                    ref currentPath,
                    ref currentWaypoint,
                    ref moving,
                    ref waitingForPathResult,
                    updateMoveAnimation);

                if (!string.IsNullOrEmpty(logPrefix))
                {
                    ModBehaviour.DevLog(logPrefix + " [WARNING] 路径计算失败: " + error);
                }
            }
        }
    }
}
