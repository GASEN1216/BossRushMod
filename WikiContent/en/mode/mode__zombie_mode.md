## Zombie Mode

### What Is It?

A Roguelite survival mode in BossRush — you're dropped into a map with nothing, facing endless waves of zombies. Clear each wave, pick a reward to grow stronger, but enemies scale up too. Survive long enough to extract and convert your Purification Points into cash; die and you lose everything.

### Entry Requirements

1. Purchase a **Zombie Tide Invitation** from the base merchant (consumed on entry)
2. Use the invitation to open the map selection screen
3. After confirming, optionally **invest cash**: 100 cash = 1 starting Purification Point (rounded down). Investing 0 is fine
4. Upon entering the map, all your items are automatically transferred to storage (naked entry)
5. Choose your starter loadout to begin

> The invitation is not refunded on death. If an error occurs during loading, both the invitation and cash are automatically refunded.

### Starter Loadout Choice

You must pick one of two loadouts upon entering:

| Loadout | Starting Gear |
|---------|---------------|
| **Melee** | Random melee weapon ×1 (quality ≤5) + healing items (with guaranteed recovery items) + food ×3 + drinks ×2 |
| **Gunner** | Random firearm ×1 + matching caliber ammo ×1000 + medical ×3 + food ×2 + drinks ×1 |

---

### Core Loop

```
Preparation → Combat Wave → Settlement → Reward Selection → Preparation → ...
```

Every 5th wave is a **Boss Wave**; all others are normal waves.

---

### Preparation Phase

- Lasts **30 seconds** countdown
- A **Safe Zone** spawns at your feet (radius 8m, green circle)
- Inside the Safe Zone:
  - Zombies are pushed outside the boundary
  - Zombies won't aggro you (threat suppression)
  - Attacking zombies with guns/melee weapons while inside **breaks stealth** (zone turns orange, zombies regain aggro)
- Zone flashes yellow in the last 5 seconds as warning
- A **Supply Terminal** (merchant NPC) spawns inside the safe zone
- You can use the **Zombie Tide Beacon** to skip the countdown and start the next wave immediately (3-second channel)

### Extraction Opportunity

After each Boss Wave, the preparation phase includes an **extraction opportunity**:
- A prompt appears: "Extract Now" or "Continue Fighting"
- Choosing extraction requires standing in the extraction zone for **15 seconds**
- Leaving the zone cancels extraction and starts the next wave
- Successful extraction: Purification Points convert 1:1 to cash, return to base

---

### Combat Waves

#### Normal Waves (non-multiples of 5)

- Kill target: **32 + (current wave - 1) × 5** zombies
- Zombies continuously respawn, max **50** normal zombies on the field at once
- Population checked and replenished every second

#### Boss Waves (wave 5, 10, 15...)

- No kill count target — defeat all Bosses instead
- Boss count scales with wave number
- Wave completes when all Bosses are dead

#### Ambient Pressure

- Zombies spawn during both preparation and combat phases
- Zombies actively track the player (trace distance: 500m)

---

### Enemy Types

#### Normal Zombies

Base enemies using the `Cname_Zombie` preset. Drop **1** purification star (3–8 points).

#### Special Zombies

Stronger than normal with unique abilities. Drop **3** purification stars (30–60 points total).

| Type | Traits |
|------|--------|
| **Sprinter** | Move speed ×1.2, dash ability (12m distance, 8s cooldown) |
| **Exploder** | HP ×1.3, detonates within 2.5m (1s delay, 4m radius, 80 damage) |
| **Plague** | HP ×1.5, speed ×0.95, green plague aura, releases poison cloud (4m radius, 3s duration, 8 DPS) |
| **Summoner** | HP ×1.5, speed ×0.95, periodically summons 2 normal zombies (15s cooldown) |
| **Harasser** | HP ×1.3, fires projectiles (speed 12, 25 damage), creates slow zone on hit (3.5m radius, 50% slow, 2s) |

> Exploders don't appear in the first 5 waves.

#### Elite Zombies

Powerful mutants carrying 1–3 affixes. Drop **5** purification stars (80–150 points total).

**Base multipliers**:
- Normal elite: HP ×2.5 / Damage ×1.5 / Speed ×1.1
- Enhanced elite (pollution ≥15): HP ×3.2 / Damage ×1.7 / Speed ×1.3

**Affix System**:

