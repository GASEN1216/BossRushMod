# 联机个人统计归属回归

链接完整生产 `CoopPersonalProgressRuntimeModule`，从真实 Hurt 入口生成统计再执行 OnUpdate 汇总。覆盖同玩家切槽期间两份独立统计、失败入队后切槽重试、连续四轮 16 人成员轮换、菜园资格错槽失效、旧房间清理与事件退订。

入站 BossRushCoopMessage.Read 使用真实 BossRushCoopJson 往返 DTO；工程同时链接共享解析器与转义工具，避免直接对象强转跳过生产编码。

网络递送、Unity 数学和既有个人持久化服务使用隔离替身；此夹具检查递送时冻结的目标身份与金额，不证明真实网络及存档写入。收据去重和原生 API 分别由 CoopProtocol、CoopInstalledApi 验证，真实多人玩法仍需 L3。

独立复审增补真实 ApplyReport 应用回调：写屏障和 Store 故障时不产生部分计数，恢复后准确转发本人击杀/Boss 击杀/死亡、双向伤害和峰值。服务替身记录实际调用量；不以恒真统计方法证明交付。图鉴应用由 ContentThirdReviewFixes 链接真实采集器单独执行。
