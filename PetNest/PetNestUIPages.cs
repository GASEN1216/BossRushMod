// ============================================================================
// PetNestUIPages.cs - 遗种巢四个面板页的数据组装（实施计划 步骤 10）
// ============================================================================
// 单向数据流（照 ModeH/ModeHUIPages.cs 的 PageContent / CardData / ActionData）：
//   服务层状态 -> 这里组装成只读快照 -> PetNestUI 只负责画。
//   页面本身不读全局状态、不写存档；按钮回调只调服务层入口并返回 failureReasonId。
//
// 与 PetNestUI.cs 拆开只为单文件行数预算；两者共用同一套 canvas 与层级常量，
// 不是第二套 UI 系统。本文件**不创建 canvas、不碰 sortingOrder**。
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
        /// <summary>卡片列表（崽、蛋、目的地、图鉴条目、碑文）。</summary>
        public List<PetNestCardData> Cards = new List<PetNestCardData>();
        /// <summary>逐行拆解（遗魂账本、统计明细）。</summary>
        public List<string> Lines = new List<string>();
        /// <summary>底部动作按钮。</summary>
        public List<PetNestActionData> Actions = new List<PetNestActionData>();
        /// <summary>顶部警示行（远征出发页的死亡率明示等）；空表示不显示。</summary>
        public string Notice;
        /// <summary>
        /// 引导行：画在正文之后、卡片之前（「选一个目的地」「当前选中」一类）。
        /// 旧写法混在 Lines 里，排在它要引导的卡片**下面**（审美审查 UA-14）。
        /// </summary>
        public List<string> Header = new List<string>();
        /// <summary>Lines 前面的分区标题（遗魂账本、纪念碑）；空表示不画。区头不再和正文同样式（UA-15）。</summary>
        public string LinesHeader;
        /// <summary>卡片按网格排（博物馆血脉图鉴，UA-17）；否则一行一张长卡。</summary>
        public bool CardsAsGrid;
    }

    /// <summary>一张卡片的只读数据。</summary>
    internal sealed class PetNestCardData
    {
        /// <summary>标题（崽名 / 血脉名 / 目的地名）。</summary>
        public string Title;
        /// <summary>副标题（血脉 · 等级 · 性格）。</summary>
        public string Subtitle;
        /// <summary>正文（天赋、战痕、统计）。</summary>
        public string Body;
        /// <summary>是否异色（用 Legendary token 高亮）。</summary>
        public bool Shiny;
        /// <summary>是否是危险选项（亡命档、真死记录）。</summary>
        public bool IsDanger;
        /// <summary>点击回调；为 null 表示只读卡。</summary>
        public Action OnClick;
        /// <summary>点击按钮文案。</summary>
        public string ActionLabel;
        /// <summary>次动作回调；为 null 表示不画第二个按钮。</summary>
        public Action OnSecondary;
        /// <summary>次动作按钮文案。</summary>
        public string SecondaryLabel;
        /// <summary>该卡是否处于选中态（远征目标 / 放生目标 / 批量勾选）。</summary>
        public bool Selected;
        /// <summary>点卡片本身（不是按钮）的回调：选中 / 批量勾选；null 表示卡片不可点。</summary>
        public Action OnCardClick;
        /// <summary>炫彩两色（面板可读色 #RRGGBB）；非炫彩为 null。卡片左侧色条用。</summary>
        public string ChromaHexA;
        public string ChromaHexB;
        /// <summary>卡片左侧的图（Boss 立绘 / 官方图标 / 物品图标）。取不到为 null，**不画那一格**（UA-10）。</summary>
        public UnityEngine.Sprite Icon;
        /// <summary>图压成剪影（博物馆未解锁的血脉）。</summary>
        public bool IconLocked;
        /// <summary>批量放生模式：右上角画真勾选框，勾选态 = Selected（UA-18）。</summary>
        public bool Checkbox;
        /// <summary>
        /// 面板开着时每秒重算的正文（远征剩余时间，UA-22）；返回 null 表示该整页刷新了（到点结算）。
        /// null 表示正文不需要走表。
        /// </summary>
        public Func<string> LiveBody;
    }

    /// <summary>
    /// 巢页的交互状态与回调。面板（PetNestUI）持有状态，这里只读；
    /// 放进一个对象而不是把七八个参数排成一串，避免参数顺序错位。
    /// </summary>
    internal sealed class PetNestNestPageContext
    {
        public string SelectedPetId;
        /// <summary>批量放生模式：点卡片是勾选 / 取消勾选，而不是选中单只。</summary>
        public bool BatchMode;
        public HashSet<string> BatchSelection;
        public Action Refresh;
        public Action<string> Select;
        public Action<string> Rename;
        public Action<IList<string>> Release;
        public Action<bool> SetBatchMode;
        public Action<string> ToggleBatch;
    }

    /// <summary>一个底部动作按钮。</summary>
    internal sealed class PetNestActionData
    {
        /// <summary>按钮文案（已本地化）。</summary>
        public string Label;
        /// <summary>点击回调。</summary>
        public Action OnClick;
        /// <summary>是否可交互。</summary>
        public bool Interactable = true;
        /// <summary>是否是危险动作（用 Danger token）。</summary>
        public bool IsDanger;
    }

    /// <summary>四个页面的内容组装器。无状态，每次打开重新组装。</summary>
    internal static class PetNestUIPages
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
        /// 远征页二级选择的当前目的地。进程级静态，和 LastFailureText 同纪律：
        /// 关面板 / 切页时必须清掉，否则下次开面板会直接落在上一次的二级页上。
        /// </summary>
        private static string _expeditionDestinationId;

        /// <summary>清空跨面板残留的失败提示。面板关闭 / 过图 / 切档都要调。</summary>
        internal static void ClearLastFailureText()
        {
            LastFailureText = null;
            _expeditionDestinationId = null;
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

        #endregion

        #region 巢

        /// <summary>巢页：崽列表 + 出战席位 + 遗魂账本摘要。</summary>
        internal static PetNestPageContent BuildNestPage(PetNestNestPageContext ctx)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Nest");
            Action refresh = ctx.Refresh;

            List<PetNestPetRecord> pets = PetNestService.Pets;
            page.Body = L10n.T("巢容量", "Nest capacity") + " " + pets.Count + " / " + PetNestService.Capacity
                + DescribeRarityCounts(pets);

            string deployedId = PetNestService.Nest.deployedPetId;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null) continue;
                page.Cards.Add(BuildPetCard(pet, deployedId, ctx));
            }

            if (pets.Count == 0)
            {
                // 读档失败与「真的一只都没有」必须分开讲。写屏障期间内存被换成了空包、
                // 写入也已封锁（磁盘数据是安全的），此时再说「巢是空的」等于告诉玩家
                // 崽全丢了——最吓人的是这话还出现在存档其实完好的情况下。
                if (PetNestService.IsSaveReadOnly)
                {
                    page.Lines.Add(L10n.T(
                        "存档读取失败，已暂停写入以保护数据。这不是「崽丢了」——请退回主菜单重进本档，或联系作者。",
                        "Save data could not be read; writing is paused to protect it. Your cubs are not lost - reload this save slot."));
                }
                else
                {
                    page.Lines.Add(L10n.T("巢是空的。去打 Boss，把它们的遗种带回来。",
                        "The nest is empty. Go kill bosses and bring their relics home."));
                    // 空巢是新玩家第一次看到这个面板的地方，顺手把系统本身讲清楚
                    page.Lines.Add(T("SystemDesc"));
                }
            }
            else if (ctx.BatchMode)
            {
                page.Notice = L10n.T("批量放生：点崽的卡片勾选或取消，远征中的崽不能放生；出战中和异色 / 炫彩的崽不参与一键全选，要逐只勾。",
                    "Batch release: click cards to tick or untick. Cubs on an expedition cannot be released; the deployed cub and shiny / chroma cubs are never ticked by \"tick all\" - tick them one by one.");
            }
            else
            {
                // 引导行放在卡片之前：十几只崽时不用滚到底才看到「当前选中」（UA-14）
                PetNestPetRecord selected = PetNestService.TryGetPet(ctx.SelectedPetId);
                page.Header.Add(selected != null
                    ? L10n.T("当前选中：", "Selected: ") + PetNestService.GetDecoratedPetName(selected)
                        + L10n.T("（远征、放生都作用于它）", " (expeditions and release act on it)")
                    : L10n.T("点崽的卡片即可选中它，远征与放生都作用于选中的崽。",
                        "Click a cub's card to select it; expeditions and release act on the selected cub."));
            }

            AppendCompanionSlotLine(page);
            page.Lines.Add(DescribePity());

            // 巢满时的扩建提示：容量由图鉴解锁数派生，数字取自 Tuning 避免两套真相
            int[] milestones = PetNestTuning.NestCapacityMilestoneLineageCounts;
            if (milestones != null && milestones.Length > 0
                && PetNestService.Capacity < PetNestTuning.MaxNestCapacity)
            {
                page.Lines.Add(T("CapacityMilestoneHint") + " ("
                    + PetNestMuseumStats.UnlockedLineageCount + " / "
                    + ResolveNextCapacityMilestone(milestones) + ")");
            }

            if (ctx.BatchMode)
            {
                AppendBatchActions(page, ctx);
                return page;
            }

            page.Actions.Add(new PetNestActionData
            {
                Label = L10n.T("不带崽出门", "Leave the nest empty"),
                Interactable = !string.IsNullOrEmpty(deployedId),
                OnClick = delegate
                {
                    string reason;
                    NoteFailure(PetNestService.ClearDeployedPet(out reason), reason);
                    if (refresh != null) refresh();
                },
            });

            // 放生：巢满时唯一可预期的腾位手段（此前只能押亡命远征的死亡率等崽死）。
            // 不可逆，因此走确认弹窗；远征锁定期间禁用（服务层也会再拒一次）。
            // 点卡片选中即可放生，不必先设为出战（owner 2026-09-22）。
            PetNestPetRecord releaseTarget = PetNestService.TryGetPet(ctx.SelectedPetId);
            string releaseTargetId = releaseTarget != null ? releaseTarget.id : null;
            page.Actions.Add(new PetNestActionData
            {
                // 红底按钮上只写**素名**：炫彩「黑」端 #858B95 压在 Danger 底上只有约 2.6:1（2026-09-23 复核第 12 项），
                // 装饰名（渐变 / 金字 / 流光）改在放生确认弹窗的正文里给，那里是深色面板底。
                Label = T("Release_Action") + (releaseTarget != null
                    ? " · " + PetNestService.GetPetDisplayName(releaseTarget)
                    : string.Empty),
                IsDanger = true,
                Interactable = ctx.Release != null && releaseTarget != null
                    && releaseTarget.state != (int)PetNestPetState.OnExpedition,
                OnClick = delegate
                {
                    if (ctx.Release != null && releaseTargetId != null) ctx.Release(new[] { releaseTargetId });
                },
            });

            if (pets.Count > 1)
            {
                page.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("批量放生…", "Batch release..."),
                    Interactable = ctx.SetBatchMode != null,
                    OnClick = delegate { if (ctx.SetBatchMode != null) ctx.SetBatchMode(true); },
                });
            }
            return page;
        }

        /// <summary>批量放生模式的底部动作：放生已勾选 / 全选或全不选 / 退出。</summary>
        private static void AppendBatchActions(PetNestPageContent page, PetNestNestPageContext ctx)
        {
            List<string> picked = new List<string>();
            // 一键全选只收「普通、在巢待命」的崽：出战中的和异色 / 炫彩崽要逐只点（2026-09-23 复核第 11 项：
            // 旧写法一键把出战崽和稀有崽一起勾上，确认页也不提示，最容易误放稀有崽）。
            List<string> bulk = new List<string>();
            string deployedId = PetNestService.Nest.deployedPetId;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null || pet.state == (int)PetNestPetState.OnExpedition) continue;
                if (ctx.BatchSelection != null && ctx.BatchSelection.Contains(pet.id)) picked.Add(pet.id);
                bool protectedPet = pet.shiny || PetNestChroma.HasChroma(pet)
                    || string.Equals(pet.id, deployedId, StringComparison.Ordinal);
                if (!protectedPet) bulk.Add(pet.id);
            }

            int refund = PetNestTuning.ReleaseSoulRefund * picked.Count;
            page.Actions.Add(new PetNestActionData
            {
                Label = L10n.T("放生已勾选的 " + picked.Count + " 只（返还遗魂 +" + refund + "）",
                    "Release " + picked.Count + " ticked cubs (+" + refund + " souls)"),
                IsDanger = true,
                Interactable = picked.Count > 0 && ctx.Release != null,
                OnClick = delegate { if (ctx.Release != null && picked.Count > 0) ctx.Release(picked); },
            });

            bool allBulkPicked = bulk.Count > 0;
            for (int i = 0; i < bulk.Count && allBulkPicked; i++)
            {
                if (ctx.BatchSelection == null || !ctx.BatchSelection.Contains(bulk[i])) allBulkPicked = false;
            }
            bool untick = picked.Count > 0 && (allBulkPicked || bulk.Count == 0);
            page.Actions.Add(new PetNestActionData
            {
                Label = untick
                    ? L10n.T("全部取消勾选", "Untick all")
                    : L10n.T("勾选全部普通崽", "Tick all common cubs"),
                Interactable = (untick || bulk.Count > 0) && ctx.ToggleBatch != null,
                OnClick = delegate
                {
                    if (ctx.ToggleBatch == null) return;
                    // 取消：已勾的全部取消（含逐只勾上的稀有崽）；勾选：只勾普通崽
                    List<string> targets = untick ? picked : bulk;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        bool ticked = ctx.BatchSelection != null && ctx.BatchSelection.Contains(targets[i]);
                        if (ticked == untick) ctx.ToggleBatch(targets[i]);
                    }
                    if (ctx.Refresh != null) ctx.Refresh();
                },
            });

            page.Actions.Add(new PetNestActionData
            {
                Label = L10n.T("退出批量放生", "Leave batch release"),
                OnClick = delegate { if (ctx.SetBatchMode != null) ctx.SetBatchMode(false); },
            });
        }

        /// <summary>「炫彩 N 只 · 异色 M 只」小计；两者都没有时不写，避免一行零。</summary>
        private static string DescribeRarityCounts(List<PetNestPetRecord> pets)
        {
            int chroma = 0, shiny = 0;
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] == null) continue;
                if (PetNestChroma.HasChroma(pets[i])) chroma++;
                if (pets[i].shiny) shiny++;
            }
            if (chroma == 0 && shiny == 0) return string.Empty;
            return L10n.T("  ·  炫彩 " + chroma + " 只  ·  异色 " + shiny + " 只",
                "  ·  Chroma " + chroma + "  ·  Shiny " + shiny);
        }

        /// <summary>保底进度：最多再孵几枚必出炫彩 / 异色（巢页、孵化页共用）。</summary>
        internal static string DescribePity()
        {
            PetNestNestData nest = PetNestService.Nest;
            int chroma = PetNestPity.RemainingUntilGuaranteed(nest.hatchesSinceChroma, PetNestTuning.ChromaPityHatches);
            int shiny = PetNestPity.RemainingUntilGuaranteed(nest.hatchesSinceShiny, PetNestTuning.ShinyPityHatches);
            return L10n.T("保底：最多再孵 " + chroma + " 枚必出炫彩，最多再孵 " + shiny + " 枚必出异色（出了就重新计数）。",
                "Pity: a chroma cub within " + chroma + " more hatches, a shiny within " + shiny + " (resets when one appears).");
        }

        /// <summary>
        /// 出战崽给的「捡漏背包」格子讲清楚：它加在**官方宠物背包**上，只在 BossRush 系列出击里生效。
        /// owner 2026-09-22 问「加宠物格子在哪里加了」——此前只有天赋说明里提了一句，面板上看不到。
        /// </summary>
        private static void AppendCompanionSlotLine(PetNestPageContent page)
        {
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (deployed == null)
            {
                page.Lines.Add(L10n.T(
                    "带崽出击：设一只崽为出战，BossRush 系列出击时它会随行，并给官方宠物背包加格子（捡漏背包）。",
                    "Deploy a cub: it joins your BossRush-family raids and adds slots to the official pet backpack."));
                return;
            }
            int bonus = PetNestCompanionRuntime.ResolveCapacityBonus(deployed);
            // 借席失败时不加格子（PetNestCompanionRuntime：fail-closed），这一条也要写给玩家（2026-09-23 复核第 10 项）
            page.Lines.Add(L10n.T(
                "捡漏背包：出战崽随你进 BossRush 系列出击（标准 / 无间炼狱 / D / E / F）时，官方宠物背包 +" + bonus
                    + " 格；在出击中打开宠物背包查看。只有崽借到官方宠物的随行席位时才加格子（席位被别的随从占着就不加）；基地与普通出击不生效，崽重伤退场后收回。",
                "Scavenger backpack: in BossRush-family raids (standard / Endless / D / E / F) the deployed cub adds +"
                    + bonus + " slots to the official pet backpack; open the pet backpack during the raid. The slots only apply when the cub can borrow the official pet seat (not if another companion holds it). Not active in the base or normal raids; removed if the cub is carried off."));
        }

        /// <summary>下一个尚未达成的容量里程碑阈值；全部达成时返回最后一个。</summary>
        private static int ResolveNextCapacityMilestone(int[] milestones)
        {
            int unlocked = PetNestMuseumStats.UnlockedLineageCount;
            for (int i = 0; i < milestones.Length; i++)
            {
                if (unlocked < milestones[i]) return milestones[i];
            }
            return milestones[milestones.Length - 1];
        }

        private static PetNestCardData BuildPetCard(PetNestPetRecord pet, string deployedId, PetNestNestPageContext ctx)
        {
            PetNestCardData card = new PetNestCardData();
            bool locked = pet.state == (int)PetNestPetState.OnExpedition;
            card.Selected = ctx.BatchMode
                ? ctx.BatchSelection != null && ctx.BatchSelection.Contains(pet.id)
                : string.Equals(pet.id, ctx.SelectedPetId, StringComparison.Ordinal);
            // 批量模式的勾选框由面板在卡片右上角画真框（UA-18），不再往标题里塞 ■ / □ 字符
            card.Checkbox = ctx.BatchMode;
            card.Title = PetNestService.GetDecoratedPetName(pet);
            card.Shiny = pet.shiny;
            card.Icon = ResolveLineagePortrait(pet.lineageKey);
            PetNestChromaColor colorA = PetNestChroma.Find(pet.chromaA);
            PetNestChromaColor colorB = PetNestChroma.Find(pet.chromaB);
            if (colorA != null && colorB != null)
            {
                card.ChromaHexA = colorA.TextHex;
                card.ChromaHexB = colorB.TextHex;
            }

            PetNestLineageInfo lineage;
            string lineageName = PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) && lineage != null
                ? lineage.DisplayName
                : pet.lineageKey;
            card.Subtitle = lineageName
                + " · Lv" + pet.level
                + (PetNestProgressionService.IsAdult(pet)
                    ? " · " + T("Level_Adult")
                    : " (" + pet.exp + "/" + PetNestTuning.PetExpPerLevel + ")")
                + " · " + PetNestLocalization.DescribePersonality(pet.personalityId);

            string appearance = DescribeAppearance(pet);
            card.Body = (appearance != null ? appearance + "\n" : string.Empty)
                + DescribePetState(pet)
                + "\n" + DescribePersonality(pet)
                + "\n" + DescribeTalents(pet)
                + "\n" + DescribeScars(pet);

            string cardPetId = pet.id;
            if (ctx.BatchMode)
            {
                // 批量模式只做勾选，出战 / 改名按钮收起，免得误触把崽派上席位
                if (!locked)
                {
                    card.OnCardClick = delegate
                    {
                        if (ctx.ToggleBatch != null) ctx.ToggleBatch(cardPetId);
                        if (ctx.Refresh != null) ctx.Refresh();
                    };
                    card.ActionLabel = card.Selected ? L10n.T("取消勾选", "Untick") : L10n.T("勾选放生", "Tick");
                    card.OnClick = card.OnCardClick;
                }
                else
                {
                    card.ActionLabel = L10n.T("远征中", "On expedition");
                }
                return card;
            }

            bool deployed = string.Equals(pet.id, deployedId, StringComparison.Ordinal);
            bool selectable = !locked && pet.state != (int)PetNestPetState.Downed;

            // 点卡片本身就是选中（远征 / 放生目标），不必先设为出战
            card.OnCardClick = delegate
            {
                if (ctx.Select != null) ctx.Select(cardPetId);
                if (ctx.Refresh != null) ctx.Refresh();
            };

            card.ActionLabel = deployed
                ? L10n.T("已出战", "Deployed")
                : L10n.T("设为出战", "Deploy");
            if (!deployed && selectable)
            {
                card.OnClick = delegate
                {
                    string reason;
                    NoteFailure(PetNestService.TrySetDeployedPet(cardPetId, out reason), reason);
                    // 上席同时选中它：远征页的默认目标随之跟上，省掉一次来回
                    if (ctx.Select != null) ctx.Select(cardPetId);
                    if (ctx.Refresh != null) ctx.Refresh();
                };
            }

            // 次动作：远征中的崽也允许改名
            card.SecondaryLabel = L10n.T("改名", "Rename");
            card.OnSecondary = delegate
            {
                if (ctx.Select != null) ctx.Select(cardPetId);
                if (ctx.Rename != null) ctx.Rename(cardPetId);
            };
            return card;
        }

        /// <summary>
        /// 外观那一行：炫彩写两色色块 + 搭配名。普通崽与纯异色崽返回 null（不占一行）。
        /// 色块 ■ 是 GBK 收录字符，用各色的面板可读色画（owner 2026-09-22：炫彩要在巢里看得出来）。
        /// 异色不再在正文重写一遍「★ 异色」：卡片标题的装饰名里已经有了（2026-09-23 复核第 7 项）。
        /// </summary>
        private static string DescribeAppearance(PetNestPetRecord pet)
        {
            PetNestChromaColor a = PetNestChroma.Find(pet.chromaA);
            PetNestChromaColor b = PetNestChroma.Find(pet.chromaB);
            if (a == null || b == null) return null;
            return L10n.T("炫彩：", "Chroma: ")
                + "<color=" + a.TextHex + ">■</color><color=" + b.TextHex + ">■</color> "
                + PetNestChroma.DescribePair(pet, L10n.IsChinese);
        }

        private static string DescribePetState(PetNestPetRecord pet)
        {
            switch ((PetNestPetState)pet.state)
            {
                case PetNestPetState.Deployed:
                    return L10n.T("状态：出战席位", "State: deployed");
                case PetNestPetState.OnExpedition:
                    return L10n.T("状态：远征中", "State: on expedition");
                case PetNestPetState.Downed:
                    return L10n.T("状态：本局重伤退场", "State: carried off this run");
                default:
                    return L10n.T("状态：在巢待命", "State: resting in the nest");
            }
        }

        /// <summary>
        /// 性格那一行。性格现在真的会改索敌距离、追击意愿、跟随距离与属性
        /// （表在 PetNest/PetNestPersonality.cs），所以必须把效果写给玩家看，
        /// 否则它和改动之前一样只是个词。
        /// </summary>
        private static string DescribePersonality(PetNestPetRecord pet)
        {
            string name = PetNestLocalization.DescribePersonality(pet.personalityId);
            string effect = PetNestLocalization.DescribePersonalityEffect(pet.personalityId);
            string text = L10n.T("性格：", "Temperament: ") + name;
            if (!string.IsNullOrEmpty(effect)) text += " " + effect;
            return text;
        }

        private static string DescribeTalents(PetNestPetRecord pet)
        {
            if (pet.talents == null || pet.talents.Count == 0)
            {
                return L10n.T("出身：无", "Endowments: none");
            }
            string text = L10n.T("出身：", "Endowments: ");
            bool first = true;
            for (int i = 0; i < pet.talents.Count; i++)
            {
                PetNestTalentEntry t = pet.talents[i];
                if (t == null) continue;
                if (!first) text += "，";
                first = false;
                // 文案单点在 PetNestLocalization：此前这里直接拼英文 statKey，
                // 中文玩家看到的是 "PetCapcity+2"（还是官方拼错的那个词）
                text += PetNestLocalization.DescribeTalent(t);
            }
            return text;
        }

        /// <summary>
        /// 天赋/战痕数值的统一格式化。实现已收敛到 PetNestLocalization（面板、孵化揭晓、
        /// 战痕说明三处共用同一口径）；此处保留薄转发，避免调用点散落两个入口。
        /// </summary>
        internal static string FormatModifierValue(float value, bool percentage)
        {
            return PetNestLocalization.FormatModifierValue(value, percentage);
        }

        private static string DescribeScars(PetNestPetRecord pet)
        {
            int total = (pet.scars != null ? pet.scars.Count : 0) + pet.mergedOldScarCount;
            if (total == 0) return L10n.T("战痕：无", "Scars: none");

            // 把当前生效的减益一并写出来：战痕是履历也是代价，代价必须可见
            string text = L10n.T("战痕：", "Scars: ") + total;
            string effect = DescribeScarEffect(pet);
            if (!string.IsNullOrEmpty(effect)) text += "（" + effect + "）";
            return text;
        }

        /// <summary>
        /// 当前生效的战痕减益。
        /// 「有哪些 stat 挨过疤」与「封顶后的实际数值」两份口径都在 PetNestDownedHandler，
        /// 展示侧不再自建聚合与封顶——此前这里写的是自己的 Math.Max，随从入场挂的却是
        /// GetEffectiveScarPercent，两边一旦跑偏，面板上写的就不是玩家实际承受的。
        /// </summary>
        private static string DescribeScarEffect(PetNestPetRecord pet)
        {
            List<string> statKeys = new List<string>();
            PetNestDownedHandler.CollectScarStatKeys(pet, statKeys);
            if (statKeys.Count == 0) return null;

            string text = null;
            for (int i = 0; i < statKeys.Count; i++)
            {
                float clamped = PetNestDownedHandler.GetEffectiveScarPercent(pet, statKeys[i]);
                if (text != null) text += "，";
                text += PetNestLocalization.DescribeStatDelta(statKeys[i], clamped, true);
            }
            return text;
        }

        #endregion

        #region 孵化

        /// <summary>孵化页：可孵化的蛋 + 可凝蛋的血脉。</summary>
        internal static PetNestPageContent BuildHatchPage(Action refresh, Action<PetNestHatchResult> onHatched)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Hatch");

            List<ItemStatsSystem.Item> eggs = PetNestHatchService.CollectAvailableEggs();
            page.Body = L10n.T("背包与仓库里的遗种蛋", "Relic eggs in your inventory and storage")
                + "：" + eggs.Count;
            // 保底进度放在孵化页最上面：开蛋前就知道还差几枚（owner 2026-09-22 要保底）
            page.Notice = DescribePity();

            for (int i = 0; i < eggs.Count; i++)
            {
                ItemStatsSystem.Item egg = eggs[i];
                string lineageKey = RelicEggConfig.ReadLineage(egg);
                PetNestLineageInfo lineage;
                bool known = PetNestLineageCatalog.TryGet(lineageKey, out lineage) && lineage != null;

                PetNestCardData card = new PetNestCardData();
                card.Title = known ? lineage.DisplayName : T("Fail_lineage_unknown");
                card.Subtitle = RelicEggConfig.GetDisplayName();
                // 蛋卡左侧画蛋本身的物品图标（UA-10）
                try { card.Icon = egg != null ? egg.Icon : null; }
                catch (Exception) { card.Icon = null; }
                card.Body = known
                    ? L10n.T("孵化后锁定出身、性格、炫彩与异色。", "Hatching locks endowments, temperament, chroma and shiny.")
                    : T("Fail_lineage_unknown");
                card.ActionLabel = L10n.T("孵化", "Hatch");
                if (known)
                {
                    ItemStatsSystem.Item captured = egg;
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
                page.Cards.Add(card);
            }

            AppendCondenseCards(page, refresh, onHatched);
            return page;
        }

        private static void AppendCondenseCards(
            PetNestPageContent page, Action refresh, Action<PetNestHatchResult> onHatched)
        {
            // 血脉有几十条，攒过魂的会一行一行铺满整页。按「离凝蛋还差多少」升序，
            // 可凝的与快凑够的排最前，玩家一眼看到的是下一步能做什么。
            List<PetNestLineageInfo> ledger = CollectSoulLedgerLines();
            bool ledgerHeaderWritten = false;
            for (int i = 0; i < ledger.Count; i++)
            {
                PetNestLineageInfo lineage = ledger[i];
                int souls = PetNestService.GetSouls(lineage.LineageKey);

                if (!ledgerHeaderWritten)
                {
                    // 区头讲清「遗魂是什么、攒够能干嘛」，逐行只留进度。
                    // 区头走独立的分区标题样式（粗体 + 分隔线），不再和下面的进度行同字号同色（UA-15）。
                    page.LinesHeader = T("SoulLedger");
                    page.Lines.Add(T("SoulDesc"));
                    ledgerHeaderWritten = true;
                }

                page.Lines.Add(lineage.DisplayName + "  "
                    + souls + " / " + PetNestTuning.SoulsPerCondensedEgg + "  " + T("CondenseProgress"));

                if (!PetNestHatchService.CanCondense(lineage.LineageKey)) continue;

                string key = lineage.LineageKey;
                page.Actions.Add(new PetNestActionData
                {
                    Label = T("CondenseEgg") + " · " + lineage.DisplayName,
                    OnClick = delegate
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
                    },
                });
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
        /// 远征页：进行中的远征 + 派遣入口。
        /// **死亡率必须明示**：每个档位按钮上都带出发时会固化的那个数字。
        /// </summary>
        internal static PetNestPageContent BuildExpeditionPage(Action refresh, string selectedPetId)
        {
            // 玩家已经站在基地、远征在此期间到期时，场景回调不会再触发一次结算。
            // 打开页面本身就是一次自然的结算时机，否则卡片会一直停在"剩余 0h0m"，
            // 要重新过图回基地才翻牌。幂等：没到期的记录不受影响。
            PetNestExpeditionService.SettleDueExpeditions();
            PetNestExpeditionService.TryGrantPendingRewards();

            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Expedition");
            page.Notice = L10n.T(
                "亡命档是真死：崽不会回来，只会留在纪念碑上。出发前请看清死亡率。",
                "Desperate runs kill for real: the cub never comes back, only its name on the memorial. "
                + "Read the death rate before you commit.");

            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r == null) continue;
                page.Cards.Add(BuildExpeditionCard(r));
            }

            PetNestPetRecord pet = PetNestService.TryGetPet(selectedPetId);
            if (pet == null)
            {
                page.Body = L10n.T("先在巢里选一只崽，再派它出发。",
                    "Pick a cub in the nest first, then send it out.");
                return page;
            }

            page.Body = L10n.T("待派遣：", "Ready to depart: ") + PetNestService.GetDecoratedPetName(pet);
            string failureReasonId;
            if (!PetNestExpeditionService.CanDepart(pet, out failureReasonId))
            {
                page.Body = PetNestService.GetDecoratedPetName(pet) + "\n"
                    + PetNestLocalization.DescribeFailure(failureReasonId)
                    + "\n" + L10n.T("请回巢选择另一只可派遣的崽。", "Pick another available cub in the nest.");
                return page;
            }
            AppendDepartCards(page, pet, refresh);
            return page;
        }

        /// <summary>
        /// 出发选项。2026-09-20 改版（owner：「可选择的地方太小了而且中间有很多留白」）：
        ///   旧版把 3 目的地 × 3 档位 = 9 个按钮塞进面板底部 128px 的动作条，
        ///   上面 412px 的内容区在没有在途远征时是整片空白。
        ///   现在改成**两级卡片**，都画在内容区里：
        ///     一级 = 目的地卡（3 张，带元素契合提示）；
        ///     二级 = 选中目的地后的风险档卡（3 张，写清时长 / 死亡率 / 产出），外加「返回」。
        ///   这同时满足 AGENTS 4.14「同一页超过 3–4 项就分二级」。
        /// </summary>
        private static void AppendDepartCards(PetNestPageContent page, PetNestPetRecord pet, Action refresh)
        {
            PetNestDestinationInfo[] destinations = PetNestExpeditionService.Destinations;
            if (destinations == null || destinations.Length == 0) return;

            // 一级：还没选目的地
            if (string.IsNullOrEmpty(_expeditionDestinationId)
                || PetNestExpeditionService.TryGetDestination(_expeditionDestinationId) == null)
            {
                page.Header.Add(L10n.T("选一个目的地：", "Pick a destination:"));
                for (int d = 0; d < destinations.Length; d++)
                {
                    string destinationId = destinations[d].Id;
                    bool affinity = PetNestExpeditionService.HasElementAffinity(pet, destinationId);

                    PetNestCardData card = new PetNestCardData();
                    card.Title = PetNestLocalization.DescribeDestination(destinationId);
                    card.Subtitle = affinity
                        ? T("ElementAffinity") + L10n.T("（成功率更高）", " (higher success rate)")
                        : L10n.T("无元素契合", "No elemental affinity");
                    card.Body = BuildDestinationSummary();
                    card.ActionLabel = L10n.T("选择", "Choose");
                    card.OnClick = delegate
                    {
                        _expeditionDestinationId = destinationId;
                        if (refresh != null) refresh();
                    };
                    page.Cards.Add(card);
                }
                return;
            }

            // 二级：已选目的地，挑风险档
            string chosen = _expeditionDestinationId;
            bool chosenAffinity = PetNestExpeditionService.HasElementAffinity(pet, chosen);
            page.Header.Add(L10n.T("目的地：", "Destination: ")
                + PetNestLocalization.DescribeDestination(chosen)
                + (chosenAffinity ? " · " + T("ElementAffinity") : string.Empty));

            for (int t = 0; t <= (int)PetNestRiskTier.Desperate; t++)
            {
                PetNestRiskTier tier = (PetNestRiskTier)t;
                float deathRate = PetNestExpeditionService.GetDeathRate(tier);
                string petId = pet.id;

                PetNestCardData card = new PetNestCardData();
                card.Title = DescribeRisk(t);
                card.Subtitle = FormatDuration(PetNestExpeditionService.GetDurationHours(tier))
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(deathRate);
                card.Body = DescribeTierPayoff(tier);
                card.IsDanger = tier == PetNestRiskTier.Desperate;
                card.ActionLabel = L10n.T("出发", "Depart");
                card.OnClick = delegate
                {
                    PetNestExpeditionRecord record;
                    string reason;
                    bool ok = PetNestExpeditionService.TryDepart(petId, chosen, tier, out record, out reason);
                    NoteFailure(ok, reason);
                    if (ok) _expeditionDestinationId = null;
                    if (refresh != null) refresh();
                };
                page.Cards.Add(card);
            }

            page.Actions.Add(new PetNestActionData
            {
                Label = L10n.T("换个目的地", "Pick another destination"),
                OnClick = delegate
                {
                    _expeditionDestinationId = null;
                    if (refresh != null) refresh();
                },
            });
        }

        /// <summary>三档时长一行说清，省得玩家点进去才知道要等多久。</summary>
        private static string BuildDestinationSummary()
        {
            return L10n.T("三档可选：", "Three tiers: ")
                + PetNestLocalization.DescribeRisk((int)PetNestRiskTier.Safe) + " "
                + FormatDuration(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Safe)) + " · "
                + PetNestLocalization.DescribeRisk((int)PetNestRiskTier.Rough) + " "
                + FormatDuration(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Rough)) + " · "
                + PetNestLocalization.DescribeRisk((int)PetNestRiskTier.Desperate) + " "
                + FormatDuration(PetNestExpeditionService.GetDurationHours(PetNestRiskTier.Desperate));
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

        private static PetNestCardData BuildExpeditionCard(PetNestExpeditionRecord r)
        {
            PetNestCardData card = new PetNestCardData();
            // 装饰名：炫彩渐变 / 异色金字。崽已阵亡时走记录里固化的颜色，不会退化成裸名字。
            // 刻意**不**设 card.Shiny：卡片描边色的优先级是 Selected > Shiny > IsDanger，
            // 让异色顶掉亡命档的红边会把「这趟很可能回不来」这条警示吃掉。
            // 异色/炫彩已经写在标题的富文本里，不需要再占用描边这条通道。
            card.Title = PetNestExpeditionService.DescribeDecoratedPetName(r);
            card.Subtitle = PetNestLocalization.DescribeDestination(r.destinationId)
                + " · " + DescribeRisk(r.riskTier);
            card.IsDanger = r.riskTier == (int)PetNestRiskTier.Desperate;
            card.Icon = ResolveLineagePortrait(r.petLineageKey);

            if (r.settled)
            {
                card.Body = L10n.T("已结算，等待翻牌。", "Settled. Waiting to be revealed.");
            }
            else
            {
                card.Body = DescribeExpeditionRemaining(r);
                // 面板开着时每秒走一次表（UA-22）：只改这张卡的正文，不整页重建；到点返回 null 让面板整页刷新去结算
                PetNestExpeditionRecord live = r;
                card.LiveBody = delegate
                {
                    if (live.settled || PetNestExpeditionService.GetRemainingTicks(live) <= 0L) return null;
                    return DescribeExpeditionRemaining(live);
                };
            }
            return card;
        }

        /// <summary>在途远征卡的正文：剩余时间 + 出发时固化的死亡率。</summary>
        private static string DescribeExpeditionRemaining(PetNestExpeditionRecord r)
        {
            long remaining = PetNestExpeditionService.GetRemainingTicks(r);
            TimeSpan span = TimeSpan.FromTicks(Math.Max(0L, remaining));
            // 时长梯度改成 10 / 30 / 60 分钟之后，"0h10m" 这种写法看着像坏了；
            // 一小时以内直接报分钟，最后一分钟内报秒，玩家才知道还要不要等。
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
            return L10n.T("剩余", "Remaining") + " " + remainText
                + "\n" + T("DeathRateLabel") + " " + FormatPercent(r.deathRate);
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

        /// <summary>博物馆页：血脉图鉴 + 阵亡纪念碑。</summary>
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
            PetNestLineageInfo lineage;
            string name = PetNestLineageCatalog.TryGet(stats.lineageKey, out lineage) && lineage != null
                ? lineage.DisplayName
                : stats.lineageKey;

            // 网格格子：Title = 名字，Subtitle = 第一行数字（击杀 · 孵化），Body = 第二行（最高等级 · 远征 · 异色）。
            // card.Shiny 只给格子描一圈金边，**不挂流光**：标题是白字血脉名，流光挂上去看不出（2026-09-23 复核第 12 项）。
            PetNestCardData card = new PetNestCardData();
            card.Title = name;
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

        private static void AppendMemorialCards(PetNestPageContent page, PetNestMuseumData museum)
        {
            if (museum.memorials.Count == 0 && museum.mergedMemorialCount == 0) return;

            page.LinesHeader = T("Page_Memorial");
            for (int i = museum.memorials.Count - 1; i >= 0; i--)
            {
                PetNestMemorialEntry m = museum.memorials[i];
                if (m == null) continue;
                PetNestLineageInfo lineage;
                string lineageName = PetNestLineageCatalog.TryGet(m.lineageKey, out lineage) && lineage != null
                    ? lineage.DisplayName
                    : m.lineageKey;

                // 碑文一定要刻风险档位：那是玩家自己按下的选择。
                // 名字同样要带炫彩 / 异色：崽已经被移除，碑上这一行是这份颜色**唯一**的去处。
                string memorialName = m.displayName;
                try
                {
                    memorialName = PetNestChroma.Decorate(
                        m.shiny, m.chromaA, m.chromaB, m.displayName, L10n.IsChinese);
                }
                catch (Exception)
                {
                    memorialName = m.displayName;
                }

                page.Lines.Add(memorialName
                    + " · " + lineageName
                    + " · " + PetNestLocalization.DescribeDestination(m.destinationId)
                    + " · " + DescribeRisk(m.riskTier)
                    + " · " + T("DeathRateLabel") + " " + FormatPercent(m.deathRate)
                    + " · " + L10n.T("生涯", "Career") + " " + m.careerCount);
            }

            if (museum.mergedMemorialCount > 0)
            {
                page.Lines.Add(L10n.T("碑林（更早的名字）", "Older names in the grove")
                    + " " + museum.mergedMemorialCount);
            }
        }

        #endregion
    }
}
