## 2026-09-20 第三轮：外部审查 11 条复核 + 补漏

对应 `docs/reports/testing/20260922人工实测复核修复记录.md`。兼容性：`COMPAT` + `SCHEMA+`
（远征记录 `petShiny/petChromaA/petChromaB`、纪念碑 `chromaA/chromaB`，schemaVersion 均未变）
+ `OPERATIONAL`（`compile_official.bat` 的音效部署改为整树 + 缺失告警）。无 `SCHEMA-` / `WIRE-` / `BREAKING`。

### 确认并修复（7 条）

- **CR-2026-09-20-013 套装冻结「假成功」**：`TryApplyFrostFreeze` 在 `AddBuff` 后无条件
  `return true`。官方 `AddBuff` 返回 void 且有三条静默 no-op 路径（`buffResist` 命中
  `ExclusiveTag`、同 tag 更高 `ExclusiveTagPriority`、同 tag 同优先级但现存剩余时间更长），
  抗冻目标正好走第一条。现在回读 `target.HasBuff(freezeBuff.ID)`；兜底减速也按
  CharacterItem / 速度 Stat / 协程是否真的拿到回报真实结果。反击冷却挪到冻结成功分支之内。
- **CR-2026-09-20-014 霜噬 / 雷噬提前消耗冷却**：冷却从排队时挪到结算时，
  `frostBitePending` / `thunderBitePending` 挡住延迟窗口内的重复排队。目标在 40~50 毫秒
  延迟里死掉、中途脱装备切图、或雷噬扫不到其它敌人时不再白吃一轮冷却。
  反 DPS 三道闸不变（同时最多一条在飞、伤害仍是常数、只认直接命中）。
- **CR-2026-09-20-015 官方 Boss 名单没补目录**：`AddOfficialEntries` 只遍历
  `GetFilteredEnemyPresets()`，被筛选器关掉或 preset 还没被扫到的官方 Boss 在图鉴里
  连锁定卡都没有。新增第 1b 步 `AddOfficialRosterEntries()`，按
  `CodexOfficialBossRegistry.OfficialBossKeys()`（有序，40 条）补成未解锁卡。
  连带：「全收集」的分母改为官方全部 Boss + 3 自定义 + 5 丧尸，不再随筛选器缩水。
  顺带删除只剩夹具在用的死代码 `GetEncounterHint`（owner 问题 5 已要求去掉那段提示）。
- **CR-2026-09-20-016 日报底图与运行时重复绘制**：签到格 / 签到按钮 / 图例色块
  两边都画，叠出双描边，且颜色两个来源已经漂了（C# `CellEmpty` 199,189,166 vs
  脚本 226,219,205）。收敛成「底图只画不变的装饰，会变色的归运行时」，
  `gen_daily_report_ui.py` 不再画这三处也不再保留那几个颜色常量，底图已重出并部署。
- **CR-2026-09-20-017 日报卡片内滚动实际滚不动**：内容 `sizeDelta` 写死成 viewport 高度，
  Clamped 模式下 `ScrollRect` 认为刚好装得下。改用
  `ContentSizeFitter.verticalFit = PreferredSize` 按 TMP 首选高度撑开。
- **CR-2026-09-20-018 散装音效没被部署**：`compile_official.bat` 的逐文件夹清单只列了
  BGM / SkyIsland / SetBonus / NewWeapons 四个，代码实际还读 Achievement、DragonKing、
  Goblin、Nurse、items、lottery 六个。这六个在 owner 游戏目录里是早年手工拷的，
  干净安装会静默无声——许愿台大奖音乐与遗种巢异色揭晓复用的
  `Assets/Sounds/lottery/special.mp3` 正在其中。改为整树 `xcopy /E` + 逐文件夹缺失告警。
- **CR-2026-09-20-019 远征 / 纪念碑丢炫彩与异色**：`PetNestChroma.Decorate` 只吃
  `PetNestPetRecord`，而远征卡、翻牌卡与碑文显示的往往是真死结算后已被移出巢的崽。
  新增 `SCHEMA+` 字段固化颜色、新增脱离 PetRecord 的 `Decorate` / `DescribePair` 重载，
  展示入口统一为 `PetNestExpeditionService.DescribeDecoratedPetName`。

### 复核后不成立 / 已过期（3 条）

- **审查第 4 条（立绘 bundle 与作者工程不一致）：refuted。** 比对对象错了——
  作者工程的 `AssetBundles/` 是旧的临时输出目录，正式出口是 `ResourceRelease/`。
  逐文件核对 `ResourceRelease/Assets` 与仓库、游戏目录：**全部 SHA-256 一致**，
  包括 `codex_portraits`（5,025,857 字节）。`AssetBundles/` 里的旧副本是陷阱，
  已记在交付文档里提醒不要从那里重打。
- **审查第 5 条（报箱 / 公告栏 / 展示柜没接 3D 模型）：已过期。**
  四个 builder 现在都先走 `BuildingModelHelper.TryInstantiateBundle` /
  专用 bundle 加载，缺包才退回占位；`bossrush_daily_mailbox`、`bossrush_campaign_board`、
  `bossrush_backmountain_showcase`、`petnest_relic_nest` 四个 bundle 均已存在并部署
  （另一个会话当日 21:11 产出，记录在 `Build/buildings-20260920/author-workspace-deployment.json`）。
  bundle 内部是否真的含对应 prefab 只能实机确认（本机无 UnityPy，压缩包无法离线开箱）。
- **审查第 11 条（实机跑的不是第二轮产物）：已过期。** 当日 21:19 另一个会话已重新构建部署；
  本轮再次构建部署，`Build/BossRush.dll` 与游戏目录 SHA-256 一致
  `6FF528E2777FAA2351C012E701719EE9D208FCB7EB76151688877E2B68F193EF`。

### 仍需实机（1 条）

- **审查第 10 条（Mode H 九图）**：`TryDeriveMap` 的选点已是 fail-closed，
  斗士只落在真实刷新点、离场点不会回退到地下隔离点（第二轮修复）。
  但导航连通性、视野、双方生成与安全退出**只能 L3 验证**，离线无法替代。

### 验证

- 全量守卫 **647 PASS / 0 FAIL / 0 KNOWN-RED**（新增 `LooseSoundDeploymentGuard`；
  改写 `SetBonusLifecycleGuard`、`DailyReportPresentationGuard`、`SkyIslandMosquitoGuard`）。
- 执行回归 **51 PASS / 0 FAIL**（3 个依赖本机游戏程序集的夹具需先设
  `BOSSRUSH_HARMONY_DLL` / `BOSSRUSH_GAME_MANAGED`，设后通过）。
- 反向验证：11 个探针（4 套装守卫 + 1 图鉴回归 + 2 日报守卫 + 2 音效守卫 + 3 遗种巢回归）
  全部在预期位置转红，并按字节还原。
- Windows 正式编译 `Build succeeded!`；`check_dll_identifiers --expect absent` 通过
  （14 个 Dev 标识全部缺席）；72 个 bundle SHA-256 校验通过；
  `npm --prefix wiki-site run build` 通过。
- 未验证：全部运行时观感与手感。本轮未启动游戏、未读写玩家存档（AGENTS §10 由 owner 自己做）。


<!-- 原文结束；上文保留原有换行。 -->
