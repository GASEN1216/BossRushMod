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
        /// <summary>该卡是否处于选中态（远征目标）。</summary>
        public bool Selected;
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

        /// <summary>清空跨面板残留的失败提示。面板关闭 / 过图 / 切档都要调。</summary>
        internal static void ClearLastFailureText()
        {
            LastFailureText = null;
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
        }

        private static string T(string suffix)
        {
            return LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + suffix);
        }

        #region 巢

        /// <summary>巢页：崽列表 + 出战席位 + 遗魂账本摘要。</summary>
        internal static PetNestPageContent BuildNestPage(
            Action refresh, string selectedPetId, Action<string> select, Action<string> rename,
            Action<string> release)
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Nest");

            List<PetNestPetRecord> pets = PetNestService.Pets;
            page.Body = L10n.T("巢容量", "Nest capacity") + " " + pets.Count + " / " + PetNestService.Capacity;

            string deployedId = PetNestService.Nest.deployedPetId;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null) continue;
                page.Cards.Add(BuildPetCard(pet, deployedId, selectedPetId, refresh, select, rename));
            }

            if (pets.Count == 0)
            {
                page.Lines.Add(L10n.T("巢是空的。去打 Boss，把它们的遗种带回来。",
                    "The nest is empty. Go kill bosses and bring their relics home."));
                // 空巢是新玩家第一次看到这个面板的地方，顺手把系统本身讲清楚
                page.Lines.Add(T("SystemDesc"));
            }
            else
            {
                PetNestPetRecord selected = PetNestService.TryGetPet(selectedPetId);
                page.Lines.Add(selected != null
                    ? L10n.T("远征目标：", "Expedition target: ") + PetNestService.GetPetDisplayName(selected)
                    : L10n.T("点「设为出战」或「改名」都会顺手把这只崽选为远征目标。",
                        "Deploying or renaming a cub also picks it as the expedition target."));
            }

            // 巢满时的扩建提示：容量由图鉴解锁数派生，数字取自 Tuning 避免两套真相
            int[] milestones = PetNestTuning.NestCapacityMilestoneLineageCounts;
            if (milestones != null && milestones.Length > 0
                && PetNestService.Capacity < PetNestTuning.MaxNestCapacity)
            {
                page.Lines.Add(T("CapacityMilestoneHint") + " ("
                    + PetNestMuseumStats.UnlockedLineageCount + " / "
                    + ResolveNextCapacityMilestone(milestones) + ")");
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

            // 放生：巢满时唯一可预期的腾位手段（此前只能押 22% 死亡率的亡命远征等崽死）。
            // 不可逆，因此走确认弹窗；远征锁定期间禁用（服务层也会再拒一次）。
            PetNestPetRecord releaseTarget = PetNestService.TryGetPet(selectedPetId);
            page.Actions.Add(new PetNestActionData
            {
                Label = T("Release_Action") + (releaseTarget != null
                    ? " · " + PetNestService.GetPetDisplayName(releaseTarget)
                    : string.Empty),
                IsDanger = true,
                Interactable = release != null && releaseTarget != null
                    && releaseTarget.state != (int)PetNestPetState.OnExpedition,
                OnClick = delegate
                {
                    if (release != null) release(selectedPetId);
                },
            });
            return page;
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

        private static PetNestCardData BuildPetCard(
            PetNestPetRecord pet, string deployedId, string selectedPetId,
            Action refresh, Action<string> select, Action<string> rename)
        {
            PetNestCardData card = new PetNestCardData();
            card.Selected = string.Equals(pet.id, selectedPetId, StringComparison.Ordinal);
            card.Title = PetNestService.GetPetDisplayName(pet);
            card.Shiny = pet.shiny;

            PetNestLineageInfo lineage;
            string lineageName = PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) && lineage != null
                ? lineage.DisplayName
                : pet.lineageKey;
            card.Subtitle = lineageName
                + " · Lv" + pet.level
                + (PetNestProgressionService.IsAdult(pet)
                    ? " · " + T("Level_Adult")
                    : " (" + pet.exp + "/" + PetNestTuning.PetExpPerLevel + ")")
                + " · " + T("Personality_" + (pet.personalityId ?? string.Empty));

            card.Body = DescribePetState(pet)
                + "\n" + DescribeTalents(pet)
                + "\n" + DescribeScars(pet);

            bool deployed = string.Equals(pet.id, deployedId, StringComparison.Ordinal);
            bool selectable = pet.state != (int)PetNestPetState.OnExpedition
                && pet.state != (int)PetNestPetState.Downed;

            string cardPetId = pet.id;

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
                    if (select != null) select(cardPetId);
                    if (refresh != null) refresh();
                };
            }

            // 次动作：远征中的崽也允许改名，只有它不能被选为新的远征目标
            card.SecondaryLabel = L10n.T("改名", "Rename");
            card.OnSecondary = delegate
            {
                if (select != null && selectable) select(cardPetId);
                if (rename != null) rename(cardPetId);
            };
            return card;
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

        private static string DescribeTalents(PetNestPetRecord pet)
        {
            if (pet.talents == null || pet.talents.Count == 0)
            {
                return L10n.T("出身：无", "Endowments: none");
            }
            string text = L10n.T("出身：", "Endowments: ");
            for (int i = 0; i < pet.talents.Count; i++)
            {
                PetNestTalentEntry t = pet.talents[i];
                if (t == null) continue;
                if (i > 0) text += "，";
                text += t.statKey + FormatModifierValue(t.value, t.percentage);
            }
            return text;
        }

        /// <summary>
        /// 把 Modifier 数值格式化成玩家看得懂的文本。
        /// 百分比项内部存的是**小数**（0.08 = +8%，官方 PercentageAdd 口径），
        /// 展示时要 ×100，否则玩家看到的是 "+0.08%"。官方 EndowmentEntry 同款换算。
        /// </summary>
        /// <summary>
        /// 天赋/战痕数值的统一格式化。百分比项在数据层存的是小数（0.08 = 8%），
        /// 直接拼 "%" 会显示成 "+0.08%"，因此展示侧一律走这里。
        /// internal：孵化揭晓演出也要用同一口径。
        /// </summary>
        internal static string FormatModifierValue(float value, bool percentage)
        {
            if (!percentage)
            {
                return (value >= 0f ? "+" : "") + ((int)Math.Round(value)).ToString();
            }
            float percent = value * 100f;
            return (percent >= 0f ? "+" : "") + percent.ToString("0.#") + "%";
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

        private static string DescribeScarEffect(PetNestPetRecord pet)
        {
            if (pet.scars == null || pet.scars.Count == 0) return null;
            Dictionary<string, float> byStat = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < pet.scars.Count; i++)
            {
                PetNestScarRecord s = pet.scars[i];
                if (s == null || string.IsNullOrEmpty(s.statKey)) continue;
                float sum;
                byStat.TryGetValue(s.statKey, out sum);
                byStat[s.statKey] = sum + s.percent;
            }
            if (byStat.Count == 0) return null;

            string text = null;
            foreach (KeyValuePair<string, float> pair in byStat)
            {
                float clamped = Math.Max(pair.Value, PetNestTuning.ScarModifierCapFraction);
                if (text != null) text += "，";
                text += pair.Key + FormatModifierValue(clamped, true);
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

            for (int i = 0; i < eggs.Count; i++)
            {
                ItemStatsSystem.Item egg = eggs[i];
                string lineageKey = RelicEggConfig.ReadLineage(egg);
                PetNestLineageInfo lineage;
                bool known = PetNestLineageCatalog.TryGet(lineageKey, out lineage) && lineage != null;

                PetNestCardData card = new PetNestCardData();
                card.Title = known ? lineage.DisplayName : T("Fail_lineage_unknown");
                card.Subtitle = RelicEggConfig.GetDisplayName();
                card.Body = known
                    ? L10n.T("孵化后锁定出身、性格与异色。", "Hatching locks endowments, temperament and shiny.")
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
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            bool ledgerHeaderWritten = false;
            for (int i = 0; i < lineages.Count; i++)
            {
                PetNestLineageInfo lineage = lineages[i];
                if (lineage == null) continue;
                int souls = PetNestService.GetSouls(lineage.LineageKey);
                if (souls <= 0) continue;

                if (!ledgerHeaderWritten)
                {
                    // 区头讲清「遗魂是什么、攒够能干嘛」，逐行只留进度
                    page.Lines.Add(T("SoulLedger"));
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

            page.Body = L10n.T("待派遣：", "Ready to depart: ") + PetNestService.GetPetDisplayName(pet);
            AppendDepartActions(page, pet, refresh);
            return page;
        }

        private static PetNestCardData BuildExpeditionCard(PetNestExpeditionRecord r)
        {
            PetNestCardData card = new PetNestCardData();
            card.Title = PetNestExpeditionService.DescribePetName(r);
            card.Subtitle = T("Dest_" + r.destinationId) + " · " + DescribeRisk(r.riskTier);
            card.IsDanger = r.riskTier == (int)PetNestRiskTier.Desperate;

            if (r.settled)
            {
                card.Body = L10n.T("已结算，等待翻牌。", "Settled. Waiting to be revealed.");
            }
            else
            {
                long remaining = PetNestExpeditionService.GetRemainingTicks(r);
                TimeSpan span = TimeSpan.FromTicks(remaining);
                card.Body = L10n.T("剩余", "Remaining") + " "
                    + ((int)span.TotalHours) + "h" + span.Minutes + "m"
                    + "\n" + T("DeathRateLabel") + " " + FormatPercent(r.deathRate);
            }
            return card;
        }

        private static void AppendDepartActions(PetNestPageContent page, PetNestPetRecord pet, Action refresh)
        {
            PetNestDestinationInfo[] destinations = PetNestExpeditionService.Destinations;
            for (int d = 0; d < destinations.Length; d++)
            {
                PetNestDestinationInfo destination = destinations[d];
                bool affinity = PetNestExpeditionService.HasElementAffinity(pet, destination.Id);

                for (int t = 0; t <= (int)PetNestRiskTier.Desperate; t++)
                {
                    PetNestRiskTier tier = (PetNestRiskTier)t;
                    float deathRate = PetNestExpeditionService.GetDeathRate(tier);
                    string petId = pet.id;
                    string destinationId = destination.Id;

                    page.Actions.Add(new PetNestActionData
                    {
                        // 死亡率写在按钮上：赌的知情权是底线
                        Label = T("Dest_" + destination.Id) + " · " + DescribeRisk(t)
                            + " · " + T("DeathRateLabel") + " " + FormatPercent(deathRate)
                            + (affinity ? " · " + T("ElementAffinity") : string.Empty),
                        IsDanger = tier == PetNestRiskTier.Desperate,
                        OnClick = delegate
                        {
                            PetNestExpeditionRecord record;
                            string reason;
                            NoteFailure(
                                PetNestExpeditionService.TryDepart(petId, destinationId, tier, out record, out reason),
                                reason);
                            if (refresh != null) refresh();
                        },
                    });
                }
            }
        }

        private static string DescribeRisk(int riskTier)
        {
            switch ((PetNestRiskTier)riskTier)
            {
                case PetNestRiskTier.Rough: return T("Risk_rough");
                case PetNestRiskTier.Desperate: return T("Risk_desperate");
                default: return T("Risk_safe");
            }
        }

        private static string FormatPercent(float rate)
        {
            return ((int)UnityEngine.Mathf.Round(rate * 100f)) + "%";
        }

        #endregion

        #region 博物馆

        /// <summary>博物馆页：血脉图鉴 + 阵亡纪念碑。</summary>
        internal static PetNestPageContent BuildMuseumPage()
        {
            PetNestPageContent page = new PetNestPageContent();
            page.Title = T("Page_Museum");

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

            PetNestCardData card = new PetNestCardData();
            card.Title = name;
            card.Shiny = stats.shinyHatched > 0;
            card.Subtitle = stats.unlocked
                ? L10n.T("已解锁", "Unlocked")
                : L10n.T("未解锁", "Locked");
            card.Body = L10n.T("击杀", "Kills") + " " + stats.kills
                + " · " + L10n.T("孵化", "Hatched") + " " + stats.hatched
                + " · " + L10n.T("异色", "Shiny") + " " + stats.shinyHatched
                + " · " + L10n.T("最高等级", "Max level") + " " + stats.maxLevel
                + " · " + L10n.T("远征", "Expeditions") + " " + stats.expeditions;
            return card;
        }

        private static void AppendMemorialCards(PetNestPageContent page, PetNestMuseumData museum)
        {
            if (museum.memorials.Count == 0 && museum.mergedMemorialCount == 0) return;

            page.Lines.Add(T("Page_Memorial"));
            for (int i = museum.memorials.Count - 1; i >= 0; i--)
            {
                PetNestMemorialEntry m = museum.memorials[i];
                if (m == null) continue;
                PetNestLineageInfo lineage;
                string lineageName = PetNestLineageCatalog.TryGet(m.lineageKey, out lineage) && lineage != null
                    ? lineage.DisplayName
                    : m.lineageKey;

                // 碑文一定要刻风险档位：那是玩家自己按下的选择
                page.Lines.Add(m.displayName
                    + " · " + lineageName
                    + " · " + T("Dest_" + m.destinationId)
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
