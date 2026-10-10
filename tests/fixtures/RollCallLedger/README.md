# 点名册执行回归

从仓库根目录运行 `python tools/run_runtime_regressions.py --filter RollCallLedger`。

夹具直接链接生产 `RollCallLedgerRules`、`RollCallLedgerRuntime`、`RollCallLedgerFx`、配置与 TypeID 表。测试从实际订阅的 `Health.OnHurt` 入口推进，验证不同目标去重、首击伤害快照、固定点名窗口、冷却、致死命中、下一帧结算、友方及效果归因、暂停、卸下、死亡、主角替换、切图与伤害期间取消，以及数字提示向官方 API 传递的参数。

宿主替身仅提供 Unity 对象和事件、装备缓存查询、官方阵营判据、气泡及爆点调用记录。销毁 `GameObject` 必然销毁组件并执行 `OnDestroy`，已销毁对象按 Unity 语义等于 null；每轮测试都销毁对象图。伤害入口模拟官方致死时先 `OnDead` 再 `OnHurt` 的顺序。装备缓存的内部刷新、真实护甲/物理抗性、气泡渲染和视觉手感不在本夹具证据范围内。
