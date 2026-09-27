# SaveFailureRecovery

针对 CR-2026-09-25-001/002/003 的 L2 执行回归。

- 真实生产逻辑：直接链接日报 Service、Persistence、Coordinator、DTO 与 codec，Mode H 押注账本、押品身份/收走/奖品计划/交付，共享槽位 store、保存协调器、节流器、JSON 解析器。生产 `SettleReservedBet` 和数值常量逐字抽取，SHA-256 写到构建目录。
- 宿主替身：钱包、背包、物品模板、Unity 对象和 ES3 缓存/磁盘。物品树快照复制 Variables，重启必经旧 GameObject/组件销毁并重建新实例。官方 `SaveFile` 抛错后 `IsSaving` 保持 true；可在准备计划、资产确认、现金结清三个写盘点注入失败。
- 覆盖：日报 Store 拒绝与物理写失败的区别、领取页失败反馈；奖品送达前/后异常、满包、部分交付、三个崩溃边界、资产采集失败、槽位切换；同型号/重复身份/旧账本、堆叠数量变化、输局删除与账本同存；v1 兼容升级与高版本写屏障。

运行：`python tools/run_runtime_regressions.py --filter SaveFailureRecovery`。

不访问玩家存档或启动游戏；不验证真实 ES3 文件原子性、Unity 帧时序、物品资源和 UI 表现。
