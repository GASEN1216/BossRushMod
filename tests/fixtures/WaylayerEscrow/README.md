# 截信人个人扣物事务执行回归

聚合入口：`python tools/run_runtime_regressions.py --filter WaylayerEscrow`。

逐字抽取生产 `CoopWaylayerEscrow`，及首领的 `BestTarget`、`GiveBack`、`AcceptCoopSnatch`、`SuspendCoopSnatch`、`PublishCoopSnatch`、`CompleteCoopSnatch`；直接链接 `CoopWaylayerJournal`、共享槽位 store、保存协调器与真实 JSON codec，记录源码哈希，执行本次新建 TargetPath 与 SHA-256。覆盖最贵耗材选择、整堆仅一件、房主库存不受客人劫包影响、重复消息、冻结原物、工厂 await 后切档/销毁/同图重载、实际入包与退款失败重试。

Unity 对象销毁、库存操作、官方 ItemTreeData 序列化/异步工厂和网络发送是受控替身。替身明确提供失败或通知异常，工厂通过 TaskCompletionSource 暂停真实生产续作；不预填非空 prefab，测试显式登记资源。新 protocol3 claim 收据直接链接完整生产 CoopPersonalDelivery，仅类型名重命名以兼容旧 Decision 观测替身，真实执行 Deliver/Self/Receive/ACK/Tick 与故障重放；旧 Decision 替身只捕获收据并执行生产应用回调。它不验证网络延迟、真实背包事件、尸体箱拾取或游戏实机效果，属于 L1/L2。

发送替身经真实 `BossRushCoopJson` 往返嵌套物品树与变量列表；直接链接共用 token/parser/转义工具，没有自制 codec。可变存储替身区分缓存与实际文件，能在写前、写后和回读阶段报错。普通用例采用隔离内存存储；`CrashCases` 在本次 TargetPath 同目录新建专用文件，实际读写 journal JSON，从不访问玩家存档。

Claude PC-07 的完成交易跨 27 次代次更新仍只登记一次 `Deliver`。PC-06 原数量 3→4 的实际失败证据保存在 `Build/coop-independent-audit/personal-claude-followup/pc06-blocker/`；本轮修复另外留证，不覆盖旧红。

PC-06（SCHEMA+ / WIRE+）在自有 key `BossRush_CoopWaylayerJournal` 保存存档 GUID、单件完整原树和原房主终态。未知结果断房、换房或销毁不退款，重连相同房主存档并经过当前原生 host 来源认证后核账；明确失败只返还原件一份，成功只终结一次。玩家在真实未结事务离房/重载时收到一次中英提示，结清后提示一件物品的去向。正常即时成功保留原招式、首领包、死亡尸体箱和四成血丢物行为。

旧盘重载只排除同一精确来源的一份，保留同类型同属性其它原物；覆盖整堆、完整附件树、移动/合堆、出击中新获得而旧盘不存在的原物、自然保存已扣量后的排除收据、原槽加载间隙与异步旧槽续作。正常角色保存 Hook 只附带本人原树收据；仅 PC06 原件领取、追回或明确退款按 owner 本次明文授权增加一次官方角色物品保存屏障，不触及 Health、仓库或 CollectSaveData。全新无 journal 的原生首载先持久自有 GUID 再贴 owner；首次写盘失败不贴临时 GUID，仍保留独立 GUID 的原物来源。

新事务使用 PublicationProtocol=3，首领首次 Add 前将完整根/子树锁定为领取凭证，真实入包后暂存，仅耐久 Success 确证后重新公开。世界凭证不能直接进入玩家包、插槽、合堆或 Drop fallback；真实拾取/库存入口向房主请求一次领取。房主依据实际 transport 发送者、当前角色/asset 与持久 journal GUID 唯一冻结 claimant；两个实际 peer 声称同一 journal GUID 时拒绝冻结。位置依据 ActiveAgent 或真实首领/尸体容器锚点，不用道具初始 transform 坐标。

领取复用真实个人递送，先耐久单件 Grant，再在一个精确 Item→实际本人 pack→单次 AddAt 范围入包；同步通知的 Detach/Drop/Plug/Combine/二次 AddAt/异目标 AddItem 都拒绝。await 前后、通知后重核实际身体存活、asset、journal 与角色租约。通知已入包后抛异常保留同一原件，释放残留原生 agent；死亡保留同一私有锁定原件与核账义务。原生保存重载依据实际角色根 Returned proof 或树中的 GrantKey 识别已拥有的原件，并清除根/子件私有锁，不因个人 ACK 丢失再造。

领取与明确退款在实际入本人包、原生通知结束且租约仍有效后，通过 `ItemSavesUtilities.Save(...,"MainCharacterItemData")` 与共享每帧保存门写 `Item/MainCharacterItemData`，再用 ES3.File 读取完整当前物品树逐字比较；同批 Returned proof 与原件必须同时存在。写前失败、写后确认失败、readback 回调切槽/死亡/换角色根/销毁原物时均保留私有锁，不 ACK、不解锁，不再 Add 同实例触发第二次通知。下帧以同实例重试；一帧只放行一份角色保存。直接读回后及最终 NoteReturned 前均重核 owner/slot/path/asset/当前活体/同一根与 exact 包归属。

