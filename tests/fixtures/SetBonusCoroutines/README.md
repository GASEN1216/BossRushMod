# 套装延时技能生命周期回归

直接链接完整生产引雷术、冰葬源码，逐字提取生产主角死亡分派器与激活代数推进方法；另读取能量盾、雷电戒指、冰霜套、雷霆套的完整 OnHurt 方法，执行其生产 OnDead 并检查致死后的 OnHurt 早返位于所有效果之前。
验证旧激活的延时协程不得清除新链的在飞标志/命中集合、当前链仍能正常清理，
以及主角死亡后两种延时伤害均作废；四个装备/套装的死亡状态复位均执行精确生产 OnDead，致死 OnHurt 顺序由生产方法体探针核对。Unity 调度、扫描、表现和生命外围使用内存替身，
不代表游戏内特效或战斗体验已通过。四个生产源码文件哈希写入 `Build/set-bonus-coroutines/production-death-hurt-sources.sha256`。

运行：`python tests/fixtures/SetBonusCoroutines/run.py`。生成文件、编译产物和提取哈希仅写入 `Build/`。
