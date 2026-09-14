// ============================================================================
// SkyIslandSessionEcho.cs - 噬风·回响：引风、本趟计数与回响遗存
// ============================================================================
// 从 SkyIslandSession.cs 拆出来单独放（会话主文件贴着 1200 行预算，写法同 SkyIslandSessionRecall）：
// 主文件只在 BeginStoryChallenge / EncounterWasSaved / OnEncounterCleared / OnStormDefeated 各多一句转发。
//
// 纪律：
// - **判据只有一份**：面板挂不挂「引风」（SkyIslandWorldStoryEcho）与点下去这一次（TryBeginStormEcho）都走
//   CanSummonStormEcho → SkyIslandStoryRules.CanSummonStormEcho；
// - **不写存档**：本趟引过几次、回响清没清都只在会话里，离岛即清；清场也不进 clearedEncounters；
// - **先占住风晶再开战、开成了才扣**：风晶经 SkyIslandInventoryTransaction 预留，遭遇 owner 拒绝（站得太远、上一场没打完、
//   活体上限）时原件归还，玩家不白烧一块；
// - 奖励只装岛上的东西（SkyIslandStormEchoReward），落点与首战战利品同一套退避。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>这一趟引过几次风（F3 只读 SKY_STORM_ECHO 经观测面读）。</summary>
        private int stormEchoStarts;

        /// <summary>这一趟的回响清场了没有：给遭遇 owner 当「已记下」的事实，存档不记。</summary>
        private bool stormEchoCleared;

        /// <summary>回响此刻在栈道上（有活着的回响组员）。</summary>
        internal bool StormEchoActive
        {
            get { return encounters != null && encounters.IsBusy(SkyIslandStormEchoRules.EncounterId); }
        }

        /// <summary>
        /// 栈道与桥上是不是「噬风将至」的大风：双航标亮着、噬风还没散，**或者回响正在栈道上**。
        /// 夜风（SkyIslandFieldcraft.TickWind）读这一个口径——回响把风带回来，驱风香、风灯与噬风之核重新有事可做。
        /// </summary>
        internal bool StormWindPending
        {
            get { return BothBeaconsLit && (!StormResolved || StormEchoActive); }
        }

        /// <summary>
        /// 此刻能不能引风。面板挂不挂、点下去成不成，都只问这一处：数背包顶层的噬风之核与晴岚风晶（与合成台、夜风同一口径），
        /// 再交给 <see cref="SkyIslandStoryRules.CanSummonStormEcho"/>。
        /// </summary>
        internal bool CanSummonStormEcho(out string blocker)
        {
            blocker = null;
            if (!IsSessionValid() || story == null) return false;
            bool core = PackCount(BossRushItemIds.SkyIslandWindeaterCore) > 0;
            int crystals = PackCount(BossRushItemIds.SkyIslandQinglanWindcrystal);
            return SkyIslandStoryRules.CanSummonStormEcho(story.Current, stormEchoStarts >= SkyIslandStormEchoRules.MaxPerRaid,
                core, crystals, out blocker);
        }

        /// <summary>引风：判据 → 预留风晶 → 遭遇 owner 开战 → 开成了才扣风晶、记这一趟。失败时 <paramref name="message"/> 说明原因。</summary>
        internal bool TryBeginStormEcho(out string message)
        {
            string blocker;
            if (!CanSummonStormEcho(out blocker))
            {
                message = blocker ?? (stormEchoStarts > 0 ? SkyIslandStormEchoRules.SpentThisRaid : SkyIslandStormEchoRules.NotReady);
                return false;
            }
            SkyIslandInventoryTransaction crystal = null;
            try
            {
                if (!SkyIslandInventoryTransaction.TryReserve(player, SkyIslandStormEchoReward.Cost, out crystal))
                {
                    message = SkyIslandStormEchoRules.ReserveFailed;
                    return false;
                }
                // 预留会同步触发官方库存事件：那期间本趟可能已经结束，开战之前再核一次。
                if (!IsSessionValid() || encounters == null || !encounters.BeginChallenge(SkyIslandStormEchoRules.EncounterId))
                {
                    message = SkyIslandStormEchoRules.StartFailed;
                    return false;
                }
                crystal.Commit();
                stormEchoStarts++;
                Debug.Log("[SkyIsland] STORM_ECHO_BEGIN starts=" + stormEchoStarts);
                message = SkyIslandStormEchoRules.Opened;
                return true;
            }
            finally
            {
                // 没提交的预留在这里整块归还；提交之后 Dispose 什么都不做。
                if (crystal != null) crystal.Dispose();
            }
        }

        /// <summary>遭遇 owner 清场回调里的回响分支：记下本趟已清，返回 true 表示已处理（不写存档）。</summary>
        private bool RecordStormEchoCleared(string id)
        {
            if (!SkyIslandStormEchoRules.IsEcho(id)) return false;
            stormEchoCleared = true;
            return true;
        }

        /// <summary>回响本体倒下：在它倒下的位置旁边留一箱回响遗存（只装岛上的东西）。</summary>
        private void OnStormEchoDefeated(Vector3 position)
        {
            if (closed || root == null) return;
            Vector3 drop;
            if (!SkyIslandRewardCrate.TryFindCratePosition(root.transform, position,
                SkyIslandLootTables.StableHash(SkyIslandStormEchoReward.Stream) % 360, SkyIslandRewardCrate.InteractableSeparation,
                groundMask, out drop)) drop = position;
            SkyIslandYield[] goods = SkyIslandStormEchoReward.For(
                SkyIslandLootTables.CreateStream(raidSeed, SkyIslandStormEchoReward.Stream).NextDouble());
            bool dropped = SkyIslandRewardCrate.CreateWithGoods(root.transform, drop, "SkyIslandStormEchoTrophy", goods);
            if (story != null) story.LogTimingOnce("echo_defeated", SkyIslandStormEchoRules.EncounterId);
            Debug.Log("[SkyIsland] STORM_ECHO_DEFEATED trophy=" + dropped);
        }

        /// <summary>背包顶层某件物品的件数（不数基地仓库与背包里的容器，与合成台同一口径）。</summary>
        private static int PackCount(int typeId)
        {
            try { return ItemFactory.GetItemCountInInventory(typeId); }
            catch (Exception) { return 0; }
        }
    }
}
