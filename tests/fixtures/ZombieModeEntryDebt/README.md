# ZombieModeEntryDebt

丧尸入场退款与欠账的 L2 执行回归，覆盖 CR-2026-09-11-019 及 2026-09-25 邀请函送达分支修复。

- 真实生产逻辑：直接链接 `ZombieModeEntryDebt.cs`，执行读写回读、退款、逐张结账、实物回执判断以及订阅/退订。
- 宿主替身：内存存档、钱包、角色背包、仓库、Buffer、拾取物与物品模板。Unity 对象模拟销毁后的假 null 和 GameObject 连带销毁组件，Buffer 交付会实际经过这一销毁路径。
- 覆盖：接收方未就绪、实例化失败、投递前失败、入包后通知异常、满包落地、Buffer 收到树后销毁实例、无效拾取物、写入失败、跨槽与关卡就绪补发；未送达不销账，有回执不重复发。

运行：`python tools/run_runtime_regressions.py --filter ZombieModeEntryDebt`。

不启动游戏或访问玩家存档；不证明真实切图时序、官方拾取代理与 ES3 物理保存正确。
