# 天空岛官方独立出击租约回归

直接编译生产 SkyIslandRaidLease.cs，SceneLoader、Unity Scene/AssetBundle 与角色死亡状态使用可控替身。
覆盖加载中宿主消失、迟到官方关卡装配、等待实际 scene 卸载和官方加载完成、死亡任务所有权、
返航失败重试、准备/装配错误清理。测试不启动 Unity，不修改玩家存档，不能替代实机切图与死亡验收。

运行：`python tools/run_runtime_regressions.py --filter SkyIslandRaidLease`。
