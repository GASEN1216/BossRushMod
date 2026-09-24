来源：`FIX_TRACKER.md`；归档区间 2026-08，原节正文保持逐字不变。

## 变更日志

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-08-28 | 新增遗种巢（PetNest）养崽系统 | 基地侧养成收集：遗种蛋+遗魂双轨掉落（TypeID 500059）、全 Boss 谱系幼体化、孵化 roll 与命名、单席随从进局与捡漏背包、重伤退场与战痕、天灾远征与真死、博物馆图鉴与纪念碑、驯养成就。零新增 Harmony 补丁、零新增 Unity 资源、只占一个 TypeID；唯一反射写点 `LevelManager.petCharacter`（借席不夺席）。默认关闭。契约见 docs/contracts.md §6.2。 |
| 2026-08-27 | 全项目 UI 收口与观感升级 | 新增共享 UI 库（设计 token、层级表、程序化圆角九宫格、图集注入点）；修高分屏面板不缩放与模态互压；legacy Arial 清零，中文不再显示为方块。 |
| 2026-08-27 | 审查修复：命火量纲、安全区仇恨泄漏与双槽改造 | 命火成长改入场上限 4%/杀使机制真正可达；修复被弹出敌人永久失去仇恨的卡波次风险；便携安全区改为战斗期替换、准备期并存；恢复手雷不取消安全区的武器 tag 门控。 |
| 2026-08-26 | 修复 Mode F 退出生命并新增命火过载 | 生命成长封顶 +50%，溢出转为带烧伤和双倍失血风险的短时火力/移速强化；退出时清理全部临时属性并钳制当前生命。 |
| 2026-08-25 | 完善丧尸模式波次奖励与安全区体验 | 散落物按波次清理、Boss 解卡阈值下调、骚扰弹道可读化、可选休息时长 15～300 秒，新增背包废品回收与便携安全区装置（TypeID 500058）。 |
| 2026-08-17 | 修复 Mode F 准备阶段停刷 | 移除 180 秒准备阶段的补位门控，四阶段均按死亡/失效缺口持续逐只补位，并保留单任务限流。 |
| 2026-08-07 | 提高丧尸模式前期密度并修复停刷 | 击杀目标保持旧值；场上压力、补怪频率与硬上限至少提高 3 倍，并增加可靠刷新、计数校准和远怪回收。 |
| 2026-08-07 | 修复 Mode E 两个重刷道具不可用 | 按 owner 决定完全移除重刷人口上限和点位裁剪，仅保留单任务互斥与会话安全。 |
| 2026-08-07 | 丧尸模式 Boss 与变异尸潮持续成长 | Boss 数量按五波轮次无上限递增，后期精英/特殊权重持续提高，总风险与逐只掉落收益同步成长。 |
| 2026-08-07 | 补齐丧尸模式 Boss 风险收益曲线 | 按轮次同步提升 Boss 本体、支援、净化收益、奖励箱和肉鸽奖励次数；固定单 Boss 部分已被后续设计覆盖。 |
| 2026-08-07 | 丧尸模式 Boss 波取消地图联动 | 移除地图刷怪点数量联动并明确非 Boss 速度曲线；初版固定单 Boss 已被后续递增设计覆盖。 |
| 2026-08-07 | 优化丧尸模式奖励与终端 | 移除死刷新选项，补齐终端余额/饮料库存，并收敛临时 NPC 保护扫描和失效 UI 记录。 |
| 2026-08-07 | 修复 Wiki 右书页越界与跳页 | 全文只生成一次分页边界，左右页显示连续缓存页块并以 TMP `Page` 限制越界。 |
| 2026-07-01 | AI 协作文档收敛 | 从旧 `docs/协作/FIX_TRACKER.md` 迁移 confirmed 修复记录；新增状态、owner decision、兼容分类字段。 |

## 2026-08-30 三系统落地：鸭皇图鉴 / 局内随机事件 / 词缀锻造

分类：`COMPAT` + `SCHEMA+`（新存档 key 与物品 KV 前缀，均为向后兼容新增）
+ `OPERATIONAL`（编译脚本改用 Roslyn 响应文件，见下）。

### 交付内容

| 系统 | 目录 | 规模 | 开关（默认） |
| --- | --- | --- | --- |
| 鸭皇图鉴 | `Integration/Codex/` + `Localization/CodexLocalization.cs` + `Config/ConfigCodex.cs` | 15 文件 | `BossRush_CodexEnabled`（true） |
| 局内随机事件「鸭生无常」 | `RandomEvents/` + `Config/ConfigRandomEvents.cs` | 12 文件 | `BossRush_RandomEventsEnabled`（true）+ 频率档 |
| 词缀锻造 | `Integration/AffixForge/` + `Integration/Reforge/ReforgeUIManager_AffixForge*.cs` + `Localization/AffixForgeLocalization.cs` + `Config/ConfigAffixForge.cs` | 14 文件 | `BossRush_AffixForgeEnabled`（true） |

新增 TypeID：`500060` 词缀熔石、`500061` 鸭皇图鉴（台账三处已同步，下一可用 `500062`）。
新增存档 key：`BossRush_Codex_v1`（槽位级）。词缀数据寄生官方物品 KV 的 `AFX_` 前缀，随机事件零存档。
成就分类枚举末尾追加 `Codex`（未插入中间，老档 int 值不漂移）。
零新增 Harmony patch。

### 修复的实际缺陷

1. `ItemContentRegistry.cs` 注册的类名写错（`CodexBookItem` → 实际 `CodexBookConfig`），编译期 CS0103。
2. `RandomEventEffectsBridge_Loot.cs` 把官方 `TagsData.AllTags`（`ReadOnlyCollection<Tag>`）
   直接赋给 `List<Tag>`，CS0029。改用 `IList<Tag>` 接住，只做顺序遍历，避免多余拷贝。
3. 三系统的宿主销毁清理原本内联在 `ModBehaviour.OnDestroy` 末尾，越过了
   `StaticCacheLifecycleGuard` 判定「调用是否在 OnDestroy 路径上」的回溯窗口，
   导致 4 个类被误判漏清理。按仓库既有约定收口成三个具名方法：
   `CleanupCodexRuntimeOnDestroy` / `CleanupAffixForgeRuntimeOnDestroy`
   / `CleanupRandomEventsRuntimeOnDestroy`（后者新增 `AffixForgeHostCleanup.cs` 承载）。

### OPERATIONAL：编译脚本改用响应文件（需 owner 知悉）

**症状**：登记 40 个新文件后 `compile_official.bat` 直接失败，输出
`The system cannot execute the specified program.` 并以 60 退出，**没有任何 C# 错误行**。

**根因**：清单增至约 690 个文件后，展开后的 csc 命令行超过进程创建上限，csc 根本没被启动。
用响应文件单独调 csc 编译同一份清单则 0 错误通过，据此定位。

**改法**：`compile_official.bat` 里 csc 的全部参数改为先写进 `Build\bossrush.rsp`，
再 `csc @rsp`。源码清单仍逐条显式列出（不用通配符），编译清单守卫照常双向生效。
写法上踩过三个坑，已在脚本顶部注释固化：
  - 必须「括号块 + 块尾一次重定向」；行首重定向（`>>"file" echo ...`）在本机 cmd 上
    直接报 ERROR_INVALID_NAME(123)；行尾重定向会让 `.cs` 后跟 `>>`，守卫正则扫不到。
  - 必须用 `echo(` 而非 `echo`：未开 DEV 时 `%BOSSRUSH_DEFINE_ARGS%` 为空，
    `echo` 会把 "ECHO is on." 写进响应文件。
  - 含括号的路径（`C:\Program Files (x86)\...`）在引号内已实测安全。
备份留在 `compile_official.bat.bak`，确认无碍后可删。

### 同步的守卫（AGENTS.md 4.10）

- 新增：`RandomEventsWaveIsolationGuard.py`（波次符号零触碰 + 敌对性安全网 + 事件清理成对）、
  `RandomEventsModeGateGuard.py`（禁入 5 模式 + fail-closed + 禁引内部符号）、
  `RandomEventsRuntimeModuleGuard.py`（单实例 + dormant + 热路径零日志）、
  `AffixForgeInvariantGuard.py`（AFX_ 互斥 + 订阅逐事件成对 + 12 词缀 + 死契 1 血保命）。
  另有实现阶段产出的 `CodexPersistenceGuard.py`、`CodexKillTrackingGuard.py`。
- 因结构变化而同步（非放宽掩盖）：
  - 三个 ZombieMode 清单守卫与 `ModeHCompileManifestGuard`：适配响应文件行格式。
  - `PetNestEggItemRegistryGuard`：台账断言由硬编码 500059/500060 改为动态上界
    （守的是「遗种蛋的号已被占用」，不是「它必须是最后一个」）。
  - `PetNestAchievementCategoryGuard`：由「Taming 必须是最后一项」改为「冻结前缀逐字相等」，
    允许末尾追加新分类，同时仍然禁止插入与重排。
  - `LocalizationInjectionGuard`：新增识别字典索引式注入（`map[XxxConfig.LOC_KEY] = ...`），
    这是新一代本地化文件的写法，旧正则只认 `Inject(...)` 调用式会误报。
  - `ModBehaviourInstanceClassificationGuard` + 对应文档：基线 361 → 374，新增 `RandomEvents` 组。
  - `tests/empty_catch_budget.txt`：919 → 968。新增的 49 处都是
    `base.Awake()` / `onFailed()` 这类一行防御式空 catch（AGENTS.md 4.7 明令不成批清理）；
    刷怪与敌对性安全网等关键路径**已带 DevLog**，未以空 catch 掩盖失败。

