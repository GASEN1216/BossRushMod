# FIX_TRACKER.md — 修复状态与兼容性流水账

> 修 bug、回归、兼容问题或 owner decision 后更新本文件。旧路径 `docs/协作/FIX_TRACKER.md` 只做兼容转发。

## 状态定义

| 状态 | 含义 |
| --- | --- |
| `fixed` | 已修复，并记录验证方式 |
| `accepted` | 已确认是问题，方案已定，尚未落地 |
| `refuted` | 曾怀疑是问题，核实后不是 |
| `deferred` | 确认是问题，但本轮不修，原因明确 |
| `documented` | 不改代码，仅文档化限制、取舍或人工验收要求 |
| `needs-owner-decision` | 需要 owner 对兼容/产品/安全取舍拍板 |

## 条目模板

```markdown
---
### YYYY-MM-DD 标题

**状态**: fixed / accepted / refuted / deferred / documented / needs-owner-decision  
**Finding**: CR-YYYY-MM-DD-NNN / 无  
**兼容分类**: SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**版本/Commit**: commit hash / 未提交 / 不适用  
**Owner decision**: 需要/不需要；结论
**现象**: 玩家可见症状、日志或触发条件
**根因**: 代码路径、引入机制、为什么会发生
**修复内容**:
- 新增文件: 路径（是否加入 `compile_official.bat`）
- 修改文件: 路径
**兼容性影响**: 对存档、配置、TypeID、Harmony/反射、资源、部署的影响
**验证方法**:
1. 编译:
2. Guard:
3. 人工 smoke:
**未验证/需人工**: 如有
**失败尝试**: 如有
```

---
### 2026-07-09 丧尸模式自定义自爆怪无距离门控且未自毁

