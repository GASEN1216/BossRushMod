// ============================================================================
// SkyIslandShellLottery.cs - 贝壳签筒：把官方海岛挑战的「贝壳抽奖」搬到晴岚群岛
// ============================================================================
// owner 2026-10-01：「这些 Boss 好像都会掉贝壳，在天空岛加一些交互点，把官方海岛地图上的抽奖功能放进去」。
//
// 三栏（SkyIsland/AGENTS.md §3）：
// - **从哪来**：官方贝壳（ItemMetaData 名 SeaShell）。岛上头目、岛主、具名对手与噬风保留各自官方 Boss 的掉落表，
//   官方 Boss 表里本来就带贝壳（海岛挑战的口径）；小兵与巡守换成拾荒者那一份（SkyIslandMinionKit），不掉。
// - **岛上拿来做什么**：在登云码头与风铃集的航路图旁抽签——小签 3 枚抽一件航务补给档（品质 2–5），
//   大签 10 枚抽一件星工遗存档（品质 4–8），东西直接进背包。贝壳以前在岛上只能带回去卖 20 块一枚。
// - **串到哪**：打头目 → 掉贝壳 → 抽签补给下一段路；品质带、按品质加权、单件价值上限与黑名单全用搜刮箱同一套池
//   （SkyIslandLootPools），不另建奖池，抽不出岛上箱子里不会出的东西。
//
// 落点：签筒是航路图交互组里的两个子选项（与「换一种天色」同组），不新增任何世界交互体——
// 不抢别的交互点（SkyIslandInteractionCompetitionPropertyTest 不变），也不改运行时摆放表与场景净空证据。
//
// 扣贝壳、发奖品走合成台同一套事务（SkyIslandInventoryTransaction）：奖品先实例化、贝壳整份预留，
// 奖品送进背包才提交扣除；背包塞不下或任何一步失败都把贝壳原样还回去。按出击刷新，不写存档。
// ============================================================================

