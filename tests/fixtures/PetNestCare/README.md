# 宠物照料事务与正式入口回归

运行 `python tools/run_runtime_regressions.py --filter PetNestCare`。

直接链接完整生产 `PetNestCompanionCare.cs`、共享存档协调引擎和落盘节流器；逐次原样抽取天空岛库存事务的预留、提交、回滚，以及 PetNest 的 `StageCommit`、资产采集、资产保存、语言注入入口、历史死亡率判据。菜单名使用官方 `InteractableBase.InteractName` 属性。

覆盖部分堆叠/末件移除后的通知异常、变更前失败、候选提交拒绝、物理保存失败重试、提交后表现异常、基地三个正式子选项、实例及存档槽归属、切换语言无需重生宠物、已结算历史概率与未结算旧风浪保护。

Unity 替身模拟销毁对象等于 null、销毁根对象连带组件；库存通知按官方实际变更在前的时序注入故障。保存替身只写内存快照，不访问玩家存档；Bundle 的候选接纳用 DTO 替身，JSON codec 兼容已有持久层回归负责。实际菜单排列、动画、寻路、回血画面及真实存取仍需实机验证。
