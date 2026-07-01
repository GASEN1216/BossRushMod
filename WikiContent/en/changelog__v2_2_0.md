## v2.2.0

### Release Date
- 2026-07-01

### Main Theme
- **Zombie Mode, Run Mutator System, and New Equipment**: This major version adds a brand-new Roguelite survival mode, five new melee weapons, two new equipment sets, and a per-run mutator system that applies random modifiers every run. Several impactful bugs have also been fixed.

---

### Detailed Update Log

#### New: Zombie Mode (Doomsday Zombie Survival)

A full Roguelite survival mode joins the mod. Buy a "Zombie Tide Invitation" from the base merchant to enter — you go in empty-handed and face endless waves of zombies.

- Core loop: Preparation → Combat Wave → Settlement → Reward Selection → repeat
- Every 5th wave is a Boss Wave (5 boss types: Titan / Hunter / Splitter / Shielder / Corruptor)
- Three enemy tiers (Normal / Special / Elite) with 12 Elite affixes
- Pollution system: difficulty escalates as you progress — enemy HP, damage, and elite/special rates all increase
- Purification Points system: earn by killing zombies, spend at the supply terminal, cash out 1:1 on successful extraction
- 70+ reward types (stats / ballistic mods / triggers / mutators / fortifications / temporary NPCs / contracts / insurance…)
- Safe-zone supply terminal spawns each preparation phase with nurse and merchant services
- Extraction opportunity appears after each boss wave — leave anytime with your purification earnings
- Entry items: Zombie Tide Invitation (entry ticket) + Zombie Tide Beacon (optional: skip prep countdown)

> See the dedicated "Zombie Mode" wiki page for full rules.

---

#### New: Run Mutator System

Before each run starts, **1–3 random mutators** are drawn from a pool of 15 (default 2) and applied immediately for the entire run.

- **Applies to**: Standard BossRush, Infinite Hell, From Scratch, Faction War, Blood Hunt (Zombie Mode excluded)
- **Three categories**: ⚔ Enemy Buff (7) / ★ Player Boon (2) / ☠ Environment Rule (6)
- On by default; disable via `enableMutators` config
- Count adjustable via `mutatorCount` (1–3)

Pool includes: Swift Storm / Iron Fortress / Bullet Rain / Giants / Ratswarm / Bloodhounds / Vicious / Fleet Footed / Sharpshooter / Hemorrhage / Festering Wounds / Undying / Glass Cannon / Lifesteal / Volatile Remains.

> See the dedicated "Mutator System" wiki page for full effect details.

---

#### New: Equipment (Developer Preview)

9 new equipment items have been added to the game database. **No standard drop or purchase path exists yet** — obtain routes will be added in a future update.

**P0 New Weapons (5 items)**

| Equipment | Type | Core Feature |
|-----------|------|-------------|
| **Viper Dagger** | Melee / Poison | Stack 5 poison layers on a target; burst for 35 bonus damage at max stacks |
| **Summoning Staff** | Melee | Right-click (12s CD): summon 3 soul warriors (80 HP / 15s) |
| **Energy Shield** | Totem | Frontal hits restore 30% of damage as HP (cap 25, 0.5s CD) |
| **Frost Spear** | Melee / Ice | 100% freeze on hit, 2.4m reach |
| **Thunder Ring** | Totem | Charge on hits taken (max 5); next attack releases 40 lightning damage |

**P1 Sets (4 pieces / 2 sets)**

| Set | Pieces | 2-Piece Effect |
|-----|--------|---------------|
| **Frost Set** | Frost Crown + Ice Armor | Ice resistance +50%; 30% chance to freeze close-range (<5m) attacker on hit (5s CD) |
| **Thunder Set** | Thunder Horn + Thunder Armor | Electricity resistance +50%; 25% chance to release 4m lightning AOE on close-range (<6m) hit (3s CD) |

---

#### Fixes

- **Fixed**: Dragon King's "Guardian Sacrifice" second-life and Dragon Descendant's one-time revive now trigger correctly; no longer bypassed by lethal damage.
- **Fixed**: Reforge screen stat comparison values displaying blank or incorrect numbers.
- **Fixed**: Vending machine UI crash on open (caused by deferred item injection caching issue; affected tickets, invitations, and similar items).
- **Fixed**: Achievement progress no longer resets on save load; boss kill counts no longer double-counted.
- **Fixed**: Courier deposit service now returns the correct item snapshot; deposited items no longer lost or misdelivered on retrieval.

#### Improvements

- **Performance**: Noticeably reduced stutter during map transitions and entry — integration init is now spread across frames, localization and reflection results are cached, death-frame serialization no longer blocks the main thread.
- **Improvement**: Zombie Mode in-run rewards greatly expanded; temporary NPCs (merchant, nurse, goblin, courier) can now be summoned via reward selection options.
