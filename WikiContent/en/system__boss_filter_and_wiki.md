## Boss Filter

### Overview
- The Boss Filter allows you to customize the Boss pool by disabling Bosses you don't want to encounter or adjusting the appearance weight of specific Bosses in Infinite Hell.
- Disabled entries affect the Boss pools used by **Standard BossRush, Infinite Hell, From Scratch, Faction War, Blood Hunt and Fate Echo**, plus the pool the "Uninvited Guest" random event draws its intruder from; Infinite Hell additionally supports its own per-Boss weight multipliers.
- **Two exceptions**: the Black Market Duck Cup runs its own fighter/opposition roster, and Zombie Mode uses its own zombies. Neither reads this filter.
- The Duck King Codex and the PetNest bloodline roster follow the same pool - disable a Boss and you can no longer fight it, but **entries you already collected do not disappear**.
- The filter follows the current Boss roster; fodder units spawned mid-run by Faction War / Blood Hunt never make it onto that list.

### How to Open
- Press **Ctrl+F10** to open the Boss Filter panel.

### Features

#### Disable Bosses
- Uncheck a Boss in the panel and it will no longer appear in the Boss pools listed above.
- Use cases:
  - Disable all other Bosses when you want to practice against a specific one
  - Temporarily exclude a Boss that's too difficult or annoying
  - Narrow down the Boss pool to improve efficiency when farming a specific Boss's drops

#### Infinite Hell Weight
- You can set an individual appearance weight multiplier for each Boss in Infinite Hell:
  - Default weight is 1.0
  - Set to 2.0 = double the appearance probability
  - Set to 0.5 = halve the appearance probability
  - Set to 0 = equivalent to disabling

#### Boss Pool Refresh Rules
- After a mod update or Boss-pool rebuild, the filter reconstructs its list from the current roster.
- Fodder spawned by Faction War / Blood Hunt is removed automatically, so the list never fills up with junk.
- The Dragon Descendant, the Skyburner Dragon Lord and a few special Bosses are preserved and will not disappear from the filter because of that cleanup.

### Data Persistence
- Filter settings are saved to the configuration file and automatically loaded the next time the game starts.
