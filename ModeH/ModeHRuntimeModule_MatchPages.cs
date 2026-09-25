// ============================================================================
// ModeHRuntimeModule_MatchPages.cs - Mode H 看盘页与赔率页的内容组装（2026-09-24 UI 共识对照审查）
// ============================================================================
// 从 ModeHRuntimeModule_MatchFlow.cs 移出（那边贴着 1200 行预算），同一个 partial 类。
// 每场先停在双方参数页，玩家选好押注并确认后才由 RunAutoAdvance 推到开打；
// 赔率/整备页保留给主动调整阵容与口令，技术重试与续赛也返回赛前确认。
//
// 本轮按 UI 制作共识重排（审查 B-03 / B-06 / B-07 / B-11 / B-19 / B-21）：
//   - 看盘页：免费侦察从底部动作条挪到页头下的一排分段按钮；底栏只留「看赔率、自己调整」（次级）与「开打」（主）；
//     计划没排好时不挂灰掉的按钮；
//   - 赔率页：下注档从底部动作条挪到动作带上方的一排分段按钮；底栏只留「调整阵容 / 配装 / 口令」与「锁定并开打」；
//     押了仓库物品时，锁盘前先弹共享确认框，写清件数、最坏损失与胜利可得；
//   - 锁盘 / 押品被拒的原因画在按钮带正上方（_pageFailureText），不再走官方全局提示。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        /// <summary>刚才那一下为什么没成：下一次建页时画在按钮带上方，画一次就清掉。纯运行时。</summary>
        private string _pageFailureText;
        private bool _showReconDetails;

        /// <summary>记下一条就地失败提示（下一次 OpenPage 取走）。</summary>
        private void NotePageFailure(string text)
        {
            _pageFailureText = text;
        }

        #region 看盘页

        private ModeHPageContent BuildBriefPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Brief");
            // 押注后果就近写在底部押注行；双方对照不重复放一条风险横幅。
            if (_season == null || _runState == null) return page;

            // matchIndex 是 1-based（0 表示尚未开赛），展示时不再 +1
            int displayIndex = _runState.MatchIndex > 0
                ? _runState.MatchIndex
                : ModeHConfig.FirstMatchIndex;
            // Label_Match 是「第 {0} 场」这样的模板，不是纯前缀：直接拼接会把 "{0}" 原样显示给玩家
            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                    .Replace("{0}", displayIndex.ToString())
                + " / " + ModeHConfig.SeasonMatchCount;

            // 恢复停在名单锁定时先打开首场赛前页；这里不直接生成双方，也不提前下注。
            if (_runState.Lifecycle == ModeHLifecycle.RosterLocked)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_StartMatch"),
                    IsPrimary = true,
                    OnClick = delegate { RunAutoAdvance("roster_start", null); },
                });
                return page;
            }

            EnsureMatchPlan();
            // 计划没排好（EnsureMatchPlan 已经请求技术重试、相位随即离开这一页）：只写原因，不挂灰掉的按钮（B-21）
            if (_season.currentMatchPlan == null)
            {
                page.Body += "\n" + L10n.T("这一场的对手还在安排，马上会重新排一次。", "This match's opponents are being set up again.");
                return page;
            }
            string prepareFailure;
            bool freshRoster = _season.matchRoster == null || _season.matchRoster.matchIndex != _runState.MatchIndex;
            if (!EnsurePreparedMatchSelection(out prepareFailure))
            {
                RequestTechnicalRetry(prepareFailure ?? "prepare_failed");
                return page;
            }
            if (freshRoster)
            {
                ApplyAutoRosterDefaults();
                if (!EnsurePreparedMatchSelection(out prepareFailure))
                {
                    RequestTechnicalRetry(prepareFailure ?? "prepare_failed");
                    return page;
                }
            }
            if (_showReconDetails)
            {
                AppendMatchPreview(page);
                AppendReconLinesAndActions(page);
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("返回双方对照", "Back to matchup"),
                    IsCancel = true,
                    OnClick = delegate { _showReconDetails = false; RouteUiForLifecycle(_runState.Lifecycle); },
                });
                return page;
            }
            AppendMatchSides(page);
            page.Headline = L10n.T("胜利返还倍率", "Win payout multiplier");
            page.HeadlineValue = FormatPayoutMultiplier(_currentOddsQuote.Odds);
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("赛况 / 侦察", "Briefing / recon"),
                OnClick = delegate { _showReconDetails = true; RouteUiForLifecycle(_runState.Lifecycle); },
            });
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_CustomSetup"),
                OnClick = delegate { EnterLoadoutEditing(); },
            });
            AppendCashBetRow(page);
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_StartMatch") + DescribeStandingBetSuffix(),
                IsPrimary = true,
                OnClick = StartMatchFromBrief,
            });
            return page;
        }

        /// <summary>
        /// 免费侦察的看盘页呈现（§17.5）。
        ///
        /// 此前 `ModeHEncounterPlanner.TryApplyRecon` 与四条 `reconChoices` 数据、
        /// `Button_Recon` / `Recon_Consumed` 文案全都写好了，但**没有任何按钮调用它**，
        /// 于是「每场一次免费侦察」这条设计在游戏里根本不存在：玩家只能盲押。
        ///
        /// 呈现口径：
        /// - 未用过：页头下一排分段按钮「免费侦察一次：[a] [b] [c]」（审查 B-07：旧版是底部动作按钮，和「开打」挤在一起）；
        /// - 已用过：只回显揭示了哪一项，不再出按钮（TryApplyRecon 自己也会以
        ///   `recon_already_consumed` 拒绝，这里是让玩家看得见，而不是靠点了才知道）。
        ///
        /// `nameKey` 在 ThreatPlans.json 里存的是**完整** key（`BossRush_ModeH_Recon_*`），
        /// 不要再拼 LocalizationKeyPrefix，否则会变成 BossRush_ModeH_BossRush_ModeH_xxx。
        /// </summary>
        private void AppendReconLinesAndActions(ModeHPageContent page)
        {
            if (page == null || _season == null) return;
            ModeHMatchPlanDto plan = _season.currentMatchPlan;
            if (plan == null) return;

            if (!string.IsNullOrEmpty(plan.reconChoiceId))
            {
                string line = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recon_Consumed");
                string revealKey = plan.publicSummary != null ? plan.publicSummary.reconRevealKey : null;
                if (!string.IsNullOrEmpty(revealKey))
                {
                    line += L10n.T("：", ": ") + L10n.T(revealKey);
                }
                // reconResult 是「成员顺序」「第二装备」两项的文本结果。
                if (!string.IsNullOrEmpty(plan.reconResult) || plan.reconChoiceId == "current_injury")
                {
                    line += "　" + DescribeReconResult(plan);
                }
                // coreTraitTags（「隐藏坏习惯」那一项）此前**全仓零消费**：写进
                // publicSummary 后再没人读，玩家消耗掉本场唯一一次侦察机会却什么都看不到。
                // 旧存档的履历侦察仅回显，新的按钮不消费纯履历信息。
                List<string> traits = plan.publicSummary != null
                    ? plan.publicSummary.coreTraitTags : null;
                if (traits != null && traits.Count > 0)
                {
                    for (int t = 0; t < traits.Count; t++)
                    {
                        if (string.IsNullOrEmpty(traits[t])) continue;
                        line += (t == 0 ? "　" : "、") + ResolveTraitDisplayName(traits[t]);
                    }
                }
                page.Lines.Add(line);
                return;
            }

            List<ModeHReconChoiceSpec> choices = ModeHContentCatalog.ReconChoices;
            if (choices == null || choices.Count == 0) return;

            ModeHOptionRow recon = new ModeHOptionRow();
            recon.Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Recon");
            recon.Caption = L10n.T("每场只能看一项，开打前有效。", "One peek per match, before the fight starts.");
            recon.AtTop = true;
            for (int i = 0; i < choices.Count; i++)
            {
                ModeHReconChoiceSpec choice = choices[i];
                if (!ModeHEncounterPlanner.IsReconChoicePlayable(choice) || string.IsNullOrEmpty(choice.ReconChoiceId)) continue;
                // 闭包不能捕获循环变量，否则按钮点下去都是最后一条（照 SelectSettlementReward 的写法）
                string selectedReconId = choice.ReconChoiceId;
                recon.Options.Add(new ModeHActionData
                {
                    Label = L10n.T(choice.NameKey),
                    OnClick = delegate { ApplyRecon(selectedReconId); },
                });
            }
            if (recon.Options.Count > 0) page.OptionRows.Add(recon);
        }

        /// <summary>
        /// 赛前双方参数卡：左侧我方先发（含本场装备），右侧敌方编制（含官方基础属性摘要）。
        /// 看盘页仍保留公开人数、进场剧本与条件行；这里把真正影响押注的参数放在同一排卡片里。
        /// </summary>
        private void AppendMatchSides(ModeHPageContent page)
        {
            if (page == null || _season == null || _season.currentMatchPlan == null) return;
            ModeHMatchRosterDto roster = _season.matchRoster;
            if (roster == null) return;

            ModeHProfileDto starter = FindSeasonProfile(roster.matchStarterProfileId);
            ModeHProfileDto relay = FindSeasonProfile(roster.matchRelayProfileId);
            if (starter != null)
            {
                ModeHCardData card = BuildProfileCard(starter, false);
                card.Stats.Clear();
                card.Equipment.Clear();
                card.Subtitle = L10n.T("首发", "Starter") + " · " + DescribeFighterState(starter);
                FillFighterDetails(card, GetPreparedFighterStats(starter, roster.starterKitIds));
                page.PlayerFighters.Add(card);
            }
            if (relay != null)
            {
                ModeHCardData card = BuildProfileCard(relay, false);
                card.Stats.Clear();
                card.Equipment.Clear();
                card.Subtitle = L10n.T("接力", "Relay") + " · " + DescribeFighterState(relay);
                FillFighterDetails(card, GetPreparedFighterStats(relay, roster.relayKitIds));
                page.PlayerFighters.Add(card);
            }
            ModeHMatchPlanDto plan = _season.currentMatchPlan;
            for (int i = 0; plan.enemyStableKeys != null && i < plan.enemyStableKeys.Count; i++)
            {
                string key = plan.enemyStableKeys[i];
                ModeHCardData card = new ModeHCardData
                {
                    Title = ResolveOfficialBossName(key),
                    Subtitle = L10n.T("敌方 " + (i + 1), "Opponent " + (i + 1)),
                    PortraitKey = key,
                };
                FillFighterDetails(card, GetPreparedEnemyStats(plan, i));
                page.EnemyFighters.Add(card);
            }
            NormalizeFighterStatScales(page.PlayerFighters, page.EnemyFighters);
        }

        private static string DescribeKitNames(List<string> kitIds)
        {
            if (kitIds == null || kitIds.Count == 0) return L10n.T("无", "None");
            List<string> names = new List<string>();
            for (int i = 0; i < kitIds.Count; i++)
            {
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitIds[i]);
                if (kit == null || kit.Spec == null) continue;
                string name = L10n.T(kit.Spec.NameKey);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names.Count > 0 ? string.Join(L10n.T("、", ", "), names.ToArray()) : L10n.T("无", "None");
        }

        private static string ResolveOfficialBossName(string stableKey)
        {
            string name = L10n.T(stableKey);
            if (!string.IsNullOrEmpty(name) && name[0] != '*') return name;
            return string.IsNullOrEmpty(stableKey) ? L10n.T("未知敌人", "Unknown enemy") : stableKey;
        }

        #endregion

        #region 赔率页

        private ModeHPageContent BuildOddsPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Odds");

            // 押品选择器可用性是 §22.1 的**只读派生结果**，不是开关
            // （ModeHConfigApiGuard 禁止任何 RealWarehouseStake 开关符号）。
            // 证据不足时禁用并原位说明原因，赛季照常用虚拟筹码跑完整闭环。
            page.RealStakeSelectorEnabled = ModeHWarehouseStakeJournal.IsSlotConsistent;
            // 风险提示只在真的能押的时候出：功能不可用还挂着「失败永久没收」
            // 会让玩家以为自己的存档坏了，而不是"这个功能现在用不了"。
            page.ShowRealStakeNotice = page.RealStakeSelectorEnabled;
            if (!page.RealStakeSelectorEnabled)
            {
                page.RealStakeDisabledReason = ResolveRealStakeDisabledReason();
            }

            if (_season == null || _runState == null) return page;

            string prepareFailure;
            if (!EnsurePreparedMatchSelection(out prepareFailure))
            {
                // 这条分支此前直接 return，page.Actions 为空——而 CreateActions 遇到
                // 零按钮直接 return，一个控件都不画；赔率页却已经 ClaimModalInput
                // （timeScale=0 + 禁输入）且没有 ESC 处理，玩家就被困在时停页上。
                // 整备拿不出阵容属技术故障，与 EnsureMatchPlan 同口径消耗一次重试预算，
                // 由状态机把玩家路由到恢复壳（那里有可点的动作），而不是留在死页上。
                page.Body = L10n.T("本场整备不可用：", "Match setup unavailable: ")
                    + L10n.T(ModeHAvailability.GetReasonLocalizationKey(prepareFailure));
                RequestTechnicalRetry(prepareFailure != null ? prepareFailure : "prepare_failed");
                return page;
            }

            if (_showLoadoutEditor) return BuildLoadoutEditorPage();

            // 大字只放锁定赔率；双方公开分与筹码余额降到一行次级小字（UB-34）。当前下注看下面那排分段按钮
            page.Headline = L10n.T("锁定赔率", "Locked odds");
            page.HeadlineValue = FormatPayoutMultiplier(_currentOddsQuote.Odds);
            page.Body = L10n.T("我方公开分 ", "Player public score ") + _currentOddsQuote.PlayerPublicScore
                + L10n.T("　敌方公开分 ", "  Enemy public score ") + _currentOddsQuote.EnemyPublicScore
                + L10n.T("　筹码余额 ", "  Credits ") + _season.virtualStakeCredits;

            if (_currentOddsQuote.Breakdown != null)
            {
                for (int i = 0; i < _currentOddsQuote.Breakdown.Count; i++)
                {
                    ModeHOddsBreakdownEntry entry = _currentOddsQuote.Breakdown[i];
                    if (entry == null) continue;
                    // LabelKey 在 ModeHOddsController.Add 里已经拼过 LocalizationKeyPrefix，存的是**完整** key；
                    // 这里再拼一次会变成 BossRush_ModeH_BossRush_ModeH_Odds_xxx，18 条分量标签全显示星号 raw key。
                    page.Lines.Add(L10n.T(entry.LabelKey)
                        + "  " + (entry.Value >= 0 ? "+" : string.Empty) + entry.Value);
                }
            }

            // 押注：动作带上方一排分段按钮（审查 B-06 / B-19：旧版「下注 0 / 1 / 2」是底部动作按钮，和锁盘挤在一起）。
            // 2026-09-24 起押的是钱（ModeHCashBetService），与选人页、结算页同一排、同一个押注档
            AppendMatchSides(page);
            AppendCashBetRow(page);
            if (_currentOddsQuote != null && ModeHCashBetService.StandingAmount > 0)
            {
                page.Lines.Insert(0, L10n.T("这一场押 ", "This match: bet ") + FormatMoney(ModeHCashBetService.StandingAmount)
                    + L10n.T("，赢了拿回 ", "; a win pays ")
                    + FormatMoney(ModeHCashBetService.ComputePayout(ModeHCashBetService.StandingAmount, _currentOddsQuote.Odds)));
            }

            AppendRealStakeLinesAndActions(page);

            ModeHMatchRosterDto editOwner = _season.matchRoster;
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("调整阵容 / 配装 / 口令", "Edit roster / kits / command"),
                OnClick = delegate
                {
                    if (!CanEditLoadout(editOwner)) return;
                    _showLoadoutEditor = true;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });

            // 锁盘前提（计划、阵容、报价）不齐时不挂灰按钮（B-21）：上面 EnsurePreparedMatchSelection 已经兜住，这里只防御
            if (_season.currentMatchPlan != null && _season.matchRoster != null && _currentOddsQuote != null)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_LockIn") + DescribeStandingBetSuffix(),
                    IsPrimary = true,
                    OnClick = ConfirmRealStakeThenLock,
                });
            }
            return page;
        }

        /// <summary>
        /// 押了仓库物品时，锁盘前先确认（审查 B-03，UI 制作共识第 4 节）：写清押上哪几件、输了全部没收、赢了拿什么。
        /// 没押时直接锁盘。确认框走共享 BossRushConfirmDialog；锁盘本身仍由 LockLoadoutAndStartMatch 校验相位。
        /// </summary>
        private void ConfirmRealStakeThenLock()
        {
            if (!ModeHRealStakeService.HasSelection)
            {
                LockLoadoutAndStartMatch();
                return;
            }
            List<int> picked = ModeHRealStakeService.GetSelectedPositions();
            List<string> names = new List<string>(picked.Count);
            for (int i = 0; i < picked.Count; i++) names.Add(ModeHRealStakeService.DescribePosition(picked[i]));
            int reward = _currentOddsQuote != null ? ModeHRealStakeService.PreviewRewardCount(_currentOddsQuote.Odds) : 0;
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("押上 " + picked.Count + " 件仓库物品开打？", "Stake " + picked.Count + " storage item(s) and start?"),
                Target = string.Join(L10n.T("、", ", "), names.ToArray()),
                Warning = L10n.T("输了这 " + picked.Count + " 件永久没收；赢了原样返还，再给 " + reward + " 件同品质的新物品。",
                    "Lose: all " + picked.Count + " are gone for good. Win: they come back, plus " + reward + " new item(s) of the same quality."),
                ConfirmLabel = L10n.T("押上并开打", "Stake and start"),
                Danger = true,
                OnConfirm = LockLoadoutAndStartMatch,
            });
        }

        #endregion
    }
}
