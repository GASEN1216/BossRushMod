# Mode G 生产流程回归

2026-10-13 联机补充：逐字抽取生产 `ApplyCoopCombat` 并链接真实个人遥测，覆盖负数 AI ID、客端跳过原生重复事件、乱序收据等待前序、重复收据幂等、本人护甲后贡献、他人末击仍结束共享 Boss。网络角色索引是边界替身，不能证明游戏内 RPC、Boss AI 或九波联机可玩。

2026-10-15 round10 修复回归：移除共享死亡处理替身，逐字执行完整 `HandleBossDead`、原类型解析和宿敌归因，链接完整 `ModeGSpawnTransaction`。原生 GameObject/Health 先按 Unity 假 null 销毁，再实际消费连续个人伤害/死亡事实；必须注销原 Health、保留个人末击和护甲后贡献并排定下一帧结算。未知引用、未登记缓存、旧 run、同图新场景代次、乱序及重复均有拒绝或幂等断言。托管 Boss 的三种稳定 key 和类型从生产源取值，队列最终交付必须保留 DragonDescendant / DragonKing / PhantomWitch；普通 preset 仍为 Normal。管理器原生清理、角色索引、成就持久化接收器和展示依赖仍是边界替身。本回归不证明真实九波已经恢复，需新 DLL 三端复验。

2026-10-06 携崽扩展：新增非主角随从击杀已登记 Boss 的场景，生产死亡回调正常结案一次、末击不归到主角武器轴、主角直伤分母保持为 0；不把宠物伤害伪报成玩家直伤。

运行：`python tools/run_runtime_regressions.py --filter ModeGCombat`，需要 .NET 8 SDK。

真实生产逻辑：完整链接战斗遥测、直伤分类器、三轴判据与 Modifier 操作、运行状态、九波计划、确定性随机、八项契约和两份持久化类；逐字抽取波末结算、下一波弹药准备、HUD 目标格式和奖励计划。源文件 SHA-256 写入 `Build/runtime-regressions/ModeGCombat/source-sha256.txt`。

宿主替身：Health 按官方“先扣血 → OnDead → OnHurt”的顺序分发事件；调用方指定官方已算好的 `finalDamage`，因此不证明护甲/暴击公式。Item/Stat、元数据、角色切换与存档使用内存替身，不访问玩家存档。Unity 对象替身模拟已销毁对象等于 null 与 GameObject 连带销毁组件。

覆盖致命伤、减伤与暴击后的计分、间接伤害、精确抑制、污染与缓存溢出、普通弹药缺省字段、休整期禁令违规、属性末击提示、退订、小 Boss 池、契约候选与目标可达性、Resolve 上限、奖励档位及未来存档版本保护。另测缓存预热后同一玩家/主 Boss/武器的1,000次非致命命中托管分配，同时检查伤害未漏计；这只代表 .NET 8 替身内的伤害路径，不代表 Unity Mono 全局帧耗。八契约可达性使用九波规则允许的进度作为见证，不代表玩家手感或实机九波战斗已通过。

不覆盖 Unity 物理/导航、真实 Boss AI、异步生成工厂、实际物品交付、HUD 像素可读性与帧耗；这些需要 Windows 编译及 owner 实机验证。

本轮补充：链接完整 ModeGRewardTransaction，逐字抽取实际 ModeGRewardStrictMaterializer、刷怪几何选点与暂停超时计算。覆盖奖励背包销毁、交付事件/逐件回调重入取消、每帧一件、一次完成回调和租约释放；败北打断连胜并保留历史；贪心首选堵塞后的双/三Boss合法组合，以及三种编队共1200组固定种子样本与独立穷举的可行性对照。物理落地在替身中直接接受候选，物品/背包/仓库仍为替身，不据此声称真实导航与奖励实物交付已通过。暂停用例验证生产时间预算纯函数，真实暂停状态接线由 ModeGProductionFlowGuard 检查，异步 Unity 调度仍需实机。

2026-10-15 原 host Stop 补审：逐字执行 FreezeCoopAsset、CheckCoopAsset、CanContinueRun，原网络 host 实际停止后不能获得单机世界权限；同房间 SessionReady 暂失和空握手仍可继续，原单机在离线时可继续，连入新房间后不接管。当前 36 cases PASS。End 在该用例为记录边界，完整异步 SpawnWave 和 Unity 回收不据此宣称通过。
# 2026-10-18 联机死亡兜底补充

新增完整生产 `ModeGRuntimeModule_SilentDeathSweep`。先让前置 `Health.OnDead` 订阅者真实抛异常截断遥测，再执行正常死亡路由、生成事务注销和波结算排队。生产发布器正文同时执行，验证在注销前沿冻结的原参与者资产交付死亡；网络末端只记录收件资产。原生对象销毁和场景代次改变分别覆盖冻结 AI 身份回取与拒绝越代，客端不能自行补世界死亡。实际频道投递与 ordered receipt 消费仍由已有联机夹具覆盖；本组不证明真实多端游戏时序。
