# NPC 与交易审计修复执行回归

通过 `python tools/run_runtime_regressions.py --filter NpcAuditFixes` 运行；无需启动游戏或访问玩家存档。

好感管理、解码、日报存储与协调器、NPC 文本配置和 actor 工厂直接链接生产源码。寄存事务、婚礼资格与场景判据、重铸按钮收费入口，以及阿稳扫箱「开启下次扫箱」的整箱寄快递（`CourierService.BufferItemsSilently` 静默入缓冲、一次落盘、一条汇总横幅；只寄扫箱产出的物品，玩家塞进箱子的东西退回背包；快递站未就绪时退回逐件交付、交付失败保留旧箱）按原文逐次抽取；产物及源文件 SHA-256 写入 `Build/runtime-regressions/NpcAuditFixes/`。

宿主替身提供内存存档、可控恢复任务、经济账户、官方快递站缓冲（`PlayerStorageBuffer`，Instance 可置空模拟 Awake 前）与横幅计数，以及 Unity 已销毁对象等于 null 的语义。工程目标框架写的是 net10.0；只装了 .NET 8 SDK 的机器需要临时换成 net8.0 才能跑（2026-09-23 就是这么跑的，5 组全绿）。重铸资格与收费入口为生产逻辑，随机数值修改算法由成功/失败结果替身隔离；这套用例验证先后顺序、补偿和 owner，不替代算法属性测试或游戏实机。UI 渲染、物理拾取和 Harmony 实际挂载仍须 L3 验证。
