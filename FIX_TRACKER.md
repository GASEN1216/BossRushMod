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
