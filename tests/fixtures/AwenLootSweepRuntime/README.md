# 扫箱运行时执行回归

运行 `python tools/run_runtime_regressions.py --filter AwenLootSweepRuntime`。

直接链接完整 `AwenLootSweepRuntime` 与宿主兼容桥，执行实际跨模式查询及装配。覆盖 200ms 缓存边界、最近邻顺序、目标副本、销毁后过滤、启用时重新取样、双 owner 隔离、清理与待交付物品释放次序、E/F 会话优先级、20 次死亡发放边界、退款失败、注册失败与气泡节流。

Unity 物件、可控时钟、真实箱子查询、扫箱 runner 和物品投递为替身；GameObject 销毁连带组件并实现 Unity 假 null。夹具不模拟实际寻路、官方背包写入或游戏内交互。
