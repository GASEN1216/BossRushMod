# FIX_TRACKER.md — 修复状态与兼容性流水账

> 修 bug、回归、兼容问题或 owner decision 后更新本文件。旧路径 `docs/协作/FIX_TRACKER.md` 只做兼容转发。

## 状态定义

| 状态 | 含义 |
| --- | --- |
| `fixed` | 已修复，并记录验证方式 |
| `accepted` | 已确认是问题，方案已定，尚未落地 |
| `refuted` | 曾怀疑是问题，核实后不是 |
| `deferred` | 确认是问题，但本轮不修，原因明确 |
| `documented` | 不改代码，仅文档化限制、取舍或人工验收要求 |
| `needs-owner-decision` | 需要 owner 对兼容/产品/安全取舍拍板 |

## 条目模板

```markdown
---
### YYYY-MM-DD 标题

**状态**: fixed / accepted / refuted / deferred / documented / needs-owner-decision  
**Finding**: CR-YYYY-MM-DD-NNN / 无  
**兼容分类**: SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**版本/Commit**: commit hash / 未提交 / 不适用  
**Owner decision**: 需要/不需要；结论
**现象**: 玩家可见症状、日志或触发条件
**根因**: 代码路径、引入机制、为什么会发生
**修复内容**:
- 新增文件: 路径（是否加入 `compile_official.bat`）
- 修改文件: 路径
**兼容性影响**: 对存档、配置、TypeID、Harmony/反射、资源、部署的影响
**验证方法**:
1. 编译:
2. Guard:
3. 人工 smoke:
**未验证/需人工**: 如有
**失败尝试**: 如有
```
---
### 2026-08-07 Mode E 分类商店首开卡顿与贝壳价格过低