using System;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static class SkyIslandShellLottery
    {
        /// <summary>官方贝壳的 ItemMetaData 名（与 Mode E 贝壳经济同一个名字；运行时解析 TypeID，不写死编号）。</summary>
        internal const string ShellItemName = "SeaShell";
        internal const int SmallCost = 3;
        internal const int BigCost = 10;

        private static bool busy;

        /// <summary>这一签抽哪一档池：小签 = 航务补给（2–5），大签 = 星工遗存（4–8）。都按品质加权，不开保底带。</summary>
        internal static SkyIslandLootTier TierOf(bool big) { return big ? SkyIslandLootTier.Starworks : SkyIslandLootTier.Voyage; }
        internal static int CostOf(bool big) { return big ? BigCost : SmallCost; }

        internal static string Label(bool big)
        {
            return big
                ? string.Format(L10n.T("贝壳签筒 · 抽大签（{0} 枚贝壳）", "Shell lots · draw a big lot ({0} seashells)"), BigCost)
                : string.Format(L10n.T("贝壳签筒 · 抽小签（{0} 枚贝壳）", "Shell lots · draw a small lot ({0} seashells)"), SmallCost);
        }

        /// <summary>抽一签。成功时奖品已经进了背包、贝壳已经扣掉；失败时什么都没动，<paramref name="message"/> 说原因。</summary>
        internal static bool Draw(SkyIslandSession session, bool big, out string message)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (busy || session == null || !session.IsReady || player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null)
            {
                message = L10n.T("签筒这会儿摇不动，等一下再来。", "The lot tube won't shake right now. Try again in a moment.");
                return false;
            }
            int shellId = ShellTypeId();
            if (shellId <= 0)
            {
                message = L10n.T("签筒认不出你的贝壳。", "The lot tube doesn't recognise your seashells.");
                return false;
            }
            int cost = CostOf(big), held = CountInPack(player, shellId);
            if (held < cost)
            {
                message = string.Format(L10n.T("贝壳不够：这一签要 {0} 枚，背包里只有 {1} 枚。岛上的头目和岛主身上会掉贝壳。",
                    "Not enough seashells: this lot costs {0} and your pack holds {1}. Island chiefs and lords drop seashells."), cost, held);
                return false;
            }
            System.Random random = new System.Random(unchecked(Environment.TickCount ^ player.GetInstanceID()));
            SkyIslandLootTier tier = TierOf(big);
            int prize = SkyIslandLootPools.Pick(tier, false, random);
            if (prize <= 0)
            {
                message = L10n.T("签筒里今天是空的，贝壳没收。", "The lot tube is empty today. Your seashells stay with you.");
                return false;
            }
            Item output = null;
            SkyIslandInventoryTransaction shells = null;
            busy = true;
            try
            {
                output = ItemAssetsCollection.InstantiateSync(prize);
                if (output == null || output.TypeID != prize) throw new InvalidOperationException("奖品实例无效");
                // 子弹一签给一堆，口径同搜刮箱的子弹格。
                if (output.Stackable && output.GetBool("IsBullet", false))
                    output.StackCount = Mathf.Clamp(SkyIslandLootTables.RollAmmoStack(tier, random), 1, Mathf.Max(1, output.MaxStackCount));
                string name = output.DisplayName;
                int quality = output.Quality, count = output.Stackable ? output.StackCount : 1;
                if (!SkyIslandInventoryTransaction.TryReserve(player, new[] { new SkyIslandIngredient(shellId, cost) }, out shells))
                    throw new InvalidOperationException("预留贝壳失败");
                if (!session.IsReady) throw new InvalidOperationException("本趟已经结束");
                if (!SkyIslandInventoryTransaction.TryDeliver(output, player))
                {
                    // 奖品还在手里（finally 销毁），贝壳预留没提交（Dispose 归还）。
                    message = L10n.T("背包满了，签没抽，贝壳还你。腾个地方再来。",
                        "Your pack is full, so no lot was drawn and your seashells are back. Make room and try again.");
                    return false;
                }
                output = null;
                shells.Commit();
                Debug.Log("[SkyIslandShellLottery] DRAW big=" + big + " prize=" + prize + " q" + quality + " x" + count);
                message = string.Format(L10n.T("签文落下：{0}（品质 {1}）。已放进背包。", "The lot falls: {0} (quality {1}). It's in your pack."),
                    name + (count > 1 ? " ×" + count : string.Empty), quality);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandShellLottery] 抽签失败：" + e.Message);
                message = L10n.T("签没抽成，贝壳还你。", "The draw didn't go through. Your seashells are back.");
                return false;
            }
            finally
            {
                SkyIslandInventoryTransaction.DestroyUnowned(output);
                try { if (shells != null) shells.Dispose(); }
                finally { busy = false; }
            }
        }

        private static int ShellTypeId()
        {
            try { return ItemAssetsCollection.TryGetIDByName(ShellItemName, false); }
            catch (Exception) { return -1; }
        }

        private static int CountInPack(CharacterMainControl player, int typeId)
        {
            int count = 0;
            foreach (Item item in player.CharacterItem.Inventory)
                if (item != null && item.TypeID == typeId) count += item.Stackable ? item.StackCount : 1;
            return count;
        }
    }

    /// <summary>航路图交互组里的「贝壳签筒」子选项：一个抽小签、一个抽大签（见 <see cref="SkyIslandShellLottery"/>）。</summary>
    public sealed class SkyIslandShellLotteryInteractable : BossRushBuildingInteractableBase
    {
        private SkyIslandSession session;
        private bool big;

        internal void Bind(SkyIslandSession owner, bool bigLot) { session = owner; big = bigLot; }

        protected override string InteractNameKey
        {
            get
            {
                string key = big ? "BossRush_SkyIsland_ShellLotteryBig" : "BossRush_SkyIsland_ShellLotterySmall";
                LocalizationHelper.InjectLocalization(key, SkyIslandShellLottery.Label(big));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIslandShellLottery] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandGuide]"; } }
        protected override float InteractMarkerHeight { get { return 2.1f; } }

        /// <summary>组员：基类会重新启用交互碰撞体、官方 Start 会亮起世界标记，两样都关掉（口径同船点的「前往天空岛」子选项）。</summary>
        protected override void Awake()
        {
            base.Awake();
            try
            {
                MarkerActive = false;
                if (interactCollider != null) interactCollider.enabled = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandShellLottery] 子选项隐藏交互标记失败: " + e.Message);
            }
        }

        protected override bool IsBuildingInteractable() { return session != null && session.IsReady; }

        protected override void OnInteractCompleted()
        {
            if (session == null) return;
            string message;
            bool drawn = SkyIslandShellLottery.Draw(session, big, out message);
            session.Announce(message, !drawn);
        }
    }
}