### 验证状态

- Windows `compile_official.bat`：**通过**（691 源文件，0 error）。
- `python tools/run_guards.py`：**PASS=499 / NEW-FAIL=0 / KNOWN-RED=1**（既有 DragonKing 红项）。
- **实机 smoke：未做**。以下必须人工验证后才能认为交付完成：
  1. 图鉴：买书开面板 → 杀 2 Boss 验证解锁与成就 → 回基地落盘 → 重启读档 → 切槽位隔离；
     ModeG 托管龙裔计入、丧尸 Titan 计入、遗种巢随从不计。
  2. 随机事件：F3 逐事件强制触发；**关键回归**——乱入 Boss 在场时打死波次 Boss，
     波次应正常推进且乱入仍在；五条清理路径（到时/死亡/撤离/切图/关开关）零残留。
     另需实测：竞技场是否存在 `WeatherManager.Instance`；零伤害爆炸是否带击退。
  3. 词缀：门控四连测（手持/切换/空手/穿卸 → 订阅计数归零）；旁观 NPC 同武器不触发；
     仓库闲置零订阅；存读档 KV 完整；卖店买回/快递往返/掉落拾回 KV 保留。
- 美术资产（约 35~43 张 Boss 立绘 + 熔石图标 + 12 词缀图标 + 8 事件图标）**尚未生成**，
  当前全部走占位链（图鉴用官方 Boss 图标、其余用文字/程序化底）。
- `.qoder/repowiki/` 与 Wiki 站词条**尚未同步**（AGENTS.md 4.13 要求，属未完成部分）。

### 2026-08-30 补：文档同步与美术资产（承接上条）

分类：`SAFE`（文档）+ `OPERATIONAL`（新增美术资产与 Unity 构建器）。

**repowiki 同步（AGENTS.md 4.13，此前列为未完成项，现已补齐）**

- 新增 3 张模块知识卡（`.qoder/repowiki/knowledge/zh/`）：鸭皇图鉴、局内随机事件、词缀锻造。
  每张都按既有知识卡体例写清系统概述、关键文件职责、架构与设计约定、性能、契约面、已知未完成项，
  并把「为什么否掉另一条路」的决策记进去（例如词缀为何不用官方 Effect 挂件、
  图鉴为何不接 AchievementTracker、随机事件为何不复用变异词条 roll 基建）。
- `knowledge/zh/_index.yaml` 登记 3 个模块（codex / random_events / affix_forge），YAML 解析校验通过。
- 新增 3 篇主题详解（`.qoder/repowiki/zh/content/高级功能/`），并挂进「高级功能」索引页的
  三处清单（简介、分层说明、目录）。

**游戏内 Wiki 与在线站**

- `WikiContent/{zh,en}/` 各新增 3 篇玩家向词条（图鉴 / 随机事件 / 词缀锻造），文风对齐日报词条。
- `WikiContent/catalog.tsv` 追加 3 行（order 10/11/12）。
- `wiki-site/scripts/sync-content.mjs` 的 `ENTRY_TO_PATH` 补 3 条映射，
  `docs/.vitepress/config.mts` 侧边栏中英各补 3 条，`vitepress build` 通过。
- **既有缺口（非本次引入，供 owner 决策）**：`system__pet_nest` 与 `system__daily_report`
  从未进入 `sync-content.mjs` 的映射表，因此遗种巢与日报在**在线站上没有页面**（游戏内 Wiki 有）。
  同步脚本现在仍报「跳过 3」，就是它们加上另一条。本次未擅自补，因为不属于本批范围。

**美术资产**

- 新增 `tools/gen_codex_art.py`：一次性批量生成脚本，可断点续跑（目标文件存在即跳过），
  含网关抽风的指数退避重试。共 58 项：36 张 Boss 立绘 + 2 个物品图标 + 12 个词缀图标 + 8 个事件图标。
- Boss 名册来自静态汇总而非实机导出：ModeH `BossProfiles.json` 的 `profileTemplates`（12）
  ∪ `excludedStableKeys` 里的官方 Boss ∪ 官方 `AchievementManager` 的 `KillCountAchievement` 表
  ∪ 3 个自定义 Boss ∪ 5 个丧尸合成条目 = 28 官方 + 3 自定义 + 5 丧尸 = 36。
  **注意**：运行时真实 Boss 池由 `showName` + 血量阈值决定，静态汇总可能与之有出入；
  立绘缓存是 fail-open，多出来的 Boss 会走占位链，不会报错。
- 流程：gpt-image-2 出色键图（#ff00ff）→ `remove_chroma_key.py` 抠图 → 裁包围盒 → 等比缩放 →
  居中贴进透明正方形画布。立绘/物品 512px、词缀 256px、事件 128px。
- 新增 Unity 构建器 `CodexPortraitBundleBuilder.cs`（兄弟工程 `Assets/Editor/`）：
  扫 `Assets/UI/Codex` 目录、程序化打 bundle 标签（不依赖手工 .meta）、
  构建后回读校验 asset 数量与命名契约（必须全小写、必须带 `codex_portrait_` 前缀）、
  体积硬上限 8 MiB。与 ModeG/ModeH 那两个「固定两张图」的构建器不同，这个是批量扫目录型。

**美术与 bundle 已全部完成（此前列为未完成项，现已补齐）**

- 58 项资产全部生成落位：36 张 Boss 立绘（512px）、2 个物品图标（512px）、
  12 个词缀图标（256px）、8 个事件图标（128px）。
- 生成过程分三轮：首轮 33 成功 / 25 失败，失败全是 `APIConnectionError`。
  根因是**网关限流约 1 次/分钟而脚本只隔 3 秒**，不是 prompt 问题。
  把间隔改为可配置（`ART_GEN_DELAY`，默认 20 秒）并加三次指数退避重试后，
  第二轮 23/25、第三轮 2/2 全部补齐。
- 立绘 AssetBundle 已实际构建并落位 `Assets/ui/codex_portraits`（3.1 MB，8 MiB 上限内），
  构建器回读校验通过（36 个 asset、命名全小写、前缀正确）。
- `compile_official.bat` 最终验证：`Build succeeded`，并成功部署立绘 bundle、
  词缀图标、事件图标三类资产到游戏目录。

**Unity 构建踩的三个坑（已固化进 tools/build_codex_bundle.ps1 注释）**

1. PowerShell 5.1 按 ANSI 读 `.ps1`，含中文的脚本必须存成 **UTF-8 with BOM**，否则解析报
   `Unexpected token`。
2. Unity.exe 是 GUI 程序，用 `&` 调**不会等待**，`$LASTEXITCODE` 为空，且外层任务结束时
   会把正在启动的 Unity 子进程带走（表现为日志停在 `Begin MonoManager ReloadAssembly`、无产物）。
   必须用 `Start-Process -Wait -PassThru`。
3. 上一条留下的孤儿 Unity 实例会占住工程锁，导致后续调用**刚切到工程路径就以返回码 0 退出**
   （日志里只有 `Exiting without the bug reporter`，极易误判成构建器没跑）。
   排查方法：看 `Temp/UnityLockfile` 与 `Unity.exe` 进程启动时间。

**仍未完成**

- **实机 smoke 未做**（owner 指示本轮不做）。这是当前唯一的未完成项，
  各系统的必测清单见本条目上方与 2026-08-30 主条目。
- 立绘的 Boss 名册是**静态汇总**得来（ModeH 档案 + 官方成就表 + 自定义/丧尸常量 = 36，
  后续又补了 `Cname_Ghost` 共 37；补它的依据是 mod 旧版构建里存在
  `!(enemyPresetInfo.name == "Cname_Ghost")` 这种「从 Boss 选取中显式排除」的写法，
  说明它本来能通过 Boss 池筛选、会出现在图鉴目录里。全仓其余无立绘的 `Cname_*`
  —— Wolf / Usec / GunTurret / Zombie —— 已逐个确认是杂兵或 AI 预设，不进 Boss 池），
  而运行时真实 Boss 池由 `showName` + 血量阈值决定。两者若有出入，多出来的 Boss 会走
  占位链（fail-open），不会报错；实机跑一次 F3 的目录导出即可核对差集并补图。
- 立绘是按 nameKey 语义生成的**风格化演绎**，不是游戏内模型的还原（模型无法读取）。

## 2026-08-30 鸭王征程 / 竞技场后山 全面审核

审核范围：本轮新增的 Campaign、Integration/BackMountain、BossBgmCoordinator、
BossRushUISkinLoader 与相关接线。编译 + 503 guard + 内容一致性 + 逻辑走查。

