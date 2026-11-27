# CODE_REVIEW_FINDINGS.md — 已确认问题库

更早记录见 `archive/`；月度归档索引保留在本文件正文。

## 2026-11-27 玩家反馈：重铸后再也出不了 Max（004–007 Fixed L1/L2；正式真编译通过，L3 待验）

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-27-004 · P2 / COMPAT · Fixed L1/L2 | 2026-11-25 下调后每次从预制体原值重抽，`新值 = 原值 + 方向 × 幅度 × 半宽`，上限正好是 `u^expo` 连续分布的端点。小数属性抽中上限的概率只有约万分之一（出厂 4.5 满投入也只有 0.37%）；上限是三位小数时（出厂 0.35 → 0.525、0.05 → 0.075）舍到两位只剩 0.52，幅度再大也够不着。读档夹到 150% 的旧满值装备一重铸就掉下来，再也出不了 Max，金色揭晓和 `UI/pop` 等于失效。Min 一侧对称。 | `ReforgeSystem.Reforge` 加顶档：幅度 ≥ `BOUND_SNAP_MAGNITUDE`（0.95）直接取上下限，这一支不舍入、不强行推开。默认投入（p≈0.5）每条约 2.5%、满投入约 8%，再乘倾向；只比普通结果多最多 2.5% 出厂值。`EntryAndReforgeCompatibility` 新增三类属性 × 小数三位上限 / 整数 / 0 值 / 护甲 / 后坐力下限 / 已在 Max 的旧装备 / 顶档以下普通结果；把阈值改成 1.01 后转红，报 `top band reaches Max on a 3-decimal cap: 0.52`。 |
| CR-2026-11-27-005 · P2 / COMPAT · Fixed L1/L2 | 详情对比里 Stat 两边都取 `stat.Value`，而重铸核心与快照都取 `BaseValue`。枪装了配件时 `Value` 带着配件修正：配件的加减被当成重铸差值标出来；满值属性被配件往回拉就不显示 Max，没到上限的属性被配件推过上限反而显示 Max。 | `SetupDetailsWithPrefabComparison` 预制体与玩家 Stat 都取 `BaseValue`；没洗动的 Stat 行照旧显示玩家这件的实际读数（含配件），只是不带差值。守卫断言两处 `= stat.BaseValue;`、无 `= stat.Value;`，改回一处即转红。 |
| CR-2026-11-27-006 · P3 / COMPAT · Fixed L1/L2 | 最终对比面板的差值阈值 `DIFF_THRESHOLD = 0.01` 等于结果舍入的一格：`0.06f - 0.05f` 只有 0.0099999，一格变化被当成没变；出厂 0.02 这类属性顶到上限 0.03 时整行不标，Max 也一起丢。 | 阈值改为 0.005（半格）；守卫要求 0 < 阈值 ≤ 0.005，改回 0.01 即转红。 |
| CR-2026-11-27-007 · P3 / COMPAT · Fixed L1/L2 | 005 修完后，括号差值与 Max / Min 按 `BaseValue`，行里的数字仍是官方 `Value`（`TryGetCurrentItemPropertyDisplayText` 与重铸后即时揭晓 `ShowPropertyChanges` 里 `Setup(selectedItem)` 写的文本）。枪装了 +5 的枪口、伤害洗到上限 45 时，整行显示「50 (↑15.00) Max」，数字和 Max 对不上，玩家会以为上限是 50。owner 要求重铸只看、只显示原始值。 | 两处 Stat 文本改按 `BaseValue` 写，即时揭晓与之后的刷新同口径，没洗动的 Stat 行也换成原始值；背包等官方详情不动。`GetItemValue` 在自身价值为 0 时先回退预制体价值，物品自身 `GetTotalRawValue()`（这时只剩配件价值）挪到最后。守卫断言 `stat.BaseValue.ToString(...)`、揭晓走 `TryGetCurrentItemPropertyDisplayText`、预制体价值先于自身总价；三处分别改回即转红。 |

实机清单（L3，未执行；没有启动游戏、没有读写存档）：

- `M_REFORGE_MAX_01`：拿一把出厂暴击率、散布这类小数属性的枪，投入拉满、倾向拉到最高连续重铸 30 次。应能看到某条属性显示金色 Max 并伴随 `UI/pop`，数值正好是详情页原值的 150%。不合格：30 次一次 Max 都没有，或显示 Max 的数值不是 150%。
- `M_REFORGE_MAX_02`：同一把枪装上会改散布或后坐力的配件后打开重铸页。没重铸过的行不应出现绿 / 红差值或 Max / Min；重铸后差值只等于这次洗出来的变化。不合格：未重铸的行带差值，或配件拆装前后 Max 标记出现 / 消失。
- `M_REFORGE_RAW_01`：枪装上会改伤害或后坐力的配件后打开重铸页，记下背包详情里的读数，再看重铸页同一行。重铸页的数字应是不含配件的原始值（与拆掉配件后背包里的读数一致），重铸后揭晓的那一下与停稳后的数字相同。不合格：重铸页数字含配件，或揭晓时先闪一个含配件的数字。
- `M_REFORGE_MAX_03`：后坐力类属性倾向拉满重铸，出现 Min 时为金色、数值正好是原值的 50%；伤害出现 Min 时为灰色。不合格：颜色反了。

## 2026-11-27 出征令战规复审：F3 用例过期、血债白扣药、内容首领词缀不足（001–003 Fixed L1/L2；Dev 与正式真编译通过，L3 待 owner）

范围：owner 要求审出征令改动「词条够不够有趣多变、是否都实装、效果好不好」。逐档对照 `ArenaTrialsTuning.ProfileFor`、Sky / Curses / Hazards / Affixes / FireRing 各部件、悬赏令文案与 Wiki，四档战规都有生产代码落地；下面三条成立，owner 原话「登记并全修」。共用证据：Dev 与正式真编译均 0 错 0 警告；正式 `Build\BossRush.dll` 与游戏目录 SHA-256 同为 `CAAB5CF7…582C`，`check_dll_identifiers --expect absent` 两份 PASS；`ArenaHostRemainder`、`LegacyArenaEntry` 隔离回归 PASS；守卫与反向验证见 FIX_TRACKER 同日节；未进游戏，未读写玩家存档。

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-27-001 · P2 / COMPAT（只在 Dev 构建） · Fixed L1/L2 | 2026-11-25 战规机制化后，困难 / 噩梦不再借变异词条（`ForcedMutatorIds` 只剩轻松的「妙手回春」），F3 仍按旧口径断言：`ARENA_EXPEDITION_TUNING` 要战规条数 {3,0,5,8}、除标准外都有强制词条；`ARENA_EXPEDITION_ORDER` 要场上恰好是三条困难战帖词条。owner 跑 F3 会得到 `forced_Hard`、`writ_mutators_missing` 这类假红，天象、诅咒、天灾、词缀、血债一项都没核。 | 条数改 {3,0,6,10}，强制词条只认轻松；新增逐档战规开关核对 `WishTrialProfileMismatch`，词缀抽取核对 `WishAffixRollMismatch`（40 轮 × 1–4 条 × 有无预设，直接跑生产 `RollInto`），两个血债补丁是否装上 `DescribeWishPrefix`；困难一局改核「不抽变异词条、战规对象在、有天象和诅咒、首领名牌带词缀前缀、额外掉落 1 件」；清理用例加「战规对象已自毁」。判定函数原样挪到新文件 `DebugAndTools/F3GameplayValidationExpeditionTrials.cs`（`LargeFileBudgetGuard` 1200 行），已登记编译清单。Dev 真编译通过；F3 实跑待 owner。 |
| CR-2026-11-27-002 · P1 / COMPAT · Fixed L1/L2 | 噩梦「血债」只拦 `Health.AddHealth`。官方 `Drug.CanBeUsed` 只看生命是否未满，`CA_UseItem.OnFinish` 先 `item.Use` 再扣堆叠 / 耐久，所以血债下吃回血药读条照走、药照扣、一滴血不回，白丢一管药。断补只在封禁窗口里挡用药，挡不住这条。 | `ArenaTrialsBloodDebtDrugPatch` 前置 `Drug.CanBeUsed`：战规在、主玩家、血债正挡回血、`healValue > 0` 时判不可用，官方 `UseItem` 因此不起读条、不扣药；不回血的止血药与同件上的其它效果不受影响。`ArenaTrialsBloodDebtUseNoticePatch` 前置 `CharacterMainControl.UseItem`：只在「整件因此不可用、且含回血药」时改推「血债未清，伤药不灵」（1.2 秒节流）并截断，不与官方「无法使用」叠出。`ArenaTrialsGuard` 补补丁目标与门控断言。 |
| CR-2026-11-27-003 · P2 / COMPAT · Fixed L1/L2 | 内容首领（龙裔、龙皇、女巫、冠军之影、蚀日花冠）没有可复制的预设，只能挂身法 / 死相两类；旧 `Roll` 在「不同类」档位只取 `min(条数, 类数)`，噩梦本该 3 条（压轴 4 条）实际只挂 2 条，额外掉落也从 2 件变成 1 件。悬赏令与 Wiki 写「压轴四类各一条」，内容首领做不到。 | 抽取提成静态 `RollInto`：先每类取一条，条数不够再在已取的类里补同类另一条；内容首领 3 条 = 两类各一再补一条，4 条 = 两类全挂；普通首领结果不变。悬赏令改为「每只首领带 3 条词缀，压轴带 4 条」（数字取自 `NightmareTrials`）；Wiki 中英源与站点生成页同步，并写明内容首领的补法。 |

L3：见 FIX_TRACKER 同日节 [L3-TRIALS-01]～[L3-TRIALS-04]。

## 2026-11-26 外部复审第二轮：启动半途失败、迟到激活漏救、重建期间换档、Harmony 备用副本（005–008 Fixed L1/L2；正式真编译通过，L3 待 owner）

