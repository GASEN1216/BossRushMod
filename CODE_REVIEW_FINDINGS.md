# CODE_REVIEW_FINDINGS.md — 已确认问题库

> 只记录 confirmed findings。未验证线索放本文件的 UNVERIFIED 区，或在 `FIX_TRACKER.md` 中标为 `accepted/deferred/refuted/documented`。

## 状态汇总

| 严重级 | Open | Fixed | Deferred | WontFix | 合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| P0 | 0 | 1 | 0 | 0 | 1 |
| P1 | 0 | 0 | 0 | 0 | 0 |
| P2 | 0 | 0 | 0 | 0 | 0 |
| P3 | 0 | 0 | 0 | 0 | 0 |

最后更新：2026-07-01。

## Confirmed Findings

### CR-2026-07-01-001：售货机 UI 崩溃 — 延迟注入商品未缓存 itemInstance

**严重级**：P0 Blocker  
**兼容分类**：`WIRE-` / `OPERATIONAL`  
**状态**：Fixed  
**来源**：从旧 `docs/代码审查/CODE_REVIEW_FINDINGS.md` 与 `docs/协作/FIX_TRACKER.md` 迁移；本次文档收敛未重新进游戏验证。

#### 位置

- 修复文件：`Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`
- 编译清单：`compile_official.bat`

#### 问题

商店条目延迟注入晚于原生 `StockShop.Start()` 的缓存时机，导致 BossRush 注入商品的 `itemInstance` 未缓存。`StockShopItemEntry.Setup()` 取到 null 后访问 `item.StackCount` 触发 `NullReferenceException`，售货机 UI 无法打开。

#### 修复

通过 Harmony Prefix 拦截 `StockShop.GetItemInstanceDirect(typeID)`：缓存命中时放行；缓存未命中且属于延迟注入条目时即时实例化并写回缓存，使调用点不再拿到 null。

#### 验证需求

Windows 实机：进入基地，打开售货机，确认商品显示、可购买，`Player.log` 无对应 NPE。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- （空）

## 新条目模板

```markdown
### CR-YYYY-MM-DD-NNN：问题标题

**严重级**：P0/P1/P2/P3  
**兼容分类**：SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**状态**：Open / Fixed / Deferred / WontFix  
**来源**：代码审查 / 用户复现 / Player.log / guard / 人工 smoke

#### 位置

- `文件路径:行号`

#### 问题

是什么错，为什么是错。

#### 影响

玩家可见后果、静默失效、性能、存档或维护风险。

#### 建议修复

最小修复步骤。

#### 验证需求

编译、guard、人工 smoke 或无法验证原因。
```
