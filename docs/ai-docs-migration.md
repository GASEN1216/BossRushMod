# AI 协作文档收敛迁移记录

## 2026-09-22 官方任务授权范围扩到鸭王征程（SAFE）

根 `AGENTS.md` §4.14 与 §10 的 `Duckov.Quests` 授权文字改为「天空岛跨局主线 + 鸭王征程六章（590101–590106，给予者官方 Jeff）」，任务表按子系统各一份、投影核心只有 `Utilities/OfficialQuests/` 一份；`Utilities/AGENTS.md` 加 `OfficialQuests/` 职责边界；`docs/contracts.md` §7.1 加征程一行与 ID 保留段、§3.2 加 `chapterId` / `clueId` 冻结说明与基地侧目标口径；教程 `docs/制作教程/官方任务系统接入教程.md` 加 §12a「多客户端：共享投影核心」。

## 2026-09-20 头盔佩戴与装备尺寸口径统一（SAFE / OPERATIONAL）

按实际官方挂载源码、两龙参考、源网格及 owner 雷霆试戴反馈，修正旧教程的底部原点、统一负 Y 偏移、bounds 约 0.8、根节点全部重置等错误。补充源网格轴向与盔壳中心的区别，Blender 标准化和 Editor 佩戴变换分开；唯一校准表为 `tools/helmet_fit_profiles.json`。同时纠正制作教程建议 600xxx ID 和“仅放包无需内容接线”的旧说法。更新两篇头盔护甲教程、Tripo 教程、模型绑定知识库、根/Integration AGENTS 与 contracts，统一入口为 `docs/制作教程/头盔佩戴与装备尺寸校准.md`。关键约束由入库规则和 HelmetFit 两项检查承载，不只留在 local-only 文档；第一阶段文档保留历史并注明 owner 确认及第二轮入口。


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

## 2026-09-14 AI 协作文档体系整理（SAFE）

起因：owner 要求全面检查文档里「不新增内容、不加 TypeID、不改存档 schema、不重打包」这类约束是否属于模型能力较弱时期的过时限制，检查整套文档是否适配当前开发，并参考高星项目的 agent 规范全面更新。

盘点结论：

- 仓库里没有字面上的「不新增内容 / 不加 TypeID」长期禁令。这类说法来自三处：各轮交付记录（`FIX_TRACKER.md`、repowiki 天空岛文档的带日期小节、代码注释）对**那一轮**范围的描述，用 `rg` 单独搜到时像长期规则；`docs/天空岛全面优化提示词.md` 的「优先不新增 TypeID」；根 `AGENTS.md` §7 / §10 偏保守的通用条款（「不把产品 / 数值决策擅自定案」，以及与本项目无关的「计费 / 支付」「认证 / 权限模型」）。
- 根 `AGENTS.md` 796 行，其中约 470 行是 §14 的按日期变更记录；编译调用还写 `cmd /c`（沙箱下失败），验证步骤还写 `python3 tests/*.py` 循环（与 `tests/AGENTS.md` 矛盾）；Claude 会话里 `CLAUDE.md` 只是文字转发，规则并不会自动进上下文。
- `tests/README.md` 运行方式过时；`skills/` 下 8 个 bossrush 技能大多过时且没有工具加载，丧尸编排技能里有「每阶段停下来问」「未经批准不编译」这类闸门；`.cunzhi-memory/preferences.md` 的「不要生成测试脚本」与现行做法冲突；`docs/项目全景文档.md` 停在 08-27；`wiki-site/AGENTS.md` 有 5 处与代码不符。
- repowiki 319 个文件中 231 个自 08-13 导入后未动，§4.13「每次变更必须同步、过时即未完成」无法做到也无法校验。

处理：