**状态**: fixed
**Finding**: 玩家实机反馈 / `Player.log` 与官方 `StockShopView.Setup` 反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 本提交
**Owner decision**: 已确认；提高 Mode E 贝壳商品价格并修复商店首开卡顿
**现象**: Mode E 商人分类商店首次打开前会明显卡顿；首轮分帧创建上线后，玩家实测从饰品切到护甲仍会出现数秒 0 帧停顿。当前 `/10000` 价格量化还使玩家击杀少量 Boss 后即可快速购买大量商品。
**根因**: 官方 `StockShopView.Setup()` 会在主线程一次遍历分类全部条目并同步创建、绑定 UI；当前最大分类有 456 件商品。首轮修复只分摊了创建，最新 `Player.log` 显示饰品已成功分帧创建 `24 + 432` 个条目，随后打开护甲时才卡顿；官方 `PrefabPool.ReleaseAll()` 对 active list 逐个调用 `Remove`，再逐个禁用和重挂 456 个 UI，切店释放仍形成二次方与布局尖峰。线性回收上线后的实测进一步显示 Setup 已降至 `1-3ms`、`ShowUI` 约 `37-72ms`，但完整打开仍偶发 `153-961ms`；剩余时间位于 Attach 阶段，其全量刷新使用 `GetComponentsInChildren(..., true)`，把对象池中已隐藏的旧条目也重复刷新。Mode E 贝壳奖励常见为晋升 Boss 每只约 7–20，首个正奖励额外 10，而大量低中价商品经 `/10000` 向上取整后只需少量贝壳。
**修复内容**:
- 修改 `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeEMerchantSupportClasses.cs`：Mode E 首开只同步建立 24 个商品条目，其余每帧追加 12 个；切店时先关闭商品内容根节点、清空 active 索引并线性归还旧条目，使原版 `ReleaseAll()` 不再重复做二次方删除，首批绑定后一次恢复布局；切换分类、关闭 UI、模式失效和 runtime reset 会让旧任务失效，反射契约漂移时回退完整原版 Setup；同分类完整加载后继续复用现有 UI。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：新增 `[Profile] shop setup sync` 与 `shop open sync` 日志，分别记录分类条目数、回收数、回收/Setup 耗时，以及样本就绪检查、`ShowUI` 和完整交互同步耗时，供实机确认剩余尖峰。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：Attach、余额和交易门控刷新不再遍历任何商品行；价格完成事件只刷新当前活动层级中匹配的商品行，排除对象池内隐藏条目；打开日志新增 `attachMs`。复用的一键卖出按钮尺寸改为每次从原整理按钮派生，避免重复打开后宽度累加。
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：现金价格量化单位由 `10000` 调整为 `2000`，即非取整边界商品约提高到原来的 5 倍贝壳价格，保留向上取整、子弹满堆和最小 1 贝壳规则。
- 修改 `tests/ModeEMerchantOpenReuseGuard.py`、`tests/ModeEShellPriceNormalizationGuard.py`：锁定 Mode E 作用域、首批/逐帧预算、线性回收、内容根节点恢复、完整 entries 恢复、生命周期失效和新价格单位。
**兼容性影响**: 不改变存档、配置、TypeID、商品池、Boss 奖励、出售现金、模式 F 或普通官方商店；沿用现有精确 `StockShopView.Setup(StockShop)` Harmony 目标，并新增对其私有 `_entryPool`、`PrefabPool.activeObjects` 和 `StockShopItemEntry.Setup` 的可选反射使用，字段类型或方法契约不匹配时不接管对应优化并回退原版行为。
**验证方法**:
1. Windows 编译: `compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: `ModeEMerchantOpenReuseGuard.py`、`ModeEShellPriceNormalizationGuard.py`、`ModeEShellHarmonyUiContractGuard.py` 通过。
3. 最新实机日志: 分类切换 Setup 为 `1-3ms`、`recycleMs=0`，护甲稳定重开为 `47-58ms`；日志未出现 Mode E 商店异常。该日志同时定位并推动修复了 Attach 隐藏池条目刷新尖峰。
4. 静态检查: 本次 Mode E 作用域 `git diff --check` 通过。
**未验证/需人工**: Attach 收敛后的最终 DLL 仍需按“饰品完整加载 -> 关闭 -> 打开护甲/诱饵”复测，确认新日志 `attachMs` 保持低值、完整打开不再出现百毫秒至秒级尖峰，UI 首屏立即显示且余下条目约 1 秒内补齐；还需复测 5 倍价格下的前 10 次击杀购买节奏。
**失败尝试**: 第一版只分帧创建，没有处理下一次切店时原版同步释放全部活动条目，最新实测仍卡；首次误把量化单位提高到 `50000`，复核公式后确认这会降低价格，未保留该改动；Harmony `__state` 初版公开方法触发 C# 可见性编译错误，已将该补丁方法收为私有静态并重新验证。


---
### 2026-08-06 丧尸模式变异僵尸中英文图鉴与源码一致性守卫

**状态**: fixed
**Finding**: `2026-08-03_玩家反馈保守优化方案复审.md` 第 12.1 节
**兼容分类**: SAFE
**版本/Commit**: `224c35d`
**Owner decision**: 不需要；纯文档与开发期静态导出，不修改战斗运行时
**现象**: 丧尸模式 Wiki 只有 Special/Elite 能力简表，未覆盖 `OfficialExploder`，也没有登记确定性的目标色、安全视觉体型、污染倍率、完整技能参数或精英颜色优先级。
**根因**: Wiki 页面早于变异视觉身份实现，`SpecialKind`、`EliteAffixes`、`ZombieModeTuning` 与运行时技能数据没有开发期一致性检查，页面随源码演进后出现信息缺口。
**修复内容**:
- 修改 `WikiContent/zh/mode/mode__zombie_mode.md` 与 `WikiContent/en/mode/mode__zombie_mode.md`：覆盖 6 个 Special 和 12 个 Elite 词缀，登记污染前战斗倍率、目标色、安全视觉缩放、颜色优先级、爆炸/冲刺/毒区/减速/召唤/护盾/适应等已知数据及运行时未知边界。
- 生成 `wiki-site/docs/game-modes/zombie-mode.md` 与 `wiki-site/docs/en/game-modes/zombie-mode.md`。
- 新增 `tests/ZombieModeMutantWikiGuard.py`：从 C# 枚举读取图鉴身份，核对调优/技能/视觉常量、双语覆盖、生成页同步和 ZombieMode 运行时代码无 Wiki 扫描引用；Python guard 不进入 `compile_official.bat`。
**兼容性影响**: 不改变 TypeID、存档、配置、掉落、战斗倍率、技能、资源或 schema；开发期同步继续以 `WikiContent/` 为唯一权威源，未新增 C# Wiki 导出器、战斗实体扫描或常驻工作。
**验证方法**:
1. C# 编译：未运行；本条只修改 Markdown、生成 Markdown 与 Python guard，没有 `.cs` 或编译清单变更。
2. Guard：`ZombieModeMutantWikiGuard.py` 通过（6 个 Special、12 个词缀、双语生成页同步）；`ZombieModeMutantVisualProfileGuard.py` 与 `BossWikiGuideContentGuard.py` 通过。
3. Wiki：`npm.cmd run sync` 成功生成中文 99 篇、英文 99 篇；`npm.cmd run build` 通过，VitePress 1.6.4 完成客户端/服务端 bundle 与页面渲染。
4. 静态检查：本条作用域 `git diff --check` 无 whitespace 错误，仅有仓库既有 LF/CRLF 转换提示。
**未验证/需人工**: 游戏内置 Wiki 的长表横向阅读、字体换行和颜色代码显示仍需实机打开页面确认；纯文档改动不需要战斗行为 smoke。

---
### 2026-08-06 玩家反馈：D/E/F 开局资源、丧尸破隐/朝向与分类商店重复卡顿

**状态**: fixed
**Finding**: 最新玩家反馈 / 完整调用链与官方反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: `012c900` / `8f5d9b5`
**Owner decision**: 不需要；沿用已确认的 ZombieMode 医疗/近战排除契约与现有 Mode E/F 商店边界
**现象**: Mode D/E/F 共用的开局医疗/近战发放仍可能绕过丧尸模式排除；丧尸模式前方延迟爆炸使用角色根节点朝向；安全区内对丧尸的致死一击或自定义近战伤害可能不破隐；Mode E/F 分类商店重开时每次重复全量 UI Setup，Mode E 附加按钮和余额文本还反复克隆/销毁。
**根因**: `GivePlayerStarterKit()` 只调用未带 Zombie 排除的池选择，医疗硬编码兜底也未过滤；`ZombieModeBattlefieldAreaCoroutine()` 直接读取 `player.transform.forward`；官方致死事件顺序为 `OnDead -> SetActive(false) -> OnHurt`，死亡处理先注销 marker 后，旧 `OnHurt` 破隐路径会提前返回，且自定义近战实际使用的 `Weapon` 标签未被识别；官方 `StockShopView.Setup()` 每次 `SetupAndShow()` 都遍历商品并对玩家/宠物/仓库重复调用会清空重建条目的 `InventoryDisplay.Setup()`，Mode E 附加控件生命周期也没有复用。
**修复内容**:
- 修改 `ModeD/ModeDEquipment_StarterKit.cs`：D/E/F 共用医疗与近战 starter pool 使用 `IsZombieModeRewardCandidateAllowed()`，401/402/403 硬编码兜底也经过同一过滤；无安全候选时跳过该格。
- 修改 `ZombieMode/ZombieModeRewardProjectileSpread.cs`：延迟爆炸优先取 `player.characterModel.transform.forward`，水平化并归一化，根节点/世界前方仅作兜底。
- 修改 `ZombieMode/ZombieModeWaveController.cs`：非致死命中只在确认 Zombie marker 后破隐；致死命中按官方同帧事件顺序在 `DeathSettled` 和 marker 注销前破隐；补充自定义近战 `Weapon` 标签，避免误伤非丧尸目标也破隐。
- 修改 `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeE.cs`、`ModeE/ModeEMerchantSupportClasses.cs`：同一 BossRush Mode E/F 分类商店重开时跳过重复官方 `Setup`；切换分类仍完整刷新商品列表，但相同玩家/宠物/仓库 Inventory 不再清空重建；一键卖出按钮与贝壳余额文本关闭时隐藏、运行时销毁时释放，避免重复 Instantiate/Destroy。
- 新增/修改守卫：`tests/ModeDStarterKitZombieFilterGuard.py`、`tests/ModeEMerchantOpenReuseGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`、`tests/ZombieModeRewardAreaOptionGuard.py`、`tests/ZombieModeSafeZoneGuard.py`。
**兼容性影响**: 不改变 TypeID、存档、配置、奖励数值、商店商品池或普通官方商店；新增 `StockShopView.Setup(StockShop)` 与 `InventoryDisplay.Setup(Inventory,Func<Item,bool>,Func<Item,bool>,bool,Func<Item,bool>)` Harmony 契约只在 BossRush Mode E/F 分类商店同步 Setup 调用栈内复用相同 Inventory，契约缺失时既有 Mode E fail-closed 逻辑保持关闭。新增文件均为 Python guard，不进入 `compile_official.bat`。
**验证方法**:
1. 日志：`Player.log`（2026-08-06 19:41，282265 bytes）未出现 `[ZombieMode]`、商店打开耗时或 BossRush 相关异常；可见异常是官方 `Duckov.MiniGames.GamingConsole.Load` 在 `Item ID:-1` 后的 NRE，以及第三方 TriangleDuckAttachmentExpansion 退出清理 NRE。
2. Windows 编译：`cmd.exe /c compile_official.bat` 通过并部署 `Build/BossRush.dll`。
3. Guard：定向 starter、safe-zone、延迟爆炸朝向、Mode E Harmony 契约和商店复用 guard 通过；`git diff --check` 通过。
**未验证/需人工**: ZombieMode 实际 characterModel 朝向、同帧死亡/对象销毁窗口、Harmony 实际命中、Mode D/E/F 开局抽样、安全区枪械/近战破隐、同店重开/切换分类 UI 手感和商店卡顿改善仍需实机验证；当前日志无法量化商店卡顿。
**失败尝试**: 无。

---
### 2026-08-06 Mode E 贝壳物品内部名误用导致商人全部关闭

**状态**: fixed
**Finding**: 玩家实机反馈 / 最新 `Player.log` 静态回溯
**兼容分类**: COMPAT
**版本/Commit**: `012c900`
**Owner decision**: 不需要；修复既定贝壳 economy capability 的运行时物品身份解析
**现象**: 进入 Mode E 后分类商人完全不生成；日志先出现 `[ModeE/Shell] capability disabled: session preflight failed`，随后出现 `merchant spawn preflight failed`。
**根因**: `ItemAssetsCollection.TryGetIDByName()` 按 `ItemMetaData.Name` 查找。当前游戏资源中贝壳内部名为 `SeaShell`，`Item_SeaShell` 是显示文本本地化键；错误查询返回 `-1`，使商人创建按 fail-closed 契约直接退出。
**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：使用常量 `MODE_E_SHELL_ITEM_NAME = "SeaShell"` 查询，并在返回 `-1` 或抛异常时记录可区分的身份诊断；反射契约与 Harmony 安装证明现在逐项报告失败名称。
- 修改 `tests/ModeEShellCurrencyBoundaryGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`：锁定 metadata name 与 localization key 的边界，并要求完整契约/补丁诊断。
**兼容性影响**: 不改变 TypeID、价格、余额、存档、配置、Harmony 目标或商人商品池；仅恢复当前资源下本应可用的 Mode E 贝壳 capability 和商人生成，并改善失败诊断。
**验证方法**:
1. 资源/官方契约：当前 `resources.assets` 同一贝壳记录包含内部名 `SeaShell` 与本地化键 `Item_SeaShell`；官方反编译确认 `TryGetIDByName` 比较 `Entry.metaData.Name`。
2. Windows 编译：`compile_official.bat` 通过并部署到游戏 Mods 目录。
3. Guard：全部 Mode E guards 通过；`BossWikiGuideContentGuard.py` 通过。
**未验证/需人工**: 需重新启动游戏进入 Mode E，确认日志出现 `M1 capability ready ... SeaShell=<TypeID>`、四个 Harmony 安装证明成功、神秘商人生成成功，并实际打开至少一个分类商店；购买/出售、Incoming Buffer、切图和 UI 手感仍需实机验证。
**失败尝试**: 无。

---
### 2026-08-06 Mode E 退役商店伪 null 现金旁路与复审 guard 失真

**状态**: fixed
**Finding**: 2026-08-03 玩家反馈保守优化方案复审 / 本轮完整调用链复核
**兼容分类**: COMPAT / SAFE
**版本/Commit**: `012c900` / `224c35d`
**Owner decision**: 不需要；修复既定“受管 Mode E 商店不得回落原版现金购买”契约，并收紧静态守卫
**现象**: Mode E 商店退役并调用 `Destroy` 后，Unity 会在实际销毁前把组件比较为 `null`。此时同帧迟到的购买/出售/UI 回调可能被三态路由误判为普通商店，放行原版现金逻辑。另有发货 guard 仍断言旧版五参数事务入口，造成正确实现被误报；Wiki 12 个目标页面没有成对同步与最低完整度 guard。
**根因**: `GetModeEShellShopPatchDisposition()` 使用 Unity 重载的 `shop == null` 作为 `PassOriginal` 条件，没有区分真实 CLR null 与仍在 tombstone 中的 Unity 伪 null；`ModeEShellDeliveryContractGuard.py` 未随事务 owner 签名收敛；Wiki 仅依赖人工同步和站点构建。
**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：用 `object.ReferenceEquals` 识别真实 CLR null，使受管但已退役的 Unity 对象继续返回 `Block`，不再回落现金路径。
- 修改 `tests/ModeEShellModeScopeGuard.py`：精确提取 disposition 方法，强制真实 null / 伪 null 分类顺序，并禁止重新引入 `shop == null` 放行。
- 修改 `tests/ModeEShellDeliveryContractGuard.py`：改为当前事务 owner 签名，并验证 `isSellAll`、Busy 所有权初始状态等契约。
- 新增 `tests/BossWikiGuideContentGuard.py`：检查 6 个 canonical 页面与 6 个生成页面、catalog 登记、玩家所需的概述/基础数据/技能或阶段/掉落/战斗建议/出现限制章节，以及忽略站点标题和提示框标记后的正文同步；不再强制开发者式源码路径、待实机字样、固定标题或表格数量。
**兼容性影响**: 不改变 TypeID、存档、配置、掉落、价格或交易 schema；仅阻止已属 Mode E 的退役商店在 Unity 销毁窗口误入原版现金路径。新文件为 Python guard，不进入 `compile_official.bat`。
**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过并部署。
2. Guard：复审文档 31 个 guard 加 Wiki guard 共 32/32 通过；全部 `ZombieMode*.py` 与 `ModeE*.py` 共 171/171 通过；编译清单与内容注册 guard 通过。
3. Wiki：`npm.cmd --prefix wiki-site run build` 通过，中文 99 篇、英文 99 篇同步，VitePress 1.6.4 构建完成。
4. 静态检查：`git diff --check` 无 whitespace 错误，仅有既有 LF/CRLF 提示。
**未验证/需人工**: Unity 伪 null 同帧迟到回调、Harmony 实际命中、商店对象销毁、UI 手感、Incoming Buffer 故障注入和性能仍需实机验证。
**失败尝试**: 首次从 `wiki-site` 子目录直接运行仓库根相对路径 guard，因工作目录错误失败；改回仓库根后通过，内容同步本身未失败。首次最终 `git diff --check` 还发现本条目的 Markdown 硬换行空格，已移除并复检。

---
### 2026-08-05 丧尸模式候选、准备节奏、直接掉落与变异辨识优化

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: COMPAT
**版本/Commit**: `8f5d9b5`
**Owner decision**: 已确认；采用九个 opaque TypeID 的上下文过滤、45/75 秒节奏和双失败脚底标记规则
**现象**: 丧尸模式可能抽到不适用的医疗品或近战物品，普通准备时间偏紧；敌人直接掉落在分类失败时可能退化为任意物品，重物或背包满时缺少统一落地反馈；Special/Elite 混群中的常驻辨识不足。
**根因**: 候选缓存只有标签/品质通用过滤；准备阶段只有单一常量；敌人直接掉落保留无类别 fallback 且未在 `AddAndMerge` 前投影负重；变异身份虽已登记，但没有安全视觉节点、完整 CustomFace 探针和双失败常驻 fallback 的闭环。
**修复内容**:
- 新增文件: `tests/ZombieModeRewardContextFilterGuard.py`、`tests/ZombieModeMutantVisualProfileGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeEntry.cs`、`ZombieMode/ZombieModeTuning.cs`、`ZombieMode/ZombieModeWaveController.cs`
- 修改文件: `ZombieMode/ZombieModeDropsAndPerformance.cs`、`ZombieMode/ZombieModeEnemyRuntime.cs`、`ZombieMode/ZombieModePollution.cs`、`ZombieMode/ZombieModeRuntimeHooks.cs`
- 修改文件: `tests/ZombieModeStateModelGuard.py`、`tests/ZombieModePacingTuningGuard.py`、`tests/ZombieModeEnemyDropInventoryGuard.py`、`tests/ZombieModeEnemyDropPickupBubbleGuard.py`
- 变异视觉节点现在同时检查 renderer 子树和 renderer 到敌人根节点的祖先链；武器、socket、碰撞、攻击、导航等祖先一律 fail-closed。Elite 常驻颜色按词条语义映射为黄/红/绿/紫/青/橙，并保留 CustomFace 与双失败脚底标记兜底。
**兼容性影响**: 不新增 TypeID、存档 key、配置 key 或地图 schema；过滤仅作用于 ZombieMode 对应医疗/近战上下文，同标签逐级降品质，不扩散到其他模式。敌人直接掉落不再做无类别 fallback，箱子和其他发货路径保持原样。全部 Boss 跳过变异视觉二次缩放。
**验证方法**:
1. Windows 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1&&compile_official.bat"` 已在最终共享代码树通过并部署
2. Guard: 15 个 ZombieMode 定向守卫全部通过；其中 `ZombieModeRunScopedRegistryGuard.py` 成功路径静默退出码 0
3. 静态检查: `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.1 节仍需游戏内完成 30 局医疗/近战候选抽样、200 次敌人直接掉落、CustomFace 覆盖与故障注入、安全缩放节点及碰撞/socket 核对，以及 50 敌人 Profiler 压测。

---
### 2026-08-05 Mode E 局内贝壳购买与 Boss 奖励闭环

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: BREAKING / WIRE+ / OPERATIONAL
**版本/Commit**: `012c900`
**Owner decision**: 已确认；购买替换为本局账本式贝壳，价格按现金基准 `/10000` 单调量化，首版限制 `amount == 1`，出售继续使用现金
**现象**: Mode E 分类商店沿用局外账户现金，裸装入场仍可用局外财富购买；敌对 Boss 没有局内货币奖励，缺少从战斗到购买的会话内经济闭环。
**根因**: 原商店完全复用官方现金购买事务、UI 和商品 sample 缓存，没有独立 session 余额、价格身份、交易 owner、发货提交边界或 Boss 奖励 state。
**修复内容**:
- 新增文件: `tests/ModeEShellCurrencyBoundaryGuard.py`、`tests/ModeEShellModeScopeGuard.py`、`tests/ModeEShellTransactionGateGuard.py`
- 新增文件: `tests/ModeEShellDeliveryContractGuard.py`、`tests/ModeEShellObserverOrderGuard.py`、`tests/ModeEShellPriceNormalizationGuard.py`
- 新增文件: `tests/ModeEShellRewardGuard.py`、`tests/ModeEShellRuntimeLifecycleGuard.py`、`tests/ModeEShellHarmonyUiContractGuard.py`（均为 Python guard，不进入编译清单）
- 修改文件: `ModeE/ModeE.cs`、`ModeE/ModeEStartup.cs`、`ModeE/ModeEMerchant.cs`、`ModeE/ModeEMerchantSupportClasses.cs`
- 修改文件: `ModeE/ModeEHarmonyPatch.cs`、`ModeE/ModeEBattle.cs`、`ModeE/ModeEBattle_ScalingAndRuntime.cs`、`ModeE/ModeELifecycle.cs`、`ModeE/ModeERuntimeModule.cs`
- 修改文件: `Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`、`compile_official.bat`
- 交易 owner 初始不认领官方 `buying`/`selling`；Buy、Sell、SellAll 只有在通过捕获商店 `Busy` 检查后才标记对应字段所有权，`finally` 不会清理由其他流程持有的 Busy。异步购买返回后还会确认捕获条目仍以同一对象引用存在于当前 `shop.entries`。
**兼容性影响**: Mode E 已登记分类商店的购买支付由现金破坏性替换为本局内存贝壳账本；不写存档、不生成实体贝壳，不改变商品池、库存、Bullet 满堆、出售现金、Mode F 或普通商店。新增四个精确 Harmony/反射契约与非 Harmony 安装证明；`compile_official.bat` 增加现有游戏 DLL `Plugins.dll` 引用以使用官方通知格式扩展。
**验证方法**:
1. Windows 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1&&compile_official.bat"` 通过并部署
2. Guard: 9 个 `ModeEShell*.py` 全部通过
3. Guard: 复审文档第 9 节列出的 10 个 Mode E 回归守卫全部通过
4. Guard: `StaticCacheLifecycleGuard.py` 为 `PASS=64, WARN=11, FAIL=0`，警告均为既有白名单
5. 静态检查: `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.2 节 30 项事务矩阵、M0 Harmony 命中/对象状态探针、Incoming Buffer 故障注入、UI 操作、Mode F 隔离和连续购买 Profiler 数据仍需游戏内执行；当前静态 guard 不能替代这些运行时门禁。

---
### 2026-08-05 三个原创 Boss 中英文完整攻略与数据卡

**状态**: fixed
**Finding**: 玩家反馈 / `2026-08-03_玩家反馈保守优化方案复审.md`
**兼容分类**: SAFE
**版本/Commit**: `224c35d`
**Owner decision**: 不需要；仅按当前活动源码补齐攻略和来源标注
**现象**: 龙裔遗族、焚天龙皇和幽灵女巫的中英文页面缺少统一生存/移动与武器/技能数据卡，阶段打法、常见误区、模式规则和来源说明不足。
**根因**: 旧页面以简短机制摘要为主，没有系统区分活动源码常量、掉落装备面板、AssetBundle 数据和待实机快照字段。
**修复内容**:
- 修改文件: `WikiContent/zh/boss/boss__dragon_descendant.md`、`boss__dragon_king.md`、`boss__phantom_witch.md`
- 修改文件: `WikiContent/en/boss/boss__dragon_descendant.md`、`boss__dragon_king.md`、`boss__phantom_witch.md`
- 生成文件: `wiki-site/docs/bosses/` 与 `wiki-site/docs/en/bosses/` 下对应六个页面
- 三位 Boss 的掉落概率与龙裔/龙皇成就奖金已补充到具体源码文件；幽灵女巫实际手持武器行逐项登记伤害、元素、攻击间隔、近战射程的待实机状态，以及散布、弹速、弹匣、装填的近战 N/A。
**兼容性影响**: 纯文档内容；不改变 `WikiContent/catalog.tsv`、运行时数值、TypeID、资源或任何 schema。龙裔二阶段直线弹与扇形弹明确记录 `SpawnBulletDirect` 强制 100% 火焰元素，未知面板继续标为待实机快照。
**验证方法**:
1. 同步: `npm.cmd run sync` 成功，中文 99 篇、英文 99 篇，共 198 篇
2. 构建: `npm.cmd run build` 通过，VitePress 1.6.4 完成客户端/服务端 bundle 与页面渲染
3. 定向检查: canonical 与生成页面均包含已确认的二阶段火焰元素、当前英文成就名称、掉落/成就源码路径及幽灵女巫逐项武器字段
4. 浏览器渲染: `/BossRushMod/` 下中英文三个 Boss 共六个路由均已打开；标题、正文、三张表、warning 块正常，根容器无横向溢出
5. 静态检查: Wiki 作用域 `git diff --check` 无 whitespace 错误，仅有换行符提示
**未验证/需人工**: 文档第 10.3 节的游戏内置 Wiki 实机阅读仍待后续验证，所有标注“待实机快照”的字段也仍需按指定采样条件补录。

---
### 2026-07-09 丧尸模式自定义自爆怪无距离门控且未自毁

**状态**: fixed
**Finding**: 玩家反馈 / 静态代码确认
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于既有 `ExploderTriggerDistance` 调参常量未接入、且“自爆怪”语义未闭环的行为修复
**现象**: 玩家反馈丧尸模式 BOSS 关有东西持续追着玩家爆炸。静态排查确认 BOSS 波仍会按既有压力系统维持环境尸潮；第 6 波以后特殊怪池包含自定义 `Exploder` 和官方 `OfficialExploder`。其中自定义 `Exploder` 的技能冷却到点后直接原地起手爆炸，没有检查与玩家距离，爆炸后不会死亡；起手期间若丧尸移动，旧实现还会保留起手时的旧爆心。
**根因**: `ZombieModeTuning.ExploderTriggerDistance = 2.5f` 已存在，但 `TryExecuteZombieModeSpecialSkill()` 的 `ZombieModeSpecialKind.Exploder` 分支未使用该距离门控；同时自定义爆炸只调用 telegraph/ExplosionManager 伤害路径，没有对自身走 `Health.Hurt()` 死亡链路，导致自定义自爆怪只要存活并追踪玩家，就会按 9 秒冷却反复放红圈爆炸。通用 telegraph 默认固定起手坐标，不能表达“自爆中心始终是丧尸当前位置”。
**修复内容**:
- 新增文件: `tests/ZombieModeExploderTriggerDistanceGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeEnemyRuntime.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeExploderOfficialSelfDestructGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变 BOSS 波环境尸潮规则，也不改变官方 `OfficialExploder` 的原版自爆保留逻辑。自定义 `Exploder` 改为遵守既有 2.5 米触发距离，技能起手红圈跟随丧尸当前位置，爆炸后走正常死亡链路自毁；被玩家提前打死时也会在死亡位置触发一次自爆。通过 marker 标记跳过技能自爆后的死亡二次爆炸，并让 pending 跟随红圈在来源已死亡时取消，避免双炸。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py` 通过
3. Guard: `python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
4. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py` 通过
5. Guard: `python tests\ZombieModeWaveEventMarkerCacheGuard.py` 通过
6. Guard: `python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
**未验证/需人工**: 需要进游戏在第 10 波及以后 BOSS 关实测，确认自定义 `Exploder` 只有贴近后才起爆，红圈/爆心跟随丧尸当前位置，起爆后正常死亡、不再反复追炸；同时确认起手期间被击杀只在死亡位置爆一次。`Hunter` Boss 每 5 秒闪近并在玩家脚下放爆炸圈仍是当前设计技能。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式瘟疫/骚扰特殊怪行为与公开描述不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于公开描述、调参常量与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 技能后确认，瘟疫者描述为持续毒云但旧实现是一次性爆点；精英 `ToxicAura`/`Plague` 描述为毒雾/毒云但旧实现也是一次性爆点；骚扰者描述为发射投射物命中后生成减速区，但旧实现是在玩家脚下直接延迟减速；共享区域 tick 在带 `slowPercent` 时直接给玩家上减速，导致可见区域外也可能吃到减速；腐蚀者毒圈有起手常量但首跳没有真正延迟。
**根因**: `TryExecuteZombieModeSpecialSkill()` 和 `TryExecuteZombieModeEliteSkill()` 复用了瞬发 telegraph damage helper 表达毒云/毒雾；骚扰者复用了玩家减速 telegraph 而没有投射物 runtime；`ZombieModeAreaTickRuntime` 只把 slow 当作全局玩家状态写入，没有走范围判定 helper；腐蚀区只把 startup 加进对象寿命，未传入首 tick 延迟。
**修复内容**:
- 新增文件: `tests/ZombieModePlagueCloudBehaviorGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeHarasserProjectileGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeAreaTickSlowScopeGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `tests/ZombieModeAreaDamagePlayerGuard.py`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变特殊怪/精英/BOSS 刷新池或数值常量。瘟疫者和精英毒雾改为 telegraph 后生成持续区域伤害云；骚扰者改为生成可飞行投射物，命中或到期后才结算 25 伤害并创建 3.5m 减速区；区域减速改为只在玩家位于可见区域内时生效；腐蚀区首跳遵守 `CorruptorZoneStartupSeconds`。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py` 通过
3. Guard: `python tests\ZombieModeHarasserProjectileGuard.py` 通过
4. Guard: `python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
5. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py` 通过
6. Guard: `python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py` 退出码 0
7. Guard: `python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 通过
8. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
9. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏在普通波和 BOSS 波实测，确认瘟疫/毒雾持续云、骚扰者投射物、腐蚀区起手与减速范围符合可见表现。
**设计确认项**: `SprinterDashStartupSeconds = 0.5f` 已在后续修复接入；`Hunter` Boss 仍按当前 Wiki 明文描述执行 15m 传送式 Dash，若后续要改成可躲避冲刺，需要 owner 作为设计改动确认。
**失败尝试**: 无

---
### 2026-07-09 丧尸模式疾行者瞬移与死亡爆炸表现不一致

**状态**: fixed
**Finding**: 全面静态排查 / Wiki 与调参常量对照
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于调参常量、公开描述与运行时行为对齐的行为修复
**现象**: 继续排查特殊丧尸、精英词缀和 BOSS 死亡效果后确认，疾行者描述/常量是 0.5 秒起手后的 12m 冲刺，但旧实现直接 `transform.position` 改坐标，表现为瞬移；精英 `Burst`、Splitter/Titan 死亡爆炸走 player-only 区域伤害路径，玩家会吃到伤害但缺少原生爆炸 VFX、屏幕反馈和墙体阻挡语义。
**根因**: `ZombieModeTuning.SprinterDashStartupSeconds` 只存在于调参表，`TryExecuteZombieModeSpecialSkill()` 的 `Sprinter` 分支没有接入；死亡爆炸沿用了早期 `DealZombieModeAreaDamageToPlayer()` fallback，而不是 `ExplosionManager.CreateExplosion` 封装。
**修复内容**:
- 新增文件: `tests/ZombieModeSprinterDashTelegraphGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 新增文件: `tests/ZombieModeDeathExplosionVisualGuard.py`（非 `.cs`，不需要加入 `compile_official.bat`）
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeComponents.cs`
- 修改文件: `ZombieMode/ZombieModePollution_RuntimeSkills.cs`
- 修改文件: `ZombieMode/ZombieModeBossController.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；不改变疾行者冷却、冲刺距离或死亡爆炸数值。疾行者改为先显示跟随本体的短起手提示，再用 `SetForceMoveVelocity` 执行 12m 冲刺，并在暂停、死亡、run 清理和销毁时归零强制速度；精英 Burst、Splitter/Titan 死亡爆炸改走原生爆炸路径，保留 fallback 避免 ExplosionManager 不可用时技能失效。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过并部署到游戏 Mods 目录
2. Guard: `python tests\ZombieModeSprinterDashTelegraphGuard.py` 通过
3. Guard: `python tests\ZombieModeDeathExplosionVisualGuard.py` 通过
4. Guard: `python tests\ZombieModeExploderTriggerDistanceGuard.py`、`python tests\ZombieModeExploderOfficialSelfDestructGuard.py` 通过
5. Guard: `python tests\ZombieModePlagueCloudBehaviorGuard.py`、`python tests\ZombieModeHarasserProjectileGuard.py`、`python tests\ZombieModeAreaTickSlowScopeGuard.py` 通过
6. Guard: `python tests\ZombieModeAreaDamagePlayerGuard.py`、`python tests\ZombieModePauseMenuGuard.py`、`python tests\ZombieModeTimeAxisGuard.py`、`python tests\ZombieModeRunOnlyCleanupGuard.py`、`python tests\ZombieModeCompileListGuard.py` 退出码 0
7. Guard: `python tests\ZombieBoomAttachmentSanitizerGuard.py`、`python tests\ZombieModeWaveEventMarkerCacheGuard.py`、`python tests\ZombieModeNormalZombieCapAndAggroGuard.py` 通过
8. 静态检查: `git diff --check` 无 whitespace 错误，仅有既有 CRLF 提示
**未验证/需人工**: 需要进游戏实测疾行者起手提示/冲刺体感，以及 Burst、Splitter、Titan 死亡爆炸的 VFX、屏幕反馈、伤害和墙体阻挡。
**设计确认项**: `Hunter` Boss 仍按 Wiki 明文描述执行 15m 传送式 Dash；若需要把它也改为可预警冲刺，应按设计改动单独确认。
**失败尝试**: 无

