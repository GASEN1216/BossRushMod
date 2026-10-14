直接链接 Arena/Mode D 联机 partial、个人收据/玩法频道和原里程碑算法，复用 CoopProtocol 的内存 16 人传输替身。覆盖固定参赛份数、迟加入与换槽资格、原资产奖励重试、客人个人成就/快递、重复死亡/胜利、迟到旧局事实及 16 份原里程碑总量。

Unity 对象、成就服务与原生物品落地是受控替身；通关入口实际接线另由守卫与正式编译核验。此夹具属于 L2，不证明游戏内物理箱子、联机掉落拾取或 Steam 16 人稳定性。

2026-10-13 追加完整生产 LootAndRewards.OnPlayerDeath、Mode D 实际 starter 提交／回滚欠账、房主 Boss 成就个人收据。房主换槽、原队伍全死但新槽旁观者存活、失败的部分 starter 回收保留原对象与当前未结案波次准入均有执行判据。CoopProtocol 传输替身已改为调用真实 BossRushCoopJson；个人交付及幂等逻辑仍为生产实现。实际背包、成就存盘、Unity 死亡后角色时序仍由引擎适配器隔离。

本轮 2026-10-13 边界补充：新增原客户端投影在 StopNetwork 后退出的回归：真实 projection owner 使波次失效，撤 active/arena/planned，销毁旧 replica 并清引用；模拟 Unity 销毁对象同时销毁组件与 null 语义。真实 deferred client entry 还按原 room、client role 和 scene handle 栅栏。完整原生游戏销毁/刷怪由实机再验。
