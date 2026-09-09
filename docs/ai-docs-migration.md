# AI 协作文档收敛迁移记录

日期：2026-07-01  
范围：登记知识库和文档维护入口；同一提交另含 Mode G 功能基线。

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

---

# 追加记录：登记 `.qoder/repowiki/` 详细 Wiki 内容库

日期：2026-08-12
范围：只改文档，不改业务代码。

## 背景

仓库 `.qoder/repowiki/` 已生成详细的 Wiki 内容库（约 290 个文件）：`knowledge/zh/` 模块级知识卡（`_index.yaml` 为模块索引）与 `zh/content/` 主题级详解（架构、模式、Boss、装备、NPC、调试等），此前未在任何协作文档中登记，导致代码变更不会同步维护该目录。

## 本次变更

- `.qoder/repowiki/README.md`：新增面向维护者的知识库导航、检索方式、文档边界和同步清单。
- `AGENTS.md`：阅读顺序、子系统地图、Golden Rule 4.13（代码变更必须同步 repowiki）、验证要求第 3 步（repowiki 同步检查）、第 11 节区分 `.qoder/` 工具材料与 repowiki、最后更新日期。
- `docs/项目全景文档.md`：目录结构总览、补充说明、第 11.4 节文档索引。
- `README.md` / `README_EN.md`：开发向说明指向 repowiki。

## 兼容性

本节记录的知识库登记为 `SAFE`；同一提交中的 Mode G 为 `COMPAT`（新增玩法、物品、持久化 key 与共享层接入）。

## 验证

以最终提交前实际执行的 Windows 编译、Python guard 和知识库静态检查为准。

---

## 2026-09-02 F3 复测暴露的文档与实际代码冲突

兼容分类：SAFE（事实校正）；对应生产修复为 COMPAT，明细见 FIX_TRACKER。

- Mode H 历史设计提案 §17.2 要求原 preset.canDieIfNotRaidMap=true，但实际 SpawnBridge
  早已在独立 clone 打开该字段。官方 Health 只在非 Raid 图据此保护角色，静态按原值拒绝会
  把全部 12 个候选挡在已实现的生成逻辑之外。当前约束以生成后的 Health 和真实死亡事件为准，
  其他静态资格、最低候选数和签名门保留；历史提案不再作为此字段的执行事实。
- 根 AGENTS 2026-09-01 的 BGM DTO/CS0649 记录属于当时版本。本次非空部署表实机读成空数组，
  现改为 BossBgmTrackTable 显式 token 解析；该文件字段有实际赋值，原四个 JsonUtility DTO 的
  局部 pragma 不再适用。一般 JsonUtility 字段契约仍不改变，未批量改造其他 DTO。
- 旧注释将 SetHealth(0) 或 F10 称为 Health.Kill 不准确。官方 SetHealth 只写生命值；
  标准验收和 Mode H 死亡认证必须走 Hurt 并观察 IsDead/实际事件。


## 2026-09-04 深度复审更正（SAFE / OPERATIONAL）

旧龙皇掉落顺序 guard 只考虑等待宝箱的标准分支，漏掉无间炼狱同步消费；当前按 CR-2026-09-04-031 改为 pending 生产者先注册。源码 CI 按用户本轮“全部修复”授权明确外部制品 PARTIAL，发布完整验收不降级。info.ini/捏脸制作本地资料仍 local-only，不强制纳管。

## 2026-09-07 近两周审核校正（SAFE）

- `docs/contracts.md` 仍写 Mode H 默认 false、读取 ModConfig 镜像；实际 `Config/Config.cs` 默认 true，
  `ConfigContentSystemSwitches.ForceContentSystemSwitchesOn` 按已有 owner 决策强制内容开启，并撤下旧开关 UI。
  本轮只修正第 2 / 6.1 节文字，不改变产品开关或配置数据。
