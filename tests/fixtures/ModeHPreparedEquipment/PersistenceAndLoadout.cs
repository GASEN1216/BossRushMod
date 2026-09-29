using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BossRush
{
    static class PersistenceAndLoadout
    {
        static void Check(bool condition, string reason)
        { if (!condition) throw new Exception(reason); Console.WriteLine("PASS " + reason); }

        internal static void Run()
        {
            var season = new ModeHSeasonDto
            {
                profiles = new List<ModeHProfileDto> { new ModeHProfileDto
                    { profileId = "starter", scarIds = new List<string> { "first" }, enteredMatchCount = 2 } },
                matchRoster = new ModeHMatchRosterDto
                {
                    matchIndex = 2, matchStarterProfileId = "starter", matchRelayProfileId = "relay",
                    starterKitIds = new List<string> { "helmet" }, relayKitIds = new List<string> { "armor" },
                },
            };
            string reason;
            SavesSystem.IsSaving = true;
            Check(ModeHProfilePersistence.StageWrite(season, out reason), "busy save accepts immutable season snapshot");
            Check(!ModeHProfilePersistence.FlushPending(), "busy official save leaves pending snapshot queued");
            season.profiles[0].enteredMatchCount++;
            season.profiles[0].scarIds.Add("later");
            season.matchRoster.starterKitIds.Clear();
            SavesSystem.IsSaving = false;
            Check(ModeHProfilePersistence.FlushPending(), "runtime mutations cannot invalidate pending digest");
            var saved = ModeHProfilePersistence.Cached;
            Check(saved.profiles[0].enteredMatchCount == 2 && saved.profiles[0].scarIds.Count == 1
                && saved.matchRoster.starterKitIds.Count == 1, "nested lists and DTOs preserve the staged moment");
            Check(ModeHProfilePersistence.StageWrite(season, out reason), "new state can be staged after prior snapshot");
            SavesSystem.FailNext = true;
            Check(!ModeHProfilePersistence.FlushPending(), "temporary write exception retains pending state");
            Check(ModeHProfilePersistence.FlushPending(), "temporary write exception does not permanently lock the slot");

            var runtime = new ModeHRuntimeModule { _season = saved };
            var starter = runtime.Default(new ModeHProfileDto { profileId = "starter" });
            var relay = runtime.Default(new ModeHProfileDto { profileId = "relay" });
            Check(starter.Count == 1 && starter[0] == "helmet" && relay[0] == "armor",
                "next-match equipment follows fighter identity across seat changes");
            starter.Clear();
            Check(saved.matchRoster.starterKitIds.Count == 1, "next-match selection cannot mutate previous roster");
            Check(runtime.Default(new ModeHProfileDto { profileId = "new" }).Count == 0,
                "new fighter without history uses original full outfit");

            var bag = new Inventory { Capacity = 2 };
            bag.Content.Add(new Item()); bag.Content.Add(new Item());
            ItemAssetsCollection.Prefabs[701] = new Item { TypeID = 701, IsBullet = true, MaxStackCount = 6 };
            var application = new ModeHKitApplication();
            Check(ModeHLoadoutKitApplicator.Store(bag, 701, 240, null, application, out reason),
                "full temporary fighter bag accepts 240 low-stack reserve rounds");
            int rounds = 0;
            foreach (Item item in bag.Content) if (item.TypeID == 701) rounds += item.StackCount;
            Check(rounds == 240 && bag.Content.Count == 42 && bag.Capacity == 42,
                "reserve expansion preserves loot and exact frozen ammunition count");
            var magazine = new Inventory { Capacity = 1 };
            magazine.Content.Add(new Item());
            Check(!ModeHLoadoutKitApplicator.Store(magazine, 701, 6, new ItemSetting_Gun(),
                new ModeHKitApplication(), out reason) && magazine.Capacity == 1,
                "gun magazine capacity is never expanded");
        }
    }

    partial class ModeHRuntimeModule
    {
        internal ModeHSeasonDto _season;
        internal List<string> Default(ModeHProfileDto profile) { return BuildDefaultKitSelection(profile); }
    }
    static partial class ModeHProfilePersistence
    {
        static readonly object _lock = new object();
        static bool _writeBarrier, _storeFaulted;
        static int _slotGeneration;
        static ModeHSeasonDto _pending, _cache;
        static string _pendingDigest, _lastError;
        static string StorageKey { get { return "fixture_season"; } }
        internal static ModeHSeasonDto Cached { get { return _cache; } }
    }
    static class ModeHContentCatalog { internal const string ContentCatalogSignature = "content"; }
    static class ModeHConfig
    {
        internal const int CurrentSchemaVersion = 1, CurrentSignatureAlgorithmVersion = 1;
        internal const int MaxKitsPerFighter = 4;
    }
    // Persistence boundary uses an independent round trip; digest formula itself has separate canonical tests.
    static class SavesSystem
    {
        internal static bool IsSaving, FailNext;
        static string data;
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        internal static void Save<T>(string key, T value)
        {
            if (FailNext) { FailNext = false; throw new IOException("fixture temporary IO failure"); }
            data = JsonSerializer.Serialize(value, Json);
        }
        internal static T Load<T>(string key) { return JsonSerializer.Deserialize<T>(data, Json); }
    }
    static class ModeHCanonicalDigest
    {
        internal static bool TryGetGameBuildSignature(out string signature, out string reason)
        { signature = "game"; reason = null; return true; }
        internal static bool TryGetModBuildSignature(out string signature, out string reason)
        { signature = "mod"; reason = null; return true; }
        internal static bool IsValidDigest(string digest) { return digest != null && digest.Length == 64; }
        internal static bool TryComputeObjectDigest(object value, string excluded, out string digest, out string reason)
        {
            var copy = JsonSerializer.Deserialize<ModeHSeasonDto>(JsonSerializer.Serialize(value, SavesSystem.Json), SavesSystem.Json);
            copy.payloadDigest = null;
            using (var hash = SHA256.Create()) digest = Convert.ToHexString(hash.ComputeHash(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(copy, SavesSystem.Json))));
            reason = null; return true;
        }
    }
    class Inventory
    {
        public int Capacity;
        public readonly List<Item> Content = new List<Item>();
        public int GetFirstEmptyPosition(int first) { return Content.Count < Capacity ? Content.Count : -1; }
        public void SetCapacity(int capacity) { Capacity = capacity; }
        public bool AddItem(Item item)
        { if (Content.Count >= Capacity) return false; Content.Add(item); return true; }
    }
    class ModeHKitApplication { public List<Item> CreatedItems = new List<Item>(); }
    static partial class ModeHLoadoutKitApplicator
    {
        internal static bool Store(Inventory inventory, int id, int count, ItemSetting_Gun gun,
            ModeHKitApplication application, out string reason)
        { return TryStoreAmmo(inventory, id, count, gun, application, out reason); }
    }
}
