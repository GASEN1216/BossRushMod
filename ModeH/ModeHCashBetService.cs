// ============================================================================
// ModeHCashBetService.cs - 鸭王杯「押钱」：押你的选手赢，按赔率抽水赔付（2026-09-24 owner 拍板）
// ============================================================================
// 为什么是押钱而不是押仓库物品：仓库（PlayerStorage）只在基地场景里存在，鸭王杯在出击地图上开打，
// 原来的真实押品链锁盘与结算都要读写仓库，在比赛里永远「仓库暂不可用」。owner 定：改成玩家自己选押多少钱，
// 看比赛输赢、按赔率抽水，长期让玩家的钱慢慢往下掉（「这才符合赌徒的性质」）。
//
// 赔付口径（ModeHConfig.CashBet*）：
//   赢了拿回 = 押金 × (1000 − 抽水‰) ÷ 假定胜率‰，按赔率档取假定胜率（冷门档胜率低、拿回多）；
//   输了押金归庄家。假定胜率取「表里的保守值」与「本档位实际胜率」（样本够了之后）两者的较大值——
//   实际胜率高于表里的数时赔付自动下调，长期回报恒不高于 1 − 抽水。每档的押注次数与胜场记在账本里，
//   实机跑够场次后按它调表（owner：「赢钱概率取决于实战，要实机跑够场次再调」）。
//
// 资金安全（形态照 Achievement/AchievementRewardJournal）：
//   账本（一条进行中的押注 + 分档统计）按本槽保存 JSON 字符串；扣钱 / 发钱之后把官方 EconomyData 快照
//   与账本同批落盘（BeforeCollectSaveData），崩在中途要么都没变、要么都变了。
//   状态单向：Reserved（已扣押金）→ Settled（按输赢付过）或 Refunded（技术中止原样退回）。
//   先写账本再动钱：发钱永远「至多一次」；崩在写账本与动钱之间，读档后看到的是动钱之前的账本与钱。
//   押注跟着这一场走（2026-09-24 追加）：技术中止、挂起、退游戏重进都不退，重打这一场时沿用、按重打的结果结算
//   （旧版一中断就整额退回，打输了强退重进等于重掷，押钱就不会「慢慢往下掉」）。
//   只有这一季不再打了才原样退回：恢复页放弃赛季、开新赛季时发现上一季挂着的押注。
//
// 押背包物品（kind = KindItems，物品侧在 ModeHItemBetStake）：锁盘不扣钱或搬物，给押品盖持久身份，随主角物品树同存；
//   押什么、押多少都不限（owner：「押上的物品不要有限制，只是其品质和价钱会影响到再次给予其奖品的品质和价钱」）。
//   赢了物品留着，另发奖品：奖品的品质 = 押上物品按估值加权的平均品质，奖品的总价值 = 「赔付 − 估值」，
//   件数 = 押上的件数（最多 ItemBetMaxPrizeItems），凑不满的价值折成钱；输了收走押上的物品，找不到的按估值从余额扣。
//   结算先固定计划，再同批保存实物与剩余义务，全部交付后才结清现金与统计；满包保留欠账，每秒最多重试一次。
//   schemaVersion=2 接受旧 v1，新增字段缺省安全；旧记录没有物品身份时不认领同型号替代品。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Duckov.Economy;
using Saves;

namespace BossRush
{
    /// <summary>押钱账本：一条进行中的押注 + 分赔率档的押注次数与胜场。</summary>
    internal sealed class ModeHCashBetRecord
    {
        public string runId = string.Empty;
        public int matchIndex;
        public long amount;
        public int odds;
        /// <summary>0 无 / 1 已扣押金 / 2 已结算 / 3 已退回。</summary>
        public int status;
        public long payout;
        /// <summary>0 押钱 / 1 押背包物品（amount 是物品估值合计）。</summary>
        public int kind;
        /// <summary>押物品时押了哪几件（ModeHItemBetEntry.Encode）。</summary>
        public string items = string.Empty;
        /// <summary>押物品输了、押上的东西找不到时按估值从余额扣了多少。</summary>
        public long charged;
        /// <summary>押物品赢了发的奖品（「名字 ×n、名字」，给结算页看）。</summary>
        public string prizes = string.Empty;
        /// <summary>押物品赢了、奖品凑不满的价值折成的钱。</summary>
        public long prizeCash;
        /// <summary>实物结算进度：0 未准备 / 1 赢 / 2 输。旧档缺省为 0；结果一旦准备就不再重算。</summary>
        public int itemSettlement;
        /// <summary>尚未交付的奖品或尚未收走的押品（带持久身份的 ModeHItemBetEntry）。</summary>
        public string pendingItems = string.Empty;
        /// <summary>收走押品后累计的缺失估值；与实物快照一起保存，最终结算只扣一次。</summary>
        public long missingValue;
        /// <summary>按赔率档（下标 = 赔率 1–5）的押注次数与胜场，用来校准赔付。</summary>
        public long[] tierBets = new long[ModeHConfig.MaxOdds + 1];
        public long[] tierWins = new long[ModeHConfig.MaxOdds + 1];

