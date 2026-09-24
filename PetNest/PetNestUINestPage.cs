// ============================================================================
// PetNestUINestPage.cs - 巢页的数据组装：左栏列表 + 右栏详情（2026-09-24 交互重排）
// ============================================================================
// owner：「我们现在一股脑把所有功能都做成按钮丢出来」。旧巢页每张崽卡挂「设为出战」「改名」两颗按钮、
// 正文铺五行，底部动作条再混着「不带崽出门 / 放生选中 / 批量放生」，选中是看不见的隐式状态。
// 现在照主流的「列表 + 详情」（Apple Split View、宝可梦 HOME 的盒子 + 概要）：
//   - 左栏：出战席位格 + 容量行（右上角「选择」进批量）+ 紧凑行，行上不放按钮，点一下就选中；
//   - 右栏：选中那只的全部信息，底栏只放作用在它身上的操作——危险的「放生」靠左、描边不实心，
//     「派去远征」次级，「设为出战」是这一屏唯一的主操作；出战中的崽换成「取消出战」（替代全局「不带崽出门」）；
//     远征中的崽不挂按钮，底栏写剩余时间（§4.14 不挂灰掉的占位项）；
//   - 批量放生是 iOS 照片的「选择」模式：行上出现勾选框，右栏换成「已选 N 只」摘要与放生按钮。
// 与 PetNestUIPages.cs 是同一个 partial 类，拆开只为行数预算；本文件同样**不创建 canvas、不碰 sortingOrder**。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static partial class PetNestUIPages
    {
        /// <summary>巢页：出战席位 + 崽列表 + 当前崽详情（或批量摘要）。</summary>
        internal static PetNestPageContent BuildNestPage(PetNestNestPageContext ctx)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Nest");
            PetNestNestView view = new PetNestNestView();
            page.Nest = view;

            List<PetNestPetRecord> pets = PetNestService.Pets;
            string deployedId = PetNestService.Nest.deployedPetId;
            view.Slot = BuildDeployedSlot(ctx);
            view.CapacityText = L10n.T("巢", "Nest") + " " + pets.Count + " / " + PetNestService.Capacity
                + DescribeRarityCounts(pets);

            if (pets.Count > 1 || ctx.BatchMode)
            {
                view.ModeLabel = ctx.BatchMode ? L10n.T("完成", "Done") : L10n.T("选择", "Select");
                bool next = !ctx.BatchMode;
                view.OnMode = delegate { if (ctx.SetBatchMode != null) ctx.SetBatchMode(next); };
            }

            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] != null) view.Rows.Add(BuildPetRow(pets[i], deployedId, ctx));
            }

            if (pets.Count == 0)
            {
                view.EmptyText = PetNestService.IsSaveReadOnly
                    ? L10n.T("存档读取失败，已暂停写入。", "Save could not be read; writing is paused.")
                    : L10n.T("巢是空的。", "The nest is empty.");
                view.Detail = BuildEmptyDetail();
                return page;
            }

            view.Detail = ctx.BatchMode
                ? BuildBatchDetail(ctx)
                : BuildPetDetail(PetNestService.TryGetPet(ctx.SelectedPetId), deployedId, ctx);
            return page;
        }

        /// <summary>
        /// 左栏顶部的出战席位格（像宝可梦的队伍栏）：出战崽 + 捡漏背包格数；空席时写「未带崽」。
        /// 点它 = 选中出战崽。捡漏背包的长说明挪进出战崽详情与「说明」页（交互重排 #5）。
        /// </summary>
        private static PetNestCardData BuildDeployedSlot(PetNestNestPageContext ctx)
        {
            PetNestCardData slot = new PetNestCardData();
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (deployed == null)
            {
                slot.Title = L10n.T("出战席位：空", "Deployed: none");
                slot.Subtitle = L10n.T("选一只崽「设为出战」，它会随你出击", "Deploy a cub and it joins your raids");
                return slot;
            }
            slot.Title = L10n.T("出战：", "Deployed: ") + PetNestService.GetDecoratedPetName(deployed);
            slot.Subtitle = L10n.T("出击时官方宠物背包 +", "Official pet backpack +")
                + PetNestCompanionRuntime.ResolveCapacityBonus(deployed) + L10n.T(" 格", " slots");
            slot.Icon = ResolveLineagePortrait(deployed.lineageKey);
            slot.Shiny = deployed.shiny;
            string id = deployed.id;
            if (!ctx.BatchMode)
            {
                slot.Selected = string.Equals(id, ctx.SelectedPetId, StringComparison.Ordinal);
                slot.OnCardClick = delegate
                {
                    if (ctx.Select != null) ctx.Select(id);
                    if (ctx.Refresh != null) ctx.Refresh();
                };
            }
            return slot;
        }

        /// <summary>「 · 炫彩 N · 异色 M」小计；两者都没有时不写，避免一行零。</summary>
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
            return L10n.T(" · 炫彩 " + chroma + " · 异色 " + shiny, " · Chroma " + chroma + " · Shiny " + shiny);
        }

        /// <summary>左栏一行：头像、装饰名、「血脉 · Lv · 状态」。行上不放按钮，点一下选中（批量模式下是勾选）。</summary>
        private static PetNestCardData BuildPetRow(PetNestPetRecord pet, string deployedId, PetNestNestPageContext ctx)
        {
            PetNestCardData row = new PetNestCardData();
            bool deployed = string.Equals(pet.id, deployedId, StringComparison.Ordinal);
            row.Title = PetNestService.GetDecoratedPetName(pet);
            row.Subtitle = LineageName(pet.lineageKey) + " · Lv" + pet.level + " · " + DescribeShortState(pet, deployed);
            row.Icon = ResolveLineagePortrait(pet.lineageKey);
            row.Shiny = pet.shiny;
            PetNestChromaColor colorA = PetNestChroma.Find(pet.chromaA);
            PetNestChromaColor colorB = PetNestChroma.Find(pet.chromaB);
            if (colorA != null && colorB != null)
            {
                row.ChromaHexA = colorA.TextHex;
                row.ChromaHexB = colorB.TextHex;
            }

            string petId = pet.id;
            if (ctx.BatchMode)
            {
                // 远征中的崽不能放生：行照常显示（状态写着「远征中」），不画勾选框、不可勾——不挂点不动的框
                if (pet.state == (int)PetNestPetState.OnExpedition) return row;
                row.Checkbox = true;
                row.Selected = ctx.BatchSelection != null && ctx.BatchSelection.Contains(petId);
                row.OnCardClick = delegate
                {
                    if (ctx.ToggleBatch != null) ctx.ToggleBatch(petId);
                    if (ctx.Refresh != null) ctx.Refresh();
                };
                return row;
            }

            row.Selected = string.Equals(petId, ctx.SelectedPetId, StringComparison.Ordinal);
            row.OnCardClick = delegate
            {
                if (ctx.Select != null) ctx.Select(petId);
                if (ctx.Refresh != null) ctx.Refresh();
            };
            return row;
        }

        /// <summary>行上的短状态：★ 出战 / 远征中 / 重伤 / 在巢。</summary>
        private static string DescribeShortState(PetNestPetRecord pet, bool deployed)
        {
            if (deployed) return L10n.T("★ 出战", "★ Deployed");
            switch ((PetNestPetState)pet.state)
            {
                case PetNestPetState.OnExpedition: return L10n.T("远征中", "On expedition");
                case PetNestPetState.Downed: return L10n.T("本局重伤", "Carried off");
                default: return L10n.T("在巢", "Resting");
            }
        }

        /// <summary>右栏：一只崽的详情与作用在它身上的操作。</summary>
        private static PetNestDetailData BuildPetDetail(PetNestPetRecord pet, string deployedId, PetNestNestPageContext ctx)
        {
            PetNestDetailData detail = new PetNestDetailData();
            if (pet == null)
            {
                detail.Title = L10n.T("点左边的崽查看详情", "Pick a cub on the left");
                return detail;
            }
            string petId = pet.id;
            bool deployed = string.Equals(petId, deployedId, StringComparison.Ordinal);
            bool onExpedition = pet.state == (int)PetNestPetState.OnExpedition;
            bool downed = pet.state == (int)PetNestPetState.Downed;

            detail.Icon = ResolveLineagePortrait(pet.lineageKey);
            detail.Title = PetNestService.GetDecoratedPetName(pet);
            detail.Shiny = pet.shiny;
            detail.Subtitle = LineageName(pet.lineageKey)
                + " · Lv" + pet.level
                + (PetNestProgressionService.IsAdult(pet)
                    ? " · " + T("Level_Adult")
                    : " (" + pet.exp + "/" + PetNestTuning.PetExpPerLevel + ")")
                + " · " + DescribePetState(pet, deployed);
            // 改名是低频操作：名字旁一颗小按钮，远征中的崽也允许改名
            detail.OnRename = delegate { if (ctx.Rename != null) ctx.Rename(petId); };

            string appearance = DescribeAppearance(pet);
            if (appearance != null) detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("外观", "Look"), appearance));
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("性格", "Temperament"), DescribePersonality(pet)));
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("出身", "Endowments"), DescribeTalents(pet)));
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("战痕", "Scars"), DescribeScars(pet)));
            if (deployed)
            {
                detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("捡漏背包", "Scavenger backpack"),
                    DescribeScavengerBackpack(PetNestCompanionRuntime.ResolveCapacityBonus(pet))));
            }

            if (onExpedition)
            {
                // 远征中的崽不挂出战 / 放生 / 远征按钮，底栏只写还要多久（§4.14 不挂灰按钮）
                PetNestExpeditionRecord record = FindActiveExpedition(petId);
                detail.FooterText = L10n.T("远征中，回来之前不能出战或放生", "On an expedition; can't deploy or release until it returns");
                if (record != null)
                {
                    detail.FooterText = DescribeExpeditionFooter(record);
                    // 只给还在路上的记录走表：到点后返回 null 触发一次整页刷新，刷新后不再挂走表，不会每秒重建
                    if (record.settled || PetNestExpeditionService.GetRemainingTicks(record) <= 0L) return detail;
                    PetNestExpeditionRecord live = record;
                    detail.LiveFooter = delegate
                    {
                        if (live.settled || PetNestExpeditionService.GetRemainingTicks(live) <= 0L) return null;
                        return DescribeExpeditionFooter(live);
                    };
                }
                return detail;
            }

            // 危险操作靠左、描边不实心，点了照旧走确认弹窗
            detail.Actions.Add(new PetNestActionData
            {
                // 按钮上只写素名：装饰名（渐变 / 金字）放在确认弹窗里，那里是深色面板底（2026-09-23 复核第 12 项）
                Label = L10n.T("放生", "Release"),
                IsDanger = true,
                OnClick = delegate { if (ctx.Release != null) ctx.Release(new[] { petId }); },
            });

            string reason;
            if (ctx.SendOnExpedition != null && PetNestExpeditionService.CanDepart(pet, out reason))
            {
                detail.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("派去远征", "Send on expedition"),
                    OnClick = delegate { ctx.SendOnExpedition(petId); },
                });
            }

            if (deployed)
            {
                detail.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("取消出战", "Undeploy"),
                    OnClick = delegate
                    {
                        string failure;
                        NoteFailure(PetNestService.ClearDeployedPet(out failure), failure);
                        if (ctx.Refresh != null) ctx.Refresh();
                    },
                });
            }
            else if (!downed)
            {
                detail.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("设为出战", "Deploy"),
                    IsPrimary = true,
                    OnClick = delegate
                    {
                        string failure;
                        NoteFailure(PetNestService.TrySetDeployedPet(petId, out failure), failure);
                        if (ctx.Refresh != null) ctx.Refresh();
                    },
                });
            }
            else
            {
                detail.FooterText = L10n.T("本局重伤退场，回基地后恢复", "Carried off this run; recovers back at base");
            }
            return detail;
        }

        private static PetNestExpeditionRecord FindActiveExpedition(string petId)
        {
            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && string.Equals(records[i].petId, petId, StringComparison.Ordinal)) return records[i];
            }
            return null;
        }

        private static string DescribeExpeditionFooter(PetNestExpeditionRecord record)
        {
            if (record.settled) return L10n.T("远征已归来，关掉面板就揭晓结果", "Back from the expedition; close the panel to see how it went");
            // 到点但还没结算：巢页不结算，远征页打开时会结算
            if (PetNestExpeditionService.GetRemainingTicks(record) <= 0L)
            {
                return L10n.T("已经回来了，打开「天灾远征」页结算", "Back already; open the Expedition tab to settle");
            }
            return PetNestLocalization.DescribeDestination(record.destinationId) + " · "
                + DescribeExpeditionRemaining(record);
        }

        /// <summary>
        /// 批量放生的右栏摘要：已选几只、返还多少遗魂，「勾选全部普通崽 / 全部取消」与放生按钮。
        /// 一键全选只收「普通、在巢待命」的崽：出战中的和异色 / 炫彩崽要逐只点（2026-09-23 复核第 11 项）。
        /// </summary>
        private static PetNestDetailData BuildBatchDetail(PetNestNestPageContext ctx)
        {
            List<string> picked = new List<string>();
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

            PetNestDetailData detail = new PetNestDetailData();
            int refund = PetNestTuning.ReleaseSoulRefund * picked.Count;
            detail.Title = L10n.T("已选 " + picked.Count + " 只", picked.Count + " selected");
            detail.Subtitle = L10n.T("放生返还遗魂 +" + refund, "Release returns +" + refund + " souls");
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("批量放生", "Batch release"),
                L10n.T("点左边的崽勾选或取消。远征中的崽不能放生；出战中和异色 / 炫彩的崽不参与一键勾选，要逐只勾。",
                    "Click cubs on the left to tick or untick. Cubs on an expedition can't be released; the deployed cub and shiny / chroma cubs are never ticked by \"tick all\" - tick them one by one.")));

            bool allBulkPicked = bulk.Count > 0;
            for (int i = 0; i < bulk.Count && allBulkPicked; i++)
            {
                if (ctx.BatchSelection == null || !ctx.BatchSelection.Contains(bulk[i])) allBulkPicked = false;
            }
            bool untick = picked.Count > 0 && (allBulkPicked || bulk.Count == 0);
            if ((untick || bulk.Count > 0) && ctx.ToggleBatch != null)
            {
                detail.Actions.Add(new PetNestActionData
                {
                    Label = untick ? L10n.T("全部取消勾选", "Untick all") : L10n.T("勾选全部普通崽", "Tick all common cubs"),
                    OnClick = delegate
                    {
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
            }

            // 放生是这个模式的主操作，而且后面还有一道确认：Danger 实心
            if (picked.Count > 0 && ctx.Release != null)
            {
                detail.Actions.Add(new PetNestActionData
                {
                    Label = L10n.T("放生 " + picked.Count + " 只", "Release " + picked.Count),
                    IsDanger = true,
                    IsPrimary = true,
                    OnClick = delegate { ctx.Release(picked); },
                });
            }
            return detail;
        }

        /// <summary>空巢（或读档失败）时的右栏：新玩家第一次打开面板看到的就是这里，顺手把系统讲清楚。</summary>
        private static PetNestDetailData BuildEmptyDetail()
        {
            PetNestDetailData detail = new PetNestDetailData();
            if (PetNestService.IsSaveReadOnly)
            {
                // 读档失败与「真的一只都没有」必须分开讲：此时说「巢是空的」等于告诉玩家崽全丢了
                detail.Title = L10n.T("存档读取失败", "Save could not be read");
                detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("这不是「崽丢了」", "Your cubs are not lost"),
                    L10n.T("存档读取失败，已暂停写入以保护数据。请退回主菜单重进本档，或联系作者。",
                        "Save data could not be read; writing is paused to protect it. Reload this save slot.")));
                return detail;
            }
            detail.Title = T("SystemName");
            detail.Subtitle = L10n.T("去打 Boss，把它们的遗种带回来。", "Go kill bosses and bring their relics home.");
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("这是什么", "What this is"), T("SystemDesc")));
            detail.Sections.Add(new KeyValuePair<string, string>(L10n.T("怎么开始", "How to start"),
                L10n.T("击杀 Boss 可能掉落遗种蛋，每次击杀都会攒下同血脉的遗魂。到「孵化」页把蛋孵出来，或用攒够的遗魂凝一枚。",
                    "Bosses may drop relic eggs, and every kill banks souls of that bloodline. Hatch eggs on the Hatch tab, or condense one once you have enough souls.")));
            return detail;
        }

        #region 详情文案

        /// <summary>外观：炫彩写两色色块 + 搭配名；普通崽与纯异色崽返回 null（异色已经写在装饰名里）。■ 是 GBK 收录字符。</summary>
        private static string DescribeAppearance(PetNestPetRecord pet)
        {
            PetNestChromaColor a = PetNestChroma.Find(pet.chromaA);
            PetNestChromaColor b = PetNestChroma.Find(pet.chromaB);
            if (a == null || b == null) return null;
            return L10n.T("炫彩：", "Chroma: ")
                + "<color=" + a.TextHex + ">■</color><color=" + b.TextHex + ">■</color> "
                + PetNestChroma.DescribePair(pet, L10n.IsChinese);
        }

        private static string DescribePetState(PetNestPetRecord pet, bool deployed)
        {
            if (deployed) return L10n.T("出战中", "Deployed");
            switch ((PetNestPetState)pet.state)
            {
                case PetNestPetState.OnExpedition: return L10n.T("远征中", "On expedition");
                case PetNestPetState.Downed: return L10n.T("本局重伤退场", "Carried off this run");
                default: return L10n.T("在巢待命", "Resting in the nest");
            }
        }

        /// <summary>性格名 + 效果：性格真的会改索敌、追击、跟随与属性（PetNestPersonality.cs），效果必须写出来。</summary>
        private static string DescribePersonality(PetNestPetRecord pet)
        {
            string text = PetNestLocalization.DescribePersonality(pet.personalityId);
            string effect = PetNestLocalization.DescribePersonalityEffect(pet.personalityId);
            if (!string.IsNullOrEmpty(effect)) text += " " + effect;
            return text;
        }

        private static string DescribeTalents(PetNestPetRecord pet)
        {
            if (pet.talents == null || pet.talents.Count == 0) return L10n.T("无", "None");
            string text = string.Empty;
            for (int i = 0; i < pet.talents.Count; i++)
            {
                PetNestTalentEntry t = pet.talents[i];
                if (t == null) continue;
                if (text.Length > 0) text += L10n.T("，", ", ");
                // 文案单点在 PetNestLocalization（面板、孵化揭晓、战痕说明三处共用同一口径）
                text += PetNestLocalization.DescribeTalent(t);
            }
            return text.Length > 0 ? text : L10n.T("无", "None");
        }

        /// <summary>
        /// 战痕数 + 当前生效的减益。聚合与封顶口径都在 PetNestDownedHandler，展示侧不自建一份
        /// （随从入场挂的是 GetEffectiveScarPercent，两边跑偏时面板写的就不是玩家实际承受的）。
        /// </summary>
        private static string DescribeScars(PetNestPetRecord pet)
        {
            int total = (pet.scars != null ? pet.scars.Count : 0) + pet.mergedOldScarCount;
            if (total == 0) return L10n.T("无", "None");
            string text = total.ToString();
            List<string> statKeys = new List<string>();
            PetNestDownedHandler.CollectScarStatKeys(pet, statKeys);
            string effect = null;
            for (int i = 0; i < statKeys.Count; i++)
            {
                float clamped = PetNestDownedHandler.GetEffectiveScarPercent(pet, statKeys[i]);
                if (effect != null) effect += L10n.T("，", ", ");
                effect += PetNestLocalization.DescribeStatDelta(statKeys[i], clamped, true);
            }
            return effect != null ? text + L10n.T("（" + effect + "）", " (" + effect + ")") : text;
        }

        #endregion

        #region 放生确认文案（弹窗本身是共享 BossRushConfirmDialog，由 PetNestUI.ConfirmRelease 打开）

        /// <summary>批量放生的确认页最多点名几只，其余并成「等 N 只」。</summary>
        private const int MaxReleaseListed = 6;

        /// <summary>
        /// 单只写装饰名；多只写「N 只：甲、乙、丙……等 M 只」，最多点名 MaxReleaseListed 只。
        /// 名单一律用装饰名（炫彩渐变 / 异色金字），出战中的崽后面标「（出战中）」（2026-09-23 复核第 11 项：旧写法是裸名、只列 4 只）。
        /// </summary>
        internal static string DescribeReleaseTargets(List<PetNestPetRecord> pets)
        {
            string deployedId = PetNestService.Nest.deployedPetId;
            if (pets.Count == 1) return DescribeReleaseTarget(pets[0], deployedId);
            string names = string.Empty;
            int listed = 0;
            for (int i = 0; i < pets.Count && listed < MaxReleaseListed; i++)
            {
                if (pets[i] == null) continue;
                if (listed > 0) names += L10n.T("、", ", ");
                names += DescribeReleaseTarget(pets[i], deployedId);
                listed++;
            }
            if (pets.Count > listed)
            {
                names += L10n.T("……等 " + pets.Count + " 只", " ... " + pets.Count + " in total");
            }
            return L10n.T(pets.Count + " 只：", pets.Count + " cubs: ") + names;
        }

        private static string DescribeReleaseTarget(PetNestPetRecord pet, string deployedId)
        {
            if (pet == null) return string.Empty;
            string name = PetNestService.GetDecoratedPetName(pet);
            if (string.Equals(pet.id, deployedId, StringComparison.Ordinal))
            {
                name += L10n.T("（出战中）", " (deployed)");
            }
            return name;
        }

        /// <summary>名单里的稀有崽与出战崽单独点一行；都没有时返回 null（弹窗不画这一行）。</summary>
        internal static string DescribeReleaseRareTargets(List<PetNestPetRecord> pets)
        {
            int shiny = 0, chroma = 0, deployed = 0;
            string deployedId = PetNestService.Nest.deployedPetId;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null) continue;
                if (pet.shiny) shiny++;
                if (PetNestChroma.HasChroma(pet)) chroma++;
                if (string.Equals(pet.id, deployedId, StringComparison.Ordinal)) deployed++;
            }
            if (shiny == 0 && chroma == 0 && deployed == 0) return null;
            string cn = "注意：名单里有", en = "Heads up: this includes";
            bool first = true;
            if (shiny > 0) { cn += " 异色 " + shiny + " 只"; en += " " + shiny + " shiny"; first = false; }
            if (chroma > 0) { cn += (first ? " " : "、") + "炫彩 " + chroma + " 只"; en += (first ? " " : ", ") + chroma + " chroma"; first = false; }
            if (deployed > 0) { cn += (first ? " " : "、") + "出战中 " + deployed + " 只"; en += (first ? " " : ", ") + deployed + " deployed"; }
            return L10n.T(cn, en + ".");
        }

        #endregion
    }
}
