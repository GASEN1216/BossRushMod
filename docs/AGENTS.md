# docs/AGENTS.md — 文档目录专项规则

> 先读根目录 `AGENTS.md`。目录总览见 `docs/README.md`。

## 纳管

- `docs/` 默认 local-only（`.gitignore` 的 `/docs/*`）。例外是被守卫直接读取的文档，在 `.gitignore` 里逐个放行；新增这类「守卫依赖的文档」时同步放行，否则 fresh clone 一跑守卫就红。当前放行清单以 `.gitignore` 为准（`docs/contracts.md` 在内）。
- `docs/architecture/` 等仍是 local-only。根 `AGENTS.md` 或子系统 `AGENTS.md` 引用它们时，关键结论要在 AGENTS 或守卫里有落点，不能只存在于本地文档。
- 含密钥的本地文件（`飞书应用密钥.md`、`AI生图API和密钥.md`）不复制进回答、提交、公开文档或提示词。

## 放哪里

目录按 Diátaxis 加「设计 / 报告 / 参考」的常见分法组织。目录名用英文，文件名保持中文。

| 内容 | 位置 | 要求 |
| --- | --- | --- |
| 长期有效的架构约定与系统设计说明（explanation） | `docs/architecture/` | 与代码保持一致；只写文件与符号名，不写行号 |
| 制作教程、操作手册、接线清单（tutorial / how-to） | `docs/guides/`，天空岛专项在 `docs/guides/sky-island/` | 步骤、命令、路径与当前代码和工具一致 |
| 查表类资料：物品 ID 表、官方本地化表、分类表、参考图 | `docs/reference/` | 以代码为准，改代码时同步 |
| 外部契约、breaking 边界、TypeID 台账、官方 API 陷阱 | `docs/contracts.md`（守卫解析它的 §1，路径不动） | 同上 |
| 设计稿与提案 | `docs/design/`；末日丧尸在 `design/zombie-mode/`，未实现的拓展在 `design/roadmap/` | 文首写状态行：已实现 / 部分实现 / 未实现 / 已放弃 + 核对日期 |
| 审查、测试、实机验收、交付评估的结论报告 | `docs/reports/reviews/`、`reports/testing/`、`reports/sky-island/`，文件名带日期 | 历史快照，文首注明；不追着代码改，但会误导的「现行做法」要标已过时 |
| 版本记录 | `docs/changelog/` | |
| 可复用的长任务提示词 | `docs/prompts/` | 现状数字写「以当前代码 / 命令输出为准」 |
| AI 协作文档的变更与冲突记录 | `docs/ai-docs-migration.md` | |

- 过程件不留：批次 csv / json、中间轮次、逐轮截图、实施计划书在结论写进报告、`FIX_TRACKER.md` 或 `CODE_REVIEW_FINDINGS.md` 后删除；每个专题只留结论件。
- 一个专题的设计、报告分别进 `design/` 与 `reports/`，不再为单个专题建顶层目录。

## 写文档的规则

- 历史计划、视频稿、旧设计稿不是当前代码事实，只能当线索；和代码冲突时以代码为准。
- 某一轮交付的「本轮不加 TypeID / 不加存档字段 / 不重打包」只描述那一轮的范围：放在带日期的小节里，写明是哪一批。**不要写成长期禁令**；长期规则只写在根 `AGENTS.md`、子系统 `AGENTS.md` 或守卫里。
- 数字（守卫数、文件数、包体大小、顶点数、下一可用 TypeID）会过期。规则文档里不写死，需要时写「以某命令 / 某文件为准」；交付记录里写数字要带日期。
- 引用路径用仓库内真实路径。移动或改名文档时，同步改所有引用：`AGENTS.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md`、`.qoder/repowiki/`、代码注释与守卫文案。
- 包含未确认事实的文档标 `Needs owner confirmation`。

## 写给无人值守代理的提示词

- 不设「停下等 owner 确认」的闸门：无人应答，任务会卡死在第一个决策点。遇到歧义自行决定（玩法取舍好玩优先，其次离线可证、改动小、可回退），把决定、理由与回退办法写进待拍板清单。
- 写清授权边界（能否重打包、部署、本地提交；开不开游戏、碰不碰玩家存档）和止损规则。
- 把证据分级 L1 / L2 / L3 写进提示词，并要求交付一份粒度到操作步骤的人工验证清单。
- 现状数字写「以当前代码 / 命令输出为准」，不要从旧提示词里抄。
