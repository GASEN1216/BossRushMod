# 钟守节拍器（ReloadMetronome）

2026-10-10，COMPAT。TypeID 500107，不新增存档字段或配置键。

## 玩家路径

无声钟守（`sky_bellkeeper`）好感 5 级 → 永久 NPC 的 `ShopOption` → `ReloadMetronomeShopInteractable` → 共享单商品兑换弹窗 → 20 枚背包贝壳 → `SkyIslandInventoryTransaction.TryReserve` → 背包 `Inventory.AddItem`（满包失败返还，不落地）→ `Commit`。失败归还预留原件；相同组件随婚后本人在基地重建，货币与价格保持一致。旧物匣不依赖岛上可能缺失的 `StockShopView`。钟庭 `Search_H` 装置复用同一兑换组件，居民缺席或婚后离岛仍可买；装配与销毁复用 `SkyIslandWorldStoryServices` 的装置商店 helper，好感显隐仍是事件驱动，不增加 Tick。商品不进叮当商店或随机奖池。

## 生产调用链

- [配置与数值](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeConfig.cs)：窗口中心 62%，宽度按 15% 装填时长限制在 0.16–0.30 秒；整匣装填且总时长至少 0.75 秒。
- [纯判定](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeRules.cs)：一轮一次，早按/晚按均消费机会，不延长换弹。
- [运行时](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeRuntime.cs)：正式 `BeginReload` 成功才开会话；`TryToReload` 的再次请求判定；命中时排队，由该枪下一次 `UpdateStates` 前置推进 `stateTimer` 至原版 `ReloadTime`。
- [精确补丁](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomePatches.cs)：不跳过原方法，不写弹药、属性或官方存档键；原版完成 `StartLoadBullets`、音效与 `OnLoadedEvent`。
- [节拍 HUD](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeHud.cs)：借用 `AimMarker` 层级，机械刻度、带帽白针与短拖尾显示进度；命中播放铜色高光与短机械卡扣声，显示「合拍装填」，自然完成后保留 0.24 秒尾光。硬取消立即销毁；暂停与官方 HUD 隐藏双门。
- [兑换](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeShop.cs)：复用好感商店显隐，确认时重验本人、角色、距离、场景、存活与好感，物品事务无跨帧状态。

## 生命周期与接线

`NewWeaponRuntime` 统一初始化、切图清理与销毁。换手清会话，槽位事件置脏后检查实际佩戴；卸装、死亡、切图起始取消排队完成与 HUD。未佩戴不创建 HUD 或轮询，`UpdateStates` 非目标枪只做字段比较。

装备配置器、ItemFactory 配置器、动态注册与克隆兜底均登记。图标为赛璐璐风格的弹匣配重节拍器，深青外壳、黄铜边框与钟守铃铛标记；源文件 `Assets/Items/reload_metronome.png`，提示词 `ArtSource/ReloadMetronome/icon-prompt-cel.txt`。由已授权的备用生图 API 生成并保留其透明通道。

[独立资源包](file://Integration/NewWeapons/ReloadMetronome/ReloadMetronomeAssets.cs) `Assets/ui/reload_metronome` 内含图标、拖尾、高光与短机械音；`tools/build_reload_metronome_resources.ps1` 解析作者工程并调用 `ArtSource/ReloadMetronome/ReloadMetronomeBundleBuilder.cs.txt`，只重打这四项，回读检查 BC7、尺寸、不可读贴图与音频后复制。图标经 `ProductionIconCache.CloneTexture` 做 GPU 副本，归持久 prefab 持有；克隆物品销毁不释放共享副本，Mod 卸载包也不损坏已有图标。程序化图标仅作资源缺失兜底。反馈资源只在实际换弹创建 HUD 时加载，包由装备模块清理。

中英名称和描述进入集成本地化入口，随机掉落黑名单同时更新数据与 fallback。Wiki 正文、导航及 TypeID 台账同步。

## 验证边界

执行回归入口：`python tools/run_runtime_regressions.py --filter ReloadMetronome`。结构接线、纯节拍与宿主替身中的事件路径属于 L1/L2；编译不能证明 Harmony 实机安装、鼠标附近布局、手感或婚后 UI 正确。人工验收步骤见 `docs/reports/testing/2026-10-10-钟守节拍器验收.md`。

2026-10-10 合并：物品编号经 owner 授权改为 500107，点名册继续使用 500106。商品与好感解锁接入 permanent.shop 蓝图；钟守专属贝壳交互优先装配，浮舟的现金商店保持独立。
