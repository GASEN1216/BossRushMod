# AI 使用提示词（归档入口）

旧版本文混合了通用 AI 提示、历史任务原文、地图坐标和一次性需求。它不再作为仓库级规则源。

当前 AI 协作入口：

1. `../AGENTS.md`
2. 最近目录的 `<subsystem>/AGENTS.md`
3. `../CODE_REVIEW.md`
4. `../CODE_REVIEW_FINDINGS.md`
5. `../FIX_TRACKER.md`
6. `contracts.md`
7. `ai-docs-migration.md`

旧提示词中仍保留的原则已经迁移到 `../AGENTS.md`（主要在 §7、§8）和 `ai-docs-migration.md`：读实际代码、最小化修改（指不做与任务无关的改动，不限制完成任务所需的新增内容）、复用现有架构、低端机 / 中端机性能意识、验证后再宣称完成并标明证据级别。
