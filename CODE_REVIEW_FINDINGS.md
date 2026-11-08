# CODE_REVIEW_FINDINGS.md — 已确认问题库

更早记录见 `archive/`；月度归档索引保留在本文件正文。

## 2026-11-08 潮闸镇敌人捏脸与演练误报（001–004；COMPAT，L1/L2，修正版 L3 待验）

来源：owner 外观反馈、`BossRushValidation_20261107_170000_978.log` 与对应 Player.log、部署 DLL/资源和当前代码。详细验证见 `docs/reports/testing/潮闸镇_2026-11-08_敌人捏脸与演练误报修复.md`。

- **CR-2026-11-08-001 · P2 · 各岗位只有装备差异，仍是同一张 Scav 脸。** `TidegateEnemyIdentity.Dress` 原先只换装备；官方异步创建在返回前设 `CustomFacePreset_ScavBlue`。新增 `TidegateEnemyAppearance`，创建/配装后从实例完整值复制，应用 11 套真实官方部件、嘴型、羽色、头型和体型；不改共享 preset。部件预检、失败恢复与当前实例读回一并接线；装备遮挡与穿模仍需实机观察。
- **CR-2026-11-08-002 · P2 · 敌人验收仍按旧 27 人断言。** 实机已有 31 槽、镇 10 / 港 7、6 守卫 / 1 Boss、31 个距离样本且无失败，却报 `encounter_roster_expected_27...`。改为当前 31 人纯判据；512 个生产阵容样本同时验证此判据，旧数、错分区、错同伴和错岗位均不通过。
- **CR-2026-11-08-003 · P2 · Q4 鸭蹼继承红色光晕，被误计高档随机奖。** 失败点 `LOOT_buried_50` 实为 Q4 的 135/125/111/500114，整趟抽签未中奖；`CountsAsUnderwaterJackpot` 却额外 OR `DisplayQuality >= Red`。实包模板 Q4/DQ6 经遗种卵和潮闸镇物品克隆保留颜色，鸭蹼仍 Q4。按 owner 定义改为严格 `Quality >= 5`、固定怀表豁免；夹具链接生产注册链与实包模板，复现原失败种子，F3 逐件记录 Q/DQ。概率与物品颜色均未调整。
- **CR-2026-11-08-004 · P3 · 卷走演练先移除唯一投掷手。** 生产把 `dig` 首位分配为 Gunner，旧深水探针选首个非 Boss，随后岗位检查 `gunners_armed=0`。探针改选普通 digger，保留真实卷走过程；自然交战造成样本不足仍报告 WARN。

上一轮性能项 `CR-2026-11-07-011` 已收到 L3：本报告码头/镇区 10 秒 P95 38.22/39.29 ms，均 PASS，基线 31.75 ms、峰值 154.94 ms；游泳禁翻滚也 PASS。结论只覆盖该次旧构建窗口，新捏脸后的性能仍待复测；全项目覆盖 INCOMPLETE 保留。

## 2026-11-07 潮闸镇后续室内图与水下奖励口径（007 复开、012；L3 待复测）

owner 新图证实 007 的上一轮“只隐藏瓦面”修复不满足室内视野。现按官方建筑资源的上部外壳分组修正：23 栋入室隐藏上部外壳、设备和高墙，保留本图 0.9 m 低墙及完整家具。实际包语义/面积验证、反向破坏、民居与商店作者预览已通过；仍需实机进出门复测。官方存在 Fade 和 ShadowOnly 两种模式，并无统一 0.9 m 常量。

- **CR-2026-11-07-012 · P2 / COMPAT · 水下高档随机奖励过密。** 旧金库普通池、精品、所长/船长珍藏及滩涂货单各自出高档货，没有整趟限制。owner 明确以 Quality ≥ 5 计“红”，要求整趟约 80% 有、20% 无，并确认固定剧情怀表豁免。改为进图一次分配随机奖励预算，中奖趟一件 Q5–6，其余随机物品最高 Q4；怀表固定保留。2,048 趟实际物品生成样本 1,642 趟有奖、406 趟无奖、每趟最多一件；开箱顺序、货单及陆地兼容由生产逻辑隔离回归覆盖。

完整证据与交付状态见 `docs/reports/testing/潮闸镇_2026-11-07_室内切顶与水下红品概率.md`。

## 2026-11-07 潮闸镇建筑、桥梁和第三份实机报告（007–011；新版 L3 待复测）

来源：owner 四张截图、`BossRushValidation_20261107_024811_443.log`、当前生产代码与作者模型。详情见 `docs/reports/testing/潮闸镇_2026-11-07_建筑桥梁敌人与性能修复.md`。

- **CR-2026-11-07-007 · P1 / COMPAT · 建筑虚化范围错误。** 旧运行时给全体建筑安装视线裁切属性。第一轮改为脚点进入本栋触发官方 `ShadowOnly`，但资源只隐藏真实瓦面，owner 新图证实仍留下多层外墙和悬空太阳能设备。该问题重新打开，修正判据为同栋上部建筑及附着物一起隐藏、低墙与完整家具保留；官方住宅和商店实际 `Fade` 组有上层外墙和屋顶设备，证据见后续室内切顶报告。新版 L3 待复测。
- **CR-2026-11-07-008 · P1 / COMPAT · 桥头落差、重叠面及栏杆无碰撞。** 平滑地形先低于甲板、逐格甲板断开，导致卡桥唇；地形与甲板重叠，多个栏杆只有渲染。作者构建新增连续坡面、可见引桥、地形让位及按实际栏杆网格的碰撞。用 Unity 物理逐点检查，不以导航连通替代真实通道。
- **CR-2026-11-07-009 · P2 / COMPAT · 岗位敌人继承拾荒者通名。** 除 Boss 外未覆盖 `nameKey`，外观仍随机。创建前写入双语岗位名，创建后按岗位装备；全部人形以官方 Scav 为底模。另按 owner 要求适度提高人数与箱子奖励，数值见台账。
- **CR-2026-11-07-010 · P2 / WIRE+ · 游泳仍可翻滚。** 官方 `StartAction` / `CA_Dash` 无游泳门。新增潮闸镇主角限定拦截，入水后终止未结束翻滚并清除冲量。隔离回归通过；真实补丁命中由 `TIDE_DRILL_SWIM` 观测。
- **CR-2026-11-07-011 · P1 / COMPAT · 远处角色持续运行造成显著帧耗。** 实机码头/镇区 P95 105.75/134.91 ms；同站位停用远处角色从 61.37 降至 34.40 ms，仅停 AI 或建筑无同等收益。新增保留实体状态的距离挂起；战斗、调查和潮汐行动豁免。2026-11-08 收到后续 L3 `20261107_170000_978`：两个 10 秒窗口 38.22/39.29 ms 均通过，原窗口超阈值问题已取得修复证据；峰值仍 154.94 ms，不延伸为全程流畅或新捏脸构建通过。

## 2026-11-07 潮闸镇第二份实机报告（`BossRushValidation_20261106_154008_887`；001–006 已修复，L1/L2，L3 待复测）

来源是 owner 实机跑的 Dev 演练与 Player.log（Dev MVID `c7f3c516`）。修复与验证见 `FIX_TRACKER.md` 同日节。

- **CR-2026-11-07-001 · P2 / COMPAT · 四相用例把锁着的领班柜判成故障。** `DebugAndTools/F3GameplayValidationTidegateDrill.cs` 的箱柜核对只比 `BoxActive` 与 `Flooded`；`Tidegate/TidegateLoot.cs` 的 `Locked` 让 `LOOT_safe_35` 在拿钥匙前停用交互。实机 `Low/High:loot_LOOT_safe_35_flooded_False_deep_False_active_False`。**修复：**纯判据纳入上锁与埋货模型。
- **CR-2026-11-07-002 · P3 / COMPAT · 卷走用例依赖瞬移把人留在深水。** 官方寻路把角色拉回可走面，`deep_s=0.0,left_deep=True`，用例长期 WARN。**修复：**Dev 钩子在生产判定前按住深水点。
- **CR-2026-11-07-003 · P2 / COMPAT · 开闸增援先于撤退阈值被淹。** `Tidegate/TidegateEnemies.cs` 的 `vault` 组等 `LagoonLevel` ≥ −0.9 m 才回海堤，而开闸后泻湖跟随外海，金库地面先超过 0.5 m 深水；实机 `SWEPT_AWAY id=TidegateExtraSpawn_28`。**修复：**与淘沙帮同一「涨潮前 20 秒撤」窗口。
- **CR-2026-11-07-004 · P2 / COMPAT · 普通工作箱内容公式化。** `TidegateLoot.ShiftItem` 写死两套物品 ID（2/3 概率）；实机一趟 7/7 渔篓以 1106 开头、6/10 工具柜为 332+335。**修复：**按官方标签不放回发牌。
- **CR-2026-11-07-005 · P3 / COMPAT · 同趟高价值柜精品重复。** 各柜独立抽精品，实机所长柜 `LOOT_vault_71` 与船长柜 `LOOT_safe_77` 都出 12009。**修复：**同趟精品去重。
- **CR-2026-11-07-006 · P3 / COMPAT · 响铊每杀必掉且可无限使用。** 第二把起只剩卖钱的用处；沙老大与天空岛风猎看守同为 1.0 倍 BALeader。**修复：**已携带时换私货，模型放大 1.2 倍。

## 2026-11-06 新内容审核（`63c9e9df` 之后至 `fc15dbd8`，含未提交的潮闸镇 F3 演练；001–010 已修复）

状态：001–010 全部 Fixed（2026-11-06，L1/L2；L3 待验）。每项保留初审时的调用链和位置，**修复**为当前行为。001–005 影响正式玩法，006–009 影响 Dev 验收，010 影响作者工具。修复与验证见 `FIX_TRACKER.md` 同日节。

- **CR-2026-11-06-001 · P1 / COMPAT · 游泳碰撞体不随潮汐或开闸升降。** `Tidegate/TidegateWater.cs:107`、`:112`：两个缓存初始为 `NaN`，与它的差值取绝对值再比较，首帧和之后都为 false，`SetTop` 一次都不会调用。水盒始终是中心 y=0、高 1 m 的初始形状；官方 `CharacterMainControl.cs:1581` 用 Water 碰撞体检测入水，`:1598` 取其顶面浮起，视觉水位和实际游泳脱节。L1 + .NET 数值复算：−1.5、+0.45、+0.62 m 均不更新。**修复：**首次按真实水位设置海、湖物理顶面，之后各自跟随涨退和开闸。
- **CR-2026-11-06-002 · P2 / COMPAT · 泻湖用一个包围矩形，覆盖了本应退水的岸边。** `Tidegate/TidegateWater.cs:66` 把遮罩的外接矩形整块设为泻湖，外海四块恰好避开它；`Tidegate/TidegateWorld.cs:210` 和水面材质按实际遮罩区分水域。L1/L2：读取本地 `tidegate_raid` 内的 `TidegateHeight`；Unity `(x=41,z=32)` 命中该矩形，G=0.1917（<0.5，外海），地面 y=0.0375 m，无墙或障碍。即使修完 001，未开闸、低潮时这块干地仍会收到 +0.62 m 的泻湖水盒。**修复：**按材质的纹素中心取样并合并水域；发布遮罩上原四处岸点均判海，海、湖可同时查询。
- **CR-2026-11-06-003 · P2 / COMPAT · 交怀表可先保存结局、漏掉谢礼。** `Tidegate/TidegateStory.cs:214` 在扣表、发防水袋前 `Mutate`，`:53` 请求落盘，`:76–78` 声明不欠资产快照。官方 `SavesSystem.cs:503` 的 `SaveFile(false)` 只写已有缓存。若剧情已落盘、官方尚未采集新背包就退出或中断，重载可见旧怀表、没有谢礼，`WatchDelivered` 又禁止重领。F3 清空剧情后才移除物品也有同一保存窗口。L1。**修复：**交表、寄信与 Dev 清空先同批发布资产和事实；缓存失败回滚、物理失败重试。外部抢先写盘和重新加载均已回归。
- **CR-2026-11-06-004 · P2 / COMPAT · 剧情首次读取失败仍能清掉图鉴镜像。** `Tidegate/TidegateNoteBridge.cs:95–96` 在加载前检查可写性；`BossRushSlotJsonStore.LoadOrInitCore` 到加载时才识别坏 JSON 或未知版本，返回全 false 的默认值，随后 `EnsureRegistered` 会收回原有条目。`:143` 的主动点亮也绕过写屏障，读信或翻页写入失败后仍会调用它。官方下次采集可保存错误镜像。L1；是 11-05-007 未覆盖的入口。**修复：**首次加载后再检查写屏障与已记录事实，坏数据不增删官方图鉴。
- **CR-2026-11-06-005 · P2 / COMPAT · 灯塔漏标揭示期间才刷出来的敌人。** `Tidegate/TidegateEnemies.cs:154` 先调 `MarkOnMap`，`:158` 才登记角色；`:251` 因角色为空直接返回，之后没有补挂路径。点灯后的 40 秒内，刚进入刷怪距离的敌人和海堤增援不会出现在地图上。L1。**修复：**新角色成功登记后再挂灯塔标记；揭示期间的异步生成已加入回归。
- **CR-2026-11-06-006 · P2 / COMPAT · 没见到守卫或沙老大，刷怪用例仍可 PASS。** `DebugAndTools/F3GameplayValidationTidegateDrillCases.cs:886` 跳过没开始的槽，`:917–919` 只要求至少启动一个槽。只刷出普通拾荒者时，守卫武器和 Boss 身份都未检查，报告仍可通过。L1。**修复：**分别核对守卫、Boss、距离与活体样本，缺失报告 WARN。
- **CR-2026-11-06-007 · P2 / COMPAT · 演练取消后，尚在创建的增援仍能加入本趟出击。** `Tidegate/TidegateDrillHooks.cs:295` 跳过未返回的创建任务；`DebugAndTools/F3GameplayValidationTidegateDrill.cs:549` 只记错，`:572` 标记已还原。`TidegateEnemies.Spawn` 等待结束仅检查出击会话，取消演练后会话仍有效。L1。**修复：**先取消额外槽，再处理迟到角色与预设；取消后不入场、不加标记。
- **CR-2026-11-06-008 · P2 / COMPAT · 专用测试档清空进度后，旧快递还能领取。** `Tidegate/TidegateDrillHooks.cs:533` 只统计快递缓冲里的剧情物品。留着阿稳寄来的第一封信再点清空，旧信仍可领，`TidegateStory.TryDeliverLetter` 又按未寄送状态寄一封。L1。**修复：**只清四类目标，保留普通物品和子树；连续清空再重载、补寄只留一封。
- **CR-2026-11-06-009 · P2 / COMPAT · 缺失泻湖水盒时，装配和潮相用例跳过这一判据。** `DebugAndTools/F3GameplayValidationTidegateDrillCases.cs:65` 只要求有外海盒；`DebugAndTools/F3GameplayValidationTidegateDrill.cs:457` 仅在泻湖盒存在时才校验。遮罩缺失、全空或不可读时，生产端会退为一个外海盒，演练不会报泻湖物理缺件。L1。**修复：**两类水盒均须存在，逐盒核对有限顶面；缺失、NaN、无穷与错高均不能通过。
- **CR-2026-11-06-010 · P2 / OPERATIONAL · 作者导出服务接受其他网页覆盖导出文件。** `tools/tidegate_dev_server.py:19` 只校验路径和名字，`:26` 直接覆盖同名文件。L2：在临时目录启动原服务，向 `/__save?name=tidegate_layout.json` 发送带不同本机端口 `Origin` 的 `text/plain` POST，实际返回 200 并覆盖测试文件。写入限于配置的导出目录；仅在服务开启时成立。**修复：**合法同源导出可写，异源或伪造 Host 一律拒绝；HTTP 回归确认原文件字节不变。

修复验证：最终全量守卫 756/756、相关执行回归 16/16、Windows 正式与 Dev 真编译通过。隔离部署哈希一致，20 个验收标识只在 Dev DLL 出现。实包水域遮罩与测试输入逐像素一致，另查了两包着色器、导航和音效。

待实机：Dev 专用槽按 `M_TIDEGATE_09`、`M_TIDEGATE_10` 验收，查看文字报告 `BossRushValidation_<runId>.log` 的 `TIDE_DRILL_TIDE_PHASES / SWIM / LIGHTHOUSE / ENEMIES / RESTORE`。四相快进已补足淘沙帮撤回窗口；实际 AI 站位、物理接缝和帧耗仍待实测。未启动游戏或访问玩家存档。

## 2026-11-05 潮闸镇生产水准复审（HEAD 4f9a1642 之后；017～028 当日全部修复；COMPAT，L1/L2，L3 待验）

owner「审核最新代码与 3D 场景是否达到生产水准、UI 交互逻辑特效够不够精致」，看完报告后「开始全部修复」。在 001～016 已修复的基础上补审：通读 `Tidegate/`、`Integration/Tidegate/`、`Utilities/RaidScenes/`，作者工程四个自研着色器与构建器，导航 / 碰撞 / 布局导出数据与作者预览图；与 001～016 及其未证实线索不重复。修复与验证见 `FIX_TRACKER.md` 同日节。没有启动游戏、没有访问存档。

### CR-2026-11-05-017 · P1 / COMPAT · 潮汐换相、渡口来船、暗渠露出全程没有提示

- 状态：Fixed（2026-11-05），L1 + L2，L3 待验。
- 位置：`Tidegate/TidegateTide.cs` 的 `PhaseChanged` 原先全仓零订阅；会话只刷 HUD 文本。owner 认可的原型有涨潮前 15 秒警报、每相字幕与「渡口的船要走」提醒（`docs/design/tidegate-town/v2/js/game.js:180-196`）。
- 影响：涨潮只有 30 现实秒，滩涂约 16 秒后变深水；背重的人在滩涂 / 沉船搜刮时没有任何预警就下沉缺氧；渡口与暗渠开合都没有提示。
- 修复：会话订阅并在清理时退订 `PhaseChanged`，每相出字幕（涨潮警示色加警报、高潮在渡口拉汽笛）；纯规则 `TidegateTideRules.NoticeAt` 给出低潮最后 15 秒涨潮警报、高潮最后 12 秒渡口要走，会话每轮各报一次（执行回归覆盖「每轮恰好一次」）。

### CR-2026-11-05-018 · P2 / COMPAT · 潮位读数放在 HUD「目标」行，读数一变就触发「目标更新」

- 状态：Fixed，L1 + L2，L3 待验。
- 位置：会话每 0.5 秒 `SetObjective(tide.Describe(...))`，文本含一位小数的潮位与 m:ss 倒计时；`SkyIsland/SkyIslandHud.cs` 值一变就挂「目标更新」眉题（停 6 秒）、闪强调条、目标行淡出淡入。
- 影响：带着潮汐怀表时整趟出击眉题常驻、强调条每秒闪、目标行每秒换字；潮闸镇也没有真正的目标引导。
- 修复：目标行改成按进度变化的引导（还剩哪几处阀门 → 带手轮去潮位站 / 手轮在挖沙船 → 泻湖放水中 / 金库露出水面 / 又被淹了 → 带怀表回去交给闸伯）；`TidegateTide.Chip` 把潮相、潮位与怀表倒计时放小标行，负重、游泳与出口同行；没带怀表的人在落地横幅里看到怀表的用处。

### CR-2026-11-05-019 · P2 / COMPAT · 海侧地图边界碰撞顶面 0.307 m，低于高潮水位 0.45 m

- 状态：Fixed，L1 + L2，L3 待验（游不游得出去要实机看）。
- 位置：`ArtSource/Tidegate/collision_boxes.json` 海侧墙（three.js `[-89,-3.5,69]→[89,0.307,70]`）被构建器原样照搬；官方游泳把脚底压在水面下约 0.5 m（`CharacterMainControl.UpdateSwim`）；外海水体铺到 ±170 m，旧兜底要 |z| > 150 才拉回。
- 修复：`TidegateWorld.AddBoundary` 在 `ApplyLayers` 里沿可玩区（180 × 140 m）四边补墙层碰撞（y −12…+6），不重打场景包；越界兜底改为可玩区外 8 m（`OutsidePlayArea`）。

### CR-2026-11-05-020 · P2 / COMPAT · 灯塔点光只照到角色，照不到地面与建筑

- 状态：Fixed，L1 + L2，L3 待验。
- 位置：自研着色器在 GBuffer 里 albedo 置 0、`kLightingInvalid`，延迟光源对地面与建筑的贡献为 0，本图只认 `_TidegateLamp*` 灯数组；`TidegateDevices.LightBeam` 的注释却写「照亮周围的角色与地面」。
- 修复：灯塔点亮后灯头进灯数组第一格（半径 16 m、暖色，白天 35% 亮度）；入夜（夜色 ≥ 0.25）拿着带手电的枪时，瞄准方向前 3.5 m 每帧放一盏近似手电的灯（owner「全部修复」即接）。夜灯最多 14 盏、留一格给手电。注释改正。

### CR-2026-11-05-021 · P2 / COMPAT · 全图没有任何声音

- 状态：Fixed，L1 + L2；音效没人耳试听，L3 待验。
- 修复：新增 `Tidegate/TidegateSounds.cs`（与 `SkyIslandAmbience` 同一套 `PostCustomSFX` / `StopAll` 反射绑定）：码头、滩涂、栈桥、灯塔岬岸边的海浪循环（38 / 44 m 滞回）、栈桥浮标钟、涨潮警报、来船汽笛、阀门、大闸、灯塔点亮。文件由 `tools/gen_tidegate_sfx.py` 程序化生成（标准库，`--check` 校验格式、峰值、首尾与循环接缝）进 `Assets/Sounds/Tidegate`（local-only），正式编译的声音目录自检加 `Tidegate`。

### CR-2026-11-05-022 · P2 / COMPAT · 地表贴图 2–4 m 一铺、不旋转不偏移，一屏能看到约十次同样的裂纹

- 状态：Fixed，L1 + 作者预览前后对比目检，L3 待验。
- 修复：作者工程 `TidegateTerrain.shader` 每层按权重 `[branch]` 采样（梯度分支外算好），水泥、沥青、泥、岩石、海床走 Inigo Quilez「texture repetition」第三法去平铺；铺路石有砖缝，不偏移；整体叠 10–15 m 中频明暗，草地按 30–40 m 一块偏黄。重打 `tidegate_raid`（`fbee4ab5…`），两包着色器判包与 79 包资源校验通过。

### CR-2026-11-05-023～026 · P3（均 Fixed，L1 + L2，L3 待验）

- **023 观感**：水厂沉淀池平涂像绿色地板 → `TidegateEnvironment` 加静水 `_Ripple`（细波纹法线 + 天光），构建器只给 `water{...}` 材质开；水面不接阴影 → `TidegateWater` 接主光阴影（透明按阴影贴图、单级阴影顶点算坐标）；镇外大片同色草地 → 地形着色器草地黄斑。外圈地形没加起伏（要回 three.js 改导出，游戏镜头只看得到边界外约 14 m，那一带已有散布）。
- **024 交互反馈**：走近被淹的箱柜提示一次「等退潮」；潮位站缺手轮写进交互名（交互名随状态变化才重注入）；阀门交互名括号随语言；读信写不进存档时提示稍后再读，不说竹筏出现了。
- **025 一致性**：HUD 地名按 24 m 到访半径与 4 m 滞回；「带着怀表」HUD、交表（`FindCarried`）、竹筏交表选项（半秒缓存）与金库同按整棵物品树。
- **026 维护**：剧情箱柜改成一张表 `TidegateLoot.StoryContainers`，`tools/build_tidegate_navigation.py --check` 逐条核对布局 `fixed` 标签（两个方向都报）；原型「小金条」落为沉船船长保险柜固定多一件金库档值钱物（过价值上限与黑名单）；两处 XML 注释归位；在建角色改计数器；`guard` 按天空岛精英档配枪；游泳时 `VisableDistanceFactor` ×0.5（原型「游泳时敌人发现距离减半」）。Wiki 中英同步。