---
### 2026-07-08 焚天龙铳烟花弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳烟花弹表现修复
**现象**: 烟花弹 FireWork 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Firework profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Firework_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化烟花弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空烟花弹自定义资源特效引用，螺旋飞行、空爆分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 FireWork 烟花弹实测，确认开火、空爆分裂和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳纳米弹多余开火/终点特效

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳纳米弹表现修复
**现象**: 纳米弹 NM 与前面多个弹种类似，开火或弹幕结束处仍会出现不适配的多余资源特效。
**根因**: Nano profile 已关闭 `PlayObstacleHitFx` / `PlaySplitTriggerFx`，但仍保留 `Fx_DragonGun_Nano_*` 的 Trail/Hit/Explosion prefab，导致部分路径仍可实例化纳米弹专属资源特效。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅清空纳米弹自定义资源特效引用，追踪、分裂、伤害和标记逻辑保持不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过并部署到游戏 Mods 目录
2. Guard: `Get-ChildItem tests -Filter 'DragonKingBossGun*.py' | ForEach-Object { python $_.FullName }` 通过
**未验证/需人工**: 需要进游戏用 NM 纳米弹实测，确认开火、分裂触发和弹幕结束处不再出现多余光效。

---
### 2026-07-08 焚天龙铳雪球弹滚雪球重做

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳雪球弹表现修复
**现象**: 用户不希望 Snow 继续作为高弧线冰弹，而是要超慢速直线滚雪球：主雪球滚 5 秒并越滚越大，命中/死亡后分裂 4 个小雪球；小雪球滚 2 秒继续变大，出生后短暂无敌防止刚分裂就被敌怪吞掉。后续又要求主雪球最大改为旧最大雪球 10 倍、小雪球最大 5 倍、降低基础伤害，并且只允许主雪球留下冰区。
**根因**: 旧 Snow profile 使用 High arc / gravity / 落地冰区的通用配置，主弹和二段弹都复用同一套冰区生成策略；分裂弹没有专门的出生保护，也没有“当前体积影响伤害”的运行时状态。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunSnowballGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
- 修改文件: `WikiContent/zh/equipment/equipment__dragon_cannon.md`
- 修改文件: `WikiContent/en/equipment/equipment__dragon_cannon.md`
- 修改文件: `wiki-site/docs/equipment/dragon-cannon.md`
- 修改文件: `wiki-site/docs/en/equipment/dragon-cannon.md`
- 修改文件: `FIX_TRACKER.md`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名、掉落表或 Harmony/反射；仅调整 Snow profile 与 DragonKingBossGunProjectileAgent/发射上下文运行时表现。复用既有弹体、对象池、分裂、半径伤害和冰区系统；未新增每帧分配或独立弹幕系统。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 编译通过；若游戏进程运行中，自动部署会因 DLL 被锁而失败，需关游戏后重新部署
2. Guard: `python tests\\DragonKingBossGunSnowballGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunEnergyPwsGuard.py` 通过
5. Guard: `python tests\\F3DragonKingBossGunDebugKitGuard.py` 通过
**未验证/需人工**: 需要进游戏用雪球弹实测，确认主雪球直线慢滚 5 秒、最大视觉接近旧最大 10 倍且伤害随体积提高；主雪球命中/寿命结束分裂 4 个小雪球，小雪球 0.1 秒内不会被敌怪吞掉、2 秒内长到出生 5 倍；只有主雪球留下 1 秒冰区，小雪球不再铺冰。