范围：对 f32a1bd4 的外部复审（对方对真实入口注入故障、做隔离测试，未改仓库、未进游戏），四条逐条对照代码与官方 IL 核实成立。共用证据：去掉 `BOSSRUSH_DEV_BUILD` 的正式真编译 0 错 0 警告；`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `9C2236AE…4470`；`check_dll_identifiers --expect absent` 两份 PASS；全量守卫 800 PASS；相关 22 个隔离执行回归 PASS；未进游戏，未读写玩家存档。

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-26-005 · P1 / COMPAT · Fixed L1/L2 | 宿主启动只隔离了一半：`RegisterRuntimeModules` 抛错时 `Instance` 已设，后面各阶段与启动自检都不跑；`StartIntegrationRuntime` 抛错时全部模块的 OnStart 被跳过，而自检早在 Awake 末尾写了 `failed=none`。表现为管理器显示已加载、进游戏像没加载。玩家说重新开关就恢复，这与 Harmony 永久缓存那条路径（开关救不回）不符，更像这一条（推断，待 Player.log）。 | 模块登记与集成启动各自单行隔离（`register-modules` / `integration-start`），模块 OnStart 照常分发（宿主逐个模块 try）；启动自检改由 `CompleteStart` 在 Start 末尾打印，覆盖全程。`HostStartupIsolationGuard` 增探针；`CustomBossArenaPresetAwakeRegistrationGuard` 的 `[HostStartOrder]` 按新写法精确比较，「先装配置、再分发 OnStart」不变；`HostDestroyOwnership` 夹具抽取真实 Start，加模块登记抛错、集成启动抛错两例；把生产改回旧写法（两处去隔离、去掉 CompleteStart）三种都转红。宿主 +1 行，partial 预算 19658/19659。 |
| CR-2026-11-26-006 · P1 / COMPAT · Fixed L1/L2 | 迟到激活漏救：官方 `LevelManager.CreateMainCharacterAsync` 先 await `LoadOrCreateCharacterItemInstance`、再 await `CharacterCreator.CreateCharacter`、最后才写 `mainCharacter`（ikdasm 核对 `TeamSoda.Duckov.Core.dll`）。本 Mod 在这几帧里激活：读档失败发生在补丁装上前，没有记号；`BeginAwake` 又以 `MainCharacter != null` 判迟到，抢救 0 次，主武器槽仍空。反馈玩家是否命中未证实。 | 激活时 `LevelManager.Instance` 非空（关卡场景在加载或已加载）即算迟到，抢救照旧等关卡初始化完成再动手；主菜单激活不受影响。新夹具 `EquipmentRescueTiming` 执行生产 `BossRushStartupGuard` 与 `BossRushEquipmentRescue` 复现：判定改回旧写法时红在「生成主角期间激活必须抢救一次」；主菜单激活后正常读档不读存档、不扫场景、不重建。 |
| CR-2026-11-26-007 · P1 / COMPAT · Fixed L1/L2 | 重建期间换档：`RescueMainCharacterAsync` 在 await `InstantiateAsync` 之后只核对宿主与代次，换了存档槽或关卡后仍调当前角色的背包 / 仓库接口，旧档装备进了新档。实际撞上的机会小（换档要先回主菜单），后果是跨档复制。 | 重建前记下存档槽、关卡、主角、角色物品；完成后全部一致且原父物品还在才放，否则销毁重建品、整轮作废、旧孤儿不动（`SameContext`）。孤儿扫描改为 `FindObjectsByType`（含未激活、不排序），不再连同物品预制体资源遍历。`EquipmentRescueEvidenceGuard` 增 5 个探针（共 18）；`EquipmentRescueTiming` 覆盖换槽换关卡、只换槽、都没变三例；去掉核对、去掉存档槽一项、扫描不含未激活对象，三种改法都红在预期断言。 |
| CR-2026-11-26-008 · P2 / COMPAT · Fixed L1/L2 | 首选 Harmony 副本坏了不改试备用：`FindHarmonyCopy` 只要前置目录有这个文件就选它、不验文件头，`EnsureHarmonyLoaded` 只 LoadFrom 一次。 | `FindHarmonyCopies` 只收文件头读得出 0Harmony 身份的文件，前置第一、其余按版本从高到低；`EnsureHarmonyLoaded` 逐个 LoadFrom，抛错换下一份，自检写明跳过了哪份。Unity 2022.3 的 Mono 对真实 `0Harmony.dll` 跑六种目录布局：前置完好、前置截断 + 备用完好、前置不是程序集 + 备用完好、只有备用都载入可用的 Harmony；只有损坏前置、全无时报 `missing`。上一版在后两种「前置坏 + 备用好」里报 `BadImageFormatException`、Harmony 未载入。实测的截断和节表翻转都让文件头读取也失败，逐个重试是第二道兜底，这条分支只有守卫覆盖。`HostStartupIsolationGuard` 共 22 个探针。 |

L3：`M_STARTUP_SELFCHECK_01` 不变，那一行现在在 Start 末尾打印，`failed=` 可能出现 `register-modules` 或 `integration-start`。迟到激活的那几帧人工难以复现，由 `EquipmentRescueTiming` 夹具覆盖；`M_EQUIP_RESCUE_01` 照旧。

## 2026-11-26 玩家反馈：重进加载不出来、Mod 装备消失；「云上的坐标」交不了（001–004 Fixed L1/L2；正式真编译通过，L3 待 owner）

范围：两条玩家反馈，外加一份外部审查意见（对方未改仓库、未进游戏）。全节共用证据：去掉 `BOSSRUSH_DEV_BUILD` 的正式真编译 0 错 0 警告；`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `F4AB0362…653E`；`check_dll_identifiers --expect absent` 两份 PASS；全量守卫 800 PASS；相关 20 个隔离执行回归 PASS；未进游戏，未读写玩家存档。玩家侧没有冷启动失败那次的 Player.log，问题 1 具体走的哪条路径仍未证实，下面是已证实的代码缺陷。

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-26-001 · P1 / COMPAT · Fixed L1/L2 | 天空岛「云上的坐标」等交付：`TryDeliverQuest` 在 `EconomyManager.Add` 之后要求余额恰好 `+money`。官方 Add 先改余额、再同步派发 `OnMoneyChanged`，别的订阅者（其它 Mod、收益倍率补丁）在通知里再改一次钱，净变化对不上就判失败：不记账、不上闩，钱却已到账，提示「奖金这会儿发不出去」，玩家每点一次再到账一笔。成就奖金同病。 | 新增 `Common/Infrastructure/BossRushMoneyTransfer.cs`：官方返回 true，或这次调用里余额（扣钱时连同背包现金）确实变了，都算到账。天空岛到账即上闩；排队失败时退款成功才撤闩，两种提示分开写；存档不可写时显示存档状态。成就奖金、征程章节与引导奖金、鸭王杯押注同一口径，鸭王杯原有的「Pay 返回 false 但已扣」用例仍通过。`SkyIslandStory` 夹具加了替身 `AfterAdd` 和两条用例，把天空岛、成就的判定改回「恰好 +奖金」时都红在预期断言上。新守卫 `MoneyDeliveryNetDeltaGuard` 扫全部编译源（9 个变异探针）；征程三份守卫的锚点随调用同步，不变式不变，变异后转红。 |
| CR-2026-11-26-002 · P1 / COMPAT · Fixed L1/L2 | 读档时 Mod 装备没注册上：官方 `ItemTreeData.InstantiateAsync` 造 `FallbackItem_<id>` 空壳，又丢弃 `Slot.Plug` 的返回值（ikdasm 核对 IL），空壳插不进装备槽就成了孤儿。官方下次 `SaveMainCharacter` 只写挂在身上的东西，装备连同配件、背包内容一起丢失；外部审查在隔离环境复现了同一路径。两种触发：Mod 激活晚于读档（创意工坊要等 Steam 回调）；读档时某件注册失败。 | 新增 `Integration/BossRushEquipmentRescue.cs`，只在上述两种情况各跑一次：读还没被覆盖的 `Item/MainCharacterItemData`，按槽位对照；必须有同 TypeID 的空壳孤儿作证据。从记录重建整棵子树，插回原槽或背包；仍是空壳的进仓库原样保住，下次资源就绪即恢复。旧孤儿销毁，不直接写存档。`NeedsFallback` 交出空壳前记一笔。新守卫 `EquipmentRescueEvidenceGuard`（13 个探针，另对照反编译源的存档键与空壳命名）；`DynamicItemInitialization` 夹具断言「注册成功不记、持续缺失记一笔」。抢救流程本身没在游戏里跑过，L3。 |
| CR-2026-11-26-003 · P1 / COMPAT · Fixed L1/L2 | 宿主 Awake 没有隔离：任一初始化段抛错（含 JIT 时程序集缺失）都会中断后面所有阶段。本 Mod 先于前置 HarmonyLib 激活时，Mono 6.13 会把 0Harmony 引用永久记为缺失（同日用 Unity 2022.3 的 Mono 实测，之后再加载、再开关本 Mod 都救不回来）。正式构建里也没有任何启动诊断（DevLog 整条消失），玩家日志无从定位。 | 新增 `Common/Lifecycle/BossRushStartupGuard.cs`（不引用 HarmonyLib）。Awake 第一步预加载 0Harmony：前置目录优先，前置不在时用已安装 Mod 里版本最高的副本。五个初始化段各自单行 try/catch，记下失败阶段；补丁层失败时退回同步登记全部 Mod 物品。正式构建输出一行启动自检：目录、场景、是否迟到激活、Harmony 来源、关键补丁已核 / 总数、失败阶段。新守卫 `HostStartupIsolationGuard`（15 个探针）；`HostDestroyOwnership` 夹具执行真实 Awake，覆盖阶段顺序和三种阶段抛错。宿主 +9/−6 行，在 partial 预算内。 |
| CR-2026-11-26-004 · P3 / OPERATIONAL · Fixed L1/L2 | `compile_official.bat` 把前置 HarmonyLib 的创意工坊 ID 写成 3588386576，工坊页标注的必需前置是 3589088839。本机两份 `0Harmony.dll` SHA-256 相同，编译产物不受影响，但注释和路径会误导人。 | 改为 3589088839（保持 CRLF 和 ASCII 注释）；`HostStartupIsolationGuard` 核对它与 `BossRushStartupGuard` 里的常量一致。五份 Harmony 类夹具 `run.py` 的本机默认路径仍指 3588386576（同一份 DLL），未改。 |