        internal ModeHCashBetRecord Clone()
        {
            ModeHCashBetRecord copy = (ModeHCashBetRecord)MemberwiseClone();
            copy.tierBets = (long[])tierBets.Clone();
            copy.tierWins = (long[])tierWins.Clone();
            return copy;
        }
    }

    /// <summary>账本中的物品：typeId|数量|估值|品质|名字|持久身份；兼容没有第六列的旧记录。</summary>
    internal sealed class ModeHItemBetEntry
    {
        public int TypeId;
        public int Count;
        public long Value;
        public int Quality;
        public string Name;
        public string Identity;

        internal static string Encode(List<ModeHItemBetEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                ModeHItemBetEntry e = entries[i];
                if (e == null) continue;
                if (sb.Length > 0) sb.Append(';');
                string name = (e.Name ?? string.Empty).Replace("|", " ").Replace(";", " ");
                sb.Append(e.TypeId.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(e.Count.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(e.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(e.Quality.ToString(CultureInfo.InvariantCulture)).Append('|').Append(name)
                    .Append('|').Append(e.Identity ?? string.Empty);
            }
            return sb.ToString();
        }

        internal static List<ModeHItemBetEntry> Decode(string text)
        {
            List<ModeHItemBetEntry> result = new List<ModeHItemBetEntry>();
            if (string.IsNullOrEmpty(text)) return result;
            string[] parts = text.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(new[] { '|' }, 6);
                int typeId, count, quality;
                long value;
                if (fields.Length < 5
                    || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out typeId)
                    || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                    || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                    || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out quality)
                    || count <= 0 || value < 0) continue;
                result.Add(new ModeHItemBetEntry { TypeId = typeId, Count = count, Value = value, Quality = quality,
                    Name = fields[4], Identity = fields.Length > 5 ? fields[5] : string.Empty });
            }
            return result;
        }

