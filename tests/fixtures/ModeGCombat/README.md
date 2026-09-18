# Mode G 生产流程回归

运行：`python tools/run_runtime_regressions.py --filter ModeGCombat`，需要 .NET 8 SDK。

真实生产逻辑：完整链接战斗遥测、直伤分类器、三轴判据与 Modifier 操作、运行状态、九波计划、确定性随机、八项契约和两份持久化类；逐字抽取波末结算、下一波弹药准备、HUD 目标格式和奖励计划。源文件 SHA-256 写入 `Build/runtime-regressions/ModeGCombat/source-sha256.txt`。

宿主替身：Health 按官方“先扣血 → OnDead → OnHurt”的顺序分发事件；调用方指定官方已算好的 `finalDamage`，因此不证明护甲/暴击公式。Item/Stat、元数据、角色切换与存档使用内存替身，不访问玩家存档。Unity 对象替身模拟已销毁对象等于 null 与 GameObject 连带销毁组件。

覆盖致命伤、减伤与暴击后的计分、间接伤害、精确抑制、污染与缓存溢出、普通弹药缺省字段、休整期禁令违规、属性末击提示、退订、小 Boss 池、契约候选与目标可达性、Resolve 上限、奖励档位及未来存档版本保护。另测缓存预热后同一玩家/主 Boss/武器的1,000次非致命命中托管分配，同时检查伤害未漏计；这只代表 .NET 8 替身内的伤害路径，不代表 Unity Mono 全局帧耗。八契约可达性使用九波规则允许的进度作为见证，不代表玩家手感或实机九波战斗已通过。

不覆盖 Unity 物理/导航、真实 Boss AI、异步生成工厂、实际物品交付、HUD 像素可读性与帧耗；这些需要 Windows 编译及 owner 实机验证。
