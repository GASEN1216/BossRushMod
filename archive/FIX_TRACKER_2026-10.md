来源：`FIX_TRACKER.md`；归档区间 2026-10，原节正文保持逐字不变。

## 2026-10-01 天空岛实机反馈七项（第二批）：完成面板、序章前入口、Boss 备弹、贝壳签筒、数值减半、岛上白天户外后处理、静默门 5 m（COMPAT，L1/L2）

- 序章交付完成面板一开就关（根因已查实，L1）：官方 `QuestCompletePanel` 逐帧等「奖励全已领取 / 点跳过」，投影奖励的 `Claimed` 读交付事实、交付那一拍已为真。`OfficialQuestCompletePatch` 加 Postfix，只把刚完成的那份实例的奖励行先显示成未领取，官方 `Claim` 收起；读档重建照旧已领取。征程任务同受益。
- 序章完成前基地船点仍有天空岛选项：`SkyIslandPreludeFlow.routeUnlocked` 只在开门面时读一次，F3 清空进度后缓存不刷新（L1 推断为实测成因）；改为每拍按剧情事实重读。「航路已开放」横幅改为只在 Jeff 交付那一拍置位、挂入口时念一次，开游戏进基地不再重念。
- Boss 有枪没子弹（L1）：`SkyIslandEnemyArmory` 只给换上的枪放备弹，达标留下的官方原装靠官方 `AddBullet`（按 Boss 预设弹药品质表抽，不少口径抽不到）。新增 `EnsureAmmo`：主 / 副武器每把枪补到三匣、至少 45 发（`ReserveRounds`），弹种先沿用已装 / 已带的。
- 贝壳签筒（新内容）：码头与风铃集航路图交互组各加小签（3 贝壳，航务补给档 q2–5）/ 大签（10 贝壳，星工遗存档 q4–8），走搜刮箱同一套池与合成台同一套扣料事务；不新增世界交互体。贝壳来源是保留官方 Boss 掉落表的头目 / 岛主 / 具名对手 / 噬风。
- 天空岛 Boss 数值减半：`SkyIslandCombatBalance.Multiplier` 3 → 1.5（感知 1.5 不变）；序章守卫 750、噬风 3000。
- 「视野外白天和黑夜一样、烟雾太浓」（根因已查实，离线读官方场景）：租约克隆的地堡 `TimeOfDayConfig` 晴天条目白天 / 黎明 / 夜里全用室内 `BaseVolume`，远景雾纯黑。新 `SkyIslandOutdoorDaylight` 在副本里把白天、黎明换成官方 `Volume_Level_GroundZero_Morning`（浅蓝远景雾），夜里与基地原件不动。一度试过调官方视野迷雾 `FogOfWarWorld` 的不透明度，查实不是成因后撤回。
- 「附近有威胁」静默门 35 m → 5 m（剧情面板、对话、交任务、航徽、云蚋散群同一道门）；巡守摆放净空与写盘半径不变。
- 证据（L2）：全量守卫 735/735；执行回归全量 113/113（三变量）；`JeffQuestFlow` 新增完成面板断言、`SkyIslandPlayerEntryGuard` 公告一次、`SkyIslandEnemyArmoryGuard` 补弹、`SkyIslandLifecycleGuard` 与 `SkyIslandRaidLease` 执行回归的户外后处理（只换副本）均反向验证转红后按字节还原；正式编译部署 `BossRush.dll` 1fbec4a1…（游戏目录同 sha）；Wiki 地图页 / 更新日志中英同步，站点重建通过。
- 未实机（L3）：Jeff 交付后完成面板停住、点「全部领取」或跳过才收；F3 清空进度后船点无「前往天空岛」、Jeff 重新挂「云上的坐标」；进基地横幅只在刚交付后出现一次；头目打空弹匣后能换弹继续开火（Player.log 无 `[SkyIslandArmory] 这把枪找不到可用弹药`）；航路图滚轮有两支签、贝壳不够有提示、背包满不扣贝壳；岛上白天远处不再压成一片黑、呈浅蓝远景雾（Player.log `DAYLIGHT_VOLUME replaced=` 大于 0），夜里观感不变；贴身 5 m 外的敌人不再挡对话。

## 2026-10-01 收尾：天空岛 CR-2026-09-30-005～012 与审查遗留 P3 / 复用全部修复（COMPAT / WIRE+ / OPERATIONAL，L2）

