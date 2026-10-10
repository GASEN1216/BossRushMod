# 天空岛交互与语言刷新执行回归

运行 `python tools/run_runtime_regressions.py --filter SkyIslandInteraction`。

入口脚本逐字抽取生产交互提交、服务判据、奖励落点、音频停止、环形几何和语言刷新方法；其余剧情判据链接完整生产文件。Unity 布局、物理、音频和经济服务使用显式替身，不访问玩家存档。

语言回归抽取居民 RefreshLocalizedNames、序章 InjectLocalizations、ResidentName 与 NPCNameTagHelper 的已有登记更新方法及字段。保留语言覆盖字典，对同一批 UI 中英双向切换，核对船点交互等稳定 key、隐藏/销毁/注销居民和稳定语言下的写入次数。09-23 按 owner 复查移除了船点浮空招牌，夹具同步移除其专属逻辑；无浮空字由 SkyIslandHudGuard 守卫，组内入口仍由 SkyIslandPlayerEntryGuard 保护。官方 UI 创建与布局仍是替身；生命周期调用接线由 SkyIslandLiveLocalizationGuard 补充，异步出生后现取姓名目前为 L1 验证。

2026-09-28 扩展：抽取完整天空岛物品定义与名称/描述注入、场景名注入、共享建筑交互基类、搜索点及信鸽/采集交互体的真实刷新方法；物品名称价值规则和见闻文案链接完整生产文件。官方 `Item.Description` / `DescriptionRaw` / `description` 与 `InteractableBase.InteractName` getter 从只读反编译源逐字抽取，避免“description 可写”或“每次读取都会重注入”的替身假设。验证 18 件物品描述、20 处固定搜索点、同一信鸽与隐藏采集点的中英反复切换、读条和回调保留，以及 120 次名称查询不增加字典写入。覆盖字典和 Unity 场景仍是内存替身，不启动游戏、不读写玩家存档；全局语言入口接线由 `SkyIslandLiveLocalizationGuard` 检查。

2026-09-23 审美审查追加（PanelLooks）：抽取面板的主动关闭 `Close`（状态当帧收掉、旧画布改名交给共享淡出）、重开时把共享遮罩的淡入当帧落定、选项纯显示修饰（弱表挂在 Choice 旁边，Choice 仍只有 Label / Select 两个字段）、材料不够的计数标红与手记正文排版（不拆句子，F3 按子串匹配）。淡出、图标与实际排版仍是替身，观感只能实机看。

2026-10-10 渡口备用商店：`DockShopRegression` 逐字执行 `SkyIslandWorldStory` 构造和 `Dispose`、备用商店装配/清理、通用 `NPCInteractionGroupHelper`、`NPCInteractableBase.Awake`、`NPCShopInteractable`，以及 `NPCShopSystem` 的解锁和归属关闭判据。覆盖缺失渡口装置、保留原交互组成员、配置与入组先于 `Awake`、初始隐藏后靠浮舟好感事件解锁、其他 NPC 好感不干扰、重复装配和幂等清理、延迟激活场景、关闭正确的商店、销毁场景时组件假空与退订。好感等级/配置存储、父 NPC 查找、场景激活调度和实际商店 UI 是明确的宿主替身；这里的关闭断言只证明把关闭请求送到正确 owner，不证明官方商店窗口实际消失。

该项反向验证运行 `python tests/fixtures/SkyIslandInteraction/negative_probes.py`，先在本机跑上面的聚合入口生成源码清单。脚本只修改 `Build/dock-shop-negative-*/` 下的隔离副本，经聚合入口实跑，要求删除构造接线、删除清理接线、错配 NPC ID、遗漏组成员移除、销毁前未失活、无条件关闭别家商店这 6 种破坏均红在指定行为断言。每次按字节恢复并校验 SHA-256，最后重跑全绿；JSON 与各轮日志保存在同一个隔离目录。
