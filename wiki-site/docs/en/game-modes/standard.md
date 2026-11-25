# Standard BossRush

## How It Plays

The bread-and-butter mode. Buy a ticket, pick a map, walk into the arena, choose your difficulty at the signpost, and start fighting Bosses. Kill 'em all, grab your loot, and evacuate.

## Entry

- **BossRush Ticket** (from the base merchant)

## War Writ: Set Your Own Terms

In the arena, pick **War Writ** at the signpost. Choose one option in each of the three rows on the left; the notice on the right spells out that challenge's rules and rewards. Press **Swear** on the notice to start, or **Wait** to put it away. Next time it opens on whatever you picked last.

**Lineup**

- **A Snap (Easy)** — 1 Boss per wave. The tutorial difficulty
- **Getting Interesting (Normal)** — 3 Bosses per wave. The real deal
- **Infinite Hell** — Unlimited waves, no escape. See Infinite Hell mode

**Challenge** (arena only, stacked on top of the global multiplier in settings)

Each challenge is a stack of rules, and like roguelike ascensions every step adds more. Rules come in four kinds: **Foes** (enemies get stronger), **Self** (you get weaker; on Relaxed these are graces), **Field** (arena and pacing) and **Omen** (the same for everyone; it's how you use it).

- **Relaxed** — 3 graces
  - Holding Back: Boss HP ×0.6, damage ×0.7
  - Field Medic: healing +50%
  - At Ease: rest between waves ×1.5
  - Clear crate holds half as many items (at least 1), Hell cash ×0.5
- **Standard** — No extra rules, same as before
- On Relaxed and Standard the sky changes every 3-4 waves (clear, rain, snow, dusk, night). It's only a look; no numbers change
- **Hard** — 6 rules
  - Whetted Blades: Boss HP ×1.3, damage ×1.15, 15% quicker to act
  - Fell Aspect: each Boss bears 1 affix, the headliner 2
  - Hex: a light curse each wave, swapped the next
  - Forced March: rest between waves halved, no extra rest every 5 waves
  - Fickle Skies (Omen): the weather turns every 4-5 waves
  - Calamity (Omen): a calamity every 30-45s that hurts both sides
  - Clear crate items start at tier 6, Bosses with affixes drop 1 extra item, Hell cash ×1.25
- **Nightmare** — 10 rules
  - Killing Intent: Boss HP ×2.5, damage ×1.75, 50% quicker to act
  - Dread Aspect: each Boss bears 3 affixes of different kinds; the headliner gets one of all 4
  - Blinded: narrower sight, Boss health bars show only a few segments, no Boss beacons on the map
  - Cut Supply: using items takes twice as long, items get sealed for 8s now and then, Bosses drop half the ammo
  - Blood Debt: you heal only by killing; every Boss hit cuts your max HP by 3% (up to 30%), restored when the wave ends
  - Dire Hex: a heavy curse each wave, swapped the next
  - Forced March, as on Hard
  - City Ablaze: 30s into each wave a fire wall closes in; outside it you lose 4% HP per second
  - Upheaval (Omen): the weather turns every 2-3 waves
  - Chained Calamities (Omen): a calamity every 15-25s, a 1-in-4 chance of two at once
  - Clear crate items ×1.5 starting at tier 6, Bosses with affixes drop 2 extra items, Hell cash ×2; every Boss gets an ink banner on entry
- Omens, calamities, affixes, curses and the fire wall are detailed under "Rule Details" below
- Runs started from the War Writ roll no random mutators; the challenge you picked is the only difficulty. Relaxed's Field Medic borrows that mutator's effect; it's listed on the notice, not in the mutator list
- Each Boss's own drop crates are the same on every challenge, so Relaxed doesn't cost you loot

**March** (fixed to the end on Infinite Hell)

- **Short March** — 12 waves, the last one is a strong headliner Boss
- **Long March** — 24 waves, also ending on a headliner
- **Full Campaign** — Every enabled Boss, same as before
- Short and Long March clear crates hold base items × √(waves ÷ enabled Bosses), rounded up, then adjusted by the challenge. Dying or leaving mid-run still pays nothing

## Rule Details

On Hard and Nightmare the omens, calamities, affixes and curses are rolled during the run. In the run you only see their names in the corner on the right; what they do is all here.

**Omens** (the omen's name is written across the top of the screen when the weather turns)

- Blizzard: everyone moves 15% slower, and Frost shows up more often
- Thunderstorm: lightning flashes on the horizon, and Lightning shows up more often
- Blood Moon: Bosses move 25% faster and hit 20% harder
- Endless Night: view distance ×0.45, view angle ×0.55, close-range awareness ×0.6
- Downpour: view distance ×0.75; Miasma and Molten Rift last half as long
- When several "can't see far" effects stack, you always keep at least 30% of your sight

**Calamities** (they hurt both sides: you take the full share of your max HP, Bosses take 30%. They mostly land around you, sometimes around a Boss. The rings and gold lines on the ground are the real areas)

- Meteor Rain: 6-10 marks light up one by one, then fire meteors hit 1.5s later, 16% HP each (fire)
- Lightning: three bolts strike at your feet one after another, each with a silver ring 1s ahead, 20% HP each (electric)
- Miasma: 2-3 clouds that linger for 20s, 3.5% HP per second inside (poison)
- Molten Rift: a crack glows, erupts and stays for 15s, 9% HP per second on contact (fire)
- Ink Tide: a whole wall of ink sweeps across the arena, with two gold lines marking the gap; getting caught costs 14% HP and knocks you back, and you can roll through it
- Gale: 10s of side gusts. The wind rises first, then it pushes, both sides alike
- Vortex: pulls toward the center, harder the closer you are, 8% HP per second at the core
- Frost: one or two ice patches for 14s, 40% slower while standing on them, both sides alike
- Fogmire: a thick fog for 16s. Inside it you can't see far, and enemies in the fog (or all enemies, while you're in it) can't see you beyond 3m

**Affixes** (four kinds, two each. They're written in front of the Boss's name, as in "Splitting · Frenzied", and the Boss glows in the kind's color)

- Clone kind (silver)
  - Splitting: on death it splits into two smaller copies with 35% HP each
  - Twinned: it arrives as a pair, the second with 60% HP. If you don't drop both within 10s, the first to fall is raised with half its HP. A pair can be raised at most twice
- Guard kind (old gold)
  - Escorted: 3 guards follow it; until they're cleared the Boss takes 80% less damage
  - Summoner: calls 2 shades every 20s, at most 6 on the field
- Motion kind (ink violet)
  - Blinking: every 7-11s a puff of smoke, and half a second later it's behind you
  - Frenzied: below half HP it grows to ×1.3 size and moves 40% faster
- Death kind (cinnabar)
  - Haunting: after death its ghost chases you for 15s, 12% HP per second on contact; you can roll through it
  - Volatile: 3s after death it blows up, 6m radius, 35% HP (fire); you can roll out of it
- Copies, twins, guards and shades are all smaller versions of that Boss. Guards and shades have 12% HP and half damage, and scatter when their master falls
- With Splitting or Twinned the wave only ends once every body is down. If a body gets stuck somewhere you can't reach, it dissolves after two and a half minutes
- Content Bosses with their own story phases (Dragon Descendant, Champion's Shadow and the like) only get Motion and Death affixes
- Bosses with affixes drop extra loot from their own pool: 1-2 affixes give 1 extra item, 3-4 give 2. Copies, guards and shades drop nothing, and the clear crate doesn't change

**Curses** (one per wave, named in the corner on the right. Hard rolls light curses, Nightmare heavy ones; heavy values are in brackets)

- Butterfingers: reload speed -25% (-45%)
- Leaden: stamina recovery -40% (-65%)
- Jitters: scatter +50% (+100%)
- Flinch: after an enemy hits you, -25% move speed for 1.2s (-40% for 1.8s)
- Nearsighted: view distance ×0.75 (×0.6)

**City Ablaze** (Nightmare)

- When each wave starts, a ring of fire rises around the arena. Its center sits in the middle of the map's Boss spawn points, and its radius is the farthest spawn point plus 10m. The ring is always marked on the M map
- 24s into the wave, a gold line and twelve seals appear on the ground (and on the M map) where the wall will stop
- From 30s, the wall closes in over 14s to 60% of its size, or 40% on the headliner wave
- Outside the ring you lose 4% HP per second. It's fire damage, so fire resistance gear still helps, and rolling doesn't dodge it. If the screen edges glow with firelight, you're outside
- The wall only burns you; Bosses don't mind it. When the wave ends the wall recedes outward and dies down, and it rises again with the next wave

## How It Works

1. Swear the writ → first wave spawns
2. Kill all Bosses in the wave
3. Loot crates drop at death locations
4. Next wave auto-starts after 15s (or rush it via signpost if enabled in config)
5. On-screen compass shows Boss directions (E/W/S/N)
6. Multi-Boss waves = surrounded from all sides. Fun!

Screen suddenly going red, a supply crate falling out of the sky, or a Boss that clearly isn't part of this wave walking in? Those are timed random events, and Standard BossRush is one of the three modes that roll them. Each one is announced, runs for a set time, and cleans itself up. See the "Random Events" page.

## Boss Pool

- Every Boss in the game is in the pool
- The initial order tries to move heavy hitters (Four Horsemen, Koko Koko, Dragon Descendant, Skyburner Dragon Lord) beyond the first 20 candidate positions. Short pools, too few ordinary Bosses or filtering can let them appear earlier. The Phantom Witch is not excluded
- Use the **Boss Filter** to disable specific Bosses

## Clear Rewards

- After the last wave, the reward crate shows up as a glowing ghost crate above you, follows you down, and becomes a normal crate you can open once it lands.
- **Easy**: Reward crate with 3 high-quality items
- **Normal**: Reward crate with 10 high-quality items
- These counts are for Standard challenge on a Full Campaign; other choices scale as described under War Writ

After clearing, an evacuation point opens. Interact to return to base.

## Arena Facilities

- **Ammo Shop** — Buy ammo
- **Repair Station** — Fix durability
- **Trash Can** — Clean up loot crates (clear boxes or clear all)
- **Return to Spawn** — Teleport back to the safe zone

## Arena NPCs

- **Awen** (Courier): Always present, storage & retrieval
- **Dingdang or Yu Zhi** (random): Reforge/shop or healing (married NPCs excluded from random pool)

## Related Achievements

- **First Steps** — First clear on any difficulty
- **Easy Peasy / Getting Serious** — Clear the respective difficulty
- **Flawless Easy / Flawless Normal** — No-damage clears
- **Lightning Run / Speed Demon / Time Assassin** — Speedrun achievements
- **Nightmare Cleared** — Clear a Full Campaign on the Nightmare challenge
- Clear achievements only count on a Full Campaign with Standard or harder

See the full achievement list for details.
