using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

// 只替换宿主 IO、Unity 和官方任务投影；引导状态机、JSON codec、共享存档管线均直接链接生产源码。
public sealed class CharacterRandomPreset : UnityEngine.Object
{ internal CharacterRandomPreset Clone() { return new CharacterRandomPreset(); } }
public sealed class DamageInfo { }
public sealed class Health : MonoBehaviour
{
    public bool IsDead;
    public readonly UnityEvent<DamageInfo> OnDeadEvent = new UnityEvent<DamageInfo>();
    public void Kill() { IsDead = true; OnDeadEvent.Invoke(new DamageInfo()); }
}
public sealed class CharacterMainControl : MonoBehaviour
{ public static CharacterMainControl Main; public Health Health; }
public sealed class LevelManager : MonoBehaviour
{ public static LevelManager Instance; public static bool AfterInit; public bool IsBaseLevel; }
public static class SceneLoader { public static bool IsSceneLoading; }

namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot;
        public static bool IsSaving, FailWrites, FailPhysicalWrites;
        public static int PhysicalSaves;
        public static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        public static readonly Dictionary<int, Dictionary<string, string>> Slots = new Dictionary<int, Dictionary<string, string>>();
        public static readonly List<Tuple<int, string, string>> Writes = new List<Tuple<int, string, string>>();
        public static int Subscribers { get { return Count(OnCollectSaveData) + Count(OnSetFile) + Count(OnSaveDeleted); } }
        public static int SubscriptionCount { get { return Subscribers; } }
        private static int Count(Action action) { return action == null ? 0 : action.GetInvocationList().Length; }
        private static Dictionary<string, string> Slot
        { get { Dictionary<string, string> values; if (!Slots.TryGetValue(CurrentSlot, out values)) { values = new Dictionary<string, string>(); Slots.Add(CurrentSlot, values); } return values; } }
        public static void Reset(int slot = 0)
        {
            CurrentSlot = slot; IsSaving = FailWrites = FailPhysicalWrites = false; PhysicalSaves = 0;
            Slots.Clear(); Writes.Clear(); OnCollectSaveData = OnSetFile = OnSaveDeleted = null;
        }
        public static void SetSlot(int slot) { CurrentSlot = slot; if (OnSetFile != null) OnSetFile(); }
        public static void SetRaw(string key, string value) { Slot[key] = value; }
        public static string GetRaw(string key) { string value; return Slot.TryGetValue(key, out value) ? value : null; }
        public static bool KeyExisits(string key) { return Slot.ContainsKey(key); }
        public static T Load<T>(string key) { return (T)(object)Slot[key]; }
        public static void Save<T>(string key, T value)
        {
            if (FailWrites) throw new InvalidOperationException("injected typed save failure");
            string raw = (string)(object)value; Slot[key] = raw; Writes.Add(Tuple.Create(CurrentSlot, key, raw));
        }
        public static void SaveFile(bool collect)
        { if (collect) Collect(); if (FailPhysicalWrites) throw new InvalidOperationException("injected physical save failure"); PhysicalSaves++; }
        public static void Collect() { if (OnCollectSaveData != null) OnCollectSaveData(); }
        public static void Delete() { Slot.Clear(); if (OnSaveDeleted != null) OnSaveDeleted(); }
    }
}

