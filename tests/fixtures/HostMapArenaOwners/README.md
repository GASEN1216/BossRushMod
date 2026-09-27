# HostMapArenaOwners

通过聚合入口运行：`python tools/run_runtime_regressions.py --filter HostMapArenaOwners`。

夹具链接完整生产文件 `BossRushMapRuntime.cs`、`WavesArenaRuntimeModule_HostState.cs`、`CommonNpcSpawnPointPolicy.cs`，同时链接真实 `BossRushMapConfig`、`MapSpawnPointRegistry` 和共享 JSON 解析器。`run.py` 仅在 `Build/runtime-regressions/HostMapArenaOwners/` 写合成 JSON 与项目文件，保留生产源码 SHA-256；不改写生产方法。

地图覆盖 JSON 顺序和同序名称排序、SceneID 的首个匹配与大小写语义、自定义位置 / 路牌 / 原 DEMO 数值回落、路牌玩家偏移、动态数组引用身份、空数组回落、场景切换和双 owner 隔离。Arena 覆盖赋值后清理、激活订阅、停用清理顺序、普通 / 无限模式的参数与实时配置、进度重置、补弹入口及异常、主玩家优先、销毁后缓存回落、SetPosition 异常回落与缺玩家早返。NPC 覆盖四个实时查询、短路顺序、显式场景优先、Boss / 普通点池选择和数组引用身份、空点池回落及双 owner 隔离。

宿主替身仅记录龙息订阅、变异 / UI / 现金清理、消息和补弹副作用；Arena 其他 partial 的数据槽由测试显式设置，未测试那些 partial 的默认初始化或完整生命周期。绑定回调初始为 null，必须调用真实 Bind 方法后才能使用。普通 NPC 点位提供器用可观察替身，验证选择策略及传入场景，不验证点位数据本身。Unity 替身模拟销毁对象等于 null 与 GameObject 连带销毁组件，返程测试必经销毁；Vector3 比较只适用于这里的精确构造值。场景 API、真实传送、HUD、订阅处理器与游戏线程仍需 L3 实机验证。
