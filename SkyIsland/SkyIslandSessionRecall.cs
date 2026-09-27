// ============================================================================
// SkyIslandSessionRecall.cs - 晴岚航徽：拉一下缆绳回到登云码头
// ============================================================================
// 航徽的描述一直写着「戴着它回来，码头永远留着一条缆绳给你」。这里让那条缆绳真的能拉：在岛上使用航徽，回到登云码头的出生点。
// 从 SkyIslandSession.cs 拆出来单独放（会话主文件贴着 1200 行预算），写法与 F3 场景巡览、落水救援同一种：
// 先核落点地面，再 SetPosition，腾空计时与撤离读条清零。
//
// 纪律：
// - 每趟一次（会话按出击新建，下一趟自然复位）；不消耗物品。
// - 附近有敌人时不许拉：与剧情面板同一道战斗门（CanOpenStoryPanel），不是战斗中的逃生键。
// - 落点是出生点 PlayerSpawn：离码头撤离圈中心 11 m（撤离半径 2.5 m），落地不会自己开始撤离读条。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>这一趟的缆绳拉过没有。</summary>
        private bool recallUsed;

        /// <summary>现在能不能拉缆绳：会话有效、这趟还没拉过、出生点与玩家都在。航徽的使用按钮亮不亮读这里。</summary>
        internal bool RecallAvailable
        {
            get { return IsSessionValid() && !recallUsed && playerSpawn != null && player != null; }
        }

        /// <summary>拉缆绳回码头。成功返回 true；失败时 <paramref name="message"/> 说明原因（这趟已经拉过、附近有敌人、码头那头没有地面）。</summary>
        internal bool TryRecallToDock(out string message)
        {
            if (!RecallAvailable)
            {
                message = recallUsed ? SkyIslandFieldcraftRules.RecallSpent : SkyIslandFieldcraftRules.RecallNotReady;
                return false;
            }
            string reason;
            if (!CanOpenStoryPanel(out reason))
            {
                message = reason;
                return false;
            }
            try
            {
                VerifyGround(playerSpawn);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 航徽回码头失败：" + e.Message);
                message = SkyIslandFieldcraftRules.RecallFailed;
                return false;
            }
            safePosition = playerSpawn.position + Vector3.up * 0.15f;
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionHeld = -1;
            recallUsed = true;
            if (story != null) story.LogTiming("recall", null);
            Debug.Log("[SkyIsland] BADGE_RECALL position=" + safePosition);
            message = SkyIslandFieldcraftRules.RecallArrived;
            return true;
        }
    }
}
