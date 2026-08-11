## v2.2.5

### Release Date
- 2026-08-10

### Main Theme
- **Faction War now has a real in-run economy**: defeat hostile Bosses for Shells, then spend them on categorized shops, category lotteries, or hiring Bosses. Fighting and progression now feed into each other throughout the run.
- **Zombie Mode pacing overhaul**: denser opening waves, more reliable spawning, and continuous Boss, mutant, and reward growth as the run goes deeper.
- **Clearer multi-mode UI**: the Blood Hunt bounty radar, active-mutator panel, and Zombie Mode dialogs have been reorganized around the information players need in the moment.
- **Stability fixes**: fixed dynamic items occasionally turning into question-mark placeholders, plus in-game Wiki overflow, skipped pages, and reopen issues.

---

### New: Faction War Shell Economy

- All 13 Mode E category shops now use a **run-only Shell balance** for purchases instead of account cash. Shells are not physical items and do not persist after leaving the run.
- Defeating a hostile Boss grants Shells. A direct player kill earns the full reward; otherwise, being within 8 meters of the death position when the Boss dies earns half. The first positive reward of the run includes 10 bonus Shells.
- Rewards scale continuously from the Boss's maximum health at spawn, so stronger targets are worth more. Promoted minions pay 70% of the standard Boss value.
- Shop prices convert from cash at one Shell per 2,500 cash, rounded up with a minimum of one. Ammo is still delivered as a full stack. Selling continues to pay cash into the normal account.
- Shell balance now appears beside cash and bank balance, while shop actions show the Shell price, current balance, and insufficient-funds state directly.

### New: Category Lottery and Boss Hiring

- Every category shop now has a lottery button. Its cost is the median Shell price of that category, and it can only award an item from the category currently open.
- Lottery quality weights gradually shift upward during the first 30 minutes of the run. It is an alternative to a guaranteed purchase, not a free reward button.
- If you can afford the offer, you may approach and hire any living Boss on the field, regardless of its original faction. It joins the player faction, follows you, and retains its combat abilities.
- Base hiring cost scales from maximum health: roughly 200 Shells at 1,000 HP, clamped from 50 to 2,000 and rounded up to the nearest 10. Every currently living hire doubles all later offers.
- There is no gameplay cap on hired Bosses. Their killing blows are credited to the player for experience, quests, and Mode E growth. A hired Boss no longer pays a Shell death reward.

### Improved: Faction War Shops and Tactical Items

- Category shops now reveal item rows in batches and clear old lists more efficiently when switching categories. Opening or revisiting very large categories no longer causes the long stalls seen before.
- Sell All still protects locked and wishlisted items. Shell purchases, cash sales, and lotteries now reject overlapping clicks, preventing duplicate charges or deliveries during rapid input.
- Shell prices were raised so a handful of early kills can no longer empty most of a category shop.
- Taunt Smoke Bomb and Chaos Detonator no longer stop working because too many Bosses are alive. Taunt Smoke always attempts the nearest 10 spawn points, while Chaos Detonator attempts every point on the map. Only one respawn task runs at a time.

### Improved: Zombie Mode

- Normal preparation now lasts 45 seconds, while the post-Boss extraction preparation lasts 75 seconds. A low-tide horde remains outside the safe zone during preparation instead of the battlefield going empty.
- Early field pressure and refill speed are substantially higher. Count reconciliation, distant-enemy recycling, and more reliable nearby spawn selection fix cases where faraway enemies occupied every slot and stopped local spawning.
- Boss count now grows only with five-wave cycles: waves 5/10/15/20/25 contain 1/2/3/4/5 Bosses, then continue upward independently of map size. Every Boss drops its own Purification Stars and reward crate.
- Boss health, damage, support pressure, Purification income, and crate quality grow together. From wave 10 onward, each Boss node grants one combat-only four-choice reward followed by the normal four-choice reward.
- Special and Elite weights keep increasing after wave 6. The first two waves now guarantee both an output option matching the starting path and a survival-oriented option, giving early builds a clearer direction.
- Special and Elite zombies received stronger size, color, and skill identities. Invalid Zombie Mode reward candidates are filtered, and direct drops fall to the ground with pickup feedback when inventory space or carry weight is insufficient.
- Dead refresh rewards were removed. Terminal affordability feedback, drink stock, and temporary-NPC protection were tightened, while reward, investment, extraction, service, and HUD screens now share one visual language.

### Improved: Information and UI

- Blood Hunt's bounty radar is now an edge-direction display. It shows up to five nearest off-screen regular targets and tracks the gold Bounty Leader separately, with direction, distance, and mark count visible at a glance.
- Active mutators now appear in a compact list labeled Enemy, Boon, or Rule. Hover any row for the full description; the details panel scrolls when the list is long.
- Expanded the bilingual Wiki with all 6 Zombie Mode Special types, 12 Elite affixes, and detailed data and fight guidance for Dragon Descendant, Skyburner Dragon Lord, and Phantom Witch.

### Fixes

- Fixed right-page text overflowing the in-game Wiki, even pages being skipped, the catalog not restoring correctly after reopening, and the website link being treated as a missing content page.
- Fixed BossRush dynamic items occasionally becoming white question-mark placeholders during save restoration. Hunter's Whistle and Blood Hunt Beacon resource mappings were corrected as part of the same path.
- Fixed invalid Zombie Mode starting medical/melee candidates, lethal safe-zone hits failing to break concealment, dead reward-refresh choices, and terminal feedback or cleanup issues after repeated use.