**状态**: fixed
**Finding**: 玩家反馈 / 静态代码确认
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于既有 `ExploderTriggerDistance` 调参常量未接入、且“自爆怪”语义未闭环的行为修复
**现象**: 玩家反馈丧尸模式 BOSS 关有东西持续追着玩家爆炸。静态排查确认 BOSS 波仍会按既有压力系统维持环境尸潮；第 6 波以后特殊怪池包含自定义 `Exploder` 和官方 `OfficialExploder`。其中自定义 `Exploder` 的技能冷却到点后直接原地起手爆炸，没有检查与玩家距离，爆炸后不会死亡；起手期间若丧尸移动，旧实现还会保留起手时的旧爆心。
**根因**: `ZombieModeTuning.ExploderTriggerDistance = 2.5f` 已存在，但 `TryExecuteZombieModeSpecialSkill()` 的 `ZombieModeSpecialKind.Exploder` 分支未使用该距离门控；同时自定义爆炸只调用 telegraph/ExplosionManager 伤害路径，没有对自身走 `Health.Hurt()` 死亡链路，导致自定义自爆怪只要存活并追踪玩家，就会按 9 秒冷却反复放红圈爆炸。通用 telegraph 默认固定起手坐标，不能表达“自爆中心始终是丧尸当前位置”。
**修复内容**:
- 新增文件: `tests/ZombieModeExploderTriggerDistanceGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeEnemyRuntime.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeExploderOfficialSelfDestructGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变 BOSS 波环境尸潮规则，也不改变官方 `OfficialExploder` 的原版自爆保留逻辑。自定义 `Exploder` 改为遵守既有 2.5 米触发距离，技能起手红圈跟随丧尸当前位置，爆炸后走正常死亡链路自毁；被玩家提前打死时也会在死亡位置触发一次自爆。通过 marker 标记跳过技能自爆后的死亡二次爆炸，并让 pending 跟随红圈在来源已死亡时取消，避免双炸。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py` 通过
3. Guard: `python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
4. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py` 通过
5. Guard: `python tests\ZombieModeWaveEventMarkerCacheGuard.py` 通过
6. Guard: `python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
**未验证/需人工**: 需要进游戏在第 10 波及以后 BOSS 关实测，确认自定义 `Exploder` 只有贴近后才起爆，红圈/爆心跟随丧尸当前位置，起爆后正常死亡、不再反复追炸；同时确认起手期间被击杀只在死亡位置爆一次。`Hunter` Boss 每 5 秒闪近并在玩家脚下放爆炸圈仍是当前设计技能。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式瘟疫/骚扰特殊怪行为与公开描述不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于公开描述、调参常量与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 技能后确认，瘟疫者描述为持续毒云但旧实现是一次性爆点；精英 `ToxicAura`/`Plague` 描述为毒雾/毒云但旧实现也是一次性爆点；骚扰者描述为发射投射物命中后生成减速区，但旧实现是在玩家脚下直接延迟减速；共享区域 tick 在带 `slowPercent` 时直接给玩家上减速，导致可见区域外也可能吃到减速；腐蚀者毒圈有起手常量但首跳没有真正延迟。
**根因**: `TryExecuteZombieModeSpecialSkill()` 和 `TryExecuteZombieModeEliteSkill()` 复用了瞬发 telegraph damage helper 表达毒云/毒雾；骚扰者复用了玩家减速 telegraph 而没有投射物 runtime；`ZombieModeAreaTickRuntime` 只把 slow 当作全局玩家状态写入，没有走范围判定 helper；腐蚀区只把 startup 加进对象寿命，未传入首 tick 延迟。
**修复内容**:
- 新增文件: `tests/ZombieModePlagueCloudBehaviorGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeHarasserProjectileGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeAreaTickSlowScopeGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeAreaDamagePlayerGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变特殊怪/精英/BOSS 刷新池或数值常量。瘟疫者和精英毒雾改为 telegraph 后生成持续区域伤害云；骚扰者改为生成可飞行投射物，命中或到期后才结算 25 伤害并创建 3.5m 减速区；区域减速改为只在玩家位于可见区域内时生效；腐蚀区首跳遵守 `CorruptorZoneStartupSeconds`。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py` 通过
3. Guard: `python tests\ZombieModeHarasserProjectileGuard.py` 通过
4. Guard: `python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
5. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py` 通过
6. Guard: `python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py` 退出码 0
7. Guard: `python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 通过
8. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
9. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏在普通波和 BOSS 波实测，确认瘟疫/毒雾持续云、骚扰者投射物、腐蚀区起手与减速范围符合可见表现。
**设计确认项**: `SprinterDashStartupSeconds = 0.5f` 已在后续修复接入；`Hunter` Boss 仍按当前 Wiki 明文描述执行 15m 传送式 Dash，若后续要改成可躲避冲刺，需要 owner 作为设计改动确认。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式疾行者瞬移与死亡爆炸表现不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于调参常量、公开描述与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 死亡效果后确认，疾行者描述/常量是 0.5 秒起手后的 12m 冲刺，但旧实现直接 `transform.position` 改坐标，表现为瞬移；精英 `Burst`、Splitter/Titan 死亡爆炸走 player-only 区域伤害路径，玩家会吃到伤害但缺少原生爆炸 VFX、屏幕反馈和墙体阻挡语义。
**根因**: `ZombieModeTuning.SprinterDashStartupSeconds` 只存在于调参表，`TryExecuteZombieModeSpecialSkill()` 的 `Sprinter` 分支没有接入；死亡爆炸沿用了早期 `DealZombieModeAreaDamageToPlayer()` fallback，而不是 `ExplosionManager.CreateExplosion` 封装。
**修复内容**:
- 新增文件: `tests/ZombieModeSprinterDashTelegraphGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeDeathExplosionVisualGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变疾行者冷却、冲刺距离或死亡爆炸数值。疾行者改为先显示跟随本体的短起手提示，再用 `SetForceMoveVelocity` 执行 12m 冲刺，并在暂停、死亡、run 清理和销毁时归零强制速度；精英 Burst、Splitter/Titan 死亡爆炸改走原生爆炸路径，保留 fallback 避免 ExplosionManager 不可用时技能失效。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeSprinterDashTelegraphGuard.py` 通过
3. Guard: `python tests\ZombieModeDeathExplosionVisualGuard.py` 通过
4. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py`、`python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
5. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py`、`python tests\ZombieModeHarasserProjectileGuard.py`、`python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
6. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py`、`python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py`、`python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 退出码 0
7. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
8. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏实测疾行者起手提示/冲刺体感，以及 Burst、Splitter、Titan 死亡爆炸的 VFX、屏幕反馈、伤害和墙体阻挡。
**设计确认项**: `Hunter` Boss 仍按 Wiki 明文描述执行 15m 传送式 Dash；若需要把它也改为可预警冲刺，应按设计改动单独确认。
**失败尝试**: 无

