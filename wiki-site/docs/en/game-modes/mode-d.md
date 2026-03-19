# From Scratch

# Overview
- From Scratch is a "lite roguelike" growth challenge mode.
- You must enter naked. The system randomly issues a set of starting gear, then you snowball by collecting drops from killing enemies.
- Enemies grow stronger each wave and carry random gear. You need to adapt on the fly based on the resources you obtain.

# Entry Requirements
- **Naked**: Cannot wear any equipment; backpack and pet bag must also be empty.
- Carry a **BossRush Ticket**.
- Cannot carry a Faction Banner or Bloodhunt Transponder (otherwise you will enter a different mode).

# Starting Gear
- The system randomly issues the following gear at the start:
- Weapon (100%): Quality 1-3, random attachments (roughly 30% chance per slot).
- Ammo (100%): Matching weapon caliber, 120-180 rounds, magazine pre-loaded.
- Armor (50%): Quality 1-3.
- Helmet (50%): Quality 1-3.
- Melee weapon (40%).
- Backpack (40%).
- Medical supplies (100%): 3 healing items.
- Totem (30%).
- Mask (30%).

# Wave Structure
- From Scratch uses an unlimited wave system with enemy types progressing by wave:
- Waves 1-2: Weakest grunts only (low HP, no ghosts), 0 Bosses.
- Waves 3-5: All grunts (no ghosts), 0 Bosses.
- Waves 6-10: Grunts + Bosses (no elite Bosses), 1 Boss.
- Waves 11-15: Grunts + Bosses, 2 Bosses.
- Waves 16+: Full enemy pool + full Boss pool, 2+ Bosses.
- Each wave spawns 3 enemies by default (adjustable to 1-10 in config).

# Enemy Gear
- Enemies in From Scratch don't come empty-handed — they also carry random gear.
- Gear quality scales with wave and enemy HP: Quality = 1 + (Wave/5) + (HP/500), max 6.
- Enemies carry weapons (with random attachments), melee weapons, ammo, and multiple random items.
- Boss-type enemies retain their original helmet and armor.
- All this gear drops in loot crates after killing enemies.

# Difficulty Scaling
- Enemy health +3% per wave (cumulative).
- Enemy gear quality also increases as waves progress.
- Later waves feature all Bosses, including custom Bosses.

# Wave Intervals
- By default, the next wave starts automatically on a countdown (interval is configurable).
- You can also manually trigger "Rush Next Wave" via the signpost (must enable manual mode in config).

# Arena NPCs
- Awen (Courier): Always present, provides item storage and retrieval services.
- One of Dingdang or Yuori (random): Provides Reforge/shop or healing services (married NPCs are excluded from the random pool).

# Related Achievements
- From Scratch — Complete 10 waves (bonus 30,000).
- Perfect Scratch — 5 waves without taking damage (bonus 350,000).

# Strategy Tips
- Starting gear is completely random. Use whatever you get; don't fixate on a "perfect start."
- The first 5 waves have no Bosses. Use this time to scavenge gear upgrades from grunts.
- Enemy gear quality increases with waves. Later grunts may drop better gear than what you have.
- Medical supplies are a scarce resource. Minimize unnecessary damage.

::: tip
See the Infinite Hell & From Scratch strategy guide for detailed tips.
:::