- 根 `AGENTS.md` 重写：新增 §2 常用命令、§4.16 新增内容原则（新内容 / TypeID / `SCHEMA+` / 重打包是正常开发手段，要求接得上）、§7 决策权三档、§8 证据分级 L1–L3；§10 按本项目实际改写；§14 变更记录迁出（原文 `git show 00c8624:AGENTS.md`）。§4.1–§4.15、§5、§6、§10 编号不变——守卫与代码注释按编号引用，§4.1 / §4.3 的文本被 `OfficialCompileListFileExistenceGuard`、`TypeIdLedgerGuard`、`SkyIslandFieldcraftGuard`、`PetNestEggItemRegistryGuard` 解析。
- 经验按主题归位：天空岛 → 新建 `DebugAndTools/SkyIsland/AGENTS.md`；官方 API 静默失败类陷阱 → `docs/contracts.md` §7.1；守卫、反向验证与夹具纪律 → `tests/AGENTS.md`；物品接线与可达性 → `Integration/AGENTS.md`。
- 其余更新：各子系统 `AGENTS.md`、`CODE_REVIEW.md`、`CLAUDE.md`（改为 `@AGENTS.md` 导入）、`README.md` / `README_EN.md`、`tests/README.md`、`docs/README.md`、`docs/AGENTS.md`、两份长任务提示词、`.qoder/repowiki/README.md` 与 `_index.yaml`、遗种巢 repowiki 文档的自相矛盾处、`.github/workflows/guards.yml` 注释里写死的数字。
- 过时资料只加归档标注、不删除：`docs/项目全景文档.md`、`skills/`（新增 `skills/README.md`，8 个 bossrush 技能加标注）、`codex-skills` 示例补参数；`.cunzhi-memory` 两份按现状改写。
- repowiki：§4.13 改为「改到已有专题文档时更新，代码为准」，快照期旧文档按快照对待。

未做：没有编译（纯文档）；没有改代码注释里的「见 AGENTS §14」（原文仍可由上面的提交取到）；没有删除任何 local-only 文件；没有提交。

仍需 owner 决定：

1. `skills/`、`codex-skills/` 是删除、移出仓库目录，还是保留归档。
2. `docs/架构说明/` 是否纳入 git（根规则引用了它，fresh clone 看不到）。
3. 根目录 2026-09-13 的五份 WSL 审查报告（未入库、内容不可靠）是否删除。

## 2026-09-14 全自动实机验收：F3 第三档（SAFE / OPERATIONAL）

- 根 `AGENTS.md` §4.17 新增「全自动实机回归是第三档」：入口只有「自动验收 + 完整待测清单」按钮；Dev 构建 + 专用测试档；可以写该槽的天空岛剧情与背包，但必须先快照、写入过 `AutotestWriteAllowed`、两道还原并复位环境；只读套件与演练不得引用；守卫 `F3AutotestOrchestratorGuard`、`SkyIslandAutotestTableGuard`，正式构建后 `tools/check_dll_identifiers.py --expect absent`。
- `DebugAndTools/SkyIsland/AGENTS.md` §5：两个 Dev 入口文件（`SkyIslandSessionAutotest.cs`、`SkyIslandStoryServiceAutotest.cs`）的纪律，以及「改标记、居民站位、采集点、面板与字幕文案时同步步骤表」。
- 天空岛人工清单（local-only）顶部改为「按按钮 + AI 审阅」，列出仍需人手的 6 行与两处照原步骤手测的范围；原步骤保留作判据出处。
- repowiki「调试工具」新增「全自动实机验收」一节。
- 冲突记录：旧口径「天空岛是独立出击，只能岛内按钮跑、不能挂在自动验收上」（F3 页面文案、Runner 注释）已按新实现改写——自动验收会自己出发、跑完回基地；两个岛内按钮保留为局部重跑。

## 2026-09-15 F3 截图由 owner 看，AI 不读（SAFE / OPERATIONAL）

- 起因：owner 指出读截图很耗 token，要求「非必要不看截图」；随后进一步明确：「每次如果要跑 F3 的话，就告诉我要我看截图的哪些地方，然后我看完后把结果给你就行了，不用你读截图除非我要求」。
- 根 `AGENTS.md` §4.17 在「全自动实机回归是第三档」后面新增一条「截图由 owner 看，AI 不读」，共三点：
  - AI 审阅只读文字：`review.md`、`review.json`、`summary.md`、`manifest.json` 里的断言与 metrics，以及 `Player.log` 里的 `[AUTOTEST]` 行。`shots/`、`thumbs/`、`sheets/` 里的图，owner 没有明确要求就不读。
  - 每次请 owner 跑 F3 时附一份看图清单，写明步骤 id、文件名、看画面哪里、什么算不合格。owner 看完回报，证据来源记「owner 目检」。
  - 同一类视觉项反复需要人看时，改成进程内像素探针。