---
### 2026-07-08 焚天龙铳烟花弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳烟花弹表现修复
**现象**: 烟花弹 FireWork 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Firework profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Firework_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化烟花弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空烟花弹自定义资源特效引用，螺旋飞行、空爆分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 FireWork 烟花弹实测，确认开火、空爆分裂和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳纳米弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳纳米弹表现修复
**现象**: 纳米弹 NM 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Nano profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Nano_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化纳米弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空纳米弹自定义资源特效引用，追踪、分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 NM 纳米弹实测，确认开火、分裂触发和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳雪球弹滚雪球重做

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳雪球弹表现修复
**现象**: 用户不希望 Snow 继续作为高弧线冰弹，而是要超慢速直线滚雪球：主雪球滚 5 秒并越滚越大，命中/死亡后分裂 4 个小雪球；小雪球滚 2 秒继续变大，出生后短暂无敌防止刚分裂就被敌怪吞掉。后续又要求主雪球最大改为旧最大雪球 10 倍、小雪球最大 5 倍、降低基础伤害，并且只允许主雪球留下冰区。
**根因**: 旧 Snow profile 使用 High arc / gravity / 落地冰区的通用配置，主弹和二段弹都复用同一套冰区生成策略；分裂弹没有专门的出生保护，也没有“当前体积影响伤害”的运行时状态。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunSnowballGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
- 修改文件: `WikiContent/zh/equipment/equipment__dragon_cannon.md`
- 修改文件: `WikiContent/en/equipment/equipment__dragon_cannon.md`
- 修改文件: `wiki-site/docs/equipment/dragon-cannon.md`
- 修改文件: `wiki-site/docs/en/equipment/dragon-cannon.md`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅调整 Snow profile 与 DragonKingBossGunProjectileAgent/发射上下文运行时表现。复用既有弹体、对象池、分裂、半径伤害和冰区系统；未新增每帧分配或独立弹幕系统。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 编译通过；若游戏进程运行中，自动部署会因 DLL 被锁而失败，需关游戏后重新部署
2. Guard: `python tests\\DragonKingBossGunSnowballGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunEnergyPwsGuard.py` 通过
5. Guard: `python tests\\F3DragonKingBossGunDebugKitGuard.py` 通过
**未验证/需人工**: 需要进游戏用雪球弹实测，确认主雪球直线慢滚 5 秒、最大视觉接近旧最大 10 倍且伤害随体积提高；主雪球命中/寿命结束分裂 4 个小雪球，小雪球 0.1 秒内不会被敌怪吞掉、2 秒内长到出生 5 倍；只有主雪球留下 1 秒冰区，小雪球不再铺冰。

---
### 2026-07-06 逆鳞致死触发兼容与复活后短暂无敌

