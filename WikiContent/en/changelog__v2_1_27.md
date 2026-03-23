## v2.1.27

### Release Date
- 2026-03-23

### Main Themes
- **Boss-pool and Reforge Cleanup**: the Boss Filter no longer picks up runtime minions from Faction War / Blood Hunt, and Reforge now excludes unsafe internal timing stats while cleaning old invalid data.
- **Courier, Marriage, and Interaction QoL**: one-click store/deposit skips locked slots, spouse auto-return feedback is clearer, and grouped NPC interactions are more reliable.
- **Mode E / F Display and Shop Sync**: Faction War / Blood Hunt naming, reward notes, and merchant categories were aligned further, and the face-wear shop now includes headsets.

### Detailed Update Log
#### 2026-03-21
- The Boss Filter now strips out runtime fodder presets created by Faction War / Blood Hunt while preserving Dragon Descendant / Dragon King related special Boss presets.
- Reforge no longer includes melee hit-timing internals in the roll pool, and legacy invalid reforge data is cleaned on load and reset to prefab defaults.
- Fixed grouped single-point multi-option NPC interactions so the goblin, nurse, courier, and wedding chapel options initialize more reliably.

#### 2026-03-23
- One-click store / deposit now excludes locked inventory slots.
- When a spouse auto-returns home after dropping below Affinity Lv.10, the player now also gets a dialogue-bubble prompt.
- The face-wear shop of the Faction War / Blood Hunt mystery merchant now sells headsets as well.
- Blood Hunt bounty bonus drops and extraction rewards now both resolve directly through the shared high-quality reward pool.
- Continued cleanup of Mode E / F health-bar names, bounty display, and fortification flow, with the related Wiki pages updated to match.
