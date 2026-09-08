# 2026-09-07 实机日志修复回归

运行：`python tests/fixtures/GameplayLogFixes/run.py`，或 `python tools/run_runtime_regressions.py --filter GameplayLogFixes`。

兼容分类：`COMPAT`、`WIRE+`（新增精确目标的兼容补丁）。覆盖晚于选档加载 Mod 导致 Mode H 无法锁盘、死亡/停用龙王收到迟到受伤回调、教堂已有放置记录但缺少好感历史标记、AI 延迟搜索回调、动态交互体的空列表初始化及已销毁烟雾申请帧末等待。

每次构建逐字提取生产初始化入口、押品日志一致性检查及龙王受伤方法；两个 AI 前缀与交互体 Awake 前缀直接链接生产文件。官方回调和 `InteractableBase.Awake` 从仓库反编译源码逐字提取，先复现未修复时的空引用，再验证修复保留正常结果、已有交互组及碰撞体初始化。哈希与生成源码留在 `Build/gameplay-log-fixes/`。

仓库/存储、Unity 销毁对象比较、组件、帧末等待和资源加载由边界替身提供。共 25 条断言，包括未结押品与损坏日志继续阻断、仓库未就绪延迟重算、正常龙王技能保持、教堂未解锁空档不提前开放、正常烟雾等待及其他 runner 行为保留。烟雾回归只执行生产前缀，计时状态机未整体模拟。此回归不验证 Harmony 实际挂载、AssetBundle 或 Unity 场景调度；这些仍需启动自检和实机 F3 日志确认。Mode H 同帧归档与真实 I/O 失败由 `ModeHReviewFixes` 覆盖。
