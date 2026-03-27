## Death Wraith

### Overview
- The Death Wraith system turns one of your deaths into a return fight against your own aftermath.
- When you die in a normal gameplay scene, the game records your death location, appearance, gear, name, and weapon state.
- The next time you return to the same map and sub-scene, a Wraith appears at that death spot, using a name like "Strong / Balanced / Weak + player name + 's Wraith."

### When It Appears
- It only works in normal gameplay scenes, not in menus or loading screens.
- A Wraith only appears in the same map and the same sub-scene where you died.
- It does not instantly aggro the moment you enter the map. It now follows normal enemy perception and starts fighting after naturally noticing you.

### What the Wraith Inherits
- Appearance: it copies your face setup and character look from the moment of death.
- Gear: it recreates most of the equipment you were wearing and carrying.
- Audio feel: it also inherits your voice type and footstep material.
- Combat style:
  - If you died with a gun-focused loadout, it uses a firearm-oriented enemy behavior template.
  - If you died in melee mode, it uses a wolf-style melee behavior template.

### How Strength Is Calculated
- The system compares the total value of the items you were carrying at death against your total wealth: cash + carried item value.
- The more of your total wealth was tied up in what you carried, the stronger the Wraith becomes.

#### Strong Wraith
- Trigger: carried item value is at least 50% of total wealth
- Around 10x health
- Around 1.5x gun and melee damage
- Around 1.9x movement-related speed
- Around 1.0 mobility

#### Balanced Wraith
- Trigger: carried item value is 10% to 50% of total wealth
- Around 6x health
- Around 1.25x gun and melee damage
- Around 1.5x movement-related speed
- Around 0.9 mobility

#### Weak Wraith
- Trigger: carried item value is below 10% of total wealth
- Around 3x health
- No extra damage bonus
- Around 1.2x movement-related speed
- Around 0.8 mobility

### Refresh and Cleanup Rules
- Only one valid Death Wraith record is kept at a time.
- If you die again, the new death record overwrites the old one.
- If an older Wraith is still alive in the current scene, it gets replaced when a new death is recorded.
- Once you kill the Wraith, that record is cleared and will not keep respawning until you die again.

### Rewards and Risk
- A Death Wraith shows its own name and health bar, so it is easy to identify.
- It does not drop a loot crate when killed.
- The more expensive your loadout was when you died, the more dangerous the rematch will be when you come back.
