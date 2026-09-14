// ============================================================================
// ZombieModeEntryDebt.cs - 丧尸模式入场回滚的「欠玩家」账本（CR-2026-09-11-019）
// ============================================================================
// 为什么需要它：
//   入场时先扣尸潮邀请函、再扣现金，任一后续步骤失败就要把两样都退回去。
//   但退款发生在**切图途中**：`EconomyManager.Instance` 是场景里的 MonoBehaviour，
//   卸场景那一刻它已经被销毁，`EconomyManager.Add` 会返回 false（反编译源
//   `Duckov/Economy/EconomyManager.cs`：`Instance == null` 时直接 `return false`）。
//   邀请函那边同理：`ItemAssetsCollection.InstantiateSync` 在资源未就绪时返回 null。
//   修复前这两处都把返回值丢掉、还在 `finally` 里清掉事务状态，于是玩家花掉的
//   入场费和邀请函**永久蒸发**，而且没有任何地方记得欠了他。
//
// 口径：
//   - **落存档**。只靠当前对象重试不够：对象随切图一起销毁，玩家也可能直接退出游戏。
//     账本写在两个独立的原始类型 key 上（`SavesSystem` 天然按槽位隔离）。
//   - **SCHEMA+**。老档没有这两个 key 就是「不欠」，读出 0；不动任何既有 key。
//   - **不缓存**。只在入场回滚与经济加载完成这两个低频点读写，因此不需要缓存，
//     也就没有「缓存跨槽位串台」这类问题（对照 `BossRushSlotJsonStore` 的槽位烙印）。
//   - **先送达再销账**（与 `SkyIslandBounty.TryClaim`、日报里程碑同一条纪律）：
//     钱/物先真的到手，回读核对通过才把账销掉。极端情况下宁可重发，也不吞玩家的东西。
//   - **全程 no-throw**。存档与经济路径的异常一律吞掉并记日志，不拖崩宿主。
//
// 何时结账：
//   1. 官方 `EconomyManager.OnEconomyManagerLoaded`（`Awake` 与切换存档文件时都会触发，
//      此刻 `Instance` 已经赋值）——这是「经济回来了」最准确的信号；
//   2. 每次准备进丧尸模式之前顺手结一次，保证老欠账不会一直挂着。
//
// 由 tests/ZombieModeEntryDebtGuard.py 守卫。
// ============================================================================