- 冲突记录：上一节写的「按按钮 + AI 审阅」，在截图这一环改为 owner 目检；文字部分仍由 AI 审阅。`tools/autotest_review.py` 照常生成拼图，但拼图是给 owner 看的。
- 未做：没有改代码与工具。

## 2026-09-15 天空岛头目 / 岛主 R2–R4（SAFE；代码本体见 FIX_TRACKER）

- 根 `AGENTS.md` §4.3：「5000xx 区间」改成「500xxx 区间」（TypeID 已越过 500099），登记范围 `500001-500102`、下一可用 `500103`；`docs/contracts.md` §1 同步。`TypeIdLedgerGuard` 的扫描正则同步放宽，否则 500100 起的号整段漏扫。
- `DebugAndTools/SkyIsland/AGENTS.md` §3 头目组补两条（夜限定在遭遇层等夜、不在 Forge 跳过；换阵营写在 wolf 安全网之后），§4 补两条（招式控制器走 `SkyIslandBossProps` 共用件：换位顺序、倒影不克隆角色、面罩 / 耳机由控制器照头盔口径磨；主角穿戴只在 `SkyIslandFieldcraftBossGear` 读、其余读快照）。
- repowiki「天空岛剧情与持久化」新增 R2–R4 一节，「调试工具」补 `approach_boss` / `feed_shots` 两个动词。
- 冲突记录：计划稿里的 `WakeGroup`、镜池对称、悬根猎装「减速减半」、苔纱面罩「不累积瘙痒」与最终代码不一致，文档一律按代码写（`CallGroup`、以玩家为轴翻身、翻箱特产翻倍、云蚋不躲枪口）；官方 `Health.Hurt` 是暴击磨头盔、非暴击磨身甲，不是「只在暴击时磨头盔与身甲」。
- 天空岛人工清单：2.20 已被剧情面板一节占用，R2–R4 用 2.21–2.23；`SkyIslandAutotestTableGuard` 的清单区间暂不含 2.20（那一节还没进覆盖表）。

## 2026-09-16 Jeff 官方任务接入规则校正与教程（SAFE）

- 起因：owner 明确要求复用官方任务系统，把天空岛序章接到官方 NPC Jeff；生产代码已注册运行时 `Quest` / `Task`，官方任务保存快照会过滤本 Mod ID，进度权威仍是 Mod 分槽故事。
- 冲突：根 `AGENTS.md` §4.14 仍写「官方任务系统刻意不接」，§10 只登记了 `NoteIndex` 例外，已经与当前代码、`docs/contracts.md` 和 owner 的 2026-09-16 授权冲突。
- 处理：§4.14 改为“跨局、一次性持久剧情可在明确授权后接入，Jeff 序章为已授权实例；按出击刷新的岛内委托不接”；§10 登记 Jeff 例外的保存过滤边界，明确不自动授权其它新任务。
- 新增 `制作教程/官方任务系统接入教程.md`，记录官方调用链、运行时 prefab、Jeff 绑定、Mod 权威投影、四类保存快照过滤、ID 所有权保护、生命周期、性能与 L1–L3 验证方法；`docs/README.md` 增加入口。
- 本轮只改文档，没有启动游戏、部署或读写玩家存档。

## 2026-09-16 岛上主线接官方任务、昼夜对齐官方（COMPAT / SCHEMA+）

- 起因：owner 审核天空岛后拍板两件事：昼夜边界对齐官方 `TimeOfDayController`（19–5）；Jeff 只留序章，岛上主线三段挂到岛上自己的 NPC 名下走官方任务。
- 冲突：`DebugAndTools/SkyIsland/AGENTS.md` §4 仍写「官方任务系统刻意不接」（上一轮只改了根 AGENTS）、「夜里约 8 分钟」；repowiki「天空岛剧情与持久化」仍有「刻意不接」一节；教程示例标识符与实现对不上。
- 处理：根 §4.14 / §10 授权范围改为「天空岛跨局主线（590001 + 590011–590013，自定义给予者 5901–5903）」；子 AGENTS §4 两条改口径；contracts Quest 段改成任务表 + 副作用、§7.1 补判夜口径；教程补多任务注册表、自定义给予者一节并对齐标识符；repowiki 该节重写；Wiki 两语补岛上三条任务与 19–5。