| Affix | Effect | Unlock Tier |
|-------|--------|-------------|
| **Swift** | Speed ×1.3 | 0 |
| **Frenzied** | Damage ×1.15, speed ×1.1 | 0 |
| **Tough** | HP ×1.4 | 0 |
| **Stalwart** | HP ×1.15, 90% ranged damage reduction | 1 |
| **Regenerating** | Continuous health regeneration | 1 |
| **Burst** | Explodes on death (4m radius, 40 damage) | 1 |
| **Plague** | Releases poison clouds | 1 |
| **Commander** | Aura buffs nearby zombies (8m radius, +20% speed, +15% damage) | 3 |
| **Toxic Aura** | Continuous toxic damage | 3 |
| **Splitting** | Splits into 2 smaller zombies on death | 3 |
| **Shielded** | HP ×1.25, periodic shield (25% max HP, 5s duration, 12s cooldown) | 3 |
| **Adaptive** | After 5 consecutive hits from same source, gains 60% damage reduction for 8s | 5 |

**Affix count by pollution**:
- Pollution < 5: 1 affix
- Pollution 5–14: 1 (65%) or 2 (35%)
- Pollution 15–24: 2 affixes
- Pollution ≥ 25: 2–3 affixes

#### Spawn Probability

| Pollution | Elite % | Special % | Normal % |
|-----------|---------|-----------|----------|
| 0–4 | 1% | 5% | 94% |
| 5–9 | 2% | 10% | 88% |
| 10–14 | 4% | 15% | 81% |
| 15–19 | 6% | 20% | 74% |
| 20–24 | 8% | 25% | 67% |
| ≥25 | 10% | 30% | 60% |

---

### Boss System

Boss Waves appear every 5 waves. There are 5 Boss types. Each drops **8** purification stars (300–800 points total).

| Boss | HP Mult | Dmg Mult | Scale | Speed | Traits |
|------|---------|----------|-------|-------|--------|
| **Titan** | ×35 | ×1.8 | ×1.8 | ×0.7 | Slow but extremely tanky, shockwave + damage reduction |
| **Hunter** | ×18 | ×1.4 | ×1.2 | ×1.6 | Fast dash, low-HP frenzy |
| **Splitter** | ×25 | ×1.1 | ×1.5 | ×0.95 | Summons minions, HP-threshold splits |
| **Shielder** | ×28 | ×1.3 | ×1.3 | ×0.9 | Self shield + group shield aura |
| **Corruptor** | ×26 | ×1.2 | ×1.4 | ×1.0 | Ground corruption zones + poison trail |

#### Boss Abilities

**Titan**:
- **Shockwave**: 6m radius, 60 damage, 12s cooldown, 1s startup
- **Fortify**: 40% damage reduction, 4s duration, 20s cooldown

**Hunter**:
- **Dash**: Teleports 15m toward player, 3.5m radius dealing 40 damage, 5s cooldown
- **Frenzy**: Triggers below 30% HP — +50% attack speed, +30% move speed, size increase, lasts 15s

**Splitter**:
- **Summon**: Spawns 4 smaller zombies (0.7× scale), 15s cooldown
- **HP Split**: At 50% and 25% HP, splits into 2 small zombies (0.5× scale)
- **Death Burst**: Explodes on death (4m radius, 45 damage) and spawns 2 small zombies

**Shielder**:
- **Self Shield**: 35% max HP shield, 8s duration, 25s cooldown
- **Group Shield**: All zombies within 8m get 35% max HP shield, 6s duration, 35s cooldown
- **Damage Reduction Aura**: 15% damage reduction for zombies within 6m (passive)

**Corruptor**:
- **Corruption Zone**: Places toxic circle at player's feet (4m radius, 8s duration, 6 DPS, 20% slow), 12s cooldown
- **Poison Trail**: Leaves toxic path while moving (1.2m wide, 5s duration, 4 DPS)
- **Death Cloud**: Releases poison cloud on death (5m radius, 6s duration, 5 DPS)

#### Boss Stuck Handling

If a Boss hasn't moved or taken damage for 45 seconds, it's teleported near the player.

---

### Purification Points

The core currency of Zombie Mode:
- **Buy supplies**: All merchant items cost Purification Points
- **Cash out on extraction**: 1 Purification Point = 1 cash on successful extraction

#### Sources

- Killing zombies drops **Purification Stars** (auto-magnetize within 30m)
- Cash investment at entry (100 cash = 1 point)
- "Purification Points" reward option

#### Star Drops

| Enemy Type | Stars | Point Range (total) |
|------------|-------|---------------------|
| Normal | 1 | 3–8 |
| Special | 3 | 30–60 |
| Elite | 5 | 80–150 |
| Boss | 8 | 300–800 |

> High pollution grants bonus points: +10% per 10 pollution, up to +50%.

---

