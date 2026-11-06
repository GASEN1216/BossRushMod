逐字链接 `TidegateStory.cs`、`TidegateNoteBridge.cs` 和共享槽位 store、保存协调器、物理保存闩及 JSON 编解码；从 `TidegateDrillHooks.cs` 提取完整剧情 partial，从 `OfficialQuestItems.cs` 提取真实资产就绪检查与采集器。生成文件记录生产源 SHA-256。

宿主替身仅模拟 Unity 销毁判空、物品归属和序列化树、内存保存缓存/物理保存副本、官方笔记事件；物品和仓库保存使用官方 `Item/`、`Inventory/` 前缀与对应 `ItemTreeData`、`InventoryData` 类型。不会启动游戏、加载玩家存档或写游戏目录。ES3 删除替身只接受显式 `Location.Cache`，防止回滚误写物理文件。

失败注入覆盖交表发放失败、通知重入、key 写入/回读、资产采集失败、持续 key 写故障，以及写失败后恢复读取也失败。缓存发布失败必须撤回原件/礼品/清理计划并保留旧事实，玩家可以重试；完整缓存成功后物理保存失败保留新态自动重试。失败返回后主动让其他模块直接 `SaveFile(false)`，并用新建持久化 owner 重载物理副本；也覆盖角色先采集、仓库和缓冲订阅位于剧情订阅前/后的官方采集顺序。每份保存副本都检查为完整旧态或完整新态，成功批次另断言四份资产各采集一次。

另覆盖首次/主动镜像屏障、漏收切槽且官方采集先于下一 Tick，以及 F3 清空缓冲和非目标子树（实例号、变量与格位锁）的保留。反向探针在稀疏副本运行，不临时破坏共享工作树。

入口：`python tools/run_runtime_regressions.py --filter TidegateStoryTransactions`。属于 L2 隔离证据，官方 UI 和真实游戏行为仍需 L3。
