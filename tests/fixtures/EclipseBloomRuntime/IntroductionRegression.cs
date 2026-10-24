using System;
using System.Reflection;
using BossRush;
using Saves;
using UnityEngine;

/// <summary>直接执行生产引导、codec 与共享保存管线；Unity、官方投影和磁盘 IO 为替身。</summary>
internal static class IntroductionProgram
{
    private static EclipseBloomIntroduction introduction;
    private static int passed;
    private const string Key = EclipseBloomIntroductionPersistence.StorageKey;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Main()
    {
        try
        {
            Run("Jeff 接取、普通 37 击杀、回基地交付和完成后刷新", CompleteFlow);
            Run("死亡事件立即放弃、不自动重接并可回 Jeff 重接", AcceptedDeath);
            Run("击败后同帧死亡撤销交付资格", DefeatedDeath);
            Run("暂时 Store 失败先撤销内存事实并在恢复后重试", RetryAbandon);
            Run("物理保存失败保留重试且不会恢复已放弃任务", PhysicalSaveRetry);
            Run("死亡写失败后换槽不会把旧槽状态写入新槽", SlotIsolation);
            Run("坏格式、未知 schema 和矛盾事实保守闭锁", InvalidPayloads);
            Run("主玩家换实例退订旧死亡事件、宿主销毁完整退订", DeathOwnerLifecycle);
            Run("引导地图圈随目标场景归属并在死亡后移除", ObjectiveMarker);
            Run("事实只在换槽重读，已打开缓存不每帧解析 JSON", CachedFacts);
            Run("死亡拒写时宿主销毁不能把旧 accepted pending 冲入存档", HostDestroyWhileAbandonFails);
            Console.WriteLine("PASS EclipseBloomIntroduction: " + passed + " cases");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Stop(); }
    }