---
### 2026-07-06 逆鳞致死触发兼容与复活后短暂无敌

**状态**: fixed
**Finding**: 玩家反馈 / 官源静态确认
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 本次提交
**Owner decision**: 不需要；属于官方 `Health.Hurt()` 时序变化后的逆鳞兼容修复
**现象**: 玩家反馈新版本更新后逆鳞不触发；致死伤害下无法完成“濒死回血、反击、碎裂”的保命流程。
**根因**: 当前官源 `Health.Hurt()` 的执行顺序为扣血后先进入死亡分支、触发 `OnDeadEvent` / `Health.OnDead` 并 `SetActive(false)`，最后才触发 `OnHurtEvent` / `Health.OnHurt`。逆鳞旧逻辑只在 `Health.OnHurt` 里看 `CurrentHealth <= 1`，致死伤害会先把主角送入死亡流程，导致保命窗口来不及触发。
**修复内容**:
- 新增文件: `tests/ReverseScaleLethalProtectionGuard.py`
- 修改文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleAbilityManager.cs`
- 修改文件: `Integration/ReverseScale/ReverseScaleConfig.cs`
**兼容性影响**: 不涉及 TypeID、存档 key、配置 schema、资源命名或掉落表；扩展既有 `Health.Hurt` / `Health.CurrentHealth` Harmony 兼容补丁，在玩家装备逆鳞且受到致死伤害时先钳到触发阈值，并确保逆鳞 `OnHurt` 回调已注册。逆鳞触发回血后新增 0.5 秒免伤窗口。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过；最终部署目标 `Mods\\BossRush\\BossRush.dll` 被正在运行的 `Duckov.exe` 占用，自动部署和手动覆盖均失败，`Build\\BossRush.dll` 已生成
2. Guard: `python tests\\ReverseScaleLethalProtectionGuard.py` 通过
3. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
4. Guard: `python tests\\MenuSceneRuntimeHookGuard.py` 通过
**未验证/需人工**: 关闭游戏释放 DLL 锁后重新部署，再进游戏装备逆鳞承受致死伤害，确认回血、棱彩弹、气泡、图腾销毁和触发后 0.5 秒免伤都符合预期。

---
### 2026-07-06 焚天龙铳火箭弹空爆只单次爆炸

**状态**: fixed
**Finding**: 玩家实机反馈 / 无
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于焚天龙铳火箭弹表现修复
**现象**: 火箭弹到空爆距离后只在空中触发一枚原版爆炸，未表现为多枚分裂火箭落地/命中后的连续爆炸；后续实测中分裂弹又会在敌人头上散开后高速飞走，且主弹仍按固定射程比例空爆，不是射到鼠标位置再爆开。再次调整时用户要求分裂弹不再按固定定时爆炸，而是依靠自身碰撞爆炸。最新实测中日志已缓存 `BulletRocket`，但分裂弹命中后没有爆炸。
**根因**: 火箭 profile 的 `SplitCount` 仍为 1 且使用向下分裂；同时主弹使用原版火箭预制体，空爆置 `dead` 后仍会被原版 `Projectile.Update()` 按 `context.explosionRange` 当作最终爆炸处理。后续迭代中分裂弹固定引信路径全生命周期跳过碰撞，且主弹空爆距离仍用 `distance * AirburstDistanceFactor`，没有读取玩家当前鼠标瞄准点；原版火箭预制体缓存还会接受 Rocket 口径武器上的 `BulletNormal_Burn`，导致分裂弹视觉/行为取错基底。官源 `Projectile.Init()` 的 `hitLayers` 只包含敌人、墙、地面和 `blockBulletLayers`，不包含 Projectile 自身，因此分裂弹之间不会因为彼此弹体互相触发爆炸。最新问题是分裂弹虽已使用原版 `BulletRocket` 预设，但自定义 `ProjectileContext` 没有设置 `explosionRange` / `explosionDamage`，原版 `Projectile.Update()` 因 `context.explosionRange == 0` 不会调用 `ExplosionManager.CreateExplosion`。
**修复内容**:
- 新增文件: `tests/DragonKingBossGunRocketSplitGuard.py`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs`
**性能处理**: 按用户要求将主弹/分裂弹预设互换：主弹用焚天龙铳轻量弹体负责飞到玩家鼠标瞄准点后空爆，分裂弹使用原版火箭视觉，并且原版预制体缓存改为按火箭弹 TypeID `326` 的确定关系绑定：只接受 `TargetBulletID`、`PreferdBulletsToLoad.TypeID` 或枪内当前装填弹 TypeID 等于 `326` 的原版枪械，不再用 `Rocket` / `RPG` / `Missile` 名字模糊匹配，也不再扫 `Projectile` 名字池兜底。火箭分裂弹数量固定 6 枚，继续走对象池与复用的 `raycastBuffer`；取消出生 `0.1s` 免碰撞窗口和 `SplitFuseTime` 定时爆炸，让分裂弹从第一帧开始使用同一套 `SphereCastNonAlloc` 检测敌人、墙和地面，自身不互相碰撞，命中后由原版 `Projectile.Update()` 根据 `context.explosionRange` 走 `ExplosionManager.CreateExplosion` 爆炸，未命中则按射程自然结束。重力分裂弹禁用追踪，二段初速从 `0.78x` 降到 `0.12x`，`SplitGravity` 提到 `24`，带重力 Radial 分裂固定使用世界水平圆环轴并加入轻微向下偏置，使其按水平 360 度径向初速 + 重力在首发弹四周下坠散开；非原版预设的二段爆炸才保留自定义 `OverlapSphereNonAlloc` 半径伤害，避免原版火箭双重伤害/双重特效。火焰爆炸 FX 改为尊重 profile 的 `ExplosionFxDuration`，火箭小爆炸按 `0.35s` 级别清理，避免固定残留 2 秒。
**兼容性影响**: 不涉及存档、配置、TypeID 或资源文件变更；仅调整焚天龙铳火箭弹运行时弹幕表现。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\DragonKingBossGunRocketSplitGuard.py` 通过
3. Guard: `python tests\DragonKingBossGunProfileCoverageGuard.py` 通过
4. Guard: `python tests\DragonKingBossGunAmmoSwitchGuard.py` 通过
5. Guard: `python tests\DragonKingBossGunReforgeBaselineGuard.py` 通过
6. Guard: `python tests\F3DragonKingBossGunDebugKitGuard.py` 通过
7. 格式: `git diff --check -- Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs tests/DragonKingBossGunRocketSplitGuard.py FIX_TRACKER.md` 通过
**未验证/需人工**: 需要进游戏用 TypeID `326` 火箭弹实测，确认日志出现 `按火箭弹 TypeID=326 缓存原版火箭弹预制体: BulletRocket`，主弹在鼠标瞄准点附近空爆、空爆后散出 6 枚子火箭、分裂弹不会因彼此重叠互相引爆、分裂弹可正常因命中敌人/墙/地面触发原版火箭爆炸且未命中时按射程自然结束。
---
### 2026-07-06 焚天龙铳弹种 baseline 与射击热路径修复

**状态**: fixed
**Finding**: CR-2026-07-05-001 / CR-2026-07-05-002 / CR-2026-07-05-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于已确认的焚天龙铳运行时稳定性修复
**现象**: 重铸过容量的焚天龙铳连续切换弹种时，弹匣 baseline 可能因取整反推被污染；场景切换清理会丢失手持枪的弹种 baseline；射击兜底每发重复写 Stat 并在 dev 模式刷覆盖日志；弹药 UI 选择仅靠 Caliber 兼容的 TypeID 时，本次换弹可能沿用上一弹种容量/换弹时间。
**根因**: 弹种 baseline 捕获优先从已套 profile 的当前 Stat 反推；`ClearSceneCaches()` 同时清掉枪实例弹种状态；`ShootOneBullet` 路径没有按枪实例跳过已应用的同 profile；`SetTargetBulletType(int)` / 空枪 TargetBulletID 兜底只查 `TryResolveTypeId`，没有回到真实弹药实例走 Caliber 解析。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 修改文件: `tests/DragonKingBossGunAmmoSwitchGuard.py`
- 修改文件: `tests/DragonKingBossGunReforgeBaselineGuard.py`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
**兼容性影响**: 不改 TypeID、存档 key、配置 schema、资源命名或掉落表；仅调整焚天龙铳运行时 Stat 覆盖缓存生命周期与重复写入判定。场景级弹幕/命中缓存仍在切图时清理，枪实例弹种 baseline 改为卸载/reset 时清理；targetTypeId 解析兜底只读取背包/枪内弹药实例用于 Caliber profile 同步。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
2. Guard: `python tests\\DragonKingBossGunProfileCoverageGuard.py` 通过
3. Guard: `python tests\\DragonKingBossGunAmmoSwitchGuard.py` 通过
4. Guard: `python tests\\DragonKingBossGunReforgeBaselineGuard.py` 通过
5. Guard: `python tests\\EventSubscriptionLifecycleGuard.py` 通过
6. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内验证重铸容量后连续切弹、切图后当前弹种属性、dev 模式高射速射击日志量，以及 UI 选择仅按 Caliber 兼容弹药后的首次换弹容量/时间。
**失败尝试**: 无

---
### 2026-07-04 Mode E/F 刷怪卡顿低风险优化

**状态**: fixed
**Finding**: 无（玩家反馈 + `docs/测试分析/2026-07-03_ModeE刷怪卡顿无行为优化审查.md` 静态审查建议）
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 用户要求按审查文档执行；当前工作区已补齐普通 Boss plan 化/隐藏物化、Mode E/F 共享 postprocess scheduler、提交屏障，以及三类自定义特殊 Boss 的 Mode E/F 显式 deferred activation。P0 现有 dev 日志仅覆盖 Mode E 开局，剩余三类场景仍需同机 profiler 复测。
**现象**: 玩家反馈 Mode E/F 刷怪时仍会“一卡一卡”，尤其单只 Boss 生成配置链和重刷道具连续生成时容易产生主观尖刺。
**根因**: 静态核对显示普通 Boss 的配装/倍率/激活/变异词条/掉落追踪与 Mode E/F 登记回调仍会压到相邻帧；BossRegen 词条开启时 Mode E/F 还会每帧重建存活 Boss 列表；挑衅烟雾弹首次使用时会同步解析原版烟雾 VFX；最近点选择仍对全量点排序。
**修复内容**:
- 新增文件: 无
- 修改文件: `ModBehaviour.cs`
- 修改文件: `Utilities/EnemySpawnCore.cs`
- 修改文件: `Utilities/ModeRuntimeHooks.cs`
- 修改文件: `ModeE/ModeE.cs`
- 修改文件: `ModeE/ModeEStartup.cs`
- 修改文件: `ModeE/ModeEIntegrityAndHelpers.cs`
- 修改文件: `ModeE/ModeERespawnItems.cs`
- 修改文件: `ModeE/ModeEBattle.cs`
- 修改文件: `ModeF/ModeFEntry.cs`
- 修改文件: `ModeF/ModeFPhases.cs`
- 修改文件: `ModeF/ModeFRespawn.cs`
 - 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
 - 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
 - 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
 - 修改文件: `tests/ModeEFSpawnPostprocessSchedulerGuard.py`
 - 修改文件: `tests/ManagedSpecialBossDeferredActivationGuard.py`
 - 修改文件: `tests/ModeESpawnFailureResolutionGuard.py`
**兼容性影响**: 不改 Boss 数量、阵营、刷怪点、安全距离、重刷 250ms 节奏、掉落/奖励、TypeID、配置或存档 schema。新增普通 Boss 激活前一帧屏障默认关闭，仅 Mode E/F 显式启用；不会出现已激活但未登记的跨帧窗口。BossRegen 仍在有非空缓存时每帧调用 `TickBossRegen` 推进内部 10 秒计时。
**验证方法**:
1. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_dev.bat"` 通过
2. 编译: `cmd.exe /c "set BOSSRUSH_NO_PAUSE=1 && compile_official.bat"` 通过
3. Guard: `python tests\\ModeEFSpawnPostprocessSchedulerGuard.py` 通过
4. Guard: `python tests\\ManagedSpecialBossDeferredActivationGuard.py` 通过
5. Guard: `python tests\\EnemySpawnCoreObservableGuard.py` 通过
6. Guard: `python tests\\ModeFRespawnObservableSpawnGuard.py` 通过
7. Guard: `python tests\\ModeEFSpawnParityGuard.py` 通过
8. Guard: `python tests\\ModeESpawnFailureResolutionGuard.py` 通过
9. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
10. 人工 smoke: 未运行
11. dev/profiler 日志: 2026-07-04 `latest.log` / `2026-07-04_11-20-22.log` 已覆盖 `PrepareModeEStartup`、`StartModeE` 与普通 Boss `ModeEFSpawnPostprocess`
**未验证/需人工**: 现有本机 dev 日志未覆盖 `ModeERespawn`（挑衅烟雾弹重刷）、`StartModeF`（Mode F 开局批量刷怪）、`ModeFRespawn`（Mode F 死亡补位）。已按文档实现并通过编译/guard，但仍需实机 profiler 与游戏内 smoke 才能断言问题已解决。
**失败尝试**: 无

