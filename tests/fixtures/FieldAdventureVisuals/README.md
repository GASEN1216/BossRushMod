# 野外趣事实体视觉执行回归

入口：`python tools/run_runtime_regressions.py --filter FieldAdventureVisuals`。`run.py` 把本次生产 Visual、纯规则、共享 FX 与真实 `field_adventures_visuals` 包按字节复制到 `Build/FieldAdventureVisuals/UnityProject`，用 Unity 2022.3.62f3 的真实播放循环和 URP Deferred 渲染六张 PNG。报告记录源文件 SHA-256、三维体积、网格顶点数、实体像素、粉色错误像素、动画变化、原生粒子错误、HUD 双门与资源租约回收。每张截图读回相机 UniversalRenderer 的实际 renderingModeActual，不能只凭配置宣称 Deferred；租约包含从未激活的 parent、失败构造、重复释放与外部销毁。

真实生产逻辑包括：三次投喂后的贪吃宝箱模型、嘎嘎蛋、易怒汽水、寄件/收件/试用站、喷气炮与反弹炮，以及资源包加载、根对象租约和失败构造清理。原生 Transform 检查覆盖 32 米两侧、40 米和 45 米舌头端点、十二段连续性、各段不超过四米和独立迷雾绑定；真实 Unity AudioSource 检查主音量、音效音量、两类静音与恢复后的数值。角色、官方界面状态、音量总线与迷雾边界由 `HostStubs.cs` 替换；本夹具不证明实机迷雾、实际听感、背负尺寸或战斗玩法正确。截图由 owner 查看，代理只读 JSON 和引擎文字日志。

碰撞回归另复制当前 `GreedyMimicEncounter`、`GreedyMimicRuntimeModule` 和只读官方 `HealthSimpleBase` / `DamageReceiver` / `ExplosionManager`。`FieldAdventureMimicCollisionRegression.cs` 走真实构造、确认回调投喂、成长装配和 Encounter.Tick。它把 12 个主箱体部件的真实 MeshFilter 顶点烘焙到独立世界坐标 MeshCollider，包含木箱、牙龈、三根铁箍、三块铆板、锁板、锁孔、木箱盖和盖唇。对 0～3 餐、八朝向、正负吞咽峰值、起始/后段/接近结束、呼吸膨缩、跳动及警告期开盖逐状态取样；每个部件六面的中心与靠近角点投影，分别沿水平/高/低射向发射。真实网格未占据的投影空角记录为 projectedEmpty，不计成功；所有实际网格命中的射线及 0.001/0.02/0.1 米球扫必须打中生产受击体。命中对象自身 GetComponent 取得官方 DamageReceiver，继续通过官方事件核对实际扣血；第三餐吞咽外沿的 45 点伤害还必须中断真实回血轮次。

测试将 `Physics.autoSyncTransforms` 关闭，在生产 Tick 或成长回调后立即查询，没有夹具调用 SyncTransforms、Simulate 或等待物理步；独立网格只更新自己的世界顶点，不发布生产 Rigidbody 姿态。每口成长后先检查零时间 Visual.Tick 不会再改变姿态，随后验证实际网格即时受击，前两口仅查碰撞以免伤害提前唤醒。收缩后必须拒收上一膨胀外沿的空域，真实顶点包络限制重复成长缩放，单 Collider / Health / Receiver 在动画期间保持身份，实际 RuntimeModule 负责暂停门，Dispose 立即停碰撞并退订。官方 ExplosionManager 完整执行其原生 OverlapSphere 和受击分支，证明 simpleHealth 一次爆炸只扣一次血。角色、相机抖动、爆炸表现、背包、确认框、场景门、交互分组和奖励为宿主替身。

球扫适配器按当前官方 Projectile 的起点后退、末端余量、SphereCastAll、距离排序、阵营和 GameObject 去重语义执行，再调用真实官方 receiver；它没有执行完整 Projectile 类，也没有证明真实游戏 PlayerLoop 的任意前后帧顺序。此回归是 Unity Editor 隔离播放模式 L2；其余物理世界、游戏暂停/存档与玩家操作仍需 L3。

官方反编译 ExplosionManager 同时引入 System 与 UnityEngine，原文的 Object 在隔离编译时有名称歧义；prepare 只向复制件加 `using Object = UnityEngine.Object;`，不改任何方法体，`compile-adaptations.json` 保留原源与适配后 SHA-256。官方只读目录不写入。

`--negative-probe` 只在隔离副本里删除快递实体材质，要求回归因材质/粉色像素失败；结束后按字节恢复并核对 SHA-256。

`--tongue-negative-probe` 在隔离副本恢复旧的 32 米舌头截断，要求真实 Unity 端点断言失败；结束后按字节恢复并核对 SHA-256。两个参数可独立运行，生产文件从不变异。报告 `passed` 汇总所有断言，单项布尔字段表示该检查已执行到末尾，失败原因以 `failures` 为准。

受击体反向验证默认运行，每个 mutant 都有独立 Unity 工程：恢复旧固定包络（吞咽上/下面均须明确转红）、移除 Grow 当刻姿态应用、交换 Grow 和受击体装配顺序、移除每帧几何同步、固定世界 yaw，以及添加第二个 Collider（实际官方爆炸必须因双伤失败）。旧固定 Root 变异已移除，因为生产主动发布 Rigidbody 姿态后它属于等价变异。每项锚点必须唯一、实跑在预期断言转红，再按字节恢复并保留前/后 SHA-256。`--skip-mimic-negative-probe` 适用于仅预览；聚合入口可用环境变量 `BOSSRUSH_FIELD_VISUAL_EVIDENCE_DIR` 指定证据与隔离工程目录，不修改作者工程。

`FieldAdventureBundleBuilder.cs` 是作者工程 Editor 构建器的源本，复制到作者工程 `Assets/Editor/` 后调用 `FieldAdventureBundleBuilder.BuildAndExit`。它只构建显式指定的单个材质包，验证 GBuffer、不透明深度写入和零外部依赖，再将制品写入仓库资源目录。作者工程与 Editor 路径由 `tools/unity_project_path.py` 解析，不在夹具中固化。