**状态**: fixed
**Finding**: 玩家反馈 / 官源静态确认
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 本次提交
**Owner decision**: 不需要；属于官方 `Health.Hurt()` 时序变化后的逆鳞兼容修复
**现象**: 玩家反馈新版本更新后逆鳞不触发；致死伤害下无法完成“濒死回血、反击、碎裂”的保命流程。
**根因**: 当前官源 `Health.Hurt()` 的执行顺序为扣血后先进入死亡分支、触发 `OnDeadEvent` / `Health.OnDead` 并 `SetActive(false)`，最后才触发 `OnHurtEvent` / `Health.OnHurt`。逆鳞旧逻辑只在 `Health.OnHurt` 里看 `CurrentHealth <= 1`，致死伤害会先把主角送入死亡流程，导致保命窗口来不及触发。
**修复内容**:
- 新增文件: `tests/ReverseScaleLethalProtectionGuard.py`
- 修改文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleAbilityManager.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleConfig.cs`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名或掉落表；扩展既有 `Health.Hurt` / `Health.CurrentHealth` Harmony 兼容补丁，在玩家装备逆鳞且受到致死伤害时先钳到触发阈值，并确保逆鳞 `OnHurt` 回调已注册。逆鳞触发回血后新增 0.5 秒免伤窗口。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过；最终部署目标 `Mods\\BossRush\\BossRush.dll` 被正在运行的 `Duckov.exe` 占用，自动部署和手动覆盖均失败，`Build\\BossRush.dll` 已生成
2. Guard: `python tests\\ReverseScaleLethalProtectionGuard.py` 通过
3. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
4. Guard: `python tests\\MenuSceneRuntimeHookGuard.py` 通过
**未验证/需人工**: 关闭游戏释放 DLL 锁后重新部署，再进游戏装备逆鳞承受致死伤害，确认回血、棱彩弹、气泡、图腾销毁和触发后 0.5 秒免伤都符合预期。

---
### 2026-07-06 焚天龙铳火箭弹空爆只单次爆炸

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳火箭弹表现修复
**现象**: 火箭弹到空爆距离后只在空中触发一枚原版爆炸，未表现为多枚分裂火箭落地/命中后的连续爆炸；后续实测中分裂弹又会在敌人头上散开后高速飞走，且主弹仍按固定射程比例空爆，不是射到鼠标位置再爆开。再次调整时用户要求分裂弹不再按固定定时爆炸，而是依靠自身碰撞爆炸。最新实测中日志已缓存 `BulletRocket`，但分裂弹命中后没有爆炸。
**根因**: 火箭 profile 的 `SplitCount` 仍为 1 且使用向下分裂；同时主弹使用原版火箭预制体，空爆置 `dead` 后仍会被原版 `Projectile.Update()` 按 `context.explosionRange` 当作最终爆炸处理。后续迭代中分裂弹固定引信路径全生命周期跳过碰撞，且主弹空爆距离仍用 `distance * AirburstDistanceFactor`，没有读取玩家当前鼠标瞄准点；原版火箭预制体缓存还会接受 Rocket 口径武器上的 `BulletNormal_Burn`，导致分裂弹视觉/行为取错基底。官源 `Projectile.Init()` 的 `hitLayers` 只包含敌人、墙、地面和 `blockBulletLayers`，不包含 Projectile 自身，因此分裂弹之间不会因为彼此弹体互相触发爆炸。最新问题是分裂弹虽已使用原版 `BulletRocket` 预设，但自定义 `ProjectileContext` 没有设置 `explosionRange` / `explosionDamage`，原版 `Projectile.Update()` 因 `context.explosionRange == 0` 不会调用 `ExplosionManager.CreateExplosion`。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunRocketSplitGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
**性能处理**: 按用户要求将主弹/分裂弹预设互换：主弹用焚天龙铳轻量弹体负责飞到玩家鼠标瞄准点后空爆，分裂弹使用原版火箭视觉，并且原版预制体缓存改为按火箭弹 TypeID `326` 的确定关系绑定：只接受 `TargetBulletID`、`PreferdBulletsToLoad.TypeID` 或枪内当前装填弹 TypeID 等于 `326` 的原版枪械，不再用 `Rocket` / `RPG` / `Missile` 名字模糊匹配，也不再扫 `Projectile` 名字池兜底。火箭分裂弹数量固定 6 枚，继续走对象池与复用的 `raycastBuffer`；取消出生 `0.1s` 免碰撞窗口和 `SplitFuseTime` 定时爆炸，让分裂弹从第一帧开始使用同一套 `SphereCastNonAlloc` 检测敌人、墙和地面，自身不互相碰撞，命中后由原版 `Projectile.Update()` 根据 `context.explosionRange` 走 `ExplosionManager.CreateExplosion` 爆炸，未命中则按射程自然结束。重力分裂弹禁用追踪，二段初速从 `0.78x` 降到 `0.12x`，`SplitGravity` 提到 `24`，带重力 Radial 分裂固定使用世界水平圆环轴并加入轻微向下偏置，使其按水平 360 度径向初速 + 重力在首发弹四周下坠散开；非原版预设的二段爆炸才保留自定义 `OverlapSphereNonAlloc` 半径伤害，避免原版火箭双重伤害/双重特效。火焰爆炸 FX 改为尊重 profile 的 `ExplosionFxDuration`，火箭小爆炸按 `0.35s` 级别清理，避免固定残留 2 秒。
**兼容性影响**: 不涉及存档、配置、TypeID 或资源文件变更；仅调整焚天龙铳火箭弹运行时弹幕表现。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\DragonKingBossGunRocketSplitGuard.py` 通过
3. Guard: `python tests\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\DragonKingBossGunAmmoSwitchGuard.py` 通过
5. Guard: `python tests\DragonKingBossGunReforgeBaselineGuard.py` 通过
6. Guard: `python tests\F3DragonKingBossGunDebugKitGuard.py` 通过
7. 格式: `git diff --check -- Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs tests/DragonKingBossGunRocketSplitGuard.py FIX_TRACKER.md` 通过
**未验证/需人工**: 需要进游戏用 TypeID `326` 火箭弹实测，确认日志出现 `按火箭弹 TypeID=326 缓存原版火箭弹预制体: BulletRocket`，主弹在鼠标瞄准点附近空爆、空爆后散出 6 枚子火箭、分裂弹不会因彼此重叠互相引爆、分裂弹可正常因命中敌人/墙/地面触发原版火箭爆炸且未命中时按射程自然结束。
---
### 2026-07-06 焚天龙铳弹种 baseline 与射击热路径修复

