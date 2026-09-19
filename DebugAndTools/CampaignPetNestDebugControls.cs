#if BOSSRUSH_DEV
// 手动演练会持久改变专用测试槽；入口门复用完整验收的槽标记，所有业务操作走生产服务。
using System;
using System.Collections.Generic;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class CampaignPetNestDebugControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action close, Action<string, bool> report)
        {
            GameObject panel = new GameObject("CampaignPetNestDebugControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("征程 / 遗种巢手动演练", "Campaign / Pet Nest manual tests"), 24, 38);
            Label(panel.transform, L10n.T("仅限基地专用测试档。按钮会永久推进此测试档、发放真实奖励；远征保留真实风险。",
                "Base + dedicated test save only. Actions persist progress and grant real rewards in that test slot; expeditions retain their normal risks."), 18, 62);

            for (int order = 1; order <= CampaignTuning.ChapterCount; order++)
            {
                int selectedOrder = order;
                CampaignChapterDef chapter = CampaignContentCatalog.GetChapterByOrder(order);
                if (chapter == null) continue;
                Button(panel.transform, L10n.T("完成第 ", "Complete chapter ") + order + " · " + L10n.T(chapter.TitleCN, chapter.TitleEN),
                    host, report, () => CompleteChapter(selectedOrder));
            }
            Label(panel.transform, L10n.T("锁定章节会先交付前置章，所选章停在待交付，回公告板检查剧情与奖励。",
                "Locked chapters first deliver their prerequisites. The selected chapter stays ready to deliver: inspect its dialogue and reward at the board."), 17, 52);

            // 逐委托、逐目标的细粒度入口。上面的「完成第 N 章」是一键推进，
            // 这一组用来单独验一条目标的上报链——目标写错、上报缺一条，只有这样才看得出来。
            int chapterCursor = 0;
            Func<CampaignChapterDef> selectedChapter = () => {
                CampaignChapterDef def = CampaignContentCatalog.GetChapterByOrder(chapterCursor % CampaignTuning.ChapterCount + 1);
                if (def == null) throw new InvalidOperationException(L10n.T("章节目录尚未就绪", "Chapter catalog is not ready"));
                return def;
            };
            Label(panel.transform, L10n.T("征程 · 单个委托与单条目标", "Campaign · one contract, one objective"), 22, 34);
            Button(panel.transform, L10n.T("下一个委托（章）", "Next contract (chapter)"), host, report,
                () => { chapterCursor++; return DescribeChapter(selectedChapter()); });
            Button(panel.transform, L10n.T("查看所选委托的目标与进度", "Show selected contract's objectives"), host, report,
                () => DescribeChapter(selectedChapter()));
            Button(panel.transform, L10n.T("只接取所选委托", "Accept selected contract only"), host, report,
                () => AcceptContract(selectedChapter()));
            Button(panel.transform, L10n.T("完成所选委托的下一条目标", "Complete the next objective of the selected contract"), host, report,
                () => CompleteNextObjective(selectedChapter()));
            Button(panel.transform, L10n.T("交付所选委托", "Deliver the selected contract"), host, report,
                () => DeliverContract(selectedChapter()));
            Button(panel.transform, L10n.T("自检：所有委托目标是否都能完成", "Self-check: can every contract objective be completed"), host, report,
                AuditAllObjectives);
            Button(panel.transform, L10n.T("打开征程公告板", "Open campaign board"), host, report,
                () => { close(); host.OpenCampaignBoardUI(); return L10n.T("已打开公告板", "Board opened"); });

            int lineageIndex = 0;
            int petIndex = 0;
            int destinationIndex = 0;
            Func<PetNestLineageInfo> lineage = () => {
                IList<PetNestLineageInfo> all = PetNestLineageCatalog.All;
                if (all == null || all.Count == 0) throw new InvalidOperationException(L10n.T("血脉目录尚未就绪", "Lineage catalog is not ready"));
                return all[lineageIndex % all.Count];
            };
            Func<PetNestPetRecord> pet = () => {
                List<PetNestPetRecord> pets = PetNestService.Pets;
                if (pets == null || pets.Count == 0) throw new InvalidOperationException(L10n.T("请先孵化一只崽", "Hatch a pet first"));
                return pets[petIndex % pets.Count];
            };
            Func<string> describe = () => {
                string petName = PetNestService.PetCount > 0 ? PetNestService.GetPetDisplayName(pet()) : L10n.T("无", "none");
                return L10n.T("血脉：", "Lineage: ") + lineage().DisplayName + L10n.T("；崽：", "; pet: ") + petName
                    + L10n.T("；远征：", "; destination: ") + PetNestExpeditionService.Destinations[destinationIndex].Id;
            };
            Label(panel.transform, L10n.T("遗种巢 · 选择与材料", "Pet Nest · selection and materials"), 22, 34);
            Button(panel.transform, L10n.T("查看当前选择与余额", "Show selection and souls"), host, report,
                () => describe() + L10n.T("；遗魂：", "; souls: ") + PetNestService.GetSouls(lineage().LineageKey));
            Button(panel.transform, L10n.T("下一种血脉", "Next lineage"), host, report, () => { lineageIndex++; return describe(); });
            Button(panel.transform, L10n.T("下一只崽", "Next pet"), host, report, () => { petIndex++; return describe(); });
            Button(panel.transform, L10n.T("下一个远征目的地", "Next expedition destination"), host, report,
                () => { destinationIndex = (destinationIndex + 1) % PetNestExpeditionService.Destinations.Length; return describe(); });
            Button(panel.transform, L10n.T("所选血脉：加一枚蛋所需遗魂", "Selected lineage: add one egg's souls"), host, report, () => {
                string key = lineage().LineageKey;
                int before = PetNestService.GetSouls(key);
                PetNestService.AddSouls(key, PetNestTuning.SoulsPerCondensedEgg, true);
                int credited = PetNestService.GetSouls(key) - before;
                if (credited <= 0) throw new InvalidOperationException("soul_credit_failed");
                PetNestSoulNotice.Queue(key, credited);
                return describe() + " +" + credited;
            });
            Button(panel.transform, L10n.T("所选血脉：发一枚实体遗种蛋", "Selected lineage: grant a physical egg"), host, report,
                () => GrantEgg(lineage().LineageKey));
            Button(panel.transform, L10n.T("所选血脉：孵化一枚背包/仓库蛋", "Selected lineage: hatch an inventory egg"), host, report, () => {
                string key = lineage().LineageKey;
                List<Item> eggs = PetNestHatchService.CollectAvailableEggs();
                for (int i = 0; i < eggs.Count; i++)
                {
                    if (RelicEggConfig.ReadLineage(eggs[i]) != key) continue;
                    PetNestHatchResult result; string reason;
                    if (!PetNestHatchService.TryHatchEgg(eggs[i], out result, out reason)) throw new InvalidOperationException(reason);
                    close(); PetNestHatchRevealView.Play(result); return L10n.T("孵化已提交", "Hatch committed");
                }
                throw new InvalidOperationException(L10n.T("没有所选血脉的实体蛋", "No physical egg of the selected lineage"));
            });
            Button(panel.transform, L10n.T("所选血脉：消耗遗魂凝蛋孵化", "Selected lineage: condense and hatch"), host, report, () => {
                PetNestHatchResult result; string reason;
                if (!PetNestHatchService.TryCondenseAndHatch(lineage().LineageKey, out result, out reason)) throw new InvalidOperationException(reason);
                close(); PetNestHatchRevealView.Play(result); return L10n.T("凝蛋已提交", "Condensation committed");
            });
            Button(panel.transform, L10n.T("所选崽：经验 +100", "Selected pet: +100 XP"), host, report, () => AddExperience(pet().id, 100));
            Button(panel.transform, L10n.T("所选崽：快速成年", "Selected pet: grow to adulthood"), host, report,
                () => AddExperience(pet().id, PetNestTuning.PetMaxLevel * PetNestTuning.PetExpPerLevel));
            Button(panel.transform, L10n.T("所选崽：设为出战", "Selected pet: deploy"), host, report, () => {
                string reason; if (!PetNestService.TrySetDeployedPet(pet().id, out reason)) throw new InvalidOperationException(reason);
                return L10n.T("出战席位已设置，下次进局观察随从", "Deployment set; inspect the companion on the next run");
            });
            Button(panel.transform, L10n.T("召回出战崽", "Recall deployed pet"), host, report, () => {
                string reason; if (!PetNestService.ClearDeployedPet(out reason)) throw new InvalidOperationException(reason);
                return L10n.T("已召回", "Recalled");
            });
            for (int risk = 0; risk <= 2; risk++)
            {
                PetNestRiskTier tier = (PetNestRiskTier)risk;
                string riskLabel = tier == PetNestRiskTier.Safe ? L10n.T("平安", "Safe")
                    : tier == PetNestRiskTier.Rough ? L10n.T("风浪", "Rough") : L10n.T("亡命", "Desperate");
                Button(panel.transform, L10n.T("所选崽远征 · ", "Send selected pet · ") + riskLabel, host, report, () => {
                    PetNestExpeditionRecord record; string reason;
                    if (!PetNestExpeditionService.TryDepart(pet().id, PetNestExpeditionService.Destinations[destinationIndex].Id,
                        tier, out record, out reason)) throw new InvalidOperationException(reason);
                    return L10n.T("已出发：", "Departed: ") + record.id;
                });
            }
            Button(panel.transform, L10n.T("全部远征：立即到期并正常结算", "Finish and settle all expeditions"), host, report, FinishExpeditions);
            Button(panel.transform, L10n.T("重试未到账的远征奖励", "Retry pending expedition rewards"), host, report,
                () => L10n.T("完成补发：", "Rewards granted: ") + PetNestExpeditionService.TryGrantPendingRewards());
            Button(panel.transform, L10n.T("播放待揭晓的远征结果", "Reveal pending expedition results"), host, report,
                () => { close(); PetNestExpeditionRevealView.PlayPending(); return L10n.T("已请求揭晓", "Reveal requested"); });
            foreach (PetNestUIPage page in Enum.GetValues(typeof(PetNestUIPage)))
            {
                PetNestUIPage selected = page;
                string pageLabel = page == PetNestUIPage.Nest ? L10n.T("巢", "Nest")
                    : page == PetNestUIPage.Hatch ? L10n.T("孵化", "Hatch")
                    : page == PetNestUIPage.Expedition ? L10n.T("远征", "Expedition") : L10n.T("博物馆", "Museum");
                Button(panel.transform, L10n.T("打开遗种巢页面 · ", "Open Pet Nest page · ") + pageLabel, host, report,
                    () => { close(); PetNestUI.Open(selected); return L10n.T("已打开", "Opened"); });
            }
        }

        /// <summary>
        /// 让追踪器认为这一条目标已达标。与 <see cref="CompleteChapter"/> 共用，
        /// 保证「一键完成整章」和「单条完成」走的是同一套上报调用。
        /// </summary>
        private static void SatisfyObjective(CampaignObjectiveDef objective)
        {
            switch (objective.Kind)
            {
                case CampaignObjectiveKind.StandardClear: CampaignObjectiveTracker.ReportStandardClear(); break;
                case CampaignObjectiveKind.ReachWave: CampaignObjectiveTracker.ReportWaveReached(objective.Threshold); break;
                case CampaignObjectiveKind.NoDamageUntilWave: CampaignObjectiveTracker.ReportWaveReached(objective.Threshold + 1); break;
                case CampaignObjectiveKind.MeleeKills:
                case CampaignObjectiveKind.FactionBossKills:
                case CampaignObjectiveKind.BountyKills:
                    for (int n = 0; n < objective.Threshold; n++) CampaignObjectiveTracker.ReportPlayerKill(true, true, true);
                    break;
                case CampaignObjectiveKind.SurviveMinutes: CampaignObjectiveTracker.Tick(objective.Threshold * 60f); break;
                case CampaignObjectiveKind.ModeExtract: CampaignObjectiveTracker.ReportExtract(); break;
                case CampaignObjectiveKind.FinalBossKill: CampaignObjectiveTracker.ReportFinalBossKill(); break;
                default: throw new InvalidOperationException("unknown_objective: " + objective.Kind);
            }
        }

        private static string StateLabel(CampaignChapterState state)
        {
            switch (state)
            {
                case CampaignChapterState.Locked: return L10n.T("未解锁", "locked");
                case CampaignChapterState.Available: return L10n.T("可接取", "available");
                case CampaignChapterState.ContractActive: return L10n.T("进行中", "active");
                case CampaignChapterState.ReadyToDeliver: return L10n.T("待交付", "ready");
                case CampaignChapterState.Completed: return L10n.T("已交付", "delivered");
                default: return state.ToString();
            }
        }

        private static string DescribeChapter(CampaignChapterDef chapter)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("ch").Append(chapter.Order).Append(' ').Append(L10n.T(chapter.TitleCN, chapter.TitleEN))
                .Append(" [").Append(StateLabel(CampaignProgressService.GetState(chapter.ChapterId))).Append(']');
            bool armedHere = string.Equals(CampaignObjectiveTracker.ArmedChapterId, chapter.ChapterId, StringComparison.Ordinal);
            IList<CampaignObjectiveProgress> progress = armedHere ? CampaignObjectiveTracker.Progress : null;
            for (int i = 0; i < chapter.Objectives.Count; i++)
            {
                CampaignObjectiveDef objective = chapter.Objectives[i];
                text.Append(" | ").Append(L10n.T(objective.DescCN, objective.DescEN));
                if (progress != null && i < progress.Count && progress[i] != null)
                {
                    text.Append(' ').Append(progress[i].Current).Append('/').Append(objective.Threshold);
                    if (progress[i].Failed) text.Append(L10n.T("(已失败)", "(failed)"));
                }
                else
                {
                    text.Append(L10n.T("(本局未武装)", "(not armed)"));
                }
            }
            return text.ToString();
        }

        private static string AcceptContract(CampaignChapterDef chapter)
        {
            CampaignChapterState state = CampaignProgressService.GetState(chapter.ChapterId);
            if (state == CampaignChapterState.ContractActive || state == CampaignChapterState.ReadyToDeliver)
                return DescribeChapter(chapter);
            if (state != CampaignChapterState.Available)
                throw new InvalidOperationException("not_available: " + StateLabel(state)
                    + L10n.T("（先用上面的按钮完成前置章）", " (deliver its prerequisites with the buttons above first)"));
            if (!CampaignProgressService.TryAcceptContract(chapter.ChapterId))
                throw new InvalidOperationException("contract_accept_failed: " + chapter.ChapterId);
            CampaignObjectiveTracker.ResetSession();
            CampaignObjectiveTracker.EnsureArmedFor(chapter.Mode);
            return DescribeChapter(chapter);
        }

        /// <summary>只把当前第一条未达标的目标打勾，用来逐条核对目标判定与 HUD 反馈。</summary>
        private static string CompleteNextObjective(CampaignChapterDef chapter)
        {
            if (CampaignProgressService.GetState(chapter.ChapterId) != CampaignChapterState.ContractActive)
                throw new InvalidOperationException("contract_not_active: "
                    + StateLabel(CampaignProgressService.GetState(chapter.ChapterId)));
            if (!string.Equals(CampaignObjectiveTracker.ArmedChapterId, chapter.ChapterId, StringComparison.Ordinal))
            {
                CampaignObjectiveTracker.ResetSession();
                CampaignObjectiveTracker.EnsureArmedFor(chapter.Mode);
            }
            IList<CampaignObjectiveProgress> progress = CampaignObjectiveTracker.Progress;
            if (progress == null || progress.Count == 0) throw new InvalidOperationException("tracker_not_armed");
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i] == null || progress[i].Def == null) continue;
                if (progress[i].IsSatisfied) continue;
                SatisfyObjective(progress[i].Def);
                if (!progress[i].IsSatisfied)
                    throw new InvalidOperationException("objective_not_satisfied: " + progress[i].Def.Kind);
                return DescribeChapter(chapter);
            }
            return L10n.T("本委托目标已全部达标", "Every objective of this contract is already satisfied")
                + " | " + DescribeChapter(chapter);
        }

        private static string DeliverContract(CampaignChapterDef chapter)
        {
            CampaignChapterState state = CampaignProgressService.GetState(chapter.ChapterId);
            if (state == CampaignChapterState.Completed) return DescribeChapter(chapter);
            if (state != CampaignChapterState.ReadyToDeliver)
                throw new InvalidOperationException("not_ready_to_deliver: " + StateLabel(state));
            if (!CampaignProgressService.TryDeliver(chapter.ChapterId))
                throw new InvalidOperationException("delivery_failed: " + chapter.ChapterId);
            return DescribeChapter(chapter);
        }

        /// <summary>SatisfyObjective 真正处理得了的目标种类。少一条就等于那条委托永远做不完。</summary>
        private static readonly CampaignObjectiveKind[] HandledKinds =
        {
            CampaignObjectiveKind.StandardClear,
            CampaignObjectiveKind.NoDamageUntilWave,
            CampaignObjectiveKind.ReachWave,
            CampaignObjectiveKind.MeleeKills,
            CampaignObjectiveKind.FactionBossKills,
            CampaignObjectiveKind.BountyKills,
            CampaignObjectiveKind.SurviveMinutes,
            CampaignObjectiveKind.ModeExtract,
            CampaignObjectiveKind.FinalBossKill,
        };

        /// <summary>
        /// 静态自检：每章都有定义、有目标、每条目标的 Kind 都能被上报链覆盖、门槛为正。
        /// **刻意不真的驱动追踪器**——目标一旦达标就会经 NotifyObjectivesSatisfied 改章节状态，
        /// 自检不能有这种副作用。「上报链真的能推到达标」由执行回归
        /// `tests/fixtures/CampaignPlayability` 逐章跑，那里没有存档副作用。
        /// </summary>
        private static string AuditAllObjectives()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            int bad = 0;
            for (int order = 1; order <= CampaignTuning.ChapterCount; order++)
            {
                CampaignChapterDef chapter = CampaignContentCatalog.GetChapterByOrder(order);
                if (chapter == null || chapter.Objectives == null || chapter.Objectives.Count == 0)
                {
                    bad++;
                    text.Append(" | ch").Append(order).Append(L10n.T("：缺定义或没有目标", ": missing definition or objectives"));
                    continue;
                }
                for (int i = 0; i < chapter.Objectives.Count; i++)
                {
                    CampaignObjectiveDef objective = chapter.Objectives[i];
                    bool handled = false;
                    for (int k = 0; k < HandledKinds.Length; k++)
                        if (HandledKinds[k] == objective.Kind) { handled = true; break; }
                    if (handled && objective.Threshold > 0) continue;
                    bad++;
                    text.Append(" | ch").Append(order).Append('#').Append(i + 1).Append(' ')
                        .Append(objective.Kind).Append('/').Append(objective.Threshold)
                        .Append(handled ? L10n.T(" 门槛非正", " non-positive threshold")
                                        : L10n.T(" 没有上报链", " has no reporting path"));
                }
            }
            if (bad == 0)
                return L10n.T("自检通过：六章目标的种类与门槛都合法，且都有上报链；实际达标由执行回归 CampaignPlayability 覆盖",
                    "Self-check passed: every objective has a valid kind, a positive threshold and a reporting path; "
                    + "actual completion is covered by the CampaignPlayability regression");
            return L10n.T("自检发现 ", "Self-check found ") + bad + L10n.T(" 条问题", " problem(s)") + text;
        }

        private static string CompleteChapter(int order)
        {
            for (int i = 1; i <= order; i++)
            {
                CampaignChapterDef chapter = CampaignContentCatalog.GetChapterByOrder(i);
                CampaignChapterState state = CampaignProgressService.GetState(chapter.ChapterId);
                if (state == CampaignChapterState.Completed) continue;
                if (state == CampaignChapterState.Available && !CampaignProgressService.TryAcceptContract(chapter.ChapterId))
                    throw new InvalidOperationException("contract_accept_failed: " + chapter.ChapterId);
                if (CampaignProgressService.GetState(chapter.ChapterId) == CampaignChapterState.ContractActive)
                {
                    CampaignObjectiveTracker.ResetSession();
                    CampaignObjectiveTracker.EnsureArmedFor(chapter.Mode);
                    for (int j = 0; j < chapter.Objectives.Count; j++)
                    {
                        SatisfyObjective(chapter.Objectives[j]);
                    }
                }
                if (CampaignProgressService.GetState(chapter.ChapterId) != CampaignChapterState.ReadyToDeliver)
                    throw new InvalidOperationException("objective_completion_failed: " + chapter.ChapterId);
                if (i < order && !CampaignProgressService.TryDeliver(chapter.ChapterId))
                    throw new InvalidOperationException("prerequisite_delivery_failed: " + chapter.ChapterId);
            }
            CampaignObjectiveTracker.ResetSession();
            return L10n.T("所选章已就绪或已交付，请到公告板检查", "Selected chapter is ready or already delivered; inspect the board");
        }

        private static string GrantEgg(string lineageKey)
        {
            BossRushDynamicItemRegistry.EnsureRegistered(RelicEggConfig.TYPE_ID);
            if (ItemAssetsCollection.GetPrefab(RelicEggConfig.TYPE_ID) == null) throw new InvalidOperationException("egg_prefab_missing");
            Item egg = ItemAssetsCollection.InstantiateSync(RelicEggConfig.TYPE_ID);
            if (egg == null) throw new InvalidOperationException("egg_instantiate_failed");
            bool added = false;
            try
            {
                if (!RelicEggConfig.TryStampLineage(egg, lineageKey)) throw new InvalidOperationException("egg_stamp_failed");
                Inventory inventory = CharacterMainControl.Main.CharacterItem.Inventory;
                if (inventory == null || !inventory.AddAndMerge(egg, 0)) throw new InvalidOperationException("inventory_full");
                added = true;
                return L10n.T("实体蛋已进入背包", "Physical egg added to backpack");
            }
            finally { if (!added) egg.DestroyTree(); }
        }

        private static string AddExperience(string petId, int amount)
        {
            string reason;
            if (!PetNestPersistenceAccess.BeginTransaction(out reason)) throw new InvalidOperationException(reason);
            try
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                if (pet == null) throw new InvalidOperationException("pet_missing");
                PetNestProgressionService.AddExp(pet, amount);
                if (!PetNestService.Commit(out reason)) throw new InvalidOperationException(reason);
                return PetNestService.GetPetDisplayName(pet) + " Lv." + pet.level;
            }
            finally { if (PetNestPersistenceAccess.IsTransactionActive) PetNestPersistenceAccess.AbortTransaction(); }
        }

        private static string FinishExpeditions()
        {
            string reason;
            if (!PetNestPersistenceAccess.BeginTransaction(out reason)) throw new InvalidOperationException(reason);
            try
            {
                List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
                long now = DateTime.UtcNow.Ticks;
                for (int i = 0; i < records.Count; i++)
                    if (records[i] != null && !records[i].settled) records[i].returnTicks = now;
                if (!PetNestService.Commit(out reason)) throw new InvalidOperationException(reason);
            }
            finally { if (PetNestPersistenceAccess.IsTransactionActive) PetNestPersistenceAccess.AbortTransaction(); }
            return L10n.T("已正常结算：", "Settled through normal rules: ") + PetNestExpeditionService.SettleDueExpeditions();
        }

        private static void Button(Transform parent, string label, ModBehaviour host, Action<string, bool> report, Func<string> action)
        {
            GameObject child = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            child.transform.SetParent(parent, false);
            Image image = child.GetComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(image, 8);
            child.GetComponent<LayoutElement>().preferredHeight = 44;
            child.GetComponent<Button>().targetGraphic = image;
            child.GetComponent<Button>().onClick.AddListener(() => {
                string reason;
                if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }
                try { report(action(), false); }
                catch (Exception e) { report(e.Message, true); ModBehaviour.DevLog("[ManualProgression] " + e); }
            });
            TextMeshProUGUI text = Label(child.transform, label, 18, 44);
            text.color = BossRushUI.GetButtonTextColor(image.color);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8, 0);
            text.rectTransform.offsetMax = new Vector2(-8, 0);
        }

        private static TextMeshProUGUI Label(Transform parent, string value, int size, float height)
        {
            GameObject child = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            child.transform.SetParent(parent, false);
            TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.text = value;
            text.fontSize = size;
            text.color = BossRushUIColors.TextPrimary;
            text.raycastTarget = false;
            child.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }
    }
}
#endif
