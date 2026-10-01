// ============================================================================
// SkyIslandSessionTick.cs - 天空岛出击：每帧驱动的子系统，逐步隔离异常
// ============================================================================
// 从 SkyIslandSession.Update 原样提取（调用顺序与参数逐字不变，会话主文件贴着 1200 行预算，根 AGENTS §4.15）。
//
// 为什么逐步 try：Update 里撤离读条与坠落捞回排在这些子系统之后。任何一个子系统每帧抛异常，
// 后面的撤离判定就永远走不到，玩家被困在这一趟里只能强退（2026-09-29 发版审查 A-01）。
// 每一步失败只记第一次（带堆栈），之后静默跳过这一步，其余步骤与撤离照常；无每帧分配。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        private SkyIslandPatrols patrols;

        /// <summary>本趟已经报过异常的步骤：同一步只记一次，免得每帧刷 Player.log。</summary>
        private readonly HashSet<string> tickFaults = new HashSet<string>(StringComparer.Ordinal);

        private void TickFault(string step, Exception e)
        {
            if (tickFaults.Add(step)) Debug.LogError("[SkyIsland] TICK_FAULT " + step + "（本趟后续同类异常不再重复记录）: " + e);
        }

        private bool HasHostileEnemiesWithin(Vector3 at, float radius)
        {
            return (encounters != null && encounters.HasLivingEnemiesWithin(at, radius))
                || (patrols != null && patrols.HasLivingEnemiesWithin(at, radius));
        }

        private void TickSubsystems()
        {
            try { if (patrols != null) patrols.Tick(); } catch (Exception e) { TickFault("patrols", e); }
            try { if (encounters != null) encounters.Tick(); } catch (Exception e) { TickFault("encounters", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Encounters);
            try { if (scavenging != null) scavenging.Tick(); } catch (Exception e) { TickFault("scavenging", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Scavenging);
            try
            {
                if (residents != null)
                {
                    // 折翎开战后本趟退下休整，避免最后一名倒下与剧情落盘之间的空窗、尸体和交互体重叠。
                    // 下一次出击恢复本人：胜负事实只开路，不永久剥夺聊天、送礼与婚姻入口；旧战败档同样恢复。
                    // 钟守的战斗对象是守钟装置，本人照旧只在战斗中隐藏。
                    residents.SetVisible("sky_zheling", encounters == null || !encounters.WasStartedThisRaid("Zheling"));
                    residents.SetVisible("sky_bellkeeper", !IsStoryChallengeActive("BellKeeper"));
                    // 活人感：头顶气泡 + 说话时停下脚步。自己节流，说话与否的四道门都在 SkyIslandChatter。
                    residents.Tick(player.transform.position, story == null ? null : story.Current);
                }
            }
            catch (Exception e) { TickFault("residents", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Residents);
            // 落盘门按半径而不是全图。自动组改成按出击刷新之后，全岛几乎总有活着的敌人，
            // 用 `HasLivingEnemies` 等于把这道门永久关上：已接受的剧情事实只能等离岛或死亡才写盘，
            // 中途崩溃或强退就全丢。半径口径既保留「不在交火帧写盘」的本意，又让玩家清完手边这一段
            // 就能安全落盘。
            try
            {
                if (story != null)
                    story.Tick(!HasHostileEnemiesWithin(player.transform.position, SaveQuietRadius));
            }
            catch (Exception e) { TickFault("story_save", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.StorySave);
            try
            {
                if (gates != null) gates.Apply(story.Current);
                // 钟庭环与 BellExitIfUnlocked() 同一事实源：地上看得到的圈，就是站进去能走的圈。
                if (extractionRings != null) extractionRings.Apply(BellExitIfUnlocked() != null);
                if (extractionRings != null) extractionRings.ApplyBeacons(WindExitIfUnlocked() != null, StarExitIfUnlocked() != null);
                if (mapMarkers != null) mapMarkers.Apply(story.Current, exitMarker, BellExitIfUnlocked(), WindExitIfUnlocked(), StarExitIfUnlocked());
            }
            catch (Exception e) { TickFault("gates_and_markers", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.GatesAndMarkers);
            try { if (lighting != null) lighting.Tick(); } catch (Exception e) { TickFault("lighting", e); }
            // 路灯跟着光照刚写下的日光色亮灭（与着色器自发光同一口径），白天 O(1) 早返。
            try { if (streetLamps != null && lighting != null) streetLamps.Tick(player.transform.position, lighting.AppliedSun); }
            catch (Exception e) { TickFault("street_lamps", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Lighting);
            try
            {
                if (ambience != null)
                {
                    ambience.ApplyStory(story.Current);
                    ambience.Tick(player.transform.position);
                    // 背景音乐只在登云码头（落地点）放；还没踩到任何岛面时就是刚落地，也算码头。
                    ambience.SetDockMusic(standingRegion == null || standingRegion == "A");
                }
            }
            catch (Exception e) { TickFault("ambience", e); }
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Ambience);
        }
    }
}
