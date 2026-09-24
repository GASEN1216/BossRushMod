# Mode H 玩家入场与交战回归

通过聚合入口运行：`python tools/run_runtime_regressions.py --filter ModeHPlayerFlow`。

真实生产逻辑：目录 JSON、目录校验、兼容矩阵、报告生成/恢复、静态资格检查、口令说明、对手目标补充与每场拍铃门，
以及普通入场、F3 开跑门、认证驱动和完成/取消后的选人页恢复。覆盖子协程异常、同步结束、旧回调迟到和重复启动；
前后比较完整赛季的 canonical 内容，确认测试不改赛季。
方法按原文字节提取，源文件 SHA-256 写在 Build 的 source-hashes.json。

宿主替身：Unity 基础数据、角色/Health、AI 控制点、预设查询、当前受控角色、F3 档位和保存门、动态认证、UI 及协程宿主；
不模拟物理、行为树、鼠标事件或真实存档 I/O。动态认证返回嵌套协程与注入异常，协程展开使用真实 ValidationCoroutineStack。
夹具程序集名模拟官方 Assembly-CSharp，仅供真实签名生成函数找到宿主，不加载游戏、不碰玩家存档。
因此只能证明 L2；真实 AI 行为、点击命中和整季观战需 L3。
