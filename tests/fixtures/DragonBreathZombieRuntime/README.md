# 龙息对丧尸伤害及旧实例恢复回归

```powershell
python tools/run_runtime_regressions.py --filter DragonBreathZombieRuntime --jobs 1
python tests/fixtures/DragonBreathZombieRuntime/negative_probes.py
```

本夹具是 L2 隔离执行证据，不加载游戏进程、不部署、不访问玩家存档。需要 .NET SDK。`run.py` 每次抽取当前源码、保存来源 SHA-256，并通过中央 runner 构建后执行本次 MSBuild 返回的 `TargetPath`；日志保留执行路径和 DLL SHA-256。

## 真实生产逻辑

- 龙息的实际 `ConfigureWeapon`、属性表、专属 `NeedsRuntimeConfig` / `RestoreRuntimeConfig`、缺项补配、属性设置和字典缓存失效方法。
- `CustomItemRuntimeStateHelper` 的真实配置登记、残缺判定、磨损保存与恢复、重铸恢复调度；实际龙息登记方法与宿主转发、`ItemInitializeRestorePatch.Postfix`、手持变化订阅及已手持物品恢复入口。
- `ReforgeDataPersistence` 的真实 RF_STAT 恢复、已恢复标记、prefab 基准查找与差值恢复方法。本夹具使用 `RF_STAT_Damage=15`，验证 prefab 23 恢复至 38，不另外实现重铸计算。
- 完整官方 `StatCollection.cs` 直接编入，包含真实 `_cachedStatsDictionary` 懒缓存及 Add 不自动失效的行为。官方 `Item.GetStatValue(int)`、`ItemAgent_Gun.DamageFactorToZombie` 和完整 `ShootOneBullet`、`Projectile.UpdateMoveAndCheck` / `AddElementsToDamageInfo`、`DamageReceiver.Hurt`、`Health.Hurt`、`DamageInfo` 构造和元素设置方法从源码原样抽取。没有复制另一套伤害算法来证明结果。

## 覆盖场景

1. 新龙息按实际 int hash 读到中性系数 1；旧完整龙息先预热缺项的官方 hash 缓存，再经真实 Item.Initialize postfix 补配后读到 1。
2. 旧实例分别保留 0、17、99 的耐久与 0.3 的损耗；保留 RF_STAT 恢复后的 Damage 38 与已有运行时属性加值。重复恢复不再重配或重复应用 RF。
3. 已有 0.25、1、2.5 的合法基础系数及运行时加值保持不变。另覆盖预置合法系数、先预热缺 Damage 的 hash 缓存，再完整配置的情况，确保基础枪械属性也刷新至 hash 读取路径。
4. 基础属性残缺时走完整配置后恢复 RF 和磨损；未登记物品不被配置；切图重新订阅时修复已手持龙息，切枪事件修复随后手持的旧龙息。仅缺系数的已手持实例保持非默认 Damage 61。
5. 从官方开火方法到弹道碰撞、DamageReceiver、Health 的整段链路：旧缺项龙息对僵尸直接伤害为 0、对非僵尸为 23；实际恢复后对僵尸为 23；已有系数 2.5 时为 57.5。这里使用无护甲、抗性 1、不暴击的固定靶，数字用于隔离乘数缺失。

## 宿主替身与边界

Unity 对象、场景、资源、音画、inventory、输入和物理查询由替身提供；SphereCast 固定返回一个靶。Unity Object 实现销毁后等于 null、销毁 GameObject 连带销毁组件。枪械基础组件和表现配置替身模拟耐久重置，不执行真实资源查找、槽位、音效、火焰特效、装弹状态机或输入 Update。`Stat` 用基础值加运行时加值模拟属性 modifier；真实缓存由官方 StatCollection 承担。RF_MOD / RF_VAR 分支、重铸清理和其它武器配置器不在本夹具验证范围。

