using System;using System.Linq;using System.Reflection;using System.Reflection.Emit;using System.Collections.Generic;using HarmonyLib;using ItemStatsSystem;using ItemStatsSystem.Items;using EscapeFromDuckovCoopMod;using BossRush;
internal static class Program
{
 const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 internal static int count,Allowed,Rejected;
 internal static void Assert(bool value,string reason){count++;if(!value)throw new Exception("ASSERT "+reason);}
 static bool Locked(Item item){return item!=null&&!string.IsNullOrEmpty(item.GetString("lock"));}
 static bool InventoryGate(Inventory __instance,Item item,ref bool __result){if(CoopWaylayerClaims.AllowInventory(item,__instance,true)){if(Locked(item))Allowed++;return true;}Rejected++;__result=false;return false;}
 static bool SlotGate(Slot __instance,Item otherItem,ref bool __result){if(CoopWaylayerClaims.AllowSlot(otherItem,__instance)){if(Locked(otherItem))Allowed++;return true;}Rejected++;__result=false;return false;}
 static Item Root(int type=1){var value=new Item(type);value.Inventory=new Inventory(value);return value;}
 static ItemSnapshot Leaf(int type=2){return new ItemSnapshot{TypeId=type,Stack=1,CustomDataKeys=new[]{"lock"},CustomDataValues=new[]{"world"}};}
 static ItemSnapshot Tree(bool slot=false){return new ItemSnapshot{TypeId=1,Stack=1,InventoryCapacity=4,Inventory=new[]{new ItemInventoryEntrySnapshot{Slot=0,Item=Leaf()}},Slots=slot?new[]{new ItemSlotSnapshot{Key="head",HasItem=true,Item=Leaf(3)}}:null};}
 static void Queue(params Item[] items){ItemAssetsCollection.Items.Clear();foreach(var item in items)ItemAssetsCollection.Items.Enqueue(item);}
 static void Clear(){CharacterMainControl.Main=null;BossRushCoop.Players.Clear();ItemTool.Applied=null;ItemAssetsCollection.Items.Clear();UnityEngine.Object.Instantiated=null;CoopWaylayerClaims.Anchors.Clear();}
 static void Owner(int value){typeof(CoopWaylayerNativeBuildScope).GetField("owners",F).SetValue(null,value);}
 static void BuildCases()
 {
  var root=Root();var child=Root(2);var hat=Root(3);Queue(root,child,hat);
  int before=Allowed;Assert(ReferenceEquals(ItemTool.BuildItemFromSnapshot(Tree(true)),root),"complete native returns exact factory root");
  Assert(ReferenceEquals(root.Inventory.GetItemAt(0),child)&&ReferenceEquals(root.Slots.GetSlot("head").content,hat),"native inventory and slot child assembly survives claim locks");
  Assert(Allowed==before+2&&!child.Dead&&!hat.Dead,"only two exact mutations admitted and children remain alive");
  var outsider=Root(8);root=Root();child=Root(2);Queue(root,child);Item captured=child;Inventory target=root.Inventory;bool probed=false,borrowed=false,reused=false,borrowedSlot=false;
  child.Added=i=>{if(probed)return;probed=true;borrowed=outsider.Inventory.AddAt(captured,0);Item unused;borrowedSlot=outsider.Slots.GetSlot("head").Plug(captured,out unused);captured.Detach();reused=target.AddAt(captured,1);if(!reused)using(CoopWaylayerClaims.Admit(captured,target))target.AddAt(captured,0);};
  ItemTool.BuildItemFromSnapshot(Tree());Assert(!borrowed&&!borrowedSlot&&!reused,"notification cannot reuse consumed exact destination");Assert(ReferenceEquals(root.Inventory.GetItemAt(0),child)&&root.Inventory.GetItemAt(1)==null,"callback cannot duplicate child");
  root=Root();child=Root(2);var nestedRoot=Root(7);var nestedChild=Root(2);Queue(root,child,nestedRoot,nestedChild);bool once=false;
  child.Added=i=>{if(once)return;once=true;Assert(ItemTool.BuildItemFromSnapshot(Tree())==nestedRoot,"nested true builder receives independent frame");Assert(!root.Inventory.AddAt(nestedChild,1),"outer callback cannot borrow nested completed child");};
  ItemTool.BuildItemFromSnapshot(Tree());Assert(nestedRoot.Inventory.GetItemAt(0)==nestedChild&&root.Inventory.GetItemAt(0)==child,"nested frames restore exact owner");
  foreach(bool remote in new[]{false,true})
  {
   Clear();root=Root();child=Root(2);var player=new CharacterMainControl{CharacterItem=root};if(remote)BossRushCoop.Players.Add(new BossRushCoopPlayer{Character=player});else CharacterMainControl.Main=player;
   Queue(root,child);ItemTool.BuildItemFromSnapshot(Tree());Assert(root.Inventory.GetItemAt(0)==null&&child.Dead,(remote?"remote":"main")+" existing player root cannot receive builder admission");
  }
  Clear();root=Root();child=Root(2);Queue(root,child);ItemTool.Applied=(i,s)=>{if(i.TypeID==1)throw new InvalidOperationException("native final custom-data error");};
  bool threw=false;try{ItemTool.BuildItemFromSnapshot(Tree());}catch(InvalidOperationException){threw=true;}
  Assert(threw&&typeof(CoopWaylayerNativeBuildScope).GetField("frame",F).GetValue(null)==null,"exception finalizer restores scope without swallowing native error");
  Assert(!CoopWaylayerNativeBuildScope.AllowInventory(child,root.Inventory,true),"exception leaves no stale admission");
  Clear();root=Root();child=Root(2);Queue(root,child);ItemTool.BuildItemFromSnapshot(Tree());Assert(root.Inventory.GetItemAt(0)==child,"next real native call recovers after exception");
  root=Root();child=Root(2);Queue(root,child);child.Added=i=>Owner(0);ItemTool.BuildItemFromSnapshot(Tree());Assert(typeof(CoopWaylayerNativeBuildScope).GetField("frame",F).GetValue(null)==null,"owner shutdown inside callback cannot revive old frame");Owner(1);
 }
 static InteractableLootbox Corpse(Item item){return InteractableLootbox.CreateFromItem(item,new UnityEngine.Vector3(),new UnityEngine.Quaternion());}
 static void Attach(Item root,Item child){using(CoopWaylayerClaims.Admit(child,root.Inventory))Assert(root.Inventory.AddItem(child),"host custody setup exact admission");CoopWaylayerNativeBuildScope.RegisterTicketRoot(root);}
 static Item Ticket(int type=2){var item=Root(type);item.StackCount=1;item.SetString("lock","world");return item;}
 static void CorpseCases()
 {
  Clear();var root=Root();var child=Ticket();var hat=Root(3);Item ignored;Assert(root.Slots.GetSlot("head").Plug(hat,out ignored),"source official slot setup");hat.SetString("lock","world");Attach(root,child);
  var a=Root(4);a.Stackable=true;a.StackCount=3;var b=Root(4);b.Stackable=true;b.StackCount=4;root.Inventory.AddItem(a);root.Inventory.AddItem(b);
  Assert(hat.ParentItem==root&&root.Slots.GetSlot("head").content==hat,"slot source attached before native corpse");
  var box=Corpse(root);Assert(box!=null&&box.Inventory==child.InInventory&&hat.InInventory==box.Inventory,"complete native corpse preserves marked inventory and slot originals");
  Assert(box.Inventory.Count(i=>i!=null)==3&&box.Inventory.First(i=>i!=null&&i.TypeID==4).StackCount==7,"ordinary corpse loot retains original AddAndMerge behavior");
  Assert(CoopWaylayerClaims.Anchors.Contains(child)&&CoopWaylayerClaims.Anchors.Contains(hat),"real corpse inventory anchors both world tickets");
  Assert(Duckov.Scenes.MultiSceneCore.Calls>0&&box.Inventory.NeedInspection,"remaining native scene and inspection side effects preserved");
  Clear();root=Root();child=Ticket();Attach(root,child);var destination=Root(9);CharacterMainControl.Main=new CharacterMainControl{CharacterItem=destination};Item held=child;Inventory actualBox=null;
  UnityEngine.Object.Instantiated=created=>{created.needInspect=false;};
  child.Added=i=>{actualBox=i.InInventory;Assert(!destination.Inventory.AddAt(held,0),"corpse notification cannot move ticket into real player inventory");Assert(!actualBox.AddAt(held,1),"corpse notification cannot reuse exact consumed AddAt");Item unused;Assert(!destination.Slots.GetSlot("head").Plug(held,out unused),"corpse notification cannot borrow slot permission");};
  box=Corpse(root);Assert(child.InInventory==box.Inventory&&destination.Inventory.GetItemAt(0)==null&&box.Inventory.GetItemAt(1)==null,"corpse ticket remains single original after notification attacks");
  foreach(bool remote in new[]{false,true})
  {
   Clear();root=Root();child=Ticket();Attach(root,child);var player=new CharacterMainControl{CharacterItem=root};if(remote)BossRushCoop.Players.Add(new BossRushCoopPlayer{Character=player});else CharacterMainControl.Main=player;
   box=Corpse(root);Assert(!box.Inventory.Any(i=>i!=null),(remote?"remote":"main")+" player root cannot receive corpse transfer permission");
  }
  Clear();root=Root();child=Ticket();Attach(root,child);child.Added=i=>{throw new InvalidOperationException("post mutation native notification");};bool threw=false;
  try{Corpse(root);}catch(InvalidOperationException){threw=true;}
  Assert(threw&&child.InInventory!=null&&CoopWaylayerClaims.Anchors.Contains(child),"post mutation exception still freezes corpse anchor");
  Assert(typeof(CoopWaylayerNativeBuildScope).GetField("corpse",F).GetValue(null)==null,"corpse exception finalizer restores scope");
  Assert(!CoopWaylayerClaims.AllowInventory(child,child.InInventory,true),"corpse exception leaves no borrowed admission");
  Clear();root=Root();child=Ticket();Attach(root,child);box=Corpse(root);Assert(child.InInventory==box.Inventory,"next corpse native call recovers after exception");
  Clear();root=Root();child=Ticket();Attach(root,child);var outsider=Ticket(8);UnityEngine.Object.Instantiated=created=>{using(CoopWaylayerClaims.Admit(outsider,root.Inventory))root.Inventory.AddItem(outsider);};
  box=Corpse(root);Assert(child.InInventory==box.Inventory&&outsider.InInventory==null,"post prefix inserted ticket is not in frozen child set");
  Clear();root=Root();var ordinary=Root(5);root.Inventory.AddItem(ordinary);Inventory.Enumerations=0;SlotCollection.Enumerations=0;
  UnityEngine.Object.Instantiated=created=>Assert(Inventory.Enumerations==0&&SlotCollection.Enumerations==0&&typeof(CoopWaylayerNativeBuildScope).GetField("corpse",F).GetValue(null)==null,"ordinary corpse prefix O(1) with no scans or permission frame");
  box=Corpse(root);Assert(ordinary.InInventory==box.Inventory,"ordinary corpse original path still completes");
  Clear();root=Root();child=Root(2);Queue(root,child);ItemTool.BuildItemFromSnapshot(Tree());box=Corpse(root);Assert(child.InInventory==box.Inventory,"native snapshot assembly registers corpse root without host Track");
  Clear();root=Root();child=Ticket();Attach(root,child);var later=Ticket(3);Attach(root,later);var plainRoot=Root(9);plainRoot.Inventory.AddItem(Root(10));var nestedRoot=Root(7);var nestedTicket=Ticket(8);Attach(nestedRoot,nestedTicket);bool nested=false;
  child.Added=i=>{if(nested)return;nested=true;Corpse(plainRoot);var nestedBox=Corpse(nestedRoot);Assert(nestedTicket.InInventory==nestedBox.Inventory,"nested tracked corpse receives its own exact frame");};
  box=Corpse(root);Assert(child.InInventory==box.Inventory&&later.InInventory==box.Inventory&&CoopWaylayerClaims.Anchors.Contains(child),"ordinary and tracked nested corpse calls restore outer frame");
 }
 static int Main(string[] args)
 {
  try
  {
   using(CoopWaylayerNativeBuildScope.Acquire())Assert(ModBehaviour.Warnings.Count==0,"no installed COOP safely leaves optional bridge absent");
   Owner(1);Native.Install(args);
   var harmony=new Harmony("Test.WaylayerNativeBuild.Gates");harmony.Patch(typeof(Inventory).GetMethod("AddAt"),prefix:new HarmonyMethod(typeof(Program),"InventoryGate"));harmony.Patch(typeof(Slot).GetMethod("Plug"),prefix:new HarmonyMethod(typeof(Program),"SlotGate"));
   BuildCases();CorpseCases();Native.SiteFailures();Owner(0);int warnings=ModBehaviour.Warnings.Count;using(CoopWaylayerNativeBuildScope.Acquire())Assert(ModBehaviour.Warnings.Count==warnings+1,"incompatible native identity closes optional seam with readable warning");Assert(!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m).Owners.Contains("BossRush.Coop.WaylayerNativeBuild")),"compatibility failure unpatches only optional seam");Console.WriteLine("PASS "+count+" assertions; actual complete native body; no game/save access");return 0;
  }catch(Exception error){Console.WriteLine(error);return 1;}
 }
}
