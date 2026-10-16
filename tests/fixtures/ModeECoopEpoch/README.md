# Mode E 原局永久奖励与交付

运行 `python tools/run_runtime_regressions.py --filter ModeECoopEpoch`。入口每次明确构建并执行本次 TargetPath，真实 codec 与源码哈希随日志保存。

逐字提取生产个人收据、原资产 epoch 账本、房主累计击杀发布、每 10 次挑衅烟雾弹与仓库树身份去重，同时链接真实 `TryGiveItemToPlayerOrDrop` 和 `SkyIslandInventoryTransaction.TryDeliver`。覆盖 16 个原参与者、换槽、迟到旧收据、乱序、部分交付重试、工厂/保存失败、20 次对应 2 件、通知异常、真实合堆差额与原生入包/Drop 提交后异常。

宿主替身只承接 Unity、原生物品/仓库与支付前后状态；AddItem、合堆、Drop 刻意模拟官方提交早于通知的时序。原生物理背包、真实 UI、Mono、磁盘写入与跨进程连接不由本夹具证明。停止网络/换房执行真实 projection owner 退出方法，EndModeE 的引擎收尾由适配器记录调用；完整生命周期在 AuditModeLifecycle 等夹具另外链接。
# 2026-10-15 追加玩家阵营与 Dev 身份边界

逐字抽取生产 Remember/Set/RestoreCoopModeEPlayerTeam、实际 BossRushCoop.GetPlayerId、F3 ValidationCountHostileCharacters 与 IsAutotestHostileWithin。新增覆盖三端原队伍分别归还、重复快照、NPC 不取得租约、换角色/换房间不写新 owner、暂时解绑仍归还原实例、原实例被另一玩家接管时拒绝修改、断线清 roster 并复位 LocalId 后恢复、外部团队变更、SetTeam 失败后共享模式状态复位仍能重试、Unity 已销毁对象，以及两个 scav 玩家被准确排除但同队伍 NPC 仍被计数及允许成为真实敌人目标。团队表与角色身份为有状态宿主边界；并未加载真实场景或执行网络。

EndModeE 的完整生命周期仍由真实游戏与接线守卫补充，本夹具中的 End 仅为投影退休用的宿主替身，不将其当作完整生产 End 已执行的证据。新守卫独立钉住真正 End 的归还调用与原单机回退分支。
# 2026-10-15 直连烟雾世界投影

`SmokeWorld` 子组逐字执行生产烟雾 DTO、发布、接收、Play（包括实际 Instantiate 调用点）与个人 utility 退款方法，消息字段经过真实 codec。HasBoundScene/SceneToken 是默认空且可独立变化的边界，实际生成与 Core 绑定由 CoopInstalledApi/SceneIdentity、CoopSession 独立证明。保持公开 generation 不变时验证未绑定及旧 token 不实例化，正确 token 不被旧包 Guid 墓碑吞掉；原房/原生 host/代次校验继续保留。Unity Instantiate/物品交付是可记录、可失败的端点，未执行真实渲染。

退款用例在未绑定世界、原房退出、错资产及交付失败后重试：原资产仍足额偿还一次，不借世界门丢弃个人欠账。新增 guard 反例须在稀疏副本实际红/字节恢复；普通入口仍运行原个人/队伍用例与本子组，不以单独绿项替代整组。

周期重报子夹具 RepeatCallers 逐字提取真实 E TickCoopUtilities/TickCoopCoal 和 F TickCoopFortificationTransactions，链接完整真实 Channel、codec、解析器与可变 Core/连接边界。1000 次实际周期 Tick 保留 4 条 E、2 条 F 原事务，新增 ID 足额增加，退款端点持续调用；绑定后全部实际分发与 ACK，ACK 后仍允许业务补报。刷新/退款实现只作记录端点，未替代生产队列或去重判据。父组当前 102 条、烟雾 11 条及此子组 6 条均有本次新编译 TargetPath/SHA。
