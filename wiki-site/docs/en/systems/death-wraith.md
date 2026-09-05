# Death Wraith

## What Is It?

Wherever you died, the body is still standing there — waiting for you to come back.

The next time you set foot on that same map, in that same sub-scene, something will be standing on
the spot: **wearing your gear, wearing your face, breathing with your voice.** Its name is written
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

| Tier | Trigger | Health | Damage | Move speed | Moveability |
| --- | --- | --- | --- | --- | --- |
| **Strong** | carried share **≥ 50%** | **10x** | **1.5x** | 1.9x | 1.0 |
| **Balanced** | carried share **10%–50%** | 6x | 1.25x | 1.5x | 0.9 |
| **Weak** | carried share **< 10%** | 3x | none | 1.2x | 0.8 |

10x health with 1.5x damage is not a figure of speech. Wipe while kitted out, and going back to
clean up after yourself is often harder than the Boss that killed you in the first place.

## The Fine Print

- **Only one at a time.** There is a single active wraith record. Die again and the new one
  overwrites the old — even if the old one is still alive and standing on the field, it gets replaced.
- **Beating it settles the account.** Kill the wraith and the record clears. It does not respawn
  again and again; nothing comes back until your next death.
- **It drops nothing.** A wraith has no loot crate, and killing it returns not one piece of gear.
  This is a grudge match, not baggage reclaim.
- **Normal game scenes only** — menus and loading screens never trigger it.
- It carries its own name and health bar, so you can pick your own echo out at a glance.

## Do I Need to Turn It On?

On by default. `enableDeathWraithSystem` in ModConfig switches it off — after which nothing is
recorded and nothing spawns, and the existing wraith record in your save is cleared out.

::: warning
Think it through before you head out: what you're wearing now is what you'll be fighting next.
:::
