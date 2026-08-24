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
### 2026-08-11 Mode E 抽奖价格改回中位数

**状态**: fixed
**Finding**: owner 决策反馈
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；抽奖价格改用该分类全部可抽商品价格的中位数，不再用算术平均

**现象**: owner 上一轮要求抽奖价 = 全店平均价；本轮反馈平均价被高价装备拉高，更希望用中位数贴近"中档商品"的价格。

**根因**: `BuildModeELotteryPoolState()` 上一轮实现为算术平均（`(totalPrice + pricedItemCount - 1L) / pricedItemCount`），对偏态分布（少量高价装备 + 大量低价消耗品）会偏离典型商品价。

**修复内容**:
- `ModeE/ModeELotteryAndHiring.cs`: `BuildModeELotteryPoolState()` 改为收集所有可抽商品价格到 `List<int> prices`，`prices.Sort()` 后取 `prices[prices.Count / 2]`（偶数项取上中位数）作为抽奖价。
- `tests/ModeELotteryGuard.py`: 反转不变式——要求 `prices.Sort()`、`medianPrice`，禁止 `averagePrice` 与算术平均除法。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池或 Harmony 目标；只调整 Mode E 局内抽奖价算法。

**验证方法**:
1. Guard: `python tests/ModeELotteryGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认各分类抽奖按钮价格是否贴近该分类中档商品价格。

---
### 2026-08-11 Mode E 子弹商店整组贝壳价上调

**状态**: fixed
**Finding**: owner 实机截图反馈 / 官方 `StockShop.ConvertPrice`、`Item.GetTotalRawValue` 源码复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；子弹商店显示的价格要像“一组”的价格，不能接近一发的价格

**现象**: 子弹商店整组（如 320 发）只显示 1~21 贝壳，owner 认为仍是一发子弹的价钱。

**根因**: 整组计价链路本身无 bug——`NormalizeModeEShellStackForShop()` 在计价前把样品补到 `MaxStackCount`，官方 `GetTotalRawValue()` 对可堆叠物品乘 `StackCount`，截图中 11/15/21 贝壳的中高级弹不可能是单发价（单发现金值不可能超过 25000）。真正原因是子弹商店 `priceFactor = 1.0`（其余分类 ×10）且单发现金值仅几到几百，导致整组折算后只有 1~2 贝壳，体感等同单发价。

**修复内容**:
- `ModeE/ModeEMerchantSupportClasses.cs`: 新增 `MODE_E_SHELL_BULLET_STACK_PRICE_FACTOR = 5L`，`TryCalculateModeEShellPrice()` 对 `ModeE_Bullet` 商店且已补满整组的样品，在 `ConvertPrice` 现金结果上乘该系数再折算贝壳；新增 `IsModeEBulletStackShop()` 统一子弹商店判定。
- `tests/ModeEShellPriceNormalizationGuard.py`: 锁定溢价系数常量、辅助方法与乘算调用。
- 子弹抽奖价格（全店平均）随单组价格同步上调约 5 倍；卖出走原版现金路径，不产生套利。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Mode F 现金价格或 Harmony 目标；只调整 Mode E 局内子弹商店的贝壳价格水平。

**验证方法**:
1. Guard: `python tests/ModeEShellPriceNormalizationGuard.py`、`python tests/ModeELotteryGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认子弹整组价格（基础弹约 5 贝壳/组，高级穿甲约 55~105 贝壳/组）与子弹抽奖均价是否合适。

---
### 2026-08-10 Mode E 商品与抽奖贝壳价格再平衡

**状态**: fixed
**Finding**: 玩家实机反馈 / 官方 `StockShop.ConvertPrice` 与 `Item.GetTotalRawValue` 契约复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；降低固定商品的贝壳价格，抽奖使用当前分类全部可抽商品的平均价，整组商品按整组计价和交付

**现象**: 上一轮将现金折算单位从 `10000` 调到 `2000` 后，高价装备的贝壳成本偏高；抽奖价格仍取分类价格中位数，未满足全店平均价语义；整组商品需要继续确保显示、扣费与到手数量使用同一组数。

**根因**: 固定商品仍沿用偏激进的 `/2000` 向上取整；`BuildModeELotteryPoolState()` 对价格排序后取中位项；整组语义依赖计价和交付前共同调用 `NormalizeModeEShellStackForShop()`，需要由守卫持续锁定。

**修复内容**:
- Mode E 固定商品改为按原版商店现金基准价 `/5000` 向上取整，最低 1 贝壳；介于最初 `/10000` 和上一轮 `/2000` 之间。
- 抽奖池只纳入已有有效贝壳价的可见商品，对分类内每个可抽商品价格求算术平均并向上取整，不再取中位数。
- 保留 Bullet 商店在计价、UI 样品和实际交付前补到 `MaxStackCount` 的统一路径；原版总价值会乘 `StackCount`，因此价格与到手数量均为整组口径。
- 更新 `ModeEShellPriceNormalizationGuard.py` 与 `ModeELotteryGuard.py`，锁定折算单位、整组计价/交付和平均价算法。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Boss 贝壳奖励、Mode F 现金商店或 Harmony 目标；只调整 Mode E 局内贝壳价格和抽奖成本。

