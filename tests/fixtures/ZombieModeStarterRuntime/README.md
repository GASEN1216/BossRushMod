# ZombieModeStarterRuntime

通过 `python tools/run_runtime_regressions.py --filter ZombieModeStarterRuntime` 运行。

抽取当前开局发放/选择、现金投入及预览、净化点收集与 SoulCube 查询缓存、地图隔离协调和视觉恢复的生产方法。覆盖保底治疗及弹药失败不推进状态、成功时发放与资源确认顺序、现金失败保留旧选择、负数与大数处理、净化点逆序/幂等/失效 run 结算、缓存未命中只扫描一次、地图隔离及恢复顺序、已销毁视觉目标和脚标只回收一次。

物品选择/发放、EconomyManager、隔离底层服务、UI/特效及资源扫描为可记录替身；不替代真实物品树、原版角色/撤离点筛选或 Unity 场景执行。Unity 替身模拟 destroyed-null 和 GameObject 连带组件销毁，场景销毁是必经测试步骤。实际源 SHA-256 写入对应构建目录。
