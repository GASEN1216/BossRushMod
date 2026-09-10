# 天空岛剧情与槽位持久化执行回归

分类：COMPAT / SCHEMA+。运行 `python tools/run_runtime_regressions.py --filter SkyIslandStory`。

工程直接链接生产剧情规则、编解码、服务、共享 JSON parser/writer、槽位 store 与保存协调引擎。
SavesSystem、基地状态与每帧写盘节流是内存替身，不加载 Unity 或接触玩家存档。

覆盖东西顺序自由、清场前置、四支线、先发现后交付、折翎互斥结果、两种终章、通关补支线、
重复提交、重入恢复、战斗帧禁落盘、物理写失败后重试、槽位切换、同槽删除、未知 schema、
损坏字段保护与 JSON 转义。它不证明实际敌人死亡、交互触达、门碰撞或官方 ES3 落盘行为。

2026-09-10 可玩性评估追加：岛上落盘去抖（到访 / 清场 / 见闻攒满 `FlushDebounceSeconds` 才写、剧情动作立刻写、
离岛绕闸全部写掉、重入不丢）、三件战斗了结结果的字幕与 `TryApply` 回话同源、`SKY_TIMING` 分段计时行的格式与去重。
`UnityEngine.Time` 替身多了 `realtimeSinceStartup`（计时时钟，测试显式赋值），`Debug` 替身多了记录最后一行的 `Log`。

2026-09-10 内容批次二追加（链接 `SkyIslandLetters` / `SkyIslandPuzzles` / `SkyIslandCrew` / `SkyIslandJournal` / `SkyIslandItemRules`，
以及它们引用的 `SkyIslandLootTables` 与 `Config/ConfigItemIds.cs`）：12 封信鸽来信的 id、区域、前置与「每趟一封、应时的先来」的送达顺序；
四道秘境谜题的每一步、每个错误选项（先提示、再点破、停在原步、之后照样能解开）以及旗标与 `TrySearchAction` → `TryApply` 同一映射；
归航船名册四页随和解 / 战胜分支变化；手记章节恰好覆盖 20 处见闻、未收录只给标题、收齐才出终页、未收到的信不剧透；
纪念品台账（罗盘随第一封信、航徽随结局、噬风之核随噬风，发过不再发）；岛上特产按档次的精确出现率；罗盘八方位与 10 米取整；
英文界面无中文残留；来信 / 名册 / 纪念品经 `RecordNote` 只收登记过的 id、同一条不写两次、离岛落盘后重进仍在。
**直接 `dotnet run` 会在本目录生成 `bin/`、`obj/`，之后聚合执行器会把其中的 AssemblyInfo 再编一遍而报 CS0579**：请只用上面那条命令跑。