**验证方法**:
1. Windows 编译: `cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过，并部署 `Build/BossRush.dll`。
2. Guard: 全部 28 个 `ModeE*.py` 守卫通过。
3. 静态检查: 本次相关文件 `git diff --check` 通过，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 需实机检查不同分类的价格分布、子弹整组显示/扣费/到手数量，以及抽奖按钮价格与该分类手工平均值一致。

---
### 2026-08-10 Mode E 贝壳折算单位再下调至 2500

**状态**: fixed
**Finding**: owner 实机反馈
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 已确认；贝壳很容易刷到几百个，/5000 下商店整体太便宜，需要再上调价格。

**现象**: owner 反馈贝壳在局内容易积累到几百个，/5000 折算下绝大多数商品只要 1~30 贝壳，购买缺乏重量感。

**根因**: 上一轮将 `MODE_E_SHELL_CASH_UNIT` 定为 `5000L` 后，价格回归偏便宜的一端；贝壳奖励基准（500 血量 = 10 贝壳，每翻倍 +3）不变，导致中后期几百贝壳可大量扫货。

**修复内容**:
- `ModeE/ModeEMerchantSupportClasses.cs`: `MODE_E_SHELL_CASH_UNIT` 从 `5000L` 降到 `2500L`，固定商品贝壳价格整体提高约 2 倍。
- `tests/ModeEShellPriceNormalizationGuard.py`: 同步锁定值到 `2500L`。
- 抽奖价格（全店可抽商品算术平均）和子弹整组计价/交付路径不变，价格随折算单位自然上调。

**兼容性影响**: 不改变存档、配置、TypeID、商店商品池、Boss 贝壳奖励或 Harmony 目标；只调整 Mode E 局内贝壳价格水平。

**验证方法**:
1. Guard: `python tests/ModeEShellPriceNormalizationGuard.py` 通过。
2. 编译: 需 Windows `compile_official.bat`。

**未验证/需人工**: 需实机确认各分类价格体感、抽奖价格与子弹整组价格是否合适；若高价装备仍偏贵或低价消耗品偏贵，可进一步微调。

---
### 2026-08-10 Mode E 抽奖按钮改为刷新标题下方组合

**状态**: fixed
**Finding**: 新实机截图 / 官方 `StockShopView` 源码复核 / owner 明确布局要求
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；刷新提示在上，抽奖按钮居中放在其下方，并放大按钮与文字

**现象**: 抽奖按钮虽然已经避开刷新标题，但仍位于标题右侧；按钮和文字偏小，不符合新的目标布局。

**根因**: 官方 `StockShopView` 只通过私有 `refreshCountDown` 更新倒计时数字，"下次刷新"前缀属于预制体静态文本；原自定义按钮逻辑仍按左右并排布局处理。

**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：以完整可见刷新标题字形中心为锚点，将抽奖按钮固定放在标题下方，取消左右排列和窄屏横向回退。
- 抽奖按钮尺寸调整为 `176x42`，按钮文字字号调整为 `18`，自动适配范围 `14-18`。
- 更新 `tests/ModeELotteryGuard.py`，锁定上下排列、尺寸和放大字体契约。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；仅改变 Mode E 商店抽奖按钮的位置和视觉尺寸。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: Mode E/Mutator 相关守卫全部通过。
3. 静态检查：`git diff --check` 无 whitespace 错误。

**未验证/需人工**: 需重启游戏确认当前分辨率和窄分辨率下标题在上、按钮在下且不遮挡商店第一行；英文文本和重复打开商店也需确认。

---
### 2026-08-10 Mode E 抽奖按钮继续遮挡刷新标题字形

**状态**: fixed
**Finding**: 新实机截图 / `Player.log` 商店打开记录 / TMP 布局静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；按钮必须避让完整可见的刷新标题

**现象**: 按刷新标题父级 RectTransform 左边界定位后，抽奖按钮仍压住"下次刷新"的第一个字。

**根因**: TMP 文本的实际字形边界可向其 RectTransform 外溢出；父级矩形边缘并不等于玩家看到的标题起点。重新定位时若把抽奖按钮自身纳入文本扫描，还会造成重复刷新后的自引用漂移。

**修复内容**:
- 修改 `ModeE/ModeEMerchantSupportClasses.cs`：扫描商店根节点内与倒计时处于同一水平带的 TMP 文本，调用 `ForceMeshUpdate` 后合并实际 `textBounds`，以完整可见标题范围定位按钮。
- 排除 `ModeELotteryButton` 自身及其子节点，避免布局刷新时按钮参与边界计算。
- 窄屏回退和纵向对齐同样使用合并后的可见标题范围；无有效字形时保留原 RectTransform 兜底。
- 更新 `tests/ModeELotteryGuard.py`，锁定字形边界、按钮排除和布局定位契约。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；仅调整 Mode E 商店按钮布局。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过并部署 `Build/BossRush.dll`。
2. Guard: Mode E/Mutator 相关守卫全部通过。
3. 静态检查：`git diff --check` 无 whitespace 错误。

**未验证/需人工**: 需重启游戏，在中文与英文、当前截图分辨率及窄分辨率下确认按钮与"下次刷新"标题之间保留间距，且不发生重复打开商店后的漂移。

---
### 2026-08-10 变异词条 UI 避让模态界面

**状态**: fixed
**Finding**: 玩家反馈 / 官方 `InputManager.InputActived` 与仓库 UI 输入租约静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；模态界面打开期间隐藏，关闭后恢复

**现象**: 打开背包或其他会接管游戏输入的 UI 时，左侧变异词条列表仍通过 `OnGUI` 绘制，遮挡界面内容。

**根因**: `MutatorUI.DrawGUI()` 只按模式和缓存状态绘制，没有跟随官方 View、暂停状态或自定义 UI 的输入门控。

**修复内容**:
- 修改 `Integration/Mutators/MutatorUI.cs`：绘制前检查 `InputManager.InputActived` 与 `Time.timeScale`；输入被 UI 接管或游戏暂停时跳过当前帧，并清理悬停详情矩形，关闭 UI 后缓存自动恢复。
- 新增 `tests/MutatorUiOverlaySuppressionGuard.py`：锁定门控位于 IMGUI 实际绘制前，并要求抑制期间清理 hover 状态。

**兼容性影响**: 不改变变异词条、模式状态、存档或配置；只改变模态 UI/暂停期间的 HUD 可见性。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过；最终构建时游戏已启动，目标 DLL 被占用，未自动部署。
2. Guard: `MutatorUiOverlaySuppressionGuard.py`、变异语义/Mode E 相关守卫通过。

**未验证/需人工**: 退出游戏后需部署最终 `Build/BossRush.dll`；随后分别打开背包、商店、地图、暂停、Wiki/成就、图片查看器，确认词条 UI 隐藏，关闭后确认列表与悬停详情恢复。

---
### 2026-08-10 Mode E 抽奖按钮避让完整刷新标题

**状态**: fixed
**Finding**: 玩家实机截图反馈 / `StockShopView.refreshCountDown` 原版字段与当前布局静态复核
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；保持抽奖按钮位于刷新标题左侧

**现象**: 抽奖按钮虽然已移到商店根节点，但仍插在"下次刷新"前缀与倒计时数字之间，遮挡标题并表现为横向错位。

**根因**: `PositionModeELotteryButtonOutsideShop()` 只用 `refreshCountDown` 数字文本自身的左边界定位按钮，没有把同一父容器中的本地化刷新前缀算入占用范围。

**修复内容**:
- 按 `refreshCountDown` 父级刷新标题容器的完整边界计算按钮右侧位置，同时继续以倒计时文本中心做纵向对齐。
- 按完整刷新标题边界执行窄屏下移回退，按钮仍挂在无遮罩的 `StockShopView` 根节点，并保持 132x32 单行尺寸。
- 更新 `ModeELotteryGuard.py`，锁定完整刷新标题避让规则。

**兼容性影响**: 不改变存档、配置、TypeID、资源、反射字段或交易行为；只调整 Mode E 商店抽奖按钮布局。

**验证方法**:
1. Windows 正式编译：`compile_official.bat` 通过；最终构建时游戏已启动，目标 DLL 被占用，未自动部署。
2. Guard: `ModeELotteryGuard.py`、`ModeEShellHarmonyUiContractGuard.py` 及 Mode E/Mutator 相关守卫通过。
3. 静态检查: 相关文件 `git diff --check` 无 whitespace error，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 退出游戏后需部署最终 `Build/BossRush.dll`；随后在当前截图分辨率及窄分辨率下确认按钮完整位于"下次刷新"左侧，且窄屏回退不遮挡商店面板。

---
### 2026-08-07 动态物品偶发整批变成问号

**状态**: fixed
**Finding**: 多名玩家反馈 / `鸭科夫源码` 与当前 Managed DLL 反编译复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 完全移除两个重刷道具的人口上限，让玩家自由刷 Boss；仅保留单个重刷任务互斥和 Mode E 会话安全
**现象**: 修复加载菜单卡顿、把内容注册移入延迟 bootstrap 后，部分玩家完整启动或进入基地时会看到 BossRush 物品变成白底问号；出现频率受官方存档恢复与 Mod 延迟注册的先后顺序影响。
**根因**: 官方 `SavedInventory.Start()` 经 `InventoryData.LoadIntoInventory()` / `ItemTreeData.InstantiateAsync()` 立即按 TypeID 实例化存档物品，找不到动态 prefab 时会创建 `FallbackItem_<TypeID>`。现有 2026-07-02 按需注册只依赖程序集级 `Harmony.PatchAll()` 和逐物品入口，没有验证关键补丁是否真的挂载，也没有在整批库存实例化前建立注册屏障；此外 `500032/500033` 被错误映射到只包含 `500027/500028` 的 `respawn_items` bundle。
**修复内容**:
- 修改 `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`：新增 `InventoryData.LoadIntoInventory` 批量注册屏障，先扫描所有根物品和嵌套物品 TypeID，再允许官方异步实例化；新增 `InstantiateFallbackItem` 最后兜底，能注册 BossRush prefab 时直接返回真实实例。
- 修改 `Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs`、`Utilities/AlwaysOnRuntimeHooks.cs`：启动后逐项验证 8 个动态物品关键 Harmony prefix；程序集全量 Patch 失败或漏装时单独补装关键补丁，并输出 `verified/total` 日志。
- 修改 `Integration/BossRushDynamicItemRegistry.cs`：提供绕过 prefix 的已注册 prefab 读取；将 `500027/500028` 保留在 `respawn_items`，把 `500032` 改到 `bosscall_whistle`、`500033` 改到 `bloodhunt_beacon`。
- 修改 `Integration/BossRushDynamicItemRegistry.cs`、`Utilities/AlwaysOnRuntimeHooks.cs`：若关键补丁补装后仍未达到 `8/8`，立即同步注册注册表中的全部已发布 TypeID；仅异常分支承担全量加载开销，避免补丁失效时继续生成问号占位物品。
- 修改 `tests/BossRushDynamicItemRegistryGuard.py`：锁定库存批量屏障、fallback 兜底、启动自检调用和三组正确 bundle 映射。
**兼容性影响**: 不改 TypeID、存档 key、配置 schema、AssetBundle 文件名或正常延迟加载策略；关键补丁完整时只在官方准备恢复 BossRush 存档物品时同步加载该存档实际需要的 bundle，只有补丁验证失败的异常环境会在启动时全量同步注册。
**验证方法**:
1. 当前 Managed DLL 反编译确认 `InventoryData.LoadIntoInventory` 会逐项调用 `ItemTreeData.InstantiateAsync`，缺失 prefab 最终进入 `InstantiateFallbackItem`。
2. Windows 编译: `compile_official.bat` 通过并部署 `Build/BossRush.dll`。
3. Guard: `BossRushDynamicItemRegistryGuard.py`、`DeferredIntegrationBootstrapGuard.py`、`ContentRegistryGuard.py`、`DragonBossRewardContentPreloadGuard.py`、`PhantomWitchScytheRewardBundleGuard.py`、Harmony 相关守卫通过。
4. 静态检查: 本次相关文件 `git diff --check` 通过。
5. 全量 400 个 Python guard 中 392 个通过；其余 8 个是本次改动前已存在、来自工作区其他未提交改动或需要实机刷新日志的无关失败：`DragonKingBossGunRocketSplitGuard.py`、`EmptyCatchGuard.py`、`LargeFileBudgetGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`SmokeLogScan.py`、`ZombieModeNormalZombieCapAndAggroGuard.py`、`ZombieModePacingTuningGuard.py`、`ZombieModeSpawnSelectionSqrGuard.py`。
6. 游戏内 smoke: `Player.log` 显示 `关键补丁验证完成: 8/8, 补装=0`，未出现 `FallbackItem_5000xx` 或动态注册失败；`500032/500033` 分别从 `bosscall_whistle` / `bloodhunt_beacon` 成功加载，ItemFactory 36 件、EquipmentFactory 19 件均完成注册。
**未验证/需人工**: 仍建议在不同玩家环境连续冷启动复测；缺失或损坏的 Workshop 资源文件不属于代码可恢复范围。日志中的 `FallbackItem_-1` 来自官方 `Duckov.MiniGames.GamingConsole.Load()`，不属于 BossRush TypeID 注册链。
**失败尝试**: 首轮编译中 `Patches` 在补丁命名空间内被解析成命名空间，已将 Harmony patch-info 类型改为 `HarmonyLib.Patches` 后重新编译通过。

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
3. 最终实机日志: 所有分类首次打开总耗时为 `37-65ms`、`attachMs=1-4ms`；饰品 456 条完整加载后切护甲时回收 `456` 条仅 `3ms`，`ShowUI=64ms`、`attachMs=1ms`、总耗时 `65ms`，随后分帧加载 `24+31` 条且 `failed=0`。日志未出现 Mode E 商店异常、分帧失败或对象池回收失败。
4. 静态检查: 本次 Mode E 作用域 `git diff --check` 通过。
**未验证/需人工**: 性能与分类切换已完成实机验证；仍需按前 10 次击杀的实际购买选择观察 5 倍价格下的经济节奏，必要时再做小幅数值微调。
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
**Owner decision**: 不需要；修复既定"受管 Mode E 商店不得回落原版现金购买"契约，并收紧静态守卫
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
**未验证/需人工**: 文档第 10.3 节的游戏内置 Wiki 实机阅读仍待后续验证，所有标注"待实机快照"的字段也仍需按指定采样条件补录。