### CR-2026-11-05-027 · P2 / OPERATIONAL · `4f9a1642` 让 `LedgerSizeGuard` 转红

- 状态：Fixed（2026-11-05），L2。
- 位置：本文件提交时 1218 行，超过 1200 行上限；该提交的 `FIX_TRACKER.md` 写全量守卫 753 PASS，跑在追加台账之前。
- 修复：按 owner「满了立刻归档」把 13 个已闭环节逐字搬进 `archive/CODE_REVIEW_FINDINGS_2026-10.md` / `_2026-09.md` 并补索引，核对零丢失。

### CR-2026-11-05-028 · P2 / COMPAT · 潮闸镇没有预热官方物资池与敌人武器池

- 状态：Fixed，L1 + L2，帧时间未采样。修复期间发现。
- 位置：天空岛进岛读条时 `SkyIslandLootPools.Prewarm()`（顺带武器池，CR-2026-09-14-014）；潮闸镇的箱子同样走 `SkyIslandLootPools`，却没预热，第一只箱子（出生点 60 m 内，落地 0.5 秒就建）那一帧现查官方物品表；守卫配枪后武器池也一样。
- 修复：导航扫描后、放行官方初始化前逐帧预热（读条里），失败只记一次、箱子照常懒建。

### 未证实线索（UNVERIFIED，要实机）

- 竹筏在基地下水道用自研 Environment 着色器：吃 URP 主光与固定天光，不吃下水道的点光与烘焙光，也照不到自己的马灯，可能比周围亮或平（人工清单 `M_TIDEGATE_08` ⑨）。
- 性能没采样：1209 个渲染器、181 个材质、不开静态合批；Environment 着色器对所有不透明件 `clip()`，整镇失去 early-Z。地形改成按权重分支采样后平均采样数应下降，去平铺层多一次采样，同样没采样。
- 灯塔光束白天和夜里一样亮（`TidegateBeam.shader` 不读 `_TidegateNight`）；交表选项挂在竹筏交互组里，不在 6.6 m 外的闸伯身上。
- 程序化音效与手电近似灯（点光、不是光锥）的观感没人看过、听过。

## 2026-11-05 潮闸镇提交 bea44008 全面审查（16 项已当日修复；COMPAT，L3 待验）

owner「全部审核一遍，确保没问题把代码提交了」。按三块分给三个独立审查（出击基础设施与会话、局内玩法运行时、剧情 / 物品 / NPC / 数据），只读、各自对照 `鸭科夫源码/` 与调用链；结论由主会话逐条复核代码后才登记。另有主会话自查守卫与资源校验脚本的改动（未发现放宽断言）。修复与验证见 `FIX_TRACKER.md` 同日节。没有启动游戏、没有访问玩家存档。

### CR-2026-11-05-001 · P1 / COMPAT · 闸伯的信没读就丢了，这个档永远进不了潮闸镇

- 状态：Fixed（2026-11-05），L1 + L2，L3 待验。
- 位置：`Tidegate/TidegateStory.cs` `TryDeliverLetter`（送达即记 `LetterDelivered`，此后永不再送）与 `ReadLetter`（全仓唯一写 `LetterRead` 的地方）；信的配置器只加了 `Special`，官方 `Item.CanBeSold` 只看 `NotSellable`（`鸭科夫源码/ItemStatsSystem/Item.cs`）。
- 触发与影响：快递进仓库后、读信前把信卖掉 / 扔掉 / 带进出击死在外面，竹筏、闸伯、日志、怀表与两件随身装备的获取全部永久锁死，没有恢复途径。
- 修复：送过没读、而身上（整棵物品树）、基地仓库、快递缓冲都找不到信时，阿稳补寄一封（每个官方游戏日最多一次；仓库不在时不判「没有」）；信加 `NotSellable`。不加存档字段。

### CR-2026-11-05-002 · P2 / COMPAT · 在竹筏边结婚的闸伯，玩家一走出下水道就被删掉

- 状态：Fixed，L1 + L2，L3 待验。
- 位置：`Tidegate/TidegateZhabo.cs`（生成后挪进下水道子场景、`Despawn` 不看婚姻）；婚姻系统取的是登记表里同一个实例传送去教堂。
- 触发与影响：竹筏边给戒指 → 婚礼后配偶仍在下水道子场景、仍归本模块；走出下水道时子场景卸载或 `ClearEntry → Despawn` 把配偶销毁并注销，要到下次进基地才由婚姻恢复补回。天空岛居民同类问题早有婚后跳过（`SkyIslandResidents.Dispose`）。
- 修复：`ReleaseIfMarried`：结婚后交给婚姻接管、`MultiSceneCore.MoveToMainScene` 挪回主场景、不回收；模块每秒检查一次，`Despawn` 先走它。

### CR-2026-11-05-003 · P2 / COMPAT · 三个刷怪点落在车辆 / 设备的实心碰撞盒里

- 状态：Fixed，L1 + L2，L3 待验。
- 位置：`Tidegate/TidegateEnemies.cs` `Spawn`：只按地面层射线贴地，射线穿过墙层的 Prop 盒落到盒底；`ArtSource/Tidegate/README.md` 本来写着「运行时要 GetNearest」。
- 触发与影响：`SPAWN_town_scav_0`、`SPAWN_ww_guard_7`、`SPAWN_docks_scav_11` 的敌人出生在碰撞盒内，被挤出或卡住。
- 修复：先吸到本图 A* 最近的可走面（新增 `ArenaPrototypeNavigation.TryNearestWalkable`，只看本租约图），再贴地。

### CR-2026-11-05-004 · P2 / COMPAT · 撤离回基地时「刷新基地环境光」一直被跳过（天空岛同病）

- 状态：Fixed，L1 + L2，L3 待验；天空岛那处不是本提交引入，结构相同一并修。
- 位置：`Tidegate/TidegateRuntimeModule.cs` 与 `SkyIsland/SkyIslandRuntimeModule.cs` 的 `OnLevelReady` / `OnUpdate`：以「会话组件还在」判断仍在出击。撤离返航时组件要等 `ReturnToBase` 走完（基地就绪之后）才销毁，`OnAfterLevelInitialized` 那一刻它还在。
- 影响：`DynamicGI.UpdateEnvironment()` 只在阵亡返航时执行；天空岛 SI-A 实机清单要的「返航进基地光照」日志在撤离时根本不会出现，误导排查。
- 修复：会话加 `IsClosed`；已收尾的会话按「不在出击」算，挂起标记也只在会话未收尾时置位。

### CR-2026-11-05-005～016 · P3

- **005** 交表谢礼走岛上口径「背包满了落地」，在基地会掉在下水道地上，发放失败也照说「塞给你」。→ 基地改 `ItemUtilities.SendToPlayer(item, false, true)`（满了进仓库），失败时换一句提示。
- **006** 潮汐怀表（6000 / 品质 5）每趟金库必出，和「独一份纪念品」的定价理由冲突（经济，按「好玩优先」拍板）。→ 身上已经带着一块时不放；仍是三阀一闸 + 等低潮这条主线的回报。定价注释与 Wiki 同步。
- **007** 剧情存档进写屏障时 `Load()` 是全 false 默认值，图鉴镜像会把六条官方图鉴条目全部收回。→ 存档不可读写时不同步，镜像保留原状态（口径同天空岛）。
- **008** `docs/contracts.md` §3 漏登记 `BossRush_Tidegate_Story_v1` 与图鉴键前缀 `BossRushTidegate_`。→ 补登字段、矛盾态与 NoteIndex 例外。
- **009** Wiki：「四处撤离点都在地上画着光环」（实际只有当前能走的亮环）、「背包总重 14 kg」（实际是含装备的全身负重）。→ 中英改正，并补写「信不能卖、丢了会补寄」「带着怀表时金库不放」。
- **010** 箱子旁 4 m 的保护同时挡住了「水退放出」，站在旁边等退潮的人要先走开再回来。→ 只在收起时看 4 m。
- **011** 灯塔引怪把 `forceTracePlayerDistance` 抬到 80 m 后整趟不还原；沙老大写死 40 m。官方 AI 在这个距离内无视视线每帧锁定玩家（`AICharacterController.cs`），等于透视（按「好玩优先」拍板）。→ 引怪与全图显示敌人同为 40 秒、到时还原官方原值；沙老大不再改，选敌交给官方感知。
- **012** 沙老大也会被潮水卷走，官方 Boss 掉落跟着消失。→ Boss 不卷。
- **013** 撤离读条只走官方控件，控件缺失或反射字段对不上时屏幕上没有任何读秒（天空岛有 HUD 兜底）。→ `Show` 返回 false 时 HUD 文字读秒。
- **014** `ArenaPrototypeNavigation.SetWalkable` 每次给全图约 3900 个节点重写 `Walkable`；官方带的 A* 里这个 setter 无条件把节点交给分层图重算（主会话核对过 IL），涨落期间每秒一次。→ 只写变了的节点、有变化才标图脏。未采样帧时间。
- **015** 泻湖放水与湿痕用 `Time.unscaledDeltaTime`，暂停与拍照模式下潮位停了、泻湖照样放干。→ 用会话传入的游戏帧时间。
- **016** 闸伯第一次生成失败（官方生成返回空）后不再重试，直到下次进下水道。→ 竹筏在时每秒补一次，失败退避 30 秒。

### 未证实线索（UNVERIFIED，未修）

- `SetWalkable` 与 `SetBlockedAreas` 共用一份原始可走性，后调的覆盖先调的封锁；潮闸镇只用前者，暂无触发。
- 主角基础 `CanSwim` 若本来 > 0.01，负重门槛失效、HUD 也显示错；基础值在资源数据里，未核实。
- 防水袋若被官方 `SendToPlayerCharacter` 先 `TryPlug` 进角色槽位，就不在背包顶层，加成不生效；取决于克隆源标签。
- 闸伯固定在 y = 0.02，没贴地；礼物与装备 ID 未离线核对。
- 卷走用物理地面射线、导航封锁用逻辑顶点高度，两者 p95 差 0.49 m；桥面下方射线会打到上方桥面。
- 宠物背包里的手轮 / 防水袋 / 鸭蹼不算「带在身上」（设计口径是背包顶层）。
- `TidegateSession.OnSceneUnloaded` 可能把已卸载场景句柄写回缓存；两张图登记后未登记的 bundle 场景每次多读一遍 `scene.path`。
- `ForceSpawnAtAsync` 等待期间若别的 owner 先登记了闸伯，本模块会把它当成自己的。

## 2026-10-29 HEAD a8aed728 全面复审（4 项已于 2026-10-30 修复；COMPAT，L3 待验）

起点工作区干净，审查对象为 HEAD `a8aed728`。逐调用链复核 `41360aa4..a8aed728` 最新改动及其直接影响面，同时抽查未改动大模块的关键入口、退出、异步回调和存档边界。覆盖共享持久层、遗种巢、成就、好感、野外趣事、霜之哀伤、F3、标准竞技场、Mode D–H、丧尸、征程、天空岛和随机事件；全仓守卫与已有执行回归均运行。大模块采用关键路径抽查，不表示所有源码逐行或所有状态组合穷举。

2026-10-29 仅审查与登记；2026-10-30 按 owner「全部修复」完成以下四项生产修复及回归。下面保留原始触发与审查证据，不将历史报告作为确认依据，也不表示四项均由最后一个提交引入。没有提交、部署到真实游戏目录、启动游戏或访问玩家存档。

### CR-2026-10-29-008 · P1 / COMPAT · 零到账的远征补发失败仍可阻塞出击中的崽包保存

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；CR-2026-10-29-007 修复后的残余分支。
- 位置：`PetNest/PetNestExpeditionService.cs:585-590`、`:629-637`；后续阻塞在 `PetNest/PetNestSaveCoordinator.cs:148`。
- 触发：基地里有已结算欠奖，现金已经到账，但剩余物品的真实 prefab 暂时不可用。补发先调用 `RequireAssetSnapshot` 登记必须采集基地仓库的义务，实际没有调用现金或物品交付；无进展分支只提交诊断计数或中止事务，既不申请物理保存，也不撤销本次空义务。
- 证据：链接真实远征服务、Bundle、崽包 Store、共享保存引擎和协调器的隔离探针确认 `Adds=0`、无物品交付、`HasDeferredFlush=False`。额外模拟官方 `LevelManager.NotifySaveBeforeLoadScene` 的 Collect + SaveFile，物理写成功一次，义务仍保留；切到没有 `PlayerStorage` 的出击场景后，崽包前置检查和 Store 均成功，`RequestAssetFlush` 返回 `asset_save_not_ready:storage_instance`。官方 SaveFile 没有通知该协调器清义务，`PetNestPersistence.HandleCollectSaveData` 也只采集/flush，因而常规切图保存不能消除此状态。证据：`Build/review-head-20261029/persistence-transition/result.log`；原始探针 `Build/review-persistence-20261029/probe.log`。
- 影响：一次未到账的奖励重试把后续崽包转移绑定到出击图不存在的基地仓库，造成保存失败并反复提示，符合此前修复想消除的阻塞类型。
- 建议：补发持有 `AssetSnapshotToken`，在确认尚未调用任何资产变更 API 的失败路径撤销本次新建义务，或在基地妥善结清该批次。不能单凭奖励游标没有前进就撤销，外部通知可能在资产已变更后抛异常；已有真实欠账和重入的新义务必须保留。
- 修复：持有本次 token，以 `ref assetDeliveryStarted` 在现金/物品外部 API 调用前置位，`finally` 仅在投递尚未开始时撤销。缺 prefab、实例化/身份/盖章失败及计数封顶不再遗留空义务；旧 owner、重入 owner 和可能已变更资产的异常路径保留。ContentTransactions 新回归在旧代码先红、修后 698 条断言通过；两项同步守卫的 6 个隔离反向探针均命中并按字节还原，证据 `Build/fixes-20261030/content-before.log`、`content-after.log`、`asset-guard-negative-results.json`。
- 验证需求：补齐“现金已领 + 物品 prefab 缺失 + 官方切图前保存成功 + 出击崽包转移”回归，同时保留部分发奖、外部通知抛错、已有义务与重入 owner 的保护。L3 在专用测试档模拟缺资源后进出基地，观察崽包转移是否仍报仓库未就绪，并确认重载后的资产一致性。

### CR-2026-10-29-009 · P2 / COMPAT · 宝箱扑跳落地冲击忽略墙体遮挡

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；当前既有缺陷，与已修的炮弹撞墙分支不同。
- 位置：`Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:395-396`。
- 触发与影响：扑跳预警锁定落点后，玩家移动到附近薄墙另一侧，只要仍在落地半径内且高度差小于 2.5 米，就会被直接扣血。落点自身的净空检查不覆盖整个冲击半径，合法落点也能出现这一条件。
- 证据：真实 Encounter、Rules、官方 HealthSimpleBase / DamageReceiver 隔离执行，落点 z=12、玩家 z=14，墙体位于两者之间；未投喂时身位半径 1.1 米、冲击半径 2.4 米，墙距落点 1.5 米不妨碍落点净空。遮挡查询分别返回无遮挡/有遮挡，当前实现两种情况均造成 26 伤害。只在隔离副本补 `Unblocked(leapTo, p)` 后，无遮挡仍伤 26、有墙伤害为 0。真实 PhysX 未执行。证据：`Build/review-integration-20261029/pounce-evidence.json`、`pounce-current.log`、`pounce-occlusion-counterfactual.log`。
- 建议：扑跳伤害结算复用舌击和炸弹已有的 `Unblocked` 门控，保留落地视觉、伤害值、半径和后续连招。
- 修复：落地伤害在原半径/高差判定后增加 `Unblocked(leapTo, p)`。执行回归覆盖薄墙分割冲击范围、无遮挡伤害、半径和高度边界；旧生产先红，4 个隔离反向探针验证承重判据。
- 验证需求：覆盖墙前/墙后、冲击半径边缘、落点有效但冲击区域被墙分割的情形；L3 在薄墙拐角观察扑跳预警后躲到墙后是否仍掉血。

### CR-2026-10-29-010 · P2 / COMPAT · 烟花试用品异步达标后遗漏完成通知和借用收尾

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；当前既有缺陷。
- 位置：`Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs:93`、`:117-125`；仅有的成功收尾在 `:369-373`。
- 触发：巨型烟花筒（GrandFinale）的主弹或子弹在后续 `OnUpdate → TickFinales → TryHurt → ObserveHit` 中达到目标，规则立即变成 `Completed=true / Active=false`。OnUpdate 随后从 `!rules.Active` 分支返回；成功的 `EndLease(false)` 和完成通知仅存在于同步的 `OnShot` 尾部。再次开枪又被 `IsHeld()` 内的 Active 条件提前拦截，不能补做收尾。
- 证据：从生产文件逐字抽取 OnUpdate、OnShot、LaunchFinale、TickFinales、Explode、TryHurt、EndLease 等方法，链接真实 Rules，用隔离宿主/物理/视觉替身执行。真实烟花爆炸达成 10 次命中后，`completed=True, subscribed=True, borrower=True, rig=True, notifications=0`；再开一枪并推进 200 秒仍保持同样残留。证据：`Build/review-head-20261029/finale-completion/result.log`。现有 `FieldAdventurePrototypeRuntime` 只验证射向/后坐，并将 FireSpecial 替身化，已有通过结果不覆盖此路径。
- 影响：试验已完成却无达标提示，枪上试用品模型和借用事件保持到换枪/切图等后续操作；试验台仍可按 Completed 状态领奖，未证实奖励丢失。
- 建议：成功收尾由一个幂等入口负责，同步射击和异步爆炸达标均调用，执行退订、清借用引用、收模型与一次性完成通知，同时保留已发射烟花的尾效。
- 修复：同步 OnShot 与 TickFinales 后共用 `CompleteLeaseIfReady()`，在 Active 早返前通过 lease owner 幂等收尾并通知；保留在途烟花的 Shooter 与尾效。新增 FieldAdventurePrototypeCompletion 执行真实方法，覆盖主弹、子弹、收枪后达标、重复 Tick/开枪、规则领奖、暂停、取消与超时；旧生产先红，81 条断言与 4 个隔离反向探针通过，真实奖励交付仍待实机。
- 验证需求：覆盖主弹达标、子弹达标、达标后不换枪等待、再次开枪、领奖和切图；L3 观察完成提示及手持模型是否及时撤去。

### CR-2026-10-29-011 · P2 / COMPAT · F3 数字输入会误触发录制快捷键

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；只影响 Dev 构建。
- 位置：`DebugAndTools/PromoRecording/PromoRecordingDirector.cs:102`、`:127-133`。
- 触发与影响：录制模式保持运行时打开 F3 的物品/金额输入框，用小键盘输入数字。HandleKeys 无条件先于暂停门执行，原始 `Input.GetKeyDown` 也不检查官方输入阻塞或输入框焦点：输入 3 会切换拍摄项，0 发起三个靶子生成，1 销毁录制组件。F3 的 `InputManager.DisableInput` 只登记官方输入阻塞源，不会截获 Unity 原始按键查询。
- 证据：完整生产 PromoRecordingDirector.cs 配现有生命周期夹具的宿主替身，将 `IsGamePaused` 设为 true 后驱动实际 Update：数字 3 令 index 从 -1 变 0，数字 0 产生 3 个敌人请求，数字 1 销毁导演。证据：`Build/review-head-20261029/recording-input/result.log`。输入焦点/实际 UI 尚未 L3。
- 建议：处理录制快捷键前检查 F3/模态输入与文本框焦点，暂停菜单中不消费拍摄操作；保留正常录制时的小键盘兼容和录制自身暂停键。
- 修复：快捷键统一检查官方暂停、timeScale、模态租约、官方输入和 legacy/TMP 焦点，录制自身 paused 不屏蔽 Home/7 恢复。真实 Update 回归在旧代码先红、修后 319 条断言通过，7 个隔离反向探针确认各门及恢复键；证据 `Build/fix-recording-input-20261030/`。
- 验证需求：录制中分别在 F3 金额、数量和属性输入框键入含 0/1/3/7/9 的数值，确认不会刷怪、切项或结束；关闭输入界面后快捷键照常生效。

2026-10-30 修复验证：全量守卫 752 PASS / 0 FAIL；Windows 正式/Dev 隔离真编译通过，独立复核无新增 confirmed issue。全量回归首轮 148 PASS / 1 组视觉夹具超时；单独延长该次等待重跑，真实 Unity 基线和 6 项原生反向验证通过，最终 149 组均有本轮通过证据，是多轮合并。原日志、各次结果、合并清单和源码/DLL 哈希证据见 `Build/fixes-20261030/`，详细修复与 FIX30-01～04 实机步骤见 `FIX_TRACKER.md`。未取得 L3，未提交或部署。

2026-10-29 审查验证：

- `python tools/run_guards.py --verbose`：752 PASS / 0 NEW-FAIL / 0 KNOWN-RED，未使用 source-only；日志 `Build/review-head-20261029/guards.log`。
- `python tools/run_runtime_regressions.py --jobs 4`：148 组 PASS / 0 FAIL；汇总 `Build/review-head-20261029/regression-results.json`，日志 `regressions.log`。新增审查探针单独执行，现有套件全绿不覆盖上述四项遗漏。
- Windows 官方脚本正式和 Dev 构建均退出 0，使用本机游戏 Managed 的独立拷贝。只有 ModeHUIPages 两个既有 CS0649 警告。隔离目录没有 Mod 子目录，脚本的自动复制未完成，本轮不将其记为部署或发布资源验证。日志 `Build/review-head-20261029/compile-release.log`、`compile-dev.log`；两种 DLL 分别保留，`Build/BossRush.dll` 已还原为本轮正式产物。
- `check_dll_identifiers.py`：正式 DLL 不含所检查的 14 个 Dev 专用标识，Dev DLL 能检出全部 14 个，正反对照均通过。
- 未取得 L3：真实输入栈、场景物理遮挡、过图存档时序和运行帧耗仍需游戏内验证。审查探针模拟宿主，不视作实机证据。

## 2026-10-29 最新代码审核与遗种崽反馈（COMPAT；L1/L2 修复，L3 待验）

本轮从干净工作区、HEAD `f9ef9fd4` 开始，审查 `41360aa4..HEAD` 的最新变更及直接影响面，并沿玩家反馈回查遗种巢、共享持久层和旧物理保存入口。三位子代理均为 gpt-6.1-sol / xhigh。覆盖宿主生命周期、存档与本地化、Mode D–H / 标准竞技场 / 丧尸 / 征程 / 天空岛 / 随机事件的相关状态路径、Integration 野外趣事与装备、F3 录制；未改动的大模块做重点链路抽查与全量已有回归，不声称全部源码逐行或状态组合穷举。

以下七项均已修复，兼容分类 COMPAT，证据为 L1 生产调用链与 L2 执行回归；未发现新增 P0，未取得游戏内 L3：

