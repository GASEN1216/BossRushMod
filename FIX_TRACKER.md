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

## 修复记录

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