挡不住的情况：读档时本 Mod 根本没在运行，玩家随后撤离或切场景，官方就已经把没有这件装备的角色写回存档，Mod 内补救不了。给玩家的建议：进游戏看到「Mod 变更」提示时点取消，等 Mod 加载完再继续，并保留 Player.log；已经丢的装备可用官方存档备份恢复。要证实问题 1 的真实原因，需要玩家冷启动失败那次的 Player.log（看启动自检那一行）；问题 2 需要第一次失败前后的余额，核对是否有别的 Mod 改钱。

L3 清单（owner）：
- `M_STARTUP_SELFCHECK_01`：正式构建进主菜单，在 Player.log 里搜 `[BossRush] 启动自检`。合格：只有一行，`harmony=loaded:` 或 `preloaded:`，`patches=N/N`，`failed=none`。不合格：没有这一行、`harmony=missing`、patches 两个数不等，或 failed 不是 none。
- `M_EQUIP_RESCUE_01`（用测试档）：身上装 Mod 武器（带配件）进基地后退到桌面；在 Mod 管理器里关掉 BossRush，进档后不撤离、不切场景，直接退到主菜单；重新打开 BossRush 再进档。合格：Player.log 有 `[BossRush] 装备抢救` 且 restored≥1，武器回到原槽，配件和耐久不变，右上角提示「已经放回」；撤离后再读档，武器仍在。不合格：武器没回来、多出一份，或仓库里多出问号空壳。
- `M_SKY_DELIVERY_01`：天空岛序章「云上的坐标」拿到航向仪后交付。合格：一次就成功，任务变成已完成，余额增加 5000（同时装了收益倍率类 Mod 时增加量可以不同）；再点不会再给钱。不合格：提示「奖金这会儿发不出去」，或每点一次钱多一笔。

## 2026-11-25 owner 成就页截图 + 水墨 UI 复核（022–038 Fixed L1/L2，014 复开并改正；C4、C8 不成立；正式真编译通过，L3 待 owner）

