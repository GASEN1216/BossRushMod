# CODE_REVIEW.md — BossRushMod 代码审查方法

> 当前代码审查入口。旧路径 `docs/代码审查/CODE_REVIEW.md` 只做转发。规则背景见根目录 `AGENTS.md`。

## 1. 基本原则

- 先看风险，再看风格。
- seeded lead 是未验证线索，不是 bug。finding 要有代码、日志、运行复现或守卫证据支撑。
- 不把既有设计决策当缺陷：防御式 catch、离散模式 bool、docs local-only、loot 变异移除、官方任务系统刻意不接等，除非有新证据证明它们破坏了当前需求。
- 某一轮交付说明里的「本轮不加 TypeID / 不改存档 / 不重打包」只是那一轮的范围，不是审查标准。新增内容本身不是问题；接不上、拿不到、破坏兼容才是。
- 审本轮改动及其直接影响面；顺带发现的既有债务记到 `CODE_REVIEW_FINDINGS.md` 的 UNVERIFIED 或 `FIX_TRACKER.md` 的 deferred，不顺手扩大范围。

## 2. 审查阶段

1. **范围**：记录 `git status --short`，看 `git diff --stat` 与变更文件。工作区可能有别的会话的未提交改动，先分清哪些属于本轮。
2. **项目门禁**：新增 `.cs` 进了 `compile_official.bat`；TypeID 台账三处一致；本地化 key 已注入；守卫同步。
3. **兼容契约**：按 `docs/contracts.md` 判定 `SAFE`、`COMPAT`、`SCHEMA±`、`WIRE±`、`BREAKING`、`OPERATIONAL`。
4. **运行时风险**：事件订阅、Harmony / 反射、刷怪阵营与距离休眠、模式状态、存档、UI 注入、过图热路径；官方 API 用法对照 `docs/contracts.md` §7.1。
5. **可达性**：新内容能不能从玩家入口拿到或走到——获取途径、入口接线、配置器登记。编译和守卫都查不出这一类问题。
6. **改动范围**：有没有与任务无关的重构、格式化、命名清洗、speculative abstraction；有没有另起第二套状态、缓存或解析器。
7. **验证闭环**：已跑的命令、没跑的原因、证据级别（L1 / L2 / L3）、仍需实机的点。

## 3. 严重等级

| 等级 | 含义 | 处理 |
| --- | --- | --- |
| `P0 Blocker` | 编译失败、静默不编译、存档损坏、宿主崩溃、核心玩法不可用 | 阻断发布 / 合并 |
| `P1 Major` | 功能未接线、跨局泄漏、反射 / Harmony 静默失效、本地化缺失、玩家可见主流程回归 | 核心路径阻断；否则下版本前修 |
| `P2 Minor` | 局部性能退化、防御不足、维护性风险、非核心 UI 问题 | 排期处理 |
| `P3 Note` | 设计建议、后续重构素材、需 owner 取舍 | 不当作 bug |

## 4. Finding ID

Confirmed finding 使用：

```text
CR-YYYY-MM-DD-NNN
```

示例：`CR-2026-07-01-001`。同一天按发现顺序递增，跨文档引用使用同一个 ID。

## 5. Seeded Leads

旧审查报告、AI 提示词、用户怀疑、Player.log 片段、历史计划里的「可能问题」一律先当 seeded lead：

- 先定位代码和调用链。
- 能复现或静态证明才升格为 confirmed finding。
- 证伪后写 `refuted`，避免重复排查。
- 无法判断则标 `Needs owner confirmation` 或 `Needs runtime smoke`。

根目录 2026-09-13 在 WSL 里生成的审查报告含编造锚点，同样按 seeded lead 处理。

## 6. 必查清单

- 新增 `.cs`：`compile_official.bat` 命中。
- 新 TypeID：`docs/contracts.md` §1、根 `AGENTS.md` 的 TypeID 台账、`docs/Bossrush使用物品ID表.md` 三处一致，没有回填空洞。
- 新物品：配置器、`BossRushDynamicItemRegistry`、`item.Value`、掉落黑名单、获取途径（清单见 `Integration/AGENTS.md`）。
- `DisplayNameRaw = "BossRush_*"`：有中英本地化注入并挂接。
- 事件订阅：幂等 + 退订。
- Harmony / 反射：目标仍存在；重载显式；官方更新后读 `docs/架构说明/Harmony补丁契约稳定性.md`。
- 刷怪：生成后敌对性、距离休眠解除、恢复系统、Mode E/F 独立阵营不被破坏。
- 自建伤害与爆炸：`canHurtSelf: false`、`isFromBuffOrEffect`、`fromWeaponItemID` 设置正确。
- Config / Hooks：归位符合 `docs/架构说明/Config归位约定.md`、`docs/架构说明/Hooks分层约定.md`；新 ModConfig 键进了白名单。
- 存档：新增字段是 `SCHEMA+`（可选、旧档有默认值、掩码同步），落盘复用共享引擎。
- ZombieMode：不接共享 mutator roll，不按性能档改变玩法，不破坏 run-only cleanup。
- 防御式 catch：不批量删除；关键路径新增日志要低噪声。
- 守卫改动：做过反向验证；没有放宽断言或加白名单掩盖问题；没有只靠子串判断。
- **UI 观感类改动**：
  - 底图分档传对了没有？细条（`radius <= 3`）不穿图集里的任何一张（Unity 会把 border 压到中心区归零）；卡片走 `Card`、分隔线走 `Rule`（rect 高度 ≥5）、滚动条走 `ScrollHandle`。
  - 深色面板要有边就调 `BossRushUI.ApplyPanelStroke`，不指望图集里烤进去的内描边——它会被深色 token 乘到约 3/255，肉眼不可见。
  - 按钮标签字色一律 `GetButtonTextColor(背景色)`，不写死 `TextPrimary`。
  - 对比度要算不要目测：正文 ≥4.5:1、大字 ≥3:1、可点控件的边界 ≥3:1；背景按**实际合成**算（token 的 alpha + 背后的场景亮度），不是拿两个 token 直接比。`tests/SkyIslandUiContrastGuard.py` 是现成的算法与探针模板。
  - 走 unscaled 时间的表现层带 `BossRushUI.IsGamePaused()` 门；缓动只用 `EaseOut`（位移）/ `SmoothStep`（原地淡变），不引入第三方缓动库。
  - 每帧路径：值没变就不写 RectTransform / CanvasGroup / Image.color。

## 7. 输出格式

审查回复先列 findings，按严重级排序。每条包含：

- ID
- 严重级
- 兼容分类
- 文件 / 行号
- 证据与证据级别
- 玩家 / 维护影响
- 建议修复
- 验证需求

没有 confirmed findings 时明确写「未发现 confirmed issue」，并列出剩余测试缺口。

## 8. 验证

命令见根 `AGENTS.md` §2。默认要求：

- `python tools/run_guards.py --changed-only`，大改动跑全量（`run_guards.bat` 等价）。不要用 `for %f in (tests\*.py) do python %f`：它不聚合结果，存在已知红项时会误判为已通过。已知红项登记在 `tests/known_red_guards.txt`。
- Windows 编译。本机跑不了时至少做语法探针 `python tools/verify_syntax.py --with-bcl`，并写成「语法通过，未正式编译」。
- 可隔离的逻辑跑执行回归：`python tools/run_runtime_regressions.py --filter <名字>`。
- 运行时项写明是否实机 smoke；未 smoke 的不写成已验证，证据级别按根 `AGENTS.md` §8 标注。
