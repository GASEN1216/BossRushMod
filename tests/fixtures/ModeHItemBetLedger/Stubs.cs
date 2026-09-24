// 宿主替身：钱包、日志、本地化与账本的存储 / 落盘。账本方法本身（押物品记账、结算、退回）与赔付公式是从生产逐字抽取的。
using System;

namespace BossRush
{
    internal static class EconomyManager
    {
        public static long Money;
    }

    internal sealed class ModBehaviour
    {
        public static void DevLog(string message) { }
    }

    internal static class L10n
    {
        public static string T(string zh, string en) { return zh; }
    }

    internal static partial class ModeHCashBetService
    {
        /// <summary>校准统计（生产里是账本 Current 的只读副本）。</summary>
        internal static ModeHCashBetRecord Stats;

        internal static ModeHCashBetRecord Current
        {
            get { return Stats; }
        }

        internal sealed partial class CashBetJournal
        {
            internal sealed class MemoryStore
            {
                public ModeHCashBetRecord Current = new ModeHCashBetRecord();
            }

            internal readonly MemoryStore _store = new MemoryStore();
            internal bool AllowMoney = true;
            internal int Commits;
            internal long LastDelta;

            private bool CanMoveMoney(out string failureReasonId)
            {
                failureReasonId = AllowMoney ? null : "cash_bet_save_busy";
                return AllowMoney;
            }

            /// <summary>替身 Commit：记下这一笔动了多少钱、换上新账本，并真的改钱包（生产的顺序与撤回由守卫钉住）。</summary>
            private bool Commit(ModeHCashBetRecord previous, ModeHCashBetRecord candidate, long delta, out string failureReasonId)
            {
                failureReasonId = null;
                Commits++;
                LastDelta = delta;
                _store.Current = candidate;
                EconomyManager.Money += delta;
                return true;
            }
        }
    }
}
