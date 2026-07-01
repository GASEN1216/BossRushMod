# AI 协作文档收敛迁移记录

日期：2026-07-01  
范围：只改文档，不改业务代码。

## 1. Inventory 摘要

| 路径 | 旧用途 | 处理 |
| --- | --- | --- |
| `AGENTS.md` | 根级协作规则 | 合并重写为唯一事实源 |
| `CLAUDE.md`、`GEMINI.md` | Claude/Gemini 入口 | 改为薄转发 |
| `.github/copilot-instructions.md` | Copilot 入口 | 改为薄转发 |
| `.cursor/rules/agents.mdc` | Cursor 入口 | 保留 frontmatter，正文改为薄转发 |
| `README.md`、`README_EN.md` | 用户/开发总览 | 保留；README_EN 补 AI 入口提示 |
| `docs/AI使用提示词.md` | 旧 AI 提示词和历史需求片段 | 改为转发/归档说明，保留少量原则到 AGENTS |
| `docs/代码审查/CODE_REVIEW.md` | 旧审查方法 | 内容迁移到根 `CODE_REVIEW.md`，旧路径转发 |
| `docs/代码审查/CODE_REVIEW_FINDINGS.md` | 旧 confirmed findings | 迁移到根 `CODE_REVIEW_FINDINGS.md`，旧路径转发 |
| `docs/协作/FIX_TRACKER.md` | 旧修复流水 | 迁移到根 `FIX_TRACKER.md`，旧路径转发 |
| `docs/架构说明/*.md` | 专项架构约定 | 保留，AGENTS 索引 |
| `.kiro/specs/architecture-extensibility-refactor/*` | 历史重构 spec/plan | 保留为历史，不作为当前规则源 |
| `.cunzhi-memory/*`、`.claude/*` | 工具私有记忆/计划/权限 | 不迁为 canonical；仅记录冲突/偏好 |
| `skills/*/SKILL.md`、`codex-skills/*/SKILL.md` | 技能工作流 | 保留；与 AGENTS 冲突时以 AGENTS 为准 |
| `docs/飞书应用密钥.md` | 本地敏感资料 | 未展开迁移；只记录不要提交/泄露 |
| 大量 `docs/设计文档`、`docs/实现方案`、`docs/superpowers`、`docs/视频策划` | 设计稿、历史计划、内容策划 | 保留为参考，不作为协作规则 |

## 2. 保留并迁移的规则

- 新增 `.cs` 必须加入 `compile_official.bat`。
- 仅 Windows 能真正编译，WSL/Linux 不能声明“已编译验证”。
- TypeID 严格递增、不复用、不回填空洞。
- `DisplayNameRaw = "BossRush_*"` 必须配本地化注入。
- Boss 生成后敌对性安全网 `SetTeam(Teams.wolf)` 不得移除。
- 静态/全局事件订阅必须幂等并退订。
- 防御式 catch 是宿主防崩策略，不成批清理。
- Config 三层归位、Hooks 分层、Utilities 边界、BOSS 模板约定继续有效。
- Python guard 与被守卫结构同步。
- ZombieMode 不接共享 mutator roll，loot 类变异不回归。
- 审查只记录 confirmed finding；seeded lead 不等于 bug。
- 修复后记录验证方式、失败尝试和兼容分类。

## 3. 改写压缩的规则

- 多个 AI 入口重复的红线被压缩进根 `AGENTS.md`，入口文件只保留转发。
- 旧 `CODE_REVIEW.md` 的 9 维检查清单压缩为阶段式审查和必查清单。
- 旧 `FIX_TRACKER.md` 模板保留核心字段，新增兼容分类和 owner decision。
- Kiro/spec 中“行为保真、先结构后语义、小步验证”被保留为 AGENTS 的修改前/修改后要求。
- `.cunzhi-memory` 中“低端机可用不是过度删减”被归并为性能审查语境，不单独作为硬规则。

## 4. 覆盖或废弃的旧规则

- 旧 AI 入口中的重复红线不再独立维护。
- `docs/AI使用提示词.md` 中具体旧任务需求、地图坐标、一次性提示不进入 canonical。
- 旧 `bossrush-code-review` skill 中“所有 external-facing methods 都必须 try/catch + DevLog”的泛化要求被覆盖：新规则采用“保留防崩空 catch，关键初始化/绑定/存档路径补低噪声日志”。
- `.cunzhi-memory/preferences.md` 中“不生成总结性 Markdown 文档/不要生成测试脚本”的历史偏好被本次用户明确请求覆盖。本次创建迁移/审查/契约文档。
- `.kiro/specs/*` 中已完成的 P1/P2/P3 任务清单不作为当前待办，只作为历史证据。

## 5. 冲突记录

| 冲突点 | 旧说法 | 新采用说法 | 理由 | 是否需确认 |
| --- | --- | --- | --- | --- |
| 审查/修复文档位置 | 旧 AGENTS 说过程文档放 `docs/` local-only | 根级 `CODE_REVIEW.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md` 为当前入口，旧 docs 路径转发 | 用户明确要求创建这些根级文件；兼容旧路径 | 需要 owner 确认是否纳入 git |
| catch 日志 | `bossrush-code-review` 要求 external-facing method 都 DevLog | 防御式 catch 保留；只在关键路径补低噪声日志 | 当前 AGENTS/架构文档明确 catch 是宿主防崩保险 | 不需要，除非 owner 想更新 skill |
| AI 入口厚度 | Copilot/Cursor/Claude/Gemini 各自重复红线 | 全部薄转发到 AGENTS | 避免多处分叉 | 不需要 |
| docs 是否 git 跟踪 | docs local-only | docs 仍 local-only；根级新流程文档是否跟踪待定 | 兼容旧约定和新请求 | 需要 owner 确认 |
| 旧工具偏好 | 不生成总结性 Markdown | 本次生成迁移、契约、审查文档 | 用户当前明确要求 | 不需要 |

## 6. Needs owner confirmation

1. 根级 `CODE_REVIEW.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md` 是否要加入版本控制，还是仅作为本地协作文件保留。
2. 是否需要同步更新 `skills/bossrush-code-review/SKILL.md`，使其 catch 日志要求与新 AGENTS 完全一致。
3. `docs/飞书应用密钥.md` 是否需要改名为更明显的 local-secret 名称，或迁移到不被 AI 默认读取的位置。
4. 是否希望为 `ModeD/`、`ModeE/`、`ModeF/` 分别新增更细的子系统 `AGENTS.md`。本次只为 Integration、Patches、Utilities、ZombieMode、tests、docs 创建专项入口。

## 7. 以后 AI 应按什么顺序读

1. `AGENTS.md`
2. 最近目录的 `AGENTS.md`
3. `docs/contracts.md`
4. `CODE_REVIEW.md` / `CODE_REVIEW_FINDINGS.md` / `FIX_TRACKER.md`（审查或修复任务）
5. `docs/架构说明/` 对应专项文档
6. README / 项目全景文档
7. 实际代码、构建脚本、guard

## 8. 本次未做

- 未修改业务代码。
- 未运行 `compile_official.bat`。
- 未运行 Python guard 全量套件。
- 未提交 git commit。
