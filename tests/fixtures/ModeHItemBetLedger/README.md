# ModeHItemBetLedger

鸭王杯押钱 / 押背包物品账本的执行回归（2026-09-24）。

- **真实生产逻辑**（`run.py` 从 `ModeH/ModeHCashBetService.cs`、`ModeH/ModeHConfig.cs` 逐字抽取）：`ModeHCashBetRecord`、`ModeHItemBetEntry`（押了哪几件的编码 / 解码 / 描述）、`ComputePayout`、`ResolveAssumedWinPermille`、账本的 `TryReserveItems` / `TrySettle` / `TryRefund`，以及赔付相关的常量。
- **宿主替身**（`Stubs.cs`）：钱包 `EconomyManager.Money`、账本存储（内存里的一条记录）、`CanMoveMoney`（可置为存档正忙）、`Commit`（记下动了多少钱并改钱包）、`DevLog`、`L10n`。生产 `Commit` 的「先排账本、再动钱、钱没变就撤回、同批落盘」顺序由 `tests/ModeHCashBetGuard.py` 钉住，不在这里重复。
- 核对：编码来回不丢数（含品质）；奖品品质按估值加权、夹在官方范围、件数跟押上件数走且有上限；按表算押钱与押物品期望都为负（含 2,000 万这种大额）；校准只降不升；押物品记账不动钱、赢了只把奖品凑不满的零头发成钱且不超过「赔付 − 估值」、输了找不到的按估值扣到 0 为止；押钱赢输；只结算对得上的一笔、不重复结算；退回押钱退钱、押物品不动钱；存档正忙时不动钱。
- 物品侧（`ModeHItemBetStake`：列候选、收走、认领、挑奖品、实例化与发奖）要真实的官方 `Item` 与物品表，离线造不出来，只有 `ModeHIsolationGuard.check_item_bet_stake` 与 `ModeHCashBetGuard` 的 L1 守卫。

只用聚合入口跑：`python tools/run_runtime_regressions.py --filter ModeHItemBetLedger`。
