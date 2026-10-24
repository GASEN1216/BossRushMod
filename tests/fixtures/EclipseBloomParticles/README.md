# 蚀日花冠真实 Unity 粒子回归

入口：`python tools/run_runtime_regressions.py --filter EclipseBloomParticles`。
反向验证：`python tests/fixtures/EclipseBloomParticles/run.py --negative-probe`。

需要本机 Unity Editor 2022.3.62f3（由 `tools/unity_project_path.py` 定位，可设 `BOSSRUSH_UNITY_EDITOR`）和真实 `Assets/ui/eclipse_bloom_visuals`。缺少依赖时输出 `PARTIAL`、返回非零，不以静态检查代替引擎运行。独立工程在 `Build/EclipseBloomParticles/UnityProject`，每次重新复制生产源码与资源；该工程仅用于本夹具，不并发执行两个实例。报告和 Unity 原日志分别写入当次 `run-*/`。

真实生产路径：完整 `EclipseBloomBody`、资源租约与 AssetBundle 加载、Visuals / Geometry / GroundProjection / Rules、共享 FxKit / Materials / ParticleTextures / ProceduralSprites。执行入口实际进入 Unity 播放模式；经过真实 `Attach` 后逐项调用三阶段 `ApplyFrame`、破瓣和 `Dissipate`，死亡爆发与 4 秒清理由真实 Unity 播放帧驱动。所有常驻和爆发粒子都实际调用 `ParticleSystem.Simulate` / `Play`，检查存活粒子、启用的 xyz 速度曲线模式一致性，并保持余烬 y 轴的 `-0.08..0.3` 随机漂移。`Application.logMessageReceived` 全程收集引擎错误；任何 Error / Exception / Assert 都使基线失败，原始速度曲线报错另计数。

宿主替身仅包括角色 Health、暂停、地面 layer 配置、迷雾挂载、震屏和 Mod 路径。粒子系统、Unity 对象销毁、播放帧、shader 支持检查、包内真实网格和材质均不使用替身；不启动游戏、不访问玩家存档、不部署。此项是 L2 Unity 引擎隔离执行证据，不证明游戏内视觉、音效、帧率或完整官方角色交互。

反向验证先要求完整基线通过，再仅在独立 Unity 工程的 `EclipseBloomBody.cs` 副本删除 x / z 的 `TwoConstants(0, 0)` 赋值，重新执行相同测试。必须返回失败并真实捕获 `Particle Velocity curves must all be in the same mode`；单凭静态曲线判据失败不算探针成功。结束时按字节恢复该副本并核对所有仓库生产输入的 SHA-256。
