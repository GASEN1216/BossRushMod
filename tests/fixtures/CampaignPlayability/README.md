# 战役目标可完成性回归

通过 `python tools/run_runtime_regressions.py --filter CampaignPlayability` 运行。

直接链接生产章节目录、实际 `Chapters.json`、共享 JSON 解析器、目标追踪器和死亡/受伤采集器；覆盖六章的可达完成路径、无伤边界、非玩家击杀、暂停时间、写入失败重试、重复通知与换局复位。零伤害用例依据官方 `Health.Hurt`：即使 `finalDamage` 为零，仍发出 `Health.OnHurt`。

替身仅提供官方事件输入、武器标签、当前模式波次与持久化接收端，不模拟战斗、Harmony、Unity 场景、玩家存档和奖励落盘。L2 通过证明这些输入能驱动目标完成；玩家入口和模式事件是否在实机到达仍需 L3。存档与奖金另由 `ContentTransactions` 覆盖。
