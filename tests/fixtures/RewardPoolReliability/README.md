# 奖励池交付可靠性

直接链接完整生产 `DailyReportRewards`、`ModeHRewardItemPool` 与 `ModeHSeedStream`，从公开发奖/抽样入口执行，不复制抽样算法。

宿主替身仅模拟官方精确筛选、Search 的降品质行为、缺资源时返回同 TypeID 空壳、Unity 已销毁对象等于 null、快递成功/回退/拒绝。生产采样面对不同合法候选枚举顺序、品质空池、缓存后资源销毁及恢复、实例化失败和投递失败。检查交付结果、实例存活和扫描次数；不访问玩家存档，不启动游戏。

边界：不模拟真实快递持久化、Mode H 押品清算事务、Unity 资源加载和实际帧耗时，这些由已有回归与实机补验负责。

```powershell
python tools/run_runtime_regressions.py --filter RewardPoolReliability
```

2026-09-19：日报直接链接共享 `BossRushQualityItemPool`，验证先由另一个调用方建池后日报不再扫描、复用同一候选数组，以及查询异常后仍可恢复。原 Mode H 断言保留；该工作区基线已有 Mode H 候选顺序和空壳检查失败，不能把综合入口称为全绿。