- **CR-2026-10-29-001，P1：已有崽的默认名泄漏 Cname key。** `PetNest/PetNestService.cs:428` 原来在血脉被当前 Boss 筛选器排除、目录尚未建成时直接返回 `lineageKey`；`PetNest/PetNestLineageCatalog.cs:33` 又缓存构建当时的本地化结果，官方缺词条的 `*Cname_*` 也被当作有效名字。现按取用时的语言解析，即使目录缺项仍查询名字；没有词条时显示双语“未知血脉”，保留稳定血脉 ID 与玩家自定义昵称。`PetNestLineageNames` 14 条断言覆盖语言晚就绪、切语言、筛选缺项、目录未建与昵称；两项隔离反向探针均在预期断言失败。玩家入口与报告吻合，但实际玩家包和日志尚未取得。
- **CR-2026-10-29-002，P1：部分 Mod 保存入口失败后可把全局保存状态永久留在忙碌。** `ModeG/ModeGPersistenceFlushCoordinator.cs`、`ModeG/ModeGProfilePersistence.cs`、`ModeG/ModeGNemesisPersistence.cs` 的三个物理入口，以及 `Integration/Affinity/AffinityManager.cs:151` 的关停保存，原来直接调用官方 `SaveFile`。官方 `SavesSystem` 先置 `saving=true` 再备份/写文件，异常时没有 finally；后续遗种崽背包、共享存档等都被 `IsSaving` 拦截，可能贯穿基地与出击。现四处复用既有 `BossRushSaveFileThrottle.RunSaveFile`，只复位本调用留下的闩，保留原异常、已有缓存及各模块原有故障语义，不重入或释放他方保存。`ModeGPersistenceRecovery` 14 个独立进程场景 / 86 条断言、`NpcAuditFixes` 关停失败回归先红后绿；三个 Mode G 旧调用反向探针和相关守卫反向验证均通过。该缺陷需要先发生物理保存异常，不能仅凭玩家提示认定本次报告就是此因。
- **CR-2026-10-29-003，P2：崽包把隐藏的官方宠物容器也当作必须就绪的转移来源。** `PetNest/PetNestSaveCoordinator.cs:79`、`:116` 原无条件检查 `PetProxy.PetInventory.Loading`，与官方 `LootView.OnOpen` 只在 `LevelConfig.SavePet=true` 展示该容器不一致。现前置与采集共同采用官方可见条件；显示中的官方宠物包仍须加载完成并同批采集。`PetNestCare` 51 条断言及恢复旧门控的隔离反向探针通过。此为条件性缺陷，正常关卡隐藏容器默认不加载，不把它说成玩家“基地和出击都持续报错”的充分根因。`PetNestBackpack.cs:373` 另按原因变化记录一次 Warning，区分保存中、槽位、主角、角色物品、官方宠物包；用户提示仍经原本地化前缀解析。
- **CR-2026-10-29-004，P2：宝箱投喂未遵守“选最便宜”承诺。** `Integration/FieldAdventures/Mimic/GreedyMimicInventory.cs:49` 原先按品质优先，低品质高价和高品质低价同类食物同时存在时会吃掉更贵者。现价格优先，同价才用品质排序。真实生产 Rules 夹具修前失败、修后 1417 条断言通过，12 项隔离负探针包含恢复旧品质排序；仍保留锁槽、装备、配件和非空容器保护。L3 核对不同品质/价格食物的实际消耗。
- **CR-2026-10-29-005，P2：宝箱炮弹可在同帧隔墙扣血。** `Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:432` 原先先判玩家命中，再以 else-if 查墙；本帧弹道同时越过墙和墙后玩家时墙判定完全不执行。现 `GreedyMimicTerrain.cs:41` 返回最近墙点，先截短有效弹道，再判墙前玩家，并阻止命中半径跨过墙面。`FieldAdventureMimicRuntime` 生产逻辑与官方伤害组件隔离执行：修前墙后玩家仍受伤，修后 271 条断言、3 项隔离负探针通过，含墙前/墙后/同面和半径边缘。真正场景的 PhysX 墙层仍需 L3。
- **CR-2026-10-29-006，P2：Dev 录制的迟到角色生成越过结束和场景边界。** `DebugAndTools/PromoRecording/PromoRecordingDirector.cs` 的靶子 await 后原无 owner 校验，花冠旧回调还能重写清理后的静态引用。现绑定请求代数、原主角与普通出击场景，开始/换步骤/结束/销毁取消在途请求，花冠生成期间预留，拒绝并回收迟到角色；已提交的拍摄角色仍按原场景生命周期运行。`PromoRecordingLifecycle` 完整生产文件 65 条断言、旧生产及四项隔离变异均在预期断言转红。仅影响 Dev，正式构建不含该录制实现。
- **CR-2026-10-29-007，P1：无奖励的基地维护留下仓库保存义务，阻塞之后的崽包转移。** `PetNest/PetNestExpeditionService.cs:572` 原先在检查欠奖、重试间隔和事务可用性之前调用 `RequireAssetSnapshot`。空扫描没有资产变化，也不请求物理保存，却遗留必须采集基地仓库的标志；进入没有 `PlayerStorage` 的出击图后，崽包转移落盘被该标志拦住。现只有真实欠奖、已过重试间隔且取得事务时才建立义务。`PetNestHatchService.cs:128` 同类失败分支也已修复：先核对蛋的归属，未摘取或确认完整回滚时凭本次 owner 撤销新建义务；已有真实欠账、重入中新建的义务和不完整回滚均保留，回滚不得覆盖原槽新物品。`ContentTransactions` 旧实现先红、修后通过，覆盖基地空扫描后出击实际崽包快照落盘、退避、拒绝事务、旧债、部分发奖及孵化回滚。真正开始外部到账后不凭奖励游标未变就清除义务，避免宿主通知抛异常后漏存已改变的资产。无 L3，不据此认定玩家在基地的持续阻塞已完全解释。

CR-2026-10-29-002 同因入口已继续补齐：`DebugAndTools/F3GameplayValidationRunner.cs` 四处、`F3GameplayValidationAutotestStory.cs` 两处、`F3GameplayValidationEconomy.cs` 与 `F3GameplayValidationPersistence.cs` 各一处，均复用同一保护器，保持回读和快照回滚顺序。新增 `AuditModeLifecycle/f3_save_latch` 执行八个生产方法与真实保护器，50 条断言通过；八次执行反向验证及八次守卫反向验证均准确转红并还原（`Build/F3SaveLatch-negative/results.json`）。这八条为 Dev 验收入口，不将它们推断为玩家运行正式版时的原因。

玩家反馈的归因边界：名字有明确的本 Mod 展示缺陷；背包已确认三类可阻塞路径，007 在正常基地维护到出击的链路中已离线复现。玩家补充“基地和出击地图都会出现”，仍需实际运行包和日志确认完整归因。本机只有 2026-10-24 的旧 Player.log，未含对应错误，且不是玩家日志。需要修复包复现后的 `[PetNest] 背包暂不可用: asset_save_not_ready:<原因>` 及其前置异常判断实际命中哪个条件；不为消除提示强行清宿主保存状态或跳过资产采集。

完整验证、构建产物和哈希见 `FIX_TRACKER.md` 同日记录。人工验收记录建议文件名 `BossRush_Audit_20261029_smoke.txt`，以下为手工 ID，不是新增 F3 自动步骤：

- `AUDIT-29-01`：重进游戏，在遗种巢列表、出战 HUD、改名框核对默认名；改变 Boss 筛选并切换中英后重开面板。出现 Cname / 星号 key、旧语种锁死或玩家昵称变化即不合格。
- `AUDIT-29-02`：没有远征奖励待领取时在基地停留至少 5 秒，基地闲逛崽和随后出击的随从各按 F 打开背包，存入/取出一件普通物品，关闭后重开并正常切图。持续“准备中”、丢失/重复物品或正常就绪仍不能开启即不合格；复现时提交 Player.log 中上述新诊断及前面的保存/物品加载异常。
- `AUDIT-29-03`：向贪吃宝箱投喂两种同类、品质与价格高低相反的食物，观察背包消耗；未选择低价者即不合格。
- `AUDIT-29-04`：分别站在宝箱炮弹与实体墙之间、墙后，观察血量；墙前正常命中，墙后隔墙扣血即不合格。
- `AUDIT-29-05`：Dev 按 F3 开始录制，在刷靶/花冠慢创建期间结束录制或返基地；加载后出现迟到敌人、重复花冠即不合格。正常结束录制已在场的拍摄对象不应被误删。

本轮未启动游戏、未访问玩家存档或部署；修复按 owner 要求纳入本地提交。未采集帧耗时；离线通过不代表玩家实机故障全部消失。

## 2026-10-25 全模块重点链路审查（COMPAT；L1/L2 修复，L3 待验）

本轮从干净工作区开始，四路并行检查宿主与共享持久层、各玩法模式、Integration 内容，以及世界/UI/音频链路，并交叉复核加载时宿主早返后的实际可达性。范围包括进入/退出、死亡、玩家销毁与替换、异步晚到、切图与切槽、重复回调、无效数值、交易和资源清理；这是按风险选择的生产链路审核，不代表全部源码逐行或全部状态组合已穷举。

以下问题均已修复，兼容分类为 COMPAT，证据为生产接线 L1 与隔离执行 L2。有效配置、已发布 key、TypeID 和存档 schema 保持兼容；没有游戏内 L3 证据。

- **CR-2026-10-25-001，P1：共享存档漏收切槽通知后可跨槽写入。** `Common/Lifecycle/BossRushSlotJsonStore.cs` 的 Store / FlushPending 原来只依赖读取入口检查槽位。官方 `SavesSystem.SetFile` 先切槽再调用普通多播事件，前置订阅者异常会截断后续通知；旧 pending 随协调器 flush 覆盖新槽。现两条写入口复核槽位，漂移时拒绝请求、清旧 pending 并通知 owner；首次直接 Store 也先加载 schema 并绑定槽位。`SaveFailureRecovery` 以真实 store 和内存宿主先复现红项，再验证通知中断、首次直接写入、高版本屏障和新槽恢复。
- **CR-2026-10-25-002，P1：危险试用品委托迟到完成可写入新槽。** `Integration/FieldAdventures/Prototypes/DangerousPrototypeCommission.cs` 的异步对话原未绑定原玩家、关卡与存档槽。现提交时复核请求所有权，并在加载开始、切图、切槽、玩家失效和销毁时取消；旧回调不能重复收尾新对话。`PrototypeCommissionLifecycle` 完整执行生产模块，67 条断言通过；忽略取消的迟到回调、加载时不调用模块 Tick、NPC 复位与退订均覆盖，三项隔离反向探针按预期转红。
- **CR-2026-10-25-003，P1：Mode D 的旧波次任务不绑定原玩家。** `ModeD/ModeDRuntimeModule.cs` 的 CaptureValidity 原只检查模式、波次及场景。死亡广播被截断、主角销毁或同图替换时仍可接受旧生成结果与自动开波。现捕获原玩家并检查身份和 Health；`AuditModeLifecycle` 在旧生产代码的死亡用例转红，修复后覆盖死亡、销毁、替换和重生后的旧请求失效。
- **CR-2026-10-25-004，P2：加载开始至 sceneLoaded 之间旧生成任务仍有效。** `ModeD/ModeDRuntimeModule.cs` 与 `WavesArena/WavesArenaRuntimeModule.cs` 的 CaptureValidity 补充 SceneLoader 加载门。根 Update 早返不会终止已运行的协程和异步生成，单靠场景代数更新过晚。`AuditModeLifecycle` 覆盖仅改变 loading 标志、场景和玩家仍相同的两模式窗口，旧 Mode D 实跑失败。
- **CR-2026-10-25-005，P2：Mode D 退出后可误结算第 0 波。** `ModeD/ModeDRuntimeModule_WaveResolution.cs` 的 TryResolveModeDWaveComplete 原可把退出后的空列表与零 pending 判为完成，迟到死亡回调可能改提示和路牌。现要求 modeDActive 且波次大于 0；真实入口的关停后再次结算用例先红后绿，现有连续波次回归仍通过。
- **CR-2026-10-25-006，P2：霜之哀伤慢创建被正常收招取消。** `Integration/Frostmourne/FrostmourneAction.cs` 原在 1.5 秒动作结束时无条件取消生成代数，冷加载超过动作时长便销毁合法召唤物。现区分正常完成和主动中断，管理器在离手、停用、死亡、加载与销毁时仍拒绝旧请求。`ManualEquipmentRecovery` 合计 72 条断言通过，恢复旧正常收势取消行为的隔离探针按预期失败。
- **CR-2026-10-25-007，P2：成就面板可跨加载或玩家失效残留。** `Achievement/AchievementRuntimeModule.cs` 与 `Achievement/AchievementView.cs` 统一检查关卡、主角与加载状态；View 自身 Update 负责关闭已开面板，独立于根宿主 gameplay gate。`AchievementRuntime` 执行真实宿主早退/模块分发及 View.Update，先证明加载时模块未被驱动，再证明面板自行关闭。实际 Unity 输入租约归还仍需 L3。
- **CR-2026-10-25-008，P2：无效 Boss 倍率污染生命与 AI 数值。** `Utilities/BossStatScaling.cs` 原直接使用零、负数或非有限倍率，可生成零生命并除零计算 AI 时序。现优先回退有效全局倍率，否则使用中性值 1；无效生命比例也使用 1。`BossHealthScaling` 在旧版零生命用例转红，修复后覆盖五类无效值，保留正常倍率叠加、护卫减半和生命比例语义。
- **CR-2026-10-25-009，P2：NaN 配置可拖住波次倒计时。** `Config/Config.cs` 的两个休息时间 getter 原仅用大小比较钳制，NaN 可穿透。现 NaN 沿用本地默认值，有限值与正负无穷沿用既有范围钳制。`BossHealthScaling` 链接真实 getter，旧版 NaN 断言转红，修复后默认值和上下限均通过。
- **CR-2026-10-25-010，P2：JSON 浮点转 Int64 在上边界溢出。** `Common/Data/BossRushJsonValue.cs` 原将 double 与 long.MaxValue 比较，但后者转为 double 已舍入到 2^63，导致该越界值被转成负数。现排除上界并拒绝非有限整数转换，保留真正的 long.MaxValue 整数 token、long.MinValue 浮点边界和既有四舍五入。`AuditCoreParsing` 在旧版解析 `9223372036854775808` 时先红，修复后边界回归通过。

首轮全量守卫为 751 PASS / 1 FAIL，唯一红项是 `architecture/modules.json` 漏登记既有 PromoRecording 两份源码，已补归属（SAFE）。首轮全量执行回归为 142 PASS / 2 环境失败；两组失败分别缺少显式 Managed / Harmony 环境变量，需补齐环境后重跑，不能计为通过。最终构建、门禁与重跑结果在同日 `FIX_TRACKER.md` 记录。

其他已审查链路未形成新增 confirmed finding：Mode E 阵营退出与共享租约、Mode F 补怪和撤离、Mode G/H 生成与场景准备、丧尸波次与安全区及结算、征程与随机事件取消、里程碑奖励、装备启停、NPC 商店/好感/婚姻交易、天空岛返航/剧情/巡守、遗种巢晚到生成、共享确认框、地图选择及 BGM owner。未测实际帧耗时，不据静态门控宣称无性能问题；历史待实机条目不因本轮离线通过而关闭。

人工验收记录建议文件名 `BossRush_Audit_20261025_smoke.txt`，以下是手工步骤 ID，并非新增 F3 自动用例：

- `AUDIT-01`：Mode D 待刷怪或自动下一波期间死亡；观察死亡画面和波次提示，死亡后继续生成或提示新波即不合格。
- `AUDIT-02`：标准竞技场批量生成、Mode D 自动下一波期间返基地；观察加载画面及入基地后的场景，不得有迟到敌人、下一波提示或“第 0 波完成”。
- `AUDIT-03`：冷启动后持霜之哀伤右键召唤；观察玩家周围，慢加载仍应完成召唤。另在生成中切武器和切图，原请求不得留下迟到亡灵。
- `AUDIT-04`：基地与加载、死亡期间分别按 L；观察屏幕中央成就面板、鼠标和移动控制，加载/死亡不得被面板遮挡，关闭后控制必须恢复。
- `AUDIT-05`：接叮当危险试用品委托后中断对话并返基地；观察叮当交互选项与朝向，取消后应能重新交互，正常完成只记录当前槽一次。
- `AUDIT-06`：使用独立测试槽正常切换两槽，核对图鉴、征程与日报各自进度；新槽出现另一槽进度或未知格式被覆盖即不合格。前置订阅者异常只在离线夹具注入，不要求修改真实玩家存档。

本轮未启动游戏、未访问玩家存档；以上实机结果尚未采集。

## 2026-10-24 龙息丧尸伤害修复（COMPAT / WIRE+；Fixed at L1/L2，L3 待验）

CR-2026-10-24-001，P2：龙息资源及运行时配置缺少 `DamageFactorToZombie`，官方枪械读取缺项为 0，弹道送入 `Health.Hurt` 后对 isZombie 目标把直接伤害清零。现装 resources.assets 五种 Cname_Zombie 的该标记均为 1，当前 DLL 的创建状态机也明确复制 preset.isZombie 至 Health，故不是只由测试替身构造的靶子。玩家未提供“无法使用”的具体表现，不能据此认定所有无法开火/装弹反馈都同因。

`Integration/DragonDescendant/DragonBreathWeaponConfig.cs` 为缺项补中性值 1，并刷新官方 StatCollection 的懒哈希缓存；`Integration/BossRushIntegrationRuntimeModule_Initialization.cs` 登记龙息专属恢复判据，使旧枪经初始化和手持入口补配，保留已有系数、重铸、磨损与运行时修饰。42 项回归直接执行生产恢复入口及完整官方枪械→弹道→DamageReceiver→Health 方法，旧行为直伤为 0、修复后恢复伤害，5 项负向探针均在预期断言转红。正式 Windows 隔离编译通过；无 L3。证据及宿主替身边界见 `Build/champion-dragonbreath-20261024/` 与夹具 README；待实机验收 BR 装弹、开火、旧枪重入场及普通/大体型丧尸伤害。

## 2026-10-23 CR-008 补充：吞咽动画受击外沿修复（COMPAT；Fixed at L1/L2，L3 待验）

CR-2026-10-23-008 的动画外沿遗漏已补齐。`GreedyMimicVisual.cs:66、165、207` 缓存主箱身和两块箱盖网格包络，每帧变换 24 个角点；`GreedyMimicEncounter.cs:69、156、194` 在成长回调及动画后更新唯一 BoxCollider，通过 kinematic Rigidbody 发布姿态，保留胸口基点与一次成长缩放。吞咽正负相、呼吸、跳动与开盖均覆盖；不逐帧重建碰撞体或全局 SyncTransforms。官方爆炸和近战不为 simpleHealth 多 Collider 去重，因此保持单盒。

真实 Unity 原生基线 5,754,526 项检查通过，486,373 次实际主网格射线、1,459,119 次球扫全部命中，吞咽外沿漏判为 0，四次官方爆炸均只扣一次血；成长当刻、回血打断、收缩拒收、暂停和销毁均覆盖。六项原生变异、15 项结构变异、独立旧盒变异及原有两项伤害变异共 24 项反向验证均实跑预期转红并恢复字节/SHA。独立复审另有 129,702 项检查通过，旧盒 875 条射线漏判准确转红；三位子代理均 gpt-6.1-sol / xhigh，未发现最终候选新增 confirmed issue。

533 项相关守卫、六组 FieldAdventure 中央回归与共享迷雾回归通过；Windows 正式隔离构建通过，DLL SHA-256 `B1DD5331ABDEBD4E2C1F172EE2A658631544CC93826F0288625F8648082B65A2`，14 个 Dev 标识缺席、77 个资源包 SHA 校验通过，仅两条既有 ModeH CS0649。真实游戏 DLL SHA 未变，其他任务改动保留；未提交、真实部署、启动游戏或访问存档。单盒为当前主箱体的保守包络，包含开口空隙，曲面边缘不逐三角形贴合；L3 与实际帧耗仍待验。完整证据与人工清单见 [吞咽受击修复报告](Build/mimic-swallow-collision-fix-20261023/修复与验证.md)。

## 2026-10-23 CR-008 / 009 修复与交叉复审（COMPAT；Fixed at L1/L2，L3 待验）

- **CR-2026-10-23-008，P2 / COMPAT / Fixed at L1/L2。** `Integration/FieldAdventures/Mimic/GreedyMimicVisual.cs:48、174` 新增单位缩放 Facing，由它统一朝向；`GreedyMimicEncounter.cs:48` 的受击体挂同节点。Body 保留原成长、呼吸、吞咽、跳动，受击尺寸/中心仅乘一次 Rules.Scale，世界预警与长舌仍挂 Root。当前 Encounter + Visual + 官方 HealthSimpleBase / DamageReceiver 的真实 Unity 装配通过 456 条断言：0～3 餐、8 朝向、左右边缘 64 组网格/射线/0.1 米球扫均中；隔离恢复固定 Root 后 16 组横向射线与球扫在预期断言转红，按字节恢复。成长、锁向、第三餐即时受击与清理均覆盖。该轮仅闭环稳定朝向；吞咽动画外沿的后续补充修复见上节。

- **CR-2026-10-23-009，P2 / COMPAT / Fixed at L1/L2。** `Integration/FieldAdventures/Delivery/OddParcelRuntimeModule.cs:200` 用 CanInteract(Offer) 同步阿稳组员；变更前保留选中对象，变更后通过官方索引切换保留服务或收敛到阿稳主项。`OddParcelInteractable.cs:88` 定向调用当前官方 HUD 的内容/选择刷新，保留选中事件幂等并同步索引缓存；隐藏祖先、池中 inactive 行同样处理。ClearWorld 先退组清引用再结束订单，旧 generation 对话不能修改新 owner。永久夹具实际执行官方 F 动作、分组和 HUD，3300 条通过，覆盖订单全流程、LIFO 池复用、持续/重开菜单、跨代次挂起对话；19 项隔离负探针全部在预期断言转红并恢复。局部新增官方 HUD 私有成员兼容调用属于 WIRE+，目标已对照当前官方实现与独立执行核实。

最终 533 项相关守卫、6 组 FieldAdventure 中央回归、共享迷雾回归与 Windows 正式隔离构建通过；7 项结构守卫负探针、1 项原生固定受击盒负探针、19 项快递负探针、2 项独立选择负探针及原有 2 项宝箱伤害负探针通过。三个子代理均 gpt-6.1-sol / xhigh；互相复审与独立 77 条完整官方交互探针未发现新增 confirmed issue。DLL SHA-256 `458722F995D2B9B7BBE3239591754D0150ADC900411453062DEC8C1330C46EA5`，无 Dev 标识，仅两条既有 ModeH CS0649。

证据和人工验收见 [本轮交付报告](Build/field-adventures-fixes-008-009-20261023/修复与验证.md)。只改 4 份既有生产文件及配套回归/守卫/专题文档，保留其它任务改动；无提交、真实游戏部署、游戏启动或存档访问。L3、游戏 PhysX 帧序及实际性能仍待验；未重跑全量守卫。

## 2026-10-23 野外趣事初始独立审核（SAFE；008、009 初始复现，后续修复见上节）

本轮重新对照当前生产、官方源码与资源，三个子代理统一 gpt-6.1-sol / xhigh。确认两个 P2，未发现 P0/P1；均在上一轮六项修复前存在。CR-2026-10-23-002～007 维持 Fixed at L1/L2，尚无 L3。本轮只审核，未修生产。

- **CR-2026-10-23-008，P2 / COMPAT / Fixed at L1/L2（修复见上节；以下为初始复现）。** `Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:48、52` 将矩形受击盒挂在固定 Root，`GreedyMimicVisual.cs:171` 只转 Body。玩家绕到 90/270 度后射击主木质箱体边缘，真实网格命中但受击盒漏判，可能同时影响回血打断。真实 Unity 装配当前 Encounter、Visual 与官方 HealthSimpleBase / DamageReceiver：0～3 餐、四朝向共 16 组网格均命中，其中横向 8 组 BoxCollider 射线与 0.1 米 SphereCast 均漏，正反向 8 组正常。建议同步稳定 yaw 并避免重复成长缩放；修后补斜向、成长、实际子弹及 L3 四侧射击。原生 42 条生命周期/装配断言通过包含缺陷特征复现，不代表已修复。

- **CR-2026-10-23-009，P2 / COMPAT / Fixed at L1/L2（修复见上节；以下为初始复现）。** `Integration/FieldAdventures/Delivery/OddParcelRuntimeModule.cs:185、231、461` 把快递子项持续留在阿稳组中；已有订单或两单额度耗尽时仅拒绝动作。官方 GetInteractableList 不检查子项可用性，HUD 仍建可选行。生产挂组、接单与取消，结合完整官方 HUD / CA_Interact / CharacterActionBase，复现两状态均 `visible=true、selected=true、StartActionByCharacter=false`，F 无反馈。建议按订单/额度同步成员并刷新列表、按对象保留选择；验证首单前中后、第二单用尽、持续打开菜单与重新靠近。3048 条隔离断言包含该坏行为的特征复现。

