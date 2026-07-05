# Configuration Options

# Overview
- BossRush Mod provides multiple configurable parameters, allowing you to adjust the gameplay experience to your preferences.

# Configuration Methods
- **Config file**: `StreamingAssets/BossRushModConfig.txt` (JSON format), auto-generated on first run.
- **In-game settings**: If the game supports ModConfig UI, you can modify settings directly in the Mod settings interface.
- When you change BossRush-registered options through ModConfig UI, supported settings now sync immediately to the running Mod and are written back to the local config file.

# Configuration Reference

# waveIntervalSeconds
- Default: 15
- Range: 2-60
- Countdown seconds between waves

# enableRandomBossLoot
- Default: true
- Range: true/false
- Whether to enable BossRush random Boss loot logic (when disabled, BossRush no longer takes over the vanilla Boss drop flow)

# useLegacyBossLootProbabilities
- Default: true
- Range: true/false
- Quality distribution algorithm: `true` uses Legacy Probability Mode (per-quality independent probabilities + Q5+ guarantee), `false` uses Simplified Probability Mode (combined high-quality probability, no guarantee)
- Scope:
  - Standard BossRush: affects the quality distribution of Boss loot crates and keeps the Q5+ guarantee for high-HP Bosses
  - From Scratch / Faction War / Blood Hunt: affects the quality distribution of regular on-death backpack bonus items, backpack refill items, and extra backpack ammo stacks
  - Does not affect drop counts, currently equipped weapons, equipped armor/helmets/accessories, melee weapons, current bullet type, or Blood Hunt bounty / extraction bonus rewards
  - Current versions now apply this scope correctly across Standard BossRush and the corresponding regular loot paths in From Scratch / Faction War / Blood Hunt, rather than only Standard BossRush

# useInteractBetweenWaves
- Default: false
- Range: true/false
- Whether to switch to manually triggering the next wave (via signpost interaction)

# lootBoxBlocksBullets
- Default: false
- Range: true/false
- Whether loot crates block bullets

::: tip
This option is off by default. When enabled, loot crates can serve as temporary cover to block enemy bullets, but they will also block your own shots. In longer modes where crates accumulate, enabling this option significantly changes arena tactics.
:::

# infiniteHellBossesPerWave
- Default: 3
- Range: 1-10
- Number of Bosses per wave in Infinite Hell

# bossStatMultiplier
- Default: 1.0
- Range: 0.1-10
- Global Boss stat multiplier (affects health and damage)

# milestoneRestBonusSeconds
- Default: 30
- Range: 0-120
- Extra rest time added every 5 completed waves; set it to 0 to disable the bonus rest entirely

# modeDEnemiesPerWave
- Default: 3
- Range: 1-10
- Number of enemies per wave in From Scratch

# disabledBosses
- Default: []
- Range: Boss name list
- List of disabled Bosses (can also be set via the Boss Filter UI)

# bossInfiniteHellFactors
- Default: {}
- Range: Boss:multiplier
- Weight multiplier for each Boss in Infinite Hell

# enableDragonDash
- Default: true
- Range: true/false
- Whether to enable the dash ability for Dragon Descendant / Skyburner Dragon Lord sets

# achievementHotkey
- Default: L
- ModConfig dropdown options: L / K / J / H / G / Y / U / O / P / F5 / F6 / F7 / F8
- Config-file storage: integer value of the corresponding `KeyCode`
- Achievement panel hotkey

# useWolfModelForWildHorn
- Default: true
- Range: true/false
- Whether the mount summoned by Wild Horn uses the wolf model

# enableDeathWraithSystem
- Default: true
- Range: true/false
- Whether to enable the Death Wraith system; when disabled, the mod stops recording/spawning wraiths and clears the current saved wraith record

# enableMutators
- Default: true
- Range: true/false
- Whether to enable the per-run mutator system; when disabled, modes no longer roll mutators at run start (Zombie Mode is unaffected by this system either way)

# mutatorCount
- Default: 3
- Range: 1-10
- Number of mutators drawn per run, default 3

# Recommended Adjustments
- Want a faster pace: lower `waveIntervalSeconds` (for example `5-8`).
- Want higher difficulty: increase `bossStatMultiplier` (for example `1.5-2.0`).
- Want manual pacing control: enable `useInteractBetweenWaves`.
- From Scratch too easy: increase `modeDEnemiesPerWave` (for example `5-8`).
- Infinite Hell too crowded: lower `infiniteHellBossesPerWave` (for example `1-2`).
- Do not want the extra rest every 5 waves: set `milestoneRestBonusSeconds` to `0`.
