# 2026-09-27 模块解耦审计修复与远端合并

本轮从 `f90d6a5d71beec04887e50165b54f8589b7ed6e5` 开始。独立审计记录保留在 `D:/code/ykf/BossRushMod_audit_20260927_01/AUDIT_REPORT.md`；修复、反向探针、构建与合并证据统一在 `D:/code/ykf/BossRushMod_fix_20260927_01/`。原审计文件没有改写。

用户随后授权修复全部确认项，并在修复验证后拉取、合并远端。七项审计问题在拉取前已完成修复，699 个全量守卫、99 项全部隔离回归及 Windows 正式/Dev 隔离构建通过。然后执行 `git fetch origin`，取得 `e172936db1288a91e0b7ec81a450d88c7024d927`；本地与远端分别有 27、19 个独有提交。合并先在独立工作树进行，避免中间冲突覆盖原工作区。

合并终态验证已通过：711 个全量守卫、107 项全部隔离回归、Windows 正式/Dev 完整隔离构建及两类 Dev 标识检查均通过。七项确认问题均关闭；本轮未再发现未修复的确认代码问题。验收边界仍为 L1/L2。所有构建使用独立游戏根及真实 Managed 文件副本；真实游戏目录只读，未启动游戏、未接触玩家存档。

## 已关闭的审计问题

| ID / 严重度 | 触发、影响与修复 | 当前实现及执行证据 |
| --- | --- | --- |
| CR-2026-09-27-001 / P1 | D/E/F 活动中同场景停用宿主，原模块销毁没有结束本局。现在先使会话失效，再由模式 owner 回收实体、事件、属性和任务；未开局或已正常结束时不清别的模式。E 额外登记并取消入场前预热及独立商人 child；F 保留正常退出的 0.25 秒等待，并用对象→协程账本处理等待期间卸载。F 只退款已消费而未提交的放置/维修道具，不在卸载时发待领奖励。 | `ModeD/ModeDRuntimeModule_Lifecycle.cs`、`ModeE/ModeERuntimeModule.cs`、`ModeE/ModeEStartup.cs`、`ModeF/ModeFRuntimeModule.cs`、`ModeF/ModeFPhases.cs`。ModeDestroyLifecycle 执行真实销毁、退出、属性恢复和退订；103 断言。`runtime_order/REPORT.md` 与 `test_contracts/final-mode-crossreview-result.json`。 |
| CR-2026-09-27-002 / P1 | 商人工厂 await 后返回时 E owner 已销毁，旧判据及 catch 再次解引用，迟到角色未回收。真实 E/F 判据现在拒绝 destroyed/null/fake-null owner；商人预检、await 后与异常分支共用可失败关闭的门。共享敌人生成也使用同一真实门。 | `Utilities/ModeEFMerchantRuntime.cs`、`ModeE/ModeEStartup.cs`、`ModeF/ModeFEntry.cs`。AuditModeLifecycle 与 EnemySpawnRuntime 连接生产 gate、真实 OnDestroy 和受控异步工厂；后者 51 断言。 |
| CR-2026-09-27-003 / P1 | 聚合器自定义输出目录后仍由 dotnet run 启动默认旧 DLL，生产破坏也可能假绿。改为每次 fresh 目录 build，读取同次 MSBuild TargetPath，校验路径及文件，记录 SHA 后直接执行 DLL；编译失败不运行旧产物。 | `tools/run_runtime_regressions.py`、RuntimeRegressionRunner。保留旧 ReviewSeptember DLL 后移除真实保存屏障，新聚合器命中 `same-frame stake barrier 0`；原字节恢复通过。`test_contracts/validation.json`。 |
| CR-2026-09-27-004 / P2 | 重复宿主在 Awake 早退，未装配成就模块，却继续全局销毁链。OnDestroy 首先以 ReferenceEquals 判断实际活动 owner；正常 Unity fake-null 实例仍执行原链。 | `ModBehaviour.cs`。HostDestroyOwnership 抽取真实 Awake、OnDestroy、成就桥并连接模块 dispatcher，覆盖重复/未 Awake/正常/迟到旧宿主及清理顺序；3 个强反向。`host-destroy/results.json`。 |
| CR-2026-09-27-005 / P2 | 静态缓存 guard 扫入 tmp 旧树，且不追随真实 OnDestroy→Cleanup，删除生产 reset 仍绿。输入改为正式编译清单，清理从真实实例 void OnDestroy() 建调用图，排除不可达/未调用代码。 | `tests/StaticCacheLifecycleGuard.py`、`tests/cs_cleanup_reachability.py`。28 项语法/可达性用例；保留污染 tmp 的 4 个 Affinity 破坏均命中实际断言，恢复后绿。没有增加 allowlist。`test_contracts/final-guard-validation.json`。 |
| CR-2026-09-27-006 / P2 | D 和三个内容 Boss 仍跨宿主拿到 Arena 可变状态；套装、装备和 NPC 的 OnDestroy 不能独立回收自身资源。改为 Arena 窄查询/动作，私有共享时钟；各模块自主幂等清理，保留宿主原有提前清理槽。任务 owner 持有并停止实际句柄。 | `WavesArena/WavesArenaRuntimeModule_BossAccess.cs`、`Integration/Bonus/SetBonusRuntimeHostBridge.cs`、三个装备 Bootstrap、三个 NPC 模块。ModuleOwnerCleanup 39 项真实路径检查，5 个 owner 守卫及 11 个强反向。`host_ownership/fix_handoff.md`、`reverse-results.json`。 |
| CR-2026-09-27-007 / P2 | 两篇当前专题仍指向已迁走的天空岛目录。修正 BOSS 模板与渲染约定的三处路径，核对真实目标。 | `docs/architecture/BOSS模板约定.md`、`docs/architecture/自研着色器与官方渲染管线约定.md`；专题引用与索引检查。 |