新增真实 PhysX 40 个胸口在墙外的射击组合均挡住墙后伤害；另 8 个胸口在墙内/表面的构造有穿墙伤害，尚未证明合法角色位置可达，记 UNVERIFIED。官方 HUD 真实资源父链、FOW.dll 启停 IL 已核实；性能无实机采样。

本轮 533 项 changed-only 守卫、6 组 FieldAdventure 回归、共享迷雾回归与 Windows 正式隔离构建通过；未重跑上轮全部 20 项负探针及全量守卫。旧丧尸文件仍 1239 行。详细触发、锚点、替身边界、证据与 FA-A01～A08 实机清单见 [独立审核报告](Build/field-adventures-independent-audit-20261023/审核报告.md)。没有真实部署、游戏启动、存档访问或提交；其他任务原字节保留。

## 2026-10-23 野外趣事六项修复与交叉复审（COMPAT；002～007 Fixed at L1/L2，L3 待验）

本轮覆盖三个玩法的 17 份生产源码及直接调用链、宿主/资源登记与相关夹具；实际修改 6 份既有生产文件，无新增 TypeID、存档字段或生产源码文件。三个子代理均按用户要求使用 GPT-6.1 Sol / xhigh，分别修复后交叉审核其他模块，主审复核共享门、材质租约、官方 API、构建与测试边界。最终未发现新的 confirmed P0/P1/P2；不将未实机的观感或性能推测记为已确认问题。

- **CR-2026-10-23-002，P2 / COMPAT / Fixed at L1/L2。** 原 `OnHurt` 仅累加 raw damageValue，30 × 2 暴击实扣 60 却无法达到 45 点回血打断。`Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:159` 现使用官方已判定的 crit 倍率累计正伤害，保留主玩家过滤，零/负倍率和回血不计入。FieldAdventureMimicRuntime 完整执行生产 Encounter、Rules、官方 HealthSimpleBase 与 DamageReceiver，覆盖普通/暴击边界及累计、拒收、非主玩家和负伤害；恢复旧累计方式会在真实扣血归因断言转红。L3 验证：实际普通/暴击达到 45 时药瓶蓄力应中断。

- **CR-2026-10-23-003，P2 / COMPAT / Fixed at L1/L2。** 原包裹按角色根 forward 摆放，官方按模型朝向检测，转身约 81° 后失去交互。`Integration/FieldAdventures/Delivery/OddParcelRuntimeModule.cs:350` 现沿 CurrentAimDirection 放在前方一米，抱起当刻同步；`:360` 的携带交互球退回玩家上方，放下/喷发成功落地时恢复中心，失败保持携带态。复审发现仅改朝向还不足以覆盖移动先于包裹更新，已补该帧序修复与回归；完整官方 CA_Interact 覆盖移动 0.5 米、任意转向与先搜索后更新。L3 验证：奔跑转身期间 F 交互持续可用，落地后须靠近包裹。

- **CR-2026-10-23-004，P2 / COMPAT / Fixed at L1/L2。** 原 32 米舌长与 45 米攻击门不一致，40 米仍受击但舌尖差约 8 米。`Integration/FieldAdventures/Mimic/GreedyMimicVisual.cs:216` 现伸至实际锁定落点，独立十二段各有迷雾采样与清理；弹幕保留八段/32 米。真实 Unity Transform 检查 31.99/32.01/40/45 米的连续端点、每段长度及采样登记，恢复旧截断会在真实端点断言转红。官方迷雾后端仍为替身，L3 验证最大射程实体、预警、命中及远端迷雾是否一致。

- **CR-2026-10-23-005，P2 / COMPAT / Fixed at L1/L2。** 原只更新本地化表，持续同一目标时压力/完整度文字不刷新。`Integration/FieldAdventures/Delivery/OddParcelInteractable.cs:62` 现仅在值/语言变化时刷新官方 HUD 已绑定行，不清目标、不重新选中；喷发当刻刷新。夹具执行完整官方 InteractHUD / InteractSelectionHUD，覆盖同目标变化、组选中与事件保留、语言切换；恢复只改本地化时按预期失败。L3 验证官方交互文字随搬运和喷发变化，组内选择不跳回。

- **CR-2026-10-23-006，P2 / COMPAT / Fixed at L1/L2。** 原无条件用瞄点减枪口，近瞄点会使附加攻击和巨炮后坐反向。`Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs:316` 与官方基础方向一致：水平距离超过角色到枪口距离加 0.1 米且 Moveability > 0.5 时才取瞄点，否则保留枪口前向；OnShot 和后坐共用结果。`:296` 遇墙沿胸口检查段退回，避免切向/反向射击时沿射向退到墙后。310 场景/2316 断言比对抽取的真实生产与官方方向分支，并执行 OnShot、TraceBlast、TickRecoil；六项隔离旧行为均转红。墙面为解析平面替身，L3 仍需脚边/高差/薄墙/斜墙与实际接触验证；私有 OverrideShootPoint 和第三方改弹道不在官方基础方向合同内。

- **CR-2026-10-23-007，P2 / COMPAT / Fixed at L1/L2。** 原 Unity 音源固定 0.7，不受官方 FMOD 设置影响。`Integration/FieldAdventures/Delivery/OddParcelVisual.cs:256` 现桥接 Master 与 Master/SFX 的 Volume、Mute；播放前及 runtime 暂停早返之前同步，AI 声源独立。运行时回归覆盖设置与暂停，真实 Unity AudioSource 检查实际音量属性；固定音量、漏 Mute、暂停后更新均被负探针抓到。L3 验证播放期间改主/音效音量或静音，叫声立即服从设置、敌人听觉不随玩家静音关闭。

验证：中央 `python tools/run_runtime_regressions.py --filter FieldAdventure` **6 PASS / 0 FAIL**；规则/库存 1376、场景门 43、宝箱 199、快递 2993、试用品 2316 条断言通过，真实 Unity 六组 Deferred 视觉无引擎错误和粉色像素。新运行时负探针 15 项、舌头结构守卫负探针 4 项、原生舌头负探针 1 项全部在预期断言转红，隔离副本按字节还原，生产输入 SHA 未变。新夹具已登记中央入口，各 README 写明真实生产与替身边界，不能把整体生命周期都声称为 L2。

全量 751 项守卫初跑 749 PASS / 2 FAIL：台账超限已把三节九月已闭环记录原字节追加至月档、正文留索引，LedgerSizeGuard 复测通过；唯一保留的是既有 ZombieModeRuntimeModule.cs 1239 > 1200，与本次无关。最终 FieldAdventureIntegrationGuard 与 533 项 changed-only 守卫全部通过。Windows 正式隔离构建通过、无 Dev 定义，只有两条既有 ModeH CS0649 警告；DLL SHA-256 `A38565E7B6B3E1848EAE4812CAFAB478D1C89714E568D010C32B3A3D7E02ABF8`。GAME_PATH 指向 Managed 实体拷贝，未部署、启动游戏、访问存档或提交。

证据汇总与人工验收见 `Build/field-adventures-fixes-20261023/`：`final-evidence.json`、`runtime-final.log`、`windows-build-final.log`、`guards-full.log`、`guard-probes/results.json` 与 `验收与复审.md`。原生最终报告 `Build/FieldAdventureVisuals/run-arsh4p5k/baseline.json`，旧舌长反向报告 `run-9l2xui66/negative-short-tongue.json`；快递七项负探针 `Build/runtime-regressions/FieldAdventureDeliveryRuntime/source-9sjlupx2/result.json`，试用品六项 `Build/runtime-regressions/FieldAdventurePrototypeRuntime-negative/copy-t_2yc3ek/report.json`，宝箱负探针随中央夹具默认执行。

剩余证据边界（非 confirmed finding）：反复切回借用枪会重建视觉对象，尚无帧耗采样，不能认定卡顿；宝箱直接 Health.Hurt 的载具保护路径未证实在普通出击可达；真实 PhysX、地形、迷雾遮挡、音量听感与操作手感仍待 L3。公开发布前按人工步骤检查，不能以编译/离线预览宣称游戏内已验证。

## 2026-10-23 蚀日花冠粒子持续报错（COMPAT）

CR-2026-10-23-001，P1 / COMPAT / Fixed at L1/L2，Needs runtime smoke：玩家日志中花冠生成后出现 239 次 `Particle Velocity curves must all be in the same mode`。`Integration/EclipseBloom/EclipseBloomBody.cs` 的 `BuildParticles` 只把线性 y 轴设为 TwoConstants，x/z 留为 Constant；现显式设置 x/z 零区间，保留 y 随机漂移与原轨道速度。真实 Unity 2022.3.62f3 播放模式的 94 次原生模拟零错误，三阶段/四瓣破碎/死亡清理均通过；隔离副本去掉 x/z 赋值后相同测试真实重现 112 次原报错，源码与资源 SHA 不变。此前静态预览和宿主替身未覆盖该约束，已新增 `EclipseBloomParticles` 中央执行入口。Windows 正式编译通过；修复后的游戏内复测待 owner。详见 [本轮报告](docs/reports/testing/蚀日花冠粒子报错与独立配乐-2026-10-23.md)。

## 2026-10-22 蚀日花冠地图接入与正式资源审核（COMPAT / SCHEMA+ / WIRE+）

CR-2026-10-22-003～009 已在 L1/L2 修复，L3 待验收。详见 [专题报告](docs/reports/testing/蚀日花冠官方地图与资源审核-2026-10-22.md)。

- 003，P1：盾片早期缺独立完整三维资源；已接 Blender MCP 作者模型、同源 512 图标、地面/装备代理及缺资源拒绝。
- 004，P2：日核旧网格接缝/极点退化；作者网格及最终包九个部件逐一验证闭合、绕序、各连通体积和包络。
- 005，P2：平面预警在起伏地面可能埋入地表仍判伤；改为分帧贴地与真实绘制面判伤。
- 006，P2：玩家同帧死亡使 owner 失效时可能吞已击杀 Boss 的专属掉落；以已提交实例实际死亡及 loaded 目标场景为准，发奖仍幂等。
- 007，P2：迷雾 forceRenderingOff / Renderer 关闭仍通过预警覆盖门；补真实可绘制状态，执行隐藏/恢复回归。
- 008，P2：死亡放弃拒写时销毁 flush 可能提交旧 accepted pending；共享保存采集受待放弃与交付事务门约束。
- 009，P2：SpawnCore 再归一化抹掉花冠已缩放的伤害；仅花冠保留工厂倍率，其他 Boss 不变。

全量 750 守卫为 749 PASS / 1 无关既有大文件预算 FAIL；Windows 正式隔离构建、相关中央回归与 108 项反向验证通过。已实读最终资源和离线 URP 预览；未部署、启动游戏或访问存档。

## 2026-10-22 Demo 丧尸入场与旧传送防护（COMPAT，CR-001/002 Fixed at L1/L2；未部署）

- CR-2026-10-22-001，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：custom 请求现归 IntegrationRuntimeModule，绑定实际目标 Scene 与官方 AfterInit 后的 Main；新入场、切图、卸载、角色替换 / 死亡、销毁及 H / 丧尸接管取消。写位置前复核归属，加载计入超时，GroundZero、持续清场与 Mode E 验证共用取消范围；正常与跨局路径由真实驱动隔离回归覆盖。
- CR-2026-10-22-002，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：目标图等待绑定本次入场身份，超时后完整复核场景 handle、loader、LevelInited 与 AfterInit；未就绪走原失败 / 退款协议，旧等待不清理或退款新局。真实等待、Begin / Initialize 与失败退款门覆盖 12 组场景，包含最终 yield 才 ready 和同图重新入场。

461 项 changed-only 守卫、153 项 ZombieMode 守卫与 11 组相关执行回归通过；12 个隔离变异均在预期处转红并逐字还原。Windows 正式隔离编译通过、游戏 DLL 哈希未变，未部署 / 启动游戏 / 访问存档。官方资源与 DLL 未发现 Demo 自动回起点规则，玩家第 17 波现场原因仍 UNVERIFIED。产物与验证见 `Build/teleport-safety-20261022/`，原调查见 `Build/reviews/2026-10-22-zombie-demo-teleport/investigation.txt`；验收步骤见 FIX_TRACKER 同名条目。

## 2026-10-21 自定义实体绕过迷雾隐藏（COMPAT / WIRE+）

CR-2026-10-21-011，P2 / COMPAT / WIRE+ / Fixed at L1/L2，Needs runtime smoke：自定义装备 / 近战模型缺官方 CharacterSubVisuals，独立 NPC / 沙暴外形缺 FOW 接线，女巫技能恢复还会强制 Show 清除官方 Hidden。现在分别补齐原版组件、共享官方 hider 渲染绑定并移除跨职责 Show；友好 NPC 不改阵营，AI / 交互继续运行。真实资源与 DLL 链已核对，正式隔离编译、260 项 changed-only 守卫、38 条实链执行断言和相关回归通过；19 个隔离守卫变异及其中 15 个执行变异按预期失败、还原后通过。L3 待实机，验证与步骤见 FIX_TRACKER 同名条目。

## 2026-10-21 自定义友好 NPC 常驻头顶姓名（COMPAT）

CR-2026-10-21-003，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：旧 `NPCNameTagHelper` 禁用原版名牌组件后手工投影、每帧强制打开姓名，俯视、第一和第三人称均常驻。原版基地友好 NPC 没有这类头顶名牌，姓名在对话 / 商店 / 交互界面中展示。现撤掉独立 UI，保留登记兼容与显式语言更新；不创建 actor / Health，不覆盖原版姓名键，普通刷新不重写延迟 actor 的当前语言。正式编译通过，L3 待实机；验证见 FIX_TRACKER 同名条目。

## 2026-10-21 丧尸模式专项审查（COMPAT，L1/L2；未部署、L3 待验）

- CR-2026-10-21-004，P1：撤离付款失败仍被共享撤离工厂送回基地。原工厂不读取丧尸结算结果，`OnSucceed` 返回后无条件通知撤离并返程，导致显示保留的净化点在退局清理中丢失。`Utilities/ModeExtractionPointFactory.cs::ConfigureEvents` 增加可选成功回执，丧尸 `CompleteZombieModeExtractionSuccess` 返回明确结果；失败留场并允许原区域重试，成功保持幂等与原 Mode F 行为。生产事件装配→结算→递归回调→清理回归确认，实机仍需验证付款失败留场和正常撤离。
- CR-2026-10-21-005，P2：撤离现金已到账后，通知抛错会保留可重复兑换的净化点；官方 `EconomyManager.Add` 也可能写完余额后在 `OnMoneyChanged` 抛错。`SettleZombieModeExtractionCashShell` 核对异常后的余额，到账先清点数、再隔离通知，避免重复兑付。前提交失败、后提交异常、通知失败和重复回调均由生产执行回归覆盖。
- CR-2026-10-21-006，P2：净化点拾取只摘掉待结算星点，吸收动画销毁后，`RunOnlyObjects` 中的 `PurificationPoint` 记录仍整局累积；后续敌人/光环/清理扫描会反复遍历这些死记录。`ZombieModeRuntimeModule.cs::PruneZombieModeUnknownRunOnlyRecords` 纳入已销毁星点，`ZombieModeDropsAndPerformance.cs::CleanupZombieModeExpiredDropCandidates` 在强制开波清理时无条件调用，即使没有实体掉落也回收。20 轮共 4,000 个星点的生产剪枝回归保持活跃记录数有界，活星点与吸收动画继续受退局 owner 管理；未做实机帧耗采样。
- CR-2026-10-21-007，P2：三类预警的触发窗口只有到期前 0.1 秒，单帧跨过触发与过期时间时，基类直接销毁，爆炸/减速/毒云不执行。`ZombieModePollution_RuntimeComponents.cs` 的对应 `OnRuntimeStopping` 在自然过期时复用单次触发路径，保留暂停、换局和来源死亡取消；真实 Update 掉帧回归已复现原错并验证修复。
- CR-2026-10-21-008，P2：骚扰弹仅测移动前后端点，10 m/s、0.3 秒一帧时可跨过玩家而漏判；也可能越过锁定落点继续飞。现按当前帧线段检测，并限制步长到原目标点，保留躲开后不追踪的行为。生产 Update 回归覆盖中途命中、侧移躲避、目标点后方不误伤和到期取消。
- CR-2026-10-20-001（既有 Open）本轮修复：三叉/散射不再重复修改武器 Damage，仅扩弹数/角度，由官方分弹公式处理；各卡原有代价保持。生产重建、官方分弹语句、重复调用、切枪和清理回归及反向验证通过。
- CR-2026-10-21-009，P2 / COMPAT / Open，L1：入场回退时即时退款及欠账写入同时失败，`ZombieModeRuntimeModule.cs:407,1008` 虽保留暂扣旗标，`ZombieModeRuntimeModule_HostLifecycle.cs:111,127` 随后无条件 `Reset()` 丢掉未兑现义务，下一次入口也会重置。触发需要经济/物品交付不可用再叠加 ES3 或缓存读写异常；正常切图单独不足以触发，未获玩家 L3 证据。完整修复需在暂扣前绑定可验证的存档身份，并设计跨场景/销毁重试、换槽及同槽删档重建边界，不能只删 Reset 而造成串档。留待退款生命周期专项；需故障注入覆盖双失败、恢复、换槽和销毁。
- CR-2026-10-21-010，P2 / COMPAT / Open，L1：`ZombieModeEntryDebt.cs:109,125` 吞读取异常返回 0，`OweCash/OweInvitation:180,190` 随后若可写成功，会把已有欠账覆盖为本次新增数量。触发条件为已有账本、读取暂时失败而写入可用；不属于正常读取下的数量计算错误。建议读写入口改用区分“缺键”和“读取失败”的 TryRead，失败禁止覆盖；需已有现金/邀请函债务、读错但可写、恢复后继续累加的执行回归。本轮未修改欠账协议或 owner。

说明对齐（SAFE）：枪手开局当前代码、游戏文案与 2026-10-17 数量回归均为普通弹药一组、重弹药至少 4 发；中英 Wiki 仍写历史 2,000 发，现已纠正并同步站点。未修改弹药发放数值；不能把旧文档不一致报告成新增发放缺陷。

本轮覆盖奖励与弹道、敌人/技能/刷怪、入场转存与撤离、波次/局内清理；尚未证实第 13 / 23 波无敌的首次 NaN 来源。统一守卫、正式编译及最终验证记录见 FIX_TRACKER 同名条目。

## 2026-10-20 丧尸第 23 波无敌反馈修复（COMPAT / WIRE+；L1/L2，玩家现场 L3 日志，未自行实机复现）

- CR-2026-10-20-002，P1 / COMPAT / Fixed at L1/L2，Needs runtime smoke：玩家日志按原字节 LF 计行，44905–49110 行共 198 次 Collider 变换矩阵 NaN / Infinity；同名迅捷精英的根、DamageReceiver、HeadCollider 各 66 次。原恢复只按位置距离判断，NaN 会绕过比较，旋转 / 缩放和受击祖先也没有恢复入口。共享 `Utilities/EnemyRecoveryMonitor.cs` 现先检测根与缓存受击祖先，配合 `EnemyRecoveryTransformSafety.cs` 保留合法姿态、在原可达落点恢复同一实体并保留剩余 HP，清除 Movement 输入、ECM2 速度 / 积累力及动态刚体惯性，最后同步物理并复核。无历史快照或可达点不假报成功；完整恢复失败保留待重试状态。常规下坠 / 卡住冷却及各模式落点 / 仇恨策略沿用。正式限频诊断保留原始坏值、节点路径、实例 ID 和移动状态。
- 同项已证传播缺口：`ZombieModeRuntimeModule_EnemyRuntime.cs::RefreshZombieModeGravityWellTargets` 原先让 NaN 穿过范围比较并直接写 Transform；现检查输入、旋转和中间结果，调用 `ECM2.CharacterMovement.SetPositionAndRotation(..., false)` 同步。真实 ECM2.dll IL 已核对该入口不禁启角色、不暂停贴地、保留正常速度；正式编译补显式 ECM2 引用与新安全助手清单。不能把这条传播链等同于现场首发源。
- UNVERIFIED / 继续保留：首次写入 NaN 的调用者，以及「第 23 波全体完全不受伤」与碰撞错误的具体对应。日志没有波次、BossRush 版本或首写托管栈，Run13 是局次而非波次，名称不含实例 ID；不能认定唯一实体或归因于其他 Mod。已核对官方 Movement、AI 路径与真实 ECM2.dll：未找到正常有限输入必然产生首个 NaN 的闭环算式；没有第 23 波专属无敌分支、普通 / Boss 生成托管无敌或污染刷新反复回血证据。原日志证据摘要及 SHA-256 在 `Build/zombie-invincible-20261020/player-log-evidence.json`。
- 2026-10-21 追加线索（SAFE，只读复核，UNVERIFIED）：玩家确认第 13 波也出现同样反馈，仍使用原版本、未安装上述修复 DLL，奖励组合已忘记；不能当作修复后复发。第 13 / 23 波均为普通波，波次移速从第 9 波起已封顶；黑洞 / 引力拖拽没有第 13 / 23 波专属门槛。正常领取上限下，丧尸奖励自身的枪伤代价最多累加 52%，与三叉 / 散射对武器 Damage 的修饰分属不同 Stat，不能直接相加判为归零。刚硬 / 反制可进一步削低伤害，护盾可暂时吸收，但尚未证实正常奖励组合造成全场永久无敌或首个 NaN。原 Player.log 当前路径已不存在，本次只复核会话记录、保存的证据摘要和当前代码，未新增 L3 证据。
- CR-2026-10-20-001，P2 / COMPAT / Fixed at L1/L2（2026-10-21）：三叉 / 散射原先按原弹数 / 新弹数降低枪械 `Damage`，官方 `ItemAgent_Gun.ShootOneBullet` 又除以 `ShotCount`，造成额外总伤损失。现仅增加弹数 / 散射角，由官方分摊单颗伤害，选卡代价保留；实际生产重建与官方分弹语句回归、反向验证已通过，详见本轮专项审查。该问题不足以解释第 13 / 23 波全体完全无敌，不能当作现场根因；游戏内全中伤害与切枪恢复仍待 L3。

- 2026-10-21 玩家补充：圈外击杀时仍显示战斗击杀进度且已满，之后进圈用尸潮信标开波，新波无法命中。自动准备安全区与战斗便携安全区均有生产入口；站位不控制结算。不能把现场直接解释为准备期环境怪，也不能凭描述确定圈的来源。
- CR-2026-10-21-001，P1 / COMPAT / Fixed at L1/L2，Needs runtime smoke：满目标 Combat Tick 只停刷、未补结算；死亡副作用抛错可能吞击杀或中断官方 OnDead 后的禁用，CompleteWave 的清理 / 补给异常可能留下 Settling 无协程。现达标检查先于安全区工作；死亡先去重 / 计数再执行隔离死效；波末副作用分别隔离，结算协程有唯一 owner，启动 / 迭代器失败最多每秒重试且不重复补给、污染。尚未证明谁首次制造现场满目标滞留。
- CR-2026-10-21-002，P2 / COMPAT / Fixed at L1/L2：StartWave 原只验 run，重复调用可再增波；信标尾帧只验 bool，缺阶段与操作 owner。现仅准备阶段可开波，副作用前初始化目标；信标捕获 run / wave / operationId，尾帧复核阶段与撤离、finally 仅释放自有读条，启动 null 回滚。正常入口下导致本次现场的竞态未复现，不能把防御缺口称作现场根因。
- 验证（2026-10-21）：全量守卫 747/747，最终 changed-only 144/144；相关执行回归 13/13（ZombieMode 八项及原五项），新波次夹具 80 场景 / 274 断言、恢复 117 断言；4 项新隔离变异均预期转红、按字节还原后通过（另保留 10-20 的 8 项证据）。Windows 正式编译退出码 0，无 Dev，2 条既有警告；DLL SHA-256 `16ED59D3573F2622A1213FEE9DD5FAF53C5E6CA25891D7F95023EECF19ACE727`，未部署。证据 `Build/zombie-wave-progress-20261021/`、`Build/runtime-regression-probes/ZombieModeWaveRuntime-9_8a2p97/report.json`。L3 待验：22→23→24 波，圈外达标应进入结算 / 选奖励；选完奖励才进入准备倒计时，正常 / 信标均只开下一波一次，身体 / 头部连续命中应扣血；持续满进度、跳波、重复奖励、无伤害或重复 Collider NaN 均不合格。首次 NaN 托管来源仍未证实。

