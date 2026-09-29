# 鸭王杯群战：Boss 池 / 两队抽取 / 名人堂排名

通过 `python tools/run_runtime_regressions.py --filter ModeHGroupRoster` 执行，不启动游戏、不读取玩家存档。

直接编译生产的 `ModeH/ModeHGroupRoster.cs`、`ModeH/ModeHGroupHallOfFame.cs` 与 `ModeH/ModeHStateDtos.cs`；
替身只有宿主 Boss 预设表（`BossFilterEnemyPresets`）、官方预设目录（`ResolveAuditedPreset`）、本地化与 `Mathf`。

覆盖（2026-09-29 owner 改版）：
- 池 = 宿主表里能在官方目录查到 preset 的全部 Boss + 三只自定义 Boss（各一次、战力固定 1000），查不到的不进池；
- 3000 组种子：左边人数覆盖 3~20 两端；右边与左边合计战力差 ≤ 500、人数 1~30；每边同一只自定义 Boss 至多一只；
- 名人堂编码往返、按胜场 → 净赚 → 先入堂排名、满员挤排名最末的一季，没有群战记录时仍挤最早一条。

这是 L2 逻辑回归，不证明刷怪、AI 互殴、拍铃天灾或页面在实机上的表现。
