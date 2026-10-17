# ZombieModeAmmoSelection

通过 `python tools/run_runtime_regressions.py --filter ZombieModeAmmoSelection` 运行。

抽取丧尸模式按口径发弹药的生产方法（`TryGiveZombieModeAmmo`、`PickZombieModeAmmoTypeForCaliber`、`SearchZombieModeAmmo`、品质判定与候选抽取），对一张可控物品表执行。覆盖：普通子弹仍按调用方品质区间发一整组；火箭弹这类只在高品质出现的口径改发该口径最低品质那档；不堆叠 / 小堆叠的重弹药补到最少发数且不超过调用方要的数量；不堆叠物品不写数量；只挂 Bullet 标签的弹药也能找到；同口径的发射器本体不会被当弹药发出；完全没有弹药的口径什么也不发。

物品表、官方 `Search` 的向下降级、`StackCount` 夹到上限与发放入口是替身；不替代真实物品表与官方 `ItemFilter` 实现（标签全含、品质闭区间是假设）。实际源 SHA-256 写入对应构建目录。