验证（2026-10-20）：全量守卫 747/747 通过；最终失败重试 / 层级排序补丁后 changed-only 114/114 通过。隔离反向验证 8 个变异全部按预期转红并还原；相关生产执行回归 12/12 通过（ZombieMode 七项、EnemyRecoveryRuntime、IntegrationThirdReviewFixes、SpawnPositionPolicy、EnemySpawnRuntime、ModeEFEnemySpawnRuntime），恢复夹具 117 条断言。Windows 正式编译退出码 0，无 Dev 定义，2 条既有 ModeHPageContent 未赋值警告；使用 Managed 实体拷贝，游戏目录 DLL SHA-256 未变。正式产物 `Build/BossRush.dll` SHA-256 `C0159A3BAED3DF957265B79D500D824DB1FD1677B7AB2876259CF6BB816AC7DC`，完整证据在 `Build/zombie-invincible-20261020/` 和 `Build/runtime-regressions/`。L3 待验证：正常入场打过 22→23→24 波，命中身体 / 头部应掉血、波次可推进；发生恢复时怪的剩余血量应保留。持续无敌、恢复后满血或重复 Collider NaN 均为不合格，不把编译与隔离回归写成实机通过。

## 2026-10-18 v2.3.5 之后五个提交审查（001～023 全部 Fixed，COMPAT / OPERATIONAL，L1/L2，L3 待 owner）

范围 `3abfddcc..e25005ee`：`5a835089`、`9f9f9449`、`1170e834`、`c09c53cb`、`e25005ee`。审查时 HEAD 隔离签出 Windows 正式编译 0 错误；全量守卫 747/747；相关执行回归 7 项全过。没有 P0 / P1。下列位置按审查时 HEAD 行号；owner 要求全部修复，钻戒限量与官方存档键两条授权按好玩优先自定。

| ID | 级 / 分类 | 问题 | 位置 |
| --- | --- | --- | --- |
| CR-2026-10-18-001 | P2 / COMPAT | 丧尸模式减伤按负数累加，判上限用正数 0.40，满级后仍出卡、选了不加 | `ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs:329`、`ZombieModeRewardItemGrants.cs:534` |
| CR-2026-10-18-002 | P2 / COMPAT | 商人「下次购买保底 6 品」在该口径没有 6 品弹药时被回退成最低品质弹药并清掉保底 | `ZombieModeRewardItemGrants.cs:653-681`、`ZombieModeRewardNpcServices.cs:183-195` |
| CR-2026-10-18-003 | P2 / COMPAT（PLAUSIBLE） | 弹药补给 / 弹雨：手上口径发不出弹药时不再退回开局口径，直接发随机弹（是否存在无弹药口径的枪未核实） | `ZombieModeRewardItemGrants.cs:89-99,560-569`、`ZombieModeRewardTriggerEffects.cs:307-318` |
| CR-2026-10-18-004 | P2 / COMPAT | 钻石戒指终身限 1 枚，离婚不重置，求婚 / 丢失后只能赌许愿池，离婚后基本无法再婚 | `Integration/Affinity/NPCs/GoblinAffinityConfig.cs:805`、`NPCShopStockLedger.cs:96` |
| CR-2026-10-18-005 | P2 / COMPAT | 好感页对所有 NPC 读 `DiscountsByLevel` 并写「商店折扣」，羽织的治疗折扣被写成商店折扣 | `Integration/Affinity/UI/AffinityTabData.cs:101,195-198` |
| CR-2026-10-18-006 | P2 / COMPAT | 「全部孵化」每枚蛋各走一次 `RequestAssetFlush(bypassGates)`，一次点击同步整档写盘 N 次 | `PetNest/PetNestHatchService.cs:123-138,156-180` |
| CR-2026-10-18-007 | P2 / COMPAT | 刚归来未揭晓的崽再派出后，巢页按 petId 取到旧的已结算记录，显示「远征已归来 / 查看结果」 | `PetNest/PetNestUINestPage.cs:265-284,372-380` |
| CR-2026-10-18-008 | P3 / COMPAT | 弹药区间回退一律取最低品质；请求区间高于可用品质时应取最接近的那档 | `ZombieModeRewardItemGrants.cs:669-681` |
| CR-2026-10-18-009 | P3 / SAFE | 「按手上的枪配口径」实际是主武器槽优先，没读当前手持 | `ZombieModeRewardNpcServices.cs:210-228` |
| CR-2026-10-18-010 | P3 / SAFE | 弹药夹具替身降级语义与官方 `Search` 不一致，缺保底 (6,6) 与区间高于可用品质用例 | `tests/fixtures/ZombieModeAmmoSelection/Stubs.cs:84-93` |
| CR-2026-10-18-011 | P3 / SAFE | 衰减提示直接赋值，未聊天期间第二次衰减覆盖第一次，提示天数 / 扣分偏少 | `Integration/Affinity/AffinityManagerPersistenceAndDecay.cs:118` |
| CR-2026-10-18-012 | P3 / OPERATIONAL | 动态 `StockShop` 的 `Awake` 已订阅 `SavesSystem.OnCollectSaveData`，商店开着时会写官方存档键 `StockShop_BossRushNpc_<npcId>`（Mod 不读） | `Integration/Affinity/Systems/NPCShopSystem.cs:85-87,776-777` |
| CR-2026-10-18-013 | P3 / COMPAT | 永久 NPC 折扣上限 0.5 超过低买高卖临界 1/3（现有数据最高 0.20） | `Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcData.cs:109,172` |
| CR-2026-10-18-014 | P3 / SAFE | 对话系统吞掉非取消异常后，剧情仍被记成看过并发奖 | `PermanentDuckNpcStoryPlayer.cs:148-150`、`Integration/Dialogue/DialogueManager.cs:424-427` |
| CR-2026-10-18-015 | P3 / SAFE | 远征队满时巢页仍挂「派去远征」，点进去才被拒 | `PetNest/PetNestUINestPage.cs:326-334` |
| CR-2026-10-18-016 | P3 / SAFE | 「治一道疤」遗魂不够时不挂、按钮不写价钱，不符合付费服务口径 | `PetNest/PetNestUINestPage.cs:308-324` |
| CR-2026-10-18-017 | P3 / COMPAT | 摸摸脉冲结束时把体型还原成开始时的值，期间升级的新体型被盖回；曲线用 `Mathf.Sin` | `PetNest/PetNestUIMotionParts.cs:138-172`、`PetNestCompanionCare.cs:265` |
| CR-2026-10-18-018 | P3 / COMPAT | 远征到期提醒去重集合跨存档槽不清，换槽后同 id 的远征不提醒 | `PetNest/PetNestRuntimeModule.cs:324,340,354-364` |
| CR-2026-10-18-019 | P3 / COMPAT | 「全部孵化」孵出过一枚后吞掉后续失败原因；写盘失败但已提交的那枚不进揭晓 | `PetNest/PetNestUIPages.cs:546`、`PetNestHatchService.cs:138,175-177` |
| CR-2026-10-18-020 | P3 / SAFE | 博物馆驯养成就行复用 `Shiny`，给白字标题挂每帧流光 | `PetNest/PetNestUIMuseumPage.cs:222` |
| CR-2026-10-18-021 | P3 / SAFE | 守卫缺口：三份新 PetNestUI 文件不在分层守卫清单；丧尸反制守卫只断言子串 `IsPlayerOwnHit(`，加 `|| IsPlayerSideAttack` 仍能过 | `tests/PetNestUILayerGuard.py:130-134`、`tests/ZombieModeCompanionCounterplayGuard.py:39-44` |
| CR-2026-10-18-022 | P3 / SAFE | 页签指示条用 `AccentFill`（应为 `Accent`）；`RollNewPet` 的 summary 错挂到 `TryHatchAll` 头上 | `PetNest/PetNestUIMotion.cs:84`、`PetNestHatchService.cs:148-156` |
| CR-2026-10-18-023 | P2 / SAFE | 修复阶段全量回归发现：`1170e834` 后 HEAD 上两份执行回归就是红的（当轮只跑了按名过滤的回归）。`ManualSeptemberReview` 抽取的揭晓 / 归巢方法引用新成员却没有替身，编译失败；`IntegrationThirdReviewFixes` 仍断言「崽的远程命中吃刚硬减伤」，与有意改成「反制只认玩家本人」的设计相反 | `tests/fixtures/ManualSeptemberReview/Stubs.cs`、`HomecomingStubs.cs`；`tests/fixtures/IntegrationThirdReviewFixes/ZombieBossCases.cs:235` |

修复闭环（详见 `FIX_TRACKER.md` 同日节）：001 `IsZombieModeAttributeBonusAtCap` 比绝对值；002 / 008 `allowQualityFallback` + 取离区间最近的品质；003 `TryGiveZombieModeSupplyAmmo` 退回开局口径；009 先读 `GetGun()`；010 替身按官方 `DownGradeSearch`、补用例；004（拍板）戒指 `RestockOnlyWhenSingle` + 3 天，已婚不补、单身补；005 羽织标「治疗折扣」；011 衰减提示累加；012（拍板）开店摘掉官方 `Save` / `Load` 回调；013 折扣上限 0.30；014 `DialogueManager.SequenceFailureSerial`；006 / 019 整批单次落盘、部分孵化写原因；007 按 `lockedByExpeditionId` 找本趟；015 队满不挂；016 治疤有疤就挂并写价钱；017 `PetNestPulse.Rebase` + 共享缓动；018 换槽清空、键带出发时间；020 `ShinyFrameOnly`；021 两个守卫补强；022 `Accent` 与 summary 归位；023 补替身、断言随设计更新。为满足大文件预算，`NPCShopSystem` 改 partial，官方商店钩子三方法原样移到 `NPCShopSystemOfficialShopHooks.cs`（已登记编译清单）。

- Refuted：「好感写屏障期间剧情奖励能反复领、钻戒限量守不住」。屏障只在读档失败时保持，反序列化先进候选表、失败不写入，内存好感表为空且加分同样被拦，等级停在 0，到不了 3 级以上剧情与 7 级戒指。
- UNVERIFIED / Needs runtime smoke：没有 `GameClock` 的场景里 `GameClock.Day` 为 0，可能被当成「日子倒退」补满每日商品；好感页签克隆自笔记按钮，笔记未解锁时是否随模板隐藏、是否带「笔记」悬浮字；好感页根节点盖住 `ViewArea` 是否挡页签栏点击；批量孵化揭晓汇总 9–10 行时是否压住关闭按钮；博物馆首次打开全谱系头像同步加载耗时；白手起家小怪池按实例 ID 正负排除运行时 preset 的假设，以及 Mode E/F 阵营小怪表是否因排除 `isBoss` 变空；平安档现金期望实际上调约 16%（文字口径写的是不变）。

## 2026-10-13 推送后跨平台门禁修复（Fixed，COMPAT / SAFE，L2）

- CR-2026-10-13-012，P2 / COMPAT：天空岛导航、道路与摆放审计将 Windows CRLF 原字节 SHA 带入公开仓库，Linux LF 签出误报布局/证据过期；生产 `apply_navigation` 同样误拒绝。复用 `sky_island_road_layout` 的纯函数，新签名只统一 CRLF→LF，读取兼容既有原字节、LF 和 CRLF。真实坐标、字段、空白、BOM 与字符串变化仍拒绝；不重写现有地图或审计 JSON。三项旧码在真实 Ubuntu LF 环境均复现，修后两种换行及生产拒绝路径完成反向验证。
- CR-2026-10-13-013，P2 / SAFE：植物几何测试在公开源码 CI 强制读取私有 OBJ/MTL，干净签出必失败。源码模式改用公开摆放包络驱动真实细化算法，明确报告 `PARTIAL`；完整模式保留真实模型、原半径与拓扑检查，缺原件仍失败。公开 12 类 / 36 包络、本地 12 个真实模型及 4 类破坏/恢复检查通过，证据 `Build/ci-flora-20261013/probe-687b5c9a/evidence.json`。

首推提交 `15439905` 的远端 Guards 失败由以上两类原因导致，共 4 项；Wiki 构建与部署通过。修后公开 LF 副本全量 736 PASS / 9 PARTIAL / 0 FAIL，日志 `Build/commit-20261013/source-only-final-outside-build.log`；指纹证据 `Build/ci-fingerprint-20261013/`。本次只改离线工具、测试与文档，1196 个已编译生产源哈希一致；不据门禁修复宣称实机或性能验证完成。

## 2026-10-13 玩家反馈实现全面复审与修复（归档索引；L3 待验）

CR-001～011 已在 L1/L2 修复，完整根因、验证和手工验收原文见 [2026-10 月归档](archive/CODE_REVIEW_FINDINGS_2026-10.md)。REF / GIFT / MD / MH / PET / BR 实机体验与 PERF-01 帧耗仍待 L3；归档不关闭这些验收事项。

## 2026-10-12 玩家反馈与 Player(1).log 全面修复（COMPAT / SCHEMA+，L1/L2）

| ID | 等级 / 状态 / 分类 | 修复与验证边界 |
| --- | --- | --- |
| CR-2026-10-12-001 | P1 / Fixed / COMPAT | 天空岛合同把 `SpawnTomb` 当硬门槛，死亡不掉落类 Mod 让进岛必被拒（日志两次）。现只要求 `SaveCharacter`，`SpawnTomb=false` 记警告放行；夹具改为「墓碑关闭放行 / 保存关闭仍拒」，守卫禁止回退。 |
| CR-2026-10-12-002 | P1 / Fixed / COMPAT | 官方狗子背包跨局保留，里面任何东西（含船票）都让白手起家静默回落普通模式，正式版无日志；出战遗种崽的独立背包又完全不查。裸装遍历带出拦截项并双语提示，船票放行，加载中不算空，补查崽背包。 |
| CR-2026-10-12-003 | P2 / Fixed / COMPAT | `TryStartModeD` 恒返回 true，开局武器没发出时留下半激活状态；旧回归把这当预期。现以本次启动结论返回，失败回滚并提示；夹具三条断言反转并补重试。路牌晚于启动时重新布置 Mode D。 |
| CR-2026-10-12-004 | P1 / Fixed / COMPAT | 第三方 `OnAfterLevelInitialized` 订阅者抛异常时官方 `afterInit` 永远为假，鸭王杯卡满 30 秒后中止退票。改认官方最终落点后写入的 `"Done!"`，顺序由真实 DLL IL 断言钉住（反向改值转红）。 |
| CR-2026-10-12-005 | P2 / Fixed / COMPAT | 鸭王杯中止原因只写 DevLog；退票提示不看结果；退票失败即丢凭据；地图界面打不开时走直接传送丢 H 意图且不扣票；付票到目标场景之间无退款依据；意外场景静默清状态。退票迁 `ModeHTicketRefund`（NotPaid / Refunded / Owed，欠账入口补发），`OnCostPaid` 即登记，其余分支如实提示。 |
| CR-2026-10-12-006 | P2 / Fixed / COMPAT | 航向仪入箱 / 入包后广播抛错被代码销毁；死亡提示无条件说「在箱子里」；任一模式激活时守卫被静默清掉。改用共享 `TryAddExtraItem` 与归属核对，提示按真实结果，冲突时明说一次。 |
| CR-2026-10-12-007 | P1 / Fixed / COMPAT | 一击打到 1 血时孩儿护我与二阶段同时启动，二阶段收尾撤无敌、开组件。现互斥；二阶段被打断时补完收尾；召唤请求作废或龙裔消失时联动死亡收尾；宿命回响龙裔刷在龙皇旁。 |
| CR-2026-10-12-008 | P2 / Fixed / COMPAT | 龙裔死后迟到的受伤回调仍起冰减速协程（日志 10 次 inactive 报错）。补存活 / 激活守门。 |
| CR-2026-10-12-009 | P2 / Fixed / COMPAT | 擂台阵营模式下死目标仍算「有目标」，攻击循环空转、心跳照刷，看门狗不触发。清掉死目标逼官方 AI 重新索敌，`Resume` 不写回死目标。 |
| CR-2026-10-12-010 | P1 / Fixed / COMPAT | 开局洗牌只改 `EnemyPresets`，出场读的过滤缓存不失效，顺序从未打乱，龙裔 / 龙皇 / 幽灵女巫固定最后一波。洗牌后失效缓存。 |
| CR-2026-10-12-011 | P2 / Fixed / COMPAT | 无间炼狱关闭奖励箱后龙皇 / 龙裔专属装备整份丢失；单波现金三等分丢余数、超过单堆上限被截断、发放前先清账。专属抽签抽成纯函数落地掉落；现金按堆叠上限分堆、余额记账户、记不进滚入下一波。 |
| CR-2026-10-12-012 | P2 / Fixed / COMPAT | 遗种巢风浪档卡面「不会真死」，实际 6% 删档。owner 定风浪档改不死、亡命档保留真死；在途旧记录经 `EffectiveDeathRate` 封顶，不改存档。 |

均为 L1/L2：真实 DLL 合同、守卫与执行回归、Windows 正式编译；未启动游戏、未读写存档。验收清单与玩法新增（回火液 500108、毒蛇匕首厚血项、宠物行动方式 / 喂食 / 经验共享 / 伤害曲线）见 FIX_TRACKER 同日条目与本地 `docs/reports/testing/玩家反馈全面修复_2026-10-12.md`。

## 2026-10-11 最近五次提交审查修复（COMPAT / SAFE，L1/L2）

| ID | 等级 / 状态 / 分类 | 修复与验证边界 |
| --- | --- | --- |
| CR-2026-10-11-001 | P2 / Fixed / COMPAT | 冠军有效战斗遇到零游戏帧时长时不推进 Update 内招式；暂停与拍照保留冲锋，失效 owner 仍清理。真实 Update / Dashes 暂停与恢复回归通过。 |
| CR-2026-10-11-002 | P2 / Fixed / COMPAT | 穿墙冲锋以不超过 50 ms 的子步消费整帧时间，逐段验证地面；超时采用位移帧的已消费时间，途中取消停止剩余子步。120/60/30/20/10 FPS、交替长帧、断崖/高度、穿墙、翻滚与部分安全段伤害回归通过。 |
| CR-2026-10-11-003 | P2 / Fixed / COMPAT | 棍卫三档地面光带显式启用游戏时间；玩家默认仍用未缩放时间，光带几何、颜色与寿命参数不变。完整生产光带与棍卫前摇回归覆盖拍照、慢动作、结束与销毁。 |
| CR-2026-10-11-004 | P2 / Fixed / SAFE | 两个 2026-09-23 已闭环章节逐字追加到既有月归档，正文留索引；原文 SHA-256 与归档字节一致，1500 行上限未放宽。LedgerSizeGuard 通过。 |

新增执行入口 `SandstormCombatTiming`，旧代码先有 8 个场景失败，修复后通过；原 SandstormCombatRuntime / SandstormChampionPattern 继续覆盖完整调度表。只增加普通分支与小步地面检查，不改伤害、招式数值、音效、资源、存档或 Harmony 目标。最终守卫、正式编译及回归证据见 FIX_TRACKER 同日条目；没有实机 L3，不以离线通过承诺观感或性能。

## 2026-10-10 提交前复核（COMPAT / SAFE / OPERATIONAL，L1/L2）

- CR-2026-10-10-004，P1 / Fixed：钟守的商品原仅挂本人交互，`SkyIslandResidents.SpawnOneAsync` 会跳过婚后居民，缺席时岛内无法兑换。`SkyIslandWorldStoryServices` 现让 `Search_H` 复用同一 `ReloadMetronomeShopInteractable`，装配/清理与渡口商店共用方法，不增加 Tick。真实方法回归 955 项通过；断开钟庭入口的隔离探针准确转红，字节与 SHA-256 还原后通过。本人及婚后基地入口保持不变，实机可见性待验。
- CR-2026-10-10-005，P2 / Fixed：AchievementIcons 与 ResourceProduction 的旧 Unity 替身缺少共享 `ProductionIconCache.CloneTexture` 使用的 GraphicsFormat / CopyTexture API，导致两组回归编译失败。仅补共享测试替身，新增不支持、尺寸/格式/mip/采样参数与异常释放断言；两组回归均通过，不改变生产复制逻辑，不代表真实 GPU 像素或耗时通过。

- CR-2026-10-10-006，P2 / Fixed：已提交的点名册资源清单要求原图和正式包 alias，但本机两者均缺，导致 DailyReportArt 正式资源校验失败。复用 owner 指定作者工程原 PNG，以现有 IconsBuild 重建并只替换本地 production_icons；349 个 alias/Sprite 与压缩、预算、日报像素检查通过，旧包已备份。未部署实机，私有资源不进公开 commit。完整证据见[提交前审核报告](docs/reports/reviews/2026-10-10-提交前全面审核.md)。

## 2026-10-10 节拍器生产图标持有（COMPAT，L1/L2）

- CR-2026-10-10-003，P2 / Fixed：节拍器接入独立资源包时直接把包内 Sprite 交给持久物品 prefab，`ReloadMetronomeAssets.ResetStaticCaches()` 的 `Unload(true)` 会销毁其依赖贴图。已改为 `ReloadMetronomeIcon` 经 `ProductionIconCache.CloneTexture` 持有 GPU 副本，并以组件实例 ID 限定释放 owner；普通物品克隆销毁不释放副本。守卫删除实际 GPU 复制调用后按预期转红，按字节恢复、SHA-256 核对后通过。此项为交付前静态发现，热重载实机仍待验。

## 2026-10-10 点名册获取与商店收口（COMPAT，L1/L2）

- CR-2026-10-10-001，P1 / Fixed：先前把唯一获取途径接到普通玩家无法正常遇到的小满示例蓝图。商品迁至天空岛浮舟 sky_fuzhou，好感 3 级、售价 20000；Search_A 渡口工台追加同配置商店，兜底婚后离岛和居民生成失败。数据回归 666 条、婚姻 949 条、交互 949 条通过；删除装配/清理接线与错误 NPC ID 等 6 个隔离探针准确转红。
- CR-2026-10-10-002，P2 / Fixed：NPCShopSystem.Cleanup 销毁店铺对象却未关闭官方 StockShopView，owner 回收或 runtime 清理后仍可能残留窗口与输入屏蔽。现先停服务、退订，再仅关闭显示当前店且仍打开的 View；NPCShopPayment 18 条通过，旧 Cleanup 与删 Target 归属检查均在隔离副本转红。

743 项守卫、Windows 正式编译和 73 包部署哈希通过；13 个关键部署文件另行匹配。L3 待 RC-FZ-01～05，见[浮舟商店修正报告](docs/reports/testing/2026-10-10-点名册浮舟商店修正.md)。

## 2026-10-09 小满商店升级提示（COMPAT，L1/L2）

- CR-2026-10-09-001，P2 / Fixed：PermanentDuckNpcAffinityConfig.UnlocksByLevel 与 AffinityUIManager 都添加“解锁”/“Unlocked”前缀，导致好感升级横幅重复显示。配置改为仅返回 ShopName；中英文完整名称回归先确认旧实现失败，修正后 PermanentDuckNpcDialogue 666 checks 通过。正式编译部署完成；真实横幅与婚后购买待 RC-05 实机检查。见[点名册交付报告](docs/reports/testing/2026-10-09-点名册与小满商店交付.md)。

## 2026-10-08 噬魂挽歌内部名称显示（COMPAT，L1/L2）