这些关闭状态是 L1/L2。原正常调度、随机消费、等待、退款与清理槽保持；有意改变的是失效 owner 的防御行为与遗漏的销毁路径，不能把它们称为完全无行为变化。分类为 COMPAT（生产）、SAFE（文档/守卫）与 OPERATIONAL（运行器及合并）。

## 合并适配与制品边界

远端内容仍基于迁移前目录。天空岛新增四源归入 `SkyIsland/`，后山变身接入 `BossRushStatModifierRecord`。Zombie 的防御、初始出生、污染和 RunOnly 修剪接到真实运行时 owner；旧 `ZombieModeCleanup.cs` 不恢复。新增 Boss 纹理缓存补 owner 销毁。所有远端新增回归和本轮四组修复回归保留，另用 ModeDEntryOwnership 覆盖宿主准入算法迁出后的真实顺序。

最初缺失的 `Assets/ui/skyisland_fx` 已随 owner 指定的 Unity 作者项目更新补齐；作者项目合并基点 `9ec6ce6`，源码来自 `cae185e`。包内两个材质、对应着色器和 D3D11 编译数据经真实制品检查。发布清单同步登记，正式/Dev 两套各 73 个 bundle 的源文件与隔离目标哈希一致。原始缺件不是用跳过检查或替代资源掩盖。三个新增 BGM（`jukebox_dragon_elegy.ogg`、`jukebox_umbral_corridors.ogg`、`sky_island_theme.ogg`）也缺本地来源，运行时按原策略跳过。五个新增 Boss 音效由远端现有生成器仅生成缺件，整套 11 个音效检查通过，原有音效字节未改。详见 `host_ownership/merge/audio-resource-inventory.json` 与 `merge-generated-sfx.json`。

作者导出包随后经独立 Unity 工程重建复核：原样复制作者输入及真实依赖，Unity 2022.3.62f3 的 BuildOnlyAndExit 退出 0。重新产出的 21,531 字节包与作者导出、验收构建使用的包完全相同，SHA-256 `d80936f587810f0a520a92f9e1cca00a97c406d6663748beb7598652de190e65`；两个 Shader 的实际编译数据和材质逐项一致。证据 `host_ownership/fx/rebuilt-payload-comparison.json`、`input-provenance.json`、`unity-build.log`。首轮独立工程缺 NUnit 的环境错误已保留日志，补齐同一作者缓存依赖后成功；未改作者工程输入，未用替身替代编译。