using System;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    /// <summary>入场回滚欠下的现金与邀请函。发布后两个 key 冻结。</summary>
    internal static class ZombieModeEntryDebt
    {
        /// <summary>欠玩家的现金总额（long）。</summary>
        internal const string CashKey = "BossRush_ZombieMode_RefundDebt_Cash";

        /// <summary>欠玩家的尸潮邀请函张数（int）。</summary>
        internal const string InvitationKey = "BossRush_ZombieMode_RefundDebt_Invitations";

        private const string LogPrefix = "[ZombieMode] ";

        /// <summary>幂等订阅标志（AGENTS 4.6：静态事件必须防重复订阅并成对退订）。</summary>
        private static bool subscribed;

        #region 订阅

        /// <summary>订阅官方经济加载完成事件。幂等；由 ZombieMode 运行时模块的启动路径调用。</summary>
        internal static void Attach()
        {
            if (subscribed) return;
            try
            {
                EconomyManager.OnEconomyManagerLoaded += OnEconomyLoaded;
                subscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 欠账账本订阅失败: " + e.Message);
            }
        }

        /// <summary>退订。幂等；由运行时模块销毁路径调用。</summary>
        internal static void Detach()
        {
            if (!subscribed) return;
            try
            {
                EconomyManager.OnEconomyManagerLoaded -= OnEconomyLoaded;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 欠账账本退订失败: " + e.Message);
            }
            finally
            {
                subscribed = false;
            }
        }

        /// <summary>命名方法，不用 lambda：lambda 退订退不掉（AGENTS 4.6）。</summary>
        private static void OnEconomyLoaded()
        {
            TrySettleAll();
        }

        #endregion

        #region 读写

        /// <summary>读欠下的现金；读不到、负数或异常一律按 0。</summary>
        internal static long ReadCash()
        {
            try
            {
                if (!SavesSystem.KeyExisits(CashKey)) return 0L;
                long value = SavesSystem.Load<long>(CashKey);
                return value > 0L ? value : 0L;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 读现金欠账失败: " + e.Message);
                return 0L;
            }
        }

        /// <summary>读欠下的邀请函张数；读不到、负数或异常一律按 0。</summary>
        internal static int ReadInvitations()
        {
            try
            {
                if (!SavesSystem.KeyExisits(InvitationKey)) return 0;
                int value = SavesSystem.Load<int>(InvitationKey);
                return value > 0 ? value : 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 读邀请函欠账失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 写入并**回读核对**。`SavesSystem.Save` 在没有当前存档文件时只打一行日志就返回，
        /// 不回读就会把「写失败」当成「已记账」，那正是这条 finding 要杜绝的情形。
        /// </summary>
        private static bool WriteCash(long amount)
        {
            try
            {
                SavesSystem.Save<long>(CashKey, amount);
                return SavesSystem.Load<long>(CashKey) == amount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 写现金欠账失败: " + e.Message);
                return false;
            }
        }

        private static bool WriteInvitations(int count)
        {
            try
            {
                SavesSystem.Save<int>(InvitationKey, count);
                return SavesSystem.Load<int>(InvitationKey) == count;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 写邀请函欠账失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 记账

        /// <summary>
        /// 记下一笔退不出去的现金。返回 false 表示**连账都没记下**，
        /// 调用方必须保留自己的事务状态，等下一次清理路径再试。
        /// </summary>
        internal static bool OweCash(long amount)
        {
            if (amount <= 0L) return true;
            long total = ReadCash() + amount;
            if (!WriteCash(total)) return false;
            ModBehaviour.DevLog(LogPrefix + "入场现金退款失败，已记入欠账: +" + amount + "，合计 " + total);
            return true;
        }

        /// <summary>记下一张退不出去的邀请函。返回 false 表示连账都没记下。</summary>
        internal static bool OweInvitation(int count)
        {
            if (count <= 0) return true;
            int total = ReadInvitations() + count;
            if (!WriteInvitations(total)) return false;
            ModBehaviour.DevLog(LogPrefix + "入场邀请函返还失败，已记入欠账: +" + count + "，合计 " + total);
            return true;
        }

        #endregion

        #region 入场回滚

        /// <summary>
        /// 入场回滚：退还已扣的入场现金。
        ///
        /// 返回 true 表示这笔账已经了结——钱退到玩家手上，或者退不出去但已经落进存档账本；
        /// 两种情况调用方都该清掉自己的事务状态（留着会和账本重复退款）。
        /// 返回 false 表示**钱没退出去、账也没记下**，调用方必须保留事务状态等下一次重试。
        /// </summary>
        internal static bool RefundCash(long amount)
        {
            if (amount <= 0L) return true;
            bool paid = false;
            try
            {
                paid = EconomyManager.Add(amount);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 退还入场现金异常: " + e.Message);
            }
            if (paid)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedCash"));
                return true;
            }
            return OweCash(amount);
        }

        /// <summary>
        /// 入场回滚：返还一张尸潮邀请函。
        ///
        /// 判据是「实例造出来了没有」：官方 <c>ItemUtilities.SendToPlayer</c> 没有返回值，
        /// 而 <c>ItemAssetsCollection.InstantiateSync</c> 在资源未就绪时返回 null。
        /// 造不出来就转进存档账本等资源就绪再补；造出来之后送达抛异常按已发放处理
        /// （实例已经存在，再发一次会凭空多一张）。返回语义同 <see cref="RefundCash"/>。
        /// </summary>
        internal static bool RefundInvitation()
        {
            Item refund = null;
            try
            {
                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                refund = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideInvitation);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 返还尸潮邀请函异常: " + e.Message);
            }
            if (refund == null) return OweInvitation(1);
            Deliver(refund);
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedInvitation"));
            return true;
        }

        /// <summary>把造好的邀请函送到玩家身上。异常按已发放处理，不重复发。</summary>
        private static void Deliver(Item refund)
        {
            try
            {
                ItemUtilities.SendToPlayer(refund, true, PlayerStorage.Inventory != null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 邀请函送达异常，按已发放处理: " + e.Message);
            }
        }

        #endregion

        #region 结账

        /// <summary>把能还的都还掉。两笔账互相独立：一笔还不上不影响另一笔。</summary>
        internal static void TrySettleAll()
        {
            TrySettleCash();
            TrySettleInvitations();
        }

        /// <summary>还现金。**先到账再销账**：`Add` 返回 true 且销账回读通过才算了结。</summary>
        internal static bool TrySettleCash()
        {
            long owed = ReadCash();
            if (owed <= 0L) return true;
            try
            {
                if (!EconomyManager.Add(owed))
                {
                    ModBehaviour.DevLog(LogPrefix + "经济实例暂不可用，现金欠账继续保留: " + owed);
                    return false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 结清现金欠账异常，账目保留: " + e.Message);
                return false;
            }

            // 钱已经到账。销账写失败只会导致极端情况下重发一次，绝不会吞掉玩家的钱。
            if (!WriteCash(0L))
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 现金欠账已发放但销账失败，可能重复发放: " + owed);
                return true;
            }
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedCash"));
            ModBehaviour.DevLog(LogPrefix + "现金欠账已结清: " + owed);
            return true;
        }

        /// <summary>
        /// 还邀请函。一次只还一张并立即销一张：官方 `ItemUtilities.SendToPlayer` 没有返回值，
        /// 逐张推进才能保证中途失败时剩下的张数还留在账上。
        /// </summary>
        internal static bool TrySettleInvitations()
        {
            int owed = ReadInvitations();
            if (owed <= 0) return true;
            while (owed > 0)
            {
                Item refund = null;
                try
                {
                    ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                    refund = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideInvitation);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 结清邀请函欠账异常，账目保留: " + e.Message);
                    return false;
                }
                if (refund == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "邀请函资源暂不可用，欠账继续保留: " + owed);
                    return false;
                }

                // 实例已经造出来了：无论送达是否抛异常都销掉这一张，不重复发。
                Deliver(refund);
                owed--;
                if (!WriteInvitations(owed))
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 邀请函欠账已发放但销账失败，可能重复发放");
                    return true;
                }
            }
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedInvitation"));
            ModBehaviour.DevLog(LogPrefix + "邀请函欠账已结清");
            return true;
        }

        #endregion

        /// <summary>静态缓存复位（宿主销毁路径）。账本本身在存档里，这里只清订阅标志。</summary>
        internal static void ResetStaticCaches()
        {
            Detach();
        }
    }
}