---
### 2026-07-02 进存档时卡死在许愿台弹幕预热
**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要
**现象**: 进入基地存档后主线程卡死，最新日志停在 `WishFountain` 注册完 `OnBuildingBuilt` / `OnBuildingDestroyed` 事件后，不再继续打印“布满了灰尘的星愿许愿台建筑系统初始化完成”。
**根因**: `WishFountainView.CreateRuntime()` 在初始化阶段调用弹幕层预热，随后进入 `WishFountainDanmakuView.EnsurePoolCapacity()`。原实现用 `AcquireItem()` 在 `while (allocatedItemCount < targetCount)` 里反复从池中取出再放回；首个对象创建后，后续循环只会复用池中对象，不再增加 `allocatedItemCount`，导致循环永不退出，主线程卡在许愿台 View 运行时构建阶段。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、外部协议或反射目标变更；仅修正弹幕对象池预热的容量补足逻辑，保持既有 UI 行为与资源结构不变。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: 未运行（本次未触及相关 guard 断言结构）
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新加载问题存档，确认不再卡死，且许愿台面板首次打开时弹幕层仍能正常显示。
**失败尝试**: 无

## 修复记录

---
### 2026-07-02 BossRush 动态物品重启后显示白底问号

**状态**: fixed
**Finding**: 玩家反馈 / 鸭科夫源码核对
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 玩家反馈 Mod 启用/禁用后装备和道具会恢复可用，但完整重启游戏后又变成白底问号占位图标；前置存在且排序在本 Mod 前。
**根因**: 官方 `ItemAssetsCollection.GetMetaData/GetPrefab/InstantiateSync/InstantiateAsync` 在 BossRush 延迟内容 bootstrap 完成前被存档、仓库、商店或 UI 按 TypeID 调用时，动态 prefab 尚未注册；`InstantiateSync` 会创建 `FallbackItem_<id>`，`GetMetaData` 也会返回默认 metadata，最终表现为白底问号或不可用占位。
**修复内容**:
- 新增文件: `Integration/BossRushDynamicItemRegistry.cs`（已加入 `compile_official.bat`）
- 新增文件: `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`（已加入 `compile_official.bat`）
- 新增文件: `tests/BossRushDynamicItemRegistryGuard.py`
- 修改文件: `Integration/BossRushIntegration.cs`
- 修改文件: `Integration/EquipmentContentRegistry.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs`
- 修改文件: `Integration/Bonus/SetBonusPlaceholderRegistry.cs`
- 修改文件: `Integration/WikiBookItem.cs`
- 修改文件: `LootAndRewards/LootAndRewardsSpecialLoot.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs`
- 修改文件: `tests/DragonBossRewardContentPreloadGuard.py`
- 修改文件: `tests/PhantomWitchScytheRewardBundleGuard.py`
- 修改文件: `docs/contracts.md`
- 修改文件: `docs/架构说明/Harmony补丁契约稳定性.md`
- 修改文件: `docs/Bossrush使用物品ID表.md`
- 修改文件: `docs/制作教程/WikiBookUI_Guide.md`

