# 前哨场景租约执行回归

直接链接生产 `StoneOutpostSceneLease.cs`，验证 `isDone` 早于完成回调时不发布就绪、路径大小写、加载期间取消、延迟卸载、缺失根节点及外部同名场景隔离。

UnityStubs 仅提供可控的 Scene / AssetBundle / AsyncOperation 宿主替身；不验证 Unity 实际调度、场景包序列化、光照、交互、敌人或玩家传送，不替代实机。

运行：`python tools/run_runtime_regressions.py --filter StoneOutpostSceneLease`。
