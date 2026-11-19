# 数据文件、音频与 Mod 根目录的统一入口

> 2026-11-19 统一。代码、守卫与执行回归是事实源，本页帮人定位。同日更早的两批统一见
> [物品图标统一入口](../装备与物品系统/物品图标统一入口.md)（按路径取图 `ModSprites`）与
> [AssetBundle 管理系统](../装备与物品系统/装备框架与工厂系统/AssetBundle%20管理系统.md)（`ResourceBundleLoader`）。

<cite>
**本文引用的文件**
- [DebugAndTools.cs](file://DebugAndTools/DebugAndTools.cs)
- [BossRushOfficialAudio.cs](file://Audio/BossRushOfficialAudio.cs)
- [BossRushAudioRuntimeService.cs](file://Audio/BossRushAudioRuntimeService.cs)
- [BossRushAudioManager.cs](file://Audio/BossRushAudioManager.cs)
- [JsonDataRegistry.cs](file://Common/Data/JsonDataRegistry.cs)
- [BossRushJsonValue.cs](file://Common/Data/BossRushJsonValue.cs)
- [SimpleJsonHelper.cs](file://Utilities/SimpleJsonHelper.cs)
- [MapSpawnPointRegistry.cs](file://Common/MapConfig/MapSpawnPointRegistry.cs)
- [SkyIslandPatrolRules.cs](file://SkyIsland/SkyIslandPatrolRules.cs)
- [SkyIslandPatrols.cs](file://SkyIsland/SkyIslandPatrols.cs)
- [SetBonusVisuals.cs](file://Integration/Bonus/SetBonusVisuals.cs)
- [DragonKingConfig.cs](file://Integration/DragonKing/DragonKingConfig.cs)
- [DragonKingAbilityController_SpecialAttacks.cs](file://Integration/DragonKing/DragonKingAbilityController_SpecialAttacks.cs)
- [WishFountainConfigAndValidation.cs](file://Integration/WishFountain/WishFountainConfigAndValidation.cs)
- [WishFountainFetchPipeline.cs](file://Integration/WishFountain/WishFountainFetchPipeline.cs)
- [ModPathAndAudioGuard.py](file://tests/ModPathAndAudioGuard.py)
- [MapSpawnRegistryPropertyTest.py](file://tests/MapSpawnRegistryPropertyTest.py)
- [AudioRuntime 执行回归](file://tests/fixtures/AudioRuntime/README.md)
- [AuditCoreParsing 执行回归](file://tests/fixtures/AuditCoreParsing/README.md)
</cite>

## 一句话

Mod 根目录只经 `ModBehaviour.GetModPath()` 取；调官方音频只经 `BossRushOfficialAudio`；`Assets/Data` 下的数据表只经 `JsonDataRegistry.TryReadDataFile` 读、嵌套 JSON 只用 `BossRushJsonParser` 解析。`tests/ModPathAndAudioGuard.py` 钉住这三条与本轮删掉的死代码。

## Mod 根目录

- `GetModPath()`（`DebugAndTools/DebugAndTools.cs`）：先取程序集所在目录、`Directory.Exists` 通过就返回；不行再读官方 `ModBehaviour.info.path`；都不行返回 null，不抛。官方 `ModManager` 用 `Assembly.LoadFrom` 加载 Mod，程序集位置总有值，所以正常运行时它和从前各处自己拼的 `Path.GetDirectoryName(typeof(X).Assembly.Location)` 是同一个目录。
- 建筑（婚礼、许愿台、遗种巢、日报箱）、生日蛋糕、Wiki 书与 `WikiContentManager`、动态物品注册、图片查看器、NPC 资源包、`ItemFactory` / `EquipmentFactory`、龙王 / 龙裔、竞技场原型都改调它；返回 null 时各处原来的处理照旧（`string.IsNullOrEmpty` 早退，或 `Path.Combine(null, …)` 抛出后被原有 `try` 接住）。
- 静态字段初始化器里不取 Mod 根目录：取不到时会抛 `TypeInitializationException`，把整个类型永久毒化。套装音效 `SetBonusSfx` 与龙王的 13 个 `DragonKingConfig.Sound_*` 改成用时才拼的属性（目录取到一次就记住），取不到返回 null，`PlaySoundEffect` 静默跳过。
- 守卫白名单只有两处：`GetModPath` 自身；`ModeHCanonicalDigest.TryComputeAssemblyFileDigest`（对已加载的程序集文件本身算摘要，不是取 Mod 根目录）。

## 官方音频

正式构建不引用 FMOD 程序集：官方 `AudioManager.Post` / `PostCustomSFX` 返回 `EventInstance?`，`AudioObject.StopAll` 是 internal 且参数是 `STOP_MODE`，只能反射。`Audio/BossRushOfficialAudio.cs` 是全 Mod 唯一的这类反射：

| 入口 | 官方成员 | 约定 |
| --- | --- | --- |
| `PostCustomSfx` / `TryPostCustomSfx(path, target, loop, out instance, out error)` | `AudioManager.PostCustomSFX(string, GameObject, bool)` | 返回装箱的 `EventInstance` 或 null |
| `Post(eventName, target)` / `TryPost` | `AudioManager.Post(string, GameObject)` | 同上 |
| `Post(eventName)` | `AudioManager.Post(string)` | UI 音效，不挂发声体；参数数组复用 |
| `TryStopAll(audio, out error)` | `AudioObject.StopAll(STOP_MODE.IMMEDIATE)` | `ReferenceEquals` 判空：发声体已被 Unity 销毁也停得掉 |
| `TrySetEventVolume` / `TrySetEventPitch` / `TryStopEvent` | `EventInstance.setVolume` / `setPitch` / `stop(ALLOWFADEOUT)` | 按句柄类型解析一次 |

- 首次用到时解析一次并长期持有（官方程序集元数据，不随场景、存档或 Mod 重新启用变化，所以没有 `ResetStaticCaches`）；解析放在禁止内联的方法里，官方类型加载失败也只当入口缺失。
- 一律不抛：`Try*` 返回 false 时，`error` 为 null 表示入口缺失，非 null 就是 `MethodInfo.Invoke` 抛出的那个异常。调用方据此保持原来的告警文案与降级时机：云蚋嗡声出错后不再重试、潮闸镇 / 末班编组站的 `Debug.LogWarning` 只记一次、天空岛环境音的停止告警等。
- 调用点：音频运行服务（`PlaySoundEffect`、ngm）、天空岛环境音 / 云蚋 / 头目招式音、潮闸镇、末班编组站、成就弹窗、龙裔（自定义音效与开枪音）、婚礼过场（留住实例压音量、淡出停止）、共享 UI 按钮音、集成层 UI 反馈音、星阙法杖、空仓地雷、快递员与寄存服务的鸭叫、装备技能动作。
- 龙王武器射击音（2026-11-19 owner 拍板修好）：`DragonKingAbilityController_SpecialAttacks.PlayWeaponShootSound` 原来用 void 委托绑官方 `Post`，绑不上它 `EventInstance?` 的返回值，`CreateDelegate(..., false)` 返回 null，这条音从未响过。现在改调 `BossRushOfficialAudio.Post`，事件与官方 `ItemAgent_Gun.PostShootSound` 相同（`SFX/Combat/Gun/Shoot/<shootKey>`），挂在龙皇身上；原有的每把枪 0.05 秒与跨龙皇 0.015 秒节流不变。守卫已没有例外，旧委托与解析标志不得再出现。
- 删除：音频服务里从未绑上的 void 委托与每次重新 `GetMethod` 的 fallback；`BossRushAudioManager` 没有调用方的单参 `PlayBossBGM(string)` / `StopBossBGM(string)` 与 `RegisterNPCInteractSFXHandler` / `UnregisterNPCInteractSFXHandler`；成就弹窗不存在的 `default.mp3` 兜底（只部署了 `default.wav`）。BGM 协调器 `BossBgmCoordinator` 的 `PlayCustomBGM` / `StopBGM` 反射与租约逻辑不在本轮范围。

## 数据文件与 JSON

- `JsonDataRegistry.TryReadDataFile(subDirectory, fileName)` 读 `Assets/Data/...`；缺文件、读失败按文件名去重记 `CriticalLog`。
- 天空岛巡逻表：`SkyIslandPatrolRules` 是纯规则，不读文件、不依赖 Unity 或宿主；`SkyIslandPatrols` 入图时经 Registry 读 `Patrols.json`，把文本交给 `SkyIslandPatrolRules.FromText`。缺文件、出错、校验失败都回退 `CreateFallback()`（与从前的 `Load(modPath)` 相同），差别只在缺文件时 Registry 多记一条 CriticalLog。`SkyIslandPatrols.DataSource` 仍在：F3 巡逻验收 `ValidateRuntime` 的 metrics 里有 `source=JSON / Fallback`。
- `MapSpawnPointRegistry`：整份解析成树，全部字段从顶层键取。字符串字段缺失 / null / 非字符串为 `""`（`previewImageName` 为 null），`sortOrder` 缺省 `int.MaxValue`、`beaconIndex` 缺省 0。改动前用新旧两份实现对 `Assets/SpawnPoints/*.json` 9 个文件逐字段比对（162 个字段完全一致）；只在实际文件里不出现的写法上不同：旧的原文本搜键会命中嵌套同名键、不解 `\uXXXX` / `\/`、把 `10.7` 截成 10，新的按标准 JSON 读（取顶层键、解转义、四舍五入）。从未被调用的导出器 `ExportFromHardcoded` / `SerializeToJson` 与追加 JSON 的辅助函数删除。
- `SimpleJsonHelper` 只剩扁平写出（`Append*` / `EscapeString`）与许愿台飞书接口响应的扁平取值（`ExtractString` / `ExtractInt` / `UnescapeString` / `ForEachObject`）；`ExtractLong` / `ExtractFloat` / `ExtractBool` / `FindArrayBounds` 删除。
- 许愿台飞书接口的解析没有迁到 `BossRushJsonParser`：旧扫描不解 `\uXXXX` / `\/`、按首次出现找键、把富文本数组里所有 `text` 拼起来，树解析在带这类转义的代表性返回上会给出不同（更正确）的弹幕文字，证明不了「新旧完全一致」。
- 许愿台上报的版本号只取 `WikiContent` 下最新的 `changelog__v*.md` 文件名，取不到为 `unknown`。读程序集版本特性的分支删掉了：编译清单不写任何 `Assembly*Version` 特性，构建出的 `BossRush.dll` 版本恒为 0.0.0.0（2026-11-19 对构建产物核对过）。
- `DailyReportLayoutTable.LoadedFromJson` 只写不读，删除（版面表回退仍记 CriticalLog）。`CodexOfficialBossRegistry.LoadedFromJson` 有夹具在读，保留；`ModeHContentCatalog.UsedOddsFallback` 被 `ModeHOddsGuard` 当作「记录了是否用 fallback」钉住，未删，待 owner 决定（删并同步守卫，或接进诊断页）。

## 守卫与回归

- `tests/ModPathAndAudioGuard.py`：上述三条入口规则、各调用点改调统一入口、统一入口不抛 / 只解析一次 / 按 `ReferenceEquals` 停止、巡逻规则不读文件、死代码不回来（含夹具）。2026-11-19 在副本上做了 19 条反向探针，全部转红在预期断言、按字节还原。
- 执行回归：`AudioRuntime`（服务 + 统一入口，三种官方签名形态）、`SkyIslandInteraction`（云蚋停声先于发声体销毁）、`AuditCoreParsing`（每个实际刷新点文件的标量字段与 Python json 独立读出的值逐个比对）。
- 证据级别 L2：实际音量、听感、FMOD 句柄行为与 3D 定位仍需实机。
