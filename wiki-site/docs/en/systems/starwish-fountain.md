# Dust-Covered StarWish Fountain

# What Is It

- The Dust-Covered StarWish Fountain is a buildable interactive structure in the base.
- It lets you send ideas, suggestions, or future feature wishes directly from the game.
- The current implementation submits wishes to a Feishu Bitable workflow and does not require a separate custom server.

# How To Use It

- Build the Dust-Covered StarWish Fountain from the base building menu.
- Walk up to it and interact with `Make a Wish`.
- It opens a runtime-created, vanilla-style `View` panel rather than the old IMGUI window.

# Current UI Rules

- The title is `Write Your Wish`.
- The input area is a fixed-height multi-line text box.
- Long text uses an internal vertical scrollbar instead of expanding the whole window.
- The fixed reminder on the right says: `Please don't enter invalid or spam content~`
- The anonymous toggle is currently off by default.
- The allowed length range is `20 ~ 10000` characters.

# Feedback After Submission

- While sending, the panel shows `Sending your wish to the stars…`
- On success, the game shows the original large banner:
  - `May this starlight illuminate the utopia in your heart~`
- The panel closes automatically about 3 seconds after success.
- The cooldown is currently a global `30 seconds`, not per individual fountain.

# Privacy

- If anonymous mode is enabled, the submitted player name becomes `Anonymous`.
- If anonymous mode is disabled, the game tries to use your Steam display name.
- If Steam name lookup fails, it falls back to anonymous mode automatically.
- No Steam ID or other unique identity token is submitted.

# Content Restrictions

- Blank input, meaningless spam, and heavily repeated content are rejected.
- Links, contact info, and traffic-pulling text are rejected.
- Profanity, abusive language, and ad-like content are rejected.

::: tip
If the submit button is disabled, first check whether you have at least 20 characters or whether the shared 30-second cooldown is still active.
:::
