// ============================================================================
// RandomEventAirdropHold.cs - E1 空投补给：翻箱保护（RandomEventAirdropSupply 的 partial 追加）
// ============================================================================
// 为什么单独成文件：
//   RandomEventCatalog.cs 已经贴着 tests/LargeFileBudgetGuard.py 的 1200 行硬预算
//   （且不在 tests/large_file_existing_allowlist.txt 里，没有豁免额度），
//   本段逻辑内联进去会直接把它顶穿（实测 1237 行，guard 转红）。
//
//   文件名**刻意不以 RandomEventCatalog 开头**：
//   tests/RandomEventsWaveIsolationGuard.py 会把 RandomEventCatalog* 全部并成一份文本，
//   再数「: RandomEventBase 子类数」与「OnCleanup 实现数」是否配平。
//   叫成 RandomEventCatalog_* 会让 partial 的类声明被多数一次，配平失败。
//   为双保险，下面的 partial 声明也不重复写基类。
//
// 本文件解决的问题：
//   空投箱由 ctx.Scope 托管，事件到时即销毁。玩家正开着战利品界面翻箱时，
//   箱子会在眼前连同没拿走的东西一起消失，界面还留在屏幕上指着一个已销毁的目标。
//
// 方案（关键：延的是「到期」，不是「清理」）：
//   RandomEventDirector.EndActiveEvent 在 evt.OnCleanup 之后**无条件**再跑一次
//   ctx.Scope.Clear，所以在 OnCleanup 里放行毫无意义。真正能延后的只有
//   TickEventActive 的到期判据 ctx.ElapsedSeconds >= ctx.DurationSeconds。
//
// 硬约束：
//   - 只有 Expired 一条路径经这里延后。RunEnded / SceneChanged / SwitchDisabled /
//     HostDestroyed / DebugForced / TriggerFailed 都直接调 EndActiveEvent，
//     绕过到期比较，照旧立刻强制销毁，不存在跨图跨局泄漏。
//   - 延期总量有硬帽 RandomEventsTuning.AirdropHoldOpenMaxSeconds。并发恒 1，
//     没有上限玩家挂着界面就能把整局随机事件钉死在 EventActive。
//   - OnTick 是每帧热路径：零分配、零日志、no-throw（AGENTS 4.7 / 4.12）。
//   - 不触碰任何波次状态机符号（tests/RandomEventsWaveIsolationGuard.py）。
// ============================================================================

using System;

namespace BossRush
{
    // partial 追加部分不重复写 ": RandomEventBase"，理由见文件头。
    internal sealed partial class RandomEventAirdropSupply
    {
        /// <summary>
        /// 箱子的本地库存引用，OnTrigger 缓存一次，只用于「战利品界面开在这一箱上」比对。
        /// 全限定而不加 using：RandomEventCatalog.cs 那半边已 using UnityEngine.UI，
        /// 两个 partial 的 using 集合不同容易让人误判，这里索性写全名，零歧义。
        /// </summary>
        private ItemStatsSystem.Inventory _crateInventory;

        /// <summary>本次事件已因开箱累计续期的秒数，上限 AirdropHoldOpenMaxSeconds。</summary>
        private float _holdExtendedSeconds;

        /// <summary>
        /// 本箱的战利品界面是否开着。由官方 OnStartLoot / OnStopLoot 驱动。
        ///
        /// 不能只看 InteractableBase.Interacting：官方在界面打开后一帧就 StopInteract()，
        /// 那之后 Interacting 恒为 false，而界面还开着——旧判据因此几乎永不命中。
        /// </summary>
        private bool _crateLootViewOpen;

        /// <summary>箱子上的「搬起」交互组件（惰性缓存）。</summary>
        private BossRushCarryInteractable _crateCarry;

        /// <summary>官方 loot 事件是否已订阅（幂等 + 成对退订，AGENTS 4.6）。</summary>
        private bool _lootEventsHooked;

