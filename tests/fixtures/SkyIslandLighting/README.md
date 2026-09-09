# 天空岛时钟光照回归

分类 COMPAT / WIRE+。工程链接完整生产 `SkyIslandLighting.cs` 与本机真实游戏、Unity 渲染程序集；只替代本地化助手。实际执行生产 `ResolveTimeBlend`，覆盖午夜、四时段锚点、黎明中点、非有限输入、跨日归一，以及全部时段边界的连续性。不会运行 Unity API、修改时间、创建场景或读取玩家存档。

使用 `python tests/fixtures/SkyIslandLighting/run.py`，或聚合入口 `python tools/run_runtime_regressions.py --filter SkyIslandLighting`。需要先编译生成包含本机 Managed 路径的 `Build/BossRush.rsp`，也可设置 `GAME_PATH`。

这组测试验证时钟混色算法，不验证画面、Volume、场景卸载顺序和手动光色操作；后者需要游戏实机验收。