| # | 分类 | 问题 | 处理 |
| --- | --- | --- | --- |
| 1 | 回归 | `EmptyCatchGuard` 由绿转红：本轮新增 35 个空 catch 把全仓库总数从预算 968 顶到 1003。此前误报为"守卫全绿"——runner 只打印输出末行，看起来像一个无关文件 | 逐处按 AGENTS §4.7 处理：27 处冷路径（存档/注册/通知漏斗/清理）补 `DevLog`，8 处必须静默（日志函数自身会递归、`OnGlobalHurt` 是最热事件、每帧 tick）写明理由。**不动预算**，计数回到 968 |
| 2 | BREAKING(玩法) | 幽影蘑菇物品描述中英文均写"受到的伤害更少 / takes less damage"，实现却加 `MaxHealth` +10%。两者不是一回事，玩家会被误导 | 改用 `ElementFactor_Physics` −10% PercentageAdd——受击侧伤害倍率，`Health` 结算时读取（`DragonSetConfig.cs:11` 有说明，丧尸模式守护护盾同款 −25%）。描述按项目惯例明写"物理伤害" |
| 3 | 数据丢失 | `RaidMealService.ApplyForRun` 先清登记再 `switch`，遇到旧存档里的陌生 TypeID 会走 `default` 直接 return——饭被吃掉、加成没给、玩家无任何提示 | 改为先用 `GetDefinition` 确认认识该餐品再消费；无法识别时记 WARNING 并清掉（避免每局重试） |
| 4 | 卡死 | 第一章"第 3 波前无伤"靠"波次超过门槛"置达成，而标准模式总波次 = BossFilter 过滤后的 Boss 数 / 每波数。玩家把 Boss 池筛小后波次可能永远到不了 4，目标卡死在未达成——哪怕全程没掉血 | 新增 `SatisfyNoDamageOnRunComplete()`，通关或撤离成功时补判（走完整局已蕴含"熬过全部波次"）；已破防的不会被救回 |
| 5 | 冲突 | 终章决战门禁漏了 ModeG（宿命回响）与 ModeH（黑市鸭王杯），两者都在竞技场里跑，会与决战抢场地 | 门禁补上 `modeGActive` 与 `ModeHRuntime.RunState.Lifecycle != None` |

已核对无问题：编译清单登记、TypeID 台账 500062-500067、本地化注入配对与
`Note_<key>_Title` 键格式、订阅/退订配对（3/3、1/1、采集器 2/2）、19 处
`ResetStaticCaches` 全部被中央复位调用、契约文档登记、恒开策略与 ModConfig
白名单、repowiki 同步、JSON 合法性、六章硬编码内容完整。

历史审核时 `Assets/Data/Campaign/*.json` 尚不存在；2026-08-31 已按发布门槛新增并改为正式来源，
硬编码仅作整表校验失败时的灾备 fallback。

---
### 2026-08-31 征程/后山审核修复：四个 P1 + 两个 P2

**状态**: fixed（P1×4、P2×2）；同轮 7 项 P3 为 needs-owner-decision，见 CR-2026-08-31-007
**Finding**: CR-2026-08-31-001 ~ -006
**兼容分类**: `COMPAT`（四个 P1，行为修正、不动 schema）+ `SAFE`（两个 P2，纯性能）
**版本/Commit**: 未提交
**Owner decision**: 不需要（六项都是「实现与既定设计不符」，不涉产品取舍）；
同轮 7 项 P3 需要，已单列 CR-2026-08-31-007

**现象**

1. 出击餐吃了没效果，每局都如此（三种餐全部）。
2. 展示柜「登记得越多越经打」在战局里不成立，进图后加成为零。
3. 终章决战打输一次，召唤石永不再现，终章无法重打（本会话内）。
4. 同会话换档后，A 档的展示柜收藏在 B 档生效；在 B 档登记会把 A 档收藏整体写进 B 档存档。
5. / 6. 两处每帧路径持续产生 gen0 垃圾（召唤石维护的场景名查询、HUD 的先构建后比较）。

**根因**

- 1 与 2 同源：官方主角由 `LevelManager.CreateMainCharacterAsync` **异步**创建，
  而两处加成都挂在 `SceneManager.sceneLoaded` 驱动的场景回调上，那一刻
  `CharacterMainControl.Main` 必为 null；早返后无任何重试路径。
  `RaidMealService.cs` 的头注释本就写明该用 `OnLevelInitialized`，实现没接上。
- 3：`CleanupCampaignFinalBoss` 只有「死亡回调」「让路 tick」两个调用点，
  玩家打输时 Boss 随场景销毁、回调永不触发，`campaignFinalBossActive` 卡死。
- 4：`ShowcaseService` 缓存无槽位烙印且不订阅换槽事件，`NotifySlotChanged()` 全仓零调用。
- 5：判定顺序把每帧分配字符串的场景查询排在了零分配的契约查询之前。
- 6：脏检查放在字符串构建**之后**，等于每帧都付了分配代价。

**修复内容**

- 修改文件（均为既有文件，无新增 `.cs`，`compile_official.bat` 无需改动）:
  - `Integration/BackMountain/BackMountainRuntimeModule.cs`：新增 `LevelManager.OnAfterLevelInitialized`
    与 `SavesSystem.OnSetFile` / `OnSaveDeleted` 两组幂等订阅（命名方法 + 成对退订，AGENTS.md 4.6）；
    把「设施注入」与「角色加成」拆成 `RefreshFacilitiesForScene` / `RefreshCharacterBoundEffects`
    两个时机；切场景走 `ClearCharacterBoundEffects`；bootstrap 迟到时用 `LevelManager.AfterInit` 补一次。
  - `Integration/BackMountain/ShowcaseService.cs`：缓存加槽位烙印（`_loadedSlot` + `ReadCurrentSlotSafe`），
    `EnsureLoaded` 检测到槽位漂移即摘旧加成并重读；`NotifySlotChanged` 一并复位烙印。
  - `Campaign/CampaignFinalBoss.cs`：新增 `campaignFinalBossRunId`（作废在飞的异步生成）与
    `campaignFinalBossSpawnResolved`（区分「生成中」与「Boss 已不在场」）；让路 tick 补
    「Boss 已不在场」收尾；收尾复位终章局内追踪；召唤石判定改序 +
    `IsCampaignArenaSceneCached` 按 scene generation 缓存场景判定。
  - `Campaign/CampaignRuntimeModule.cs`：`OnSceneLoaded` 幂等调用 `CleanupCampaignFinalBoss(false)`。
  - `Campaign/CampaignHud.cs`：改为构建前零分配脏检查（复用 `List<int>` / `List<bool>` 快照）。
- 同步文档: `.qoder/repowiki/` 两张知识卡（后山 3.2/3.3、征程 3.4 与文件职责表）、
  `CODE_REVIEW_FINDINGS.md`（新增 7 条 + 状态汇总）。

**兼容性影响**

- 存档：无 schema 变更。修复 4 之后，此前若已发生跨槽污染的存档不会被自动纠正
  （已写进 B 档的收藏就是 B 档的数据），玩家可在展示柜面板自行确认；新污染不再产生。
- 配置 / TypeID / Harmony / 反射 / 资源 / 部署：均无影响。
- 新增两处官方事件订阅（`OnAfterLevelInitialized`、`OnSetFile` / `OnSaveDeleted`），
  均为静态事件，已按 4.6 做幂等 + dormant 与宿主销毁两条路径退订。

**验证方法**

1. 编译: `compile_official.bat` → `Build succeeded!`，exit 0，DLL 与资源已部署。
2. Guard: `python tools/run_guards.py` → PASS=503 / NEW-FAIL=0 / KNOWN-RED=1
   （既有红项 `DragonKingBossGunRocketSplitGuard`，与本轮无关）。
3. 静态复核: 订阅/退订 3/3 配对；`ApplyForRun` / `ReapplyBonuses` / `NotifySlotChanged`
   调用点逐一确认；`CleanupCampaignFinalBoss` 现有 4 个调用点覆盖击杀/让路/Boss消失/切场景。

**未验证/需人工**

四条 P1 的实机 smoke 尚未做（脚本无法驱动进基地/进局）。建议顺序：
① 基地吃出击餐 → 进局确认飘字与属性；② 登记一件 Q≥5 战利品 → 进局确认最大生命提升；
③ 召唤终章 → 故意战死 → 回基地再进竞技场确认召唤石重现、HUD 不残留；
④ A 档登记 → 不退游戏切 B 档 → 确认 B 档为空且加成为 0。
先用 `set BOSSRUSH_DEV_BUILD=1 && compile_official.bat` 出 dev 包，否则 `DevLog` 被剥离。

---

### 2026-08-31 全内容开启与可用性闭环

**状态**: fixed（静态实现、Windows 编译与 guard 已完成；实机玩法 smoke 待人工）
**Finding**: CR-2026-08-29-018、CR-2026-08-29-021、CR-2026-08-31-007、CR-2026-08-31-008
**兼容分类**: `COMPAT` + `SCHEMA+`（日报未读提示、征程待交付态仅新增可选字段）
**版本/Commit**: 未提交

**内容开启策略**

- 遗种巢、日报、鸭皇图鉴、词缀锻造、鸭生无常、鸭王征程、竞技场后山、模式H
  共八个内容系统的字段默认值均为 `true`。
- `LoadConfigFromFile` 后统一执行 `ForceContentSystemSwitchesOn()`，抹平历史配置和旧档遗留的
  `false`；八个总开关不再从 ModConfig 读取，也不再注册到 UI，避免玩家把正式内容误关。