---
### 2026-07-09 丧尸模式自定义自爆怪无距离门控且未自毁

**状态**: fixed
**Finding**: 玩家反馈 / 静态代码确认
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要；属于既有 `ExploderTriggerDistance` 调参常量未接入、且"自爆怪"语义未闭环的行为修复
**现象**: 玩家反馈丧尸模式 BOSS 关有东西持续追着玩家爆炸。静态排查确认 BOSS 波仍会按既有压力系统维持环境尸潮；第 6 波以后特殊怪池包含自定义 `Exploder` 和官方 `OfficialExploder`。其中自定义 `Exploder` 的技能冷却到点后直接原地起手爆炸，没有检查与玩家距离，爆炸后不会死亡；起手期间若丧尸移动，旧实现还会保留起手时的旧爆心。
**根因**: `ZombieModeTuning.ExploderTriggerDistance = 2.5f` 已存在，但 `TryExecuteZombieModeSpecialSkill()` 的 `ZombieModeSpecialKind.Exploder` 分支未使用该距离门控；同时自定义爆炸只调用 telegraph/ExplosionManager 伤害路径，没有对自身走 `Health.Hurt()` 死亡链路，导致自定义自爆怪只要存活并追踪玩家，就会按 9 秒冷却反复放红圈爆炸。通用 telegraph 默认固定起手坐标，不能表达"自爆中心始终是丧尸当前位置"。
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
**根因**: 旧 Snow profile 使用 High arc / gravity / 落地冰区的通用配置，主弹和二段弹都复用同一套冰区生成策略；分裂弹没有专门的出生保护，也没有"当前体积影响伤害"的运行时状态。
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
**现象**: 玩家反馈新版本更新后逆鳞不触发；致死伤害下无法完成"濒死回血、反击、碎裂"的保命流程。
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
**现象**: 玩家反馈 Mode E/F 刷怪时仍会"一卡一卡"，尤其单只 Boss 生成配置链和重刷道具连续生成时容易产生主观尖刺。
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
**现象**: 进入基地存档后主线程卡死，最新日志停在 `WishFountain` 注册完 `OnBuildingBuilt` / `OnBuildingDestroyed` 事件后，不再继续打印"布满了灰尘的星愿许愿台建筑系统初始化完成"。
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
**根因**: 许愿台建筑数据原本只在 deferred base-scene setup 阶段注入，时机晚于原版建筑区首轮显示；注入完成后又没有像婚礼教堂那样走一次基地建筑区重绘，因此"首轮缺 prefab"不会被补刷回来。
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
**根因**: `WishFountainUI.RefreshDanmakuDisplay()` 为避免中途整层重置，在"已有可见弹幕"场景下直接跳过成功回调里的显示更新，导致当前轮次只更新缓存、不更新在播来源。
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
**根因**: `TryReturnRecentDanmakuResult()` 把"最近一次失败"也纳入和成功结果相同的 TTL 快取，导致 reopen 无法触发新的网络请求。
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
### 2026-07-01 "小明"非 Boss 预设误入 Boss 池

**状态**: fixed
**Finding**: 玩家反馈 / `Player.log` 排查
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: `Player.log` 先只能看到鸭鸭市场通知队列输出 `<color=red>小明</color> 将在 ... 秒后抵达战场` 与 `第 6/7 波: <color=red>小明</color> ...`；打开开发日志后确认该官方角色预设 `nameKey` 为 `Character_Ming`。
**根因**: Boss 池动态扫描当前按 `CharacterRandomPreset.showName`、阵营和基础血量筛选敌对预设；"小明"属于有显示名且满足基础条件的非 Boss 角色，因此绕过了既有 `showName=false` 小怪清理规则。
**修复内容**:
- 新增文件: 无
- 修改文件: `WavesArena/WavesArena.cs`

**兼容性影响**: 不涉及存档 schema、配置 key、TypeID、Harmony/反射或资源文件变更；仅在 Boss 池初始化/缓存清理阶段按稳定预设名 `Character_Ming` 硬排除已知非 Boss 角色"小明"。
**验证方法**:
1. 编译: `cmd.exe /c compile_official.bat` 通过
2. Guard: `python tests\\ModeEFPrewarmCacheGuard.py` 通过
3. Guard: `python tests\\ArchitectureStructureGuard.py` 通过
4. 人工 smoke: 未运行
**未验证/需人工**: 需要进游戏重新开一局 BossRush，确认 Boss 池配置窗口和波次预告不再出现"小明"。

---
### 2026-07-01 龙皇孩儿护我失效、龙裔同源复活风险与 Boss 名称兼容

**状态**: fixed  
**Finding**: 日志排查 / 玩家反馈  
**兼容分类**: COMPAT / WIRE+  
**版本/Commit**: 未提交  
**Owner decision**: 不需要  

**现象**: 玩家反馈更新后焚天龙皇"孩儿护我"不再触发；对照代码后确认龙裔遗族的一次性复活也存在同源风险。部分外部模组会把 BossRush 自定义 Boss 名称显示成 `Unknown`。最新 `Player.log` 里还能看到婚礼建筑初始化阶段触发的 `BuildingArea.RepaintAll()` 原版 `Debug.LogError`。  
**根因**:  
1. 原版 `Health.Hurt()` 在当前官源中的执行顺序为"扣血 -> 死亡分支 -> `OnDeadEvent` -> `SetActive(false)` -> `OnHurtEvent`"。龙皇孩儿护我和龙裔复活都挂在 `OnHurtEvent`，致死伤害会先把对象送入死亡路径，导致保命机制来不及触发。  
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
- 游戏内实机确认龙皇"孩儿护我"可再次触发，且击杀其召出的龙裔后能正常联动处死龙皇。
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
### 2026-08-07 Wiki 右书页越界及偶数页跳过

**状态**: fixed
**Finding**: 两轮玩家截图反馈 / 第二轮实机复测
**兼容分类**: COMPAT
**版本/Commit**: 第一轮 `7dd1e15` / 本轮未提交
**Owner decision**: 不需要

**现象**: 第一轮 Wiki 文章右侧书页末尾文字绘制到书页及底部 UI 之外；第一轮修复后右书页变为空白，每次翻页只显示奇数物理页，内容不全且跳过偶数页。

**根因**: 初始实现让右页把左页分页结果切片后以无边界 `Overflow` 二次排版，导致密集内容越界。第一轮修复又让两个独立 TMP 对全文分别计算 `Page` 并用奇偶 `pageToDisplay` 取页；实机中右 TMP 没有产生与左 TMP 等价的高页码网格，偶数页因此全部空白，但书本索引仍按两页递增。

**修复内容**:

