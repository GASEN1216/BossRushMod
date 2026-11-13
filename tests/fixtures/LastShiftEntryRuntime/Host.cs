using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NodeCanvas.DialogueTrees;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class UIInputEventData
{
    public bool Used { get; private set; }
    public void Use() { Used = true; }
}

public static class UIInputManager
{
    public static event Action<UIInputEventData> OnCancelEarly;
    public static int CancelSubscribers { get { return OnCancelEarly == null ? 0 : OnCancelEarly.GetInvocationList().Length; } }
    public static bool Escape()
    {
        var data = new UIInputEventData();
        OnCancelEarly?.Invoke(data);
        return data.Used;
    }
}

public class SceneLoadingContext { }

public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static int Subscribers { get { return onStartedLoadingScene == null ? 0 : onStartedLoadingScene.GetInvocationList().Length; } }
    public static void Begin()
    {
        IsSceneLoading = true;
        onStartedLoadingScene?.Invoke(new SceneLoadingContext());
    }
}

public class LevelManager
{
    public static LevelManager Instance = new LevelManager();
    public static bool LevelInitializing, LevelInited = true;
    public static event Action OnAfterLevelInitialized;
    public bool IsRaidMap = true;
    public static void Initialized() { OnAfterLevelInitialized?.Invoke(); }
}

public static class RaidUtilities
{
    public struct RaidInfo { public bool ended; public uint ID; }
    public static RaidInfo CurrentRaid;
}

public class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public readonly Health Health = new Health();
}

public class Health { public bool IsDead; }
public class DuckovDialogueActor : MonoBehaviour, IDialogueActor
{
    public string NameKey;
    public string DisplayName { get { return BossRush.LocalizationHelper.Texts[NameKey]; } }
}

namespace SodaCraft.Localizations
{
    public static class LocalizationManager
    {
        public static event Action<SystemLanguage> OnSetLanguage;
        public static int Subscribers { get { return OnSetLanguage == null ? 0 : OnSetLanguage.GetInvocationList().Length; } }
        public static void SetLanguage(SystemLanguage language)
        {
            BossRush.L10n.Chinese = language == SystemLanguage.Chinese;
            OnSetLanguage?.Invoke(language);
        }
    }
}

namespace Duckov.MiniMaps
{
    public class SimplePointOfInterest : MonoBehaviour
    {
        public Color Color;
        public float ScaleFactor;
        public bool IsArea;
        public string Key, DisplayedName;
        public void Setup(object icon, string displayName)
        {
            Key = displayName;
            DisplayedName = BossRush.LocalizationHelper.Texts[displayName];
        }
    }
}

namespace Duckov.UI.DialogueBubbles
{
    public static class DialogueBubblesManager
    {
        public static int Calls;
        public static UniTaskVoid Show(string text, Transform target, float time, bool a, bool b, float c, float d)
        { Calls++; return new UniTaskVoid { task = Task.CompletedTask }; }
    }
}

namespace ItemStatsSystem
{
    public class Item
    {
        public object InInventory, PluggedIntoSlot;
        public void Drop(CharacterMainControl player, bool notify) { }
    }

    public static class ItemAssetsCollection
    {
        public static int Given;
        public static Item InstantiateSync(int id) { Given++; return new Item(); }
    }
}

namespace BossRush.Utils
{
    public static class ItemUtilities
    {
        public static void SendToPlayer(ItemStatsSystem.Item item, bool a, bool b) { item.InInventory = new object(); }
    }
}

