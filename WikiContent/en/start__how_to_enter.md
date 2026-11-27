## Installation & Activation

### Install

1. Open **Steam Workshop** for Escape from Duckov
2. Search "BossRush" or go to the mod page directly
3. Click **Subscribe**
4. Launch the game. The mod loads automatically

### Confirm It's Working

- **BossRush Ticket** appears at the base merchant
- Press **L** (default key, configurable) → achievement panel opens
- **Dingdang** and **Yu Zhi** visible on normal maps

### If It Didn't Load

BossRush needs the **HarmonyLib** prerequisite (Workshop ID 3589088839). Steam offers to subscribe to it along with BossRush; don't skip it.

If **Continue** on the main menu warns that your mods don't match the save, BossRush most likely didn't start this time. Don't load the save yet:

- Cancel and quit the game. In the mod list, check that BossRush and HarmonyLib are both enabled and move HarmonyLib above BossRush (mods higher up load first), then launch again.
- If you load a save while the mod isn't running, the game can't recognise BossRush gear and drops it at the next save. At base, opening or closing any panel or pressing Esc saves automatically.
- If the mod just started a moment late, BossRush gear that didn't go back on during loading is returned to its original slot from the save record (or to your backpack if it doesn't fit), with a notice. Anything that still can't load goes into storage as-is and comes back the next time you load.

[tip] Before adding or updating mods, copy the whole `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\Saves` folder. If something goes wrong, put the copy back.

Still stuck? Close the game and send the author `Player.log` from `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov`. The line containing `[BossRush] 启动自检` (the startup check) is the most useful one.

### Config

First run generates `BossRushModConfig.txt` in `StreamingAssets/`. Edit it to tweak wave intervals, Boss count, difficulty, etc.

If the game supports ModConfig UI, you can also change settings in-game.
