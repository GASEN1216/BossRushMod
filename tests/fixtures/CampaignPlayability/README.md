# 战役目标与终章生命周期回归

通过 `python tools/run_runtime_regressions.py --filter CampaignPlayability` 运行。

直接链接生产章节目录、实际 `Chapters.json`、共享 JSON 解析器、目标追踪器、死亡/受伤采集器、模式桥与 NoteIndex 桥。覆盖六章完成路径、无伤边界、近战契约的开局工具判据、友军/中立与非玩家击杀过滤、暂停、计数封顶、待交付不重新武装、死亡/换局复位和图鉴双向修复。零伤害用例依据官方 `Health.Hurt`：即使 `finalDamage` 为零，仍发出 `Health.OnHurt`。

每次运行从当前 `CampaignFinalBoss.cs` 抽取门禁、独白到生成、生成、死亡和清理方法，从 `CampaignDialoguePlayer.cs` 抽取取消方法，仅把异步载体 UniTask 换成 Task。覆盖取消独白不生成、后继对话使用新 token、工厂迟到成功回收、旧请求异常不取消新请求、让路退订、场景销毁后可重打、胜利只完成一次。对象替身模拟 Unity 已销毁等于 null 和 GameObject 销毁连带组件；测试必须经过这条销毁路径。

替身提供官方事件输入、武器标签、模式字段、图鉴列表/字典、持久化接收端与可控 Boss 工厂。独白等待为可取消 Task；实际对白、官方 UI 与 UniTask player loop 由 `SkyIslandDialogue` 的共享管理器回归另行覆盖。此夹具不模拟战斗、物理落点、渲染、原生输入、玩家存档和奖金落盘。近战实际配装调用点由 `CampaignFlowGuard` 验证，物品工厂成功率须 L3。L2 证明这些输入能驱动生产目标/编排，不能代替实机入口、战斗手感和帧耗采样。奖金与持久化由 `ContentTransactions` 覆盖。
