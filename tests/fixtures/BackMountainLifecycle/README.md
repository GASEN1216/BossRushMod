# 竞技场后山执行回归

运行：`python tools/run_runtime_regressions.py --filter BackMountainLifecycle`。需要 .NET 8 SDK，不启动游戏、不读取玩家存档。生成工程、源码 SHA-256 与产物位于 `Build/backmountain-lifecycle/`。

直接链接后山配置、物品、解锁、运行时模块、菜地注入、点唱机注入、出击餐服务/使用行为、展示柜服务，以及共享属性跟踪器、使用绑定、JSON 解析/写出和真实曲目解析器。UI 的 Open/Close/Tick/销毁入口逐字抽取；只有画布构造由替身替代，真实构造到输入租约的接线由 `BackMountainPlayabilityGuard` 检查。

用例覆盖：六件物品注册与官方派生描述键、种子/餐食用法、官方使用完成时的扣量补偿、覆盖与读回失败恢复、缺少任一 stat 时不消费、零基础换弹增益、同局换区/角色销毁重建/结束/换槽、实时与历史解锁、棘轮恢复/失败重试、静止一万次 tick 无场景扫描、曲目切语言不改索引、收藏升级与坏档写保护、Esc/画布销毁/刷新/换槽的输入租约释放；起步种子（菜地开放后主角在基地就绪才发、每槽一次、先落标记回读再发、存档忙 / 回读失败 / 交付全失败时下次重试且不重复、局内不发、飘字等对话结束）与基地售货机种子条目（菜地未开放 / 后山关闭 / 非基地普通商人不挂、价 = 价值 × 3、幂等、仅棘轮也挂）。

宿主替身模拟 Unity 已销毁对象等于 null、GameObject 连带销毁组件；存档按槽隔离并可注入写/读回失败；stat 保留 Add 与 PercentageAdd 的数值语义；官方换弹公式按当前反编译源码核对。不能证明实际 Unity 物理、渲染、音频、官方输入栈和帧耗；这些必须通过 L3 检查。

2026-09-25 三形态扩展：后山 UsageBehavior 统一转到即时 MorphService；本夹具的变身宿主只控制成功/拒绝/异常，验证消费与补偿事务，不再将它伪装为 RaidMealService 登记。真实三形态外观、属性、碰撞和清理见 BackMountainMorph。BackMountainLifecycle 仍逐字链接旧 RaidMealService，验证旧档已预备餐食兑现、过区重挂与局末清理。