- CR-2026-10-08-021，P1 / Fixed：500044 专属包走 EquipmentFactory，旧装配只登记 ItemFactory 与实例恢复；文本位于配置末段，未接统一启动/语言刷新。首次查看可能漏出 PhantomScythe_Melee_Item，切语言保留旧文本。补装备配置器 TypeID 过滤、统一文本注入及实际 raw key/历史别名，先命名再配置组件；保留编号和资源身份。实际包字段已读回；28 项生产入口/文本断言、4 个行为与 1 个登记守卫反向探针通过。见[文本修复报告](docs/reports/testing/2026-10-08-噬魂挽歌内部名称显示修复.md)。

提交前再次独立复核 CR-2026-10-08-018～021，未发现新增 confirmed issue。提交候选全量 743 项守卫、122 组回归与 Windows 正式编译通过；竞技场另做重入取消及 MoveNext / Current 异常释放探针，57 项通过。L3、完整 Unity 生命周期及自定义位置地图旧流程的边界见[提交前复核报告](docs/reports/reviews/2026-10-08-天空岛与镰刀提交前复核.md)。

## 2026-10-08 天空岛入场与兼容恢复（COMPAT / WIRE+，L1/L2）

- CR-2026-10-08-018，P1 / Fixed：旧竞技场等待跨图迟到派发，可能在岛上误启动裸装 Mode D。由 Integration owner 绑定原场景、请求代数和角色，在切图、后继请求与销毁时取消；超时不派发，嵌套协程透传并释放。LegacyArenaEntry 53 项及模式 / 征程接线回归通过，旧码与归属 / 取消变异准确转红。
- CR-2026-10-08-019，P1 / Fixed：不支持或缺少 GBuffer 的世界 shader 原来直接阻断整趟入场。现按支持与 GBuffer 检查选择本机官方材质，岛面 / 水面优先双面 Mask，保留世界材质名、贴图与参数；全部候选不可用及碰撞缺失仍拒绝。完整生产渲染类 180 项与旧码 / 7 类变异通过。本机官方 Cull、GBuffer 及当前包 alpha / 单位 ST 已核实，GPU 等价与新增图形后端支持未承诺。
- CR-2026-10-08-020，P1 / Fixed：慢读包挤占相机激活预算、导航 owner / data 延迟以及可选表现失败均可能误返航。激活独立计时，导航真实就绪后再扫描；HUD / 灰显 / 路灯 / 音景 / 居民 / 预热隔离，失败清理，居民装置任务兜底。预热 Build 上层与 owner 取消均释放，首个失败原因在基地黑幕后提示。租约 100、婚姻 949、战斗 64 条执行验证及 9 个隔离反向探针通过。

Fixed 表示代码与 L2 闭环；全量 743 项守卫、122 组回归、Windows 正式构建与 73 包部署哈希验证通过，最终引用清理另复跑相关验证。尚未取得反馈玩家当次日志或 L3，不能宣称已经确认玩家当次根因、实际画面或性能通过。证据与 SI-01～06 人工步骤见 [入场修复报告](docs/reports/sky-island/2026-10-08-天空岛入场与兼容修复.md)。

## 2026-10-08 未提交改动全面审核（COMPAT / OPERATIONAL，L1/L2）

| ID | 级别 / 状态 | 证据、根因与修复 |
| --- | --- | --- |
| CR-2026-10-08-014 | P1 / Fixed | F3 清空失败仍留下默认 pending，后续 Tick 可能真正清空。生产事务回归确认；改候选 store 保存，失败恢复原快照与重试义务，确认槽号再次核对，提交成功后的界面异常不误报失败，并清旧对白队列。 |
| CR-2026-10-08-015 | P2 / Fixed | 原磁盘诊断走官方内存缓存。改 ES3 File 显式读回；遗漏物理更新的故障注入与改回 Cache 的反向探针准确失败。Dev 真编译补齐 EasySave3 引用和构造器重载。 |
| CR-2026-10-08-016 | P1 / Fixed | 三 Boss 补注册时配置尚未载入，单纯作废筛选或立即读默认配置均不闭环。按真实宿主 Awake → LoadConfig → OnStart 顺序回归，由 BossFilter.OnStart 统一恢复实际禁用状态/因子；20 项反向探针通过。 |
| CR-2026-10-08-017 | P2 / Fixed | 共享包卸载摧毁地雷盒持久 prefab 借用的图标。生产 Config/Icon 回归重现，由 prefab 持有独立压缩 Texture/Sprite 副本；4 项资源所有权反向探针通过。 |

全量 743 项守卫、120 组回归、最终征程专项回归、Windows Dev/正式编译及部署哈希验证通过；保留 L3 与实际性能采样缺口。逐项需求、制品哈希与人工验收见 [审核报告](docs/reports/reviews/2026-10-08-未提交改动全面审核.md)。

## 2026-10-08 实档章节缺失、沙圈出生点与末阶段调度（COMPAT，L1/L2）

- CR-2026-10-08-008，P1 / Fixed：授权只读的实际征程 JSON 缺少整个 chapters 字段，前五章交付 token 仍在。以共享显式 JSON 编解码替换 Unity DTO 序列化，按持久化交付凭据兼容恢复，不重新发奖。
- CR-2026-10-08-009，P2 / Fixed：双生沙圈把 target 作为出生位置，直接落在玩家两侧。现统一使用 Boss 发射点贴地，并保持接触或到时凝柱。

末阶段停在首次换侧为日志确认的症状，宿主触发点尚未复现；局部改为 Update 驱动嵌套招式及定时等待，隔离回归验证宿主停止协程不会中断主攻击。泡泡官方 Hurt / HealthSimpleBase / OnShotDown 接线执行回归通过，外观更新为空心薄弧及细砂裂光。证据边界及人工验收见 [本轮报告](docs/reports/testing/2026-10-08-实档章节恢复与沙暴战斗修复.md)。没有离线修改玩家存档，未启动游戏。

## 2026-10-08 首次读档时机、沙暴持续与星阙命中（COMPAT，L1/L2）

- CR-2026-10-08-005，P1 / Fixed：征程主菜单阶段缓存未就绪的槽位并提前锁定初始化；对齐天空岛序章的关卡就绪门，已加载同槽保留退出保存，未知事实不反锁线索。真实生产读写/延迟加载/退出/换槽及 9 对独立写读进程回归通过。
- CR-2026-10-08-006，P1 / Fixed：召卫清除沙柱且存活门禁止喷弹；改并行召卫与沙柱独立寿命，转阶段也保留场上柱。
- CR-2026-10-08-007，P2 / Fixed：星阙重击没有核验 Hurt 结果及实际掉血，空砸仍强反馈；现真实伤害才触发停顿、震屏、击退，兼容沙泡简单生命并移除重复提示。

Fixed 为代码和离线闭环，真实重启与战斗尚待 L3。新制品、机制来源和实机步骤见 [升级报告](docs/reports/testing/2026-10-08-征程重启进度与猪鲨机制升级.md)。

## 2026-10-08 沙暴冠军接管与移动（COMPAT，L1/L2）

- CR-2026-10-08-012，P1 / Fixed：NodeCanvas BehaviourTree 继承 ScriptableObject，旧暂停辅助类转 Component 得空，原图可继续跑。新冠军/棍卫局部显式 Stop 四图与 owner，停 AI 子树、剥原武器和技能；未整体替换共享辅助类。
- CR-2026-10-08-013，P1 / Fixed：新冠军连续移动使用 SetPosition，官方每次暂停贴地并清速度，且每米近点 0.8 门槛可拒绝正常地板；改用官方 SetForceMoveVelocity / ECM2 与低频 A* 绕障，根碰撞体不放大，受阻后结束整组连冲转绕障。

用户反馈站桩，日志确认生成成功及 BehaviourTrees:0；没有记录具体地形拒绝分支。Fixed 表示代码与 L2 验证闭环，未宣称新 DLL 实机移动通过。小弟无掉落创建窗口、额外奖励过滤和清理接线已检查；证据见 [修复报告](docs/reports/testing/2026-10-08-沙暴冠军站桩与召唤修复.md)。

## 2026-10-08 进入竞技场的额外预热（COMPAT，L1/L2）

- CR-2026-10-08-010，P2 / Fixed：普通 DEMO 场景无条件启动整套 Mode E 预热；已删除场景公共路径的启动，E/F 实际入口仍保留按需准备。
- CR-2026-10-08-011，P2 / Fixed：焚皇戟管理器切图时同步加载龙王包，背包闲置也承担成本；现由主玩家手持事件触发异步加载并分帧扩池，离手/死亡/切图/停用取消，预览不抢同步请求。

本轮日志可证旧戟初始化时载包，Mode E 的 1607.5 ms 仅来自此前对话，本轮未重获竞技场样本。报名石无主因证据，性能提升比例和视觉手感等待 L3。守卫、回归、正式构建及部署核对记录见 [交付报告](docs/reports/testing/2026-10-08-沙暴冠军与星阙交付.md)。

## 2026-10-08 空仓地雷盒全面复审（COMPAT / SAFE，L1/L2）

| ID | 级别 / 状态 / 分类 | 根因与最小修复 |
| --- | --- | --- |
| CR-2026-10-08-002 | P2 / Fixed / COMPAT | ManualEquipmentRecovery 夹具未链接新增引用的 BossRushItemIds，导致 CS0103；补链接真实 Config/ConfigItemIds.cs，复跑 57 checks 通过。 |
| CR-2026-10-08-003 | P2 / Fixed / COMPAT | 地雷只等待目标场景 LevelBeginInitializing 取消，官方旧场景黑幕渐变期间仍可引爆。订阅 SceneLoader.onStartedLoadingScene 即时取消，使用判据检查 IsSceneLoading，阻断回调中开始切图后的剩余目标。旧生产代码失败后补修，地雷回归扩为 17 场景 / 223 断言。 |
| CR-2026-10-08-004 | P2 / Fixed / SAFE | 中英 Mod 简介仍写 37 / 20 件装备，和本轮 38 / 21 不一致。同步 WikiContent 源文并补全掉落专题与切图取消说明。 |

全量守卫 738 / 738、执行回归 118 / 118、Windows 正式编译、Wiki 构建与 80 项导航检查通过；未发现剩余 confirmed issue，L3 尚未运行。证据与剩余实机步骤见 [全面复审记录](docs/reports/reviews/2026-10-08-空仓地雷盒全面复审.md)。

## 2026-10-08 空仓地雷盒实现期复核（COMPAT，L1）

CR-2026-10-08-001：P2 / Fixed。新图腾动态 prefab 使用 DontDestroyOnLoad 并留在 ItemAssetsCollection；初版却由 NewWeaponRuntime cleanup 销毁程序化 Sprite/Texture，同进程重新启用时复用旧 prefab 会继续引用失效图标。改由动态 prefab 上的组件拥有资源，用创建者 instanceId 保护共享图标，宿主 cleanup 不再释放它。当前代码审查与正式编译通过；实际热重载未做 L3，证据和验收步骤见 [交付记录](docs/reports/testing/2026-10-08-空仓地雷盒交付与验收.md)。

## 2026-09-29 Mode H 发布前审核第二轮（14 项 Fixed / COMPAT / L1+L2）
CR-2026-09-29-013～015（P1）：看台退出回基地押注被误退、进程内换档后内容闸不重开、休眠原生敌人未清场；016～020（P2）：附加场景离场残留无敌中立、名人堂暂存失败重试误报成功、建页异常留下无按钮模态页、五处单行 TMP 框高不足、战斗每帧拼字符串；021～026（P3）六项。证伪 3、未证实 4；owner 拍板 2 项已落地（开战后退出 / 放弃押注按输结清、观战击杀不给经验），触发 / 修法 / 实机清单见[第二轮报告](docs/reports/reviews/2026-09-29-模式H发布前审核-第二轮.md)。
## 2026-09-27 UI / 特效 / Boss 与模块解耦审计归档

两项 L1/L2 已闭环记录已按原字节迁入 [9 月归档](archive/CODE_REVIEW_FINDINGS_2026-09.md)，保留原验证结果与 L3 待验边界。

2026-09-27 Jeff 任务与菜地果实链路复审：8 项 Fixed / L1+L2，L3 仍待验；完整原文见 [2026-09 归档](archive/CODE_REVIEW_FINDINGS_2026-09.md)。

2026-09-26 Jeff 任务与菜地果实链路：4 项 Fixed / L1+L2，L3 仍待验；完整原文见 [2026-09 归档](archive/CODE_REVIEW_FINDINGS_2026-09.md)。

2026-09-26 天空岛发版审查：代码修复与 L1/L2 已闭环，L3 仍待验；完整原文见 [2026-09 归档](archive/CODE_REVIEW_FINDINGS_2026-09.md)。

## 2026-09-25 全仓审查与修复：5 项新登记、1 项旧问题局部复开（均 Fixed / L1+L2，L3 待 owner）

六项已修复的原始根因、验证和待实机边界已逐字移至 `archive/CODE_REVIEW_FINDINGS_2026-09.md`（2026-11-08）；未证实线索保留如下。

UNVERIFIED 不计入上述六项：嵌套 Sticky 押品需要 `AcceptSticky=true` 的玩家可达容器，尚未证实；特殊 Boss 提交失败分支的完整回收与反向遍历回调多删元素，也仍缺可达性 / 后续回收证据。云蚋 render 报错已证伪为缺 Pillow 后的半初始化连带异常，依赖齐全时原守卫通过。

## 归档索引

