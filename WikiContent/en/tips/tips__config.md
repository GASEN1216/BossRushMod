## Mod Configuration

### Config Entry
- ModConfig is supported:
- https://steamcommunity.com/sharedfiles/filedetails/?id=3590674339
- Local file config is also supported (requires game restart):
- `..\Escape from Duckov\Duckov_Data\StreamingAssets\BossRushModConfig.txt`

![BossRush-ModConfigExample](TODO.png)

### Config Keys (Defaults)
- `waveIntervalSeconds`: Wave interval (2~60 seconds, default 15)
- `enableRandomBossLoot`: Boss loot randomization (loot slots/high-quality cap scale with Boss MaxHP; faster kills further increase high-quality chance)
- `useInteractBetweenWaves`: Require interaction between waves to start the next wave
- `lootBoxBlocksBullets`: Whether boss loot boxes can block bullets (act as cover)
- `infiniteHellBossesPerWave`: Mode C bosses per wave (1~10, default 3)
- `bossStatMultiplier`: Global boss stats multiplier (0.1~10, default 1)
- `modeDEnemiesPerWave`: Mode D enemies per wave (1~10, default 3)

### Boss Pool Filter
- Custom boss pool is supported, press **Ctrl+F10** to open the config window

![BossRush-BossPoolConfigWindow(Ctrl+F10)](TODO.png)
