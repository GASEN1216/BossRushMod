## PetNest

### What Is It?

PetNest is a base-side raising and collection system: **kill bosses, bring their relics home, raise the cubs, take one along, or send them somewhere you would not go yourself.**

It hooks into the modes you already play and adds two questions before each run: bring a cub this time? Got that boss's egg yet?

PetNest is on by default and needs no setting turned on anywhere. Build the nest and it works.

### Getting Started

#### 1. Kill bosses, collect relics
Every boss kill feeds two tracks at once:

- **Relic Egg (the lucky track)**: a low-probability direct drop of that boss's egg. It follows that boss's normal loot path: into the **loot crate** in standard arena and From Scratch, **onto the ground** in Infinite Hell (no crate there), and **on the boss itself** only in Faction War and Blood Hunt, where you do have to search the body.
- **Boss Relic Soul (the guaranteed track)**: **always dropped**, scaled by the boss's max health. Souls are used to condense an egg of that bloodline and to treat the scars of cubs from it. They are not currency and never enter shops.

Drops cover the three standard difficulties, From Scratch, Faction War and Blood Hunt. **Fate Echo, the Black Market Duck Cup and Zombie Mode do not drop relics** - Bosses in those three modes spawn through their own separate paths and never enter the relic ledger.

#### 2. Build a PetNest in your base
Find "PetNest" in the base bunker's build menu. Once built, interacting with it offers four options:

- **Open PetNest** — cub roster, deployment slot, soul ledger
- **Hatch Relic Egg** — hatch an egg, or condense one from stored souls (**240** souls of that bloodline per egg)
- **Send on Expedition** — dispatch a cub to one of three disaster destinations
- **Relic Museum** — bloodline index and memorial

#### 3. Hatching
Hatching locks in three things at once, **with no rerolls**:

- **Two endowments** — small bonuses to move speed, damage, health, pet backpack slots, and so on
- **One temperament** — Reckless / Cautious / Lazy / Loyal
- **Shiny** — about **0.4%**, ultra rare. Its name gets a golden "★ Shiny" with a shimmer, and it wears a golden halo and star sparks. Purely a collector's honour, no stats
- **Chroma** — about **10%**, any two of ten colours, in order (Black-White and White-Black are different gradients), 90 combinations. The name shows the pair as a two-colour gradient ("Black-White - Cub") and the cub carries matching particle effects. Rolled independently of shiny; hitting both gives a shiny chroma cub

**Pity**: after 9 hatches in a row without a chroma, the 10th is guaranteed chroma; after 99 in a row without a shiny, the 100th is guaranteed shiny. The counters reset when one appears; the hatch page shows both as progress bars.

**Hatch all**: with two or more hatchable eggs, the hatch page offers "Hatch all (N)", capped by the free places in the nest. It plays one reveal: the rarest cub takes the spotlight and the rest are listed in a summary. When the nest is full, the Hatch and Condense buttons don't appear and the page tells you to release or expand first.

**Reveal tells**: a chroma or shiny result makes the card border glow while the egg shakes (two alternating colours for chroma, gold for shiny), and the reveal bursts with a ring and stars. Ordinary eggs don't glow.

Temperament affects combat and following as well as personality:

- **Reckless** — notices enemies farther away and pursues aggressively; +10% melee damage multiplier, -8% body armor
- **Cautious** — shorter sight range and less pursuit; +10% body armor, -5% gun damage multiplier
- **Lazy** — less eager to chase, -6% run speed, but +1 extra cub-backpack slot
- **Loyal** — stays closer to you and gains +8% max health

If an egg's bloodline can no longer be identified after a game update, the system says so and **leaves your egg untouched** rather than consuming it.

### Taking a Cub Into a Run

#### Deployment slot
- **One cub per run.** Set it as "deployed" in the nest; it does **not** take an inventory slot.
- The system spawns it beside you on entry. It acts according to the **behavior** you set (see below).
- Its damage is a bonus on top. It earns its keep with pressure and finishing blows; the source preset's base multiplier keeps bloodlines distinct before the companion safety cap.

The cub only scales its visual root, never its collider. At level 1 its body is **62%** of the official base model (**64%** for the Dragon Lord). Each level adds **5%** of its hatch size, reaching about **90%** of the base model at level 10 (**93%** for the Dragon Lord).

#### Behavior, feeding and petting
Walk up to your deployed cub; besides "Backpack", its interaction group has three more entries:

- **Behavior** (saved on the cub; new cubs and old saves default to Follow). In a raid, tapping it moves to the next behavior and the option name says which one; on the nest page you can pick any of the three directly:
  - **Follow**: tags along and never starts a fight. It joins in once you hit an enemy or an enemy hits you or it, then breaks off and returns **10 seconds** after combat ends.
  - **Aggressive**: engages any enemy it sees (the behavior before this option existed).
  - **Evasive**: never fights, stays close, **takes no damage**, and every **5 seconds** shakes off bleeding, poison, nausea, pain, burning, shock, freeze and stun. Good for using it as a pack mule.
