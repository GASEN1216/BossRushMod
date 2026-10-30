## v2.3.9

### Release Date
- 2026-10-30
- This patch focuses on cubs, save recovery, transition cleanup and field adventures.

### Fixed

- **Cub names**: existing cubs no longer display an internal lineage ID as their default name. Names resolve again when the language changes; custom nicknames are preserved.
- **Cub backpack saving**: raids no longer wait for the absent base warehouse, only for official pet containers that are actually present. Idle maintenance, failed expedition rewards that never reach asset delivery, and fully rolled-back hatching no longer leave unnecessary base-asset save requirements. Delivered rewards and real unsaved assets remain protected.
- **Saves and transitions**: fixed some save failures leaving saving permanently busy, and improved pending-data and asynchronous-object cleanup when changing save slots, loading maps or leaving modes. Fixed stale-wave settlement in From Scratch and normal action completion cancelling slow Frostmourne summons.
- **Field-adventure placement**: parcel signing stands, mimics and prototype benches try suitable distance ranges and require a route the player can actually walk, avoiding placements beyond map boundaries or invisible walls.
- **Gluttonous Mimic**: feeding selects the cheapest eligible item by its per-unit price. Cannonballs and landing impacts check wall obstruction, so taking cover behind a wall prevents damage through it.
- **Dangerous Prototypes**: when a firework shell or bomblet completes the trial in a later explosion, the gun rig is recalled and the completion message appears once. Launched fireworks continue exploding, and the completed reward remains claimable.

[tip] This patch keeps the existing saves, configuration and item IDs.
