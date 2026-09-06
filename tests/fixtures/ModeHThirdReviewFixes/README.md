# Mode H 第三轮修复回归

兼容分类：`COMPAT`。覆盖 CR-2026-09-06-007 / 008。

在仓库根目录执行：

```powershell
python tests/fixtures/ModeHThirdReviewFixes/run.py
python tests/ModeHThirdReviewFixesGuard.py
```

每次运行直接编译当前生产 `ModeHRealStakeService`、`ModeHRuntimeModule_SettlementFlow`、配置、DTO、run owner、状态机和虚拟筹码实现；另从当前源码逐字提取恢复、赛季投影、奖励应用与 profile 克隆方法。生成源码、SHA-256、工程和日志只写 `Build/modeh-third-review-fixes/`，不接触玩家数据。

59 条执行断言覆盖押品 Prepared 到 SettlementPending 六个阶段、退款四个失败点、初次生成失败、直接恢复、ManualIntervention、已提交结果、异 owner、空选择和旧终态；战痕接受、拒绝、显式替换、无效替换、冷恢复、写入前失败、写入成功但返回失败、陈旧 callback、跨场延期及最后一场收口。候选与处置 token 通过生产投影进入原有 `appliedEventTokenIds`，不新增持久 DTO 字段。

明确隔离的边界：Unity/UI、仓库实物和 journal 的磁盘提交、赛季写盘、地图路由。赛季用测试 JSON 完成字段往返，用前写失败/后写失败模拟结果不确定；这证明收益与处置凭据不分离，不等同于官方 JsonUtility/ES3 实机验收。真实满仓返还、切槽、六场赛季与 AI/协程时序仍需实机验证。旧版无处置凭据的候选保留原记录并解释无法确认是否领取，不猜测并补发收益、不阻断赛季；已持有的旧候选不会被当成未领取。反复查看/保存不补造 offered 或 resolved 历史。

专属结构 guard 另有 13 个反向变异；执行夹具与结构 guard 互补，不能代替 Windows 正式编译。
