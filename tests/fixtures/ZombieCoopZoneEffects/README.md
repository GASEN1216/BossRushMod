# ZombieCoopZoneEffects

真实链接完整 `ZombieModeCoopZoneEffects.cs`、`CoopPersonalDelivery.cs` 及 JSON 实现，并从当前源码提取完整区域伤害、范围减速、本机减速应用和冻结参与者判定。使用真实 Harmony，把当前安装 COOP DLL 的完整 `Patch_ServerForwardRemotePlayerDamage.Prefix`、`DamageForwardPayload.FromDamageInfo` / `ToDamageInfo` IL 重放到对应宿主接口替身，没有替换其分支、参数或前缀返回值。

执行覆盖 host / guest / 未入场玩家、死亡 / 暂停 / 切槽 / run / wave / 场景变更、范围、Buff 原生转发、原生无敌分支、ACK 丢失去重、个人移动属性应用、房主死亡、短寿命确认和独立单调钟、事实与 nonce 的消费 / 超时 / 换波 / 清理。

Unity 对象和销毁、网络字节投递、时钟及最终移动属性宿主为受控替身。验证安装 DLL 的实际 IL 与生产接线，不启动游戏、不读取玩家存档，不能代替双人实机的画面、帧序与手感验证。需要 Windows 和本机 COOP / 游戏 / Harmony；缺依赖不报告为 PASS。

聚合入口：`python tools/run_runtime_regressions.py --filter ZombieCoopZoneEffects`。独立证据可用 `python tests/fixtures/ZombieCoopZoneEffects/run.py --output Build/transport-audit/ZombieCoopZoneEffects`。

持续区域子项直接提取完整 `ZombieModeTimedRunScopedRuntime`、`ZombieModeAreaTickRuntime` 与内部宿主转发，执行真实 Initialize / Update / armed / 暂停恢复；生产慢速收据绑定 host 上的实际区域组件，tick 强度与真实源寿命分开。受控网络分别执行上下行 125 ms、450/10 ms、10/450 ms、400/400 ms 与互不相干的时钟起点，逐时间采样持续减速、原时长上限、真实区域终点、销毁 / 停用、移动中心、离开、死亡、场景失效、丢 probe / receipt 和暂停重试。完整 RTT 只扣有效寿命，不假设两向时延对称；一次性效果继续保守扣完整 RTT。最终移动属性与视觉淡出仍是可观测宿主边界，不能替代双人实机。
