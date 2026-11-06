# 死亡 / 受伤广播逐订阅者隔离执行回归

2026-11-06，玩家反馈「末日丧尸第 10 波 Boss 不见了，卡关」。官方 `Health.Hurt` 的致死顺序是
`OnDeadEvent` → `Health.OnDead` → `SetActive(false)` → `OnHurtEvent` → `Health.OnHurt`，全程没有 try；
前面任何一个订阅者抛异常（击杀提示类 Mod 不判空攻击者），后面的订阅者都收不到死亡，`SetActive(false)` 也不执行，
但延迟销毁已经启动，尸体照常消失。生产补丁 `Patches/Combat/DeathBroadcastIsolationPatch.cs` 把这几处广播换成逐订阅者隔离调用。
分类 `COMPAT`。

运行：`python tools/run_runtime_regressions.py --filter DeathBroadcastIsolation`。
产物、IL 与哈希写入 `Build/death-broadcast-isolation-fixture/`，不启动游戏、不访问玩家存档、不部署。
需要 Windows、.NET SDK、本机游戏的 Harmony 与 Managed 程序集（`BOSSRUSH_HARMONY_DLL`、`BOSSRUSH_GAME_MANAGED` 可覆盖）。

## Part A：官方方法体 + 真实 Harmony

- **真实生产逻辑**：完整的 `DeathBroadcastIsolationPatch.cs`（两个补丁类与隔离实现），由真实 Harmony 的 class processor 安装；
  官方 `Health.Hurt` 与 `CharacterMainControl.OnDead` 方法体从 `鸭科夫源码/` 逐字提取，`DamageInfo` 字段与构造也取自官方源。
- **宿主替身**（`Stubs.cs`）：Unity 基础类型、`UnityEvent`（按现装 UnityEngine 的成员形态写，`Invoke` 不带 try）、
  `LevelManager`、掉落箱与距离休眠登记等方法体外围依赖。
- 断言：未打补丁时复现「后面的订阅者收不到死亡、SetActive 被跳过」；打补丁后三处广播与掉落广播每个订阅者都收到、
  官方 `SetActive(false)` / 掉落箱 / 距离休眠注销照常执行、异常计数与日志节流、非致死、嵌套死亡、回调内退订、
  调用列表缓存复用与清理、未知 IL 原样返回。

## Part B：现装游戏程序集

- 同一份生产补丁对本机 `Duckov_Data/Managed` 编译，读取现装 `Health.Hurt` 与 `CharacterMainControl.OnDead` 原始 IL：
  三处广播各改写一次、按官方顺序、原地改写（指令数不变）、只剩每击触发的 `OnHurtEvent` 走官方 `UnityEvent.Invoke`；掉落广播改写一次。
- 在现装 `UnityEngine.CoreModule` 上解析 `UnityEventBase.PrepareInvoke` 与 `BaseInvokableCall.Invoke(object[])`，
  并用真实 `UnityEvent<DamageInfo>` 实例验证：官方 `Invoke` 会被抛异常的监听器截断，隔离调用逐个送达。
- 只用托管代码与元数据，不调用 Unity 原生方法（日志节流窗口预先占住）。

## 边界

不代表游戏内验证：补丁实际安装以游戏加载日志为准，第三方 Mod 的真实抛异常场景需要实机或玩家 `Player.log`。
官方游戏更新后先跑本回归：IL 失配时生产补丁原样返回并告警，玩法回到未隔离状态（丧尸、宿命回响、标准竞技场仍有各自的死亡兜底轮询）。
