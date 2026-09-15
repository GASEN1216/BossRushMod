# 全自动实机验收：离线判据执行回归

分类：SAFE（测试）。运行 `python tools/run_runtime_regressions.py --filter F3AutotestJudges`（或 `python tests/fixtures/F3AutotestJudges/run.py`）。
**不要在本目录直接 `dotnet run`**：会留下 `bin/`、`obj/`，之后聚合执行器报 CS0579。

## 编的是什么

`run.py` 把两份整份 `#if BOSSRUSH_DEV` 的生产文件去掉首尾包裹（行号不变）写进 `Build/runtime-regressions/F3AutotestJudges/gen/`：

- `DebugAndTools/F3GameplayValidationAutotestJudges.cs`：步骤表模型与校验、剧情阶段推进、快照编码、线性对比度 / 可见度 / 溢出判据、结果与报告；
- `DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs`：剧情存档门面的 Dev 入口（与链接的生产 `SkyIslandStoryService` 同一个 partial）。

工程**不定义** BOSSRUSH_DEV（别的生产文件里的 Dev 区块引用 Unity）。Judges 剥掉注释与字符串字面量之后若出现 `UnityEngine`、`Mathf.`、`GameObject`、`Transform`、`Texture2D`，`run.py` 当场失败；
扫描器自带小自检，防止清洗把代码一起抹掉而恒绿。

- **真实生产逻辑**：上面两份、剧情规则 / 编解码 / 灯 / 信、共享 JSON 解析器与写出器、`BossRushSlotJsonStore`、`BossRushSaveCoordinatorEngine`、`SkyIslandStoryService`；
  数据读真实的 `Assets/Data/SkyIslandAutotest.json`、`Assets/Data/GameplayCoverage.json` 与 `F3GameplayValidationAutotestAsserts.cs` 的 case 字面量。
- **宿主替身**：`SavesSystem`、`Time`、`L10n` 等沿用 `tests/fixtures/SkyIslandStory/Stubs.cs`；写入门 `F3GameplayValidationRunner.AutotestWriteAllowed`
  在本目录 `AutotestGateStub.cs`，只是一个默认关闭的静态开关（真实判定读 Runner 单例、专用测试档标记与当前槽位，离线造不出来）。

## 覆盖（每节都有绿样本与红样本；断言失败不中断，全部跑完再汇总）

1. **步骤表**：真实表解析不丢行；`ValidateTable` 以 null 已知集合、以「已知用例 = coverage automatic ∪ AutotestCaseDelegate」+「已知离线证据 = 表里真实存在的路径」、再加 checklist = 覆盖行编号都通过；
   每条 `offline:` 路径在仓库里存在。红样本：未知动词、未知断言名、`assert:case:` 未知 id / 缺 id、只剩瞬移与等待的步骤（另有只含 `use_buff:Charm` 的绿样本）、
   shot 步骤没截图、步骤与覆盖行清单编号格式错、knownChecklist 下的未知编号、覆盖行重复、manual 理由不含手感 / 声音 / 好不好玩、offline 证据挂在 shot 行、
   shot 行的证据步骤不截图、第一个 story 阶段不以 reset 开头、未登记的灯、不存在的剧情动作、预算 601 秒、步骤 id 不以 SKY_AUTO_ 开头、未知离线路径、表版本 2、缺 coverage、截断 JSON。
2. **剧情阶段**：`SimulateStages` 从空档推完六个 story 阶段；双航标两处旗标与四组清场、噬风、钟守、结局、七盏灯（按 `SkyIslandLights` 点亮十盏）逐项抽查；
   每阶段编解码往返后 `JudgeStageData` 双向通过，且后一阶段包含前一阶段。红样本：敲钟挪进新档阶段、reset 前放操作、少一个旗标 / 一条清场 / 一条手记、缺数据、结局无航标被编解码拒绝。
3. **快照**：`EncodeSnapshot`→`DecodeSnapshot` 字段逐个相等（raw 为 null、含中文引号换行反斜杠、钱超 int / long.MaxValue、小数与极小 timeScale、多条物品与图鉴键）；
   版本 2 / 缺版本 / 字符串版本 / 截断 / 乱码 / 空 / null 返回 null；`SnapshotStory` 的默认档、正常原文、坏原文、raw 为 null、编解码拒绝的原文。
   **替身存档上的真实门面**：生产入口写一份非默认剧情并落盘 → 按 `SkyIslandStoryRules.StorageKey` 读原文做快照 → 门关着 `DevAutotestReplace` / `DevAutotestFlush` 拒绝且原文逐字不变、不写盘 →
   门开着先不落盘、关门挡住 flush、开门 flush，存档里是阶段数据 → 编解码拒绝与 null 的替换被拒 → 用快照还原并 flush，存档与快照前 `JudgeStageData` 双向一致且逐字相同，新会话读回一致。
   另测写屏障（坏原文不被覆盖）、槽位变了拒写且新旧槽都不出现剧情键、原文不存在的快照还原成默认剧情。
4. **线性对比度**：`SrgbToLinear` 0 / 1 / 0.5≈0.2140、线性段、钳位；亮度权重；白对黑 21:1；`CompositeLuminance` 在线性光里合成（50% 白压黑 Y=0.5，与 sRGB 空间混合的 0.214 不同）、背景不二次反解、alpha 钳位；
   `JudgeTextContrast` 亮字压暗底 PASS（取离底色最远的一截而非抗锯齿均值、外圈取中位数、至少 4 个）、恰好等于下限 PASS、灰字压灰底 FAIL、样本不足 SKIP。
5. **可见度与溢出**：`JudgeWorldVisibility` 亮物体 PASS 且不被大投影框稀释、暗物体压亮邻域 PASS、同亮 FAIL、略亮 FAIL 且 Weber 值低、样本不足 SKIP；`JudgeTextOverflow` 的 SKIP / FAIL / 截断只列不判红。
   `JudgeRingCoverage`（地面圆环沿环带的覆盖率）：整圈画出来覆盖率 1、被台面盖住为 0、碎成虚线按比例、日照石面比邻域亮但不朝环色偏记 0（旧亮度口径会过）、偏移不足 min_shift 不算、环色与邻域同色不算、屏幕上不足 16 段 SKIP。
6. **结果与报告**：`AggregateResult`、`ChooseEncoding` 六成 / 九成 / 满额两侧分档、`LogLine`、`JudgeOfficialNotesAfterAutotest`；`RenderManifest` 用 `BossRushJsonParser` 解析回来核对 schema、头部、restore / shots 块、
   每步字段与断言截图、每行覆盖的状态（PASS / FAIL / MANUAL / NOT_RUN / SKIP）；`RenderSummary` 还原失败或 PENDING 时红色提示在标题之前、成功时没有、环境还原失败给警告、红项与覆盖表；
   `RestoreNeedsAttention`；`RunStatus` 的还原失败 / 取消 / 中止 / 有红 / 不完整 / 全绿优先级。

## 证明不了什么

Unity 侧那一半只能在 Dev 构建里实机跑：截图取像素（亮度样本从哪来、元素矩形算得对不对）、场景里的物体与灯、官方对话 / 地图 / 图鉴接口、背包与金钱记账、
写入门的真实判定（专用测试档、Runner 状态）、真实 ES3 落盘与崩溃后回基地的恢复时序。夹具只证明「拿到这些输入之后」判据、编码与报告的行为。
