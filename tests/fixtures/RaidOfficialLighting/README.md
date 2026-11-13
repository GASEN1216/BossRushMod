# 独立出击地图官方日光执行回归

运行 `python tools/run_runtime_regressions.py --filter RaidOfficialLighting`。

夹具直接链接 `Common/World/RaidOfficialLighting.cs`，覆盖官方方向光来源的选择、停用克隆跨 Single 幕布、场景激活后绑定、阴影资源隔离、游戏原版光色变化的只读采样，以及取消、失败和离图时的清理。潮闸镇额外覆盖基地外景卸载后复用模板、转用实时主光、直接进入下水道的官方组件路径，以及户外晴天资源及同时间雨天资源的选择。诊断用例区分官方目标天气、当前 Volume 混合值、实际主光与雾幕状态。替身模拟 Unity 场景、销毁对象的空值和官方 Volume 写入的属性。官方天气渲染、阴影表现和屏幕亮度需要进游戏核对。
