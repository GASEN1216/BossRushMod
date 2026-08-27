# 2026-05-14 Final Runtime Smoke Record

Conclusion: User-reported smoke passed after the 2026-05-14 21:01 deploy; latest log scan PASS with 0 BossRush-related error blocks.

## Current Deploy Baseline

- Validation command: `validate_refactor_step.bat 2026-05-14-modbehaviour-classification-codex`
- C# logic tests: 8/8 PASS
- Windows compile/deploy: PASS
- Deployed DLL timestamp: `2026-05-14 21:01:09 +0800`
- Latest pre-smoke log scan: `python3 tests/SmokeLogScan.py` = `STALE_LOG`
- Latest known root log before this smoke: `2026-05-14_12-42-02.log`, older than the deployed DLL
- Latest post-smoke root log: `E:\SteamLibrary\steamapps\common\Escape from Duckov\2026-05-14_21-12-14.log`
- Latest post-smoke Player.log mtime: `2026-05-14 21:21:01 +0800`

## Tester Record

- Tester: User reported in chat: "我已经进游戏测试了，似乎都没问题"
- Game version:
- Start time: `2026-05-14 21:12` from root log name
- End time: `2026-05-14 21:21` from Player.log mtime
- Related latest log file after smoke: `E:\SteamLibrary\steamapps\common\Escape from Duckov\2026-05-14_21-12-14.log`
- `SmokeLogScan.py` result after smoke: PASS
- BossRush-related error blocks: 0
- Total error blocks: 57
- Issues found: None reported by user for BossRush smoke. The latest log still contains non-BossRush/external or base-game error stacks such as `MakeTimeQuacker.Bed2Interactable`, `KINEMATION.MagicBlend`, `ItemStatsSystem.ItemAgent`, `AIMainBrain.CheckObsticle`, `SceneLoader`, and DuckMarket/item-market messages; `SmokeLogScan.py` found 0 BossRush-related error blocks.

## Checklist

- [x] Base_SceneV2 loads. User reported smoke had no issues; log shows Base scene initialization after deploy.
- [x] Normal merchant still contains BossRush ticket, adventure journal, achievement medal, Awen token, brick stone, and zombie invitation. User reported smoke had no issues; log shows `已购买 Boss Rush船票`.
- [x] Map selection opens and at least one JSON-backed map enters and exits. User reported smoke had no issues; log shows scene transitions after ticket purchase.
- [x] Standard BossRush full run works: arena setup, sign options, first wave, enemy spawn, kill resolution, reward/lootbox drops, and arena exit. User reported smoke had no issues; `SmokeLogScan.py` found 0 BossRush-related error blocks.
- [x] Mode D entry, combat, reward, and cleanup path works. User reported smoke had no issues.
- [x] Mode F entry, combat, reward, respawn/target refresh, bounty/reward, and cleanup path works. User reported smoke had no issues.
- [x] Mode E full run works: entry, startup spawn flow, respawn/scaling behavior, merchant/service UI, reward/drop, and cleanup. User reported smoke had no issues.
- [x] Zombie Mode full run works: entry, early waves, boss/reward UI, safe-zone/extraction prompts, temporary NPC/service flow, and cleanup. User reported smoke had no issues; log shows Zombie-mode start/wave/kill lines and no BossRush-related error blocks.
- [x] Courier storage/sweep opens and closes without item loss, stuck UI, or save callback errors. User reported smoke had no issues.
- [x] Wish Fountain opens and closes without UI, reward, or transition regression. User reported smoke had no issues.
- [x] Melee slashFx / hitFx visuals are unchanged for FenHuangHalberd, Frostmourne, or PhantomWitch scythe. User reported smoke had no issues; log includes custom weapon/item interactions with no BossRush-related error blocks.

## Post-Smoke Commands

Already run after exiting/after the smoke session produced a new root log:

```bash
python3 tests/SmokeLogScan.py
```

Observed result:

```text
SmokeLogScan: latest log: /mnt/e/SteamLibrary/steamapps/common/Escape from Duckov/2026-05-14_21-12-14.log
SmokeLogScan: error blocks found: 57
SmokeLogScan: BossRush-related error blocks: 0
SmokeLogScan: PASS
```

## Notes

- This record validates the current deployed behavior only. It does not close `#34` official `Zone` + `ZoneDamage` migration unless that migration is implemented later and a dedicated focused smoke is run.
- The user has currently said global empty `catch` cleanup does not need to block their practical testing, but final audit remains evidence-gated and should not be changed to `Complete` while `STALE_LOG`, missing real smoke, or open accepted source items remain.
