# 2026-09-20 人工实测补漏回归

通过 `python tools/run_runtime_regressions.py --filter ManualSeptemberReview` 执行。

2026-09-22 增补：远征 `PlayRoutine` 与暂停等待逐字抽取，测试驱动递归执行每个 `Current` 子 IEnumerator。分别在进入阶段和结果停留阶段暂停，验证不消费 `MarkRevealed`，恢复后同一记录仅确认一次。面板控件和存档确认只以计数替身观测，不证明实机模态菜单的操作表现。

直接编译生产的基地/局内随从生命周期、Mode H 地图派生和图鉴场景解析；孵化结果、跳过、暂停与套装命中判据逐字抽取。九张地图来自真实 JSON。替身只替代 Unity 角色、UI、时间、官方场景表与 UniTask 宿主，用可控的 TaskCompletionSource 在两段 await 之间切换席位和取消请求。

覆盖迟到生成、旧 finally 与新请求竞争、清理/重伤、跳过保留完整结果、大奖音效一次、暂停停表、子场景记录与未就绪重试、Mode E 玩家阵营下的敌友过滤，以及九图派生的实际落点/互斥席位/离场兜底。不能证明实际寻路连通性、模型显示、粒子观感或字体测量（L3 待实机）。
