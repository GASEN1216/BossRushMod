# PC06 私有原件消费屏障回归（L2）

链接生产 `CoopWaylayerClaimUseHooks.cs` 六个 Harmony prefix、完整 `SkyIslandFieldcraftUsage.cs`、物品 ID 常量；从当前 Claims/Journal 原文提取真实 `Locked` 方法与锁键。生产 Claim/退款 Save/readback 状态机由 WaylayerEscrow 组验证，本组不另写一份状态机。

运行时从当前安装官方 DLL 读取 16 个完整方法体：Item.Use、UsageUtilities.IsUsable/Use、UsageBehavior.Use、CharacterMainControl.UseItem、CA_UseItem.OnStart/OnUpdateAction/OnFinish/OnStop、Drug.CanBeUsed/OnUse/CheckCanHeal/Heal、FoodDrink.CanBeUsed/OnUse/Eat。使用实际安装 Harmony 将所有 opcode、分支、异常块、局部变量与字段/方法签名映射到明确的宿主替身，不改原生方法体判据。每条 opcode/标签/EH 做一致性断言，类型/字段/方法签名不匹配立即失败。

便当配置的 FoodDrink、Drug(15)、Meal；药膏 Drug(40)；风灯生产行为分别检查锁定、解锁、直接调用、开始后转 pending 和完成扣量。OnUpdateAction→OnFinish→StopAction→OnStop 真实链验证 pending 结束后不消费且归还手持；另覆盖非主角色入口、耐久与不可堆叠 DestroyTree。替身只隔离 Unity 对象/变换、角色动作调度、生命/能水效果与 Fieldcraft owner；不启动游戏、不读写玩家存档，不能代替 L3。

`python tools/run_runtime_regressions.py --filter WaylayerClaimUse`；输出是当次独立路径、SHA 与实际 DLL 输入哈希。`negative.py` 在隔离源码副本逐个禁用六门并破坏目标/字段/编译登记；要求在对应断言变红，再用未改原文重新全绿。所有源码字节保持不变。
