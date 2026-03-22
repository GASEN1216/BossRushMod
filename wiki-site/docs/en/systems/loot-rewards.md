# Loot & Rewards

# Overview
- The BossRush Mod loot system follows different rules depending on the mode. This page details the loot mechanics for each mode.

# Standard BossRush Loot

# Loot Crates
- After each Boss is killed, a loot crate spawns at its death location.
- The number of drops is determined by the Boss's health, ranging from 7 to 15 items (higher health = more drops).

# Quality Distribution
- Item quality is divided into two tiers: normal (1-4) and high quality (5-8).
- High quality weight is affected by two factors:
  - Boss health bonus: +5% per 100 health points
  - Kill speed bonus: the faster the kill, the higher the high quality chance (up to +10%)
- Internal high quality distribution: quality 5 : 6 : 7 : 8 = 4 : 3 : 2 : 1

# Completion Reward Crate
- After defeating all waves, a completion reward crate spawns at the center of the arena:
- Easy (1 Boss/wave): 3 high quality items
- Normal (multiple Bosses/wave): 10 high quality items

# Loot Blacklist
- The following types of items will not appear in the loot pool:
  - Quest items
  - Items flagged as non-droppable
  - Demo-locked items
- Crown drop weight is reduced to 0.1x, lowering its appearance frequency.

# Infinite Hell Rewards

# Per-Wave Cash
- Each Boss kill grants cash = Boss max health × 10.
- Example: a Boss with 1000 health drops 10,000 cash on kill.
- Cash automatically flies toward the player for pickup, no manual looting required.

# Kill Speed Bonus
- The faster you kill a Boss, the higher the drop chance for high quality items (up to an extra +10%).
- The reference time window scales with Boss health — higher health allows more time.

# Random High Quality Items
- Every 5 waves, 1 item with quality ≥ 5 and value ≥ 10,000 drops.

# Milestone Grand Rewards
- Every 100 waves triggers a milestone reward containing Crowns and large amounts of cash, scaling exponentially:
  - Wave 100: 1 Crown + 10,000,000 cash
  - Wave 200: 2 Crowns + 20,000,000 cash
  - Wave 300: 4 Crowns + 40,000,000 cash
  - Wave 400: 8 Crowns + 80,000,000 cash
  - And so on, doubling every 100 waves.

# Early Wave Protection
- Early waves automatically exclude certain high-intensity Bosses to avoid encountering overpowered enemies at the start.
- As waves progress, the Boss pool gradually unlocks all Bosses, with difficulty steadily increasing.

# From Scratch Loot
- Enemies carry random equipment, all of which drops in a loot crate on kill.
- Equipment quality increases with waves: quality = 1 + (wave/5) + (enemy health/500).
- Later enemies may drop equipment better than what you currently have.

# Faction War Loot
- Killing an enemy faction's Boss drops a loot crate.
- Killing a same-faction Boss does not drop a loot crate.
- Dragon Descendant and BEAR faction promoted minions carry random equipment.

# Blood Hunt Loot
- Killing a bounty Boss drops extra high quality items, with the quantity equal to the number of Bounty Marks on that Boss.
- Upon successful extraction, each Bounty Mark on a player = 1 high quality reward item, sent to the Storage Point.

# Custom Boss Exclusive Drops

When killing a custom Boss, in addition to the regular loot crate, exclusive equipment drops separately (independent roll, not affected by quality weights):

# Dragon Descendant
- Crimson Dragon Helm (Helmet): 30% drop rate
- Flame Scale Armor (Armor): 60% drop rate
- Dragon Breath (Firearm): 10% drop rate

# Skyburner Dragon Lord
- Cloud Rider (Totem): 20% drop rate
- Dragon King Crown (Helmet): 15% drop rate
- Dragon King Scale Armor (Armor): 15% drop rate
- Reverse Scale (Totem): 35% drop rate
- Skyburner Halberd (Melee): 15% drop rate
- Dragon Cannon (Firearm): 5% drop rate

# Death Protection

::: tip
In all BossRush modes, players do not drop their items on death.
:::