## 验证、度量和归属证据

船票修复在合并后仍从 IntegrationRuntimeModule.BossRushTicketTypeId 的真实注册状态读取，GetBossRushTicketTypeId → CreateBossRushCost 同时接入 HasEnoughTickets、MapSelectionEntry.cost 和 CostDisplay.Setup。IntegrationLeafOwners 执行注册与费用生产方法，覆盖发布 ID 500001、未注册 868、备用正 ID、重新注册和零现金一张票；官方 UI 的实际扣票/取消/退款仍归 L3。

拉取前终态：`final-premerge/results.json`，699 PASS / 0 NEW-FAIL / 0 KNOWN-RED；99 PASS / 0 FAIL。正式 DLL SHA-256 为 `370BB579F52CFF124716133C06C3BDE28741F7F5B1DCAFA6A1D4DBB8D8C137A2`，Dev 为 `846F8F9FA250ACE303D3BF003F2D06B4005F2BC86F116A5ED8F502B58389645C`；各自与隔离目标一致。正式 14 个 Dev 标识 absent，Dev 14 个 present；每套 72 个 bundle 哈希通过。

合并后终态日志分存 `merge-final-guards/`、`merge-final-runtime/`、`merge-final-release/`、`merge-final-dev/`。正式 DLL SHA-256 `373FF9CC93B8504383649A002E78AC944F312D1BC121B2E175E1A7953CC6DF99`；Dev `E5765F650B69ECD273F245F5D65EF3918707DC2512FF7CC5E8812D3E958856A3`。两套均与各自隔离发布副本一致，正式 14 个 Dev 标识缺席、Dev 14 个在位。源清单 1,114 个、47 个模块；宿主 58 文件 / 20,090 行，预算同步收紧到 58 / 20,090；增加一行的真实反向命中预算断言后按 SHA 恢复。

Wiki 使用当前锁文件构建通过，80 个导航检查通过，237 页 / 39,211 个引用无缺失目标和破损锚点。471 个跟踪 Markdown 正文一致，110 个字节变化仅为换行，原输入及锁文件未改；分块大小提示保留，不声称完成浏览器观感或性能验收。证据 `repair/runtime_order/wiki/REPORT.md`。

迁出声明共 3,784 条，合并后 213 个公开宿主签名全部保留；其中原来停在宿主属性的 70 条状态路由已继续穿透到物理字段、允许保留的引用或无存储计算。Zombie 的活动 owner 与未 Attach/Detach 备用路径均有记录。本轮完整重扫 1,114 个源文件，3,784 条均有真实落点，70 条物理状态链重解析成功。TryStartModeD 准入主体移到既有 ModeDRuntimeModule，宿主保留原 public 薄桥，40 项真实入口检查及 8 个强反向通过。远端 ApplyZombieModeEnemyHurtAffixes 改为伤前 ApplyZombieModeEnemyDefense，两个旧补血私有 helper 退役并由 ReduceFinalDamage 直接消费模块吸收结果；这些已明确标为远端行为修复，不冒称等价迁移。声明匹配不是完整行为证明，调用顺序另由源差异和生产执行回归支持。机器数据见 `member-owner-routes.json/.tsv`、`owner-route-verification.json`、`host_ownership/state-physical-owner-routes.json`；上下文按磁盘字节计算，不换算为 token。

真实部署 DLL 与原工作区 Build DLL 全程保持 SHA-256 `47F26AD82B1E67BFE1ECB0C728A8E97B40EBA80745790694B54AA7AD184A88B3`。最初存在的 Goblin bin/obj 文件以 26 项保护清单（含两套 DLL）逐次核对；没有清理、覆盖这些原有产物。

## 当前五类任务开销

| 任务 | 完整源码文件 | 源码字节 | 固定规则字节 |
| --- | ---: | ---: | ---: |
| 日报版面改动 | 6 | 133,578 | 52,522 |
| 天空岛居民服务加一项 | 4 | 130,430 | 122,816 |
| NPC 商店交易改价 | 6 | 202,917 | 109,589 |
| 给一把新武器配数值 | 9 | 175,215 | 109,589 |
| Mode F 奖励调整 | 12 | 187,482 | 99,072 |

