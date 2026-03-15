## v2.1.21

### Release Date
- 2026-03-16

### Main Themes
- **Bug fix**: Fixed an issue where the gift UI could still be opened after already gifting an NPC today.
- **Performance**: Optimized Faction Battle (Mode E) boss spawn pacing to reduce stuttering on low-end machines.
- **Refactor**: Migrated vending machine item injection and boat interactable injection to Harmony Postfix patches for better stability and mod compatibility.

### Source Commit Subjects
- fix(gift): Fixed nurse/goblin gift UI still openable after daily gift given
- perf(modeE): Optimized Mode E boss spawn intervals to reduce lag on low-end machines
- refactor(integration): Migrated shop and boat injection to Harmony Postfix patches
