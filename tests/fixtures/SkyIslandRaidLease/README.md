# 天空岛官方独立出击租约回归

2026-10-13 联机补充：原生投票把 `LoadScene` 提前返回 CompletedTask 时，生产租约必须继续等真实岛图与关卡装配；客端入场不能再发起一次加载，仅等房主投票所触发的真实地图。两个执行用例使用可控原生替身，不代表真实投票 UI 与多人切图已通过实机。

独立复核原样抽取 `EnterCoopArrival`，接真实租约：同步 Prepare 必须先于 Subscribe/协程调度，重复官方入口只准备一次；基地原天气销毁后副本仍有效，客端不再调第二次官方加载，等待原生实际目标场景；准备失败释放资源与会话。调度/会话其余 UI 是边界替身，天气复制和 bundle 生命周期直接执行生产代码。

直接编译生产 SkyIslandRaidLease.cs，SceneLoader、Unity Scene/AssetBundle 与角色死亡状态使用可控替身。
覆盖加载中宿主消失、迟到官方关卡装配、等待实际 scene 卸载和官方加载完成、死亡任务所有权、
返航失败重试、准备/装配错误清理。测试不启动 Unity，不修改玩家存档，不能替代实机切图与死亡验收。

2026-10-06 独立深审补充：原样抽取 `SkyIslandSession.DispatchReturnIfReady`，检查尚未收到岛图 sceneLoaded 的失败返航不会循环重载基地；会话其余清理用边界替身，资源保留与最终释放仍执行真实租约。另覆盖官方异常主动取消、原生 operation 完成前禁止重试、岛图已被幕布卸载后返航失败仍由租约重试，以及重试启用目标场景快照保护。此前三个缺陷均以旧生产方法实跑转红，修后通过；抽取方法 SHA-256 写入 `session-return-source.json`。

加载时钟回归直接执行生产租约：官方“点击继续”停留超过 120 秒或两小时仍可继续，点击后实际加载卡住仍超时，陈旧提示与取消状态不延长预算。提示文字与激活顺序已对照本机官方 `SceneLoader.LoadScene` DLL；Unity 加载、按钮与时钟仍为替身。

运行：`python tools/run_runtime_regressions.py --filter SkyIslandRaidLease`。

2026-10-08 入场恢复补充：完整链接 `SkyIslandSessionEntry`，原样抽取 Build 的场景读入 / 激活等待与预热驱动，以及生产 Safe / TickFault。验证 A* owner / data 延迟、既有扫描忙、错误即时取消、独立激活超时、可选步骤释放、预热 Current 与 Build 上层取消、owner 显式取消及二次释放幂等。租约覆盖失败提示只在成功回基地、黑幕后消费一次，重试保留首次原因。每次构建使用独立目录，只执行 MSBuild 返回的本次 TargetPath，并留存源文件与产物 SHA-256。Unity / UI / GPU 仍为边界替身，不证明实机画面和切图通过。
