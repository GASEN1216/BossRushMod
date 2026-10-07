// ============================================================================
// ModeHRuntimeModule_BetFlow.cs - 鸭王杯押注的流程接线（2026-09-24 owner 拍板）
// ============================================================================
// 账本与赔付口径在 ModeHCashBetService.cs，押背包/穿戴物品的物品侧在 ModeHItemBetStake.cs；
// 这里只把它们接进鸭王杯的页面与状态机：
//   - 赛前双方对照页与赔率页下方一排「押注」：不押 / 快捷金额 / 自选金额 / 押物品；
//     押钱选的是「接下来每场押多少」，默认不押，读档回到不押；押物品可选背包、容器和穿戴物品，只管下一场；
//   - 自选金额按当前钱包与返还倍率设上限；锁盘复验后只扣本金，资金或押品不可用时记录原因，比赛照打；
//   - 本场分出胜负时按赔率结算：押物品赢了东西留着、另发奖品（品质跟押上的东西走、总价值跟估值和赔率走），
//     输了收走押上的东西；
//   - **押注跟着这一场走**：技术中止、挂起、退游戏重进都不退，重打这一场时沿用、按重打的结果结算
//     （旧版一中断就整额退回，打输了强退重进等于重掷）。只有这一季不再打了才原样退回：恢复页放弃赛季、
//     开新赛季时发现上一季挂着的押注、F3 验收清理。已确定输赢的实物结算继续履约，不按中止退款。
// 同一个 partial 类，拆开只为单文件行数预算。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        /// <summary>这一场为什么没押成（钱不够、东西不在、上一笔没结清），结算页写出来。纯运行时。</summary>
        private string _cashBetSkipNote;
        private int _cashBetSkipMatchIndex = -1;
        /// <summary>押物品选择页开着（盖在赛前对照 / 赔率页上，「完成」回原页）。</summary>
        private bool _showItemBetPicker;

        #region 页面：押注行

        /// <summary>
        /// 页脚「押注」一排（钱包不在时不挂，§4.14）：押钱快捷档 + 自选金额 + 押物品。资金不可用时锁盘再说明。
        /// 这一场已经押上（中断后重打）时说明写「沿用」，下面选的只管之后几场。
        /// </summary>
        private void AppendCashBetRow(ModeHPageContent page)
        {
            if (page == null || !ModeHCashBetService.IsAvailable) return;
            long[] amounts = ModeHConfig.CashBetAmounts;
            bool itemsMode = ModeHItemBetStake.HasSelection;
            ModeHCashBetRecord carried = CarriedBetForCurrentMatch();
            ModeHOptionRow row = new ModeHOptionRow();
            row.Label = L10n.T("押注", "Bet");
            // 2026-09-29 owner：押注行下不再放输赢说明 / 免责式文字，只留做决定要用的数（已押多少、估值、余额）
            if (carried != null)
            {
                row.Caption = L10n.T("本场已押 ", "Already bet this match: ") + DescribeRecordStake(carried)
                    + L10n.T(" · 下面选的从下一场起", " · the choice below starts next match");
            }
            else if (itemsMode)
            {
                row.Caption = L10n.T("押上 " + ModeHItemBetStake.SelectedCount + " 件物品 · 估值 ", ModeHItemBetStake.SelectedCount + " item(s) · worth ")
                    + FormatMoney(ModeHItemBetStake.SelectedValue);
            }
            else
            {
                row.Caption = L10n.T("余额 ", "Balance ") + FormatMoney(Duckov.Economy.EconomyManager.Money);
            }
            for (int i = 0; i < amounts.Length; i++)
            {
                int tier = i;
                row.Options.Add(new ModeHActionData
                {
                    Label = amounts[i] <= 0 ? L10n.T("不押", "No bet") : L10n.T("押 ", "Bet ") + FormatMoney(amounts[i]),
                    IsSelected = !itemsMode && ModeHCashBetService.StandingTier == tier,
                    OnClick = delegate { SelectStandingBet(tier); },
                });
            }
            row.Options.Add(new ModeHActionData
            {
                Label = L10n.T("自选金额", "Custom amount"),
                IsSelected = !itemsMode && ModeHCashBetService.IsCustomStandingBet,
                OnClick = delegate
                {
                    if (_commandsClosed || _runState == null) return;
                    ModeHCashBetService.SelectCustomStandingAmount(ModeHCashBetService.StandingAmount, ResolveStandingCashOdds());
                    ModeHItemBetStake.ClearSelection();
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
            if (!itemsMode && ModeHCashBetService.IsCustomStandingBet)
            {
                int odds = ResolveStandingCashOdds();
                ModeHCashBetService.SelectCustomStandingAmount(ModeHCashBetService.StandingAmount, odds);
                long maximum = ModeHCashBetService.GetMaximumStandingAmount(ModeHCashBetService.MaximumStandingAmount, odds);
                row.SliderMaximum = ModeHCashBetService.CustomSliderSteps;
                row.SliderValue = ModeHCashBetService.ProgressForAmount(maximum, ModeHCashBetService.StandingAmount);
                row.OnSliderChanged = delegate(int value)
                {
                    if (_commandsClosed || _runState == null) return;
                    int currentOdds = ResolveStandingCashOdds();
                    long limit = ModeHCashBetService.GetMaximumStandingAmount(ModeHCashBetService.MaximumStandingAmount, currentOdds);
                    ModeHCashBetService.SelectCustomStandingAmount(ModeHCashBetService.AmountAtProgress(limit, value), currentOdds);
                };
                row.SliderCaption = DescribeCustomStandingBet;
            }
            row.Options.Add(new ModeHActionData
            {
                Label = itemsMode
                    ? L10n.T("押物品 · " + ModeHItemBetStake.SelectedCount + " 件", "Items · " + ModeHItemBetStake.SelectedCount)
                    : L10n.T("押物品", "Bet items"),
                IsSelected = itemsMode,
                OnClick = OpenItemBetPicker,
            });
            page.OptionRows.Add(row);
        }

        private string DescribeCustomStandingBet()
        {
            long amount = ModeHCashBetService.StandingAmount;
            int odds = ResolveStandingCashOdds();
            long maximum = ModeHCashBetService.GetMaximumStandingAmount(ModeHCashBetService.MaximumStandingAmount, odds);
            long payout = ModeHCashBetService.ComputePayout(amount, odds);
            return L10n.T("投入 ", "Stake ") + FormatMoney(amount)
                + L10n.T(" / 上限 ", " / Max ") + FormatMoney(maximum)
                + L10n.T(" · 获胜返还 ", " · Return on win ") + FormatMoney(payout)
                + L10n.T(" · 净收益 ", " · Net ") + FormatMoney(payout - amount);
        }

        private int ResolveStandingCashOdds()
        {
            return GroupModeEnabled ? ResolveGroupOdds(_groupBetOnRed)
                : _currentOddsQuote != null ? _currentOddsQuote.Odds : ModeHConfig.MinOdds;
        }

        private void SelectStandingBet(int tier)
        {
            if (_commandsClosed || _runState == null) return;
            ModeHCashBetService.StandingTier = tier;
            ModeHItemBetStake.ClearSelection();
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        /// <summary>「 · 押 5,000」「 · 押 3 件物品」「 · 已押 5,000」（不押时为空），接在「下一场」「开打」按钮上。</summary>
        private string DescribeStandingBetSuffix()
        {
            if (!ModeHCashBetService.IsAvailable) return string.Empty;
            ModeHCashBetRecord carried = CarriedBetForCurrentMatch();
            if (carried != null) return L10n.T(" · 已押 ", " · Bet placed: ") + DescribeRecordStake(carried);
            if (ModeHItemBetStake.HasSelection)
            {
                return L10n.T(" · 押 " + ModeHItemBetStake.SelectedCount + " 件物品", " · Bet " + ModeHItemBetStake.SelectedCount + " item(s)");
            }
            // 自选滑条只更新本行，按钮不保留拖动前的旧金额；精确金额显示在滑条读数。
            if (ModeHCashBetService.IsCustomStandingBet) return L10n.T(" · 自选金额", " · Custom stake");
            long amount = ModeHCashBetService.StandingAmount;
            if (amount <= 0) return string.Empty;
            return L10n.T(" · 押 ", " · Bet ") + FormatMoney(amount);
        }

        internal static string FormatMoney(long amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        internal static string FormatSignedMoney(long amount)
        {
            return (amount >= 0 ? "+" : string.Empty) + FormatMoney(amount);
        }

        internal static string FormatPayoutMultiplier(int odds)
        {
            return "x" + ((1000m - ModeHConfig.CashBetHouseCutPermille) / ModeHCashBetService.ResolveAssumedWinPermille(odds))
                .ToString("0.00", CultureInfo.InvariantCulture);
        }

        internal static string FormatPayoutMultiplier(ModeHCashBetRecord record)
        {
            return record != null && record.payoutNumerator > 0 && record.payoutDenominator > 0
                ? "x" + ((decimal)record.payoutNumerator / record.payoutDenominator).ToString("0.00", CultureInfo.InvariantCulture)
                : FormatPayoutMultiplier(record != null ? record.odds : ModeHConfig.MinOdds);
        }

        /// <summary>「5,000」或「3 件物品（估值 12,345）」。</summary>
        private static string DescribeRecordStake(ModeHCashBetRecord record)
        {
            if (record.kind != ModeHCashBetService.KindItems) return FormatMoney(record.amount);
            int count = ModeHItemBetEntry.Decode(record.items).Count;
            return L10n.T(count + " 件物品（估值 " + FormatMoney(record.amount) + "）",
                count + " item(s) (worth " + FormatMoney(record.amount) + ")");
        }

        /// <summary>这一季这一场已经押上、还没结清的那一笔（中断后重打时沿用）。</summary>
        private ModeHCashBetRecord CarriedBetForCurrentMatch()
        {
            if (_runState == null) return null;
            return ModeHCashBetService.ReservedFor(_runState.RunId, _runState.MatchIndex);
        }

        /// <summary>结算页的一行：押了多少、赢了拿回多少 / 输了；没押成时写原因。</summary>
        private void AppendCashBetReportLine(ModeHPageContent page, ModeHMatchReportDto report)
        {
            if (page == null || report == null || _runState == null) return;
            if (_cashBetSkipMatchIndex == report.matchIndex && !string.IsNullOrEmpty(_cashBetSkipNote))
            {
                page.Lines.Add(L10n.T("押注：", "Bet: ") + _cashBetSkipNote);
                return;
            }
            ModeHCashBetRecord record = ModeHCashBetService.Current;
            if (record != null && record.kind == ModeHCashBetService.KindItems
                && record.status == ModeHCashBetService.StatusReserved && record.itemSettlement != 0
                && record.matchIndex == report.matchIndex && record.runId == _runState.RunId)
            {
                page.Lines.Add(record.itemSettlement == 1
                    ? L10n.T("押注已赢，奖品正在补发；请留出背包空位，结算记录会保留到全部到账。",
                        "Bet won. Prizes are pending; make room in your backpack. The bet stays open until delivery completes.")
                    : L10n.T("押注结算正在等待保存，稍后会自动重试。", "Your bet settlement is waiting to be saved and will retry automatically."));
                return;
            }
            if (record == null || record.status != ModeHCashBetService.StatusSettled
                || record.matchIndex != report.matchIndex
                || !string.Equals(record.runId, _runState.RunId, StringComparison.Ordinal)) return;
            string head = L10n.T("押注：", "Bet: ") + DescribeRecordStake(record) + L10n.T("　", "  ");
            if (record.kind == ModeHCashBetService.KindItems)
            {
                if (ModeHCashBetService.IsWinningRecord(record)) AppendPrizeIcons(page, record);
                page.Lines.Add(head + (ModeHCashBetService.IsWinningRecord(record)
                    ? L10n.T("押品保留", "stake kept")
                        + (record.prizeCash > 0 ? L10n.T("，零头折成 ", "; remainder paid as ") + FormatMoney(record.prizeCash) : string.Empty)
                    : L10n.T("输了，押上的东西归庄家", "lost: the house takes the items")
                        + (record.charged > 0
                            ? L10n.T("；有的找不到了，按估值从余额扣了 " + FormatMoney(record.charged),
                                "; some were missing, so " + FormatMoney(record.charged) + " was taken from your balance")
                            : string.Empty)));
                return;
            }
            page.Lines.Add(head + (ModeHCashBetService.IsWinningRecord(record)
                ? L10n.T("赢了，拿回 ", "won, paid ") + FormatMoney(record.payout)
                : L10n.T("输了，押金归庄家", "lost, the house keeps it")));
        }

        #endregion

        #region 页面：押物品选择页

        /// <summary>从押注行点「押物品」：盖一页选背包里的东西，「完成」回原页。</summary>
        private void OpenItemBetPicker()
        {
            if (_commandsClosed || _runState == null) return;
            _showItemBetPicker = true;
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        /// <summary>押物品选择页能盖在哪些相位上（就是挂押注行的那几页）。</summary>
        private static bool IsItemBetPickerHost(ModeHLifecycle lifecycle)
        {
            // 群战第 1 场的赛前页停在 Drafting，押注行也在这一页
            if (lifecycle == ModeHLifecycle.Drafting) return GroupModeEnabled;
            switch (lifecycle)
            {
                case ModeHLifecycle.RosterLocked:
                case ModeHLifecycle.MatchBrief:
                case ModeHLifecycle.LoadoutEditing:
                case ModeHLifecycle.OddsPreview:
                case ModeHLifecycle.MatchSettling:
                case ModeHLifecycle.Intermission:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 押物品选择页（UI 制作共识：卡片栅格，一张卡一件东西，整张可点；选中是金边 + 「√ 已押上」）。
        /// 只列能押的：能卖的、不是装着东西的容器、单件估值不超过上限；按估值从高到低。
        /// </summary>
        private ModeHPageContent BuildItemBetPickerPage()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T("选择押注物品", "Choose your stake");
            List<ModeHItemBetCandidate> candidates = ModeHItemBetStake.ListCandidates();
            int selectedCount = ModeHItemBetStake.SelectedCount;
            if (candidates.Count == 0)
            {
                page.Body = L10n.T("身上暂时没有物品。", "You are not carrying any items.");
            }
            else if (selectedCount > 0)
            {
                // 2026-09-29 owner：只留已选件数与估值，不再附输赢说明
                page.Body = L10n.T("已选 " + selectedCount + " 件 · 估值 " + FormatMoney(ModeHItemBetStake.SelectedValue),
                    selectedCount + " item(s) picked · worth " + FormatMoney(ModeHItemBetStake.SelectedValue));
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                ModeHItemBetCandidate candidate = candidates[i];
                int key = candidate.Key;
                page.Cards.Add(new ModeHCardData
                {
                    Title = candidate.Name,
                    Count = candidate.Count,
                    Subtitle = (candidate.Equipped ? L10n.T("穿戴 · ", "Equipped · ") : string.Empty)
                        + L10n.T("估值 ", "Worth ") + FormatMoney(candidate.Value)
                        + (candidate.Contents > 0
                            ? L10n.T("（连里面的 " + candidate.Contents + " 件）", " (with " + candidate.Contents + " inside)")
                            : string.Empty),
                    Body = string.Empty,
                    GameQuality = candidate.Quality,
                    Icon = candidate.Icon,
                    IsSelected = candidate.Selected,
                    SelectedBadge = L10n.T("√ 已押上", "√ Bet"),
                    ActionLabel = candidate.Selected ? L10n.T("取下", "Take back") : L10n.T("押上", "Bet this"),
                    OnClick = delegate { ToggleItemBet(key); },
                });
            }
            if (selectedCount > 0)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("全部取下", "Take all back"),
                    OnClick = delegate
                    {
                        if (_commandsClosed || _runState == null) return;
                        ModeHItemBetStake.ClearSelection();
                        RouteUiForLifecycle(_runState.Lifecycle);
                    },
                });
            }
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("完成", "Done"),
                IsPrimary = true,
                IsCancel = true,
                OnClick = CloseItemBetPicker,
            });
            return page;
        }

        private void ToggleItemBet(int key)
        {
            if (_commandsClosed || _runState == null) return;
            string failure;
            if (ModeHItemBetStake.Toggle(key, out failure)) ModeHCashBetService.StandingTier = 0;
            else NotePageFailure(failure);
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        private void CloseItemBetPicker()
        {
            _showItemBetPicker = false;
            if (_commandsClosed || _runState == null) return;
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        #endregion

        #region 群战押注方向与赛季净赚（2026-10-01 审查；放在这里是为 GroupFlow 的单文件行数预算）

        /// <summary>这一季这一场已经押上、还没结清的那一笔（中断后重打时沿用）；按页面场次找（第 1 场 Drafting 时 MatchIndex 还是 0）。</summary>
        private ModeHCashBetRecord CarriedGroupBet()
        {
            if (_runState == null) return null;
            return ModeHCashBetService.ReservedFor(_runState.RunId, GroupMatchIndex);
        }

        /// <summary>沿用的押注记了押哪边（旧档没记的无从恢复，只能保留页面选择）。</summary>
        private static bool IsGroupBetSideLocked(ModeHCashBetRecord carried)
        {
            return carried != null && carried.betSide != ModeHCashBetService.BetSideNone;
        }

        /// <summary>沿用押注时把押哪边恢复成押注时那一边；返回沿用的那一笔（没有为 null）。</summary>
        private ModeHCashBetRecord RestoreCarriedGroupBetSide()
        {
            ModeHCashBetRecord carried = CarriedGroupBet();
            if (IsGroupBetSideLocked(carried)) _groupBetOnRed = carried.betSide == ModeHCashBetService.BetSideRed;
            return carried;
        }

        /// <summary>写进押注账本的方向：群战按锁盘那一刻押的队，单挑不记。</summary>
        private int LockedGroupBetSide()
        {
            if (!GroupModeEnabled) return ModeHCashBetService.BetSideNone;
            return _groupLockedBetOnRed ? ModeHCashBetService.BetSideRed : ModeHCashBetService.BetSideBlue;
        }

        /// <summary>
        /// 本季押注净赚：以账本里按季累计的 runNet 为准（跨会话、退游戏重进都在）；
        /// 修复前已结、账本里没累计到的场次，再用这一趟记下的已结押注补上（同一场只计一次）。
        /// </summary>
        private long ComputeGroupSeasonNet()
        {
            if (_runState == null) return 0L;
            long net = 0L;
            int counted = 0;
            Dictionary<int, ModeHCashBetRecord> byMatch = new Dictionary<int, ModeHCashBetRecord>();
            List<ModeHCashBetRecord> all = new List<ModeHCashBetRecord>();
            try
            {
                all.AddRange(ModeHSessionSummary.AllBets());
                ModeHCashBetRecord current = ModeHCashBetService.Current;
                if (current != null)
                {
                    all.Add(current);
                    if (string.Equals(current.netRunId, _runState.RunId, StringComparison.Ordinal))
                    {
                        net = current.runNet;
                        counted = current.netMatchMask;
                    }
                }
            }
            catch (Exception e) { LogFailure("group_season_net", e); }
            for (int i = 0; i < all.Count; i++)
            {
                ModeHCashBetRecord record = all[i];
                if (record == null || record.status != ModeHCashBetService.StatusSettled
                    || !string.Equals(record.runId, _runState.RunId, StringComparison.Ordinal)) continue;
                byMatch[record.matchIndex] = record;
            }
            foreach (ModeHCashBetRecord record in byMatch.Values)
            {
                int bit = record.matchIndex > 0 && record.matchIndex < 31 ? 1 << record.matchIndex : 0;
                if (bit != 0 && (counted & bit) != 0) continue; // 账本累计里已经有这一场
                net = ModeHCashBetService.SaturatingAdd(net, record.payout - record.amount);
            }
            return net;
        }

        #endregion

        #region 锁盘、结算与退回

        /// <summary>
        /// 锁盘成功后下注。先看这一场是不是已经押上（中断后重打）：是就沿用，不重扣也不重押。
        /// 否则押物品（勾过的话）或押钱。钱不够、东西不在、上一笔没结清时这一场不押：记一句原因给结算页，比赛照打。
        /// 赔率已在赛前展示，押成后直接进入生成。
        /// </summary>
        private void ReserveStandingCashBet()
        {
            if (_season == null || _runState == null || _season.currentLoadoutLock == null) return;
            _cashBetSkipNote = null;
            _cashBetSkipMatchIndex = -1;
            _showItemBetPicker = false;
            // 先对账：上一场若因战报落盘失败挂起过，押注还没结，按已有战报补结（至多一次）；别的赛季挂着的原样退回
            ReconcileCashBetOnRestore();
            // 账本只留最新一条：新押注覆盖之前，把上一场的结果抄进本场总结（奖品可能是结算页之后才补发完）
            ModeHSessionSummary.NoteBet(ModeHCashBetService.Current);
            ModeHCashBetRecord carried = CarriedBetForCurrentMatch();
            if (carried != null)
            {
                // 押注跟着这一场走：沿用中断前那一笔（读档后按账本从背包里认领押上的东西）
                if (carried.kind == ModeHCashBetService.KindItems)
                    ModeHItemBetStake.RebindFromLedger(ModeHItemBetEntry.Decode(carried.items));
                return;
            }
            if (!ModeHCashBetService.IsAvailable) return;
            int odds = _season.currentLoadoutLock.lockedOdds;
            if (ModeHItemBetStake.HasSelection)
            {
                ReserveItemBet(odds);
                return;
            }
            long amount = ModeHCashBetService.StandingAmount;
            if (amount <= 0) return;
            string failure;
            if (!ModeHCashBetService.TryReserve(_runState.RunId, _runState.MatchIndex, odds, amount, out failure,
                    LockedGroupBetSide()))
            {
                if (failure == "cash_bet_limit_changed")
                {
                    long limit = ModeHCashBetService.GetMaximumStandingAmount(ModeHCashBetService.MaximumStandingAmount, odds);
                    NoteCashBetSkipped(L10n.T("本场最多可押 " + FormatMoney(limit) + "，这一场没押。",
                        "The current maximum stake is " + FormatMoney(limit) + "; no bet this match."), failure);
                    return;
                }
                NoteCashBetSkipped(failure == "cash_bet_not_enough_money"
                    ? L10n.T("钱不够 " + FormatMoney(amount) + "，这一场没押。", "Not enough money for " + FormatMoney(amount) + "; no bet this match.")
                    : L10n.T("这一场没押成（存档正忙或上一笔还没结清）。", "No bet this match (save busy or a previous bet is still open)."), failure);
                return;
            }
        }

        /// <summary>押背包物品：记下押上的是哪几件、估值多少（物品原地不动），账本记成后才清掉勾选。</summary>
        private void ReserveItemBet(int odds)
        {
            long value;
            List<ModeHItemBetEntry> entries;
            string failureText;
            if (!ModeHItemBetStake.TryLock(out value, out entries, out failureText))
            {
                NoteCashBetSkipped(failureText, "item_bet_lock_failed");
                return;
            }
            string failure;
            if (!ModeHCashBetService.TryReserveItems(_runState.RunId, _runState.MatchIndex, odds, value, entries, out failure,
                    LockedGroupBetSide()))
            {
                ModeHItemBetStake.ReleaseLocked();
                NoteCashBetSkipped(L10n.T("这一场没押成（存档正忙或上一笔还没结清）。", "No bet this match (save busy or a previous bet is still open)."), failure);
                return;
            }
            ModeHItemBetStake.ClearSelection();
        }

        private void NoteCashBetSkipped(string note, string failureId)
        {
            _cashBetSkipMatchIndex = _runState.MatchIndex;
            _cashBetSkipNote = note;
            ModBehaviour.DevLog("[ModeH] 押注未成: " + (failureId ?? "unknown"));
        }

        /// <summary>本场分出胜负：按赔率结算（至多一次）。</summary>
        private void SettleCashBetForMatch(bool won)
        {
            if (_runState == null) return;
            SettleReservedBet(CarriedBetForCurrentMatch(), won);
        }

        /// <summary>实物结算交给账本保存计划、资产与剩余交付义务，重试沿用同一个结果。</summary>
        private void SettleReservedBet(ModeHCashBetRecord record, bool won)
        {
            if (record == null || record.status != ModeHCashBetService.StatusReserved) return;
            if (record.kind == ModeHCashBetService.KindItems)
            {
                ModeHCashBetService.TrySettleItems(record.runId, record.matchIndex, won,
                    _runState != null ? _runState.RunSeed : 0L);
                return;
            }
            long payout;
            ModeHCashBetService.TrySettle(record.runId, record.matchIndex, won, 0L, 0L, string.Empty, out payout);
        }

        /// <summary>这一季不再打了（放弃赛季、上一季的押注、F3 清理）：原样退回挂着的押注，并告诉玩家。</summary>
        private void RefundCashBet(string context)
        {
            ModeHCashBetRecord record = ModeHCashBetService.Current;
            bool items = record != null && record.status == ModeHCashBetService.StatusReserved
                && record.kind == ModeHCashBetService.KindItems;
            long refunded;
            if (!ModeHCashBetService.TryRefund(context, out refunded)) return;
            ModeHSessionSummary.NoteBet(ModeHCashBetService.Current); // 本场总结的「原样退回」
            ModeHItemBetStake.ReleaseLocked();
            if (refunded <= 0 || _owner == null) return;
            _owner.ShowMessage(items
                ? L10n.T("押上的物品不算了，东西都还在背包里。", "Your item bet was called off; the items stay in your backpack.")
                : L10n.T("押注 " + FormatMoney(refunded) + " 已原样退回。", "Your bet of " + FormatMoney(refunded) + " was returned."));
        }

        /// <summary>放弃前先履行已有胜负；已开战的未决押注按输结清，没开战的才退款；剩余义务必须保留原赛季作为依据。</summary>
        private bool TryResolveCashBetBeforeAbandon()
        {
            ReconcileCashBetOnRestore();
            ModeHCashBetRecord record = ModeHCashBetService.Current;
            if (record == null) return false;
            if (record.status != ModeHCashBetService.StatusReserved) return true;
            ModeHSeasonDto savedSeason = _season ?? ModeHProfilePersistence.LoadCurrent();
            if ((record.kind == ModeHCashBetService.KindItems && record.itemSettlement != 0)
                || FindCashBetReport(savedSeason, record) != null) return false;
            if (record.combatStarted != 0) ForfeitStartedCashBet(record, "abandon_season");
            else RefundCashBet("abandon_season");
            record = ModeHCashBetService.Current;
            return record != null && record.status != ModeHCashBetService.StatusReserved;
        }

        /// <summary>
        /// 已开战、没有战报的押注按输结清（2026-09-29 owner 拍板：开战后退出 / 放弃不能白退）。
        /// 押物品时收走押品；钱包或背包暂时不可用就留着，由下一次对账补结（至多一次）。
        /// </summary>
        private void ForfeitStartedCashBet(ModeHCashBetRecord record, string context)
        {
            if (record == null || record.status != ModeHCashBetService.StatusReserved) return;
            if (record.kind == ModeHCashBetService.KindItems)
            {
                ModeHCashBetService.TrySettleItems(record.runId, record.matchIndex, false,
                    _runState != null ? _runState.RunSeed : 0L);
            }
            else
            {
                long payout;
                ModeHCashBetService.TrySettle(record.runId, record.matchIndex, false, 0L, 0L, string.Empty, out payout);
            }
            ModeHSessionSummary.NoteBet(ModeHCashBetService.Current);
            ModBehaviour.DevLog("[ModeH] 已开战押注按输结清 (" + context + "): " + record.amount);
        }

        private static ModeHMatchReportDto FindCashBetReport(ModeHSeasonDto savedSeason, ModeHCashBetRecord record)
        {
            if (record == null || savedSeason == null || savedSeason.runState == null
                || !string.Equals(record.runId, savedSeason.runState.runId, StringComparison.Ordinal)) return null;
            List<ModeHMatchReportDto> reports = savedSeason.matchReports;
            for (int i = 0; reports != null && i < reports.Count; i++)
                if (reports[i] != null && reports[i].matchIndex == record.matchIndex) return reports[i];
            return null;
        }

        /// <summary>
        /// 读档恢复赛季时对账：挂着的押注如果本场已有战报，就按战报结算；属于别的赛季或没有活动赛季，就原样退回。
        /// 同一赛季同一场还没打完的留着：重打这一场时沿用（押注跟着这一场走）。
        /// </summary>
        private void ReconcileCashBetOnRestore()
        {
            try
            {
                ModeHCashBetRecord record = ModeHCashBetService.Current;
                if (record == null || record.status != ModeHCashBetService.StatusReserved) return;
                if (record.kind == ModeHCashBetService.KindItems && record.itemSettlement != 0)
                {
                    SettleReservedBet(record, record.itemSettlement == 1);
                    return;
                }
                // 已结束的赛季不会重建 run owner，但其中的已存战报仍是未结押注的权威结果。
                // 先找同一季同一场的结果，不能把延迟到账的赢注误退成只有本金。
                ModeHSeasonDto savedSeason = _season ?? ModeHProfilePersistence.LoadCurrent();
                ModeHMatchReportDto report = FindCashBetReport(savedSeason, record);
                if (report != null)
                {
                    bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
                    if (record.kind == ModeHCashBetService.KindItems)
                        ModeHCashBetService.TrySettleItems(record.runId, record.matchIndex, won, savedSeason.runState.runSeed);
                    else SettleReservedBet(record, won);
                    return;
                }
                // 看台退出 / 挂起送回基地后内存 owner 已清空，但磁盘上同一季仍可续：押注跟着这一场走，不能在回基地时退掉。
                bool sameRun = _runState != null
                    ? string.Equals(record.runId, _runState.RunId, StringComparison.Ordinal)
                    : HasResumableSeasonRecord(savedSeason)
                        && string.Equals(record.runId, savedSeason.runState.runId, StringComparison.Ordinal);
                if (sameRun) return;
                // 这一季不再打了：已开战的押注不能白退（放弃赛季同口径），没开战的原样退回
                if (record.combatStarted != 0) ForfeitStartedCashBet(record, "restore_other_run");
                else RefundCashBet("restore_other_run");
            }
            catch (Exception e)
            {
                LogFailure("cash_bet_reconcile", e);
            }
        }

        #endregion
    }
}
