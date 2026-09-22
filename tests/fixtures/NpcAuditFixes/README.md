# NPC 与交易审计修复执行回归

通过 `python tools/run_runtime_regressions.py --filter NpcAuditFixes` 运行；无需启动游戏或访问玩家存档。

好感管理、解码、日报存储与协调器、NPC 文本配置和 actor 工厂直接链接生产源码。寄存事务、婚礼资格与场景判据、重铸按钮收费入口按原文逐次抽取；产物及源文件 SHA-256 写入 `Build/runtime-regressions/NpcAuditFixes/`。

宿主替身提供内存存档、可控恢复任务、经济账户及 Unity 已销毁对象等于 null 的语义。重铸资格与收费入口为生产逻辑，随机数值修改算法由成功/失败结果替身隔离；这套用例验证先后顺序、补偿和 owner，不替代算法属性测试或游戏实机。UI 渲染、物理拾取和 Harmony 实际挂载仍须 L3 验证。
