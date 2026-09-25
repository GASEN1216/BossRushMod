// ============================================================================
// ModeHRuntimeModule_BetFlow.cs - 鸭王杯押注的流程接线（2026-09-24 owner 拍板）
// ============================================================================
// 账本与赔付口径在 ModeHCashBetService.cs，押背包/穿戴物品的物品侧在 ModeHItemBetStake.cs；
// 这里只把它们接进鸭王杯的页面与状态机：
//   - 赛前双方对照页与赔率页下方一排「押注」：不押 / 1,000 / 5,000 / 20,000 / 押物品；
//     押钱选的是「接下来每场押多少」，默认不押，读档回到不押；押物品可选背包、容器和穿戴物品，只管下一场；
//   - 锁盘成功后扣押金或记下押上的物品（钱不够、东西不在就这一场不押，写一句原因，比赛照打），开盘动画结束才开战；
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
        /// 页脚「押注」一排（钱包不在时不挂，§4.14）：押钱四档 + 押物品。选中档染主色；钱不够照挂，锁盘时再说明。
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
            if (carried != null)
            {
                row.Caption = L10n.T("这一场沿用中断前押的", "This match keeps the bet placed before the interruption: ")
                    + DescribeRecordStake(carried) + L10n.T("（中断不退，重打照算）；下面选的是之后几场。", " (not refunded; the rematch settles it). The choice below is for later matches.");
            }
            else if (itemsMode)
            {
                row.Caption = L10n.T("押上 " + ModeHItemBetStake.SelectedCount + " 件物品，估值 ", "Betting " + ModeHItemBetStake.SelectedCount + " item(s) worth ")
                    + FormatMoney(ModeHItemBetStake.SelectedValue)
                    + L10n.T("：赢了东西留着、另得同等品质的奖品；输了归庄家。押物品只管下一场。",
                        ": win and you keep them plus prizes of matching quality; lose and the house takes them. Applies to the next match only.");
            }
            else
            {
                row.Caption = L10n.T("押你的选手赢：赔率越冷门拿回越多，输了押金归庄家。余额 ", "Bet on your fighter: longer odds pay more; lose and the house keeps it. Balance ")
                    + FormatMoney(Duckov.Economy.EconomyManager.Money);
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
                Label = itemsMode
                    ? L10n.T("押物品 · " + ModeHItemBetStake.SelectedCount + " 件", "Items · " + ModeHItemBetStake.SelectedCount)
                    : L10n.T("押物品", "Bet items"),
                IsSelected = itemsMode,
                OnClick = OpenItemBetPicker,
            });
            page.OptionRows.Add(row);
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
            long amount = ModeHCashBetService.StandingAmount;
            if (amount <= 0) return string.Empty;
            return L10n.T(" · 押 ", " · Bet ") + FormatMoney(amount);
        }

        internal static string FormatMoney(long amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        internal static string FormatPayoutMultiplier(int odds)
        {
            return "x" + (ModeHCashBetService.ComputePayout(1000000L, odds) / 1000000d)
                .ToString("0.00", CultureInfo.InvariantCulture);
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
                if (record.itemSettlement == 1 || record.payout > 0) AppendPrizeIcons(page, record);
                page.Lines.Add(head + (record.itemSettlement == 1 || record.payout > 0
                    ? L10n.T("押品保留", "stake kept")
                        + (record.prizeCash > 0 ? L10n.T("，零头折成 ", "; remainder paid as ") + FormatMoney(record.prizeCash) : string.Empty)
                    : L10n.T("输了，押上的东西归庄家", "lost: the house takes the items")
                        + (record.charged > 0
                            ? L10n.T("；有的找不到了，按估值从余额扣了 " + FormatMoney(record.charged),
                                "; some were missing, so " + FormatMoney(record.charged) + " was taken from your balance")
                            : string.Empty)));
                return;
            }
            page.Lines.Add(head + (record.payout > 0
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
                page.Body = L10n.T("已选 " + selectedCount + " 件 · 估值 " + FormatMoney(ModeHItemBetStake.SelectedValue)
                    + "。赢了保留物品并获奖，输了押品归庄家。",
                    selectedCount + " item(s) picked, worth " + FormatMoney(ModeHItemBetStake.SelectedValue)
                    + ". Win: keep them and earn prizes. Lose: the house takes your stake.");
            }
            else
            {
                page.Body = L10n.T("背包和穿戴都可选。点选押上，再点取消；容器连同内容一起押。",
                    "Choose carried or equipped items. Tap to select or clear. Containers include their contents.");
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

        #region 锁盘、结算与退回

        /// <summary>
        /// 锁盘成功后下注。先看这一场是不是已经押上（中断后重打）：是就沿用，不重扣也不重押。
        /// 否则押物品（勾过的话）或押钱。钱不够、东西不在、上一笔没结清时这一场不押：记一句原因给结算页，比赛照打。
        /// 押成就播一次「开盘」揭晓（纯表现，不挡状态机）。
        /// </summary>
        private void ReserveStandingCashBet()
        {
            if (_season == null || _runState == null || _season.currentLoadoutLock == null) return;
            _cashBetSkipNote = null;
            _cashBetSkipMatchIndex = -1;
            _showItemBetPicker = false;
            ModeHCashBetRecord carried = CarriedBetForCurrentMatch();
            if (carried != null)
            {
                // 押注跟着这一场走：沿用中断前那一笔（读档后按账本从背包里认领押上的东西）
                if (carried.kind == ModeHCashBetService.KindItems)
                    ModeHItemBetStake.RebindFromLedger(ModeHItemBetEntry.Decode(carried.items));
                ModeHBetRevealView.Play(carried.amount, carried.odds, carried.kind == ModeHCashBetService.KindItems);
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
            if (!ModeHCashBetService.TryReserve(_runState.RunId, _runState.MatchIndex, odds, amount, out failure))
            {
                NoteCashBetSkipped(failure == "cash_bet_not_enough_money"
                    ? L10n.T("钱不够 " + FormatMoney(amount) + "，这一场没押。", "Not enough money for " + FormatMoney(amount) + "; no bet this match.")
                    : L10n.T("这一场没押成（存档正忙或上一笔还没结清）。", "No bet this match (save busy or a previous bet is still open)."), failure);
                return;
            }
            ModeHBetRevealView.Play(amount, odds, false);
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
            if (!ModeHCashBetService.TryReserveItems(_runState.RunId, _runState.MatchIndex, odds, value, entries, out failure))
            {
                ModeHItemBetStake.ReleaseLocked();
                NoteCashBetSkipped(L10n.T("这一场没押成（存档正忙或上一笔还没结清）。", "No bet this match (save busy or a previous bet is still open)."), failure);
                return;
            }
            ModeHItemBetStake.ClearSelection();
            ModeHBetRevealView.Play(value, odds, true);
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
            ModeHItemBetStake.ReleaseLocked();
            if (refunded <= 0 || _owner == null) return;
            _owner.ShowMessage(items
                ? L10n.T("押上的物品不算了，东西都还在背包里。", "Your item bet was called off; the items stay in your backpack.")
                : L10n.T("押注 " + FormatMoney(refunded) + " 已原样退回。", "Your bet of " + FormatMoney(refunded) + " was returned."));
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
                bool sameRun = _runState != null && string.Equals(record.runId, _runState.RunId, StringComparison.Ordinal);
                if (!sameRun)
                {
                    RefundCashBet("restore_other_run");
                    return;
                }
                List<ModeHMatchReportDto> reports = _season != null ? _season.matchReports : null;
                if (reports == null) return;
                for (int i = 0; i < reports.Count; i++)
                {
                    ModeHMatchReportDto report = reports[i];
                    if (report == null || report.matchIndex != record.matchIndex) continue;
                    SettleReservedBet(record, report.winner == (int)ModeHMatchOutcome.PlayerVictory);
                    return;
                }
            }
            catch (Exception e)
            {
                LogFailure("cash_bet_reconcile", e);
            }
        }

        #endregion
    }
}
