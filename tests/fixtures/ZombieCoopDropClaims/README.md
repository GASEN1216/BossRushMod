# 丧尸普通共享掉落申领执行回归

聚合运行：`python tools/run_runtime_regressions.py --filter ZombieCoopDropClaims`。单独证据目录：`python tests/fixtures/ZombieCoopDropClaims/run.py --output Build/drop-claims-audit`。需要 Windows、.NET SDK、当前安装的 COOP / 游戏 / Harmony 程序集。只执行隔离进程，不启动游戏、部署或读写玩家存档。

直接链接 `ZombieModeCoopDropClaims.cs`、`CoopPersonalDelivery.cs` 和真实网络 JSON 管线。抽取官方 `CharacterMainControl.PickupItem`、`CharacterItemControl.PickupItem`、`ItemUtilities.SendToPlayerCharacterInventory` / `AddAndMerge`、失败 `ItemExtensions.Drop`、`Item.Combine` / `OnDestroy`、`ItemTreeExtensions.DestroyTree` 的完整方法体，以及生产 Transport 的 `NetworkDropScope`、`NetworkDropLease`、Capture / Complete / Protect / BeginScope / FindType / Read。仅把反编译 AddAndMerge 的非法缓存临时变量名换成合法名并补其隐式 null 初始化；不改分支和业务语句。每次构建独立 TargetPath，记录源码、安装 DLL 与执行文件 SHA-256。

通过 Harmony 把当前安装 COOP 的 17 个完整方法 IL 映射到显式宿主端点，实际执行 CharacterPickupPostfix、ItemDrop Prefix / Postfix、拾取静默前缀 / 终结器、ItemNet 的 Client_RequestPickup / Server_HandleLocalPickup / Server_HandlePickupRequest / Client_HandleDespawn / Server_RegisterLocalDrop、DropSyncRegistry 的登记 / 双向查找 / 注销，以及 RpcContext 的三个 getter。外层原生 pickup postfix 挂在实际 CharacterMainControl 方法，内层申领前缀挂在 CharacterItemControl 方法；不以常真 Capture / Complete 替身证明世界退役。

当前执行 217 条断言，覆盖两客同时抢与房主本人竞争、完整正常通知一次、丢 ACK、迟到授权、断线、换角色资产、原角色恢复、明确失败重落地、带 Slots 物品失败时不额外请求网络 Drop、异常时保守锁定、旧失败 nonce / 迟到 grant / 丢结果及结果 ACK 重放、主客合堆 Unity-null、入包 / 装备后通知异常、原生 postfix 发请求后异常、重复原生 despawn 后库存活物与数量守恒、无标记物品直接走原方法。主客两种部分合堆均经官方完整方法先合入 3 个、满包失败，再经原生登记生成 2 个余量快照供另一客领取，总库存 7+5=12，不因丢结果 ACK 重复生成。冻结 lease 分别验证 room / generation / token / registry / entry / item / ItemNet / service 的身份变化；原地更新同一个 Entry 的 Item 也不能被旧 lease 删除。

顺序与生命周期包括 ItemSpawn metadata 在 Run=0 先到，分别验证 adapter 已连接与未连接 / IsClient=false 两种顺序，恢复连接并实际 Reset 新局后保留匹配候选，两客再抢只入包一次；旧局未拾物在客端 Reset / Cleanup 后真实销毁，房主 Reset 使用冻结 DropId 驱动原生退役所有复制体；已入包物在 Cleanup 后仍存活。旧房 marker 尚无 lease、同一个 CLR Item 已由新房登记时，Reset 只撤旧 marker，保留新房 Item / Agent / Entry 和正常拾取能力。Unity 替身把 Destroy(GameObject) 连带销毁组件，已销毁对象非 CLR null 但比较为 null。

替身隔离玩家身份、三个进程的静态 owner / 事件 / 消息队列、库存枚举 / 空位 / AddAt、TryPlug、变量存取、Agent 创建、ItemSnapshot 序列化和构造物品、网络 RPC 传输与物理场景。库存底层端点明确记录实际物品与数量；Unity Destroy 调用抽取的官方 Item.OnDestroy，执行 Detach 和 ReleaseActiveAgent；RPC 端点把原生删除请求送入上述完整原生 handler，生成 RPC 用数据快照建立独立客端物品并送入生产 metadata 入口。CreatePickupAgent 的模型不证明真实资源、碰撞或 UI 正确；真实地图落地、拾取提示、断线重连后的双端显示与帧耗仍需 L3。
