# 蚀日花冠规则执行回归

入口：`python tools/run_runtime_regressions.py --filter EclipseBloomRules`。

工程直接链接完整生产 `Integration/EclipseBloom/EclipseBloomRules.cs`，不复制阶段、选招或几何算法，不使用 Unity / 游戏替身。覆盖 60% / 25% 精确边界、跳阶段与不可倒退、24 种破瓣顺序、16 种花瓣状态、阶段一轮流照射、阶段二对向与三瓣照射、旋转缺口、无瓣核心兜底、阶段三双向交替和休整、暂停、卡帧预警、销毁停止、非法输入与多实例隔离。

地面扇区网格采样直接调用生产 `GetSectorPoint`，确认可见边界与 `ContainsSector` 一致；半径内外和角边界外均另有反例。每步时间最多推进 0.1 秒，低于 10 FPS 时战斗会变慢，回归不宣称不同帧率的真实时间完全一致。`ElapsedSeconds` / `LastStepSeconds` 是特效共享的战斗时钟。

反向验证入口：`python tests/fixtures/EclipseBloomRules/negative_probes.py`。探针仅在 `Build/runtime-regressions/EclipseBloomRules-negative/` 中创建独立副本，分别破坏阶段单向性、预警时长、破瓣伤害清除、扇区角度判定、旋转时钟和无瓣核心兜底，要求每种破坏都在对应行为断言上转红，同时核对原生产文件 SHA-256 未变。

这里提供 L2 离线规则证据。宿主是否实际调用规则、有限高度、伤害 tick、敌对阵营、花瓣命中映射、死亡清理、掉落和特效可读性仍需要调用点验证与游戏内验收。