        /// <summary>
        /// 奖品的品质：押上物品按估值加权的平均品质，四舍五入，夹在官方品质 1–8 之间。
        /// 一件贵的高品质东西配几件零碎，奖品跟着贵的那件走。
        /// </summary>
        internal static int PrizeQuality(List<ModeHItemBetEntry> entries)
        {
            long weight = 0;
            long weighted = 0;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                ModeHItemBetEntry e = entries[i];
                if (e == null || e.Value <= 0) continue;
                int q = Math.Max(ModeHConfig.MinGameQuality, Math.Min(ModeHConfig.MaxGameQuality, e.Quality));
                weight += e.Value;
                weighted += e.Value * q;
            }
            if (weight <= 0) return ModeHConfig.MinGameQuality;
            long rounded = (weighted * 2 + weight) / (weight * 2);
            return (int)Math.Max(ModeHConfig.MinGameQuality, Math.Min(ModeHConfig.MaxGameQuality, rounded));
        }

        /// <summary>奖品件数：押上几件发几件，最多 ModeHConfig.ItemBetMaxPrizeItems 件，至少 1 件。</summary>
        internal static int PrizeSlots(List<ModeHItemBetEntry> entries)
        {
            int count = 0;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i] != null) count++;
            }
            return Math.Max(1, Math.Min(ModeHConfig.ItemBetMaxPrizeItems, count));
        }

        /// <summary>「名字 ×n、名字」。</summary>
        internal static string Describe(List<ModeHItemBetEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                ModeHItemBetEntry e = entries[i];
                if (e == null) continue;
                if (sb.Length > 0) sb.Append(L10n.T("、", ", "));
                sb.Append(e.Name ?? "?");
                if (e.Count > 1) sb.Append(" ×").Append(e.Count.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>鸭王杯押钱的唯一入口。全部入口 no-throw。</summary>
    internal static class ModeHCashBetService
    {
        internal const int StatusNone = 0;
        internal const int StatusReserved = 1;
        internal const int StatusSettled = 2;
        internal const int StatusRefunded = 3;
        internal const int KindCash = 0;
        internal const int KindItems = 1;

        private static CashBetJournal _journal;

        /// <summary>玩家在选人页 / 结算页选的押注档（ModeHConfig.CashBetAmounts 的下标）。纯运行时：读档回到「不押」。</summary>
        internal static int StandingTier;

        /// <summary>官方钱包在不在（没有 EconomyManager 时不挂押钱选项）。</summary>
        internal static bool IsAvailable
        {
            get { return EconomyManager.Instance != null; }
        }

        /// <summary>当前押注档对应的金额；0 = 不押。</summary>
        internal static long StandingAmount
        {
            get
            {
                long[] amounts = ModeHConfig.CashBetAmounts;
                int tier = StandingTier < 0 || StandingTier >= amounts.Length ? 0 : StandingTier;
                return amounts[tier];
            }
        }

        /// <summary>
        /// 赢了拿回多少（含押金）：押金 × (1000 − 抽水) ÷ 假定胜率，向下取整到 10。
        /// 假定胜率 = max(表里的保守值, 本档实际胜率)；样本不足时只用表。
        /// </summary>
        internal static long ComputePayout(long stake, int odds)
        {
            if (stake <= 0) return 0;
            int tier = odds < ModeHConfig.MinOdds ? ModeHConfig.MinOdds : (odds > ModeHConfig.MaxOdds ? ModeHConfig.MaxOdds : odds);
            int assumed = ResolveAssumedWinPermille(tier);
            long gross = stake * (1000 - ModeHConfig.CashBetHouseCutPermille) / assumed;
            return gross / 10 * 10;
        }

        /// <summary>本档用来算赔付的假定胜率（‰）。</summary>
        internal static int ResolveAssumedWinPermille(int tier)
        {
            int table = ModeHConfig.CashBetAssumedWinPermilleByOdds[tier];
            ModeHCashBetRecord record = Current;
            if (record == null || record.tierBets[tier] < ModeHConfig.CashBetCalibrationMinSamples) return table;
            long observed = record.tierWins[tier] * 1000 / Math.Max(1L, record.tierBets[tier]);
            int assumed = (int)Math.Max(table, observed);
            // 全胜的档位也要给一点赔付：假定胜率封顶 990‰（赢了至少拿回押金的 92%，仍是亏）
            return Math.Min(990, Math.Max(1, assumed));
        }

        /// <summary>当前账本（只读副本）；存档不可用时为 null。</summary>
        internal static ModeHCashBetRecord Current
        {
            get
            {
                try { return EnsureJournal().Current; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>这一季这一场已经押上、还没结清的那一笔（中断后重打时沿用）；没有时为 null。</summary>
        internal static ModeHCashBetRecord ReservedFor(string runId, int matchIndex)
        {
            ModeHCashBetRecord record = Current;
            if (record == null || record.status != StatusReserved || record.matchIndex != matchIndex
                || !string.Equals(record.runId, runId, StringComparison.Ordinal)) return null;
            return record;
        }

        /// <summary>锁盘时保存押品凭据与主角物品快照，不扣钱或搬动物品。</summary>
        internal static bool TryReserveItems(string runId, int matchIndex, int odds, long value,
            List<ModeHItemBetEntry> entries, out string failureReasonId)
        {
            failureReasonId = null;
            if (value <= 0 || entries == null || entries.Count == 0)
            {
                failureReasonId = "item_bet_empty";
                return false;
            }
            try
            {
                return EnsureJournal().TryReserveItems(runId, matchIndex, odds, value, ModeHItemBetEntry.Encode(entries), out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "item_bet_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 锁盘后扣押金。钱不够、钱包不在、上一笔还没结清时不押（返回 false 并给出原因），比赛照打。
        /// </summary>
        internal static bool TryReserve(string runId, int matchIndex, int odds, long amount, out string failureReasonId)
        {
            failureReasonId = null;
            if (amount <= 0) return true;
            try
            {
                return EnsureJournal().TryReserve(runId, matchIndex, odds, amount, out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "cash_bet_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 按本场输赢结算（至多一次）。没有本场的押注时返回 false、payout = 0。以下三个参数只对押物品有意义：
        /// <paramref name="lossCharge"/> 输了时押上的东西找不到的部分的估值，从余额里扣（扣到 0 为止）；
        /// <paramref name="winCash"/> 赢了时奖品凑不满、折成钱的部分（不超过「赔付 − 估值」）；
        /// <paramref name="prizes"/> 赢了发的奖品清单（给结算页看）。
        /// </summary>
        internal static bool TrySettle(string runId, int matchIndex, bool won, long lossCharge, long winCash, string prizes, out long payout)
        {
            payout = 0;
            try
            {
                return EnsureJournal().TrySettle(runId, matchIndex, won, lossCharge, winCash, prizes, out payout);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 押钱结算异常: " + e.Message);
                return false;
            }
        }

        /// <summary>实物结算：先保存固定计划，再逐批交付/收走并保存资产，最后结清现金与统计。</summary>
        internal static bool TrySettleItems(string runId, int matchIndex, bool won, long runSeed)
        {
            try { return EnsureJournal().TrySettleItems(runId, matchIndex, won, runSeed); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 押物品结算顺延: " + e.Message);
                return false;
            }
        }

        /// <summary>宿主驱动欠账与保存重试；没有账本时不创建实例。</summary>
        internal static void Tick()
        {
            if (_journal != null) _journal.Tick();
        }

        /// <summary>放弃赛季 / 孤儿押注：原样退回（至多一次）。已准备实物结算的记录继续履约，不能退款覆盖。</summary>
        internal static bool TryRefund(string context, out long refunded)
        {
            refunded = 0;
            try
            {
                return EnsureJournal().TryRefund(context, out refunded);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 押钱退回异常 (" + context + "): " + e.Message);
                return false;
            }
        }

        /// <summary>模块销毁：退订存档事件、清掉实例（§4.6）。</summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                if (_journal != null) _journal.Shutdown();
            }
            catch (Exception)
            {
                // 退订失败也要丢引用
            }
            _journal = null;
            StandingTier = 0;
        }

        private static CashBetJournal EnsureJournal()
        {
            if (_journal == null) _journal = new CashBetJournal();
            return _journal;
        }

        /// <summary>账本、现金与必要的主角物品快照同批落盘（照 AchievementRewardJournal）。</summary>
        private sealed class CashBetJournal : IBossRushSaveBatchSource
        {
            private const string StorageKey = "BossRush_ModeHCashBet_v1";
            private readonly BossRushSlotJsonStore<ModeHCashBetRecord> _store;
            private readonly BossRushSaveCoordinatorEngine _coordinator;
            private bool _cashSnapshotRequired;
            private bool _itemSnapshotRequired;
            private bool _staging;
            private int _slot = -1;
            private float _nextItemRetry;

            internal CashBetJournal()
            {
                _store = new BossRushSlotJsonStore<ModeHCashBetRecord>(new BossRushSlotJsonStoreSpec<ModeHCashBetRecord>
                {
                    StorageKey = StorageKey, SchemaVersion = 2, LogPrefix = "[ModeH] ", DisplayName = "鸭王杯押钱账本",
                    CreateDefault = () => new ModeHCashBetRecord(), Encode = Encode, Decode = Decode,
                    ReadSchemaVersion = ReadCompatibleSchema, NotifySlotChanged = OnSlotChanged,
                    BeforeCollectSaveData = CollectCash,
                });
                _coordinator = new BossRushSaveCoordinatorEngine(this, false);
                _store.EnsureSubscribed();
            }

            internal ModeHCashBetRecord Current
            {
                get { return _store.Current.Clone(); }
            }

            internal bool TryReserve(string runId, int matchIndex, int odds, long amount, out string failureReasonId)
            {
                failureReasonId = null;
                if (!CanMoveMoney(out failureReasonId)) return false;
                ModeHCashBetRecord previous = _store.Current;
                if (previous.status == StatusReserved)
                {
                    failureReasonId = "cash_bet_previous_unsettled";
                    return false;
                }
                if (EconomyManager.Money < amount)
                {
                    failureReasonId = "cash_bet_not_enough_money";
                    return false;
                }
                ModeHCashBetRecord candidate = previous.Clone();
                candidate.runId = runId ?? string.Empty;
                candidate.matchIndex = matchIndex;
                candidate.amount = amount;
                candidate.odds = odds;
                candidate.status = StatusReserved;
                candidate.payout = 0;
                candidate.kind = KindCash;
                candidate.items = string.Empty;
                candidate.charged = 0;
                candidate.prizes = string.Empty;
                candidate.prizeCash = 0;
                candidate.itemSettlement = 0;
                candidate.pendingItems = string.Empty;
                candidate.missingValue = 0;
                return Commit(previous, candidate, -amount, out failureReasonId);
            }

            internal bool TryReserveItems(string runId, int matchIndex, int odds, long value, string items, out string failureReasonId)
            {
                failureReasonId = null;
                if (!CanMoveMoney(out failureReasonId)) return false;
                if (!ModeHItemBetStake.CanSnapshotPlayer())
                {
                    failureReasonId = "item_bet_player_missing";
                    return false;
                }
                ModeHCashBetRecord previous = _store.Current;
                if (previous.status == StatusReserved)
                {
                    failureReasonId = "cash_bet_previous_unsettled";
                    return false;
                }
                ModeHCashBetRecord candidate = previous.Clone();
                candidate.runId = runId ?? string.Empty;
                candidate.matchIndex = matchIndex;
                candidate.amount = value;
                candidate.odds = odds;
                candidate.status = StatusReserved;
                candidate.payout = 0;
                candidate.kind = KindItems;
                candidate.items = items ?? string.Empty;
                candidate.charged = 0;
                candidate.prizes = string.Empty;
                candidate.prizeCash = 0;
                candidate.itemSettlement = 0;
                candidate.pendingItems = string.Empty;
                candidate.missingValue = 0;
                // 锁盘的持久身份随主角物品树与账本同批保存，不能只存账本。
                _itemSnapshotRequired = true;
                return Commit(previous, candidate, 0, out failureReasonId);
            }

            internal bool TrySettleItems(string runId, int matchIndex, bool won, long runSeed)
            {
                ModeHCashBetRecord previous = _store.Current;
                if (previous.kind != KindItems || previous.status != StatusReserved
                    || previous.matchIndex != matchIndex
                    || !string.Equals(previous.runId, runId, StringComparison.Ordinal)) return false;
                string reason;
                // 接收方与写入边界没就绪时，尤其不能先收走押品。
                if (!CanMoveMoney(out reason) || !ModeHItemBetStake.CanSnapshotPlayer()) return false;

                if (previous.itemSettlement == 0)
                {
                    ModeHCashBetRecord plan = previous.Clone();
                    plan.itemSettlement = won ? 1 : 2;
                    plan.pendingItems = previous.items;
                    if (won)
                    {
                        List<ModeHItemBetEntry> entries = ModeHItemBetEntry.Decode(previous.items);
                        long budget = Math.Max(0L, ComputePayout(previous.amount, previous.odds) - previous.amount);
                        long prizeValue;
                        string summary;
                        List<ModeHItemBetEntry> prizes = ModeHItemBetStake.PreparePrizePlan(budget,
                            ModeHItemBetEntry.PrizeQuality(entries), ModeHItemBetEntry.PrizeSlots(entries),
                            runSeed, previous.runId + "|" + previous.matchIndex, out prizeValue, out summary);
                        plan.pendingItems = ModeHItemBetEntry.Encode(prizes);
                        plan.prizes = summary;
                        plan.prizeCash = Math.Max(0L, budget - prizeValue);
                    }
                    if (!Commit(previous, plan, 0L, out reason)) return false;
                    previous = _store.Current;
                }

                // 使用已保存的输赢与奖品计划：重试、跨场景和退出重进都不能重掷或改变结果。
                if (!string.IsNullOrEmpty(previous.pendingItems))
                {
                    if (!CanMoveMoney(out reason)) return false;
                    ModeHCashBetRecord candidate = previous.Clone();
                    _slot = SavesSystem.CurrentSlot;
                    _staging = true;
                    try
                    {
                        List<ModeHItemBetEntry> pending = ModeHItemBetEntry.Decode(previous.pendingItems);
                        if (previous.itemSettlement == 1)
                        {
                            candidate.pendingItems = ModeHItemBetEntry.Encode(ModeHItemBetStake.DeliverPrizes(pending));
                            if (candidate.pendingItems == previous.pendingItems) return false;
                        }
                        else
                        {
                            ModeHItemBetStake.RebindFromLedger(pending);
                            candidate.missingValue = ModeHItemBetStake.ForfeitLocked();
                            candidate.pendingItems = string.Empty;
                        }
                        _itemSnapshotRequired = true;
                        // Store 只接受候选；采集门在 staging 期间关闭。资产与剩余义务同批进入 ES3。
                        if (!_store.Store(candidate)) return false;
                    }
                    finally { _staging = false; }
                    string error;
                    if (!_coordinator.RequestFlush(out error, true)) return false;
                    previous = _store.Current;
                    if (!string.IsNullOrEmpty(previous.pendingItems)) return false;
                }

                long payout;
                bool settled = TrySettle(runId, matchIndex, previous.itemSettlement == 1,
                    previous.missingValue, previous.prizeCash, previous.prizes, out payout);
                if (settled) ModeHItemBetStake.ReleaseLocked();
                return settled;
            }

            internal void Tick()
            {
                _coordinator.Tick();
                ModeHCashBetRecord record = _store.Current;
                if (_staging || record.status != StatusReserved || record.kind != KindItems
                    || record.itemSettlement == 0 || UnityEngine.Time.realtimeSinceStartup < _nextItemRetry) return;
                _nextItemRetry = UnityEngine.Time.realtimeSinceStartup + 1f;
                TrySettleItems(record.runId, record.matchIndex, record.itemSettlement == 1, 0L);
            }

            internal bool TrySettle(string runId, int matchIndex, bool won, long lossCharge, long winCash, string prizes, out long payout)
            {
                payout = 0;
                ModeHCashBetRecord previous = _store.Current;
                if (previous.status != StatusReserved || previous.matchIndex != matchIndex
                    || !string.Equals(previous.runId, runId, StringComparison.Ordinal)) return false;
                string reason;
                if (!CanMoveMoney(out reason)) return false;
                int tier = Math.Max(ModeHConfig.MinOdds, Math.Min(ModeHConfig.MaxOdds, previous.odds));
                // 赔付按「结算前」的统计算：本场结果不回头影响本场赔付
                long gross = won ? ComputePayout(previous.amount, previous.odds) : 0;
                long delta = gross;
                long charged = 0;
                if (previous.kind == KindItems)
                {
                    // 押物品：调用方已经完成实物结算。赢了只把奖品凑不满的部分发成钱，
                    // 钱不超过「赔付 − 估值」；输了物品被收走、找不到的按估值扣到 0 为止
                    delta = won ? Math.Max(0L, Math.Min(winCash, gross - previous.amount)) : 0L;
                    if (!won && lossCharge > 0) charged = Math.Min(lossCharge, Math.Max(0L, EconomyManager.Money));
                    if (charged > 0) delta = -charged;
                }
                ModeHCashBetRecord candidate = previous.Clone();
                candidate.status = StatusSettled;
                candidate.payout = gross;
                candidate.charged = charged;
                candidate.prizes = won && previous.kind == KindItems ? (prizes ?? string.Empty) : string.Empty;
                candidate.prizeCash = won && previous.kind == KindItems ? delta : 0L;
                candidate.tierBets[tier]++;
                if (won) candidate.tierWins[tier]++;
                if (!Commit(previous, candidate, delta, out reason)) return false;
                payout = gross;
                ModBehaviour.DevLog("[ModeH] 押钱结算: " + (previous.kind == KindItems ? "押物品 估值 " : "押 ") + previous.amount
                    + " x" + tier + (won ? " 赢 拿回 " + gross : " 输" + (charged > 0 ? " 扣 " + charged : ""))
                    + " | 本档 " + candidate.tierWins[tier] + "/" + candidate.tierBets[tier]);
                return true;
            }

            internal bool TryRefund(string context, out long refunded)
            {
                refunded = 0;
                ModeHCashBetRecord previous = _store.Current;
                if (previous.status != StatusReserved) return false;
                // 已确定输赢的实物结算必须继续交付，不能被放弃赛季覆盖成退回。
                if (previous.kind == KindItems && previous.itemSettlement != 0) return false;
                string reason;
                if (!CanMoveMoney(out reason)) return false;
                ModeHCashBetRecord candidate = previous.Clone();
                candidate.status = StatusRefunded;
                candidate.payout = previous.amount;
                // 押物品退回：东西本来就没离开背包，不动钱
                long delta = previous.kind == KindItems ? 0L : previous.amount;
                if (!Commit(previous, candidate, delta, out reason)) return false;
                refunded = previous.amount;
                ModBehaviour.DevLog("[ModeH] 押钱原样退回 (" + context + "): " + previous.amount);
                return true;
            }

            /// <summary>先把账本排进队列，再动钱，钱没按预期变就撤回账本；最后与现金快照同批落盘。</summary>
            private bool Commit(ModeHCashBetRecord previous, ModeHCashBetRecord candidate, long delta, out string failureReasonId)
            {
                failureReasonId = null;
                long before = EconomyManager.Money;
                if (delta > 0 && before > long.MaxValue - delta)
                {
                    failureReasonId = "cash_bet_overflow";
                    return false;
                }
                _slot = SavesSystem.CurrentSlot;
                _staging = true;
                try
                {
                    // Store 只排队；钱真实变动前，BeforeCollectSaveData 拒绝把这份账本单独落盘
                    if (!_store.Store(candidate))
                    {
                        failureReasonId = "cash_bet_store_failed";
                        return false;
                    }
                    try
                    {
                        if (delta < 0) EconomyManager.Pay(new Cost(-delta), true, false);
                        else if (delta > 0) EconomyManager.Add(delta);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[ModeH] 押钱动钱通知异常: " + e.Message);
                    }
                    if (EconomyManager.Money != before + delta)
                    {
                        _store.Store(previous);
                        failureReasonId = "cash_bet_money_unchanged";
                        return false;
                    }
                    _cashSnapshotRequired |= delta != 0;
                }
                finally
                {
                    _staging = false;
                }
                string error;
                if (!_coordinator.RequestFlush(out error, true))
                {
                    // 物理落盘没成：账本与钱仍在内存里同批待写，下一次存档一起落（BeforeCollectSaveData）
                    ModBehaviour.DevLog("[ModeH] 押钱账本落盘顺延: " + (error ?? "unknown"));
                }
                return true;
            }

            private bool CanMoveMoney(out string failureReasonId)
            {
                failureReasonId = null;
                if (_staging || SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0)
                {
                    failureReasonId = "cash_bet_save_busy";
                    return false;
                }
                if (EconomyManager.Instance == null)
                {
                    failureReasonId = "cash_bet_wallet_missing";
                    return false;
                }
                _store.LoadOrInit();
                if (_store.HasWriteBarrier || _store.IsStoreFaulted)
                {
                    failureReasonId = "cash_bet_store_faulted";
                    return false;
                }
                return true;
            }

            internal void Shutdown()
            {
                _coordinator.TryFlushOnHostDestroy();
                _store.ShutdownSubscription();
            }

            private void OnSlotChanged()
            {
                _slot = -1;
                _cashSnapshotRequired = false;
                _itemSnapshotRequired = false;
                _nextItemRetry = 0f;
                ModeHItemBetStake.ResetStaticCaches();
                if (_coordinator != null) _coordinator.NotifySlotChanged();
            }

            private bool CollectCash()
            {
                if (_staging) return false;
                if (_itemSnapshotRequired && !ModeHItemBetStake.CollectPlayerSnapshot(_slot)) return false;
                if (!_cashSnapshotRequired) return true;
                if (SavesSystem.IsSaving || SavesSystem.CurrentSlot != _slot || EconomyManager.Instance == null) return false;
                SavesSystem.Save<EconomyManager.SaveData>("EconomyData", (EconomyManager.SaveData)EconomyManager.Instance.GenerateSaveData());
                return true;
            }

            public string LogPrefix { get { return "[ModeH] "; } }
            public bool HasPendingWrite { get { return _store.HasPendingWrite; } }
            public bool IsStoreFaulted { get { return _store.IsStoreFaulted; } }
            public bool HasSnapshotObligation { get { return _cashSnapshotRequired || _itemSnapshotRequired; } }
            public string LastError { get { return _store.LastError; } }
            public bool CollectSnapshot(out string error) { bool ok = CollectCash(); error = ok ? null : "cash_snapshot_unavailable"; return ok; }
            public bool FlushPending() { return _store.FlushPending(); }
            public void OnPhysicalSaveSucceeded() { _cashSnapshotRequired = false; _itemSnapshotRequired = false; }

            private static int ReadSchema(string json)
            {
                BossRushJsonValue root; string error; int schema;
                return BossRushJsonParser.TryParse(json, out root, out error) && root.TryGetInt("schemaVersion", out schema) ? schema : -1;
            }

            private static int ReadCompatibleSchema(string json)
            {
                int version = ReadSchema(json);
                // v1 的新增字段都有安全默认值；只在下次正常写入时升级。高版本仍由共享 store 拒写。
                return version == 1 ? 2 : version;
            }

            private static ModeHCashBetRecord Decode(string json)
            {
                BossRushJsonValue root; string error;
                if (!BossRushJsonParser.TryParse(json, out root, out error) || ReadCompatibleSchema(json) != 2) return null;
                ModeHCashBetRecord record = new ModeHCashBetRecord();
                string runId;
                if (root.TryGetString("runId", out runId)) record.runId = runId ?? string.Empty;
                int value;
                if (root.TryGetInt("matchIndex", out value)) record.matchIndex = value;
                if (root.TryGetInt("odds", out value)) record.odds = value;
                if (root.TryGetInt("status", out value)) record.status = value;
                long big;
                if (TryGetLong(root, "amount", out big)) record.amount = big;
                if (TryGetLong(root, "payout", out big)) record.payout = big;
                if (TryGetLong(root, "charged", out big)) record.charged = big;
                if (TryGetLong(root, "prizeCash", out big)) record.prizeCash = big;
                if (TryGetLong(root, "missingValue", out big)) record.missingValue = big;
                if (root.TryGetInt("itemSettlement", out value)) record.itemSettlement = value;
                string pending;
                if (root.TryGetString("pendingItems", out pending)) record.pendingItems = pending ?? string.Empty;
                string prizes;
                if (root.TryGetString("prizes", out prizes)) record.prizes = prizes ?? string.Empty;
                if (root.TryGetInt("kind", out value)) record.kind = value;
                string items;
                if (root.TryGetString("items", out items)) record.items = items ?? string.Empty;
                if (record.status < StatusNone || record.status > StatusRefunded || record.amount < 0
                    || record.kind < KindCash || record.kind > KindItems
                    || record.itemSettlement < 0 || record.itemSettlement > 2 || record.missingValue < 0) return null;
                if (!string.IsNullOrEmpty(record.pendingItems)
                    && (record.kind != KindItems || record.status != StatusReserved || record.itemSettlement == 0
                        || ModeHItemBetEntry.Decode(record.pendingItems).Count != record.pendingItems.Split(';').Length)) return null;
                ReadTier(root, "tierBets", record.tierBets);
                ReadTier(root, "tierWins", record.tierWins);
                return record;
            }

            private static bool TryGetLong(BossRushJsonValue root, string key, out long value)
            {
                value = 0;
                string text;
                if (!root.TryGetString(key, out text)) return false;
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            private static void ReadTier(BossRushJsonValue root, string key, long[] target)
            {
                string text;
                if (!root.TryGetString(key, out text) || string.IsNullOrEmpty(text)) return;
                string[] parts = text.Split(',');
                for (int i = 0; i < parts.Length && i < target.Length; i++)
                {
                    long v;
                    if (long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 0) target[i] = v;
                }
            }

            private static string Encode(ModeHCashBetRecord record)
            {
                // 金额与统计写成字符串：共享 JSON 解析器的整数读取是 int，押注统计与金额要 long
                StringBuilder sb = new StringBuilder("{\"schemaVersion\":2,\"runId\":\"");
                SimpleJsonHelper.EscapeString(sb, record.runId ?? string.Empty);
                sb.Append("\",\"matchIndex\":").Append(record.matchIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"odds\":").Append(record.odds.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"status\":").Append(record.status.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"amount\":\"").Append(record.amount.ToString(CultureInfo.InvariantCulture));
                sb.Append("\",\"payout\":\"").Append(record.payout.ToString(CultureInfo.InvariantCulture));
                sb.Append("\",\"charged\":\"").Append(record.charged.ToString(CultureInfo.InvariantCulture));
                sb.Append("\",\"kind\":").Append(record.kind.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"items\":\"");
                SimpleJsonHelper.EscapeString(sb, record.items ?? string.Empty);
                sb.Append("\",\"prizeCash\":\"").Append(record.prizeCash.ToString(CultureInfo.InvariantCulture));
                sb.Append("\",\"prizes\":\"");
                SimpleJsonHelper.EscapeString(sb, record.prizes ?? string.Empty);
                sb.Append("\",\"itemSettlement\":").Append(record.itemSettlement.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"missingValue\":\"").Append(record.missingValue.ToString(CultureInfo.InvariantCulture));
                sb.Append("\",\"pendingItems\":\"");
                SimpleJsonHelper.EscapeString(sb, record.pendingItems ?? string.Empty);
                sb.Append("\",\"tierBets\":\"").Append(JoinTier(record.tierBets));
                sb.Append("\",\"tierWins\":\"").Append(JoinTier(record.tierWins));
                return sb.Append("\"}").ToString();
            }

            private static string JoinTier(long[] values)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }
    }
}