**兼容性影响**: 不改 TypeID、存档 key、配置 schema 或资源命名；新增 Harmony prefix 覆盖官方按 TypeID 查询/实例化入口，按需精确加载已登记的 BossRush 资源。统一注册表优先复用现有 Config 常量和集中 TypeID 数组，避免多处重复维护 bundle/TypeID 映射；冒险家日志旧教程临时 ID `500100` 会在运行时收敛为发布 ID `500007`。属于向后兼容运行时兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\BossRushDynamicItemRegistryGuard.py` 通过
3. Guard: `python tests\DragonBossRewardContentPreloadGuard.py` 通过
4. Guard: `python tests\PhantomWitchScytheRewardBundleGuard.py` 通过
5. Guard: `python tests\ContentRegistryGuard.py` 通过
6. Guard: `python tests\DeferredIntegrationBootstrapGuard.py` 通过
7. Guard: `python tests\SetBonusLifecycleGuard.py` 通过
**未验证/需人工**: 需要游戏内用包含 BossRush 自定义物品/装备的存档完整重启后进入基地/仓库/背包，确认 `500001-500056` 内已发布物品不再显示白底问号，装备可正常实例化、装备和使用。
**失败尝试**: 无

---
### 2026-07-02 已建许愿台在旧存档进基地时可能不显示

**状态**: fixed
**Finding**: `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 基地加载时原版 `BuildingArea.Display` 在许愿台注入前先报 `No prefab for building starwish_fountain`；如果玩家存档里已经放过许愿台，该建筑可能不会在本次进档时被实例化出来，导致场景里不可见也无法交互。
**根因**: 许愿台建筑数据原本只在 deferred base-scene setup 阶段注入，时机晚于原版建筑区首轮显示；注入完成后又没有像婚礼教堂那样走一次基地建筑区重绘，因此“首轮缺 prefab”不会被补刷回来。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainBuilder.cs`
- 修改文件: `Integration/BossRushIntegration_StartAndScene.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、反射目标或资源命名；仅把许愿台建筑注入提前到基地场景早期，并复用基地建筑区重绘 helper 作为晚注入兜底。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py` 通过
3. Guard: `python tests\\SceneObjectTypeCacheGuard.py` 通过
4. Guard: `python tests\\StaticCacheLifecycleGuard.py` 通过
5. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏加载一个已经建过许愿台的存档，确认基地首帧后建筑可见、可交互，且不再出现同轮次的缺 prefab 回归。

---
### 2026-07-02 许愿台弹幕当前打开轮次不接最新数据

**状态**: fixed
**Finding**: CR-2026-07-02-001
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 许愿台面板先用本地缓存或内存缓存启动弹幕后，联网成功拿到的新弹幕数据不会作用到当前这次打开；玩家要关掉再打开一次，才会看到更新后的内容池。
**根因**: `WishFountainUI.RefreshDanmakuDisplay()` 为避免中途整层重置，在“已有可见弹幕”场景下直接跳过成功回调里的显示更新，导致当前轮次只更新缓存、不更新在播来源。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `Integration/WishFountain/WishFountainDanmakuView.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅把联网成功后的新数据无缝切入后续入场弹幕，保留已有对象池与滚动状态。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuJsonEscapeGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏确认已有缓存时打开许愿台，联网返回后不会整层闪烁，且后续入场弹幕会换成最新数据源。

