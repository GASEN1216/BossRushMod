# Dragon Descendant

## Overview
Dragon Descendant is the first custom boss in BossRush Mod, featuring a two-phase combat system. It is your entry point to the custom boss drop system — defeating it yields the Dragon Set and Dragon Breath weapon.

## Base Stats
- HP: 500
- Contact Damage: 20 (1.5m range, 0.5s cooldown, knockback force 10)
- Equipment: Crimson Dragon Helm, Flame Scale Armor, Dragon Breath
- **Fire Immune**: Fire damage not only deals no damage, it actually heals the boss

## Combat Phases

### Phase 1 (Full HP ~ First Lethal Hit)
- Shooting: Uses Dragon Breath for standard gunfire; every 10th shot triggers a small blast at the player's feet if within 5m (5 fire damage, 1m radius) — staying 5m+ away avoids this entirely
- Incendiary Grenade: Thrown every 5 seconds, always aimed at the player's feet
- Phase 1 damage multiplier is low (0.3x), mainly to let you learn its attack patterns

### Revival Mechanic
Dragon Descendant doesn't die when its HP first hits zero. When it takes lethal damage:

1. HP locks at 1, and it says: **"I... will not fall!"**
2. Throws incendiary grenades in eight directions
3. Restores to 50% of maximum HP and enters the frenzied Phase 2

This triggers only once. Note: **it's NOT triggered at 50% HP** — it triggers on the first lethal hit.

::: warning
When you see 1 HP and the revival line, create distance! Standing on the body waiting for loot is the easiest way to get hit by the eight-way incendiaries and contact damage at the same time.
:::

### Phase 2 (After Revival)
After revival it enters a frenzied state with a completely new rhythm:
- Damage multiplier increased to 1.1x
- Contact damage now procs on touch
- Incendiary frequency increased to every 1 second
- Fixed attack loop: **10 straight shots → 0.5s rush → 30-shot fan sweep (60° arc, 3 seconds) → repeat**
- Glowing aura around its body
- **Ice Vulnerability**: After taking cumulative ice damage equal to 10% of max HP, it's slowed for 10 seconds

## Drops

**Exclusive equipment** - each kill drops **exactly one** of the three, weighted (not independent rolls):

- Flame Scale Armor (Armor): **60%**
- Crimson Dragon Helm (Helmet): **30%**
- Dragon Breath (Firearm): **10%**

The three add up to 100%, so you are **guaranteed** one piece - but collecting all three takes
several kills, and Dragon Breath is the stubborn one.

**What every Boss drops on top of that** (parallel, never displacing each other)

- **A regular loot crate** - size scales with its health
- **Relic souls** - guaranteed, bankable toward a Dragon Descendant bloodline egg
- **A relic egg (Descendant bloodline)** - about 4%, landing on the body, so search it
- **An Affix Forge Stone** - about 8%
- **A Dragon Seed** - about 25%, **requires Duck King Campaign chapter 1 to unlock the garden**.
  Grows Dragon Breath Fruit: +10% gun and melee damage for your next run

## Combat Strategy
- Phase 1 pressure is low — keeping 5m+ distance avoids most damage
- Watch for the incendiary arc — move away from your current spot when you see the throw
- On first lethal hit, **don't rush in to loot** — create distance and watch for gaps in the eight-way incendiaries
- Don't use fire DoT during the revival sequence — it only heals the boss
- In Phase 2, read the loop: strafe during straight shots, dodge the rush, then use the short pause after the fan sweep as your best damage window
- Melee players shouldn't facetank — the 1.5m contact hitbox plus the 5m blast check means taking multiple damage instances at once
- **Ice weapons/ammo shine in Phase 2**: stack up the slow threshold, attack from the side while it's slowed, then disengage before the next rush
- Wearing Dragon Set or Dragon King Set turns fire damage into healing for you
- Melee players should wait for the ice slow to trigger before committing

## Spawn Limits
- Standard BossRush: excluded from the first 20 waves
- Faction War: Max 1 per session
- Blood Hunt: Max 1 per session

## Related Achievements
- **Dragon Heir Hunter** — First kill (reward: 30,000)
- **Perfect Dragon Hunt** — No-damage kill (reward: 200,000)
- **Dragon Heir Collection** — Collect all exclusive drops (reward: 300,000)
