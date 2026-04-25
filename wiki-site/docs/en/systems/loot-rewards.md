# Loot & Rewards

# Overview
- The BossRush Mod loot system follows different rules depending on the mode. This page details the loot mechanics for each mode.

# Standard BossRush Loot

# Random Loot Toggle
- The config option `enableRandomBossLoot` (default: on) controls whether the BossRush random loot system is active.
- When disabled, BossRush no longer takes over the vanilla Boss drop flow, and all quality distribution rules below do not apply.

# Loot Crates
- After each Boss is killed, a loot crate spawns at its death location.
- The number of drops is determined by the Boss's health, ranging from 7 to 15 items (higher health = more drops).

# Quality Distribution Modes
The config option `useLegacyBossLootProbabilities` controls the quality distribution algorithm. Default is `true` (Legacy Probability Mode).

##### Legacy Probability Mode (Default)
- Each quality tier has an independent drop probability determined by a `bonusFactor`.
- `bonusFactor` = 80% × health factor + 20% × kill speed factor, range [0, 1].
  - Health factor: normalized position of the Boss's max health within the Boss pool's min–max health range.
  - Speed factor: remaining ratio of kill time relative to a reference time window (faster = higher).
- Quality probabilities scale linearly as bonusFactor goes from 0→1:
  - Quality 8: 0.05% → 0.10%
  - Quality 7: 0.10% → 1.00%
  - Quality 6: 1.00% → 5.00%
  - Quality 5: 5.00% → 10.00%
  - Quality 1-4: remaining probability split by weights 0.1345 : 0.3655 : 0.3655 : 0.1345
- **Q5+ Guarantee**: when the Boss's max health > 250 and the loot crate contains no quality 5+ item, one extra Q5+ item is appended (90% Q5, 9% Q6, 0.9% Q7, 0.1% Q8).

##### Simplified Probability Mode
- Set `useLegacyBossLootProbabilities` to `false` to switch to this mode.
- Item quality is divided into two tiers: normal (1-4) and high quality (5-8).
- Total high quality probability is affected by two additive factors:
  - Boss health bonus: +5% per 100 health points
  - Kill speed bonus: the faster the kill, the higher the bonus (up to +10%)
- Internal high quality distribution: quality 5 : 6 : 7 : 8 = 4 : 3 : 2 : 1
- No Q5+ guarantee mechanism.

> **Note**: Both modes exclude quest-tagged items from the candidate pool. The only differences are the probability distribution algorithm and the Q5+ guarantee mechanism.

# Completion Reward Crate
- After defeating all waves, a completion reward crate spawns at the center of the arena:
- Easy (1 Boss/wave): 3 high quality items
- Normal (multiple Bosses/wave): 10 high quality items

# Loot Blacklist
- The following types of items will not appear in the loot pool:
  - Items flagged as non-droppable
  - Demo-locked items
  - Quest-tagged items (excluded in both probability modes)
- Crown drop weight is reduced to 0.1x, lowering its appearance frequency.

# Infinite Hell Rewards

# Per-Wave Cash
- Each Boss kill grants cash = Boss max health × 10.
- Example: a Boss with 1000 health drops 10,000 cash on kill.
- Cash pickups within 2 meters of the player automatically fly in; outside that radius you still need to move closer.

# Random High Quality Items
- Every 5 waves, 1 high-quality item drops.
- The reward pool prefers items with quality ≥ 5 and value ≥ 10,000; if no candidate meets that value threshold, it falls back to other quality ≥ 5 items.

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
- When `useLegacyBossLootProbabilities = true`, From Scratch uses the Legacy Probability Mode for the quality distribution of enemy backpack bonus items, backpack refill items, and extra backpack ammo stacks.
- This toggle does not change drop counts, equipped weapons, melee weapons, equipped gear, or the ammo currently loaded for combat.

# Faction War Loot
- Killing an enemy faction's Boss drops a loot crate.
- Killing a same-faction Boss does not drop a loot crate.
- Dragon Descendant and BEAR faction promoted minions carry random equipment.
- When `useLegacyBossLootProbabilities = true`, regular on-death backpack loot in Faction War also switches to the Legacy Probability Mode.
- The toggle only affects backpack bonus items / backpack refill items / extra backpack ammo stacks, not the enemy's live combat loadout.

# Blood Hunt Loot
- When `useLegacyBossLootProbabilities = true`, regular on-death backpack loot in Blood Hunt also switches to the Legacy Probability Mode.
- This only affects regular backpack loot quality. It does not affect drop counts, bounty bonus drops, extraction rewards, or any other shared high-quality reward-pool payouts.
- Killing a bounty Boss drops extra items from the shared high-quality reward pool, with the quantity equal to the number of Bounty Marks on that Boss.
- Upon successful extraction, each Bounty Mark on a player = 1 item from the shared high-quality reward pool, sent to the Storage Point.
- Both reward sources currently reuse the same shared reward-pool rules.

# Custom Boss Exclusive Drops

When killing a custom Boss, in addition to the regular loot crate, exclusive equipment drops separately (independent roll, not affected by quality weights):

# Dragon Descendant
- Crimson Dragon Helm (Helmet): 30% drop rate
- Flame Scale Armor (Armor): 60% drop rate
- Dragon Breath (Firearm): 10% drop rate

# Skyburner Dragon Lord
- Cloud Rider (Totem): 15% drop rate
- Dragon King Crown (Helmet): 15% drop rate
- Dragon King Scale Armor (Armor): 15% drop rate
- Reverse Scale (Totem): 35% drop rate
- Skyburner Halberd (Melee): 15% drop rate
- Dragon Cannon (Firearm): 5% drop rate

# Phantom Witch
- Soulreaper's Requiem (Melee): 50% drop rate
