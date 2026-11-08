# 潮闸镇远区角色与游泳翻滚回归

直接链接生产 `TidegateEnemyActivity.cs` 与 `TidegateSwimDashPatch.cs`，检查远区挂起、55/65 m 滞回、原角色血量与装备保留、死亡不复活、寻路取消与恢复、潮汐队伍豁免、玩家交火豁免、灯塔唤醒、暂停和消融冻结，以及游泳动作拦截与翻滚入水清冲量。

Unity 生命周期、导航和 Harmony 特性由替身隔离，执行真实补丁方法但不假装安装补丁；不能证明真实物理、帧时间或补丁命中。游泳实机调用在 `TIDE_DRILL_SWIM`，性能仍由两个 10 秒窗口采样。入口：`python tools/run_runtime_regressions.py --filter TidegateActivity`。