namespace BossRush
{
    public abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public abstract void OnAwake(ModBehaviour host);
        public abstract void OnUpdate(float delta, float unscaledDelta);
        public abstract void OnDestroy();
    }

    public partial class ModBehaviour : MonoBehaviour
    {
        public static string ModPath;
        public static string GetModPath() { return ModPath; }
        public static void CriticalLog(string key, string text) { }
        public static void DevLog(string message) { }
        public void ShowBigBanner(string text) { }
        public void ShowMessage(string text) { }
    }

    public static class ModRaidSceneBridge
    {
        public static void Register(ModRaidSceneIdentity identity) { identity.Inject(); }
        public static void Unregister(ModRaidSceneIdentity identity) { }
        public static bool IsScene(ModRaidSceneIdentity identity, Scene scene) { return false; }
    }

    public class ModRaidSceneIdentity
    {
        internal readonly Action Inject;
        public ModRaidSceneIdentity(string id, string path, string guid, string name, Action inject, string logTag)
        { Inject = inject; }
    }

    public class ModRaidProfile
    {
        public ModRaidSceneIdentity Identity;
        public string BundleRelativePath, ServicesRootName, WorldRootName, SpawnMarkerPath;
        public string[] RequiredWorldPaths;
        public Action<object> AdjustTimeOfDay;
        public Action<Scene> SceneLoaded, SceneUnloaded;
        public Func<string> DisplayName;
    }

    public static class ModRaidLease
    {
        public static bool IsBundleDeployed(ModRaidProfile profile)
        { return File.Exists(Path.Combine(ModBehaviour.ModPath, profile.BundleRelativePath)); }
    }

    public static class SkyIslandExplosionObstaclePatch
    {
        public static void Arm(Scene scene) { }
        public static void Disarm() { }
    }

    public class LastShiftStoryPersistence : IDisposable
    {
        public LastShiftStoryPersistence(Action changed) { }
        public void Dispose() { }
    }

    public static class LastShiftNoteBridge
    {
        public static void RequestSync() { }
        public static void InjectNoteKeys() { }
        public static void EnsureRuntime() { }
        public static void ResetStaticCaches() { }
    }

    public class LastShiftJeffIntro : IDisposable
    {
        public LastShiftJeffIntro(object projection) { }
        public void Tick() { }
        public void Dispose() { }
    }

    public class OfficialQuestRuntime { public object Projection; }
    public partial class ModBehaviour { public OfficialQuestRuntime OfficialQuestRuntime; }

    public static class LastShiftChiefFx { public static void ResetStaticCaches() { } }
    public static class SceneRuntimeGate { public static bool IsBaseHubSceneName(string value) { return value == "Base"; } }
    public class TidegateSession : MonoBehaviour { }
    public class SkyIslandSession : MonoBehaviour { }
    public class LastShiftSession : MonoBehaviour
    {
        public static int Departures;
        public bool IsClosed;
        public static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = "entry unavailable";
            return owner.GetComponent<LastShiftSession>() == null && !SceneLoader.IsSceneLoading;
        }
        public static void Enter(ModBehaviour owner, Action<string, bool> report)
        {
            Departures++;
            owner.gameObject.AddComponent<LastShiftSession>();
        }
        public void Close(bool restorePlayer, string reason) { IsClosed = true; }
    }

    public static class ResourceBundleLoader { public static AssetBundle LoadFromFile(string path) { return new AssetBundle(); } }

    public static class LastShiftEntryCollision
    {
        public static void Attach(Transform model) { }
    }

    public class LastShiftTicketPunch : MonoBehaviour
    {
        public static int Punches;
        public static void Attach(Transform model) { model.gameObject.AddComponent<LastShiftTicketPunch>(); }
        public void Press() { Punches++; }
    }

    public static class LastShiftSounds
    {
        public const string TicketPunch = "TicketPunch";
        public static void Play(string id, GameObject at) { }
        public static void ResetStaticCaches() { }
    }

    public abstract class BossRushBuildingInteractableBase : MonoBehaviour
    {
        protected abstract string InteractNameKey { get; }
        protected abstract string LogPrefix { get; }
        protected abstract bool IsBuildingInteractable();
        protected abstract void OnInteractCompleted();
        public bool CanUse { get { return IsBuildingInteractable(); } }
        public void Interact() { if (CanUse) OnInteractCompleted(); }
    }

    public static class NPCInteractionGroupHelper
    {
        public static T GetOrCreateStandaloneInteractable<T>(Transform parent, string name, Vector3 pos, Action<T> setup)
            where T : MonoBehaviour, new()
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<SphereCollider>();
            T result = go.AddComponent<T>();
            setup(result);
            return result;
        }
    }

    public class LastShiftDeviceInteractable : BossRushBuildingInteractableBase
    {
        private Func<bool> available;
        private Action callback;
        protected override string InteractNameKey { get { return "Talk"; } }
        protected override string LogPrefix { get { return "[Talk]"; } }
        public void Bind(string id, float seconds, Func<string> label, Func<bool> canUse, Action action)
        { available = canUse; callback = action; }
        protected override bool IsBuildingInteractable() { return available(); }
        protected override void OnInteractCompleted() { callback(); }
    }

    public sealed class CustomFaceSettingData { }

    public sealed class DuckNpcBlueprint
    {
        public string id = LastShiftYuXiang.BlueprintId;
        public float modelScale = 1f;
        public bool invincible = true, showHealthBar = false, pushCharacter = false;
        public string baseModel = null;
        public int[] equipmentTypeIds = { 1136, 791 };
        public string[] equipmentSlots = null;
        public int equipmentSeed = 0;
        public bool HasEquipment { get { return equipmentTypeIds.Length > 0; } }
        public int ResolveTeam() { return 0; }
    }

    public static class DuckNpcRegistry
    {
        public static bool TryGet(string id, out DuckNpcBlueprint blueprint)
        { blueprint = new DuckNpcBlueprint(); return id == blueprint.id; }
        public static bool TryResolveFace(DuckNpcBlueprint blueprint, out CustomFaceSettingData face)
        { face = null; return false; }
    }

    public static class DuckNpcFaceCatalog { public static object ResolveModelByName(string name) { return null; } }

    public sealed class DuckNpcSpawnRequest
    {
        public string NpcId;
        public Vector3 Position, Facing;
        public int Team;
        public float ModelScale;
        public bool Invincible, ShowHealthBar, PushCharacter, HasFace;
        public object BaseModelOverride;
        public CustomFaceSettingData Face;
    }

    public static class DuckNpcFactory
    {
        public static readonly List<TaskCompletionSource<CharacterMainControl>> Requests = new List<TaskCompletionSource<CharacterMainControl>>();
        public static readonly List<CharacterMainControl> Released = new List<CharacterMainControl>();
        public static GameObject OfficialHolder;

        public static UniTask<CharacterMainControl> SpawnAsync(DuckNpcSpawnRequest value)
        {
            var request = new TaskCompletionSource<CharacterMainControl>();
            Requests.Add(request);
            return new UniTask<CharacterMainControl> { task = request.Task };
        }

        public static CharacterMainControl Create(int index, bool success = true)
        {
            var npc = success ? new GameObject("YuXiang").AddComponent<CharacterMainControl>() : null;
            if (npc != null)
            {
                npc.transform.SetParent(OfficialHolder.transform, true);
                npc.transform.position = LastShiftYuXiang.Position;
            }
            Requests[index].SetResult(npc);
            return npc;
        }

        public static CharacterMainControl Complete(int index)
        {
            var npc = Create(index);
            if (npc != null) DuckNpcOutfitter.Complete(npc);
            return npc;
        }

        public static void Despawn(CharacterMainControl npc)
        {
            if (npc == null) return;
            Released.Add(npc);
            UnityEngine.Object.Destroy(npc.gameObject);
        }
    }

    public sealed class DuckNpcOutfitResult
    {
        public int EquippedCount;
        public readonly List<string> Skipped = new List<string>();
    }

    public static class DuckNpcOutfitter
    {
        public static readonly Dictionary<CharacterMainControl, TaskCompletionSource<DuckNpcOutfitResult>> Pending
            = new Dictionary<CharacterMainControl, TaskCompletionSource<DuckNpcOutfitResult>>();

        public static UniTask<DuckNpcOutfitResult> EquipByTypeIdsAsync(CharacterMainControl npc, int[] ids)
        {
            var request = new TaskCompletionSource<DuckNpcOutfitResult>();
            Pending.Add(npc, request);
            return new UniTask<DuckNpcOutfitResult> { task = request.Task };
        }

        public static UniTask<DuckNpcOutfitResult> EquipRandomSeededAsync(CharacterMainControl npc, string[] slots, int seed)
        { throw new InvalidOperationException("Expected fixed equipment"); }

        public static void Complete(CharacterMainControl npc)
        {
            var request = Pending[npc];
            Pending.Remove(npc);
            request.SetResult(new DuckNpcOutfitResult { EquippedCount = 2 });
        }
    }

    public static class NPCShopSystem { public static void CloseShopIfOwnedBy(Transform owner) { } }

    public static class DialogueActorFactory
    {
        public static bool Fail;
        public static readonly HashSet<GameObject> Cached = new HashSet<GameObject>();
        public static DuckovDialogueActor Create(GameObject host, string id, string key, Vector3 offset)
        {
            if (Fail) return null;
            var actor = host.AddComponent<DuckovDialogueActor>();
            actor.NameKey = key;
            Cached.Add(host);
            return actor;
        }
        public static void Remove(GameObject host)
        {
            if (!ReferenceEquals(host, null)) Cached.Remove(host);
        }
    }

    public static class BossRushUIColors { public static Color WarningText; }
    public static class BossRushItemIds { public const int LastShiftTicketStub = 500118, LastShiftStamp = 500117; }
    public static class TidegateInventory
    {
        public static bool HasStub, HasStamp;
        public static bool Carries(CharacterMainControl player, int id)
        { return id == BossRushItemIds.LastShiftTicketStub ? HasStub : HasStamp; }
    }

    public class LastShiftStoryData
    {
        public bool Met, Journal;
        public int Notes, Runs, Told;
    }

    public static class LastShiftStory
    {
        public const int AllNotes = 7, NoteCount = 3;
        public static LastShiftStoryData Current = new LastShiftStoryData();
        public static LastShiftJeffIntro JeffIntro;
        public static bool FailSave;
        public static bool Journal { get { return Current.Journal; } }
        public static void Bind(LastShiftStoryPersistence persistence) { }
        public static void Tick() { }
        public static bool TryShowNotice() { return false; }
        public static void MarkMet() { Current.Met = true; }
        public static void MarkTold(int bits) { Current.Told |= bits; }

        public static bool TryHandIn(CharacterMainControl player)
        {
            if (Current.Journal || !TidegateInventory.HasStub || FailSave) return false;
            Current.Journal = true;
            TidegateInventory.HasStub = false;
            return true;
        }
    }

    public enum LastShiftEvent { Slow, Heavy, Express }
}