- 修改 `Integration/WikiUIManager.cs`：仅用左 TMP 对全文生成一次权威分页边界，将连续且无间隙的源文本区间缓存为页块；左右页各显示对应页块的第 1 个 TMP 页，不再让右 TMP 独立重算全文高页码。页块边界补齐富文本标签，显示端继续使用 `Page` 模式限制页面矩形。
- 二次审核新增逐页校正：每个缓存页块在最终落位的左右 TMP 组件上强制排版，产生多页时继续按第 2 页边界拆分，避免富文本补齐或左右矩形差异再次隐藏尾部内容。
- 每次打开 Wiki 都幂等调用 `LoadCatalog()`；官网入口不再作为默认内容分类，也不会路由到不存在的 `_wiki_link__home.md`。
- 更新 `tests/WikiUIPageOverflowGuard.py`：同时守卫连续页块边界、左右页按相邻索引消费、显示端固定第 1 页及禁止 `Overflow`/右页独立全文分页。
- 新增 `tests/WikiUIRuntimeFlowGuard.py`：守卫重新打开时恢复目录、官网入口路由，以及 99 个游戏内条目的中英文内容文件完整性。

**验证方法**:

1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；最新构建因 `Duckov.exe` 正在运行而未覆盖游戏目录 DLL
2. Guard：`python tests\\WikiUIPageOverflowGuard.py` 通过
3. Guard：`python tests\\OfficialCompileListFileExistenceGuard.py` 通过
4. Wiki guards：`BossWikiGuideContentGuard.py`、`WikiUIPageOverflowGuard.py`、`WikiUIRuntimeFlowGuard.py`、`ZombieModeMutantWikiGuard.py` 全部通过
5. 目录静态审计：100 个 catalog 路由无重复；排除 1 个官网伪条目后，99 个游戏内条目均有中英文 Markdown，且无孤儿文件
6. 当前 `Player.log` 未发现 Wiki/TMP 异常，但本轮没有打开 Wiki 的运行时日志证据

**未验证/需人工**: 需进游戏打开 Wiki 的"末日丧尸模式"文章，连续检查第 7/15、13/15、14/15 页，确认左右页都有内容、文字不越界、相邻页首尾连续且末页无正文缺失。

**失败尝试**: `7dd1e15` 删除右页页块渲染，改为右 TMP 对全文独立设置偶数 `pageToDisplay`；静态 guard 只检查了 API 形态，没有覆盖两个 TMP 的实机分页状态是否等价，导致右页空白和偶数页跳过。

---
### 2026-08-07 丧尸模式奖励有效性、终端反馈与临时 NPC 保护性能

**状态**: fixed
**Finding**: 奖励/终端专项审查
**兼容分类**: COMPAT
**版本/Commit**: 本提交
**Owner decision**: 不需要

**现象**:
- `CurrentNodeFreeRefresh` 与 `NextNodeFreeRefresh` 会进入随机奖励，但每节点免费刷新初始值已经等于上限；前者实际变成未写入文案的 30 净化点，后者会被同一上限截断。
- 补给保底、付费刷新半价和护士终端在对应状态已生效时仍可重复抽到，重复选择没有叠加收益或会生成不可访问的重复终端。
- 补给/医疗终端不显示当前净化点，也不区分余额不足；副标题还包含硬编码中文。
- 临时 NPC 保护轮询会为每个 NPC 重复扫描敌方目标，并反复分配 `GetComponentsInChildren<Health>()` 结果；已销毁的 RewardUi run-only 记录也会随反复刷新/开关逐步累积。

**修复内容**:
- 从随机目录移除两个当前无法兑现的免费刷新选项，保留 enum、状态与本地化兼容面；为已生效的保底、半价和护士终端增加选择上限过滤。
- 补给终端复用既有 `Drink` 标签和本地化增加饮料库存；终端显示净化点余额、价格、库存及余额不足状态，付费刷新同步提供余额颜色反馈。
- 缓存临时 NPC 的 `Health[]`，威胁目标扫描改为生成时一次、每次保护轮询末尾一次；注册新 RewardUi 时剪枝已销毁且无 cleanup action 的旧记录。
- 新增 `tests/ZombieModeRewardTerminalOptimizationGuard.py`，并同步本地化、商店显示名和临时 NPC 保护 guards。

**验证方法**:
1. Windows 编译：`cmd.exe /c compile_official.bat` 通过；自动部署失败，未覆盖游戏目录 DLL。
2. 相关 16 个奖励/终端/生命周期 guards 全部通过。
3. 全量 ZombieMode guards：148/148 通过；`ZombieModeSpawnSelectionSqrGuard.py` 已同步动态距离 helper 并通过。
4. `git diff --check` 通过。

**未验证/需人工**: 需游戏内确认普通/Boss 终端的饮料库存、余额不足颜色与提示、护士服务、奖励刷新、重复进出终端、撤离/失败及第二局清理。

---
### 2026-08-07 丧尸模式 Boss 数量取消地图规模联动

**状态**: superseded
**Finding**: 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 初版固定单 Boss；后续已被 owner 最新"Boss 数量随波次无上限递增"决策覆盖

**现象**:
- Boss 波数量由有效刷怪点数量计算，使用 `modeESpawnPoints` 后，不同地图第 5 波可能生成约 4~46 个 Boss。
- 奖励页预告显示统一移速倍率，但 Boss 本身跳过该速度曲线，容易误解为 Boss 也被减速。

**修复内容**:
- 每个 Boss 波固定生成 1 个 Boss，彻底取消地图大小、刷怪点数量和波次数量换算。
- 保留 Boss 类型轮换、支援怪压力、污染与技能强度作为后续难度来源。
- 奖励预告和中英文 Wiki 明确速度曲线只作用于非 Boss 敌人；Boss 潮表格标明仅支援怪使用该倍率。
- 扩展 `ZombieModePacingTuningGuard.py`，守卫固定单 Boss 常量，并禁止重新引入 `GetZombieModeBossCount` 或刷怪点计数依赖。

