# 截信人个人扣物事务执行回归

聚合入口：`python tools/run_runtime_regressions.py --filter WaylayerEscrow`。

逐字抽取生产 `CoopWaylayerEscrow`，及首领的 `BestTarget`、`GiveBack`、`AcceptCoopSnatch`、`PublishCoopSnatch`、`CompleteCoopSnatch`；直接链接 `CoopWaylayerJournal`、共享槽位 store、保存协调器与真实 JSON codec，记录源码哈希，执行本次新建 TargetPath 与 SHA-256。覆盖最贵耗材选择、整堆仅一件、房主库存不受客人劫包影响、重复消息、冻结原物、工厂 await 后切档/销毁/同图重载、实际入包与退款失败重试。

Unity 对象销毁、库存操作、官方 ItemTreeData 序列化/异步工厂和网络发送是受控替身。替身明确提供失败或通知异常，工厂通过 TaskCompletionSource 暂停真实生产续作；不预填非空 prefab，测试显式登记资源。个人收据的传输与去重由 CoopProtocol 的独立生产回归验证，本夹具只捕获收据并实际执行生产应用回调，避免恒真发奖替身。它不验证网络延迟、真实背包事件、尸体箱拾取或游戏实机效果，属于 L1/L2。

发送替身经真实 `BossRushCoopJson` 往返嵌套物品树与变量列表；直接链接共用 token/parser/转义工具，没有自制 codec。可变存储替身区分缓存与实际文件，能在写前、写后和回读阶段报错；仅测试专用内存存储，不读写玩家存档。

Claude PC-07 的完成交易跨 27 次代次更新仍只登记一次 `Deliver`。PC-06 原数量 3→4 的实际失败证据保存在 `Build/coop-independent-audit/personal-claude-followup/pc06-blocker/`；本轮修复另外留证，不覆盖旧红。

PC-06（SCHEMA+ / WIRE+）在自有 key `BossRush_CoopWaylayerJournal` 保存存档 GUID、单件完整原树和原房主终态。未知结果断房、换房或销毁不退款，重连相同房主存档并经过当前原生 host 来源认证后核账；明确失败只返还原件一份，成功只终结一次。玩家在真实未结事务离房/重载时收到一次中英提示，结清后提示一件物品的去向。正常即时成功保留原招式、首领包、死亡尸体箱和四成血丢物行为。

旧盘重载只排除同一精确来源的一份，保留同类型同属性其它原物；覆盖整堆、完整附件树、移动/合堆、出击中新获得而旧盘不存在的原物、自然保存已扣量后的排除收据、原槽加载间隙与异步旧槽续作。正常角色保存 Hook 只附带本人原树收据，不主动采集或保存整背包。全新无 journal 的原生首载先持久自有 GUID 再贴 owner；首次写盘失败不贴临时 GUID，仍保留独立 GUID 的原物来源。

房主实际 `Add` 原物后依次确认 custody、公开屏障和终态；公开前同帧撤销必须销毁实物并持久拒绝。第二、第三次写后异常叠加确认不可读时不公开、不发终态；IO 恢复、换图和重启原房主后可明确失败并返还一份。终态确认失败但已留下真实可取物时保留实际物品重试，不克隆第二份。正常成功物品被合法取走后，持久成功仍能结清。严格复合故障边界：如果物理写异常、回读失败和强杀恰落在公开屏障与撤回拒绝写盘之间，重启的 `Custody/PublicationStarted` 不能证明物品是否另存；当前保留待核账，不谎称已自动消除该窗口。原房主存档永不再可用时也没有其它房间代判退款的权限；人工复制账主存档视作同一身份。

周期处理使用未结债索引，已交 PersonalDelivery 的房主交易退出活跃表，终态持久墓碑保留。重载 5000 条终态历史后 10000 轮双端真实 Tick 的发送、物理保存、活跃表和工作表均为 0；记录实际线程分配量，允许 CLR 的一次性编译噪声，禁止历史数量级分配。身份已确认时不发 heartbeat，实际房间、槽位、asset、ReadyEpoch、native service/host peer 变化或显式 host challenge 恢复握手。历史持久墓碑仍线性增长，实际新交易的整份 journal 编码/写入随历史增长；本夹具不证明无限历史性能。

`run_harmony.py` 另用实际安装 Harmony，在可变受控原生签名和已核 IL 顺序上执行三生产 Hook。验证同步 LoadItem→InstantiateAsync 输入域、原任务未包装、首个 await 前域已退订、其它 key/直接工厂不受影响、正常 Save 收据与全新槽首载。原生 `UniTask<Item>` 类型与三 Hook 通过实际游戏程序集 C# 7.3 API 编译；当前桌面 .NET Framework 缺 Unity Mono 的 IValueTaskSource，而 .NET 8 与安装的旧 Harmony 不兼容，因此不能把受控 Harmony 绿写成安装游戏内三 Hook 绑定已绿，真实绑定仍待 L3。
