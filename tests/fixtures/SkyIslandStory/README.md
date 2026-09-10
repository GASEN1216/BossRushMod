# 天空岛剧情与槽位持久化执行回归

分类：COMPAT / SCHEMA+。运行 `python tools/run_runtime_regressions.py --filter SkyIslandStory`。

工程直接链接生产剧情规则、编解码、服务、共享 JSON parser/writer、槽位 store 与保存协调引擎。
SavesSystem、基地状态与每帧写盘节流是内存替身，不加载 Unity 或接触玩家存档。

覆盖东西顺序自由、清场前置、四支线、先发现后交付、折翎互斥结果、两种终章、通关补支线、
重复提交、重入恢复、战斗帧禁落盘、物理写失败后重试、槽位切换、同槽删除、未知 schema、
损坏字段保护与 JSON 转义。它不证明实际敌人死亡、交互触达、门碰撞或官方 ES3 落盘行为。

2026-09-10 可玩性评估追加：岛上落盘去抖（到访 / 清场 / 见闻攒满 `FlushDebounceSeconds` 才写、剧情动作立刻写、
离岛绕闸全部写掉、重入不丢）、三件战斗了结结果的字幕与 `TryApply` 回话同源、`SKY_TIMING` 分段计时行的格式与去重。
`UnityEngine.Time` 替身多了 `realtimeSinceStartup`（计时时钟，测试显式赋值），`Debug` 替身多了记录最后一行的 `Log`。
