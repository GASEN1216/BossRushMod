# Boss 生命比例执行回归

链接完整 `Utilities/BossStatScaling.cs`，提取原样的 Config 默认字段、比例 getter 和宿主缩放入口。测试 10% / 100% / 200%、越界钳制、普通敌人排除、伤害与 AI 反应不变、旧全局倍率叠加和征程显式二次缩放不重复应用比例。

Item / Stat、Health 与 AI 为无 Unity 引擎的替身；不证明实际游戏中的数值、技能或性能。运行 `python tools/run_runtime_regressions.py --filter BossHealthScaling`。

另提取真实 `ApplyModeDWaveScaling`，覆盖 50% / 100% / 200% 的第 2 波增量只缩放一次、实际当前血量与上限一致，以及后装备的加法生命和重复调用幂等。

独立复审补入 `ApplyDescendantStatReduction` 与真实召唤降属性常量，覆盖龙皇护卫龙裔在 10% / 50% / 100% / 200% 配置下减半基础数值后仍以实际满血登场。Health 替身按官方 `SetHealth` 钳到当前 MaxHealth；隔离副本恢复旧的 `SetHealth(newHealth)` 后，200% 用例实际报 `500 != 1000`，副本按字节还原并核对 SHA-256。
