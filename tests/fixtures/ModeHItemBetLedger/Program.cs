// 鸭王杯押钱 / 押背包物品账本的执行回归（2026-09-24）。
// 生产逐字抽取：ModeHCashBetRecord、ModeHItemBetEntry、ComputePayout、ResolveAssumedWinPermille、
// 账本的 TryReserveItems / TrySettle / TryRefund 与 ModeHConfig 的赔付常量；钱包与存储是替身。
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class Program
    {
        private static int _failures;

        private static void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine("FAIL " + what);
            _failures++;
        }

        private static ModeHCashBetService.CashBetJournal Journal(ModeHCashBetRecord current)
        {
            ModeHCashBetService.CashBetJournal journal = new ModeHCashBetService.CashBetJournal();
            if (current != null) journal._store.Current = current;
            return journal;
        }

        private static ModeHCashBetRecord Reserved(int kind, long amount, int odds)
        {
            return new ModeHCashBetRecord
            {
                runId = "run", matchIndex = 2, amount = amount, odds = odds,
                status = ModeHCashBetService.StatusReserved, kind = kind,
            };
        }

        public static int Main()
        {
            ModeHCashBetService.Stats = new ModeHCashBetRecord();

            // 1. 押了哪几件：来回编码不丢数，名字里的分隔符被去掉，坏条目跳过
            List<ModeHItemBetEntry> entries = new List<ModeHItemBetEntry>
            {
                new ModeHItemBetEntry { TypeId = 101, Count = 3, Value = 1200, Quality = 2, Name = "弹|药;盒" },
                new ModeHItemBetEntry { TypeId = 202, Count = 1, Value = 4500, Quality = 5, Name = "Rifle" },
            };
            string encoded = ModeHItemBetEntry.Encode(entries);
            List<ModeHItemBetEntry> decoded = ModeHItemBetEntry.Decode(encoded);
            Check(decoded.Count == 2 && decoded[0].TypeId == 101 && decoded[0].Count == 3 && decoded[0].Value == 1200
                  && decoded[0].Quality == 2 && decoded[1].TypeId == 202 && decoded[1].Value == 4500 && decoded[1].Quality == 5,
                "item entries round-trip typeId / count / value / quality");
            Check(decoded[0].Name.IndexOf('|') < 0 && decoded[0].Name.IndexOf(';') < 0, "separators are stripped from names");
            Check(ModeHItemBetEntry.Decode("bad;1|0|5|1|x;2|1|-3|1|y;3|2|7|4|ok").Count == 1, "malformed / zero-count / negative entries are skipped");
            Check(ModeHItemBetEntry.Describe(decoded).Contains("×3"), "stack count shows in the description");

            // 1b. 奖品的品质跟押上的东西走（按估值加权、四舍五入、夹在官方品质范围里），件数跟押上的件数走（有上限）
            // (1200×2 + 4500×5) / 5700 = 4.37 → 4
            Check(ModeHItemBetEntry.PrizeQuality(decoded) == 4, "prize quality is the value-weighted stake quality");
            Check(ModeHItemBetEntry.PrizeQuality(new List<ModeHItemBetEntry>
                {
                    new ModeHItemBetEntry { Value = 100, Quality = 1 },
                    new ModeHItemBetEntry { Value = 100000, Quality = 7 },
                }) == 7, "one pricey high-quality item drives the prize quality");
            Check(ModeHItemBetEntry.PrizeQuality(new List<ModeHItemBetEntry> { new ModeHItemBetEntry { Value = 10, Quality = 99 } })
                  == ModeHConfig.MaxGameQuality, "prize quality is clamped to the official range");
            Check(ModeHItemBetEntry.PrizeQuality(new List<ModeHItemBetEntry> { new ModeHItemBetEntry { Value = 10, Quality = 0 } })
                  == ModeHConfig.MinGameQuality, "quality-0 stakes still map to the lowest official quality");
            Check(ModeHItemBetEntry.PrizeSlots(decoded) == 2, "one prize per staked item");
            List<ModeHItemBetEntry> many = new List<ModeHItemBetEntry>();
            for (int i = 0; i < 30; i++) many.Add(new ModeHItemBetEntry { Value = 10, Quality = 1 });
            Check(ModeHItemBetEntry.PrizeSlots(many) == ModeHConfig.ItemBetMaxPrizeItems, "prize count is capped however many items are staked");

            // 2. 按表算：押钱与押物品的期望都严格为负（押物品输了失去的是估值那么多的东西）
            for (int odds = ModeHConfig.MinOdds; odds <= ModeHConfig.MaxOdds; odds++)
            {
                long[] stakes = { 1000L, 5000L, 20000L, 50000L, 20000000L };
                for (int i = 0; i < stakes.Length; i++)
                {
                    long stake = stakes[i];
                    long payout = ModeHCashBetService.ComputePayout(stake, odds);
                    int p = ModeHConfig.CashBetAssumedWinPermilleByOdds[odds];
                    Check(payout >= stake, "x" + odds + " stake " + stake + " win pays back at least the stake");
                    // 押钱：p × 赔付 − 押金 < 0；押物品：p × (赔付 − 估值) − (1 − p) × 估值 = 同一个数
                    Check(payout * p < stake * 1000L, "x" + odds + " stake " + stake + " has negative expectation");
                }
            }

            // 3. 校准只让赔付变少：实际胜率高于表时赔付下调，低于表时不加赔
            long baseline = ModeHCashBetService.ComputePayout(10000, 3);
            ModeHCashBetRecord hot = new ModeHCashBetRecord();
            hot.tierBets[3] = ModeHConfig.CashBetCalibrationMinSamples;
            hot.tierWins[3] = ModeHConfig.CashBetCalibrationMinSamples - 1;
            ModeHCashBetService.Stats = hot;
            Check(ModeHCashBetService.ComputePayout(10000, 3) < baseline, "a tier that wins more than the table pays less");
            ModeHCashBetRecord cold = new ModeHCashBetRecord();
            cold.tierBets[3] = ModeHConfig.CashBetCalibrationMinSamples;
            cold.tierWins[3] = 0;
            ModeHCashBetService.Stats = cold;
            Check(ModeHCashBetService.ComputePayout(10000, 3) == baseline, "a tier that loses more than the table never pays more");
            ModeHCashBetService.Stats = new ModeHCashBetRecord();

            // 4. 押物品记账不动钱；上一笔没结清不能再押
            EconomyManager.Money = 7000;
            ModeHCashBetService.CashBetJournal journal = Journal(null);
            string failure;
            Check(journal.TryReserveItems("run", 2, 3, 5000, encoded, out failure) && journal.LastDelta == 0
                  && EconomyManager.Money == 7000 && journal._store.Current.kind == ModeHCashBetService.KindItems
                  && journal._store.Current.status == ModeHCashBetService.StatusReserved,
                "reserving an item bet records it without touching money");
            Check(!journal.TryReserveItems("run", 3, 3, 5000, encoded, out failure) && failure == "cash_bet_previous_unsettled",
                "an unsettled bet blocks the next item bet");

            // 5. 押物品赢了：东西留着，奖品另发；凑不满的折成钱，钱不超过「赔付 − 估值」
            long payoutX3 = ModeHCashBetService.ComputePayout(5000, 3);
            long got;
            Check(journal.TrySettle("run", 2, true, 0, 700, "奖品A、奖品B", out got) && got == payoutX3 && journal.LastDelta == 700
                  && EconomyManager.Money == 7700 && journal._store.Current.status == ModeHCashBetService.StatusSettled
                  && journal._store.Current.prizes == "奖品A、奖品B" && journal._store.Current.prizeCash == 700,
                "an item bet win pays only the prize remainder in money and records the prizes");
            int commits = journal.Commits;
            Check(!journal.TrySettle("run", 2, true, 0, 700, "x", out got) && journal.Commits == commits, "a settled bet is never paid twice");
            journal = Journal(Reserved(ModeHCashBetService.KindItems, 5000, 3));
            Check(journal.TrySettle("run", 2, true, 0, 999999, string.Empty, out got) && journal.LastDelta == payoutX3 - 5000,
                "the money part of an item win never exceeds payout minus value");

            // 6. 押物品输了：东西都在就不动钱；找不到的按估值扣，扣到余额为止
            journal = Journal(Reserved(ModeHCashBetService.KindItems, 5000, 3));
            EconomyManager.Money = 900;
            Check(journal.TrySettle("run", 2, false, 0, 0, string.Empty, out got) && journal.LastDelta == 0 && EconomyManager.Money == 900
                  && journal._store.Current.charged == 0, "an item bet loss with every item present moves no money");
            journal = Journal(Reserved(ModeHCashBetService.KindItems, 5000, 3));
            EconomyManager.Money = 900;
            Check(journal.TrySettle("run", 2, false, 3000, 0, string.Empty, out got) && journal.LastDelta == -900 && EconomyManager.Money == 0
                  && journal._store.Current.charged == 900, "missing items are charged at value, but never below zero");
            Check(journal._store.Current.tierBets[3] == 1 && journal._store.Current.tierWins[3] == 0, "the loss is counted in the tier stats");

            // 7. 押钱：赢了发含押金的赔付，输了不动钱
            journal = Journal(Reserved(ModeHCashBetService.KindCash, 5000, 3));
            EconomyManager.Money = 0;
            Check(journal.TrySettle("run", 2, true, 999, 999, "x", out got) && journal.LastDelta == payoutX3
                  && journal._store.Current.prizes == string.Empty && journal._store.Current.prizeCash == 0,
                "a money bet win pays the full payout and ignores the item-only arguments");
            journal = Journal(Reserved(ModeHCashBetService.KindCash, 5000, 3));
            Check(journal.TrySettle("run", 2, false, 999, 0, string.Empty, out got) && journal.LastDelta == 0, "a money bet loss moves no more money");

            // 8. 只结算对得上的那一笔
            journal = Journal(Reserved(ModeHCashBetService.KindCash, 5000, 3));
            Check(!journal.TrySettle("other", 2, true, 0, 0, string.Empty, out got) && !journal.TrySettle("run", 3, true, 0, 0, string.Empty, out got)
                  && journal.Commits == 0, "settling needs the same season and match");

            // 9. 退回：押钱原样退钱，押物品不动钱（东西本来就在背包里）
            long refunded;
            journal = Journal(Reserved(ModeHCashBetService.KindCash, 5000, 3));
            Check(journal.TryRefund("test", out refunded) && refunded == 5000 && journal.LastDelta == 5000, "a money bet refund returns the stake");
            journal = Journal(Reserved(ModeHCashBetService.KindItems, 5000, 3));
            Check(journal.TryRefund("test", out refunded) && journal.LastDelta == 0
                  && journal._store.Current.status == ModeHCashBetService.StatusRefunded, "an item bet refund moves no money");
            Check(!journal.TryRefund("test", out refunded), "a refunded bet is never refunded twice");

            // 10. 存档正忙时一分钱都不动
            journal = Journal(Reserved(ModeHCashBetService.KindItems, 5000, 3));
            journal.AllowMoney = false;
            Check(!journal.TrySettle("run", 2, false, 3000, 0, string.Empty, out got) && journal.Commits == 0
                  && journal._store.Current.status == ModeHCashBetService.StatusReserved, "a busy save leaves the bet reserved");

            Console.WriteLine(_failures == 0 ? "ModeHItemBetLedger: PASS" : "ModeHItemBetLedger: FAIL (" + _failures + ")");
            return _failures == 0 ? 0 : 1;
        }
    }
}
