# 共享生成后处理执行回归

运行 `python tools/run_runtime_regressions.py --filter ModeEFSpawnPostprocessScheduler`。

直接链接完整生产 scheduler 和 profiler；SpawnCore 的参数、上下文和结果类型从生产文件原样提取。验证空队列早返、逐任务轮询、8/16 步上限、60 帧期限的最后 5 帧加速、实时时钟预算、配装/倍率/提交顺序、独立 owner、清空与销毁、失效局、外部提交冻结、变异/掉落门和提交异常清理。

替身模拟 Unity 假 null 与 GameObject 连带销毁、受控时间、UniTask 完成源，以及显式装配的配装/属性/掉落动作。不模拟实际装备加载、真实场景激活、伤害或帧耗；生产调度位置仍由 ModeRuntimeDispatch 回归覆盖。
