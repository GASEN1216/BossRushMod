## Loot & Rewards

### Overview
- This mod's loot system follows different rules depending on the mode. This page details the loot mechanics for each mode.

### Standard BossRush Loot

#### Random Loot Toggle
- The config option `enableRandomBossLoot` (default: on) controls whether the BossRush random loot system is active.
- When disabled, BossRush no longer takes over the vanilla Boss drop flow, and all quality distribution rules below do not apply.

#### Loot Crates
- After each Boss is killed, a loot crate spawns at its death location.
- The number of drops is determined by the Boss's health, ranging from 7 to 15 items (higher health = more drops).

#### Quality Distribution Modes
The config option `useLegacyBossLootProbabilities` controls the quality distribution algorithm, defaulting to `true` (Legacy Probability Mode).
- Alongside Standard BossRush crates, it also governs the quality distribution of extra backpack drops from defeated enemies in From Scratch, Faction War, and Blood Hunt.

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

> **Note**: The key gameplay difference is the probability curve and whether a Q5+ floor exists; candidate filtering details (such as quest-item exclusions) also differ slightly between paths.

#### Completion Reward Crate
- After defeating all waves, the completion crate appears as a glowing ghost chest above you, settles onto the ground, and becomes interactable:
  - Easy (1 Boss/wave): 3 high quality items
  - Normal (multiple Bosses/wave): 10 high quality items

#### Loot Blacklist
- The following types of items will not appear in the loot pool:
  - Items flagged as non-droppable
  - Demo-locked items
  - Quest-tagged items (Legacy mode excludes them more strictly)
- Crown drop weight is reduced to 0.1x, lowering its appearance frequency.

### Infinite Hell Rewards

#### Per-Wave Cash
- Each Boss kill grants cash = Boss max health × 10.
- The cash drops in several piles by the signpost, each within the game's stack limit; anything beyond 12 piles in one wave goes straight into your account instead of being lost.
- Example: a Boss with 1000 health drops 10,000 cash on kill.
- Cash pickups within 2 meters of the player automatically fly in; outside that radius you still need to move closer.

#### Random High Quality Items
- Every 5 waves, 1 high-quality item drops.
- The reward pool prefers items with quality ≥ 5 and value ≥ 10,000; if no candidate meets that value threshold, it falls back to other quality ≥ 5 items.

#### Milestone Grand Rewards
- Every 100 waves triggers a milestone reward containing Crowns and large amounts of cash, scaling exponentially:
  - Wave 100: 1 Crown + 10,000,000 cash
  - Wave 200: 2 Crowns + 20,000,000 cash
  - Wave 300: 4 Crowns + 40,000,000 cash
  - Wave 400: 8 Crowns + 80,000,000 cash
  - And so on, doubling every 100 waves.

#### Early Wave Protection
- Infinite Hell draws enabled Bosses by weight from wave 1, without a 20-wave heavy-hitter exclusion.
- As waves progress, high-base-health Bosses gain relative weight; use the Boss Filter to disable Bosses or adjust their weights.

### From Scratch Loot
- Enemies carry random equipment, all of which drops in a loot crate on kill.
- Equipment quality increases with waves: quality = 1 + (wave/5) + (enemy health/500).
- Later enemies may drop equipment better than what you currently have.
- When `useLegacyBossLootProbabilities` is enabled, extra backpack supplies and spare ammo stacks roll quality using Legacy Probability Mode (equipped gear, melee, and chambered ammo are unaffected).

### Faction War Loot
- Killing an enemy faction's Boss drops a loot crate; same-faction kills drop none.
- Dragon Descendant and BEAR promoted minions carry random equipment.
- When `useLegacyBossLootProbabilities` is enabled, regular backpack drops follow Legacy Probability Mode (active combat loadouts are unaffected).

### Blood Hunt Loot
- When `useLegacyBossLootProbabilities` is enabled, regular backpack drops follow Legacy Probability Mode; drop counts, bounty bonus drops, and extraction payouts remain untouched.
- Killing a bounty Boss drops extra items from the shared high-quality reward pool equal to its Bounty Marks.
- On extraction, each Bounty Mark you hold pays 1 item from that same shared high-quality pool into your Storage Point.

### Custom Boss Exclusive Drops

Killing a custom Boss drops exclusive equipment on top of the regular loot crate. **The two
dragons work differently from the Witch - read this before planning a farm route:**

#### Dragon Descendant - one of three

Each kill drops **exactly one** piece, weighted across three. The numbers add up to 100%:

