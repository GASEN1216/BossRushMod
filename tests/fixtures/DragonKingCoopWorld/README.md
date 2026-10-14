# 龙皇多人世界伤害与个人击退回归

经 `python tools/run_runtime_regressions.py --filter DragonKingCoopWorld` 执行。每次逐字提取当前生产碰撞、光束、岩浆、手动投射物、长矛、永恒彩虹、冲击波及击退 RuntimeModule，直接链接真实 `CoopPersonalDelivery`、`BossRushCoopJson` 与共享 JSON 解析器。来源 SHA-256、独立输出目录及本次程序集路径随运行留下。另提取女巫真实查询与诅咒领域伤害，验证大于 32 个碰撞体的查询和接收体去重。

可变替身只提供角色、物理查询结果、网络传输、时钟、角色装备属性和官方强制移动调用。覆盖 16 人单独命中与冷却、同人多碰撞体、友军及死亡过滤、40 个 AI 目标扩容、600 个碰撞体兜底、常驻弹幕打全体而普通弹幕消费一次、闪避、房主死亡后仍有活人、无 COOP 与阵营分支。个人击退使用生产收据和真实 JSON 往返，覆盖重复、乱序、第一次生命握手、死亡后同对象复活、替换角色、错存档、同图重载、旧房间及 runtime 清理。静态岩浆缓存复位执行真实入口，并核对后继实例可重建。

女巫领域用 16 人各 4 个碰撞体验证实际查询扩容、按 DamageReceiver 去重、逐人诅咒与友军排除；客端世界权威早返也实际执行。这里没有真实 PhysX、Harmony 顺序、原版角色插值、Unity 主线程调度或音画。L2 能证明受控输入驱动当前生产逻辑，不能据此声称真实联机的移动、伤害和帧耗已通过。

收尾新增实际 CoopBossCreationLease、个人冲击波 scope/KnockbackPlayer 与龙裔 ApplyCollisionDamage。旧实体权限 API 是可控边界（可显式 denied），实际 HasEntityAuthority 永久标记/房间规则由 CoopAiPrediction 的真实 helper/Harmony 回归独立证明；本夹具验证受击入口确实调用该边界。个人冲击波按 client 合法实际 SetForceMoveVelocity、Stop 后无推力、重连不复活、换房和替换身体逐项执行。
