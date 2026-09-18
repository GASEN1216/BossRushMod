// ============================================================================
// SkyIslandWorldStoryServices.cs - 居民服务按钮与「航务委托」子页的挂法
// ============================================================================
// 从 SkyIslandWorldStory.cs 拆出来单独放（主文件有 1200 行预算）：主文件的 ReadPoint / ResidentChoices
// 只各多一句调用；派单本身（BountyChoices）仍在主文件，服务回话与按钮在本文件。
// 手记与合成台两个入口（JournalChoice / CraftChoice）2026-09-15 原样搬来：主文件拆出 ResidentChoices 供对话判断「有没有事可办」要腾行数。
//
// 2026-09-14 UI 优化对照审核 F-06 与拍板 O-3（照主流游戏的商店 / 服务口径）：
// - 付费服务：没有要做的（装备都结实、没受伤）不挂；钱不够、还在冷却照挂，按钮上写价钱或还要等几秒——
//   这两件玩家马上就能改变，藏起来反而找不到服务在哪；状态与点下去的判断共用 SkyIslandServices 的 Evaluate*。
// - 归航菜：剧情前置没到不挂、「还差什么」进正文；这一趟吃过了也不挂。
// - 航务委托：派单最多四类再加交付 / 退单，和留言板、苇白的其它事挤在一页就是五六项平铺，单独成一页。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandWorldStory
    {
        private string Repair()
        {
            SkyIslandServices services = session.Services;
            return services == null ? L10n.T("渡口暂时没人。", "Nobody is at the dock right now.") : services.Repair();
        }

        private string Heal()
        {
            SkyIslandServices services = session.Services;
            return services == null ? L10n.T("眠苔不在。", "Miantai is not here.") : services.Heal();
        }

        private string Meal()
        {
            SkyIslandServices services = session.Services;
            return services == null
                ? L10n.T("菜畦还没开张。", "The garden is not open yet.")
                : services.Meal(session.HasPlantingDelivered);
        }

        /// <summary>
        /// 渡口整备挂不挂、按钮上写什么（口径见文件头）。服务 owner 还没建好（群岛没就绪）时挂原标签，点了回「请等待群岛就绪」。
        /// 面板开着时游戏暂停，标签上的价钱与秒数不会过期。
        /// </summary>
        private void RepairChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            string label = L10n.T("渡口整备 · 修补随身装备", "Dock refit · repair what you carry");
            SkyIslandServices services = session.Services;
            if (services != null)
            {
                int price;
                SkyIslandServiceReadiness state = services.RepairReadiness(out price);
                if (state == SkyIslandServiceReadiness.NothingToDo) return;
                label += ServiceTag(state, price, 0);
            }
            ServiceChoice(choices, label, Repair);
        }

        /// <summary>苔药：口径同 <see cref="RepairChoice"/>。</summary>
        private void HealChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            string label = L10n.T("请眠苔敷一副苔药", "Ask Miantai for a moss remedy");
            SkyIslandServices services = session.Services;
            if (services != null)
            {
                int price, wait;
                SkyIslandServiceReadiness state = services.HealReadiness(out price, out wait);
                if (state == SkyIslandServiceReadiness.NothingToDo) return;
                label += ServiceTag(state, price, wait);
            }
            ServiceChoice(choices, label, Heal);
        }

        /// <summary>
        /// 归航菜：剧情前置（种植记录交还晴禾）没到不挂，「还差什么」进正文；这一趟吃过了也不挂（与便当共用一次）。
        /// 判据与服务 owner <c>SkyIslandServices.Meal</c> 的两句拒绝一致。
        /// </summary>
        private void MealChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            if (!session.HasPlantingDelivered)
            {
                Hint(L10n.T("种植记录交还晴禾之后，菜畦重新开张，才讨得到归航菜。",
                    "The garden reopens, and the homecoming meal with it, once Qinghe has her planting record back."));
                return;
            }
            SkyIslandServices services = session.Services;
            if (services != null && services.MealEaten) return;
            ServiceChoice(choices, L10n.T("讨一份归航菜（本次出击生效）", "Ask for a homecoming meal (this raid only)"), Meal);
        }

        /// <summary>服务按钮的尾巴：「（120）」「（要 120 · 钱不够）」「（药还在熬 · 42 秒）」。</summary>
        private static string ServiceTag(SkyIslandServiceReadiness state, int price, int waitSeconds)
        {
            switch (state)
            {
                case SkyIslandServiceReadiness.Ready:
                    return L10n.T("（", " (") + price + L10n.T("）", ")");
                case SkyIslandServiceReadiness.ShortOfMoney:
                    return L10n.T("（要 ", " (costs ") + price + L10n.T(" · 钱不够）", " · not enough money)");
                case SkyIslandServiceReadiness.CoolingDown:
                    return L10n.T("（药还在熬 · ", " (still steeping · ") + waitSeconds + L10n.T(" 秒）", "s)");
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 「航务委托」入口：委托单独一页（<see cref="OpenContracts"/>），这里只挂一个入口。
        /// 这一趟派完了、暂时没有能接的活时入口也不挂，原因由 <see cref="BountyChoices"/> 写进正文。
        /// </summary>
        /// <param name="back">「返回」重开的上一页。</param>
        private void ContractsChoice(List<SkyIslandStoryPresentation.Choice> choices, Func<Vector3> rewardPosition, Action back)
        {
            SkyIslandBounty contract = session.Bounty;
            if (contract == null) return;
            // 按委托页自己的判据试建一遍：一项都挂不出来就不挂入口（「为什么没有」已经进了本页的 Hint）。
            var page = new List<SkyIslandStoryPresentation.Choice>();
            BountyChoices(page, rewardPosition);
            if (page.Count == 0) return;
            string label = contract.HasActive
                ? L10n.T("航务委托 · ", "Lane contracts · ") + contract.Describe()
                : L10n.T("航务委托 · 苇白有活要派", "Lane contracts · Weibai has work to hand out");
            choices.Add(new SkyIslandStoryPresentation.Choice(label, delegate
            {
                OpenContracts(rewardPosition, back);
                // 返回 null：新开的委托页自己的正文保持不动（见 SkyIslandStoryPresentation.BuildChoice）。
                return null;
            }));
        }

        /// <summary>
        /// 委托页：派单 / 交付 / 退单 + 返回。它是列表页，和合成台一样允许到 6 项（AGENTS §4.14），也和合成台一样不带立绘：
        /// 立绘要在主视觉里占一截高度（还要给右上角 ESC 键帽让位），列表页的选项行需要那一截（面板布局属性测试按此复算）。
        /// </summary>
        private void OpenContracts(Func<Vector3> rewardPosition, Action back)
        {
            if (BlockedByCombat()) return;
            reopen = delegate { OpenContracts(rewardPosition, back); };
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            BountyChoices(choices, rewardPosition);
            if (back != null)
                choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("返回", "Back"), delegate { back(); return null; }));
            presentation.Show(L10n.T("航务委托", "Lane contracts"), WithNextStep(ContractsBrief()), choices, null,
                SkyIslandUiArt.GetScene("Search_B"));
        }

        /// <summary>委托页正文：手上这一单的进度 + 这一趟交了几单。</summary>
        private string ContractsBrief()
        {
            SkyIslandBounty contract = session.Bounty;
            if (contract == null) return string.Empty;
            string rounds = L10n.T("这一趟已交付 ", "Delivered this trip: ") + contract.CompletedRounds + "/" + SkyIslandBounty.MaxRounds;
            return contract.HasActive
                ? L10n.T("手上这一单：", "Current contract: ") + contract.Describe() + "\n" + rounds
                : rounds;
        }

        /// <summary>「翻阅群岛手记」：苇白与码头装置各挂一份，打开的是同一本（只读存档，不写任何东西）。</summary>
        private void JournalChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("翻阅群岛手记", "Open the archipelago journal"), delegate
            {
                OpenJournal();
                // 回调的返回值会写进（新开的）面板正文：返回导语，与手记首页自己的正文一致。
                return SkyIslandJournal.Brief(story.Current);
            }));
        }

        /// <summary>
        /// 「打开合成台」。居民与兜底装置各挂一份：渡口工台 = 浮舟 / 码头装置，灶台 = 晴禾 / 菜畦，药臼 = 眠苔 / 悬根林见闻点。
        /// 居民婚后离岛（晴禾）或生成失败时，配方照样可用。
        /// </summary>
        private void CraftChoice(List<SkyIslandStoryPresentation.Choice> choices, SkyIslandCraftStation station)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(SkyIslandFieldcraftRules.StationChoice(station), delegate
            {
                if (fieldcraft == null)
                    return L10n.T("工具还没摆开，等群岛就绪再来。", "The tools are not laid out yet — come back once the isles are ready.");
                OpenCrafting(station);
                // 返回 null：新开的合成面板自己的正文保持不动（见 SkyIslandStoryPresentation.BuildChoice）。
                return null;
            }));
        }
    }
}
