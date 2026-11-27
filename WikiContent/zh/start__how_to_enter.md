## 安装与启用

### 怎么装？

装好就能玩，一共四步：

- 1. 打开 Steam → Escape from Duckov 的创意工坊页面
- 2. 搜 "BossRush"（或者直接点 Mod 页面链接）
- 3. 一键 **订阅**
- 4. 启动游戏，Mod 自动加载，完事

### 怎么知道装上了？

进游戏后看三个地方，有一个对上就说明没问题：

- 基地商人那里多了 **BossRush 船票** 之类的新货
- 按 **L 键**（默认键，可在设置里改）弹出来一个成就面板
- 普通地图场景里蹦出来一个小绿人 **叮当** 和一位白衣天使 **羽织**

都看不到？那大概是忘点订阅了，回去再看一眼创意工坊。

### 没加载出来怎么办？

BossRush 要配前置 **HarmonyLib**（创意工坊编号 3589088839）。订阅时 Steam 会提示一起订阅，别跳过。

主菜单点「继续游戏」时，如果弹出 Mod 和存档记录对不上的警告，多半是 BossRush 这次没跑起来。先别进档：

- 取消，退出游戏。在 Mod 列表里确认 BossRush 和 HarmonyLib 都开着，并把 HarmonyLib 调到 BossRush 上面（越靠上越先加载），再启动一次。
- Mod 没跑就进了档，游戏认不出 BossRush 的装备，下一次存档就会把它们丢掉。在基地里开关界面、按 Esc 都会自动存档。
- 要是 Mod 只是晚了一步才加载上，读档时没穿上的 BossRush 装备会按存档记录放回原来的栏位（放不上就进背包），并弹一条提示；实在读不出来的会原样放进仓库，下次进档恢复。

[tip] 换 Mod 或更新 Mod 之前，先把 `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\Saves` 整个文件夹复制一份，出了问题换回来就行。

还是不行，就关掉游戏，把 `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov` 里的 `Player.log` 发给作者，里面带 `[BossRush] 启动自检` 的那一行最有用。

### 配置文件在哪？

第一次启动后，Mod 会自动在 `StreamingAssets/` 目录下生成 `BossRushModConfig.txt`。想微调参数的话直接编辑这个文件就行，详见配置选项。

如果游戏支持 ModConfig UI，那更省事——直接在游戏内的 Mod 设置界面里改，所见即所得。
