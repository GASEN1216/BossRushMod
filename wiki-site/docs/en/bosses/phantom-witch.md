# Phantom Witch

## Overview
The Phantom Witch is the third custom Boss in BossRush Mod. She roams the battlefield by alternating between blinks and stealth, combining Curse Realms, scythe sweeps, and undead summoning in a three-phase fight. Defeating her grants a 50% chance to drop her exclusive melee weapon, Soulreaper's Requiem.

## Base Stats
- HP: 1000
- Damage Multiplier: 1.1x
- Size: 2x a regular ghost
- Held Weapon: Soulreaper's Requiem

## Phase Thresholds
- Phase 1: 100% ~ 60% HP (attack interval 1.2s)
- Phase 2: 60% ~ 25% HP (attack interval 0.85s)
- Phase 3: below 25% (attack interval 1.1s, last-stand summoning)

## Attack Skills

### Tracked Teleport
Her most important move. The Witch places a violet marker about 2.2m from the player, and **the marker follows you for 2 seconds**, locking at its final position before the Witch blinks there and immediately sweeps.
- Sweep damage: 18, range 3.1m, 170° arc
- Moving one step when the marker appears isn't enough — keep strafing until it locks, or she'll land on your retreat path

### Scythe Sweep (Standalone)
- Windup: 0.35s
- Damage: 18
- Range: 3.1m, 170° arc

### Requiem Arc
- Windup: 0.55s
- Damage: 16
- Range: 4.8m midrange sector

### Wraith Trail
- Windup: 0.45s (with a ~3m outline warning)
- Two-part hit: first cone at ~3.2m, **second cone at ~3.6m after 0.3 seconds**
- 18 damage per hit
- Many players dodge the first hit and turn back for damage, only to be caught by the second

### Curse Realm
- Shows a 1.05-second shrinking warning ring under the player, then creates a damage zone
- Phases 1/2: 4.5m radius, lasts 4 seconds
- Phase 3: radius shrinks to 3.6m, lasts 3 seconds
- 15 ghost damage every 0.5 seconds, applies curse slow

### Undead Summoning (Phase 3)
- 1.0s windup, summons 2 ghost minions (max 2 alive at once)
- Minion HP: 150
- Two roles:
  - **Sustain**: heals the Witch for 6 HP/s, boosted to 9 HP/s when within 6m
  - **Harass**: applies one curse stack every 2.4s to players within 3.2m

## Stealth System
The Phantom Witch cycles between true stealth, semi-stealth, and visible states:
- Phase 1: ~38% of the time in stealth
- Phase 2: ~32% of the time in stealth
- Phase 3: ~18% of the time in stealth
- True stealth max duration: 1.1s

## Curse Debuff
- Duration: 5 seconds
- Max stacks: 3
- Per-stack slow: -30% move speed
- At 3 stacks, that's -90% — you're practically rooted

::: warning
Be wary at one curse stack. If you eat another from a Curse Realm or the Harass minion while already cursed, reaching three stacks means you probably can't escape the next attack.
:::

## Tactical Package Rotation

### Phase 1 (interval 1.2s)
Tracked Teleport → Requiem Arc → Wraith Trail

### Phase 2 (interval 0.85s)
Tracked Teleport → Requiem + Trail Combo → Curse Realm → Tracked Teleport

### Phase 3 (interval 1.1s)
Short Drift → Undead Summon → Curse Realm → Minion Retreat

## Drops

**Exclusive equipment**

- Soulreaper's Requiem (melee weapon): **50%** extra drop chance

Unlike the two dragons, the Witch's drop is an **independent extra roll**: when it hits, the
scythe is appended to the loot crate without displacing anything already in it.

**What every Boss drops on top of that**

- **A regular loot crate** - size scales with its health
- **Relic souls** - guaranteed, bankable toward a Phantom Witch bloodline egg
- **A relic egg (Witch bloodline)** - about 4%, landing on the body, so search it
- **An Affix Forge Stone** - about 8%
- **A Phantom Spore** - about 25%, **requires Duck King Campaign chapter 1 to unlock the garden**.
  Grows Shadow Mushroom: -10% physical damage taken for your next run

## Combat Tips
- **The violet tracking marker is the key signal**: keep strafing for 2 seconds until it locks, don't just take one step
- Curse Realm has a ~1s warning ring — leave the area as soon as you see it
- Wraith Trail requires waiting for the second hit (0.3s later) before it's safe to turn back for damage
- Phase 2 ramps up attack tempo significantly — keep moving constantly
- **In Phase 3, kill the Sustain minion first** — without removing it, the Witch heals 6-9 HP/s and your damage goes to waste
- Then deal with the Harass minion's curse pressure, and finally return to the Witch
- Don't chase the semi-transparent model — wait for the tracking marker to lock before looking for damage windows
- Ranged players should circle in open ground, keeping markers and Realms in different spots
- Melee players should treat each entry as a short trade: wait for the teleport sweep to end, hit from the side, then disengage
- At 3 curse stacks you lose 90% move speed — avoid eating consecutive curse abilities
- Don't peek the same cover repeatedly — the 2-second tracking marker can deliver a teleport sweep behind it

## Spawn Restrictions
- Standard BossRush and Infinite Hell: not in the strong-Boss exclusion list, can appear normally
- From Scratch: eligible from the first wave
- Faction War: joins the normal draw
- Blood Hunt: joins the normal draw

## Related Achievements
- **Ghost Hunter** — First kill
- **Perfect Exorcism** — No-damage kill
- **Requiem Collector** — Obtain Soulreaper's Requiem