- **Feed**: picks the **cheapest food** in your pack for **+20 experience** and **10%** health. **10-minute** cooldown per cub (real time). Multi-use foods are skipped so a whole item is never eaten at once. The option name shows which food it will eat, or how many minutes are left on the cooldown.
- **Pet**: a short reaction that depends on its temperament, plus a little hop; no stats.

The HUD shows a health bar with a thin experience bar under it, plus level and current behavior. Leveling up in a raid pops "Lv5!" over the cub with a level-up sound.

#### Cub backpack
Every cub has its own backpack: **4 slots** to start, with more from levels, endowments and the Lazy temperament. In the base or on a raid, walk up to your deployed cub and choose **Backpack** above its head to use the official inventory grid. Contents stay with that cub across maps, deployment changes and knockouts. Empty it before releasing the cub or sending it on an expedition. The nest page shows how many slots the cub has.

#### While you are there, it does not die
- A downed cub **does not die**. It is **carried off wounded**: out for the rest of the run, restored when you return to base.
- Each knockdown leaves a **scar**: when, which map, and who put it down, plus one small debuff (-2% each, filling walk speed, then run speed, then health, at most -10% per stat).
- **Scars can be treated**: select the cub on the nest page and press "Treat a scar" to spend **40** souls of its bloodline and heal its oldest scar. A treated scar moves into "old wounds": it still counts in the record but no longer carries a debuff. Cubs away on expeditions cannot be treated; the button is hidden when you don't have enough souls, and the Scars line on the nest page shows the cost and how many souls that bloodline has.
- Scars double as its service record. Each cub has a scar cap; older ones merge into an "old wounds" count.

#### Where cubs are not allowed
- **Cubs allowed**
  - The three standard difficulties
  - From Scratch / Faction War / Blood Hunt
  - Fate Echo / Zombie Mode / Sky Islands and all raid maps
- **Only barred in**
  - Black Market Duck Cup: a spectator mode; you stay out of the ring, the two Boss teams own the arena, and so does your cub
- **Base**: the deployed cub idles around the nest (spawns as a non-combat companion with combat AI disabled, preventing accidental targeting of base NPCs)

### Disaster Expeditions

This is the **only** place in the system where a cub can truly die.

#### Three destinations
- **Storm Sea**: the disaster is a thunderstorm; suits **Electric** and **Space**
- **Acid Rain Ruins**: the disaster is acid rain; suits **Poison** and **Physical**
- **Frozen Waste**: the disaster is a blizzard; suits **Ice**, **Fire** and **Ghost**

Every one of the seven elements suits one destination, so every bloodline has somewhere it fits. A matching destination (marked ★) adds `12` percentage points to the loot-success chance. It does not reduce the death rate. A bloodline's element is the one its source boss resists best; bosses with no clear resistance count as Physical. The expedition page tells you which element the cub is.

#### How many at once
- Up to **2** expeditions can be out at once, +1 each when your index reaches **10 / 20** unlocked bloodlines, up to **4**.
- Cubs that are back and waiting for their card flip don't take a team. When every team is out, the expedition page shows the count; wait for one to return.