原 09-30 审查报告已不在本机与私有仓库，三路子代理按摘要在当前代码里重新定位，8 条 CR 与审查遗留 4 处全部成立并修复。

- CR-005～012：见 CODE_REVIEW_FINDINGS（撤离提前写盘同批存角色资产；婚后恢复补每日衰减；租约放下官方卡住的加载忙标志；帧时间跨终点长帧计入；HUD 恢复后过期警示即收；自动验收总状态并入套件红项；Shader 检查自动探测游戏路径；循环音不依赖发声体存活也能停）。
- 同日更正 CR-003 描述：官方死亡处理（丢物、存角色、写盘）先于会话回调执行，`OnPlayerDied` 是紧接其后立即结算写盘，不是「之前」。
- P3 性能：划地为营玩家血条与鸭王杯群战队伍后缀在名字 / 阵营 / 语言不变时复用缓存串；快递员首次见面只在走近时查存档且记住已见面；龙皇铳子弹补丁在没有龙皇铳子弹在飞时第一句早返（全场子弹每帧少一次 GetComponent）。
- 复用：`Integration/Items` 下 4 份私有 `SetHiddenMember` 改用 `ModeFItemConfigHelper.SetHiddenMember`（静态证明等价；`ModeE/RespawnItemConfig.cs` 那份带 try/日志，行为不同，未动）。
- 证据（L2）：全量守卫 735/735；执行回归全量 113/113；新增 / 扩展守卫 `ModeHGroupTeamTagsNoRebuildGuard`、`CourierFirstMeetSaveQueryGuard`、`DragonKingBossGunProjectilePatchEarlyOutGuard`、`ModeEHealthBarNameCacheGuard`、`PermanentSpouseRestoreLifecycleGuard`、`SkyIslandHudGuard`、`F3AutotestOrchestratorGuard`、`WindowsPathDetectionGuard`、`AssetSnapshotBoundaryGuard`、`SkyIslandContentPackGuard` 与对应执行回归均做反向验证；Dev 与正式构建都编过，正式 DLL `49336682` 已部署，`check_dll_identifiers --expect absent` PASS；`verify_sky_island_bundle_shaders.py` 不设变量实跑 PASS。
- 未实机（L3）：撤离结算页强退后灯与材料一致；婚后多日不互动好感下降；返航加载异常后约 2 秒重试（日志 `RAID_LOADER_FLAG_RELEASED`）；开地图 5 秒再关不再续播旧警示；回基地后听不到岛上风声；Mode E / 鸭王杯血条名字显示不变；快递员首次见面照常；龙皇铳各弹种照常。

## 2026-10-01 发布前外部审查：6 条 P1 + 11 条 P2/P3 全部核实成立并修复（COMPAT / SCHEMA+，L2）

外部审查（只读、未编译未实机）列的每条都先对照当前代码与官方反编译源核实，全部成立，按最小改法修。三路并行：本会话（天空岛 / 永久 NPC）+ 两个子代理（ModeH / 无间炼狱 / 划地为营 / 重铸 / 好感；丧尸 / 遗种巢 / 武器 / 龙王）。

