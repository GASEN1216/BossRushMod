# 收集品执行回归

入口：`python tools/run_runtime_regressions.py --filter CollectibleLoot`。

**真实生产逻辑**（直接编译，不复制）：
- `Integration/Collectibles/CollectibleCatalog.cs`：物品表；
- `CollectibleTuning.cs`：档次权重、官方箱子名键分档、场景到系列、集齐奖励、送礼偏好 `LikedGiftAffinity`；
- `CollectibleLootRoller.cs`：抽签与本趟账本 `CollectibleRaidLedger`；
- `CollectionBookPersistence.cs`：存档编解码与登记规划 `CollectionRegistration.Plan`；
- `Localization/CollectiblesLocalization.cs`：中英文案；
- `Config/ConfigItemIds.cs`；
- `Common/Data/BossRushJsonValue.cs` 与 `Utilities/SimpleJsonHelper.cs`。

**宿主替身**（`Stubs.cs`）：`L10n`、`ModBehaviour.DevLog`、`LocalizationHelper`，以及存档管线的 `BossRushSlotJsonStore` / `BossRushSaveCoordinatorEngine`。替身不读写真实存档，只把 `Encode` 的结果记下来。

覆盖内容：
- **物品表**：
  - 100 件，TypeID 从 500119 连续；
  - 各档件数 24/22/18/14/10/6/5/1，系列件数 9×8、2×11、6；
  - 7、8 档只在珍品组；
  - 价值落在官方同档收藏品 / 奢侈品区间里，8 档高于皇冠的 21,593,218；
  - 每件都有中英名称和描述。
- **抽签**：每档各抽 400 万到 800 万次，固定种子。
  - 普通箱出现率约 4%，高价值箱约 12%；
  - 各档份额对上权重表；
  - 8 档只出在高价值箱，每箱约 0.006%；7 档高价值箱每箱约 0.03%、普通箱约 0.006%；
  - 本地系列只在本图出，和全图系列同档都有候选时按 6 : 4 抽；
  - 本图没有本地系列时只出全图件；
  - 潮闸镇水下上限 4 档、天空岛上限 6 档，只压品质、不降出现率。
- **本趟账本**：已搜过的箱子不判，同一只箱子只判一次，每趟最多 3 件，换一趟清零。
- **登记规划**：
  - 只收没登记过的，同一次放两件同样的只收一件；
  - 规划不改动当前事实；
  - 系列奖金只发一次；
  - 11 个系列全齐时连同各系列奖金和全集大奖一起发一次；
  - 珍品可登记但不发钱。
- **存档**：
  - 编解码往返一致；
  - 越界、重复、未知系列、未知 schema 都整份拒收；
  - 缺键按「什么都没发生」读；
  - 接过引导即解锁售货机的收藏册。
- **送礼**：
  - 叮当、闸伯按 LikedBy 喜欢对应的收集品，好感随品质递增；
  - 非收集品和没有 NPC id 时不走这条；
  - 余响没有好感系统，什么都不喜欢。
- **官方箱子分档**：
  - 保险箱、武器箱算高价值，普通箱和未知名键算普通；
  - 宠物背包、快递箱、爱心箱、任务箱排除；
  - 37 号实验区的主场景是 `Level_SnowMilitaryBase_Main`。

**不覆盖**，留给 Windows 编译与实机验证：
- 开箱当刻同步塞物品、满箱扩一格；
- 官方 `LootView` 显示与搜索；
- 收藏册界面与登记容器；
- 杰夫任务投影；
- 售货机上架；
- 系列加成挂到主角身上。