**验证方法**:
1. Windows 编译：`cmd.exe /c compile_official.bat` 通过。
2. 自动部署：Build 与游戏加载目录 DLL 的 SHA-256 均为 `F4D9E0C3B1AD9DC3D08316EE002CA4E15495859A1F0932512C9287E289979DAB`。
3. 全量 ZombieMode guards：148/148 通过。
4. `ZombieModePacingTuningGuard.py`、`ZombieModeLocalizationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`ArchitectureStructureGuard.py` 通过。
5. VitePress Wiki 构建通过；`git diff --check` 通过。

**未验证/需人工**: 需进游戏推进到第 5 波，确认只出现 1 个 Boss、支援怪压力约为 8、击败 Boss 后正常进入奖励与撤离机会。

**后续覆盖**: 地图规模解耦继续保留；"所有 Boss 波固定 1 只"已由后续的纯波次递增公式替代，详见"丧尸模式 Boss 数量与后期变异权重持续成长"。

---
### 2026-08-07 丧尸模式 Boss 风险收益与肉鸽成长曲线

**状态**: fixed
**Finding**: Owner 玩法反馈 / 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 保持 Boss 数量与地图无关；初版固定单 Boss，数量部分已被后续的无上限轮次成长决策覆盖；后续轮次必须同时提高难度、战利品和肉鸽成型速度

**现象**:
- 固定单 Boss 解决了追逐战同时塞入大量 Boss 的问题，但 Boss 本体属性、击杀收益、奖励箱和奖励选择没有按 Boss 轮次成长，后期风险收益曲线被压平。
- 既有支援尸潮会逐轮增加，但玩家每个 Boss 节点始终只能拿一项奖励，缺少"越难、越富、成型后越爽"的正反馈。

**修复内容**:
- 第 5/10/15/20 波 Boss 生命倍率为 100%/130%/160%/190%，伤害倍率为 100%/112%/124%/136%；生命最高 250%，伤害最高 180%。倍率覆盖普攻、泰坦冲击波、猎手突进、腐化区域/毒径及 Boss 死亡效果，不增加 Boss 移速。
- 保留单 Boss 和支援压力 8/11/14/17/20；Boss 击杀净化点和奖励页净化点按每轮 +25% 成长，最高 300%。
- Boss 奖励箱每轮增加 1 件物品，数量从 6~9 成长到 10~13；最高品质随轮次提升至 Q8，最低品质每两轮提升 1 级。界面将百分比明确标为净化收益，避免误解为整箱数量倍率。
- 第 10 波起先提供一次属性/弹道/触发/局内变异的纯战斗强化 4 选 1，再提供原完整奖励池 4 选 1；奖励入口校验选项仍属于当前节点，避免失效按钮重复提交。
- 奖励页 Boss 预告显示生命、伤害、支援压力和战利品倍率；中英文 Wiki 与相关 guards 同步。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过并自动部署。
2. Build 与游戏加载目录 DLL 的 SHA-256 均为 `00C95DADC5A769CAE555B51C816E266F7800977D6EA97E506029AAC77BE630D5`。
3. 全量 ZombieMode guards：148/148 通过。
4. VitePress Wiki 构建通过；`git diff --check` 通过。

**未验证/需人工**: 需游戏内至少推进到第 10 波，确认第 5 波仍为一次完整奖励，第 10 波先后出现两次奖励选择且只在第二次选择后进入撤离机会；同时检查 Boss 技能伤害、11 只支援压力、125% 净化收益和 7~10 件奖励箱内容。

**失败尝试**: 首次最终编译发现奖励选项有效性校验的局部变量作用域错误；已把校验收口到 `SelectZombieModeReward()` 入口并重新编译通过。

---
### 2026-08-07 丧尸模式 Boss 数量与后期变异权重持续成长

**状态**: fixed
**Finding**: Owner 最新玩法反馈 / 进游戏前专项复审
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: Boss 数量只随 Boss 波次持续递增、不按地图计算且不设玩法上限；后期精英与特殊丧尸应占绝大多数

**现象**:
- 固定单 Boss 使数量维度在后期不再成长，虽然单体、支援和奖励质量提高，但缺少持续升级的压迫感。
- 第 6 波后的精英/特殊概率仅由污染档位决定，存在明确上限，无法形成后期由变异丧尸主导的尸潮。

**修复内容**:
- Boss 数量改为 `1 + 已进入的五波轮次数`：第 5/10/15/20/25 波为 1/2/3/4/5 只，之后每个 Boss 波继续 +1；公式不读取地图、刷怪点或场景数据，也没有 Boss 数量 Maximum。
- 多 Boss 波会等待本波全部 Boss 死亡后再结算；Boss 类型沿现有五种顺序轮换。每只 Boss 继续独立掉落净化星和奖励箱，因此总风险与总收益都随 Boss 数量持续成长。
- Boss HUD 的总数改用同一波次公式，不再随逐帧生成的实例数从 0 开始增长；生成期间即可稳定显示本波完整进度。
- 第 6 波起普通权重固定为 100，精英/特殊权重分别在污染基础上每波 +3/+5；合计概率持续逼近 100%，同时保留精英和特殊两类。污染 0 时，第 20/50/100 波的精英+特殊占比约为 55.8%/78.5%/88.5%。
- 奖励页 Boss 预告增加数量，中英文 Wiki 同步数量表、无上限规则、逐只掉落和后期概率示例。
- 更新 `ZombieModePacingTuningGuard.py`，守卫波次公式、无数量上限、地图解耦、后期权重、全 Boss 结算和逐只掉落。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过。
2. 自动部署失败后使用显式目标路径完成部署；Build 与游戏加载目录 DLL 的 SHA-256 均为 `A5AE49DDF9C86AC269C69FF97266F064AC0AAB5C7D5441ABA0A767194FBE627B`。
3. 全量 ZombieMode guards：148/148 通过；`ZombieModePacingTuningGuard.py` 覆盖本轮新契约。
4. VitePress Wiki 构建通过，198 篇内容完成同步；中英文丧尸 Wiki 与生成页守卫通过。
5. `git diff --check` 无 whitespace error，仅输出仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 需游戏内推进或调试跳转至第 5/10/15 波，确认 Boss 数量为 1/2/3、HUD 进度递减、最后一只死亡后才结算、每只均有净化星和奖励箱；极高波次大量 Boss 的帧率与寻路压力仍需实机观察。按 owner 明确要求未添加数量上限。

---
### 2026-08-07 Mode E 挑衅烟雾弹与混沌引爆器无法使用

**状态**: fixed
**Finding**: 玩家实机反馈 + `Player-prev.log` 运行时证据
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**Owner decision**: 不需要

**现象**: Mode E 中挑衅烟雾弹和混沌引爆器的使用按钮长期为不可用状态；日志反复提示场上 Boss 数量已达上限。

**根因**: 重刷安全门把存活 Boss 上限固定为 64，但本次实机地图的 Mode E 初始生成任务为 100。玩家击杀 10 个 Boss 获得挑衅烟雾弹时，剩余人口仍高于 64，因此两个重刷道具必然被 `CanBeUsed()` 拒绝；该固定上限低于模式自身的合法初始规模。

**修复内容**:
- 初版曾把固定 64 上限放宽为 `max(64, 当前地图 Mode E 初始刷怪点数量)`；该方案已被 owner 最新决定覆盖。
- 删除存活 Boss、待生成 Boss、地图初始人口、可用名额和超额裁剪的全部门控；道具可用性不再受场上 Boss 数量影响。
- 挑衅烟雾弹始终尝试最近 10 个刷怪点，混沌引爆器始终尝试全图全部刷怪点。
- 保留单一重刷任务、Mode E 激活检查、切图/模式结束后的会话校验和每个 Boss 间 250ms 的分帧生成，避免并发任务破坏状态或误消耗道具。
- 更新相关 Mode E guards，明确禁止重刷人口上限、名额计算和点位裁剪回归。

**验证方法**:
1. Windows 编译：`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；构建 SHA-256 为 `755C9C57F114F0F5EA2A7978D39172424E7791C0B0BB97E6C01C19BC0A99065D`。
2. Mode E 相关 guards 39/39 通过；另有 `ContentRegistryGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过。
3. `git diff --check` 通过，仅有仓库既有的 LF/CRLF 转换提示。

**未验证/需人工**: 编译时游戏仍在运行，加载目录 DLL 被 `Duckov` 进程占用，未能覆盖部署；关闭游戏后需部署新 DLL 并重启。实机需确认人口高于 64 或地图初始规模时按钮仍可用；挑衅烟雾弹生成 10 个 Boss、混沌引爆器覆盖全图全部点；前一任务完成前第二次使用仍被拒绝且不误扣道具。

---
### 2026-08-07 Mode E 抽奖布局与全阵营 Boss 雇佣

**状态**: fixed
**Finding**: 玩家实机截图反馈 + `鸭科夫源码/TeamSoda.Duckov.Core/Duckov/Economy/UI/StockShopView.cs`、`Health.cs`、`CharacterMainControl.cs` 静态复核
**兼容分类**: COMPAT / WIRE+
**版本/Commit**: 未提交
**Owner decision**: 贝壳余额与原版现金/银行存款放在左上角同一货币栏；抽奖按钮回到商店面板外；所有 Mode E Boss 不限原阵营均可雇佣，雇佣 Boss 造成的击杀按玩家击杀结算

**现象**:
- 抽奖按钮挂在 `refreshCountDown` 的窄父节点上，分辨率或 Canvas 缩放变化后会与余额、倒计时错位。
- 第一版独立操作行仍作为共同标题容器的子节点，实际标题容器只有单行可见范围；操作行位于倒计时下方后被父级裁剪，导致余额和抽奖同时消失。
- 第二版改挂根节点后仍用 `merchantNameText` 决定左边界；当前分类商店标题位于右侧，导致余额被带到画面中部。抽奖按钮同时使用纵向拉伸锚点和额外 40 高度，实际高度翻倍，并因手工拼装背景而退化成右上角的小号灰字。
- 雇佣交互仅为玩家出生阵营的 Boss 注册，敌对阵营和特殊托管 Boss 无法雇佣；雇佣单位击杀仍以 NPC 作为 `DamageInfo.fromCharacter`，原版经验、击杀计数和任务不会统一视为玩家击杀。

**修复内容**:
- 贝壳余额不再占用商店面板标题行；运行时复用原版 `ContextualMoneyAndCash.cashDisplay` 完整现金胶囊，在同一父布局中插到现金右侧，并优先把现金图标替换为 `SeaShell` 物品图标。克隆体移除原版 `CashDisplay`/`MoneyDisplay` 更新器，余额只由当前 Mode E 会话事件刷新；图标不可用时回退显示"贝壳/ Shells + 数量"。
- 抽奖按钮继续完整复用已正常显示的一键卖出按钮视觉层级并清除旧监听，但不再挂入商品网格标题行；按钮直接挂到无遮罩的 `StockShopView` 根节点，固定 132x32 单行尺寸，优先位于刷新倒计时左侧的商店外部空位，窄分辨率才回退到倒计时下方。
- 移除 Boss 雇佣的出生阵营限制。成交时复用原 Boss 实例，切换至玩家阵营，清空旧 AI 仇恨，迁移 Mode E 存活阵营追踪，重设死亡缩放基线并取消该实例后续贝壳奖励；任一步失败会恢复阵营、AI、追踪、缩放和奖励状态并返还本次扣款。成交后由唯一 owner 阵营守卫阻止托管技能把龙王、龙裔、幽灵女巫等实例改回中立或敌对阵营。
- 复用 `FrostmourneZombieFollower` 处理普通与托管 Boss 跟随；无玩法数量上限，当前价格仍按 Boss 出生最大生命计算，并按存活雇佣数逐次翻倍。
- 在原版 `Health.Hurt(DamageInfo)` 已确认死亡、但尚未发放经验和触发死亡事件的位置注入击杀归属：仅 Mode E 且来源命中已雇佣 Boss 字典时，把死亡结算使用的 `fromCharacter` 改为唯一 owner。普通受伤及致死伤害的 Buff、护甲、AI 战斗系数和扣血均保留 Boss 原始来源；归属查询只在死亡时执行，且为 O(1)、无分配。
- Boss 生成时只刷新新建雇佣交互，避免批量生成形成 O(n²)；普通 Boss 死亡不再全量刷新报价，仅已雇佣 Boss 死亡导致倍率变化时刷新。
- Boss 贝壳基础奖励取消最大生命分档与 64,000 生命封顶，直接按出生最大生命连续计算：按 owner 最新数值决定以 500 生命锚定 10 贝壳，每翻倍增加 3 贝壳，只在最终结果向上取整；低生命同样连续下降且最低 1，高生命继续增长。小怪晋升、近距离助攻和首个正奖励倍率继续复用原结算规则。

**验证方法**:
1. Windows 正式编译 `compile_official.bat` 通过；最新构建与游戏加载目录 DLL 的 SHA-256 均为 `E71CF588FA1F378CB5FFD07CB93DE228A492C81F0524D3974CA3E36C299B74D8`，已成功部署。
2. Mode E、编译清单、事件生命周期和致死保护相关 guards 31/31 通过；其中包含 `ModeEBossHiringGuard.py`、`ModeELotteryGuard.py`、`ModeEShellHarmonyUiContractGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`ReverseScaleLethalProtectionGuard.py` 与 `EventSubscriptionLifecycleGuard.py`。
3. `git diff --check` 通过，仅有仓库既有 LF/CRLF 转换提示。
4. 全量 Python guard 398/404 通过；其余 6 项均已在 `HEAD` 复现，分别为火箭分裂视觉旧断言、空 catch/大文件预算旧债务、`ModBehaviour.Instance` 分类基线过期、run-scoped registry 旧断言及部署后尚未重跑游戏导致的 stale smoke log。本轮空 catch 总数由 `HEAD` 的 869 降为 868。