## 2026-09-18 Mode H 按生产代码校正

`docs/contracts.md` §6.1 的“一次性完整实现/快照续战”超出了当前代码事实，改为已接线与同场看盘
重开，明确 L1/L2 不等于实机验收。补充 owner 本轮授权的擂台与伤势规则、实例事件生命周期和
公开赔率边界；同步 Mode H 专题、中英文 Wiki 与夹具边界。不改变存档 schema、key 或既有授权范围。


## 2026-09-19 实测要求同步

按本轮实测清单，第21项恢复鸭生无常默认开启、玩家可关闭，作为恒开策略例外；第23项后山跳过战役调试入口从设置移除，正式构建忽略旧值。已同步仓库/Integration规则、配置读写和守卫。

## 2026-09-19 实测补漏（第二轮）三条长期规则入库

- §4.14 新增「玩家可见文本只能用 GBK 收录的符号」。起因：雷戒的 ⚡ 在游戏里是空白豆腐块，
  复查发现征程 HUD 的 ✓✗、百科项目符号 •◦▪、诅咒词缀里的 U+2212 减号等同类越界字符还有二十多处。
  判据落在 `tests/PlayerFacingGlyphGuard.py`（GBK 可编码性，不靠人肉记字形），日志与只给人读的报告不受限。
- §4.16 新增「贴图导入设置属于交付内容」。起因：第一轮只改了出图脚本的输出上限，
  但真正决定玩家看到什么的是作者工程的 `TextureImporter`；口径落在 `tools/apply_unity_texture_policy.py`
  与 `tests/UnityTextureImportPolicyGuard.py`。
- §4.16 新增「作者工程与 Unity Editor 路径不要写死」。起因：仓库里六处写死 `D:/code/ykf/...`，
  工程搬家后这些「找不到就跳过」的引用让守卫静默变成永远 PASS——
  `SkyIslandMiniMapGuard` 的 Unity 构建器检查从来没真正执行过。统一走 `tools/unity_project_path.py`。


## 2026-09-22 资源守卫清单归位

`tests/AGENTS.md` 不再把依赖 local-only 制品的守卫写死成“三个”，改以 `tools/run_guards.py` 的 `EXTERNAL_ARTIFACT_GUARDS` 为事实源；新增日报实际 Sprite 检查亦在其中。源码 CI 的 PARTIAL 不等于发布资源验证通过。

## 2026-09-22 模块解耦计划补为完整迁移执行书（SAFE / OPERATIONAL）

owner 要求全面审查计划，并使新窗口可一次授权后完成全部迁移。原计划末尾仍仅推荐 0/1A/1B，目标目录是示意；本次将范围改为全仓，补入逐域目标、精确文件/成员台账、M00–M15 连续检查点、工作区快照/合并/续接、完整验收及可复制启动指令。见 [执行计划](设计提案/2026-09-22_BossRushMod模块解耦与上下文治理计划.md) §7–12。

复审核实的重点包括：Host 多阶段 Tick 与 early-return、G 每局核心、Campaign 消费 F 死亡闩、OfficialQuest 初始化顺序、内容分阶段装配、公共 UI 租约、目录空扫假绿、编译清单解析差异、正式/Dev 环境变量和 CI 分级协议。它们是执行计划的约束，不宣称已修生产代码或验证实机。

本次精确放行计划与配套 GitHub 研究记录两份文件，便于后续提交后跨窗口/签出使用；不开放其他 local-only 提案。同步 docs 导航和研究记录的范围说明，保留原历史研究依据。已跑编译清单与 partial 预算两个守卫、文档结构/路径核验；未启动游戏、未访问玩家存档、未编译部署，未提交。新架构目录和工具仍待实施。
