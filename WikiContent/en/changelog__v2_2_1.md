## v2.2.1

### Release Date
- 2026-07-02

### Main Theme
- **StarWish Fountain Danmaku**: the StarWish Fountain now shows a scrolling danmaku layer when its panel opens, turning saved wishes into moving background messages behind the UI.
- **Item Loading Fix**: fixed an issue where some custom equipment and items could turn into white question-mark placeholder icons after a full game restart.

---

### Detailed Update Log

#### New: StarWish Fountain Danmaku

- Opening the "Dust-Covered StarWish Fountain" panel now displays a scrolling danmaku layer between the dimmed overlay and the main panel.
- Danmaku contents come from saved wish records, preferring freshly fetched data and falling back to local cache if the network is temporarily unavailable.
- The danmaku layer uses object pooling and lane-based scrolling to reduce first-open hitching and runtime stutter.

#### Fixed: Item Loading Issue

- Fixed an issue where some BossRush custom equipment and items could temporarily recover after toggling the mod, but turned back into white question-mark placeholder icons after a full game restart.
- Save restore, storage, shops, and UI queries for these dynamic items now go through an on-demand registration fallback before resolving by TypeID, reducing fallback placeholder restores when the prefab has not been registered yet.
