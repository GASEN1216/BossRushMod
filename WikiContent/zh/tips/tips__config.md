## Mod配置

### 配置入口
- 已支持 ModConfig：
- https://steamcommunity.com/sharedfiles/filedetails/?id=3590674339
- 同时也支持从本地文件读取配置（需重启游戏）：
- `..\Escape from Duckov\Duckov_Data\StreamingAssets\BossRushModConfig.txt`

![BossRush-ModConfig示意](TODO.png)

### 可配置项（默认值）
- `waveIntervalSeconds`：波次间休息时间（2~60 秒，默认 15）
- `enableRandomBossLoot`：Boss 掉落随机化（按 Boss MaxHP 影响掉落格子数/高品质上限；击杀耗时越短会额外提高高品质概率）
- `useInteractBetweenWaves`：波次间是否需要交互才开启下一波
- `lootBoxBlocksBullets`：Boss 掉落箱是否可挡子弹（作为掩体）
- `infiniteHellBossesPerWave`：模式 C 每波 Boss 数量（1~10，默认 3）
- `bossStatMultiplier`：Boss 全局数值倍率（0.1~10，默认 1）
- `modeDEnemiesPerWave`：模式 D 每波敌人数（1~10，默认 3）

### Boss池筛选
- 可自定义 Boss 池，按 **Ctrl+F10** 弹出配置窗口

![BossRush-Boss池配置窗口(Ctrl+F10)](TODO.png)
