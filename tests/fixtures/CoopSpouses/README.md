执行完整生产 `CoopNpcWorld` 与独立配偶 partial，保留源码 SHA-256。覆盖 16 名成员分别结婚同 NPC、独立 prefab/service shell、主人移动与回家、离婚只回收本人、事务重发幂等、乱序跟随、断线重连重新开始序号、切槽旧请求拒绝、异步更换不删后继、永久 NPC 不覆写公共 named registry、晚加入恢复所有实例及切图清理。

Unity 对象/组件、角色名册、工厂 await、网络 API 入口及婚姻个人读取为内存替身；执行真实生产调度、factory 组合和所有权判断。不启动游戏，不读取玩家存档，不验证传输、NavMesh、正式 prefab、游戏内交互或性能。

独立复审增补倒序世界列表：新版空列表先删除配偶，旧版仍有配偶的快照不得重建；移除生产顺序比较时实际转红。运行器使用本次独立输出的 TargetPath 与 SHA-256，不执行历史 bin。

网络发送替身实际调用生产 `BossRushCoopJson` 往返完整 DTO 数组及位置/旋转字段，不直接传对象引用；解析复用 `BossRushJsonValue` 与 `SimpleJsonHelper`。此证据证明 codec 数据形状，不证明工坊通道实际投递。
