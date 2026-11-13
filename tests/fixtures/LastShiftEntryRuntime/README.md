# 末班编组站仓库入口执行回归

运行 `python tools/run_runtime_regressions.py --filter LastShiftEntryRuntime`。

直接链接生产 `LastShiftRuntimeModule`、`LastShiftEntry`、`LastShiftYuXiang`、双语台词、场景本地化入口、共享 `DialogueManager` 与 `DuckNpcSpawner`。
覆盖仓库区加载、重复初始化、正在运行与已关闭会话、跨出击回访、同名子场景重载、卸载时撤销旧请求、入口包租约释放和失败重试。
独立控制官方角色创建和装备加载完成，验证等待装备期间切图会立即收掉角色，且迟到结果不影响新角色。
从两个实际交互回调进入官方字幕和选项请求，断言语言切换、迟到的官方回调、交票与登车分支、Esc 语义及剪票延迟。

夹具替代 Unity 场景与对象层级、官方对话播放器回调、官方角色工厂、装备加载、剧情持久化和出击启动。剧情提交的存档先后顺序由 LastShiftRaidGuard 另核。
Unity 渲染、仓库区实际站位、官方界面显示与输入仍需实机检查。