---
### 2026-07-02 许愿台弹幕失败结果被短 TTL 缓存，重开面板也不会立刻重试

**状态**: fixed
**Finding**: CR-2026-07-02-002
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 只要飞书鉴权或弹幕列表请求瞬时失败一次，玩家在接下来约 45 秒里重复关闭再打开许愿台，也只会马上复用失败结果或旧缓存，不会重新联网拉取。
**根因**: `TryReturnRecentDanmakuResult()` 把“最近一次失败”也纳入和成功结果相同的 TTL 快取，导致 reopen 无法触发新的网络请求。
**修复内容**:
- 新增文件: 无
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainService.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、反射或外部协议；仅将 TTL 缓存限定为成功结果，失败后允许玩家重开面板立即重试。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏在弱网或临时断网后反复开关许愿台，确认恢复联网后无需等待 45 秒即可重新拉到弹幕。

---
### 2026-07-02 许愿台关闭后未解绑静态弹幕回调

**状态**: fixed
**Finding**: CR-2026-07-02-003
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: 在慢网、切场景或频繁开关许愿台时，旧面板实例会一直被静态拉取回调引用到请求结束；虽然版本号判断会把回调变成 no-op，但这些闭包和 View 引用会额外滞留一段时间。
**根因**: `WishFountainService.RequestRecentWishes()` 把 success/failure lambda 追加到静态 waiter 委托中，而 `WishFountainView.CancelDanmakuFetch()` 只递增本地版本号，没有显式从静态 waiter 中退订。
**修复内容**:
- 新增文件: `tests/WishDanmakuFetchLifecycleGuard.py`
- 修改文件: `Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修改文件: `Integration/WishFountain/WishFountainUI.cs`
- 修改文件: `CODE_REVIEW_FINDINGS.md`
- 修改文件: `FIX_TRACKER.md`

**兼容性影响**: 不涉及存档、配置、TypeID、Harmony/反射或资源结构；仅为弹幕拉取 waiter 增加显式解绑路径，降低旧 View 在慢网场景下的无效保留。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\WishDanmakuFetchLifecycleGuard.py` 通过
3. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏弱网下频繁打开/关闭许愿台并切图，确认不会报错、不会出现旧 UI 干扰下一次打开。

