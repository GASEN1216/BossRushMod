# 潮闸镇危险区开箱回归

运行 `python tools/run_runtime_regressions.py --filter TidegateTreasure`；统计输出在 `Build/runtime-regressions/TidegateTreasure.log`。每个点各试 2,048 个相同种子，涵盖 9 个实际点位，包括普通对照、泥滩、沉船、手轮柜、水厂控制室、普通金库、所长和船长保险柜。

`UnderwaterAudit` 另对 2,048 趟完整水下搜刮执行真实建箱与开箱流程。按用户确认，随机奖励中品质 `Quality >= 5` 的物品整趟 80% 恰好一件、20% 零件；概率每趟独立，不保证任意连续十趟恰好八趟中奖。固定剧情怀表仍在所长柜必给（已携带时沿用原规则），明确豁免该随机奖励预算。普通水下物资为 2–4 档、精品为 4 档，中奖实物为 5–6 档。只按数值品质统计；显示光晕不参与预算，也不改写物品品质或颜色。

完整作者点位摘录在 `HarborLayout.json`：水下范围由实际高度独立核对为 15 点，含 6 个埋货、4 个金库、4 个沉船货箱和船长保险柜；水厂控制室与船舱手轮柜属于陆地。抽样核对每趟高级物品总件数、容量上限、价值上限、黑名单与怀表例外，并在 64 组种子中反序创建/开箱、增删货单 POI、切换货单赢家，确保高级奖励计划不变。陆地完整物品指纹与本轮修改前生产源码的同种子结果对照；开箱复开不增物资。另直接执行 10,000 次生产抽签计划，核对 80/20 频率与所有 15 个候选点均可中选。空高级目录必须报告缺货；低档物品的显示颜色变化不能把它误算为高级奖励。

2026-11-08 补上实机误报复现：日志 `20261107_170000_978` 的种子 `-1327683468` 抽中零奖，但埋货 `LOOT_buried_50` 含 `135/125/111/500114` 四件物品，鸭蹼被旧判据的显示颜色分支错计。夹具现在直接编译生产 `RelicEggConfig` 和 `TidegateItems` 的真实克隆注册、配置代码：从部署包数值快照 `FallbackTemplate.json` 的 `500058:Q4/DQ6` 经遗种蛋 `500059:Q5/DQ6` 得到鸭蹼 `500114:Q4/DQ6`。宿主只替代 Unity 克隆、图标、使用组件与动态目录，不再给自定义物品伪造白色光晕。逐件预算核对覆盖所有箱内物品，只有怀表豁免；此前只统计官方物品会漏掉鸭蹼。记录中的四件实物回放和整个失效种子的建箱流程均执行；外部 Mod 的额外目录不在本夹具内，因此不声称所有普通随机槽与实机逐件相同。

`HarborShiftAudit` 还在每组 256 个相同种子中搜完全部 78 个作者箱子；统计普通区单格率、真实成套物品和全图总标价，检查普通点每趟最多一件惊喜。该统计刻意不放运单、不计剧情物/敌人掉落。箱种清单按布局 ID 顺序保存于 `HarborBoxTypes.txt`；锁柜先搜/后搜、重开和重入由 `TidegateCargo` 验证。

直接编译生产 `TidegateLoot` 完整建箱/开箱/退订流程、`SkyIslandLootPools` 的实际分类及候选过滤、`SkyIslandLootTables` 的品质加权和随机流、`LootExcludeTagPolicy` 及 TypeID 范围规则。固定剧情物不计入标价，但核对手轮与怀表确实装入；每只箱子开两次，不能增加物资或多计探索。危险区仍有可售的 4 档精品，数值不再要求所长柜、船长柜每次给 6 档；普通陆地点不能变精品点。

Unity 场景、箱子容量、商店与玩家均为宿主替身。箱价对比把各点置于干燥近地，不注入装货 POI，避免运货改变箱价。独立的 48 局货单验证使用真实布局摘录：开第一只普通箱后，必须出现两处不同的真实货区，三种组合均可出现；通过控制最后一次抽样，逐项枚举生产挑选器接受的医疗、检修、贵重货。货单胜出、备选箱回填与失败路径由 `TidegateCargo` 单独执行。替身按官方 `ItemFilter` 处理标签及品质查询，使用生产黑名单 JSON；不加载其它 Mod 的动态物品，不代表 Unity 帧耗或游戏内实际装箱一定成功。

`catalog.txt` 是 2026-11-07 本机 Steam 游戏 `resources.assets` 的静态目录快照，仅保存数值和标签，不含图片、文案或游戏资源。字段为 `TypeID|quality|value|maxStackCount|tags|displayQuality`，取自 `ItemAssetsCollection.Entry.metaData`，价格是其中的 `priceEach`（官方从 `Item.Value` 拷贝）。本轮额外反查全部 1,569 个真实 Item prefab，`Quality` 与 `DisplayQuality` 均与元数据一致；134 个标签来自 `GameplayDataSettings.TagsData`。安装版 getter 与物品格 UI 已核实显示品质与数值品质独立，不能把 Q5 或 Q6 简写成原生红光晕。来源文件 SHA-256 记在快照头。要更新快照可执行：

```powershell
python tests/fixtures/TidegateTreasure/extract_catalog.py "<Duckov_Data>/resources.assets" tests/fixtures/TidegateTreasure/catalog.txt
```

提取器依赖 Python 3.11+、UnityPy；运行日常回归只需 .NET 与 Python。箱价是官方物品的基础标价 × 实收堆叠数量（按最大堆叠数截断），不含枪械附属件、耐久、任务物或装货；商店按各自收购系数与单品覆写换算，不能用这里的数字计算可兑现收益。

复测旧版本：将 `TidegateSnapshot` 指到含 `TidegateLoot.cs`、`SkyIslandLootPools.cs`、`SkyIslandLootTables.cs` 的旧源码目录，仅在测旧版本时设 `TIDEGATE_MEASURE_BASELINE=1` 跳过新增品质断言，然后运行同一聚合命令。普通点的 `hash` 是所有种子的物品 ID 和件数序列摘要，可以检验普通箱是否一件不差；不是哈希程序或随机函数的自测。