**状态**: fixed
**Finding**: CR-2026-07-05-001 / CR-2026-07-05-002 / CR-2026-07-05-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于已确认的焚天龙铳运行时稳定性修复
**现象**: 重铸过容量的焚天龙铳连续切换弹种时，弹匣 baseline 可能因取整反推被污染；场景切换清理会丢失手持枪的弹种 baseline；射击兜底每发重复写 Stat 并在 dev 模式刷覆盖日志；弹药 UI 选择仅靠 Caliber 兼容的 TypeID 时，本次换弹可能沿用上一弹种容量/换弹时间。
**根因**: 弹种 baseline 捕获优先从已套 profile 的当前 Stat 反推；`ClearSceneCaches()` 同时清掉枪实例弹种状态；`ShootOneBullet` 路径没有按枪实例跳过已应用的同 profile；`SetTargetBulletType(int)` / 空枪 TargetBulletID 兜底只查 `TryResolveTypeId`，没有回到真实弹药实例走 Caliber 解析。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `tests/DragonKingBossGunAmmoSwitchGuard.py`
- 修改文件: `tests/DragonKingBossGunReforgeBaselineGuard.py`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
**兼容性影响**: 不改 TypeID、存档 key、配置 schema、资源命名或掉落表；仅调整焚天龙铳运行时 Stat 覆盖缓存生命周期与重复写入判定。场景级弹幕/命中缓存仍在切图时清理，枪实例弹种 baseline 改为卸载/reset 时清理；targetTypeId 解析兜底只读取背包/枪内弹药实例用于 Caliber profile 同步。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunAmmoSwitchGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunReforgeBaselineGuard.py` 通过
5. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
6. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内验证重铸容量后连续切弹、切图后当前弹种属性、dev 模式高射速射击日志量，以及 UI 选择仅按 Caliber 兼容弹药后的首次换弹容量/时间。
**失败尝试**: 无

---
### 2026-07-04 Mode E/F 刷怪卡顿低风险优化

**状态**: fixed
**Finding**: 无（玩家反馈 + `docs/测试分析/2026-07-03_ModeE刷怪卡顿无行为优化审查.md` 静态审查建议）
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 用户要求按审查文档执行；当前工作区已补齐普通 Boss plan 化/隐藏物化、Mode E/F 共享 postprocess scheduler、提交屏障，以及三类自定义特殊 Boss 的 Mode E/F 显式 deferred activation。P0 现有 dev 日志仅覆盖 Mode E 开局，剩余三类场景仍需同机 profiler 复测。
**现象**: 玩家反馈 Mode E/F 刷怪时仍会“一卡一卡”，尤其单只 Boss 生成配置链和重刷道具连续生成时容易产生主观尖刺。
**根因**: 静态核对显示普通 Boss 的配装/倍率/激活/变异词条/掉落追踪与 Mode E/F 登记回调仍会压到相邻帧；BossRegen 词条开启时 Mode E/F 还会每帧重建存活 Boss 列表；挑衅烟雾弹首次使用时会同步解析原版烟雾 VFX；最近点选择仍对全量点排序。
**修复内容**:
- 新增文件: 无
- 修改文件: `ModBehaviour.cs`
- 修改文件: `Utilities/EnemySpawnCore.cs`
- 修改文件: `Utilities/ModeRuntimeHooks.cs`
- 修改文件: `ModeE/ModeE.cs`
- 修改文件: `ModeE/ModeEStartup.cs`
- 修改文件: `ModeE/ModeEIntegrityAndHelpers.cs`
- 修改文件: `ModeE/ModeERespawnItems.cs`
- 修改文件: `ModeE/ModeEBattle.cs`
- 修改文件: `ModeF/ModeFEntry.cs`
- 修改文件: `ModeF/ModeFPhases.cs`
- 修改文件: `ModeF/ModeFRespawn.cs`
 - 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
 - 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
 - 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
 - 修改文件: `tests/ModeEFSpawnPostprocessSchedulerGuard.py`
 - 修改文件: `tests/ManagedSpecialBossDeferredActivationGuard.py`
 - 修改文件: `tests/ModeESpawnFailureResolutionGuard.py`
**兼容性影响**: 不改 Boss 数量、阵营、刷怪点、安全距离、重刷 250ms 节奏、掉落/奖励、TypeID、配置或存档 schema。新增普通 Boss 激活前一帧屏障默认关闭，仅 Mode E/F 显式启用；不会出现已激活但未登记的跨帧窗口。BossRegen 仍在有非空缓存时每帧调用 `TickBossRegen` 推进内部 10 秒计时。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_dev.bat"` 通过
2. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
3. Guard: `python tests\\ModeEFSpawnPostprocessSchedulerGuard.py` 通过
4. Guard: `python tests\\ManagedSpecialBossDeferredActivationGuard.py` 通过
5. Guard: `python tests\\EnemySpawnCoreObservableGuard.py` 通过
6. Guard: `python tests\\ModeFRespawnObservableSpawnGuard.py` 通过
7. Guard: `python tests\\ModeEFSpawnParityGuard.py` 通过
8. Guard: `python tests\\ModeESpawnFailureResolutionGuard.py` 通过
9. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
10. 人工 smoke: 未运行
11. dev/profiler 日志: 2026-07-04 `latest.log` / `2026-07-04_11-20-22.log` 已覆盖 `PrepareModeEStartup`、`StartModeE` 与普通 Boss `ModeEFSpawnPostprocess`
**未验证/需人工**: 现有本机 dev 日志未覆盖 `ModeERespawn`（挑衅烟雾弹重刷）、`StartModeF`（Mode F 开局批量刷怪）、`ModeFRespawn`（Mode F 死亡补位）。已按文档实现并通过编译/guard，但仍需实机 profiler 与游戏内 smoke 才能断言问题已解决。
**失败尝试**: 无

