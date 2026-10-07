# 九月深度复审隔离回归

运行：`python tools/run_runtime_regressions.py --filter ReviewSeptember`。

直接链接生产保存协调器、每帧节流器、规范 JSON、物品树捕获/恢复源码，覆盖同帧四阶段屏障、普通写延期、战斗延期预算、IO 异常重试、宿主销毁、切槽欠账清除，以及嵌套物品/变量类型/堆叠/排序锁跨实例恢复、摘要与品质拒绝、旧载荷处理。

宿主文件 IO 和物品数据边界用明确的 stub 替代，不调用游戏、不读取玩家存档。通过不等于 Unity 内实例化、满仓返还或实机崩溃恢复已验证。生产 DLL 仍须用官方编译清单和游戏程序集编译。

2026-10-07 合并复核：保存替身真实保留官方私有 `saving` 闩锁的异常语义，直接执行独立的 `ModeHSaveFlushCoordinator` 与共享包装器；物理失败后不手工清闩，验证自动恢复、保留物理写盘欠账和下一帧重试。移除 H 生产调用处的包装后，必须在“owned H physical failure releases saving”断言转红。该替身也被 ModeHReviewFixes 复用，两组均需验证。
