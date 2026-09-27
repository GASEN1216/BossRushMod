# 模式调度顺序回归

`run.py` 从 `Utilities/ModeRuntimeHooks.cs` 抽取生产 `TickModeRuntimeGroup`，用只记录调用的宿主方法替身运行。验证共享生成后处理先于 Arena、模式间相对顺序、Arena 早返、以及普通时间与 unscaled 时间的传参。没有复制调度实现或构造期望顺序表。该夹具不模拟 Unity 场景、敌人和事件回调。
