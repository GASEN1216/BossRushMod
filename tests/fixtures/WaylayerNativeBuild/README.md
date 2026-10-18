# Waylayer 原生票据装配回归

通过 `python tools/run_runtime_regressions.py --filter WaylayerNativeBuild` 执行。需要 Windows、本机游戏 Managed 与已安装 COOP / Harmony。可以用 `BOSSRUSH_GAME_MANAGED`、`BOSSRUSH_HARMONY_DLL`、`BOSSRUSH_COOP_DIRECTORY` 指定相应副本。每次构建独立 Framework 可执行文件，明确执行本次路径，记录源码、官方输入、工具、DLL 哈希。不会启动游戏或访问玩家存档。

真实执行的代码：

- 链接完整生产 `CoopWaylayerNativeBuildScope.cs`，逐字抽取生产 `CoopWaylayerClaims.Admission`、`Admit`、`AllowInventory`、`AllowSlot`。
- 读取本机安装 COOP 的完整 688 字节同步 `ItemTool.BuildItemFromSnapshot` 方法，执行全部分支与异常区域。宿主类型和成员按完整名称映射到替身，然后运行真实生产 transpiler。逐条验证除根绑定的两条指令和两个原生装配调用外，原操作码、操作数、跳转标签和异常区域均保持。
- 同样执行官方完整 513 字节 `InteractableLootbox.CreateFromItem` 方法。仅在 `CreateLocalInventory` 后绑定新箱，并替换唯一 `AddAndMerge` 调用；普通物仍运行完整官方合堆方法。
- 官方 `Inventory.AddAt`、`AddItem`、`Slot.Plug` 原文完整编译执行。`ItemUtilities.AddAndMerge` 只把反编译无效标识符改为合法局部变量，并显式初始化编译器缓存局部变量为空，控制流不变。

宿主边界：Unity 对象、预制体创建、Item 工厂、标签、父子通知、合堆内部 Item.Combine、槽位资格检查、场景迁移、网络 custom-data 事件为替身。替身实现 Unity 销毁等于 null 与 GameObject 连带组件销毁。未映射的 Unity / 游戏 / COOP 类型会拒绝运行，避免在离线进程意外执行真实宿主。票据解析与 `TrackCorpse` 身份路由是本组边界；它们由 WaylayerEscrow 的完整 Claims / Journal 生产回归覆盖。本组不证明游戏画面、真实网络顺序或可玩性。

行为覆盖：库存和槽位递归装配；真实本地与远端玩家根拒绝；通知先脱离再重入的单次许可；嵌套原生装配；完整异常传播与 finalizer 回收；原生 root 自动登记；尸体来源集合冻结、唯一新箱和真实入箱锚点；普通尸体 O(1) 无扫描前缀、保留普通合堆；嵌套普通 / 带票据尸体；通知异常后保留已入箱事实；COOP 缺失和版本不兼容安全关闭；缺失 / 重复五类 IL 站点全部 fail closed。

`python tests/fixtures/WaylayerNativeBuild/negative.py` 在独立源码副本中做 8 项真实行为破坏与 5 项结构守卫破坏；每项必须在指定断言失败，最后重跑原始源码并确认源文件字节未改。证据在 `Build/runtime-regressions/WaylayerNativeBuild/negative-*/summary.json`。
