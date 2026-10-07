# 天空岛普通巡逻调度器执行回归

执行 `python tools/run_runtime_regressions.py --filter SkyIslandPatrols`。只运行这一夹具，不启动游戏，不读取玩家存档，不依赖游戏 DLL。

生产逻辑包含完整的 `SkyIsland/SkyIslandPatrolSchedule.cs` 与从 `SkyIslandPatrols.cs` 逐字抽取的 `ConfigureNavigation`。聚合入口调用 `run.py`，取得生产源字节快照与 SHA-256，在 `Build/runtime-regressions/SkyIslandPatrols/snapshot-*` 下编译原样副本。生成的项目显式列出生产源、Host、Program 和 NavigationRegression，不替换生产实现、不使用测试版调度器。编译使用 C# 7.3、net8.0；允许 Major runtime roll-forward，以便只有 .NET 10 的本机也能执行纯逻辑，不安装新运行时。每次构建使用新输出目录，执行 MSBuild 返回的本次 TargetPath，并记录产物 SHA-256，不使用旧 bin 或 `dotnet run`。静态 `Regression.csproj` 只保留纯调度测试，完整验证必须走聚合入口。

Host 仅替代 Unity Actor 与异步结果的所有权。Actor 带可观测的 HP、装备引用、激活状态和幂等销毁计数；Host 调用生产调度器，不实现第二份预算或状态机。Program 通过真实返回值、计数和 Actor 身份验证行为。

七组行为覆盖 24 个近区活体上限、在途请求占用预算、唯一 pending、远区完成、反复近远恢复保留同一 Actor/HP/装备、死亡不重刷、重试后的旧完成和旧 abort、重复完成和双重释放、最终失败、pending 失效、Close 晚到结果、重复 Close、越界与零预算。

每次成功 TryReserve 后读取 Generation 作为该请求 token。Generation 在成功预留和首次 Close 时推进，防止同 slot 重试后的旧请求冒充新请求。CompleteSpawn(..., false) 进入 Suspended，已生成状态保持；AbortSpawn(..., false) 进入 Defeated，表示该 slot 本趟结束。IsSpawned 仅表示仍由服务持有的 Active/Suspended 活体；暂停不算死亡。Close 清掉活体和在途记录，保留已结束的 slot 标记，但不把其余 slot 伪造为死亡，所有新工作仍被 Closed 拒绝。

额外直接执行生产 `ConfigureNavigation`：角色根先失活，AI / Seeker / PathControl 放在子物体；新生成装配与同一实体挂起恢复都必须撤旧路径、重设图 mask、保留自动选敌，并通过现有激活安全网重新启用。缺 AI / 导航仍须拒绝。该场景在旧实现的实际方法上因 inactive 子树查不到 AI 转红，原调度 Host 不覆盖这一行为。

八个非等价反向变异分别破坏 inactive 子树查询、生成预算、恢复预算、唯一 pending、generation token、重复完成状态门、关闭后的新请求门和最终失败终态。每个变异只写隔离生产源副本，锚点要求恰好一次；必须构建成功并在预期的运行时 ASSERT 上转红。每次按原始字节还原并核对 SHA-256，结束时再确认主仓库生产源未改变。日志与隔离副本保留在 Build 下用于审查。

证据为 L2：真实生产调度与导航接线的隔离执行；Unity inactive 查询按父子激活规则替代，不证明实机 AI 射击、实际视距、NPC 装备接线、FPS 或内存表现。
