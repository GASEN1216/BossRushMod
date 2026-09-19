# 天空岛遭遇 owner 执行回归

运行 `python tools/run_runtime_regressions.py --filter SkyIslandEncounters`。

直接链接生产 `SkyIslandEncounters.cs` 全文件与真实内容 registry / JSON。Unity 对象销毁、Health 事件、
异步角色工厂、物理地面和 UI 日志使用替身；不访问游戏存档或真正生成 Unity 角色。

覆盖敌人先死后销毁仍能清场、提交失败重试、不重复生成已死亡槽、缺 Health 的异常角色回收、
离场期间在途 preset 保留、迟到角色回收、角色清理后延迟释放 preset、实际 JSON 接线和共享剧情脸。
官方掉落仅验证生产配置启用其默认路径，不声称替身验证了官方 LootBox / 经验 / 魂的游戏行为。

气泡调度器和话语表也直接链接生产文件；官方气泡 UI、暂停及剧情对话状态是显式替身。
检验冷却、距离、暂停、owner 失效和显示失败不消费预算，不声称已验证官方气泡渲染。
头目配装/招式由 Forge 替身承接；`SkyIslandBossVoice.cs` 直接链接生产全文件，Forge 将真实组件挂到替身角色。

`ChatterBehaviorRegression.cs` 从真实遭遇 Tick / Health 事件 / BossVoice Update 驱动，覆盖当前目标与近期动静、
交战禁止闲话、脱战恢复、忙碌/距离/展示失败后的事件重试、目标变更丢弃、多人争用与死亡优先级、
事件过期及进度合并、事件与闲话各自冷却、当刻语言、补刷复位、血线重试及重复绑定/销毁退订。
真实导航、官方 AI 感知和 UI 像素未被替身验证；待播只承诺进入展示流程，不保证之后异步展示完成。

2026-09-18 补齐官方气泡合同：manager 缺席/停用/销毁、缺 prefab 的同步静默完成、
已失败/取消的异步结果、正常跨帧完成及清理后的迟到异常。替身的 Forget 不阻塞，
不再用同步抛错代替官方 UniTask 失败。仅证明请求记账与异常观察，不证明实际像素可见。