- `BossRush_BackMountainUnlockAll` 仍是默认关闭、可配置的调试旁路，不属于内容开关。
- 模式H 的真实仓库押注仍保持 fail-closed；这不是内容总开关，正式玩法使用完整的虚拟押注与结算，
  不对玩家仓库物品做未获授权的高风险扣押。

**模式H 可玩闭环**

- 新增 `ModeHRuntimeModule_CombatFlow.cs` 与查询/克隆辅助 partial
  `ModeHRuntimeModule_CombatProfiles.cs` 并登记 `compile_official.bat`：完成确定性出战名单、
  套装选择、公开分/胜率/赔率、分帧生成、战斗、拍铃接力、遥测、结算、伤病/战痕、奖励、
  赛间恢复、转会、总决赛与名人堂更新，不再在生成成功后以 `combat_wiring_pending` 回滚看盘。
- 报告恢复在继续前先核对已结算/奖励状态，技术重试回滚报告与奖励，避免重放和重复结算。
- 地图选择页只展示 ModeH 支持的 sceneName/sceneID 组合，并按原地图配置索引重新冻结目标，
  修复选中项与预扣票 intent 漂移。
- 原生敌人隔离只清明确敌对角色，保留主玩家、玩家队、遗种巢随从及 `INPCController` 功能 NPC。

**其余内容修复**

- 鸭皇图鉴：书不可出售、价格系数正常；商店暂不可注入时保留库存缓存；存档订阅完整退订。
- 鸭生无常随机商人：反射绑定前置，配置完成前 inactive；首次激活单次补货，时间戳稳定；
  弹药/医疗按 99 堆叠，高品质物品单件出售。
- 日报：`PendingIssueBanner` 作为可选 JSON 字段落盘；日报 UI 的 `OnDestroy` 调用基类清理。
- 词缀锻造：全部 KV 写入读回核验；重铸/锁定按事务顺序扣款与扣材料，失败恢复槽位，
  退款或回滚失败会给玩家明确错误，不再静默吞资源。
- 鸭王征程：`ReadyToDeliver` 向后兼容持久化；第一章无伤降为 2 波，第三章文案统一 8 名头目；
  终章召唤石仅在契约进行中出现；交付改为克隆存档事务，存档成功后才发布解锁 token，
  现金或写盘失败均回滚并允许安全重试。
- 竞技场后山：展示柜可登记手持或穿戴的高品质战利品，排除自产种子/餐品；登记、移除、
  出击餐登记和清除均写后读回，失败恢复内存/加成；陌生旧餐品 ID 不再静默吞掉。
- 七项原 P3 取舍已按用户明确指示闭环，详见 CR-2026-08-31-007。

**守卫与文档**

- 同步 ModeH 结构、入口地图、竞技场隔离、词缀事务、图鉴持久化、日报持久化、随机事件、
  征程与后山结构守卫；相关九个 guard 单独执行均 PASS。
- `.qoder/repowiki/` 已同步 ModeH、图鉴、日报、随机事件、词缀锻造、征程、后山知识卡和主题文档，
  删除「ModeH 战斗未接线」「内容默认关闭」等过期描述。
- `ModeHRuntimeModule_CombatFlow.cs` 拆分后为 1137 行，低于新文件 1200 行硬预算；
  `ErrorRecoveryPending → Recovering` 改为显式状态机出口，恢复通道无死态。
