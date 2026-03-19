## 配置选项

### 概述
- BossRush Mod 提供多项可配置参数，允许你根据个人偏好调整游戏体验。

### 配置方式
- **配置文件**：`StreamingAssets/BossRushModConfig.txt`（JSON 格式），首次运行自动生成。
- **游戏内设置**：如果游戏支持 ModConfig UI，可在 Mod 设置界面中直接修改。

### 配置项一览

#### waveIntervalSeconds
- 默认值：15
- 范围：2-60
- 波次之间的倒计时秒数

#### enableRandomBossLoot
- 默认值：true
- 范围：true/false
- 是否启用随机 Boss 掉落（关闭则使用原始掉落）

#### useInteractBetweenWaves
- 默认值：false
- 范围：true/false
- 是否改为手动触发下一波（通过路牌互动）

#### lootBoxBlocksBullets
- 默认值：false
- 范围：true/false
- 战利品箱是否阻挡子弹

[tip] 此选项默认关闭。开启后，战利品箱可以充当临时掩体挡住敌人子弹，但同时也会挡住你自己的射击。在长线模式中箱子堆积较多时，开启此选项会显著改变场地战术。

#### infiniteHellBossesPerWave
- 默认值：3
- 范围：1+
- 无间炼狱每波 Boss 数量

#### bossStatMultiplier
- 默认值：1.0
- 范围：0.1+
- Boss 全局属性倍率（影响生命值和伤害）

#### modeDEnemiesPerWave
- 默认值：3
- 范围：1-10
- 白手起家每波敌人数量

#### disabledBosses
- 默认值：[]
- 范围：Boss 名称列表
- 禁用的 Boss 列表（也可通过 Boss 筛选器 UI 设置）

#### bossInfiniteHellFactors
- 默认值：{}
- 范围：Boss:倍率
- 各 Boss 在无间炼狱中的权重倍率

#### enableDragonDash
- 默认值：true
- 范围：true/false
- 是否启用龙裔/龙王套装的冲刺能力

#### achievementHotkey
- 默认值：L
- 范围：任意按键
- 成就面板快捷键

#### useWolfModelForWildHorn
- 默认值：true
- 范围：true/false
- 荒野号角召唤的坐骑是否使用狼模型

### 常用调整建议
- 想要更紧凑的节奏：降低 waveIntervalSeconds（如 5-8）。
- 想要更高难度：提高 bossStatMultiplier（如 1.5-2.0）。
- 想要手动控制节奏：启用 useInteractBetweenWaves。
- 白手起家太简单：提高 modeDEnemiesPerWave（如 5-8）。
- 无间炼狱太拥挤：降低 infiniteHellBossesPerWave（如 1-2）。
