using System;
using BossRush;

class Program
{
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    static void Kill(string key)
    {
        var target = new Health
        {
            IsDead = true,
            Character = new CharacterMainControl
            {
                isBossCharacter = true, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = key }
            }
        };
        CodexKillCollector.OnGlobalDead(target, new DamageInfo
        {
            fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 100
        });
    }

    static void Main()
    {
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        // 2026-09-20 第三轮：官方 Boss 名单补目录。过滤池此时是空的（ModBehaviour.Instance.Pool
        // 没加任何 preset），正是「玩家还没进过竞技场 / 在筛选器里关掉了 Boss」那一档，
        // 旧实现在这一档下整册只有 8 张卡，官方 Boss 一张锁定卡都没有。
        int roster = CodexOfficialBossRegistry.OfficialBossKeys().Count;
        int initial = CodexBossCatalog.Count;
        Check(CodexOfficialBossRegistry.LoadedFromJson && ModBehaviour.CriticalLogs.Count == 0,
            "official boss roster comes from the production JSON, not the hard-coded fallback");
        Check(roster == 40, "official boss roster lists forty bosses");
        Check(initial == 8 + roster, "production catalog seeds custom, zombie and the full official roster");
        CodexBossInfo seeded;
        Check(CodexBossCatalog.TryGet("Cname_StormBoss1", out seeded)
            && !seeded.IsHistoricalOnly && !seeded.IsCustomBoss && !seeded.IsZombieBoss,
            "official boss absent from the filtered pool still gets a locked catalog card");
        Check(CodexOfficialBossRegistry.IsOfficialBoss("Cname_StormBoss1")
            && !CodexOfficialBossRegistry.IsOfficialBoss("Cname_Boss_Red")
            && CodexOfficialBossRegistry.IsOfficialCreature("Cname_Boss_Red"),
            "one roster feeds both the catalog and the category label");
        for (int i = 0; i < initial - 1; i++)
            CodexPersistence.Current.GetOrCreate(CodexBossCatalog.All[i].Key, "boss").Kills = 1;
        string missing = CodexBossCatalog.All[initial - 1].Key;
        const string champion = "BossRush_Campaign_FinalBoss_Name";
        Kill(champion);
        CodexBossInfo info;
        Check(CodexPersistence.Current.UnlockedCount == initial && CodexPersistence.Current.Find(champion) != null,
            "real global kill collector records new campaign champion");
        Check(CodexBossCatalog.TryGet(champion, out info) && CodexBossCatalog.Count == initial + 1,
            "new historical boss is visible immediately before panel rebuild");
        Check(!BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll)
            && !CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "historical entry cannot substitute for missing original catalog key");
        int revision = CodexBossCatalog.BuildCount;
        Kill(champion);
        Check(CodexBossCatalog.BuildCount == revision && CodexPersistence.Current.Find(champion).Kills == 2,
            "repeat kills do not rebuild unchanged catalog");
        Kill(missing);
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll)
            && CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "all-collected achievement unlocks after every actual key is killed");

        BossRushAchievementManager.Unlocked.Clear();
        CodexMilestones.ResetStaticCaches();
        CodexBossCatalog.Invalidate();
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(CodexBossCatalog.TryGet(champion, out info) && BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll),
            "panel evaluation rebuilds invalidated catalog before checking existing save");

        // Adversarial count: the same unlocked entry appears twice, while a required key has zero kills.
        // A raw UnlockedCount >= Count check passes; key membership must reject it.
        BossRushAchievementManager.Unlocked.Clear();
        CodexPersistence.Current.Find(missing).Kills = 0;
        CodexPersistence.Current.Entries.Add(CodexPersistence.Current.Find(champion));
        Check(CodexPersistence.Current.UnlockedCount >= CodexBossCatalog.Count, "fixture creates equal counts with an unlocked-key gap");
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(!BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll),
            "all-collected evaluates keys rather than aggregate counts");

        CodexPersistence.Current = new CodexData();
        CodexBossCatalog.NotifySlotChanged();
        CodexMilestones.ResetStaticCaches();
        CodexMilestones.EvaluateOnPanelOpen(CodexPersistence.Current);
        Check(!CodexBossCatalog.TryGet(champion, out info) && CodexBossCatalog.Count == initial,
            "slot change removes previous slot historical membership");
        Check(!CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current), "empty new slot cannot be all-collected");
        CheckNewAchievementThresholds();
        CheckBossTimerIsolation();
        CheckSaveTransactions();
        CheckCatalogAndLanguage();
        CheckCodec();
        CheckCapacityAndCleanup();
        CheckKillEligibility();
        CheckActualPlayerTeam();
        CheckOfficialRosterWithoutBossFlag();
        Console.WriteLine("Codex regression checks=" + checks);
    }

    static void CheckOfficialRosterWithoutBossFlag()
    {
        Reset();
        const string key = "Cname_RaiderIce";
        // 官方 EnemyPreset_Snow_Raider 的 isBoss=false；用户指定的 Boss 名单包含它。
        // 夹具读取真实名单，角色标记按实际官方资源复现，不能为方便测试改成 true。
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        CodexBossInfo card;
        Check(CodexOfficialBossRegistry.IsOfficialBoss(key) && CodexBossCatalog.TryGet(key, out card),
            "official roster contains the ice raider before any kill or filtered-pool entry");
        foreach (CodexBossInfo info in CodexBossCatalog.All)
            if (info.Key != key) CodexPersistence.Current.GetOrCreate(info.Key, info.DisplayName).Kills = 1;
        Check(!CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current), "ice raider remains required for completion");

        var target = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = false, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = key }
        } };
        var hit = new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 10 };
        UnityEngine.Time.time = 100;
        CodexKillCollector.OnGlobalHurt(target, hit);
        Check(CodexKillCollector.TrackedFightCount == 1, "official roster boss without runtime flag opens a fight timer");
        UnityEngine.Time.time = 105;
        target.IsDead = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        CodexKillCollector.OnGlobalDead(target, hit);
        CodexKillCollector.OnGlobalHurt(target, hit);
        CodexEntry entry = CodexPersistence.Current.Find(key);
        Check(entry != null && entry.Kills == 1 && entry.FastestKillSeconds == 5
            && entry.FirstKillTicks > 0 && entry.FirstScene == CodexSceneNames.Captured
            && entry.FirstMode == CodexTuning.ModeIdRaid && CodexKillCollector.TrackedFightCount == 0,
            "ice raider records all four values once and lethal OnHurt does not reopen its timer");
        Check(CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current)
            && BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll),
            "collecting the real ice raider unlocks completion without changing its runtime boss flag");
        CodexEntry restored = CodexCodec.Decode(CodexPersistence.SavedJson).Find(key);
        Check(restored.Kills == 1 && restored.FastestKillSeconds == 5
            && restored.FirstKillTicks == entry.FirstKillTicks && restored.FirstScene == entry.FirstScene,
            "ice raider four-value record survives the production codec round trip");

        Reset();
        // 名单兜底必须留在既有身份过滤之后，不能让同 key 的随从、友军或丧尸杂兵入册。
        for (int exclusion = 0; exclusion < 7; exclusion++)
        {
            target = new Health { Character = new CharacterMainControl
            {
                isBossCharacter = false, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = key }
            } };
            hit.fromCharacter = new CharacterMainControl { IsMainCharacter = exclusion != 0 };
            ModBehaviour.ModeHRunning = exclusion == 1;
            target.IsCompanion = exclusion == 2;
            target.Character.Team = exclusion == 3 ? Teams.player : Teams.wolf;
            LevelManager.Instance.IsBaseLevel = exclusion == 4;
            target.IsMainCharacterHealth = exclusion == 5;
            ModBehaviour.Instance.IsZombieModeActive = exclusion == 6;
            if (exclusion == 6) target.Character.Component = new ZombieModeEnemyRuntimeMarker { IsBoss = false };
            CodexKillCollector.OnGlobalHurt(target, hit);
            target.IsDead = true;
            CodexKillCollector.OnGlobalDead(target, hit);
            Check(CodexKillCollector.TrackedFightCount == 0 && CodexPersistence.Current.Entries.Count == 0,
                "official roster fallback preserves kill eligibility exclusion " + exclusion);
        }
        ModBehaviour.ModeHRunning = false;
        LevelManager.Instance.IsBaseLevel = false;
        // 同 nameKey 的丧尸 Boss 仍只按 marker 归入丧尸卡，不额外解锁官方卡。
        target.Character.Component = new ZombieModeEnemyRuntimeMarker { IsBoss = true, BossKind = ZombieModeBossKind.Titan };
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexPersistence.Current.Find(key) == null
            && CodexPersistence.Current.Find(CodexBossCatalog.BuildZombieBossKey(ZombieModeBossKind.Titan)) != null,
            "zombie marker wins over the official roster name key");
        ModBehaviour.Instance.IsZombieModeActive = false;

        target = new Health { IsDead = true, Character = new CharacterMainControl
        {
            isBossCharacter = false, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "Cname_Bear" }
        } };
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexOfficialBossRegistry.IsOfficialCreature("Cname_Bear")
            && CodexPersistence.Current.Find("Cname_Bear") == null,
            "official non-boss creatures do not gain kill eligibility from the display roster");
    }

    static void CheckActualPlayerTeam()
    {
        foreach (Teams team in new[] { Teams.player, Teams.wolf, Teams.scav, Teams.usec, Teams.bear, Teams.lab })
        {
            Reset();
            var player = new CharacterMainControl { IsMainCharacter = true, Team = team };
            var target = new Health { Character = new CharacterMainControl { isBossCharacter = true, Team = team,
                characterPreset = new CharacterRandomPreset { nameKey = "allied_boss" } } };
            var hit = new DamageInfo { fromCharacter = player, finalDamage = 100 };
            CodexKillCollector.OnGlobalHurt(target, hit);
            Check(CodexKillCollector.TrackedFightCount == 0, "same actual team does not start timer: " + team);
            target.IsDead = true;
            CodexKillCollector.OnGlobalDead(target, hit);
            Check(CodexPersistence.StoreCalls == 0 && CodexPersistence.Current.Find("allied_boss") == null,
                "same actual team does not record kill: " + team);
            target = new Health { Character = new CharacterMainControl { isBossCharacter = true,
                Team = team == Teams.wolf ? Teams.bear : Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "hostile_boss" } } };
            CodexKillCollector.OnGlobalHurt(target, hit);
            Check(CodexKillCollector.TrackedFightCount == 1, "enemy of actual team starts timer: " + team);
            target.IsDead = true;
            CodexKillCollector.OnGlobalDead(target, hit);
            Check(CodexPersistence.Current.Find("hostile_boss").Kills == 1, "actual enemy kill records: " + team);
        }
    }

    static void CheckNewAchievementThresholds()
    {
        Reset();
        LevelManager.Instance.IsBaseLevel = false;
        for (int i = 0; i < 30; i++)
            ModBehaviour.Instance.Pool.Add(new EnemyPresetInfo { name = "threshold_" + i, displayName = "boss" });
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        var keys = new System.Collections.Generic.List<string>();
        foreach (var boss in CodexBossCatalog.All) keys.Add(boss.Key);
        CodexPersistence.RejectStore = true;
        Kill(keys[0]);
        Check(BossRushAchievementManager.Unlocked.Count == 0, "failed kill transaction awards no codex achievement");
        CodexPersistence.RejectStore = false;
        for (int i = 0; i < keys.Count; i++)
        {
            Kill(keys[i]);
            int count = i + 1;
            Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFirstEntry), "first logged boss unlocks first entry");
            Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementTen) == (count >= 10), "ten entries exact threshold " + count);
            Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementTwenty) == (count >= 20), "twenty entries exact threshold " + count);
            Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementAll) == (count == keys.Count), "complete codex requires final actual key " + count);
        }
        int earned = BossRushAchievementManager.Unlocked.Count;
        Kill(keys[0]);
        Check(BossRushAchievementManager.Unlocked.Count == earned, "repeat boss does not award milestones again");
        foreach (float seconds in new[] { 10f, 10.01f })
        {
            Reset();
            var player = new CharacterMainControl { IsMainCharacter = true };
            var boss = new Health { Character = new CharacterMainControl { isBossCharacter = true, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "timed" } } };
            var damage = new DamageInfo { fromCharacter = player, finalDamage = 10 };
            UnityEngine.Time.time = 100;
            CodexKillCollector.OnGlobalHurt(boss, damage);
            UnityEngine.Time.time = 100 + seconds;
            boss.IsDead = true;
            CodexKillCollector.OnGlobalDead(boss, damage);
            Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill) == (seconds <= 10f),
                "observed hit and death enforce ten-second boundary " + seconds);
            int kills = CodexPersistence.Current.Find("timed").Kills;
            CodexKillCollector.OnGlobalDead(boss, damage);
            Check(CodexPersistence.Current.Find("timed").Kills == kills, "duplicate death cannot advance achievements");
        }
    }

    static void Reset()
    {
        CodexPersistence.Current = new CodexData();
        CodexPersistence.HasWriteBarrier = CodexPersistence.IsStoreFaulted = CodexPersistence.RejectStore = false;
        CodexPersistence.StoreCalls = 0;
        CodexKillCollector.ResetStaticCaches();
        CodexBossCatalog.ResetStaticCaches();
        CodexMilestones.ResetStaticCaches();
        BossRushAchievementManager.Unlocked.Clear();
        ModBehaviour.Instance.Pool.Clear();
    }

    static void CheckSaveTransactions()
    {
        Reset();
        var original = CodexPersistence.Current;
        CodexPersistence.RejectStore = true;
        Kill("rejected");
        CodexBossInfo ignored;
        Check(ReferenceEquals(original, CodexPersistence.Current) && original.Entries.Count == 0
            && !CodexBossCatalog.TryGet("rejected", out ignored) && BossRushAchievementManager.Unlocked.Count == 0,
            "rejected first kill cannot change current data, catalog or rewards");
        CodexPersistence.RejectStore = false;
        Kill("accepted");
        original = CodexPersistence.Current;
        string saved = CodexPersistence.SavedJson;
        CodexPersistence.RejectStore = true;
        Kill("accepted");
        Check(ReferenceEquals(original, CodexPersistence.Current) && original.Find("accepted").Kills == 1
            && CodexPersistence.SavedJson == saved, "rejected repeat kill preserves committed record and bytes");
        CodexPersistence.RejectStore = false;
        CodexPersistence.HasWriteBarrier = true;
        int calls = CodexPersistence.StoreCalls;
        Kill("barrier");
        Check(CodexPersistence.StoreCalls == calls && original.Find("barrier") == null,
            "known write barrier blocks mutation and serialization");
        CodexPersistence.HasWriteBarrier = false;
        CodexPersistence.IsStoreFaulted = true;
        Kill("fault");
        Check(CodexPersistence.StoreCalls == calls && original.Find("fault") == null,
            "known storage fault blocks mutation and serialization");
        original.Find("accepted").FastestKillSeconds = 5;
        BossRushAchievementManager.Unlocked.Clear();
        CodexMilestones.EvaluateOnPanelOpen(original);
        Check(BossRushAchievementManager.Unlocked.Count == 0, "faulted panel cannot award milestones");
        CodexPersistence.IsStoreFaulted = false;
        CodexMilestones.EvaluateOnPanelOpen(original);
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "panel restores earned fast-kill achievement from saved measurement");
        original.Find("accepted").Kills = int.MaxValue;
        Kill("accepted");
        Check(CodexPersistence.Current.Find("accepted").Kills == int.MaxValue, "kill count saturates without losing unlock");
    }

    static void CheckCatalogAndLanguage()
    {
        Reset();
        ModBehaviour.Instance.Pool.Add(new EnemyPresetInfo { name = DragonKingConfig.BossNameKey, displayName = "stale king" });
        ModBehaviour.Instance.Pool.Add(new EnemyPresetInfo { name = "official", displayName = "old name" });
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        CodexBossInfo king, official;
        // 8 张固定卡（3 自定义 + 5 丧尸）+ 官方名单 + 池里那一条 "official"
        int expected = 8 + CodexOfficialBossRegistry.OfficialBossKeys().Count + 1;
        Check(CodexBossCatalog.Count == expected && CodexBossCatalog.TryGet(DragonKingConfig.BossNameKey, out king)
            && king.IsCustomBoss && king.DisplayName != "stale king", "custom boss already in shared pool keeps custom classification");
        CodexBossCatalog.TryGet("official", out official);
        LocalizationHelper.Text["official"] = "中文名";
        Check(official.DisplayName == "中文名", "current language takes precedence over cached pool display name");
        LocalizationHelper.Text["official"] = "English name";
        Check(official.DisplayName == "English name", "same catalog object follows language switch without rebuilding");
        LocalizationHelper.Text.Clear();
        Check(official.DisplayName == "old name", "missing translation retains recorded fallback name");
        var ghost = CodexPersistence.Current.GetOrCreate("empty_history", "empty");
        CodexBossCatalog.SynchronizeHistoricalEntries(CodexPersistence.Current);
        CodexBossInfo ignored;
        Check(!CodexBossCatalog.TryGet(ghost.Key, out ignored), "zero-kill historical entry cannot become an unreachable requirement");
        // owner 2026-09-20 问题 5：详情页的「遭遇说明」整段删掉，
        // 连带 GetEncounterHint 本体一起移除——留着不用的话，下一个读代码的人
        // 会以为那段提示还在发给玩家。
        Check(typeof(CodexBossCatalog).GetMethod("GetEncounterHint",
                  System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                  | System.Reflection.BindingFlags.Public) == null,
            "encounter guidance is gone from the catalog, not merely unused by the panel");
        L10n.IsChinese = true;
        Check(CodexBossCatalog.BuildZombieBossKey((ZombieModeBossKind)999) == null, "unknown zombie kind cannot invent a collectible");
        string key = CodexBossCatalog.BuildZombieBossKey(ZombieModeBossKind.Titan);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) CodexBossCatalog.BuildZombieBossKey(ZombieModeBossKind.Titan);
        Check(GC.GetAllocatedBytesForCurrentThread() == before && key == "zombie_boss_Titan", "zombie identity hot lookup allocates zero bytes and preserves frozen key");
        ModBehaviour.Instance.Pool = null;
        CodexBossCatalog.Invalidate();
        CodexBossCatalog.EnsureBuilt(ModBehaviour.Instance);
        foreach (CodexBossInfo info in CodexBossCatalog.All)
            CodexPersistence.Current.GetOrCreate(info.Key, info.DisplayName).Kills = 1;
        Check(!CodexBossCatalog.IsFullyUnlocked(CodexPersistence.Current),
            "unavailable official pool cannot grant completion from fallback entries alone");
        ModBehaviour.Instance.Pool = new System.Collections.Generic.List<EnemyPresetInfo>();
    }

    static void CheckCodec()
    {
        Check(CodexCodec.ReadSchemaVersion("{\"schemaVersion\":1.2}") == -1,
            "invalid fractional schema version cannot be rounded into a writable v1 save");
        Check(CodexCodec.Decode("{\"schemaVersion\":1,\"entries\":[{\"k\":\"old\"}]}").Find("old").Kills == 0,
            "v1 optional fields retain defaults");
        string[] invalid =
        {
            "{\"schemaVersion\":1}", "{\"schemaVersion\":1,\"entries\":{}}",
            "{\"schemaVersion\":1,\"entries\":[null]}", "{\"schemaVersion\":1,\"entries\":[{}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\"},{\"k\":\"a\"}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"kills\":\"5\"}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"kills\":-1}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"first\":9223372036854775807}]}",
            "{\"schemaVersion\":1,\"entries\":[{\"k\":\"a\",\"fast\":1e100}]}"
        };
        foreach (string json in invalid) Check(CodexCodec.Decode(json) == null, "damaged collection rejects whole payload: " + json);
        var entries = new System.Text.StringBuilder();
        for (int i = 0; i <= CodexTuning.MaxEntries; i++)
        {
            if (i > 0) entries.Append(',');
            entries.Append("{\"k\":\"").Append(i).Append("\"}");
        }
        Check(CodexCodec.Decode("{\"entries\":[" + entries + "]}") == null,
            "oversized stored collection is protected, never truncated and overwritten");
        var data = new CodexData();
        var entry = data.GetOrCreate("escaped\\\"", "中文");
        entry.Kills = 7; entry.FirstMode = "raid"; entry.FastestKillSeconds = 0.001f;
        string encoded = CodexCodec.Encode(data);
        Check(CodexCodec.Encode(CodexCodec.Decode(encoded)) == encoded,
            "real codec round trip preserves escaped key, unicode and sub-frame timing bytes");
    }

    static void CheckCapacityAndCleanup()
    {
        Reset();
        var hit = new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 10 };
        var targets = new Health[CodexTuning.MaxFightStartTracked + 1];
        UnityEngine.Time.time = 10;
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = new Health { Character = new CharacterMainControl { isBossCharacter = true,
                Team = Teams.wolf, characterPreset = new CharacterRandomPreset { nameKey = "capacity_" + i } } };
            CodexKillCollector.OnGlobalHurt(targets[i], hit);
        }
        Check(CodexKillCollector.TrackedFightCount == CodexTuning.MaxFightStartTracked,
            "capacity keeps existing timers while rejecting only extra timing");
        UnityEngine.Time.time = 19;
        targets[0].IsDead = true;
        CodexKillCollector.OnGlobalDead(targets[0], hit);
        Check(CodexPersistence.Current.Find("capacity_0").FastestKillSeconds == 9,
            "oldest boss keeps measured time when table is full");
        targets[1].Character.Team = Teams.player;
        targets[1].IsDead = true;
        CodexKillCollector.OnGlobalDead(targets[1], hit);
        Check(CodexKillCollector.TrackedFightCount == CodexTuning.MaxFightStartTracked - 2
            && CodexPersistence.Current.Find("capacity_1") == null, "allegiance change clears timing without counting a friendly kill");
        CodexKillCollector.NotifySceneChanged();
        Check(CodexKillCollector.TrackedFightCount == 0, "scene transition clears all outstanding timers");
    }

    static void CheckKillEligibility()
    {
        Reset();
        var hit = new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, finalDamage = 10 };
        var target = new Health { Character = new CharacterMainControl { isBossCharacter = true,
            Team = Teams.wolf, characterPreset = new CharacterRandomPreset { nameKey = "excluded" } } };
        ModBehaviour.ModeHRunning = true;
        CodexKillCollector.OnGlobalHurt(target, hit);
        target.IsDead = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexKillCollector.TrackedFightCount == 0 && CodexPersistence.Current.Entries.Count == 0,
            "Mode H player-attributed kill is excluded before timer and collection");
        ModBehaviour.ModeHRunning = false;
        target.IsCompanion = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.IsCompanion = false;
        target.Character.Team = Teams.player;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.Character.Team = Teams.wolf;
        LevelManager.Instance.IsBaseLevel = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        LevelManager.Instance.IsBaseLevel = false;
        target.IsMainCharacterHealth = true;
        CodexKillCollector.OnGlobalDead(target, hit);
        target.IsMainCharacterHealth = false;
        target.Character.isBossCharacter = false;
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexPersistence.Current.Entries.Count == 0, "companions, allies, base, player and ordinary enemies cannot unlock entries");
        ModBehaviour.Instance.IsZombieModeActive = true;
        foreach (ZombieModeBossKind kind in Enum.GetValues(typeof(ZombieModeBossKind)))
        {
            var boss = new Health { Character = new CharacterMainControl { Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "ordinary_preset" },
                Component = new ZombieModeEnemyRuntimeMarker { IsBoss = true, BossKind = kind } } };
            UnityEngine.Time.time = 100;
            CodexKillCollector.OnGlobalHurt(boss, hit);
            UnityEngine.Time.time = 109;
            boss.IsDead = true;
            CodexKillCollector.OnGlobalDead(boss, hit);
            CodexKillCollector.OnGlobalDead(boss, hit);
            var entry = CodexPersistence.Current.Find(CodexBossCatalog.BuildZombieBossKey(kind));
            Check(entry != null && entry.Kills == 1 && entry.FastestKillSeconds == 9
                && entry.FirstMode == CodexTuning.ModeIdZombie,
                "zombie marker drives unique identity, timing and duplicate-death exclusion: " + kind);
        }
        target.Character.isBossCharacter = true;
        target.Character.Component = new ZombieModeEnemyRuntimeMarker { IsBoss = false };
        CodexKillCollector.OnGlobalDead(target, hit);
        Check(CodexPersistence.Current.Find("excluded") == null && CodexPersistence.Current.Entries.Count == 5,
            "ordinary zombie marker cannot inherit a boss preset identity");
        ModBehaviour.Instance.IsZombieModeActive = false;
    }

    static void CheckBossTimerIsolation()
    {
        CodexKillCollector.ResetStaticCaches();
        var player = new CharacterMainControl { IsMainCharacter = true };
        var info = new DamageInfo { fromCharacter = player, finalDamage = 10 };
        var boss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "timer_boss" }
        } };
        UnityEngine.Time.time = 100;
        CodexKillCollector.OnGlobalHurt(boss, info);
        for (int i = 0; i < CodexTuning.MaxFightStartTracked + 10; i++)
        {
            var trash = new Health { Character = new CharacterMainControl
            {
                isBossCharacter = false, Team = Teams.wolf,
                characterPreset = new CharacterRandomPreset { nameKey = "trash" }
            } };
            CodexKillCollector.OnGlobalHurt(trash, info);
            trash.IsDead = true;
            CodexKillCollector.OnGlobalDead(trash, info);
        }
        Check(CodexKillCollector.TrackedFightCount == 1, "long run trash kills do not fill or evict boss fight timer");
        UnityEngine.Time.time = 109;
        boss.IsDead = true;
        CodexKillCollector.OnGlobalDead(boss, info);
        CodexKillCollector.OnGlobalHurt(boss, info);
        Check(CodexKillCollector.TrackedFightCount == 0
            && Math.Abs(CodexPersistence.Current.Find("timer_boss").FastestKillSeconds - 9f) < .001f,
            "boss retains nine-second timing and lethal OnHurt does not reopen dead timer");
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "ten-second achievement remains attainable after long trash fight");
        var assistedBoss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "assisted_boss" }
        } };
        CodexKillCollector.OnGlobalHurt(assistedBoss, info);
        assistedBoss.IsDead = true;
        CodexKillCollector.OnGlobalDead(assistedBoss, new DamageInfo { finalDamage = 10 });
        Check(CodexKillCollector.TrackedFightCount == 0 && CodexPersistence.Current.Find("assisted_boss") == null,
            "environment or companion final blow closes timer without counting personal kill");

        BossRushAchievementManager.Unlocked.Clear();
        var instantBoss = new Health { Character = new CharacterMainControl
        {
            isBossCharacter = true, Team = Teams.wolf,
            characterPreset = new CharacterRandomPreset { nameKey = "same_frame_boss" }
        } };
        CodexKillCollector.OnGlobalHurt(instantBoss, info);
        instantBoss.IsDead = true;
        CodexKillCollector.OnGlobalDead(instantBoss, info);
        Check(Math.Abs(CodexPersistence.Current.Find("same_frame_boss").FastestKillSeconds - 0.001f) < .00001f,
            "same-frame observed first hit and final blow retain a positive measured-time bucket");
        Check(BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "same-frame kill with a real first-hit observation qualifies for ten-second achievement");
        BossRushAchievementManager.Unlocked.Clear();
        Kill("unknown_start_boss");
        Check(CodexPersistence.Current.Find("unknown_start_boss").FastestKillSeconds == 0f
            && !BossRushAchievementManager.Unlocked.Contains(CodexTuning.AchievementFastKill),
            "missing timer remains unknown rather than inventing a one-hit duration");
    }
}
