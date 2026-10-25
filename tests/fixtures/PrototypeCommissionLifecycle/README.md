# 危险试用品委托异步生命周期回归

入口：`python tools/run_runtime_regressions.py --filter PrototypeCommissionLifecycle`。

完整提取当前生产 `DangerousPrototypeCommission` 模块和数据类型，执行真实接取、异步对话收尾、完成提交、场景/存槽回调和清理。覆盖正常完成、重复交互、旧交互体、切槽事件及无事件槽位漂移、加载、死亡、主玩家替换、关卡替换、NPC/选项销毁、模块销毁，以及取消后开启新对话再收到旧回调。

宿主替身包括 Unity 对象、对话服务、好感、NPC 交互组与内存持久层。对话替身故意忽略取消并延迟返回成功，验证业务 owner 自身能够拒绝过期回调；持久层只记录写入槽号，不读取或写入玩家存档。Unity 销毁模拟假 null 与根对象连带组件销毁。该夹具不验证官方对话画面、真实磁盘写入或游戏存档流程。

加载开始用真实模块所订阅的 `SceneLoader.onStartedLoadingScene` 替身事件驱动，期间不调用模块 Tick，与宿主加载时跳过模块 Update 的门保持一致；销毁后检查订阅归零。

每次用独立目录显式构建并只执行本次 TargetPath，保存生产源码 SHA-256 与结果日志。反向验证见 `negative_probes.py`。
