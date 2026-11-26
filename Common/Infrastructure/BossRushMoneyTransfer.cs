// BossRushMoneyTransfer.cs - Mod 发钱 / 收钱的到账判定
//
// 官方 EconomyManager.Add（反编译源 Duckov/Economy/EconomyManager.cs）：Instance 为空时返回 false、不动钱；
// 否则先改余额，再同步派发 OnMoneyChanged。订阅者（包括其它 Mod，或给 Add 打补丁的收益倍率 Mod）
// 可能在通知里顺手再改一次钱，也可能直接抛异常。这两种情况下这笔钱都已经到账。
//
// 旧写法在 Add 之后要求余额「恰好增加 amount」：净变化一不同就判失败、不记账，玩家再点一次就再发一笔。
// 2026-11-26 玩家反馈「云上的坐标」交不了、提示奖金发不出去，就是这条路径。
//
// 到账判定：官方返回 true；或者这次调用里余额（扣钱时连同背包现金）确实变了。后一条兜住两种情况：
// 通知里抛异常（钱已改完才派发通知）；别的 Mod 给 Add / Pay 打补丁后返回值与实际不符，钱动了却返回 false
// （鸭王杯夹具 SaveFailureRecovery 的 RejectAfterPay 用例）。只要钱动了就必须记账，否则下一次会重复动钱。
using System;
using Duckov.Economy;

namespace BossRush
{
    internal static class BossRushMoneyTransfer
    {
        /// <summary>
        /// 按官方有符号 Add 动账户余额。返回这笔钱是否已经动到账户上（delta 为 0 视为成功）。
        /// 调用方先自行做溢出检查；这里不要求余额净变化恰好等于 delta。
        /// </summary>
        internal static bool TryAdd(long delta, string logTag)
        {
            if (delta == 0) return true;
            long before = EconomyManager.Money;
            try
            {
                return EconomyManager.Add(delta) || Moved(before, 0L, false);
            }
            catch (Exception e)
            {
                bool moved = Moved(before, 0L, false);
                ModBehaviour.DevLog("[" + logTag + "] 动钱通知异常，已到账=" + moved + ": " + e.Message);
                return moved;
            }
        }

        /// <summary>
        /// 按官方 Pay(Cost) 扣一笔纯现金。返回这笔钱是否已经扣掉（amount 为 0 视为成功）。
        /// </summary>
        internal static bool TryDebit(long amount, bool accountAvailable, bool cashAvailable, string logTag)
        {
            if (amount == 0) return true;
            if (amount < 0) return false;
            long beforeMoney = EconomyManager.Money;
            long beforeCash = cashAvailable ? EconomyManager.Cash : 0L;
            try
            {
                return EconomyManager.Pay(new Cost(amount), accountAvailable, cashAvailable)
                    || Moved(beforeMoney, beforeCash, cashAvailable);
            }
            catch (Exception e)
            {
                bool moved = Moved(beforeMoney, beforeCash, cashAvailable);
                ModBehaviour.DevLog("[" + logTag + "] 扣钱通知异常，已扣=" + moved + ": " + e.Message);
                return moved;
            }
        }

        private static bool Moved(long beforeMoney, long beforeCash, bool includeCash)
        {
            return EconomyManager.Instance != null
                && (EconomyManager.Money != beforeMoney || (includeCash && EconomyManager.Cash != beforeCash));
        }
    }
}
