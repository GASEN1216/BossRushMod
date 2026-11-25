# Boss 地图烽烟圈

2026-11-24，COMPAT。来自许愿台玩家愿望第 8 项「Boss 地图标记」。在 M 地图上给 Boss 先画一圈大致范围，玩家走近、或者长时间没交手时，再落到实时位置。不新增 TypeID、配置键、官方任务或存档字段；标记只在本局内存里。视觉、手感和性能还要实机验收，守卫、编译和离线回归都不能代替。

## 入口与边界

- 跨模块基础设施，按 Hooks 分层放在 `Utilities/BossMapBeacon/`。宿主 [ModBehaviourRuntimeModules](file://ModBehaviourRuntimeModules.cs) 只注册一行；状态都在 [BossMapBeaconRuntimeModule](file://Utilities/BossMapBeacon/BossMapBeaconRuntimeModule.cs) 里，不进 ModBehaviour partial。
- 宿主注册时传入四个来源委托：
  - 竞技场与 Mode D 的开关（`IsActive || modeDRuntime.IsActive`）；
  - 竞技场来源 `CopyMapBeaconBossesTo`：Boss 掉落登记表（`bossSpawnTimes`，普通 Boss 与 Mode D 的 Boss 都经 `RegisterBossRandomLootTracking` 进表），再补上本波登记的内容 Boss（`CurrentBoss` / `CurrentWaveBosses`）。
    冠军之影、蚀日花冠经 `RegisterContentWaveBoss` 只登记为本波 Boss，刻意不挂随机掉落漏斗，不在掉落表里；2026-11-24 F3 首跑（BOSS_MAP_BEACON）发现只读掉落表会漏标这两只，于是改为合并两处。
    `CopyTrackedBossCharactersTo` 本身不改：女巫离场清理会逐个销毁那张表里的角色；
  - Mode E 的开关与 `ModeEAliveEnemies`。
- Mode G 不经宿主，模块直接读 `ModeGRunContext.Current` 的 `IsActive` 和 `CollectTrackedBosses`。
- 不接入的模式：Mode F（血猎追击）、Mode H（百战留痕）、丧尸模式、天空岛、潮闸镇和末班编组站。它们各有自己的指引或目标标记。
- 全程只读现成登记表，不做场景查找（`BossMapBeaconGuard` 禁止 `FindObjectsOfType` 一类调用）。
- 只标与主角敌对的 Boss（`Team.IsEnemy(main.Team, boss.Team)`）。在 Mode E 里，玩家自己所在阵营的 Boss 不标。

## 规则

所有数字都只在纯规则类 [BossMapBeaconRules](file://Utilities/BossMapBeacon/BossMapBeaconRules.cs) 里定义；执行回归 `BossMapBeaconRules` 直接编译这份文件。

| 项 | 值 |
|---|---|
| 半径三档 | 35 / 20 / 10 米 |
| 圆心偏移上限 | 半径 × 15/35（35 米那档为 15 米），在圆盘内均匀取点 |
| 重新落圆心 | 每 8 秒 |
| 收一档 | 安静满 60 秒 |
| 显形 | 主角距离 ≤ 40 米；或收到 10 米那档后又安静满 60 秒 |
| 同屏上限 | 8 个，空位按离主角由近到远补 |
| 显形放大 | 1.3 倍 |
| 光柱 | 1.5 秒，全场同一时刻最多一道 |

- 「安静」从上次收紧算起；主角每打到一只还没显形的 Boss，这只 Boss 的安静计时就从头算。从不交手时，第 60 秒收到 20 米，第 120 秒收到 10 米，第 180 秒显形，回归用整条时间线钉住这一点。
- 计时用 `Time.time`，游戏暂停时不走。

## 运行时

- `OnUpdate` 先读三道模式开关，三道都关时：有残留就清空，然后直接返回。所以不在这些模式里时，每帧只是 O(1) 判断。
- 在模式里时，每 0.5 秒扫一次来源：
  - 本轮扫到的 Boss 打上标记；
  - 没扫到的 Boss，移除它的标记；
  - 空位按上限补齐。
- 每帧 `Tick` 只遍历已有的标记，最多 8 个：
  - Boss 死亡或已销毁的，移除；
  - 已显形的，标记跟着 Boss 走；
  - 未显形的，依次检查显形、收档、到点重落圆心。
- 每只 Boss 对应一个官方 `SimplePointOfInterest`。官方地图只在 `Setup` 时读取 `IsArea`、半径、图标和 `HideIcon`，所以换档和显形都要重新 `Setup`：
  - 烽烟段：区域圈，朱砂色，不画中心图标；
  - 显形段：点标记，换成烽火图标并放大。
- 标签用 Boss preset 的 `nameKey`，M 图上直接显示 Boss 名。nameKey 为空时，回落到注入的 `BossRush_MapBeacon_Boss`。
- 场景参数取自 `MapPointSceneResolver.Resolve()`。

## 订阅与清理

- 唯一 owner 是本模块。
- `Health.OnHurt` 只在建出第一个标记时订阅，用私有布尔防止重复订阅。以下路径都会用命名方法退订：
  - 最后一个标记移除时；
  - `ClearAll`；
  - 换场景；
  - 模块销毁。
- `OnSceneLoaded` 清空所有标记。`OnDestroy` 清空标记，并经 `BossMapBeaconFx.ResetStaticCaches()` 销毁图标贴图。

## 表现

- 实现在 [BossMapBeaconFx](file://Utilities/BossMapBeacon/BossMapBeaconFx.cs)。
- 图标是程序生成的 64×64 贴图：一圈毛笔环，在 -125° 处留一道飞白缺口，环里一簇火苗。按水墨东方的底色调色，不依赖外部资源包。
- 显形光柱由四层叠成，配一声 `BossRushInkSkin.SfxWarDrum` 战鼓：
  - 一圈平铺冲击波；
  - 暗红墨烟（alpha）；
  - 竖直亮芯（additive）；
  - 余烬火星。
- 粒子只用共享的 `BossRushFxKit`，不新建材质或着色器。

## 守卫与回归

- `tests/BossMapBeaconGuard.py` 守以下内容：
  - 规则常量；
  - 不做场景查找；
  - OnUpdate 的门控顺序；
  - 三处来源和敌对过滤；
  - 宿主注册行；
  - 订阅与退订的幂等；
  - 清理路径；
  - 烽烟段和显形段的 Setup 顺序；
  - 光柱节流与战鼓；
  - 编译清单和回归登记。
- 执行回归 `BossMapBeaconRules` 覆盖：半径夹取、偏移圆盘、收档、显形边界、180 秒整条时间线、光柱节流。
- 实机清单为 `GameplayCoverage.json` 的 `M_BOSS_BEACON_01`：
  - 圈是否偏开 Boss；
  - 是否每分钟收一档；
  - 40 米内是否显形，显形时是否有光柱和战鼓；
  - 同屏是否最多 8 个；
  - 模式结束或换场景后是否清空。

玩家正文：[中文](file://WikiContent/zh/mode/mode__overview.md)、[英文](file://WikiContent/en/mode/mode__overview.md) 的「通用规则」一节。
