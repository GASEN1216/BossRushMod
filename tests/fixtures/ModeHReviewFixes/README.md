# Mode H 复审修复回归

运行：`python tools/run_runtime_regressions.py --filter ModeHReviewFixes`。

覆盖 CR-2026-09-04-022/024/025 与 CR-2026-09-05-001。协调器、节流器、规范树、摘要、DTO、run owner 和冻结状态机直接编译生产文件；每次构建从当前源码原样提取主流程方法及休息枚举段，摘要记录到 `Build/modeh-review-fixes/generated/source-hashes.txt`。生成源码与所有构建产物均留在 `Build/modeh-review-fixes/`。

回归检查实际锁盘/冠军终局推进、同步屏障后的普通节流与故障欠账、旧/新摘要与同 TypeID 替换拒绝、赛季/事件/新 owner 恢复、构建和槽位门禁、原图租约与意图消费、换槽清理、已结算场次回幕间，以及未出场合同选手的伤病恢复。

2026-09-07（`COMPAT`）：补入实际 `DebugFinishValidationSeason` 方法，验证同帧已有普通保存时认证归档仍能同步落盘并退出；真实 I/O 失败仍返回失败。新增 4 条断言，与 `GameplayLogFixes` 的晚启动押品日志恢复回归配套。

宿主 I/O、官方物品模型、地图及租约操作、认证环境为替身；游戏构建摘要缓存由测试注入。生成过程及异步 SceneLoader 的 Unity 调度仍需实机验证。这些断言不能替代完整六场赛季、真实仓库或切图 smoke。

2026-09-29（`COMPAT`）：抽取实际技术重试、挂起和恢复入口，覆盖选秀 / 签约阶段重试耗尽，以及早期恢复目标未知时的安全挂起。仍断言早期恢复不能跳过准备直接开赛；这些用例在修复前于选秀挂起断言失败。

同轮补入实际 `ReleaseMatchRuntime`：生成期技术重试须先停止主生成协程并回滚事务，再回同一场看盘。协程调度器 / 生成事务为可观察宿主边界；修复前于旧协程未停止断言失败。

同轮执行实际 `TryDriveSuspendedExit`，证明同一 owner 的重复 tick 只离场一次，新 owner 即使状态序号相同仍能离场。修复前第二个 owner 的退出被旧序号吞掉。