- Flame Scale Armor (Armor): **60%**
- Crimson Dragon Helm (Helmet): **30%**
- Dragon's Breath (Firearm): **10%**

So you are **guaranteed** one piece, but collecting all three takes several kills, and Dragon
Breath in particular is down to luck.

#### Skyburner Dragon Lord - one of six

Again **exactly one** piece, weighted across six:

- Reverse Scale (Totem): **39%**
- Cloud Soar I (Totem): **15%**
- Dragon King's Crown (Helmet): **15%**
- Dragon King's Scale Mail (Armor): **15%**
- Inferno Emperor's Realm-Breaking Halberd (Melee): **15%**
- Skyburner Dragon Cannon (Firearm): **1%**

[tip] That 1% on the Skyburner Dragon Cannon means roughly 100 Dragon Lord kills on expectation. It is the hardest item in the Mod to obtain - go in knowing that.

#### Phantom Witch - an independent extra

The Witch works the other way: Soulreaper's Requiem is an **independent extra roll** at `50%`,
appended to the loot crate without competing with anything else. Frostmourne's drop from the
vanilla "???" Boss uses the same independent-extra mechanism.

### Extra equipment drops from official Bosses

#### Mad Bomber's Empty-Mag Mine Box

Defeating the official boss **Mad Bomber** gives an **independent 20% chance** to add an **Empty-Mag Mine Box** without replacing his normal loot. This also works during normal raids on vanilla maps; a BossRush run is not required.

- **When a BossRush loot crate takes over the drop** — the Empty-Mag Mine Box goes into that Boss's reward crate.
- **Infinite Hell** — the extra item drops on the ground where the Boss fell.
- **Vanilla maps and modes that retain official corpse loot** — search the Boss's corpse loot container.

The Empty-Mag Mine Box is not sold at Dingdang's Shop and is excluded from general random reward pools. Equip it in a totem slot, fire the same gun at least 6 times in one loading cycle, then empty its magazine to leave a delayed mine at your feet. See its equipment page for the full trigger rules.

### Extra progression drops from arena Bosses

The above covers loot crates and exclusive gear. On top of that, **eligible Bosses in Standard BossRush, Infinite Hell, From Scratch, Faction War and Blood Hunt** run the
rolls below. They're independent of each other and never take away anything you'd otherwise get.

Vanilla raids, the Sky Islands, Fate Echo, the Black Market Duck Cup and Zombie Mode do not use this relic-soul, relic-egg and forge-stone drop path.

#### Relic Souls (guaranteed)

Every Boss kill grants souls: **Boss max health / 15**, rounded, minimum 1. Tankier Bosses pay
more. Souls exist only to condense eggs: bank **240** in one bloodline and you can condense that
Boss's egg on demand.

#### Relic Eggs (about 4%)

A small chance to drop an egg of the matching bloodline outright. This is the lucky line;
condensing is the guaranteed line - two parallel paths, so unlucky players can still complete
the collection.

#### Affix Forge Stones (about 8%)

Material for affix forging, roughly one per standard arena run. The steady alternative is
Dingdang's shop (stocks at affinity Lv.10, up to 5 per restock).

#### Where eggs and stones actually land

Both **follow that Boss's normal loot path** - they are not always left on the corpse:

- **Standard BossRush, From Scratch** - into the **BossRush loot crate**, alongside everything else
- **Infinite Hell** - this mode spawns no crate at all, so eggs and stones **drop on the ground**;
  check where the Boss fell
- **Faction War, Blood Hunt** - no mod crate here, so the item **stays on the Boss** - search the body

[tip] One-line rule: if there's a crate, look in the crate; if there isn't, look at the ground; only then search the body.

#### Backyard Seeds (about 25%, the three custom Bosses only)

**Requires delivering Duck King Campaign chapter 1 to unlock the garden.** After that, the three
custom Bosses' loot crates each carry an extra seed:

- Dragon Descendant -> **Dragon Seed** (grows Dragonbreath Fruit)
- Skyburner Dragon Lord -> **Skyburner Ember Seed** (grows Emberheart Chili)
- Phantom Witch -> **Phantom Spore** (grows Shadow Mushroom)

The seed is free - it never displaces the Dragon set or Dragon Lord exclusive drops you'd
otherwise get.

[tip] So a full Skyburner Dragon Lord kill pays out: one loot crate + **one** piece of exclusive gear from the six + a pile of souls + a 4% egg + an 8% forge stone + a 25% ember seed. Don't just watch the gear slot.
