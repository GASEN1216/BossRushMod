# 配置选项

# 概述
- BossRush Mod 提供多项可配置参数，允许你根据个人偏好调整游戏体验。

# 配置方式
- **配置文件**：`StreamingAssets/BossRushModConfig.txt`（JSON 格式），首次运行自动生成。
- **游戏内设置**：如果游戏支持 ModConfig UI，可在 Mod 设置界面中直接修改。
- 通过 ModConfig UI 修改由 BossRush 注册的配置项时，当前支持的选项会即时同步到运行中的 Mod，并写回本地配置文件。

# 配置项一览

# waveIntervalSeconds
- 默认值：15
- 范围：2-60
- 波次之间的倒计时秒数

# enableRandomBossLoot
- 默认值：true
- 范围：true/false
- 是否启用 BossRush 随机 Boss 掉落逻辑（关闭后 BossRush 不再接手原版 Boss 掉落逻辑）

# useLegacyBossLootProbabilities
- 默认值：true
- 范围：true/false
- 战利品品质分布算法：`true` 使用原版概率模式（各品质独立概率 + Q5+ 保底），`false` 使用简化概率模式（高品质总概率 + 无保底）
- 影响范围：
  - 标准 BossRush：影响 Boss 战利品箱的品质分布，并保留高血量 Boss 的 Q5+ 保底
  - 白手起家 / 划地为营 / 血猎追击：影响敌人常规死亡后背包内额外物品、背包随机补物、背包额外弹药堆的品质分布
  - 不影响上述模式的掉落数量，也不影响已装备武器、已装备护甲/头盔/配件、近战武器、当前子弹类型，以及血猎追击的悬赏额外奖励/撤离奖励
  - 当前版本中，这个作用范围已正确覆盖标准 BossRush 与白手起家 / 划地为营 / 血猎追击，不再只限于标准 BossRush

# useInteractBetweenWaves
- 默认值：false
- 范围：true/false
- 是否改为手动触发下一波（通过路牌互动）

# lootBoxBlocksBullets
- 默认值：false
- 范围：true/false
- 战利品箱是否阻挡子弹

::: tip
此选项默认关闭。开启后，战利品箱可以充当临时掩体挡住敌人子弹，但同时也会挡住你自己的射击。在长线模式中箱子堆积较多时，开启此选项会显著改变场地战术。
:::

# infiniteHellBossesPerWave
- 默认值：3
- 范围：1-10
- 无间炼狱每波 Boss 数量

# bossStatMultiplier
- 默认值：1.0
- 范围：0.1-10
- Boss 全局属性倍率（影响生命值和伤害）

# milestoneRestBonusSeconds
- 默认值：30
- 范围：0-120
- 每完成 5 波额外增加的休息时间；设为 0 表示关闭这段额外休息

# modeDEnemiesPerWave
- 默认值：3
- 范围：1-10
- 白手起家每波敌人数量

# disabledBosses
- 默认值：[]
- 范围：Boss 名称列表
- 禁用的 Boss 列表（也可通过 Boss 筛选器 UI 设置）

# bossInfiniteHellFactors
- 默认值：{}
- 范围：Boss:倍率
- 各 Boss 在无间炼狱中的权重倍率

# enableDragonDash
- 默认值：true
- 范围：true/false
- 是否启用龙裔/龙王套装的冲刺能力

# achievementHotkey
- 默认值：L
- ModConfig 下拉选项：L / K / J / H / G / Y / U / O / P / F5 / F6 / F7 / F8
- 配置文件存储：`KeyCode` 对应的整数值
- 成就面板快捷键

# useWolfModelForWildHorn
- 默认值：true
- 范围：true/false
- 荒野号角召唤的坐骑是否使用狼模型

# enableDeathWraithSystem
- 默认值：true
- 范围：true/false
- 是否启用死亡亡魂系统；关闭后不会再记录或生成亡魂，并会清理当前存档里的亡魂记录

# enableMutators
- 默认值：true
- 范围：true/false
- 是否启用每局变异词条系统；关闭后各模式开局不再抽取变异词条（末日丧尸模式本就不受此系统影响）

# mutatorCount
- 默认值：2
- 范围：1-3
- 每局抽取的变异词条数量

# 常用调整建议
- 想要更紧凑的节奏：降低 `waveIntervalSeconds`（如 `5-8`）。
- 想要更高难度：提高 `bossStatMultiplier`（如 `1.5-2.0`）。
- 想要手动控制节奏：启用 `useInteractBetweenWaves`。
- 白手起家太简单：提高 `modeDEnemiesPerWave`（如 `5-8`）。
- 无间炼狱太拥挤：降低 `infiniteHellBossesPerWave`（如 `1-2`）。
- 不想每 5 波多歇 30 秒：把 `milestoneRestBonusSeconds` 调到 `0`。
