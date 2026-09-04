# 九月深度复审隔离回归

运行：`dotnet run --project tests/fixtures/ReviewSeptember/ReviewSeptember.csproj --configuration Release`。

直接链接生产保存协调器、每帧节流器、规范 JSON、物品树捕获/恢复源码，覆盖同帧四阶段屏障、普通写延期、战斗延期预算、IO 异常重试、宿主销毁、切槽欠账清除，以及嵌套物品/变量类型/堆叠/排序锁跨实例恢复、摘要与品质拒绝、旧载荷处理。

宿主文件 IO 和物品数据边界用明确的 stub 替代，不调用游戏、不读取玩家存档。通过不等于 Unity 内实例化、满仓返还或实机崩溃恢复已验证。生产 DLL 仍须用官方编译清单和游戏程序集编译。