### Pollution System

Pollution is the difficulty scaling mechanic.

#### Sources

- +1 natural pollution per Boss Wave cleared
- Some reward options add pollution (e.g., "Pollution Deal" contracts)

#### Effects

- **Enemy HP**: +5% per pollution point
- **Enemy Damage**: +4% per pollution point
- **Increased elite/special spawn rates** (see probability table above)
- **More elite affixes**
- **Higher-tier affixes unlock**
- **Price inflation**: At pollution 5/10/15/20/25, prices multiply by 1.1/1.2/1.3/1.4/1.5

---

### Reward Selection

After each wave, choose from rewards:
- Normal waves: **3** options
- Boss waves: **4** options

**Refresh** options:
- **3 free refreshes** per node
- Paid refreshes after (escalating cost: 100 → 200 → 350 → 550 → 800 points)

#### Reward Categories

| Category | Description |
|----------|-------------|
| **Attribute** | Permanent boosts to HP/speed/melee damage/ranged damage/reload speed/damage reduction |
| **Equipment** | Random weapons/ammo/medical/armor/high-quality items |
| **Economy** | Purification points/free refreshes/healing |
| **NPC** | Temporarily summon merchant/nurse/goblin/courier |
| **Fortification** | Defensive structure supply packs |
| **Contract** | High-risk high-reward trades (may increase pollution) |
| **Insurance** | Keep some items on death |
| **Map Event** | High-value airdrop/elite squad |
| **Projectile Mod** | Penetration/burn/cold/poison/armor break/trident/shotgun spray/stasis/ricochet/fork/return/helix/trail |
| **Trigger** | Lifesteal/crit burst/purification siphon/second wind/doom pulse |
| **Mutator** | Crit focus/bullet time/guardian shield/quick reload/dash boost |
| **Battlefield** | Ammo rain/purge aura/curse trap/black hole/gravity drag |

---

### Supply Terminal (Merchant NPC)

Spawns automatically in the safe zone each preparation phase. All items cost Purification Points.

#### Normal Wave Stock

| Item | Stock | Base Price |
|------|-------|------------|
| Firearm | 1 | 500 |
| Melee Weapon | 1 | 300 |
| Accessory | 1 | 260 |
| Ammo | 120 | 100 |
| Helmet | 1 | 350 |
| Armor | 1 | 400 |
| Backpack | 1 | 260 |
| Totem | 1 | 500 |
| Mask | 1 | 180 |
| Medical | 3 | 80 |
| Food | 4 | 30 |
| Bait | 3 | 45 |

#### Boss Node Stock

After Boss Waves, stock quality increases (quality 3–6) with higher prices.

#### Nurse Services

| Service | Price | Uses |
|---------|-------|------|
| Heal 50% HP | 120 | 5 |
| Full Heal | 300 | 2 |
| Detox | 80 | 4 |
| Stop Bleeding | 60 | 4 |
| First Aid (revive insurance) | 500 | 1 |

---

### Fortification System

Obtained through rewards, fortification packs let you place defensive structures:
- **Foldable Cover** — provides cover
- **Reinforced Roadblock** — blocks zombie movement
- **Barbed Wire** — slows and damages zombies
- **Emergency Repair Spray** — repairs damaged fortifications

Normal wave packs contain 1 of each; Boss node packs contain 2 of each.

---

### Failure & Death

- Player death = game over, auto-return to base
- All Purification Points are lost (no cash conversion)
- Invitation is not refunded
- Insurance rewards can preserve some items on death

### Successful Extraction

- Choose to extract during the extraction opportunity and stand in the zone for 15 seconds
- Purification Points convert 1:1 to cash
- Return to base with all acquired items

---

### Tips

- **Investing cash** is a solid strategy — starting Purification Points let you buy gear after wave 1
- **Melee loadout** suits aggressive playstyles with more healing; **Gunner loadout** suits kiting with abundant ammo
- First 5 waves are your buildup phase — use the safe zone to rest and shop
- Always consider extracting after Boss Waves — it gets harder, but points also increase
- Watch your pollution level — high-pollution elites are terrifying (3 affixes + enhanced multipliers)
- Shielder Boss is the most annoying — group shield makes all zombies tanky, prioritize killing it
- Hunter Boss frenzies at low HP — keep enough health to survive the dash
- Don't fight Splitter Boss in tight spaces — split zombies will block your escape
- Use the Zombie Tide Beacon to skip preparation when you're well-equipped
- Projectile mods stack (most cap at 3) — Penetration + Burn is a universal combo
- Lifesteal trigger is the best sustain option — grab it early
