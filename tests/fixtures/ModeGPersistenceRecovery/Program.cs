using System;
using System.Diagnostics;
using System.Reflection;
using BossRush;
using Saves;
using Cysharp.Threading.Tasks;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static void QueueBoth()
    {
        Check(ModeGProfilePersistence.Store(new ModeGProfilePersistence.ProfileDto { totalRuns = 4 }), "profile accepted");
        Check(ModeGNemesisPersistence.Store(new ModeGNemesisPersistence.NemesisRecordDto { bossPresetKey = "boss", rank = 2 }), "nemesis accepted");
    }
    private static void CheckIndependentSave()
    {
        SavesSystem.FailAt = null;
        BossRushSaveFileThrottle.RunSaveFile(() => SavesSystem.SaveFile(false));
        Check(!SavesSystem.IsSaving && SavesSystem.Writes == 2, "other subsystem can still save after Mode G I/O failure");
    }
    private static void Run(string scenario)
    {
        string[] parts = scenario.Split(':');
        if (parts[0] == "profile" || parts[0] == "nemesis")
        {
            bool profile = parts[0] == "profile";
            if (profile) ModeGProfilePersistence.Store(new ModeGProfilePersistence.ProfileDto { totalRuns = 4 });
            else ModeGNemesisPersistence.Store(new ModeGNemesisPersistence.NemesisRecordDto { bossPresetKey = "boss", rank = 2 });
            SavesSystem.FailAt = parts[1];
            if (profile) ModeGProfilePersistence.FlushPending(true); else ModeGNemesisPersistence.FlushPending(true);
            Check(!SavesSystem.IsSaving, scenario + ": failed synchronous writer released official saving latch");
            Check(profile ? ModeGProfilePersistence.HasPendingFlush : ModeGNemesisPersistence.HasPendingFlush, "direct flush failure retains pending DTO");
            Check(profile ? ModeGProfilePersistence.IsStoreFaulted : ModeGNemesisPersistence.IsStoreFaulted, "direct flush failure remains one-way faulted");
            CheckIndependentSave();
            Check(profile ? !ModeGProfilePersistence.Store(new ModeGProfilePersistence.ProfileDto()) : !ModeGNemesisPersistence.Store(new ModeGNemesisPersistence.NemesisRecordDto()), "failure recovery does not reopen Mode G writes");
        }
        else if (parts[0] == "coordinator" || parts[0] == "destroy")
        {
            QueueBoth(); SavesSystem.FailAt = parts[1];
            if (parts[0] == "destroy")
            { ModeGRuntimeGates.IsModeGHostFileWriteDeferred = true; ModeGPersistenceFlushCoordinator.TryFlushOnHostDestroy(); }
            else UniTask.Frame();
            Check(!SavesSystem.IsSaving, scenario + ": coordinated writer released official saving latch");
            Check(SavesSystem.Writes == 1 && SavesSystem.TypedWrites == 2, "one physical write after both typed DTO saves");
            Check(!ModeGProfilePersistence.HasPendingFlush && !ModeGNemesisPersistence.HasPendingFlush, "successful typed flush preserves prior pending-cleared contract");
            Check(ModeGPersistenceFlushCoordinator.IsFaulted && ModeGProfilePersistence.IsStoreFaulted && ModeGNemesisPersistence.IsStoreFaulted, "coordinator and both DTOs remain one-way faulted");
            UniTask.Frame(); ModeGPersistenceFlushCoordinator.RequestFlush(); UniTask.Frame();
            Check(SavesSystem.Writes == 1, "faulted coordinator does not retry itself"); CheckIndependentSave();
        }
        else if (scenario == "busy")
        {
            SavesSystem.BeginOfficialSave(); QueueBoth(); UniTask.Frame();
            Check(SavesSystem.IsSaving && SavesSystem.Writes == 0 && SavesSystem.TypedWrites == 0, "busy official save not reset or reentered");
            SavesSystem.FinishOfficialSave(); UniTask.Frame();
            Check(SavesSystem.Writes == 1 && !SavesSystem.IsSaving && !ModeGPersistenceFlushCoordinator.IsFaulted, "pending batch resumes after official save finishes");
        }
        else if (scenario == "combat")
        {
            ModeGRuntimeGates.IsModeGHostFileWriteDeferred = true; QueueBoth(); UniTask.Frame();
            Check(SavesSystem.TypedWrites == 2 && SavesSystem.Writes == 0, "combat keeps typed writes and defers host file");
            ModeGRuntimeGates.IsModeGHostFileWriteDeferred = false; ModeGPersistenceFlushCoordinator.RequestFlush(); UniTask.Frame();
            Check(SavesSystem.TypedWrites == 2 && SavesSystem.Writes == 1 && !ModeGPersistenceFlushCoordinator.IsFaulted, "noncombat drains deferred debt once without duplicate typed writes");
        }
        Console.WriteLine(scenario + ": " + checks + " PASS / 0 FAIL");
    }
    private static int Main(string[] args)
    {
        if (args.Length > 0) { Run(args[0]); return 0; }
        int cases = 0;
        foreach (string writer in new[] { "profile", "nemesis", "coordinator", "destroy" })
            foreach (string failure in new[] { "backup", "indexed-backup", "store" })
            { Child(writer + ":" + failure); cases++; }
        Child("busy"); Child("combat"); cases += 2;
        Console.WriteLine("ModeGPersistenceRecovery: " + cases + " isolated process cases PASS / 0 FAIL"); return 0;
    }
    private static void Child(string scenario)
    {
        var start = new ProcessStartInfo("dotnet", "\"" + Assembly.GetExecutingAssembly().Location + "\" " + scenario) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (Process process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
            Console.Write(output); if (process.ExitCode != 0) throw new Exception(scenario + " failed: " + error);
        }
    }
}
