# 天空岛遭遇 owner 执行回归

运行 `python tools/run_runtime_regressions.py --filter SkyIslandEncounters`。

直接链接生产 `SkyIslandEncounters.cs` 全文件与真实内容 registry / JSON。Unity 对象销毁、Health 事件、
异步角色工厂、物理地面和 UI 日志使用替身；不访问游戏存档或真正生成 Unity 角色。

覆盖敌人先死后销毁仍能清场、提交失败重试、不重复生成已死亡槽、缺 Health 的异常角色回收、
离场期间在途 preset 保留、迟到角色回收、角色清理后延迟释放 preset、实际 JSON 接线和共享剧情脸。
官方掉落仅验证生产配置启用其默认路径，不声称替身验证了官方 LootBox / 经验 / 魂的游戏行为。