- 2026-10-07 Player.log 复现：二次传送与终章误清场（COMPAT，L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-07 征程直接关闭与终章报名石（COMPAT，L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-07 远端合并后的全部改动复审（COMPAT / WIRE+，L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-07 最新提交独立深度复审（COMPAT / WIRE+，L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-06 第二轮深审：征程重复报名、宠物全模式、押注全余额与晴岚黑屏（L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-06 玩家 16 项反馈审核（COMPAT / SCHEMA+，L1/L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-09-30 天空岛整体生产审查（2026-10-01 全部 Fixed，L2；实机待验）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-29 Mode H 发布前审核（12 项 Fixed / COMPAT / L1+L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-29 Mode H 发布前审核与复核（19 项 Fixed / COMPAT / L1+L2）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-28 天空岛全面审核与修复（8 项 Fixed / L1+L2，L3 待实机）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-27 生产就绪全面审计（代码修复与 L1/L2 已闭环，L3 待实机）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-25 F3 实机报告（runId 20260925_044506_391）复核：4 项生产缺陷 + 4 项验收数据 / 判据问题（均 Fixed / L1+L2，L3 待下一轮 F3）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-26-001 — 船票 owner 迁移后地图费用读回旧 ID（P1，已修复，COMPAT）（2026-11-05 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-27 天空岛发版复审（Wiki 对账、可达性、居民、Boss 质感）（Fixed，L3 待 owner；2026-10-25 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-27 丧尸模式 Boss 表现、官方计数与补丁隔离复审（Fixed，L3 待 owner；2026-10-25 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-29 天空岛发布前生产级审查（Fixed，L3 待 owner；2026-10-25 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-30 天空岛道路与阴影（COMPAT，模型专题）（Fixed，L3 待 owner；2026-10-25 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-22 人工实测复核修复交付（12 项均 Fixed / L1+L2，L3 待 owner；2026-10-18 台账满 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-24 UI 共识对照审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-23 UI / 交互 / 特效审美审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-23 人工实测 16 项中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-22 全仓审计条目修复闭环 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 人工实测第二轮补漏（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 资源生产化续作（COMPAT / OPERATIONAL） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 Unity 资源交付与运行时所有权（COMPAT / OPERATIONAL） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 词缀选物 UI 覆盖（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 奖励池可靠性复核（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 Mode G 异常路径复核（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 NPC 对白与天空岛气泡复核（COMPAT / SAFE） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 鸭科夫日报入口、阅读与边界复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 晴岚群岛生产复核：库存与奖励交付 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 Mode G 生产审核与可玩性修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 鸭皇图鉴生产审核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 竞技场后山生产复审 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 第三轮（外部审查 11 条复核） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-21—22 全仓范围审计阶段记录（SAFE / OPERATIONAL；未修代码） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛导航优化后全面复审（023–025 已修复，待 L3） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛导航与场景刷新复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛婚姻修复复审（两项均已修复，待实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 全项目续审：战斗追加效果与生命周期 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛 NPC 中途结婚与剧情衔接（已修复，待实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 龙裔燃烧弹误选烟花 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 全项目玩法闭环复核：无伤判定、纯演出与额外战利品 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛主线与任务生命周期复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-16 天空岛昼夜时钟与官方任务接口审核：1 P1 + 1 P2 + 2 P3（均已修，L1 / L2） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-16 天空岛入口与 Jeff 序章审核：2 P1（均已修，L1 / L2） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-15 F3 全自动验收首轮复核补修：1 P2 + 2 P3（均已修，未实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（四）UI 优化对照审核：6 P2 + 18 P3（均已修）+ 5 条 PLAUSIBLE → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（三）天空岛 B 轮「噬风·回响」：1 P3（已修）+ 两条帧时间线索的处置 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（二）天空岛首轮岛内 F3 实机日志复核：1 P2 + 3 P3（均已修）+ 线索 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14 天空岛与共享 UI「实机前减负」：4 P2 + 5 P3（均已修）+ 登记与未验证线索 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-13（第三轮）天空岛 owner 试用反馈：4 P2 + 2 P3（均已修）+ 1 条 documented 决策 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-13 天空岛 UI 美术化审核：1 P1 + 4 P2 + 2 P3（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第四轮）玩家链路审核：3 P2 + 4 P3（均已修，含待拍板项 R-6 落地） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第五轮）owner 授权后的待拍板项落地：1 P1 + 2 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第三轮）F3 实机报告驱动：1 P1 + 2 P2 + 4 P3（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第二轮）关闭历史 Open 项：1 P1 + 1 孪生缺陷 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12 近两月新增内容的「可玩性」审核：2 P2（均已修）+ 2 条登记在案的取舍 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 本轮全面生产审核新增确认项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 天空岛内容批次四「云蚋」：接判夜与灶火时确认并修掉的既有缺陷 1 P2 + 1 P3（Fixed） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 天空岛内容批次三：顺带确认的既有经济风险 1 P1（Fixed，同日 owner 授权拍板后修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛内容批次二：评估报告遗留项 1 P2 / 3 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛可玩性与时长评估：4 P2 / 3 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛全方位审核：3 P1 / 13 P2 / 5 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 碰撞排障：1 P1（替换件是空气墙、附加件能穿过去） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 地形全黑排障：1 P0（鸭科夫跑在 URP Deferred，自研着色器没有 GBuffer pass） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 船点入口误报排障：1 P2（`基地船点入口未找到` 是假警报） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 实机日志排障：3 P0（天空岛 100% 进不去，三个独立成因） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛验收设施轮：0 新 confirmed，3 条既有 finding 补 L2 证据 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛全面审核（第三轮）：1 P1 / 2 P2 / 6 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛可玩性复审（第二轮）：1 P1 / 5 P2 / 2 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛进出岛流程对照复审：2 P2 / 1 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 天空岛全面审计追加：1 P1 / 1 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 天空岛端到端验收：3 P1 / 1 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 附加资源 Scene 与装备卸装：1 项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-07 最新实机日志修复：7 项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-07 近两周复核与 F3：4 项已修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 全量深度审查：5 P1 / 5 P2 已修复（2026-09-07 回填） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 设计与代码规范复审：D-4 / D-3 / D-2 三项已修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 冰霜 / 雷霆套装开放获取后的全面审核：4 项（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 冰霜 / 雷霆套装重做时确认的 3 项（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-05 二次深度复审：10 项已完成代码修复（2026-09-06 验收） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-05 全面复审：15 项已完成代码修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-001：遗种蛋与词缀熔石 100% 作废（两个新系统的入门产出口全断） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-002：无间炼狱下全部额外掉落丢失（含既有的寒霜长矛与女巫镰刀） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-003：龙皇绕过掉落登记，defer 判定对它恒假 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-004：Mode H 赔率页「锁盘」被推出屏幕，玩家只能弃局 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-005：Mode H 入口页第 4、5 张选秀卡被画在动作按钮底下 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-006：Mode H 恢复壳动作行 5 个按钮就出面板 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-007：Mode H 赔率分量 18 条标签双前缀，全显示星号 raw key → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-008：焚心椒「换弹更利索」用了不存在的 stat key（丧尸模式同款奖励一并失效） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-009：Mode F 血猎 Boss 加速完全不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-010：Mode H 真实押品在仓库满时只留在内存，重启/切槽即永久丢失 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-011：Mode H 押品阶段机三个死态，中止返还必然失败并连带锁死七个旧模式入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-012：随机事件乱入 Boss 顶掉本波 Boss 身份，标准竞技场卡波或误推波 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-013：`ShowMessage` 在正式构建里对玩家完全不可见（约 137 处调用） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-014：随机事件商人交互名 key 全仓零注入 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-015：大兴兴血脉的遗种巢随从入场即被自家清理扫描销毁并循环重生 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-016：空投「翻箱保护」判据选错，宽限窗口几乎永不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-017：两个落盘协调器的重试链被自身消费，SaveFile 失败后数据永不落盘 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-018：Mode F 补位重生克隆 preset 未挂租约，每次补位泄漏一个 ScriptableObject → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-019：九个新 TypeID（500059-500067）全部未登记掉落黑名单 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-020：本轮次要项汇总（7 条 P2/P3） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-022：在线 Wiki 的 favicon 一直 404 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-021：多行 callout 在线上 Wiki 掉出提示框（渲染缺陷） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-019：Mode H 八条战痕只有两条能生效（触发接线 + 自结算分量双重断链） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-020：战痕的 `appliesWhen` 条件层完全未求值 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-017：Mode H 口令点火目标无生产者，`finish` 整条是空操作 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-018：Mode H 战场快照的重建侧整条不可达，与 §20.3 恢复语义互斥 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-016：游戏内 Wiki 三处内容与代码不符（玩家可见） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-012：模式H 伤病 / 战痕 / 公开异常三层内容整体不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-013：ERROR 完整互换（§17.6.5）从未被调用，且租约不让渡输入 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-015：焚天龙皇不掉词缀熔石（挂接点漏并联） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-014：ApplyRetirement 零调用点，名人堂把冠军与替补记反 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-001：模式H 押品脱离仓库后阶段推进失败无回滚，真实物品永久丢失 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-002：模式H 濒退制「休息一场解除带伤」完全未实现 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-003：模式H 锁盘按钮所有失败原因均静默 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-004：ERROR 互换期间的击杀归属渗入图鉴与日报 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-005：战役交付退款失败时可重复领取奖金 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-006：ResolveFallbackActions 已成死逻辑 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-007：GameQuality 的 CS0649 注释与实际取色不符 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-008：摘要集合语义清单漏登记第二份入场名单 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-009：非本波 Boss 经掉落漏斗推进波次（跳波 + Mode D 跨模式串台） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-010：空投补给箱到时即销毁，不看玩家是否正在开箱 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-011：战役目标追踪按「进场景」而非「开局」武装，且换模式不解除 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-001：F3 直载基地子场景导致黑屏，模式失败返程污染后续用例 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-002：F3 终章 DamageInfo 零值初始化造成伤害空引用 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-003：F3 验收源码引用三个不存在的 API，阻断正式编译 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-004：距离休眠退订使用了角色的主场景索引 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-005：Mode H 认证拒绝已归一化的克隆，且旧受控击杀未触发死亡 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-006：撤离工厂删除官方回调后仍跳过通知与返程兜底 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-007：BGM 非空曲目表经 JsonUtility 读取后数组为空 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-008：F3 在 Mode F 退出重置后读取瞬时结算标志 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-009：F3 在标准 Boss 完成登记之前击杀创建中对象 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-010：F3 多波测试把波次推进误当作下一波生成完成 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-011：Mode H 诊断伤害空来源与外部死亡订阅不兼容 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-012：F3 同帧连续击杀触发外部经验提示的未初始化文本解析 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-013：Mode H 口令矩阵无人写入，缓存也未恢复逐效果证据 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-014：丧尸撤离验收要求正数净化点，却没有准备样本 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-015：H 成功创建赛季后保留入场意图，回到地图会重开 H → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-016：H 将冻结弹药写入装备槽，真实比赛生成反复失败 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-017：终章直接销毁 Boss 未清掉熔石掉落订阅 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-001：焚天龙铳切弹时容量 baseline 会被取整反推污染 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-002：焚天龙铳场景清理会丢失手持枪弹种 baseline → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-003：焚天龙铳射击热路径每发重复写 Stat → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-01-001：售货机 UI 崩溃 — 延迟注入商品未缓存 itemInstance → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-001：许愿台弹幕当前打开轮次不会接入新拉取结果 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-002：许愿台弹幕失败结果被 45 秒 TTL 缓存，重开面板也不会立即重试 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-003：许愿台关闭后未解绑静态弹幕回调，旧 View 会被挂到请求结束 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-08-17-001：Mode G 官方快照绕过逐 key eligibility，且九波计划错误要求整局全局去重 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-002：未知存档版本和临时挂起宿敌可能被后续对局覆盖 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-003：Mode G 奖励 API 回调异常会误判交付失败，Rewarding 死亡可能被完成回调抢占 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-004：Mode G 启动退款所有权不唯一，后续初始化异常可能双退 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-005：Mode G 候选地图没有显式验证状态，未实测地图会被当作 Verified → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-008：模式H 锁盘按钮转换非法，玩家被困在时停赔率页 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-009：模式H 生成回滚「退回看盘」转换非法，成功路径卡死在 MatchSpawning → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-010：模式H Recovering 是死态：全部技术故障出口通向无按钮的壳 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-011：模式H shutdown 闩锁永不复位：一次会话只能玩一局，二次入场吞船票并搁浅 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-012：模式H 恢复壳在重启后不可达，「船坞恢复分支」未实现 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-013：模式H 开局中止链全程无玩家可见文案，14 个 Unavailable_* 键零消费 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-014：模式G 无伤成就读跨局残留的 HasTakenDamage，同进程受过伤后 flawless 永久锁死 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-015：遗种巢 会话重启后血脉目录空窗：进一次竞技场之前官方血脉在基地全面不可用 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-016：遗种巢 关开关不清掉落追踪，dormant 契约被已挂接的 per-boss handler 穿透 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-017：日报 开关关闭期间换档：跨存档槽状态渗漏并覆写新档 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-018：模式H P2/P3 打磨项汇总（复审确认，6+3 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-019：模式G P2 打磨项汇总（复审确认，4 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-020：遗种巢 P2：PetNestDropService._hooks 慢泄漏 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-021：日报 P2/P3 打磨项汇总（复审确认，1+5 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-001：后山 P1：出击餐在正常流程中永远不会生效 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-002：后山 P1：展示柜加成在战局内实际不存在 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-003：征程 P1：终章决战打输一次即永久卡死 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-004：后山 P1：展示柜收藏跨存档槽泄漏，可写脏另一个档 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-005：征程 P2：召唤石维护 tick 每帧分配字符串 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-006：征程 P2：契约 HUD 每帧构建字符串，与头注释承诺不符 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-007：征程/后山 P3 设计取舍汇总（7 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-008：全内容可用性复核补充（5 个 P1 + 2 个 P2） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-09-04-021：真实押品 journal 与仓库快照没有共同提交 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-022：持久化 escrow 快照没有跨会话实物重建入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-023：Mode H 延迟物理落盘后下一帧丢弃欠账 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-024：真实押品同步四阶段必然撞每帧保存节流 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-025：Mode H 赛前阵容、kit 与口令缺少玩家编辑入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-026：孵化新崽已保存但蛋消耗未采集 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-027：远征发奖游标先于实物快照落盘 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-028：新材料和餐食继承便携安全区使用行为 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-029：日报跨日改写待发悬赏日期 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-030：普通 NPC 模块与永久模块重复生成小满 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-031：无间炼狱龙皇额外掉落订阅晚于同步消费 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-032：全量 CI 依赖未纳管制品与本机文档 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-033：展示柜未知版本或读档失败后仍可覆盖原 key → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-034：出击餐登记失败 return 仍触发官方扣量 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-035：血月收尾清表前未结清最后轮询窗口的击杀 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-036：孵化资产延期成功后不再补记孵化统计 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`


<!-- BEGIN UI CONSENSUS AUDIT FINDINGS 2026-09-24 -->

<!-- BEGIN FULL AUDIT 2026-09-21 -->

## 2026-09-19 Wiki 内容与公开部署复核（SAFE / OPERATIONAL）

来源：owner 要求全面核对 Wiki 与最新代码，随后授权修复并提交本地 commit。文案问题已按现有代码修正，公开部署仍待推送授权；验证证据见 `FIX_TRACKER.md` 同日 Wiki 小节。

| ID | 级别 / 分类 | 已确认问题与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-013 | P2 / SAFE | 中文消耗品、NPC 物品和护士页把安神滴剂写成负面 Buff 全清，并推荐解除幽灵女巫诅咒；实际 `CalmingDropsUsage` 要求 `NurseHealingService.HasDebuffs`，可治疗名单不含诅咒 ID 500043，仅有该状态时无法使用。 | Fixed：三组中英正文同步限定清除范围与使用条件，使用实际本地化名 Ghost Curse，生成页与构建后 HTML 已核对（L1/L2）。未扩展药品或治疗行为。 |
| CR-2026-09-19-014 | P2 / SAFE | `infobox.mts` 的护士服务中英文都列出复活、野战诊所，普通护士交互与治疗代码没有这些服务；正文背景中的诊所不是独立服务入口。 | Fixed：速查框改为恢复满血、清除可治疗的负面状态；正文同时修正 Lv.6 的 75 折笔误为 7.5 折。双语渲染与 HTTP 页面检查通过（L1/L2）。 |
| CR-2026-09-19-015 | P2 / OPERATIONAL | 公开 main 与成功 Pages 部署仍为 2026-09-08 的 4b1b5b68，天空岛双语/新装备页面 404，日报仍显示旧悬赏规则；本地 commit 不会更新公开网站。 | Open（待发布）：本地最新完整站点构建、237 页链接检查和 12 个关键页面 HTTP 检查通过；owner 本轮授权本地 commit，未授权 push，未发布。需推送经审核的提交后核对 Actions head_sha 和公开页面。 |

## 状态汇总

| 严重级 | Open | Fixed | Deferred | WontFix | 合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| P0 | 0 | 17 | 0 | 0 | 17 |
| P1 | 6 | 44 | 0 | 0 | 50 |
| P2 | 4 | 42 | 0 | 0 | 46 |
| P3 | 1 | 25 | 0 | 0 | 26 |

最后更新：2026-09-03 在线 Wiki 渲染层核查（owner 追问"页面风格是否一致"）。
新增 CR-2026-09-03-021（P2，已修）：`[tip]/[warn]` 的 sync 正则 `(.+)$` 只吃第一行，
源文里折成两行的 callout 其续行会掉到闭合 `:::` 之外，线上渲染成「提示框 + 游离正文」，
多数还是从逗号处断开。全站 42 处（其中 8 处是前一批 CR-2026-09-03-016 改写整节时新引入的），
连同全站唯一的双 `<h1>` 页面（reforge）一并修平。修在源文不动正则——那个 transform 被
`ZombieModeMutantWikiGuard` 逐字节镜像，JS/Python 的 `$`+MULTILINE 语义不同，改正则会静默漂移。
新增 `WikiCalloutSingleLineGuard`（4 例反向验证），生成物全量审计 224 篇零缺陷，
并起 VitePress dev server 做了 DOM 核对。

上一次更新：2026-09-03「全部玩法完整性」全量扫描批（数据表 → 代码反查，覆盖全部 Assets/**/*.json）。
新增 CR-2026-09-03-019（P1，已修）与 CR-2026-09-03-020（P2，已修）：
Mode H 八条战痕**只有两条真能生效**——三条触发型的 triggerId 与数据表逐字对不上或干脆没有调用点，
加上 `bell_dependence` 的自结算收益分量识别不到、只兑现代价的"纯负面利弊绑定"；
以及 `appliesWhen` 条件层被解析后零读者，9 个分量一律无条件施加。
两条均已修：019 当轮修复；020 由 owner 拍板「随战斗持续求值」后实施。
编译零警告；`ModeHScarTriggerWiringGuard`（按 JSON 反查代码）共 9 条断言逐条反向验证。

上一次更新：2026-09-03「新模式生产可玩性」审核批（f9b83c0..工作区，零调用点扫描）。
新增 CR-2026-09-03-017（P1）与 CR-2026-09-03-018（P2），两条都属"实现完整但入口没接线"：
口令点火目标无生产者导致 `finish` 整条是空操作（且认证/遥测都报 held，三层检查全绿），
战场快照的重建侧全链零调用且与 §20.3 恢复语义互斥、在冻结转换表下结构性不可达。
两条均已修：Windows 编译零警告、新增 `ModeHCommandFireTargetGuard`，
新旧断言共 8 项逐条反向验证转红（其中 2 条断言方向由"必须存在"反转为"必须缺席"）。

上一次更新：2026-09-03 游戏内 Wiki 内容核对批（f9b83c0 以来全量改动 vs `WikiContent/`）。
新增 CR-2026-09-03-016：1 条合并立条的内容缺陷（1 个 P2 + 2 个 P3），全部玩家可见且已修——
图鉴页写了一个不存在的"Wiki 书入口"（`ToggleCodexPanel` 全仓库只有物品这一个调用点）、
随机事件页承诺乱入 Boss 掉战利品箱（无间炼狱里既不掉箱也不进现金池，杀它零产出）、
Boss 筛选器页的生效范围停在 Mode G / Mode H / 随机事件立项之前。
另有 14 处开发预览装备提示不再宣传"调试授予"路径。纯内容改动，`SAFE`，无代码变更；
517 guard 中 Wiki/repowiki/图鉴/随机事件相关全绿（工作区另有 4 个与本批无关的红项，见下）。

上一次更新：2026-09-03 可达性接线批（审核发现的全部问题，含次要项）。新增 CR-2026-09-03-012..014：
1 个 P0（Mode H 伤病/战痕/公开异常三层内容整体不生效——认证只覆盖 13 条口令，
异常与分量 ID 永远查不到实测记录，战痕一条开不出、伤病永远无名、四个异常一次不触发，
而选秀卡照样把异常当卖点展示）、1 个 P1（ERROR 完整互换 §17.6.5 零调用点，
连同两个 Harmony postfix 与看台表演整条是死代码；同时修掉观战租约不让渡输入这个硬阻断）、
1 个 P1（ApplyRetirement 零调用点，名人堂把已退役的主选手记成冠军、真正夺冠的替补
反被写进 substituteHistory，冠军与替补整个对调）。
三条均已修：Windows 编译通过、516 guard 全绿（新增 ModeHDataStampGuard）、
15 项新断言逐条反向验证转红。次要项 7 条一并消化（接线 4、删除 2、documented 1）。
实机 smoke 七项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-03 f9b83c0 以来 365 个改动 .cs 的玩法向审核。新增 CR-2026-09-03-009..011：
1 个 P0（非本波 Boss 经掉落漏斗推波——乱入 Boss 跳波 + Mode D 把标准 Boss 刷进自己局内）、
1 个 P1（空投箱到时即销毁，不看玩家是否正在开箱）、1 个 P2（战役追踪按进场景而非开局武装）。
三条均已修：Windows 编译通过、515 guard 全绿、两个新 guard 各做过反向验证（共 12 项逐条转红）。
实机 smoke 五项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-03 七日全面审核（96 个提交 / 约 10 万行新增）。新增 CR-2026-09-03-001..008：
1 个 P0（押品脱离仓库后无回滚，真实物品可永久丢失）、1 个 P1（濒退制「休息解除带伤」完全未接线）、
2 个 P2（锁盘全静默；ERROR 互换归属渗入图鉴与日报）、4 个 P3。八条全部已修：
Windows 编译通过、513 guard 全绿、新增/增补 guard 均经**反向验证**（逐条破坏不变式确认转红）。
实机 smoke 四项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-02 第六轮（138 PASS / 0 FAIL / 0 SKIP / 0 WARN；014–017 转 Fixed。
H 初始整备 8/8、入场意图清除、丧尸结算返程、终章与最终订阅差值均实机通过。
人工清单仍有 113 条待验；既有 AI / 外部 Mod 异常不等同已排除）。

上一次更新：2026-09-02 第五轮（134 PASS / 3 FAIL / 1 WARN；H 首次认证及缓存通过，005/013 转 Fixed。
新增 014–017：丧尸撤离测试缺少正数样本、H 成功入场意图未消费、H 弹药误走装备槽、终章掉落订阅残留。
代码与离线验证完成；新增项在下一轮实机验证前保持 Open）。

上一次更新：2026-09-02 第四轮（完整 F3 报告 69 PASS / 2 FAIL；D 多波通过，010/012 转 Fixed。
H 逐候选不再拒绝或出现伤害异常，011 转 Fixed，但总体认证和缓存仍卡口令门，005 保持 Open。
新增 013：口令矩阵无生产写入者、签名绑定顺序与缓存恢复缺失；已修正并编译，游戏内复测前保持 Open）。

上一次更新：2026-09-01（新增 CR-2026-09-01-010，记录第二次 F3 报告确认的
2 个 P1 + 1 个 P2 + 1 个 P3；修复与静态验证已完成，下一份完整实机报告通过前保持 Open）。

上一次更新：2026-08-31（新增 CR-2026-08-31-009，记录首次 F3 完整验收日志确认的
4 个 P1 + 3 个 P2；代码、守卫和 Windows 编译修复已完成，但按发布门槛在下一份完整实机
报告通过前保持 Open，不提前标 Fixed）。

更早更新：2026-08-31（用户明确要求全部修复并保持内容开启。CR-2026-08-31-001..006
与原 deferred 的 CR-2026-08-31-007 七项均已 Fixed；新增 CR-2026-08-31-008 记录本轮
静态确认并修复的 5 个 P1 + 2 个 P2。模式H 赔率、日报未读提示两个旧 deferred 也已闭环。
修复内容与验证见 `FIX_TRACKER.md` 同日条目；涉及真实游戏对象、UI 和存档切槽的实机 smoke 仍待人工）。

更早更新：2026-08-29（四系统复审全面修复完成：CR-2026-08-29-008..021 全部 Fixed，
修复内容与验证见 `FIX_TRACKER.md` 的「四系统复审全面修复」条目。
Open 计数按问题条目计，分组条目内多项分别计数。上午修复轮的
CR-2026-08-29-001..007 未单独立条，见 `FIX_TRACKER.md` 四个修复包）。

## Confirmed Findings

### CR-2026-08-31-009：首次 F3 完整验收暴露的运行时回归（4 个 P1 + 3 个 P2）

**严重级**：P1（4 项）/ P2（3 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_125247_246.log`

#### 已确认问题

1. (P1) `CampaignContentCatalog` 使用 `JsonUtility` 解析两层对象数组时，实机只读出 version、
   `chapters` 静默为 null，导致已部署且哈希正确的正式表回退到硬编码。
2. (P1) Boss 乱入从图鉴目录抽到稳定 key 后直接进入 SpawnCore，但标准模式没有初始化
   `cachedCharacterPresets`；五次候选都报未找到预设。F3 只看 `TryForceTrigger=true`，仍把空转计 PASS。
3. (P1) F3 清场把除主玩家以外的所有存活角色都算成敌人；日志在开波前已有一个友方角色，
   清掉唯一 Boss 后仍报 `enemies=1` 并中止后续全套模式。
4. (P1) 动态商人 Animator 首个 `MagicBlendState.OnStateEnter` 早于 `MagicBlending.Start`，
   对空 Playable 调 `SetJobData` 抛异常；同时自定义 merchantID 被官方数据库报“未配置商人”。
5. (P2) Harmony 逐类隔离扫描对程序集内每个普通类型都创建 processor，普通业务方法名
   `Cleanup` 被 Harmony 当成 cleanup 回调，产生 3 条虚假补丁失败；真实补丁实际为 53/53。
6. (P2) 运行时 AddComponent 的日报报箱和征程公告板没有在 `base.Awake` 前初始化官方私有
   `otherInterablesInGroup`，每次进基地都产生可稳定复现的 NRE 警告；同类新增交互组件有相同风险。
7. (P2) F3 在 0.35 秒内轮流触发八种事件并立刻收尾，事件横幅在官方队列中延后播放，
   验收结束后仍持续弹出；这既污染清场结论，也遮蔽异步生成失败。

#### 已实现修复

- 征程表复用 Mode H 的严格 token parser，并保留整表签名/顺序/目标校验。
- 乱入桥先幂等准备官方 preset 缓存；八个事件新增实际副作用验收协议，F3 逐项等待至
  `Passed/Failed` 或 30 秒超时并写独立 case，不再把调度成功等同功能成功。
- 清场只统计 `Team.IsEnemy(Teams.player, team)` 的存活角色，明确排除遗种随从，并把残留实例、
  运行时 team 与 preset key 写进报告。
- 新增 MagicBlend 初始化顺序兼容补丁；商店以官方 ID 引导 Awake，同帧 Start 前切回稳定 Mod ID
  并覆盖事件库存。
- Harmony scanner 只处理类级或方法级含 `[HarmonyPatch]` 元数据的类型。
- 新增交互体均在 `base.Awake` 前建立私有分组空表；完整验收期间抑制普通消息/大横幅入队，finally 复位。

#### 验证需求

下一份 Dev F3 完整报告必须满足：Campaign `source=Json`；八个 `RANDOM_EVENT_*` 分项均 PASS；
Boss 乱入 `spawned>0`；商人 `merchant/shop=true, entries>0`；标准清场 `enemies=0`；完成后继续覆盖
Mode D/E/F/G/H、Zombie、终章/BGM、回基地回读与最终性能。Player.log 同时不得再出现本条所列
BossRush Harmony、Campaign、MagicBlend、merchantID 或自建 Interactable `base.Awake` 错误。

### CR-2026-09-01-010：第二次 F3 验收暴露的共享队列与模式清理回归

**严重级**：P1（2 项）/ P2（1 项）/ P3（1 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_152526_013.log`

#### 已确认问题

1. (P1) 标准模式的 Boss 乱入复用 Mode E/F 分帧后处理队列，但 scheduler 位于
   `TickWavesArenaRuntime` early-return 之后；标准模式中任务永远不推进，最终超时并在下一模式被清空。
2. (P1) Mode E/F 克隆预设在角色销毁前被提前 Destroy。Unity 伪 null 使后续
   `dropBoxOnDead`、Health 与 OnDestroy 链 NRE，Mode E 结束后留下 `no_preset` 角色并阻断整套验收。
3. (P2) Mode D 生成只强制 AI 仇恨，没有把中立官方预设的 runtime team 改成玩家敌对；
   角色虽进入本波登记表，却不满足战斗与 F3 敌对判定。旧 `EndModeD` 还只清列表，不销毁实体。
4. (P3) Mode E 动态分类商店在配置 merchantID 前以默认 `Albert` 执行 Awake，每次生成商人产生
   13 次无效官方数据库查询与警告；库存随后被覆盖，未造成当前用例失败，但污染日志并做无用工作。

#### 已实现修复

- 共享后处理 scheduler 在 WavesArena early-return 前无条件推进；空队列为 O(1) 快速返回。
- Mode D 在登记前设置并回读 wolf 敌对状态，失败即销毁；结束路径确定性注销恢复、禁掉落并销毁实体，
  非活动状态下重复调用也会清残留。
- Mode E/F 克隆预设改由对象级 lease 持有，角色 OnDestroy 后延迟释放；退出路径不再 Hurt，按顺序
  注销运行时并销毁角色。
- Mode E StockShop 使用 inactive → 官方 bootstrap ID → Awake → 稳定 Mode ID → 分类库存流程。
- F3 新增模式自有角色诊断与 2 秒逐帧清场等待；Mode D 报告登记对象的存活、阵营和敌对状态。

#### 验证需求

下一份完整 Dev F3 报告必须看到 `RANDOM_EVENT_BOSSINTRUSION`、`MODE_D_LIFECYCLE`、
`CLEANUP_AFTER_MODE_D`、`MODE_E_LIFECYCLE` 与 `CLEANUP_AFTER_MODE_E` 全部 PASS，并继续执行
Mode F/G/H、Zombie、终章/BGM、最终清场与存档回读。Player.log 不得再出现 Mode E 清理 NRE、
`scheduler_cleared` 乱入失败或 Mode E 分类商店 `Albert` 未配置噪声。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- **遗种巢 跨档写污染组合链**（016 的延伸，逐环节已静态核过、多步组合需实机）：关开关后已挂 handler 触发 `AddSouls` 会从当前槽重建缓存 → 主菜单切档（无人清缓存）→ 重开开关（EnsureBootstrapped 不重置缓存）→ 下次 Commit 把 A 档巢数据写进 B 档。与 017 的日报跨档渗漏同构。
- **遗种巢 远征奖励非崩溃失败被吞（已复核关闭）**：该历史线索已由 `PetNestExpeditionService.TryGrantPendingRewards` 的按条目游标与 `rewardsGranted` 门控修复；`GrantRewards` 任一物品或现金失败都会保留欠账和游标，下一次运行时重试。`ContentTransactions` 82 条断言含盖章失败后恢复且只投递一次；仍需 Unity 实机资源故障注入。
- **遗种巢 OnExpedition 孤儿锁无自愈（已实现，待专门验证）**：`PetNestExpeditionService.ReconcileOrphanedExpeditionLocks` 在可写事务内检查远征记录，确认无匹配后才复位 `OnExpedition` / `lockedByExpeditionId`，并在写屏障或存档故障时保持不动，避免误解锁。当前尚无专门夹具覆盖该入口，保留验证缺口，不再视为未修复代码问题。
- **遗种巢 P3 一组**：基地闲逛崽实为跟随玩家（含 >40m 传送，观感是仪仗队）；场景切换只停两个演出层、主面板 modal lease 极端时序可带进战斗图；天赋/战痕/蛋 KV 展示裸英文 key。
- **模式H 关停竞态孤儿**：StopCoroutine 打断 `CreateCharacterAsync` await 期间，`CreateIsolatedAsync`（SpawnBridge:58-176）async 延续仍会完成并登记抑制表+生成隔离角色，RollbackAll 只回收已入列 handle（窗口数帧）。
- **模式G 弃局确认页开在 Spawning 相位且时停拖住工厂 >15s 是否误报 TechnicalIntegrityLoss**：取决于官方 `CreateCharacterAsync` 是否受 timeScale 影响，静态无法判定。
- **模式G SceneChanged 终局的 `ShowMessage` 在 OnSceneLoaded 时机是否可见**：需实机确认。
- **日报 中途弃 raid 可能计成「成功撤离」**：`DailyReportStatsCollector.cs:190-204` 按 `!info.dead` 计撤离；官方对「战局中退出→回基地」是否判死亡未定，若不判则撤离数可刷（并入 D6 冒烟项）。

## 新条目模板

```markdown
### CR-YYYY-MM-DD-NNN：问题标题

**严重级**：P0/P1/P2/P3  
**兼容分类**：SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**状态**：Open / Fixed / Deferred / WontFix  
**来源**：代码审查 / 用户复现 / Player.log / guard / 人工 smoke

#### 位置

- `文件路径:行号`

#### 问题

是什么错，为什么是错。

#### 影响

玩家可见后果、静默失效、性能、存档或维护风险。

#### 建议修复

最小修复步骤。

#### 验证需求

编译、guard、人工 smoke 或无法验证原因。
```


## 2026-09-18 鸭王征程生产审核与体验优化

来源：owner 要求全面审核并优化。以下为当前代码确认并已修复的问题；未做 L3，完整范围与验收见 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md`。第三章去等待属于体验取舍，不列为缺陷。


### CR-2026-09-22-001 · documented · COMPAT / SCHEMA+ / WIRE+ · 鸭王征程改由官方 Jeff 发放、后山三设施改接官方建筑（设计决策归档）

