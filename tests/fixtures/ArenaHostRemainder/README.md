通过 `python tools/run_runtime_regressions.py --filter ArenaHostRemainder` 执行。

完整编译 Arena 的 LegacySpawn、LootTemplates 和 CharacterRegistry 三份生产文件，并抽取同一模块真实 OnAwake/OnDestroy。验证标准生成分支、迟到取消、队伍安全网、既有倍率和回调顺序、归属及范围清理、共享缓存和场景失效、奖励模板及碰撞配置。

Unity 对象、官方角色工厂、异步任务桥、Stats/AI/资源查找是可控制替身。销毁会销毁组件并模拟 Unity 假 null；Recovery 和掉落追踪为记录调用顺序的替身，不替代其它夹具或真实地图实测。生产 SHA-256 随执行结果保存。

同时链接完整 MutatorBossRegenRuntime，验证 Arena 与 D/E/F 分支顺序、原 deltaTime 取用和已销毁角色跳过。
