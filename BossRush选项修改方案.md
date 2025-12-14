方案文档：Boss Rush 入口改为“先弹官方传送 UI，再扣费传送”

目标：当前用户点击“Boss Rush”入口时，会直接扣费并传送。希望改为：点击后弹出官方的地图传送 UI（MapSelectionView），在 UI 中选择目的地、确认后才扣费并跳转。仍使用 1 张船票作为费用。

官方传送 UI 机制梳理（证据）
交互入口只是触发 UI：ViewsProxy.ShowMapSelectionView() 打开地图选择面板（文件：Duckov/UI/ViewsProxy.cs (lines 58-61)）。
目的地条目：MapSelectionEntry 序列化了 sceneID、beaconIndex、Cost、conditions。点击时先检验 Cost.Enough 再回调主面板（Duckov/UI/MapSelectionEntry.cs (lines 14-95)）。
确认与扣费：
MapSelectionView.NotifyEntryClicked 记录 LevelManager.loadLevelBeaconIndex = entry.BeaconIndex，启动 LoadTask（MapSelectionView.cs (lines 56-80)）。
LoadTask 显示目的地信息与费用，等待确认；确认且 cost.Enough 时调用 cost.Pay()，然后 SceneLoader.Instance.LoadScene(sceneID, overrideLoadingScreen, clickToConinue:true) 进行加载传送（反编译自 TeamSoda.Duckov.Core.dll）。
落点选择：LevelManager 在目标场景用 loadLevelBeaconIndex 选择出生点路径 StartPoints 或 StartPoints_<index>（LevelManager.cs (lines 610-639)）。如果路径不存在，回退到默认起点。
费用模型：Cost 支持 money 与 items；是否免费取决于 money==0 且 items 为空（Duckov/Economy/Cost）。
改造思路
拦截 Boss Rush 入口，不直接传送

现有入口（例如按钮点击、交互）改为调用 ViewsProxy.ShowMapSelectionView() 或 MapSelectionView.Instance.Open(null)，而不是直接扣费/传送。
这样点击只会打开传送 UI，不发生扣费或跳转。
配置 Boss Rush 的目的地选项

在 MapSelectionView 中提供一个条目代表 Boss Rush 场景。
条目关键字段：
sceneID：Boss Rush 目标场景 ID（如 Level_DemoChallenge_Main）。
beaconIndex：落点编号，对应目标场景下 SceneLocationsProvider 的路径 StartPoints_<index>（或默认 StartPoints）。
Cost：设为 1 张船票（自定义票的 item id），money 设 0；这样 UI 会显示费用，确认时才扣除船票。
conditions：可选，控制解锁条件。
你可以静态序列化配置条目，或在运行时克隆/填充 MapSelectionEntry 组件并设置上述字段。
保持扣费与跳转逻辑使用官方流程

不要自己手动扣费/传送，直接让 MapSelectionView.LoadTask 走原逻辑：确认后 cost.Pay()，然后 SceneLoader.LoadScene 加载目标场景。
这样“扣费在确认时”满足需求，且复用官方 loading/黑屏/落点逻辑。
落点与场景准备

确保目标 Boss Rush 场景在 SceneInfoCollection 里有 sceneID 和 SceneReference（已有即复用）。
在目标场景的 SceneLocationsProvider 下存在 StartPoints 或 StartPoints_<beaconIndex>。否则会回退到默认出生点。
船票费用的具体配置

若船票是自定义物品：在 Cost 中填 items = [{ id = BossRushTicketTypeId, amount = 1 }]，money=0。
若要兼容已有的 Boss Rush 船票逻辑，可直接复用你在 Mod 中动态注册的船票 type id。
实现步骤（最小改动指引）
入口改写：在 Boss Rush 的入口事件中，替换“直接传送/扣费”逻辑为 ViewsProxy.ShowMapSelectionView()。
注入/配置 MapSelectionEntry：
如果已存在 Boss Rush 条目，修改其 Cost 为一张船票；确保 sceneID 指向 Boss Rush 场景，beaconIndex 指向正确落点。
如果没有条目，在 MapSelectionView 初始化时（或打开前）动态创建/克隆一个 entry，设置上述字段。
确认逻辑无需修改：保留 MapSelectionView.LoadTask 默认行为，让确认后再 cost.Pay() + SceneLoader.LoadScene。
关键文件与位置
Duckov/UI/ViewsProxy.cs (lines 58-61)（ShowMapSelectionView）
Duckov/UI/MapSelectionEntry.cs（sceneID/beaconIndex/Cost 条目）
Duckov/UI/MapSelectionView.cs (lines 56-110)（点击→确认→扣费→LoadScene）
Duckov/Economy/Cost（费用数据结构）
LevelManager.cs (lines 610-639)（落点选取）
注意点
确认 UI 里 Cost.Enough 逻辑会阻止无法支付时的点击；确保票的 type id 与玩家拥有的物品一致。
若需要多目的地（多个落点或不同 Boss Rush 场景），可添加多个 MapSelectionEntry，各自设置 beaconIndex/sceneID。
入口依然使用 InteractTime 和交互名字，可保持原有交互表现；关键是避免在交互回调里直接调用传送 API。