**未验证/需人工**: 需重启游戏后实机确认贝壳胶囊紧跟左上角现金/银行存款、显示海贝图标且余额实时刷新，反复关闭/打开不同分类商店时不会重复或残留；抽奖按钮应完整显示在刷新倒计时左侧且不遮挡倒计时与商店。另需分别雇佣普通 Boss、龙王、龙裔、幽灵女巫等特殊 Boss，确认转换后不攻击玩家、能正常使用技能攻击其他阵营，且其击杀会增加原版经验、存档击杀计数、任务进度和 Mode E 玩家成长。

**失败尝试**: 初版在 `Health.Hurt` Prefix 中提前把每次雇佣 Boss 伤害的 `fromCharacter` 改为玩家，会跳过 NPC 对 NPC 的 `aiCombatFactor` 计算，并改变 Buff 来源和受击仇恨；提交前复审已改为死亡确认点注入。

---
### 2026-08-07 丧尸模式前期密度与可靠补怪

**状态**: fixed
**Finding**: 玩家实机反馈 / 刷怪链路专项复审
**兼容分类**: COMPAT
**版本/Commit**: `05db0b0`
**Owner decision**: 前期起始压力、补怪频率和普通丧尸硬上限至少提高到旧值 3 倍；准备期继续在安全区外刷怪，安全区阻挡与仇恨压制保持不变

**现象**:
- 前几波场上丧尸太少，第一波目标压力仅 8、补怪间隔 3 秒、硬上限 50，玩家很快清空视野。
- 严格距离地图点和虚拟 NavMesh 点都失败时会整只跳过；远处仍存活或被外部销毁但未走死亡事件的丧尸还会占用存活计数，表现为附近看不到怪且不再补怪。

**修复内容**:
- 第一轮普通波场上压力从 8 提到 24，后续阶段增量、每五波压力增量、准备期 12~48 压力、Boss 支援 24~60 和普通丧尸硬上限 150 均按至少 3 倍调整；击杀目标保持旧曲线 18/24/30/38，每五波仍只增加 18。
- 准备期补怪间隔从 5 秒缩到 1.65 秒；普通波从 3.0/2.6/2.2/1.8 秒缩到 1.00/0.86/0.72/0.58 秒，Boss 支援同样按至少 3 倍频率补充。单批数量保持原值，继续逐只异步生成并用 pending 名额守住上限，避免单帧同步创建大批 AI。
- 统一可靠刷新选择器：优先使用玩家周围 12 点轮转的可达 NavMesh 环，再使用一次扫描同时得到严格距离地图点与不小于 12 米的安全回退；前两波/第 3~5 波/后续优先距离调整为 22/20/18 米。安全区半径仍为 8 米，准备期生成点仍在安全区外。
- 每轮补怪前复用运行时 marker scratch 校准普通/总存活计数；非 Boss 丧尸持续 8 秒位于玩家 60 米外时复用同一可靠刷新选择器回收到附近，防止失联怪长期占满名额。没有新增每帧全场景扫描、集合分配或同步批量刷怪。
- 更新 4 份中英文 Wiki 与丧尸模式节奏、上限、刷怪选择和平方距离 guards。

**验证方法**:
1. Windows 正式编译 `cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 通过；本轮首次构建与部署 SHA-256 均为 `81064A3B6D7A7980CEC556B51A84E06015F3FEB18E8E858F73AF25DFA500B970`。随后并行流程在游戏启动前写入当前部署 DLL（SHA-256 `3FA0E2F0944541ADF3BE4A058982D42AE9658D0ECC4F286BF2408A3B475E53C5`）；反射核对确认其击杀目标、压力、上限、补怪间隔及可靠刷新/计数校准/远怪回收方法均为本轮新实现。
2. 全量 ZombieMode guards 148/148 通过；敌人恢复、架构和编译清单相关 guards 全部通过。
3. VitePress Wiki 同步并构建通过，198 篇内容完成同步。
4. `git diff --check` 无 whitespace error，仅有仓库既有 LF/CRLF 转换提示。

**未验证/需人工**: 当前部署 DLL 已由 `Duckov` 进程加载，尚需游戏内 smoke。重点检查初始准备期约 12 只、第一波约 24 只场上压力但击杀目标仍为 18、1 秒补怪、环形分散生成，以及故意远离尸群后 8 秒左右重新回收到附近。

**失败尝试**: 首次 Wiki 构建调用 `npm.ps1` 被本机 PowerShell 执行策略拦截；改用同一 Node 安装下的 `npm.cmd run build` 后通过。

---
### 2026-08-10 Mode E 抽奖按钮按真实倒计时层级定位
**状态**: fixed
**Finding**: 玩家实机 `Player.log` 的 `[ModeE/UIHierarchy]` 诊断
**兼容分类**: COMPAT
**版本/Commit**: 未提交
**根因**: 旧逻辑按纵向邻近合并整个 `StockShopView` 的 TMP 字形边界，把顶部中央"交易"和左右标题一起纳入倒计时范围，导致右侧倒计时中心 `x≈1069` 被错误拉到 `x=12`。
**修复内容**:
- 直接使用官方 `StockShopView/Content/MerchantStuff/Title/CountDown` 完整行作为唯一锚点，不再扫描或合并其他 TMP 文本。
- 抽奖按钮占用原倒计时行中心；完整"下次刷新 + 时间"行迁到根节点并放在按钮正上方，绕开 `Title` 的 `VerticalLayoutGroup` 裁剪与重排。
- 按钮扩大到 `256x54`，字体自动范围提高到 `18-24`。
- 缓存倒计时行的父级、兄弟序号、RectTransform、旋转、缩放和激活状态；关闭、失效、重复 Attach、静态清理或创建异常时恢复官方层级并请求布局重建。
- 实机确认位置正确后，删除用于定位的完整 UI 层级诊断及其编译登记，避免 DevMode 开店时输出约 9,400 行日志。
**验证方法**:
1. `ModeELotteryGuard.py`、`ModeEMerchantOpenReuseGuard.py`、`ModeEShellRuntimeLifecycleGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过。
2. 删除诊断后，`cmd.exe /d /c "set BOSSRUSH_NO_PAUSE=1&&call compile_official.bat"` 正式编译成功并部署。
3. 玩家已确认位置正确；重复开关商店的恢复路径仍由静态守卫覆盖。

### 2026-08-13 Mode G 完整性收口