- P1 永久 NPC 与天空岛居民头顶名字只登记一次（不跟随、隐藏不收、销毁不注销）：`DuckNpcRuntimeMarker` 加 LateUpdate 刷新、OnDisable 收起、OnDestroy 注销（口径同快递员 / 护士）。
- P1 群战重打沿用押注不记押哪队：押注记录加可选 `betSide`（旧档缺省 0，SCHEMA+）；沿用时恢复当初那一边、收起选边与「换一批」、倍率显示押注那档（`ModeHCashBetService` / `ModeHRuntimeModule_GroupFlow` / `_BetFlow`，GroupFlow 超行数把四个小方法挪进 BetFlow）。
- P1 无间炼狱本波现金跨局残留：复位块补 `InfiniteHellWaveCashThisWave = 0L`。
- P1 CR-2026-09-30-002 / 003 / 004：见 CODE_REVIEW_FINDINGS（回主菜单不再保留暂不入档记录；倒下紧接官方死亡存档立即结算写盘；折翎 / 钟守婚后跟随能走）。
- P2 划地为营 / 随机事件神秘商人 999999 生命没生效：`GetComponent<Item>()` 改 `character.CharacterItem`（新守卫 `ModeEMerchantHealthGuard`）。
- P2 天空岛巡守离居民太近：选点净空居民 35 m、码头 45 m（与剧情面板 / 写盘安静半径同口径），重生成 176 个槽位（钟庭合格位只剩 9 个，20 → 9），派生落点与场景审计同步（缺陷仍是 23 条设计接触）。
- P2 尸潮信标 / 便携安全区 / 尸潮邀请函悬停弹通知：三个 `CanBeUsed` 只返回结果，原因留在模式侧 Try 路径。
- P2 丧尸撤离销毁 1 秒内刚捡的物品：整表销毁前 `ReleaseZombieModeOwnedDropCandidates()` 先把已在背包 / 槽位里的候选移出。
- P2 遗种巢远征奖励发不出时每 5 秒整档写盘：只有真正到账才提交落盘，诊断计数封顶 6 次；存盘失败码补 6 条中英词条（守卫自动扫描失败码）。
- P2 好感度在官方采集事件里自己 `SaveFile`（`.bac` 存半成品）：去掉，数据随官方紧随其后的写盘落地。
- P2 焚皇断界戟跳斩落地 / 爆燃补 `isFromBuffOrEffect`；龙王、龙裔补敌对性安全网（豁免遗种巢随从与 Mode E/F）。
- P2 群战名人堂净赚只算最后一次会话：账本加按季累计字段（SCHEMA+，schemaVersion 不变，已登记 `docs/contracts.md`）。
- P2 重铸冷淬液：先 `LockProperty` 成功再扣，扣不掉撤回锁定。
- P3 霜之哀伤 / 噬魂挽歌 / 焚皇戟回退刀光每刀全内存扫描：照新武器加 5 秒重扫节流。
- 证据（L2）：全量守卫 732/732；执行回归全量 113/113（设 GAME_PATH 等三变量）；新守卫 `ReleaseReviewNpcAndDepartureGuard`（7 条内存反向探针）与各子路新增断言均做反向验证；正式编译部署见提交说明；Wiki 鸭王杯页与天空岛页、站点重建。
- 未实机（L3）：各条实机判据见对应子路报告摘要——NPC 名牌随人走、回主菜单不留灯、婚后折翎跟随、群战重打选边锁定、名人堂分次净赚、无间炼狱首波现金、商人打不死、重铸扣料、背包悬停无通知、撤离末秒拾取保留、焚皇技能不触发词缀、龙王仍敌对、刀光不缺。

## 2026-10-01 天空岛实机反馈七项：全岛官方 Boss、属性 ×3、加密巡守、搜刮箱、居民中立、码头 BGM（COMPAT / OPERATIONAL，L1/L2）

