通过 `python tools/run_runtime_regressions.py --filter EnemySpawnRuntime` 运行。

直接链接完整生产 `EnemySpawnCore`、后处理 scheduler 与 profiler，覆盖普通生成的等待帧与异步失效、配装/激活/掉落/提交顺序，真实后处理队列的完成与取消、特殊 Boss 参数与清理 owner、五次随机回退与单例限制、Mode G 托管失败关闭及句柄身份、外部冻结提交、旧回调失败通知与双 owner 隔离。

Unity 对象、官方预设创建、专用 Boss 工厂和配置/掉落动作采用可观测替身；对象销毁模拟 Unity 假 null 与组件连带销毁。UniTask 转接可控制完成时刻的 Task，不模拟 Unity PlayerLoop；无法替代游戏内主线程、地形、AI、资源与表现验证。生产源码 SHA-256 写入聚合回归输出目录。
