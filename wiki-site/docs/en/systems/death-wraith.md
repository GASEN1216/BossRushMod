# Death Wraith

## What Is It?

Wherever you died, the body is still standing there — waiting for you to come back.

When you return to the matching sub-scene and the official lost-property cache spawns, an echo appears nearby: **wearing your gear, wearing your face, breathing with your voice.** Its name is written
above it: `Strong / Balanced / Weak` + your name + `'s Wraith`.

How hard it hits depends on **how expensive you were when you died**.

## What It Inherits From You

- **Your face** — the exact head and character look you died with.
- **Your gear** — most of what you were wearing and carrying is now on it.
- **Your voice** — right down to your voice lines and footstep material.
- **Your style** — died holding a gun and it fights as a gunner; died in melee and it closes the
  distance and chases you down.

It won't lunge the second you load in. Like any vanilla enemy it has to notice you first — which
gives you a moment to look at what's standing there before it looks back.

## You Set Its Strength Yourself

The measure is one ratio: **the value you were carrying ÷ (cash + the value you were carrying)**.

Which means: **walk out with your whole estate on your back, die once, and you have personally
built yourself a monster.**

- **Strong** — Trigger: carried share **≥ 50%**; Health: **10x**; Damage: **1.5x**; Move speed: 1.9x; Moveability: 1.0
- **Balanced** — Trigger: carried share **10%–50%**; Health: 6x; Damage: 1.25x; Move speed: 1.5x; Moveability: 0.9
- **Weak** — Trigger: carried share **< 10%**; Health: 3x; Damage: none; Move speed: 1.2x; Moveability: 0.8

10x health with 1.5x damage is not a figure of speech. Wipe while kitted out, and going back to
clean up after yourself is often harder than the Boss that killed you in the first place.

## The Fine Print

- **Records follow lost property.** One death record is kept per raid; repeated records for the same raid replace that entry. Different raids can coexist. The cap follows the current official difficulty's lost-property limit; overflow removes the oldest record and its wraith.
- **Beating it settles that record.** Killing a wraith removes its record, leaving other raids' records intact. Opening its matching official lost-property cache also removes the saved record, preventing later respawns from that entry.
- **It drops nothing.** A wraith has no loot crate, and killing it returns not one piece of gear.
  Recover your belongings from the official lost-property cache; the wraith gives no extra gear.
- **Normal game scenes only** — menus and loading screens never trigger it.
- It carries its own name and health bar, so you can pick your own echo out at a glance.

## Do I Need to Turn It On?

On by default. `enableDeathWraithSystem` in ModConfig switches it off — after which nothing is
recorded and nothing spawns, and the existing wraith record in your save is cleared out.

::: warning
Think it through before you head out: what you're wearing now is what you'll be fighting next.
:::
