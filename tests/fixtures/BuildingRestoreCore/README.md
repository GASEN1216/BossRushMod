# 建筑恢复核心回归

运行 `python tools/run_runtime_regressions.py --filter BuildingRestoreCore`。夹具直接编译生产文件 `Common/Buildings/BuildingRestoreCore.cs`，用可控的 Unity 对象、场景、对象缓存和协程调度替身验证两个 owner 并存、两帧后取活动场景、同场景实例去重、功能点修复、取消与旧迭代器迟到收尾、Unity 已销毁对象、装配失败后的重试。

建筑身份判定、官方反射绑定与真实功能点组件仍由两个模块的源码、守卫和 Windows 正式构建校验；游戏内摆放、拆除及旧档恢复须由 owner 实机确认。
