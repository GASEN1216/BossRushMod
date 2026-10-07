# 战役目标与终章生命周期回归

2026-10-07：终章独白增加由官方 DialogueUI / actor 失效独立发出的取消，取消前不调用 Campaign cleanup。真实终章编排必须复位当前决战并允许重新交互；旧对话迟到取消不得清掉后继决战。已有工厂在途 → cleanup → 迟到角色回收，以及旧生成异常不能取消新请求的用例继续保留。渲染与官方对话 UI 为可控替身，生产 runId、取消、清理、实例认领与死亡逻辑逐字执行。

通过 `python tools/run_runtime_regressions.py --filter CampaignPlayability` 运行。

直接链接生产章节目录、实际 `Chapters.json`、共享 JSON 解析器、目标追踪器、死亡/受伤采集器、模式桥与 NoteIndex 桥。覆盖六章完成路径、无伤边界、近战契约的开局工具判据、友军/中立与非玩家击杀过滤、暂停、计数封顶、待交付不重新武装、死亡/换局复位和图鉴双向修复。零伤害用例依据官方 `Health.Hurt`：即使 `finalDamage` 为零，仍发出 `Health.OnHurt`。

Mode F 闩回归直接链接生产 `ModeFRuntimeModule_BountyLatch.cs` 与战役模式桥，验证印记先写后删、纯查询不消费、victim 身份匹配和一次性消费；场景、装备和 HUD 等模块依赖不进入该夹具。

每次运行从当前 `CampaignFinalBoss.cs` 抽取真实状态、只读属性、启动、门禁、场景缓存、独白到生成、生成、死亡和清理方法，放入实际 `CampaignRuntimeModule` owner；从 `CampaignRuntimeModule.cs` 抽取真实 `OnSceneLoaded` 与 `OnDestroy`，并直接链接整份 `CampaignRuntimeModuleHostBridge.cs`。从 `CampaignDialoguePlayer.cs` 抽取取消方法，仅把异步载体 UniTask 换成 Task。覆盖旧宿主入口落到同一模块、独立宿主状态隔离、场景代数失效、取消独白不生成、后继对话使用新 token、工厂迟到成功回收、旧请求异常不取消新请求、让路退订、场景销毁后可重打、已排队的重复死亡回调只完成一次。卸载用例在真实 `OnDestroy` 清空模块 owner 后继续完成旧工厂，验证异步入口捕获的原宿主仍先回收掉落再销毁迟到 Boss。对象替身模拟 Unity 已销毁等于 null 和 GameObject 销毁连带组件；测试必须经过这条销毁路径。来源 SHA-256 写入 `Build/campaign-playability/production-source-sha256.json`。

替身提供官方事件输入、武器标签、模式字段、图鉴列表/字典、持久化接收端与可控 Boss 工厂。独白等待为可取消 Task；实际对白、官方 UI 与 UniTask player loop 由 `SkyIslandDialogue` 的共享管理器回归另行覆盖。此夹具不模拟战斗、物理落点、渲染、原生输入、玩家存档和奖金落盘。近战实际配装调用点由 `CampaignFlowGuard` 验证，物品工厂成功率须 L3。L2 证明这些输入能驱动生产目标/编排，不能代替实机入口、战斗手感和帧耗采样。奖金与持久化由 `ContentTransactions` 覆盖。
