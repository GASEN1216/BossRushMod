# 鸭王杯联机执行回归

执行完整生产 `ModeHCoopLedger.cs`，以及从当前源码提取的房间命令、准备屏障、开战收据函数。覆盖 16 人准备、场景屏障、断线、旧对阵拒绝、阵容冻结、个人选择恢复、付款失败、收据重放、押品弃赛与个人名人堂。

替身只承担房间传输、Unity、存档接口和原押注服务；不证明真实联机传输、钱包存盘或游戏画面。原现金与押品服务由已有 Mode H 回归覆盖。

用 `python tools/run_runtime_regressions.py --filter ModeHCoop` 运行，显式构建并执行本次 TargetPath，记录产物与生产源码 SHA-256。

2026-10-13 追加原房主资产换槽后的共享 owner、个人弃赛和 Season 写入门、重入角色观战租约、已死未选择房主与加载中 null 的区别。当前 48 条断言覆盖房主加 15 位客人准备仍可前进；个人凭证使用真正联机 codec，嵌套赔率和结果不再以 System.Text.Json 替身充当生产编码。

本轮 2026-10-13 边界补充：新增真实 TickCoop 与 ReleaseCoopProjection 的跨原生房间/StopNetwork 回归，释放旧 spectator/运行态后才能处理新 room 或离线帧。角色、镜头和 UI 对象仍由明确的引擎适配器承接，不能据此声称真实游戏镜头正确。
