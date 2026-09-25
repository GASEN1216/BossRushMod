# 后山三形态执行回归

运行：`python tools/run_runtime_regressions.py --filter BackMountainMorph`。需要 .NET 8 SDK，不启动游戏、不读取玩家存档；生成工程、生产源码 SHA-256 与编译产物写入 `Build/backmountain-morph/`。

真实生产逻辑：完整链接 `BackMountainBossMorphService.cs`、`RaidMealUsageBehavior.cs` 和 `RuntimeStatModifierTracker.cs`，不抽取或重写服务方法。通过实际食用入口和服务启动，再用反射调用原始 `Update`。所有资源、玩家角色及必需挂点均由测试显式装配。

覆盖三种果实的模型、全套对应装备外观与女巫镰刀、属性与不同攻击形状/范围/伤害/冷却；真实 Item 树和当前/最大生命保持不变；模型切换篡改碰撞胶囊后立即还原；缺资源、缺挂点和属性中途失败的回滚；官方无条件扣量前对最后一份及满堆的补偿；重复使用拒绝；暂停、30 秒到期、死亡、换角色、切图、停用、owner/角色销毁与其他系统换模的退订/恢复；射击和近战只排队下一帧结算；敌我、墙、距离、死者、自身与重复碰撞体过滤；手持/装备变化后的纯视觉隐藏、脚本及碰撞禁用、原手势恢复。

宿主替身：Unity 对象的已销毁即 null 与 GameObject 连带组件/子物体销毁，模型/外观树克隆，官方换模和食用完成的调用顺序，显式可控的物理命中与墙，Stats 百分比计算、通知和特效接口。食用完成按 `UsageUtilities.Use` 二次检查 `CanBeUsed`、通过才调用 `OnUse`、`CA_UseItem.OnFinish` 随后无条件扣量的顺序；缺资源/属性失败发生在使用门仍为 true 时，才能验证补偿。重复变身只验证 UI 与服务拒绝，不声称官方二次门关闭时会补回消费。`SetCharacterModel` 故意覆盖碰撞尺寸并关闭受击胶囊，确保生产还原代码不能缺席。物理替身只提供候选碰撞体，目标筛选与伤害全由生产代码执行。女巫武器修复与挥击特效本身是宿主接口替身，只证明调用时机，不能证明真实模型渲染或实际动画。

本夹具证据为 L2，不能替代 Unity 渲染、物理、官方绑定和游戏手感的 L3 验证。

2026-09-25 验证：887 条断言通过。反向探针在 `Build/backmountain-morph-negative/` 稀疏副本中执行：先确认基线绿，再将生产 `RestoreCollision` 的 `_damageCollider.enabled = _damageEnabled;` 改成 `false`，真实运行转红于“Ghost does not disable damage collider”；按字节还原、核对 SHA-256 后重跑转绿。未修改共享工作区的生产代码。证据保存为该副本的 `negative-result.log`、`original-sha256.txt` 和 `restored-sha256.txt`。
