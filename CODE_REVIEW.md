# CODE_REVIEW.md — BossRushMod 代码审查方法

> 本文件是当前代码审查入口。旧路径 `docs/代码审查/CODE_REVIEW.md` 只做兼容转发。

## 1. 基本原则

- 审查先看风险，不先看风格。
- seeded lead 是未验证线索，不是 bug。
- finding 必须有代码、日志、运行时复现或 guard 证据支撑。
- 不把既有设计决策当缺陷：防御式 catch、离散模式 bool、docs local-only、loot 变异移除等，除非有新证据证明它们破坏当前需求。
- 只审查本轮改动及其直接影响面；预存债务记录到 `CODE_REVIEW_FINDINGS.md` 的 UNVERIFIED 或 `FIX_TRACKER.md` 的 deferred，不顺手扩大范围。

## 2. 审查阶段

1. **范围界定**：记录 `git status --short`，查看 `git diff --stat`、变更文件、是否有用户未提交业务改动。
2. **项目门禁**：新增 `.cs` 是否加入 `compile_official.bat`；TypeID 是否递增登记；本地化 key 是否注入；guard 是否同步。
3. **兼容契约**：按 `docs/contracts.md` 判断 `SAFE`、`COMPAT`、`SCHEMA±`、`WIRE±`、`BREAKING`、`OPERATIONAL`。
4. **运行时风险**：事件订阅、Harmony/反射、刷怪队伍、模式状态、存档、UI 注入、过图热路径。
5. **最小改动**：是否有无关重构、格式化、命名清洗、 speculative abstraction。
6. **验证闭环**：记录已跑命令、未跑原因、仍需人工 smoke 的点。

## 3. 严重等级

| 等级 | 含义 | 处理 |
| --- | --- | --- |
| `P0 Blocker` | 编译失败、静默不编译、存档损坏、宿主崩溃、核心玩法不可用 | 阻断发布/合并 |
| `P1 Major` | 功能未接线、跨局泄漏、反射/Harmony 静默失效、本地化缺失、玩家可见主流程回归 | 核心路径阻断；否则下版本前修 |
| `P2 Minor` | 局部性能退化、防御不足、维护性风险、非核心 UI 问题 | 排期处理 |
| `P3 Note` | 设计建议、后续重构素材、需 owner 取舍 | 不当作 bug |

## 4. Finding ID

Confirmed finding 使用：

```text
CR-YYYY-MM-DD-NNN
```

示例：`CR-2026-07-01-001`。

同一天按发现顺序递增。跨文档引用必须使用同一个 ID。

## 5. Seeded Leads

旧审查报告、AI 提示词、用户怀疑、Player.log 片段、历史计划中的“可能问题”全部先放 seeded lead。处理方式：

- 先定位代码和调用链。
- 能复现或静态证明才升格为 confirmed finding。
- 证伪后写 `refuted`，避免重复排查。
- 无法判断则标 `Needs owner confirmation` 或 `Needs runtime smoke`。

## 6. 必查清单

- 新增 `.cs`：`compile_official.bat` 命中。
- 新 TypeID：`docs/Bossrush使用物品ID表.md` 登记，未复用空洞。
- `DisplayNameRaw = "BossRush_*"`：有本地化注入并挂接。
- 事件订阅：幂等 + 退订。
- Harmony/反射：目标仍存在；重载需显式；官方更新后读 `docs/架构说明/Harmony补丁契约稳定性.md`。
- 刷怪：生成后敌对性、恢复系统、Mode E/F 独立阵营不被破坏。
- Config/Hooks：归位符合 `docs/架构说明/Config归位约定.md`、`docs/架构说明/Hooks分层约定.md`。
- ZombieMode：不接共享 mutator roll，不按性能档改变玩法，不破坏 run-only cleanup。
- 防御式 catch：不批量删除；关键路径新增日志要低噪声。

## 7. 输出格式

审查回复先列 findings，按严重级排序。每条包含：

- ID
- 严重级
- 兼容分类
- 文件/行号
- 证据
- 玩家/维护影响
- 建议修复
- 验证需求

没有 confirmed findings 时明确写“未发现 confirmed issue”，并列出剩余测试缺口。

## 8. 验证

代码改动默认要求：

```cmd
compile_official.bat
```

相关 guard：

```cmd
for %f in (tests\*.py) do python %f
```

或只跑相关脚本。运行时项必须写明是否已人工进游戏 smoke；未 smoke 不得写成已验证。
