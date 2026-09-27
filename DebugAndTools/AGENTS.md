# DebugAndTools/AGENTS.md — F3 与调试专项规则

> 先读根目录 `AGENTS.md`。以下保留原根规则 §4.17 的完整条文。

### 4.17 F3 验收用例与常驻 HUD

F3 玩法验收只在 Dev 构建里存在（`BOSSRUSH_DEV_BUILD=1`），目标是把实机前能自动看的都收进报告，人工清单只留读报告与手感项。新增用例时：

- **判据与取数分开**：判据写成 `#region 纯判据` 里的静态函数，只吃基本类型与小结构、返回 ok / reason / metrics，由执行回归逐字抽取运行（天空岛是 `tests/fixtures/SkyIslandValidationJudges`）；取数方法只读观测面（天空岛是 `SkyIslandSessionValidation.cs` 的属性）。物理、渲染、Harmony 这类离线造不出来的只做 L1 守卫，不硬凑执行回归。
- **只读套件就是只读**：不写剧情与存档、不注册或写官方图鉴、不刷怪、不改强制夜里、不打补丁（`SkyIslandValidationSuiteGuard` 的禁用清单）。会改状态的检查进 Dev 演练套件：整文件 `#if BOSSRUSH_DEV`、独立按钮、复用专用测试档的开跑门、报告头 `read_only=false`、`finally` 还原、不写存档不收录；只读套件不得引用演练代码（`SkyIslandReadOnlySuiteDrillIsolationGuard`、`SkyIslandDrillNoPersistenceGuard`）。
- **全自动实机回归是第三档**：入口只有「自动验收 + 完整待测清单」这一个按钮（不另加按钮）。主套件回到基地之后，它带着专用测试档经船点去天空岛，按步骤表 `Assets/Data/SkyIslandAutotest.json`（加步骤只改数据）瞬移、交互、刷怪、换语言、截图并做进程内像素检查，结果写进 `BossRushTestReports/<runId>/`（manifest.json、summary.md、shots/），由 AI 用 `tools/autotest_review.py` 读。只在 Dev 构建、只在专用测试档；**可以**写这个槽的天空岛剧情与背包，但必须先取快照，写入前过 `AutotestWriteAllowed`，收尾阶段与 `CompleteSession` 两道还原并复位语言、强制夜里、无敌、血量与时间流速，中途崩溃回基地按快照键恢复。只读套件与演练不得引用它（`F3AutotestOrchestratorGuard`、`SkyIslandAutotestTableGuard`）；换回正式构建后用 `python tools/check_dll_identifiers.py --expect absent` 实查 DLL。
- **截图由 owner 看，AI 不读**（2026-09-15 owner 定：读图很耗 token）：
  - AI 审阅结果时只读文字：`tools/autotest_review.py` 产出的 `review.md` / `review.json`、`summary.md`、`manifest.json` 里的断言与 metrics，以及 `Player.log` 里的 `[AUTOTEST]` 行。`shots/`、`thumbs/`、`sheets/` 里的图，owner 没有明确要求就一张都不读。
  - 每次请 owner 跑 F3 时，同时给一份**看图清单**：步骤 id、截图或拼图的文件名、画面里看哪个位置、什么算不合格。清单按本轮改过的画面来列；跑完后如果有文字判断不了的红项或结果翻转，再补几条。owner 看完回报，结论的证据来源写「owner 目检」。
  - 同一类视觉项反复要人看时，就改成进程内像素探针，把结论变成 manifest 里的数值（例：撤离环按环带覆盖率判定）。
- **SKIP 不能吞缺陷**：先查「缺了会让这条用例永远 SKIP」的前提（资源在不在、补丁装没装、皮肤注入没有），再按场景条件（白天、场上没有目标）记 SKIP。
- **用例 id 写字面量交给外壳**：`RunSyncCase` / `RunSkyIslandSync` / `RunSkyIslandCase` 一类外壳的第一个参数写字面量；新增外壳登记进 `tools/gameplay_coverage.py` 的 `CASE_RUNNERS`，用例登记进 `Assets/Data/GameplayCoverage.json`（`GameplayCoverageCaseRunnerGuard`）。要在基地看的（官方图鉴镜像、出击残留）挂主套件 `RunSuite`，不塞进岛内套件。
- **常驻 HUD**（进局就一直在、不是玩家主动打开的）每帧入口同时经过 `BossRushUI.IsOfficialHudHidden()` 与 `BossRushUI.IsGamePaused()`，并登记进 `tests/PersistentHudVisibilityGuard.py`（它按语义核对判定与宿主驱动，不只查子串）。引用任何 UI 层级常量的文件都要在那里归类（常驻 / 排除 / 模态，写明理由）；画布层级不写字面量；`OnGUI` 同样登记。

