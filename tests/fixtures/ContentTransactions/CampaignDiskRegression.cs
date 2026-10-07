using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BossRush;
using Duckov.Economy;
using Saves;

partial class Program
{
    static bool RunCampaignDiskRegression(string[] args)
    {
        if (args.Length != 3 || !args[0].StartsWith("--campaign-disk-", StringComparison.Ordinal)) return false;
        Reset();
        SavesSystem.CurrentSlot = 7;
        string scenario = args[2];
        CampaignChapterState expected = scenario == "completed" ? CampaignChapterState.Completed : CampaignChapterState.ContractActive;
        if (args[0] == "--campaign-disk-write")
        {
            SavesSystem.DiskPath = args[1];
            // 真实 runtime 的 bootstrap：先订阅共享 store，再加载进度。
            CampaignSaveCoordinator.EnsureSubscribed();
            CampaignProgressService.EnsureInitialized();
            if (scenario == "completed")
            {
                Check(CampaignProgressService.TryAcceptContract("ch1"), "disk fixture accepts chapter before delivery");
                Check(CampaignProgressService.NotifyObjectivesSatisfied("ch1"), "disk fixture reaches delivery through the production event");
                UnityEngine.Time.frameCount++;
                SavesSystem.FailKey = CampaignTuning.ProgressSaveKey;
                Check(CampaignProgressService.TryDeliver("ch1"), "delivery keeps the accepted completion when its first key write fails");
            }
            else
            {
                if (scenario == "readback") SavesSystem.FailReadAfterSaveKey = CampaignTuning.ProgressSaveKey;
                else SavesSystem.FailKey = CampaignTuning.ProgressSaveKey;
                Check(CampaignProgressService.TryAcceptContract("ch1"), "acceptance reaches the production pending store");
            }
            Check(CampaignPersistence.IsSubscribed && CampaignPersistence.IsStoreFaulted,
                "failure occurs on the same subscribed store used by the real runtime");
            UnityEngine.Time.unscaledTime += 2f;
            UnityEngine.Time.frameCount++;
            CampaignSaveCoordinator.Tick();
            Check(!CampaignPersistence.IsStoreFaulted && CampaignPersistence.IsSubscribed,
                "regular coordinator tick replaces the faulted subscribed writer");
            Check(CampaignProgressService.GetState("ch1") == expected,
                "subscribed recovery must not discard the accepted chapter snapshot");
            Check(File.Exists(args[1]) && !CampaignPersistence.HasPendingWrite,
                "recovered chapter reaches the physical file adapter before this process exits");
            Check(scenario != "completed" || DiskMoney == 104000,
                "recovery preserves the credited money obligation with chapter completion");
        }
        else if (args[0] == "--campaign-disk-read")
        {
            SavesSystem.LoadCampaignDisk(args[1]);
            // 提前只读初始化后再次走真实 bootstrap，重订阅必须重读同槽盘上事实。
            CampaignProgressService.EnsureInitialized();
            CampaignSaveCoordinator.EnsureSubscribed();
            CampaignProgressService.EnsureInitialized();
            Check(CampaignProgressService.GetState("ch1") == expected,
                "a separate fresh process restores the recovered chapter using only the file bytes");
            Check(!CampaignProgressService.TryAcceptContract("ch1"), "Jeff cannot offer the first chapter again after recovered progress reload");
            if (scenario == "completed")
                Check(!CampaignProgressService.TryDeliver("ch1") && EconomyManager.Money == 104000,
                    "fresh process neither loses the reward nor pays a completed chapter twice");
            SavesSystem.SetFile(8);
            Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Available,
                "changing to an empty slot does not inherit the recovered chapter");
        }
        else throw new InvalidOperationException("Unknown campaign disk fixture mode");
        Console.WriteLine("Campaign disk process PASS: " + scenario + " / " + args[0]);
        return true;
    }
}

namespace Saves
{
    // 官方与文件 IO 边界：只保存测试文件中的 JSON 字符串与钱包数值。
    // 生产 key/store/协调器/异常恢复全部直接链接；不访问游戏或玩家存档。
    static partial class SavesSystem
    {
        public static string DiskPath;
        internal static void SaveDiskIfRequested()
        {
            if (DiskPath == null) return;
            object progress, economy;
            var data = new Dictionary<string, object>();
            if (Disk.TryGetValue(CampaignTuning.ProgressSaveKey, out progress)) data[CampaignTuning.ProgressSaveKey] = progress;
            if (Disk.TryGetValue("EconomyData", out economy)) data["money"] = ((EconomyManager.SaveData)economy).money;
            File.WriteAllText(DiskPath, JsonSerializer.Serialize(data));
        }
        internal static void LoadCampaignDisk(string path)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                Cache.Clear();
                Cache[CampaignTuning.ProgressSaveKey] = document.RootElement.GetProperty(CampaignTuning.ProgressSaveKey).GetString();
                EconomyManager.Money = document.RootElement.GetProperty("money").GetInt64();
                Cache["EconomyData"] = new EconomyManager.SaveData { money = EconomyManager.Money };
                Disk = new Dictionary<string, object>(Cache);
            }
        }
    }
}