因此本回归证明龙息系数缺失、旧实例实际恢复接线及官方直接伤害链，不证明实际游戏里的开火输入、BR 弹药补给与装填、火焰画面、性能或玩家所说的全部“无法使用”症状。

## 反向验证

`negative_probes.py` 只破坏 `Build/champion-dragonbreath-20261024/dragon-negative-probes/` 下的稀疏副本，每个副本仍跑中央入口，并核对非零退出码和指定断言。共享源码在前后逐文件核对 SHA-256，不在共享工作区临时破坏代码。

- 移除中性系数补项：新实例 hash 断言转红。
- 保留 Add、移除补项后的 cache 失效：新实例 hash 断言转红，能捕获 string 查询已命中但战斗 hash 仍读 0 的假修复。
- 移除完整属性配置后的 cache 失效：已有合法系数情况下的 Damage hash 断言转红。
- 登记退回通用残缺判定：旧完整实例经真实初始化入口仍缺系数，断言转红。
- 仅缺系数也执行全量配置：已手持 Damage 61 被重置，断言转红。

2026-10-24 基线为 42 项通过，以上 5 个探针均在指定断言转红。结果与共享源码哈希保存在该目录的 `results.json`。

## 官方与资源取证（2026-10-24）

实际作者工程 `Assets/Prefabs/dragon/dragon_Gun_Item.prefab`（SHA-256 `4cb52d4c0695e49af6053b223d7fb1bb4fcc745957ed6e772719f4596f92623e`）与当前 `Assets/Equipment/dragon_equipment`（`9a94165c633d92dbe7b6719adfe7a2aab3d5c1b41ce249f31746b4b8bc0d9f1d`）中，龙息 TypeID 500005 的 Stats 只有 BuffChance，没有 DamageFactorToZombie；bundle 中 Item path_id 为 -1258923828365836097、StatCollection path_id 为 -200171630346340987。生产完整属性表此前也未补该项。

现装官方 `resources.assets`（`93c4ab6ad71fdb3bf4a331bbb2ac6bc2f7db7b0f12efe60dd43ea019ab2e543d`）的 CharacterRandomPreset 对象 path_id 76566 至 76570，对应 EnemyPreset_Zombie / Big / Range / SelfDestruction / Snow，nameKey 均为 Cname_Zombie、isZombie 均为 true。通过 UnityPy 与由现装程序集生成的 typetree 读取，标准 MonoBehaviour 头的脚本引用核为 `{m_FileID:1,m_PathID:583}`；isZombie 在各对象内的字节偏移分别为 136、140、140、152、140，原始字节均为 `01 00 00 00`。ZombieModeSpawner 使用 Cname_Zombie preset，这不是仅构造理论僵尸靶。

另经现装 DLL 只读 PE / IL 交叉审查：Core SHA-256 `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`，`CharacterRandomPreset.<CreateCharacterAsync>d__100.MoveNext` 的 IL_0622 读 preset.isZombie，紧邻 IL_0627 写 Health.isZombie；`ItemStatsSystem.dll`（`a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60`）的 StatCollection.Add 仍只有 list.Add 与 ret，无缓存失效，枪属性 getter 仍经静态 hash 调用 GetStatValue。

完整本机资源取证 JSON 和只读解析脚本保存在 `Build/champion-dragonbreath-20261024/official-zombie-asset-evidence.json`、`inspect_official_zombie_asset.py`；它们是本轮忽略的审查产物，不是运行回归的外部依赖。

## 实机待验

交付后由 owner 在已授权的实机验收中，分别以近战、射击开局切到龙息，确认手持 UI 显示龙息、BR 弹药可以装填，按开火后普通僵尸血条立即下降；切回另一把枪再切回来、重新进入丧尸场景后重做。另选一把有磨损和重铸的旧龙息，核对切换前后耐久和重铸数值。按键无响应、弹数不变、弹道命中却无直接掉血、切换后属性变化均为不合格，需记录具体表现。本轮未启动游戏，未取得 L3 证据。
