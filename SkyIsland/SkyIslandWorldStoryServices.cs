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
                choices.Add(SkyIslandStoryPresentation.AsSecondary(new SkyIslandStoryPresentation.Choice(L10n.T("返回", "Back"), delegate { back(); return null; })));
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
                    return L10n.T("工具还没摆开，等群岛就绪再来。", "The tools aren't laid out yet. Come back once the isles are ready.");
                OpenCrafting(station);
                // 返回 null：新开的合成面板自己的正文保持不动（见 SkyIslandStoryPresentation.BuildChoice）。
                return null;
            }));
        }

        #region 确认页与阅读页栏目（2026-09-24 UI 共识对照审查 B-14 / B-15 / B-32）

        private static readonly string ConfirmWarningTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";

        /// <summary>
        /// 不可逆动作的确认页（退单、和解 / 战胜二选一的挑战）。用面板自己的换页（<see cref="reopen"/> 机制），不另开共享确认框：
        /// 这块面板本身就是模态（时间压到 0），再叠一层弹窗就是两份输入租约、两套 ESC。
        /// 页型照 UI 制作共识的确认页：标题问句 + 后果（红字）+ 确认一项 +「再想想」（次级色，回到点之前那一页）；
        /// ESC 照常整个关掉面板，什么都没做。打开时不继承上一页的键盘当前项，免得回车连按两下就把确认按掉了。
        /// 确认时 <see cref="reopen"/> 先指回上一页，动作里的 <see cref="Refreshed"/> 重开的就是那一页。
        /// 返回 null：新开的确认页自己的正文保持不动（见 SkyIslandStoryPresentation.BuildChoice）。
        /// </summary>
        private string ConfirmPage(string title, string warning, string confirmLabel, Func<string> confirm, Sprite banner)
        {
            ShowConfirmPage(title, warning, confirmLabel, confirm, banner, reopen);
            return null;
        }

        private void ShowConfirmPage(string title, string warning, string confirmLabel, Func<string> confirm, Sprite banner, Action back)
        {
            reopen = delegate { ShowConfirmPage(title, warning, confirmLabel, confirm, banner, back); };
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(confirmLabel, delegate
            {
                reopen = back;
                return confirm();
            }));
            choices.Add(SkyIslandStoryPresentation.AsSecondary(new SkyIslandStoryPresentation.Choice(L10n.T("再想想", "Not yet"), delegate
            {
                if (back != null) back();
                else presentation.Close();
                return null;
            })));
            presentation.ResetFocusOnNextShow();
            presentation.Show(title, ConfirmWarningTag + warning + "</color>", choices, null, banner);
        }

        /// <summary>退单确认页的后果（B-14）：进度作废、没有谢礼；不占轮次，退完马上能接下一单（SkyIslandBounty.TryAbandon）。</summary>
        private static string DropContractWarning(string contract)
        {
            return L10n.T("手上这一单：", "Current contract: ") + contract + "\n" +
                L10n.T("退掉之后，这一单已经做的进度全部作废，也拿不到谢礼。退完可以马上再接一单。",
                    "Dropping it throws away all progress on it, and there is no reward. You can take another contract right away.");
        }

        private static string ChallengeConfirmTitle(string id)
        {
            if (string.Equals(id, "Zheling", StringComparison.Ordinal))
                return L10n.T("真的要和折翎动手吗？", "Really fight Zheling?");
            if (string.Equals(id, "BellKeeper", StringComparison.Ordinal))
                return L10n.T("真的要挑战守钟装置吗？", "Really take on the bell engine?");
            return L10n.T("现在开打吗？", "Start the fight now?");
        }

        /// <summary>挑战确认页的后果（B-15）：两个具名对手都是和解 / 战胜二选一，打赢就永久关掉和解线（SkyIslandStoryRules：ZhelingResolved / BellKeeperResolved）。</summary>
        private static string ChallengeWarning(string id)
        {
            if (string.Equals(id, "Zheling", StringComparison.Ordinal))
                return L10n.T("打赢以后，和折翎坐下来谈的那条路就永久关了，旧信与航路图再也交不出去。想和解的话，别点这里，去走「留下来谈」那条线。",
                    "Win this and talking it out with Zheling is closed for good: the old letter and the route chart can never be shown. To make peace, skip this and follow the 'Stay and talk' route.");
            if (string.Equals(id, "BellKeeper", StringComparison.Ordinal))
                return L10n.T("打赢以后就再也不能和钟守和解。想和解的话，别点这里，先证明航路安全。",
                    "Win this and you can never reconcile with the Bell Keeper. To make peace, skip this and prove the lanes are safe.");
            return L10n.T("开打之后就没有回头路。", "Once it starts, there is no turning back.");
        }

        /// <summary>
        /// 手记子页的一个栏目：点了正文换成它，这一行一直用 WarningText 行边标着「正在看这一栏」，直到点别的栏目
        /// （B-32；UI 制作共识第 6 节：选中态看得见、不置灰）。键盘 / 悬停焦点仍是 Accent 行边，两者分开。
        /// </summary>
        private static SkyIslandStoryPresentation.Choice Section(List<SkyIslandStoryPresentation.Choice> group, string label, Func<string> body)
        {
            SkyIslandStoryPresentation.Choice choice = null;
            choice = new SkyIslandStoryPresentation.Choice(label, delegate
            {
                SkyIslandStoryPresentation.MarkCurrent(group, choice);
                return body();
            });
            return choice;
        }

        #endregion

        #region 世界里的光与纪念物木桩（2026-09-23 审美审查 UE-08 / UE-20）

        /// <summary>
        /// 纪念物点光的世界色：调用点仍按语义传 UI token（航标绿 / 星灯金 / 天青），这里换成世界里的光色。
        /// UI token 是给深底上的小字配的：<c>Success</c> 是给白字垫底的暗绿按钮色，拿来当 14 m 的点光在地上是一片发闷的绿；
        /// <c>Accent</c> 的薄荷青、<c>WarningText</c> 的亮黄在暖琥珀的岛上也都偏艳。强度与范围不变。
        /// </summary>
        private static Color WorldLight(Color token)
        {
            if (token == BossRushUIColors.Success) return new Color(0.72f, 0.92f, 0.76f);        // 风标、菜畦：柔和的叶绿
            if (token == BossRushUIColors.WarningText) return new Color(1.0f, 0.86f, 0.58f);     // 星灯、钟庭：暖金
            if (token == BossRushUIColors.Accent) return new Color(0.66f, 0.88f, 0.90f);          // 星图、风眼、腰牌、归航船：浅天青
            return token;
        }

        /// <summary>信鸽头顶那盏光：暖白，不再借 TextPrimary 的冷白（在暖色岛上发青）。</summary>
        private static readonly Color PigeonLight = new Color(1.0f, 0.92f, 0.80f);

        private Material memorialWood;
        private bool memorialWoodSearched;

        /// <summary>纪念物木桩用的作者木材质：每趟在世界根里找一次（与桥口木牌同一张），找不到就不建木桩。</summary>
        private Material MemorialWood()
        {
            if (!memorialWoodSearched)
            {
                memorialWoodSearched = true;
                memorialWood = SkyIslandGates.FindWood(root);
            }
            return memorialWood;
        }

        #endregion
    }
}
