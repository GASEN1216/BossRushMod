## v2.3.0

### Release Date
- 2026-09-03

### Main Theme
- **A mode where you never step onto the field**: in the Black Market Duck Cup you play the manager — sign two fighters, read the odds, place your bet, call one order per match, and let them do the rest.
- **The mod has a story now**: the Duck King Campaign strings five existing modes into a six-chapter investigation, and finishing chapters unlocks facilities in the Arena Backyard.
- **There is more to do at base**: raise cubs at the Relic Nest, farm and display trophies out back, and read a new issue of the Duckov Daily every in-game day. The time between runs is no longer just restocking.
- **More reasons to hunt Bosses**: the Duck Emperor Codex records every Boss you personally kill, Affix Forging changes how your gear *behaves* rather than just its numbers, and runs now throw timed events at you.

---

### New: Black Market Duck Cup

- A new mode. Bring one BossRush ticket and take the new "Black Market Duck Cup" option next to "Boss Rush" at base — no mode-specific item required. The ticket is refunded if entry is turned away.
- You don't fight. From five tryout candidates you sign a **starter and a substitute**, and they play out six matches for you. Match six is the final.
- Before each match you read the board: the enemy lineup is already locked in, and the odds convert the public strength gap into x1 through x5.
- You get virtual chips to bet with, 0 to 2 per match. Winning pays out by the odds and adds candidates to your gear reward.
- The brief gives you one **free scout** per match: pick one of four reveals about the enemy. What it uncovers folds into the public summary, so it moves the odds too.
- Your only action mid-fight is **ringing the bell** — lock in one order before the match, then call it once.
- A downed fighter gets injured; an injured fighter who goes down again retires for good. Benching them for a match lets them recover instead.
- This mode lets you stake **real items from your warehouse**, and losing forfeits them permanently. The entry page keeps that warning on screen.
- Winning puts your champion in the **Hall of Fame**: persistent across seasons, exactly 32 seats, and the thirty-third arrival pushes the oldest out. The season wrap-up page lets you browse it.

### New: Duck King Campaign

- The mod's first story campaign, taken from the **Campaign Board** you build at base.
- Six chapters send you to the Standard Arena, From Scratch, Faction War, Blood Hunt and Zombie Mode for special objectives, then to a final showdown in the arena against the Shadow of the Champion.
- Objectives track themselves while you play the mode normally. Nothing extra to activate.
- Each delivery pays a reward and hands you a piece of evidence, which is filed into the game's own notes index.
- The first three chapters each unlock one Arena Backyard facility.

### New: Arena Backyard

- Three base facilities, unlocked one at a time by the first three campaign chapters.
- The **garden** plugs into the game's own farming system, and the three custom Bosses drop the matching seeds.
- The **trophy showcase** registers gear you've earned. **Registering does not take the item away** — the bonus comes from having beaten it.
- The **jukebox** adds mod battle tracks to the base playlist.
- Adds three seeds and three **raid meals**. You eat a meal at base and the effect applies to your next run, then clears at the end of it.

### New: Relic Nest

- Killing a Boss has a chance to drop a **relic egg**, and always yields **souls** — bank enough of one bloodline and you can condense an egg of exactly that Boss.
- Hatching locks in talents, temperament and colour variance all at once. No rerolls.
- You can bring one cub per run. It fights alongside you and gives you an extra scavenging bag. While you're on the field it can't die — it only withdraws wounded and carries a scar out of it.
- The **disaster expedition** is the only place a cub is truly at risk: three destinations, three risk tiers, and the death rate is printed on the button before you commit.
- The Relic Museum tracks the bloodline index, taming achievements, and a memorial for cubs lost on expedition.

### New: Duck Emperor Codex

- A Boss collection book that keeps its own records. Buy one from the base shop and use it from your bag — using it doesn't consume it.
- Records total kills, the date you first met each Boss, which mode you first killed it in, and your fastest kill.
- **Almost every mode counts**, including Bosses you happen to kill on vanilla raid maps. The Black Market Duck Cup is the one exception - you never step onto the field there, so those kills are not yours.
- Only kills where you personally dealt the finishing blow count. Companions, pets and environmental damage don't.
- Reaching collection milestones unlocks achievements and pays out rewards.

### New: Affix Forging

- Talk to the Goblin and pick "Affix Forging" to spend a **forge stone** on a random affix for a piece of gear.
- Reforging changes numbers; affixes change **behaviour** — explode on kill, leech on hit, reflect damage from armor.
- Twelve affixes across three tiers. The cursed tier is powerful, and every one of them charges you for it.
- How many affixes a piece can hold depends on its rarity, and you can lock the ones you like before rerolling.
- Forge stones come from the Goblin's shop and from Boss drops.

### New: Random Events

- Things now happen mid-run: an airdrop slams down, a blood moon rises, money falls out of the sky, or a Boss that has no business being there walks in.
- Eight events in all, each announced by a banner, each on a timer that ends it automatically.
- Different from mutators: mutators are rolled at run start and last the whole run; events happen partway through and expire.
- They only fire in Standard BossRush, Infinite Hell and From Scratch, and only one runs at a time.
- The Boss from "Uninvited Guest" **does not count toward the current wave**, so you can ignore it and still progress.

### New: Duckov Daily

- Build a **mailbox** at base and a new issue arrives every in-game day, writing up what you did yesterday.
- Each issue carries the front-page story, yesterday's numbers, a daily bounty, weather and gossip, and a check-in wall.
- One bounty per day. Completing it is announced in the next day's paper and pays out automatically.
- Check-in runs 30 days per issue, with high-quality prizes on milestone days. Missing a day resets the current issue's progress.
- All prizes go to the courier's pending list rather than straight into your bag.

### Improved: Music and Interface

- Boss fights now have looping battle music and victory stingers, and the tracks are available on the jukebox. Tracks are driven by a playlist file, so swapping the audio files swaps the music.
- Panels, buttons and cards now use a replaceable interface skin, and fall back automatically if the skin files are missing.

### Changed: Content Systems No Longer Need Enabling

- The Relic Nest, Duckov Daily, Duck Emperor Codex, Affix Forging, Random Events, Duck King Campaign, Arena Backyard and Black Market Duck Cup are now **on by default**, and their master toggles no longer appear in the settings screen.
- Old saves that still had them switched off are corrected automatically, so you won't end up with a system installed but unreachable.
- Tuning options are unaffected — the random event frequency tier and the backyard's skip-unlock toggle are still there.

### Fixes

- Fixed mod-spawned enemies being silently switched off once you moved far away, which left waves that could never finish. This was the single biggest cause of stuck waves.
- Fixed deaths of **enemies outside the current wave** — intruding event Bosses, From Scratch Bosses — counting toward wave progress and causing skipped or early-finished waves.
- Fixed airdrop crates expiring while you still had them open, taking everything you hadn't picked up with them.
- Fixed the Skyburner Dragon Lord never dropping **Affix Forge Stones**. It was the only one of the three custom Bosses missing that roll; it now drops them at the same rate as everything else.
- Fixed broken achievement chains and a silent ending in Fate Echo, and added a way to abandon a run partway.
- Fixed the faction assignment of enemies spawned in From Scratch, and a failure to initialize Faction War's roaming merchant.
- Fixed movement behaviour under Blood Hunt's bloodfire state, and added a kill reward prompt.
- Fixed the portable safe zone in Zombie Mode behaving inconsistently between combat and preparation phases.
