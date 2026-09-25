# 装备初始化归属回归

通过聚合入口运行 `python tools/run_runtime_regressions.py --filter EquipmentBootstrapOwners`。

夹具直接链接逆鳞工厂、bootstrap、配置、宿主兼容桥，以及共享 `AbilitySystemHelper` / `EquipmentAbilityConfig`。从两个原 bootstrap 文件抽取完整龙王/幽灵女巫模块类，从现有宿主桥抽取原入口；独立额外掉落类由既有掉落回归和守卫覆盖。

检查逆鳞初始化 owner 隔离、重入语言刷新、真实配置器登记与旧公开入口、两次半秒等待；检查大镰共享引用跨 owner 幂等、场景缓存失效后的恢复、两种武器的 15 秒等待上限、等待后绑定、同角色不重复绑定、换角色重绑，以及清理次序和重复资源释放。

Unity 对象/协程、物品变量、管理器与资源服务为记录调用的替身；销毁 GameObject 会销毁组件并体现 Unity 假 null。共享辅助算法、逆鳞配置和三个装备流程来自生产源。渲染资源与游戏内装备行为仍需正式 Windows 构建和实机验证。
