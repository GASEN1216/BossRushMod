# 天空岛独立场景合同回归

`Program.cs` 链接真实 `SkyIslandOfficialContract.cs`，用宿主替身执行独立场景归属、官方初始化、主角重建、时间配置与墓碑身份拒绝边界。

另以 .NET `PEReader` 只读打开本机 `TeamSoda.Duckov.Core.dll`，核对官方初始化、角色恢复、死亡扣除/落盘/返回与墓碑恢复的实际 IL 调用序列。不会加载运行游戏程序集，也不会访问或改变玩家存档。

缺少游戏 DLL 明确失败。优先读取 `GAME_PATH`，否则使用 Windows 编译响应文件中的 Managed 目录。入口：`python tools/run_runtime_regressions.py --filter SkyIslandOfficialContract`。

这些是生产门控执行回归和官方二进制静态验证，不能替代 Unity 场景载入、死亡 UI、背包实际掉落、墓碑重进与取回的游戏验收。
