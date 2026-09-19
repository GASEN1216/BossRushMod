# 套装延时技能生命周期回归

直接链接完整生产引雷术、冰葬源码，逐字提取生产主角死亡分派器与激活代数推进方法；另读取能量盾、雷电戒指、冰霜套、雷霆套的完整 OnHurt 方法，执行其生产 OnDead 并检查致死后的 OnHurt 早返位于所有效果之前。
验证旧激活的延时协程不得清除新链的在飞标志/命中集合、当前链仍能正常清理，
以及主角死亡后两种延时伤害均作废；四个装备/套装的死亡状态复位均执行精确生产 OnDead，致死 OnHurt 顺序由生产方法体探针核对。Unity 调度、扫描、表现和生命外围使用内存替身，
不代表游戏内特效或战斗体验已通过。四个生产源码文件哈希写入 `Build/set-bonus-coroutines/production-death-hurt-sources.sha256`。

运行：`python tools/run_runtime_regressions.py --filter SetBonusCoroutines`。生成文件、编译产物和提取哈希仅写入 `Build/`。

2026-09-19：追加累计 3 次直接击杀门、效果伤害/无效目标不计数、冷却、目标上限/半径/实际伤害、死亡重置累计与雷击不续跳的执行断言。

能量盾另外逐字提取完整 OnHurt/GetFacing/IsFrontalAttack，直接执行真实分支；Health.AddHealth、FX.PopText 与向量运算使用内存替身，验证 30% 回补、25 HP 上限、0.5 秒冷却、瞄准转向、满血钳制后的实际飘字、致死与未佩戴早返。
