# Blood Hunt

## What Is It?

The hardest mode in BossRush. **You're bleeding out from the moment you enter.** Killing Bosses is your main sustain tool, but not your only support option. Survive four escalating phases and evacuate before you bleed dry. Good luck.

## Entry

- **Naked** — no equipment
- **BossRush Ticket** + **Bloodhunt Transponder** (both consumed)
- No Banner (otherwise you enter Faction War)

## Four Phases

- **Preparation** (180s, 1%/sec bleed) — Loot, kill early Bosses, set up fortifications
- **Bounty** (180s, 1.5%/sec) — Bounty list generated; hunt marked Bosses for big rewards
- **Hunt Surge** (180s, 2%/sec) — All Bosses go on full assault. **Kill or die**
- **Extraction** (unlimited, 3%/sec) — Extraction point spawns far away. **Run. Now.**

Bleed is true damage based on your initial max HP. Armor won't help.

## Bounty System

When the Bounty Phase begins, all surviving Bosses get marked.

### Bounty Marks

- Kill a bounty Boss → you get +1 Mark
- Bosses that kill each other inherit victim's Marks + get stat bonuses (+5% HP/damage per mark)
- Most-marked entity = **Bounty Leader**, prioritized by other Bosses

### Bounty Radar

When a bounty target moves off-screen, the radar pins it to a safe screen edge:

- **Red-orange rings** track up to the 5 nearest regular bounty targets
- A separate **gold ring** tracks the Bounty Leader with a LEADER label and pulse
- `xN` inside the ring is its mark count; a direction arrow and distance label show where to move
- The edge marker disappears when the target returns on-screen
- The radar hides while the full map or another interaction screen is open

## Kill Rewards

- **Regular Boss**: Heal 30% of your entry max HP, max HP +4% of entry max HP
- **Bounty Boss**: Heal 45% of entry max HP, plus 5% per extra mark up to 60%; max HP +4% of entry max HP per mark (at least one), plus extra high-quality drops
- **Kill drops**: 1 Cover Pack per kill / 1 Repair Spray per 3 / 1 Roadblock per 10 / 1 Barbed Wire per 20

Bounty Boss drops = additional high-quality items equal to its mark count.

## Bloodfire Overload

- Run-only max-HP growth caps at **+50% of entry max HP**, so total max HP cannot exceed 150% through this mechanic; at +4% per kill that cap is reached in roughly 13 regular kills
- Growth earned past that cap becomes `0–100` Bloodfire charge: each regular kill adds 8 charge, so about 13 more kills fill the gauge (roughly 25 kills to the first Overload)
- At full charge, a **15-second Overload** starts automatically: gun and melee damage +40%, movement speed +15%, but Mode F bleed ×2 and you immediately receive the Burn Buff
- Killing a Bounty Boss during Overload adds 3 seconds, up to 24 seconds remaining; a completed Overload leaves 25 charge
- Player-head bubbles report charge, extensions, and Overload entry; phase broadcasts show charge or remaining Overload time

Late-game power now becomes a dangerous damage window instead of unlimited passive durability.

## Arena NPCs

All three present:
- **Awen** — Storage & retrieval, plus **`Sweep Loot`** for fast cleanup of tracked BossRush lootboxes in the current scene
- **Dingdang** — Reforge & shop
- **Yuori** — Healing

## Mystery Merchant

Also spawns in Blood Hunt with categorized shops + `Repair` option + `Sell All` button.

## Fortifications

4 deployable items:

- **Foldable Cover** (250 HP) — Light cover, most common
- **Reinforced Roadblock** (500 HP) — Heavy barricade, **very tough**
- **Barbed Wire** (200 HP) — Slows enemy advance
- **Repair Spray** — Fixes nearest friendly fortification within 3m, restores 25% max HP

Deploy where your mouse points. Can't overlap, can't clip into scenery. Failed deployment refunds the item. Start with 1 free Cover Pack.

## Boss Behavior by Phase

- **Preparation** — Free roam, no pursuit
- **Bounty** — Chase the Bounty Leader
- **Hunt Surge** — All Bosses chase YOU. Unmarked +50% move speed
- **Extraction** — All chase you. All +50% speed. Unmarked +100% speed

Dead Bosses auto-replaced with new ones.
::: tip
Boss Filter also applies to Blood Hunt; disabled Bosses will not enter this mode's Boss pool.
:::

## Extraction

- Point spawns far from you
- Stand in it for **15 seconds** to evacuate
- Reward: 1 high-quality item per Bounty Mark you hold

## Win / Lose

- **Win**: Evacuate during Extraction
- **Lose**: Die (bleed, Boss attacks, hubris)

::: warning
Extraction = 3%/sec bleed. Hesitation is death.
:::

::: tip
See the Blood Hunt strategy guide for detailed tips.
:::
