# F3 协程执行回归

直接链接生产 `DebugAndTools/ValidationCoroutineStack.cs`，并由 `run.py` 逐字提取生产
`RunIsolatedCase` / `TryStep` 后编译。验证嵌套异常、取消/超时式释放、
`Current`/`Dispose` 失败、逆序清理、幂等释放、循环拒绝与同步步骤分帧；
隔离壳还验证工厂异常、双异常，以及父协程清理先于竞技场回收。
不复制算法，不调用 Unity，不证明游戏内模式、加载与 UI 已通过。

2026-09-14 追加**天空岛外壳**：同样逐字提取 `RunSkyIslandSync` / `RunSkyIslandCase` / `SkyIslandSessionStillValid`、
共享的 `RunSyncCase` 与 `SkyIslandSkipCase`。此前岛内外壳与它的 SKIP 分支**一次都没离线执行过**。现在覆盖：
会话没了 / 未就绪 / 场景实例换了三种 SKIP（且用例本体不被调用）；判据真 → PASS、假 → FAIL、
`SkyIslandSkipCase` → SKIP（带 metrics，绝不记 PASS）、意外异常 → FAIL；协程外壳的中止 SKIP、
会话没了在创建协程之前就 SKIP、工厂抛异常 / 返回 null → FAIL、嵌套异常 → `_UNHANDLED` FAIL 且挂起的 finally 照跑、
正常结束外壳不代记结论。

入口：`python tools/run_runtime_regressions.py --filter F3ValidationExecution`。
只用上述聚合入口，确保显式构建后执行本次实际 TargetPath；不要直接运行 run.py 或 dotnet run。
产物位于 `Build/`。

2026-09-28 追加性能窗口：逐字提取生产 `SamplePerformance` / `SamplePerformanceWindow` 及岛内分项录制开关，
直接链接 `ResourcePerformanceMetrics`。宿主时钟、场景句柄和录制结果用内存替身；验证完整 5 秒窗口、换图、
直接观察到取消、外层 Dispose（含当前模式已经复位）、采样帧不足与原有最终帧时间阈值。
中断样本不得更新基线，任何退出路径都要关闭开窗时的录制。10 秒资源窗口保持原默认时长。
构建经聚合 runner 的 fresh TargetPath 解析后执行，不再依赖 `dotnet run` 的默认输出选择。

2026-10-13 追加完整生产 LoadScene／Ready 判断，以原生投票完成后真正变化的 scene handle 为栅栏，覆盖同名重载、旧 Ready 假通过、超时；返程只在实际 Closure 按钮可继续时点击原按钮。

EntryPreparation 链接完整生产 F3ModeDLoadoutLease：保管原 Item 对象和整棵树、独立官方 Inventory 容量预检、部分卸装失败恢复、付款阶段临时移出原费用候选、真实官方 delivery 拒绝／寄存兜底、切槽阻止恢复与同资产新角色的原 key／index 归位。原 Item.DestroyTree 是 ItemStatsSystem 扩展，生成文件保留生产 using，替身也定义同签名扩展；移除生产 using 会产生真实 CS1061，不能被假实例方法遮住。

2026-10-13 round7 实机确认基地 PlayerStorage 在竞技场为空，旧夹具恒定初始化它漏掉了前提。现在 Fresh 默认 PlayerStorage.Inventory=null；生产 lease 创建 inactive、DontDestroyOnLoad 的官方 Inventory 载体，完整归还且空后才销毁。边界替身模拟 Unity 销毁对象等于 null、GameObject 销毁连带组件及库存原物；执行真实 helper 覆盖空基地仓库、容量通知/部分转入失败、旧 root 真销毁后死亡换体、切槽拒绝归还、归还目标容量不足保留载体、外部原费用归还阻塞后重试。正式玩法 runtime 未改，这是 Dev 专用档验收准备修复。真实 AddComponent/SetCapacity 的官方 API 绑定另做 Windows 定向编译；这些隔离执行用例仍不证明 Unity 实际场景与存盘。

GChildProtection 提取专属孩儿护我护卫与 Hurt 方法；ExtractionFlows 提取真实 F 撤离入口、整个 F3 F_EXTRACTION 用例和丧尸撤离协程，验证异步分数／现金／结算、全员进圈、未准入拒绝、资产变化及超时失败。最新 119 条断言为绿，包含容量与物品转入回调中切槽时仍保管原资产原物的边界。原生物理、导航、背包保存、场景投票和 UI 依赖仍为替身；生产用例执行链为真，L2 不等于多进程实机通过。请用聚合入口运行并保留本次 TargetPath 和 SHA-256。

2026-10-15 追加三端场景完成条件：逐字提取 F3 的本机准备发布、全员等待与真实 Base Core 判定，实际链接生产 `BossRushCoopSceneBarrier`。各端的 AfterInit、当前 Main、Item、Health、相机及加载状态分别可变；前两人就绪时第三人必须继续阻塞完整 LoadScene，第三人实际就绪后才成功，同名旧代次和错误 Core 不可放行。合法 Base 船舱子场景可以恢复，竞技场伪装 active scene 名称不可以。客户端 Update 常驻发布接线由守卫覆盖，未声称这个 Unity 每帧入口已由隔离执行验证。

ModeHButtons 抽取实际 H 按钮解析及标签，覆盖中英文正式联机确认、原单机下注金额后缀和近似错误文字。此轮合计 161 条，旧 host-only 场景成功、缺 Item 仍准备、错误 Core 基地恢复三项均有真实执行反例，连同 Update 接线守卫在稀疏副本破坏后转红并按字节恢复。该数量只对应当次源码与日志，不代表真实三端加载或 H 赛季已通过。

2026-10-16 同步房主开始入口：本夹具执行真实 DecorateCoopBetPage、按钮解析和点击回调，验证未确认房主/客端没有越权开始按钮，已确认房主的独立开始按钮恰好调用正确回调。该回调在此只计调用次数；完整跳过、退款及准入策略由 ModeHCoop 的实际生产模块回归覆盖，不以本 UI 边界替代。
