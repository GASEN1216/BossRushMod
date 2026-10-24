# ZombieModeStarterRuntime

通过 `python tools/run_runtime_regressions.py --filter ZombieModeStarterRuntime` 运行。

抽取当前开局发放/选择、现金投入及预览、净化点收集与 SoulCube 查询缓存、地图隔离协调和视觉恢复的生产方法。覆盖保底治疗及弹药失败不推进状态、成功时发放与资源确认顺序、现金失败保留旧选择、负数与大数处理、净化点逆序/幂等/失效 run 结算、缓存未命中只扫描一次、地图隔离及恢复顺序、已销毁视觉目标和脚标只回收一次。

物品选择/发放、EconomyManager、隔离底层服务、UI/特效及资源扫描为可记录替身；不替代真实物品树、原版角色/撤离点筛选或 Unity 场景执行。Unity 替身模拟 destroyed-null 和 GameObject 连带组件销毁，场景销毁是必经测试步骤。实际源 SHA-256 写入对应构建目录。

撤离回归直接执行生产 `EnsureZombieModeExtractionArea`、结算 / 完成 / 成功事件派发，以及共享工厂 `ConfigureEvents`。仅用替身创建场景对象、模拟钱包、通知和返程。覆盖官方区域先触发与模式协程先触发、结算失败不返程且原区域可重试、钱包写入前 / 写入后事件异常、通知异常、递归成功与重复事件、成功清理实际销毁区域并复位 run 状态，以及没有回执回调的既有工厂消费者。
