# Integration/AGENTS.md — 集成层专项规则

> 先读根目录 `AGENTS.md`。本文件只记录 `Integration/` 独有的约束。官方 API 的静默失败类陷阱见 `docs/contracts.md` §7.1。

## 职责边界

`Integration/` 是内容集成总线：动态物品与装备、NPC（含捏脸 NPC `NPCs/DuckNpc/`）、商店、游戏内 Wiki、好感度与婚姻、重铸、词缀锻造、鸭皇图鉴、日报、竞技场后山、新武器与套装、死亡亡魂、Boss 专属资源，以及天空岛物品（`SkyIsland/`；岛上运行时在 `DebugAndTools/SkyIsland/`）。

## 新增物品 / 装备要接的地方

漏掉任何一处都**不会编译报错**，只会在运行时表现为「有代码拿不到」或「拿到了是坏的」。逐项清单与参考实现见 `docs/guides/新增物品代码接线清单.md`（local-only），最少过这几项：

1. **TypeID**：取 `docs/contracts.md` §1 的下一可用号，同时更新 contracts §1、根 `AGENTS.md` §4.3、`docs/reference/Bossrush使用物品ID表.md`（`TypeIdLedgerGuard` 交叉核对）。常量放 `BossRushItemIds`，不散落魔法数。
2. **配置器**：在 `Integration/Items/ItemContentRegistry.cs` 的 `RegisterItemContentConfigurators()` 里登记。「注册 TypeID」和「登记配置器」是两件事，漏后者就是只注册不配置（`ContentRegistryGuard`）。
3. **动态注册**：`BossRushDynamicItemRegistry` 登记注册计划（bundle，或无专属模型时的克隆兜底）。漏了，重启后玩家手里的物品会退化成 `FallbackItem`（`BossRushDynamicItemRegistryGuard`）。
4. **本地化**：`DisplayNameRaw = "BossRush_*"` 注入中英文，并挂进 `InjectLocalization_Extra_Integration()`（根 §4.4，`LocalizationInjectionGuard`）。
5. **数值**：设 `item.Value`（不设 NPC 商店标价 0）；有耐久的装备加 `EquipmentHelper.AddRepairableTag`；配置器可能被重复调用，挂 Modifier 用幂等的 `EquipmentHelper.EnsureModifierOnItem`。
6. **掉落黑名单**：自定义物品默认登记 `Config/LootBlacklistRegistry.cs` 与 `Assets/Data/LootBlacklist.json`（`LootBlacklistDataRegistryGuard`）。黑名单只挡随机奖池，专属掉落、商店、奖励箱不查它；`Special` tag 挡不住随机奖池。
7. **获取途径**：至少一条玩家实际走得到的产出（商店、掉落、奖励、配方、剧情）。「类型存在」不等于「拿得到」，编译和守卫都查不出零获取途径。
8. **资源部署**：新 bundle 登记进 `tools/resource_release_manifest.json` 的 `bundles`（`compile_official.bat` 结尾由 `tools/Deploy-ResourceBundles.ps1` 按它部署并校验 SHA-256，游戏目录出现清单外的 bundle 会让构建失败）；新的 JSON / PNG 目录才需要在 `compile_official.bat` 部署段补 copy 行（`Assets/Sounds` 整树已自动复制），否则只在作者本机能用。
9. **Wiki**：玩家可见的内容同步 `WikiContent/{zh,en}/`（规则见 `wiki-site/AGENTS.md`）。

内容设计上，每件新东西写清「从哪来 / 拿来做什么（卖钱不算）/ 串到哪条系统线」，功能重叠的拉开定位。

## 自定义头盔与装备尺寸

- `EquipmentModel` 根节点保持单位变换。官方 `ChangeEquipmentModel` 清零根节点位置/旋转，不清缩放；轴向、尺寸与佩戴偏移只改网格子节点。
- 头盔以 `tools/helmet_fit_profiles.json` 为唯一校准数值源，登记源网格 bounds、源上方/正面及子节点绝对变换。先核对轴向，再按盔壳校准；长角/帽绳/飘带拉偏的整体盒中心不能当佩戴中心。参考龙王/龙裔的佩戴关系，不照抄 Scale。
- `python tools/helmet_fit.py --sync` 同步至作者工程，`HelmetFitUtility` 在生成时绝对赋值，不能把偏移重复烤入 FBX。新头盔或源网格尺寸改变须更新校准，缺登记或与源资源不符时构建失败。
- 头盔走 `HelmetFitBundleBuilder`，天空岛模型生成保留校准调用，全量 `DuckovBundleBuilder` 打包前验证。构建后运行 `python tools/helmet_fit.py --check` 与 `python tools/run_guards.py --filter HelmetFit`，再核对包内姿态和部署哈希；缺作者工程的 PARTIAL 不是资源通过。
- 护甲按胸腹与肩臂活动范围、背包按贴背面、面罩/耳机按脸部与两侧定位，不套统一尺寸或偏移。实际头型和动作仍需 owner 试戴；固定资源无法保证极端捏脸均无穿模。教程见 `docs/guides/头盔佩戴与装备尺寸校准.md`。

