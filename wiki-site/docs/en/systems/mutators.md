# Mutator System

# What Is It?

At the start of every run, the system draws **1–10 random mutators** from a pool of 28 and applies them immediately for the entire run. Mutators can buff enemies, buff the player, or change environment rules — randomized every time.

> **Zombie Mode is excluded**: it has its own independent in-run buff system and does not use this mechanic.

# Applicable Modes

| Mode | Rolls mutators? |
|------|----------------|
| Standard BossRush | ✅ |
| Infinite Hell | ✅ |
| From Scratch (Mode D) | ✅ |
| Faction War (Mode E) | ✅ |
| Blood Hunt (Mode F) | ✅ |
| Zombie Mode | ❌ (separate system) |

# How to See Active Mutators

- A persistent hint stays on the **left edge** of the screen (~25% down) after the run starts
- Hover the left-side mutator hint to show full effect details; moving the mouse away hides them

# Configuration

- **Toggle**: `enableMutators` (default: `true`)
- **Count**: `mutatorCount`, range 1–10, default **3** per run
- Adjustable via ModConfig UI or config file

---

# Mutator Pool (28 Total)

# ⚔ Enemy Buffs (9)

| Mutator | Effect |
|---------|--------|
| **Swift Storm** | All enemies movement speed **+30%** |
| **Iron Fortress** | All enemies max HP **+50%** (existing enemies gain HP immediately) |
| **Bullet Rain** | All enemies fire rate **+25%** |
| **Giants** | All enemies size **×1.4**, HP **+40%** |
| **Ratswarm** | All enemies size **×0.6**, speed **+45%** (smaller and faster) |
| **Bloodhounds** | All enemies have infinite aggro range — **permanently lock onto you** |
| **Vicious** | All enemies deal **+30%** gun and melee damage |
| **Enemy Marksman** | All enemies gun scatter **−25%** (shots are tighter) |
| **Frenzy** | All enemies movement speed and fire rate **+20%** |

# ★ Player Boons (11)

| Mutator | Effect |
|---------|--------|
| **Fleet Footed** | Player walk/run speed **+35%** |
| **Sharpshooter** | Player gun crit rate **+30%** |
| **Trigger Discipline** | Player fire rate **+25%** |
| **Steady Aim** | Player gun scatter **−30%** |
| **Fast Hands** | Player reload speed **+40%** |
| **Lethal Strike** | Player gun crit damage **+50%** |
| **Long Shot** | Player gun range **+30%** |
| **Melee Master** | Player melee damage **+40%** |
| **Tank** | Player max HP **+30%** |
| **Field Medic** | Player healing effectiveness **+50%** |
| **Lucky Star** | Player gun and melee crit rate **+20%** |

# ☠ Environment Rules (8)

| Mutator | Effect | Note |
|---------|--------|------|
| **Hemorrhage** | Bleed damage speed **×1.5** | Blood Hunt (Mode F) only |
| **Festering Wounds** | All healing effectiveness **−40%** | |
| **Undying** | Bosses regenerate **5% HP every 10 seconds** | |
| **Glass Cannon** | Player damage **+50%**, but armor is zeroed | Applies to gun and melee |
| **Blitz** | Player move speed **+40%**, but max HP **−20%** | Current HP is clamped to the new cap |
| **Lifesteal** | Killing an enemy restores **8% max HP** | |
| **Blood Pact** | Direct player kills restore **16% max HP**, but healing **−30%** | Direct player kill credit only |
| **Volatile Remains** | Enemies explode on death (3m radius, 40 fire damage) | **Can injure you** — watch spacing |

---

# FAQ

**Q: Are player boon mutators equally likely?**  
All 28 entries are in the same pool with equal weight. You might roll both "Giants" and "Glass Cannon" in the same run.

**Q: Can I reroll or choose mutators?**  
No. The draw is random and non-configurable mid-run.

**Q: Do mutators carry over between runs?**  
No. All mutators are cleanly removed on any run-end (clear, death, or manual exit).

**Q: Does Volatile Remains chain-explode?**  
The system has a re-entry guard — only the direct kill triggers an explosion, not the explosion's secondary kills. No infinite chain.
