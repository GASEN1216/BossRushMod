#if WAYLAYER_HARMONY_FIXTURE
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using BossRush;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace Saves
{
    internal static class ItemSavesUtilities
    {
        // Same installed MoveNext order: load tree, invoke factory, then first await. No task wrapper in any hook.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static async Task<Item> LoadItem(string key)
        {
            ItemTreeData data=SavesSystem.Load<ItemTreeData>("Item/"+key);
            return await ItemTreeData.InstantiateAsync(data);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Save(Item item,string key)
        {
            SavesSystem.CharacterWrites++;
            SavesSystem.Save("Item/"+key,ItemTreeData.FromItem(item));
        }
    }
}
internal static partial class Program
{
    private static async Task HookCases()
    {
        var harmony=new Harmony("BossRush.Tests.WaylayerJournal");
        harmony.CreateClassProcessor(typeof(CoopWaylayerLoadScopeHook)).Patch();
        harmony.CreateClassProcessor(typeof(CoopWaylayerLoadTreeHook)).Patch();
        harmony.CreateClassProcessor(typeof(CoopWaylayerSaveTreeHook)).Patch();
        try
        {
            Setup();Guest();var j=Journal(guest);Add(guestPlayer,5002,3,"native boundary original");
            var raw=ItemTreeData.FromItem(guestPlayer.CharacterItem);
            Saves.SavesSystem.Save("Item/MainCharacterItemData",raw);
            var first=await Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            Check(first.Inventory.GetItemAt(0).GetString(CoopWaylayerJournal.SourceKey)!=null,"real Harmony LoadItem to factory scope tags original source before first await");
            guestPlayer.CharacterItem=first;var packet=Start();var response=Response();Guest();
            ItemTreeData.Deferred=new TaskCompletionSource<Item>();
            var waiting=Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            Check(!waiting.IsCompleted&&CoopWaylayerLoadScopeHook.Loading==null,"async native load returns original incomplete task and clears synchronous hook scope");
            ItemTreeData.Deferred.SetResult(ItemTreeData.Clone(ItemTreeData.LastInput));var loaded=await waiting;ItemTreeData.Deferred=null;
            Check(loaded.Inventory.GetItemAt(0).StackCount==2,"real factory input scope preserves exact withheld original on load");
            var direct=await ItemTreeData.InstantiateAsync(raw);
            Check(direct.Inventory.GetItemAt(0).StackCount==3,"direct unrelated factory outside native load is unchanged");
            Saves.SavesSystem.Save("Item/OtherCharacter",raw);
            var other=await Saves.ItemSavesUtilities.LoadItem("OtherCharacter");
            Check(other.Inventory.GetItemAt(0).StackCount==3,"other native load keys cannot consume player debt");
            Saves.ItemSavesUtilities.Save(guestPlayer.CharacterItem,"MainCharacterItemData");
            var saved=Saves.SavesSystem.Load<ItemTreeData>("Item/MainCharacterItemData");
            var again=await Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            Check(again.Inventory.GetItemAt(0).StackCount==2,"real native save hook and factory hook prevent second debit");
            Check(Saves.SavesSystem.CharacterWrites==1,"journal only follows one explicitly requested native character save");
            Guest();guest.Dispose();Host();host.Dispose();Guest();
            Check(CoopWaylayerJournal.Current==null,"runtime owner releases journal before native-first load");
            var beforeRuntime=await Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            Check(beforeRuntime.Inventory.GetItemAt(0).StackCount==2&&CoopWaylayerJournal.Current==null,"native load before runtime OnAwake uses and disposes a temporary journal owner");
            Check(CoopWaylayerLoadScopeHook.Loading==null,"temporary owner leaves no thread static load scope");
            // A genuinely fresh save has no journal key, unlike the loaded-debt native-first case above.
            Saves.SavesSystem.Reset();guestPlayer=Player();CharacterMainControl.Main=guestPlayer;
            Add(guestPlayer,5002,3,"fresh native-first source");
            var freshRaw=ItemTreeData.FromItem(guestPlayer.CharacterItem);Saves.SavesSystem.Save("Item/MainCharacterItemData",freshRaw);
            var freshLoaded=await Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            string freshIdentity=freshLoaded.GetString(CoopWaylayerJournal.CharacterOwnerKey);
            Check(CoopWaylayerJournal.Current==null&&freshIdentity!=null,"fresh native-first temporary owner releases after stamping original tree");
            using(var current=new CoopWaylayerJournal())
                Check(current.Identity==freshIdentity&&current.OwnsCharacter(freshLoaded),"fresh native-first identity remains durable and trusted by later runtime owner");
            Check(Saves.SavesSystem.CharacterWrites==0,"fresh native-first persists only its own journal, never a character snapshot");
            Saves.SavesSystem.Reset();Saves.SavesSystem.FailBeforeDisk=true;
            Saves.SavesSystem.Save("Item/MainCharacterItemData",freshRaw);
            var failedIdentity=await Saves.ItemSavesUtilities.LoadItem("MainCharacterItemData");
            Check(failedIdentity.GetString(CoopWaylayerJournal.CharacterOwnerKey)==null,"failed first identity save cannot stamp a transient foreign owner on native character");
            Check(failedIdentity.Inventory.GetItemAt(0).GetString(CoopWaylayerJournal.SourceKey)!=null,"failed first identity save still preserves exact disk-source provenance for later successful reservation");
            Saves.SavesSystem.FailBeforeDisk=false;
            Console.WriteLine("WaylayerJournalHarmony: PASS actual Harmony patches; controlled native signatures/order, no state-machine patch or task replacement.");
        }
        finally {harmony.UnpatchAll(harmony.Id);}
    }
}
#endif
