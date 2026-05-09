# Code Structure Refactor In-Game Smoke Record

Date: 2026-05-09

Target DLL:

- `Build/BossRush.dll`
- `D:\sofrware\steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\BossRush\BossRush.dll`

Run order:

1. Run `test_bossrush_official.bat`.
2. Run `test_bossrush_smoke_manual.bat`.
3. Complete the checklist in-game.
4. Run `python3 tests/SmokeLogScan.py` after exiting or returning from the smoke run.

## Checklist

- [x] Start the game and load into `Base_SceneV2`. Log-verified in `2026-05-09_15-31-18.log`.
- [x] Normal merchant still contains BossRush ticket, adventure journal, achievement medal, Awen token, brick stone, and zombie invitation. User manual smoke reported OK.
- [x] Enter a standard BossRush arena through the existing map selection flow. User manual smoke reported OK.
- [x] Standard BossRush arena setup is normal. User manual smoke reported OK.
- [x] Sign options are normal. User manual smoke reported OK.
- [x] First wave starts normally. User manual smoke reported OK.
- [x] Enemy spawn is normal. User manual smoke reported OK.
- [x] Kill resolution is normal. User manual smoke reported OK.
- [x] Arena exit is normal. User manual smoke reported OK.
- [x] Enter Mode D through the naked + ticket flow. User manual smoke reported OK.
- [x] Mode D starter kit is normal. User manual smoke reported OK.
- [x] Mode D sign option is normal. User manual smoke reported OK.
- [x] Mode D first wave starts normally. User manual smoke reported OK.
- [x] Mode D stuck-wave self-check behavior is normal. User manual smoke reported OK.
- [x] Mode D exit cleanup is normal. User manual smoke reported OK.
- [x] Frostmourne or FenHuangHalberd ability still initializes and triggers. User manual smoke reported OK.
- [ ] Optional: DebugTools hotkeys behave normally in DevMode.
- [ ] Optional: achievement hotkey still opens the achievement UI.

## Result

Conclusion: Passed by user manual smoke

Notes:

- Tester: user
- Game version:
- Start time:
- End time:
- Issues found:
- Related log file:
- Latest command verification refresh: 2026-05-09 15:11 CST
- Command verification status: static guards, official compile, logic tests, wiki sync, and DLL deploy passed.
- Post-run log scan helper: `python3 tests/SmokeLogScan.py`
- Latest log scan helper result: `SmokeLogScan: PASS` on `D:\sofrware\steam\steamapps\common\Escape from Duckov\2026-05-09_12-37-48.log`; this is weak evidence only because the in-game checklist is still not run.
- Launch attempt: `test_bossrush_smoke_manual.bat` launched `Duckov.exe` at 2026-05-09 15:31 CST.
- Active process observed after launch: `Duckov.exe` PID `23852`.
- Later process check: no `Duckov.exe` process found.
- Latest launch log: `D:\sofrware\steam\steamapps\common\Escape from Duckov\2026-05-09_15-31-18.log`.
- Latest launch log scan helper result after base load: `SmokeLogScan: PASS`; 3 total error blocks and 0 BossRush-related error blocks.
- User manual smoke report: "都没问题了" after checking the requested in-game flows.

## Current Log Scan

Latest scanned log:

- `D:\sofrware\steam\steamapps\common\Escape from Duckov\2026-05-09_15-31-18.log`
- `D:\sofrware\steam\steamapps\common\Escape from Duckov\2026-05-09_12-37-48.log`

Observed weak evidence:

- Latest launch reached `MainMenu`.
- Latest launch loaded `Base` and `Base_SceneV2`; log contains `Base_SceneV2 场景加载完毕` and `Player entered base`.
- Latest launch registered BossRush config keys, including `BossRush_EnableRandomBossLoot` and `BossRush_AchievementHotkey`.
- Latest launch log scan found 0 BossRush-related error blocks.
- BossRush config keys were registered, including `BossRush_EnableRandomBossLoot` and `BossRush_AchievementHotkey`.
- Log contains a purchase notification for `Boss Rush船票`.
- The session loaded `Base_SceneV2` and returned to `MainMenu`.
- `python3 tests/SmokeLogScan.py` found 8 total error blocks and 0 BossRush-related error blocks in the latest scanned log.

Observed errors not attributed to BossRush by stack:

- `ArgumentNullException: The Playable is null` from `KINEMATION.MagicBlend.Runtime.MagicBlendState.OnStateEnter`.
- `NullReferenceException` from `BattlefieldTypeKillNotice.ModBehaviour.OnDead`.

Still missing:

- No remaining item from the original smoke checklist. New structural changes after this record still require their own targeted verification.
