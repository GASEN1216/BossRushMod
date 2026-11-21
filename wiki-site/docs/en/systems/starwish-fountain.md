# Dust-Covered StarWish Fountain

## What Is It

- The Dust-Covered StarWish Fountain is a buildable structure at base.
- Write what you'd like to say or see added; the author can read it.

## How To Use It

- Build the Dust-Covered StarWish Fountain from the base building menu.
- Walk up to it and interact with `Make a Wish`.
- It opens a panel styled to match the base game, so fonts, colors and controls feel native.

## Writing a Wish

- `20 ~ 10000` characters, with a 30-second cooldown.
- The anonymous toggle is off by default.

## Feedback After Submission

- While sending, the panel shows `Sending your wish to the stars…`
- On success, the status line shows `Wish sent`.
- The panel pauses for a moment after success, then closes automatically.
- The cooldown is currently a global `30 seconds`, not per individual fountain.

## Background Danmaku

- When you open the wish panel, wishes that have already been submitted scroll across the background from right to left like danmaku (scrolling comments). Only the wish text is shown, with no names, and each line is cut off with an ellipsis after 40 characters.
- They drift only in the bands along the top and bottom of the screen, so the panel in the middle stays clear.
- The newest fetched results are used first; if the network is down for a moment, the game falls back to the local cache.

## Wish Reward

- After each successful wish, the fountain also checks whether a reward is ready to claim.
- The reward cooldown is a global `4 hours`, not tracked per individual fountain.
- If the reward cooldown is ready, a successful submission will trigger the `Starwish Draw` animation and grant 1 reward item when it finishes.
- If the reward is still on cooldown, your wish is still submitted normally, but the game only shows the remaining cooldown and does not grant another reward.
- Reward quality starts at tier 3 and gets rarer as it climbs: about 45% tier 3, 33% tier 4, 15% tier 5, 5.5% tier 6, 1.2% tier 7, and 0.3% tier 8.
- The highest tier you can draw depends on this save's play time: under 5 hours caps at tier 4, under 15 hours at tier 5, under 30 hours at tier 6, and under 60 hours at tier 7. Tier 8 needs 60 hours or more. Odds for tiers you haven't unlocked are spread over the tiers below.
- The content of your wish influences the reward direction:
  - Mentioning certain item types or keywords makes related rewards more likely.
  - What you write mostly decides which kind of item you get. It only nudges quality a little and never goes past the play-time cap.
  - The current bias can lean toward weapons, melee gear, armor, helmets, totems, gifts, healing, faction flags, summon items, fortification items, and travel-related items.
- You can press `Esc` to skip the draw animation, and the reward will still be granted normally.

## Privacy

- If anonymous mode is enabled, the submitted player name becomes `Anonymous`.
- If anonymous mode is disabled, the game tries to use your Steam display name.
- If Steam name lookup fails, it falls back to anonymous mode automatically.
- No Steam ID or other unique identity token is submitted.

## Content Restrictions

- Blank input, meaningless spam, and heavily repeated content are rejected.
- Links, contact info, and traffic-pulling text are rejected.
- Profanity, abusive language, and ad-like content are rejected.

::: tip
If the submit button is disabled, first check whether you have at least 20 characters or whether the shared 30-second cooldown is still active. If your wish submits successfully but no reward appears, check whether the 4-hour reward cooldown is still active.
:::
