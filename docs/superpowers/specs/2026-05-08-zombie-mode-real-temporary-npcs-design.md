# ZombieMode Real Temporary NPCs Design

## Goal

Extend `ZombieMode` reward selection with three new low-probability reward options that summon real standing NPCs instead of terminal capsules:

- Goblin
- Nurse
- Awen Courier

These NPCs must:

- spawn only after the player selects the reward
- stand in place and remain invincible
- keep their original interaction structure and service behavior
- use ZombieMode purification points for paid actions instead of global cash
- clean up with the current run without affecting other modes or shared NPC systems

## Scope

### In Scope

- Add three new reward types to the ZombieMode reward pool
- Make their reward weights lower than existing temporary terminal rewards
- Spawn the real prefab-backed NPCs in ZombieMode
- Reuse existing controller, interactable, and service logic for each NPC
- Convert paid interactions to purification-point payment only for ZombieMode temporary real NPCs
- Reuse existing ZombieMode temporary-NPC protection and cleanup paths
- Add static guards for reward wiring, localization, payment routing, and cleanup isolation

### Out of Scope

- Replacing the existing temporary merchant terminal reward
- Replacing the existing temporary nurse terminal reward
- Rewriting NPC affinity, marriage, or story systems
- Making these NPCs persist across runs or scenes
- Changing non-ZombieMode cash economy behavior

## Player-Facing Behavior

### Reward Pool

ZombieMode reward selection gains three new reward cards:

- summon goblin
- summon nurse
- summon Awen

Each new reward uses a lower weight than the existing temporary terminal rewards. Initial target weight is `3`, compared with current temporary merchant / temporary nurse reward weight `10`.

### Spawn Behavior

When selected, the chosen NPC is spawned near the active safe zone using the same general placement intent as the current temporary-NPC arrangement:

- near the safe-zone center when one exists
- otherwise near the player as a fallback
- distributed to avoid stacking multiple temporary NPCs on the same point

Each reward can produce at most one active NPC of its own type per run. If the matching temporary real NPC already exists, that reward must be excluded from future reward rolls for the same run.

### Interaction Behavior

The real summoned NPC keeps its original interaction menu and service flow:

- Goblin keeps its normal goblin interaction and reforge flow
- Nurse keeps its normal nurse interaction and healing flow
- Awen keeps its normal courier interaction group, including paid service flows

The player should not see the old terminal-style standalone ZombieMode service UI for these new rewards.

## Technical Design

### 1. Reward Model Extension

Add three new `ZombieModeRewardType` values for the real NPC rewards:

- `TempGoblinNpc`
- `TempNurseNpc`
- `TempCourierNpc`

Wire them into:

- reward catalog construction
- reward category mapping
- reward display text resolution
- reward application routing
- selection-cap / availability filtering if needed

All three belong to the existing ZombieMode NPC reward category.

### 2. Temporary Real NPC Runtime Tracking

Introduce a dedicated ZombieMode runtime marker for real summoned NPCs. This marker should be separate from the current terminal-only meaning of `ZombieModeTemporaryNpc`, because the new behavior needs stronger identity and payment isolation.

The marker records:

- `runId`
- `npcType`
- `usesPurificationPayment`
- the runtime references needed for cleanup and payment resolution

The marker is attached only to ZombieMode temporary real NPC instances. Shared or normal-mode NPCs must never receive it.

### 3. Spawn Strategy

Do not route these spawns through the normal persistent scene-NPC lifecycle. Instead, create a ZombieMode-only spawn path that reuses the existing prefab loading and component assembly logic while keeping runtime ownership in ZombieMode.

Target per-NPC behavior:

- Goblin: reuse goblin prefab and setup path, then force stationary mode
- Nurse: reuse nurse prefab and setup path, then force stationary mode
- Awen: reuse courier prefab and setup path, then force stationary / non-pathing mode

Required constraints:

- disable wandering / movement after spawn
- preserve talk, interaction, and service components
- attach ZombieMode temporary real NPC marker
- register into ZombieMode run cleanup
- register into ZombieMode temporary-NPC invincibility / threat-clearing system

### 4. Payment Adaptation Layer

Do not replace global economy APIs. Instead, add a narrow adaptation layer that checks whether the current service interaction belongs to a ZombieMode temporary real NPC. If yes, payment must be resolved through ZombieMode purification points. If not, the original cash path stays unchanged.

This adaptation must be explicit and local to each service family.

