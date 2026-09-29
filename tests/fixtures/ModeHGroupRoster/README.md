# 鸭王杯群战：Boss 池 / 两队抽取 / 名人堂排名

通过 `python tools/run_runtime_regressions.py --filter ModeHGroupRoster` 执行，不启动游戏、不读取玩家存档。

直接编译生产的 `ModeH/ModeHGroupRoster.cs`、`ModeH/ModeHGroupHallOfFame.cs` 与 `ModeH/ModeHStateDtos.cs`；
替身只有宿主 Boss 预设表（`BossFilterEnemyPresets`）、官方预设目录（`ResolveAuditedPreset`）、本地化与 `Mathf`。

覆盖（2026-09-29 owner 改版）：
- 池 = 宿主表里能在官方目录查到 preset 的全部 Boss + 三只自定义 Boss（各一次；焚天龙皇战力 2000，另两只 1000），查不到的不进池；
- 1500 组种子 × 6 场：蓝队 3~20 人、两队合计战力差 ≤ 500、红队 1~30 人；每队同一只自定义 Boss 至多一只，官方 Boss 允许重复；
- 场次爬升（第二轮）：蓝队平均战力逐场上升、后一场不低于前一场，第 1 场人数小、第 6 场到 16 人以上；
- 名人堂编码往返、按胜场 → 净赚 → 先入堂排名、满员挤排名最末的一季，没有群战记录时仍挤最早一条。

这是 L2 逻辑回归，不证明刷怪、AI 互殴、拍铃天灾或页面在实机上的表现。
