# 天空岛官方独立出击租约回归

直接编译生产 SkyIslandRaidLease.cs，SceneLoader、Unity Scene/AssetBundle 与角色死亡状态使用可控替身。
覆盖加载中宿主消失、迟到官方关卡装配、等待实际 scene 卸载和官方加载完成、死亡任务所有权、
返航失败重试、准备/装配错误清理。测试不启动 Unity，不修改玩家存档，不能替代实机切图与死亡验收。

2026-10-06 独立深审补充：原样抽取 `SkyIslandSession.DispatchReturnIfReady`，检查尚未收到岛图 sceneLoaded 的失败返航不会循环重载基地；会话其余清理用边界替身，资源保留与最终释放仍执行真实租约。另覆盖官方异常主动取消、原生 operation 完成前禁止重试、岛图已被幕布卸载后返航失败仍由租约重试，以及重试启用目标场景快照保护。此前三个缺陷均以旧生产方法实跑转红，修后通过；抽取方法 SHA-256 写入 `session-return-source.json`。

加载时钟回归直接执行生产租约：官方“点击继续”停留超过 120 秒或两小时仍可继续，点击后实际加载卡住仍超时，陈旧提示与取消状态不延长预算。提示文字与激活顺序已对照本机官方 `SceneLoader.LoadScene` DLL；Unity 加载、按钮与时钟仍为替身。

运行：`python tools/run_runtime_regressions.py --filter SkyIslandRaidLease`。
