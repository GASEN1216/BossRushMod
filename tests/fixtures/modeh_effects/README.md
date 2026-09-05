# Mode H 效果生命周期回归夹具

对应 CR-2026-09-05-002 / 008 / 009 / 010，兼容分类 COMPAT。

运行：

```powershell
python tests/fixtures/modeh_effects/run.py
python tests/fixtures/modeh_effects/run.py --verify-regressions
```

需要 .NET 8 SDK；C# 以 7.3 语言版本编译。项目和输出生成到
`Build/fix-20260905/modeh-effects-fixture`，不写生产文件、不读取玩家存档、不部署 DLL。
第二条命令将六种旧缺陷分别注入 Build 内的源码副本；每个副本必须编译并运行到对应失败断言，
编译失败或错误早退不能当作反向验证通过。原生产文件全程不变。

生产代码覆盖：

- 完整直接编译 `ModeHInjuryAndScarSystem.cs`、`ModeHCommandAdapters.cs`、
  `ModeHCommandController.cs` 与 `ModeHContentModels.cs`。
- 从当前 `ModeHCombatControl.cs` 逐字提取 `OnFighterEntered` 与 `TryRingBell`，
  测试真实入口的条件刷新、绑定、倍率消费和触发提交顺序。
- 从当前 `Assets/Data/ModeH/Scars.json` 投影效果字段，避免重新手写战痕数值；
  不覆盖生产 JSON parser 本身。

41 条断言覆盖无持有触发拒绝、已有伤病门、整条验证门、触发一次性、接力清理、
5/6/8 秒窗口在 16ms 帧步长下的字段还原、无引擎适配器的自结算期限、范围组合、
首发擂台条件、拍铃预览/成功/资格失败/Apply 拒绝。六个行为反向变异分别退回归属缺失、
到期未还原、全口令倍率、永久倍率、空首发上下文、拍铃后才得到倍率。

`Stubs.cs` 替代 Unity/AI 字段、兼容矩阵、场况采集与无关状态 DTO。兼容矩阵按场景可控，
因此 `blood_rush` 的回归只证明其整条被验证后效果正确，不宣称它目前在游戏中可通过认证。
“无效口令 Effects=null”仅用于注入 Apply 拒绝并核对现有拍铃次数契约，不是生产数据。
本夹具不能证明真实 AI 行为、字段重写节奏、场况采集、UI 或完整赛季流程通过。

结构守卫 `tests/ModeHEffectLifecycleGuard.py` 不依赖 .NET，常规 guard runner 可直接运行；
它同时执行九种内存反向变异，锁住生产入口和效果 owner 的必要连接。
