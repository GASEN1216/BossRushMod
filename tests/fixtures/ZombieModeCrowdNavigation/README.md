# ZombieModeCrowdNavigation

通过 `python tools/run_runtime_regressions.py --filter ZombieModeCrowdNavigation` 运行。

2026-11-24 玩家反馈：尸潮第 5 波起丧尸一抽一抽、不贴身、挤在拐角和门外。根因是官方 `TraceTarget` 每 0.15 秒调一次 `AI_PathControl.MoveToPos`，后者先取消在途寻路再重新排队；上百只丧尸同时追玩家时 A* 队列积压超过 0.15 秒，几乎每条路径都在算完前被取消。

**真实生产逻辑**：逐字抽取 `ZombieMode/ZombieModeCrowdNavigation.cs` 的 `ShouldThrottleZombieModePathRequest` 及两个几何辅助方法；节流常量按 `ZombieModeTuning.Performance` 原文生成。验证在途请求不被取消（超时除外）、无路径 / 到终点 / 被瞬移立即放行、近身保持官方 0.15 秒、远处按距离插值到最多 1 秒、目标大幅挪动提前重算、时钟回拨与非有限值不拦。另用 0.4 秒回调延迟的队列模拟复现「官方节奏下一条路径都算不完」，并证明节流后路径能送达。

**宿主替身**：Unity `Vector3` / `Mathf` 与 marker 字段。Harmony 前缀的安装与 `AllowPathRequest` 的 marker 查找、run 过滤、叠罗汉拨离（ECM2 `groundCollider`、`SetPositionAndRotation`、墙体射线）不在此夹具内，由正式编译和实机验证；结论为 L2。
