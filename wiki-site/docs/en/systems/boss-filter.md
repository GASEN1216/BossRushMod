# Boss Filter

## Overview
- The Boss Filter allows you to customize the Boss pool by disabling Bosses you don't want to encounter or adjusting the appearance weight of specific Bosses in Infinite Hell.
- Disabled entries affect the Boss pools used by **Standard BossRush, Infinite Hell, From Scratch, Faction War, Blood Hunt and Fate Echo**, plus the pool the "Boss Intrusion" random event draws its intruder from; Infinite Hell additionally supports its own per-Boss weight multipliers.
- **Two exceptions**: the Black Market Duck Cup runs its own Boss roster (every official Boss plus the three mod Bosses), and Zombie Mode uses its own zombies. Neither reads this filter.
- The Duckov Codex and the PetNest bloodline roster follow the same pool - disable a Boss and you can no longer fight it, but **entries you already collected do not disappear**.
- The filter follows the current Boss roster; fodder units spawned mid-run by Faction War / Blood Hunt never make it onto that list.

## How to Open
- Press **Ctrl+F10** to open the Boss Filter panel. It has two tabs at the top: **Boss Pool** and **Hell Factors**, and it always opens on the Boss Pool tab.
- There is no separate save step: the ×, Esc, pressing Ctrl+F10 again and "Save & Close" all take the same path and save before closing. If no Boss is enabled it neither saves nor closes; the stats line tells you to keep at least one.

## Features

### Disable Bosses
- Uncheck a Boss on the Boss Pool tab and it will no longer appear in the Boss pools listed above; the tab header has Select All and Deselect All.
- Use cases:
  - Disable all other Bosses when you want to practice against a specific one
  - Temporarily exclude a Boss that's too difficult or annoying
  - Narrow down the Boss pool to improve efficiency when farming a specific Boss's drops

### Infinite Hell Factors
- On the Hell Factors tab you pick an appearance level for each Boss in Infinite Hell, five levels in all:
  - **Very Low** ×0.2, **Low** ×0.5, **Medium** ×1.0 (default), **High** ×1.5, **Very High** ×2.0
  - This is a relative draw weight; the final probability also depends on every other Boss's weight. Tankier Bosses already get drawn more often later in a run, and the factor multiplies on top of that
  - There is no "never" level; to exclude a Boss, uncheck it on the Boss Pool tab
  - "Reset All" in the tab header asks for confirmation, then puts every Boss back to Medium; your adjustments cannot be recovered

### Boss Pool Refresh Rules
- After a mod update or Boss-pool rebuild, the filter reconstructs its list from the current roster.
- Fodder spawned by Faction War / Blood Hunt is removed automatically, so the list never fills up with junk.
- The Dragon Descendant, the Skyburner Dragon Lord and a few special Bosses are preserved and will not disappear from the filter because of that cleanup.

## Data Persistence
- Filter settings are saved to the configuration file and automatically loaded the next time the game starts.
