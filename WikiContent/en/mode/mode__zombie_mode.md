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

### How it connects to the other systems

Zombie Mode has its own lifecycle and reward system, so most Mod systems don't apply here.
Two exceptions are worth knowing:

- **Duck King Campaign chapter 5, "The Match Nobody Takes", sends you here** - hold the tide to wave 5
  and extract once, for a 100,000 payout. It's the only chapter that enters Zombie Mode.
- **All five Zombie Bosses appear in the Duck King Codex** - Titan, Hunter, Splitter, Shielder
  and Corruptor each get a square, tracking total kills, first-seen date and fastest kill just
  like any other Boss. There's no completing the codex without this mode.

**What does not apply here**: mutators (this mode has its own in-run upgrades), random events,
PetNest companions, affix forging (the temporary goblin doesn't offer it), and relic soul/egg drops.

### Starter Loadout Choice

You must pick one of two loadouts upon entering:

- **Melee** — Starting Gear: Random melee weapon ×1 (quality ≤5) + healing items (with guaranteed recovery items) + food ×3 + drinks ×2
- **Gunner** — Starting Gear: Random firearm ×1 + matching caliber ammo ×2000 + medical ×3 + food ×2 + drinks ×1

---

### Core Loop

```
Preparation → Combat Wave → Settlement → Reward Selection → Preparation → ...
```

Every 5th wave is a **Boss Wave**; all others are normal waves.

---

### Preparation Phase

- The reward screen normally shows only the current **Rest Time** with an **Edit** button, keeping the reward area compact. Edit opens a 15-second-step slider from **15 to 300 seconds (5 minutes)**; click **Apply** to save.
- Each run starts at **45 seconds**. Once changed, that value becomes the default for every later wave, including the post-Boss extraction preparation.
- A **Safe Zone** spawns at your feet (radius 8m, green circle)
- Inside the Safe Zone:
  - Normal zombies inside are removed immediately when the zone is deployed; Bosses are moved outside the boundary
  - New zombies entering the zone are continuously pushed out, so the zone never becomes a zombie standing area
  - Zombies won't aggro you (threat suppression)
  - Directly damaging a zombie while inside cancels the entire safe zone immediately, including its circle, map marker, and bound terminal
- Portable Safe-Zone Device: **used in combat** it moves the safe zone to your current position, and the normal merchant-bearing zone is recreated as usual once the wave ends; **used during preparation** it deploys an extra zone without a merchant that coexists with the normal one until the next wave formally starts, when both are cleared
- Both uses obey the attack-cancellation rule above; damaging a zombie inside either zone cancels both.
- Zone flashes yellow in the last 5 seconds as warning
- A **Supply Terminal** (merchant NPC) spawns inside the safe zone
- You can use the **Zombie Tide Beacon** to skip the countdown and start the next wave immediately (3-second channel)
- Preparation does not stop spawning: 12-48 low-tide zombies remain outside the safe zone, replenishing at most one zombie every 1.65 seconds

### Extraction Opportunity

After each Boss Wave, the preparation phase includes an **extraction opportunity**:
- A prompt appears: "Extract Now" or "Continue Fighting"
- Choosing extraction requires standing in the extraction zone for **15 seconds**
- Leaving the zone cancels extraction and starts the next wave
- Successful extraction: Purification Points convert 1:1 to cash, return to base

---

### Combat Waves

#### Normal Waves (non-multiples of 5)

The tide runs in five-wave cycles: Low Tide → Rising Tide → High Tide → Peak Tide → Boss Tide.

- **Wave 1** — Stage: Low Tide; Field Pressure: 24; Kill Target: 18; Refill Interval: 1.00s; Non-Boss Move Speed: 72%
- **Wave 2** — Stage: Rising Tide; Field Pressure: 36; Kill Target: 24; Refill Interval: 0.86s; Non-Boss Move Speed: 76%
- **Wave 3** — Stage: High Tide; Field Pressure: 51; Kill Target: 30; Refill Interval: 0.72s; Non-Boss Move Speed: 79%
- **Wave 4** — Stage: Peak Tide; Field Pressure: 69; Kill Target: 38; Refill Interval: 0.58s; Non-Boss Move Speed: 83%
- **Wave 5** — Stage: Boss Tide; Field Pressure: 24 support zombies; Kill Target: Defeat the Boss; Refill Interval: 1.00s; Non-Boss Move Speed: 86% (support only)

- After each Boss, the next cycle drops back to Low Tide with +18 normal-wave baseline pressure, the original +18 kill-target growth, and slightly faster refills
- Non-Boss enemy move speed starts at 72%, increases by 3.5% each wave, and caps at the original 100%
- **150** remains a hard safety cap for normal zombies, not a target that every refill tries to fill
- As a normal wave approaches its kill target, pressure falls automatically to create a finishable ebb

#### Boss Waves (wave 5, 10, 15...)

- There is no kill-count target; defeat every Boss in the wave instead
- Boss count grows only with progression: waves 5/10/15/20/25 contain **1/2/3/4/5 Bosses**, then every later Boss wave adds one more with no gameplay cap
- Boss count is independent of map size and spawn-point count
- Boss cycles do not add movement speed; difficulty grows through Boss health, damage, and support pressure
- The wave completes only after every Boss is dead

- **Wave 5** — Boss Count: 1; Per-Boss HP: 100%; Per-Boss Damage: 100%; Support Pressure: 24; Per-Boss Purification: 100%; Roguelite Rewards: One full-pool 4-choice pick
- **Wave 10** — Boss Count: 2; Per-Boss HP: 130%; Per-Boss Damage: 112%; Support Pressure: 33; Per-Boss Purification: 125%; Roguelite Rewards: One combat 4-choice pick + one full-pool 4-choice pick
- **Wave 15** — Boss Count: 3; Per-Boss HP: 160%; Per-Boss Damage: 124%; Support Pressure: 42; Per-Boss Purification: 150%; Roguelite Rewards: One combat 4-choice pick + one full-pool 4-choice pick
- **Wave 20** — Boss Count: 4; Per-Boss HP: 190%; Per-Boss Damage: 136%; Support Pressure: 51; Per-Boss Purification: 175%; Roguelite Rewards: One combat 4-choice pick + one full-pool 4-choice pick
- **Wave 25** — Boss Count: 5; Per-Boss HP: 220%; Per-Boss Damage: 148%; Support Pressure: 60; Per-Boss Purification: 200%; Roguelite Rewards: One combat 4-choice pick + one full-pool 4-choice pick

- Later Bosses cap at 250% HP and 180% damage; support pressure caps at 60 and purification gain caps at 300%
- The bonus combat pick only contains attributes, projectiles, triggers, and run mutators; NPC, ordinary supply, and economy choices cannot occupy it
- Every Boss independently drops purification stars and one lootbox, so total wave purification and lootbox count keep growing with Boss count and have no gameplay cap
- Per-Boss kill purification and the reward-screen purification choice use the purification multiplier; each lootbox's quantity and quality rise on the same cycle
- Each Boss lootbox gains one item per cycle (starting at 6-9, capped at 10-13); maximum quality rises toward Q8 and minimum quality rises every two cycles

#### Ambient Pressure

- Preparation maintains 35% of the next wave's pressure, clamped to 12-48 zombies, and replenishes at most one every 1.65 seconds
- Combat refills toward its current pressure target in bounded batches; it prefers reachable NavMesh positions near the player and retains a safe fallback of at least 12m when strict candidates fail
- Preferred spawn distance is 22m for waves 1-2, 20m for waves 3-5, and 18m afterward
- Living counts are reconciled against valid runtime enemies before each refill; a normal zombie that remains more than 60m away for 8 seconds is recovered near the player instead of occupying a hidden slot
- Zombies actively track the player (trace distance: 500m)

---

### Enemy Types

#### Normal Zombies

The rank-and-file of the horde. Drop **1** purification star (3-8 points).

#### Special Zombies

Stronger than normal with unique abilities. Drop **3** purification stars (30–60 points total). The multipliers below are the final special-type values before pollution scaling; every Special starts from HP ×1.40, damage ×1.20, and speed ×1.10.

- **Sprinter Zombie** — Combat multipliers before pollution: HP ×1.40 / damage ×1.20 / speed ×1.32; Target color / size: Yellow target `#FFD91F`; size ×1.35; Known behavior: Dash distance 12m, 0.5s startup, 8s cooldown.
- **Exploder Zombie** (mod blast) — Combat multipliers before pollution: HP ×1.30 / damage ×1.20 / speed ×1.10; Target color / size: Red-orange target `#FF4D14`; size ×1.60; Known behavior: Detonates when the player enters 2.5m; 1s delay, 4m radius, 80 damage, 9s skill cycle; self-destructs after triggering, and a pre-detonation death can trigger the blast once.
- **Exploder Zombie** (vanilla blast) — Combat multipliers before pollution: HP ×1.30 / damage ×1.20 / speed ×1.10; Target color / size: Red-orange target `#FF4D14`; size ×1.60; Known behavior: Uses the base game zombie's own self-destruct skill. The mod does not layer a second blast on top; its exact range and damage come from the base game and are not guessed here.
- **Plague Zombie** — Combat multipliers before pollution: HP ×1.50 / damage ×1.20 / speed ×0.95; Target color / size: Green target `#2EFF59`; size ×1.80; Known behavior: Every 12s, casts a poison cloud with 0.9s telegraph, 4m radius, 3s duration, and 8 DPS. The ground zone stays at the cast point; the mutant also carries a green plague aura.
- **Summoner Zombie** — Combat multipliers before pollution: HP ×1.50 / damage ×1.20 / speed ×0.95; Target color / size: Purple target `#BF4DFF`; size ×2.00; Known behavior: Every 15s, summons 2 normal zombies. Summoned zombies are scaled to ×0.60.
- **Harasser Zombie** — Combat multipliers before pollution: HP ×1.30 / damage ×1.20 / speed ×1.10; Target color / size: Cyan target `#26F2FF`; size ×1.45; Known behavior: Every 4s, fires a visible-trail projectile (speed 10, damage 25, flight lifetime 3.5s). It deals damage and creates a 3.5m slow zone only on an actual player hit; reaching the launch-time target point or expiring without a hit counts as a successful dodge.

> Both exploder variants show in-game as "Exploder Zombie"; they differ only in whether the blast is the mod's or the base game's.
> Neither appears in waves 1-5; the full special pool opens up from wave 6.

#### Elite Zombies

Powerful mutants carrying 1–3 affixes. Drop **5** purification stars (80–150 points total). Normal elites use HP ×2.50 / damage ×1.50 / speed ×1.10; at pollution **≥15**, enhanced elites use HP ×3.20 / damage ×1.70 / speed ×1.30.

Pollution is applied after the base and affix multipliers: HP ×`(1 + pollution × 0.05)` and damage ×`(1 + pollution × 0.04)`; speed has no pollution multiplier. Affix multipliers stack multiplicatively.

**Affix encyclopedia**:

- **Swift** — Exact effect: Additional speed ×1.30.; Target color (priority): Yellow `#FFD91F` (5); Size bonus: None; Unlock tier: 0
- **Frenzied** — Exact effect: Additional damage ×1.15 and speed ×1.10.; Target color (priority): Yellow `#FFD91F` (5); Size bonus: None; Unlock tier: 0
- **Tough** — Exact effect: Additional HP ×1.40.; Target color (priority): Default orange `#FFA61F` (6); Size bonus: +0.20; Unlock tier: 0
- **Stalwart** — Exact effect: Additional HP ×1.15; damage from the main player that is not melee is reduced to 10% (90% reduction).; Target color (priority): Default orange `#FFA61F` (6); Size bonus: +0.20; Unlock tier: 1
- **Regenerating** — Exact effect: Restores 2.5% of max HP every second, with a minimum of 1 HP.; Target color (priority): Green `#2EFF59` (2); Size bonus: None; Unlock tier: 1
- **Burst** — Exact effect: Death explosion: 4m radius, 40 damage.; Target color (priority): Red-orange `#FF4D14` (4); Size bonus: None; Unlock tier: 1
- **Plague** — Exact effect: Every 12s, casts a fixed-position poison cloud: 0.9s telegraph, 5.5m radius, 3s duration, 26 total damage (about 8.67 DPS).; Target color (priority): Green `#2EFF59` (2); Size bonus: None; Unlock tier: 1
- **Commander** — Exact effect: An 8m aura refreshes every 0.5s; nearby zombies gain +20% walk/run speed and +15% melee/gun damage.; Target color (priority): Purple `#BF4DFF` (1); Size bonus: None; Unlock tier: 3
- **Toxic Aura** — Exact effect: Every 12s, creates a caster-following toxic zone: 0.9s telegraph, 5.5m radius, 3s duration, 26 total damage (about 8.67 DPS).; Target color (priority): Green `#2EFF59` (2); Size bonus: None; Unlock tier: 3
- **Splitting** — Exact effect: On death, spawns 2 normal small zombies at ×0.60 size.; Target color (priority): Purple `#BF4DFF` (1); Size bonus: None; Unlock tier: 3
- **Shielded** — Exact effect: Additional HP ×1.25; every 12s gains a shield equal to 25% of max HP for 5s.; Target color (priority): Cyan `#26F2FF` (3); Size bonus: +0.20; Unlock tier: 3
- **Adaptive** — Exact effect: After 5 consecutive melee or 5 consecutive non-melee hits from the main player, damage from that category is reduced by 60% for 8s; switching category resets the opposite counter.; Target color (priority): Cyan `#26F2FF` (3); Size bonus: None; Unlock tier: 5

**Affix count by pollution**:
- Pollution < 5: 1 affix
- Pollution 5–14: 1 (65%) or 2 (35%)
- Pollution 15–24: 2 affixes
- Pollution ≥ 25: 2–3 affixes

**Forbidden combinations**:
- Stalwart + Shielded + Regenerating is never allowed.
- Below pollution 15, Stalwart + Swift is forbidden.
- Below pollution 15, ToxicAura + Plague + Swift is forbidden.

**Color and scale rules**:
- For multiple affixes, color priority is purple (Commander/Splitting) > green (Plague/ToxicAura/Regenerating) > cyan (Shielded/Adaptive) > red-orange (Burst) > yellow (Swift/Frenzied) > default orange (Tough/Stalwart or no match). These are target colors: what you actually see is blended toward the zombie's original palette, so it won't match exactly.
- Elites grow with affix count: 1 = ×1.65, 2 = ×1.95, 3 = ×2.25. Tough, Stalwart, or Shielded adds ×0.20; the hard cap is ×3.00 (the current three-affix high-threat maximum is ×2.45).
- Only the appearance grows. Attack range, collision and pathing are unaffected - don't judge its reach by its size. If the recolor and resize can't be applied, a persistent marker appears at its feet so you can still pick it out.
- All Bosses, including Titan, are excluded from this mutation identity system; this page documents only Specials and Elites.

#### Spawn Probability

The first five waves use a fixed onboarding curve instead of the pollution table:

- **Wave 1** — Elite %: 0%; Special %: 0%
- **Wave 2** — Elite %: 0%; Special %: 3%
- **Wave 3** — Elite %: 0%; Special %: 5%
- **Waves 4–5** — Elite %: 1%; Special %: 8%

From wave 6 onward, the mode uses continuously growing weights. Normal weight stays at 100; Elite weight is "pollution base + 3 × (wave - 5)"; Special weight is "pollution base + 5 × (wave - 5)." The three weights are normalized, so the combined Elite/Special chance keeps approaching 100% without either type crowding out the other.

Examples below assume pollution 0; higher pollution raises the Elite and Special shares further:

- **Wave 6** — Elite %: 3.5%; Special %: 8.8%; Elite + Special: 12.3%
- **Wave 10** — Elite %: 11.0%; Special %: 20.5%; Elite + Special: 31.5%
- **Wave 20** — Elite %: 20.4%; Special %: 35.4%; Elite + Special: 55.8%
- **Wave 50** — Elite %: 29.2%; Special %: 49.4%; Elite + Special: 78.5%
- **Wave 100** — Elite %: 33.0%; Special %: 55.4%; Elite + Special: 88.5%

---

### Boss System

Boss Waves appear every 5 waves. There are 5 Boss types. Each drops **8** purification stars (300–800 points total).

- **Titan** — HP Mult: ×35; Dmg Mult: ×1.8; Scale: ×1.8; Speed: ×0.7; Traits: Slow but extremely tanky, shockwave + damage reduction
- **Hunter** — HP Mult: ×18; Dmg Mult: ×1.4; Scale: ×1.2; Speed: ×1.6; Traits: Fast dash, low-HP frenzy
- **Splitter** — HP Mult: ×25; Dmg Mult: ×1.1; Scale: ×1.5; Speed: ×0.95; Traits: Summons minions, HP-threshold splits
- **Shielder** — HP Mult: ×28; Dmg Mult: ×1.3; Scale: ×1.3; Speed: ×0.9; Traits: Self shield + group shield aura
- **Corruptor** — HP Mult: ×26; Dmg Mult: ×1.2; Scale: ×1.4; Speed: ×1.0; Traits: Ground corruption zones + poison trail

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

If a Boss has made no positional progress for 12 seconds, it is teleported near the player with its ground position, NavMeshAgent, and rigidbody velocity corrected; ongoing player damage does not block recovery.

---

### Purification Points

The core currency of Zombie Mode:
- **Buy supplies**: All merchant items cost Purification Points
- **Cash out on extraction**: 1 Purification Point = 1 cash on successful extraction

#### Sources

- Killing zombies drops **Purification Stars** (auto-magnetize within 30m)
- Ordinary loose drops, including elite zombies' ordinary drops, are cleared when the next wave actually starts. They remain pickable during reward selection and rest; Boss lootboxes remain until collected or end-of-run cleanup.
- Cash investment at entry (100 cash = 1 point)
- "Purification Points" reward option

#### Star Drops

- **Normal** — Stars: 1; Point Range (total): 3–8
- **Special** — Stars: 3; Point Range (total): 30–60
- **Elite** — Stars: 5; Point Range (total): 80–150
- **Boss** — Stars: 8; Point Range (total): 300–800

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
- Wave 5 Boss: **pick 1 of 4**
- From the Wave 10 Boss onward: first pick **1 of 4** combat upgrades, then pick **1 of 4** from the full reward pool, for two rewards total
- Waves 1–2 always include one offense upgrade matching the starter loadout and one medical, armor, or healing option
- The bottom of the panel keeps a compact “Rest Time + Edit” row. The 15–300 second choices appear only after Edit is clicked; choosing one collapses the editor and carries that value into later waves.

**Refresh** options:
- **3 free refreshes** per node
- Paid refreshes after (escalating cost: 100 → 200 → 350 → 550 → 800 points)

#### Reward Categories

- **Attribute** — Description: Permanent boosts to HP/speed/melee damage/ranged damage/reload speed/damage reduction
- **Equipment** — Description: Random weapons/ammo/medical/armor/high-quality items/one-use Portable Safe-Zone Device
- **Economy** — Description: Purification Points/paid-refresh discounts/healing/a high-weight low-quality junk recycling option; weapons, ammo, medical, food, keys, special items, and containers with attachments or contents are protected
- **NPC** — Description: Temporarily summon merchant/nurse/goblin/courier
- **Fortification** — Description: Defensive structure supply packs
- **Contract** — Description: High-risk high-reward trades (may increase pollution)
- **Insurance** — Description: Keep some items on death
- **Map Event** — Description: High-value airdrop/elite squad
- **Projectile Mod** — Description: Penetration/burn/cold/poison/armor break/trident/shotgun spray/stasis/ricochet/fork/return/helix/trail
- **Trigger** — Description: Lifesteal/crit burst/purification siphon/second wind/doom pulse
- **Mutator** — Description: Crit focus/bullet time/guardian shield/quick reload/dash boost
- **Battlefield** — Description: Ammo rain/purge aura/curse trap/black hole/gravity drag

---

### Supply Terminal (Merchant NPC)

Spawns automatically in the safe zone each preparation phase. All items cost Purification Points.

#### Normal Wave Stock

- **Firearm** — Stock: 1; Base Price: 500
- **Melee Weapon** — Stock: 1; Base Price: 300
- **Accessory** — Stock: 1; Base Price: 260
- **Ammo** — Stock: 120 purchases; Base Price: 100 per purchase, 200 rounds each
- **Helmet** — Stock: 1; Base Price: 350
- **Armor** — Stock: 1; Base Price: 400
- **Backpack** — Stock: 1; Base Price: 260
- **Totem** — Stock: 1; Base Price: 500
- **Mask** — Stock: 1; Base Price: 180
- **Medical** — Stock: 3; Base Price: 80
- **Food** — Stock: 4; Base Price: 30
- **Drinks** — Stock: 4; Base Price: 30
- **Bait** — Stock: 3; Base Price: 45

#### Boss Node Stock

After Boss Waves, stock quality increases (quality 3–6) with higher prices. Drinks have 3 units of stock with a base price of 50.

The terminal shows current Purification balance, stock, and price directly. When you cannot afford an item, its price and purchase action provide clear feedback before you commit.

#### Nurse Services

- **Heal 50% HP** — Price: 120; Uses: 5
- **Full Heal** — Price: 300; Uses: 2
- **Detox** — Price: 80; Uses: 4
- **Stop Bleeding** — Price: 60; Uses: 4
- **First Aid (revive insurance)** — Price: 500; Uses: 1

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
