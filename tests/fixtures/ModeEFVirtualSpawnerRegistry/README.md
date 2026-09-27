# E/F 虚拟 spawner 登记回归

运行 `python tools/run_runtime_regressions.py --filter ModeEFVirtualSpawnerRegistry`。

完整链接生产登记服务，覆盖空调用不创建、原命名与常驻、禁用官方生成循环、重复登记及 null 清理、精确注销、分阶段清空、两个实例隔离、销毁前列表清空、重复清理及外部销毁后重建。

官方 `CharacterSpawnerRoot` 替身保留私有 `createdCharacters` 字段和公开 `CreatedCharacters` getter，执行生产反射访问。Unity 替身实现假 null 和组件连带销毁。不运行官方 AI 或 BossLiveMapMod 扫描。