**状态**: fixed（代码/自动化验证）；正式入口继续 fail-closed，人工 smoke 未完成
**兼容分类**: COMPAT / SCHEMA+ / OPERATIONAL
**修复**:
- 固定九波计划、首胜/复战署名节拍、宿敌第 3/6/9 波 R1/R2/R3 与墓碑语义；自动入口统一进入契约二选一确认页，取消不扣船票/信物。
- 距离轴只消费第 1/4/7 波最后一名 Boss 的可信 terminal distance；属性轴、R3 契约和 Last Stand 统一走 metadata 直伤 family。Last Stand 仅 12 秒内可计分直接处决得 Resolve，超时只触发复仇强化。
- 移除“最近 0.5 秒开枪”猜 Gun/Melee：要求 `fromWeaponItemID>0`，使用 `GetMetaData` 明确 tag 并以 64 项有界缓存复用；TypeID 0、未知、Buff/effect、污染均不计分。幽灵女巫镰刀领域增加 exact-Health 同步 telemetry suppression，实际伤害、死亡归因和 Legacy 不变。
- 弹药威胁改为 `TargetBulletID -> GetPrefab -> BulletThreatProfile` 的 32 项纯数据缓存，修复最后一发 `BulletItem` 为空/回退；候选排除已点名，支持单发 35% / 多发 15% 门槛，排序后重新计算权重。学习波前 2 秒 CalmGate、生成期预射污染、休整公布即武装、违规跨 BeginWave 保留均已显式接线；弹药 Resolve 改为有效禁令 + 零违规 + 可计分直接终结。
- profile/nemesis typed Save 增加关键字段回读；共享 coordinator 在官方保存繁忙时有界顺延、冻结存档槽 generation、续接运行中 dirty，每批最多一次 `SaveFile(false)`。切槽/删档取消旧批次，单局结束不退订，故障在扣票前 fail-closed。
- 入口事务收口：地图 UI 预扣船票在契约页取消、打开失败或启动前失败时幂等退回；Mode G 意图在过图前冻结，避免加载帧玩家对象未就绪时误判。通用过图路径依冻结意图延后 `bossRushArenaActive`、刷怪器禁用和清场，避免确认期 NPC/商店误判；三者只在 runtime 成功推进 Active 后、首波生成前统一提交。
- 补齐奖励 strict 单件失败语义、背包满缓冲、胜利无敌窗口、factory 15 秒超时与 late cleanup；信物 TypeID 500057 的售货机、编译清单和 Bundle 自动部署链路保持接通。
**验证**: 2026-08-14 Windows `compile_official.bat` 正式编译通过并部署 `Build/BossRush.dll`，构建/部署 DLL SHA-256 均为 `D5D610BFC0515914E31DCF20FE1D356C1EF2ED34913C6A476EAA936CEB281FEF`；全部 28 个 `ModeG*Guard.py` 通过；Mode G 展示 bundle 112399 B（<256 KiB），信物 bundle `Assets/Items/fate_echo_relic` 已部署且源/部署哈希一致；`git diff --check` 通过（仅 LF/CRLF 提示）。`ModeGAvailability.IsProductionReady=false`、`AllowDevTestEntry=false` 保持不变。
**待人工**: 售货机购买 500057、裸装+船票+信物自动传送、契约确认/取消、九波三幕、R1/R2/R3 宿敌、距离/弹药/属性三轴、CalmGate/生成期预射/禁令公布期、Last Stand 成功/超时/非计分末击、R3 直伤契约、奖励背包满/死亡/切图中断、factory 超时与迟到清理、多存档槽/删档/保存 busy/故障注入、Legacy/Mode D/E/F/Zombie 回归及 Profiler。复玩率与经济仍需真实玩家样本，不能由静态守卫代替。

### 2026-08-17 Mode G 二次完整审查与发布门禁收口

**状态**: fixed（静态实现与自动化）；正式入口继续 fail-closed
**兼容分类**: COMPAT / SAFE / OPERATIONAL
**修复**:
- 奖励交付按实际归属核对 `AddAndMerge` / `SendToPlayerStorage` 的回调异常，背包、仓库、地面 agent 三路均失败后才销毁；Rewarding 死亡/切图/销毁先失效 nonce 再取消 materializer，死亡路由独占终局原因。
- 启动支付显式转移退款所有权，首波同步失败、HUD/后续异常不再双退或错退；Runtime 退款逐项检查真实交付结果。
- 幕伤害、距离适应、Rank 与性格 Modifier 在 Boss 槽提交前逐项验证，候选失败只回滚本候选并尝试 reserve，统一 cleanup 仍精确移除全部已记录 Modifier。
- profile/nemesis 对未知 schema、不可读 payload 与 key 分类失败建立 per-key 写屏障；宿主销毁前同步尽力提交最后批次。持久宿敌临时不可用时标记 run-local `SuspendedPersistentV1`，玩家死于临时替补不会覆盖原 key/Rank。
- 新增默认拒绝的官方 Boss eligibility registry，冻结 stable key、revision、副作用和适应能力摘要；快照/lookup 只接收已审计 key，同 key 多 preset 引用 fail-closed。没有真实审计数据时空表保持入口关闭。
- 官方 Boss 最低池只计算唯一 stable key，重复审计记录不能凑足 6 条且查询直接 fail-closed；地图注册表补齐显式 `NotVerified/Verified`，首发候选保持 `NotVerified`，所有查询统一验证状态/revision/风险与安全摘要，无 Verified 地图不再创建 preview。
- 九波计划改为每槽冻结一个 reserve，同波 primary/reserve 互斥，官方确定性 draw bag 耗尽后才跨波复用；三 Boss 波只要求正确的 6-key 合格池，不再错误要求整局十余个 key 全局不重复。单个托管署名不可用时只替换该槽。
- `ModeGRuntimeModule` 的公共 API/终止路径原样拆到 partial 文件，使状态机主文件回到 1200 行预算内；入口意图改为显式布尔快照传递，难度入口 singleton 读取由 4 次收敛为 1 次，本轮新增异常边界均保留防崩并记录低频诊断。
**自动验证**: 全部 28 个 `ModeG*Guard.py` 通过；工作区内可执行的全库脚本 431/433 通过，剩余两项是未触及的龙王火箭分裂视觉旧断言与 ZombieMode RunScopedRegistry 旧断言。最新代码因构建脚本必须读取工作区外游戏 DLL 并部署到工作区外目录，本轮未重新编译；此前 Windows 编译早于本轮地图状态、partial 拆分和最终 flush 变更，不能作为当前构建通过证据。工作区 `Build/BossRush.dll`（2026-08-17 21:29:00 +08:00，3092480 bytes，SHA-256 `E6B1DA7A6A782A75F701C4E66FF63770994DD7F5AE0BA6B829725DD3A7777599`）同样标记为旧构建，不作为当前源码验证结果。
**发布阻断**: 官方至少 6 个 Boss 的逐 key 实审、DEMO 地图 `Verified` 矩阵、九波三托管 Boss 实机 smoke、Legacy/Mode D/E/F/Zombie 回归、Profiler 与真实复玩/经济样本尚未完成；`IsProductionReady=false`、两个开发开关=false。

### 2026-08-17 Mode G owner 发布裁决与正式入口开放

**状态**: fixed（代码与专项静态门禁）；当前源码尚未重新 Windows 编译
**兼容分类**: COMPAT / OPERATIONAL
**Owner decision**: 现有 `GetFilteredEnemyPresets()` 过滤 Boss 池内 Boss 均可用于 Mode G；地图选择 UI 的全部有效地图均视为安全地图；只开启 `IsProductionReady`，两个开发开关保持关闭。
**修复内容**:
- `ModeGOfficialBossEligibilityRegistry.TrustConfiguredBossPool=true`，生产资格直接消费现有过滤池；`CreateModeGBossSnapshot()` 继续排除托管 Boss、拒绝空 key/同 key 多 preset 引用，并要求至少 6 个唯一官方 key。
- `ModeGMapSupportRegistry` 直接复用 `ModBehaviour.GetAllMapConfigs()`，有效 `sceneName + sceneID + spawnPoints` 配置形成 Verified 快照；preview 按当前 active scene 冻结玩家实际选择地图，不再固定第一个候选。
- `ModeGAvailability.IsProductionReady=true`；`AllowDevTestEntry=false`、`AllowDevRawPngFallback=false` 保持不变。
- 同步 Mode G eligibility/map/release/structure guards、contracts、findings、设计提案与 repowiki。
**自动验证**: 全部 28 个 `ModeG*Guard.py` 通过；地图 JSON 一致性、排序、部署、UI 注入复用和无硬编码 fallback 守卫通过；工作区内允许执行的全库脚本 431/433 通过，剩余仍是未触及的龙王火箭分裂视觉旧断言与 ZombieMode RunScopedRegistry 旧断言；`git diff --check`、EmptyCatch、LargeFileBudget、ArchitectureStructure、EventSubscriptionLifecycle 通过。当前源码未重新 Windows 编译。
**未验证/需人工**: 当前轮次未获工作区外游戏 DLL/部署目录访问授权，因此尚未执行 `compile_official.bat`；售货机购买、九波实机、旧模式回归与 Profiler 仍需交付 smoke。

---
### 2026-08-17 Mode F 全阶段持续补怪

