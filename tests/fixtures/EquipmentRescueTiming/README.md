# EquipmentRescueTiming

2026-11-26 外部复审（第二轮）指出读档装备抢救的两个时序缺口，本夹具执行生产代码复现并守住修复：

1. 迟到激活漏救：官方 `LevelManager.CreateMainCharacterAsync` 先 await 恢复角色物品树、再 await 生成角色、最后才写 `mainCharacter`（2026-11-26 用 ikdasm 核对 IL）。本 Mod 在这几帧里激活时，读档失败发生在补丁装上之前，没有 `NoteUnresolvedLoad` 记号；旧判定「主角已存在才算迟到」又判成不迟到，抢救一次都不跑。现在「激活时已身处关卡场景」就算迟到。
2. 重建期间换档：`ItemTreeData.InstantiateAsync` 要等几帧，期间换了存档槽、关卡或主角，旧代码仍把重建出的装备塞进当前角色的背包或仓库。现在重建完成后核对存档槽、关卡、主角、角色物品与原父物品，变了就销毁重建品、整轮作废、不动旧孤儿。

真实生产逻辑：`Integration/BossRushEquipmentRescue.cs`、`Common/Lifecycle/BossRushStartupGuard.cs` 原样链接编译（`BeginAwake` 的迟到判定、`CompleteAwake` 接线、证据门槛、重建、身份核对、插回原槽）。

宿主替身（`Stubs.cs`）：Unity 对象 fake-null、`FindObjectsByType`（按 `FindObjectsInactive` 过滤）、可控帧的 UniTask（`NextFrame` 挂起到测试调 `StepFrame`）、`LevelManager` / `CharacterMainControl` / `LevelConfig`、存档读取、物品树与装备槽（空壳插不进装备槽）、`ItemUtilities` 发往背包 / 仓库的计数。替身把空壳孤儿设为未激活，用来证明扫描包含未激活对象；官方实际空壳是否激活不影响结论。0Harmony 预加载在这里恒为 `missing`（替身没有游戏目录），它的行为由 `tests/HostStartupIsolationGuard.py` 守结构，并在 2026-11-26 用 Unity 2022.3 的 Mono 对真实 `0Harmony.dll` 跑过六种目录布局。

用例：激活发生在生成主角期间（必须补回原槽、销毁孤儿、只提示一次）；主菜单激活后正常读档（不读存档、不扫场景、不重建）；重建期间换槽并换关卡（重建品销毁，新档角色、背包、仓库都不收到，孤儿不动）；只换存档槽（同样作废）；什么都没变（照常补回）。

反向验证（2026-11-26）：把迟到判定改回「主角已存在」、去掉身份核对、去掉存档槽一项、孤儿扫描改成不含未激活对象，四种改法都红在预期断言上，生产文件按字节还原。

通过 `python tools/run_runtime_regressions.py --filter EquipmentRescueTiming` 执行，不启动游戏、不访问玩家存档。抢救在真实游戏里的效果仍要 L3（`CODE_REVIEW_FINDINGS.md` 2026-11-26 节 `M_EQUIP_RESCUE_01`）。
