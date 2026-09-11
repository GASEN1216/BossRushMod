# RandomEvents 异步失败回归

`run.py` 从生产源码逐字抽取 `RandomEventDirector.TickEventActive`，以及 Boss 乱入、神秘商人的 context 有效性与失败回调，编译后由宿主替身执行。覆盖：全部异步失败时自然事件名额只退一次、部分成功保留名额、F3 触发不退款、清理后同场景迟到回调不能污染新轮次。

脚本随后分别移除生产退款分支和 `ReferenceEquals(_spawnContext, ctx)`，运行反向探针；两种变体都必须失败，防止回归测试复制实现却与生产代码脱节。SpawnCore/Unity 场景线程时序、角色和 StockShop 实体回收仍需游戏内 smoke。
