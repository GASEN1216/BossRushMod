# 末班编组站与潮闸镇世界装配执行回归

直接链接 `LastShiftWorld.cs`、`TidegateWorld.cs` 及两图的实体装配代码。末班编组站用实际的跨线桥引坡、地面高度和两个浅水池的平面范围，验证按脚点高度选面、射线距离和 Water 层触发体。潮闸镇用发布包北桥一处叠层的两个高度，验证桥上桥下分别命中、容许近处台阶、跳过墙体和非行走面。两图都在停用的世界根内预装配，激活后检查复用已烘焙盒、停用子组的替代实体及重复装配。替身提供可控的碰撞查询和场景层级，真实 PhysX 以及入水画面和音效在游戏内验收。

```powershell
python tools/run_runtime_regressions.py --filter LastShiftWorldRuntime
```