## 其他规则

- 装备走 `EquipmentFactory`，物品走 `ItemFactory`，不要绕过工厂手写注册。
- 想让原版地图击杀也掉落，挂 `CharacterMainControl.OnDead` 前缀并补齐 defer 协议（`docs/contracts.md` §7.1，`ExtraBossDropDeferGuard`）。
- 新增 NPC 优先实现 `INPCModule`，由 `NPCModuleRegistry` 自动发现，不往 `InitializeAffinitySystem()` 里堆特例。永久捏脸 NPC 写 `Assets/Data/DuckNpcs.json` 蓝图（`DuckNpcInvariantGuard`）。
- 礼物、好感、商店、婚姻复用 `Integration/Affinity/`、`Integration/NPCs/Common/`、`Integration/Wedding/`；台词走 `Integration/Dialogue/` 的 `DialogueManager`。
- 事件订阅要有幂等的 owner 与退订路径（根 §4.6）；静态缓存类提供 `ResetStaticCaches()`；新子系统的状态放自己的 RuntimeModule（根 §4.15）。
- 表现层（特效、粒子、光）写独立类型，不往 `ModBehaviour` partial 上加；程序化粒子材质复用 `RingParticleEffect.GetSharedParticleMaterial()`。
- 装备专属的预热、轮询、对象池只在主玩家实际手持或穿戴时启动（根 §4.12）。
- 持久化复用共享实现：落盘走 `Common/Lifecycle/BossRushSaveCoordinatorEngine` 与 `BossRushSlotJsonStore`，嵌套 JSON 走 `Common/Data/BossRushJsonValue`，不再建第二套。新存档 key 用 `BossRush_` 前缀，读取要兼容旧档。
- 捏脸 NPC 的台词每句可以写裸字符串或 `{"cn": …, "en": …}`，两种能混排。档位判据看 `lines` 键，不能只看 JSON Kind，否则 `[{cn,en}]` 单档会被误判成档位数组、整组台词静默丢失（执行回归 `PermanentDuckNpcDialogue`）。
- 击杀触发的技能：`Health.OnDead` 回调里只做过滤与调度，结算延后到协程；每个系统只保留一个订阅点；嵌套死亡用深度计数或「结算中」标志门控；首跳只认 `!isFromBuffOrEffect` 的直接击杀。
- 建筑交互体继承 `Interactables/BossRushBuildingInteractableBase`，子类只声明交互名、日志前缀、交互组标签、标记高度、可交互条件与完成动作。
- Boss 子目录新增文件遵循 `docs/architecture/BOSS模板约定.md`；旧 Boss 不强制重构。
- 自定义武器的运行时参数在 `Integration/BossRushIntegration.cs` 的 `RegisterCustomWeaponRuntimeConfigs()` 登记。
- 各子系统的本地化放 `Localization/<子系统>Localization.cs`，挂进 `InjectLocalization_Extra_Integration()`；台词语言在取用时解析（玩家能在游戏里切语言）。
- 玩法系统总开关默认恒开，只暴露调参旋钮；鸭生无常默认开启并允许手动关闭；新增 ModConfig 键要登记白名单，否则热更新静默失效（`ModConfigOptionChangeGuard`）。
- 游戏内 Wiki 书由 `Integration/WikiContentManager.cs` 解析 `WikiContent/`：只认标题、粗体、列表、行内代码、链接与单行 `[tip]` / `[warn]`，不认图片和表格（详见 `wiki-site/AGENTS.md` §2）。

## 验证

- `python tools/run_guards.py --changed-only`，然后 Windows 编译（命令见根 `AGENTS.md` §2）。
- 名称不是 `*BossRush_*`、图标不是白底问号、商店价格、掉落与使用行为只能实机确认；没实机就写明。