    private static void Run(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        finally { Stop(); }
    }
    private static OfficialQuestBinding Start(bool inBase = true, string payload = null)
    {
        Stop();
        FixtureWorld.Reset(inBase);
        if (payload != null) SavesSystem.SetRaw(Key, payload);
        introduction = new EclipseBloomIntroduction();
        introduction.OnAwake(FixtureWorld.Host);
        FixtureWorld.Tick(introduction);
        return FixtureWorld.Projection.Binding;
    }
    private static void Stop()
    {
        if (introduction == null) return;
        introduction.OnDestroy();
        introduction = null;
        Assert(SavesSystem.SubscriptionCount == 0, "引导关闭后共享 store 的三项事件必须全部退订");
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(OfficialQuestBinding binding)
    {
        string message;
        Assert(binding.CanOffer() && binding.Accept(out message), "Jeff 应通过官方绑定接取引导");
        Assert(binding.IsAccepted() && EclipseBloomIntroduction.CanSpawnEncounter, "接取后应开放自然遭遇");
    }
    private static void ReviveAtBase()
    {
        CharacterMainControl.Main.Health.IsDead = false;
        FixtureWorld.SetBase(true);
        FixtureWorld.Tick(introduction);
    }
    private static EclipseBloomIntroductionData Stored()
    {
        SavesSystem.Collect();
        return EclipseBloomIntroductionPersistence.Decode(SavesSystem.GetRaw(Key));
    }
    private static BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData> StoreSpec()
    {
        object persistence = typeof(EclipseBloomIntroduction).GetField("persistence", Private).GetValue(introduction);
        object store = persistence.GetType().GetField("store", Private).GetValue(persistence);
        return (BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData>)store.GetType().GetField("_spec", Private).GetValue(store);
    }

    private static void CompleteFlow()
    {
        OfficialQuestBinding binding = Start();
        Assert(binding != null && binding.QuestId == 590215 && binding.GiverId == 1, "任务必须注册到 Jeff 官方任务页");
        Assert(!EclipseBloomIntroduction.CanSpawnEncounter && !binding.IsAccepted(), "未接任务不得自然生成花冠");
        Assert(binding.RequiredItemId == 0 && binding.RequiredItemCount == 0 && binding.RewardMoney == 0
            && binding.Submissions == null && binding.RewardItems == null, "引导不得收走盾片或额外发经济奖励");
        Accept(binding);
        string message;
        Assert(!binding.Accept(out message) && !binding.Deliver(out message), "不得重复接取或未击杀直接交付");
        EclipseBloomIntroduction.NotifyDefeated();
        Assert(!binding.Tasks[0].Done(), "基地中的击杀通知不得推进 37 目标");
        FixtureWorld.SetBase(false);
        FixtureWorld.OtherModeActive = true;
        EclipseBloomIntroduction.NotifyDefeated();
        Assert(!binding.Tasks[0].Done(), "其它模式的 Boss 池击杀不得推进探索引导");
        FixtureWorld.OtherModeActive = false;
        EclipseBloomIntroduction.NotifyDefeated();
        Assert(binding.Tasks[0].Done() && !EclipseBloomIntroduction.CanSpawnEncounter, "击败后完成目标并等待回 Jeff");
        Assert(!binding.CanDeliver() && !binding.Deliver(out message), "出击图不能向 Jeff 交付");
        FixtureWorld.SetBase(true);
        Assert(binding.BeginDelivery != null && binding.EndDelivery != null, "共享官方完成按钮必须接上交付事务双端");
        int assetCollections = 0;
        Assert(binding.BeginDelivery(() => { assetCollections++; return true; }, out message), "共享核心应能开始交付事务");
        int writesBeforeDelivery = SavesSystem.Writes.Count;
        int physicalBeforeDelivery = SavesSystem.PhysicalSaves;
        bool delivered = false;
        try
        {
            Assert(binding.CanDeliver() && binding.Deliver(out message), "回基地应能交付击杀事实");
            delivered = true;
            SavesSystem.Collect();
            Assert(SavesSystem.Writes.Count == writesBeforeDelivery, "交付事务结束前不得采集或物理提交事实");
        }
        finally { binding.EndDelivery(delivered); }
        Assert(SavesSystem.Writes.Count == writesBeforeDelivery + 1 && SavesSystem.PhysicalSaves > physicalBeforeDelivery,
            "结束交付事务必须提交唯一 completed 事实并补齐物理保存");
        Assert(assetCollections == 0, "无资产交付不得额外采集玩家战利品");
        Assert(binding.IsDelivered() && !binding.IsAccepted() && binding.Tasks[0].Done(), "历史投影中的目标必须保持已完成");
        Assert(EclipseBloomIntroduction.CanSpawnEncounter && !binding.CanOffer(), "完成引导仍可刷新花冠且不重复接任务");
        FixtureWorld.PlayerDeath();
        Assert(binding.IsDelivered() && EclipseBloomIntroduction.CanSpawnEncounter, "完成后的永久介绍事实不因后续死亡撤销");
        EclipseBloomIntroductionData saved = Stored();
        Assert(saved != null && saved.Completed && !saved.Accepted && !saved.Defeated, "交付须使用有效 completed schema");
    }
    private static void AcceptedDeath()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        FixtureWorld.Tick(introduction, 8);
        Assert(CharacterMainControl.Main.Health.OnDeadEvent.Count == 1, "死亡事件必须幂等订阅");
        FixtureWorld.SetBase(false);
        int notify = FixtureWorld.Projection.NotifyCount;
        FixtureWorld.PlayerDeath();
        Assert(!FixtureWorld.Projection.ActiveAccepted && !EclipseBloomIntroduction.CanSpawnEncounter
            && !binding.Tasks[0].Done(), "同帧死亡必须立即撤销 accepted、遭遇和目标");
        Assert(FixtureWorld.Projection.NotifyCount > notify, "死亡撤销须通知共享投影核心");
        EclipseBloomIntroductionData saved = Stored();
        Assert(saved != null && !saved.Accepted && !saved.Defeated && !saved.Completed, "死亡须持久化未接取事实");
        ReviveAtBase();
        Assert(!binding.IsAccepted() && !EclipseBloomIntroduction.CanSpawnEncounter && binding.CanOffer(), "复活不能自动重接");
        Accept(binding);
    }
    private static void DefeatedDeath()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        FixtureWorld.SetBase(false);
        EclipseBloomIntroduction.NotifyDefeated();
        Assert(binding.Tasks[0].Done(), "前置击败目标应成立");
        FixtureWorld.PlayerDeath();
        Assert(!binding.IsAccepted() && !binding.Tasks[0].Done() && !EclipseBloomIntroduction.CanSpawnEncounter,
            "击败后死亡必须连同 defeated 一起撤销");
        ReviveAtBase();
        string message;
        Assert(!binding.CanDeliver() && !binding.Deliver(out message) && binding.CanOffer(), "死亡后不能沿用击败事实交付");
    }
    private static void RetryAbandon()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        FixtureWorld.SetBase(false);
        BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData> spec = StoreSpec();
        Func<EclipseBloomIntroductionData, string> encode = spec.Encode;
        spec.Encode = value => null; // 真实 Store 的可恢复拒写边界，不把永久磁盘故障误作瞬态失败。
        try
        {
            FixtureWorld.PlayerDeath();
            Assert(!binding.IsAccepted() && !EclipseBloomIntroduction.CanSpawnEncounter
                && ((IOfficialQuestClient)introduction).Ready, "Store 拒写也须先取消内存投影并保持清理可运行");
            FixtureWorld.Tick(introduction, 3);
            Assert(!binding.IsAccepted() && !binding.CanOffer(), "失败重试期间不能自动接回任务");
        }
        finally { spec.Encode = encode; }
        FixtureWorld.Tick(introduction);
        EclipseBloomIntroductionData saved = Stored();
        Assert(saved != null && !saved.Accepted && !saved.Defeated, "写入恢复后须提交死亡清理事实");
        ReviveAtBase();
        Assert(binding.CanOffer() && !EclipseBloomIntroduction.CanSpawnEncounter, "恢复后只提供人工重新接取");
    }
    private static void PhysicalSaveRetry()
    {
        OfficialQuestBinding binding = Start();
        SavesSystem.FailPhysicalWrites = true;
        Accept(binding);
        Assert(SavesSystem.PhysicalSaves == 0, "注入的物理落盘失败必须真实命中");
        FixtureWorld.SetBase(false);
        FixtureWorld.PlayerDeath();
        Assert(!binding.IsAccepted() && !EclipseBloomIntroduction.CanSpawnEncounter, "物理保存失败不能阻止即时放弃");
        SavesSystem.Collect();
        SavesSystem.FailPhysicalWrites = false;
        ReviveAtBase();
        Assert(SavesSystem.PhysicalSaves > 0, "基地 tick 应补齐共享协调器欠下的物理保存");
        EclipseBloomIntroductionData saved = Stored();
        Assert(saved != null && !saved.Accepted && !saved.Defeated && binding.CanOffer(), "补保存不得恢复旧 accepted");
    }
    private static void SlotIsolation()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        FixtureWorld.SetBase(false);
        BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData> spec = StoreSpec();
        Func<EclipseBloomIntroductionData, string> encode = spec.Encode;
        spec.Encode = value => null;
        FixtureWorld.PlayerDeath();
        spec.Encode = encode;
        SavesSystem.SetSlot(1);
        string completed = EclipseBloomIntroductionPersistence.Encode(new EclipseBloomIntroductionData { Completed = true });
        SavesSystem.SetRaw(Key, completed);
        FixtureWorld.Tick(introduction, 3);
        Assert(binding.IsDelivered() && !binding.IsAccepted() && EclipseBloomIntroduction.CanSpawnEncounter,
            "切槽必须只读取新槽 completed，不沿用旧槽死亡重试");
        SavesSystem.Collect();
        Assert(SavesSystem.GetRaw(Key) == completed, "旧槽放弃义务不得覆盖新槽内容");
        foreach (Tuple<int, string, string> write in SavesSystem.Writes)
            Assert(write.Item1 != 1, "无新槽修改时不得向新槽写入旧 pending");
    }
    private static void InvalidPayloads()
    {
        string[] invalid = {
            "{", "{\"schemaVersion\":2,\"accepted\":true,\"defeated\":false,\"completed\":false}",
            "{\"schemaVersion\":1,\"accepted\":false,\"defeated\":true,\"completed\":false}",
            "{\"schemaVersion\":1,\"accepted\":true,\"defeated\":false,\"completed\":true}",
            "{\"schemaVersion\":1,\"accepted\":\"yes\",\"defeated\":false,\"completed\":false}"
        };
        foreach (string raw in invalid)
        {
            OfficialQuestBinding binding = Start(true, raw);
            Assert(!binding.CanOffer() && !binding.CanDeliver() && !EclipseBloomIntroduction.CanSpawnEncounter,
                "坏 schema 必须保持只读闭锁");
            FixtureWorld.PlayerDeath();
            FixtureWorld.Tick(introduction);
            SavesSystem.Collect();
            Assert(SavesSystem.GetRaw(Key) == raw, "坏 payload 不能被默认 DTO 覆盖");
            Stop();
        }
    }
    private static void DeathOwnerLifecycle()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        Health old = CharacterMainControl.Main.Health;
        CharacterMainControl replacement = new GameObject("replacement player").AddComponent<CharacterMainControl>();
        replacement.Health = replacement.gameObject.AddComponent<Health>();
        CharacterMainControl.Main = replacement;
        FixtureWorld.Tick(introduction, 4);
        Assert(old.OnDeadEvent.Count == 0 && replacement.Health.OnDeadEvent.Count == 1, "换主玩家必须移动唯一死亡订阅");
        old.Kill();
        Assert(binding.IsAccepted(), "已退订的旧主玩家死亡不能撤销当前任务");
        replacement.Health.Kill();
        Assert(!binding.IsAccepted(), "当前主玩家死亡必须撤销任务");
        Stop();
        Assert(replacement.Health.OnDeadEvent.Count == 0 && FixtureWorld.Projection.Binding == null,
            "关闭必须退订玩家事件并撤销共享客户端");
    }
    private static void ObjectiveMarker()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        FixtureWorld.SetBase(false);
        FixtureWorld.Tick(introduction);
        var marker = (Duckov.MiniMaps.SimplePointOfInterest)typeof(EclipseBloomIntroduction).GetField("marker", Private).GetValue(introduction);
        Assert(marker != null && marker.IsArea && marker.AreaRadius == 22f
            && marker.gameObject.scene.name == EclipseBloomEncounterPlacement.SceneName, "目标圈须实际归属 loaded 37 子场景");
        FixtureWorld.PlayerDeath();
        Assert((Duckov.MiniMaps.SimplePointOfInterest)typeof(EclipseBloomIntroduction).GetField("marker", Private).GetValue(introduction) == null,
            "死亡放弃必须立即移除目标圈");
    }
    private static void CachedFacts()
    {
        OfficialQuestBinding binding = Start();
        Accept(binding);
        SavesSystem.SetRaw(Key, "broken cache must not be re-read each frame");
        for (int i = 0; i < 20; i++) Assert(EclipseBloomIntroduction.CanSpawnEncounter, "普通查询不得重读已打开的 JSON");
        SavesSystem.SetSlot(1);
        Assert(!EclipseBloomIntroduction.CanSpawnEncounter && !binding.IsAccepted(), "实际换槽必须重新加载默认未接取状态");
    }
    private static void HostDestroyWhileAbandonFails()
    {
        OfficialQuestBinding binding = Start();
        SavesSystem.IsSaving = true;
        Accept(binding);
        Assert(SavesSystem.GetRaw(Key) == null, "前置 accepted 只入队且尚未写入官方缓存");
        FixtureWorld.SetBase(false);
        BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData> spec = StoreSpec();
        Func<EclipseBloomIntroductionData, string> encode = spec.Encode;
        spec.Encode = value => null;
        try
        {
            FixtureWorld.PlayerDeath();
            Assert(!binding.IsAccepted() && !EclipseBloomIntroduction.CanSpawnEncounter, "写失败必须先取消任务");
            SavesSystem.IsSaving = false;
            Stop();
            Assert(SavesSystem.GetRaw(Key) == null, "宿主销毁绕基地门时仍须拒绝冲出已失效的 accepted 批次");
        }
        finally { spec.Encode = encode; }
    }
}
