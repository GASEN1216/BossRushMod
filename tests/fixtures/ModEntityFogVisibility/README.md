通过 `python tools/run_runtime_regressions.py --filter ModEntityFogVisibility --jobs 1` 运行。

完整链接生产 `Utilities/ModEntityFogVisibility.cs`，抽取生产装备 `EnsureModelSubVisuals`、两把近战武器的 `PrepareRuntimeHoldAgentVisual` 与 `EnableRuntimeRenderers` 方法。执行现有 hider 复用、未知视野首帧隐藏、inactive probe 配置顺序、观察者和永久揭示、技能 `enabled` 与 `forceRenderingOff` 原值、精确光源遮罩恢复、缓存热路径、死亡和非 Raid 门控、禁用/重启/销毁退订、装备已有缓存保留，以及近战初始化结束恢复官方角色迷雾层。构建在独立输出目录完成，只执行本次 MSBuild 返回的 TargetPath，记录 SHA-256。

Unity 对象、层级和 PlayerLoop 回调、FogOfWarHider 观察者事件、LevelManager/玩家 Health、官方 CharacterSubVisuals/CharacterModel 为宿主替身；模拟 Unity 已销毁对象等于 null、GameObject 销毁连带组件和事件清理。近战 shader 与运动模糊准备使用记录顺序的替身，官方 SyncHidden 使用受控层切换。夹具不证明真实战争迷雾几何、URP 渲染、官方组件副作用或性能，需实机观察。

静态接线由 `tests/ModEntityFogVisibilityGuard.py` 验证。运行 `python tests/fixtures/ModEntityFogVisibility/negative_probes.py` 可在 `Build/` 隔离副本执行破坏、确认预期守卫和行为转红、逐字还原并核对 SHA-256；不变异共享工作区。
