using System;
using System.Collections.Generic;
using System.IO;
using Saves;

namespace UnityEngine
{
    public enum SystemLanguage { Chinese }
    public static class Time { public static int frameCount; public static float timeScale = 1f; }
}
namespace Duckov.Economy { public static class EconomyManager { public static long Money = 42; } }
namespace SodaCraft.Localizations { public static class LocalizationManager { public static UnityEngine.SystemLanguage CurrentLanguage; } }
namespace Saves
{
    // Official SaveFile sets this private latch before physical I/O and has no finally.
    // FailPhysical deliberately leaves it set; only the linked production protector may release it.
    public static class SavesSystem
    {
        private static bool saving;
        public static bool IsSaving { get { return saving; } }
        public static int CurrentSlot = 1, Writes;
        public static bool FailPhysical;
        public static readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public static readonly Dictionary<string, object> Disk = new Dictionary<string, object>();
        public static void BeginOfficialSave() { saving = true; }
        public static void FinishOfficialSave() { saving = false; }
        public static void Reset() { saving = false; FailPhysical = false; Writes = 0; Data.Clear(); Disk.Clear(); }
        public static bool KeyExisits(string key) { return Data.ContainsKey(key); }
        public static T Load<T>(string key) { object value; return Data.TryGetValue(key, out value) ? (T)value : default(T); }
        public static void Save<T>(string key, T value) { Data[key] = value; }
        public static void SaveFile(bool ignored)
        {
            Writes++;
            saving = true;
            if (FailPhysical) throw new IOException("physical_save_failed");
            Disk.Clear();
            foreach (var pair in Data) Disk[pair.Key] = pair.Value;
            saving = false;
        }
    }
}
namespace BossRush
{
    public partial class ModBehaviour
    {
        public const bool DevModeEnabled = true;
        internal static readonly List<string> Logs = new List<string>();
        public static void DevLog(string message) { Logs.Add(message); }
    }
    internal sealed partial class F3GameplayValidationRunner
    {
        private static F3GameplayValidationRunner _instance;
        private static string _status;
        private bool _running;
        private string _runId = "run";
        private const string DedicatedSlotKey = "dedicated", RunMarkerKey = "run_marker", AutotestSnapshotKey = "snapshot";
        private static readonly int[] AutotestLedgerTypeIds = { 42 };
        private sealed class AutotestSnapshot
        {
            internal string RunId, Raw;
            internal int Slot;
            internal bool RawExists, BufferCountsIncluded, ForceNight;
            internal SkyIslandStoryData Data;
            internal readonly List<string> OfficialUnlocked = new List<string>();
            internal readonly Dictionary<int, int> Items = new Dictionary<int, int>();
            internal long Money;
            internal UnityEngine.SystemLanguage Language;
            internal float TimeScale;
        }
        private sealed class AutotestState { internal AutotestSnapshot Snapshot; internal bool SnapshotPersisted; }
        private readonly AutotestState _autotest = new AutotestState();
        internal static int ReportWrites;
        private static void EnsureAttached(ModBehaviour host) { }
        private static bool IsBaseScene() { return true; }
        private static string BuildDedicatedMarker() { return "dedicated_marker"; }
        private static bool IsDedicatedCurrentSlot() { return true; }
        private static bool AutotestWriteAllowed(out string reason) { reason = null; return true; }
        private static object SkyIslandSessionOrNull() { return null; }
        private static bool TryRecoverAutotestSnapshot(out string result) { result = "recovered"; return true; }
        private static string[] CollectOfficialSkyNotes() { return new[] { "note" }; }
        private static int CountOwnedItems(int type, out string where) { where = "ok"; return 0; }
        private static string EncodeAutotestSnapshot(AutotestSnapshot snapshot) { return "snapshot_json"; }
        private static void WriteAutotestTextFile(string path, string content) { ReportWrites++; }
        internal bool SnapshotPersisted { get { return _autotest.SnapshotPersisted; } }
        internal bool SnapshotAttached { get { return _autotest.Snapshot != null; } }
        internal bool Run(string method, out string reason)
        {
            string metrics = "";
            reason = null;
            switch (method)
            {
                case "dedicated": return TryMarkCurrentSlot(new ModBehaviour(), out reason);
                case "final": return ValidateFinalSaveReadback(out metrics, out reason);
                case "run-write": return WriteRunMarker();
                case "run-clear": ClearRunMarker(); return true;
                case "snapshot-write": return TakeAutotestSnapshotCore(ref metrics, out reason);
                case "snapshot-clear": return ClearAutotestSnapshotKey();
                case "codex": return ValidateCodexPersistenceReadback(out metrics, out reason);
                case "daily": return new ModBehaviour().ValidateDailyReportRollback(out metrics, out reason);
                default: throw new ArgumentException(method);
            }
        }
    }
    internal static class PetNestSaveCoordinator { internal static int Requests; internal static void RequestFlush() { Requests++; } }
    internal static class DailyReportSaveCoordinator { internal static int Requests; internal static void RequestFlush() { Requests++; } }
    internal static class CampaignContentCatalog { internal static string Source = "Json"; }
    internal static class SkyIslandStorySaveRecovery { internal static bool IsPending() { return false; } }
    internal static class SkyIslandNight { internal static bool DevForceNight; }
    internal sealed class SkyIslandStoryData
    {
        internal int flags, visitedRegions;
        internal string[] discoveredNotes = new string[0], clearedEncounters = new string[0];
    }
    internal static class SkyIslandStoryRules
    {
        internal const string StorageKey = "story";
        internal static SkyIslandStoryData CreateDefault() { return new SkyIslandStoryData(); }
    }
    internal static class SkyIslandStoryCodec { internal static SkyIslandStoryData Decode(string raw) { return SkyIslandStoryRules.CreateDefault(); } }
    internal sealed class CodexEntry { internal long Kills = 4; }
    internal sealed class CodexData { internal List<CodexEntry> Entries = new List<CodexEntry> { new CodexEntry() }; }
    internal static class CodexPersistence
    {
        internal static CodexData Current = new CodexData();
        internal static int Resets;
        internal static bool Store(CodexData data) { Current = data; return true; }
        internal static bool FlushPending() { return true; }
        internal static void ResetStaticCaches() { Resets++; }
        internal static void EnsureSubscribed() { }
    }
    internal sealed class DailyReportStats { }
    internal sealed class DailyReportData
    {
        internal int DayIndex = 1, LastSettledDayIndex, LastSignedDayIndex;
        internal bool BountyCompleted, BountyRewardClaimed, HasYesterday, PendingIssueBanner;
        internal double CarrySeconds;
        internal DailyReportStats Today, Yesterday;
        internal DailyReportData Clone() { return (DailyReportData)MemberwiseClone(); }
    }
    internal enum DailyReportSignInOutcome { Success, PersistBlocked }
    internal struct DailyReportSignInResult { internal DailyReportSignInOutcome Outcome; }
    internal static class DailyReportPersistence
    {
        internal static DailyReportData Current = new DailyReportData();
        internal static bool Reject;
        internal static int Resets;
        internal static bool Store(DailyReportData data) { if (Reject) return false; Current = data; return true; }
        internal static bool FlushPending() { return true; }
        internal static void SetValidationRejectStore(bool reject) { Reject = reject; }
        internal static void ResetStaticCaches() { Resets++; }
        internal static void EnsureSubscribed() { }
    }
    internal static class DailyReportService
    {
        internal static void NotifySlotChanged() { }
        internal static void ResetStaticCaches() { }
        internal static bool TrySignInToday(out DailyReportSignInResult result)
        {
            result = new DailyReportSignInResult { Outcome = DailyReportPersistence.Reject ? DailyReportSignInOutcome.PersistBlocked : DailyReportSignInOutcome.Success };
            return !DailyReportPersistence.Reject;
        }
        internal static void DebugAdvanceGameSeconds(double seconds)
        {
            var current = DailyReportPersistence.Current;
            current.LastSettledDayIndex = current.DayIndex;
            current.DayIndex++;
            current.HasYesterday = true;
        }
    }
    internal static class DailyReportCodec { internal static string Encode(DailyReportData data) { return data.DayIndex + ":" + data.LastSettledDayIndex; } }
    internal static class DailyReportTuning { internal const double GameSecondsPerDay = 86400d; }
    internal static class Program
    {
        private static int checks;
        private static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
        private static F3GameplayValidationRunner Reset(string method)
        {
            SavesSystem.Reset(); ModBehaviour.Logs.Clear();
            CodexPersistence.Resets = DailyReportPersistence.Resets = 0;
            DailyReportPersistence.Current = new DailyReportData(); DailyReportPersistence.Reject = false;
            F3GameplayValidationRunner.ReportWrites = 0;
            PetNestSaveCoordinator.Requests = DailyReportSaveCoordinator.Requests = 0;
            if (method == "snapshot-clear") SavesSystem.Data["snapshot"] = SavesSystem.Disk["snapshot"] = "original_snapshot";
            return new F3GameplayValidationRunner();
        }
        private static bool Invoke(F3GameplayValidationRunner runner, string method)
        {
            string reason;
            try { return runner.Run(method, out reason); }
            // The daily method intentionally propagates to RunSyncCase's catch in production.
            catch (IOException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        private static void Main()
        {
            foreach (string method in new[] { "dedicated", "final", "run-write", "run-clear", "snapshot-write", "snapshot-clear", "codex", "daily" })
            {
                var runner = Reset(method); SavesSystem.FailPhysical = true;
                bool accepted = Invoke(runner, method);
                Check(!accepted || method == "run-clear", method + ": failed physical save is not accepted");
                Check(SavesSystem.Writes == 1 && !SavesSystem.IsSaving, method + ": failed writer releases its official saving latch");
                if (method == "snapshot-write") Check(!runner.SnapshotPersisted && !runner.SnapshotAttached && F3GameplayValidationRunner.ReportWrites == 0, "failed snapshot is not committed or written to report");
                if (method == "snapshot-clear") Check((string)SavesSystem.Data["snapshot"] == "original_snapshot" && (string)SavesSystem.Disk["snapshot"] == "original_snapshot", "failed clear preserves cached and durable recovery key");
                if (method == "codex") Check(CodexPersistence.Resets == 0, "failed codex write skips cache reset and readback success path");
                if (method == "daily") Check(!DailyReportPersistence.Reject && DailyReportPersistence.Resets == 0, "daily failure clears validation store rejection in finally");
                if (method == "run-clear") Check(ModBehaviour.Logs.Count == 1, "clear marker keeps its failure log");
                SavesSystem.FailPhysical = false;
                BossRushSaveFileThrottle.RunSaveFile(() => SavesSystem.SaveFile(false));
                Check(SavesSystem.Writes == 2 && !SavesSystem.IsSaving, method + ": another subsystem can save in the same session after failure");

                runner = Reset(method);
                Check(Invoke(runner, method) && SavesSystem.Writes == 1 && !SavesSystem.IsSaving, method + ": success keeps exactly one physical save");
                if (method == "snapshot-write") Check(runner.SnapshotPersisted && runner.SnapshotAttached && F3GameplayValidationRunner.ReportWrites == 1, "successful snapshot is committed and reported");
                if (method == "snapshot-clear") Check((string)SavesSystem.Disk["snapshot"] == "", "successful clear persists the empty recovery key");
                if (method == "codex") Check(CodexPersistence.Resets == 1, "successful codex save still resets caches for readback");
                if (method == "daily") Check(!DailyReportPersistence.Reject && DailyReportPersistence.Resets == 1, "successful daily save preserves rejection cleanup and readback");
                if (method == "final") Check(PetNestSaveCoordinator.Requests == 1 && DailyReportSaveCoordinator.Requests == 1, "final save still requests both pending flushes");

                runner = Reset(method); SavesSystem.BeginOfficialSave(); Invoke(runner, method);
                Check(SavesSystem.Writes == 0 && SavesSystem.IsSaving, method + ": existing official save is neither reentered nor released");
                SavesSystem.FinishOfficialSave();
            }
            Console.WriteLine("PASS F3SaveLatch " + checks + " assertions across 8 production save paths");
        }
    }
}
