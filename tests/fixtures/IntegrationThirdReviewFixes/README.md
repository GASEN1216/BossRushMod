# 第三轮 Integration 缺陷执行回归

对应 CR-2026-09-06-012 / 013 / 014 / 016，分类 `COMPAT`。

运行：`python tests/fixtures/IntegrationThirdReviewFixes/run.py`。
所有编译、依赖副本、哈希与 IL 证据写入 `Build/integration-third-review-fixture/`，不启动游戏、不访问玩家存档、不部署。

需要 Windows、.NET SDK、本机游戏的 Harmony 和 Managed 程序集；可通过
`BOSSRUSH_HARMONY_DLL`、`BOSSRUSH_GAME_MANAGED` 覆盖定位。

执行范围：

- 完整生产 `DuckNpcMovement` / `DuckNpcRuntimeMarker` 加完整官方 `AI_PathControl`。
  替身 Seeker 故意在取消后仍投递成功回调，覆盖暂停、取消异常、恢复后旧请求、停用、
  移动中 0.6 秒重规划、慢请求避免饥饿、寻路超时、近距离停步、远距离追赶、驻留与对话独立所有权。
- 直接提取官方 `Health.Hurt` 完整方法体，保持算式、分支、事件顺序不变，
  用真实 Harmony 安装完整生产观察补丁，生产 helper 通过真实 `OnHurt` 回调读取贡献。
  与未打补丁的官方方法分别执行每种元素所得结果对照，验证混合抗性、免疫、最低 1 点、
  最终生命限额、暴击、穿甲、忽略护甲、真实伤害、难度、丧尸倍率、本击耐久破损、
  先加 Buff 再取护甲、同目标嵌套、嵌套异常、卸装与运行时 owner 变化。
- 另在本机真实 `TeamSoda.Duckov.Core.dll` 上读取原始 IL，运行完整生产定位与 transpiler，
  确认只插入一处观察调用；保存变换前后 IL 与游戏程序集 SHA-256。
- 负向检查：累加从加法改为减法、移除最低伤害赋值均须拒绝匹配；缺少贡献记录必须返回 0 并明确诊断。

边界：Unity、A* 调度、物品事件及生命对象外围依赖使用内存替身；不代表游戏内验证。
直接在 .NET Framework 隔离进程 detour 原游戏程序集会受其 `IValueTaskSource` 运行时依赖限制；
.NET 8 又与该安装的旧 Harmony 反射 API 不兼容。因此真实游戏程序集一层如实仅验证 IL，
执行行为由完整官方方法体与真实 Harmony 的兼容宿主完成。仍需实机验证聊天/驻留/跟随体感、
游戏加载时补丁绑定日志与元素回血显示。
