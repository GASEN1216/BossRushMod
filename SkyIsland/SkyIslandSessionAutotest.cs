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
        /// <summary>瞬移落点被实体占着时，往外绕这么远再吸地（米）：口径同头目冲步落点的 LandingJitter。</summary>
        private const float TeleportJitter = 1.2f;

        /// <summary>按名字取地形根下的标记（Search_* / POI_* / Exit / Lamp_* / Relay_* …）；会话无效或找不到返回 null。</summary>
        internal Transform DevAutotestFind(string name)
        {
            if (closed || root == null || string.IsNullOrEmpty(name)) return null;
            return root.transform.Find(name);
        }

        /// <summary>
        /// 瞬移到标记处（可带水平偏移）。先核标记脚下与落点脚下都是自建地面，再 SetPosition；
        /// 腾空计时与撤离读条清零，免得一落地就被救援或开始撤离。
        ///
        /// 落点被实体占着（岩体、实体植被）时不硬塞：照头目落点的办法往外挪一圈找净空处，
        /// 一圈都不行才报红——塞进实体里会被弹出来、掉下去、再被捞回同一处（第八轮 F3 的 EnemySpawn_C 连捞 11 次）。
        /// </summary>
        internal bool DevAutotestTeleport(Transform marker, float offsetX, float offsetZ, out string reason)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out reason)) return false;
            if (!IsSessionValid()) { reason = "session_not_valid"; return false; }
            if (marker == null) { reason = "marker_missing"; return false; }
            try { VerifyGround(marker); }
            catch (Exception e) { reason = "marker_ground_missing:" + e.Message; return false; }
            Vector3 target = marker.position + new Vector3(offsetX, 0f, offsetZ);
            Vector3 footing;
            if (!TryTeleportFooting(target, out footing))
            {
                reason = "target_ground_missing";
                return false;
            }
            safePosition = footing;
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionHeld = -1;
            Physics.SyncTransforms();
            Debug.Log("[SkyIsland] AUTOTEST_TELEPORT marker=" + marker.name + " position=" + safePosition);
            return true;
        }

        /// <summary>
        /// 瞬移落点：先按原位吸地，被实体占着就照 <c>SkyIslandBossProps.SnapNear</c> 的办法绕一圈（八个方向、1.2 m）再吸。
        /// 一圈都吸不到自建地面或都被占着，返回 false（步骤记红，不硬塞）。
        /// </summary>
        private bool TryTeleportFooting(Vector3 target, out Vector3 footing)
        {
            footing = target;
            if (TrySnapFooting(target, out footing)) return true;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Vector3 probe = target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * TeleportJitter;
                if (TrySnapFooting(probe, out footing)) return true;
            }
            return false;
        }

        private bool TrySnapFooting(Vector3 at, out Vector3 footing)
        {
            footing = at;
            RaycastHit hit;
            if (!Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out hit, 6f, groundMask, QueryTriggerInteraction.Ignore)
                || !hit.transform.IsChildOf(root.transform)) return false;
            if (Blocked(hit.point)) return false;
            footing = hit.point + Vector3.up * 0.15f;
            return true;
        }

        /// <summary>
        /// 把一组自动遭遇复原成「这一趟还没刷过」（<see cref="SkyIslandEncounters.DevResetEncounter"/>），主角走近时整组照常刷出。
        /// 头目步骤开头用：前面的步骤常把这一组清掉，自动组一趟只刷一次。也是改状态，同样先过写入门。
        /// </summary>
        internal bool DevAutotestResetEncounter(string id, out string reason)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out reason)) return false;
            if (closed || encounters == null) { reason = "session_closed"; return false; }
            return encounters.DevResetEncounter(id, out reason);
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
