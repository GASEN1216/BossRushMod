# 残日盾片执行回归

由聚合入口运行：`python tools/run_runtime_regressions.py --filter ResidualSunShield`。

真实生产逻辑：直接链接 `ResidualSunShieldRules`、`ResidualSunShieldRuntime`、`ResidualSunShieldConfig` 与 `ConfigItemIds`；构建时从当前 `ZombieModeDamageRuntime` 原样抽取 `ReduceFinalDamage` 及其归因谓词。验证非丧尸模式的早返前接线、致死前方向减伤、单次充能反击、冷却、过期与 owner 清理。另外链接真实 `CharacterOnDeadPatch`，原样抽取花冠 `RestoreOfficialPresetBeforeDeath` 和官方 `CharacterMainControl.OnDead`，执行并断言官方计数、奖励准备、尸体箱的真实顺序，防止新官方存档键和继承 Boss 追加掉落。记录生产源码 SHA-256，并通过公共 runner 构建和执行当次返回的 TargetPath。

宿主替身：Unity 对象销毁和组件连带销毁、时间、装备事件、阵营、角色动作、渲染反馈与官方 `Health.Hurt` 的最终伤害计算后 / 过量钳制前调用点。防御倍率由替身提供，用来验证 shield 消费已经过护甲的 finalDamage。死亡检测中只有计数写入、尸体箱创建和其他 Boss 追加掉落 handler 是计数替身，调用顺序由原样官方方法决定。没有覆盖实际 Harmony IL 安装、真实渲染、官方伤害数值和游戏内输入；这些依赖正式编译与 L3 实机验收。

不启动游戏，不读取或写入玩家存档。

`python tests/fixtures/ResidualSunShield/negative_probes.py` 在 `Build/` 临时稀疏副本验证 `EclipseBloomIntegrationGuard` 和四项原守卫：模块登记、物品配置、真实最终伤害入口、异步代际失效、准备期不可受击和拒绝已死结果、掉落预配置、官方部位伤害绑定、胶囊方向和姿态 / 展开比例、核心伤害转发、死亡前身份恢复、自然出击名额、主 / 子场景 owner、待处理入场互斥、Boss 池双生成路径、引导实例身份、交付事务、地面预警与采样预算、专用拾取模型与 GroundPoint / 六部件完整性、待用状态取消、加载开始取消、持续清场避让与直接清场避让。原守卫另测终章隔离、初始扫描与循环预算、盾片配置器、真实 ID 黑名单，以及小弟死亡豁免必须先于额外掉落。每个探针要求红在指定断言，逐字节恢复副本，末尾核对生产源码 SHA-256 未变，结构化结果写入 `Build/runtime-regressions/ResidualSunShield/integration-negative-results.json`。
