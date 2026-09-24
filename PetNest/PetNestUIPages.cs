// ============================================================================
// PetNestUIPages.cs - 遗种巢面板页的数据组装（实施计划 步骤 10）
// ============================================================================
// 单向数据流（照 ModeH/ModeHUIPages.cs 的 PageContent / CardData / ActionData）：
//   服务层状态 -> 这里组装成只读快照 -> PetNestUI 只负责画。
//   页面本身不读全局状态、不写存档；按钮回调只调服务层入口并返回 failureReasonId。
//
// 与 PetNestUI.cs 拆开只为单文件行数预算；两者共用同一套 canvas 与层级常量，
// 不是第二套 UI 系统。本文件**不创建 canvas、不碰 sortingOrder**。
//
// 2026-09-24 交互重排（owner：「一股脑把所有功能都做成按钮丢出来」）：
//   巢页改成「列表 + 详情」（组装在 PetNestUINestPage.cs），孵化 / 远征 / 博物馆改成分区（PetNestSection），
//   按钮跟着它作用的那一行走，不再堆进底部动作条；亡命档出发先弹确认（PetNestUI.ConfirmDepart → 共享 BossRushConfirmDialog）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一个页面的只读内容快照。</summary>
    internal sealed class PetNestPageContent
    {
        /// <summary>页面标题（已本地化）。</summary>
        public string Title;
        /// <summary>正文 / 摘要（已本地化）。</summary>
        public string Body;
        /// <summary>网格卡片（博物馆血脉图鉴）。</summary>
        public List<PetNestCardData> Cards = new List<PetNestCardData>();
        /// <summary>分区（孵化的蛋与遗魂账本、远征的在途与派遣、纪念碑、说明）。</summary>
        public List<PetNestSection> Sections = new List<PetNestSection>();
        /// <summary>底部动作按钮（远征页的「出发」）。</summary>
        public List<PetNestActionData> Actions = new List<PetNestActionData>();
        /// <summary>顶部警示行（远征出发页的死亡率明示等）；空表示不显示。</summary>
        public string Notice;
        /// <summary>卡片按网格排（博物馆血脉图鉴，UA-17）。</summary>
        public bool CardsAsGrid;
        /// <summary>巢页的「列表 + 详情」；非 null 时面板改画左右两栏，上面几个字段不用。</summary>
        public PetNestNestView Nest;
    }

    /// <summary>分区的排法。</summary>
    internal enum PetNestSectionLayout
    {
        /// <summary>一行一项（蛋、遗魂、在途远征、碑文）。</summary>
        Rows = 0,
        /// <summary>并排的分段按钮（远征目的地）。</summary>
        Segments = 1,
        /// <summary>并排的对比卡（远征风险档）。</summary>
        Columns = 2,
        /// <summary>头像小卡网格（远征选崽）。</summary>
        Strip = 3,
    }

    /// <summary>一个分区：区头 + 说明 + 若干项。</summary>
    internal sealed class PetNestSection
    {
        public string Title;
        /// <summary>区头下面的一段说明；空表示不画。</summary>
        public string Caption;
        public PetNestSectionLayout Layout;
        public List<PetNestCardData> Items = new List<PetNestCardData>();
    }

    /// <summary>一项（行 / 分段 / 对比卡 / 网格格子）的只读数据。</summary>
    internal sealed class PetNestCardData
    {
        /// <summary>标题（崽名 / 血脉名 / 目的地名）。</summary>
        public string Title;
        /// <summary>副标题（血脉 · 等级 · 状态）。</summary>
        public string Subtitle;
        /// <summary>正文（产出、统计）。</summary>
        public string Body;
        /// <summary>是否异色（用 Legendary token 高亮）。</summary>
        public bool Shiny;
        /// <summary>是否是危险选项（亡命档、真死记录）。</summary>
        public bool IsDanger;
        /// <summary>行内按钮回调；为 null 表示不画按钮（不挂灰掉的占位按钮，§4.14）。</summary>
        public Action OnClick;
        /// <summary>行内按钮文案。</summary>
        public string ActionLabel;
        /// <summary>该项是否处于选中态（当前崽 / 批量勾选 / 选中的目的地与档位）。</summary>
        public bool Selected;
        /// <summary>点这一项本身（不是按钮）的回调：选中 / 勾选；null 表示不可点。</summary>
        public Action OnCardClick;
        /// <summary>炫彩两色（面板可读色 #RRGGBB）；非炫彩为 null。左侧色条用。</summary>
        public string ChromaHexA;
        public string ChromaHexB;
        /// <summary>左侧的图（Boss 立绘 / 官方图标 / 物品图标）。取不到为 null，**不画那一格**（UA-10）。</summary>
        public UnityEngine.Sprite Icon;
        /// <summary>图压成剪影（博物馆未解锁的血脉）。</summary>
        public bool IconLocked;
        /// <summary>批量放生模式：右侧画真勾选框，勾选态 = Selected（UA-18）。</summary>
        public bool Checkbox;
        /// <summary>
        /// 面板开着时每秒重算的正文（远征剩余时间，UA-22）；返回 null 表示该整页刷新了（到点结算）。
        /// null 表示正文不需要走表。
        /// </summary>
        public Func<string> LiveBody;
        /// <summary>进度条 0–1；小于 0 表示不画（遗魂账本、在途远征）。</summary>
        public float Progress = -1f;
        /// <summary>进度条右侧的数字（「12 / 20」）。</summary>
        public string ProgressText;
    }

    /// <summary>巢页左右两栏的只读数据（组装在 PetNestUINestPage.cs）。</summary>
    internal sealed class PetNestNestView
    {
        /// <summary>左栏顶部的出战席位格。</summary>
        public PetNestCardData Slot;
        /// <summary>容量一行（「巢 12 / 24 · 炫彩 1」）。</summary>
        public string CapacityText;
        /// <summary>「选择」/「完成」；null 表示不画（不到两只崽时没有批量可做）。</summary>
        public string ModeLabel;
        public Action OnMode;
        /// <summary>左栏列表的行。</summary>
        public List<PetNestCardData> Rows = new List<PetNestCardData>();
        /// <summary>空巢时列表里的那句话。</summary>
        public string EmptyText;
        /// <summary>右栏详情。</summary>
        public PetNestDetailData Detail;
    }

    /// <summary>巢页右栏：当前崽的详情，或批量放生的摘要。</summary>
    internal sealed class PetNestDetailData
    {
        public UnityEngine.Sprite Icon;
        public string Title;
        public bool Shiny;
        public string Subtitle;
        /// <summary>名字旁边的小号「改名」；null 表示不画。</summary>
        public Action OnRename;
        /// <summary>分区：区头 + 正文。</summary>
        public List<KeyValuePair<string, string>> Sections = new List<KeyValuePair<string, string>>();
        /// <summary>底栏没有按钮时的一行状态（「远征中 · 剩余 8 分钟」）。</summary>
        public string FooterText;
        /// <summary>底栏状态每秒重算（远征剩余时间）；返回 null 表示该整页刷新了。</summary>
        public Func<string> LiveFooter;
        /// <summary>底栏按钮：危险操作靠左，其余靠右，主操作最右。</summary>
        public List<PetNestActionData> Actions = new List<PetNestActionData>();
    }

    /// <summary>
    /// 巢页的交互状态与回调。面板（PetNestUI）持有状态，这里只读；
    /// 放进一个对象而不是把七八个参数排成一串，避免参数顺序错位。
    /// </summary>
    internal sealed class PetNestNestPageContext
    {
        public string SelectedPetId;
        /// <summary>批量放生模式：点行是勾选 / 取消勾选，而不是选中单只。</summary>
        public bool BatchMode;
        public HashSet<string> BatchSelection;
        public Action Refresh;
        public Action<string> Select;
        public Action<string> Rename;
        public Action<IList<string>> Release;
        public Action<bool> SetBatchMode;
        public Action<string> ToggleBatch;
        /// <summary>「派去远征」：切到远征页并把这只崽预先填进去。</summary>
        public Action<string> SendOnExpedition;
    }

    /// <summary>一个动作按钮（巢页详情底栏、远征页的「出发」）。</summary>
    internal sealed class PetNestActionData
    {
        /// <summary>按钮文案（已本地化）。</summary>
        public string Label;
        /// <summary>点击回调。</summary>
        public Action OnClick;
        /// <summary>是否可交互。</summary>
        public bool Interactable = true;
        /// <summary>是否是危险动作：主操作时 Danger 实心，否则 DangerText 描边的次级样式。</summary>
        public bool IsDanger;
        /// <summary>这一屏的主操作（AccentFill 整块填充，每屏最多一个，§4.14）。</summary>
        public bool IsPrimary;
    }

    /// <summary>各页的内容组装器。无状态，每次打开重新组装。</summary>
    internal static partial class PetNestUIPages
    {
        /// <summary>
        /// 最近一次操作的失败提示（已本地化）。面板每次重绘时读它并显示在顶部。
        /// 不给反馈的话，巢满 / 存档写屏障 / 远征锁定这些失败在界面上与"点歪了"
        /// 完全无法区分——玩家只会反复点同一个按钮。
        ///
        /// **进程级静态**，因此必须在面板关闭时显式清掉：不清会把上一次
        /// （甚至上一个存档槽）的失败提示带到下次开面板时重新弹一遍。
        /// </summary>
        internal static string LastFailureText;

        /// <summary>
        /// 远征页选中的目的地与风险档。进程级静态，和 LastFailureText 同纪律：
        /// 关面板 / 切页时必须清掉，否则下次开面板会停在上一次的选择上。
        /// </summary>
        private static string _expeditionDestinationId;
        private static int _expeditionTier;

        /// <summary>清空跨面板残留的失败提示。面板关闭 / 过图 / 切档都要调。</summary>
        internal static void ClearLastFailureText()
        {
            LastFailureText = null;
            _expeditionDestinationId = null;
            _expeditionTier = 0;
        }

        /// <summary>把 out failureReasonId 转成玩家可读文案并记下来。</summary>
        private static void NoteFailure(bool ok, string failureReasonId)
        {
            LastFailureText = ok || string.IsNullOrEmpty(failureReasonId)
                ? null
                : PetNestLocalization.DescribeFailure(failureReasonId);
        }

        /// <summary>供同层弹窗（如命名弹窗）回写失败原因。</summary>
        internal static void NoteExternalFailure(bool ok, string failureReasonId)
        {
            NoteFailure(ok, failureReasonId);
        }

        /// <summary>清空失败提示（切页时调）。</summary>
        internal static void ClearFailure()
        {
            LastFailureText = null;
            _expeditionDestinationId = null;
            _expeditionTier = 0;
        }

        private static string T(string suffix)
        {
            return LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + suffix);
        }

        #region 图

        /// <summary>
        /// 血脉的图：图鉴立绘 → 官方角色图标 → null（审美审查 UA-10）。lineageKey 就是官方 preset nameKey，
        /// 与图鉴的 bossKey 同一套键。只借用 CodexPortraitCache，不卸载它（图鉴模块在 Mod 卸载时统一卸包）。
        /// 取不到返回 null，调用方**不画那一格**，不退回汉字或灰方块。
        /// </summary>
        internal static UnityEngine.Sprite ResolveLineagePortrait(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return null;
            try
            {
                UnityEngine.Sprite portrait = CodexPortraitCache.GetPortrait(lineageKey);
                return portrait != null ? portrait : CodexPortraitCache.GetOfficialIcon(lineageKey);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>物品图标（官方物品表元数据 → 预制体）。取不到返回 null。</summary>
        internal static UnityEngine.Sprite ResolveItemIcon(int typeId)
        {
            try
            {
                ItemStatsSystem.ItemMetaData meta = ItemStatsSystem.ItemAssetsCollection.GetMetaData(typeId);
                if (meta.id == typeId && meta.icon != null) return meta.icon;
                ItemStatsSystem.Item prefab = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId);
                return prefab != null ? prefab.Icon : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string LineageName(string lineageKey)
        {
            PetNestLineageInfo lineage;
            return PetNestLineageCatalog.TryGet(lineageKey, out lineage) && lineage != null
                ? lineage.DisplayName
                : lineageKey;
        }

        #endregion

        #region 页签徽标

        /// <summary>
        /// 页签上的小数字（iOS 标签栏徽标的口径）：孵化 = 能孵的蛋 + 够数的遗魂，远征 = 在途与待揭晓。
        /// 没有要做的事时返回 null，页签只写名字。只在重绘时跑，不是热路径。
        /// </summary>
        internal static string DescribeTabBadge(PetNestUIPage page)
        {
            try
            {
                int count = 0;
                if (page == PetNestUIPage.Hatch)
                {
                    count = PetNestHatchService.CollectAvailableEggs().Count + PetNestHatchService.CountCondensable();
                }
                else if (page == PetNestUIPage.Expedition)
                {
                    count = PetNestExpeditionService.Records.Count;
                }
                return count > 0 ? " ·" + count : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 共用文案

        /// <summary>保底进度：最多再孵几枚必出炫彩 / 异色（孵化页顶部与说明页共用）。</summary>
        internal static string DescribePity()
        {
            PetNestNestData nest = PetNestService.Nest;
            int chroma = PetNestPity.RemainingUntilGuaranteed(nest.hatchesSinceChroma, PetNestTuning.ChromaPityHatches);
            int shiny = PetNestPity.RemainingUntilGuaranteed(nest.hatchesSinceShiny, PetNestTuning.ShinyPityHatches);
            return L10n.T("保底：最多再孵 " + chroma + " 枚必出炫彩，最多再孵 " + shiny + " 枚必出异色（出了就重新计数）。",
                "Pity: a chroma cub within " + chroma + " more hatches, a shiny within " + shiny + " (resets when one appears).");
        }

        /// <summary>
        /// 捡漏背包讲清楚：出战崽加在**官方宠物背包**上，只在 BossRush 系列出击里生效
        /// （owner 2026-09-22 问「加宠物格子在哪里加了」；借席失败不加格子，2026-09-23 复核第 10 项）。
        /// </summary>
        internal static string DescribeScavengerBackpack(int bonus)
        {
            string amount = bonus > 0 ? " +" + bonus : string.Empty;
            return L10n.T(
                "出战崽随你进 BossRush 系列出击（标准 / 无间炼狱 / D / E / F）时，官方宠物背包" + amount
                    + " 格，在出击中打开宠物背包查看。只有崽借到官方宠物的随行席位时才加格子（席位被别的随从占着就不加）；基地与普通出击不生效，崽重伤退场后收回。",
                "In BossRush-family raids (standard / Endless / D / E / F) the deployed cub adds" + amount
                    + " slots to the official pet backpack; open the pet backpack during the raid. The slots only apply when the cub can borrow the official pet seat (not if another companion holds it). Not active in the base or normal raids; removed if the cub is carried off.");
        }

        /// <summary>扩建提示：「解锁更多血脉可以扩建巢（3 / 5）」；已到上限返回 null。容量数字取自 Tuning，避免两套真相。</summary>
        internal static string DescribeCapacityMilestone()
        {
            int[] milestones = PetNestTuning.NestCapacityMilestoneLineageCounts;
            if (milestones == null || milestones.Length == 0
                || PetNestService.Capacity >= PetNestTuning.MaxNestCapacity) return null;
            int unlocked = PetNestMuseumStats.UnlockedLineageCount;
            int next = milestones[milestones.Length - 1];
            for (int i = 0; i < milestones.Length; i++)
            {
                if (unlocked < milestones[i]) { next = milestones[i]; break; }
            }
            return T("CapacityMilestoneHint") + " (" + unlocked + " / " + next + ")";
        }

        #endregion

        #region 说明

        /// <summary>
        /// 页眉「说明」打开的一页：系统介绍、出战、扩建、保底、远征风险、放生。
        /// 这些字原来散在巢页 24 张卡的下面，实际上没人看得到（交互重排 #5）。
        /// </summary>
        internal static PetNestPageContent BuildHelpPage()
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = L10n.T("说明", "Guide");
            AddHelp(page, T("SystemName"), T("SystemDesc"));
            AddHelp(page, L10n.T("出战与捡漏背包", "Deploying and the scavenger backpack"),
                DescribeScavengerBackpack(0));
            string milestone = DescribeCapacityMilestone();
            AddHelp(page, L10n.T("巢的容量", "Nest capacity"),
                L10n.T("巢容量", "Nest capacity") + " " + PetNestService.Pets.Count + " / " + PetNestService.Capacity
                    + (milestone != null ? "\n" + milestone : string.Empty));
            AddHelp(page, L10n.T("孵化保底", "Hatch pity"), DescribePity());
            AddHelp(page, T("SoulLedger"), T("SoulDesc"));
            AddHelp(page, T("Page_Expedition"),
                DescribeRisk((int)PetNestRiskTier.Safe) + "：" + DescribeTierPayoff(PetNestRiskTier.Safe).Replace("\n", " ")
                + "\n" + DescribeRisk((int)PetNestRiskTier.Rough) + "：" + DescribeTierPayoff(PetNestRiskTier.Rough).Replace("\n", " ")
                + "\n" + DescribeRisk((int)PetNestRiskTier.Desperate) + "：" + DescribeTierPayoff(PetNestRiskTier.Desperate).Replace("\n", " "));
            AddHelp(page, L10n.T("放生", "Releasing"), T("Release_Warn")
                + L10n.T("每只返还 " + PetNestTuning.ReleaseSoulRefund + " 遗魂。",
                    " Each returns " + PetNestTuning.ReleaseSoulRefund + " souls."));
            return page;
        }

        private static void AddHelp(PetNestPageContent page, string title, string caption)
        {
            PetNestSection section = new PetNestSection();
            section.Title = title;
            section.Caption = caption;
            page.Sections.Add(section);
        }

        #endregion

        #region 孵化

        /// <summary>孵化页：可孵化的蛋（按血脉合并）+ 遗魂账本（进度条与行内凝蛋）。</summary>
        internal static PetNestPageContent BuildHatchPage(Action refresh, Action<PetNestHatchResult> onHatched)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Hatch");
            // 保底进度放在孵化页最上面：开蛋前就知道还差几枚（owner 2026-09-22 要保底）
            page.Notice = DescribePity();

            List<ItemStatsSystem.Item> eggs = PetNestHatchService.CollectAvailableEggs();

            // 同血脉的蛋合成一行「×N」：十枚同样的蛋铺十张卡，玩家要自己数（交互重排 #6）
            List<string> order = new List<string>();
            Dictionary<string, List<ItemStatsSystem.Item>> groups = new Dictionary<string, List<ItemStatsSystem.Item>>();
            for (int i = 0; i < eggs.Count; i++)
            {
                string key = RelicEggConfig.ReadLineage(eggs[i]) ?? string.Empty;
                List<ItemStatsSystem.Item> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<ItemStatsSystem.Item>();
                    groups[key] = group;
                    order.Add(key);
                }
                group.Add(eggs[i]);
            }

            PetNestSection eggSection = new PetNestSection();
            eggSection.Title = L10n.T("可孵化的蛋", "Eggs to hatch") + (eggs.Count > 0 ? "（" + eggs.Count + "）" : string.Empty);
            if (eggs.Count == 0)
            {
                eggSection.Caption = L10n.T("背包和仓库里没有遗种蛋。打 Boss 有机会带回来，也可以在下面用遗魂凝蛋。",
                    "No relic eggs in your inventory or storage. Bosses may leave one behind, or condense one from souls below.");
            }
            for (int g = 0; g < order.Count; g++)
            {
                List<ItemStatsSystem.Item> group = groups[order[g]];
                PetNestLineageInfo lineage;
                bool known = PetNestLineageCatalog.TryGet(order[g], out lineage) && lineage != null;

                PetNestCardData card = new PetNestCardData();
                card.Title = (known ? lineage.DisplayName : T("Fail_lineage_unknown"))
                    + (group.Count > 1 ? " ×" + group.Count : string.Empty);
                card.Subtitle = RelicEggConfig.GetDisplayName();
                // 蛋行左侧画蛋本身的物品图标（UA-10）
                try { card.Icon = group[0] != null ? group[0].Icon : null; }
                catch (Exception) { card.Icon = null; }
                card.Body = known
                    ? L10n.T("孵化后锁定出身、性格、炫彩与异色。一次孵一枚。", "Hatching locks endowments, temperament, chroma and shiny. One egg at a time.")
                    : T("Fail_lineage_unknown");
                if (known)
                {
                    ItemStatsSystem.Item captured = group[0];
                    card.ActionLabel = L10n.T("孵化", "Hatch");
                    card.OnClick = delegate
                    {
                        PetNestHatchResult result;
                        string reason;
                        // commit-before-reveal：服务层先落档，成功后才把只读结果交演出层
                        bool ok = PetNestHatchService.TryHatchEgg(captured, out result, out reason);
                        NoteFailure(ok, reason);
                        if (ok && onHatched != null)
                        {
                            onHatched(result);
                        }
                        if (refresh != null) refresh();
                    };
                }
                eggSection.Items.Add(card);
            }
            page.Sections.Add(eggSection);

            AppendCondenseCards(page, refresh, onHatched);
            return page;
        }

        /// <summary>
        /// 遗魂账本：一行 = 血脉 + 进度条 + 「12 / 20」，够数的行把「凝蛋」按钮放在行内。
        /// 旧写法进度是纯文字行、按钮却堆在底部动作条，超过 4 条还退成竖排小滚动窗（交互重排 #6）。
        /// </summary>
        private static void AppendCondenseCards(
            PetNestPageContent page, Action refresh, Action<PetNestHatchResult> onHatched)
        {
            PetNestSection ledger = new PetNestSection();
            ledger.Title = T("SoulLedger");
            ledger.Caption = T("SoulDesc");
            page.Sections.Add(ledger);

            // 按「离凝蛋还差多少」升序：可凝的与快凑够的排最前，玩家一眼看到的是下一步能做什么
            List<PetNestLineageInfo> lines = CollectSoulLedgerLines();
            if (lines.Count == 0)
            {
                ledger.Caption += "\n" + L10n.T("还没有攒下遗魂。", "No souls banked yet.");
                return;
            }
            int need = PetNestTuning.SoulsPerCondensedEgg;
            for (int i = 0; i < lines.Count; i++)
            {
                PetNestLineageInfo lineage = lines[i];
                int souls = PetNestService.GetSouls(lineage.LineageKey);

                PetNestCardData row = new PetNestCardData();
                row.Title = lineage.DisplayName;
                row.Icon = ResolveLineagePortrait(lineage.LineageKey);
                row.Progress = need > 0 ? Math.Min(1f, souls / (float)need) : 1f;
                row.ProgressText = souls + " / " + need;

                if (!PetNestHatchService.CanCondense(lineage.LineageKey))
                {
                    row.Subtitle = L10n.T("还差 " + (need - souls) + " 遗魂", (need - souls) + " more souls needed");
                    ledger.Items.Add(row);
                    continue;
                }

                row.Subtitle = L10n.T("够数了，可以凝成一枚蛋直接孵化", "Enough to condense an egg and hatch it");
                string key = lineage.LineageKey;
                row.ActionLabel = T("CondenseEgg");
                row.OnClick = delegate
                {
                    PetNestHatchResult result;
                    string reason;
                    bool ok = PetNestHatchService.TryCondenseAndHatch(key, out result, out reason);
                    NoteFailure(ok, reason);
                    if (ok && onHatched != null)
                    {
                        onHatched(result);
                    }
                    if (refresh != null) refresh();
                };
                ledger.Items.Add(row);
            }
        }

        /// <summary>
        /// 攒过遗魂的血脉，按距离凝蛋阈值由近到远排序。
        /// 只在打开孵化页时跑一次，血脉量级只有几十条，不是热路径。
        /// </summary>
        private static List<PetNestLineageInfo> CollectSoulLedgerLines()
        {
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            List<PetNestLineageInfo> ledger = new List<PetNestLineageInfo>(lineages.Count);
            for (int i = 0; i < lineages.Count; i++)
            {
                PetNestLineageInfo lineage = lineages[i];
                if (lineage == null) continue;
                if (PetNestService.GetSouls(lineage.LineageKey) <= 0) continue;
                ledger.Add(lineage);
            }

            ledger.Sort(delegate (PetNestLineageInfo a, PetNestLineageInfo b)
            {
                int remainingA = PetNestTuning.SoulsPerCondensedEgg
                    - PetNestService.GetSouls(a.LineageKey);
                int remainingB = PetNestTuning.SoulsPerCondensedEgg
                    - PetNestService.GetSouls(b.LineageKey);
                if (remainingA != remainingB) return remainingA.CompareTo(remainingB);
                // 余量相同时按名字定序，避免同一份数据两次打开顺序不一样
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
            });
            return ledger;
        }

        #endregion

        #region 天灾远征

        /// <summary>
        /// 远征页：在途远征 + 一屏完成的派遣（选崽 → 目的地 → 风险档 → 出发）。
        /// **死亡率必须明示**：每张风险档卡都带出发时会固化的那个数字。
        /// <paramref name="select"/> 是远征页里换崽的回调（和巢页共用同一个选中）；为 null 时不画选崽区。
        /// </summary>
        internal static PetNestPageContent BuildExpeditionPage(Action refresh, string selectedPetId, Action<string> select = null)
        {
            // 玩家已经站在基地、远征在此期间到期时，场景回调不会再触发一次结算。
            // 打开页面本身就是一次自然的结算时机，否则卡片会一直停在"剩余 0h0m"，
            // 要重新过图回基地才翻牌。幂等：没到期的记录不受影响。
            PetNestExpeditionService.SettleDueExpeditions();
            PetNestExpeditionService.TryGrantPendingRewards();

            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Expedition");

            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            PetNestSection underway = null;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r == null) continue;
                if (underway == null)
                {
                    underway = new PetNestSection();
                    underway.Title = L10n.T("在途", "Under way");
                    page.Sections.Add(underway);
                }
                underway.Items.Add(BuildExpeditionCard(r));
            }

            AppendPetPicker(page, selectedPetId, select, refresh);

            PetNestPetRecord pet = PetNestService.TryGetPet(selectedPetId);
            if (pet == null)
            {
                page.Body = L10n.T("巢里暂时没有能派出去的崽：在巢待命或出战中的崽才能出发。",
                    "No cub can go out right now: only cubs resting in the nest or deployed can depart.");
                return page;
            }

            string failureReasonId;
            if (!PetNestExpeditionService.CanDepart(pet, out failureReasonId))
            {
                page.Body = PetNestService.GetDecoratedPetName(pet) + "："
                    + PetNestLocalization.DescribeFailure(failureReasonId);
                return page;
            }
            AppendDepartCards(page, pet, refresh);
            return page;
        }

        /// <summary>
        /// 选崽：能出发的崽排成头像小卡，点一下换人。从巢页「派去远征」过来时已经选好了；
        /// 旧写法要求「先回巢选一只」，是跨页的隐式状态（交互重排 #2）。
        /// </summary>
        private static void AppendPetPicker(PetNestPageContent page, string selectedPetId, Action<string> select, Action refresh)
        {
            if (select == null) return;
            PetNestSection picker = new PetNestSection();
            picker.Title = L10n.T("派谁去", "Who goes");
            picker.Layout = PetNestSectionLayout.Strip;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                string reason;
                if (pet == null || !PetNestExpeditionService.CanDepart(pet, out reason)) continue;
                string petId = pet.id;
                PetNestCardData chip = new PetNestCardData();
                chip.Title = PetNestService.GetDecoratedPetName(pet);
                chip.Subtitle = "Lv" + pet.level;
                chip.Icon = ResolveLineagePortrait(pet.lineageKey);
                chip.Shiny = pet.shiny;
                chip.Selected = string.Equals(petId, selectedPetId, StringComparison.Ordinal);
                chip.OnCardClick = delegate
                {
                    select(petId);
                    if (refresh != null) refresh();
                };
                picker.Items.Add(chip);
            }
            if (picker.Items.Count > 0) page.Sections.Add(picker);
        }

        /// <summary>
        /// 目的地 + 风险档，一屏选完（2026-09-24 交互重排）。
        ///   旧版：目的地卡 → 点「选择」进二级 → 风险档卡直接「出发」；三张目的地卡正文一模一样，
        ///   亡命档（真死）点一下就出发、没有确认。
        ///   现在：目的地是三个并排的分段按钮（契合的标 ★，默认落在契合的那个），风险档是三列对比卡，
        ///   底栏一个「出发」主按钮；亡命档先弹确认（PetNestUI.ConfirmDepart，共享 BossRushConfirmDialog）。3 + 3 项不需要二级（§4.14）。
        /// </summary>
        private static void AppendDepartCards(PetNestPageContent page, PetNestPetRecord pet, Action refresh)
        {
            PetNestDestinationInfo[] destinations = PetNestExpeditionService.Destinations;
            if (destinations == null || destinations.Length == 0) return;
            page.Notice = L10n.T(
                "亡命档是真死：崽不会回来，只会留在纪念碑上。出发前请看清死亡率。",
                "Desperate runs kill for real: the cub never comes back, only its name on the memorial. "
                + "Read the death rate before you commit.");

            if (string.IsNullOrEmpty(_expeditionDestinationId)
                || PetNestExpeditionService.TryGetDestination(_expeditionDestinationId) == null)
            {
                _expeditionDestinationId = destinations[0].Id;
                for (int d = 0; d < destinations.Length; d++)
                {
                    if (!PetNestExpeditionService.HasElementAffinity(pet, destinations[d].Id)) continue;
                    _expeditionDestinationId = destinations[d].Id;
                    break;
                }
            }
            string chosen = _expeditionDestinationId;

            PetNestSection where = new PetNestSection();
            where.Title = L10n.T("目的地", "Destination");
            where.Layout = PetNestSectionLayout.Segments;
            bool anyAffinity = false;
            for (int d = 0; d < destinations.Length; d++)
            {
                string destinationId = destinations[d].Id;
                bool affinity = PetNestExpeditionService.HasElementAffinity(pet, destinationId);
                anyAffinity |= affinity;
                PetNestCardData segment = new PetNestCardData();
                segment.Title = PetNestLocalization.DescribeDestination(destinationId) + (affinity ? " ★" : string.Empty);
                segment.Selected = destinationId == chosen;
                segment.OnCardClick = delegate
                {
                    _expeditionDestinationId = destinationId;
                    if (refresh != null) refresh();
                };
                where.Items.Add(segment);
            }
            if (anyAffinity)
            {
                where.Caption = "★ " + T("ElementAffinity")
                    + L10n.T("：和这只崽的元素契合，成功率更高。", ": matches this cub's element, higher success rate.");
            }
            page.Sections.Add(where);

            if (_expeditionTier < 0 || _expeditionTier > (int)PetNestRiskTier.Desperate) _expeditionTier = 0;
            PetNestSection tiers = new PetNestSection();
            tiers.Title = L10n.T("风险档", "Risk tier");
            tiers.Layout = PetNestSectionLayout.Columns;
            for (int t = 0; t <= (int)PetNestRiskTier.Desperate; t++)
            {
                PetNestRiskTier tier = (PetNestRiskTier)t;
                float deathRate = PetNestExpeditionService.GetDeathRate(tier);
                int tierIndex = t;

                PetNestCardData card = new PetNestCardData();
                card.Title = DescribeRisk(t);
                card.Subtitle = FormatDuration(PetNestExpeditionService.GetDurationHours(tier))
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(deathRate);
                card.Body = DescribeTierPayoff(tier);
                card.IsDanger = tier == PetNestRiskTier.Desperate;
                card.Selected = t == _expeditionTier;
                card.OnCardClick = delegate
                {
                    _expeditionTier = tierIndex;
                    if (refresh != null) refresh();
                };
                tiers.Items.Add(card);
            }
            page.Sections.Add(tiers);

            PetNestRiskTier picked = (PetNestRiskTier)_expeditionTier;
            string petId = pet.id;
            page.Actions.Add(new PetNestActionData
            {
                Label = L10n.T("出发 · ", "Depart · ") + DescribeRisk(_expeditionTier),
                IsPrimary = true,
                // 亡命档的出发按钮是红的：点下去还有一道确认，但颜色先把「这趟可能回不来」说出来
                IsDanger = picked == PetNestRiskTier.Desperate,
                OnClick = delegate
                {
                    if (picked == PetNestRiskTier.Desperate)
                    {
                        PetNestUI.ConfirmDepart(petId, chosen, picked, refresh);
                        return;
                    }
                    TryDepartAndNote(petId, chosen, picked);
                    if (refresh != null) refresh();
                },
            });
        }

        /// <summary>出发并记下失败原因。远征页（稳妥 / 冒险）与亡命档确认弹窗共用。</summary>
        internal static bool TryDepartAndNote(string petId, string destinationId, PetNestRiskTier tier)
        {
            PetNestExpeditionRecord record;
            string reason;
            bool ok = PetNestExpeditionService.TryDepart(petId, destinationId, tier, out record, out reason);
            NoteFailure(ok, reason);
            if (ok)
            {
                _expeditionDestinationId = null;
                _expeditionTier = 0;
            }
            return ok;
        }

        /// <summary>该档位的产出口径（经验与遗魂），出发前明示。</summary>
        private static string DescribeTierPayoff(PetNestRiskTier tier)
        {
            int exp;
            switch (tier)
            {
                case PetNestRiskTier.Rough: exp = PetNestTuning.PetExpExpeditionSurviveRough; break;
                case PetNestRiskTier.Desperate: exp = PetNestTuning.PetExpExpeditionSurviveDesperate; break;
                default: exp = PetNestTuning.PetExpExpeditionSurviveSafe; break;
            }
            string line = L10n.T("平安归来 +", "Safe return +") + exp + L10n.T(" 经验", " exp");
            if (tier == PetNestRiskTier.Safe)
            {
                return line + "\n" + L10n.T("绝对安全，最坏只是空手而归。",
                    "Absolutely safe. The worst case is coming home empty-handed.");
            }
            if (tier == PetNestRiskTier.Rough)
            {
                return line + "\n" + L10n.T("可能负伤留疤，但不会真死。",
                    "May come back scarred, but never dies.");
            }
            return line + "\n" + L10n.T("真死：回不来就只剩纪念碑上的名字。",
                "Real death: if it doesn't come back, only its name remains on the memorial.");
        }

        /// <summary>现实时长（小时）→ 「10 分钟」/「1 小时」这样的人话。</summary>
        internal static string FormatDuration(double hours)
        {
            if (hours <= 0d) return "—";
            int minutes = (int)Math.Round(hours * 60d);
            if (minutes < 60) return minutes + L10n.T(" 分钟", " min");
            if (minutes % 60 == 0) return (minutes / 60) + L10n.T(" 小时", "h");
            return (minutes / 60) + L10n.T(" 小时 ", "h ") + (minutes % 60) + L10n.T(" 分钟", "m");
        }

        /// <summary>在途远征的一行：装饰名、去向与档位、剩余时间与进度条（每秒走表）。</summary>
        private static PetNestCardData BuildExpeditionCard(PetNestExpeditionRecord r)
        {
            PetNestCardData card = new PetNestCardData();
            // 装饰名：炫彩渐变 / 异色金字。崽已阵亡时走记录里固化的颜色，不会退化成裸名字。
            // 刻意**不**设 card.Shiny：色条优先级是 Shiny > IsDanger，
            // 让异色顶掉亡命档的红条会把「这趟很可能回不来」这条警示吃掉。
            card.Title = PetNestExpeditionService.DescribeDecoratedPetName(r);
            card.Subtitle = PetNestLocalization.DescribeDestination(r.destinationId)
                + " · " + DescribeRisk(r.riskTier)
                + " · " + T("DeathRateLabel") + " " + FormatPercent(r.deathRate);
            card.IsDanger = r.riskTier == (int)PetNestRiskTier.Desperate;
            card.Icon = ResolveLineagePortrait(r.petLineageKey);

            if (r.settled)
            {
                card.Body = L10n.T("已经回来了，关掉面板就揭晓结果。", "Back already. Close the panel to see how it went.");
                card.Progress = 1f;
                return card;
            }
            card.Body = DescribeExpeditionRemaining(r);
            card.Progress = DescribeExpeditionProgress(r);
            // 面板开着时每秒走一次表（UA-22）：只改这一行的正文，不整页重建；到点返回 null 让面板整页刷新去结算
            PetNestExpeditionRecord live = r;
            card.LiveBody = delegate
            {
                if (live.settled || PetNestExpeditionService.GetRemainingTicks(live) <= 0L) return null;
                return DescribeExpeditionRemaining(live);
            };
            return card;
        }

        /// <summary>在途进度 0–1（出发到返回的现实时间）。</summary>
        private static float DescribeExpeditionProgress(PetNestExpeditionRecord r)
        {
            long total = r.returnTicks - r.departTicks;
            if (total <= 0L) return 1f;
            long remaining = PetNestExpeditionService.GetRemainingTicks(r);
            return Math.Max(0f, Math.Min(1f, 1f - remaining / (float)total));
        }

        /// <summary>在途远征的剩余时间。巢页详情底栏（远征中的崽）也用它。</summary>
        internal static string DescribeExpeditionRemaining(PetNestExpeditionRecord r)
        {
            long remaining = PetNestExpeditionService.GetRemainingTicks(r);
            TimeSpan span = TimeSpan.FromTicks(Math.Max(0L, remaining));
            // 一小时以内直接报分钟，最后一分钟内报秒，玩家才知道还要不要等
            string remainText;
            if (span.TotalMinutes >= 60d)
            {
                remainText = ((int)span.TotalHours) + L10n.T(" 小时 ", "h ") + span.Minutes + L10n.T(" 分钟", "m");
            }
            else if (span.TotalSeconds >= 60d)
            {
                remainText = ((int)span.TotalMinutes) + L10n.T(" 分钟", " min");
            }
            else
            {
                remainText = Math.Max(0, (int)span.TotalSeconds) + L10n.T(" 秒", "s");
            }
            return L10n.T("剩余", "Remaining") + " " + remainText;
        }

        /// <summary>
        /// 风险档位与百分比的文案口径都在 PetNestLocalization（面板与翻牌演出共用），
        /// 这里只保留薄转发，避免两个展示层各写一份 switch 与一份取整。
        /// </summary>
        private static string DescribeRisk(int riskTier)
        {
            return PetNestLocalization.DescribeRisk(riskTier);
        }

        private static string FormatPercent(float rate)
        {
            return PetNestLocalization.FormatPercent(rate);
        }

        #endregion

        #region 博物馆

        /// <summary>博物馆页：血脉图鉴网格 + 阵亡纪念碑。</summary>
        internal static PetNestPageContent BuildMuseumPage()
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Museum");
            // 血脉图鉴按 4 列网格排：一格一张立绘 + 名字 + 两行数字；未解锁的压成剪影（UA-17）
            page.CardsAsGrid = true;

            PetNestMuseumData museum = PetNestPersistenceAccess.Museum;
            int unlocked = 0;
            for (int i = 0; i < museum.lineages.Count; i++)
            {
                PetNestLineageStats stats = museum.lineages[i];
                if (stats == null) continue;
                if (stats.unlocked) unlocked++;
                page.Cards.Add(BuildLineageCard(stats));
            }

            page.Body = L10n.T("已解锁血脉", "Bloodlines unlocked")
                + " " + unlocked + " / " + PetNestLineageCatalog.Count;

            AppendMemorialCards(page, museum);
            return page;
        }

        private static PetNestCardData BuildLineageCard(PetNestLineageStats stats)
        {
            // 网格格子：Title = 名字，Subtitle = 第一行数字（击杀 · 孵化），Body = 第二行（最高等级 · 远征 · 异色）。
            // card.Shiny 只给格子描一圈金边，**不挂流光**：标题是白字血脉名，流光挂上去看不出（2026-09-23 复核第 12 项）。
            PetNestCardData card = new PetNestCardData();
            card.Title = LineageName(stats.lineageKey);
            card.Shiny = stats.shinyHatched > 0;
            card.Icon = ResolveLineagePortrait(stats.lineageKey);
            card.IconLocked = !stats.unlocked;
            if (!stats.unlocked)
            {
                card.Subtitle = L10n.T("未解锁", "Locked");
                card.Body = L10n.T("击杀", "Kills") + " " + stats.kills;
                return card;
            }
            card.Subtitle = L10n.T("击杀", "Kills") + " " + stats.kills
                + " · " + L10n.T("孵化", "Hatched") + " " + stats.hatched;
            card.Body = L10n.T("最高 Lv", "Best Lv") + stats.maxLevel
                + " · " + L10n.T("远征", "Trips") + " " + stats.expeditions
                + (stats.shinyHatched > 0
                    ? " · <color=" + PetNestChroma.ShinyTextHex + ">★" + stats.shinyHatched + "</color>"
                    : string.Empty);
            return card;
        }

        /// <summary>纪念碑：一行一位，名字 + 血脉与生涯 + 去向、档位、死亡率。旧写法是一长串「·」拼成的单行字。</summary>
        private static void AppendMemorialCards(PetNestPageContent page, PetNestMuseumData museum)
        {
            if (museum.memorials.Count == 0 && museum.mergedMemorialCount == 0) return;

            PetNestSection memorial = new PetNestSection();
            memorial.Title = T("Page_Memorial");
            for (int i = museum.memorials.Count - 1; i >= 0; i--)
            {
                PetNestMemorialEntry m = museum.memorials[i];
                if (m == null) continue;
                // 碑文一定要刻风险档位：那是玩家自己按下的选择。
                PetNestCardData row = new PetNestCardData();
                row.Body = PetNestLocalization.DescribeDestination(m.destinationId)
                    + " · " + DescribeRisk(m.riskTier)
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(m.deathRate);
                row.Subtitle = LineageName(m.lineageKey) + " · " + L10n.T("生涯", "Career") + " " + m.careerCount;
                row.IsDanger = true;
                // 名字同样要带炫彩 / 异色：崽已经被移除，碑上这一行是这份颜色**唯一**的去处。
                try
                {
                    row.Title = PetNestChroma.Decorate(m.shiny, m.chromaA, m.chromaB, m.displayName, L10n.IsChinese);
                }
                catch (Exception)
                {
                    row.Title = m.displayName;
                }
                memorial.Items.Add(row);
            }

            if (museum.mergedMemorialCount > 0)
            {
                memorial.Caption = L10n.T("碑林（更早的名字）", "Older names in the grove")
                    + " " + museum.mergedMemorialCount;
            }
            page.Sections.Add(memorial);
        }

        #endregion
    }
}
