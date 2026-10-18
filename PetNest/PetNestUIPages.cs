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
        /// <summary>Shiny 只描金边、不给标题挂流光（成就行这类不是崽的格子；每个流光是一个每帧组件）。</summary>
        public bool ShinyFrameOnly;
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
        /// <summary>
        /// 稳定身份（崽 id / 目的地 id / 档位 / 血脉 key）。同页重绘时据此判断「这一项是不是刚被选中」，
        /// 只有新选中的那项播描边淡入；巢页点选据此原地改行，不整页重建（审查 A-1）。
        /// </summary>
        public string Key;
        /// <summary>面板开着时每秒重算的进度（在途远征）；与 LiveBody 同一次走表，进度条平滑过去（审查 A-3）。</summary>
        public Func<float> LiveProgress;
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
        /// <summary>排序按钮文案（「排序：等级」）与回调；null 表示不画（审查 B-1）。</summary>
        public string SortLabel;
        public Action OnSort;
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
        /// <summary>
        /// 详情里的一组单选分段（行动方式，审查 B-2）：区头 + 等宽分段 + 一句说明；Choices 为空不画。
        /// 选中项用 <see cref="PetNestActionData.Selected"/> 标出。
        /// </summary>
        public string ChoicesTitle;
        public string ChoicesCaption;
        public List<PetNestActionData> Choices = new List<PetNestActionData>();
        /// <summary>名字旁的「收藏 / 取消收藏」；null 表示不画（审查 B-7）。</summary>
        public string FavoriteLabel;
        public Action OnFavorite;
        /// <summary>立绘框描边色（炫彩第一色）；null 时按异色金 / 默认描边（审查 B-6）。</summary>
        public string IconStrokeHex;
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
        /// <summary>列表排序（面板内状态，不进存档）与切换回调（审查 B-1）。</summary>
        public PetNestSortMode SortMode;
        public Action CycleSort;
        /// <summary>就地播远征翻牌（审查 D-1）；null 时退回「关面板就揭晓」。</summary>
        public Action ShowResults;
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
        /// <summary>单选分段里的当前项（详情的行动方式）。</summary>
        public bool Selected;
    }

    /// <summary>巢页列表的排序（审查 B-1，宝可梦 HOME / 帕鲁盒子的排序口径）。</summary>
    internal enum PetNestSortMode
    {
        /// <summary>入巢顺序（存档里的原序）。</summary>
        Arrival = 0,
        /// <summary>等级从高到低。</summary>
        Level = 1,
        /// <summary>按血脉名聚在一起。</summary>
        Lineage = 2,
        /// <summary>异色、炫彩、收藏在前。</summary>
        Rarity = 3,
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
            LastSuccessText = null;
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

        /// <summary>
        /// 最近一次成功操作的一句话（审查 A-4）。面板重绘后把它弹成底部轻提示并清掉；
        /// 失败仍走常驻的红字（失败要等玩家看见并处理，成功只需要一个确认）。
        /// </summary>
        internal static string LastSuccessText;

        internal static void NoteSuccess(string text)
        {
            LastSuccessText = text;
        }

        /// <summary>失败记红字、成功弹轻提示，二选一。</summary>
        private static void NoteResult(bool ok, string failureReasonId, string successText)
        {
            NoteFailure(ok, failureReasonId);
            if (ok) NoteSuccess(successText);
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
            LastSuccessText = null;
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
        /// 崽的独立背包，容量沿用原成长规则；物品与该崽绑定。
        /// </summary>
        internal static string DescribeScavengerBackpack(int bonus)
        {
            return L10n.T(
                "这只崽有 " + bonus + " 格独立背包。在基地或出击时，靠近它选择头顶的「背包」，使用官方背包格子存取。物品跟着这只崽保存，切图、换崽或重伤退场后仍保留。放生或派去远征前先取空背包。",
                "This cub has " + bonus + " backpack slots. Approach it in the base or on a raid and choose Backpack to use the official inventory grid. Contents stay with this cub across maps, deployment changes, and knockouts. Empty it before releasing the cub or sending it on an expedition.");
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
            AddHelp(page, L10n.T("出战与独立背包", "Deploying and the cub backpack"),
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
        internal static PetNestPageContent BuildHatchPage(Action refresh, Action<PetNestHatchResult> onHatched,
            Action<List<PetNestHatchResult>> onHatchedBatch = null)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Hatch");
            // 保底进度放在孵化页最上面：开蛋前就知道还差几枚（owner 2026-09-22 要保底）。
            // 2026-10-17（审查 C-3）：从一句黄字警示改成两条进度条，警示色不再用于中性信息
            AppendPitySection(page);

            // 巢满时不挂「孵化」「凝蛋」（审查 C-1，§4.14「能不能挂」与「点了会不会被拒」同一份判据）：
            // 旧写法照挂，点了才在顶上报「巢已经满了」
            bool nestFull = PetNestService.IsFull;
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
            else if (nestFull)
            {
                eggSection.Caption = DescribeNestFullHint();
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
                card.Key = order[g];
                if (known && !nestFull)
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

            // 全部孵化（审查 C-2，抽卡十连口径）：两枚以上能孵时给一个页面级主操作，一次揭晓再看汇总。
            // 数量受巢的空位限制，按钮上写实际会孵几枚。
            int free = PetNestService.Capacity - PetNestService.PetCount;
            int hatchable = CountKnownEggs(eggs);
            int batch = Math.Min(hatchable, free);
            if (!nestFull && batch >= 2 && onHatchedBatch != null)
            {
                List<ItemStatsSystem.Item> captured = eggs;
                page.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("全部孵化（" + batch + " 枚）", "Hatch all (" + batch + ")"),
                    IsPrimary = true,
                    OnClick = delegate
                    {
                        string reason;
                        List<PetNestHatchResult> results = PetNestHatchService.TryHatchAll(captured, out reason);
                        // 孵出一部分后停下的原因也要让玩家看见（巢满是按钮数量算好的正常收尾，不提示）
                        NoteFailure(reason == null || (results.Count > 0 && reason == "nest_full"), reason);
                        if (results.Count > 0 && LastFailureText != null)
                        {
                            LastFailureText = L10n.T("孵出 " + results.Count + " 枚，其余没孵：", "Hatched " + results.Count + "; the rest didn't: ")
                                + LastFailureText;
                        }
                        if (results.Count > 0) onHatchedBatch(results);
                        if (refresh != null) refresh();
                    },
                });
            }

            AppendCondenseCards(page, refresh, onHatched, nestFull);
            return page;
        }

        /// <summary>能识别血脉的蛋有几枚（认不出的蛋原样留着，不算进「全部孵化」）。</summary>
        private static int CountKnownEggs(List<ItemStatsSystem.Item> eggs)
        {
            int count = 0;
            for (int i = 0; i < eggs.Count; i++)
            {
                string key = RelicEggConfig.ReadLineage(eggs[i]);
                if (!string.IsNullOrEmpty(key) && PetNestLineageCatalog.IsKnownLineage(key)) count++;
            }
            return count;
        }

        /// <summary>巢满时的下一步：放生腾位，或解锁更多血脉扩建。</summary>
        private static string DescribeNestFullHint()
        {
            return L10n.T("巢满了（" + PetNestService.PetCount + " / " + PetNestService.Capacity
                    + "）：先在「巢」页放生几只腾出位子，或解锁更多血脉扩建。",
                "The nest is full (" + PetNestService.PetCount + " / " + PetNestService.Capacity
                    + "): release some cubs on the Nest tab, or unlock more bloodlines to expand it.");
        }

        /// <summary>保底：两条进度条（炫彩 x / 10、异色 x / 100），出了就重新计数。</summary>
        private static void AppendPitySection(PetNestPageContent page)
        {
            PetNestNestData nest = PetNestService.Nest;
            PetNestSection pity = new PetNestSection();
            pity.Title = L10n.T("孵化保底", "Hatch pity");
            pity.Caption = L10n.T("条满的那一枚必出；出了就重新计数。", "The hatch that fills a bar is guaranteed; the bar resets when one appears.");
            pity.Items.Add(BuildPityRow(L10n.T("炫彩", "Chroma"), nest.hatchesSinceChroma, PetNestTuning.ChromaPityHatches, "pity:chroma"));
            pity.Items.Add(BuildPityRow(L10n.T("异色", "Shiny"), nest.hatchesSinceShiny, PetNestTuning.ShinyPityHatches, "pity:shiny"));
            page.Sections.Add(pity);
        }

        private static PetNestCardData BuildPityRow(string name, int since, int pityHatches, string key)
        {
            int remaining = PetNestPity.RemainingUntilGuaranteed(since, pityHatches);
            int done = Math.Max(0, pityHatches - remaining);
            PetNestCardData row = new PetNestCardData();
            row.Key = key;
            row.Title = name;
            row.Subtitle = L10n.T("最多再孵 " + remaining + " 枚必出", "Guaranteed within " + remaining + " more");
            row.Progress = pityHatches > 0 ? Math.Min(1f, done / (float)pityHatches) : 0f;
            row.ProgressText = done + " / " + pityHatches;
            return row;
        }

        /// <summary>
        /// 遗魂账本：一行 = 血脉 + 进度条 + 「12 / 20」，够数的行把「凝蛋」按钮放在行内。
        /// 旧写法进度是纯文字行、按钮却堆在底部动作条，超过 4 条还退成竖排小滚动窗（交互重排 #6）。
        /// </summary>
        private static void AppendCondenseCards(
            PetNestPageContent page, Action refresh, Action<PetNestHatchResult> onHatched, bool nestFull)
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
                row.Key = "souls:" + lineage.LineageKey;
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

                if (nestFull)
                {
                    row.Subtitle = L10n.T("够数了，巢有空位就能凝蛋", "Enough souls; condense once the nest has room");
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
        internal static PetNestPageContent BuildExpeditionPage(Action refresh, string selectedPetId, Action<string> select = null,
            Action showResults = null)
        {
            // 玩家已经站在基地、远征在此期间到期时，场景回调不会再触发一次结算。
            // 打开页面本身就是一次自然的结算时机，否则卡片会一直停在"剩余 0h0m"，
            // 要重新过图回基地才翻牌。幂等：没到期的记录不受影响。
            PetNestExpeditionService.SettleDueExpeditions();
            PetNestExpeditionService.TryGrantPendingRewards();

            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Expedition");

            int usedSlots;
            int slotCap;
            bool hasFreeSlot = PetNestExpeditionSlots.HasFreeSlot(out usedSlots, out slotCap);
            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            PetNestSection underway = null;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r == null) continue;
                if (underway == null)
                {
                    underway = new PetNestSection();
                    underway.Title = L10n.T("在途 · " + usedSlots + " / " + slotCap + " 队",
                        "Under way · " + usedSlots + " / " + slotCap + " teams");
                    page.Sections.Add(underway);
                }
                underway.Items.Add(BuildExpeditionCard(r, showResults));
            }

            if (!hasFreeSlot)
            {
                page.Body = PetNestLocalization.DescribeFailure("expedition_slots_full")
                    + L10n.T("（" + usedSlots + " / " + slotCap + " 队）", " (" + usedSlots + " / " + slotCap + " teams)");
                return page;
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
                chip.Key = pet.id;
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
            // 页顶不再常驻「亡命档是真死」的警示（审查 D-2，UI 共识第 7 节：流程页不挂风险文字）：
            // 亡命档那一列本身是红条 + 死亡率，后果写在出发前必弹的确认框里。

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
                segment.Key = destinationId;
                // 契合元素直接写在按钮第二行（审查 D-3）：信息贴着它说明的那颗按钮，不再在下面挂一整段契合表
                segment.Title = PetNestLocalization.DescribeDestination(destinationId) + (affinity ? " ★" : string.Empty);
                segment.Subtitle = DescribeElements(PetNestLineageCatalog.GetDestinationElements(destinationId));
                segment.Selected = destinationId == chosen;
                segment.OnCardClick = delegate
                {
                    _expeditionDestinationId = destinationId;
                    if (refresh != null) refresh();
                };
                where.Items.Add(segment);
            }
            PetNestLineageInfo lineage;
            string own = PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) && lineage != null
                ? L10n.T("这只崽是" + DescribeElement(lineage.Element) + "系。", "This cub is " + DescribeElement(lineage.Element) + ". ")
                : string.Empty;
            where.Caption = own + (anyAffinity ? "★ " : string.Empty) + T("ElementAffinity")
                + L10n.T("：契合时成功率 +", ": +") + FormatPercent(PetNestTuning.ElementAffinityBonus)
                + L10n.T("。", " success when matched.");
            page.Sections.Add(where);

            if (_expeditionTier < 0 || _expeditionTier > (int)PetNestRiskTier.Desperate) _expeditionTier = 0;
            PetNestSection tiers = new PetNestSection();
            tiers.Title = L10n.T("风险档", "Risk tier");
            tiers.Layout = PetNestSectionLayout.Columns;
            // 收藏的崽不走亡命档（审查 B-7）：这一列不画，原因写在区头说明里，不挂点了才被拒的卡
            if (!PetNestExpeditionService.AllowsTier(pet, PetNestRiskTier.Desperate))
            {
                if (_expeditionTier == (int)PetNestRiskTier.Desperate) _expeditionTier = 0;
                tiers.Caption = L10n.T("收藏的崽不走亡命档。", "Favorite cubs don't take Desperate runs.");
            }
            for (int t = 0; t <= (int)PetNestRiskTier.Desperate; t++)
            {
                PetNestRiskTier tier = (PetNestRiskTier)t;
                if (!PetNestExpeditionService.AllowsTier(pet, tier)) continue;
                float deathRate = PetNestExpeditionService.GetDeathRate(tier);
                int tierIndex = t;

                PetNestCardData card = new PetNestCardData();
                card.Key = "tier:" + t;
                card.Title = DescribeRisk(t);
                // 关键两个数放第一行（审查 D-5，定价页对比卡口径）：成功率按这只崽与当前目的地算（含元素契合），
                // 和出发时固化进记录的是同一个数；死亡率同样是出发时会固化的那个数
                card.Subtitle = L10n.T("成功 ", "Success ")
                    + FormatPercent(PetNestExpeditionService.ComputeSuccessRate(pet, chosen, tier))
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(deathRate);
                card.Body = FormatDuration(PetNestExpeditionService.GetDurationHours(tier))
                    + " · " + DescribeTierPayoff(tier);
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

        /// <summary>「电 / 空间」：目的地按钮第二行。</summary>
        private static string DescribeElements(ElementTypes[] elements)
        {
            string names = string.Empty;
            for (int e = 0; e < elements.Length; e++)
            {
                if (names.Length > 0) names += " / ";
                names += DescribeElement(elements[e]);
            }
            return names;
        }

        /// <summary>官方七种元素的玩家可读名（远征契合说明用）。</summary>
        private static string DescribeElement(ElementTypes element)
        {
            switch (element)
            {
                case ElementTypes.fire: return L10n.T("火", "Fire");
                case ElementTypes.poison: return L10n.T("毒", "Poison");
                case ElementTypes.electricity: return L10n.T("电", "Electric");
                case ElementTypes.space: return L10n.T("空间", "Space");
                case ElementTypes.ghost: return L10n.T("幽灵", "Ghost");
                case ElementTypes.ice: return L10n.T("冰", "Ice");
                default: return L10n.T("物理", "Physical");
            }
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
            // 成功时带回什么（2026-10-12）：玩家反馈「出行不知道能带回来什么」，数值全部取结算用的同一份
            int cashMin, cashMax;
            PetNestExpeditionService.GetCashRange(tier, out cashMin, out cashMax);
            line += "\n" + L10n.T("成功时：现金 ", "On success: cash ") + cashMin + "–" + cashMax
                + L10n.T("，同血脉遗魂 ×", ", lineage souls ×") + PetNestExpeditionService.GetSoulReward(tier);
            int lootQuality = PetNestExpeditionService.GetLootQuality(tier);
            int lootCount = PetNestExpeditionService.GetLootCount(tier);
            if (lootQuality > 0 && lootCount > 0)
            {
                line += L10n.T("，品质 ", ", ") + lootQuality + L10n.T(" 的战利品 ×", "-quality loot ×") + lootCount;
            }
            if (tier == PetNestRiskTier.Desperate)
            {
                line += L10n.T("，", ", ") + PetNestLocalization.FormatPercent(PetNestTuning.ExpeditionRelicEggChanceDesperate)
                    + L10n.T(" 带回遗种蛋", " chance of a relic egg");
            }
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
            return line + "\n" + L10n.T("唯一会真死的一档：回不来就只剩纪念碑上的名字。",
                "The only tier with real death: if it doesn't come back, only its name remains on the memorial.");
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
        private static PetNestCardData BuildExpeditionCard(PetNestExpeditionRecord r, Action showResults)
        {
            PetNestCardData card = new PetNestCardData();
            card.Key = r.id;
            // 装饰名：炫彩渐变 / 异色金字。崽已阵亡时走记录里固化的颜色，不会退化成裸名字。
            // 刻意**不**设 card.Shiny：色条优先级是 Shiny > IsDanger，
            // 让异色顶掉亡命档的红条会把「这趟很可能回不来」这条警示吃掉。
            card.Title = PetNestExpeditionService.DescribeDecoratedPetName(r);
            card.Subtitle = PetNestLocalization.DescribeDestination(r.destinationId)
                + " · " + DescribeRisk(r.riskTier)
                + " · " + T("DeathRateLabel") + " " + FormatPercent(PetNestExpeditionService.EffectiveDeathRate(r));
            card.IsDanger = r.riskTier == (int)PetNestRiskTier.Desperate;
            card.Icon = ResolveLineagePortrait(r.petLineageKey);

            if (r.settled)
            {
                // 就地揭晓（审查 D-1，碧蓝航线委托「完成」口径）：演出层压在面板之上，不用先关面板。
                // 关面板时自动翻牌的旧路径保留作兜底。
                card.Body = showResults != null
                    ? L10n.T("已经回来了。", "Back already.")
                    : L10n.T("已经回来了，关掉面板就揭晓结果。", "Back already. Close the panel to see how it went.");
                card.Progress = 1f;
                if (showResults != null)
                {
                    card.ActionLabel = L10n.T("查看结果", "See results");
                    card.OnClick = showResults;
                }
                return card;
            }
            card.Body = DescribeExpeditionRemaining(r);
            card.Progress = DescribeExpeditionProgress(r);
            // 进度条跟着走表一起平滑走（审查 A-3）：旧写法只改字，进度条停在打开面板那一刻
            PetNestExpeditionRecord tracked = r;
            card.LiveProgress = delegate { return DescribeExpeditionProgress(tracked); };
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
    }
}