范围：owner 实机截图（成就页卡片左侧图标被切、右侧「已授」印与「√ 已领取」被切、统计栏一截金色胶囊浮着、卡与卡之间两道金线），加上随后三路只读复核（A / B / C 组）对 `1f24eec0` 全部水墨界面的排查。截图问题由像素几何与代码互证（图标露出约 27/54，正好左移 50）；其余由代码与坐标计算确认。证据（全节共用）：去掉 `BOSSRUSH_DEV_BUILD` 的正式真编译 0 错 0 警告，`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `DF819996…0242`，两份 `check_dll_identifiers --expect absent` PASS；全量守卫 797 PASS；相关 17 个隔离执行回归 PASS；未进游戏。

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-25-022 · P1 / COMPAT · Fixed L1/L2 | 手搭的 ScrollRect 内容节点 `new GameObject` + `AddComponent<RectTransform>()` 默认 sizeDelta 100×100，横向拉伸锚点下比视口宽 100、左右各溢出 50：成就页图标被切一半，右侧印章与「√ 已领取」被切；卡片左右墨笔边被视口遮罩裁掉，只剩上下两道横线，卡与卡之间看着是两道金线。Boss 池列表、图鉴列表同病。 | `AchievementView`、`BossFilter`（`bossPoolContent`）、`CodexView` 内容节点 `sizeDelta = Vector2.zero`，高度仍交给 ContentSizeFitter。新增 `RectTransformDefaultSizeGuard`：新建物体上取到的 RectTransform 若锚点拉伸某一轴、之后 60 行内既不设 sizeDelta 也不设 offset 就报红，无白名单；删掉成就页那一行转红于 `AchievementView.cs:413`，按字节还原后 SHA-256 一致，Boss 池与图鉴的内存探针同样转红。 |
| CR-2026-11-25-023 · P2 / COMPAT · Fixed L1/L2 | 进度底槽用 `Disabled` / `Surface` / `Header`，压在水墨深色面板上看不见，只剩一截填充，像一条浮着的金色胶囊（截图统计栏）。同样的底槽在成就条目、图鉴、收藏册、遗种巢、共享滑条、共享滚动条、Mode H 诊断条。 | 底槽统一改淡金 `Divider`：`AchievementView`、`AchievementEntryUI`、`CodexView`、`CollectionBookView`、`PetNestUILayout`、`BossRushUISlider`、`BossRushUI.ConfigureScrollRect` 滚动条槽、`ModeHUI` 诊断条。填充保持实心胶囊（试过横笔填充：右端收尖飞白，满格也像没满，已退回）。 |
| CR-2026-11-25-024 · P2 / COMPAT · Fixed L1/L2 | 2–4 像素高的细条用横笔贴图做填充：贴图 512×64 无 mipmap、双线性采样，缩到这么细只采到一两行纹素，落在飞白缝里就断成虚线，换分辨率还会闪；节拍器指针同样。 | 细条填充改 `BossRushUI.GetSolidSprite()`：Mode H 钟窗口与选手详情、遗种巢伴宠经验、随机事件进度、Mode G HUD；节拍器指针改 `SolidBar`。 |
| CR-2026-11-25-025 · P3 / COMPAT · Fixed L1/L2（014 复开） | CR-014 的修法不对：标题上移 16 后金笔在 275–283，仍低于卡顶 285，被后建的卡片整条盖住。 | 卡片改 224 见方、中心 y 152：卡顶 264，金笔（约 274–282）在卡顶之上；卡底 40 仍高于滚动名文本框顶 39。 |
| CR-2026-11-25-026 · P3 / COMPAT · Fixed L1/L2 | 远征总结卷轴换成深色纸后，深木色的轴身既贴不上纸边也压不出墨底，只剩两头铜帽浮着；翻牌正面长名字一路排到右上角，压住结果印。 | 总结卷轴上下轴身换古铜（`RodWood` 与 `RodCap` 之间取 0.55）；名字框右侧让出 60、单行自动缩字 16–24、省略号兜底。 |
| CR-2026-11-25-027 · P3 / COMPAT · Fixed L1/L2 | 天空岛 HUD 卡片的水墨强调竖条贴在 x 3–8，压在卡片墨笔边与蚀边上。 | 水墨分支把竖条挪到 x 9–14（中心 11.5），与共享 `CreateCard` 同位，文字列左缘 18.5 不受影响。 |
| CR-2026-11-25-028 · P3 / COMPAT · Fixed L1/L2 | 丧尸投资页标题金笔 360 宽、居中锚顶，落在左对齐标题的右侧，右段伸到余额列下面。 | 金笔改 240 宽、左锚、左端对齐标题字左缘（x 28），止于余额列之前。 |
| CR-2026-11-25-029 · P2 / COMPAT · Fixed L1/L2 | `StampSeal` 震动取 `shake.anchoredPosition` 当原点、结束时写回：入场动画还在推进时落印（Mode G 契约卡开场即点），取到的是半路位置，卡片永久停在那里；Steam 成就弹窗每帧由 `UpdateAllPopups` 写位置，震动写回的旧原点让弹窗跳一帧。 | 新增 `BossRushUIEntranceAnimation.IsPlaying`，入场没走完就不震；Steam 弹窗落印不再传震动目标。 |
| CR-2026-11-25-030 · P2 / COMPAT · Fixed L1/L2 | 冒险家日志切篇时按旧金笔的 `anchoredPosition` 当新笔中心，可 `PlayBrushWrite` 已把旧笔改成左端轴心、x 为左端坐标：每切一篇左移半笔长 270，第四篇起整笔被遮罩裁掉。英文标题收窄金笔时只改遮罩不改内层笔身，右端写不到头；卷首分类名两侧朱笔固定 ±124，英文长分类名被压住字头字尾。 | 标题金笔位置取常量 `ArticleTitleStrokePos`，建笔与重建共用；`FitEnglishBrushTitle` 同步内层 `Brush` 宽度；新增 `LayoutKickerStrokes`，按分类名实际字宽让位（最窄仍是原来的 ±92 内端），换文字后调用，拆除时清引用。 |
| CR-2026-11-25-031 · P3 / COMPAT · Fixed L1/L2 | 日志插图灯箱遮罩用 `CreateButton(..., interactable: false)` 建：不挂 onClick，点暗处关不掉；还被套了按钮墨笔边，整屏四边多一圈金线。 | 遮罩改成纯色铺满 Image + 无过渡 Button，点暗处关闭，关掉 Unity 导航。 |
| CR-2026-11-25-032 · P3 / COMPAT · Fixed L1/L2 | 许愿台首次打开时面板还没排版（高 0、标题框默认 200×50）就放墨：墨晕被跳过，标题金笔按折行前的旧框量宽和高，落在标题下方约 23 像素。 | `OnOpen` 先 `AdjustPanelForResolution()` 再 `PlayWishInkOpen()`。 |
| CR-2026-11-25-033 · P3 / COMPAT · Fixed L1/L2 | 许愿揭晓墨环固定在 (0, 36)，而停靠点带 ±0.3 格宽的随机偏移，墨环左右偏几十像素、还往上偏 36。 | 墨环中心取中奖柔光的实际 `anchoredPosition`（柔光已按轮带偏移 + 格子偏移对位）。 |
| CR-2026-11-25-034 · P2 / COMPAT · Fixed L1/L2 | 邸报 issue / deadline / tip 三张图标是给浅色纸画的深色线稿（PIL 实测深色像素 88% / 84% / 63%），摆在水墨暗纸 `PaperBase` 上几乎看不见。 | 只给这三张图在身后垫一片浅宣纸墨晕（图标 1.45 倍、α 0.86），其余浅色图标不动、贴图不改。重画浅色版属美术后续。 |
| CR-2026-11-25-035 · P3 / COMPAT · Fixed L1/L2 | `CreateSeal` 的英文字号是印宽×0.24，自动缩字下限却写死 9：36–40 像素的英文印（图鉴 SLAIN、成就 DONE、弹窗 RARE / FEAT）下限高于上限缩不下去，再被 `CreateText` 默认左右 4 像素边距挤掉，省略号截成「SL…」。 | 印章文字边距归零；自动缩字下限不超过上限，英文最低可到 7。 |
| CR-2026-11-25-036 · P3 / COMPAT · Fixed L1/L2 | 邸报签到「阅」印 30 像素、中心落在格内 (−12, −12)，左半压住 18 号粗体天号右侧；面板开着跨期时旧印不收，空格上还盖着上一期的「阅」。 | 印改 24 像素、斜钤右上角 (−8, −8)，出格 4 像素落在 10 像素格缝里；`RefreshSignInGrid` 对未签格收走旧印。 |
| CR-2026-11-25-037 · P3 / COMPAT · Fixed L1/L2 | 竖笔贴图上下九宫边共 168 像素，图鉴里程碑刻度与变异词条行强调笔按倍率 3 算边高 56，超过 16–24 像素的实际高度，首尾整体压扁。 | 两处倍率改按宽度换算（64/4、64/5），与 `CreateVerticalBrushStroke` 同口径。 |
| CR-2026-11-25-038 · P3 / COMPAT · Fixed L1/L2 | Boss 池列表行底 `SurfaceRaised` 压在 `Surface` 面板上只差约 1.1:1，又没有边，几十行连成一片分不出边界。 | 行底补一道卡片档细边（`Stroke` 压到 α 0.3），描边子物体 ignoreLayout，不进行内 HLG；开关行三态只作用在行底，不影响细边。 |

复核里不成立的两条：C4「远征总结印压住第 N 行」——远征最多 4 队、总结最多 6 行，印下沿够不到；C8「遗种巢满格改 Success 色变暗」——`Success` 是设计口径的完成色，在卡底上约 2.5:1 仍可辨，不改。

## 2026-11-25 水墨 UI 改动审查（62b03a57 + 未提交工作区；002、004–021 Fixed L1/L2，003 撤回；正式真编译通过，L3 待 owner）

范围：已提交 `62b03a57` 的共享水墨库与调用方，加上未提交的冒险家日志墨卷 / 插图、各集成界面、模式 HUD 与面板。002 由 owner 截图与代码坐标互证；003 已对照本机游戏 Mod 目录逐文件核对；其余由代码与坐标计算确认。审查时另一会话正在同一工作区写这些文件，未改代码。

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-25-002 · P1 / COMPAT · Fixed L1/L2 | 冒险家日志每一篇的正文都左右甩出纸面：左栏被卷轴遮罩切掉开头，右栏越过右边，底行压到页脚线；插图卡在原位。正文 TMP 是 `Page_Article_*`（已在 ±222, −27）的子物体，`ApplyColumnBox` 又写入同一组坐标，实际落在 ±444, −54。 | `WikiUIManager_InkView.cs` `CreateArticleColumn` 把正文 TMP 直接挂到 `pagesGroup`、按栏中心定位；`WikiUIManager.cs` 分页右栏只同步 `sizeDelta`，不再抄左栏偏移。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-003 · P2 / OPERATIONAL · Withdrawn（误报） | 插图清单引用的 `Assets/wiki_diagrams`（5）、`Assets/ui/Codex`（12）、`Assets/ui/ModeG`、`Assets/ui/ModeH`（各 1）没有部署规则，本机游戏目录 19 张全缺；`ResolveAssets` 静默跳过，天空岛 / 潮闸镇 / 末班编组站 / 蚀日花冠 / Mode G 战术图在游戏里不出现。`WikiImageAssetGuard` 只查网页端。 | 撤回：19 张插图都在已部署的 `production_icons` 包里（UnityPy 列包 0 缺，包与游戏目录 SHA-256 一致）；正式构建 `ModSprites.Get` 按原路径先取包、包在时不读散图，审查只查了散图目录。仍给 `WikiImageAssetGuard` 加判据 5（插图路径必须登记 `tools/production_icon_manifest.json`），反向验证转红后按字节还原。 |
| CR-2026-11-25-004 · P2 / COMPAT · Fixed L1/L2 | `CreateButton` 先由主题挂上按钮档墨笔边（ppu 240），`StyleInkButton` 复用同一节点只把 ppum 改成 2.4，边宽从约 16.7 缩到约 6.9 单位：日志目录卡、出征令、邸报、确认框、成就页的墨笔边成了细丝，选中金边几乎看不见。 | `BossRushInkSkin.AddInkFrame` 复用分支换回 `GetFrameSprite()`、Sliced、不填中心，墨笔边恢复原宽。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-005 · P2 / COMPAT · Fixed L1/L2 | 水墨主题下 Stroke 与 Accent 同为鎏金，只差 alpha（0.70 / 1.0），在 SurfaceRaised 上约 1.35:1：遗种巢当前崽、Mode H 选卡、许愿台输入焦点的选中与未选中分不出。 | 选中 / 焦点描边改用 UI 共识的 `WarningText`：`PetNestUIMotion`、`ModeHUIPageParts`（悬停时选中卡保持）、`WishFountainUI_Feel` 输入框。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-006 · P2 / COMPAT · Fixed L1/L2 | 许愿成功后「愿成」印章、余烬粒子挂在带 `VerticalLayoutGroup` + `ContentSizeFitter` 的面板下且没设 `ignoreLayout`，被当成布局行：面板末尾多出整宽金条，粒子每颗占一行；粒子场 `startBurning=true` 不熄，复用面板后每次打开都冒。 | `CreateSeal`、`SpawnRing`、余烬 `SpawnMote` 创建时自带 `LayoutElement.ignoreLayout`；许愿台余烬场改为只爆一次、不常燃。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-007 · P2 / COMPAT · Fixed L1/L2 | 许愿台打开时同一帧调两次 `AddLeftOrAlignedStroke`，第二次 `Find` 到待销毁的旧笔，第一次新建的笔永不删除：每开一次标题下多一道金笔并泄漏对象。 | `AddLeftOrAlignedStroke` 循环找旧笔，先 `SetParent(null,false)` 再销毁；`RefreshLocalizedTexts` 只在面板开着时重画金笔。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-008 · P2 / COMPAT · Fixed L1/L2 | 宿命回响入场页、弃局页、战报的标题金笔锚在面板中心，却按「从顶边往下」的 cursor 定位，落到面板中段横穿说明或契约卡（入场页约低 350 像素）。 | 入场页、弃局页、战报的 `TitleBrush` 改 `anchorMin = anchorMax = (0.5, 1)`，与顶边 cursor 一致。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-009 · P2 / COMPAT · Fixed L1/L2 | 装填节拍器判定窗口创建时宽 0 高 4，被判成竖条选了竖笔贴图；运行时拉成横条后两端约 22% 是软边，金色判定区看着比实际窄且边界模糊，影响按键时机。 | 判定窗口改用纯色硬边条（`SolidBar`，空贴图 Image），不再按尺寸猜朝向选墨笔。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-010 · P3 / COMPAT · Fixed L1/L2 | 带 `RectMask2D` 的墨笔旋转 90° 后，裁剪框按世界角点 [0]/[2] 算出负宽，整条被剔除：日志书缝、左栏典籍 / 篇目竖分隔线、许愿抽奖指针主线不显示（截图里纸面中间确实没有书缝）。 | 新增 `BossRushInkSkin.CreateVerticalBrushStroke`（普通 Image + 竖笔贴图）；书缝、左栏竖分隔、抽奖指针主线改用它，不再把遮罩笔转 90°。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-011 · P3 / COMPAT · Fixed L1/L2 | 打开面板时墨晕叠两层（峰值约 0.75），第一层按排版前尺寸画；`ImageViewerUI` 的 imageFrame 是拉伸锚点，墨晕被算成「父尺寸 + 1.22 倍」，成了全屏暗斑。 | 删掉 Mode G 入场 / 弃局、战报、Mode H 恢复页、Codex 详情的显式 `SpawnOpenBloom`，`PlayInkSurfaceOpen` 也不再另放；`SpawnOpenBloom` 固定中心锚点、按面板实际矩形定位（拉伸的看图器框不再成全屏暗斑）。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-012 · P3 / COMPAT · Fixed L1/L2 | `SpawnRing` 拿印章的 `anchoredPosition` 当中心锚点坐标，印章改成角锚点后墨环炸在卡片中部（成就领取、Steam 弹窗、邸报、宿命回响选中印与战报印）。 | 新增 `BossRushInkSkin.CenterInParent`；`StampSeal` 落印完成时按印章实际中心落环（调用方改锚点之后才算），成就领取波纹同样换算。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-013 · P3 / COMPAT · Fixed L1/L2 | `CreateCard` 强调竖条从 x 3–7 挪到 8–11（工作区又到 9–14），调用方仍按旧位置排：异色崽第二色条压住第一条只剩一色，且高度按建卡时 80 算死；伴宠 HUD 立绘框墨边压住身份色条。 | `AddIdentityRail` 读强调竖条实际贴图、宽度与位置，按槽位排在右侧并竖向拉伸，返回右沿；立绘与文字随右沿右移；伴宠 HUD 同步 `_portraitX`。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-014 · P3 / COMPAT · Reopened → 025 | 孵化揭晓标题框占 y 256–300，卡片顶边 285 且后建在上层，「遗种破壳」下沿与金笔被挡 15–29 像素。 | 标题 y 改 `SurfaceSize.y * 0.5 − 26`（上移 16），下沿与金笔不再被卡片挡。（2026-11-25 复核：金笔仍低于卡顶、被盖住，改正见 CR-2026-11-25-025。）证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-015 · P3 / COMPAT · Fixed L1/L2 | 远征翻牌共用一张卡，阵亡时挂上的余烬场不关，之后归巢的崽（连背面）也飘红灰余烬。 | 新增 `StopCardEmbers()`，翻到背面和非阵亡正面时熄火。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-016 · P3 / COMPAT · Fixed L1/L2 | 音效重复：一键领取 N 个成就同帧 N 声落印（注释写整批一次）；收藏册 `RebuildCards` 每次刷新都走 `SelectCard`，打开、切系列、状态变化自己出声和墨环；邸报落印音放两次。 | `StampSeal` 增 `playSfx`，一键领取只第一枚出落印声；收藏册 `SelectCard(byPlayer)` 只在点卡时出墨环与笔扫；邸报签到波纹不再带音效。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-017 · P3 / COMPAT · Fixed L1/L2 | 看图器「墨藏」印章比 MainImage 先建，被图盖住大半；看图器开着时换图在 `wasVisible` 早返，标题金笔还是旧标题宽度。 | 印章在 MainImage 建好后 `SetAsLastSibling`；`ShowRoot` 在 `wasVisible` 时也按新标题重画金笔（不重放书写）。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-018 · P3 / SAFE · Fixed L1/L2 | 邸报先设 `PaperBase` / `PaperCard` 与按钮色，随后被 `ApplyInkPanel` / `StyleInkButton` 覆盖；`DailyReportPresentationGuard` 仍断言并按这些未渲染的颜色算对比度（§4.10），按其模型 `PaperInkSoft` 在实际墨底上约 3.7:1。 | 纸色、卡面色挪到 `ApplyInkPanel` 之后设，`ButtonIdle = CinnabarFill`；`DailyReportPresentationGuard` 加先后顺序与按钮色断言，守卫复算的是屏幕上的颜色，反向验证转红。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-019 · P3 / COMPAT · Fixed L1/L2 | 危险确认键标签色按调用方原始 Danger 算，底色却已换成 DangerFill，打开即宣纸白字而非设计的浅朱字。 | `CreateButton` 标签色改按 `button.colors.normalColor`（已换成 DangerFill 的实际底色）算。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-020 · P3 / COMPAT · Fixed L1/L2 | 出征令 HUD 变天时天象行先全不透明显示，0.35 秒后突然变透明再淡入，「出现、消失、再出现」。 | 加动效前先 `label.alpha = 0`。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |
| CR-2026-11-25-021 · P3 / COMPAT · Fixed L1/L2 | 宿命回响「契合」印（52 像素）下沿在卡顶下 58 像素，压住从 45 像素起的契约说明第一行右端。 | 印章缩到 40 见方、上移 2，不再压说明首行。证据：正式真编译 0 错、全量守卫通过；L3 待 owner。 |

未证实线索（UNVERIFIED，2026-11-25 已按防御式修法一并处理，L3 待 owner）：关面板淡出与复位不再写根画布缩放（`IntegrationUIFeedback.ScalableRect` 跳过带 CanvasScaler 的根画布）；邸报报头右侧让出 60 像素印宽，报头金笔随居中点左移 30；友好名牌关克隆体 Graphic 时跳过名字整棵子树（`FriendlyNpcNameplateGuard` 同步改断言并反向验证）；`SpawnOpenBloom` 固定中心锚点（并入 011）；`CreateHeaderTitle` 框高不低于字号×1.45+4；日志图库页脚框高 20。

顺带（不影响运行，2026-11-25 已改）：天空岛警示线高度收成常量 `WarningRuleHeight = 5`，创建与字幕显示都用它；`ModeHUIPages.cs` 的滚动列表改用共享库属有意取舍，在 `Common/UI/AGENTS.md` 补「滚动列表用共享库 ScrollRect」例外并记 `docs/ai-docs-migration.md`；出征令 HUD 静态缓存注释改成与 `Hide()` 清空一致。审查时全量守卫 790 PASS / 2 FAIL（另一会话 `ArenaTrialsFx.cs` 未登记），修复后全量 796 PASS / 0 FAIL。

## 2026-11-25 玩家反馈：叮当重铸强度过高（001 Fixed at L1/L2；L3 待验）

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-25-001 · P2 / COMPAT · Fixed L1/L2 | 重铸每次在当前值上累加、只夹在预制体值 30%~200%，倾向拉满（90% 正向）只多收价值的一半。最低投入反复重铸，约 8 次就能把全部未锁属性推到 2 倍：伤害、射速同时翻倍，火力超过 4 倍。原值 1.0 的倍率被当整数取整，只在 0 和 2 之间跳。 | 改为每次从预制体原值重抽，上下限 ±50%（0 值 ±0.25，护甲 ±1），|值|<2 按小数处理，整数上下限取整。旧装备读档时夹进新上限，存档差值不改写，寄存 / 快递同步不写回被夹住的旧差值（owner 2026-11-25 同意）。`EntryAndReforgeCompatibilityGuard` 与同名执行回归覆盖并做反向验证；相关 8 个执行回归通过。 |

实机清单（L3，未执行；没有启动游戏、没有读写存档）：

- `M_REFORGE_CAP_01`：拿一把没重铸过的枪，倾向拉满、最低投入连续重铸 20 次。每条属性都应落在详情页原值的 50%~150% 之间，出现 Max 时正好是 150%；后坐力出现 Min 时正好是 50%。不合格：任一属性超过 150%，或多次重铸后数值整体越来越高。
- `M_REFORGE_CAP_02`：用 2.6.2 及以前重铸到 2 倍的旧装备读档。详情页数值应显示为原值的 150% 并带 Max；在仓库、寄存、快递之间各走一趟再读档，数值不变。不合格：仍显示 200%，或寄存后数值掉到原值。
- `M_REFORGE_CAP_03`：对旧装备一条超限属性用冷淬液锁住，再重铸其他属性。锁住的那条保持 150%，其他属性照常在新范围内变化。不合格：锁住的属性变化，或重铸报错。

## 2026-11-24 玩家反馈：尸潮抽搐扎堆与打不死、龙裔卡墙、叮当停留（001–004 Fixed at L1/L2；L3 待验）

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-24-001 · P1 / COMPAT / WIRE+ · Fixed L1/L2 | 尸潮第 5 波起丧尸一抽一抽、不贴身，挤在实验室出生大厅、农场镇拐角、雪地图和零度挑战的屋外空地。官方 `TraceTarget` 每 0.15 秒调 `AI_PathControl.MoveToPos`，它先取消在途请求再排队；上百只丧尸同时追玩家、A* 积压超过 0.15 秒时，路径几乎都算不完。 | `ZombieModeCrowdNavigation` 前缀只拦本局在册丧尸：在途请求不取消（2.5 秒超时除外），近身 0.15 秒、远处按距离到 1 秒，无路径、到终点、被瞬移或目标大幅挪位时立即重算；模式未激活第一句放行。执行回归 `ZombieModeCrowdNavigation` 用 0.4 秒回调延迟复现「不节流 0 条路径送达」并证明节流后送达；守卫 `ZombieModeCrowdNavigationGuard`。 |
| CR-2026-11-24-002 · P1 / COMPAT · Fixed L1/L2 | 20–30 波时怪「打不死」（戴龙王头盔也一样）。出生点和远距恢复点都取离玩家最近的同一坐标，丧尸落地即叠罗汉；站在别人头顶的丧尸高出子弹平面，命中判定打不到，还堵门卡住整波。 | 出生与恢复落点记 2 秒，被占时按三环找可达、同层、不隔墙、不更靠近玩家的空位（每只最多 8 次查询）。每秒恢复巡检连续两次看到丧尸脚下是别的角色，就用 ECM2 拨到旁边可达地面并重新寻路。安全区兜底弹出同样走散开。复查补源头预防：登记时给丧尸挂 ECM2 碰撞行为回调，别的角色不可站、不可跨上、不可停靠，叠上去会滑下，巡检拨离只兜底。`ZombieModeSpawnRuntime` 散开用例、`EnemyRecoveryRuntime` 巡检断言、`ZombieModeSafeZoneRuntime` 兜底断言、`AuditModeLifecycle` 登记断言；反向验证转红。 |
| CR-2026-11-24-003 · P2 / COMPAT · Fixed L1/L2 | 风暴区黑市鸭王杯里，龙皇「孩儿护我」召唤的龙裔有概率卡在墙里。鸭王杯、Mode E、宿命回响不用场景刷怪点，后备落点固定取起飞点世界 +Z 5 米，不做任何落地校验。 | 起飞点向 8 个方向、按 5 / 3.5 / 2.2 米找点，要求在导航面上、与起飞点连通、同层，起飞点到候选点和吸附点的射线都不碰官方墙体层；都不行就落在起飞点。守卫 `DragonKingDescendantSpawnPlacementGuard`，两处破坏反向转红。 |
| CR-2026-11-24-004 · P3 / COMPAT · Fixed L1/L2 | 和叮当对话、重铸或关掉商店后，他只站 10 秒就走；玩家翻背包挑下一件时人已经走远。 | 默认停留改为 150 秒，聊天与重铸入口改用该常量；商店、重铸界面关闭（`showFarewell`）时至少停留这么久。再次交互、剧情触发照旧打断；婚礼、跟随的短停留不受影响。停留期间移动组件丢弃寻路，所以两个召唤入口先打断停留，否则召唤道具会失效 2.5 分钟。守卫 `GoblinPostDialogueStayGuard`；`GoblinRuntimeModule`、`AffixSelectionUI`、`ReforgeAssetTransactions` 回归通过。 |

实机清单（L3，未执行；没有启动游戏、没有读写存档）：

- `M_ZOMBIE_CROWD_01`：尸潮模式分别进实验室、农场镇、雪地图、零度挑战，打到第 8 波以上，站在离出生区 30 米外的走廊或屋内。丧尸应沿路追来、贴身攻击。不合格：同一位置有丧尸原地抽动超过 10 秒，或出生区、门口出现两只以上叠在一起。
- `M_ZOMBIE_CROWD_02`：打到 20–30 波，分别不戴和戴龙王头盔，向门口的丧尸群连续射击。每只被打中都应掉血、能打死。不合格：有命中特效但血量不降，或某只丧尸站在别的丧尸头顶超过 3 秒。
- `M_DRAGON_CHILD_01`：风暴区黑市鸭王杯，选有龙皇参赛的场次，看龙皇飞起召唤龙裔，至少看 5 次（包括龙皇贴墙时）。龙裔应站在龙皇起飞点附近的空地上并能走动。不合格：龙裔身体陷进墙里，或站着不动。
- `M_GOBLIN_STAY_01`：和叮当对话打开重铸，关掉界面后开背包翻 2 分钟。叮当应一直站在原地，约 2.5 分钟后才走；中途再按交互键应正常打开对话。不合格：3 分钟内自己走开，或再交互没有反应。再做一次：关掉重铸界面后走远，用召唤道具叫他，他应立刻跑过来；不合格：站着不动。

## 2026-11-20 退出重进后武器缺失、仓库问号

| ID / 状态 | 可复现条件与影响 | 修复与证据 |
| --- | --- | --- |
| CR-2026-11-20-001 · P1 / COMPAT · Fixed L1/L2 | 冷启动首次物品树请求只带装备包的武器时，旧注册链先发布 prefab，随后才有机会登记装备配置器。官方角色槽检查标签，拒收后再保存可能漏掉该武器。仓库能容纳同编号空壳。 | 在首次按需加载前登记配置器。生产注册表及补丁的隔离往返验证覆盖主武器、嵌套配件、耐久、仓库原格和数量。移除配置器预登记时武器槽断言失败。 |
| CR-2026-11-20-002 · P1 / COMPAT · Fixed L1/L2 | 工厂会把官方拒收或仅部分成功的包记成已完成。代码克隆和特殊物品会在真实 prefab 尚不可用时锁住后续尝试。刚取消的异步请求还可能在读档接手后卸掉包。 | 逐物品确认官方表、按需补发、按实例管理克隆和资源请求。执行回归覆盖部分拒收、Unity 已销毁对象、异步跨帧、启用路径和挂起回调。 |
| CR-2026-11-20-003 · P1 / COMPAT · Fixed L1/L2 | 六件物品的模型使用独立资源包。物品先登记时，按 TypeID 早返会跳过模型。近战武器还可能把同编号另一件物品的手持绑定当作自己的绑定。 | 冷读档检查当前 prefab 实际绑定的模型，按需补齐独立模型包。同编号、同模型的近战武器共用手持模板，逐件核对绑定。执行回归覆盖先登记物品、同编号更换 prefab 和模型销毁。 |

当前游戏程序集佐证角色读档与仓库读档的装配顺序。反馈来自玩家，尚无该次会话的日志，未启动游戏或访问玩家存档。以上是已复现的代码缺陷；具体反馈属于哪条路径仍待玩家侧证据。

## 2026-11-19 收集品与收藏册审核（001–007 同日 Fixed at L1/L2，已部署；未提交，L3 待验）

范围是 v2.6.0 尚未提交的整批改动：`Integration/Collectibles/` 12 个 `.cs`、本地化、F3 面板、三图开箱钩子、注册链与台账、守卫与夹具、Wiki 与文档，连同同批的旧物品重画图标接线。逐条根因、失败场景与已证伪项见 [审核报告](docs/reports/reviews/2026-11-19-收集品与收藏册审核.md)。没有 P0 / P1；掉落主链路、存档编解码、杰夫 590217、订阅与缓存清理、加成不叠加都逐条核过。审核没有改源码、没有进游戏、没有提交。

| ID / 分类 | 已确认问题 | 建议修法 |
| --- | --- | --- |
| CR-2026-11-19-001 · P2 / COMPAT | `Integration/Collectibles/CollectibleItems.cs:46-70`、`CollectionBookItem.cs:35-59` 只写 `Quality`，`DisplayQuality` 沿用克隆链（遗种蛋 ← 便携安全区装置，实包模板 DQ6，见 `tests/fixtures/TidegateTreasure/FallbackTemplate.json`、CR-2026-11-08-003）。官方 `ItemDisplay` 按 `DisplayQuality` 画光晕：1 档公交月票和 8 档金蛋一样是红光晕，和收藏册内按 `Quality` 的配色对不上。 | 两个 `ConfigureItem` 按 `Quality` 写 `DisplayQuality`（官方枚举 1–8 一一对应）；`CollectibleLoot` 链生产注册链与模板，断言二者一致。L1+L2，颜色待 L3。 |
| CR-2026-11-19-002 · P2 / COMPAT | `CollectiblesRuntimeModule.cs:146-158`：`Commit` 成功、奖金已发之后，`AnnounceRegistered` / `jeffIntro.NotifyChanged()` 不在 try 里。官方任务刷新一抛错，`CollectionRegisterContainer.cs:112-113` 就把 `taken` 当 null，`:115-124` 把整批收集品退回：存档记已登记、奖金已到账、物品还在手上。 | 提交后的副作用逐项 try/catch，保证一旦提交就收走物品，或让 `Finish` 看「已提交」标志；补「提交后抛错仍收走、只发一次钱」与「提交失败全退、不发钱」两条故障注入。L1。 |
| CR-2026-11-19-003 · P3 / OPERATIONAL | 游戏目录 `BossRush.dll` 是 10:53 的 `0241b20e…`，`Build/` 是 11:10 的 `db274963…`（无 Dev 标识）。11:04 起 15 个图标接线文件又改过，交接里「与 Build/ 一致」已不成立，现在进游戏测到的是旧包。 | 关游戏后重跑正式构建并部署，按 D 盘路径核对 SHA-256 再交实测。 |
| CR-2026-11-19-004 · P3 / COMPAT | `CollectiblesRuntimeModule.cs:203-218` 只要找到商店就置 `shopInjectedThisScene = true`，不看 `TryInjectIntoShop` 的结果（`CollectionBookItem.cs:87`、`:93` 都可能返回 false），没挂上这趟基地就不再试。 | 真挂上或已存在才置位，否则按 2 秒节流重试。 |
| CR-2026-11-19-005 · P3 / SAFE | 文案与文档六处：挖沙船船铃英文 "Boss Sand"，已有译名是 "Leadline, the Dredge Boss"（`Localization/CollectiblesLocalization.cs:215-217`）；道岔扳手「司机室」被译成 "switch cabin"（`:239-241`）；Wiki 写「没登记的只显示剪影」，界面实际显示名称、描述与价值（`CollectionBookView.cs:418`、`:432`）；ID 表表头仍写 500001-500118（`docs/reference/Bossrush使用物品ID表.md:3`）；英文工坊简介 `b.md` 缺中文 `a.md:79` 的功能条目；收藏册进了仓库时杰夫仍说「册子你拿着」（`CollectionJeffIntro.cs:103-107`、`:142`）。 | 逐条改。Wiki 归另一个会话；`b.md` 补一行后仍在 7700 字节内。 |
| CR-2026-11-19-006 · P3 / SAFE | `tests/fixtures/TidegateCargo/Stubs.cs:159`、`LastShiftCargoRuntime/Stubs.cs:444` 说回归会核对「每只箱子只交一次」，实际没有断言读 `Notified` / `LastCap`。登记容器与官方箱门控只有结构守卫。 | 两个开箱回归断言交付次数与上限；`CollectibleLoot` 加登记事务夹具（含 002）。 |
| CR-2026-11-19-007 · P3 / COMPAT（需 owner 拍板） | 按代码权重模拟：零号区系列凑齐中位 32 件，「竞技场传说」中位约 397 件（每趟上限 3，至少 130 趟）。两组全图系列的三件 6 档单卖合计 76,500 / 93,431，都高于集齐奖金 60,000；「枪械暴击率 +1%」在官方公式里是乘在暴击率上（`ItemAgent_Gun.cs:1459`）。 | 由 owner 定：调高全图系列奖金或加成，或提高 6 档在全图系列的份额。 |

**UNVERIFIED（本轮，详见报告第 3 节）**：登记容器根对象不跨场景，开着时卸场景会连物品一起销毁（能否触发待实机）；收藏册界面过图不收、「登记」按钮不重算（图鉴过图会收）；天空岛靠活动场景身份走自研回调，需 `M_COLLECTIBLES_02` 的日志确认没走官方路径；`OtherModeActive` 是 EclipseBloom 门控的副本。已证伪：Mod 自建箱误判、满箱扩格界面不刷新、仓库满退回丢物、加成重挂缩包。

**验证**：
- 守卫与回归：`run_guards.py --changed-only` 417 个全部 PASS；`CollectibleLoot` 回归 PASS；`LedgerSizeGuard` PASS。
- 静态核对：ID 表 100 行与代码逐项一致；100 个图标、生图说明、图标清单都覆盖了；`compile_official.bat` 全文 CRLF；工坊简介 7426 / 7558 字节。
- 没做的：本审核没有复跑 Windows 编译（`Build/` 11:10 的产物晚于全部改动）；没有 L3。

写入前台账已到 1197 行，2026-10-08 至 10-11 共 14 节已闭环原文逐字归档到 `archive/CODE_REVIEW_FINDINGS_2026-10.md`（块 SHA-256 `f5df1df2…57eaee`）。

修复追记（同日，owner「全部修复」；详情见 FIX_TRACKER 同日条目）：
- 001 显示品质按档次写；002 改走 `CollectionRegistration.Apply`，写成功后逐步 try；003 正式构建已部署，`Build/` 与游戏目录都是 `c2f65247…1b6a`；004 售货机真挂上才收工；005 代码与文档改完，Wiki 措辞交给 Wiki 会话；006 三组回归补了断言。
- 007 由 owner 授权按好玩优先定：集齐奖金不低于整套卖价（3 万 / 15 万），新件优先 50%，竞技场系列加成改爆头伤害 +5%（主角枪械只有爆头算暴击，`ItemAgent_Gun.cs:1460-1463`）。
- 线索加固：官方路径按箱子所在场景排除 Mod 图，登记容器 DontDestroyOnLoad 并在读条开始时结算，过图收起收藏册。天空岛实际走哪条路径仍以 `M_COLLECTIBLES_02` 的日志为准；`OtherModeActive` 副本未改，它碰的是 EclipseBloom 及其夹具，不在本次范围。
- 验证：三组回归 PASS；改坏 8 处全部转红，按字节还原后哈希一致；Windows 正式编译通过。未实机。

## 2026-11-13 双地图官方复用复核（004–014；修复已落地，新画面 L3 待验）

详细根因、复用清单、验证与人工步骤见 [双地图报告](docs/reports/reviews/2026-11-13-双地图官方复用与制作规范复核.md)，后续实机反馈见 [地面与光照报告](docs/reports/testing/末班编组站_2026-11-13_地面与光照反馈.md)。维护方没有启动游戏；owner 的旧版 11 PASS 只证明该次演练，产物哈希与视觉结论分别记录。

| ID / 分类 | 已确认问题与修复 | 证据边界 |
| --- | --- | --- |
| CR-2026-11-13-004 · P2 / COMPAT / WIRE+ | 两图自定义日照覆盖官方天气，室内环境灯未完整覆盖原生角色。共享原生太阳 owner、SodaPointLight 组件及活动相机通道，潮闸建筑补光；原始场景卸载前捕获资源，profile 私有化。 | L1/L2：官方代码/安装资源，双地图补光 12 场景、日照 9 场景及反向验证；精确晴天 profile 缺失时有显式降级，画面仍待 L3。 |
| CR-2026-11-13-005 · P2 / COMPAT | 两图高射线探地可能误取桥面；末班可走浅池缺物理 Water。限定本图地面、近脚朝上命中，浅池补原生触发体；潮闸不可走实心池保持原样。 | L1/L2：生产 World 夹具及实际包标记/网格复算；桥上/桥下、脚步和实际通行待 L3。 |
| CR-2026-11-13-006 · P2 / COMPAT | 末班侧柜只冻结带入钢印数量，可丢旧换新绕过；部分货物创建失败仍可能消耗资格。冻结带入实例、隐藏库存核验和 Reservation 提交；官方成员听觉和技能资源链接线同时补齐。 | L1/L2：生产货箱、失败回滚/换印反向与成员听觉回归；现场交互、音效待 L3。 |
| CR-2026-11-13-007 · P2 / COMPAT | 潮闸阀门先扣物、关键箱部分失败、临界涨潮只停用物件，可能丢关键物或让已开窗口/箱状态残留。预留后提交、关键库存实物验证、匹配 LootView 关闭和原生栈退出后恢复状态。 | L1：官方事件顺序与独立复核；执行回归及最终构建见详细报告，窗口瞬间开关声/显示仍待 L3。 |
| CR-2026-11-13-008 · P2 / COMPAT | 潮闸 M 图只按出口状态更新，切换语言不刷新现有标签；末班缺原生撤离图钉。按语言/状态调用官方 Setup，末班按真实条件维护原生出口标记。 | L1：生产入口、官方缓存/注销重登路径；地图双语与状态一致性待 L3。 |
| CR-2026-11-13-009 · P2 / COMPAT | 潮闸实际包存在外露的异材质共面细节；材质对汇总跨多个节点/高度，首例修复不足。精确分离 13 视觉节点并提供重建后修复工具、实包属性守卫及全图代表点复扫。 | L2：非目标字节一致、重复执行一致、旧包反例、最终包几何/shader/室内检查通过；动态水面与实际相机闪烁待 L3。 |
| CR-2026-11-13-010 · P2 / COMPAT | 原生 SodaPointLight 四个覆盖球叠加，旧暖 HDR 色使室内身体过亮；“接到官方组件”不能证明曝光。两图共享补光改官方中性色与房灯衰减，后备灯减弱，补充总贡献上限及房形覆盖回归。 | owner 旧画面和实际 shader 为根因证据；修正 L1/L2、13 场景及负向通过，最终曝光待新 L3。 |
| CR-2026-11-13-011 · P2 / COMPAT | 末班污迹同时来自四组磨损贴花和 atlas 椭圆/飞溅画笔，修好透明不等于观感合格。精确取消目标装饰、同步源和实包，保护非目标像素/对象；天气灰雾仅补只读诊断，不无证据改官方天气。 | 污迹来源已确认，资源重放及反向证据见后续报告；灰雾现场天气未知，不能称已证明是阴天或已修复天气。 |
| CR-2026-11-13-012 · P2 / COMPAT | 仓库剪票入口包缺实体；末班矮物过滤漏箱柜/宽柱脚、门框未装配；潮闸 marker 重叠删家具碰撞。按作者几何补当前包代理并纠正制作输出，保留实际门路与交互净空。 | 真实制品/生产生成器对照、属性与运行时回归；详见 [本批报告](docs/reports/testing/仓库入口_2026-11-13_实体对话与重进.md)，通行和弹道仍待 L3。 |
| CR-2026-11-13-013 · P2 / COMPAT | 余响完整对白/交票/登车借自制确认框，未复用官方对话。改真实 DialogueActor + 共享 DialogueManager + 官方 DialogueUI，按显示 owner 处理取消，区分提交前取消与提交后结果页关闭。 | 生产入口回归及共享对话消费者回归，原版 UI 视觉与焦点待 L3；不改存档键/经济。 |
| CR-2026-11-13-014 · P2 / COMPAT | 去过编组站后入口 M 标记被隐藏；关闭中旧会话与异步全局 owner 存在阻断/误回收风险。改每趟标记、真实 Scene 归属与实例 generation，迟到请求只回收自己。 | L1/L2；旧日志第二次 ENTRY_READY/YUXIANG_READY 已出现，现场“物件消失”的唯一根因仍未证实，三次往返待 L3。 |

## 2026-11-13 后续实机：官方室内灯与装饰草（003 Fixed at L1/L2；新画面待验）

- **CR-2026-11-13-003 · P2 / COMPAT / WIRE+**：owner 新图确认室内环境亮、人物仍暗；现场 DLL 与地图已更新到上一轮版本，排除旧包未替换。此前“建立普通 Light 即能满足角色亮度”的验收依据不充分。官方 `SodaPointLight.cs` 写 `_LightColor/_Hardness/_FallOff/_EnviromentTintOn`，实际仓库有 59 个外场 / 49 个地下室网格灯（单位球 `SodaPointLightMesh`、`ZeroPoint` 占位材质、SodaLight 层），活动 URP renderer 在 `BeforeRenderingDeferredLights` 以官方 `SodaPointLight` 材质覆盖绘制、读取 GBuffer 补光。普通点光与该通路不是同一实现。
- **修复**：`LastShiftLighting` 改为官方网格灯，转场前用停用 DDOL 节点持有 mesh/material；`LastShiftSession` 在 `lease.BeginLoad` 前准备，世界材质核验后绑定。只读实际相机的 rendererFeatures 校验该 pass；不可用才记录一次回退。三个室内区的角落覆盖按官方球半径 0.91 / 衰减复算，出屋关闭、换屋复用、取消和离场释放自己的节点，官方共享资源不销毁。记录 `INDOOR_FILL_READY mode=SodaPointLight layer=15`，日志不能替代画面验收。
- **草的来源**：五组草/芦苇共 8451 三角面来自地图原型 `yard-details.mjs` / `ambience.js`；初版、上一轮前及上一轮后包的五个 Mesh 原始字节完全一致。官方幕布 `Single` 会先卸旧图，本图无 Terrain / TastyGrass owner；克隆的官方天气子树也无植被。未发现官方旧图残留证据，不把此草归为旧地面闪烁根因。按 owner 要求关闭五个渲染器，源 FBX 删除对应 leaf 网格、原型停止输出并保留其余随机序列，旧 GLB 组装也过滤；其余 5466 个包对象原始字节一致，地贴与原共面修复复验通过。
- **验证**：相关守卫 75 PASS；LastShift 4 回归 PASS（新灯光夹具含 8 场景）；缺跨图保护、漏出屋关闭，以及卸图前捕获/官方网格装配的隔离反向均转红后按字节恢复；Windows 正式编译、Dev 标识缺席、场景判包通过。当前修复未在游戏里运行，目标 DLL / 地图哈希与部署边界见 FIX_TRACKER 同日后续条目。
- **L3**：重新进司机室 / 维修棚 / 押运库，穿同一套装备在门内、中心和远角转身；M_LASTSHIFT_03 看身体是否仍黑与出屋是否残光，回传上述日志。M_LASTSHIFT_READ 到货台院落、沟边和水池边，草叶仍穿出地坪即不合格。此前现场图是旧方案失败证据，不算本次修复通过。

## 2026-11-13 末班编组站第三轮需求验收（001–002 Fixed at L1/L2；实机待验）

范围：owner 四项要求——地面闪烁、室内角色本体照明、押运长靠近签收处、签收至少 7–8 件且保底几件精品。审查当前未提交改动及实际发布包，不把历史修复记录当成验收结果。

- **CR-2026-11-13-001 · P1 / COMPAT · 换台时押运长无法在签收窗口内到达对侧。** `LastShiftYard/LastShiftEnemies.cs:682` 的 `GuardCargo` 到列车进站锁线时才更新驻点，再经 `MoveToPos` 走过去；`LastShiftYard/LastShiftTrains.cs:306` 同时按实际轨道包络封闭导航，`LastShiftYard/LastShiftLoot.cs:547` 在同侧 Dwell 即开放签收。直接读取当前 `lastshift_raid`（SHA-256 `b1b6d708f703bffd5b589888425a5fed93bddae9c70e491f8734e3ed2c0bc86c`），按游戏 A* Int3 坐标焊接及反向共边连接、生产 `InEnvelope` 判据复算 832 顶点 / 1217 三角形：西线占用封 122 节点、东线占用封 119 节点，两种状态均为驻点可站但东西驻点及对侧签收位不连通；空线才全通。作者预烘掩码会给出可达，不能替代这条运行时判据。首班默认西台后扳东即可触发该路径；同侧随车落点可达，不扩大为所有班次都失效。**建议：**保证签收期间的实际跨台路线可达，或在封线前完成换岗，并用运行时封线规则回归。**验证：**主代理复跑 `python Build/lastshift-chief-nav-review.py`，L1 + L2；实机是否被玩家战斗牵引到别处未测，不能消除普通站岗路径缺陷。
- **CR-2026-11-13-002 · P2 / COMPAT · 部分装箱失败仍消耗整趟签收机会。** `LastShiftYard/LastShiftLoot.cs:560–573` 只检查 `added == 0`，任意一格成功即写 `CargoClaimed = true`，没有核验实际达到 8/9 件及至少两件 Q4 以上；`:591–604` 精品池无货会跳过，再由 Q2–Q4 普通池补位。`SkyIsland/SkyIslandRewardCrate.cs:308–336` 在资源、实例或入箱失败时返回 false，调用方没有补货或保留欠账。**影响边界：**现货与装箱正常时计划满足普通台 8 件、加价台 9 件、头两件 Q4 以上；本次未证明常规现货池发生短缺，不能说每局都会少给。**建议：**按实装件数和精品数完成签收事务，失败补足或保留可重试状态，防重复领取。**验证：**L1 条件路径已确认，当前 LastShift 演练不实际签收，待补故障路径执行回归与实机开柜核数。

**视觉线索（UNVERIFIED，不等于已复现新版闪烁）：**地图包的四块磨损地皮已恢复顶点透明度，透明队列 / 混合 / 不写深度均已进入游戏部署包。最终包仍有很小的同层共面几何：`Connected yard / low objects 2` 子网格 0 的三角 1747 / 1758，重叠约 0.01591 m²，顶点 RGB 不同、alpha 均 255；较小面 0.01982 m² 被 `tools/build_lastshift_blend.py` 的 0.02 m² 单面阈值排除。这不证明原五张图的大色块仍在闪，需现场转镜头复测。室内四点光与官方角色 GBuffer 接线及三间房覆盖未发现阻断项，最终亮度仍待 L3。

本轮验证：相关守卫 59 PASS；LastShift 执行回归 2 PASS；在隔离源码与 Managed 副本上运行原 `compile_official.bat`，正式配置 Windows 真编译通过，未改游戏目录；地图包作者 / 仓库 / 游戏三份哈希一致。未修改玩法代码，未启动游戏或访问玩家存档。

修复追记（2026-11-13，owner「全部修复」）：

- **001 Fixed at L1/L2**：首个可签收班次锁线后才在实际铅封柜后 3.2 m 生成押运长；普通兵和围圈小队预留他的名额。已在场时扳岔提前换岗；进出站仍整线封闭，停靠按每节车身加 0.65 m 净空的三角形相交封路，发车 / 进站撤销过期路径。生产判据复算实际包全部 1217 个三角形，东西 × 普通 / 重载四种状态与独立几何判据零差异、跨台均可达。F3 同步检查封闭区与绕行区，缓存区分普通 / 重载。后续班次临近锁线才切台时仍需正常步行，未承诺停靠瞬间就位；交火与实际走路耗时留 L3。
- **002 Fixed at L1/L2**：封签货在停用父节点下装柜，实物件数、TypeID、库存归属与品质全部核对后才开放并写签收；缺货、创建 / 入箱失败、监听器异常与会话关闭时整柜取消，保留签收机会，重入不会重复领奖。`LastShiftCargoRuntime` 链接生产 loot / 班次 / 共享建箱与装箱代码，11 场景含 3 班单 × 普通 / 加价、故障及清理路径通过；去掉实物品质校验后用例转红，按字节还原后通过。
- **补光**：保留已有每屋四盏真实点光，将会话更新改为先更新本帧房间脚点，再开关角色补光；倒置顺序的隔离反向测试准确转红。最终亮度仍待上述人工项。
- **地图**：去掉会漏小面与窄交集的面积门槛，补空间分桶边界；源脚本、FBX 分层及发布包独立转换均有回读证据。Unity 因许可证服务 IPC 超时退出 199，未取得新 Unity 构建；发布包通过 UnityPy 仅更新不透明网格分层，具体证明与哈希见 FIX_TRACKER 本轮记录，不能称 Unity 重构建成功。
- 正式 / Dev 两配置均通过原 `compile_official.bat` Windows 真编译，1352 个快照源与当前源码字节一致，29 个 Dev 标识在正式版全无、Dev 版全有；三个 LastShift 回归 PASS。未启动游戏、未访问玩家存档，未提交。

后续实机清单（截图由 owner 目检；以下为建议文件名，尚未生成。M 编号对应 `Assets/Data/GameplayCoverage.json`，V01 为本轮补充项）：

| 步骤 | 操作及观察位置 | 建议文件名 | 不合格条件 |
| --- | --- | --- | --- |
| V01 地面 | 回到原五图的候车棚旁、钢轨堆场、仓库外及跨线桥下；用移动键慢走并转动镜头，盯地面深绿 / 土黄边缘 | `LSY_V01_ground_01.png` 至 `LSY_V01_ground_05.png`；动态闪烁另记发生位置 | 色块随镜头跳变或互相覆盖；静态截图本身不能证明不闪 |
| M_LASTSHIFT_03 人物 | 同一套装备进出司机室、维修棚、押运库，走到门内、中央和最远角并转身；看玩家身体而不只看地板 | `LSY_M03_driver.png`、`LSY_M03_repair.png`、`LSY_M03_bond.png` | 周围已亮而玩家身体仍近黑，或出屋 / 切图后补光残留 |
| M_LASTSHIFT_06 换台 | 首班允许时用当前绑定的交互键扳东，回访另测换西；列车停靠后到对应签收柜，观察柜后与旧站台 | `LSY_M06_east.png`、`LSY_M06_west.png` | 可签收时押运长仍在另一台，或没有战斗干扰却无法就位 |
| M_LASTSHIFT_05 奖励 | 普通台、加价台分趟用当前交互键签收并开柜，逐格数物品、鉴定前两件品质 | `LSY_M05_normal.png`、`LSY_M05_premium.png` | 普通台少于 8 件、加价台少于 9 件，或少于两件 Q4 以上；日志 `CARGO_CLAIMED items=实际/计划 q=实物品质` 已取自库存核验，但仍需实机开柜确认显示 |

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

## 2026-10-29 HEAD a8aed728 全面复审（归档索引；L3 待验）

4 项已在 L1/L2 修复，完整原文移至 [2026-10 月归档](archive/CODE_REVIEW_FINDINGS_2026-10.md)。实机验收事项继续有效，归档不等于实机通过。

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

## 2026-10-22 Demo 丧尸入场与旧传送防护（COMPAT，CR-001/002 Fixed at L1/L2；未部署）

- CR-2026-10-22-001，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：custom 请求现归 IntegrationRuntimeModule，绑定实际目标 Scene 与官方 AfterInit 后的 Main；新入场、切图、卸载、角色替换 / 死亡、销毁及 H / 丧尸接管取消。写位置前复核归属，加载计入超时，GroundZero、持续清场与 Mode E 验证共用取消范围；正常与跨局路径由真实驱动隔离回归覆盖。
- CR-2026-10-22-002，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：目标图等待绑定本次入场身份，超时后完整复核场景 handle、loader、LevelInited 与 AfterInit；未就绪走原失败 / 退款协议，旧等待不清理或退款新局。真实等待、Begin / Initialize 与失败退款门覆盖 12 组场景，包含最终 yield 才 ready 和同图重新入场。

461 项 changed-only 守卫、153 项 ZombieMode 守卫与 11 组相关执行回归通过；12 个隔离变异均在预期处转红并逐字还原。Windows 正式隔离编译通过、游戏 DLL 哈希未变，未部署 / 启动游戏 / 访问存档。官方资源与 DLL 未发现 Demo 自动回起点规则，玩家第 17 波现场原因仍 UNVERIFIED。产物与验证见 `Build/teleport-safety-20261022/`，原调查见 `Build/reviews/2026-10-22-zombie-demo-teleport/investigation.txt`；验收步骤见 FIX_TRACKER 同名条目。

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

## 2026-10-13 玩家反馈实现全面复审与修复（归档索引；L3 待验）

CR-001～011 已在 L1/L2 修复，完整根因、验证和手工验收原文见 [2026-10 月归档](archive/CODE_REVIEW_FINDINGS_2026-10.md)。REF / GIFT / MD / MH / PET / BR 实机体验与 PERF-01 帧耗仍待 L3；归档不关闭这些验收事项。

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

- 2026-10-25 全模块重点链路审查（COMPAT；L1/L2 修复，L3 待验）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-24 龙息丧尸伤害修复（COMPAT / WIRE+；Fixed at L1/L2，L3 待验）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-23 野外趣事六项修复与交叉复审（COMPAT；002～007 Fixed at L1/L2，L3 待验）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-23 蚀日花冠粒子持续报错（COMPAT）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-22 蚀日花冠地图接入与正式资源审核（COMPAT / SCHEMA+ / WIRE+）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-21 自定义实体绕过迷雾隐藏（COMPAT / WIRE+）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-21 自定义友好 NPC 常驻头顶姓名（COMPAT）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-13 推送后跨平台门禁修复（Fixed，COMPAT / SAFE，L2）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-12 玩家反馈与 Player(1).log 全面修复（COMPAT / SCHEMA+，L1/L2）（2026-11-25 台账将超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-18 v2.3.5 之后五个提交审查（001～023 全部 Fixed，COMPAT / OPERATIONAL，L1/L2，L3 待 owner）（2026-11-25 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-11 最近五次提交审查修复（COMPAT / SAFE，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-10 提交前复核（COMPAT / SAFE / OPERATIONAL，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-10 节拍器生产图标持有（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-10 点名册获取与商店收口（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-09 小满商店升级提示（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 噬魂挽歌内部名称显示（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 天空岛入场与兼容恢复（COMPAT / WIRE+，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 未提交改动全面审核（COMPAT / OPERATIONAL，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 实档章节缺失、沙圈出生点与末阶段调度（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 首次读档时机、沙暴持续与星阙命中（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 沙暴冠军接管与移动（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 进入竞技场的额外预热（COMPAT，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 空仓地雷盒全面复审（COMPAT / SAFE，L1/L2）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
- 2026-10-08 空仓地雷盒实现期复核（COMPAT，L1）（Fixed，L3 待 owner；2026-11-19 台账超 1200 行时逐字归档） → `archive/CODE_REVIEW_FINDINGS_2026-10.md`
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
- 更正（2026-11-19，追加）：陈列柜确实被前置门住（已建 `MiningMachine` + 完成任务 940），但玩家满足前置就能照官方流程建，不需要改官方数据；槽位标签已按字节读出（12 位全要 `ShowCase`）。详见 CR-2026-09-22-006 的更正。

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
- 更正（2026-11-19，追加，不改上文原文）：本条「废弃建筑、建造菜单里没有」的判断有误。按字节读 `Duckov_Data/resources.assets` 的 `BuildingDataCollection`：陈列柜 `Showcase01` 在官方 27 条建造清单里，造价 5 万 + 物品 361 ×3 + 28 ×3，前置是已建比特币矿机 `MiningMachine` + 完成任务 940「玩具收藏家-2」；`BuildingSelectionPanel.GetBuildingsToDisplay` 对前置未满足的建筑直接隐藏，09-22 实机看不到是这个原因。12 个展示位全要 `ShowCase`，`Showcase.RefreshSlot` 以 `useSpriteIfNoGraphic: false` 摆物品自己的 ItemGraphic，没有模型就不摆。修复本身（加成按官方展示建筑实摆计算、名录不补标签）不受影响，只改了 `ShowcaseTrophyCatalog` 等注释、`BackMountainPlayabilityGuard` 文案与 repowiki；扫描器本就读全部官方 `Showcase`，带 `ShowCase` 的 Mod 物品（5 档以上收集品）摆进陈列柜也计加成。owner 同日拍板维持现状：后山战利品不补展示标签，收集品照常计入，中英 Wiki 后山页同步改口。教训补一句：建造表要连前置一起读，「菜单里没看到」不等于「不在表里」。

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