---
### 2026-07-01 “小明”非 Boss 预设误入 Boss 池

**状态**: fixed
**Finding**: 玩家反馈 / `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: `Player.log` 先只能看到鸭鸭市场通知队列输出 `<color=red>小明</color> 将在 ... 秒后抵达战场` 与 `第 6/7 波: <color=red>小明</color> ...`；打开开发日志后确认该官方角色预设 `nameKey` 为 `Character_Ming`。
**根因**: Boss 池动态扫描当前按 `CharacterRandomPreset.showName`、阵营和基础血量筛选敌对预设；“小明”属于有显示名且满足基础条件的非 Boss 角色，因此绕过了既有 `showName=false` 小怪清理规则。
**修复内容**:
- 新增文件: 无
- 修改文件: `WavesArena/WavesArena.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、Harmony/反射或资源文件变更；仅在 Boss 池初始化/缓存清理阶段按稳定预设名 `Character_Ming` 硬排除已知非 Boss 角色“小明”。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\ModeEFPrewarmCacheGuard.py` 通过
3. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
4. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新开一局 BossRush，确认 Boss 池配置窗口和波次预告不再出现“小明”。

---
### 2026-07-01 龙皇孩儿护我失效、龙裔同源复活风险与 Boss 名称兼容

**状态**: fixed  
**Finding**: 日志排查 / 玩家反馈  
**兼容分类**: COMPAT / WIRE+  
**版本/Commit**: 未提交  
**Owner decision**: 不需要  

**现象**: 玩家反馈更新后焚天龙皇“孩儿护我”不再触发；对照代码后确认龙裔遗族的一次性复活也存在同源风险。部分外部模组会把 BossRush 自定义 Boss 名称显示成 `Unknown`。最新 `Player.log` 里还能看到婚礼建筑初始化阶段触发的 `BuildingArea.RepaintAll()` 原版 `Debug.LogError`。  
**根因**:  
1. 原版 `Health.Hurt()` 在当前官源中的执行顺序为“扣血 -> 死亡分支 -> `OnDeadEvent` -> `SetActive(false)` -> `OnHurtEvent`”。龙皇孩儿护我和龙裔复活都挂在 `OnHurtEvent`，致死伤害会先把对象送入死亡路径，导致保命机制来不及触发。  
2. 三只自定义 Boss 的运行时 `CharacterRandomPreset.name` 使用内部 `*_Preset` 名称，且龙皇/幽灵女巫缺少 `Characters_` / 运行时 preset 名称别名，本地化兼容键不完整，外部模组若读取 `preset.name` 或兼容键，容易回退成 `Unknown`。  
3. 婚礼建筑系统初始化时无条件重绘基地建筑区，即使当前存档没有放置婚礼教堂，也会提前触发一次原版建筑重绘。  
**修复内容**:
- 新增文件: `Patches/Combat/BossLethalHealthProtectionPatch.cs`（已加入 `compile_official.bat`）
- 修改文件: `Integration/DragonKing/DragonKingAbilityController_AttackFlow.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantAbilities_ResurrectionAndPhase.cs`
- 修改文件: `Integration/DragonKing/DragonKingBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss.cs`
- 修改文件: `Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs`
- 修改文件: `Integration/PhantomWitch/PhantomWitchBoss.cs`
- 修改文件: `Localization/EquipmentLocalization.cs`
- 修改文件: `Integration/Wedding/WeddingBuildingInjector.cs`
- 修改文件: `compile_official.bat`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID 变更；新增一处针对 `Health.Hurt` / `Health.CurrentHealth` 的 Harmony 兼容补丁；运行时 Boss preset 的 `name` 统一改为稳定 `BossNameKey`，仅影响运行时识别兼容性。  
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat`
2. Guard: `python tests\\BossCleanupSharedHelperGuard.py`
3. Guard: `python tests\\DragonKingBossEventLifecycleGuard.py`
4. Guard: `python tests\\DragonKingChildProtectionTransformCacheGuard.py`
5. Guard: `python tests\\SceneObjectTypeCacheGuard.py`
6. Guard: `python tests\\DeferredIntegrationBootstrapGuard.py`
**未验证/需人工**:
- 游戏内实机确认龙皇“孩儿护我”可再次触发，且击杀其召出的龙裔后能正常联动处死龙皇。
- 游戏内实机确认龙裔遗族一次性复活恢复为 50% 血量后仍能完整进入狂暴二阶段。
- 使用玩家提到的外部模组实机确认三只自定义 Boss 不再显示为 `Unknown`。
- 若存档里已经放置婚礼教堂，需要在基地场景实机确认跳过无意义重绘后，旧存档中的教堂仍能正确显示。

---
### 2026-07-01 售货机 UI 崩溃（延迟注入导致 itemInstances 未缓存）

**状态**: fixed  
**Finding**: CR-2026-07-01-001  
**兼容分类**: WIRE- / OPERATIONAL  
**版本/Commit**: `64c4572`（旧记录）  
**Owner decision**: 不需要，属于 P0 回归修复  

**现象**: 进入基地打开售货机，UI 无法显示，`Player.log` 出现 `StockShopItemEntry.Setup()` 相关 `NullReferenceException`。

**根因**: 性能优化把商店条目注入改为延迟执行，晚于原生 `StockShop.Start()` 的 `CacheItemInstances()`。延迟注入条目的物品实例没有进入 `itemInstances` 缓存，后续 UI 取实例返回 null。

**修复内容**:

- 新增文件: `Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`（旧记录显示已加入 `compile_official.bat`）
- 修改文件: `compile_official.bat`

**修复原理**: 在 `StockShop.GetItemInstanceDirect(typeID)` 入口兜底缓存未命中的延迟注入条目，使补丁与注入时机解耦。

**兼容性影响**: 原生商品缓存命中路径不变；BossRush 延迟注入商品首次访问多一次同步实例化。

**验证方法**:

1. Windows 编译：`compile_official.bat`
2. 人工 smoke：基地打开售货机，确认 BossRush 注入商品显示且可购买。

**未验证/需人工**: 本次文档收敛未重新运行游戏，仅迁移旧 confirmed 记录。

**失败尝试**: 旧记录中的 `StockShop.Awake` 早期注入补丁受 `ModBehaviour.Instance` 初始化时序影响，不能稳定解决。

---
### 2026-08-07 Wiki 右书页文字超出页面

**状态**: fixed
**Finding**: 玩家截图反馈
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: Wiki 文章翻页后，右侧书页末尾文字继续绘制到书页及底部 UI 之外。

**根因**: 左页使用 TMP `Page` 模式生成分页，右页则把对应页的源字符串切出后重新补齐富文本标签、重新换行，并使用无边界 `Overflow` 模式。“末日丧尸模式”第 7/15 页恰好在密集表格、列表和标题之间分页，二次排版改变了换行边界，TMP 遂继续绘制到书页外。

**修复内容**:

- 修改 `Integration/WikiUIManager.cs`：删除右页源文本切片、富文本标签修复和二次排版路径；左右页共用完整文本与 TMP `Page` 分页，只切换奇偶 `pageToDisplay`。
- 新增 `tests/WikiUIPageOverflowGuard.py`：同时守卫左右页 `Page` 模式、共享完整文本及“禁止右页二次切片”约束。

**验证方法**:

1. Windows 编译：`cmd.exe /c compile_official.bat` 通过
2. Guard：`python tests\\WikiUIPageOverflowGuard.py` 通过
3. Guard：`python tests\\OfficialCompileListFileExistenceGuard.py` 通过

**未验证/需人工**: 需进游戏打开 Wiki 的“末日丧尸模式”文章，翻到原截图的第 7/15 页，确认右页文字不再越界且翻页内容连续。

## 变更日志

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-08-07 | 修复 Wiki 右书页文字越界 | 删除右页二次文本切片，左右页统一使用完整文本 + TMP `Page` 分页，并新增守卫。 |
| 2026-07-01 | AI 协作文档收敛 | 从旧 `docs/协作/FIX_TRACKER.md` 迁移 confirmed 修复记录；新增状态、owner decision、兼容分类字段。 |
