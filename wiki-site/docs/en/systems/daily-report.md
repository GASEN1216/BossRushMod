# The Duckov Daily

## What it is

The Duckov Daily is a newspaper delivered to your base automatically, one issue per game day,
writing up what you did yesterday as news.

It connects things you already play: read your recap, check for a bounty that fits your next run, check in, then collect any prizes at the delivery point. Bounties track automatically and need no acceptance. Skipping one never blocks a run.

Fortunes and gossip are flavor text. They grant no hidden stats and do not unlock secret tasks.

## Getting a subscription

- Build a `Mailbox` from the base building menu. It costs `500`, you can only have **one**, and you can put it wherever you like.
- Walk up to it and you'll see the interact option `Read today's paper`.
- That opens the full newspaper panel.

<div class="brs-icon">

![The Mailbox](/images/icons/daily-mailbox.webp)
*The Mailbox*

</div>


## How long is a day

- One game day is roughly `24` minutes of real time (the in-game clock runs 60x).
- **It counts whenever the game is running**: idling in your base advances it too.
  Time does not accrue while paused or sitting in the main menu.
- Sleeping and "Continue" jumps do **not** burn an issue - the Daily tracks your actual
  play time rather than following the world clock.
- Quitting the game never counts as a missed day: the timer only moves while you play.
- The top of the paper shows today's progress as a percentage, so you always know how far
  the next issue is.

## What's in an issue

- **Front-page headline**: picked automatically from what you did yesterday. Killing a boss,
  wiping out dozens of enemies, dying repeatedly, or making a lot of money each get their own
  story. Doing nothing at all gets its own story too.
- **Yesterday's recap**: kills, boss kills, deployments, extractions, deaths, money in and out,
  damage dealt, damage taken, and your biggest single hit. Only hostile characters killed by the player count; bosses are included in the kill total. Damage taken includes environmental damage. Black Market Duck Cup matches are excluded - your contracted
  fighters did the work, not you, so bounties don't progress there either.
- **Bounty column**: yesterday's bounty result, plus today's new bounty, live progress, payout and settlement timing.
- **Weather & gossip**: weather at this world time tomorrow (including storms; not a forecast for the entire day, with fixed map weather labeled separately), today's do's and don'ts,
  and word on the street.
- **Check-in wall**: thirty slots per period.

## Daily bounties

One per day, drawn from five categories, with a difficulty tier (easy / standard / hardcore)
rolled alongside it. Target and payout move together:

- **Clear enemies** - `30 / 60 / 120`, paying `800 / 1,600 / 3,200`
- **Slay bosses** - `1 / 3 / 5`, paying `1,200 / 3,000 / 5,000`
- **Extract successfully** - `1 / 2 / 4` times, paying `700 / 1,400 / 2,800`
- **Earn in a day** - `5,000 / 15,000 / 40,000`, paying `900 / 2,000 / 4,500`
- **Extract with zero deaths that day** - flat `1,500`

A few rules:

- The bounty stays the same no matter how many times you restart that day - **it never rerolls**.
  If you don't like it, wait for tomorrow.
- The paper shows live progress, so you can check how far off you are at any time.
- Completing it is announced in **the next day's paper**, and the reward pays out automatically.
- "No deaths" requires **at least one successful extraction that day**, with no deaths before the next issue. Deploying without returning does not count. A raid spanning two days counts on its extraction day.
- Income uses gross earnings, without deducting spending. Daily bounty cash does not advance it.
- Unpaid cash stays pending while later days settle normally. Opening the paper retries payment; the result changes to received only after successful delivery.

::: tip
Check the paper before you head out and line the bounty up with whatever you were going to play anyway: "slay 5 bosses" points at the standard arena, "extract 4 times" points at short runs. Money you make on the way is the only free money there is.
:::

## Check-in and rewards

Open the paper and hit `Check in` once a day. One per day, 30 slots to a period.

**Period 1** is the ramp, and the rewards climb:

- Slot `7` → a random quality `5` item
- Slot `15` → quality `6`
- Slot `24` → quality `7`
- Slot `30` → quality `8`

**From period 2 onward** it settles into a steady payout: slots numbered 31 through 60, with a
reward at slot `7 / 14 / 21 / 28` of each period, **always at quality `8`**. All of these days are game days, not calendar days.

- Milestone slots are gold on the wall and marked with `★` - impossible to miss.
- **Missing a day clears the current period**: skip a check-in and the period resets to zero at
  the day boundary. But **periods you already completed are not taken back** - you never drop
  from period 2 back to period 1, so reward quality never regresses.

::: warning
Missing a game day resets this period's check-in progress. Your period number and earned prizes remain. Offline time and pauses do not advance the Daily; there is no calendar-day login requirement.
:::

## Where the rewards go

- Rewards go to your **delivery point's pending list** by default, not straight into your bag.
  You'll see a "check-in reward sent to your delivery point" banner.
- That means crossing a day mid-raid is perfectly safe - nothing is lost, collect it back at base.
- **If the delivery point can't take it** (a full buffer, say), the game falls back to handing it
  to you directly - into your bag, your stash, or onto the ground at your feet. When that happens
  it is **not** also queued at the delivery point, so an empty pending list doesn't mean the reward
  was lost. Check your bag and the floor first.
- Only if both routes fail is it counted as a failed delivery, and then it is re-sent
  automatically the next time you open the paper. Missed check-ins and period rollover never erase earned prizes.
- If the promised item quality is temporarily unavailable, the reward stays pending instead of being downgraded.
- The paper and check-in wall scroll, while the close button stays at the bottom. The check-in area shows the next reward quality or the number of pending prizes.

## Do I need to enable it?

- No. The Daily is on by default and there is nothing to switch on.
- The mailbox is what decides whether you can read it: with no mailbox there is nowhere to
  open the paper. The days keep counting either way, so building one later is never too late.