#### Three risk tiers
The tier sets the payout, the risk and the wait all at once (the wait is real time). Base loot-success chance: Safe **85%**, Rough **70%**, Desperate **80%** (after Desperate's death roll), plus 12 points for a matching element; each tier card shows this cub's actual chance at the chosen destination.

- **Safe**: **10 minutes**, low reward, **0%** death rate. Completely safe; the worst case is coming home empty-handed. +15 experience on return
- **Rough**: **30 minutes**, medium reward, **never fatal**, about 25% chance to come back wounded. +30 experience on return
- **Desperate**: **60 minutes**, high reward, about **12%** death rate, **the only tier with real death**. You are betting a life on rare output. +60 experience on return

[tip] Per minute waited, Safe gives 1.5 experience and Rough and Desperate give 1 each if the cub survives. Desperate does not level it faster and carries a risk of permanent death. Choose Safe for steady growth; Desperate offers more loot and a chance at a Relic Egg.

Successful Safe / Rough / Desperate trips bring back `300–900 / 1200–3600 / 4000–12000` money and `10 / 40 / 90` souls of the cub's bloodline.
Successful trips also roll an extra item: **one quality-3 item** for Rough, or **one quality-4 item** for Desperate; Safe has no random-item reward. If no eligible item is available at settlement, that random item is skipped and is not queued for later delivery.
After a Desperate trip survives and succeeds, it has a further `20%` chance to award a Relic Egg of that bloodline. This is not the overall egg chance per departure.
Returning empty-handed still awards survival experience; dying awards neither experience nor loot.

**The death rate is written on the button before you commit**, and it is frozen into that departure; later changes can only lower it (when Rough stopped being lethal, Rough trips already under way settled as non-lethal too). The card also lists the cash range, souls, and loot quality and count on success.

#### Countdown and settlement
- The countdown runs on **real time**: it keeps ticking while the game is closed.
- Returning to base settles automatically, then turns the cards one by one: loot / wounded / empty-handed / a death notice. Each result has its own sound, and a death card lingers longer.
- If an expedition comes due while you are already in the base, a notification pops up. Press "See results" on the Expedition tab or in that cub's details to flip the cards right there, without closing the panel.
- The card flip is only a replay; the outcome was decided and saved the moment you walked into base. Quit mid-flip and the card is still waiting next time.
- A real death is **irreversible**: the cub leaves the nest and its name goes on the memorial.

### Relic Museum

- **Bloodline index** — every bloodline has a card: unseen ones are a silhouette marked "???", defeated ones show kills, and hatching one unlocks it. Click a card for details: element, matching expedition destinations, how many are in your nest, soul progress, and how to unlock it.
- **Memorial** — cubs lost on expeditions are carved in permanently: name, bloodline, where they fell, **the risk tier you chose**, the death rate at departure, and their career record and date. The stone spells out that you chose "Desperate".
- **Taming achievements** — First Hatch, Bloodline Collector, Every Last One, The Pale Purple One (shiny), A Name on the Stone. Listed on the museum page, one per row, with whether you have it.

### Levels

Cubs grow by working with you, capping at level 10 ("Adult"), after which they stop growing.

- **Experience**:
  - Deploying a cub and coming home alive: **+20** (a cub carried out wounded still counts; the scar is already the price)
  - Each kill by the cub, **or by you within 30 m of it**: **4**, plus **1 per 100 actual max health** of the victim (up to **16** total per kill), capped at **80** kill experience per run. Weak cubs no longer need the last hit to grow
  - Feeding: **+20** (once every 10 minutes per cub)
  - Surviving a Disaster Expedition, by tier: Safe **+15**, Rough **+30**, Desperate **+60**
- **100 experience per level**, capped at level 10.
- **Bloodline baseline** — 0.25 × the square root of the source boss character damage multiplier, bounded to 0.12-0.8 (sources 0.8 / 1.35 / 12 give about 0.22 / 0.29 / 0.80; older versions flattened every bloodline above 1.35 to 0.28). This is a character stat, not a share of final damage or DPS: weapons, fire rate and AI still matter. Custom bloodlines use their boss's base health and signature equipment.
- **Combat growth** — each level above level 1 adds +6% max health and +8% to both gun and melee damage multipliers. At level 10, level bonuses total +54% max health and +72% to each damage multiplier, applied alongside endowments, temperament and scars.
- **Mid-raid growth** — a committed level-up immediately updates combat bonuses and visual size, preserving the current health fraction. The nest detail page shows the health, damage and size bonuses provided by levels.
- **Backpack growth** — +1 cub-backpack slot at levels 3 / 6 / 9, so +3 at max, stacking with the base slots, endowment slots and temperament slots.
- The index records the highest level reached for each bloodline.

### Nest capacity and releasing

- **The nest starts with 12 places.** It expands by +4 each time your index reaches 10 / 20 / 30 unlocked bloodlines, up to 24.
- **Releasing** — click a cub's card on the nest page to select it (no need to deploy it first), then press "Release" at the bottom of the right-hand panel and confirm to get back 60 relic souls of its bloodline. Releasing is permanent and the cub is not memorialized; a cub away on an expedition cannot be released, and a cub with items still in its backpack must empty it first. When the nest is full, this is how you make room; no need to send a cub on a Desperate expedition and hope.
- **Batch release** — "Select" at the top right of the nest page switches to ticking mode: click cards to tick or untick. "Tick all common cubs" on the right ticks every ordinary cub at once; the deployed cub and shiny or chroma cubs are never ticked that way and have to be ticked one by one. Press "Release N" and confirm to release them all in one go, 60 souls back per cub. Cubs that can't be ticked say why (on an expedition, items in the backpack, favorite).
- **Favorite** — press "Favorite" next to the name in the details. Favorites can't be released, are never ticked by "tick all", and can't take Desperate runs.
- **Sort** — the Sort button above the nest list cycles arrival order, level, bloodline and rarity. It only changes the display order.

### Tips
- Souls condense eggs and treat scars. Bank **240** of one bloodline and you can target that boss's egg directly, so unlucky players can still complete the index; treating one scar costs 40.
  Releasing a cub refunds 60 souls, so four released cubs pay for a fresh egg.
- When the nest is full, expand or release: unlocking more bloodlines raises the cap, releasing frees a place immediately and refunds some souls.
- Eggs are easiest to track in the top level of your bag or storage. Eggs tucked inside crates and containers are now found too, but the outer level keeps the count obvious.
- Want a safe trickle? Send Safe runs. Want to gamble? Read the death rate before you press Desperate.
- A cub on an expedition cannot be deployed or removed; while it is away, it can't fight with you.