自动导入共 17,921 B，前测为 38,854 B。五类源码阅读量没有普遍下降；按完整载体计数，不能只算薄桥或把字节降幅换成 token 降幅。详见 CONTEXT_BASELINE.md。

这些终态字节在结果落回主工作区后再次实测。主工作区全部 1,114 个生产源文件与实际测试快照逐个比较，537 个原始字节差异均仅为 Git 检出保留的 CRLF/LF，规范化后全文一致；未用批量格式化凑预算。主工作区定向元数据守卫及 Fx 制品检查再次通过，26 项原有受保护文件哈希均未变。证据 `main-final-verification/`；本地合并提交的两个父节点为 `f90d6a5d`、`e172936d`。

## P0–P6 逐项复核

| 阶段 | 修复后判断 | 退出判据与证据 |
| --- | --- | --- |
| P0 | 已完成（当前 L1/L2） | 环境、基线数字、历史红项和提交已保留；修复聚合器后全部执行当前新产物，历史假绿记录不追认。 |
| P1 | 已完成 | 根规则 180 行且低于 20 KB，台账限额、归档索引、47 模块导航、未知 ID 拒绝、五类复算和全量守卫通过。 |
| P2 | 已完成（L1/L2） | 属性追踪与建筑恢复继续使用单一共享核心；真实生产夹具、owner 隔离及两种构建通过。 |
| P3 | 已完成（L1/L2） | 七簇迁出、213 个兼容签名、3,784 条去向及 70 条物理状态链已核对；销毁、迟到任务、跨模块可变状态缺口已修复，预算达标。 |
| P4 | 已完成（L1/L2） | 指定 1、3–8 项及随 P3 处理的第 9 项闭环；第 2、10 项按计划保留。调度、查询/消费、支付回滚及共享门的生产回归通过。 |
| P5 | 已完成（L1/L2） | 天空岛正式目录、远端新增四源、清单/夹具/覆盖表/主题引用同步；Injection 空壳保持移除。 |
| P6 | 部分完成 | 离线收口、预算、后测、报告、守卫/回归、正式/Dev 构建和标识全部完成；本轮修复版本未部署真实游戏，计划所列新交付部署与 L3 留待 owner。 |

没有把未进行的实机步骤记作通过；当前确认项修复完成与完整游戏内验收分开记录。

## 测试局限与剩余验收

夹具执行生产 gate、销毁入口和清理方法；Unity native 对象、调度、UI、资源与物品交付叶子仍为显式替身。初始化中断用例通过真实 BeginSession 加 active=false 建立中间状态，没有逐个向完整 StartModeD/E/F 注入异常。退款计数证明调用、顺序和幂等，不证明物品实际进入背包/落地。Campaign 夹具的构造绑定与 EnsureBootstrapped 仍是显式替身，不证明完整 NPC 指导轮询；Zombie 的退款叶子与 RunState.ClearRuntime 替身也不证明实际经济或 GPU 释放。静态清理调用图不等于完整 C# 动态语义分析。反向验证只统计命中预期断言的变异；被其它冗余防御挡住而仍绿的探针已明确排除，不算成功证据。

合并后的全部 L3 继续 MANUAL_PENDING。按 `MIGRATION_ACCEPTANCE.md` 执行玩家真实入场、船票扣除/取消/退款、连续第二局、切图/死亡/停用、装备与 NPC、自定义 Boss、共享 UI 和 F3。新增 `M_RUNTIME_05` 包括 E 入场前与商人 child 预热期间卸载、F 正常退出后 0.25 秒内卸载、迟到工厂结果以及未提交工事/维修的真实退款。截图、观察位置与不合格条件已在清单写明。没有游戏内性能采样结论。

未证实线索仍分开保留：共享字体及部分 prefab/AssetBundle 的热重载语义、第三方 Mod 动态反射名、Harmony 实际安装情况尚无本轮 L3。此前共享敌人生成 gate 的线索已经加入真实执行回归，不再仅凭商人路径推断。
