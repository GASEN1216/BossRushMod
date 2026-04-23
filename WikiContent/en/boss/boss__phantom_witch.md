## Phantom Witch

### Overview
The Phantom Witch is the third custom Boss in BossRush Mod. She roams the battlefield by alternating between blinks and stealth, combining Curse Realms, scythe sweeps, and undead summoning in a three-phase fight. Defeating her grants a 50% chance to drop her exclusive melee weapon, Soulreaper's Requiem.

### Base Stats
- HP: 1000
- Damage Multiplier: 1.1x
- Model Scale: 2x
- Base Model: Ghost preset

### Phase Thresholds
- Phase 1: 100% ~ 60% HP
- Phase 2: 60% ~ 25% HP
- Phase 3: below 25% (last-stand summoning)

### Attack Skills

#### Blink Teleport
- Blink distance: 1 ~ 4m
- Stealth duration: 0.3s
- Tracked variant: places a marker 2.2m near the player (lingers 2s), then blinks to the marker and attacks

#### Scythe Sweep
- Windup: 0.35s
- Damage: 18
- Range: 3.1m, 170° arc
- Forward offset: 1.15m

#### Heavy Scythe Slash
- Windup: 0.5s
- Damage: 30
- Range: 3.6m, 130° arc
- Forward offset: 1.35m
- Pre-blinks 1.8 ~ 3.6m to close the gap before striking

#### Curse Aura
- Windup: 0.45s
- Damage: 12
- Radius: 3.5m

#### Curse Realm (Boss-exclusive)
- Windup: 0.5s
- Radius: 4.5m
- Duration: 4s
- Deals 15 damage every 0.5s
- Warning ring telegraph: 1.05s
- Phase 3: radius scaled to 80%, duration to 75%
- Active realms are cleared on phase transition

#### Requiem Arc
- Windup: 0.55s
- Range: 4.8m
- Damage: 16

#### Wraith Trail
- Windup: 0.45s
- Damage: 18
- Trigger delay: 0.3s
- Warning outline radius: 3.0m

#### Undead Summoning (Phase 3)
- Windup: 1.0s
- Summons 2 ghost minions simultaneously
- Minion HP: 150
- Minion regen: 15 HP/s
- Spawn distance: 3.0m
- Max alive at once: 2
- Two minion roles:
  - Sustain: heals the witch at 6 HP/s within 6m (1.5x bonus at close range)
  - Harass: pressures the player every 2.4s within 3.2m

### Tactical Package Rotation

#### Phase 1 (interval 1.2s)
Flank Pressure → Midrange Requiem → Wraith Trail Observe

#### Phase 2 (interval 0.85s)
Flank Pressure → Midrange Double → Curse Trap → Flank Pressure

#### Phase 3 (interval 1.1s)
Short Drift Pressure → Last Stand Summon → Curse Trap → Minion Retreat

### Stealth System
The Phantom Witch cycles between true stealth, semi-stealth, and visible states:
- Phase 1: target stealth ratio 38%
- Phase 2: target stealth ratio 32%
- Phase 3: target stealth ratio 18%
- True stealth max duration: 1.1s

### Curse Debuff
- Buff ID: 500043
- Duration: 5s
- Max stacks: 3
- Per-stack slow: -30% move speed

### Drops
- Soulreaper's Requiem (melee weapon): 50% extra drop chance

### Combat Tips
- Watch for the tracked marker (purple ground indicator); dodge sideways immediately when it appears
- Curse Realm has a ~1s warning ring — leave the area as soon as you see it
- Phase 2 ramps up attack tempo; keep moving
- In Phase 3, prioritize killing the sustain minion to stop the witch from regenerating
- At 3 curse stacks you lose 90% move speed — avoid eating consecutive curse abilities

### Spawn Restrictions
- Standard BossRush and Infinite Hell: excluded from the first 20 waves
- From Scratch: excluded from the first 10 waves
- Faction War: joins the normal draw
- Blood Hunt: joins the normal draw
