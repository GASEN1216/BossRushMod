## v2.2.4

### Release Date
- 2026-07-10

### Main Theme
- **Dragon Cannon release and ammo polish**: the Skyburn Dragon Cannon now drops from the Dragon Lord at 1%; all 15 compatible ammo profiles received behavior, visual, and performance polish.
- **Zombie Mode special-enemy improvements**: Exploder, Plague, and Harasser behavior now matches their telegraphs, hits, and area effects.
- **Reverse Scale compatibility fix**: fixed Reverse Scale sometimes failing to trigger after the latest damage-event timing changes; it now grants a brief invincibility window after activation.

---

### Detailed Update Log

#### New and Improved: Dragon Cannon

- The Dragon Cannon now has a **1%** drop rate from the Skyburner Dragon Lord. The transferred 4% raises Reverse Scale's drop rate to **39%**.
- All 15 compatible ammo types can be selected in the ammo-type list. Each one rewrites the cannon's damage, fire rate, magazine, range, and projectile behavior.
- Polished the special behavior of Rocket, Shotgun, Arrow, Energy, Snow, Nano, Candy, IceBlade, and Firework ammo.
- Optimized Firework split scheduling, hit-effect pooling, and warmup. Its visual and damage design are preserved while impact-related frame drops are substantially reduced.

#### Improved and Fixed: Zombie Mode Special Enemies

- Exploders now only detonate near the player; their telegraph and blast follow their current position, and they self-destruct after exploding instead of repeatedly chasing and detonating.
- Plague enemies and related elite affixes now create persistent poison clouds. Harassers now fire projectiles that create a slow zone only after impact.
- Fixed slow zones affecting players outside their radius and corrosive areas lacking a warning delay before their first damage tick.

#### Fix: Reverse Scale Near-Death Protection

- Reverse Scale now preserves its trigger window before lethal damage can enter the original death flow.
- After healing, Reverse Scale grants a **0.5s** invincibility window to reduce instant follow-up deaths.
