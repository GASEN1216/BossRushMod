# 死亡亡魂联机执行回归

完整链接 `DeathWraithCoop.cs`，逐字抽取原列表读取、查询、移除、缓存写屏障。验证 16 名玩家相同 raid 各自生成一名、单人多个 raid 保留、同记录重复请求去重，原生负 AI ID 与实际墓箱坐标，大装备快照乱序分片与发送预算。Claude PC-02 按 owner 决策改为：击杀后原记录和全部装备仍在、同代次按原资产/raid/指纹不复刷，新代次按原有效记录再生成；原墓箱 Touched→存储移除仍只删除该 raid。旧击杀回执只向正确资产确认，即使存储读写失败也不做 IO。literal emoji 的高/低代理放在原 12000 字符边界两侧，逐包 UTF-8 往返后完整重建并验证同一指纹。

Unity 角色创建、原生 AI、网络、ES3 缓存使用替身；它证明生产 owner 和个人记录事务，不证明真实 AI 外观/装备、伤害、场景加载或工坊传输。真实创建流程仍复用原 `DeathWraithSpawnFlow.cs`，正式 Windows 编译和两机实测分别验证程序集与游戏表现。

独立复审增补：逐字提取 SpawnRequested / LootboxCreated / Touched、上下文消费与移除方法，以及生成方法的联机早返分支（只取 Queue→return，后面的真实角色工厂仍是替身）。执行非墓箱排除、同坐标两条原版墓箱上下文、队列→实际 SendToHost→房主组装，逐条检查不同装备变量。覆盖房主切槽保留整个世界、空握手保留未完成分片、迟加入绑定以及同 AI ID 替换原生对象后的名字/掉落/预测保护。销毁流程执行 Unity 对象空语义。

网络快照、装备嵌套列表、指纹、消息 Read 与 UTF-8 分片直接使用真实 `BossRushCoopJson` 和共享 JSON 读写器。官方 ItemTreeData/CustomData 数据对象仍是显式边界替身；ES3 cache 替身也通过共用 codec 克隆数据隔离引用，不能据此证明原生 ES3 存取。

2026-10-15 场景消费边界：显式装配 HasBoundScene 与本次实际旅行 SceneToken，默认不放行；调用真正消息接收→绑定/AI spawn 事件或完整 Tick，覆盖相同公开 generation 下原生旅行已替换、旧包、旧 pending 和 await 完成窗口。Wraith 保留同一实际 Core 在 token 观察前生成的墓箱队列，跨 Core 不迁移该请求，原记录仍保留。旧击杀回执确认不依赖世界实体绑定，但不能删个人记录。网络/Core/Unity工厂及持久化仍为受控边界，本回归不替代 Windows 全编译与 L3。
