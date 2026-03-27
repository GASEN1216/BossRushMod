# Boss Filter

# Overview
- The Boss Filter allows you to customize the Boss pool by disabling Bosses you don't want to encounter or adjusting the appearance weight of specific Bosses in Infinite Hell.
- The filter follows the current Boss preset list, and temporary minion presets created at runtime by Faction War / Blood Hunt are excluded from the Boss list.

# How to Open
- Press **Ctrl+F10** to open the Boss Filter panel.

# Features

# Disable Bosses
- Uncheck a Boss in the panel and it will no longer appear in the Boss pool of any mode.
- Use cases:
  - Disable all other Bosses when you want to practice against a specific one
  - Temporarily exclude a Boss that's too difficult or annoying
  - Narrow down the Boss pool to improve efficiency when farming a specific Boss's drops

# Infinite Hell Weight
- You can set an individual appearance weight multiplier for each Boss in Infinite Hell:
  - Default weight is 1.0
  - Set to 2.0 = double the appearance probability
  - Set to 0.5 = halve the appearance probability
  - Set to 0 = equivalent to disabling

# Boss Pool Refresh Rules
- After a mod update or Boss-pool rebuild, the filter reconstructs its list from the current presets.
- Temporary non-Boss presets spawned by Faction War / Blood Hunt are removed automatically so the filter is not polluted by fodder units.
- Dragon Descendant, Dragon King, and a few special low-level Boss presets are preserved and will not disappear from the filter because of that cleanup.

# Data Persistence
- Filter settings are saved to the configuration file and automatically loaded the next time the game starts.