        /// <summary>
        /// 玩家正开着空投箱时把到期点往后推。
        ///
        /// 【为什么延的是「到期」而不是「清理」】
        ///   RandomEventDirector.EndActiveEvent 在 evt.OnCleanup 之后**无条件**再跑一次
        ///   ctx.Scope.Clear（防事件实现漏清的兜底），而箱子是 Scope 托管对象。
        ///   所以在 OnCleanup 里放行毫无意义——唯一能延后的只有 TickEventActive 的到期判据
        ///   ctx.ElapsedSeconds >= ctx.DurationSeconds。ctx.DurationSeconds 是可变字段，
        ///   且 OnTick 在同帧的到期比较**之前**执行，改它当帧生效。
        ///
        /// 【必须有硬帽】
        ///   并发恒 1：事件停在 EventActive 期间本局不再抽下一个事件。没有上限，
        ///   玩家挂着战利品界面就能把整局随机事件卡死。用满 AirdropHoldOpenMaxSeconds 后
        ///   本方法第二个早返让计时自然追上，箱子按原逻辑到期。
        ///
        /// 【只影响 Expired 一条路径】
        ///   RunEnded / SceneChanged / SwitchDisabled / HostDestroyed / DebugForced /
        ///   TriggerFailed 都直接调 EndActiveEvent，绕过到期比较，照旧立刻强制销毁，
        ///   不存在跨图跨局泄漏。
        ///
        /// 热路径纪律：零分配、零日志；必须 no-throw——RandomEventDirector._activeTickFaulted
        /// 一旦置位就永久停调本事件的 OnTick（届时箱子按原定时间回收，只是失去保护）。
        /// </summary>
        internal override void OnTick(RandomEventContext ctx, float deltaTime)
        {
            try
            {
                if (ctx == null || _validationCrate == null) return;
                if (_holdExtendedSeconds >= RandomEventsTuning.AirdropHoldOpenMaxSeconds) return;

                // 主判据是官方 loot 事件维护的闩：界面打开后一帧官方就会 StopInteract()，
                // 此后 Interacting 恒 false 而界面仍开着，只看它等于保护从不生效。
                // Interacting 保留为次要信号（纯属性、无副作用），覆盖「正在打开」这一小段。
                // 绝不在这里读 crate.Inventory（非纯 getter）。
                // 第三种「玩家正在处理这个箱子」的形态：箱子被搬起来了。
                // 创建时 DecorateLootbox 的 includeCarryInteraction=true 给它挂了「搬起」，
                // 而宽限窗口此前只认战利品界面——玩家把箱子扛在手上时到点回收，
                // 箱子连同里面没取的东西一起在手上蒸发。
                if (!_crateLootViewOpen && !_validationCrate.Interacting && !IsCrateCarried()) return;

                float remaining = ctx.DurationSeconds - ctx.ElapsedSeconds;
                float step = RandomEventsTuning.AirdropHoldOpenGraceSeconds - remaining;
                if (step <= 0f) return;

                float budget = RandomEventsTuning.AirdropHoldOpenMaxSeconds - _holdExtendedSeconds;
                if (step > budget) step = budget;

                ctx.DurationSeconds += step;
                _holdExtendedSeconds += step;
            }
            catch (Exception)
            {
                // 每帧热路径：延期失败最坏只是箱子按原定时间回收，不刷日志（AGENTS 4.7 / 4.12）
            }
        }

        /// <summary>
        /// 箱子是否正被玩家扛着。缓存组件引用，每帧只读一个 bool。
        /// no-throw：读不到就当没扛着，退回原有的两条判据。
        /// </summary>
        private bool IsCrateCarried()
        {
            try
            {
                if (_validationCrate == null) return false;
                if (_crateCarry == null)
                {
                    _crateCarry = _validationCrate.GetComponent<BossRushCarryInteractable>();
                    if (_crateCarry == null) return false;
                }
                return _crateCarry.IsCarrying;
            }
            catch (Exception)
            {
                // 每帧热路径，不刷日志（AGENTS 4.7 / 4.12）
                return false;
            }
        }

