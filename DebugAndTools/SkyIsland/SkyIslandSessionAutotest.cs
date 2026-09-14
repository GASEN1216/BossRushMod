#if BOSSRUSH_DEV
// ============================================================================
// SkyIslandSessionAutotest.cs - 全自动实机验收在岛上的「瞬移到位」与「返航」入口（只在 Dev 构建里存在）
// ============================================================================
// 写法与晴岚航徽回码头（SkyIslandSessionRecall）、F3 场景巡览（VisitNextLandmark）同一种：先核落点地面，再 SetPosition，
// 腾空计时与撤离读条清零。会话主文件贴着 1200 行预算，这一块单独放。
//
// 纪律：
// - 只给全自动验收用（DebugAndTools/F3GameplayValidationAutotestActions.cs）；岛内只读套件不许引用（tests/F3AutotestOrchestratorGuard.py）。
// - 搬玩家也是改状态：先过 F3GameplayValidationRunner.AutotestWriteAllowed（Dev + 专用测试档 + 自动验收正在跑）。
// - 整份 #if BOSSRUSH_DEV，正式构建里没有这些入口。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>按名字取地形根下的标记（Search_* / POI_* / Exit / Lamp_* / Relay_* …）；会话无效或找不到返回 null。</summary>
        internal Transform DevAutotestFind(string name)
        {
            if (closed || root == null || string.IsNullOrEmpty(name)) return null;
            return root.transform.Find(name);
        }

        /// <summary>
        /// 瞬移到标记处（可带水平偏移）。先核标记脚下与落点脚下都是自建地面，再 SetPosition；
        /// 腾空计时与撤离读条清零，免得一落地就被救援或开始撤离。
        /// </summary>
        internal bool DevAutotestTeleport(Transform marker, float offsetX, float offsetZ, out string reason)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out reason)) return false;
            if (!IsSessionValid()) { reason = "session_not_valid"; return false; }
            if (marker == null) { reason = "marker_missing"; return false; }
            try { VerifyGround(marker); }
            catch (Exception e) { reason = "marker_ground_missing:" + e.Message; return false; }
            Vector3 target = marker.position + new Vector3(offsetX, 0f, offsetZ);
            RaycastHit hit;
            if (!Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out hit, 6f, groundMask, QueryTriggerInteraction.Ignore)
                || !hit.transform.IsChildOf(root.transform))
            {
                reason = "target_ground_missing";
                return false;
            }
            safePosition = hit.point + Vector3.up * 0.15f;
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionHeld = -1;
            Physics.SyncTransforms();
            Debug.Log("[SkyIsland] AUTOTEST_TELEPORT marker=" + marker.name + " position=" + safePosition);
            return true;
        }

        /// <summary>取消或收尾时直接派发返航（与撤离成功同一条 Close(true, …)）。站撤离圈走不通时的兜底。</summary>
        internal bool DevAutotestReturnToBase(string why, out string reason)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out reason)) return false;
            if (closed) { reason = "session_closed"; return false; }
            Close(true, "autotest_" + (why ?? "return"));
            return true;
        }
    }
}
#endif
