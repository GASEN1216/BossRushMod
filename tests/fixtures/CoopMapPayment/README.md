# COOP 地图实际付款执行回归

仅 Windows。聚合入口：`python tools/run_runtime_regressions.py --filter CoopMapPayment`。无游戏进程、存档或部署动作；每次在独立临时目录真编译并运行本次 EXE，记录输入、官方程序集、Harmony、完整输出及 SHA。

直接链接生产 `BossRushCoopMapPaymentCompatibility`、付款 receipt、资产退款 partial、Mode H 退款和库存事务。逐字抽取真实 MapSelection 双入口、旧模式费用发布/清理、Mode H 取消、官方 Cost.Pay、EconomyManager.Pay/IsEnough、ConsumeItems、Combine/AddAndMerge 方法体；仅将反编译器不可编译的闭包名称规范化。

从本机实际 COOP DLL 读取并完整重放 13 个方法体：9 个 SceneNet 投票/准备/广播/延迟加载/取消方法、Entry 和 Notify 两个原生 Prefix、TryStartVote、HandleSceneVoteRequest。保留所有指令、分支、异常区、局部变量和泛型调用，只将游戏宿主类型/成员映射到隔离宿主。真实 Harmony 叠加生产前缀与原生前缀，两种安装先后均执行真实官方 Entry/Notify 方法。输出逐段报告原生方法字节数，缺少实际 DLL 直接失败。

覆盖一票扣款先于真实 BeginLoad send、三人准备、取消/不足不出发、同步扣款通知重入、扣后通知异常 Unknown、完整收据后的异常、广播异常与 rebind 不再扣、原资产/槽/角色/Core/房间/场景代次/费用数组变更、未发布费用按原资产退款、H 原退款 owner、已发布不得重建退款、同一界面锁账后第二地图不能绕过、首次/二次真实入口、guest 自有条目不发起与普通地图原生透传、host RPC 无自有付款上下文拒绝。

宿主替身提供 Unity 对象销毁语义、库存容器/角色/场景/Core、native 网络端点和远端三人成员；网络发送边界检查生产真实 prepaid 收据后才计 Begin。它们不证明 Unity 异步加载、网络投递、游戏内物品消耗或画面，L3 仍需三端实机。RPC 包无条目身份，所以远端拒绝门仅使用房主当前真实活动自有条目，不能把普通官方同目的地地图改为 BossRush。