---
### 2026-07-02 进存档时卡死在许愿台弹幕预热
**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要
**现象**: 进入基地存档后主线程卡死，最新日志停在 `WishFountain` 注册完 `OnBuildingBuilt` / `OnBuildingDestroyed` 事件后，不再继续打印“布满了灰尘的星愿许愿台建筑系统初始化完成”。
**根因**: `WishFountainView.CreateRuntime()` 在初始化阶段调用弹幕层预热，随后进入 `WishFountainDanmakuView.EnsurePoolCapacity()`。原实现用 `AcquireItem()` 在 `while (allocatedItemCount < targetCount)` 里反复从池中取出再放回；首个对象创建后，后续循环只会复用池中对象，不再增加 `allocatedItemCount`，导致循环永不退出，主线程卡在许愿台 View 运行时构建阶段。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、外部协议或反射目标变更；仅修正弹幕对象池预热的容量补足逻辑，保持既有 UI 行为与资源结构不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: 未运行（本次未触及相关 guard 断言结构）
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新加载问题存档，确认不再卡死，且许愿台面板首次打开时弹幕层仍能正常显示。
**失败尝试**: 无

## 修复记录

---
### 2026-07-02 BossRush 动态物品重启后显示白底问号

**状态**: fixed
**Finding**: 玩家反馈 / 鸭科夫源码核对
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 玩家反馈 Mod 启用/禁用后装备和道具会恢复可用，但完整重启游戏后又变成白底问号占位图标；前置存在且排序在本 Mod 前。
**根因**: 官方 `ItemAssetsCollection.GetMetaData/GetPrefab/InstantiateSync/InstantiateAsync` 在 BossRush 延迟内容 bootstrap 完成前被存档、仓库、商店或 UI 按 TypeID 调用时，动态 prefab 尚未注册；`InstantiateSync` 会创建 `FallbackItem_<id>`，`GetMetaData` 也会返回默认 metadata，最终表现为白底问号或不可用占位。
**修复内容**:
- 新增文件: `Integration/BossRushDynamicItemRegistry.cs`（已加入 `compile_official.bat`）
- 新增文件: `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`（已加入 `compile_official.bat`）
- 新增文件: `tests/BossRushDynamicItemRegistryGuard.py`
- 修改文件: `Integration/BossRushIntegration.cs`
- 修改文件: `Integration/EquipmentContentRegistry.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs`
- 修改文件: `Integration/Bonus/SetBonusPlaceholderRegistry.cs`
- 修改文件: `Integration/WikiBookItem.cs`
- 修改文件: `LootAndRewards/LootAndRewardsSpecialLoot.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs`
- 修改文件: `tests/DragonBossRewardContentPreloadGuard.py`
- 修改文件: `tests/PhantomWitchScytheRewardBundleGuard.py`
- 修改文件: `docs/contracts.md`
- 修改文件: `docs/架构说明/Harmony补丁契约稳定性.md`
- 修改文件: `docs/Bossrush使用物品ID表.md`
- 修改文件: `docs/制作教程/WikiBookUI_Guide.md`

**兼容性影响**: 不改 TypeID、存档 key、配置 schema 或资源命名；新增 Harmony prefix 覆盖官方按 TypeID 查询/实例化入口，按需精确加载已登记的 BossRush 资源。统一注册表优先复用现有 Config 常量和集中 TypeID 数组，避免多处重复维护 bundle/TypeID 映射；冒险家日志旧教程临时 ID `500100` 会在运行时收敛为发布 ID `500007`。属于向后兼容运行时兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\BossRushDynamicItemRegistryGuard.py` 通过
3. Guard: `python tests\DragonBossRewardContentPreloadGuard.py` 通过
4. Guard: `python tests\PhantomWitchScytheRewardBundleGuard.py` 通过
5. Guard: `python tests\ContentRegistryGuard.py` 通过
6. Guard: `python tests\DeferredIntegrationBootstrapGuard.py` 通过
7. Guard: `python tests\SetBonusLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内用包含 BossRush 自定义物品/装备的存档完整重启后进入基地/仓库/背包，确认 `500001-500056` 内已发布物品不再显示白底问号，装备可正常实例化、装备和使用。
**失败尝试**: 无

---
### 2026-07-02 已建许愿台在旧存档进基地时可能不显示