#### Goblin

Goblin reforge currently depends on money-oriented UI and payment logic. For ZombieMode temporary goblin only:

- slider max value must use available purification points
- affordability checks must compare against purification points
- confirmation payment must spend purification points instead of `EconomyManager.Pay`
- visible labels and summary text must describe purification-point spending rather than money

The underlying reforge result logic stays unchanged. Only the payment source and related UI copy change.

#### Nurse

For ZombieMode temporary nurse only:

- healing service affordability check must use purification points
- payment deduction must use purification points
- insufficient-funds messaging must refer to purification points

Healing effects, menu order, and service costs remain the same.

#### Awen

For ZombieMode temporary Awen only:

- courier fee checks must use purification points
- courier payment must spend purification points
- paid sweep fee checks and payment must use purification points
- storage or other fee-based courier actions must use purification points
- failure / refund / rollback paths must preserve the original service semantics, just swapping the currency backend

Menu structure, loot handling, service states, and animations remain unchanged.

### 5. Isolation Rules

This feature must not affect:

- normal-scene courier / goblin / nurse NPCs
- BossRush non-ZombieMode NPC services
- existing ZombieMode temporary merchant terminal reward
- existing ZombieMode temporary nurse terminal reward
- global cash balance or original `EconomyManager` behavior

All behavior switching must be gated by the ZombieMode temporary real NPC marker, not by scene name alone and not by broad `IsZombieModeActive` checks alone.

## Performance Design

### No New Hot Loops

Do not add new scene-wide per-frame scans. Reuse:

- the existing ZombieMode temporary-NPC tracking list
- the current temporary-NPC protection tick
- existing run-only cleanup registration

### Movement Cost Control

After spawn:

- Goblin movement must be disabled
- Nurse movement must be disabled
- Courier pathing / wandering must be disabled

These NPCs are intended to stand in place, so they should not incur normal roaming or path recalculation overhead.

### Load Timing

Do not preload all three NPCs on mode start. Load or instantiate only when the corresponding reward is selected.

## Cleanup and Lifecycle

Temporary real NPCs must be destroyed when the ZombieMode run cleans up, including:

- successful extraction cleanup
- failed run cleanup
- run reset or forced teardown
- safe-zone-bound temporary NPC cleanup when that lifecycle applies

Cleanup must only target ZombieMode temporary real NPC instances. It must not destroy:

- normal shared NPCs
- marriage-related NPCs
- NPCs spawned by unrelated systems

## Error Handling

If a reward cannot spawn its NPC because prefab loading or setup fails:

- do not leave a partial dummy interactable behind
- do not poison the temporary-NPC list with invalid entries
- log the failure clearly
- grant a purification-point fallback reward rather than silently losing the reward

If a paid interaction cannot adapt to purification points for the temporary real NPC path:

- fail safely without paying cash
- preserve original NPC state cleanup
- show an understandable failure notification

## Localization

Add new ZombieMode localization keys for:

- the three new reward names
- deployment notifications
- any purification-point wording introduced in goblin / nurse / courier adapted UI or prompts

Do not rename or remove the existing terminal reward strings. The new real NPC rewards coexist with the old terminal rewards.

## Testing Plan

### Static Guard Coverage

Add or update tests to verify:

- reward pool contains all three new real NPC rewards
- their weights are lower than existing terminal rewards
- real NPC rewards coexist with terminal rewards
- reward application routes to real-NPC spawn logic
- ZombieMode temporary real NPC payment paths use purification points
- non-marked NPC payment paths still use original money logic
- cleanup and protection still include the new real NPC instances
- localization coverage exists for the new reward and notification text

### Manual Verification

Minimum manual verification:

1. Run `compile_official.bat`
2. Run `test_logic_official.bat`
3. In ZombieMode, roll and select each of the three new rewards
4. Confirm each NPC spawns as a real model, not a capsule terminal
5. Confirm each NPC stands still and is invincible
6. Confirm each NPC keeps its original interaction structure
7. Confirm every paid action spends purification points, not cash
8. Confirm the run ends without leaving the NPCs behind in later scenes or later runs

## Implementation Notes

- Prefer adding a shared helper for ZombieMode temporary real NPC payment checks instead of copying `if` chains into every payment site.
- Prefer minimal integration points in goblin / nurse / courier systems rather than broad system rewrites.
- Preserve the old terminal implementation as-is unless a bug is discovered during integration.
