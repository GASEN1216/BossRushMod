# 人工实测补漏：装备恢复与召唤请求

通过统一入口执行：`python tools/run_runtime_regressions.py --filter ManualEquipmentRecovery`。

逐字抽取真实新武器登记、补配/重铸恢复、词缀资格、召唤请求代数及手持取消方法；完整链接物品 ID 与公共物品属性代码。执行三件真实 TypeID 的缺组件读档、已有 RF 增益、零耐久/磨损、重复查询，以及召唤超过 1.2 秒、换走再拿回、NPC 事件、死亡、切图、停用与销毁分支。

宿主替身只模拟基础配置覆盖、RF 差值读回、角色状态与动作调度。异步角色创建保持挂起，由测试驱动生产请求判据；不模拟 Unity 实例化、Harmony、物理、真实资源耗时或特效。属于 L2，不能代替 L3。抽取源哈希保存在 `Build/manual-equipment-recovery/production-sha256.json`。
