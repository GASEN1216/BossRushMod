# Mode H 复审修复回归

运行：`dotnet run --project tests/fixtures/ModeHReviewFixes/Review.csproj --configuration Release`。

覆盖 CR-2026-09-04-022/024/025 与 CR-2026-09-05-001。协调器、节流器、规范树、摘要、DTO、run owner 和冻结状态机直接编译生产文件；每次构建从当前源码原样提取主流程方法及休息枚举段，摘要记录到 `Build/modeh-review-fixes/generated/source-hashes.txt`。生成源码与所有构建产物均留在 `Build/modeh-review-fixes/`。

回归检查实际锁盘/冠军终局推进、同步屏障后的普通节流与故障欠账、旧/新摘要与同 TypeID 替换拒绝、赛季/事件/新 owner 恢复、构建和槽位门禁、原图租约与意图消费、换槽清理、已结算场次回幕间，以及未出场合同选手的伤病恢复。

2026-09-07（`COMPAT`）：补入实际 `DebugFinishValidationSeason` 方法，验证同帧已有普通保存时认证归档仍能同步落盘并退出；真实 I/O 失败仍返回失败。新增 4 条断言，与 `GameplayLogFixes` 的晚启动押品日志恢复回归配套。

宿主 I/O、官方物品模型、地图及租约操作、认证环境为替身；游戏构建摘要缓存由测试注入。生成过程及异步 SceneLoader 的 Unity 调度仍需实机验证。这些断言不能替代完整六场赛季、真实仓库或切图 smoke。