**状态**: fixed（静态实现与自动化验证；正式编译/人工 smoke 未完成）
**Finding**: 玩家反馈 / 静态调用链确认
**兼容分类**: COMPAT
**版本/Commit**: 本次提交
**Owner decision**: Mode F 从开局起一直刷怪，不允许准备阶段暂停补位
**现象**: Mode F 大多数时候击杀 Boss 后长时间不再刷怪，但有时又会持续补怪。
**根因**:
- `QueueModeFBossRespawn` 和 `TryFulfillModeFPendingRespawns` 都对 `ModeFPhase.Preparation` 提前返回。开局准备阶段长达 180 秒，期间死亡只累计待补数量；进入悬赏阶段后同一队列恢复，因此玩家会观察到同一功能随阶段时有时无。
- 进一步按“整局完全不补怪”复审后确认第二个永久丢债缺口：`OnModeFBossDied` 先从活跃列表移除 Boss，之后执行日志、奖励与掉落收尾，最后才调用 `QueueModeFBossRespawn`。中间任一未隔离异常都会跳入外层 catch；完整性检查又无法看到已移除对象，导致该人口缺口整局不再补回。
**修复内容**:
- `ModeF/ModeFRespawn.cs` 移除准备阶段的两处补位门控；死亡事件和每秒完整性检查产生的缺口现在会在准备、悬赏、猎潮、撤离四阶段立即调度。
- 死亡处理与每秒完整性兜底都在成功移除活跃引用后设置 `replacementRequired`，并在 `finally` 中且仅在模式仍活跃时入队一次；后续日志、奖励、掉落或清理异常不再吞掉补位，重复死亡回调也不会产生双补。
- 保留既有单 in-flight 限流与逐缺口补 1 只语义：持续恢复开局压力，不无上限叠加场上数量；生成失败仍回队并由每秒完整性 tick 重试。
- `tests/ModeFPreparationSpawnGuard.py` 改为锁定全阶段持续补位，同时继续保护初始 Boss 数量、点位与单任务限流不变。
- 同步 Mode F repowiki 知识卡、专项玩法文档与模式总览。
**兼容性影响**: 不改存档、配置、TypeID、本地化 key、资源、Harmony/反射目标或开局 Boss 数量；只让准备阶段与其余阶段采用一致的死亡补位规则。
**验证方法**:
1. 全部 13 个 `ModeF*Guard.py` 通过，其中 `ModeFPreparationSpawnGuard.py` 明确禁止准备阶段补位门控并锁定入队后立即调度；`ModeFRespawnObservableSpawnGuard.py` 锁定“移除引用 → 建立补位债务 → `finally` 唯一入队”的异常安全顺序、单 in-flight 限流与可观察生成完成语义。
2. `EnemySpawnCoreObservableGuard.py`、`ModeEFSpawnParityGuard.py`、`ModeEFSpawnPostprocessSchedulerGuard.py`、`ModeEFNoGameplayThrottleGuard.py`、`EventSubscriptionLifecycleGuard.py`、`ArchitectureStructureGuard.py`、`OfficialCompileListFileExistenceGuard.py` 通过；相关验证合计 20/20。
3. 本轮未执行 `compile_official.bat`：该脚本必须读取工作区外游戏 DLL 并向工作区外加载目录部署，而当前会话明确禁止访问或写入工作区外路径，不能越权把旧构建当作当前源码验证。
**未验证/需人工**: 当前源码未正式编译，也未做游戏内 smoke。需在获准的 Windows 游戏环境中编译后，从准备阶段开始连续击杀多只 Boss，确认每次都会逐只补位；并跨悬赏、猎潮、撤离阶段验证补位不暂停、场上数量不累加失控。
**失败尝试**: 无。

### 2026-08-18 Mode G 编译错误修复

**状态**: fixed（源码修复与静态守卫验证；Windows 正式编译待授权环境执行）
**兼容分类**: SAFE / COMPAT
**问题**:
- `ModeG/ModeGEntry.cs` 的静态 `ModeG` 清理入口直接调用实例 partial 上下文中的 `DevLog`，导致 `CS0103`。
- `ModeGRuntimeModule_PublicApiAndShutdown.cs` 将值类型 `SceneRuntimeContext` 与 `null` 比较，导致 `CS0019`。
**修复**:
- 三处宿主销毁日志改为显式 `ModBehaviour.DevLog`。
- 场景生命周期检查移除值类型空比较，继续按冻结 scene pair/revision fail-closed；空场景名仅用于日志显示 `<empty>`。
**验证**: 28 个 Mode G 守卫、`ArchitectureStructureGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、目标文件 `git diff --check` 全部通过。当前会话未执行 `compile_official.bat`，避免访问工作区外游戏 DLL 和部署目录。

### 2026-08-18 Mode G 自带装备、小 Boss 池复制与入口收口

**状态**: fixed（源码、文档与专项静态守卫；Windows 正式编译/实机 smoke 待执行）
**兼容分类**: COMPAT
**Owner decision**: 玩家允许携带自己的装备进入 Mode G；官方 Boss 少于 6 个时从现有合格 key 中按本局 seed 随机复制；旧 BossRush 路牌不再显示 Mode G 独立选项。
**修复**:
- Mode G 自动分流和最终启动不再调用裸装扫描，不卸下、不移动、不发放 StarterKit，也不改写玩家现有装备、弹药或消耗品；营旗和血猎收发器的 Mode E/F 优先级保持不变。
- 官方 Boss 启动最低值由 6 个唯一 key 调整为 1 个；6 个保留为完整 primary/reserve 编排目标。1-5 个时 `ModeGWavePlan` 使用同一 `runSeed` 的确定性随机流从现有 stable key 复制，允许同波 primary/reserve 重复，但不伪造 `EnemyPresetInfo`、不修改 run-scoped 快照，空池和同 key 多 preset 引用仍 fail-closed。
- 移除 `BossRushOption_ModeG` 路牌注入；Mode G 继续沿地图选择/传送前冻结意图，过图后由短命 `ModeGInteractable` presenter 打开契约二选一确认页。
- Mode G 奖励候选桥接显式过滤为 Q5-Q8，不改变 Legacy Q1-Q8 候选搜索；确认页补充核心玩法与奖励说明：后续波次针对上一波距离、弹药和伤害类型，改变打法获得 Resolve；第 9 波胜利按 Resolve 发放 6-10 件 Q5-Q8 奖励并返还信物。
- 同步 `ModeGManagedBossEligibilityGuard.py`、`ModeGPlayerLoadoutGuard.py`、`ModeGEntryPreviewGuard.py`、`docs/contracts.md`、设计提案与 repowiki。
**静态验证**: 全部 28 个 `ModeG*Guard.py` 通过；`ArchitectureStructureGuard.py`、`ModBehaviourInstanceClassificationGuard.py`、`OfficialCompileListFileExistenceGuard.py`、`EventSubscriptionLifecycleGuard.py`、`EmptyCatchGuard.py` 与 `git diff --check` 通过（仅既有 LF/CRLF 提示）。
**未验证/需人工**: 当前轮次未获工作区外游戏 DLL/部署目录访问授权，未执行 `compile_official.bat`。需实机确认携带装备自动分流、1/2/5 个官方 Boss 池的九波生成、确认页布局、胜利奖励与旧三难度路牌回归。

## 变更日志

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-08-17 | 修复 Mode F 准备阶段停刷 | 移除 180 秒准备阶段的补位门控，四阶段均按死亡/失效缺口持续逐只补位，并保留单任务限流。 |
| 2026-08-07 | 提高丧尸模式前期密度并修复停刷 | 击杀目标保持旧值；场上压力、补怪频率与硬上限至少提高 3 倍，并增加可靠刷新、计数校准和远怪回收。 |
| 2026-08-07 | 修复 Mode E 两个重刷道具不可用 | 按 owner 决定完全移除重刷人口上限和点位裁剪，仅保留单任务互斥与会话安全。 |
| 2026-08-07 | 丧尸模式 Boss 与变异尸潮持续成长 | Boss 数量按五波轮次无上限递增，后期精英/特殊权重持续提高，总风险与逐只掉落收益同步成长。 |
| 2026-08-07 | 补齐丧尸模式 Boss 风险收益曲线 | 按轮次同步提升 Boss 本体、支援、净化收益、奖励箱和肉鸽奖励次数；固定单 Boss 部分已被后续设计覆盖。 |
| 2026-08-07 | 丧尸模式 Boss 波取消地图联动 | 移除地图刷怪点数量联动并明确非 Boss 速度曲线；初版固定单 Boss 已被后续递增设计覆盖。 |
| 2026-08-07 | 优化丧尸模式奖励与终端 | 移除死刷新选项，补齐终端余额/饮料库存，并收敛临时 NPC 保护扫描和失效 UI 记录。 |
| 2026-08-07 | 修复 Wiki 右书页越界与跳页 | 全文只生成一次分页边界，左右页显示连续缓存页块并以 TMP `Page` 限制越界。 |
| 2026-07-01 | AI 协作文档收敛 | 从旧 `docs/协作/FIX_TRACKER.md` 迁移 confirmed 修复记录；新增状态、owner decision、兼容分类字段。 |
