直接链接 Arena/Mode D 联机 partial、个人收据/玩法频道和原里程碑算法，复用 CoopProtocol 的内存 16 人传输替身。覆盖固定参赛份数、迟加入与换槽资格、原资产奖励重试、客人个人成就/快递、重复死亡/胜利、迟到旧局事实及 16 份原里程碑总量。

Unity 对象、成就服务与原生物品落地是受控替身；通关入口实际接线另由守卫与正式编译核验。此夹具属于 L2，不证明游戏内物理箱子、联机掉落拾取或 Steam 16 人稳定性。

普通 / D 客端首次入口的位置 API 在本夹具中是 Unity 边界替身，沿用真实 `Scene`、本机身体与地图世界坐标接口；完整入口对齐、重复快照、已到达与位置回调失效用例由 `LegacyArenaEntry` 链接实际方法执行。两组均不能证明引擎内实际落点与相机跟随。

2026-10-13 追加完整生产 LootAndRewards.OnPlayerDeath、Mode D 实际 starter 提交／回滚欠账、房主 Boss 成就个人收据。房主换槽、原队伍全死但新槽旁观者存活、失败的部分 starter 回收保留原对象与当前未结案波次准入均有执行判据。CoopProtocol 传输替身已改为调用真实 BossRushCoopJson；个人交付及幂等逻辑仍为生产实现。实际背包、成就存盘、Unity 死亡后角色时序仍由引擎适配器隔离。

本轮 2026-10-13 边界补充：新增原客户端投影在 StopNetwork 后退出的回归：真实 projection owner 使波次失效，撤 active/arena/planned，销毁旧 replica 并清引用；模拟 Unity 销毁对象同时销毁组件与 null 语义。真实 deferred client entry 还按原 room、client role 和 scene handle 栅栏。完整原生游戏销毁/刷怪由实机再验。


2026-10-16 Claude 复审 M01/M03/M05/S02：完整链接生产 Arena/D COOP、个人收据、频道、里程碑和 `WavesArenaRuntimeModule_CoopRewards`，验证当波冻结资产、正式迟加入从下一波生效、客户端同一波次 cohort、每人完整单机现金／每五波物品／当前里程碑量，以及溢出只记原本人账户。周期 admission 的请求数和实际 starter 入队数有计数断言，手动多次命令和不同事务仍逐次执行。16 人均为内存模拟，不表示做过 16 端实机。

个人交互箱仍沿原虚影落地和原 Loader 奖励池。未完成箱不可打开，同一个库存与原物由收据 owner 跨场景保留；标准箱等待原生 Setup 的真实 await 和原质量清理，再核完整 3／10 件才发布、ACK。取消时若异步仍持有库存，等待该续作结束后销毁未开放箱，避免先销毁载体导致原生空引用。执行用例包含部分现金已落箱后末物失败、通知后抛异常、Setup 延迟后首物失败、必经场景销毁、同原物重试与 pending factory 销毁时序。

主夹具的 Unity、随机工厂、Loader Task、视觉材质和原质量清理适配器是明确边界，不声称执行游戏原生完整 Setup。NativeLoot 子组单独用真实生产 marker 和 Windows Harmony，执行已安装原版 COOP 的完整 Loader 注册及打开箱 IL（264／91 字节，保留跳转、局部量与异常块，仅映射引擎／库存／网络端点），确认个人 marker 阻止原生共享注册／替换，普通箱仍走原生共享。实际 Loader Setup MoveNext 的 1091 字节 IL 仅做元数据核对，不计执行覆盖。所有本次 TargetPath 和 SHA 随日志保留，游戏内箱子演出、随机内容和多人拾取仍需 L3。

跨场景的未完成箱另沿真实 scene.handle 区分同名／同 buildIndex 重载；待当前 LevelManager.AfterInit、MainCharacter 与真实 Main 绑定、加载完成且存活后，调用真实 SpawnPositionHelper.TrySnapToGround，把同一箱移到本人落点再开放。NativeLoot 链接实际三个落地方法，物理／NavMesh 返回值为可变边界，覆盖未初始化、无地面、换坐标新场景与原库存不变。
# 2026-10-18 本波静默死亡

补充实际 `SweepSilentWaveBossDeaths → HandleBossDeath → ReportCoopArenaBoss → CoopPersonalDelivery` 链，覆盖双 Boss 列表移除、原最大生命现金池、单次最终推波、房主及客人换槽后原资产回执重试、重复事件去重、客端及 Mode D 不越权。下一波真实刷怪由引擎边界计数，静态守卫另核实际 Tick 在完整性自检前调用兜底。
