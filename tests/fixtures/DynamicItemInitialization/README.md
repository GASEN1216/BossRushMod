# 动态物品初始化回归

逐字抽取生产 `InitializeInstance`，覆盖非激活克隆、重复初始化、错误主人、原版物品隔离和宿主异常诊断。适配器模拟官方 `Item.Initialize` 的幂等状态与 Agent 主人绑定；不模拟 Unity 场景、渲染或 Harmony 安装。异步返回链和三种创建入口另由 `DynamicItemInitializationGuard` 检查。

运行：`python tools/run_runtime_regressions.py --filter DynamicItemInitialization`。
