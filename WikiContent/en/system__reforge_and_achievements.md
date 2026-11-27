## Reforge System

For the full achievement list, see the "Achievement List" page.

### Overview
- The Reforge system allows you to re-randomize equipment stats, serving as the core equipment progression mechanic in this mod.
- Accessed through Dingdang (the goblin artisan)'s Reforge service.

[tip] Dingdang also runs a separate service called **Affix Forging**: reforging changes **numbers**, affixes change **behavior** (kill explosions, lifesteal on hit, armor thorns). The two never interfere - affixes are never wiped by a reforge and never appear in the reforge stat-lock list, and one item can have both. See the "Affix Forging" page.

### Reforgeable Equipment
- The following types of equipment can be reforged:
  - Armor, helmets, masks, backpacks, headsets
  - Firearms, melee weapons
  - Totems
- Reforge only rerolls the item's own stats, the ones you can see on its detail panel.

#### Properties Excluded from the Reforge Pool
- The hidden data an item uses to track its own state is never rerolled. You can't see it on the detail panel, and Reforge can't touch it.
- Temporary effects picked up during a run (bonuses from buffs and debuffs) are exactly that: temporary. They don't count as the item's own stats, so Reforge never touches them.
- A melee weapon's swing timing is never rerolled; otherwise the same blade would feel different after every reforge.

### Reforge Process
- Interact with Dingdang and select "Reforge".
- Place the equipment you want to reforge.
- Adjust the investment amount (higher investment favors larger changes).
- Adjust the benefit tendency separately. Favoring benefits makes helpful changes more likely; lower vertical and horizontal recoil counts as a benefit.
- If needed, use Cold Quench Fluid to lock stats you don't want changed.
- Click Reforge.

### Reforge Cost
- Base cost = **equipment value / 100**, minimum **100**. That's the bench fee for one attempt.
- Dingdang Affinity discount: Lv.3 = 10% off, Lv.6 = 15% off, Lv.10 = 20% off.

### How much money to invest

On top of the bench fee you can **invest extra money** to favor larger changes. The
curve scales with **multiples of the item's value**, not with an absolute amount:

- Invest **10x** the item's value → roughly **+10%** to the magnitude factor
- Invest **100x** → roughly **+30%**
- Invest **1000x** → roughly **+100%**

So the returns fall off fast: going from 10x to 100x costs ten times the money and buys twenty
percentage points.

[tip] In practice: on cheap gear just throw 1000x at it, the base is small anyway. On valuable gear **100x** is usually the sweet spot - beyond that, your money buys more by funding extra attempts than by pushing one roll.

### Reforge Results
- Each Reforge changes all unlocked stats simultaneously. Results are influenced by:
  - Investment amount: higher amounts favor larger changes; benefit tendency separately controls whether a change is helpful or harmful
  - Equipment quality: affects the distribution of change magnitudes
  - Equipment value: affects the baseline change magnitude

- Green means a benefit and red means a penalty; arrows show the numeric direction. Reduced recoil has a green down arrow, increased recoil a red up arrow.

#### Stat Change Range
- Every Reforge rolls again from the item's **original value**. It does not build on the previous result, so rerolling many times does not keep pushing a stat higher.
- Each stat stays within **50%-150%** of its original value. Stats that start at 0 stay within **±0.25**; armor stays within **±1** of its original value.
- When a roll lands in the top magnitude band, the stat goes straight to its cap or floor and shows a "Max" or "Min" tag. The more money you invest, the more often that happens.
- The numbers in the Reforge panel, the bracketed change and the Max / Min tags only look at the item itself. Bonuses or penalties from attachments on a gun are left out; the item details in your inventory still show the real value with attachments.
- **Pity**: each stat keeps its own count of how many Reforges in a row missed its best limit (Max where higher is better, Min for stats like recoil where lower is better). The longer the streak, the better the odds that the next Reforge lands right on it, slowly at first and then faster: about 25% on the 50th, guaranteed on the 100th. The count restarts once it lands; locked stats are skipped and their count stays put. Pity always gives the good end, whatever your benefit tendency, and on pity alone it comes about once every 27 Reforges. Gear reforged before this update starts counting from 0 and keeps its current values.
- Lock a value you like with Cold Quench Fluid, or the next Reforge will roll it again.

### Cold Quench Fluid Lock
- Cold Quench Fluid is the most critical material in the Reforge system.
- In the Reforge interface, you can use Cold Quench Fluid to lock individual stats.
- Locked stats will not change during Reforge.
- Once you roll a satisfactory core stat, lock it with Cold Quench Fluid, then continue reforging other stats.

#### How to Obtain Cold Quench Fluid
- Dingdang Affinity Lv.4: level-up reward.
- Dingdang's shop: available for purchase once Affinity requirement is met.
- Married Dingdang: gifted through daily chat.

### Unlocking with Tempering Solvent
- Locked the wrong stat? Carry **Tempering Solvent** and locked stats turn from gold to **blue** in the Reforge screen. Click one twice (the second click confirms) to spend 1 bottle and unlock it. The value stays and the stat can be reforged again.
- One bottle per stat; if the bottle can't be taken, the stat is locked again so nothing is wasted.
- Source: Dingdang's shop from Affinity Lv.4 (stock 3, refreshed each visit); Dingdang gives **2 bottles once at Affinity Lv.6**, and players already past Lv.6 receive them the next time they chat with him.

### Reforge Data Persistence
- Reforge results are permanently saved on the equipment.
- Supported reforged stats are automatically restored after scene changes or save/load.
- No need to worry about losing Reforge results.

### Tips
- Don't waste Reforge resources on temporary equipment. First decide which equipment you'll use long-term.
- Reforge multiple times to find core stats first, then lock them with Cold Quench Fluid.
- Think in **multiples**, not absolute cash: stop at 100x on valuable gear and spend the rest on more attempts.
- Affinity discounts cut long-term Reforge costs a lot, so build Affinity first. Note the same discount **also applies to affix forging**.
- Settle the stats before you forge affixes. The other order works too, but fixing the numbers first makes it easier to judge whether a piece is worth investing in.