**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 基地加载时原版 `BuildingArea.Display` 在许愿台注入前先报 `No prefab for building starwish_fountain`；如果玩家存档里已经放过许愿台，该建筑可能不会在本次进档时被实例化出来，导致场景里不可见也无法交互。
**根因**: 许愿台建筑数据原本只在 deferred base-scene setup 阶段注入，时机晚于原版建筑区首轮显示；注入完成后又没有像婚礼教堂那样走一次基地建筑区重绘，因此“首轮缺 prefab”不会被补刷回来。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainBuilder.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、反射目标或资源命名；仅把许愿台建筑注入提前到基地场景早期，并复用基地建筑区重绘 helper 作为晚注入兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py` 通过
3. Guard: `python tests\\SceneObjectTypeCacheGuard.py` 通过
4. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
5. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏加载一个已经建过许愿台的存档，确认基地首帧后建筑可见、可交互，且不再出现同轮次的缺 prefab 回归。

---
### 2026-07-02 许愿台弹幕当前打开轮次不接最新数据

**状态**: fixed
**Finding**: CR-2026-07-02-001
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 许愿台面板先用本地缓存或内存缓存启动弹幕后，联网成功拿到的新弹幕数据不会作用到当前这次打开；玩家要关掉再打开一次，才会看到更新后的内容池。
**根因**: `WishFountainUI.RefreshDanmakuDisplay()` 为避免中途整层重置，在“已有可见弹幕”场景下直接跳过成功回调里的显示更新，导致当前轮次只更新缓存、不更新在播来源。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅把联网成功后的新数据无缝切入后续入场弹幕，保留已有对象池与滚动状态。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuJsonEscapeGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏确认已有缓存时打开许愿台，联网返回后不会整层闪烁，且后续入场弹幕会换成最新数据源。

---
### 2026-07-02 许愿台弹幕失败结果被短 TTL 缓存，重开面板也不会立刻重试

**状态**: fixed
**Finding**: CR-2026-07-02-002
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 只要飞书鉴权或弹幕列表请求瞬时失败一次，玩家在接下来约 45 秒里重复关闭再打开许愿台，也只会马上复用失败结果或旧缓存，不会重新联网拉取。
**根因**: `TryReturnRecentDanmakuResult()` 把“最近一次失败”也纳入和成功结果相同的 TTL 快取，导致 reopen 无法触发新的网络请求。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainService.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅将 TTL 缓存限定为成功结果，失败后允许玩家重开面板立即重试。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏在弱网或临时断网后反复开关许愿台，确认恢复联网后无需等待 45 秒即可重新拉到弹幕。

---
### 2026-07-02 许愿台关闭后未解绑静态弹幕回调

**状态**: fixed
**Finding**: CR-2026-07-02-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 在慢网、切场景或频繁开关许愿台时，旧面板实例会一直被静态拉取回调引用到请求结束；虽然版本号判断会把回调变成 no-op，但这些闭包和 View 引用会额外滞留一段时间。
**根因**: `WishFountainService.RequestRecentWishes()` 把 success/failure lambda 追加到静态 waiter 委托中，而 `WishFountainView.CancelDanmakuFetch()` 只递增本地版本号，没有显式从静态 waiter 中退订。
**修复内容**:
- 新增文件: `tests/WishDanmakuFetchLifecycleGuard.py`
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、Harmony/反射或资源结构；仅为弹幕拉取 waiter 增加显式解绑路径，降低旧 View 在慢网场景下的无效保留。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏弱网下频繁打开/关闭许愿台并切图，确认不会报错、不会出现旧 UI 干扰下一次打开。

---
### 2026-07-01 “小明”非 Boss 预设误入 Boss 池

**状态**: fixed
**Finding**: 玩家反馈 / `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: `Player.log` 先只能看到鸭鸭市场通知队列输出 `<color=red>小明</color> 将在 ... 秒后抵达战场` 与 `第 6/7 波: <color=red>小明</color> ...`；打开开发日志后确认该官方角色预设 `nameKey` 为 `Character_Ming`。
**根因**: Boss 池动态扫描当前按 `CharacterRandomPreset.showName`、阵营和基础血量筛选敌对预设；“小明”属于有显示名且满足基础条件的非 Boss 角色，因此绕过了既有 `showName=false` 小怪清理规则。
**修复内容**:
- 新增文件: 无
- 修改文件: `WavesArena/WavesArena.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、Harmony/反射或资源文件变更；仅在 Boss 池初始化/缓存清理阶段按稳定预设名 `Character_Ming` 硬排除已知非 Boss 角色“小明”。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\ModeEFPrewarmCacheGuard.py` 通过
3. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
4. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新开一局 BossRush，确认 Boss 池配置窗口和波次预告不再出现“小明”。

---
### 2026-07-01 龙皇孩儿护我失效、龙裔同源复活风险与 Boss 名称兼容

**状态**: fixed  
**Finding**: 日志排查 / 玩家反馈  
**兼容分类**: COMPAT / WIRE+  
**版本/Commit**: 未提交  
**Owner decision**: 不需要  