- Windows `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning），
  DLL 与资源已部署到本机游戏 Mod 目录。
- `test_logic_official.bat`：8/8 PASS。
- `python tools/run_guards.py`：PASS=503 / NEW-FAIL=0 / KNOWN-RED=1；唯一已知红项仍是
  `DragonKingBossGunRocketSplitGuard.py`，在本次基线提交之前已存在、与本轮无关。
- `git diff --check` 与最终差异复核结果见交付回复。

**实机待测**

- 模式H：受支持地图筛选、六场完整赛季、拍铃接力、失败重试、总决赛/名人堂、友方 NPC 共图。
- 存档故障注入：征程交付、词缀锻造、展示柜和出击餐在写失败后无资源丢失且可重试。
- 商店：图鉴书刷新/回购限制、随机商人首次库存与跨日刷新。
- 跨会话：日报未读提示、征程 ReadyToDeliver、后山登记与出击餐恢复。

---

### 2026-08-31 新玩法可靠性修复与 F3 一键验收

**状态**: implemented（代码、静态守卫与 Windows 编译完成；F3 全套实机报告待 owner 运行）
**兼容分类**: `COMPAT` + `SCHEMA+`（PetNest Bundle_v2）+ `OPERATIONAL`（Dev F3 验收与 Campaign JSON 部署）
**版本/Commit**: 未提交

**修复内容**

- Mode H：认证入口改为四元签名缓存优先，未命中从静态生产目录认证；首次进入不再依赖空的
  `ProductionKeys`。缓存写失败只告警，本次通过的赛季继续；地图审计与双租约门槛保持前置。
- 图鉴 / 遗种巢：官方敌人预设池改为两个消费者共享的一次性初始化；过滤变更同时失效两方，
  增加扫描次数与图鉴构建次数诊断。
- PetNest：新增权威 `BossRush_PetNest_Bundle_v2`，三个 v1 key 只读迁移且不删除；v2 损坏或过新
  fail-closed。巢、远征、博物馆写入统一候选包事务。远征奖励以 `cashGranted` /
  `grantedLootUnits` 续发，取消六次永久放弃，资源就绪后固定退避继续，采用至少一次语义。
- UI / 输入：遗种巢全交互面板统一清理；Mode G 确认/放弃弹窗可幂等关闭；公开模态租约计数，
  结束、死亡、切图、禁用和宿主销毁均走安全清理。
- Mode F：血火负担同时挂 Move/Walk/Run 三项 1.15 倍速度与原伤害修正，统一 tracker 在所有出口移除。
- Campaign：新增并部署 `Assets/Data/Campaign/Chapters.json`，严格六章整表校验，暴露 Json/Fallback
  与内容签名；F3 只接受 Json + 六章 + 冻结签名匹配。
- 日报：签到、跨日、里程碑、种子、横幅和奖励重投递统一候选提交；悬赏先落待发债务再触碰经济。
  Dev 用例真实执行签到、跨日、保存、清缓存回读，并注入 Store 失败验证状态不变。
- 音频 / 终章：Boss BGM 改 owner 租约，同 key 引用计数、异 key 恢复、切图清空；冠军之影只由
  Campaign 发一次终章死亡表现、胜利与 stinger，公共清理仍幂等执行。
- F3：新增五阶段串行完整玩法验收、专用档/运行标记、取消安全清理、超时、性能 p95/峰值采样、
  独立 `BossRushTestReports/BossRushValidation_<runId>.log` 报告及崩溃后中断提示。

**静态验证**

- 新增源文件已登记 `compile_official.bat`，Campaign JSON 已加入正式部署步骤；
  `tools/verify_syntax.py` 已支持 `echo(<file.cs`，736 个编译源语法探针通过。
- DragonKing 火箭 guard 已改验当前对象池播放、容量、生成与清理不变式，不恢复旧 `Destroy` 路径。
- 新增/更新 ModeH、PetNest、日报、图鉴、Campaign、BGM、F3 Runner 等守卫。
- Windows 正式版与 Dev 版均编译成功（仅 JSON DTO 的既有 `CS0649` 警告）；Dev DLL 最后部署。
- `test_logic_official.bat`：8/8 PASS。
- `python tools/run_guards.py`：PASS=507 / NEW-FAIL=0 / KNOWN-RED=0。
- `git diff --check`：无空白错误；`tools/verify_syntax.py`：737 个编译源语法探针 PASS。

**实机待验收**

在 Dev 构建、基地、专用测试档中打开 F3 → “验收测试”，先标记当前档，再运行完整验收。
只有独立报告无 `FAIL` 且最终清场/性能门槛通过后，才把本条状态从 implemented 改为 fixed。

---

### 2026-08-31 首次 F3 报告同步与运行时回归修复

**状态**: implemented（正式/Dev 编译与全量 guard 已通过；完整实机复测待 owner）
**Finding**: CR-2026-08-31-009
**兼容分类**: `COMPAT` + `OPERATIONAL`
**版本/Commit**: 未提交

**首次测试进度**

- 报告：`BossRushValidation_20260831_125247_246.log`。
- 已到阶段 3：13 PASS / 2 FAIL / 0 SKIP；基线 p95 18.19ms、峰值 47.84ms。
- 已通过：数据图鉴、图鉴过滤刷新、后山数据、日报回滚、PetNest v2 与奖励债务、词缀临时物品、
  UI 幂等清理、竞技场加载、标准模式启动。
- 失败 1：Campaign 正式 JSON 被 `JsonUtility` 静默解析为空表，回退 Fallback。
- 失败 2：清场把一个友方角色计成遗留敌人，触发安全中止。因此 Mode D/E/F/G/H、Zombie、
  终章/BGM 与最终回读尚未执行，本次性能也没有最终样本。
- Player.log 额外确认：Boss 乱入实际五次预设解析失败却被总表误报 PASS；动态商人出现
  MagicBlend 空 Playable 异常和“未配置商人”；Harmony scanner 对三个普通 `Cleanup` 方法误报；
  日报/征程运行时交互组件在 `base.Awake` 前缺官方私有分组空表。
- `MakeTimeQuacker.Bed2Interactable` 的 NRE 来自另一 Mod，不属于 BossRush 修改范围；
  DragonKing 缺少可选 trail prefab 已有对象池 fallback，本次日志未显示玩法失败。

**本轮修复与测试代码升级**

- Campaign 改用 `ModeHCanonicalDigest` 严格 parser；守卫禁止退回 `JsonUtility.FromJson`。
- Boss 乱入调用 SpawnCore 前初始化官方 preset cache；F3 为八个事件输出独立
  `RANDOM_EVENT_*` case，等待空投落地、Boss/商人生成、声源/烟花序列、现金堆和巡游鸭完成。
- 清场统计改为明确敌对角色，并把残留对象名、runtime team、preset key 写入报告。
- 限时商店以 `Merchant_Normal` 引导官方 Awake，同帧恢复稳定 Mod ID 后注入库存；新增
  MagicBlend 初始化顺序兼容补丁，最多等待 10 帧且只重放仍处于当前状态的回调。
- Harmony 逐类扫描先验证类级/方法级补丁元数据，不再让普通 `Cleanup` 方法进入 processor。
- 日报、征程公告板/终章召唤石、后山展示柜、词缀锻造和随机商店子交互在
  `base.Awake` 前统一初始化官方私有分组列表。
- 完整验收运行期间不向官方普通消息/大横幅队列写入，取消、异常和正常完成均复位抑制标记。
- 新增 `LatestPlayerLogRegressionGuard.py`，并加强 Campaign、随机事件、F3 与 Harmony 架构守卫。

**当前静态验证**

- Windows 正式版与 Dev 版 `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning）；
  Dev DLL 已最后部署到游戏 Mod 目录。
- `python tools/run_guards.py`：PASS=508 / NEW-FAIL=0 / KNOWN-RED=0；定向 Campaign、随机事件、
  F3、Harmony 与最新日志回归守卫也全部 PASS。
- `test_logic_official.bat`：8/8 PASS。
- `tools/verify_syntax.py --with-bcl`：739 个编译源语法探针 PASS；生产源码与编译清单双向核对 739 项。
- `git diff --check`：无空白错误。

**下一步实机门槛**

重新启动游戏后在同一专用测试档运行 F3 完整验收。CR-2026-08-31-009 在完整报告无 FAIL、
最终清场通过且 Player.log 无对应异常之前保持 Open；不因本轮静态成功提前标 Fixed。

---

### 2026-09-01 第二次 F3 报告同步与模式生命周期修复

**状态**: implemented（代码、定向守卫与 Windows 正式编译已通过；完整实机复测待 owner）
**Finding**: CR-2026-09-01-010
**兼容分类**: `COMPAT` + `OPERATIONAL`
**版本/Commit**: 未提交

**第二次测试进度**

- 报告：`BossRushValidation_20260831_152526_013.log`，对应最新 `Player.log`。
- 已到阶段 3：23 PASS / 4 FAIL。四个失败由三个根因产生，Mode E 清场失败后按安全规则中止。
- 已通过：Campaign 正式 JSON、图鉴及过滤刷新、日报回滚、PetNest v2 与奖励债务、词缀临时物品、
  UI 幂等清理、标准模式、7/8 随机事件、随机商人、标准清场与 Mode E 启动。
- 性能中途样本：baseline p95 17.55ms、peak 51.32ms、无单帧超过 200ms；因安全中止没有最终样本。
- 外部日志中的 `MakeTimeQuacker.Bed2Interactable.Awake` 与
  `TriangleDuckAttachmentExpansion.CleanupAllSystems` NRE 不属于 BossRush，不在本轮修改范围。

**确认根因与修复**

- 随机事件 Boss 乱入进入共享 `ModeEFSpawnPostprocess` 队列后，标准 WavesArena 的 early-return
  使 scheduler 不再 tick，任务一直 Pending，下一模式启动时才以 `scheduler_cleared` 失败。
  现把 scheduler 提到模式组 early-return 前；空队列路径仅一次 Count 判断。
- Mode D 只设置 AI target，未保证 runtime team 对玩家敌对；F3 因此看到已登记角色却没有可玩敌人。
  生成登记前现执行 wolf 安全网并回读敌对状态，仍非敌对则拒绝登记并销毁。结束模式会注销恢复、
  禁掉落并销毁本波角色；重复结束也会清理可能残留的登记实体。
- Mode E/F 为角色克隆 `characterPreset`，旧清理先 Destroy preset，再访问角色的掉落、Health 与
  OnDestroy 链，Unity 伪 null 窗口导致 14 个清理 NRE 和 3 个 `no_preset` 残留。现由角色上的
  `ModeECharacterPresetLease` 持有克隆预设，角色销毁后再延迟释放；模式结束按
  禁掉落 → 注销运行时 → 停用 → 销毁角色的顺序执行，不再用 Hurt 制造死亡副作用。
- Mode E 分类 `StockShop` 现与随机商人相同：inactive 创建、先写 `Merchant_Normal` 引导 Awake，
  同帧 Start 前恢复稳定 `ModeE_*` ID，再覆盖分类库存，消除每个分类以默认 `Albert` 查询的噪声。

**F3 测试升级**

- Mode D 用例同时输出登记对象的 active、Health、team 与 hostile 回读，避免只看总敌人数。
- 清场新增模式自有角色扫描，覆盖 inactive 的 `BossRush_` / `ModeD_` / `ModeE_` / `ModeF_` /
  `RndEvt_` / `ZombieMode_` 对象；普通敌对角色统计仍单独保留。
- 每次模式结束后的清场由固定 0.5 秒改为最多 2 秒逐帧轮询，等待 Unity 延迟 Destroy 完成；
  轮询失败会输出 owned/hostile 明细并立即中止，最终清场也复用同一诊断口径。

**当前验证**

- Windows 正式版与 Dev 版 `compile_official.bat`：PASS（0 error，仅既有 JSON DTO `CS0649` warning）；
  Dev 版最后部署。Build 与游戏目录 DLL 的 SHA-256 均为
  `6806798E82F9F015EF041C4E31A5D165566AEB5B616AA2B44CEDAD7BA8316625`。
- 生产源码与编译清单双向核对：740 项 PASS。
- `GameplayValidationRunnerGuard.py`、`LatestPlayerLogRegressionGuard.py`、
  `RandomEventsWaveIsolationGuard.py`、`ModeEShellHarmonyUiContractGuard.py`、
  `ModeEFSpawnPostprocessSchedulerGuard.py` 与相关 Mode D/E 守卫：PASS。
- `python tools/run_guards.py`：PASS=508 / NEW-FAIL=0 / KNOWN-RED=0。
- `tools/verify_syntax.py --with-bcl`：740 个编译源语法探针 PASS；`test_logic_official.bat`：8/8 PASS。
- Campaign 源/部署 JSON SHA-256 均为
  `45EB48336A246D88081349A040BC647045C224B32B2D8E480E8A11986B9C1E96`；
  `compile_official.bat` 全部 1113 个换行均为 CRLF；`git -c core.autocrlf=false diff --check`：PASS。

**下一步实机门槛**

重新启动游戏后在专用测试档运行 F3 完整验收。CR-2026-09-01-010 只有在 Boss 乱入、Mode D、
Mode E 清场及后续 Mode F/G/H、Zombie、终章、最终回读全部通过后才能转 Fixed。

### 2026-09-03 游戏内 Wiki 与代码基线核对（f9b83c0..HEAD）

范围：`WikiContent/`（游戏内百科的唯一权威源）+ 由它生成的 `wiki-site/docs/`。
逐条核对 f9b83c0 以来新增/改写的玩法内容与当前代码常量，并按"面向玩家"清掉泄漏的内部标识。
分类：`SAFE`（文档）+ 一处 guard 断言口径调整。

**修正的事实性错误**

- 遗种巢页写"遗种蛋落在 Boss 的战利品箱里"，与 `PetNest/PetNestDropService.cs`
  `TrySpawnEggIntoBossInventory`（蛋写进 `boss.CharacterItem.Inventory`）相反，也和
  `item__key_items` / `boss__*` / `start__first_run` 三页"搜尸"的说法自相矛盾。zh/en 均改为
  "掉在 Boss 身上，不在战利品箱里"。
- 模式总览"通用规则"写"9 张地图随便挑，每种模式都能打"，与同页及 `map__overview` 已写明的
  "百战留痕只有 DEMO 终极挑战一张图"冲突（`Assets/SpawnPoints/` 里只有
  `Level_DemoChallenge_1.json` 带 Mode H 点位）。zh/en 均改为按模式分列。
- 丧尸模式的特殊丧尸与精英词缀在 zh 页用的是设计稿命名（疾行者 / 瘟疫者 / 坚韧 / 刚毅 /
  指挥官 / 毒雾 / 适应…），玩家在游戏里看到的是 `LocalizationInjector` 注入的
  冲刺丧尸 / 毒疫丧尸 / 厚皮 / 刚硬 / 号令 / 污染光环 / 反制。五个丧尸 Boss 同理
  （泰坦→巨坦、猎手→极速追猎、分裂者→分裂尸群、护盾者→护盾统御、腐蚀者→腐蚀地面）。
  zh 全部改为游戏内显示名；en 补齐 `* Zombie` 后缀与实际注入名一致。
  `equipment__frost_spear`、`equipment__thunder_set` 与两篇 repowiki 内容文档一并同步。
- Mode H 页补齐入口互斥名单（原文漏了标准竞技场与白手起家，`ModeHEntry.HasLegacyModeConflictForModeH`
  实际把两者也算冲突），并修一个半角逗号。

**清掉的内部标识（面向玩家）**

- 丧尸模式两张表里的 C# 枚举成员（`Sprinter`/`Exploder`/`OfficialExploder`/`Swift`/`Tough`…）、
  `Cname_Zombie` 预设名，以及"安全视觉子树 / safe visual subtree""官方 preset"等实现术语。
- 召唤法杖页的 `Cname_Zombie` 预设、许愿台页的"运行时 View 面板 / 旧版 IMGUI 窗口"、
  幽灵女巫页的"基于 Ghost 预设"。

**同步的守卫（AGENTS.md 4.10）**

- `tests/ZombieModeMutantWikiGuard.py` 原本要求两张表里出现反引号包裹的枚举成员，
  这条断言本身就是"内部标识必须出现在玩家页面"。改为从
  `Localization/LocalizationInjector.cs` 解析 `BossRush_ZombieMode_Special_*` /
  `_Affix_*` 的中英显示名，再断言这两个名字出现在对应表里——覆盖不变，
  额外多锁一层"Wiki 用词必须等于游戏内显示名"。反向验证：把任一注入名改掉即转红。

**已核对无误（抽样列举）**

- 遗种巢：240 凝蛋 / 1.5% 异色 / 12→24 容量与 10·20·30 里程碑 / 放生退 60 /
  远征 2·4·8 小时与 0%·6%·12% / 10 级 100 exp / 每 3 级 +1 格（`PetNestTuning.cs`）。
- 随机事件：八事件权重 30·25·18·15·15·12·10·8 换算出的 23/19/14/11/11/9/8/6%、
  90 秒静默、45~75 秒间隔、频率档 2/3/5、空投 120 秒续期硬帽（`RandomEventsTuning.cs`）。
- 词缀锻造：62/30/8 与 60/30/10、槽位阈值 5/7、锁 2 颗解锁免费、Boss 掉率 8%、
  好感 Lv.2 库存 5，以及 12 条词缀的全部 T1/T2/T3 数值（`AffixDefinitions.cs`）。
- Mode H：六场 180 秒、同屏敌 2/2/3/3/3/3、赔率阈值 20/5/-9/-24、筹码 6/30/2、
  奖励候选除数 2 上限 3、押品单场 3 件与"按赔率整数倍发最高品质"、名人堂 32 席、
  铃 1 次 6 秒、8+5 条口令、四类侦察、五个装备槽（`ModeHConfig.cs` / `ModeHStateModel.cs`）。
- 鸭王征程六章目标与 20000/35000/50000/75000/100000/200000（`Assets/Data/Campaign/Chapters.json`）、
  公告板 500、终章 1.6 倍属性 / 1.15 体型 / 绯红。
- 后山：种子 25%、20 分钟现实时间成熟（官方 `Crop.Tick` 走 `DateTime.Now`）、收 2 个、
  展示柜 800 金 2×1 只能一个、(品质-4)×0.5% + 满格 5% = 上限 21%、三种出击餐数值、
  点唱机两首曲名。
- 日报：报箱 500 只能一个、一个游戏日 86300/60 ≈ 24 分钟、五类悬赏三档目标与奖金、
  签到 30 格与 7/15/24/30 → Q5/6/7/8、第 2 期起 7/14/21/28 → Q8。
- 图鉴：售价 4000 库存 1、4 列网格、里程碑 5/15/40/100 万 + 十秒之内 20 万、Mode H 击杀不计。
- 成就大全：45 项 / 9 分类 / 3 个隐藏 / 总额 $19,840,000（40 条主表 + 5 条图鉴条目实测求和）。
- Mode F 命火：入场上限成长 4%/杀封顶 50%、每杀充 8 点、15 秒过载 +40%/+15%、
  失血 ×2、悬赏 +3 秒封顶 24 秒、余烬 25 点。
- 重铸投入曲线 10×/100×/1000× → +10%/+30%/+100%。
- 配置页三项新旋钮（`randomEventsFrequency` / `modeGAbandonHotkey` / `backMountainUnlockAll`）
  与实际注册项一一对应；八个内容系统总开关确实未注册进 ModConfig UI，页面的说明属实。

**验证**

- `python tools/run_guards.py`：PASS=516 / NEW-FAIL=0 / KNOWN-RED=0。
- `node wiki-site/scripts/sync-content.mjs`：222 篇重新生成，
  `BossWikiGuideContentGuard` 与 `ZombieModeMutantWikiGuard` 的逐字节比对 PASS。
- 文档-only，未触碰 `.cs`，无需编译；游戏内 Wiki 面板的实机翻页仍待人工 smoke。

### 2026-09-03 补：Wiki 二次审核（官方本地化表比对）

第一轮之后 owner 追问 `Cname_Boss_Blue` 到底是什么，顺手把游戏本体的官方本地化表
（`Duckov_Data/StreamingAssets/Localization/*.csv`）接进核对流程，重跑了一遍全量审核。
分类：`SAFE`（文档）。

**`Cname_Boss_Blue` 的答案：它的官方显示名就是字面的「???」**

- `ChineseSimplified.csv:5503` 与 `English.csv:5532` 都写着 `\?\?\?`；
  `Cname_Boss_Red`（龙裔遗族借用的那只）同样是「???」，`Cname_IslandBoss` 是「口口口口」。
- `ModeD/ModeDWaves.cs:427` 前 1~2 波过滤小怪时也显式排掉 `"???"` / `"？？？"`，
  佐证这是真实会显示出来的名字，不是缺 key。
- 因此 zh/en 共 9 处 `Cname_Boss_Blue` / `Blue Boss` 全部改写为「原版那只名字显示为「???」的 Boss」，
  不再向玩家暴露本地化 key。

**二次审核新查出的事实性错误**

- `boss__overview`（zh/en）写"前 20 波不会出现三大原创 Boss / heavy hitters 含 Phantom Witch"。
  `WavesArena/WavesArena.cs:48` 的 `EarlyWaveExcludedBosses` 只有 StormBoss1-5、
  `DragonDescendant`、`boss_dragonking`——**幽灵女巫（`boss_phantomwitch`）不在名单里**，
  第 1 波就可能出现。zh/en 均改正，并在 `mode__mode_a` / `mode__mode_d` /
  `tips__hell_and_mode_d` 同步补一句。
- 同页写"白手起家 6-15 波屏蔽强力 Boss，16 波后全 Boss 池开放"。
  `ModeD/ModeDWaves.cs:363` 的过滤条件是 `modeDWaveIndex <= 10`，**全池从第 11 波就开了**；
  16 波是另一件事——`GetModeDWaveBossCount` 在 16+ 返回 `totalEnemies`，
  即**整波不再有杂兵，全是 Boss**。zh/en 的 Mode D 页原写"2+ 个 Boss"，同样低估。
- `system__mutators`（zh/en）写"每局随机抽取 1~10 个词条"，读起来像数量随机。
  实际数量由 `mutatorCount` 固定（默认 3，可调 1~10），随机的只是抽到哪几条。
- 词缀熔石与遗种蛋一样是**掉在 Boss 身上**（`AffixForgeStoneDropService.TrySpawnStoneIntoBossInventory`），
  但只有遗种蛋那条写了"搜尸"。`system__affix_forge`、`item__key_items` 与三张 Boss 页
  统一补上落点，并标注熔石的模式覆盖与遗种一致（宿命回响 / 百战留痕 / 丧尸不掉）；
  种子则明确写"掉进战利品箱"（`BackMountainSeedDrops` 走 lootbox `inv.AddItem`）。
- 丧尸模式的怪名继续对齐官方口径：`equipment__frost_spear`、`equipment__thunder_set`
  两页残留的"疾行者 / 骚扰者（Harasser）"改为冲刺丧尸 / 骚扰丧尸。
- `equipment__frost_spear` / `equipment__frostmourne` 写"触发原版 `Cold` 效果（减速目标）"。
  官方 `Buff_Cold` 的中文名是「寒冷」，效果是"移动能力、射击速度与冰属性抗性都会下降"，
  不止减速。改用显示名与完整效果描述。

**继续清掉的内部术语**

`Cname_Zombie` 预设、"运行时 View 面板 / 旧版 IMGUI 窗口"、"基于 Ghost 预设"，
以及 `mode__mode_a` / `mode__mode_e` / `system__codex` / `system__boss_filter_and_wiki`
里剩下的"预设 / preset"表述，全部改写成玩家看得懂的说法。

**本轮额外核对通过（未发现偏差）**

- 无间炼狱：每波 +2%（`ModBehaviour.cs:1281` `1 + 0.02 * waveIndex`）、现金 = 最大生命 ×10、
  每 100 波皇冠 `2^(tier-1)` 与现金 `1000 万 × 2^(tier-1)`。
- 白手起家开局包：护甲/头盔 50%、近战/背包 40%、图腾/面罩 30%、附件槽 30%、
  医疗 3 件、弹药 120~180；敌人品质 `1 + 波次/5 + 血量/500` 封顶 6；每波血量 +3%。
- 划地为营：BEAR ×2.5、Boss 每层 +5%、玩家每次 +0.1%、贝壳 500 血≈10 且翻倍 +3、
  晋升 Boss 70%、8 米半奖、首次 +10、换算单位 2500、雇佣 1000 血≈200 且 50~2000 取整到 10、
  13 个分类商店、四种战术道具的 10 点 / 全图 / 50 米 / 全图口径。
- 宿命回响：波次编排 `{1,2,1,1,3,1,1,3,1}`、宿敌 3/6/9、休整 8s/20s、
  Last Stand 12 秒且只在多 Boss 波、属性封锁 ×0.75、Resolve 上限 11 = 3+3+2+3。
- 掉落：箱内件数 clamp 7~15、`bonusFactor = 0.8×血量 + 0.2×速度`、
  Q8/Q7/Q6/Q5 的 0.05%→0.10% / 0.10%→1% / 1%→5% / 5%→10%、
  低品质权重 0.1345:0.3655:0.3655:0.1345、Q5+ 保底门槛 250 血与 90/9/0.9/0.1 分布、
  通关箱 3 件 / 10 件。
- 变异词条：28 条，敌 9 / 玩家 11 / 环境 8，逐条数值与天降殉爆的 3 米 / 40 火伤。
- 死亡亡魂：50%/10% 分档与 10×/6×/3× 生命、1.5/1.25/1.0 伤害、1.9/1.5/1.2 移速、
  0.9/0.8 机动。
- 许愿台：20~10000 字、发送冷却 30 秒、抽奖冷却 4 小时；Boss 筛选器 Ctrl+F10。
- NPC：叮当聊天 +40 与 10%/15%/20% 折扣、羽织聊天 +30 与 10%/20%/25%/30%/40% 折扣、
  喜欢 +80 / 普通 +20 / 戒指 +500 / 砖石 -60。
- 龙裔加权掉落 60/30/10 合计 100%、龙王 15/15/15/15/1/39 合计 100%、
  幽灵女巫 50% 独立追加；宿命回响信物 20000 库存 5、船票库存 10。

**验证**

- `python tools/run_guards.py`：PASS=516 / NEW-FAIL=0 / KNOWN-RED=0。
- `node wiki-site/scripts/sync-content.mjs`：222 篇重新生成，两个 Wiki 逐字节比对 guard PASS。
- 文档-only，未触碰 `.cs`。

### 2026-09-03 补二：官方名字守卫 + 官方本地化表快照

承接同日两次 Wiki 审核。owner 认可"写个 guard 盯官方改名"的提议，并要求把官方本地化表
存进 `docs/` 便于以后查。分类：`SAFE`（文档 + 新增 guard）。

**新增 `docs/reference/official-localization/`（local-only）**

- 从 `<GAME_PATH>\Duckov_Data\StreamingAssets\Localization\` 拷 `ChineseSimplified.csv`
  与 `English.csv` 两份，附 `README.md` 说明格式、转义（`\?` `\ ` `\.`）、已知坑与刷新方式。
- **不纳管**：它是游戏本体资产，落在 `.gitignore` 的 `/docs/*` 里。已在 .gitignore 的
  「guard 依赖文档必须放行」注释块下补一条反例说明，避免下次有人误以为漏登记了。

**新增 `tests/OfficialNameReferenceGuard.py` + `tests/official_name_references.tsv`**

登记表 6 列：key / 官方中文名 / 官方英文名 / 中文页面写法 / 英文页面写法 / 页面清单。
「页面写法」写 `=` 表示与官方名逐字相同，只有官方名带冠词时才另写
（`Quest_502` 官方是 `The Four Horsemen`，正文里写 `Four Horsemen`）。

三向断言：

1. 官方表里 key 仍映射到登记的中英文名 —— **官方改名立刻转红**；
2. 登记页面里仍出现「页面写法」—— Wiki 被改写时转红；
3. 任何玩家页面都不许出现 key 本身 —— 防本地化 key 回流。

表的解析顺序 `GAME_PATH` → `docs/reference/official-localization/` → **skip**。skip 是刻意的：
CI（`.github/workflows/guards.yml`）和 fresh clone 两处都没有游戏资产，硬失败只会制造噪声。

当前登记 13 条：`Cname_Boss_Blue` / `Cname_Boss_Red`（都是「???」）、`Cname_StormBoss1-5`、
`Cname_IslandBoss` 口径的口口口口、`Quest_502` 四骑士、`Buff_Cold` 寒冷、`Cname_Ghost` 幽灵、
`Character_SnowPMC` 煤球、`Cname_Merchant_Myst` 神秘商人、`Item_Crown` 皇冠。

反向验证六项全部逐条转红/转绿：改登记的官方名 → FAIL；页面删掉该名字 → FAIL；
把 key 塞回玩家页面 → FAIL；两处表都移走 → SKIP 且 exit 0；`GAME_PATH` 分支 → PASS；
恢复后 → PASS。

**本轮据此修掉的描述不符**

- `equipment__frostmourne`（zh）写"命中附加原版 Cold 效果（减速敌人）"与"Cold Protection +2"。
  官方 `Buff_Cold` 中文名是「寒冷」，效果是"移动能力、射击速度与冰属性抗性都会下降"。
  改用显示名与完整效果，并把 `Cold Protection` 改成「寒冷防护」。
- `mode__mode_e`（en）把 Mode E 的宠物写成 `Coalball`。交互按钮确实是 `Summon Coalball`
  （`LocalizationInjector_NpcUiAndItems.cs:533`），但召唤出来的是官方 `Character_SnowPMC`，
  场上显示名是 `Coal Briquette`。补一句点明两个名字，避免玩家对不上号。
- `item__key_items`（zh/en）：船票条目补「商店里的名字」一行。物品真名是
  `Boss Rush船票` / `Boss Rush Ticket`（`LocalizationInjector.cs:26`），
  而全站正文为可读性写作「BossRush 船票」。只在物品条目钉死真名，正文不做全站替换。

**顺带查明、但属于代码侧的两条（已开 task chip，未擅自改 .cs）**

- `GoblinAffinityConfig.cs:104` 好感 Lv.4 奖励列表写「冷萃液」，物品真名是「冷淬液」
  （`ColdQuenchFluidConfig.cs:60`），英文 `Cold Quench Fluid` 也对应「淬」。玩家可见的错别字。
- `WikiBookItem.cs:39/44/59` 的 `WIKI_BOOK_DISPLAY_NAME*`（"Boss Rush 百科全书"）全无调用点；
  物品实际显示名走 `LocalizationInjector.cs:42` 的「冒险家日志」。同一物品挂着两套名字，
  且没用的那套看起来更像权威常量。

**另外核对无误**

Mod 自有物品/装备的 52 个 `DISPLAY_NAME_CN` 逐个比对 Wiki，除上述船票与两处营旗的
空格排版差异（`USEC营旗` vs 正文「USEC 营旗」，属中文排版惯例，不改）外全部一致；
Mode E 阵营标签「拾荒者 / USEC / BEAR / 实验室 / 狼群 / 独狼」
（`ModeEIntegrityAndHelpers.cs:213`）与 Wiki 一致——官方 `Cname_Usec` 的「雇佣兵」
是单位名不是阵营名，不构成冲突。

**验证**

- `python tools/run_guards.py`：PASS=517 / NEW-FAIL=0 / KNOWN-RED=0（新 guard 已计入）。
- `python tests/OfficialNameReferenceGuard.py`：PASS，13 条官方名核对通过。
- `node wiki-site/scripts/sync-content.mjs`：222 篇重新生成，逐字节比对 guard PASS。
- 文档 + guard，未触碰任何 `.cs`，无需编译。

### 2026-09-03 补三：修掉两条代码侧描述不符（owner 批准）

承接同日 Wiki 审核开出的两条 finding。owner 批准动代码。分类：`SAFE`（玩家可见文案修正 + 删死代码）。

**1. 叮当好感面板把「冷淬液」写成「冷萃液」**

`Integration/Affinity/NPCs/GoblinAffinityConfig.cs` 的 Lv.4 解锁项写的是字面量
`L10n.T("冷萃液", "Cold Quench Fluid")`，物品真名是「冷淬液」
（`Integration/Reforge/ColdQuenchFluidConfig.cs:60`），英文 `Cold Quench Fluid` 也对应「淬」。
玩家在好感面板看到的是错别字，和物品本体对不上号。

修法不是把字面量改对，而是**收口到物品 Config 自己的 `GetDisplayName()`**：
Lv.2 的钻石、Lv.4 的冷淬液、Lv.7 的钻石戒指三处一并改为
`DiamondConfig.GetDisplayName()` / `ColdQuenchFluidConfig.GetDisplayName()` /
`DiamondRingConfig.GetDisplayName()`，并就地留注释说明这里禁止抄字面量。
这条 bug 的成因就是抄字面量，只改一个字下次照样会漂。

时机语义不变：原代码同样在字典懒建时求值，`GetDisplayName()` 就是同一组常量上的 `L10n.T`。
`Cold Quench Fluid` 是内联字面量不是本地化 key，不涉及存档/配置契约。

羽织侧的同类清单（`NurseAffinityConfig.cs:94-102`）已逐项比对
`CalmingDropsConfig` / `PeaceCharmConfig`，字面量与物品名一致，**无漂移，未改动**。

**2. `WikiBookItem.cs` 里一整套零调用的显示名/描述常量**

`Integration/WikiBookItem.cs` 曾同时存着：

- `WIKI_BOOK_DISPLAY_NAME_CN = "Boss Rush 百科全书"` / `_EN = "Boss Rush Encyclopedia"`
- `WIKI_BOOK_DESCRIPTION_CN` / `_EN`
- 两个包装属性 `WIKI_BOOK_DISPLAY_NAME` / `WIKI_BOOK_DESCRIPTION`

六个成员**全部零调用点**（grep 排除 Build/ 核对）。物品实际显示名与描述走
`Localization/LocalizationInjector.cs:42` 的 `WIKI_BOOK_NAME_CN = "冒险家日志"`
与 `WIKI_BOOK_DESC_CN`，由 `InjectWikiBookLocalization` 注入，预制体按 displayName 反查 key。

即同一物品挂着两套名字，而**没生效的那套看起来更像权威常量**，读代码的人会被带偏。
六个成员全部删除，原地留注释指明真名的唯一落点，并写明不要再补第二份。
保留「冒险家日志」是无行为变化的那一侧：它已发布、且 `WikiContent/` 全站按它写。
若 owner 想反过来改名为「Boss Rush 百科全书」，那是一次玩家可见改名 + 全站 Wiki 改写，另立项。

**验证**

- `tools/verify_syntax.py`：762 个编译源，语法层 CS1xxx 零错误。
- Windows `compile_official.bat`（`GAME_PATH=D:\software\steam\...\Escape from Duckov`）：
  **Build succeeded，0 error 0 warning**，编译器输出无任何 `CS####` 诊断行；已部署到游戏目录。
- 覆盖改动文件的 guard 逐个手跑：`ArchitectureStructureGuard`、
  `BossRushDynamicItemRegistryGuard`、`DeferredIntegrationBootstrapGuard`、
  `MenuSceneRuntimeHookGuard`、`TestLogicWiringGuard`、`LocalizationInjectionGuard`、
  `OfficialCompileListFileExistenceGuard`、`OfficialNameReferenceGuard` —— 全 PASS。
- 游戏内人工 smoke 待做：叮当好感面板 Lv.2/4/7 三行文案、以及冒险家日志的物品名。

**⚠️ 本条之外的两个红项不属于本次改动（Needs owner attention）**

全量 `python tools/run_guards.py` 当前是 PASS=515 / NEW-FAIL=2：

- `ModeHBattleSnapshotGuard.py`（11 项）与 `ModeHStandInGuard.py`（1 项）转红。
- 这两个 guard 只读 `ModeH/*.cs`（BattleSnapshot / CombatControl / StandInPerformer /
  StateDtos / StateModel / Config / HarmonyPatches / RuntimeGates），
  本次改动只碰 `Integration/` 下两个文件，不在其读取范围内。
- 直接原因是工作区里有一次**并行进行中的 ModeH 重构**：
  `ModeH/ModeHBattleSnapshot.cs` 相对 HEAD 是 34 增 / 173 删，
  `ModeH/ModeHCombatControl.cs` 是 143 增 / 38 删，mtime 21:17-21:18；
  快照捕获与 fail-closed 判据被搬出 BattleSnapshot 后，guard 仍按原文件断言，故转红。
- 按 AGENTS.md 4.10，这两个 guard 应由做该重构的人同步，本次不代改、不放宽断言。
- 另有一次 `OfficialCompileListFileExistenceGuard` 瞬时红（抱怨
  `Utilities/__probe_test/BossCombatProbe.cs` 未登记），是 `tools/verify_syntax.py`
  的临时探针目录尚未清理导致，重跑即绿，非真实缺口。

---
### 2026-09-04 审核修复批（本会话）：3 个 P0 + 4 个 P1 + 9 条 P2/P3

**状态**: fixed（Windows 编译 `Build succeeded` exit 0、本批改动零 error 零 warning；
新增 5 个守卫并做过**反向验证：9 条人为破坏 9 条被抓到**；实机 smoke 待人工）

**Finding**: CR-2026-09-04-010 ~ CR-2026-09-04-020

**兼容分类**: `COMPAT` 为主，含 `SAFE`。**未触碰任何冻结契约**——
原计划以为 P0-B 要动 §22.2 冻结转换表并已取得 owner 签字，实际核对后确认
`ResultCommitted → SettlementPending → Terminal` 本来就是合法路径，缺的只是按阶段分派，
因此该授权**未被使用**，`docs/contracts.md` 无需改动。

**Owner decision**: 需要过 —— owner 2026-09-04 拍板「全部修复（P0→P3）」并批准动冻结表（未用上）；
并行会话冲突时选择「避开它，我做其余部分」。

**现象 / 根因**: 逐条见 `CODE_REVIEW_FINDINGS.md` 的 CR-2026-09-04-010..020。
共性仍是本仓库反复出现的**静默失败**：编译绿、守卫绿、日志最多一行 warning，功能实际不工作。

**修复内容**:
- 新增文件: `ModeH/ModeHWarehouseStakeJournalStorageBuffer.cs`（journal 的 partial 第二文件，
  为 1200 行预算拆出，先例照 `Config/ConfigModConfigKeys.cs`）、
  `Localization/RandomEventsLocalization.cs`。**两个都已登记 `compile_official.bat`**。
- 新增守卫: `ModeHStakeEscrowDurabilityGuard`、`NonWaveBossSpawnGuard`、
  `SaveCoordinatorRetryGuard`、`PlayerFacingMessageGuard`、`CompanionCleanupExemptionGuard`。
- 修改文件: `ModeHWarehouseStakeJournal` / `ModeHRealStakeService` / `ModeHRuntimeModule` /
  `ModeHRuntimeModule_CombatFlow` / `ModeHProfilePersistence` / `ModeHLocalization` /
  `EnemySpawnCore` / `DragonKingBoss` / `PhantomWitchBoss` / `DragonDescendantBoss` /
  `RandomEventEffectsBridge_Spawn` / `RandomEventAirdropHold` / `ModBehaviour` /
  `UIAndSigns` / `BossRushEagerReflectionCache` / `CampaignSaveCoordinator` /
  `CodexSaveCoordinator` / `CodexView` / `ShowcaseService` / `ModeFRespawn` /
  `AlwaysOnRuntimeHooks` / `PetNestUI` / `PetNestUIPages` / `LootBlacklistRegistry` /
  `BossRushIntegration_StartAndScene` / `Assets/Data/LootBlacklist.json` /
  `run_guards.bat` / `verify_syntax.bat` / `.gitignore` / `AGENTS.md` /
  `tests/ModeHStakeJournalGuard.py` / `tests/ModeHIsolationGuard.py`。

**兼容性影响**: 不改存档 schema、不改 TypeID、不改 Harmony 目标、不改冻结转换表。
掉落黑名单新增 9 个 ID 属**加法**（这些物品本来就不该进随机奖池）。
`_storeFaulted` 改为随槽复位、`RestoreForSlotChange` 改为丢弃旧 run，都是把「跨槽泄漏」收窄。

**验证方法**:
1. 编译: `& D:\code\ykf\BossRushMod\compile_official.bat` → `Build succeeded`，exit 0。
2. Guard: `python tools/run_guards.py`。本批完成时全量 528 PASS；
   之后工作树被并行会话的未登记新文件（`Integration/NPCs/DuckNpc/Permanent/`）改红，
   与本批无关。
3. 反向验证: 5 个新守卫共 9 条人为破坏，**9 条全部转红**，还原后全绿。
   过程中该验证抓出我自己写的两条断言太弱（子串匹配可被 `xxx_REMOVED` 绕过），已改为正则词边界。

**未验证/需人工**（全部为运行时行为，编译与守卫证明不了）:
- 押品：仓库填满后胜利结算；构造结算落盘失败后走恢复壳「取回押品」；重启确认物品在仓库码头。
- 乱入 Boss：标准竞技场触发 Boss 乱入，确认真 Boss 击杀仍推波、乱入者死亡不推波。
- `ShowMessage`：任意触发一次，确认玩家看得到。
- 空投：开着战利品界面到点，确认箱子被延后销毁。
- 展示柜：带收藏进局确认开局满血且上限已抬高。

**失败尝试 / 自我更正**:
- P0-A 首版把「清空内存 escrow 前排空到官方缓冲区」也用在了 `LoadPersisted` 上。
  但 `ModeHProfilePersistence.HandleSetFile` 调它时 `PlayerStorage` **已经指向新槽**，
  那会把旧档的装备搬进新档——比原来的静默丢失更糟（可当作跨档搬运手段）。
  已改为按调用场景分开：同槽的宿主销毁交缓冲区，换槽只记账不做物理转移。
- 两次因新增空 catch 顶破 `empty_catch_budget.txt` 的零余量，**都用补日志解决，未上调预算**。

**交接给并行会话 / owner 的项**:
- Mode H 伤病 `armor` 是完全空操作（`IsArmorKitDisabled` 零读者）。正确修法要在 kit 装配时
  查该选手的伤病，而 kit 装配发生在伤病绑定**之前**；且该子系统正被并行会话重构
  （`ModeHEffectConditions` / `appliesWhen`），不宜由本会话强改。
- Mode H `finish` 口令对任何 stable key 恒不可选（认证探针的 `ReadField` 不含点火型控制点）。
  同属该子系统。

