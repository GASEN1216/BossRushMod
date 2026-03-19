## Configuration Options

### Overview
- BossRush Mod provides multiple configurable parameters, allowing you to adjust the gameplay experience to your preferences.

### Configuration Methods
- **Config file**: `StreamingAssets/BossRushModConfig.txt` (JSON format), auto-generated on first run.
- **In-game settings**: If the game supports ModConfig UI, you can modify settings directly in the Mod settings interface.

### Configuration Reference

#### waveIntervalSeconds
- Default: 15
- Range: 2-60
- Countdown seconds between waves

#### enableRandomBossLoot
- Default: true
- Range: true/false
- Whether to enable random Boss loot (disabled = use original drops)

#### useInteractBetweenWaves
- Default: false
- Range: true/false
- Whether to switch to manually triggering the next wave (via signpost interaction)

#### lootBoxBlocksBullets
- Default: false
- Range: true/false
- Whether loot crates block bullets

[tip] This option is off by default. When enabled, loot crates can serve as temporary cover to block enemy bullets, but they will also block your own shots. In longer modes where crates accumulate, enabling this option significantly changes arena tactics.

#### infiniteHellBossesPerWave
- Default: 3
- Range: 1+
- Number of Bosses per wave in Infinite Hell

#### bossStatMultiplier
- Default: 1.0
- Range: 0.1+
- Global Boss stat multiplier (affects health and damage)

#### modeDEnemiesPerWave
- Default: 3
- Range: 1-10
- Number of enemies per wave in From Scratch

#### disabledBosses
- Default: []
- Range: Boss name list
- List of disabled Bosses (can also be set via the Boss Filter UI)

#### bossInfiniteHellFactors
- Default: {}
- Range: Boss:multiplier
- Weight multiplier for each Boss in Infinite Hell

#### enableDragonDash
- Default: true
- Range: true/false
- Whether to enable the dash ability for Dragon Descendant/Skyburner Dragon Lord sets

#### achievementHotkey
- Default: L
- Range: any key
- Achievement panel hotkey

#### useWolfModelForWildHorn
- Default: true
- Range: true/false
- Whether the mount summoned by Wild Horn uses the wolf model

### Recommended Adjustments
- Want a faster pace: lower waveIntervalSeconds (e.g., 5-8).
- Want higher difficulty: increase bossStatMultiplier (e.g., 1.5-2.0).
- Want manual pacing control: enable useInteractBetweenWaves.
- From Scratch too easy: increase modeDEnemiesPerWave (e.g., 5-8).
- Infinite Hell too crowded: lower infiniteHellBossesPerWave (e.g., 1-2).
