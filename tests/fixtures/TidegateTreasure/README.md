# 潮闸镇危险区开箱回归

运行 `python tools/run_runtime_regressions.py --filter TidegateTreasure`；每个点各试 2,048 个相同种子，统计输出在 `Build/runtime-regressions/TidegateTreasure.log`。统计范围是 9 个实际点位，包括 2 个普通对照、泥滩、沉船、手轮柜、水厂控制室、普通金库以及所长与船长保险柜。

直接编译生产 `TidegateLoot` 完整建箱/开箱/退订流程、`SkyIslandLootPools` 的实际分类及候选过滤、`SkyIslandLootTables` 的品质加权和随机流、`LootExcludeTagPolicy` 及 TypeID 范围规则。固定剧情物不计入标价，但核对手轮与怀表确实装入；每只箱子开两次，不能增加物资或多计探索。危险区需要命中对应品质底线并能出售；两个高级柜跨局要有不同实物，最高品质不能每局必出。普通点不能变精品点。

Unity 场景、箱子容量、商店与玩家均为宿主替身。箱价对比把各点置于干燥近地，不注入装货 POI，避免运货改变箱价。独立的 48 局货单验证使用真实布局摘录：开第一只普通箱后，必须出现两处不同的真实货区，三种组合均可出现；通过控制最后一次抽样，逐项枚举生产挑选器接受的医疗、检修、贵重货。货单胜出、备选箱回填与失败路径由 `TidegateCargo` 单独执行。替身按官方 `ItemFilter` 处理标签及品质查询，使用生产黑名单 JSON；不加载其它 Mod 的动态物品，不代表 Unity 帧耗或游戏内实际装箱一定成功。

`catalog.txt` 是 2026-11-06 本机 Steam 游戏 `resources.assets` 的静态目录快照，仅保存数值和标签，不含图片、文案或游戏资源。取自 `ItemAssetsCollection.Entry.metaData`，价格是其中的 `priceEach`（官方从 `Item.Value` 拷贝）；已核对 1,569 个唯一 ID 的 prefab 引用都有效、134 个标签来自 `GameplayDataSettings.TagsData`。来源文件 SHA-256 记在快照头。要更新快照可执行：

```powershell
python tests/fixtures/TidegateTreasure/extract_catalog.py "<Duckov_Data>/resources.assets" tests/fixtures/TidegateTreasure/catalog.txt
```

提取器依赖 Python 3.11+、UnityPy；运行日常回归只需 .NET 与 Python。箱价是官方物品的基础标价 × 实收堆叠数量（按最大堆叠数截断），不含枪械附属件、耐久、任务物或装货；商店按各自收购系数与单品覆写换算，不能用这里的数字计算可兑现收益。

复测旧版本：将 `TidegateSnapshot` 指到含 `TidegateLoot.cs`、`SkyIslandLootPools.cs`、`SkyIslandLootTables.cs` 的旧源码目录，仅在测旧版本时设 `TIDEGATE_MEASURE_BASELINE=1` 跳过新增品质断言，然后运行同一聚合命令。普通点的 `hash` 是所有种子的物品 ID 和件数序列摘要，可以检验普通箱是否一件不差；不是哈希程序或随机函数的自测。
