# 词缀选物 UI 刷新回归

入口：`python tools/run_runtime_regressions.py --filter AffixSelectionUI`。

逐字抽取生产选物处理、按钮与提示刷新、费用/锁槽判据、下一帧调度与清理方法；检查公共选物事件和 Cleanup 的接线。覆盖中英文、原版可分解/不可分解、两种事件回调顺序、钱/石不足、全锁、快速连点、取消选择、关闭、切回普通重铸及销毁，并验证共享按钮刷新不会套用普通重铸费用。

宿主替身依据官方 `ItemDecomposeView.Setup/SetupEmpty` 的按钮、cannotDecomposeIndicator、noItemSelectedIndicator 写入顺序模拟 UI 覆盖；不复制官方反编译代码，不运行 Unity。装备资格、费用与槽位数据是明确的测试输入，真实规则另由 AffixCombat 和 ManualEquipmentRecovery 覆盖。无渲染、布局、实际事件安装或真实游戏内点击的 L3 证据。