- `CODE_REVIEW_FINDINGS.md` 的 2026-09-06“全量深度审查”007–016 已被后续提交修复，顶表仍写 Open。
  本轮按生产代码、执行夹具与 Wiki 导航检查回填 Fixed，保留原审查证据及当时验证数字。
  同日套装批次另有 007–010 重号；引用必须带批次名，不擅自改写历史 ID。
- repowiki 的 F3 文档曾写“报告状态四分”却列五项，且“p95 只警告”与 `SamplePerformance` 的阈值判红冲突。
  本轮按当前实现校正，并新增 9 波 / 6 场流程、会话清理及日志判定边界。2026-09-02 的 138 PASS 保留为旧 DLL 证据。


## 2026-09-08 天空岛制作方案引用口径（SAFE）

新增[天空岛大地图制作教程](制作教程/天空岛大地图_场景设计与制作教程.md)时复核了已有地图和 NPC 资料。以下只记录旧文本与当前源码/守卫的口径差异，不修改运行时代码、数据表或既有守卫：

- [Hooks 分层约定](架构说明/Hooks分层约定.md)末尾 FAQ 将模块 Hook 描述为独立 `partial class ModBehaviour`，与根 `AGENTS.md` 第 4.15 节的新子系统状态归属要求冲突。天空岛按独立 RuntimeModule/Session 设计，宿主仅保留必要分发；旧示例不作为新增状态型 partial 的依据。
- [捏脸 NPC 工具](制作教程/捏脸NPC工具.md)前部收益表仍列 `AICharacterController.MoveToPos()`；其后续章节及当前 [DuckNpcMovement](../Integration/NPCs/DuckNpc/DuckNpcMovement.cs)实际使用 `AI_PathControl + Seeker`，不引入战斗行为树。天空岛区分外观/交互实例与带官方 AI 的战斗实例，不把修改阵营当作敌人制作已经完成。
- [捏脸 NPC 使用手册](制作教程/捏脸NPC使用手册.md)通用字段表允许 `scenes` 留空，仅适用于相应显式召唤场景；当前 [DuckNpcInvariantGuard](../tests/DuckNpcInvariantGuard.py)对永久 NPC 要求非空且已认可的场景名。天空岛永久蓝图须等真实场景接入及守卫同步后登记；不通过虚构 SpawnPoints 文件或放宽守卫绕过可达性要求。

Unity 作者工程 manifest 的 URP `17.0.3` 与本机缓存 `14.0.12` 的差异在既有可行性评估中已有记录，本次仍可见。新教程将实际编辑器解析版本/游戏材质兼容核对列为制作前置项，未据此宣称项目故障或执行升级。天空岛本身是未实装设计，不更新玩家 Wiki 或把拟建模块写成 repowiki 的当前实现。

## 2026-09-08 新场景从零制作教程（SAFE）

用户要求把从 Blender 新建模型、Unity 新建 Scene/打包，到 Mod 代码和测试的流程完整写入 docs。新增 `制作教程/从零搭建自定义场景_Blender到Unity到Mod完整教程.md`，包含手工建模和已验证石堡重建两条路线、两个完整编辑器工具示例、运行时源码职责、地图/光照/A*、部署与验收。两张自有预览图保存在 `制作教程/images/`，明确区分作者模型、地图底图和实机画面。

统一 docs/README 和三份旧场景文档的入口；原评估中“尚未进入”的描述明确限定为历史调查，石堡试用按后续成功日志更新。用户对地图版本的正向反馈只记为反馈，不扩大成完整用例验收。两份 repowiki 同步教程入口，不改变运行代码或正式地图身份。

验证：五份文档 76 个本地链接/图片存在；教程 25 个围栏代码块配对，8 段 PowerShell 经原生 Parser 检查，Python 片段语法检查通过；两个完整 Unity Editor 示例以本机 Unity 2022.3.62f3 程序集独立编译通过（C# 7.3）。验证记录位于 `Build/scene-tutorial-validation/`。未重新执行会覆盖作者成果的 Blender 生成、Unity 场景重建、Mod 部署或游戏操作。docs 及图片仍遵守 local-only，不强制 git add。