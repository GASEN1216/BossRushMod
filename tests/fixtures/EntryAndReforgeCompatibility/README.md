# 入场与重铸兼容回归

运行：`python tools/run_runtime_regressions.py --filter EntryAndReforgeCompatibility`。

逐字抽取生产 `Reforge` 整个入口、符号抽取、边界计算、属性收益判据和揭晓队列，覆盖
两轴后坐力、官方负极性、控制力与未知属性，三类属性、整数/小数/负值、正负倾向、锁定与最优边界。
费用、随机种子、属性容器、属性写入和存档通知是替身，不验证 Unity 属性刷新或存档落盘。

逐字执行生产地图点击前缀、目标查询、附加场景判定和宿主 `OnSceneLoaded`，覆盖
附加资源不触发清理、真实关卡/基地/菜单仍分发，以及普通地图条目/缺票不能选中目标。
Unity scene 与组件是确定性替身；不声称已复现秘法纪元或验证 Harmony 实机安装。
自定义出生点的加载等待由现有 `ModeHSceneEntry` 夹具补充覆盖。

抽取 SHA-256 写入 `Build/runtime-regressions/EntryAndReforgeCompatibility/source-hashes.json`。

初始出生覆盖完整 `BossRushInitialSpawn.cs`，仅把 UniTask 类型名换为 Task；生产方法体不变。
同时抽取确认/取消/消费与模式出生判据，覆盖未确认、取消、票不足、普通出击、直接传送、
主场景/子场景精确匹配、配套补丁缺失、六种模式、异步成功/失败、重复调用与销毁后迟到完成。
返回给官方首次创建的坐标、子场景加载参数与到达标记均从生产代码取值。
官方 InitLevel 的三次位置使用来自本机游戏 DLL 反编译核对，不在本夹具里重写官方初始化器；
Harmony 安装、物理落地、出场动画以及第三方改写 InitLevel 的情况仍需 L3。
