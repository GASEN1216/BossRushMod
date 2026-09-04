# 2026-05-14 ModBehaviour.Instance Classification

## Baseline

- Command: `rg -n "ModBehaviour\\.Instance" --glob "*.cs"`
- Raw matches: 404
- Current event-bus pilot: achievement popup notification only.
- Guard evidence: `BossRushEventBusLifecycleGuard.py` PASS; `LongTermGoalNonGoalGuard.py` still blocks broad `EventBus`, `IGameWorldProbe`, and `IBossRushEventSubscriber` abstractions.

## Classification Policy

| Category | Meaning | Current action |
|---|---|---|
| Keep: Unity owner | Needs the live mod instance for coroutine start/stop, `gameObject`, scene state, or Unity object ownership. | Retain until a local owner is introduced. |
| Keep: gameplay state | Reads or mutates active mode state, run id, spawn points, boss flow, temporary NPC economy, or core combat lifecycle. | Retain; do not route through event bus in this pass. |
| Candidate: notification | `ShowMessage`, `ShowBigBanner`, `PlaySoundEffect`, small UI refresh signals. | Migrate only when low-risk and guarded. Achievement notification already moved to `BossRushEventBus`. |
| Candidate: service query | NPC/shop/reforge/courier checks that ask whether a transform belongs to ZombieMode temporary service state. | Keep for now because the service owner is still `ModBehaviour`; document for later local interface extraction. |
| Debug/manual | F3, teleport monitor, manual debug actions. | Retain. |

## Current File Grouping

| Area | Matches | Classification | Evidence / reason |
|---|---:|---|---|
| `Integration/` | 263 | mixed: Unity owner, gameplay state, temporary NPC service query, notification | Most usages are NPC/reward/reforge/courier/DragonKing/PhantomWitch wiring. They touch active run state, temporary NPC currency, coroutine owners, or audio/banner notifications. 2026-08-28 +5：日报报箱交互（3）、战绩采集门控（1）、日报面板横幅（1），三处都属 Keep 类别（交互回调宿主、开关查询、通知）。2026-09-04 +4：常驻捏脸 NPC 交互体（`PermanentDuckNpcInteractable`）读配偶跟随/离婚/回家三个选项的可见性，外加一次 null 判空。它是 MonoBehaviour、手上没有 owner 引用，`ModBehaviour.Instance` 是这类交互体的既定取法（与 Interactables/ 同款），且调用前已判空，属 Keep 类别。 |
| `ZombieMode/` | 38 | gameplay state and runtime owner | Runtime components ask for `ZombieModeCurrentRunId`, pause state, reward UI, temporary NPC service opening, and projectile/reward effects. These stay direct to avoid changing mode behavior. |
| `Interactables/` | 23 | gameplay command and UI notification | BossRush sign, difficulty selection, lootbox return/clear actions call active mode commands. The Mode G entry path reuses one captured host instead of repeatedly resolving the singleton; the remaining calls are player-facing commands and should not be event-bus migrated without smoke. |
| `ModeE/` | 26 | gameplay state / cached instance | Harmony patches and Mode E merchant/UI use the current active mode state and cached instance; guarded by Mode E/F no-gameplay-throttle and parity tests. |
| `ModeF/` | 6 | gameplay state / UI | Mode F bounty radar/merchant/transponder paths use active Mode F session state. |
| `Campaign/` | 15 | notification + gameplay state | 2026-08-30 新增。鸭王征程用它做三件事：玩家可见通知（`ShowMessage`：接约/交付/线索到手）、开关与波次查询（采集器与桥读活动模式状态）、以及公告板面板的宿主。2026-09-03 +1：终章冠军独白拿不到对话 actor 时的飘字兜底（`PlayFinalBossPrologueAsync`），与既有交付剧情的兜底同款。全部属 Keep 类别——契约状态机本来就长在 `ModBehaviour` 的 partial 上（`CampaignModeBridge`），走事件总线反而要把私有模式状态再导出一遍。 |
| `Audio/` | 9 | candidate notification + Unity owner | Audio manager uses `ModBehaviour.Instance` as the component host and sound playback bridge. It is a later candidate for a narrow audio service, not a broad event bus. 2026-08-30 +1：`BossBgmCoordinator` 经它播 stinger（复用既有 `PlaySoundEffect`，不另起音频通道）。 |
| `Patches/` | 8 | patch entrypoint / Unity owner | Harmony patches need the current mod singleton to route base-game callbacks into the mod. `MagicBlendInitializationOrderPatch` additionally uses it as a coroutine owner while waiting for the official `MagicBlending.Start()` initialization, then replays the same state entry. |
| `MapSelection/` | 3 | gameplay command | Map selection must call active mod entry/exit state. |
| `ModeG/` | 4 | gameplay state / Unity owner | Mode G uses the live mod instance for entry, presentation and managed runtime ownership; these calls stay direct to preserve the run transaction boundary. |
| `ModeH/` | 1 |
| `RandomEvents/` | 5 | gameplay command / Unity owner | Mode H 场内交互只在一个解析器里取活动 mod 实例，其余路径复用捕获的 host，保持入口事务边界。 |
| `ModeD`, `DebugAndTools` | 3 | debug/manual or mode command | Retained. |

## Already Migrated

- `Achievement/BossRushAchievementManager.cs` publishes `BossRushAchievementUnlockedEvent`.
- `Achievement/AchievementRuntimeHooks.cs` subscribes/unsubscribes and owns `SteamAchievementPopup.Show`.
- `Common/Events/BossRushEventBus.cs` is reset in `AlwaysOnRuntimeHooks` and guarded by `BossRushEventBusLifecycleGuard.py`.

## Findings

- The 399 raw matches are classified; broad replacement remains out of scope because it would touch combat, reward, UI, service, and patch entrypoints at once.
- The low-risk notification pilot is complete for achievements. Other notification candidates are documented but intentionally not migrated in this pass because the user required no player-visible behavior changes.
- Any future migration should be one narrow event at a time, with a dedicated lifecycle guard and runtime smoke for the affected workflow.

## Guard Coverage

- `ModBehaviourInstanceClassificationGuard.py` locks the current raw count, file grouping, classification policy, migrated achievement notification, and explicit non-migration reasons.
- `BossRushEventBusLifecycleGuard.py` locks the controlled achievement notification pilot and subscriber cleanup.
- `LongTermGoalNonGoalGuard.py` continues to block broad generic abstractions such as `EventBus`, `IGameWorldProbe`, and `IBossRushEventSubscriber`.

## Current Completion Status

Classification is complete for the current raw count, and Batch Final-5 is source-side complete under the report's "migrate low-risk notification or document the retention reason" criteria. Broad decoupling remains a future long-term goal, not a completion gate for this pass, because the remaining direct singleton calls are gameplay state, Unity-owner, service-query, patch-entrypoint, debug/manual, or smoke-required notification paths.
