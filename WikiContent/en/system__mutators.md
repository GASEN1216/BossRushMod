## Mutator System

### What Is It?

At the start of every run, the system draws a handful of mutators from a pool of 28 and applies them immediately for the entire run. **How many is fixed**, set by `mutatorCount` in the config (**3** by default, adjustable 1-10); what gets drawn is the random part. Mutators can buff enemies, buff the player, or change environment rules.

> **Zombie Mode is excluded**: it has its own independent in-run buff system and does not use this mechanic.

### Applicable Modes

- **Standard BossRush** — Rolls mutators?: √
- **Infinite Hell** — Rolls mutators?: √
- **From Scratch (Mode D)** — Rolls mutators?: √
- **Faction War (Mode E)** — Rolls mutators?: √
- **Blood Hunt (Mode F)** — Rolls mutators?: √
- **Fate Echo (Mode G)** — Rolls mutators?: × (the nine-wave counter schedule is fixed)
- **Black Market Duck Cup (Mode H)** — Rolls mutators?: × (what the odds sheet says is what you get)
- **Zombie Mode** — Rolls mutators?: × (separate system)

### How to See Active Mutators

- An **ACTIVE MUTATORS** list appears on the left edge after the run starts and shows the total count
- Every row is labeled **Enemy / Boon / Rule**, with category colors for quick scanning
- Hover a row to open the full descriptions on the right; the row you are reading is highlighted
- The detail panel scrolls when the list is long, and remains open while moving the pointer from the compact list into the details

### Configuration

- **Toggle**: `enableMutators` (default: `true`)
- **Count**: `mutatorCount`, range 1–10, default **3** per run
- Adjustable via ModConfig UI or config file

---

### Mutator Pool (28 Total)

#### ◆ Enemy Buffs (9)

- **Swift Storm** — Effect: All enemies movement speed **+30%**
- **Iron Fortress** — Effect: All enemies max HP **+50%** (existing enemies gain HP immediately)
- **Bullet Rain** — Effect: All enemies fire rate **+25%**
- **Giants** — Effect: All enemies size **×1.4**, HP **+40%**
- **Ratswarm** — Effect: All enemies size **×0.6**, speed **+45%** (smaller and faster)
- **Bloodhounds** — Effect: All enemies have infinite aggro range — **permanently lock onto you**
- **Vicious** — Effect: All enemies deal **+30%** gun and melee damage
- **Enemy Marksman** — Effect: All enemies gun scatter **-25%** (shots are tighter)
- **Frenzy** — Effect: All enemies movement speed and fire rate **+20%**

#### ★ Player Boons (11)

- **Fleet Footed** — Effect: Player walk/run speed **+35%**
- **Sharpshooter** — Effect: Player gun crit rate **+30%**
- **Trigger Discipline** — Effect: Player fire rate **+25%**
- **Steady Aim** — Effect: Player gun scatter **-30%**
- **Fast Hands** — Effect: Player reload speed **+40%**
- **Lethal Strike** — Effect: Player gun crit damage **+50%**
- **Long Shot** — Effect: Player gun range **+30%**
- **Melee Master** — Effect: Player melee damage **+40%**
- **Tank** — Effect: Player max HP **+30%**
- **Field Medic** — Effect: Player healing effectiveness **+50%**
- **Lucky Star** — Effect: Player gun and melee crit rate **+20%**

#### ※ Environment Rules (8)

- **Hemorrhage** — Effect: Bleed damage speed **×1.5**; Note: Blood Hunt (Mode F) only
- **Festering Wounds** — Effect: All healing effectiveness **-40%**
- **Undying** — Effect: Bosses regenerate **5% HP every 10 seconds**
- **Glass Cannon** — Effect: Player damage **+50%**, but armor is zeroed; Note: Applies to gun and melee
- **Blitz** — Effect: Player move speed **+40%**, but max HP **-20%**; Note: Current HP is clamped to the new cap
- **Lifesteal** — Effect: Killing an enemy restores **8% max HP**
- **Blood Pact** — Effect: Direct player kills restore **16% max HP**, but healing **-30%**; Note: Direct player kill credit only
- **Volatile Remains** — Effect: Enemies explode on death (3m radius, 40 fire damage); Note: **Can injure you** — watch spacing

---

### FAQ

**Q: Are player boon mutators equally likely?**  
Entries eligible for the current mode have equal weight and are drawn without repeats. Hemorrhage is eligible only in Blood Hunt. You might roll both "Giants" and "Glass Cannon" in the same run.

**Q: Can I reroll or choose mutators?**  
No. The draw is random and non-configurable mid-run.

**Q: Do mutators carry over between runs?**  
No. All mutators are cleanly removed on any run-end (clear, death, or manual exit).

**Q: Does Volatile Remains chain-explode?**  
No. Only the kill you land yourself sets off an explosion; kills caused by that explosion do not set off more. There is no infinite chain.