        /// <summary>
        /// 缓存箱子的本地库存引用，并把续期额度归零。OnTrigger 在箱子建好后调一次。
        ///
        /// 单独收口而不是写在 OnTrigger 里：InteractableLootbox.Inventory **不是纯 getter**
        /// （惰性建仓 + SetMarkerUsed + 写 DisplayNameKey，失败还会 PopText + Debug.LogError），
        /// 只能在这一个可控时点读，OnTick 每帧只比缓存下来的引用。
        /// no-throw：读不到库存只影响关界面时的归属判定，箱子本身照常工作。
        /// </summary>
        private void PrimeAirdropHoldState(InteractableLootbox crate)
        {
            _crateInventory = null;
            _crateCarry = null;
            _holdExtendedSeconds = 0f;
            _crateLootViewOpen = false;
            HookLootEvents();

            try
            {
                if (crate != null)
                {
                    _crateInventory = crate.Inventory;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "[WARNING] 空投箱库存缓存失败，开箱保留将只靠 Interacting 判定: " + e.Message);
            }
        }

        /// <summary>清空翻箱保护状态。OnCleanup 调用；事件实例被目录复用，必须成对归零。</summary>
        private void ResetAirdropHoldState()
        {
            UnhookLootEvents();
            _crateLootViewOpen = false;
            _crateInventory = null;
            _crateCarry = null;
            _holdExtendedSeconds = 0f;
        }

        /// <summary>订阅官方 loot 事件。幂等：重复调用只订阅一次。</summary>
        private void HookLootEvents()
        {
            if (_lootEventsHooked) return;
            try
            {
                InteractableLootbox.OnStartLoot += HandleLootStarted;
                InteractableLootbox.OnStopLoot += HandleLootStopped;
                _lootEventsHooked = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "[WARNING] 订阅开箱事件失败，翻箱保护将只靠 Interacting 判定: " + e.Message);
            }
        }

        /// <summary>退订官方 loot 事件。事件实例被目录复用，必须与 Hook 成对。</summary>
        private void UnhookLootEvents()
        {
            if (!_lootEventsHooked) return;
            _lootEventsHooked = false;
            try
            {
                InteractableLootbox.OnStartLoot -= HandleLootStarted;
                InteractableLootbox.OnStopLoot -= HandleLootStopped;
            }
            catch (Exception)
            {
                // 退订失败不阻断清理：闩已复位，最坏是多一次无害回调。
            }
        }

        private void HandleLootStarted(InteractableLootbox box)
        {
            try
            {
                if (box != null && ReferenceEquals(box, _validationCrate)) _crateLootViewOpen = true;
            }
            catch (Exception e)
            {
                // 官方静态事件的回调必须 no-throw，否则会打断同一事件的其他订阅方。
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "[WARNING] 开箱事件回调异常: " + e.Message);
            }
        }

        private void HandleLootStopped(InteractableLootbox box)
        {
            try
            {
                if (box != null && ReferenceEquals(box, _validationCrate)) _crateLootViewOpen = false;
            }
            catch (Exception e)
            {
                // 官方静态事件的回调必须 no-throw，否则会打断同一事件的其他订阅方。
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "[WARNING] 开箱事件回调异常: " + e.Message);
            }
        }

        /// <summary>
        /// 销毁箱子前先把开在它身上的战利品界面关掉，避免界面留在屏幕上指着一个已销毁的目标。
        /// 幂等、no-throw，只在事件结束时调用一次，不在每帧路径上。
        /// </summary>
        private void CloseAirdropLootViewIfOpen()
        {
            try
            {
                InteractableLootbox crate = _validationCrate;
                if (crate == null) return;

                // 官方路径优先：StopInteract 同步走到 OnInteractStop -> InteractableLootbox.OnStopLoot
                // -> LootView.OnStopLoot -> Close()，顺带把 interactCharacter 清掉。
                if (crate.Interacting)
                {
                    crate.StopInteract();
                }

                // 兜底：官方联动没生效时手动关，且只关「确实开在这一箱上」的界面
                //（玩家可能正开着别的容器）。形态照 DespawnRandomEventMerchant 的 StockShopView 收尾。
                Duckov.UI.LootView view = Duckov.UI.LootView.Instance;
                if (view != null && view.open && _crateInventory != null
                    && view.TargetInventory == _crateInventory)
                {
                    try
                    {
                        view.Close();
                    }
                    catch (Exception)
                    {
                        try { view.gameObject.SetActive(false); }
                        catch (Exception)
                        {
                            // 界面对象本身已被销毁，无需处理
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "[WARNING] 关闭空投箱战利品界面失败: " + e.Message);
            }
        }
    }
}