- 状态：documented（owner 2026-09-22 拍板；不是缺陷）。
- 位置：`Utilities/OfficialQuests/`、`Campaign/CampaignOfficialQuestClient.cs`、`Campaign/CampaignQuestTable.cs`、`Campaign/CampaignBaseObjectives.cs`、`Integration/BackMountain/GardenConstructionSite.cs`、`ShowcaseDisplayScanner.cs`、`ShowcaseTagInjector.cs`、`ShowcaseService.cs`。
- 决定与理由：①征程六章接官方 `Duckov.Quests`（590101–590106，给予者 Jeff=1），投影核心只有一份，Mod 存档仍是权威；旧自绘公告板 / 面板退役，老档已建的保留。②官方基地菜地工地的付费交互父物体默认 inactive，玩家原本没有途径建成，Mod 只激活父物体、不写官方键；官方将来自己放出即 no-op。③自建「战利品登记簿」退役，加成改按官方陈列柜实摆计算（官方柜是基地存储，不带出击，换来真实陈列）；回退 = judges 改回读缓存、`sourceVersion` 回 1。④「建好菜地」放第二章（token 只在交付时发；上一章解锁的东西是下一章目标）。
- 证据等级：L1 + L2；L3 待 owner（清单见 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md`）。
- 待 owner 决定：官方陈列柜槽位标签（需 F3 `SHOWCASE_OFFICIAL_PROBE` 实机读出）；若官方陈列柜被 `requireQuests` 门住是否改官方数据；官方将来改成任务解锁菜地时 Mod 是否让位。

### CR-2026-09-22-002 · P0 · COMPAT · ResourceBundleLoader 的 pending 键按字符串比较，同一 bundle 两种路径写法触发同步二次加载，全部 Mod 物品注册失败

- 状态：Fixed（L2），待 L3。
- 位置：`Utilities/ResourceBundleLoader.cs`（`Prepare` / `LoadFromFile` / `Pending.ReleaseOwned`）。
- 证据：owner 实机 `Player.log`（2026-09-22 12:38）47 条 `can't be loaded because another AssetBundle with the same files is already loaded`，栈为 `Prepare` consumer → `EnsureRegistered` / `ItemFactory.LoadBundleInternal` → `ResourceBundleLoader.LoadFromFile` → `AssetBundle.LoadFromFile`；随后 `PlayerStorage.Load` 与 `CreateMainCharacterAsync` 因 prefab 为 null 抛 NRE。
- 根因：`FactoryResourceLoading.RunSpecial` 以 `Path.Combine(GetModPath(), "Assets/Items/x")` 为键，消费方以 `Path.Combine(modDir, "Assets", "Items", "x")` 查，`Dictionary<string,…>(OrdinalIgnoreCase)` 视为两个键；引入于 `60bb84b6`（09-20），此后未实机。
- 修复：键统一经 `NormalizeKey`（`Path.GetFullPath`），`Pending.Key` 持有归一化键；夹具 `ResourceProduction` 加别名用例（旧代码转红）。
- 兼容性：COMPAT；不改任何调用方路径写法。

### CR-2026-09-22-003 · P2 · COMPAT · ShowcaseTagInjector 经补丁过的 ItemAssetsCollection.GetPrefab 枚举全部物品，等于在 bootstrap 里对每个 TypeID 强制同步按需注册

- 状态：Fixed（L2）。
- 位置：`Integration/BackMountain/ShowcaseTagInjector.cs`（`IsShowcaseTrophy` / `EnsureTagged`）。
- 问题：文件头承诺「不 force-load bundle」，但 `ItemAssetsCollection.GetPrefab` 被 `ItemAssetsCollectionDynamicRegistrationPatch` 接管，对未注册的 TypeID 会同步跑 `EnsureRegistered`，异步预热失去意义，且在 CR-2026-09-22-002 存在时每件都撞二次加载。
- 修复：改走 `BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring`（`prefabCheckDepth` 旁路补丁），未注册的下次进基地再补；`BackMountainPlayabilityGuard` 加断言并反向验证。

### CR-2026-09-22-004 · P2 · COMPAT · 基地建筑 bundle 每次进基地被装配管线重复异步加载，Unity 每次报四条 "already loaded"

- 状态：Fixed（L2），待 L3。
- 位置：`Integration/FactoryResourceLoading.cs`（`RunSpecial`）、`Integration/IntegrationDeferredBootstrap.cs` 四处调用、`DailyReportMailboxBuilder` / `PetNestBuilder` 新增 `IsBundleLoaded`。
- 证据：owner 实机 `Player.log`（2026-09-22 13:29）weddingchapel / starwish_fountain / petnest_relic_nest / bossrush_daily_mailbox 各 9 条，栈在原生线程（`LoadFromFileAsync`），紧随其后是各建筑「已注入，跳过」。
- 根因：`60bb84b6` 把建筑加载改成 `RunSpecial` 无条件 `Prepare`，但建筑注入器持有静态 bundle 不释放；`LoadDirectory` 有 `loaded(name)` 门而 `RunSpecial` 没有。
- 修复：`RunSpecial(..., Func<bool> alreadyLoaded = null)`，持有中直接交给消费方；判据与消费方的持有字段同源。

### CR-2026-09-22-005 · P3 · SAFE · F3 用例 DATA_CODEX_FILTER_REFRESH 的判据停在锁定卡之前

- 状态：Fixed（L2），待 L3。
- 位置：`DebugAndTools/F3GameplayValidationCodex.cs`、`Integration/Codex/CodexBossCatalog.cs`（`CodexBossInfo.IsInCurrentPool`）。
- 证据：本轮 F3 `official=45->45->45` FAIL；09-16 之前 `47->46->47` PASS；中间 e80b1c3f 让名单把筛掉的官方 Boss 补成锁定卡（owner 2026-09-20 拍板）。
- 修复：用例改判池子给出的条目少一格再恢复，目录不缩；名单读不出（fail-open）时仍允许少一格。

### CR-2026-09-22-006 · P2 · COMPAT · 陈列加成设计押在官方「陈列柜」上，而该建筑是官方废弃的、建造菜单里没有

- 状态：Fixed（L2），待 L3。
- 位置：`Integration/BackMountain/ShowcaseTrophyCatalog.cs`（原 `ShowcaseTagInjector.cs`）、`ShowcaseDisplayScanner.cs`、征程文案（`CampaignContentCatalog` / `CampaignDialoguePlayer` / `CampaignLocalization` / `Chapters.json`）、Wiki。
- 证据：owner 2026-09-22 实机确认；F3 `SHOWCASE_OFFICIAL_PROBE`（13:29）场上只有 Gun 架（12 槽 `Gun`）、假人（7 槽按装备类型）、基地皮肤柜，Mod 枪（500035）与护甲（500004）已经摆在架子上。
- 根因：设计阶段从反编译源看到 `Tag_ShowCase` 与 `Showcase_01` 就当成可建，没有核对建造菜单数据。
- 修复：撤掉 `ShowCase` 标签注入（Mod 装备自带槽位标签），加成按枪械展示架 / 假人实摆计算（采集与公式不变）；玩家可见文案全部改口；`BackMountainPlayabilityGuard` 禁止再补展示标签。
- 教训：「官方有这个建筑类」不等于「玩家建得出来」，与菜地工地一样要先看场景 / 建造表。

### CR-2026-09-22-007 · P1 · COMPAT · 菜地指引写错入口（「建设面板」），造价与粑粑来源没告诉玩家；产物在菜地里长成克隆源的 3D 模型

- 状态：Fixed（L2），待 L3。
- 位置：`Campaign/CampaignDialoguePlayer.cs`（ch1 交付第 3 句）、`Campaign/CampaignContentCatalog.cs`（ch2 `GetEntryHint`、ch1 `GetDeliveredNotice`）、`Integration/BackMountain/BackMountainItems.cs`（`ConfigureItem`）、Wiki 中英。
- 证据：`鸭科夫源码` `ConstructionSite` / `CostTaker` / `Garden` / `Crop` / `GardenView`；level5 重解 `GardenConstruct`：`dontSave=0`、`money=0`、`items=[(98,1),(938,9)]`；`resources.assets` 扫 `Item` 组件 98=Shovel（铲子）、938=Shit（粑粑 / Poop）；官方 Wiki：粑粑只掉自蝇蝇队员 / 队长，分解粑粑枪射程模组得 5。
- 问题：①菜地是基地里的官方工地，付费交互由 Mod 打开，不在建造菜单，文案却让玩家去建设面板找；②第二章前置要 9 坨粑粑，玩家不知道去哪弄；③产物 prefab 沿克隆链带着便携安全区装置的模型，官方 `Crop.RefreshDisplayInstance` 用 `item.ItemGraphic` 摆作物，`InteractablePickup` 同理。
- 修复：三处文案改为「带铲子 ×1、粑粑 ×9 去工地交钱动工」并在对话里点出蝇蝇；Wiki 写清造价、配方与掉落地点；`ConfigureItem` 反射清空私有 `itemGraphic`，官方退回 `spriteGraphicPfb` 图标立牌（官方无模型物品同一条路）。
- 未核实：`GardenView.WateringTask` 状态机不在反编译源里，浇水按免费假设。
- 待 owner：官方造价是否接受为第二章前置；改价要走 `CostTaker.SetCost`（改官方数值，§10）。

### CR-2026-09-18-026 · P1 / COMPAT · 终章异步生成未与标准波次隔离，旧失败可清掉后继挑战

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignFinalBoss.cs`。
- 原因与修复：原工厂未传 isNonWaveSpawn，使用默认波次登记；旧请求 catch 无条件清理。现在走原工厂非波次路径，成功/异常都校验生成编号；死亡、让路、切槽、卸载回收，取消独白等待。
- 验证：L1/L2：真实编排抽取回归、非波次/旧异常/取消反向探针。

### CR-2026-09-18-027 · P1 / COMPAT · 目标终点入队失败后会随离场丢失重试机会

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignProgressService.cs`。
- 原因与修复：原目标完成只依赖局内追踪再次通知，终章死亡后立刻 ResetSession，一次性事件无法重发。服务保存会话内未入队事实，每秒重试；待交付和已达标未入队均不允许放弃，切槽清旧事实。
- 验证：L1/L2：ContentTransactions 执行一次终点、离场重试、切槽和重复操作；重试早返变异转红。

### CR-2026-09-18-028 · P2 / COMPAT · 待交付章节可被重新武装，完成计数继续变化

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignObjectiveTracker.cs`。
- 原因与修复：原 EnsureArmedFor 只认活动章节定义，ReadyToDeliver 仍被 GetActiveChapterDef 返回；计数与无伤判断在完成后继续消费事件。改为只武装 ContractActive，完成冻结、计数封顶，并重置同模式新局的波次观察。
- 验证：L1/L2：真实追踪器与模式桥覆盖；重新武装变异转红。

### CR-2026-09-18-029 · P2 / COMPAT · 敌方目标未按实际敌我关系过滤

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignObjectiveCollector.cs`。
- 原因与修复：原主玩家致命一击通过后直接统计，未排除友军与中立头目。复用官方 Team.IsEnemy，拒绝非敌对受害者，保留模式现有击杀归属。
- 验证：L1/L2：友军/中立/敌对回归，移除敌对门控变异转红。

### CR-2026-09-18-030 · P2 / COMPAT · 第二章近战目标缺少稳定开局工具

- 状态：Fixed，待 L3。
- 位置：`ModeD/ModeDEquipment_StarterKit.cs`。
- 原因与修复：原近战武器只有 40% 开局概率，而契约要求五次近战击杀且入场禁止自带装备。仅 ModeD 活动近战契约保证调用原近战配装；其它模式共用整备不获得这个保证，待交付也不触发。
- 验证：L1/L2：实际入口接线、契约判据回归、移除 ModeD 限定的反向探针；物品实例化仍待 L3。

### CR-2026-09-18-031 · P2 / COMPAT · 公告板未取得模态输入，HUD 语言与高度未随内容更新

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignBoardView.cs、Campaign/CampaignHud.cs`。
- 原因与修复：原建 Canvas 只提供 Raycaster；模态界面未占有输入，HUD 脏检查不含语言且固定高度。复用 ModalInputLease 并在 Esc/销毁/切槽释放；语言参与脏检查、正文实测量高；待交付和失败反馈明确。
- 验证：L1；L2 结构接线及模态/语言反向探针。实际输入栈、渲染和双语布局待 L3。

### CR-2026-09-18-032 · P2 / COMPAT · 线索镜像可残留旧槽状态，旧对话等待未取消

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignNoteBridge.cs、Campaign/CampaignDialoguePlayer.cs`。
- 原因与修复：原注册只补新条目的字典，解锁只向官方单向追加；常驻 actor 的异步对话可跨槽继续。改为权威存档双向校正列表/字典/解锁，读故障不反锁；共享取消 token 与代际门保护交付反馈。
- 验证：L1/L2：真实 Note 桥回归、取消方法/终章编排、共享 DialogueManager 回归及反向探针；官方图鉴界面待 L3。

### CR-2026-09-18-033 · P2 / COMPAT · 程序化资源缺少完整归属，重复创建/卸载可残留材质和图标

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignAssetCache.cs、Campaign/CampaignBoardBuilder.cs`。
- 原因与修复：召唤石子件原分别 new Material，公告板运行时图标与材质未进入统一账本。共用现有 AssetCache 的材质和 ownedObjects，运行时纹理/sprite/材质随 owner 回收，染色工具从宿主原样移入资源类型；无新增缓存或调度器。
- 验证：L1：分配与销毁路径核对；L2 资源归属接线守卫。实际 Unity 对象回收与帧耗待 L3。


提交集成补记（OPERATIONAL，L1/L2）：上游 f3d28dc 新增 SkyIslandBossVoice.cs 未登记 compile_official.bat，当前 HEAD 的 BossForge 已引用它；由 OfficialCompileListFileExistenceGuard 可直接检出。本次仅补显式编译清单一行，使新提交可独立构建，不改该模块玩法。

## 2026-09-22：20260920 人工实测 17 项当前实现复核

状态：以下均为 Open / 未修；7 项新登记，3 项旧结论因残余路径复开，2 项既有 Open finding 本轮重新取证仍成立。只登记本次已确认事实，不把待 L3 和需求口径差异列作缺陷。

完整逐子要求、触发链、官方资源/代码证据、最小修复与人工清单见 [完整报告](docs/reports/testing/20260922人工实测复核修复记录.md)。本轮未修生产代码、未重打包/真实部署、未启动游戏或访问玩家存档。

| ID | 严重度 / 分类 | 当前问题、准确锚点与玩家影响 | 证据 | 最小建议 |
| --- | --- | --- | --- | --- |
| CR-2026-09-22-011 | P1 / COMPAT | 冰原掠夺者 `Cname_RaiderIce` 被补入目录，但官方唯一预设 `EnemyPreset_Snow_Raider` 的 `isBoss=false`，`Integration/Codex/CodexKillCollector.cs:289` 拒收；全收集被该卡阻断。目录入口 `Integration/Codex/CodexBossCatalog.cs:255`。 | L1 + L2：实际官方资源字段、现机官方 DLL、生产 Catalog/Collector 探针：`listed=True recorded=False ... complete=False`。详见附录 A。 | 让采集资格与权威目录身份一致，保留玩家归属、H 排除和自定义/丧尸优先级；不通过改官方预设的全局 Boss 身份修补图鉴。 |
| CR-2026-09-22-012 | P1 / COMPAT | Mode H 已把玩家放到看台，旧普通传送等待结束后又搬到 custom 点：`Integration/BossRushIntegration_StartAndScene.cs:431` → `Integration/BossRushIntegration_TravelAndSetup.cs:121`，H 早退在同文件 `:319`，已经太晚。七张图两点相差 11.24–23.76 m。 | L1 + L2 点位复算；实际官方 async 加载顺序也存在后续覆盖源。未宣称九图全部必失败。详见附录 B。 | 普通传送在入口和等待后排除 typed H；H 租约等待当前角色、目标子场景和官方传送就绪，并带 generation 取消。 |
| CR-2026-09-22-013 | P1 / COMPAT | 远征出发/放生清 `deployedPetId` 后未通知实体层：`PetNest/PetNestExpeditionService.cs:305`、`PetNest/PetNestService.cs:289`；已激活的宠和光环继续存在，后续记录甚至可能被删除。 | L1 实体维护链 + L2 生产事务/通知探针。详见附录 C 的 PN-01。 | 成功提交所有席位变更后统一通知，复用现有代数和 CleanupOnce；失败事务不提前回收。 |
| CR-2026-09-22-014 | P1 / OPERATIONAL | 当前管线 D 盘 `Duckov_Data/Mods/BossRush` 缺 13 个发布包，包含图鉴、生产图标、四建筑；已部署 DLL 的类型集和霜冠包内姿态不含当前完整实现。`compile_official.bat:1097`、`:1391` 为目标路径依据。 | L2 文件清单、包内姿态、DLL 元数据、VerifyOnly 失败。另有 59 包字节不同，仅凭该数字不判断全部内容损坏。 | 后续修复完成后向核实的实际目标交付正式 DLL/全部清单包/散装数据，验包、验类型、验路径后由 owner 启动验收。本轮不执行。 |
| CR-2026-09-22-015 | P2 / COMPAT | 旧套装协程先清新请求的 pending，再发现 generation 失效；第三击可重排，结算又不复核冷却。`Integration/Bonus/FrostSetBonus_Nova.cs:98`、`Integration/Bonus/ThunderSetBonus_Storm.cs:105`。 | L2：两套装都复现 `extraQueued=1 hitsWithin40ms=2`。固定低伤并不能满足内置冷却要求。 | pending 绑定请求 owner，旧代数不得写新状态；结算重新核冷却。保留空结算不扣冷却。 |
| CR-2026-09-20-016（复开） | P2 / OPERATIONAL | raw 日报底图移除了动态块，但正式优先加载的 `Assets/ui/production_icons` 内仍烤旧签到格/按钮/图例。`Integration/DailyReport/DailyReportBackground.cs:36`、`Integration/ProductionIconCache.cs:29`。 | L1 + L2 三端内容取样：raw 动态位均纸色，作者源及包/仓库包保留旧色。不是实际截图。 | 更新作者生产图标输入并重导入/重打包，检查实际 alias 像素，不能仅重出 raw PNG。 |
| CR-2026-09-20-017（复开） | P2 / COMPAT | PreferredSize 修了纵向高度，但横向 stretch 保留旧 `sizeDelta.x`，正文实际宽是 viewport 两倍；遮罩裁字且不能横滚。`Integration/DailyReport/DailyReportUI_Dashboard.cs:243`、`:260`、`:264`、`:268`。 | L1 + L2 尺寸复算；现有 DailyReportPresentationGuard 仍 PASS，未覆盖此条件。 | stretch 后清 `sizeDelta.x`，保留纵向 PreferredSize；验首字、末行、宽度和中英文。 |
| CR-2026-09-20-019（复开） | P2 / COMPAT | 升级前在途记录没有颜色快照，原彩宠真死移除后翻牌退回裸名，尽管结算前原宠颜色仍在。`PetNest/PetNestExpeditionService.cs:416`、`:452`；`PetNest/PetNestModels.cs:649`。 | L2：结算前 Shiny，结算后 `LegacyCub`，纪念碑仍保有异色/black。详见 PN-02。 | 删除原宠前从同 petId 补齐缺失外观快照；不猜无来源的历史死亡记录。 |
| CR-2026-09-21-034（仍未修） | P2 / COMPAT | 远征奖励只判 InstantiateSync 非 null，官方已知 TypeID 缺 prefab 时返回非 null FallbackItem，仍消费奖励游标。`PetNest/PetNestExpeditionService.cs:936`、`:961`；官方 `鸭科夫源码/ItemStatsSystem/ItemAssetsCollection.cs:147`。 | 本轮重新取证 L1 + L2：fallback 被投递，`grantedLootUnits=1,rewardsGranted=true`，prefab 预检 0 次。 | 投递前核真实 prefab，失败保留欠账和游标，恢复后只交付一次。 |
| CR-2026-09-22-016 | P2 / COMPAT | 已远征的选中宠仍能走目的地→风险→出发菜单，但服务必拒绝。`PetNest/PetNestUIPages.cs:531`、`:561`；`PetNest/PetNestExpeditionService.cs:254`。 | L1，PN-03。没有发现重复成功派遣，不夸大为重复扣款。 | 显示和执行复用可派遣判据；锁定宠正文说明，改选其他宠后再挂出发卡。 |
| CR-2026-09-22-017 | P2 / COMPAT | 远征翻牌仍以 WaitForSecondsRealtime 自动推进且标已翻，缺孵化已有的暂停门。`PetNest/PetNestExpeditionRevealView.cs:196`、`:206`。 | L1 门控遗漏；从该模态界面调出官方暂停的具体操作尚未 L3 验证，不能称已复现错过结果。 | 复用暂停感知等待；暂停期间不推进或 MarkRevealed。 |
| CR-2026-09-21-038（仍未修） | P2 / SAFE / OPERATIONAL | 云蚋守卫仍要求旧 `Assets\\Sounds\\SkyIsland\\*.wav` 语句，构建已整树复制，导致全量门禁红。`tests/SkyIslandMosquitoGuard.py:158` 对比 `compile_official.bat:1224`。 | 本轮 L2 全量实跑重现；是守卫漂移，不是音效缺失证据。 | 守卫随当前部署语义更新，并做反向验证；不能加白名单或删除检查求绿。 |

旧 CR-2026-09-20-016 的 raw 修正、017 的纵向高度修正、019 的新快照修正仍成立；复开仅表示它们分别未覆盖正式包内容、横向 stretch 宽度和升级前在途记录。历史当日的部署记录不等同于当前 D 目标已部署。


## 2026-09-26 丧尸模式 Boss、图鉴与完整链路复核

分类：COMPAT / WIRE+；修复完成，L1+L2，L3 待 owner。详见 [审查与实机清单](docs/reports/reviews/2026-09-26_丧尸模式Boss与完整链路审查.md)。沿同日现有编号 110 继续登记，不覆盖其他会话的天空岛记录。

| ID | 等级 / 分类 | 已确认根因与影响 | 修复 / 验证 |
| --- | --- | --- | --- |
| CR-2026-09-26-111 | P1 / COMPAT / WIRE+ | `ZombieModeWaveController.HandleZombieModeHealthHurt` 在 OnHurt 补血，官方已先扣血/OnDead，Boss/群盾/减伤拦不住致死；`ZombieModePollution.ApplyZombieModeEnemyHurtAffixes` 的精英防御另按原始 damageValue 补血。 | Fixed：`ZombieModeDamageRuntime` 经既有 Hurt 补丁在 finalDamage 计算后、生命钳制前消费防御，删除补血与二次消费。L2 完整官方 Hurt + 真实 Harmony、本机官方 DLL 两种注入顺序、精英与 Boss 致死/溢出/来源边界。 |
| CR-2026-09-26-112 | P2 / COMPAT | `ZombieModeBossController.TickZombieModeHunterState` 在预警前改角色位置，然后在玩家位置落伤害，实际是提前瞬移。 | Fixed：先预警，再复用 `ZombieModeSprinterDashRuntime` 强制移动，在实际落点结算一次。L2 暂停、恢复、掉帧、死亡/换局取消通过；真实墙体碰撞待 L3。 |
| CR-2026-09-26-113 | P2 / COMPAT | 五类 Boss 没有独立轮廓；普通变异外形路径排除 IsBoss，原来只有体型与飘字，不能满足明确区分需求。 | Fixed：独立 `ZombieModeBossVisuals` 五类轮廓/色系/技能脉冲/官方姓名，跟随 modelRoot，暂停与销毁清理。L1+L2 接线与反向守卫；审美、遮挡、帧耗待 owner 目检。 |

图鉴复核：五个 `zombie_boss_*` key、主角亲手击杀、杂兵过滤、实例去重、回基地 flush 与持久化链未发现新增确认缺陷；`ContentThirdReviewFixes`、`SaveFailureRecovery` 通过。未修改图鉴生产代码，不能将隔离回归写成实机重启后已保存。独立 Windows 正式构建通过，未部署实际游戏目录；并行改动应整合后另作正式交付。
