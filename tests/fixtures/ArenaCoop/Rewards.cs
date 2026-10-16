using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using UnityEngine;
namespace UnityEngine
{
    internal partial class Object
    {
        public static T Instantiate<T>(T template, Vector3 position, Quaternion rotation) where T : MonoBehaviour, new()
        { var clone = new T(); clone.gameObject.transform.position = position; return clone; }
    }
    internal partial class MonoBehaviour
    {
        internal bool enabled = true;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    internal partial class GameObject
    {
        internal bool Persistent;
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public T GetComponent<T>() where T : class { return Components.Find(x => x is T) as T; }
        public T AddComponent<T>() where T : MonoBehaviour, new() { var value = new T(); value.gameObject = this; Components.Add(value); return value; }
    }
}
namespace Duckov.Scenes { internal partial class MultiSceneCore { public static void MoveToActiveWithScene(GameObject value, int scene) { } } }
namespace Duckov.Utilities {
 internal class LootBoxLoader : MonoBehaviour {
 public bool autoSetup=true,dropOnSpawnItem; private int inventorySize=8; internal int Quantity;
 internal static TaskCompletionSource<bool> Gate;internal static bool FailAfterFirst;internal static int Calls;
 public async Task Setup() {
 Calls++;if(Gate!=null)await Gate.Task;
 var inventory=GetComponent<InteractableLootbox>().Inventory;inventory.SetCapacity(inventorySize);inventory.Loading=true;
 for(int i=0;i<Quantity;i++){inventory.AddItem(new Item{TypeID=700001});if(FailAfterFirst){FailAfterFirst=false;throw new Exception("native setup after first item");}}
 inventory.Loading=false;
 }
 }
}
namespace ItemStatsSystem
{
    internal partial class Item
    {
        public int Quality=5;
        internal Inventory InInventory;
        public object ParentObject { get { return InInventory ?? (object)Root; } }
    }
    internal partial class Inventory
    {
        public int Capacity = 1;public bool Loading;
        public int GetFirstEmptyPosition(int start) { return Content.Count < Capacity ? Content.Count : -1; }
        public void SetCapacity(int value) { Capacity = value; }
        public bool AddItem(Item item) { if(Content.Count>=Capacity||BossRushCoop.Current.RejectType==item.TypeID)return false;Content.Add(item); item.InInventory = this; if (BossRushCoop.Current.ThrowAfterItem) throw new Exception("notification after attach"); return true; }
    }
}
internal class InteractableLootbox : MonoBehaviour
{
    internal static readonly List<InteractableLootbox> All = new List<InteractableLootbox>();
    internal string Owner = BossRushCoop.LocalPlayerId, Asset = BossRushCoop.LocalAssetId, Room=BossRushCoop.RunId;
    public InteractableLootbox() { All.Add(this); }
    private Inventory inventoryReference;
    public Inventory Inventory { get { return inventoryReference; } }
    public bool needInspect;internal bool Published;
    private void CreateLocalInventory() { inventoryReference = gameObject.AddComponent<Inventory>(); }
}
namespace BossRush
{
    internal partial class ModBehaviour {
        internal void StartCoroutine(IEnumerator routine) {while(routine.MoveNext()){var nested=routine.Current as IEnumerator;if(nested!=null)StartCoroutine(nested);}}
    }
    internal sealed partial class TestPeer { public long Money; public bool ThrowAfterItem, ThrowAfterCredit;public int RejectType; }
    internal static class CoopArenaPersonalLoot { public static bool Attach(InteractableLootbox value) { if(value==null||value.Inventory==null)return false; value.gameObject.Persistent=true;return true; } public static bool TryPublish(InteractableLootbox value) { if(!BossRushCoop.HasBoundScene||value.Asset!=BossRushCoop.LocalAssetId||value.Room!=BossRushCoop.RunId)return false;value.Published=true;value.gameObject.Persistent=false;return true;}public static void Shutdown() { } }
    internal sealed class VictoryRewardShadowCrateController : MonoBehaviour
    {
        internal static readonly List<VictoryRewardShadowCrateController> Pending = new List<VictoryRewardShadowCrateController>();
        internal string Player; internal Func<Vector3, bool> Spawn; internal Func<bool> Current;
        public bool Initialize(ModBehaviour owner, CharacterMainControl player, InteractableLootbox prefab, int quantity, Func<Vector3, bool> spawn, Func<bool> current)
        { Player = BossRushCoop.LocalPlayerId; Spawn = spawn; Current = current; Pending.Add(this); return true; }
        internal void Land() { if (!Current()) { UnityEngine.Object.Destroy(gameObject); return; } if (Spawn(Vector3.zero)) UnityEngine.Object.Destroy(gameObject); }
    }
    internal sealed partial class WavesArenaRuntimeModule
    {
        private const int InfiniteHellMaxCashPiles = 12;
        internal bool TemplateAvailable = true;
        internal readonly List<InteractableLootbox> PersonalBoxes = new List<InteractableLootbox>();
        private InteractableLootbox GetDifficultyRewardLootBoxTemplate() { return TemplateAvailable ? new InteractableLootbox() : null; }
        private InteractableLootbox GetVictoryRewardVisualLootBoxTemplate_LootAndRewards() { return GetDifficultyRewardLootBoxTemplate(); }
        // Rendering/Loader is the boundary: the real dispatch, receipt and personal ledger are linked whole.
        private InteractableLootbox SpawnDifficultyRewardLootbox_LootAndRewards(int quality, bool personal = false, Vector3? position = null)
        {
            if (!TemplateAvailable) return null;
            var box = new InteractableLootbox(); InteractableLootboxInventoryHelper.EnsureLocalInventory(box);
            CoopArenaPersonalLoot.Attach(box); box.gameObject.AddComponent<Duckov.Utilities.LootBoxLoader>().Quantity=quality;
            PersonalBoxes.Add(box); Boxes++; Quality = quality; return box;
        }
        internal void Reward(string kind, long cash = 0, int quality = 0, int itemType = 0, int tier = 0) { DeliverCoopArenaReward(kind, cash, quality, itemType, tier); }
        internal void Command(CoopGameplayCommand command) { ExecuteCoopArena(command); }
        internal bool Participant(string epoch, string player, out string asset) { return TryGetCoopCampaignParticipantAsset(epoch, player, out asset); }
        internal CoopArenaState SnapshotState() { return CaptureCoopArena(); }
        internal void RewardShutdown() { CleanupCoopArenaRewards(); }
        internal IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox box,int count) {
          while(box.Inventory.Content.Count>count){var item=box.Inventory.Content[box.Inventory.Content.Count-1];box.Inventory.Content.Remove(item);item.DestroyTree();}yield break;
        }
    }
}
