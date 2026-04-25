## v2.1.36

### Release Date
- 2026-04-25

### Main Theme
- **Awen sweep interaction, spotlight reward-crate descent, Boss Filter coverage for Mode D/E/F, and loot-probability scope fix**: this update adds a permanent `Sweep Loot` interaction to Awen for fast BossRush lootbox cleanup / consolidation, replaces the old instant post-clear reward placement with a highlighted crate that appears above the player and descends slowly, extends Boss Filter disabling to From Scratch / Faction War / Blood Hunt, and fixes `useLegacyBossLootProbabilities` so it no longer affects only Standard BossRush.

### Detailed Update Log

#### New
- Added Awen's **`Sweep Loot`** interaction: it can consolidate the current scene's tracked BossRush lootboxes into a single Awen pickup crate for faster cleanup and inventory flow.

#### Improvements
- Standard BossRush completion rewards no longer appear instantly near the signpost; the reward crate now shows up as a highlighted ghost crate above the player, then materializes and descends to the ground.
- Boss Filter disabled entries now also affect the Boss pools used by **From Scratch / Faction War / Blood Hunt**.

#### Fixes
- Fixed `useLegacyBossLootProbabilities` only affecting Standard BossRush. It now correctly applies to the intended regular loot-quality paths in From Scratch / Faction War / Blood Hunt as well.
