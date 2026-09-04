## Reforge System

For the full achievement list, see the "Achievement List" page.

### Overview
- The Reforge system allows you to re-randomize equipment stats, serving as the core equipment progression mechanic in BossRush Mod.
- Accessed through Dingdang (the goblin artisan)'s Reforge service.

[tip] Dingdang also runs a separate service called **Affix Forging**: reforging changes **numbers**, affixes change **behavior** (kill explosions, lifesteal on hit, armor thorns). The two never interfere - affixes are never wiped by a reforge and never appear in the reforge stat-lock list, and one item can have both. See the "Affix Forging" page.

### Reforgeable Equipment
- The following types of equipment can be reforged:
  - Armor, helmets, masks, backpacks, headsets
  - Firearms, melee weapons
  - Totems
- Reforge only rerolls the item's own stats — the ones you can see on its detail panel.

#### Properties Excluded from the Reforge Pool
- The hidden data an item uses to track its own state is never rerolled. You can't see it on the detail panel, and Reforge can't touch it.
- Temporary effects picked up during a run (bonuses from buffs and debuffs) are exactly that — temporary. They don't count as the item's own stats, so Reforge never touches them.
- A melee weapon's swing timing is never rerolled — otherwise the same blade would feel different after every reforge.

### Reforge Process
- Interact with Dingdang and select "Reforge".
- Place the equipment you want to reforge.
- Adjust the investment amount (higher investment = better odds of a good result).
- If needed, use Cold Quench Fluid to lock stats you don't want changed.
- Click Reforge.

### Reforge Cost
- Base cost = **equipment value / 100**, minimum **100**. That's the bench fee for one attempt.
- Dingdang Affinity discount: Lv.3 = 10% off, Lv.6 = 15% off, Lv.10 = 20% off.

### Reforge Results
- Each Reforge changes all unlocked stats simultaneously. Results are influenced by:
  - Investment amount: higher amounts increase the probability of stats changing in a positive direction
  - Equipment quality: higher quality increases the upper bound of change magnitude
  - Equipment value: affects the baseline change magnitude

#### Stat Change Range
- Stat values will not exceed reasonable bounds (there are upper and lower limits).
- When a stat reaches its extreme value, a "Max" or "Min" tag is displayed.
- Each stat is guaranteed at least a minimum amount of change per Reforge.

### Cold Quench Fluid Lock
- Cold Quench Fluid is the most critical material in the Reforge system.
- In the Reforge interface, you can use Cold Quench Fluid to lock individual stats.
- Locked stats will not change during Reforge.
- Once you roll a satisfactory core stat, lock it with Cold Quench Fluid, then continue reforging other stats.

#### How to Obtain Cold Quench Fluid
- Dingdang Affinity Lv.4: level-up reward.
- Dingdang's shop: available for purchase once Affinity requirement is met.
- Married Dingdang: gifted through daily chat.

### Reforge Data Persistence
- Reforge results are permanently saved on the equipment.
- Supported reforged stats are automatically restored after scene changes or save/load.
- If an old item still carries a reforge result this version no longer supports, it is cleared on load and that stat returns to the value the item originally shipped with.
- No need to worry about losing Reforge results.

### How much money to invest

On top of the bench fee you can **invest extra money** to improve your odds of a good roll. The
curve scales with **multiples of the item's value**, not with an absolute amount:

- Invest **10x** the item's value → roughly **+10%** chance of a positive change
- Invest **100x** → roughly **+30%**
- Invest **1000x** → roughly **+100%**

So the returns fall off fast: going from 10x to 100x costs ten times the money and buys twenty
percentage points.

[tip] In practice: on cheap gear just throw 1000x at it, the base is small anyway. On valuable gear **100x** is usually the sweet spot - beyond that, your money buys more by funding extra attempts than by pushing one roll.

### Tips
- Don't waste Reforge resources on temporary equipment — first decide which equipment you'll use long-term.
- Reforge multiple times to find core stats first, then lock them with Cold Quench Fluid.
- Think in **multiples**, not absolute cash: stop at 100x on valuable gear and spend the rest on more attempts.
- Affinity discounts significantly reduce long-term Reforge costs — build Affinity first, and note the same discount **also applies to affix forging**.
- Settle the stats before you forge affixes. The other order works too, but fixing the numbers first makes it easier to judge whether a piece is worth investing in.
