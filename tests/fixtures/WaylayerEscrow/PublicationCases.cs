using System;using System.Linq;using System.Threading.Tasks;using BossRush;using ItemStatsSystem;using ItemStatsSystem.Data;
internal static partial class Program {
 private static async Task PublicationCases(){
  foreach(string failure in new[]{"before","after-read"}){
   Setup();Add(guestPlayer,5002,3,"grant native barrier "+failure);var packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;var world=chief.Bag.GetItemAt(0);var request=SendClaim(world);ReceiveClaim(request);
   ItemTreeData.Deferred=new TaskCompletionSource<Item>();DeliverClaim();Guest();var grant=Journal(guest).Data.Grants.Single();var deferred=ItemTreeData.Deferred;ItemTreeData.Deferred=null;int factories=ItemTreeData.FactoryCalls;
   if(failure=="before")Saves.SavesSystem.FailBeforeDisk=true;else{Saves.SavesSystem.ThrowAfterDisk=true;Saves.SavesSystem.FailRead=true;}
   deferred.SetResult(ItemTreeData.Clone(grant.Tree));await Task.Yield();Guest();var exact=guestPlayer.CharacterItem.Inventory.Content.Single(v=>v!=null&&v.GetString(CoopWaylayerJournal.GrantKey)==grant.Id);
   Check(CoopWaylayerClaims.Locked(exact)&&!Journal(guest).IsReturned(grant.Id),"unconfirmed native publication keeps exact owned grant private after "+failure);
   int before=exact.StackCount;exact.Detach();var foreign=new Inventory();Check(!foreign.AddItem(exact)&&exact.InInventory==guestPlayer.CharacterItem.Inventory,"failed native publication cannot leak the exact original into another asset");
   Saves.SavesSystem.FailBeforeDisk=Saves.SavesSystem.ThrowAfterDisk=Saves.SavesSystem.FailRead=false;
   guest.Tick(2);Check(Journal(guest).IsReturned(grant.Id)&&!CoopWaylayerClaims.Locked(exact)&&ItemTreeData.FactoryCalls==factories,"publication retry confirms the same instance without factory replay: "+failure);
   var disk=(ItemTreeData)Saves.SavesSystem.ReadDisk(Saves.SavesSystem.CurrentFilePath,"Item/MainCharacterItemData");Check(disk!=null&&disk.RootData.variables.Any(v=>v.Key==CoopWaylayerJournal.ReturnedKey&&v.GetString().Contains("|"+grant.Id+"|")),"actual native file stores returned proof in same tree as original");
   int count=disk.entries.Count(v=>v.variables.Any(c=>c.Key==CoopWaylayerJournal.GrantKey&&c.GetString()==grant.Id));Check(count==1,"exactly one grant is present in the real native file image");
   guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(disk));factories=ItemTreeData.FactoryCalls;guest.Tick(2);
   Check(ItemTreeData.FactoryCalls==factories&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"latest native file reload proves grant adoption with zero factory replay");
  }
  foreach(bool stackRefund in new[]{true,false}){
   Setup();var original=Add(guestPlayer,5002,stackRefund?3:1,"refund publication");var packet=Start();var response=Response();Host();chief.Bag.Reject=true;Respond(response);Guest();var pack=guestPlayer.CharacterItem.Inventory;var foreign=new Inventory();Item seen=null;
   Action<Item> notification=item=>{seen=item;item.Detach();foreign.AddItem(item);Journal(guest).BeforeNativeSave(guestPlayer.CharacterItem);Saves.ItemSavesUtilities.Save(guestPlayer.CharacterItem,"MainCharacterItemData");Saves.SavesSystem.SaveFile(false);throw new Exception("native owned notification after disk save");};
   if(stackRefund)original.AfterCount=notification;else pack.AfterAdd=notification;
   Check(Apply(Decision()),"explicit refund publishes through native save after reentrant notification: "+stackRefund);
   Check(seen!=null&&seen.InInventory==pack&&foreign.Content.All(v=>v==null)&&!CoopWaylayerClaims.Locked(seen),"refund count/add notification cannot move the private original to another asset");
   var disk=(ItemTreeData)Saves.SavesSystem.ReadDisk(Saves.SavesSystem.CurrentFilePath,"Item/MainCharacterItemData");Check(disk.RootData.variables.Any(v=>v.Key==CoopWaylayerJournal.ReturnedKey&&v.GetString().Contains("|"+packet.Id+"|")),"notification native save includes an ownership proof only after actual refund mutation");
   guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(disk));int factories=ItemTreeData.FactoryCalls;guest.Tick(2);
   Check(ItemTreeData.FactoryCalls==factories&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==(stackRefund?3:1),"refund physical proof reload never repeats count increment or original factory");
  }
  foreach(string flow in new[]{"claim","refund"}) foreach(string boundary in new[]{"death","slot","root","destroy"}){
   Setup();var source=Add(guestPlayer,5002,3,"physical read lease "+flow+" "+boundary);var packet=Start();Item exact=null;object claim=null;
   if(flow=="claim"){Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;claim=SendClaim(chief.Bag.GetItemAt(0));ReceiveClaim(claim);}
   else{Host();chief.Bag.Reject=true;Respond(Response());}
   Guest();var originalBody=guestPlayer;var originalRoot=guestPlayer.CharacterItem;var originalJournal=Journal(guest);bool injected=false;
   Saves.SavesSystem.AfterPhysicalRead=key=>{if(key!="Item/MainCharacterItemData"||injected)return;injected=true;
    exact=flow=="refund"?source:originalRoot.Inventory.Content.First(v=>v!=null&&!string.IsNullOrEmpty(v.GetString(CoopWaylayerJournal.GrantKey)));
    if(boundary=="death")originalBody.Health.IsDead=true;
    else if(boundary=="slot")Guest("other-slot");
    else if(boundary=="root")originalBody.CharacterItem=new Item {Inventory=new Inventory()};
    else exact.DestroyTree();};
   if(flow=="claim")DeliverClaim();else Apply(Decision());
   Check(injected,"physical native file read callback was actually exercised: "+flow+" "+boundary);
   Check(!originalJournal.IsReturned(flow=="claim"?originalJournal.Data.Grants.FirstOrDefault()?.Id:packet.Id),"readback lease change cannot NoteReturned in the new identity: "+flow+" "+boundary);
   Check(boundary=="destroy"||CoopWaylayerClaims.Locked(exact),"readback lease change does not unlock an unacknowledged original: "+flow+" "+boundary);
   Saves.SavesSystem.AfterPhysicalRead=null;
  }
  foreach(string flow in new[]{"claim","whole-refund"}){
   Setup();var source=Add(guestPlayer,5002,flow=="claim"?3:1,"private death recovery "+flow);var packet=Start();
   if(flow=="claim"){Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;var claim=SendClaim(chief.Bag.GetItemAt(0));ReceiveClaim(claim);}
   else{Host();chief.Bag.Reject=true;Respond(Response());}
   Guest();var oldBody=guestPlayer;var oldPack=oldBody.CharacterItem.Inventory;oldPack.AfterAdd=item=>oldBody.Health.IsDead=true;
   if(flow=="claim")DeliverClaim();else Apply(Decision());var exact=oldPack.Content.Single(v=>v!=null&&CoopWaylayerClaims.Locked(v));int factories=ItemTreeData.FactoryCalls;
   guestPlayer=Player();member.Character=guestPlayer;Guest();guest.Tick(2);UnityEngine.Time.frameCount++;guest.Tick(2);
   Check(ReferenceEquals(guestPlayer.CharacterItem.Inventory.Content.FirstOrDefault(v=>v!=null&&v.GetString(CoopWaylayerJournal.ClaimLockKey)==string.Empty),exact),"living replacement body receives the same independent private original after death: "+flow);
   Check(!CoopWaylayerClaims.Locked(exact)&&oldPack.Content.All(v=>!ReferenceEquals(v,exact))&&ItemTreeData.FactoryCalls==factories,"death recovery retires old pack ownership without another factory: "+flow);
  }
  Setup();Guest();Add(guestPlayer,5002,3,"mixed stack no publication write");guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(ItemTreeData.FromItem(guestPlayer.CharacterItem)));Saves.ItemSavesUtilities.Save(guestPlayer.CharacterItem,"MainCharacterItemData");Saves.SavesSystem.SaveFile(false);
  var oldNative=(ItemTreeData)Saves.SavesSystem.ReadDisk(Saves.SavesSystem.CurrentFilePath,"Item/MainCharacterItemData");var refundPacket=Start();Host();chief.Bag.Reject=true;Respond(Response());Guest();Saves.SavesSystem.FailBeforeDiskAfterWrites=Saves.SavesSystem.PhysicalWrites+1;Check(!Apply(Decision()),"explicit mixed-stack refund remains private when native publication cannot write");var deadRoot=guestPlayer.CharacterItem;guestPlayer.Health.IsDead=true;
  Check(deadRoot.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3&&deadRoot.Inventory.Content.Any(v=>CoopWaylayerClaims.Locked(v)),"failed publication retains the exact mixed refunded stack until actual normal reload");
  Saves.SavesSystem.FailBeforeDiskAfterWrites=0;guest.Dispose();Saves.SavesSystem.Reload();guestPlayer=Player();member.Character=guestPlayer;Guest();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(oldNative));int noRefundReplay=ItemTreeData.FactoryCalls;guest.Tick(2);
  Check(Journal(guest).IsReturned(refundPacket.Id)&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3&&guestPlayer.CharacterItem.GetAllChildren(true,false).All(v=>!CoopWaylayerClaims.Locked(v))&&ItemTreeData.FactoryCalls==noRefundReplay,"no native publication write then death and same-slot old-file load recovers exact original stack without loss, lock or refund replay");
  Setup();Guest();var firstSaved=Add(guestPlayer,5002,1,"first frame publication");var secondSaved=Add(guestPlayer,5002,1,"second frame publication");var firstId=Guid.NewGuid().ToString("N");var secondId=Guid.NewGuid().ToString("N");int writes=Saves.SavesSystem.CharacterWrites;
  UnityEngine.Time.frameCount++;Check(Journal(guest).PublishOriginal(firstId,firstSaved),"first publication in a frame can confirm its exact physical tree");
  Check(!Journal(guest).PublishOriginal(secondId,secondSaved)&&Saves.SavesSystem.CharacterWrites==writes+1,"second same-frame publication defers before serializing a second official character save");
  UnityEngine.Time.frameCount++;Check(Journal(guest).PublishOriginal(secondId,secondSaved),"the same exact private original can publish next frame after shared throttle opens");
  Setup();Add(guestPlayer,5002,3,"track retirement");var snatch=Start();Respond(Response());Host();var first=chief.Bag.GetItemAt(0);var ticket=first.GetString(CoopWaylayerJournal.ClaimLockKey);Claims(host).Track(Journal(host).FindCommit(snatch.Id),first,boss);Check(first.GetString(CoopWaylayerJournal.ClaimLockKey)==ticket,"same exact Track is idempotent and does not change nonce");
  int calls=ItemUtilities.MergeCalls;Check(!ItemUtilities.AddAndMerge(chief.Bag,new Item {TypeID=5002,StackCount=1},0)&&ItemUtilities.MergeCalls==calls,"outer native AddAndMerge is rejected before entering its no-progress Combine loop for locked targets");
  Guest();var mirror=ItemTreeData.Clone(ItemTreeData.FromItem(first));BossRushCoop.Restore(mirror,new System.Collections.Generic.Dictionary<string,string>{{"BossRush.WaylayerClaim",ticket}});Host();var replacement=ItemTreeData.Clone(Journal(host).FindCommit(snatch.Id).Tree);Claims(host).Track(Journal(host).FindCommit(snatch.Id),replacement,boss);
  Check(first.IsBeingDestroyed&&ReferenceEquals(TicketItem(host,snatch.Id),replacement),"replacement Track retires old physical original before changing active nonce");var retired=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.claim.retired").Value;Guest();BossRushCoop.Receive("guest","sky.waylayer.claim.retired",retired,"host",true);Check(mirror.IsBeingDestroyed,"old Track replica retires through actual authenticated retirement handler");
  Console.WriteLine("WaylayerNativePublication: PASS exact physical native tree proofs, failure retry, notification reentry, zero factory reload and Track retirement.");
 }
}
