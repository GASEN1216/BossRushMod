// ============================================================================
// SkyIslandSessionFooting.cs - 天空岛出击：落脚点、坠落捞回与「捞不住就换锚点」
// ============================================================================
// 从 SkyIslandSession.cs 原样提取（行为逐字不变，会话主文件贴着 1200 行预算，根 AGENTS §4.15）。
//
// 为什么要「换锚点」：落脚点自己站不住时，捞回去就再掉一次，人会在原地弹球——
// 2026-09-16 第八轮 F3 在 EnemySpawn_C (-153, 10.15, -91) 一步之内连捞 11 次，报告里还记 PASS。
// 现在三件事一起做：
// 1. 地面探针只把**净空**的落点记成落脚点（Blocked：口径同头目冲步落点的墙体胶囊）；
// 2. 同一处连捞 SameSpotRescueLimit 次就换成真正站得住的地标锚点，并记一条 FALL_RESCUE_LOOP（带坐标，给场景包那一侧修）；
// 3. RescueCount 暴露给 F3 全自动验收，一步之内连捞就记红（不再靠人翻 Player.log）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>两次捞回在这么近以内算「同一处」（米）。</summary>
        private const float SameSpotRadius = 3f;
        /// <summary>同一处连捞这么多次就换锚点：站得住的地方不会连着掉两次，三次就是那块地的问题。</summary>
        private const int SameSpotRescueLimit = 3;
        /// <summary>落脚点的净空半径（米）：口径同头目冲步落点，站得下一个人。</summary>
        private const float FootingClearance = 0.45f;

        /// <summary>本趟被捞回落脚点的次数（给 F3 全自动验收按步骤记账；连捞同一处说明那块地站不住）。</summary>
        internal int RescueCount { get { return rescueCount; } }

        /// <summary>
        /// 这个落点站不站得下一个人：口径同头目落点（<c>SkyIslandBossForge.SnapToGround</c> 的墙体胶囊），
        /// 腰身高度上有实体就算占着。只在 0.2 秒一次的地面探针与瞬移里调，不进每帧路径。
        /// </summary>
        private static bool Blocked(Vector3 ground)
        {
            return Physics.CheckCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 1.5f, FootingClearance,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void Rescue()
        {
            rescueCount++;
            sameSpotRescues = (safePosition - lastRescueAt).sqrMagnitude < SameSpotRadius * SameSpotRadius ? sameSpotRescues + 1 : 1;
            lastRescueAt = safePosition;
            // 同一处连着捞：这块落脚点站不住，再送回去还是掉。换一个真正站得住的锚点（地标 → 出生点），别让人原地弹球。
            if (sameSpotRescues >= SameSpotRescueLimit)
            {
                Vector3 anchor;
                if (TryFallbackFooting(safePosition, out anchor))
                {
                    safePosition = anchor;
                    sameSpotRescues = 0;
                    lastRescueAt = anchor;
                }
                if (!rescueLoopReported)
                {
                    rescueLoopReported = true;
                    // 场景包那一块要单独修；这里只保证人不卡住，并把坐标留给下一次判包。
                    Debug.LogWarning("[SkyIsland] FALL_RESCUE_LOOP at=" + lastRescueAt + ",count=" + rescueCount
                        + ",moved_to=" + safePosition);
                }
            }
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionHeld = -1;
            Status(L10n.T("已返回最近安全落脚点", "Returned to the nearest safe footing"), false);
            Debug.Log("[SkyIsland] FALL_RESCUE position=" + safePosition);
        }

        /// <summary>
        /// 连捞之后的备用锚点：离出事点最近、且脚下真有自建地面的地标；一个都不合格就回出生点。
        /// 只在连捞时走，不进每帧路径。
        /// </summary>
        private bool TryFallbackFooting(Vector3 from, out Vector3 footing)
        {
            footing = from;
            Transform best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < landmarks.Count; i++)
            {
                Transform landmark = landmarks[i];
                if (landmark == null) continue;
                float distance = (landmark.position - from).sqrMagnitude;
                if (distance >= bestDistance || distance < SameSpotRadius * SameSpotRadius) continue;
                // 排除出事点本身（上面的近距离过滤）、脚下没有自建地面的、以及被实体占着的：
                // 换过去还是站不住的话，下一轮连捞会再往外挑一个更远的地标，逐步走出这片坏地。
                try { VerifyGround(landmark); }
                catch (Exception) { continue; }
                if (Blocked(landmark.position)) continue;
                best = landmark;
                bestDistance = distance;
            }
            if (best == null && playerSpawn != null) best = playerSpawn;
            if (best == null) return false;
            footing = best.position + Vector3.up * 0.15f;
            return true;
        }
    }
}