namespace Duckov.Quests
{
    public enum QuestGiverID { Jeff = 1 }
    public sealed class QuestGiver : MonoBehaviour { }
}
namespace Duckov.MiniMaps
{
    public sealed class SimplePointOfInterest : MonoBehaviour
    {
        public Color Color;
        public float ScaleFactor, AreaRadius;
        public bool IsArea;
        public static SimplePointOfInterest Create(Vector3 location, string sceneId, string label, object icon, bool follow)
        { var marker = new GameObject("objective marker").AddComponent<SimplePointOfInterest>(); marker.transform.position = location; return marker; }
    }
}
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour host) { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float delta, float unscaled) { }
        public virtual void OnDestroy() { }
    }
    internal struct SceneRuntimeContext { }
    public sealed class ModBehaviour : MonoBehaviour
    {
        internal readonly OfficialRuntime OfficialQuestRuntime = new OfficialRuntime();
        internal readonly List<string> Messages = new List<string>();
        public static void DevLog(string message) { }
        internal void ShowMessage(string message) { Messages.Add(message); }
    }
    internal sealed class OfficialRuntime { internal readonly OfficialQuestProjection Projection = new OfficialQuestProjection(); }
    internal sealed class OfficialQuestProjection
    {
        internal OfficialQuestBinding Binding;
        internal int NotifyCount, RegisterCount, UnregisterCount;
        internal bool ActiveAccepted { get { return Binding != null && Binding.IsAccepted(); } }
        internal void Register(OfficialQuestBinding binding) { Binding = binding; RegisterCount++; }
        internal void UnregisterClient(IOfficialQuestClient client) { if (Binding != null && ReferenceEquals(Binding.Client, client)) Binding = null; UnregisterCount++; }
        internal void NotifyProgressChanged(IOfficialQuestClient client) { NotifyCount++; }
        internal void PrepareGiver(Duckov.Quests.QuestGiver giver) { }
    }
    internal static class OfficialQuestGiverLocator
    {
        internal static bool TryFind(Duckov.Quests.QuestGiverID id, out Duckov.Quests.QuestGiver giver)
        { giver = FixtureWorld.Jeff; return giver != null; }
        internal static void RefreshMarker(Duckov.Quests.QuestGiver giver) { }
    }
    internal sealed class EclipseBloomRuntimeModule
    {
        internal static EclipseBloomRuntimeModule Current;
        internal bool OtherActive;
        internal bool OtherModeActive() { return OtherActive; }
    }
    internal static class EclipseBloomEncounterPlacement
    {
        internal const string SceneName = "Level_SnowMilitaryBase";
        internal static readonly Vector3 ObjectiveAnchor = new Vector3(481.5f, 0.04f, 549.5f);
        internal static bool IsLoaded(Scene scene) { return scene.IsValid() && scene.isLoaded; }
        internal static bool TryGetExplorationScene(out Scene scene)
        { scene = SceneManager.GetSceneByName(SceneName); return LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel && IsLoaded(scene); }
        internal static void BindToScene(GameObject instance, Scene scene) { SceneManager.MoveGameObjectToScene(instance, scene); }
    }
    internal static class BossRushSaveFileThrottle
    {
        internal static bool TryBeginSaveFile(bool bypass) { return true; }
        internal static void RunSaveFile(Action save) { save(); }
    }
    internal static class MapPointSceneResolver { internal static string Resolve(string scene) { return scene; } }
    internal static class BossRushUIColors { internal static Color WarningText = new Color(1f, 0.7f, 0.2f); }
    internal static class LocalizationHelper { internal static void InjectLocalization(string key, string value) { } }
    internal static class L10n { internal static string T(string cn, string en) { return en; } }
}

internal static class FixtureWorld
{
    internal static ModBehaviour Host;
    internal static Duckov.Quests.QuestGiver Jeff;
    internal static OfficialQuestProjection Projection { get { return Host.OfficialQuestRuntime.Projection; } }
    internal static bool OtherModeActive
    { get { return EclipseBloomRuntimeModule.Current.OtherActive; } set { EclipseBloomRuntimeModule.Current.OtherActive = value; } }
    internal static void Reset(bool inBase = true, int slot = 0)
    {
        GameObject.Reset(); SceneManager.Loaded.Clear(); Saves.SavesSystem.Reset(slot);
        SceneManager.Current = new Scene { handle = 1, buildIndex = 1, name = "Base" };
        SceneManager.Loaded.Add(1, SceneManager.Current);
        Scene target = new Scene { handle = 2, buildIndex = 2, name = EclipseBloomEncounterPlacement.SceneName };
        SceneManager.Loaded.Add(2, target);
        LevelManager.Instance = new GameObject("level owner").AddComponent<LevelManager>(); LevelManager.AfterInit = true;
        CharacterMainControl.Main = new GameObject("player").AddComponent<CharacterMainControl>();
        CharacterMainControl.Main.Health = CharacterMainControl.Main.gameObject.AddComponent<Health>();
        Host = new GameObject("host").AddComponent<ModBehaviour>();
        Jeff = new GameObject("Jeff").AddComponent<Duckov.Quests.QuestGiver>();
        EclipseBloomRuntimeModule.Current = new EclipseBloomRuntimeModule();
        SceneLoader.IsSceneLoading = false; Time.unscaledTime = 0f; Time.frameCount = 0;
        SetBase(inBase);
    }
    internal static void SetBase(bool value)
    {
        LevelManager.Instance.IsBaseLevel = value;
        SceneManager.Current = SceneManager.GetSceneByName(value ? "Base" : EclipseBloomEncounterPlacement.SceneName);
        SceneManager.MoveGameObjectToScene(LevelManager.Instance.gameObject, SceneManager.Current);
        SceneManager.MoveGameObjectToScene(CharacterMainControl.Main.gameObject, SceneManager.Current);
    }
    internal static void PlayerDeath() { CharacterMainControl.Main.Health.Kill(); }
    internal static void Tick(EclipseBloomIntroduction introduction, int frames = 1)
    { for (int i = 0; i < frames; i++) { Time.unscaledTime += 0.25f; Time.frameCount++; introduction.OnUpdate(0.25f, 0.25f); } }
}