- 有枪不开（根因已查实）：刷怪底模按「阵营 scav、不是 Boss」收全部 preset，官方快照里 33 个只有一半是持枪拾荒者；序章「断风游猎·守」取名字排最前的，正好是近战的 `EnemyPreset_BossMelee_SchoolBully_Child`。开不开枪由 preset 私有 `aiController` 决定，与手里的枪无关。
- owner 拍板「天空岛全部用 Boss 预设」：新 `SkyIslandEnemySources`（头目 / 岛主 / 具名对手 / 噬风 / 序章克隆各自参照的官方 Boss；小兵、精英、巡守按点位散列到官方 Boss 池，排除 `_NPC_`、口口口口、`_Test`、载具、中立 / 玩家阵营、非 `EnemyPreset_` 前缀）；新 `SkyIslandMinionKit`（小兵 / 精英 / 巡守留 Boss 行头，杂项掉落、经验、现金换岛屿拾荒者口径，`isBoss` 关掉免得刷爆战役「击杀 Boss」、日报与 Rogue 结算）；`SkyIslandEnemyArmory` 空主武器槽不补枪（近战 Boss）。巡守岛区鸭模外观 `SkyIslandPatrolAppearance` 删除，头顶是 Boss 自己的名字。
- 属性：`SkyIslandCombatBalance.Multiplier` 1.5 → 3（生命、伤害、反应、开火前摇、散布、暴击）；新 `PerceptionMultiplier = 1.5`（移速、弹速、射程、视野、听觉、夜视，乘 3 会在一屏外开火）；小兵、精英、巡守按抽到的 Boss 原版算（owner 选「全按各自 Boss 原版 ×3」，巡守再乘岛区系数）；巡守的反应 / 开火前摇不再被岛区档案覆盖成 0.5–1.2 秒；序章守卫额外 ×2 生命（1500）。
- 密度与落点（子代理）：巡守 112 → 187、ActiveLimit 24 → 32、挂起半径 110 → 95 m；选点离岛缘 ≥12 / 10 m、离路网与地标 ≤20 m、离导航边界 ≥2 m（旧槽位 67 个离岛缘 <5 m）；派生 `runtime_placements.json` 与场景审计同步。
- 搜刮箱（子代理）：只对搜刮点改交互名「搜集 / Search」（反射写 `displayNameKey`，库存标题同名）并去掉「搬起」；每箱 3–7 格（生活 3–4、航务 4–6 / 富裕岛区 5–7、星工 6–7），第一格保底一堆岛上特产，其余按类别牌发物资 / 子弹 / 武器 / 装备 / 杂项，子弹按堆；12 个类别池进预热。一趟满搜约 200 格（旧约 92 件），经济约 ×2。
- 居民在岛上改 `Teams.middle`（敌人隔桥锁打不死的居民）；背景音乐只在登云码头放（按脚下岛区获取 / 释放场景曲租约）。
- 证据（L1/L2）：全量守卫 730/730；天空岛执行回归 16/16、F3 回归 3/3（设 GAME_PATH 等三个变量）；新回归 `EnemySourcesRegression`（Boss 池口径、参照克隆、小兵换掉落）与守卫改动均做反向验证；正式编译部署（最终 DLL 见下一条，构建与游戏目录 SHA 一致），`Patrols.json` 部署 SHA 一致；Wiki 站点重建。
- F3「NPC/剧情」页新增「清空天空岛剧情/任务」（只在 Dev 构建）：`SkyIslandProgressDebugControls`（宿主 partial 预算已满，挂在 `CampaignPetNestDebugControls.Build` 末尾）→ 确认框 → `SkyIslandStoryService.DevResetCurrentSlot`（只许岛外；基地优先用序章常驻门面，免得旧数据回写；共享 store + 立即落盘 + 读回核对）。官方任务日志由投影核心下一拍撤回；物品、好感、征程不动。Dev 与正式两种构建都编过，正式 DLL `4F9B3E32` 已部署，`check_dll_identifiers --expect absent` PASS。
- 未实机（L3）：Boss 池实际成员看 `Player.log` 的 `[SkyIslandEnemy] BOSS_POOL`；非鸭模 Boss（机械雪人等）上 Forge / 守钟装置换脸是否生效；Boss 原装枪不换时的备弹是否够；187 巡守 + Boss 模型的帧时间未采样；Boss 技能（手雷、召唤等）在桥上与居民附近的表现；首次到岛时居民 35 m 内有巡守，需要先清场。

## 2026-10-01 天空岛路灯夜里亮并照亮周围（COMPAT，L1/L2）

- 根因：Tripo 雕花路灯与铜灯柱的灯罩是图集里的不透明画面玻璃，材质无自发光，`lantern()` 塞在里面的 Glow 灯芯被挡住；岛上没有灯光源，且环境着色器是 `UniversalMaterialType=Unlit`（GBuffer albedo 为 0），URP 点光源照不到岛面。
- 修：`tools/sky_island_surface_materials.py` 按灯头高度带与图集亮度把灯罩玻璃分进发光材质；生成器 `lamp_light` 登记 67 盏灯位（摆放裁决同步平移 / 撤掉），写成场景标记 `NightLamp_*`；作者着色器新增 16 盏灯的全局数组光照；新增 `SkyIsland/SkyIslandStreetLamps.cs`（会话持有，按日光色亮度判昼夜，白天 O(1) 早返，夜里写数组并在最近 6 盏放真实点光源）。
- 证据：新守卫 `SkyIslandStreetLampGuard`（反向验证）；全量守卫 730/730；天空岛执行回归 16/16；Unity 碰撞校验与 ECM2 移动实跑 0 失败；着色器判包 PASS；部署 `sky_island_raid` `498bb1f8…`、`BossRush.dll` `8f19794b…`。未实机：灯光强弱与半径按报告 §11 清单 L1–L8 实测后再调。
