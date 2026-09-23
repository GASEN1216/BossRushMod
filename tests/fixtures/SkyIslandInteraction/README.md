# 天空岛交互与语言刷新执行回归

运行 `python tools/run_runtime_regressions.py --filter SkyIslandInteraction`。

入口脚本逐字抽取生产交互提交、服务判据、奖励落点、音频停止、环形几何和语言刷新方法；其余剧情判据链接完整生产文件。Unity 布局、物理、音频和经济服务使用显式替身，不访问玩家存档。

语言回归抽取船点 RefreshSignText、居民 RefreshLocalizedNames、序章 InjectLocalizations、ResidentName 与 NPCNameTagHelper 的已有登记更新方法及字段。保留语言覆盖字典，对同一批 UI 中英双向切换，核对隐藏/销毁/注销居民、新场景招牌和稳定语言下的写入次数。官方 UI 创建与布局仍是替身；生命周期调用接线由 SkyIslandLiveLocalizationGuard 补充，异步出生后现取姓名目前为 L1 验证。

2026-09-23 审美审查追加（PanelLooks）：抽取面板的主动关闭 `Close`（状态当帧收掉、旧画布改名交给共享淡出）、重开时把共享遮罩的淡入当帧落定、选项纯显示修饰（弱表挂在 Choice 旁边，Choice 仍只有 Label / Select 两个字段）、材料不够的计数标红与手记正文排版（不拆句子，F3 按子串匹配）。淡出、图标与实际排版仍是替身，观感只能实机看。