**现象**: 玩家反馈更新后焚天龙皇“孩儿护我”不再触发；对照代码后确认龙裔遗族的一次性复活也存在同源风险。部分外部模组会把 BossRush 自定义 Boss 名称显示成 `Unknown`。最新 `Player.log` 里还能看到婚礼建筑初始化阶段触发的 `BuildingArea.RepaintAll()` 原版 `Debug.LogError`。  
**根因**:  
1. 原版 `Health.Hurt()` 在当前官源中的执行顺序为“扣血 -> 死亡分支 -> `OnDeadEvent` -> `SetActive(false)` -> `OnHurtEvent`”。龙皇孩儿护我和龙裔复活都挂在 `OnHurtEvent`，致死伤害会先把对象送入死亡路径，导致保命机制来不及触发。  
2. 三只自定义 Boss 的运行时 `CharacterRandomPreset.name` 使用内部 `*_Preset` 名称，且龙皇/幽灵女巫缺少 `Characters_` / 运行时 preset 名称别名，本地化兼容键不完整，外部模组若读取 `preset.name` 或兼容键，容易回退成 `Unknown`。  
3. 婚礼建筑系统初始化时无条件重绘基地建筑区，即使当前存档没有放置婚礼教堂，也会提前触发一次原版建筑重绘。  
**修复内容**:
- 新增文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`（已加入 `compile_official.bat`）
- 修改文件: `Integration/DragonKing/DragonKingAbilityController_AttackFlow.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantAbilities_ResurrectionAndPhase.cs`
- 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
- 修改文件: `Localization/EquipmentLocalization.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `compile_official.bat`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID 变更；新增一处针对 `Health.Hurt` / `Health.CurrentHealth` 的 Harmony 兼容补丁；运行时 Boss preset 的 `name` 统一改为稳定 `BossNameKey`，仅影响运行时识别兼容性。  
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat`
2. Guard: `python tests\\BossCleanupSharedHelperGuard.py`
3. Guard: `python tests\\DragonKingBossEventLifecycleGuard.py`
4. Guard: `python tests\\DragonKingChildProtectionTransformCacheGuard.py`
5. Guard: `python tests\\SceneObjectTypeCacheGuard.py`
6. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py`
**未验证/需人工**:
- 游戏内实机确认龙皇“孩儿护我”可再次触发，且击杀其召出的龙裔后能正常联动处死龙皇。
- 游戏内实机确认龙裔遗族一次性复活恢复为 50% 血量后仍能完整进入狂暴二阶段。
- 使用玩家提到的外部模组实机确认三只自定义 Boss 不再显示为 `Unknown`。
- 若存档里已经放置婚礼教堂，需要在基地场景实机确认跳过无意义重绘后，旧存档中的教堂仍能正确显示。

---
### 2026-07-01 售货机 UI 崩溃（延迟注入导致 itemInstances 未缓存）

**状态**: fixed  
**Finding**: CR-2026-07-01-001  
**兼容分类**: WIRE- / OPERATIONAL  
**版本/Commit**: `64c4572`（旧记录）  
**Owner decision**: 不需要，属于 P0 回归修复  

**现象**: 进入基地打开售货机，UI 无法显示，`Player.log` 出现 `StockShopItemEntry.Setup()` 相关 `NullReferenceException`。

**根因**: 性能优化把商店条目注入改为延迟执行，晚于原生 `StockShop.Start()` 的 `CacheItemInstances()`。延迟注入条目的物品实例没有进入 `itemInstances` 缓存，后续 UI 取实例返回 null。

**修复内容**:

- 新增文件: `Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`（旧记录显示已加入 `compile_official.bat`）
- 修改文件: `compile_official.bat`

**修复原理**: 在 `StockShop.GetItemInstanceDirect(typeID)` 入口兜底缓存未命中的延迟注入条目，使补丁与注入时机解耦。

**兼容性影响**: 原生商品缓存命中路径不变；BossRush 延迟注入商品首次访问多一次同步实例化。

**验证方法**:

1. Windows 编译：`compile_official.bat`
2. 人工 smoke：基地打开售货机，确认 BossRush 注入商品显示且可购买。

**未验证/需人工**: 本次文档收敛未重新运行游戏，仅迁移旧 confirmed 记录。

**失败尝试**: 旧记录中的 `StockShop.Awake` 早期注入补丁受 `ModBehaviour.Instance` 初始化时序影响，不能稳定解决。

## 变更日志

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-07-01 | AI 协作文档收敛 | 从旧 `docs/协作/FIX_TRACKER.md` 迁移 confirmed 修复记录；新增状态、owner decision、兼容分类字段。 |
