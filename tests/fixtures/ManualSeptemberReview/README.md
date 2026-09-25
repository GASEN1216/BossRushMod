# 2026-09-20 人工实测补漏回归

通过 `python tools/run_runtime_regressions.py --filter ManualSeptemberReview` 执行。

2026-09-23 复查增补：直接链接 `PetNestPetProxyBridge.cs`，不再用空的容量桥替身。覆盖官方关卡和安全箱就绪前不得生成随从、官方背包即时从 2 格扩为 6 格、重复调用不叠加、按真实属性回收溢出、清理不误改尚存的新场景背包，以及旧 Unity 对象销毁后等待安全箱快照加载并恢复越界物品。Stat、Inventory、反射字段由宿主替身模拟；字段名、官方属性和 `SetCapacity` 行为已对照 `鸭科夫源码/`，实际游戏界面的格子与存档恢复仍待 L3。

2026-09-22 增补：远征 `PlayRoutine` 与暂停等待逐字抽取，测试驱动递归执行每个 `Current` 子 IEnumerator。分别在进入阶段和结果停留阶段暂停，验证不消费 `MarkRevealed`，恢复后同一记录仅确认一次。面板控件和存档确认只以计数替身观测，不证明实机模态菜单的操作表现。

直接编译生产的基地/局内随从生命周期、Mode H 地图派生和图鉴场景解析；孵化结果、跳过、暂停与套装命中判据逐字抽取。九张地图来自真实 JSON。替身只替代 Unity 角色、UI、时间、官方场景表与 UniTask 宿主，用可控的 TaskCompletionSource 在两段 await 之间切换席位和取消请求。

覆盖迟到生成、旧 finally 与新请求竞争、清理/重伤、跳过保留完整结果、大奖音效一次、暂停停表、子场景记录与未就绪重试、Mode E 玩家阵营下的敌友过滤，以及九图派生的实际落点/互斥席位/离场兜底。不能证明实际寻路连通性、模型显示、粒子观感或字体测量（L3 待实机）。

2026-09-25 增补：逐字抽取生产 `ComputePreparedPowerEdge`，验证双方交换对称、我方属性递增时赔率分差单调。真实链接 `ModeHMapSupportRegistry` 与 `ModeHSeedStream`，可控官方A* / Physics替身覆盖同seed场地恢复、看台/隔离点随场地移动、A*缺席/扫描中/部分路径/长绕路/遮挡拒绝，以及路径池Claim归零。替身不证明游戏地图实际A*导航或碰撞数据正确。