Build-only `pc06-claim-crossasset-before-03`（原3→总4）与 `pc06-refund-crossasset-before-01`（原1→总2）保留新增屏障前的独立进程红；存储与原生库存操作是受控 seam。屏障后 `pc06-native-publication-process-03` 真实强杀写进程并由新 PID 读取本次实际 JSON 原生树文件，领取与退款均为本人3/世界0/journal factory重造0，文件同时含原件及 Returned。此处证明生产逻辑跨进程持久化，不能写成游戏内 ES3/Unity 实测。

另留存屏障后普通后续转移的 L2 旧盘回滚对照 `pc06-claim-crossasset-with-barrier-01` 与 `pc06-native-control-crossasset-01`：两者只执行受控 Inventory/Drop/文件边界，没有执行安装 COOP 的完整原生转交链。不得据此宣称实际 COOP 已复制。普通无 metadata 实物与已发布 PC06 原物的真实 Drop→Pickup→另一端保存→强杀来源→同档原盘重载，均是仍未采样的 L3 必测门；不据此擅自引入所有后续转移协议。

未确认发布的独立 Grant/whole refund 若其原身体死亡，严格同 journal+asset、新活体与 exact oldPack 条件下，通过原生 RemoveItem 与精确 Admit 将同一私有原件移到新身体；通知改变租约则停止，绝不 factory 复制仍存活的旧原件。原堆加一退款不自动搬整堆；覆盖保存根本未写入→死亡→同档实际旧树加载，原件总量恢复且无锁/无再次退款。实际尸体同步与死亡时序仍待 L3。
旧 protocol0 ambiguous 与 protocol2 已公开 Success 不重造、不猜退款、不改语义。新 protocol3 的 durable Success 只恢复锁定凭证，ClaimId 已冻结的记录由个人递送恢复，绝不重新公开另一份凭证。未公开且可证明拒绝的 Custody 保持实际原件退款路径；老未知仍必须原房主核账。

跨进程用例真实启动本次新编译的写进程，在 publication、success-before、success-after 三窗口强杀，再用全新读进程恢复。前两处得到 Failure/总量3；success-after 真实恢复一个锁定原件凭证、领取至本人总3且世界0，再把正常保存的实际原生树交第三个新进程重载，仍总3/世界0。两种宿主都在子进程 args 分派前安装各自真实门：net8 用受控原生边界的 inline seam，Windows Framework 用安装 Harmony 与完整生产各 prefix。最新分别330与346断言；这一数量只描述当前离线采样，不代表实际 Unity 已可发版。

原生 ItemTool.BuildItemFromSnapshot 与官方 InteractableLootbox.CreateFromItem 的完整 IL 接线由独立 WaylayerNativeBuild 夹具验证，本夹具不以简化 Inventory 替身冒充完整原生方法。真实 Unity 进程强杀、首领/尸体箱网络同步、正常拾取与保存重载仍需隔离 slot3 的 L3；当前不可声称游戏 PC-06 可发版。

`ThrowAfterDisk` 模拟原生保存已退出后的通知异常。实际官方 `SaveFile` 在备份或 `ES3.StoreCachedFile` 内抛错可能留下 `IsSaving=true`（函数没有 finally）；共享 RunSaveFile 只在本次同步调用抛异常后恢复其留下的 saving 闩，仍保留原异常与待办，不接管别的原生保存；当前原件屏障失败重试与重启核账均有离线覆盖。

周期处理使用未结债索引；protocol3已领墓碑按持久 owner 建一次性索引，仅实际身份握手/本机租约变化重建投递，个人递送队列负责重试；5000条protocol3已领记录重载后10000轮双端Tick，active worlds、发送、保存与线程分配量均为0。已交 PersonalDelivery 的房主交易退出活跃表，终态持久墓碑保留。重载 5000 条终态历史后 10000 轮双端真实 Tick 的发送、物理保存、活跃表和工作表均为 0；记录实际线程分配量，允许 CLR 的一次性编译噪声，禁止历史数量级分配。身份已确认时不发 heartbeat，实际房间、槽位、asset、ReadyEpoch、native service/host peer 变化或显式 host challenge 恢复握手。历史持久墓碑仍线性增长，实际新交易的整份 journal 编码/写入随历史增长；本夹具不证明无限历史性能。

`run_harmony.py` 另用实际安装 Harmony，在可变受控原生签名和已核 IL 顺序上执行三生产 Hook。验证同步 LoadItem→InstantiateAsync 输入域、原任务未包装、首个 await 前域已退订、其它 key/直接工厂不受影响、正常 Save 收据与全新槽首载。原生 `UniTask<Item>` 类型与三 Hook 通过实际游戏程序集 C# 7.3 API 编译；当前桌面 .NET Framework 缺 Unity Mono 的 IValueTaskSource，而 .NET 8 与安装的旧 Harmony 不兼容，因此不能把受控 Harmony 绿写成安装游戏内三 Hook 绑定已绿，真实绑定仍待 L3。
