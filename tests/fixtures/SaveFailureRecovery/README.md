# SaveFailureRecovery

2026-10-07：钱包替身补官方 `Pay(Cost)` 即使禁用现金仍在 `IsEnough` 相加账户与现金的溢出语义；以全 long 余额、正现金物品和低于 1 的冻结返还倍率执行真实 Commit，验证只扣本金、现金不动、一次结算，普通金额仍调用官方付款。移除生产中的溢出分支会实际转红。

同日合并远端修复：保留物理写盘异常后 private `saving` 闩仍为 true 的官方替身，由生产 `RunSaveFile` 释放本次同步调用留下的闩；`FailAt` 从指定次数起持续失败，避免同次结算中的后续尝试把故障用例意外变绿。该保护与本地键写 / 回读故障的 store 替换同时执行，互不清除对方的现金或实物义务。极值押注保留先调用 Pay 的更窄回退，原远端测试改为核对“尝试一次 Pay、实际扣款一次”，金额、背包现金和重复结算断言全部保留。

针对 CR-2026-09-25-001/002/003 的 L2 执行回归。

- 真实生产逻辑：直接链接日报 Service、Persistence、Coordinator、DTO 与 codec，Mode H 押注账本、押品身份/收走/奖品计划/交付，共享槽位 store、保存协调器、节流器、JSON 解析器。生产 `SettleReservedBet`、仓库桥 `TryDetachAt` / `TryAddAtEmpty` 和数值常量逐字抽取，SHA-256 写到构建目录。
- 宿主替身：钱包、背包、仓库物品摘要、物品模板、Unity 对象和 ES3 缓存/磁盘。物品树快照复制 Variables，重启必经旧 GameObject/组件销毁并重建新实例。官方 `SaveFile` 抛错后私有 `saving` 保持 true；生产受控调用释放本次失败的闩，可在准备计划、资产确认、现金结清三个写盘点注入失败。仓库 AddAt / RemoveAt 可在实际变更后抛通知异常，验证托管引用及已交付奖励不会丢失。
- 覆盖：日报 Store 拒绝与物理写失败的区别、领取页失败反馈；奖品送达前/后异常、满包、部分交付、三个崩溃边界、资产采集失败、槽位切换；同型号/重复身份/旧账本、堆叠数量变化、输局删除与账本同存；扣堆叠与移出容器的通知异常按实际物品状态结账，避免物品和现金双扣；v1 兼容升级与高版本写屏障。

运行：`python tools/run_runtime_regressions.py --filter SaveFailureRecovery`。

不访问玩家存档或启动游戏；不验证真实 ES3 文件原子性、Unity 帧时序、物品资源和 UI 表现。

2026-10-06：自选押注移除 20,000 固定上限，覆盖钱包及真实返还倍率设限、锁盘复验、只扣本金、赔率与队伍变动、重启后的冻结赔付、旧 v1/v2/v3 账本兼容、损坏金额/倍率写屏障、零至 long 极值、校准与统计防溢出、0..10000 滑条单调性及精确端点。小额赢注按原十位取整返还 0 时仍保存真实胜负；逐字执行生产金额与倍率格式化，验证冻结值和 long 两端可显示。拖动中的布局、指针操作与真实 Unity 保存时序仍需实机。

2026-10-07 独立复审：现金锁盘和结算分别注入一次键写失败 / 回读失败，执行真实 journal Tick 恢复，核对本金、冻结分数、赔付及状态同批保存，重复调用和重载不重扣、不重赔。物品奖品已交付后再使回读失败，核对资产快照与剩余义务没有被 replacement 的首次订阅清空，重建旧物品树后不重发；换槽只读新槽，旧 accepted 记录不迁入。旧实现实跑转红于“journal tick recovers the accepted reservation and cash obligation after transient key failure”，日志 `Build/runtime-regressions/ModeHCashRecovery-before.log`。

同轮修正钱包替身：官方 `Pay(Cost,true,false)` 的前置 `IsEnough` 仍固定计入背包 Cash，使用未检查的 `Money + Cash`。新增 `OfficialEconomyContract` 只读现装 Core DLL 的 IL 核实此条件及 `Add(long)` 的有符号 setter 路径，按 `GAME_PATH` 或 `Build/BossRush.rsp` 寻址。钱包为 long.MaxValue 且携带 1 现金时，旧实现在合法的低于 1 倍返还下仍拒付，现仅该拒付且余额未变时使用官方 Add 扣账户；测试覆盖常规支付、普通拒付、扣款后返回 false、扣款通知抛错，防止回退重复扣款。旧实现转红日志 `ModeHOfficialPayOverflow-before.log`；本组仍不执行 Unity 中的实际钱包实例。

2026-09-29 补入生产 `OnStart` / `HandleLevelReady` / `ReconcileCashBetOnRestore`：模拟主菜单对账时钱包不存在、关卡就绪后钱包出现，覆盖晚启动、重复就绪、已结束赛季的待结赢注及切槽隔离。赛季 DTO / 关卡就绪标志为宿主边界，实际退款和结算仍执行生产账本。

2026-09-29 发布审查补入押品嵌套：锁盘前把已选子物品放进已选容器，只留下选择页可见的直属根物品并计价一次；锁盘后把两件独立押品嵌套，交换选择顺序验证后代先收走、没有重复扣现金，重启后不重复收取。`GetAllChildren` 替身按官方 `includingGrandChildren` / `excludeSelf` 参数语义枚举，销毁容器必经子物品销毁。

同次复审逐字执行 `TryResolveCashBetBeforeAbandon` 与共用战报查询：钱包未就绪不得放弃，恢复后未决押注退本金、已存胜负按原结果结算；满包保留已决实物欠账，腾空间后才放弃；已存赢注因钱包上溢结算受阻时，也不能改成较小的本金退款。该夹具验证财务前置条件，真实放弃入口收到 false 后不归档、不释放 owner 由 `ModeHRecoverySecondReview` 验证。
