执行生产物品快照接收、完整重铸清理、词缀写入口与 typed key 验证方法，保留源码方法体并记录 SHA-256。覆盖旧差值替换、重复应用、零差值、空投影、词缀 UI 条目引用保留和拒绝类型/显示/物品身份错误；同时提取生产工事收据接收与 Mode E 雇佣价格方法，覆盖退款失败保留重试、重复收据不重复退款、成功修复保留消费、不同主人独立阶梯价格及死亡随从不计价；Mode E 消耗品与煤球雇佣收据也验证失败退款保留、重试成功、重复不再交付。

Item KV 容器、prefab 基线恢复、装备刷新、房间角色名册与退款物品交付为内存替身；网络 DTO 发送与消息 Read 都通过生产 codec 往返。不启动游戏、不访问玩家档，也不证明联机传输、真实背包退款或游戏内效果正确。

2026-10-13 增补直接执行生产退款取消方法与 PetNest `FinishSpawn`：A 槽消费后切到 B 槽不 apply/ack 或退款；同房未知结果保留，原槽恢复后接受房主原事实；旧房间取消只向原槽退款，失败保留重试，成功只退款一次。旧异步宠物续作完成时不能移除同 owner 已替换的新 Entry；当前失败仍释放重试，成功保留活动 Handle。这里只执行 await 后的真实收尾方法，不模拟 Unity 异步引擎。

召唤协议方法使用生产 `RequestSummon`、接收/回执/计数方法和 owner 归属清理方法，覆盖 16 位玩家各自冷却、重复请求、错槽拒绝、同一 Character 换槽后 await 归属失效与旧实体清理。替换的是消息传输、名册、Unity Destroy 与实际召唤工厂；不把协议分支回归视为真实传输或 AI 战斗验证。

独立复审增补原样执行宠物 OnRequest / OnRelease / OnAward / Clear / RefreshRewardRun 与原生投影绑定：释放与新部署乱序、重复请求、服务拒绝后重试、空握手与同房重连不重复经验、不清未确认事实/每局预算、实际换房清理，以及同 ID 新对象与重生主人绑定。成长替身可失败且记录实际经验累加；宠物生成、照料表现、传输和存档仍为外围替身。所有运行使用本次 TargetPath 与 SHA-256。

网络嵌套 DTO 编码直接链接真实 `BossRushCoopJson`、共享 `BossRushJsonValue` 与 `SimpleJsonHelper`；完整装备变量列表经真实 codec 往返。提取 DTO 恢复生产原有 `[Serializable]` 注解，不使用 Unity JSON 替身。

2026-10-15 场景消费边界：显式装配 HasBoundScene 与本次实际旅行 SceneToken，默认不放行；调用真正消息接收→绑定/AI spawn 事件或完整 Tick，覆盖相同公开 generation 下原生旅行已替换、旧包、旧 pending 和 await 完成窗口。Wraith 保留同一实际 Core 在 token 观察前生成的墓箱队列，跨 Core 不迁移该请求，原记录仍保留；宠物/配偶当前世界投影延后消费并重试，过期旅行投影拒绝。宠物经验回执继续使用原资产收据；旧亡魂击杀回执现只确认，不删除持久记录。网络/Core/Unity工厂及持久化仍为受控边界，本回归不替代 Windows 全编译与 L3。

Claude PC-03/04/05 增补：逐字执行真实 `OnSceneLoaded`、Core/room/token 生命周期和两种武器的 `OnAbilityStart`→`OnAbilityUpdate`→派生 `OnStop`→真实基类 `EquipmentAbilityAction.OnStop`，覆盖单机和同 Core Additive 不清召唤、新 Core 仅回收已登记网络实体、拒绝/未发的技能冷却退款、旧结果不能退款新技能与资产切换取消回调。真实词条初始化、写槽、清槽、未知词条名字进入发送→实际 codec→快照接收，验证隐藏空名字不会拒绝整份 RF/AFX 状态。工厂执行仅为可计数替身；这里的 accepted 表示通过房主准入，不证明异步工厂、真实 AI 或动画成功。两类 `TryGetSummonOwner` 也执行原表和当前 owner/body/asset/token 核对。
